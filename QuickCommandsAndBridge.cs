using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static partial class Launcher
{
	private const int CommandBridgeProtocolVersion = 1;
	private const int CommandBridgeMaximumLineLength = 65536;
	private const int CommandBridgeMaximumSuggestions = 100;
	private const string CommandBridgeJarName = "Minecraft-Server-Launcher-Command-Bridge-Paper.jar";
	private const string CommandBridgeSessionFileName = ".mcsl-command-bridge-session.json";
	private const string CommandBridgeManagedFileName = ".mcsl-command-bridge-managed.json";
	private const string CommandBridgeChoiceFileName = ".mcsl-command-bridge-choice.json";
	private static readonly object ActiveCommandBridgeLock = new object();
	private static CommandBridgeSession ActiveCommandBridge;
	private static string BridgeArtifactOverridePath = null;
	private static bool BridgeInstallFailureAfterBackup = false;

	private enum QuickCommandSource
	{
		Bridge,
		User,
		BuiltIn,
		History
	}

	private enum QuickCommandRisk
	{
		Normal,
		Confirm,
		Dangerous
	}

	private sealed class QuickCommandDefinition
	{
		public string Id;
		public string Name;
		public string Description;
		public string Category;
		public string Template;
		public string[] Parameters;
		public bool Confirm;
		public string[] ServerTypes;
		public string Source;

		public override string ToString() { return Name ?? string.Empty; }
	}

	private sealed class QuickCommandSuggestion
	{
		public string Value;
		public string Syntax;
		public string Description;
		public string Source;
		public string Plugin;
		public bool Dangerous;
		public int ReplaceStart;
		public int ReplaceLength;

		public override string ToString()
		{
			return Value;
		}
	}

	private sealed class CommandToken
	{
		public string Value;
		public int Start;
		public int Length;
		public bool Quoted;
	}

	private sealed class CommandParseResult
	{
		public string Text;
		public List<CommandToken> Tokens = new List<CommandToken>();
		public int CurrentIndex;
		public int ReplaceStart;
		public int ReplaceLength;
		public string Prefix;
	}

	private sealed class CommandTreeNode
	{
		public string Value;
		public bool Argument;
		public string Description;
		public bool Dangerous;
		public List<CommandTreeNode> Children = new List<CommandTreeNode>();
		public List<QuickCommandDefinition> Definitions = new List<QuickCommandDefinition>();
	}

	private sealed class BridgeReleaseInfo
	{
		public string Version;
		public int Protocol;
		public string MinimumMinecraft;
		public string MaximumMinecraft;
		public string Url;
		public string Sha256;
		public long Size;
	}

	private sealed class BridgeManagedInfo
	{
		public string JarName;
		public string Version;
		public int Protocol;
		public string Sha256;
		public string InstalledUtc;
	}

	private sealed class BridgeChoiceInfo
	{
		public string Choice;
		public bool Asked;
	}

	private sealed class BridgeDefaultPreferenceInfo
	{
		public bool HasDefault;
		public string Choice;
	}

	private sealed class BridgeConsentResult
	{
		public string Choice;
		public bool UseAsDefault;
	}

	private sealed class CommandBridgeSession : IDisposable
	{
		private readonly string serverDirectory;
		private readonly string profileName;
		private readonly string token;
		private readonly TcpListener listener;
		private readonly Thread acceptThread;
		private readonly Thread pingThread;
		private readonly object writerLock = new object();
		private readonly object requestLock = new object();
		private readonly Dictionary<string, Action<List<QuickCommandSuggestion>>> suggestionCallbacks = new Dictionary<string, Action<List<QuickCommandSuggestion>>>(StringComparer.Ordinal);
		private TcpClient client;
		private StreamWriter writer;
		private volatile bool disposed;
		private volatile bool connected;
		private int requestSequence;
		private string bridgeVersion = string.Empty;
		private int bridgeProtocol;
		private int commandCount;
		private DateTime lastConnectedUtc;
		private DateTime lastReceivedUtc;
		private string[] players = new string[0];
		private List<QuickCommandSuggestion> bridgeCommands = new List<QuickCommandSuggestion>();

		public CommandBridgeSession(string directory, string profile)
		{
			serverDirectory = Path.GetFullPath(directory);
			profileName = profile ?? string.Empty;
			token = CreateBridgeToken();
			listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start(2);
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			WriteBridgeSessionFile(serverDirectory, profileName, port, token);
			acceptThread = new Thread(AcceptLoop);
			acceptThread.IsBackground = true;
			acceptThread.Name = "명령 브리지 수신";
			acceptThread.Start();
			pingThread = new Thread(PingLoop);
			pingThread.IsBackground = true;
			pingThread.Name = "명령 브리지 연결 확인";
			pingThread.Start();
		}

		public bool Connected { get { return connected; } }
		public string BridgeVersion { get { return bridgeVersion; } }
		public int BridgeProtocol { get { return bridgeProtocol; } }
		public int CommandCount { get { return commandCount; } }
		public DateTime LastConnectedUtc { get { return lastConnectedUtc; } }
		public string[] Players { get { lock (requestLock) return (string[])players.Clone(); } }
		public List<QuickCommandSuggestion> Commands { get { lock (requestLock) return new List<QuickCommandSuggestion>(bridgeCommands); } }

		private void AcceptLoop()
		{
			while (!disposed)
			{
				TcpClient accepted = null;
				try
				{
					accepted = listener.AcceptTcpClient();
					IPEndPoint remote = accepted.Client.RemoteEndPoint as IPEndPoint;
					if (remote == null || !IPAddress.IsLoopback(remote.Address))
					{
						accepted.Close();
						continue;
					}
					HandleClient(accepted);
				}
				catch (SocketException)
				{
					if (!disposed) Thread.Sleep(250);
				}
				catch
				{
					if (accepted != null) accepted.Close();
					if (!disposed) Thread.Sleep(500);
				}
			}
		}

		private void HandleClient(TcpClient accepted)
		{
			accepted.NoDelay = true;
			accepted.ReceiveTimeout = 35000;
			accepted.SendTimeout = 10000;
			using (accepted)
			using (NetworkStream stream = accepted.GetStream())
			using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true))
			using (StreamWriter candidateWriter = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			{
				candidateWriter.AutoFlush = true;
				string helloLine = ReadLimitedLine(reader, CommandBridgeMaximumLineLength);
				Dictionary<string, object> hello = DeserializeBridgeObject(helloLine);
				if (!BridgeStringEquals(hello, "type", "hello") || !BridgeTokenEquals(token, BridgeString(hello, "token")) || !string.Equals(profileName, BridgeString(hello, "profile"), StringComparison.Ordinal) || BridgeInt(hello, "protocol") != CommandBridgeProtocolVersion)
				{
					WriteBridgeError(candidateWriter, BridgeString(hello, "id"), "handshake-rejected");
					return;
				}
				lock (writerLock)
				{
					if (client != null && client != accepted) client.Close();
					client = accepted;
					writer = candidateWriter;
				}
				connected = true;
				lastConnectedUtc = DateTime.UtcNow;
				lastReceivedUtc = DateTime.UtcNow;
				bridgeVersion = BridgeString(hello, "version");
				bridgeProtocol = BridgeInt(hello, "protocol");
				NotifyCommandBridgeChanged();
				SendObject(new Dictionary<string, object> { { "type", "capabilities" }, { "id", BridgeString(hello, "id") }, { "protocol", CommandBridgeProtocolVersion }, { "commandList", true }, { "suggest", true }, { "players", true } });
				RequestCommandList();
				int messagesInWindow = 0;
				DateTime windowStartedUtc = DateTime.UtcNow;
				while (!disposed && accepted.Connected)
				{
					string line = ReadLimitedLine(reader, CommandBridgeMaximumLineLength);
					if (line == null) break;
					if ((DateTime.UtcNow - windowStartedUtc).TotalSeconds >= 1) { windowStartedUtc = DateTime.UtcNow; messagesInWindow = 0; }
					if (++messagesInWindow > 120) { WriteBridgeError(candidateWriter, "0", "rate-limit"); break; }
					HandleBridgeMessage(DeserializeBridgeObject(line));
				}
			}
			connected = false;
			lock (writerLock)
			{
				if (client == accepted) { client = null; writer = null; }
			}
			lock (requestLock) suggestionCallbacks.Clear();
			NotifyCommandBridgeChanged();
		}

		private void HandleBridgeMessage(Dictionary<string, object> message)
		{
			lastReceivedUtc = DateTime.UtcNow;
			string type = BridgeString(message, "type");
			if (string.Equals(type, "pong", StringComparison.Ordinal)) return;
			if (string.Equals(type, "ping", StringComparison.Ordinal))
			{
				SendObject(new Dictionary<string, object> { { "type", "pong" }, { "id", BridgeString(message, "id") } });
				return;
			}
			if (string.Equals(type, "players-update", StringComparison.Ordinal))
			{
				lock (requestLock) players = BridgeStringArray(message, "players", 200);
				NotifyCommandBridgeChanged();
				return;
			}
			if (string.Equals(type, "command-list-response", StringComparison.Ordinal))
			{
				List<QuickCommandSuggestion> commands = ParseBridgeCommands(message);
				lock (requestLock) { bridgeCommands = commands; commandCount = commands.Count; }
				NotifyCommandBridgeChanged();
				return;
			}
			if (string.Equals(type, "suggest-response", StringComparison.Ordinal))
			{
				string id = BridgeString(message, "id");
				Action<List<QuickCommandSuggestion>> callback = null;
				lock (requestLock)
				{
					if (suggestionCallbacks.TryGetValue(id, out callback)) suggestionCallbacks.Remove(id);
				}
				if (callback != null)
				{
					string[] values = BridgeStringArray(message, "suggestions", CommandBridgeMaximumSuggestions);
					List<QuickCommandSuggestion> result = new List<QuickCommandSuggestion>();
					for (int i = 0; i < values.Length; i++) result.Add(NewSuggestion(values[i], values[i], LauncherUiText("서버 실시간 후보", "Live server suggestion"), "bridge", IsAdvancedDangerousCommand(values[i])));
					ThreadPool.QueueUserWorkItem(delegate { callback(result); });
				}
			}
		}

		private void PingLoop()
		{
			while (!disposed)
			{
				for (int index = 0; index < 20 && !disposed; index++) Thread.Sleep(500);
				if (disposed || !connected) continue;
				if ((DateTime.UtcNow - lastReceivedUtc).TotalSeconds > 35)
				{
					lock (writerLock) { try { if (client != null) client.Close(); } catch { } }
					continue;
				}
				string id = "p" + Interlocked.Increment(ref requestSequence).ToString(CultureInfo.InvariantCulture);
				SendObject(new Dictionary<string, object> { { "type", "ping" }, { "id", id } });
			}
		}

		public bool RequestSuggestions(string input, Action<List<QuickCommandSuggestion>> callback)
		{
			if (!connected || callback == null) return false;
			string id = "s" + Interlocked.Increment(ref requestSequence).ToString(CultureInfo.InvariantCulture);
			lock (requestLock) suggestionCallbacks[id] = callback;
			if (!SendObject(new Dictionary<string, object> { { "type", "suggest-request" }, { "id", id }, { "input", NormalizeCommandForSend(input) } }))
			{
				lock (requestLock) suggestionCallbacks.Remove(id);
				return false;
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				Thread.Sleep(1500);
				lock (requestLock) suggestionCallbacks.Remove(id);
			});
			return true;
		}

		private void RequestCommandList()
		{
			string id = "c" + Interlocked.Increment(ref requestSequence).ToString(CultureInfo.InvariantCulture);
			SendObject(new Dictionary<string, object> { { "type", "command-list-request" }, { "id", id } });
		}

		private bool SendObject(Dictionary<string, object> value)
		{
			string json = new JavaScriptSerializer().Serialize(value);
			if (Encoding.UTF8.GetByteCount(json) > CommandBridgeMaximumLineLength) return false;
			lock (writerLock)
			{
				if (writer == null) return false;
				try { writer.WriteLine(json); return true; }
				catch { return false; }
			}
		}

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			connected = false;
			try { listener.Stop(); } catch { }
			lock (writerLock) { try { if (client != null) client.Close(); } catch { } client = null; writer = null; }
			lock (requestLock) suggestionCallbacks.Clear();
			try { if (acceptThread != Thread.CurrentThread) acceptThread.Join(2000); } catch { }
			try { if (pingThread != Thread.CurrentThread) pingThread.Join(2000); } catch { }
			DeleteBridgeSessionFile(serverDirectory);
			NotifyCommandBridgeChanged();
		}
	}

	private static List<QuickCommandDefinition> GetBuiltInQuickCommands()
	{
		List<QuickCommandDefinition> commands = new List<QuickCommandDefinition>();
		AddBuiltIn(commands, "server", "접속자 확인", "List players", "접속 중인 플레이어를 확인합니다.", "List online players.", "list", false, null);
		AddBuiltIn(commands, "server", "UUID 포함 접속자", "List player UUIDs", "UUID와 함께 접속자를 확인합니다.", "List players with UUIDs.", "list uuids", false, null);
		AddBuiltIn(commands, "server", "월드 저장", "Save world", "월드 변경 사항을 저장합니다.", "Save world changes.", "save-all", false, null);
		AddBuiltIn(commands, "server", "월드 저장·플러시", "Save and flush", "월드를 디스크에 즉시 저장합니다.", "Flush world data to disk.", "save-all flush", false, null);
		AddBuiltIn(commands, "server", "자동 저장 켜기", "Enable saving", "자동 저장을 켭니다.", "Enable automatic saving.", "save-on", false, null);
		AddBuiltIn(commands, "server", "자동 저장 끄기", "Disable saving", "자동 저장을 끕니다.", "Disable automatic saving.", "save-off", true, null);
		AddBuiltIn(commands, "server", "공지", "Broadcast", "서버 전체에 메시지를 보냅니다.", "Broadcast a message.", "say {message}", false, null);
		AddBuiltIn(commands, "server", "서버 종료", "Stop server", "월드를 저장하고 서버를 종료합니다.", "Save and stop the server.", "stop", true, null);
		AddBuiltIn(commands, "player", "게임 모드 변경", "Change game mode", "플레이어 게임 모드를 변경합니다.", "Change a player's game mode.", "gamemode {gamemode} {player}", false, null);
		AddBuiltIn(commands, "player", "플레이어 텔레포트", "Teleport player", "플레이어를 다른 플레이어에게 이동합니다.", "Teleport to another player.", "tp {player} {target}", false, null);
		AddBuiltIn(commands, "player", "좌표 텔레포트", "Teleport to coordinates", "플레이어를 좌표로 이동합니다.", "Teleport to coordinates.", "tp {player} {x} {y} {z}", false, null);
		AddBuiltIn(commands, "player", "아이템 지급", "Give item", "플레이어에게 아이템을 지급합니다.", "Give an item to a player.", "give {player} {item} {count}", false, null);
		AddBuiltIn(commands, "player", "레벨 경험치", "Add levels", "경험치 레벨을 추가합니다.", "Add experience levels.", "experience add {player} {amount} levels", false, null);
		AddBuiltIn(commands, "player", "포인트 경험치", "Add points", "경험치 포인트를 추가합니다.", "Add experience points.", "experience add {player} {amount} points", false, null);
		AddBuiltIn(commands, "player", "효과 제거", "Clear effects", "모든 상태 효과를 제거합니다.", "Clear status effects.", "effect clear {player}", false, null);
		AddBuiltIn(commands, "player", "효과 지급", "Give effect", "상태 효과를 적용합니다.", "Apply a status effect.", "effect give {player} {effect} {seconds} {amplifier}", false, null);
		AddBuiltIn(commands, "player", "플레이어 추방", "Kick player", "플레이어를 서버에서 내보냅니다.", "Kick a player.", "kick {player} {reason}", false, null);
		AddBuiltIn(commands, "player", "OP 지급", "Grant OP", "플레이어에게 OP 권한을 줍니다.", "Grant operator access.", "op {player}", true, null);
		AddBuiltIn(commands, "player", "OP 회수", "Revoke OP", "플레이어 OP 권한을 회수합니다.", "Revoke operator access.", "deop {player}", false, null);
		AddBuiltIn(commands, "player", "접속 차단", "Ban player", "플레이어 접속을 차단합니다.", "Ban a player.", "ban {player} {reason}", true, null);
		AddBuiltIn(commands, "player", "차단 해제", "Pardon player", "플레이어 차단을 해제합니다.", "Pardon a player.", "pardon {player}", false, null);
		AddBuiltIn(commands, "whitelist", "화이트리스트 켜기", "Enable whitelist", "화이트리스트를 켭니다.", "Enable the whitelist.", "whitelist on", false, null);
		AddBuiltIn(commands, "whitelist", "화이트리스트 끄기", "Disable whitelist", "화이트리스트를 끕니다.", "Disable the whitelist.", "whitelist off", true, null);
		AddBuiltIn(commands, "whitelist", "화이트리스트 추가", "Whitelist player", "플레이어를 추가합니다.", "Add a player to the whitelist.", "whitelist add {player}", false, null);
		AddBuiltIn(commands, "whitelist", "화이트리스트 제거", "Remove from whitelist", "플레이어를 제거합니다.", "Remove a player from the whitelist.", "whitelist remove {player}", false, null);
		AddBuiltIn(commands, "whitelist", "화이트리스트 목록", "List whitelist", "화이트리스트를 확인합니다.", "List whitelisted players.", "whitelist list", false, null);
		AddBuiltIn(commands, "whitelist", "화이트리스트 새로고침", "Reload whitelist", "화이트리스트 파일을 다시 읽습니다.", "Reload the whitelist.", "whitelist reload", false, null);
		string[] times = new string[] { "day", "noon", "night", "midnight" };
		for (int i = 0; i < times.Length; i++) AddBuiltIn(commands, "world", "시간: " + times[i], "Time: " + times[i], "월드 시간을 변경합니다.", "Change world time.", "time set " + times[i], false, null);
		string[] weather = new string[] { "clear", "rain", "thunder" };
		for (int i = 0; i < weather.Length; i++) AddBuiltIn(commands, "world", "날씨: " + weather[i], "Weather: " + weather[i], "월드 날씨를 변경합니다.", "Change world weather.", "weather " + weather[i], false, null);
		string[] difficulties = new string[] { "peaceful", "easy", "normal", "hard" };
		for (int i = 0; i < difficulties.Length; i++) AddBuiltIn(commands, "world", "난이도: " + difficulties[i], "Difficulty: " + difficulties[i], "서버 난이도를 변경합니다.", "Change server difficulty.", "difficulty " + difficulties[i], false, null);
		AddBuiltIn(commands, "world", "기본 게임 모드", "Default game mode", "새 플레이어 기본 게임 모드를 변경합니다.", "Change the default game mode.", "defaultgamemode {gamemode}", false, null);
		AddBuiltIn(commands, "world", "낮밤 순환", "Daylight cycle", "낮밤 순환을 설정합니다.", "Configure daylight cycle.", "gamerule doDaylightCycle {boolean}", false, null);
		AddBuiltIn(commands, "world", "날씨 순환", "Weather cycle", "날씨 순환을 설정합니다.", "Configure weather cycle.", "gamerule doWeatherCycle {boolean}", false, null);
		AddBuiltIn(commands, "world", "월드 스폰", "World spawn", "월드 스폰 좌표를 설정합니다.", "Set world spawn coordinates.", "setworldspawn {x} {y} {z}", false, null);
		AddBuiltIn(commands, "world", "플레이어 스폰", "Player spawn", "플레이어 스폰 좌표를 설정합니다.", "Set a player's spawn.", "spawnpoint {player} {x} {y} {z}", false, null);
		AddBuiltIn(commands, "info", "도움말", "Help", "명령 도움말을 표시합니다.", "Show command help.", "help", false, null);
		AddBuiltIn(commands, "info", "명령 도움말", "Command help", "특정 명령 도움말을 표시합니다.", "Show help for a command.", "help {command}", false, null);
		AddBuiltIn(commands, "info", "서버 버전", "Server version", "서버 구현 버전을 표시합니다.", "Show server implementation version.", "version", false, new string[] { "paper", "purpur" });
		AddBuiltIn(commands, "info", "플러그인 목록", "Plugin list", "설치된 플러그인을 표시합니다.", "List installed plugins.", "plugins", false, new string[] { "paper", "purpur" });
		AddBuiltIn(commands, "info", "데이터팩 목록", "Datapack list", "활성 데이터팩을 표시합니다.", "List datapacks.", "datapack list", false, null);
		return commands;
	}

	private static List<QuickCommandDefinition> BuildBridgeQuickCommandDefinitions(IEnumerable<QuickCommandSuggestion> suggestions)
	{
		List<QuickCommandDefinition> result = new List<QuickCommandDefinition>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (suggestions == null) return result;
		foreach (QuickCommandSuggestion suggestion in suggestions)
		{
			if (suggestion == null || string.IsNullOrWhiteSpace(suggestion.Plugin)) continue;
			string plugin = suggestion.Plugin.Trim();
			string command = NormalizeCommandForSend(suggestion.Value).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
			if (plugin.Length == 0 || command.Length == 0 || !seen.Add(plugin + "\n" + command)) continue;
			QuickCommandDefinition definition = new QuickCommandDefinition();
			definition.Id = "bridge-" + result.Count.ToString(CultureInfo.InvariantCulture);
			definition.Name = command;
			definition.Description = string.IsNullOrWhiteSpace(suggestion.Description) ? LauncherUiText("플러그인 명령", "Plugin command") : suggestion.Description;
			definition.Category = "plugin:" + plugin;
			definition.Template = command;
			definition.Parameters = new string[0];
			definition.Confirm = suggestion.Dangerous;
			definition.ServerTypes = new string[0];
			definition.Source = "bridge";
			result.Add(definition);
		}
		return result;
	}

	private static void AddBuiltIn(List<QuickCommandDefinition> list, string category, string koreanName, string englishName, string koreanDescription, string englishDescription, string template, bool confirm, string[] serverTypes)
	{
		QuickCommandDefinition command = new QuickCommandDefinition();
		command.Id = "builtin-" + list.Count.ToString(CultureInfo.InvariantCulture);
		command.Name = LauncherUiText(koreanName, englishName);
		command.Description = LauncherUiText(koreanDescription, englishDescription);
		command.Category = category;
		command.Template = template;
		command.Parameters = ExtractTemplateParameters(template).ToArray();
		command.Confirm = confirm;
		command.ServerTypes = serverTypes ?? new string[0];
		command.Source = "builtin";
		list.Add(command);
	}

	private static string GetQuickCommandsPath(string serversRoot)
	{
		return Path.Combine(Path.Combine(Path.GetFullPath(serversRoot), "config"), "quick-commands.json");
	}

	private static List<QuickCommandDefinition> LoadUserQuickCommands(string serversRoot)
	{
		string path = GetQuickCommandsPath(serversRoot);
		if (!File.Exists(path)) return new List<QuickCommandDefinition>();
		string json = File.ReadAllText(path, Encoding.UTF8);
		QuickCommandDefinition[] values = new JavaScriptSerializer().Deserialize<QuickCommandDefinition[]>(json) ?? new QuickCommandDefinition[0];
		List<QuickCommandDefinition> result = new List<QuickCommandDefinition>();
		for (int i = 0; i < values.Length; i++)
		{
			string error;
			if (ValidateUserQuickCommand(values[i], out error)) { values[i].Source = "user"; result.Add(values[i]); }
		}
		return result;
	}

	private static void SaveUserQuickCommands(string serversRoot, IList<QuickCommandDefinition> commands)
	{
		List<QuickCommandDefinition> validated = new List<QuickCommandDefinition>();
		for (int i = 0; i < commands.Count; i++)
		{
			string error;
			if (!ValidateUserQuickCommand(commands[i], out error)) throw new InvalidDataException(error);
			commands[i].Source = "user";
			validated.Add(commands[i]);
		}
		string path = GetQuickCommandsPath(serversRoot);
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string temporary = path + ".준비중";
		File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(validated.ToArray()), new UTF8Encoding(false));
		ReplaceFile(temporary, path);
	}

	private static bool ValidateUserQuickCommand(QuickCommandDefinition command, out string error)
	{
		error = string.Empty;
		if (command == null) { error = "명령 정보가 없습니다."; return false; }
		if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 60) { error = "표시 이름은 1~60자여야 합니다."; return false; }
		if (string.IsNullOrWhiteSpace(command.Template) || command.Template.Length > 512 || command.Template.IndexOf('\r') >= 0 || command.Template.IndexOf('\n') >= 0) { error = "명령 템플릿은 줄바꿈 없이 1~512자여야 합니다."; return false; }
		command.Template = NormalizeCommandForSend(command.Template);
		if (command.Template.Length == 0) { error = "실행할 명령을 입력해 주세요."; return false; }
		List<string> parameters;
		try { parameters = ExtractTemplateParameters(command.Template); }
		catch (InvalidDataException exception) { error = exception.Message; return false; }
		command.Parameters = parameters.ToArray();
		if (string.IsNullOrWhiteSpace(command.Id)) command.Id = Guid.NewGuid().ToString("N");
		if (string.IsNullOrWhiteSpace(command.Category)) command.Category = "user";
		if (command.Description == null) command.Description = string.Empty;
		if (command.ServerTypes == null) command.ServerTypes = new string[0];
		return true;
	}

	private static List<string> ExtractTemplateParameters(string template)
	{
		HashSet<string> allowed = new HashSet<string>(new string[] { "player", "online-player", "target", "gamemode", "difficulty", "item", "effect", "boolean", "number", "amount", "count", "seconds", "amplifier", "x", "y", "z", "message", "reason", "command" }, StringComparer.OrdinalIgnoreCase);
		List<string> result = new List<string>();
		for (int i = 0; i < template.Length; i++)
		{
			if (template[i] != '{') { if (template[i] == '}') throw new InvalidDataException("명령 템플릿의 중괄호가 올바르지 않습니다."); continue; }
			int end = template.IndexOf('}', i + 1);
			if (end < 0) throw new InvalidDataException("명령 템플릿의 중괄호가 닫히지 않았습니다.");
			string parameter = template.Substring(i + 1, end - i - 1).Trim();
			if (!allowed.Contains(parameter)) throw new InvalidDataException("지원하지 않는 매개변수입니다: {" + parameter + "}");
			if (!result.Contains(parameter, StringComparer.OrdinalIgnoreCase)) result.Add(parameter);
			i = end;
		}
		return result;
	}

	private static CommandParseResult ParseCommandInput(string input, int cursor)
	{
		string text = input ?? string.Empty;
		if (text.StartsWith("/", StringComparison.Ordinal)) text = text.Substring(1);
		cursor = Math.Max(0, Math.Min(cursor - ((input ?? string.Empty).StartsWith("/", StringComparison.Ordinal) ? 1 : 0), text.Length));
		CommandParseResult result = new CommandParseResult();
		result.Text = text;
		int index = 0;
		while (index < text.Length)
		{
			while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
			if (index >= text.Length) break;
			int start = index;
			bool quoted = text[index] == '"';
			StringBuilder value = new StringBuilder();
			if (quoted) index++;
			while (index < text.Length)
			{
				char character = text[index];
				if (quoted && character == '"') { index++; break; }
				if (!quoted && char.IsWhiteSpace(character)) break;
				if (character == '\\' && index + 1 < text.Length && quoted) { index++; character = text[index]; }
				value.Append(character);
				index++;
			}
			CommandToken token = new CommandToken(); token.Start = start; token.Length = index - start; token.Value = value.ToString(); token.Quoted = quoted; result.Tokens.Add(token);
		}
		result.CurrentIndex = result.Tokens.Count;
		result.ReplaceStart = cursor;
		result.ReplaceLength = 0;
		result.Prefix = string.Empty;
		for (int i = 0; i < result.Tokens.Count; i++)
		{
			CommandToken token = result.Tokens[i];
			if (cursor >= token.Start && cursor <= token.Start + token.Length)
			{
				result.CurrentIndex = i;
				result.ReplaceStart = token.Start;
				result.ReplaceLength = token.Length;
				int prefixStart = token.Quoted ? token.Start + 1 : token.Start;
				result.Prefix = text.Substring(prefixStart, Math.Max(0, Math.Min(cursor, token.Start + token.Length) - prefixStart));
				break;
			}
		}
		return result;
	}

	private static CommandTreeNode BuildQuickCommandTree(IEnumerable<QuickCommandDefinition> definitions, string serverType)
	{
		CommandTreeNode root = new CommandTreeNode();
		foreach (QuickCommandDefinition definition in definitions)
		{
			if (!QuickCommandSupportsServer(definition, serverType)) continue;
			string[] parts = SplitTemplate(definition.Template);
			CommandTreeNode current = root;
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i];
				bool argument = part.StartsWith("{", StringComparison.Ordinal) && part.EndsWith("}", StringComparison.Ordinal);
				CommandTreeNode child = current.Children.Find(delegate(CommandTreeNode item) { return item.Argument == argument && string.Equals(item.Value, part, StringComparison.OrdinalIgnoreCase); });
				if (child == null) { child = new CommandTreeNode(); child.Value = part; child.Argument = argument; current.Children.Add(child); }
				child.Description = definition.Description; child.Dangerous = definition.Confirm; current = child;
			}
			current.Definitions.Add(definition);
		}
		return root;
	}

	private static List<QuickCommandSuggestion> GetLocalQuickCommandSuggestions(string input, int cursor, string serverType, IEnumerable<QuickCommandDefinition> userCommands, IEnumerable<string> onlinePlayers, IEnumerable<string> history)
	{
		List<QuickCommandDefinition> definitions = GetBuiltInQuickCommands();
		if (userCommands != null) definitions.AddRange(userCommands);
		CommandParseResult parsed = ParseCommandInput(input, cursor);
		CommandTreeNode node = BuildQuickCommandTree(definitions, serverType);
		for (int i = 0; i < parsed.CurrentIndex && i < parsed.Tokens.Count; i++)
		{
			string value = parsed.Tokens[i].Value;
			CommandTreeNode next = node.Children.Find(delegate(CommandTreeNode child) { return !child.Argument && string.Equals(child.Value, value, StringComparison.OrdinalIgnoreCase); });
			if (next == null) next = node.Children.Find(delegate(CommandTreeNode child) { return child.Argument; });
			if (next == null) return new List<QuickCommandSuggestion>();
			node = next;
		}
		List<QuickCommandSuggestion> result = new List<QuickCommandSuggestion>();
		foreach (CommandTreeNode child in node.Children)
		{
			IEnumerable<string> values = child.Argument ? GetArgumentCandidates(child.Value.Trim('{', '}'), onlinePlayers) : new string[] { child.Value };
			foreach (string value in values)
			{
				if (string.IsNullOrEmpty(parsed.Prefix) || value.StartsWith(parsed.Prefix, StringComparison.OrdinalIgnoreCase))
				{
					QuickCommandSuggestion suggestion = NewSuggestion(value, GetNodeSyntax(child), child.Description, NodeSource(child), child.Dangerous || IsAdvancedDangerousCommand(value));
					suggestion.ReplaceStart = parsed.ReplaceStart + (((input ?? string.Empty).StartsWith("/", StringComparison.Ordinal)) ? 1 : 0);
					suggestion.ReplaceLength = parsed.ReplaceLength;
					result.Add(suggestion);
				}
			}
		}
		if (history != null)
		{
			foreach (string item in history)
			{
				string normalized = NormalizeCommandForSend(item);
				if (normalized.StartsWith(parsed.Text, StringComparison.OrdinalIgnoreCase))
				{
					QuickCommandSuggestion recent = NewSuggestion(normalized, normalized, LauncherUiText("최근 사용 명령", "Recent command"), "history", IsAdvancedDangerousCommand(normalized));
					recent.ReplaceStart = 0; recent.ReplaceLength = (input ?? string.Empty).Length; result.Add(recent);
				}
			}
		}
		return SortAndLimitSuggestions(result, parsed.Prefix, 10);
	}

	private static List<QuickCommandSuggestion> MergeQuickCommandSuggestions(IEnumerable<QuickCommandSuggestion> bridge, IEnumerable<QuickCommandSuggestion> local, int maximum)
	{
		List<QuickCommandSuggestion> merged = new List<QuickCommandSuggestion>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (IEnumerable<QuickCommandSuggestion> source in new IEnumerable<QuickCommandSuggestion>[] { bridge, local })
		{
			if (source == null) continue;
			foreach (QuickCommandSuggestion item in source)
			{
				if (item != null && !string.IsNullOrWhiteSpace(item.Value) && seen.Add(item.Value)) merged.Add(item);
				if (merged.Count >= maximum) return merged;
			}
		}
		return merged;
	}

	private static List<QuickCommandSuggestion> SortAndLimitSuggestions(List<QuickCommandSuggestion> values, string prefix, int maximum)
	{
		values.Sort(delegate(QuickCommandSuggestion left, QuickCommandSuggestion right)
		{
			int source = SourceRank(left.Source).CompareTo(SourceRank(right.Source)); if (source != 0) return source;
			int exact = string.Equals(left.Value, prefix, StringComparison.OrdinalIgnoreCase) ? -1 : string.Equals(right.Value, prefix, StringComparison.OrdinalIgnoreCase) ? 1 : 0; if (exact != 0) return exact;
			return string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);
		});
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<QuickCommandSuggestion> result = new List<QuickCommandSuggestion>();
		for (int i = 0; i < values.Count && result.Count < maximum; i++) if (seen.Add(values[i].Value)) result.Add(values[i]);
		return result;
	}

	private static string ApplyQuickCommandSuggestion(string input, QuickCommandSuggestion suggestion)
	{
		string text = input ?? string.Empty;
		int start = Math.Max(0, Math.Min(suggestion.ReplaceStart, text.Length));
		int length = Math.Max(0, Math.Min(suggestion.ReplaceLength, text.Length - start));
		return text.Substring(0, start) + suggestion.Value + text.Substring(start + length);
	}

	private static string NormalizeCommandForSend(string command)
	{
		string value = (command ?? string.Empty).Trim();
		while (value.StartsWith("/", StringComparison.Ordinal)) value = value.Substring(1).TrimStart();
		return value.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
	}

	private static bool RequiresQuickCommandConfirmation(string command, IEnumerable<QuickCommandDefinition> definitions)
	{
		string value = NormalizeCommandForSend(command);
		string root = value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		if (string.Equals(root, "stop", StringComparison.OrdinalIgnoreCase) || string.Equals(root, "op", StringComparison.OrdinalIgnoreCase) || string.Equals(root, "ban", StringComparison.OrdinalIgnoreCase)) return true;
		if (value.StartsWith("save-off", StringComparison.OrdinalIgnoreCase) || value.StartsWith("whitelist off", StringComparison.OrdinalIgnoreCase)) return true;
		if (definitions != null) foreach (QuickCommandDefinition item in definitions) if (item.Confirm && TemplateMatchesCommand(item.Template, value)) return true;
		return false;
	}

	private static bool IsAdvancedDangerousCommand(string command)
	{
		string root = NormalizeCommandForSend(command).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		return new HashSet<string>(new string[] { "kill", "clear", "fill", "clone", "setblock", "worldborder", "reload", "execute", "data", "summon", "forceload", "tick" }, StringComparer.OrdinalIgnoreCase).Contains(root);
	}

	private static IEnumerable<string> GetArgumentCandidates(string argument, IEnumerable<string> onlinePlayers)
	{
		if (argument == "gamemode") return new string[] { "survival", "creative", "adventure", "spectator" };
		if (argument == "difficulty") return new string[] { "peaceful", "easy", "normal", "hard" };
		if (argument == "boolean") return new string[] { "true", "false" };
		if (argument == "player" || argument == "online-player" || argument == "target")
		{
			List<string> values = new List<string>(new string[] { "@a", "@e", "@p", "@r", "@s" });
			if (onlinePlayers != null) values.AddRange(onlinePlayers.Where(delegate(string value) { return !string.IsNullOrWhiteSpace(value); }));
			return values;
		}
		if (argument == "item") return new string[] { "minecraft:stone", "minecraft:diamond", "minecraft:oak_log" };
		if (argument == "effect") return new string[] { "minecraft:speed", "minecraft:strength", "minecraft:regeneration" };
		if (argument == "x" || argument == "y" || argument == "z") return new string[] { "~", "~1", "0" };
		if (argument == "count" || argument == "amount" || argument == "number" || argument == "seconds" || argument == "amplifier") return new string[] { "1", "10", "64" };
		return new string[] { "{" + argument + "}" };
	}

	private static string[] SplitTemplate(string template) { return (template ?? string.Empty).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); }
	private static bool QuickCommandSupportsServer(QuickCommandDefinition item, string serverType) { return item.ServerTypes == null || item.ServerTypes.Length == 0 || item.ServerTypes.Any(delegate(string value) { return string.Equals(value, NormalizeServerType(serverType), StringComparison.OrdinalIgnoreCase); }); }
	private static bool TemplateMatchesCommand(string template, string command) { string[] left = SplitTemplate(template); string[] right = SplitTemplate(command); if (left.Length != right.Length) return false; for (int i = 0; i < left.Length; i++) if (!(left[i].StartsWith("{") && left[i].EndsWith("}")) && !string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase)) return false; return true; }
	private static string GetNodeSyntax(CommandTreeNode node) { return node.Definitions.Count > 0 ? node.Definitions[0].Template : node.Value; }
	private static string NodeSource(CommandTreeNode node) { return node.Definitions.Count > 0 ? node.Definitions[0].Source : "builtin"; }
	private static int SourceRank(string source) { return string.Equals(source, "bridge", StringComparison.OrdinalIgnoreCase) ? 0 : string.Equals(source, "user", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(source, "builtin", StringComparison.OrdinalIgnoreCase) ? 2 : 3; }
	private static QuickCommandSuggestion NewSuggestion(string value, string syntax, string description, string source, bool dangerous) { QuickCommandSuggestion item = new QuickCommandSuggestion(); item.Value = value; item.Syntax = syntax; item.Description = description ?? string.Empty; item.Source = source; item.Dangerous = dangerous; return item; }

	private static bool IsCommandBridgeSupported(string serverType, string minecraftVersion)
	{
		string normalized = NormalizeServerType(serverType);
		if (normalized != "paper" && normalized != "purpur") return false;
		MinecraftNumericVersion version;
		return TryParseMinecraftNumericVersion(minecraftVersion, out version) && (version.First >= 26 || (version.First == 1 && version.Second >= 13));
	}

	private static string GetBridgeJarPath(string serverDirectory) { return Path.Combine(Path.Combine(serverDirectory, "plugins"), CommandBridgeJarName); }
	private static string GetBridgeManagedPath(string serverDirectory) { return Path.Combine(serverDirectory, CommandBridgeManagedFileName); }
	private static string GetBridgeChoicePath(string serverDirectory) { return Path.Combine(serverDirectory, CommandBridgeChoiceFileName); }

	private static BridgeManagedInfo ReadBridgeManagedInfo(string serverDirectory)
	{
		string path = GetBridgeManagedPath(serverDirectory);
		if (!File.Exists(path)) return null;
		try { return new JavaScriptSerializer().Deserialize<BridgeManagedInfo>(File.ReadAllText(path, Encoding.UTF8)); } catch { return null; }
	}

	private static BridgeChoiceInfo ReadBridgeChoice(string serverDirectory)
	{
		string path = GetBridgeChoicePath(serverDirectory);
		if (!File.Exists(path)) return new BridgeChoiceInfo();
		try { return new JavaScriptSerializer().Deserialize<BridgeChoiceInfo>(File.ReadAllText(path, Encoding.UTF8)) ?? new BridgeChoiceInfo(); } catch { return new BridgeChoiceInfo(); }
	}

	private static void WriteBridgeChoice(string serverDirectory, string choice)
	{
		BridgeChoiceInfo info = new BridgeChoiceInfo(); info.Choice = choice; info.Asked = true;
		WriteJsonAtomic(GetBridgeChoicePath(serverDirectory), info);
	}

	private static string GetBridgeDefaultPreferencePath(string serversRoot)
	{
		return Path.Combine(Path.Combine(Path.GetFullPath(serversRoot), "config"), "command-bridge-preference.json");
	}

	private static BridgeDefaultPreferenceInfo ReadBridgeDefaultPreference(string serversRoot)
	{
		string path = GetBridgeDefaultPreferencePath(serversRoot);
		if (!File.Exists(path)) return new BridgeDefaultPreferenceInfo();
		try { return new JavaScriptSerializer().Deserialize<BridgeDefaultPreferenceInfo>(File.ReadAllText(path, Encoding.UTF8)) ?? new BridgeDefaultPreferenceInfo(); }
		catch { return new BridgeDefaultPreferenceInfo(); }
	}

	private static void WriteBridgeDefaultPreference(string serversRoot, string choice)
	{
		BridgeDefaultPreferenceInfo value = new BridgeDefaultPreferenceInfo(); value.HasDefault = true; value.Choice = choice;
		WriteJsonAtomic(GetBridgeDefaultPreferencePath(serversRoot), value);
	}

	private static void EnsureCommandBridgeChoiceAndInstallation(string serversRoot, LauncherOptions options)
	{
		if (options == null || !IsCommandBridgeSupported(options.ServerType, options.MinecraftVersion)) return;
		BridgeChoiceInfo profileChoice = ReadBridgeChoice(options.ServerDirectory);
		if (!profileChoice.Asked)
		{
			BridgeDefaultPreferenceInfo defaultPreference = ReadBridgeDefaultPreference(serversRoot);
			BridgeConsentResult consent;
			if (defaultPreference.HasDefault && (defaultPreference.Choice == "install" || defaultPreference.Choice == "skip"))
			{
				consent = new BridgeConsentResult(); consent.Choice = defaultPreference.Choice;
			}
			else
			{
				consent = AskCommandBridgeInstallConsent();
				if (consent == null || (consent.Choice != "install" && consent.Choice != "skip")) return;
				if (consent.UseAsDefault) WriteBridgeDefaultPreference(serversRoot, consent.Choice);
			}
			WriteBridgeChoice(options.ServerDirectory, consent.Choice);
			profileChoice = ReadBridgeChoice(options.ServerDirectory);
		}
		if (string.Equals(profileChoice.Choice, "install", StringComparison.OrdinalIgnoreCase) && ReadBridgeManagedInfo(options.ServerDirectory) == null)
		{
			try
			{
				ReportLauncherLoading(LauncherUiText("실시간 명령 브리지를 설치하고 있습니다…", "Installing the live command bridge…"), 82);
				InstallOrUpdateCommandBridge(options.ServerDirectory, options.ServerType, options.MinecraftVersion);
				Console.WriteLine(LauncherUiText("실시간 명령 브리지를 설치했습니다.", "Installed the live command bridge."));
			}
			catch (Exception exception)
			{
				// 브리지 실패는 서버 실행을 막지 않고 로컬 명령 기능으로 대체합니다.
				Console.WriteLine(LauncherUiText("실시간 명령 브리지를 설치하지 못해 기본 명령 목록을 사용합니다: ", "Bridge installation failed; using local commands: ") + exception.Message);
			}
		}
	}

	private static BridgeConsentResult AskCommandBridgeInstallConsent()
	{
		LauncherForm form = launcherForm;
		if (form == null || form.IsDisposed || !form.IsHandleCreated) return null;
		BridgeConsentResult result = null;
		System.Windows.Forms.MethodInvoker show = delegate { using (CommandBridgeConsentForm dialog = new CommandBridgeConsentForm()) if (dialog.ShowDialog(form) == System.Windows.Forms.DialogResult.OK) result = dialog.Result; };
		try
		{
			if (form.InvokeRequired) form.Invoke(show); else show();
		}
		catch (ObjectDisposedException) { return null; }
		catch (InvalidOperationException) { return null; }
		return result;
	}

	private static BridgeReleaseInfo GetBridgeReleaseInfo()
	{
		if (!string.IsNullOrEmpty(BridgeArtifactOverridePath) && File.Exists(BridgeArtifactOverridePath))
		{
			BridgeReleaseInfo local = new BridgeReleaseInfo(); local.Version = BuildVersionInfo.ProductVersion; local.Protocol = CommandBridgeProtocolVersion; local.MinimumMinecraft = "1.13"; local.MaximumMinecraft = "26.2"; local.Url = new Uri(Path.GetFullPath(BridgeArtifactOverridePath)).AbsoluteUri; local.Size = new FileInfo(BridgeArtifactOverridePath).Length; local.Sha256 = GetFileSha256(BridgeArtifactOverridePath); return local;
		}
		string json = DownloadTextWithUserAgent(GetLauncherUpdateMetadataUrl(), "Minecraft-Server-Launcher/0.4");
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
		Dictionary<string, object> bridge = root != null && root.ContainsKey("bridge") ? root["bridge"] as Dictionary<string, object> : null;
		if (bridge == null) throw new InvalidDataException("업데이트 정보에 명령 브리지 자산이 없습니다.");
		BridgeReleaseInfo info = new BridgeReleaseInfo(); info.Version = Convert.ToString(bridge["version"], CultureInfo.InvariantCulture); info.Protocol = Convert.ToInt32(bridge["protocol"], CultureInfo.InvariantCulture); info.MinimumMinecraft = Convert.ToString(bridge["minimum_minecraft"], CultureInfo.InvariantCulture); info.MaximumMinecraft = Convert.ToString(bridge["maximum_minecraft"], CultureInfo.InvariantCulture); info.Url = Convert.ToString(bridge["download_url"], CultureInfo.InvariantCulture); info.Sha256 = Convert.ToString(bridge["sha256"], CultureInfo.InvariantCulture); info.Size = Convert.ToInt64(bridge["size"], CultureInfo.InvariantCulture);
		if (info.Protocol != CommandBridgeProtocolVersion || info.Size < 1024 || info.Sha256.Length != 64 || !IsAllowedBridgeDownloadUrl(info.Url)) throw new InvalidDataException("명령 브리지 메타데이터를 검증하지 못했습니다.");
		return info;
	}

	private static bool InstallOrUpdateCommandBridge(string serverDirectory, string serverType, string minecraftVersion)
	{
		if (!IsCommandBridgeSupported(serverType, minecraftVersion)) throw new InvalidOperationException("이 서버 종류 또는 버전은 Paper/Purpur 명령 브리지를 지원하지 않습니다.");
		BridgeReleaseInfo release = GetBridgeReleaseInfo();
		if (!IsMinecraftVersionInBridgeRange(minecraftVersion, release.MinimumMinecraft, release.MaximumMinecraft)) throw new InvalidOperationException("선택한 Minecraft 버전은 이 명령 브리지 자산과 호환되지 않습니다.");
		string destination = GetBridgeJarPath(serverDirectory); string managedPath = GetBridgeManagedPath(serverDirectory); BridgeManagedInfo current = ReadBridgeManagedInfo(serverDirectory);
		if (File.Exists(destination) && current == null) throw new InvalidOperationException("같은 이름의 사용자 JAR이 있어 덮어쓰지 않았습니다.");
		if (File.Exists(destination) && current != null && !string.Equals(GetFileSha256(destination), current.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("관리 중인 브리지 JAR이 설치 후 변경되어 자동으로 덮어쓰지 않았습니다.");
		Directory.CreateDirectory(Path.GetDirectoryName(destination)); string temporary = destination + ".다운로드중"; string backup = destination + ".이전"; DeleteFileIfPresent(temporary); DeleteFileIfPresent(backup);
		try
		{
			if (!string.IsNullOrEmpty(BridgeArtifactOverridePath) && File.Exists(BridgeArtifactOverridePath)) File.Copy(BridgeArtifactOverridePath, temporary, true); else DownloadFileWithUserAgent(release.Url, temporary, "Minecraft-Server-Launcher/0.4");
			if (!ValidateCommandBridgeArtifact(temporary, release.Size, release.Sha256)) throw new InvalidDataException("명령 브리지 JAR 크기 또는 SHA-256 검증에 실패했습니다.");
			if (File.Exists(destination)) File.Copy(destination, backup, true);
			if (BridgeInstallFailureAfterBackup) throw new IOException("브리지 업데이트 복구 테스트 오류");
			ReplaceFile(temporary, destination);
			BridgeManagedInfo managed = new BridgeManagedInfo(); managed.JarName = Path.GetFileName(destination); managed.Version = release.Version; managed.Protocol = release.Protocol; managed.Sha256 = release.Sha256; managed.InstalledUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); WriteJsonAtomic(managedPath, managed); DeleteFileIfPresent(backup); return true;
		}
		catch
		{
			if (File.Exists(backup)) { File.Copy(backup, destination, true); DeleteFileIfPresent(backup); }
			DeleteFileIfPresent(temporary); throw;
		}
	}

	private static bool ValidateCommandBridgeArtifact(string path, long expectedSize, string expectedSha256)
	{
		return File.Exists(path) && new FileInfo(path).Length == expectedSize && expectedSha256 != null && expectedSha256.Length == 64 && HashMatches(path, expectedSha256) && BridgeJarLooksValid(path);
	}

	private static bool IsMinecraftVersionInBridgeRange(string value, string minimum, string maximum)
	{
		MinecraftNumericVersion current; MinecraftNumericVersion lower; MinecraftNumericVersion upper;
		if (!TryParseMinecraftNumericVersion(value, out current) || !TryParseMinecraftNumericVersion(minimum, out lower) || !TryParseMinecraftNumericVersion(maximum, out upper)) return false;
		return CompareMinecraftNumericVersion(current, lower) >= 0 && CompareMinecraftNumericVersion(current, upper) <= 0;
	}

	private static int CompareMinecraftNumericVersion(MinecraftNumericVersion left, MinecraftNumericVersion right)
	{
		if (left.First != right.First) return left.First.CompareTo(right.First);
		if (left.Second != right.Second) return left.Second.CompareTo(right.Second);
		return left.Third.CompareTo(right.Third);
	}

	private static bool RemoveManagedCommandBridge(string serverDirectory, bool removeData)
	{
		BridgeManagedInfo managed = ReadBridgeManagedInfo(serverDirectory); if (managed == null) return false;
		string jar = Path.Combine(Path.Combine(serverDirectory, "plugins"), managed.JarName ?? string.Empty);
		if (!string.Equals(Path.GetFullPath(jar), Path.GetFullPath(GetBridgeJarPath(serverDirectory)), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("관리 대상 브리지 경로가 올바르지 않습니다.");
		if (File.Exists(jar) && !string.Equals(GetFileSha256(jar), managed.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("브리지 JAR이 설치 후 변경되어 자동 삭제하지 않았습니다.");
		DeleteFileIfPresent(jar); DeleteFileIfPresent(GetBridgeManagedPath(serverDirectory));
		if (removeData) { string data = Path.Combine(Path.Combine(serverDirectory, "plugins"), "MinecraftServerLauncherCommandBridge"); if (Directory.Exists(data)) Directory.Delete(data, true); }
		return true;
	}

	private static bool BridgeJarLooksValid(string path)
	{
		try { using (ZipArchive archive = ZipFile.OpenRead(path)) return archive.GetEntry("plugin.yml") != null && archive.Entries.Any(delegate(ZipArchiveEntry entry) { return entry.FullName.EndsWith("CommandBridgePlugin.class", StringComparison.Ordinal); }); } catch { return false; }
	}

	private static bool IsAllowedBridgeDownloadUrl(string url)
	{
		Uri uri; if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
		return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsSuggestionGenerationCurrent(int expected, int current) { return expected == current; }
	private static string GetQuickCommandHistoryValue(IList<string> history, int currentIndex, int direction, out int nextIndex)
	{
		if (history == null || history.Count == 0) { nextIndex = -1; return string.Empty; }
		int index = currentIndex < 0 ? history.Count : currentIndex;
		nextIndex = Math.Max(0, Math.Min(history.Count - 1, index + direction));
		return history[nextIndex] ?? string.Empty;
	}

	private static CommandBridgeSession StartCommandBridgeSessionIfInstalled(string serverDirectory, string profileName)
	{
		BridgeManagedInfo managed = ReadBridgeManagedInfo(serverDirectory); string jar = GetBridgeJarPath(serverDirectory);
		if (managed == null || !File.Exists(jar) || !string.Equals(GetFileSha256(jar), managed.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
		CommandBridgeSession session = new CommandBridgeSession(serverDirectory, profileName); SetActiveCommandBridge(session); return session;
	}

	private static void SetActiveCommandBridge(CommandBridgeSession session)
	{
		lock (ActiveCommandBridgeLock) ActiveCommandBridge = session;
		NotifyCommandBridgeChanged();
	}

	private static CommandBridgeSession GetActiveCommandBridge() { lock (ActiveCommandBridgeLock) return ActiveCommandBridge; }
	private static void ClearActiveCommandBridge(CommandBridgeSession session) { lock (ActiveCommandBridgeLock) if (ActiveCommandBridge == session) ActiveCommandBridge = null; NotifyCommandBridgeChanged(); }

	private static void NotifyCommandBridgeChanged()
	{
		LauncherForm form = launcherForm;
		if (form != null) form.UpdateQuickCommandBridgeStatus();
	}

	private static string CreateBridgeToken() { byte[] bytes = new byte[32]; using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes); StringBuilder text = new StringBuilder(64); for (int i = 0; i < bytes.Length; i++) text.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture)); return text.ToString(); }
	private static bool BridgeTokenEquals(string expected, string actual) { if (expected == null || actual == null) return false; byte[] left = Encoding.UTF8.GetBytes(expected); byte[] right = Encoding.UTF8.GetBytes(actual); if (left.Length != right.Length) return false; int difference = 0; for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i]; return difference == 0; }

	private static void WriteBridgeSessionFile(string serverDirectory, string profileName, int port, string token)
	{
		Dictionary<string, object> value = new Dictionary<string, object>(); value["port"] = port; value["token"] = token; value["protocol"] = CommandBridgeProtocolVersion; value["profile"] = profileName; value["expiresUtc"] = DateTime.UtcNow.AddHours(12).ToString("o", CultureInfo.InvariantCulture); WriteJsonAtomic(Path.Combine(serverDirectory, CommandBridgeSessionFileName), value);
	}

	private static void DeleteBridgeSessionFile(string serverDirectory) { try { DeleteFileIfPresent(Path.Combine(serverDirectory, CommandBridgeSessionFileName)); } catch { } }
	private static void WriteJsonAtomic(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + ".준비중"; File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false)); ReplaceFile(temporary, path); }
	private static string ReadLimitedLine(StreamReader reader, int maximum) { StringBuilder result = new StringBuilder(); while (true) { int value = reader.Read(); if (value < 0) return result.Length == 0 ? null : result.ToString(); char character = (char)value; if (character == '\n') return result.ToString(); if (character != '\r') result.Append(character); if (result.Length > maximum) throw new InvalidDataException("브리지 요청 크기 제한을 초과했습니다."); } }
	private static Dictionary<string, object> DeserializeBridgeObject(string json) { if (string.IsNullOrEmpty(json) || json.Length > CommandBridgeMaximumLineLength) throw new InvalidDataException("브리지 JSON 크기가 올바르지 않습니다."); Dictionary<string, object> value = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; if (value == null || string.IsNullOrEmpty(BridgeString(value, "type")) || string.IsNullOrEmpty(BridgeString(value, "id"))) throw new InvalidDataException("브리지 JSON 형식이 올바르지 않습니다."); return value; }
	private static string BridgeString(Dictionary<string, object> value, string key) { return value != null && value.ContainsKey(key) ? Convert.ToString(value[key], CultureInfo.InvariantCulture) ?? string.Empty : string.Empty; }
	private static int BridgeInt(Dictionary<string, object> value, string key) { int result; return int.TryParse(BridgeString(value, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0; }
	private static bool BridgeStringEquals(Dictionary<string, object> value, string key, string expected) { return string.Equals(BridgeString(value, key), expected, StringComparison.Ordinal); }
	private static string[] BridgeStringArray(Dictionary<string, object> value, string key, int maximum) { object[] array = value != null && value.ContainsKey(key) ? value[key] as object[] : null; if (array == null) return new string[0]; List<string> result = new List<string>(); for (int i = 0; i < array.Length && result.Count < maximum; i++) { string item = Convert.ToString(array[i], CultureInfo.InvariantCulture); if (!string.IsNullOrWhiteSpace(item) && item.Length <= 512) result.Add(item); } return result.ToArray(); }
	private static void WriteBridgeError(StreamWriter writer, string id, string message) { writer.WriteLine(new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "type", "error" }, { "id", string.IsNullOrEmpty(id) ? "0" : id }, { "message", message } })); }

	private static List<QuickCommandSuggestion> ParseBridgeCommands(Dictionary<string, object> message)
	{
		List<QuickCommandSuggestion> result = new List<QuickCommandSuggestion>(); object[] commands = message.ContainsKey("commands") ? message["commands"] as object[] : null; if (commands == null) return result;
		for (int i = 0; i < commands.Length && result.Count < 1000; i++)
		{
			Dictionary<string, object> command = commands[i] as Dictionary<string, object>;
			if (command == null) continue;
			string name = new string(BridgeString(command, "name").Where(delegate(char value) { return !char.IsControl(value); }).Take(128).ToArray()).Trim();
			if (string.IsNullOrWhiteSpace(name)) continue;
			QuickCommandSuggestion suggestion = NewSuggestion(name, BridgeString(command, "usage"), BridgeString(command, "description"), "bridge", IsAdvancedDangerousCommand(name));
			string plugin = new string(BridgeString(command, "plugin").Where(delegate(char value) { return !char.IsControl(value); }).Take(80).ToArray()).Trim();
			suggestion.Plugin = plugin;
			result.Add(suggestion);
		}
		return result;
	}
}
