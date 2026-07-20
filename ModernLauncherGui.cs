﻿﻿﻿using System;

using System.Collections.Concurrent;

using System.Collections.Generic;

using System.Drawing;

using System.Drawing.Drawing2D;

using System.Globalization;

using System.IO;

using System.Text;

using System.Threading;

using System.Threading.Tasks;

using System.Diagnostics;

using System.Windows.Forms;

using System.Net;



internal static partial class Launcher

{

	private static LauncherForm launcherForm;

	private static readonly object ServerProcessLock = new object();

	private static System.Diagnostics.Process currentServerProcess;

	private static int currentServerStopRequested;

	private static string currentSelectedJavaPath;

	private static volatile bool launcherUpdateCheckCompleted;

	private static Icon launcherWindowIcon;

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, ToolTip> FormToolTips = new System.Runtime.CompilerServices.ConditionalWeakTable<Form, ToolTip>();

	private const int WmSetRedraw = 0x000B;

	private const int EmGetScrollPos = 0x04DD;

	private const int EmSetScrollPos = 0x04DE;



	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]

	private struct RichTextScrollPoint

	{

		public int X;

		public int Y;

	}



	private sealed class RichTextUpdateState

	{

		public bool FollowTail;

		public int SelectionStart;

		public int SelectionLength;

		public RichTextScrollPoint Scroll;

	}



	private enum ConsoleLineKind

	{

		Information,

		Warning,

		Compatibility,

		Error

	}



	private static ConsoleLineKind ClassifyConsoleLine(string line)

	{

		string value = line ?? string.Empty;

		if (value.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("Caused by", StringComparison.OrdinalIgnoreCase) >= 0)

		{

			return ConsoleLineKind.Error;

		}

		if (value.IndexOf("Advanced terminal features are not available", StringComparison.OrdinalIgnoreCase) >= 0 ||

			value.IndexOf("terminally deprecated method in sun.misc.Unsafe", StringComparison.OrdinalIgnoreCase) >= 0 ||

			value.IndexOf("sun.misc.Unsafe::", StringComparison.OrdinalIgnoreCase) >= 0 ||

			value.IndexOf("Please consider reporting this to the maintainers", StringComparison.OrdinalIgnoreCase) >= 0 ||

			value.IndexOf("will be removed in a future release", StringComparison.OrdinalIgnoreCase) >= 0)

		{

			return ConsoleLineKind.Compatibility;

		}

		if (value.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0 || value.TrimStart().StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))

		{

			return ConsoleLineKind.Warning;

		}

		return ConsoleLineKind.Information;

	}



	private static bool ConsoleLineMatchesFilter(string line, int filterIndex)

	{

		if (filterIndex <= 0)

		{

			return true;

		}

		ConsoleLineKind kind = ClassifyConsoleLine(line);

		return (filterIndex == 1 && kind == ConsoleLineKind.Warning) ||

			(filterIndex == 2 && kind == ConsoleLineKind.Compatibility) ||

			(filterIndex == 3 && kind == ConsoleLineKind.Error);

	}



	[System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]

	private static extern IntPtr SendRichTextMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);



	[System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]

	private static extern IntPtr SendRichTextPointMessage(IntPtr window, int message, IntPtr wParam, ref RichTextScrollPoint point);



	private static RichTextUpdateState BeginStableRichTextUpdate(RichTextBox box)

	{

		RichTextUpdateState state = new RichTextUpdateState();

		state.FollowTail = IsRichTextAtBottom(box);

		state.SelectionStart = box.SelectionStart;

		state.SelectionLength = box.SelectionLength;

		if (box.IsHandleCreated)

		{

			SendRichTextPointMessage(box.Handle, EmGetScrollPos, IntPtr.Zero, ref state.Scroll);

			SendRichTextMessage(box.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);

		}

		box.SuspendLayout();

		return state;

	}



	private static void EndStableRichTextUpdate(RichTextBox box, RichTextUpdateState state)

	{

		try

		{

			if (state.FollowTail)

			{

				box.SelectionStart = box.TextLength;

				box.SelectionLength = 0;

				box.ScrollToCaret();

			}

			else

			{

				box.SelectionStart = Math.Min(state.SelectionStart, box.TextLength);

				box.SelectionLength = Math.Min(state.SelectionLength, Math.Max(0, box.TextLength - box.SelectionStart));

				if (box.IsHandleCreated)

				{

					SendRichTextPointMessage(box.Handle, EmSetScrollPos, IntPtr.Zero, ref state.Scroll);

				}

			}

		}

		finally

		{

			box.ResumeLayout();

			if (box.IsHandleCreated)

			{

				SendRichTextMessage(box.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);

				box.Invalidate();

			}

		}

	}



	private static bool IsRichTextAtBottom(RichTextBox box)

	{

		if (box == null || box.TextLength == 0 || box.ClientSize.Height <= 2)

		{

			return true;

		}

		int y = Math.Max(1, box.ClientSize.Height - 3);

		int lastLeft = box.GetCharIndexFromPosition(new Point(2, y));

		int lastMiddle = box.GetCharIndexFromPosition(new Point(Math.Max(2, box.ClientSize.Width / 2), y));

		return Math.Max(lastLeft, lastMiddle) >= Math.Max(0, box.TextLength - 2);

	}



	private static void ApplyCommonButtonToolTips(Form form)

	{

		if (form == null || form.IsDisposed)

		{

			return;

		}

		ToolTip toolTip;

		if (!FormToolTips.TryGetValue(form, out toolTip))

		{

			toolTip = new ToolTip();

			toolTip.InitialDelay = 350;

			toolTip.ReshowDelay = 100;

			toolTip.AutoPopDelay = 7000;

			FormToolTips.Add(form, toolTip);

		}

		toolTip.RemoveAll();

		ApplyCommonButtonToolTipsRecursive(form, toolTip);

	}



	private static void ApplyCommonButtonToolTipsRecursive(Control parent, ToolTip toolTip)

	{

		foreach (Control control in parent.Controls)

		{

			string descKey = control.AccessibleDescription;

			if (!string.IsNullOrEmpty(descKey) && descKey.StartsWith("Tooltip."))

			{

				toolTip.SetToolTip(control, Localization.T(descKey));

			}



			if (control is ButtonBase)

			{

				string accessibleName = (control.Text ?? string.Empty).Replace("&", string.Empty).Trim();

				if (!string.IsNullOrEmpty(accessibleName))

				{

					control.AccessibleName = accessibleName;

				}

			}



			if (control.HasChildren)

			{

				ApplyCommonButtonToolTipsRecursive(control, toolTip);

			}

		}

	}



	private static void ConfigureAccessibleField(Control control, string name, string description)

	{

		if (control == null)

		{

			return;

		}

		control.AccessibleName = name ?? string.Empty;

		control.AccessibleDescription = description ?? string.Empty;

	}



	private static int GetMarqueeAnimationSpeed()

	{

		return SystemInformation.IsMenuAnimationEnabled || SystemInformation.IsMinimizeRestoreAnimationEnabled ? 28 : 0;

	}



	private static void ApplyButtonIcon(Button button, ButtonIcon icon)

	{

		RoundedButton roundedButton = button as RoundedButton;

		if (roundedButton != null)

		{

			roundedButton.IconKind = icon;

		}

	}



	private static void EnsureButtonContentFits(Button button)

	{

		if (button == null)

		{

			return;

		}

		RoundedButton roundedButton = button as RoundedButton;

		bool hasIcon = roundedButton != null && roundedButton.IconKind != ButtonIcon.None;

		Size measured = TextRenderer.MeasureText(button.Text ?? string.Empty, button.Font, new Size(4096, Math.Max(1, button.Height)), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

		int requiredWidth = measured.Width + (hasIcon ? 46 : 24);

		if (button.Width < requiredWidth)

		{

			button.Width = requiredWidth;

		}

		if (button.MinimumSize.Width < requiredWidth)

		{

			button.MinimumSize = new Size(requiredWidth, button.MinimumSize.Height);

		}

	}





	[System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]

	private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);



	private static void ApplyLauncherTaskbarIdentity()

	{

		try

		{

			SetCurrentProcessExplicitAppUserModelID("MineHarbor.MinecraftServerLauncher");

		}

		catch

		{

			// 구형 Windows에서 작업표시줄 식별자를 지원하지 않아도 실행은 계속합니다.

		}

	}



	private static void ApplyLauncherWindowIcon(Form form)

	{

		if (form == null)

		{

			return;

		}

		try

		{

			if (launcherWindowIcon == null)

			{

				using (Icon extracted = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location))

				{

					if (extracted != null)

					{

						launcherWindowIcon = (Icon)extracted.Clone();

					}

				}

			}

			if (launcherWindowIcon != null)

			{

				form.Icon = launcherWindowIcon;

			}

		}

		catch

		{

			// 아이콘을 읽지 못해도 서버 실행 기능은 유지합니다.

		}

	}



	private static bool TryPostToUi(Control control, MethodInvoker action)

	{

		if (control == null || action == null || control.IsDisposed || control.Disposing || !control.IsHandleCreated)

		{

			return false;

		}

		try

		{

			control.BeginInvoke(action);

			return true;

		}

		catch (ObjectDisposedException)

		{

			return false;

		}

		catch (InvalidOperationException)

		{

			// 창이 닫히는 순간과 겹친 결과 갱신은 안전하게 생략합니다.

			return false;

		}

	}



	private static int RunGuiApplication()

	{

		// SEC-M4: 전역 TLS 1.2+ 강제 (구형 .NET Framework에서 TLS 1.0/1.1 폴백 방지)

		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

		try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { } // TLS 1.3 if available

		Application.EnableVisualStyles();

		Application.SetCompatibleTextRenderingDefault(false);

		ApplyLauncherTaskbarIdentity();

		launcherForm = new LauncherForm();

		if (!string.IsNullOrEmpty(PendingLauncherUpdateDirectory))

		{

			launcherForm.Shown += delegate

			{

				string updateDirectory = PendingLauncherUpdateDirectory;

				PendingLauncherUpdateDirectory = null;

				ConfirmLauncherUpdateStarted(updateDirectory);

			};

		}

		Console.SetOut(new LauncherTextWriter());

		Console.SetError(new LauncherTextWriter());

		Application.Run(launcherForm);

		launcherForm = null;

		return 0;

	}



	private static bool IsGuiMode()

	{

		return launcherForm != null;

	}



	private static void ShowLauncherMessage(string message, bool error)

	{

		ShowMineHarborDialog(message, error ? Localization.T("App.Error") : Localization.T("App.Title"), MessageBoxButtons.OK, error ? MessageBoxIcon.Error : MessageBoxIcon.Information);

	}



	private static void RequestLauncherClose()

	{

		LauncherForm form = launcherForm;

		if (form != null)

		{

			form.RequestClose();

		}

	}



	private static void SetLauncherConnectionAddress(string address)

	{

		LauncherForm form = launcherForm;

		if (form != null)

		{

			form.SetConnectionAddress(address);

		}

	}



	private static void ShowLauncherNotice(string message, bool warning)

	{

		LauncherForm form = launcherForm;

		if (form != null)

		{

			form.ShowNotice(message, warning);

		}

	}



	private static void ReportLauncherLoading(string message, int percent)

	{

		LauncherForm form = launcherForm;

		if (form != null)

		{

			form.SetLoadingState(message, true, percent);

		}

	}



	private static string LauncherUiText(string korean, string english)

	{

		return string.Equals(Localization.CurrentLanguage, Localization.English, StringComparison.OrdinalIgnoreCase) ? english : korean;

	}



	private static void FinishLauncherLoading()

	{

		LauncherForm form = launcherForm;

		if (form != null)

		{

			form.SetLoadingState(string.Empty, false, -1);

		}

	}



	private static string GetLocalConnectionAddress(int port)

	{

		NetworkDetails details = GetNetworkDetails();

		string ip = string.IsNullOrEmpty(details.LocalIpv4) ? "127.0.0.1" : details.LocalIpv4;

		return ip + ":" + port;

	}



	private static void NotifyServerStarted(int port)

	{

		LauncherForm form = launcherForm;

		if (form != null)

		{

			form.ServerStarted(GetLocalConnectionAddress(port));

		}

	}



	private static void NotifyServerReady(int port)

	{

		LauncherForm form = launcherForm;

		if (form != null)

		{

			form.ServerReady(GetLocalConnectionAddress(port));

		}

	}



	private static void SetCurrentServerProcess(System.Diagnostics.Process process)

	{

		lock (ServerProcessLock)

		{

			currentServerProcess = process;

			Interlocked.Exchange(ref currentServerStopRequested, 0);

		}

	}



	private static void ClearCurrentServerProcess(System.Diagnostics.Process process)

	{

		lock (ServerProcessLock)

		{

			if (object.ReferenceEquals(currentServerProcess, process))

			{

				currentServerProcess = null;

			}

		}

	}



	private static bool SendServerCommand(string command)

	{

		// SEC-M3: 줄바꿈 및 선행 슬래시를 정규화하여 명령 주입 방지

		string normalized = NormalizeCommandForSend(command);

		if (string.IsNullOrWhiteSpace(normalized))

		{

			return false;

		}

		lock (ServerProcessLock)

		{

			if (currentServerProcess == null || currentServerProcess.HasExited)

			{

				return false;

			}

			currentServerProcess.StandardInput.WriteLine(normalized);

			currentServerProcess.StandardInput.Flush();

			if (string.Equals(normalized.Trim(), "stop", StringComparison.OrdinalIgnoreCase))

			{

				Interlocked.Exchange(ref currentServerStopRequested, 1);

			}

			return true;

		}

	}



	private static bool AskForEulaGui()

	{

		DialogResult result = ShowMineHarborDialog(

			Localization.T("Eula.Text"),

			Localization.T("Eula.Title"),

			MessageBoxButtons.YesNo,

			MessageBoxIcon.Question);

		return result == DialogResult.Yes;

	}



	private static LauncherOptions ConfigureServerPropertiesGui(string serversRootDirectory, bool forceDialog)

	{

		Directory.CreateDirectory(serversRootDirectory);

		string activeProfileName = ReadActiveProfileName(serversRootDirectory);

		string serverDirectory = GetProfileDirectory(serversRootDirectory, activeProfileName);

		Directory.CreateDirectory(serverDirectory);

		string markerPath = Path.Combine(serverDirectory, ".launcher-properties-configured");

		Dictionary<string, string> launcherProperties = ReadSimpleProperties(markerPath);

		int recommendedMemory = ChooseMaximumMemoryGb();

		int maximumMemory = GetSafeMemoryMaximumGb();

		LauncherOptions currentOptions = launcherProperties.Count > 0

			? ReadLauncherOptionsFromProperties(serversRootDirectory, launcherProperties, activeProfileName, recommendedMemory, true)

			: new LauncherOptions();

		if (launcherProperties.Count == 0)

		{

			currentOptions.ProfileName = activeProfileName;

			currentOptions.ServerDirectory = serverDirectory;

			currentOptions.ServerType = "paper";

			currentOptions.MinecraftVersion = "26.2";

			currentOptions.IncludeSnapshots = false;

			currentOptions.UseManualJar = false;

			currentOptions.ManualJarPath = string.Empty;

			currentOptions.CustomJavaMajor = 25;

			currentOptions.MemoryGb = recommendedMemory;

			currentOptions.AutoUpdate = true;

			currentOptions.OwnerName = GetDefaultOwnerName();

		}

		bool validOptions = currentOptions.MemoryGb >= 2

			&& currentOptions.MemoryGb <= maximumMemory

			&& IsValidOwnerName(currentOptions.OwnerName)

			&& IsValidProfileName(currentOptions.ProfileName);



		if (validOptions && !forceDialog)

		{

			return currentOptions;

		}



		ServerSettings settings = LoadServerSettings(currentOptions.ServerDirectory, currentOptions);

		ServerSettings selected = null;

		while (selected == null)

		{

			ServerSetupForm setup = null;

			DialogResult dialogResult = DialogResult.Cancel;

			Action showDialog = delegate

			{

				setup = new ServerSetupForm(settings, recommendedMemory, maximumMemory, File.Exists(markerPath), Directory.Exists(Path.Combine(currentOptions.ServerDirectory, "world")));

				dialogResult = setup.ShowDialog(launcherForm);

			};

			if (launcherForm != null && launcherForm.InvokeRequired)

			{

				launcherForm.Invoke(showDialog);

			}

			else

			{

				showDialog();

			}

			if (dialogResult != DialogResult.OK || setup == null)

			{

				throw new OperationCanceledException("서버 설정이 취소되었습니다.");

			}



			ServerSettings candidate = setup.SelectedSettings;

			LauncherOptions candidateOptions = CreateLauncherOptionsFromSettings(serversRootDirectory, candidate);

			bool changesDirectory = !string.Equals(Path.GetFullPath(candidateOptions.ServerDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(currentOptions.ServerDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

			if (changesDirectory && Directory.Exists(candidateOptions.ServerDirectory) && Directory.GetFileSystemEntries(candidateOptions.ServerDirectory).Length > 0)

			{

				ShowMineHarborDialog(launcherForm, Localization.CurrentLanguage == Localization.Korean ? "같은 이름의 서버 프로필이 이미 있습니다. 다른 이름을 입력해 주세요." : "A server profile with that name already exists. Choose another name.", Localization.CurrentLanguage == Localization.Korean ? "프로필 이름 중복" : "Duplicate profile name", MessageBoxButtons.OK, MessageBoxIcon.Warning);

				settings = candidate;

				continue;

			}

			selected = candidate;

		}

		if (Directory.Exists(currentOptions.ServerDirectory)

			&& File.Exists(Path.Combine(currentOptions.ServerDirectory, "server.properties"))

			&& (!string.Equals(selected.ServerType, currentOptions.ServerType, StringComparison.OrdinalIgnoreCase) || !string.Equals(selected.MinecraftVersion, currentOptions.MinecraftVersion, StringComparison.OrdinalIgnoreCase)))

		{

			CreateServerBackup(currentOptions.ServerDirectory);

		}

		LauncherOptions selectedOptions = CreateLauncherOptionsFromSettings(serversRootDirectory, selected);

		bool movesProfileDirectory = !string.Equals(Path.GetFullPath(selectedOptions.ServerDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(currentOptions.ServerDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

		if (movesProfileDirectory && Directory.Exists(currentOptions.ServerDirectory))

		{

			if (Directory.Exists(selectedOptions.ServerDirectory))

			{

				Directory.Delete(selectedOptions.ServerDirectory);

			}

			Directory.Move(currentOptions.ServerDirectory, selectedOptions.ServerDirectory);

		}

		currentOptions = selectedOptions;

		Directory.CreateDirectory(currentOptions.ServerDirectory);

		string propertiesPath = Path.Combine(currentOptions.ServerDirectory, "server.properties");

		BackupConfigurationFile(propertiesPath);

		ApplyServerProperties(propertiesPath, selected);

		WriteLauncherOptions(Path.Combine(currentOptions.ServerDirectory, ".launcher-properties-configured"), currentOptions);

		WriteActiveProfileName(serversRootDirectory, currentOptions.ProfileName);

		Console.WriteLine("서버 설정을 저장했습니다: " + propertiesPath);

		if (selected.WhiteList)

		{

			Console.WriteLine("화이트리스트가 켜져 있습니다. 콘솔에서 whitelist add 플레이어이름 명령으로 사용자를 추가하세요.");

		}

		return currentOptions;

	}



	private static ServerSettings LoadServerSettings(string serverDirectory, LauncherOptions options)

	{

		ServerSettings settings = new ServerSettings();

		settings.ProfileName = options.ProfileName;

		settings.ServerType = NormalizeServerType(options.ServerType);

		settings.MinecraftVersion = options.MinecraftVersion;

		settings.IncludeSnapshots = options.IncludeSnapshots;

		settings.UseManualJar = options.UseManualJar;

		settings.ManualJarPath = options.ManualJarPath;

		settings.CustomJavaMajor = options.CustomJavaMajor >= 8 && options.CustomJavaMajor <= 30 ? options.CustomJavaMajor : 25;

		settings.PresetName = "보통 야생";

		settings.GameMode = "survival";

		settings.Difficulty = "normal";

		settings.LevelType = "minecraft:normal";

		settings.MaxPlayers = 20;

		settings.Motd = "A Minecraft Server";

		settings.ServerPort = 25565;

		settings.Pvp = true;

		settings.WhiteList = false;

		settings.Hardcore = false;

		settings.ViewDistance = 32;

		settings.SimulationDistance = 32;

		settings.CommandBlock = true;

		settings.OnlineMode = true;

		settings.MemoryGb = options.MemoryGb;

		settings.AutoUpdate = options.AutoUpdate;

		settings.OwnerName = options.OwnerName;



		Dictionary<string, string> values = ReadSimpleProperties(Path.Combine(serverDirectory, "server.properties"));

		settings.GameMode = GetStringProperty(values, "gamemode", settings.GameMode);

		settings.Difficulty = GetStringProperty(values, "difficulty", settings.Difficulty);

		settings.LevelType = GetStringProperty(values, "level-type", settings.LevelType);

		settings.Motd = GetStringProperty(values, "motd", settings.Motd).Replace("\\n", " ").Replace("\\\\", "\\");

		settings.MaxPlayers = GetIntegerProperty(values, "max-players", settings.MaxPlayers, 1, 1000);

		settings.ServerPort = GetIntegerProperty(values, "server-port", settings.ServerPort, 1, 65535);

		settings.ViewDistance = GetIntegerProperty(values, "view-distance", settings.ViewDistance, 3, 32);

		settings.SimulationDistance = GetIntegerProperty(values, "simulation-distance", settings.SimulationDistance, 3, 32);

		settings.Pvp = GetBooleanProperty(values, "pvp", settings.Pvp);

		settings.WhiteList = GetBooleanProperty(values, "white-list", settings.WhiteList);

		settings.Hardcore = GetBooleanProperty(values, "hardcore", settings.Hardcore);

		settings.CommandBlock = GetBooleanProperty(values, "enable-command-block", settings.CommandBlock);

		settings.OnlineMode = GetBooleanProperty(values, "online-mode", settings.OnlineMode);

		settings.PresetName = DetectPresetName(settings);

		return settings;

	}



	private static string GetStringProperty(Dictionary<string, string> values, string key, string fallback)

	{

		return values.ContainsKey(key) && !string.IsNullOrEmpty(values[key]) ? values[key] : fallback;

	}



	private static int GetIntegerProperty(Dictionary<string, string> values, string key, int fallback, int minimum, int maximum)

	{

		int value;

		return values.ContainsKey(key) && int.TryParse(values[key], out value) && value >= minimum && value <= maximum ? value : fallback;

	}



	private static bool GetBooleanProperty(Dictionary<string, string> values, string key, bool fallback)

	{

		bool value;

		return values.ContainsKey(key) && bool.TryParse(values[key], out value) ? value : fallback;

	}



	private static string DetectPresetName(ServerSettings settings)

	{

		if (settings.Hardcore && settings.GameMode == "survival" && settings.Difficulty == "hard")

		{

			return "하드코어 야생";

		}

		if (settings.GameMode == "creative")

		{

			return settings.LevelType == "minecraft:flat" ? "크리에이티브 월드 (평지)" : "크리에이티브 월드 (일반 지형)";

		}

		if (settings.GameMode == "survival")

		{

			if (settings.Difficulty == "peaceful") return "평화로움 야생";

			if (settings.Difficulty == "easy") return "쉬움 야생";

			if (settings.Difficulty == "normal") return "보통 야생";

			if (settings.Difficulty == "hard") return "어려움 야생";

		}

		return "직접 설정";

	}



	private sealed class LauncherTextWriter : TextWriter

	{

		private readonly object sync = new object();

		private readonly StringBuilder pending = new StringBuilder();



		public override Encoding Encoding

		{

			get { return new UTF8Encoding(false); }

		}



		public override void Write(char value)

		{

			lock (sync)

			{

				if (value == '\n' || value == '\r')

				{

					if (pending.Length > 0)

					{

						FlushPending();

					}

					return;

				}

				pending.Append(value);

			}

		}



		public override void Write(string value)

		{

			if (value == null)

			{

				return;

			}

			foreach (char character in value)

			{

				Write(character);

			}

		}



		public override void WriteLine(string value)

		{

			lock (sync)

			{

				if (!string.IsNullOrEmpty(value))

				{

					pending.Append(value);

				}

				FlushPending();

			}

		}



		private void FlushPending()

		{

			string line = pending.ToString();

			pending.Length = 0;

			LauncherForm form = launcherForm;

			if (form != null)

			{

				form.AppendConsole(line);

			}

		}

	}



	private sealed partial class LauncherForm : Form, IMessageFilter

	{

		private readonly RoundedPanel statusPill;

		private readonly Label statusLabel;

		private readonly Label statusDot;

		private readonly Label noticeLabel;

		private readonly Panel loadingPanel;

		private readonly Label loadingDetailLabel;

		private readonly RoundedProgressBar loadingProgress;

		private readonly TextBox addressBox;

		private readonly Button copyButton;

		private readonly Button startButton;

		private readonly Button stopButton;

		private readonly Button settingsButton;

		private readonly Button upgradeButton;

		private readonly Button consoleButton;

		private readonly Button profilesButton;

		private readonly Button multiServerButton;

		private readonly Button backupButton;

		private readonly Button contentButton;

		private readonly Button playersButton;

		private readonly Button networkButton;

		private readonly Button diagnosticsButton;

		private readonly Button themeButton;

		private readonly Button languageButton;

		private readonly Button launcherUpdateButton;

		private readonly Panel consolePanel;

		private readonly RichTextBox consoleBox;

		private readonly TextBox commandBox;

		private readonly Button sendButton;

		private readonly TextBox consoleSearchBox;

		private readonly ComboBox consoleFilterBox;

		private readonly CheckBox consoleWrapBox;

		private readonly List<string> consoleHistory = new List<string>();

		private readonly Dictionary<Control, string> localizedControls = new Dictionary<Control, string>();

		private readonly Dictionary<string, Form> modelessToolWindows = new Dictionary<string, Form>(StringComparer.Ordinal);

		private readonly HashSet<string> modelessToolsBlockingServerChanges = new HashSet<string>(StringComparer.Ordinal);
		private int ownedWindowClickGuardUntilTick;
		private bool clickFilterInstalled;

		private readonly ConcurrentQueue<string> consoleQueue = new ConcurrentQueue<string>();

		private readonly System.Windows.Forms.Timer consoleTimer;

		private int queuedConsoleLines;

		private bool darkTheme;

		private bool statusWarning;

		private bool noticeWarning;

		private bool workflowRunning;

		private bool serverRunning;

		private bool closeAfterStop;

		private bool startupInitializing;

		private bool launcherUpdateChecking;

		private readonly string themePath;

		private readonly string languagePath;

		private string statusTextKey;

		private string noticeTextKey;



		public bool UsesDarkTheme

		{

			get { return darkTheme; }

		}



		public LauncherForm()

		{

			ApplyLauncherWindowIcon(this);
			Application.AddMessageFilter(this);
			clickFilterInstalled = true;
			Disposed += delegate
			{
				if (!clickFilterInstalled) return;
				Application.RemoveMessageFilter(this);
				clickFilterInstalled = false;
			};
			this.HandleCreated += (s, e) => TitleBarDwm.ApplyTheme(this, ThemePalette.Create(darkTheme).Window, ThemePalette.Create(darkTheme).Text, ThemePalette.Create(darkTheme).Border);

			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

			string legacyDir = Path.Combine(localAppData, "Paper26.2Server");

			string newDir = Path.Combine(localAppData, "MineHarbor");

			

			themePath = Path.Combine(newDir, "launcher-ui.properties");

			languagePath = Path.Combine(newDir, "launcher-language.properties");



			try

			{

				if (Directory.Exists(legacyDir))

				{

					Directory.CreateDirectory(newDir);

					string legacyTheme = Path.Combine(legacyDir, "launcher-ui.properties");

					string legacyLang = Path.Combine(legacyDir, "launcher-language.properties");

					if (File.Exists(legacyTheme) && !File.Exists(themePath)) File.Move(legacyTheme, themePath);

					if (File.Exists(legacyLang) && !File.Exists(languagePath)) File.Move(legacyLang, languagePath);

				}

			}

			catch (Exception ex) { Console.WriteLine("[Launcher] 이전 설정 마이그레이션 실패: " + ex.Message); }

			darkTheme = LoadTheme();

			Localization.CurrentLanguage = LoadLanguage();

			Text = Localization.T("App.Title") + " " + BuildVersionInfo.DisplayVersion;

			Font = new Font("Pretendard", 11F);

			StartPosition = FormStartPosition.CenterScreen;

			MinimumSize = new Size(940, 800);

			Size = new Size(1120, 840);

			FormBorderStyle = FormBorderStyle.Sizable;

			MaximizeBox = true;

			KeyPreview = true;

			AutoScaleMode = AutoScaleMode.Dpi;

			consoleTimer = new System.Windows.Forms.Timer();

			consoleTimer.Interval = 100;

			consoleTimer.Tick += delegate { FlushConsoleQueue(); };

			consoleTimer.Start();
			InitializeMainAutomation();



			TableLayoutPanel root = new TableLayoutPanel();

			root.Dock = DockStyle.Fill;

			root.Padding = new Padding(36, 28, 36, 30);

			root.ColumnCount = 1;

			root.RowCount = 4;

			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));

			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 400F));

			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));

			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			Controls.Add(root);



			Panel header = new Panel();

			header.Dock = DockStyle.Fill;

			Label title = new Label();

			Localize(title, "App.HeaderTitle");

			title.Font = new Font("Pretendard", 22F, FontStyle.Bold);

			title.AutoSize = true;

			title.Location = new Point(0, 0);

			header.Controls.Add(title);

			Label subtitle = new Label();

			Localize(subtitle, "App.Subtitle");

			subtitle.AutoSize = true;

			subtitle.Location = new Point(2, 44);

			subtitle.Tag = "muted";

			header.Controls.Add(subtitle);

			languageButton = CreateButton(Localization.LanguageButtonText(), 86);

			languageButton.Tag = "ghost";

			languageButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

			languageButton.Location = new Point(header.Width - 184, 4);

			languageButton.Click += delegate

			{

				if (modelessToolWindows.Count > 0)

				{

					ShowNotice(Localization.CurrentLanguage == Localization.Korean ? "열려 있는 창을 모두 닫은 후 언어를 변경해 주세요." : "Please close all windows before changing the language.", true);

					return;

				}

				Localization.ToggleLanguage();

				SaveLanguage();

				ApplyLocalization();

				ShowNoticeKey("Language.Changed", false);

			};

			header.Controls.Add(languageButton);

			launcherUpdateButton = CreateButton(Localization.T("Button.LauncherUpdate"), 168);
			launcherUpdateButton.Tag = "ghost";
			launcherUpdateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			launcherUpdateButton.Top = 4;
			SetButtonIcon(launcherUpdateButton, ButtonIcon.Upgrade);

			launcherUpdateButton.Click += delegate { CheckLauncherUpdateNow(); };

			header.Controls.Add(launcherUpdateButton);

			languageButton.AccessibleDescription = "Tooltip.Language";

			themeButton = CreateButton(string.Empty, 110);

			themeButton.AccessibleDescription = "Tooltip.Theme";

			themeButton.Tag = "ghost";

			themeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

			themeButton.Location = new Point(header.Width - 110, 4);

			themeButton.Click += delegate { darkTheme = !darkTheme; SaveTheme(); ApplyTheme(); ApplyLocalization(); };

			header.Controls.Add(themeButton);

			header.Resize += delegate

			{

				themeButton.Left = header.ClientSize.Width - themeButton.Width;

				languageButton.Left = themeButton.Left - languageButton.Width - 8;

				launcherUpdateButton.Left = languageButton.Left - launcherUpdateButton.Width - 8;

			};

			root.Controls.Add(header, 0, 0);



			RoundedPanel card = new RoundedPanel();

			card.Dock = DockStyle.Fill;

			card.Padding = new Padding(28, 24, 28, 24);

			card.CornerRadius = 28;

			card.Tag = "main-card";

			root.Controls.Add(card, 0, 1);



			statusPill = new RoundedPanel();

			statusPill.Location = new Point(24, 18);

			statusPill.Size = new Size(150, 36);

			statusPill.CornerRadius = 18;

			statusPill.Tag = "surface";

			card.Controls.Add(statusPill);

			statusDot = new Label();

			statusDot.Name = "statusDot";

			statusDot.Text = "●";

			statusDot.Font = new Font("Pretendard", 11F);

			statusDot.AutoSize = true;

			statusDot.Location = new Point(14, 9);

			statusPill.Controls.Add(statusDot);

			statusLabel = new Label();

			statusTextKey = "Status.Off";

			statusLabel.Text = Localization.T(statusTextKey);

			statusLabel.Font = new Font("Pretendard", 11F, FontStyle.Bold);

			statusLabel.AutoSize = true;

			statusLabel.Location = new Point(36, 8);

			statusPill.Controls.Add(statusLabel);



			RoundedPanel addressSurface = new RoundedPanel();

			addressSurface.Location = new Point(24, 66);

			addressSurface.Size = new Size(760, 74);

			addressSurface.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

			addressSurface.CornerRadius = 20;

			addressSurface.Tag = "surface";

			card.Controls.Add(addressSurface);

			Label addressTitle = new Label();

			Localize(addressTitle, "Address.Title");

			addressTitle.Font = new Font("Pretendard", 11F, FontStyle.Bold);

			addressTitle.AutoSize = true;

			addressTitle.Location = new Point(18, 12);

			addressTitle.Tag = "muted";

			addressSurface.Controls.Add(addressTitle);

			addressBox = new TextBox();

			addressBox.ReadOnly = true;

			addressBox.Text = Localization.T("Address.Empty");

			addressBox.BorderStyle = BorderStyle.None;

			addressBox.Font = new Font("Pretendard", 11F, FontStyle.Bold);

			addressBox.Location = new Point(18, 38);

			addressBox.Size = new Size(600, 24);

			addressBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

			addressSurface.Controls.Add(addressBox);

			copyButton = CreateButton(Localization.T("Address.Copy"), 104);

			SetButtonIcon(copyButton, ButtonIcon.Copy);

			Localize(copyButton, "Address.Copy");

			copyButton.Tag = "secondary";

			copyButton.Enabled = false;

			copyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

			copyButton.Location = new Point(addressSurface.Width - 118, 17);

			copyButton.Click += delegate

			{

				try

				{

					if (copyButton.Enabled)

					{

						Clipboard.SetText(addressBox.Text);

						string originalText = copyButton.Text;

						copyButton.Text = "✓ " + Localization.T("Address.Copied");

						System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();

						t.Interval = 2000;

						t.Tick += delegate

						{

							t.Stop();

							t.Dispose();

							copyButton.Text = originalText;

						};

						t.Start();

					}

				}

				catch (Exception exception)

				{

					ShowNotice((Localization.CurrentLanguage == Localization.Korean ? "주소를 복사하지 못했습니다: " : "Could not copy the address: ") + exception.Message, true);

				}

			};

			addressSurface.Controls.Add(copyButton);



			Label controlSectionLabel = new Label();

			Localize(controlSectionLabel, "Main.ControlSection");

			controlSectionLabel.Font = new Font("Pretendard", 11F, FontStyle.Bold);

			controlSectionLabel.AutoSize = true;

			controlSectionLabel.Location = new Point(24, 148);

			controlSectionLabel.Tag = "muted";

			card.Controls.Add(controlSectionLabel);



			TableLayoutPanel primaryActions = new TableLayoutPanel();

			primaryActions.Location = new Point(18, 164);

			primaryActions.Size = new Size(card.ClientSize.Width - 36, 54);

			primaryActions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

			primaryActions.ColumnCount = 5;

			primaryActions.RowCount = 1;

			for (int column = 0; column < 5; column++) primaryActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));

			primaryActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			card.Controls.Add(primaryActions);



			startButton = CreateButton(Localization.T("Button.Start"), 152);

			SetButtonIcon(startButton, ButtonIcon.Play);

			Localize(startButton, "Button.Start");

			startButton.Tag = "primary";

			startButton.Dock = DockStyle.Fill;

			startButton.Margin = new Padding(6, 5, 6, 5);

			startButton.Click += delegate { StartWorkflowWithAutomationBackup(); };

			primaryActions.Controls.Add(startButton, 0, 0);

			stopButton = CreateButton(Localization.T("Button.Stop"), 136);

			SetButtonIcon(stopButton, ButtonIcon.Stop);

			Localize(stopButton, "Button.Stop");

			stopButton.Tag = "danger";

			stopButton.Dock = DockStyle.Fill;

			stopButton.Margin = new Padding(6, 5, 6, 5);

			stopButton.Enabled = false;

			stopButton.Click += delegate { StopServer(); };

			primaryActions.Controls.Add(stopButton, 1, 0);

			settingsButton = CreateButton(Localization.T("Button.Settings"), 92);

			SetButtonIcon(settingsButton, ButtonIcon.Settings);

			Localize(settingsButton, "Button.Settings");

			settingsButton.Tag = "secondary";

			settingsButton.Dock = DockStyle.Fill;

			settingsButton.Margin = new Padding(6, 5, 6, 5);

			settingsButton.Click += delegate { OpenSettings(); };

			primaryActions.Controls.Add(settingsButton, 2, 0);

			upgradeButton = CreateButton(Localization.T("Button.Upgrade"), 118);

			SetButtonIcon(upgradeButton, ButtonIcon.Upgrade);

			Localize(upgradeButton, "Button.Upgrade");

			upgradeButton.Tag = "secondary";

			upgradeButton.Dock = DockStyle.Fill;

			upgradeButton.Margin = new Padding(6, 5, 6, 5);

			upgradeButton.Click += delegate { UpgradeServerFiles(); };

			primaryActions.Controls.Add(upgradeButton, 3, 0);

			consoleButton = CreateButton(Localization.T("Button.ConsoleOpen"), 126);

			SetButtonIcon(consoleButton, ButtonIcon.Console);

			consoleButton.Tag = "secondary";

			consoleButton.Dock = DockStyle.Fill;

			consoleButton.Margin = new Padding(6, 5, 6, 5);

			consoleButton.Click += delegate { ToggleConsole(); };

			primaryActions.Controls.Add(consoleButton, 4, 0);



			Label toolSectionLabel = new Label();

			Localize(toolSectionLabel, "Main.ToolSection");

			toolSectionLabel.Font = new Font("Pretendard", 11F, FontStyle.Bold);

			toolSectionLabel.AutoSize = true;

			toolSectionLabel.Location = new Point(24, 222);

			toolSectionLabel.Tag = "muted";

			card.Controls.Add(toolSectionLabel);



			TableLayoutPanel toolActions = new TableLayoutPanel();

			toolActions.Location = new Point(18, 238);

			toolActions.Size = new Size(card.ClientSize.Width - 36, 108);

			toolActions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

			toolActions.ColumnCount = 4;

			toolActions.RowCount = 2;

			for (int column = 0; column < 4; column++) toolActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

			toolActions.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
			toolActions.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

			card.Controls.Add(toolActions);



			profilesButton = CreateButton(Localization.T("Button.ServerManagement"), 134);

			SetButtonIcon(profilesButton, ButtonIcon.Server);

			Localize(profilesButton, "Button.ServerManagement");

			profilesButton.Tag = "secondary";

			profilesButton.Dock = DockStyle.Fill;

			profilesButton.Margin = new Padding(6, 5, 6, 5);

			profilesButton.Click += delegate { RunUiAction(OpenServerManagement); };

			toolActions.Controls.Add(profilesButton, 0, 0);

			multiServerButton = CreateButton(Localization.T("Button.MultiServer"), 112);

			multiServerButton.Tag = "secondary";

			multiServerButton.Visible = false;

			backupButton = CreateButton(Localization.T("Button.Backup"), 110);

			SetButtonIcon(backupButton, ButtonIcon.Backup);

			Localize(backupButton, "Button.Backup");

			backupButton.Tag = "secondary";

			backupButton.Dock = DockStyle.Fill;

			backupButton.Margin = new Padding(6, 5, 6, 5);

			backupButton.Click += delegate { RunUiAction(OpenBackupManager); };

			toolActions.Controls.Add(backupButton, 1, 0);

			contentButton = CreateButton(Localization.T("Button.Content"), 100);

			SetButtonIcon(contentButton, ButtonIcon.Content);

			Localize(contentButton, "Button.Content");

			contentButton.Tag = "secondary";

			contentButton.Dock = DockStyle.Fill;

			contentButton.Margin = new Padding(6, 5, 6, 5);

			contentButton.Click += delegate { RunUiAction(OpenContentManager); };

			toolActions.Controls.Add(contentButton, 2, 0);

			playersButton = CreateButton(Localization.T("Button.Players"), 100);

			SetButtonIcon(playersButton, ButtonIcon.Players);

			Localize(playersButton, "Button.Players");

			playersButton.Tag = "secondary";

			playersButton.Dock = DockStyle.Fill;

			playersButton.Margin = new Padding(6, 5, 6, 5);

			playersButton.Click += delegate { RunUiAction(OpenPlayerManager); };

			toolActions.Controls.Add(playersButton, 3, 0);

			networkButton = CreateButton(Localization.T("Button.Network"), 110);

			SetButtonIcon(networkButton, ButtonIcon.Network);

			Localize(networkButton, "Button.Network");

			networkButton.Tag = "secondary";

			networkButton.Dock = DockStyle.Fill;

			networkButton.Margin = new Padding(6, 5, 6, 5);

			networkButton.Click += delegate { RunUiAction(OpenNetworkTools); };

			toolActions.Controls.Add(networkButton, 0, 1);

			diagnosticsButton = CreateButton(Localization.T("Button.Diagnostics"), 110);

			SetButtonIcon(diagnosticsButton, ButtonIcon.Diagnostics);

			Localize(diagnosticsButton, "Button.Diagnostics");

			diagnosticsButton.Tag = "secondary";

			diagnosticsButton.Dock = DockStyle.Fill;

			diagnosticsButton.Margin = new Padding(6, 5, 6, 5);

			diagnosticsButton.Click += delegate { CreateDiagnostics(); };

			toolActions.Controls.Add(diagnosticsButton, 1, 1);

			mainScheduleButton = CreateButton(Localization.T("Button.Schedules"), 110);
			SetButtonIcon(mainScheduleButton, ButtonIcon.Backup);
			Localize(mainScheduleButton, "Button.Schedules");
			mainScheduleButton.Tag = "secondary";
			mainScheduleButton.Dock = DockStyle.Fill;
			mainScheduleButton.Margin = new Padding(6, 5, 6, 5);
			mainScheduleButton.Click += delegate { RunUiAction(OpenMainAutomationManager); };
			toolActions.Controls.Add(mainScheduleButton, 2, 1);

			mainDashboardButton = CreateButton(Localization.T("Button.Dashboard"), 110);
			SetButtonIcon(mainDashboardButton, ButtonIcon.Diagnostics);
			Localize(mainDashboardButton, "Button.Dashboard");
			mainDashboardButton.Tag = "secondary";
			mainDashboardButton.Dock = DockStyle.Fill;
			mainDashboardButton.Margin = new Padding(6, 5, 6, 5);
			mainDashboardButton.Click += delegate { RunUiAction(OpenMainStatusDashboard); };
			toolActions.Controls.Add(mainDashboardButton, 3, 1);

			Label featureList = new Label();

			Localize(featureList, "Features");

			featureList.Font = new Font("Pretendard", 11F);

			featureList.AutoSize = false;

			featureList.Location = new Point(26, 348);

			featureList.Size = new Size(740, 22);

			featureList.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

			featureList.Visible = false;

			card.Resize += delegate

			{

				addressSurface.Width = card.ClientSize.Width - 48;

				primaryActions.Width = card.ClientSize.Width - 36;

				toolActions.Width = card.ClientSize.Width - 36;

				copyButton.Left = addressSurface.ClientSize.Width - copyButton.Width - 14;

				addressBox.Width = copyButton.Left - addressBox.Left - 10;

				featureList.Width = card.ClientSize.Width - 52;

			};



			Panel noticeArea = new Panel();

			noticeArea.Dock = DockStyle.Fill;

			root.Controls.Add(noticeArea, 0, 2);

			noticeLabel = new Label();

			noticeLabel.Dock = DockStyle.Top;

			noticeLabel.Height = 30;

			noticeLabel.TextAlign = ContentAlignment.MiddleLeft;

			noticeLabel.AutoEllipsis = true;

			noticeTextKey = "Notice.Ready";

			noticeLabel.Text = Localization.T(noticeTextKey);

			noticeLabel.Font = new Font("Pretendard", 11F);

			noticeArea.Controls.Add(noticeLabel);

			loadingPanel = new Panel();

			loadingPanel.Dock = DockStyle.Bottom;

			loadingPanel.Height = 28;

			loadingPanel.Visible = true;

			noticeArea.Controls.Add(loadingPanel);

			loadingDetailLabel = new Label();

			loadingDetailLabel.Dock = DockStyle.Fill;

			loadingDetailLabel.TextAlign = ContentAlignment.MiddleLeft;

			loadingDetailLabel.Text = Localization.CurrentLanguage == Localization.Korean ? "런처 화면을 준비하고 있습니다…" : "Preparing the launcher…";

			loadingPanel.Controls.Add(loadingDetailLabel);

			loadingProgress = new RoundedProgressBar();

			loadingProgress.Dock = DockStyle.Right;

			loadingProgress.Width = 220;

			loadingProgress.IsIndeterminate = true;

			loadingPanel.Controls.Add(loadingProgress);



			Panel workspacePanel = new Panel();

			workspacePanel.Dock = DockStyle.Fill;

			workspacePanel.Padding = new Padding(0, 8, 0, 0);

			root.Controls.Add(workspacePanel, 0, 3);



			consolePanel = new Panel();

			consolePanel.Dock = DockStyle.Fill;

			consolePanel.Padding = new Padding(0, 0, 12, 0);

			consolePanel.Visible = false;

			workspacePanel.Controls.Add(consolePanel);

			InitializeQuickCommandPanel(workspacePanel);

			consoleBox = new RichTextBox();

			consoleBox.Dock = DockStyle.Fill;

			consoleBox.ReadOnly = true;

			consoleBox.BorderStyle = BorderStyle.None;

			consoleBox.Font = new Font("Consolas", 9F);

			consoleBox.WordWrap = false;

			consoleBox.DetectUrls = true;

			consolePanel.Controls.Add(consoleBox);

			Panel consoleToolbar = new Panel();

			consoleToolbar.Dock = DockStyle.Top;

			consoleToolbar.Height = 40;

			consoleToolbar.Padding = new Padding(0, 0, 0, 8);

			consolePanel.Controls.Add(consoleToolbar);

			consoleSearchBox = new TextBox();

			consoleSearchBox.Width = 240;

			consoleSearchBox.Dock = DockStyle.Left;

			consoleSearchBox.TextChanged += delegate { RebuildConsoleView(); };

			consoleToolbar.Controls.Add(consoleSearchBox);

			consoleFilterBox = new ModernComboBox();

			consoleFilterBox.DropDownStyle = ComboBoxStyle.DropDownList;

			consoleFilterBox.Width = 136;

			consoleFilterBox.Dock = DockStyle.Left;

			consoleFilterBox.Items.AddRange(new object[] { Localization.T("Console.All"), Localization.T("Console.Warning"), Localization.T("Console.Compatibility"), Localization.T("Console.Error") });

			consoleFilterBox.SelectedIndex = 0;

			consoleFilterBox.SelectedIndexChanged += delegate { RebuildConsoleView(); };

			consoleToolbar.Controls.Add(consoleFilterBox);

			consoleWrapBox = new ModernCheckBox();

			Localize(consoleWrapBox, "Console.Wrap");

			consoleWrapBox.Width = 100;

			consoleWrapBox.Dock = DockStyle.Left;

			consoleWrapBox.CheckedChanged += delegate { consoleBox.WordWrap = consoleWrapBox.Checked; };

			consoleToolbar.Controls.Add(consoleWrapBox);

			Panel commandPanel = new Panel();

			commandPanel.Dock = DockStyle.Bottom;

			commandPanel.Height = 40;

			commandPanel.Padding = new Padding(0, 8, 0, 0);

			consolePanel.Controls.Add(commandPanel);

			commandBox = new TextBox();

			commandBox.Dock = DockStyle.Fill;

			commandBox.Enabled = false;

			commandBox.TextChanged += delegate { sendButton.Enabled = serverRunning && !string.IsNullOrWhiteSpace(commandBox.Text); };

			commandBox.KeyDown += delegate(object sender, KeyEventArgs eventArgs)

			{

				if (eventArgs.KeyCode == Keys.Enter)

				{

					SendCommandFromBox();

					eventArgs.SuppressKeyPress = true;

				}

				else if (eventArgs.KeyCode == Keys.Up)

				{

					NavigateConsoleCommandHistory(-1);

					eventArgs.SuppressKeyPress = true;

				}

				else if (eventArgs.KeyCode == Keys.Down)

				{

					NavigateConsoleCommandHistory(1);

					eventArgs.SuppressKeyPress = true;

				}

			};

			commandPanel.Controls.Add(commandBox);

			sendButton = CreateButton(Localization.T("Button.Send"), 86);

			SetButtonIcon(sendButton, ButtonIcon.Send);

			Localize(sendButton, "Button.Send");

			sendButton.Tag = "primary";

			sendButton.Dock = DockStyle.Right;

			sendButton.Enabled = false;

			sendButton.Click += delegate { SendCommandFromBox(); };

			commandPanel.Controls.Add(sendButton);



			FormClosing += OnLauncherClosing;

			FormClosed += delegate { consoleTimer.Stop(); DisposeMainAutomation(); };

			Shown += delegate { BeginInvoke((MethodInvoker)BeginStartupInitialization); };

			startButton.Enabled = false;

			playersButton.Enabled = false;

			ConfigureAccessibleField(addressBox, Localization.T("Address.Title"), Localization.CurrentLanguage == Localization.Korean ? "친구가 서버에 접속할 때 사용하는 주소입니다." : "The address friends use to join the server.");

			ConfigureAccessibleField(consoleSearchBox, Localization.T("Console.Search"), Localization.CurrentLanguage == Localization.Korean ? "표시된 콘솔 로그를 검색합니다." : "Search the visible console log.");

			ConfigureAccessibleField(consoleFilterBox, Localization.T("Console.All"), Localization.CurrentLanguage == Localization.Korean ? "일반 경고, 호환성 안내와 오류를 구분해 표시합니다." : "Separate actionable warnings, compatibility notices, and errors.");

			ConfigureAccessibleField(commandBox, Localization.CurrentLanguage == Localization.Korean ? "서버 명령" : "Server command", Localization.CurrentLanguage == Localization.Korean ? "실행 중인 서버에 보낼 명령을 입력합니다." : "Enter a command to send to the running server.");

			ApplyTheme();

			ApplyLocalization();

		}



		protected override bool ProcessCmdKey(ref Message message, Keys keyData)

		{

			if (keyData == Keys.F5 && startButton.Enabled)

			{

				StartWorkflowWithAutomationBackup();

				return true;

			}

			if (keyData == (Keys.Shift | Keys.F5) && stopButton.Enabled)

			{

				StopServer();

				return true;

			}

			if (keyData == (Keys.Control | Keys.Oemcomma) && settingsButton.Enabled)

			{

				OpenSettings();

				return true;

			}

			if (keyData == (Keys.Control | Keys.K))

			{

				if (!consolePanel.Visible)

				{

					ToggleConsole();

				}

				consoleSearchBox.Focus();

				consoleSearchBox.SelectAll();

				return true;

			}

			if (keyData == Keys.Escape && consolePanel.Visible)

			{

				ToggleConsole();

				return true;

			}

			return base.ProcessCmdKey(ref message, keyData);

		}



		private static Button CreateButton(string text, int width)

		{

			Button button = new RoundedButton();

			button.Text = text;

			button.Width = width;

			button.Height = 44;

			button.FlatStyle = FlatStyle.Flat;

			button.FlatAppearance.BorderSize = 0;

			button.Cursor = Cursors.Hand;

			return button;

		}



		private static void SetButtonIcon(Button button, ButtonIcon icon)

		{

			RoundedButton roundedButton = button as RoundedButton;

			if (roundedButton != null)

			{

				roundedButton.IconKind = icon;

			}

		}



		private void Localize(Control control, string key)

		{

			localizedControls[control] = key;

			control.Text = Localization.T(key);



			if (key.StartsWith("Button."))

			{

				string tooltipKey = "Tooltip." + key.Substring(7);

				if (tooltipKey == "Tooltip.ServerManagement" || tooltipKey == "Tooltip.MultiServer") tooltipKey = "Tooltip.Servers";

				if (tooltipKey == "Tooltip.ConsoleOpen" || tooltipKey == "Tooltip.ConsoleClose") tooltipKey = "Tooltip.Console";

				control.AccessibleDescription = tooltipKey;

			}

		}



		private string LoadLanguage()

		{

			try

			{

				if (File.Exists(languagePath))

				{

					string text = File.ReadAllText(languagePath, Encoding.UTF8).Trim();

					return Localization.NormalizeLanguage(text);

				}

			}

			catch

			{

			}

			return Localization.DetectDefaultLanguage();

		}



		private void SaveLanguage()

		{

			try

			{

				Directory.CreateDirectory(Path.GetDirectoryName(languagePath));

				File.WriteAllText(languagePath, Localization.CurrentLanguage, new UTF8Encoding(false));

			}

			catch

			{

			}

		}



		private void ApplyLocalization()

		{

			Text = Localization.T("App.Title") + " " + BuildVersionInfo.DisplayVersion;

			foreach (KeyValuePair<Control, string> entry in localizedControls)

			{

				if (!entry.Key.IsDisposed)

				{

					entry.Key.Text = Localization.T(entry.Value);

				}

			}

			if (themeButton != null)

			{

				themeButton.Text = darkTheme ? Localization.T("Theme.Light") : Localization.T("Theme.Dark");

			}

			if (languageButton != null)

			{

				languageButton.Text = Localization.LanguageButtonText();

			}

			if (launcherUpdateButton != null)

			{

				launcherUpdateButton.Text = Localization.T("Button.LauncherUpdate");

			}

			if (consoleButton != null)

			{

				consoleButton.Text = consolePanel.Visible ? Localization.T("Button.ConsoleClose") : Localization.T("Button.ConsoleOpen");

			}

			if (statusTextKey != null)

			{

				SetStatusText(Localization.T(statusTextKey), statusWarning);

			}

			if (noticeTextKey != null)

			{

				SetNoticeText(Localization.T(noticeTextKey), noticeWarning);

			}

			if (addressBox != null && addressBox.Text.IndexOf(":") < 0)

			{

				addressBox.Text = Localization.T("Address.Empty");

				copyButton.Enabled = false;

			}

			if (consoleFilterBox != null)

			{

				int selectedFilter = Math.Max(0, consoleFilterBox.SelectedIndex);

				consoleFilterBox.Items.Clear();

				consoleFilterBox.Items.AddRange(new object[] { Localization.T("Console.All"), Localization.T("Console.Warning"), Localization.T("Console.Compatibility"), Localization.T("Console.Error") });

				consoleFilterBox.SelectedIndex = Math.Min(selectedFilter, consoleFilterBox.Items.Count - 1);

			}

			ConfigureAccessibleField(addressBox, Localization.T("Address.Title"), Localization.CurrentLanguage == Localization.Korean ? "친구가 서버에 접속할 때 사용하는 주소입니다." : "The address friends use to join the server.");

			ConfigureAccessibleField(consoleSearchBox, Localization.T("Console.Search"), Localization.CurrentLanguage == Localization.Korean ? "표시된 콘솔 로그를 검색합니다." : "Search the visible console log.");

			ConfigureAccessibleField(consoleFilterBox, Localization.T("Console.All"), Localization.CurrentLanguage == Localization.Korean ? "일반 경고, 호환성 안내와 오류를 구분해 표시합니다." : "Separate actionable warnings, compatibility notices, and errors.");

			ApplyQuickCommandLocalization();

			ApplyCommonButtonToolTips(this);

		}



		private void InitialSetupIfNeeded()

		{

			string serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);

			string activeProfile = ReadActiveProfileName(serversRoot);

			string serverDirectory = GetProfileDirectory(serversRoot, activeProfile);

			string marker = Path.Combine(serverDirectory, ".launcher-properties-configured");

			if (File.Exists(marker))

			{

				int port = ReadConfiguredServerPort(Path.Combine(serverDirectory, "server.properties"), 25565);

				SetConnectionAddress(GetLocalConnectionAddress(port));

				return;

			}

			try

			{

				Directory.CreateDirectory(serversRoot);

				LauncherOptions options = ConfigureServerPropertiesGui(serversRoot, true);

				int port = ReadConfiguredServerPort(Path.Combine(options.ServerDirectory, "server.properties"), 25565);

				SetConnectionAddress(GetLocalConnectionAddress(port));

				ShowNoticeKey("Notice.InitialSaved", false);

			}

			catch (OperationCanceledException)

			{

				ShowNoticeKey("Notice.InitialCanceled", true);

			}

			catch (Exception exception)

			{

				ShowNotice(Localization.F("Notice.SettingsFailed", exception.Message), true);

			}

		}



		private void BeginStartupInitialization()

		{

			if (startupInitializing)

			{

				return;

			}

			if (!EnsureDataRootSelected(this, AppDomain.CurrentDomain.BaseDirectory))

			{

				SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "데이터 위치 선택을 취소해 런처를 종료합니다." : "Data location selection was canceled. Closing the launcher.", false, 0);

				BeginInvoke((MethodInvoker)Close);

				return;

			}

			string updateResult = ConsumeLauncherUpdateResult();

			if (!string.IsNullOrEmpty(updateResult)) ShowNotice(updateResult, true);

			startupInitializing = true;

			SetStartupControlsEnabled(false);

			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "런처 업데이트를 확인하고 있습니다…" : "Checking launcher updates…", true, -1);

			Task.Run(delegate

			{

				bool updateStarted = false;

				try

				{

					try

					{

						updateStarted = StartApprovedLauncherUpdateIfAvailable();

						if (!updateStarted)

						{

							launcherUpdateCheckCompleted = true;

						}

					}

					catch (Exception exception)

					{

						launcherUpdateCheckCompleted = true;

						LauncherUpdateStageException stageException = exception as LauncherUpdateStageException;

						string stage = stageException == null ? "unknown" : stageException.Stage;

						string prefix;

						if (Localization.CurrentLanguage == Localization.Korean)

						{

							prefix = stage == "connection" ? "업데이트 서버 연결 실패: " : stage == "metadata" ? "업데이트 정보 오류: " : stage == "download" ? "업데이트 다운로드 실패: " : stage == "hash" ? "업데이트 해시 불일치: " : "업데이트 실패: ";

						}

						else

						{

							prefix = stage == "connection" ? "Update server unavailable: " : stage == "metadata" ? "Invalid update metadata: " : stage == "download" ? "Update download failed: " : stage == "hash" ? "Update hash mismatch: " : "Update failed: ";

						}

						ShowNotice(prefix + exception.Message + (Localization.CurrentLanguage == Localization.Korean ? " · 서버는 계속 실행할 수 있습니다." : " · You can continue using the server."), true);

					}

					if (updateStarted)

					{

						RequestLauncherClose();

						return;

					}

					PurgeExpiredServerTrash(GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory), DateTime.UtcNow);

					SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "서버 프로필과 설정을 확인하고 있습니다…" : "Checking server profiles and settings…", true, 70);

					if (IsDisposed || Disposing || !IsHandleCreated)

					{

						return;

					}

					try

					{

						Invoke((MethodInvoker)InitialSetupIfNeeded);

					}

					catch (ObjectDisposedException)

					{

					}

					catch (InvalidOperationException)

					{

						// 시작 준비 중 창이 닫히면 최초 설정 표시를 생략합니다.

					}

				}

				finally

				{

					if (!updateStarted && !IsDisposed)

					{

						TryPostToUi(this, (MethodInvoker)delegate

						{

							startupInitializing = false;

							SetStartupControlsEnabled(true);

							SetLoadingState(string.Empty, false, -1);

						});

					}

				}

			});

		}



		private void SetStartupControlsEnabled(bool enabled)

		{

			startButton.Enabled = enabled;

			settingsButton.Enabled = enabled;

			upgradeButton.Enabled = enabled;

			profilesButton.Enabled = enabled;

			multiServerButton.Enabled = enabled;

			backupButton.Enabled = enabled;

			contentButton.Enabled = enabled;

			networkButton.Enabled = enabled;

			diagnosticsButton.Enabled = enabled;
			if (mainScheduleButton != null) mainScheduleButton.Enabled = enabled;
			if (mainDashboardButton != null) mainDashboardButton.Enabled = enabled;

			playersButton.Enabled = enabled && serverRunning;

			launcherUpdateButton.Enabled = enabled && !launcherUpdateChecking;

			UpdateQuickCommandControls();

		}



		private void CheckLauncherUpdateNow()

		{

			if (launcherUpdateChecking || startupInitializing)

			{

				return;

			}

			launcherUpdateChecking = true;

			launcherUpdateButton.Enabled = false;

			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "런처 최신 버전을 새로 확인하고 있습니다…" : "Checking for the latest launcher again…", true, -1);

			Task.Run(delegate

			{

				bool updateStarted = false;

				bool updateAvailable = false;

				try

				{

					updateStarted = StartApprovedLauncherUpdateIfAvailable(true, out updateAvailable);

					launcherUpdateCheckCompleted = true;

					if (updateStarted)

					{

						RequestLauncherClose();

						return;

					}

					ShowNoticeKey(updateAvailable ? "Notice.LauncherDeferred" : "Notice.LauncherLatest", false);

				}

				catch (Exception exception)

				{

					launcherUpdateCheckCompleted = true;

					LauncherUpdateStageException stageException = exception as LauncherUpdateStageException;

					string stage = stageException == null ? "unknown" : stageException.Stage;

					string prefix = Localization.CurrentLanguage == Localization.Korean

						? (stage == "connection" ? "업데이트 서버 연결 실패: " : stage == "metadata" ? "업데이트 정보 오류: " : stage == "download" ? "업데이트 다운로드 실패: " : stage == "hash" ? "업데이트 해시 불일치: " : "업데이트 실패: ")

						: (stage == "connection" ? "Update server unavailable: " : stage == "metadata" ? "Invalid update metadata: " : stage == "download" ? "Update download failed: " : stage == "hash" ? "Update hash mismatch: " : "Update failed: ");

					ShowNotice(prefix + exception.Message, true);

				}

				finally

				{

					if (!updateStarted && !IsDisposed)

					{

						TryPostToUi(this, (MethodInvoker)delegate

						{

							launcherUpdateChecking = false;

							launcherUpdateButton.Enabled = true;

							SetLoadingState(string.Empty, false, -1);

						});

					}

				}

			});

		}



		public void SetLoadingState(string message, bool active, int percent)

		{

			if (IsDisposed)

			{

				return;

			}

			if (InvokeRequired)

			{

				TryPostToUi(this, (MethodInvoker)delegate { SetLoadingState(message, active, percent); });

				return;

			}

			loadingPanel.Visible = active;

			loadingDetailLabel.Text = message ?? string.Empty;

			loadingDetailLabel.AccessibleName = loadingDetailLabel.Text;

			loadingProgress.AccessibleName = loadingDetailLabel.Text;

			UseWaitCursor = active;

			if (!active)

			{

				return;

			}

			if (percent >= 0 && percent <= 100)

			{

				loadingProgress.IsIndeterminate = false;

				loadingProgress.Value = percent;

			}

			else

			{

				loadingProgress.IsIndeterminate = true;

			}

		}



		private void StartWorkflow()

		{

			if (workflowRunning)

			{

				return;

			}

			startButton.Enabled = false;

			if (!EnsureNoBlockingToolWindow())

			{

				startButton.Enabled = true;

				return;

			}

			

			string serversRoot, serverDirectory;

			try

			{

				ReadActiveLauncherOptions(out serversRoot, out serverDirectory);

				if (!string.IsNullOrEmpty(serverDirectory))

				{

					DeleteBridgeSessionFile(serverDirectory);

				}

			}

			catch

			{

				// Ignore errors here if options are invalid; let the normal validation handle it.

			}



			workflowRunning = true;

			SetStatusKey("Status.Preparing", false);

			settingsButton.Enabled = false;

			upgradeButton.Enabled = false;

			ShowNoticeKey("Notice.CheckingFiles", false);

			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "서버 실행 준비를 시작합니다…" : "Preparing to start the server…", true, -1);

			Task.Run(delegate

			{

				int result = 1;

				bool canceled = false;

				try

				{

					result = Run();

				}

				catch (OperationCanceledException)

				{

					AppendConsole(Localization.T("Console.SetupCanceled"));

					result = 0;

					canceled = true;

				}

				catch (Exception exception)

				{

					AppendConsole(Localization.F("Console.LauncherError", exception.Message));

					ShowNoticeKey("Notice.StartFailed", true);

				}

				finally

				{

					WorkflowFinished(result, canceled);

				}

			});

		}



		private void WorkflowFinished(int exitCode, bool canceled)

		{

			if (IsDisposed)

			{

				return;

			}

			if (InvokeRequired)

			{

				TryPostToUi(this, (MethodInvoker)delegate { WorkflowFinished(exitCode, canceled); });

				return;

			}

			workflowRunning = false;

			serverRunning = false;

			SetStatusKey(exitCode == 0 ? "Status.Off" : "Status.Error", exitCode != 0);

			startButton.Enabled = true;

			stopButton.Enabled = false;

			settingsButton.Enabled = true;

			upgradeButton.Enabled = true;

			playersButton.Enabled = false;

			commandBox.Enabled = false;

			sendButton.Enabled = false;

			UpdateQuickCommandControls();

			SetLoadingState(string.Empty, false, -1);

			string upnpCleanup = ConsumeUpnpCleanupStatus();

			if (canceled)

			{

				ShowNoticeKey("Notice.StartCanceled", false);

			}

			else if (exitCode == 0)

			{

				if (string.IsNullOrEmpty(upnpCleanup))

				{

					ShowNoticeKey("Notice.ServerStopped", false);

				}

				else

				{

					ShowNotice(Localization.T("Notice.ServerStopped") + " " + upnpCleanup, upnpCleanup.IndexOf("실패", StringComparison.Ordinal) >= 0);

				}

			}

			else

			{

				ServerFailureAction failureAction;

				string analysis = AnalyzeServerFailure(consoleHistory, out failureAction);

				ShowNotice(Localization.T("Notice.ServerError") + " " + analysis + (string.IsNullOrEmpty(upnpCleanup) ? string.Empty : " " + upnpCleanup), true);

				

				if (failureAction != ServerFailureAction.None && !closeAfterStop)

				{

					bool korean = Localization.CurrentLanguage == Localization.Korean;

					string prompt = analysis + "\r\n\r\n" + (korean ? "문제를 해결할 수 있는 화면을 엽니다." : "Open the related manager to fix this issue?");

					DialogResult actionResult = ShowMineHarborDialog(this, prompt, Localization.T("Notice.ServerError"), MessageBoxButtons.YesNo, MessageBoxIcon.Error);

					if (actionResult == DialogResult.Yes)

					{

						TryPostToUi(this, (MethodInvoker)delegate

						{

							if (failureAction == ServerFailureAction.OpenSettings) OpenSettings();

							else if (failureAction == ServerFailureAction.OpenContentManager) OpenContentManager();

						});

					}

				}

			}

			HandleMainWorkflowFinishedAutomation(exitCode, canceled);

			if (closeAfterStop)

			{

				FormClosing -= OnLauncherClosing;

				Close();

			}

		}



		private int childPid = -1;

		private long childStartTime = -1;



		private void StopServer()

		{

			if (Interlocked.CompareExchange(ref currentServerStopRequested, 1, 0) == 0)

			{

				if (SendServerCommand("stop"))

				{

					// 첫 번째 클릭: stop 명령 전송 후 버튼을 '강제 종료'로 전환 (비활성화 안 함)

					stopButton.Text = Localization.CurrentLanguage == Localization.Korean ? "강제 종료" : "Force Stop";

					SetStatusKey("Status.Stopping", false);

					ShowNoticeKey("Notice.Stopping", false);

				}

				else

				{

					Interlocked.Exchange(ref currentServerStopRequested, 0); // 실패 시 초기화

				}

			}

			else

			{

				// 두 번째 클릭: 강제 종료 프로세스 (비동기)

				if (ShowMineHarborDialog(this, Localization.CurrentLanguage == Localization.Korean ? 

					"서버를 강제 종료하면 저장되지 않은 월드 데이터가 손상될 수 있습니다.\n\n정말로 강제 종료하시겠습니까?" : 

					"Force stopping the server may corrupt unsaved world data.\n\nAre you sure you want to force stop?", 

					Localization.CurrentLanguage == Localization.Korean ? "경고" : "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)

				{

					stopButton.Enabled = false;

					commandBox.Enabled = false;

					sendButton.Enabled = false;

					SetStatusKey("Status.Stopping", false);

					ShowNoticeKey("Notice.Stopping", false);



					Task.Run(async () =>

					{

						Process processToKill = null;

						lock (ServerProcessLock)

						{

							if (currentServerProcess != null && !currentServerProcess.HasExited)

							{

								processToKill = currentServerProcess;

							}

						}



						if (processToKill != null)

						{

							try

							{

								processToKill.Kill();

								var tcs = new TaskCompletionSource<bool>();

								processToKill.EnableRaisingEvents = true;

								processToKill.Exited += (s, e) => tcs.TrySetResult(true);

								if (!processToKill.HasExited)

								{

									await Task.WhenAny(tcs.Task, Task.Delay(5000));

								}

							}

							catch (Exception ex) { Console.WriteLine("[Launcher] 메인 프로세스 강제 종료 실패: " + ex.Message); }

						}

						

						// 내부 Java/Cmd 종료 확인 (PID 기록 기반 교차 검증)

						if (childPid != -1)

						{

							try

							{

								Process child = Process.GetProcessById(childPid);

								if (child.StartTime.Ticks == childStartTime && !child.HasExited)

								{

									var childTcs = new TaskCompletionSource<bool>();

									child.EnableRaisingEvents = true;

									child.Exited += (s, e) => childTcs.TrySetResult(true);

									if (!child.HasExited)

									{

										await Task.WhenAny(childTcs.Task, Task.Delay(3000));

									}

								}

							}

							catch (Exception ex) { Console.WriteLine("[Launcher] 자식 프로세스 확인 실패: " + ex.Message); }

						}



						// Job Object에 의해 자식 프로세스들이 완전히 정리될 시간 보장

						await Task.Delay(1000); 



						TryPostToUi(this, (MethodInvoker)delegate

						{

							if (serverRunning)

							{

								stopButton.Enabled = true;

								commandBox.Enabled = true;

								sendButton.Enabled = !string.IsNullOrWhiteSpace(commandBox.Text);

							}

						});

					});

				}

			}

		}



		public void ServerStarted(string address)

		{

			if (InvokeRequired)

			{

				TryPostToUi(this, (MethodInvoker)delegate { ServerStarted(address); });

				return;

			}

			serverRunning = true;

			SetStatusKey("Status.Starting", false);

			SetConnectionAddress(address);

			stopButton.Enabled = true;

			playersButton.Enabled = true;

			// UX-2: 서버 실행 중 설정/업그레이드 변경 방지

			settingsButton.Enabled = false;

			upgradeButton.Enabled = false;

			commandBox.Enabled = true;

			sendButton.Enabled = !string.IsNullOrWhiteSpace(commandBox.Text);

			UpdateQuickCommandControls();

			ShowNoticeKey("Notice.WaitingServer", false);

			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "서버 프로세스가 포트를 열 때까지 기다리고 있습니다…" : "Waiting for the server process to open its port…", true, -1);

		}



		public void ServerReady(string address)

		{

			if (InvokeRequired)

			{

				TryPostToUi(this, (MethodInvoker)delegate { ServerReady(address); });

				return;

			}

			SetStatusKey("Status.Ready", false);

			SetConnectionAddress(address);

			ShowNoticeKey("Notice.LocalReady", false);

			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "외부 접속 경로를 확인하고 있습니다…" : "Checking external connectivity…", true, -1);

		}



		private readonly List<string> consoleCommandHistory = new List<string>();

		private int consoleCommandHistoryIndex = -1;



		private void SendCommandFromBox()

		{

			string command;

			if (!PrepareDirectServerCommand(this, commandBox.Text, out command))

			{

				commandBox.Focus();

				return;

			}

			if (string.IsNullOrEmpty(command))

			{

				commandBox.Focus();

				return;

			}

			if (SendServerCommand(command))

			{

				// UX-4: 명령 히스토리에 추가

				if (consoleCommandHistory.Count == 0 || !string.Equals(consoleCommandHistory[consoleCommandHistory.Count - 1], command, StringComparison.Ordinal))

				{

					consoleCommandHistory.Add(command);

				}

				if (consoleCommandHistory.Count > 200) consoleCommandHistory.RemoveAt(0);

				consoleCommandHistoryIndex = -1;

				commandBox.Clear();

			}

			else

			{

				ShowNoticeKey("Notice.NoServer", true);

			}

		}



		private void NavigateConsoleCommandHistory(int direction)

		{

			if (consoleCommandHistory.Count == 0) return;

			int nextIndex;

			string value = GetQuickCommandHistoryValue(consoleCommandHistory, consoleCommandHistoryIndex, direction, out nextIndex);

			if (nextIndex < 0) return;

			consoleCommandHistoryIndex = nextIndex;

			commandBox.Text = value;

			commandBox.SelectionStart = commandBox.TextLength;

		}



		private void OpenSettings()

		{

			if (!EnsureNoBlockingToolWindow()) return;

			if (workflowRunning || serverRunning)

			{

				ShowNoticeKey("Notice.StopBeforeSettings", true);

				return;

			}

			string serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);

			try

			{

				Directory.CreateDirectory(serversRoot);

				LauncherOptions options = ConfigureServerPropertiesGui(serversRoot, true);

				int port = ReadConfiguredServerPort(Path.Combine(options.ServerDirectory, "server.properties"), 25565);

				SetConnectionAddress(GetLocalConnectionAddress(port));

				ShowNoticeKey("Notice.SettingsSaved", false);

			}

			catch (OperationCanceledException)

			{

				ShowNoticeKey("Notice.SettingsCanceled", false);

			}

			catch (Exception exception)

			{

				ShowNotice(Localization.F("Notice.SettingsFailed", exception.Message), true);

			}

		}



		private LauncherOptions ReadActiveLauncherOptions(out string serversRoot, out string serverDirectory)

		{

			serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);

			string activeProfile = ReadActiveProfileName(serversRoot);

			serverDirectory = GetProfileDirectory(serversRoot, activeProfile);

			Dictionary<string, string> properties = ReadSimpleProperties(Path.Combine(serverDirectory, ".launcher-properties-configured"));

			if (properties.Count == 0)

			{

				throw new InvalidOperationException(Localization.CurrentLanguage == Localization.Korean ? "먼저 서버 설정을 저장해 주세요." : "Save the server settings first.");

			}

			return ReadLauncherOptionsFromProperties(serversRoot, properties, activeProfile, 4, true);

		}



		private void RunUiAction(Action action)

		{

			try

			{

				action();

			}

			catch (Exception exception)

			{

				string prefix = Localization.CurrentLanguage == Localization.Korean ? "작업을 완료하지 못했습니다: " : "Could not complete the action: ";

				ShowNotice(prefix + exception.Message, true);

			}

		}



		private static bool IsOwnedWindowClickGuardActive(int nowTick, int untilTick)
		{
			return unchecked(untilTick - nowTick) > 0;
		}

		private static bool IsMouseClickMessage(int message)
		{
			return message >= 0x0201 && message <= 0x0209;
		}

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		private static extern bool IsChild(IntPtr parentHandle, IntPtr childHandle);

		private void ArmOwnedWindowClickGuard()
		{
			ownedWindowClickGuardUntilTick = unchecked(Environment.TickCount + 350);
		}

		public bool PreFilterMessage(ref Message message)
		{
			if (!IsMouseClickMessage(message.Msg) || !IsOwnedWindowClickGuardActive(Environment.TickCount, ownedWindowClickGuardUntilTick) || IsDisposed || !IsHandleCreated) return false;
			if (message.HWnd != Handle && !IsChild(Handle, message.HWnd)) return false;
			return true;
		}

		private void ShowModelessToolWindow(string key, Func<Form> factory, bool blocksServerChanges, Action onClosed)

		{

			Form existing;

			if (modelessToolWindows.TryGetValue(key, out existing) && existing != null && !existing.IsDisposed)

			{

				if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;

				existing.Show();

				existing.BringToFront();

				existing.Activate();

				return;

			}

			if (blocksServerChanges && modelessToolsBlockingServerChanges.Count > 0)

			{

				ShowNotice(Localization.CurrentLanguage == Localization.Korean ? "다른 서버 관리 창을 먼저 닫아 주세요." : "Close the other server management window first.", true);

				ActivateFirstBlockingToolWindow();

				return;

			}

			Form form = factory();

			if (form == null) throw new InvalidOperationException("기능 창을 만들지 못했습니다.");

			modelessToolWindows[key] = form;

			if (blocksServerChanges) modelessToolsBlockingServerChanges.Add(key);
			form.FormClosing += delegate(object sender, FormClosingEventArgs eventArgs)
			{
				if (eventArgs.CloseReason == CloseReason.UserClosing) ArmOwnedWindowClickGuard();
			};

			form.FormClosed += delegate

			{

				Form tracked;

				if (modelessToolWindows.TryGetValue(key, out tracked) && object.ReferenceEquals(tracked, form)) modelessToolWindows.Remove(key);

				modelessToolsBlockingServerChanges.Remove(key);

				if (!IsDisposed && !Disposing && onClosed != null) RunUiAction(onClosed);

			};

			form.Show(this);

			form.BringToFront();

		}



		private bool EnsureNoBlockingToolWindow()

		{

			if (modelessToolsBlockingServerChanges.Count == 0) return true;

			ShowNotice(Localization.CurrentLanguage == Localization.Korean ? "열려 있는 서버 관리 창을 닫은 뒤 진행해 주세요." : "Close the open server management window before continuing.", true);

			ActivateFirstBlockingToolWindow();

			return false;

		}



		private void ActivateFirstBlockingToolWindow()

		{

			foreach (string key in modelessToolsBlockingServerChanges)

			{

				Form form;

				if (!modelessToolWindows.TryGetValue(key, out form) || form == null || form.IsDisposed) continue;

				if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;

				form.BringToFront();

				form.Activate();

				break;

			}

		}



		private bool RequireStoppedServer()

		{

			if (!workflowRunning && !serverRunning)

			{

				return true;

			}

			ShowNoticeKey("Notice.StopBeforeSettings", true);

			return false;

		}



		private void OpenServerManagement()

		{

			string serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);

			Directory.CreateDirectory(serversRoot);

			ShowModelessToolWindow("server-management", delegate { return new MultiServerDashboardForm(serversRoot, workflowRunning || serverRunning); }, true, delegate

			{

				string activeProfile = ReadActiveProfileName(serversRoot);

				string directory = GetProfileDirectory(serversRoot, activeProfile);

				int port = ReadConfiguredServerPort(Path.Combine(directory, "server.properties"), 25565);

				SetConnectionAddress(GetLocalConnectionAddress(port));

			});

		}



		private void OpenBackupManager()

		{

			if (!RequireStoppedServer())

			{

				return;

			}

			string root;

			string directory;

			ReadActiveLauncherOptions(out root, out directory);

			ShowModelessToolWindow("backup", delegate { return new BackupManagerForm(directory); }, true, null);

		}



		private void OpenContentManager()

		{

			// UX-3: 서버 실행 중에도 콘텐츠 관리자를 열 수 있도록 허용 (파일 설치 후 재시작 안내)

			string root;

			string directory;

			LauncherOptions options = ReadActiveLauncherOptions(out root, out directory);

			if (serverRunning || workflowRunning)

			{

				ShowNotice(Localization.CurrentLanguage == Localization.Korean

					? "서버가 실행 중입니다. 설치한 콘텐츠는 서버를 재시작해야 적용됩니다."

					: "The server is running. Installed content will take effect after a server restart.", false);

			}

			ShowModelessToolWindow("content", delegate { return new UnifiedContentManagerForm(options); }, !serverRunning && !workflowRunning, null);

		}



		private void OpenPlayerManager()

		{

			if (!serverRunning)

			{

				ShowNoticeKey("Notice.NoServer", true);

				return;

			}

			ShowModelessToolWindow("players", delegate { return new PlayerManagementForm(SendServerCommand); }, false, null);

		}



		private void OpenNetworkTools()

		{

			string root;

			string directory;

			ReadActiveLauncherOptions(out root, out directory);

			int port = ReadConfiguredServerPort(Path.Combine(directory, "server.properties"), 25565);

			Action recheck = delegate

			{

				if (!serverRunning)

				{

					ShowNoticeKey("Notice.NoServer", true);

					return;

				}

				Thread thread = new Thread((ThreadStart)delegate

				{

					RecheckExternalReachabilityOnly(port);

				});

				thread.IsBackground = true;

				thread.Name = "외부 접속 다시 확인";

				thread.Start();

			};

			ShowModelessToolWindow("network", delegate { return new NetworkToolsForm(directory, port, currentSelectedJavaPath, recheck); }, false, null);

		}



		private void CreateDiagnostics()

		{

			try

			{

				string root;

				string directory;

				LauncherOptions options = ReadActiveLauncherOptions(out root, out directory);

				string path = CreateDiagnosticBundle(directory, options);

				DialogResult result = ShowMineHarborDialog(this, (Localization.CurrentLanguage == Localization.Korean ? "개인정보를 가린 진단 묶음을 만들었습니다.\r\n\r\n" : "Created a redacted diagnostic bundle.\r\n\r\n") + path, Localization.T("Button.Diagnostics"), MessageBoxButtons.OK, MessageBoxIcon.Information);

				ShowNotice(Localization.CurrentLanguage == Localization.Korean ? "진단 묶음을 만들었습니다." : "Diagnostic bundle created.", false);

			}

			catch (Exception exception)

			{

				ShowNotice((Localization.CurrentLanguage == Localization.Korean ? "진단 묶음을 만들지 못했습니다: " : "Could not create the diagnostic bundle: ") + exception.Message, true);

			}

		}



		private void UpgradeServerFiles()

		{

			if (!EnsureNoBlockingToolWindow()) return;

			if (workflowRunning || serverRunning)

			{

				ShowNoticeKey("Notice.StopBeforeUpgrade", true);

				return;

			}

			workflowRunning = true;

			startButton.Enabled = false;

			settingsButton.Enabled = false;

			upgradeButton.Enabled = false;

			SetStatusKey("Status.Preparing", false);

			ShowNoticeKey("Notice.UpgradeStarted", false);

			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "서버 업그레이드 정보를 확인하고 있습니다…" : "Checking server upgrade information…", true, -1);

			Thread worker = new Thread((ThreadStart)delegate

			{

				try

				{

					string serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);

					string activeProfile = ReadActiveProfileName(serversRoot);

					string serverDirectory = GetProfileDirectory(serversRoot, activeProfile);

					Dictionary<string, string> properties = ReadSimpleProperties(Path.Combine(serverDirectory, ".launcher-properties-configured"));

					if (properties.Count == 0)

					{

						ConfigureServerPropertiesGui(serversRoot, true);

						activeProfile = ReadActiveProfileName(serversRoot);

						serverDirectory = GetProfileDirectory(serversRoot, activeProfile);

						properties = ReadSimpleProperties(Path.Combine(serverDirectory, ".launcher-properties-configured"));

					}

					LauncherOptions options = ReadLauncherOptionsFromProperties(serversRoot, properties, activeProfile, ChooseMaximumMemoryGb(), true);

					CompatibleJavaRuntime compatibleJava = PrepareCompatibleJavaRuntime(options, serversRoot, options.CustomJavaMajor);

					string javaPath = compatibleJava.JavaPath;

					ServerRuntime runtime = PrepareServerRuntime(options.ServerDirectory, options, javaPath, false);

					UpgradeServerRuntime(options.ServerDirectory, options, javaPath, runtime, true);

					TryPostToUi(this, (MethodInvoker)delegate

					{

						workflowRunning = false;

						startButton.Enabled = true;

						settingsButton.Enabled = true;

						upgradeButton.Enabled = true;

						SetStatusKey("Status.Off", false);

						ShowNoticeKey("Notice.UpgradeDone", false);

						SetLoadingState(string.Empty, false, -1);

					});

				}

				catch (Exception exception)

				{

					TryPostToUi(this, (MethodInvoker)delegate

					{

						workflowRunning = false;

						startButton.Enabled = true;

						settingsButton.Enabled = true;

						upgradeButton.Enabled = true;

						SetStatusKey("Status.Error", true);

						ShowNotice(Localization.F("Notice.UpgradeFailed", exception.Message), true);

						SetLoadingState(string.Empty, false, -1);

					});

				}

			});

			worker.IsBackground = true;

			worker.Name = Localization.T("Button.Upgrade");

			worker.Start();

		}



		private void OnLauncherClosing(object sender, FormClosingEventArgs eventArgs)

		{

			if (!workflowRunning && !serverRunning)

			{

				return;

			}

			

			DialogResult result = ShowMineHarborDialog(this, Localization.T("Close.Question"), Localization.T("Close.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

			if (result != DialogResult.Yes)

			{

				eventArgs.Cancel = true;

				return;

			}

			

			eventArgs.Cancel = true;

			if (!serverRunning)

			{

				closeAfterStop = true;

				ShowNoticeKey("Notice.CloseAfterWork", false);

				return;

			}



			// 안전 종료 프로세스 시작

			if (Interlocked.CompareExchange(ref currentServerStopRequested, 1, 0) == 0)

			{

				SetStatusKey("Status.Stopping", false);

				ShowNoticeKey("Notice.Stopping", false);

				SendServerCommand("stop");

				stopButton.Text = Localization.CurrentLanguage == Localization.Korean ? "강제 종료" : "Force Stop";

				

				Task.Run(async () =>

				{

					try

					{

						// UI 닫힘 대기용: 제한 시간 10초 대기

						var tcs = new TaskCompletionSource<bool>();

						lock (ServerProcessLock)

						{

							if (currentServerProcess != null && !currentServerProcess.HasExited)

							{

								currentServerProcess.EnableRaisingEvents = true;

								currentServerProcess.Exited += (s, e) => tcs.TrySetResult(true);

								if (currentServerProcess.HasExited) tcs.TrySetResult(true);

							}

							else tcs.TrySetResult(true);

						}

						

						await Task.WhenAny(tcs.Task, Task.Delay(10000));

						

						lock (ServerProcessLock)

						{

							if (currentServerProcess == null || currentServerProcess.HasExited)

							{

								closeAfterStop = true; // 서버가 종료되었으므로 UI 닫힘 처리 허용

								TryPostToUi(this, (MethodInvoker)delegate { RequestClose(); });

								return;

							}

						}

						

						// 10초 초과 시 사용자에게 강제 종료 확인 팝업

						TryPostToUi(this, (MethodInvoker)delegate { 

							StopServer(); // StopServer의 2번째 단계(강제 종료 경고)를 띄움

						});

					}

					catch (Exception ex) { Console.WriteLine("[Launcher] 닫기 대기 중 오류: " + ex.Message); }

				});

			}

			else

			{

				// 사용자가 이미 중지를 눌렀거나 다시 X를 눌렀을 때 강제 종료 프롬프트 표시

				StopServer();

			}

		}



		private void ToggleConsole()

		{

			consolePanel.Visible = !consolePanel.Visible;

			consoleButton.Text = consolePanel.Visible ? Localization.T("Button.ConsoleClose") : Localization.T("Button.ConsoleOpen");

			int maximumHeight = Math.Max(720, Screen.FromControl(this).WorkingArea.Height - 40);

			Size = new Size(Math.Max(Width, consolePanel.Visible ? 1120 : 940), Math.Min(Math.Max(Height, 720), maximumHeight));

			if (consolePanel.Visible)

			{

				consoleBox.SelectionStart = consoleBox.TextLength;

				consoleBox.ScrollToCaret();

			}

		}



		public void RequestClose()

		{

			if (IsDisposed)

			{

				return;

			}

			if (InvokeRequired)

			{

				TryPostToUi(this, (MethodInvoker)RequestClose);

				return;

			}

			FormClosing -= OnLauncherClosing;

			Close();

		}



		public void AppendConsole(string line)

		{

			if (IsDisposed)

			{

				return;

			}

			consoleQueue.Enqueue(line ?? string.Empty);

			int count = Interlocked.Increment(ref queuedConsoleLines);

			while (count > 2000)

			{

				string ignored;

				if (!consoleQueue.TryDequeue(out ignored))

				{

					break;

				}

				count = Interlocked.Decrement(ref queuedConsoleLines);

			}

		}



		private void FlushConsoleQueue()

		{

			if (IsDisposed || consoleBox == null)

			{

				return;

			}

			string line;

			List<string> appended = new List<string>();

			int count = 0;

			while (count < 500 && consoleQueue.TryDequeue(out line))

			{

				Interlocked.Decrement(ref queuedConsoleLines);

				consoleHistory.Add(line ?? string.Empty);

				appended.Add(line ?? string.Empty);

				count++;

			}

			if (count == 0)

			{

				return;

			}

			bool trimmed = consoleHistory.Count > 2000;

			if (trimmed)

			{

				consoleHistory.RemoveRange(0, consoleHistory.Count - 2000);

			}

			bool unfiltered = consoleSearchBox != null && string.IsNullOrWhiteSpace(consoleSearchBox.Text) && consoleFilterBox != null && consoleFilterBox.SelectedIndex <= 0;

			if (unfiltered && !trimmed)

			{

				RichTextUpdateState state = BeginStableRichTextUpdate(consoleBox);

				try

				{

					for (int i = 0; i < appended.Count; i++)

					{

						AppendStyledConsoleLine(appended[i]);

					}

				}

				finally

				{

					EndStableRichTextUpdate(consoleBox, state);

				}

			}

			else

			{

				RebuildConsoleView();

			}

		}



		private void RebuildConsoleView()

		{

			if (consoleBox == null || consoleBox.IsDisposed)

			{

				return;

			}

			string search = consoleSearchBox == null ? string.Empty : consoleSearchBox.Text.Trim();

			int filterIndex = consoleFilterBox == null ? 0 : consoleFilterBox.SelectedIndex;

			RichTextUpdateState state = BeginStableRichTextUpdate(consoleBox);

			try

			{

				consoleBox.Clear();

				for (int i = 0; i < consoleHistory.Count; i++)

				{

					string line = consoleHistory[i] ?? string.Empty;

					if (!string.IsNullOrEmpty(search) && line.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)

					{

						continue;

					}

					if (!ConsoleLineMatchesFilter(line, filterIndex))

					{

						continue;

					}

					AppendStyledConsoleLine(line);

				}

			}

			finally

			{

				EndStableRichTextUpdate(consoleBox, state);

			}

		}



		private void AppendStyledConsoleLine(string line)

		{

			Color normal = Color.FromArgb(215, 225, 235);

			Color color = normal;

			ConsoleLineKind kind = ClassifyConsoleLine(line);

			if (kind == ConsoleLineKind.Error)

			{

				color = Color.FromArgb(255, 117, 117);

			}

			else if (kind == ConsoleLineKind.Warning)

			{

				color = Color.FromArgb(255, 190, 92);

			}

			else if (kind == ConsoleLineKind.Compatibility)

			{

				color = Color.FromArgb(112, 184, 255);

			}

			consoleBox.SelectionColor = color;

			consoleBox.AppendText(line + Environment.NewLine);

			consoleBox.SelectionColor = normal;

		}



		public void SetConnectionAddress(string address)

		{

			if (InvokeRequired)

			{

				TryPostToUi(this, (MethodInvoker)delegate { SetConnectionAddress(address); });

				return;

			}

			addressBox.Text = string.IsNullOrWhiteSpace(address) ? Localization.T("Address.Empty") : address;

			copyButton.Enabled = !string.IsNullOrWhiteSpace(address) && address.IndexOf(":", StringComparison.Ordinal) >= 0;

			addressBox.AccessibleDescription = copyButton.Enabled

				? (Localization.CurrentLanguage == Localization.Korean ? "복사할 수 있는 서버 접속 주소입니다." : "Server connection address ready to copy.")

				: (Localization.CurrentLanguage == Localization.Korean ? "서버가 준비되면 접속 주소가 표시됩니다." : "The connection address appears when the server is ready.");

		}



		public void ShowNotice(string message, bool warning)

		{

			if (IsDisposed)

			{

				return;

			}

			if (InvokeRequired)

			{

				TryPostToUi(this, (MethodInvoker)delegate { ShowNotice(message, warning); });

				return;

			}

			noticeTextKey = null;

			SetNoticeText(message, warning);

		}



		private void ShowNoticeKey(string key, bool warning)

		{

			if (IsDisposed)

			{

				return;

			}

			if (InvokeRequired)

			{

				TryPostToUi(this, (MethodInvoker)delegate { ShowNoticeKey(key, warning); });

				return;

			}

			noticeTextKey = key;

			SetNoticeText(Localization.T(key), warning);

		}



		private void SetNoticeText(string text, bool warning)

		{

			noticeWarning = warning;

			noticeLabel.Text = text;

			noticeLabel.AccessibleName = text;

			noticeLabel.ForeColor = warning ? ThemePalette.Create(darkTheme).Warning : ThemePalette.Create(darkTheme).Muted;

		}



		private void SetStatus(string text, bool warning)

		{

			statusTextKey = null;

			SetStatusText(text, warning);

		}



		private void SetStatusKey(string key, bool warning)

		{

			statusTextKey = key;

			SetStatusText(Localization.T(key), warning);

		}



		private void SetStatusText(string text, bool warning)

		{

			ThemePalette palette = ThemePalette.Create(darkTheme);

			statusWarning = warning;

			statusLabel.Text = text;

			statusLabel.AccessibleName = text;

			statusPill.Width = Math.Min(430, Math.Max(150, statusLabel.PreferredWidth + 58));

			Color dotColor = warning ? palette.Warning : (serverRunning ? palette.Success : palette.Muted);

			statusDot.ForeColor = dotColor;

			

			// Calculate solid color to prevent Label text box artifacts in WinForms

			Color surface = palette.CardSecondary;

			int r = (dotColor.R * 20 + surface.R * 235) / 255;

			int g = (dotColor.G * 20 + surface.G * 235) / 255;

			int b = (dotColor.B * 20 + surface.B * 235) / 255;

			Color solidBg = Color.FromArgb(r, g, b);

			

			statusPill.BackColor = solidBg;

			statusLabel.BackColor = solidBg;

			statusDot.BackColor = solidBg;

		}



		private bool LoadTheme()

		{

			try

			{

				return File.Exists(themePath) && File.ReadAllText(themePath, Encoding.UTF8).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase);

			}

			catch

			{

				return false;

			}

		}



		private void SaveTheme()

		{

			try

			{

				Directory.CreateDirectory(Path.GetDirectoryName(themePath));

				File.WriteAllText(themePath, darkTheme ? "dark" : "light", new UTF8Encoding(false));

			}

			catch

			{

			}

		}



		[System.Runtime.InteropServices.DllImport("user32.dll")]

		private static extern int SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam);

		private const int WM_SETREDRAW = 11;



		
		private void ApplyTheme()
		{
			SendMessage(this.Handle, WM_SETREDRAW, false, 0);
			try
			{
				ThemePalette palette = ThemePalette.Create(darkTheme);
				BackColor = palette.Window;
				ForeColor = palette.Text;
				TitleBarDwm.ApplyTheme(this, palette.Window, palette.Text, palette.Border);

			ApplyThemeRecursive(this, palette);

			consoleBox.BackColor = palette.Console;

			consoleBox.ForeColor = Color.FromArgb(215, 225, 235);

			addressBox.BackColor = palette.CardSecondary;

			addressBox.ForeColor = palette.Text;

			commandBox.BackColor = palette.CardSecondary;

			commandBox.ForeColor = palette.Text;

			consoleSearchBox.BackColor = palette.CardSecondary;

			consoleSearchBox.ForeColor = palette.Text;

			consoleFilterBox.BackColor = palette.CardSecondary;

			consoleFilterBox.ForeColor = palette.Text;

			ApplyQuickCommandTheme();

			ModernComboBox consoleFilter = consoleFilterBox as ModernComboBox;

			if (consoleFilter != null)

			{

				consoleFilter.SelectionBackColor = palette.AccentSoft;

				consoleFilter.SelectionForeColor = palette.Text;

				consoleFilter.Invalidate();

			}

			themeButton.Text = darkTheme ? Localization.T("Theme.Light") : Localization.T("Theme.Dark");

			SetStatusText(statusLabel.Text, statusWarning);

			}

			finally

			{

				SendMessage(this.Handle, WM_SETREDRAW, true, 0);

				this.Refresh();

			}

		}



		private static void ApplyThemeRecursive(Control parent, ThemePalette palette)

		{

			foreach (Control control in parent.Controls)

			{

				ApplyModernControlPalette(control, palette);
				RoundedPanel roundedPanel = control as RoundedPanel;

				if (roundedPanel != null)

				{

					bool surface = string.Equals(Convert.ToString(roundedPanel.Tag), "surface", StringComparison.Ordinal);

					roundedPanel.BackColor = surface ? palette.CardSecondary : palette.Card;

					roundedPanel.BorderColor = surface ? roundedPanel.BackColor : palette.Border;

				}

				else if (control is Button)

				{

					Button button = (Button)control;

					string role = Convert.ToString(button.Tag);

					button.FlatAppearance.BorderColor = palette.Border;

					if (string.Equals(role, "primary", StringComparison.Ordinal))

					{

						button.BackColor = palette.Accent;

						button.ForeColor = Color.White;

						button.FlatAppearance.MouseOverBackColor = palette.AccentHover;

					}

					else if (string.Equals(role, "danger", StringComparison.Ordinal))

					{

						button.BackColor = palette.DangerSoft;

						button.ForeColor = palette.Danger;

						button.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(palette.DangerSoft, 0.04F);

					}

					else

					{

						button.BackColor = palette.Card;

						button.ForeColor = palette.Text;

						button.FlatAppearance.BorderColor = palette.Border;

						button.FlatAppearance.MouseOverBackColor = palette.AccentSoft;

					}

				}

				else if (control is Label && string.Equals(Convert.ToString(control.Tag), "muted", StringComparison.Ordinal))

				{

					control.BackColor = control.Parent == null ? palette.Window : control.Parent.BackColor;

					control.ForeColor = palette.Muted;

				}

				else if (!(control is TextBox) && !(control is RichTextBox))

				{

					if (control.Name != "statusLabel" && control.Name != "statusDot")

					{

						control.BackColor = parent is LauncherForm ? palette.Window : control.Parent.BackColor;

					}

					control.ForeColor = palette.Text;

				}

				ApplyThemeRecursive(control, palette);

			}

		}

	}




	private static class TitleBarDwm
	{
		[System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

		private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
		private const int DWMWA_BORDER_COLOR = 34;
		private const int DWMWA_CAPTION_COLOR = 35;
		private const int DWMWA_TEXT_COLOR = 36;

		public static void ApplyTheme(Form form, Color backColor, Color foreColor, Color borderColor)
		{
			if (form == null || form.Handle == IntPtr.Zero) return;

			if (!IsWindows11OrNewer())
			{
				return;
			}

			try
			{
				int backWin32 = ColorTranslator.ToWin32(backColor);
				DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref backWin32, sizeof(int));
				
				int foreWin32 = ColorTranslator.ToWin32(foreColor);
				DwmSetWindowAttribute(form.Handle, DWMWA_TEXT_COLOR, ref foreWin32, sizeof(int));

				int borderWin32 = ColorTranslator.ToWin32(borderColor);
				DwmSetWindowAttribute(form.Handle, DWMWA_BORDER_COLOR, ref borderWin32, sizeof(int));
			}
			catch
			{
			}
		}

		private static bool IsWindows11OrNewer()
		{
			try
			{
				using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
				{
					if (key != null)
					{
						object buildObj = key.GetValue("CurrentBuildNumber");
						int build;
						if (buildObj != null && int.TryParse(buildObj.ToString(), out build))
						{
							return build >= 22000;
						}
					}
				}
			}
			catch { }
			return false;
		}
	}

	private sealed class ServerSetupForm : Form

	{

		private readonly ServerSettings source;

		private readonly int maximumMemory;

				private readonly TextBox profileBox;

		private readonly ComboBox serverTypeBox;

		private readonly ComboBox versionBox;

		private readonly Label versionStatusLabel;

		private readonly CheckBox includeSnapshotsBox;

		private readonly CheckBox manualJarBox;

		private readonly TextBox manualJarPathBox;

		private readonly ComboBox customJavaBox;

		private readonly TextBox motdBox;

		private readonly NumericUpDown playersBox;

		private readonly NumericUpDown portBox;

		private readonly NumericUpDown memoryBox;

		private readonly ComboBox gameModeBox;

		private readonly ComboBox difficultyBox;

		private readonly CheckBox hardcoreBox;

		private readonly ComboBox levelTypeBox;

		private readonly string[] levelTypeIds = new string[] { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified", "minecraft:single_biome_surface" };

		private readonly CheckBox pvpBox;

		private readonly CheckBox whitelistBox;

		private readonly NumericUpDown viewBox;

		private readonly NumericUpDown simulationBox;

		private readonly CheckBox commandBlockBox;

		private readonly CheckBox onlineModeBox;

		private readonly CheckBox autoUpdateBox;

		private readonly TextBox ownerBox;

		private readonly Panel advancedPanel;

		private readonly Panel customPanel;

		private readonly Button advancedButton;

		private readonly ErrorProvider validationErrors;

		private readonly ToolTip setupToolTip;

		private readonly Panel body;

		private readonly Label validationLabel;

		private readonly ThemePalette setupPalette;

		private readonly Label rulesLabel;

		private readonly bool worldExists;

				private int versionLoadRequest;

		private static readonly object VersionCacheLock = new object();

		private static readonly Dictionary<string, CachedVersionChoices> VersionCache = new Dictionary<string, CachedVersionChoices>(StringComparer.OrdinalIgnoreCase);



		private sealed class CachedVersionChoices

		{

			public string[] Values;

			public DateTime LoadedUtc;

		}



		public ServerSettings SelectedSettings { get; private set; }



		public ServerSetupForm(ServerSettings current, int recommendedMemory, int safeMaximumMemory, bool editing, bool existingWorld)

		{

			ApplyLauncherWindowIcon(this);
			this.HandleCreated += (s, e) => TitleBarDwm.ApplyTheme(this, setupPalette.Window, setupPalette.Text, setupPalette.Border);

			source = current;

			maximumMemory = safeMaximumMemory;

			worldExists = existingWorld;

			Text = Localization.T(editing ? "Setup.Title.Edit" : "Setup.Title.New");

			Font = new Font("Pretendard", 11F);

			AutoScaleMode = AutoScaleMode.Dpi;

			StartPosition = FormStartPosition.CenterParent;

			Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;

			int setupWidth = Math.Min(790, Math.Max(520, workingArea.Width - 30));

			int setupHeight = Math.Min(860, Math.Max(600, workingArea.Height - 30));

			Size = new Size(setupWidth, setupHeight);

			MinimumSize = new Size(Math.Min(790, setupWidth), Math.Min(600, setupHeight));

			AutoScroll = false;

			validationErrors = new ErrorProvider();

			validationErrors.ContainerControl = this;

			validationErrors.BlinkStyle = ErrorBlinkStyle.NeverBlink;

			setupToolTip = new ToolTip();

			setupToolTip.AutoPopDelay = 8000;

			bool dark = launcherForm != null && launcherForm.UsesDarkTheme;

			setupPalette = ThemePalette.Create(dark);

			BackColor = setupPalette.Window;

			ForeColor = setupPalette.Text;



			body = new Panel();

			body.Location = new Point(26, 20);

			body.Size = new Size(Math.Max(320, ClientSize.Width - 52), Math.Max(400, ClientSize.Height - 92));

			body.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

			body.AutoScroll = true;

			body.AutoScrollMinSize = new Size(0, 704);

			Controls.Add(body);



			Label title = NewLabel(Localization.T(editing ? "Setup.Heading.Edit" : "Setup.Heading.New"), 21F, true);

			title.Location = new Point(0, 0);

			body.Controls.Add(title);

			Label description = NewLabel(Localization.T("Setup.Description"), 9.5F, false);

			description.ForeColor = setupPalette.Muted;

			description.Location = new Point(2, 38);

			body.Controls.Add(description);



			Label runtimeLabel = NewLabel(Localization.T("Setup.Runtime"), 12F, true);

			runtimeLabel.Location = new Point(0, 72);

			body.Controls.Add(runtimeLabel);

			Label profileLabel = NewLabel(Localization.T("Setup.ProfileName"), 9F, true);

			profileLabel.Location = new Point(0, 106);

			body.Controls.Add(profileLabel);

			profileBox = NewTextBox();

			profileBox.Location = new Point(0, 128);

			profileBox.Size = new Size(180, 28);

			profileBox.MaxLength = 48;

			profileBox.Text = current.ProfileName;

			profileBox.TabIndex = 0;

			body.Controls.Add(profileBox);

			Label serverTypeLabel = NewLabel(Localization.T("Setup.ServerType"), 9F, true);

			serverTypeLabel.Location = new Point(200, 106);

			body.Controls.Add(serverTypeLabel);

			serverTypeBox = NewCombo(GetServerTypeLabels());

			serverTypeBox.Location = new Point(200, 128);

			serverTypeBox.Width = 170;

			serverTypeBox.TabIndex = 1;

			body.Controls.Add(serverTypeBox);

			Label versionLabel = NewLabel(Localization.T("Setup.MinecraftVersion"), 9F, true);

			versionLabel.Location = new Point(390, 106);

			body.Controls.Add(versionLabel);

			versionBox = NewCombo(new string[] { string.IsNullOrWhiteSpace(current.MinecraftVersion) ? "26.2" : current.MinecraftVersion });

			versionBox.Location = new Point(390, 128);

			versionBox.Width = 150;

			versionBox.TabIndex = 2;

			body.Controls.Add(versionBox);

			includeSnapshotsBox = NewCheckBox(Localization.T("Setup.IncludeSnapshots"), current.IncludeSnapshots);

			includeSnapshotsBox.AutoSize = false;

			includeSnapshotsBox.Size = new Size(148, 38);

			includeSnapshotsBox.Location = new Point(552, 120);

			includeSnapshotsBox.TabIndex = 3;

			body.Controls.Add(includeSnapshotsBox);

			manualJarBox = NewCheckBox(Localization.T("Setup.UseManualJar"), current.UseManualJar);

			manualJarBox.Location = new Point(0, 178);

			manualJarBox.TabIndex = 4;

			body.Controls.Add(manualJarBox);

			manualJarPathBox = NewTextBox();

			manualJarPathBox.Location = new Point(180, 176);

			manualJarPathBox.Size = new Size(250, 28);

			manualJarPathBox.Text = current.ManualJarPath;

			manualJarPathBox.TabIndex = 5;

			body.Controls.Add(manualJarPathBox);

			Button browseJar = NewFlatButton(Localization.T("Setup.BrowseJar"), 92);

			browseJar.AccessibleDescription = "Tooltip.Browse";

			ApplyButtonIcon(browseJar, ButtonIcon.Folder);

			browseJar.Tag = "secondary";

			browseJar.Location = new Point(442, 170);

			browseJar.TabIndex = 6;

			browseJar.Click += delegate

			{

				using (OpenFileDialog dialog = new OpenFileDialog())

				{

					dialog.Filter = "JAR files (*.jar)|*.jar|All files (*.*)|*.*";

					dialog.Title = Localization.T("Setup.UseManualJar");

					if (dialog.ShowDialog(this) == DialogResult.OK)

					{

						manualJarPathBox.Text = dialog.FileName;

						manualJarBox.Checked = true;

					}

				}

			};

			body.Controls.Add(browseJar);

			Label customJavaLabel = NewLabel(Localization.T("Setup.JavaVersion"), 8.5F, true);

			customJavaLabel.Location = new Point(555, 158);

			body.Controls.Add(customJavaLabel);

			customJavaBox = NewCombo(new string[] { "Java 8", "Java 11", "Java 16", "Java 17", "Java 21", "Java 25" });

			customJavaBox.Width = 110;

			customJavaBox.Location = new Point(555, 180);

			customJavaBox.TabIndex = 7;

			int[] customJavaValues = new int[] { 8, 11, 16, 17, 21, 25 };

			customJavaBox.SelectedIndex = 5;

			for (int customJavaIndex = 0; customJavaIndex < customJavaValues.Length; customJavaIndex++)

			{

				if (customJavaValues[customJavaIndex] == current.CustomJavaMajor)

				{

					customJavaBox.SelectedIndex = customJavaIndex;

					break;

				}

			}

			body.Controls.Add(customJavaBox);

			versionStatusLabel = NewLabel(string.Empty, 8F, false);

			versionStatusLabel.Location = new Point(390, 158);

			versionStatusLabel.MaximumSize = new Size(150, 36);

			versionStatusLabel.ForeColor = setupPalette.Muted;

			body.Controls.Add(versionStatusLabel);



			Label basics = NewLabel(Localization.T("Setup.Basic"), 12F, true);

			basics.Location = new Point(0, 214);

			body.Controls.Add(basics);



			customPanel = new Panel();

			customPanel.Location = new Point(0, 244);

			customPanel.Size = new Size(700, 54);

			customPanel.Visible = true;

			body.Controls.Add(customPanel);

			Label gameModeLabel = NewLabel(Localization.T("Setup.GameMode"), 9F, true);

			gameModeLabel.Location = new Point(0, 0);

			customPanel.Controls.Add(gameModeLabel);

			gameModeBox = NewCombo(new string[] { Localization.T("GameMode.Survival"), Localization.T("GameMode.Creative"), Localization.T("GameMode.Adventure"), Localization.T("GameMode.Spectator") });

			gameModeBox.Location = new Point(0, 22);

			gameModeBox.TabIndex = 10;

			customPanel.Controls.Add(gameModeBox);

			Label difficultyLabel = NewLabel(Localization.T("Setup.Difficulty"), 9F, true);

			difficultyLabel.Location = new Point(160, 0);

			customPanel.Controls.Add(difficultyLabel);

			difficultyBox = NewCombo(new string[] { Localization.T("Difficulty.Peaceful"), Localization.T("Difficulty.Easy"), Localization.T("Difficulty.Normal"), Localization.T("Difficulty.Hard") });

			difficultyBox.Location = new Point(160, 22);

			difficultyBox.TabIndex = 11;

			customPanel.Controls.Add(difficultyBox);



			string isKorean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase) ? "Korean" : "English";

			string[] levelTypeDisplay = isKorean == "Korean" ? new string[] { "기본", "완전한 평지", "넓은 생물군계", "증폭", "단일 생물군계" } : new string[] { "Normal", "Flat", "Large Biomes", "Amplified", "Single Biome" };

			Label levelTypeLabel = NewLabel(isKorean == "Korean" ? "월드 유형" : "World Type", 9F, true);

			levelTypeLabel.Location = new Point(320, 0);

			customPanel.Controls.Add(levelTypeLabel);

			levelTypeBox = NewCombo(levelTypeDisplay);

			levelTypeBox.Location = new Point(320, 22);

			levelTypeBox.TabIndex = 12;

			customPanel.Controls.Add(levelTypeBox);



			hardcoreBox = NewCheckBox(Localization.T("Hardcore"), current.Hardcore);

			hardcoreBox.Location = new Point(500, 24);

			hardcoreBox.TabIndex = 13;

			customPanel.Controls.Add(hardcoreBox);



			Label motdLabel = NewLabel(Localization.T("Setup.ServerName"), 9F, true);

			motdLabel.Location = new Point(0, 308);

			body.Controls.Add(motdLabel);

			motdBox = NewTextBox();

			motdBox.Location = new Point(0, 330);

			motdBox.Size = new Size(340, 28);

			motdBox.MaxLength = 200;

			motdBox.Text = current.Motd;

			motdBox.TabIndex = 14;

			body.Controls.Add(motdBox);



			Label playersLabel = NewLabel(Localization.T("Setup.MaxPlayers"), 9F, true);

			playersLabel.Location = new Point(360, 308);

			body.Controls.Add(playersLabel);

			playersBox = NewNumber(1, 1000, current.MaxPlayers);

			playersBox.Location = new Point(360, 330);

			playersBox.TabIndex = 15;

			body.Controls.Add(playersBox);

			Label portLabel = NewLabel(Localization.T("Setup.Port"), 9F, true);

			portLabel.Location = new Point(490, 308);

			body.Controls.Add(portLabel);

			portBox = NewNumber(1, 65535, current.ServerPort);

			portBox.Location = new Point(490, 330);

			portBox.TabIndex = 16;

			body.Controls.Add(portBox);



			Label memoryLabel = NewLabel(Localization.T("Setup.Memory"), 9F, true);

			memoryLabel.Location = new Point(0, 370);

			body.Controls.Add(memoryLabel);

			memoryBox = NewNumber(2, safeMaximumMemory, Math.Min(Math.Max(current.MemoryGb, 2), safeMaximumMemory));

			memoryBox.Location = new Point(0, 392);

			memoryBox.TabIndex = 17;

			body.Controls.Add(memoryBox);

			Label memoryHint = NewLabel(Localization.F("Setup.MemoryHint", recommendedMemory, GetTotalPhysicalMemoryGb()), 9F, false);

			memoryHint.ForeColor = setupPalette.Muted;

			memoryHint.Location = new Point(112, 396);

			body.Controls.Add(memoryHint);



			advancedButton = NewFlatButton(Localization.T("Setup.More"), 188);

			advancedButton.AccessibleDescription = "Tooltip.Settings";

			advancedButton.Tag = "secondary";

			advancedButton.Visible = false;

			rulesLabel = NewLabel(Localization.T("Setup.Rules"), 12F, true);

			rulesLabel.Location = new Point(0, 440);

			body.Controls.Add(rulesLabel);

			advancedPanel = new Panel();

			advancedPanel.Location = new Point(0, 618);

			advancedPanel.Size = new Size(700, 84);

			advancedPanel.Visible = true;

			body.Controls.Add(advancedPanel);

			pvpBox = NewCheckBox(Localization.T("Setup.Pvp"), current.Pvp);

			pvpBox.Location = new Point(0, 0);

			pvpBox.TabIndex = 23;

			advancedPanel.Controls.Add(pvpBox);

			whitelistBox = NewCheckBox(Localization.T("Setup.Whitelist"), current.WhiteList);

			whitelistBox.Location = new Point(140, 0);

			whitelistBox.TabIndex = 24;

			advancedPanel.Controls.Add(whitelistBox);

			commandBlockBox = NewCheckBox(Localization.T("Setup.CommandBlock"), current.CommandBlock);

			commandBlockBox.Location = new Point(310, 0);

			commandBlockBox.TabIndex = 25;

			advancedPanel.Controls.Add(commandBlockBox);

			gameModeBox.SelectedIndexChanged += delegate

			{

				if (gameModeBox.SelectedIndex == 1)

				{

					commandBlockBox.Checked = true;

				}

			};

			onlineModeBox = NewCheckBox(Localization.T("Setup.OnlineMode"), current.OnlineMode);

			onlineModeBox.Location = new Point(470, 0);

			onlineModeBox.TabIndex = 26;

			advancedPanel.Controls.Add(onlineModeBox);

			autoUpdateBox = NewCheckBox(Localization.T("Setup.PaperUpdate"), current.AutoUpdate);

			autoUpdateBox.Location = new Point(0, 36);

			autoUpdateBox.TabIndex = 27;

			advancedPanel.Controls.Add(autoUpdateBox);

			Label viewLabel = NewLabel(Localization.T("Setup.ViewDistance"), 9F, true);

			viewLabel.Location = new Point(210, 34);

			advancedPanel.Controls.Add(viewLabel);

			viewBox = NewNumber(3, 32, current.ViewDistance);

			viewBox.Location = new Point(210, 56);

			viewBox.TabIndex = 28;

			advancedPanel.Controls.Add(viewBox);

			Label simulationLabel = NewLabel(Localization.T("Setup.SimulationDistance"), 9F, true);

			simulationLabel.Location = new Point(340, 34);

			advancedPanel.Controls.Add(simulationLabel);

			simulationBox = NewNumber(3, 32, current.SimulationDistance);

			simulationBox.Location = new Point(340, 56);

			simulationBox.TabIndex = 29;

			advancedPanel.Controls.Add(simulationBox);

			Label ownerLabel = NewLabel(Localization.T("Setup.Owner"), 9F, true);

			ownerLabel.Location = new Point(500, 34);

			advancedPanel.Controls.Add(ownerLabel);

			ownerBox = NewTextBox();

			ownerBox.Location = new Point(500, 56);

			ownerBox.Size = new Size(180, 28);

			ownerBox.MaxLength = 16;

			ownerBox.Text = current.OwnerName;

			ownerBox.TabIndex = 30;

			advancedPanel.Controls.Add(ownerBox);



			validationLabel = NewLabel(string.Empty, 9F, false);

			validationLabel.Location = new Point(26, ClientSize.Height - 58);

			validationLabel.Size = new Size(Math.Max(180, ClientSize.Width - 310), 44);

			validationLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

			validationLabel.AutoSize = false;

			validationLabel.TextAlign = ContentAlignment.MiddleLeft;

			validationLabel.ForeColor = setupPalette.Warning;

			validationLabel.AccessibleRole = AccessibleRole.Alert;

			Controls.Add(validationLabel);



			Button cancel = NewFlatButton(Localization.T(editing ? "Setup.CancelEdit" : "Setup.Cancel"), 100);

			cancel.AccessibleDescription = "Tooltip.Cancel";

			cancel.Tag = "secondary";

			cancel.Location = new Point(ClientSize.Width - 244, ClientSize.Height - 54);

			cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

			cancel.DialogResult = DialogResult.Cancel;

			cancel.TabIndex = 40;

			Controls.Add(cancel);

			Button save = NewFlatButton(Localization.T(editing ? "Setup.SaveEdit" : "Setup.Save"), 132);

			save.AccessibleDescription = "Tooltip.Save";

			ApplyButtonIcon(save, ButtonIcon.Check);

			save.Tag = "primary";

			save.Location = new Point(ClientSize.Width - 132, ClientSize.Height - 54);

			save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

			save.BackColor = setupPalette.Accent;

			save.ForeColor = Color.White;

			save.Click += SaveSettings;

			save.TabIndex = 39;

			Controls.Add(save);

			AcceptButton = save;

			CancelButton = cancel;



			SelectRuntimeValues(current);

			serverTypeBox.SelectedIndexChanged += delegate

			{

				RefreshVersionChoices(null);

				UpdateManualJarControls();

			};

			includeSnapshotsBox.CheckedChanged += delegate { RefreshVersionChoices(null); };

			manualJarBox.CheckedChanged += delegate { UpdateManualJarControls(); };

			Shown += delegate { BeginVersionChoiceLoad(null); };

			UpdateManualJarControls();

			string levelValue = string.IsNullOrWhiteSpace(current.LevelType) ? "minecraft:normal" : current.LevelType.ToLowerInvariant();

			levelTypeBox.SelectedIndex = Array.IndexOf(levelTypeIds, levelValue);

			if (levelTypeBox.SelectedIndex < 0) levelTypeBox.SelectedIndex = 0;

			SelectComboValues(current);

			ConfigureSetupAccessibility();

			RegisterValidationReset(profileBox);

			RegisterValidationReset(motdBox);

			RegisterValidationReset(manualJarPathBox);

			RegisterValidationReset(ownerBox);

			ApplySetupTheme(this, setupPalette);

			ApplyCommonButtonToolTips(this);

			 save.BackColor = setupPalette.Accent;

			save.ForeColor = Color.White;

		}



		private void ConfigureSetupAccessibility()

		{

			bool korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);

			ConfigureAccessibleField(profileBox, Localization.T("Setup.ProfileName"), korean ? "서버 목록과 데이터 폴더를 구분하는 이름입니다." : "Name used to identify this server profile and its data folder.");

			ConfigureAccessibleField(serverTypeBox, Localization.T("Setup.ServerType"), korean ? "Paper, Purpur, Fabric, Vanilla 또는 직접 JAR을 선택합니다." : "Choose Paper, Purpur, Fabric, Vanilla, or a custom JAR.");

			ConfigureAccessibleField(versionBox, Localization.T("Setup.MinecraftVersion"), korean ? "새 서버에서 사용할 Minecraft 버전입니다." : "Minecraft version used by the server.");

			ConfigureAccessibleField(includeSnapshotsBox, Localization.T("Setup.IncludeSnapshots"), korean ? "정식 버전 외에 스냅샷과 프리릴리즈도 버전 목록에 표시합니다." : "Also show snapshots and pre-releases in the version list.");

			ConfigureAccessibleField(manualJarPathBox, Localization.T("Setup.UseManualJar"), korean ? "직접 실행할 서버 JAR 파일 경로입니다." : "Path to the custom server JAR.");

			ConfigureAccessibleField(customJavaBox, Localization.T("Setup.JavaVersion"), korean ? "직접 JAR 제작자가 요구한 Java 버전입니다." : "Java version required by the custom JAR author.");

			ConfigureAccessibleField(autoUpdateBox, Localization.T("Setup.PaperUpdate"), korean ? "서버 시작 전에 선택한 종류와 버전의 최신 빌드를 확인합니다." : "Check for the latest build of the selected server type and version before start.");

			ConfigureAccessibleField(motdBox, Localization.T("Setup.ServerName"), korean ? "서버 목록에 표시되는 소개 문구입니다." : "Description shown in the Minecraft server list.");

			ConfigureAccessibleField(playersBox, Localization.T("Setup.MaxPlayers"), korean ? "동시에 접속할 수 있는 최대 인원입니다." : "Maximum number of simultaneous players.");

			ConfigureAccessibleField(portBox, Localization.T("Setup.Port"), korean ? "서버가 사용할 TCP 포트입니다." : "TCP port used by the server.");

			ConfigureAccessibleField(memoryBox, Localization.T("Setup.Memory"), korean ? "서버에 할당할 최대 메모리입니다." : "Maximum memory allocated to the server.");

			ConfigureAccessibleField(gameModeBox, Localization.T("Setup.GameMode"), string.Empty);

			ConfigureAccessibleField(difficultyBox, Localization.T("Setup.Difficulty"), string.Empty);

			ConfigureAccessibleField(viewBox, Localization.T("Setup.ViewDistance"), korean ? "플레이어 주변에서 전송할 청크 범위입니다." : "Chunk radius sent around each player.");

			ConfigureAccessibleField(simulationBox, Localization.T("Setup.SimulationDistance"), korean ? "몹과 레드스톤이 동작하는 청크 범위입니다." : "Chunk radius where mobs and redstone remain active.");

			ConfigureAccessibleField(ownerBox, Localization.T("Setup.Owner"), korean ? "자동 OP를 받을 Minecraft 사용자 이름입니다." : "Minecraft username that receives owner operator access.");

		}



		private void RegisterValidationReset(Control control)

		{

			control.TextChanged += delegate

			{

				if (!string.IsNullOrEmpty(validationErrors.GetError(control)))

				{

					validationErrors.SetError(control, string.Empty);

					validationLabel.Text = string.Empty;

				}

			};

		}



		private void ShowValidationError(Control control, string message)

		{

			validationErrors.SetError(control, message);

			validationLabel.Text = message;

			validationLabel.AccessibleName = message;

			validationLabel.ForeColor = setupPalette.Warning;

			body.ScrollControlIntoView(control);

			control.Focus();

		}



		private void SelectRuntimeValues(ServerSettings current)

		{

			string[] values = GetServerTypeValues();

			serverTypeBox.SelectedIndex = FindIndexOrDefault(values, NormalizeServerType(current.ServerType), 0);

			ApplyFastVersionChoices(string.IsNullOrWhiteSpace(current.MinecraftVersion) ? "26.2" : current.MinecraftVersion);

		}



		private void RefreshVersionChoices(string preferredVersion)

		{

			ApplyFastVersionChoices(preferredVersion);

			if (IsHandleCreated)

			{

				BeginVersionChoiceLoad(preferredVersion);

			}

		}



		private void ApplyFastVersionChoices(string preferredVersion)

		{

			string[] values = GetServerTypeValues();

			string serverType = values[Math.Max(0, Math.Min(serverTypeBox.SelectedIndex, values.Length - 1))];

			string previous = preferredVersion;

			if (string.IsNullOrWhiteSpace(previous) && versionBox.SelectedItem != null)

			{

				previous = Convert.ToString(versionBox.SelectedItem);

			}

			string cacheKey = serverType + "|" + includeSnapshotsBox.Checked.ToString();

			string[] versions = null;

			lock (VersionCacheLock)

			{

				CachedVersionChoices cached;

				if (VersionCache.TryGetValue(cacheKey, out cached) && DateTime.UtcNow - cached.LoadedUtc < TimeSpan.FromMinutes(10.0))

				{

					versions = cached.Values;

				}

			}

			if (versions == null)

			{

				List<string> fallback = new List<string>(GetFallbackMinecraftVersions(includeSnapshotsBox.Checked));

				if (!string.IsNullOrWhiteSpace(previous) && fallback.FindIndex(delegate(string item) { return string.Equals(item, previous, StringComparison.OrdinalIgnoreCase); }) < 0)

				{

					fallback.Insert(0, previous);

				}

				versions = fallback.ToArray();

			}

			ApplyVersionChoices(versions, previous);

		}



		private void ApplyVersionChoices(string[] versions, string preferredVersion)

		{

			string previous = preferredVersion;

			if (string.IsNullOrWhiteSpace(previous) && versionBox.SelectedItem != null)

			{

				previous = Convert.ToString(versionBox.SelectedItem);

			}

			versionBox.Items.Clear();

			versionBox.Items.AddRange(versions);

			int selected = FindIndexOrDefault(versions, previous, 0);

			if (versionBox.Items.Count > 0)

			{

				versionBox.SelectedIndex = selected;

			}

		}



		private void BeginVersionChoiceLoad(string preferredVersion)

		{

			string[] serverTypeValues = GetServerTypeValues();

			string serverType = serverTypeValues[Math.Max(0, Math.Min(serverTypeBox.SelectedIndex, serverTypeValues.Length - 1))];

			bool includeSnapshots = includeSnapshotsBox.Checked;

			string previous = preferredVersion;

			if (string.IsNullOrWhiteSpace(previous) && versionBox.SelectedItem != null)

			{

				previous = Convert.ToString(versionBox.SelectedItem);

			}

			string cacheKey = serverType + "|" + includeSnapshots.ToString();

			lock (VersionCacheLock)

			{

				CachedVersionChoices cached;

				if (VersionCache.TryGetValue(cacheKey, out cached) && DateTime.UtcNow - cached.LoadedUtc < TimeSpan.FromMinutes(10.0))

				{

					versionStatusLabel.Text = Localization.T("Setup.VersionReady");

					versionStatusLabel.ForeColor = setupPalette.Muted;

					setupToolTip.SetToolTip(versionStatusLabel, string.Empty);

					return;

				}

			}

			int request = Interlocked.Increment(ref versionLoadRequest);

			versionStatusLabel.Text = Localization.T("Setup.VersionLoading");

			versionStatusLabel.ForeColor = setupPalette.Muted;

			setupToolTip.SetToolTip(versionStatusLabel, string.Empty);

			Thread worker = new Thread((ThreadStart)delegate

			{

				string[] loaded = null;

				Exception failure = null;

				try

				{

					loaded = GetMinecraftVersionChoices(serverType, includeSnapshots);

					lock (VersionCacheLock)

					{

						CachedVersionChoices entry = new CachedVersionChoices();

						entry.Values = loaded;

						entry.LoadedUtc = DateTime.UtcNow;

						VersionCache[cacheKey] = entry;

					}

				}

				catch (Exception exception)

				{

					failure = exception;

				}

				if (IsDisposed || !IsHandleCreated)

				{

					return;

				}

				TryPostToUi(this, (MethodInvoker)delegate

				{

					if (request != versionLoadRequest || IsDisposed)

					{

						return;

					}

					if (loaded != null && loaded.Length > 0)

					{

						string currentSelection = versionBox.SelectedItem == null ? previous : Convert.ToString(versionBox.SelectedItem);

						ApplyVersionChoices(loaded, currentSelection);

						versionStatusLabel.Text = Localization.T("Setup.VersionReady");

						versionStatusLabel.ForeColor = setupPalette.Muted;

						setupToolTip.SetToolTip(versionStatusLabel, string.Empty);

					}

					else

					{

						versionStatusLabel.Text = Localization.T("Setup.VersionFallback");

						versionStatusLabel.ForeColor = setupPalette.Warning;

						setupToolTip.SetToolTip(versionStatusLabel, failure == null ? string.Empty : failure.Message);

					}

					versionStatusLabel.AccessibleName = versionStatusLabel.Text;

				});

			});

			worker.IsBackground = true;

			worker.Name = "Minecraft 버전 목록";

			worker.Start();

		}



		private void UpdateManualJarControls()

		{

			string[] values = GetServerTypeValues();

			string serverType = values[Math.Max(0, Math.Min(serverTypeBox.SelectedIndex, values.Length - 1))];

			bool customServer = serverType == "custom";

			if (customServer)

			{

				manualJarBox.Checked = true;

			}

			manualJarBox.Enabled = !customServer;

			includeSnapshotsBox.Enabled = !customServer;

			versionBox.Enabled = !customServer;

			bool enabled = manualJarBox.Checked || customServer;

			manualJarPathBox.Enabled = enabled;

			customJavaBox.Enabled = enabled;

			autoUpdateBox.Enabled = !customServer && !manualJarBox.Checked;

			setupToolTip.SetToolTip(manualJarBox, customServer ? (Localization.CurrentLanguage == Localization.Korean ? "직접 JAR 서버에는 JAR 파일 지정이 필수입니다." : "A JAR file is required for a custom server.") : string.Empty);

			setupToolTip.SetToolTip(versionBox, customServer ? (Localization.CurrentLanguage == Localization.Korean ? "직접 JAR은 파일 제작자가 안내한 Minecraft 버전을 사용합니다." : "Use the Minecraft version specified by the custom JAR author.") : string.Empty);

			setupToolTip.SetToolTip(autoUpdateBox, autoUpdateBox.Enabled ? string.Empty : (Localization.CurrentLanguage == Localization.Korean ? "직접 지정한 JAR은 런처가 자동으로 교체하지 않습니다." : "The launcher does not replace a manually selected JAR."));

		}







		private void SelectComboValues(ServerSettings settings)

		{

			gameModeBox.SelectedIndex = settings.GameMode == "creative" ? 1 : settings.GameMode == "adventure" ? 2 : settings.GameMode == "spectator" ? 3 : 0;

			difficultyBox.SelectedIndex = settings.Difficulty == "peaceful" ? 0 : settings.Difficulty == "easy" ? 1 : settings.Difficulty == "hard" ? 3 : 2;

		}



		private void SaveSettings(object sender, EventArgs eventArgs)

		{

			validationErrors.Clear();

			validationLabel.Text = string.Empty;

			if (!IsValidProfileName(profileBox.Text.Trim()))

			{

				ShowValidationError(profileBox, Localization.T("Setup.ProfileInvalid"));

				return;

			}

			if (string.IsNullOrWhiteSpace(motdBox.Text))

			{

				ShowValidationError(motdBox, Localization.T("Setup.ServerNameRequired"));

				return;

			}

			string[] serverTypeValues = GetServerTypeValues();

			string selectedServerType = serverTypeValues[Math.Max(0, Math.Min(serverTypeBox.SelectedIndex, serverTypeValues.Length - 1))];

			bool useManualJar = manualJarBox.Checked || selectedServerType == "custom";

			if (useManualJar && (string.IsNullOrWhiteSpace(manualJarPathBox.Text) || !File.Exists(manualJarPathBox.Text.Trim('"', ' '))))

			{

				ShowValidationError(manualJarPathBox, Localization.T("Setup.ManualJarMissing"));

				return;

			}

			if (!IsValidOwnerName(ownerBox.Text.Trim()))

			{

				advancedPanel.Visible = true;

				advancedButton.Text = Localization.T("Setup.CloseMore");

				ShowValidationError(ownerBox, Localization.T("Setup.OwnerInvalid"));

				return;

			}

			if (!onlineModeBox.Checked)

			{

				DialogResult risk = ShowMineHarborDialog(this, Localization.T("Setup.OnlineModeWarning"), Localization.T("Setup.SecurityTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

				if (risk != DialogResult.Yes)

				{

					onlineModeBox.Checked = true;

					return;

				}

			}

			ServerSettings settings = new ServerSettings();

			settings.ProfileName = profileBox.Text.Trim();

			settings.ServerType = selectedServerType;

			settings.MinecraftVersion = versionBox.SelectedItem == null ? "26.2" : Convert.ToString(versionBox.SelectedItem);

			settings.IncludeSnapshots = includeSnapshotsBox.Checked;

			settings.UseManualJar = useManualJar;

			settings.ManualJarPath = useManualJar ? manualJarPathBox.Text.Trim('"', ' ') : string.Empty;

			settings.CustomJavaMajor = new int[] { 8, 11, 16, 17, 21, 25 }[Math.Max(0, customJavaBox.SelectedIndex)];

			settings.PresetName = Localization.T("Preset.Custom");

			settings.GameMode = new string[] { "survival", "creative", "adventure", "spectator" }[Math.Max(0, gameModeBox.SelectedIndex)];

			settings.Difficulty = new string[] { "peaceful", "easy", "normal", "hard" }[Math.Max(0, difficultyBox.SelectedIndex)];

			settings.LevelType = levelTypeIds[Math.Max(0, levelTypeBox.SelectedIndex)];

			settings.Hardcore = hardcoreBox.Checked;

			settings.MaxPlayers = (int)playersBox.Value;

			settings.Motd = motdBox.Text.Trim();

			settings.ServerPort = (int)portBox.Value;

			settings.MemoryGb = (int)memoryBox.Value;

			settings.Pvp = pvpBox.Checked;

			settings.WhiteList = whitelistBox.Checked;

			settings.ViewDistance = (int)viewBox.Value;

			settings.SimulationDistance = (int)simulationBox.Value;

			settings.CommandBlock = settings.GameMode == "creative" || commandBlockBox.Checked;

			settings.OnlineMode = onlineModeBox.Checked;

			settings.AutoUpdate = autoUpdateBox.Enabled && autoUpdateBox.Checked;

			settings.OwnerName = ownerBox.Text.Trim();

			if (worldExists && IsMinecraftVersionDowngrade(source.MinecraftVersion, settings.MinecraftVersion))

			{

				ShowMineHarborDialog(this, Localization.T("Setup.DowngradeWarning"), Localization.T("Setup.DowngradeTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);

				return;

			}

			if ((!string.Equals(settings.ServerType, source.ServerType, StringComparison.OrdinalIgnoreCase) || !string.Equals(settings.MinecraftVersion, source.MinecraftVersion, StringComparison.OrdinalIgnoreCase)) && worldExists)

			{

				DialogResult compatibilityWarning = ShowMineHarborDialog(this, Localization.T("Setup.CompatibilityWarning"), Localization.T("Setup.CompatibilityTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

				if (compatibilityWarning != DialogResult.Yes)

				{

					return;

				}

			}

			if (worldExists && !string.Equals(settings.LevelType, source.LevelType, StringComparison.OrdinalIgnoreCase))

			{

				DialogResult worldWarning = ShowMineHarborDialog(this, Localization.T("Setup.WorldWarning"), Localization.T("Setup.WorldTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);

				if (worldWarning != DialogResult.Yes)

				{

					return;

				}

			}

			SelectedSettings = settings;

			DialogResult = DialogResult.OK;

			Close();

		}



		private static Label NewLabel(string text, float size, bool semibold)

		{

			Label label = new Label();

			label.Text = text;

			label.Font = new Font(semibold ? "Pretendard" : "Pretendard", size);

			label.AutoSize = true;

			return label;

		}



		private static TextBox NewTextBox()

		{

			TextBox box = new TextBox();

			box.BorderStyle = BorderStyle.None;

			return box;

		}



		private static NumericUpDown NewNumber(int minimum, int maximum, int value)

		{

			NumericUpDown box = new NumericUpDown();

			box.Minimum = minimum;

			box.Maximum = maximum;

			box.Value = Math.Min(Math.Max(value, minimum), maximum);

			box.Width = 110;

			return box;

		}



		private static ComboBox NewCombo(string[] values)

		{

			ComboBox box = new ModernComboBox();

			box.Items.AddRange(values);

			box.Width = 160;

			box.SelectedIndex = 0;

			return box;

		}



		private static CheckBox NewCheckBox(string text, bool value)

		{

			CheckBox box = new ModernCheckBox();

			box.Text = text;

			box.Checked = value;

			box.AutoSize = true;

			return box;

		}



		private static Button NewFlatButton(string text, int width)

		{

			Button button = new RoundedButton();

			button.Text = text;

			button.Width = width;

			button.Height = 42;

			button.FlatStyle = FlatStyle.Flat;

			button.FlatAppearance.BorderSize = 0;

			return button;

		}



		
		private static void ApplySetupTheme(Control parent, ThemePalette palette)

		{

			foreach (Control control in parent.Controls)

			{

				ApplyModernControlPalette(control, palette);
				if (control is RoundedPresetButton)

				{

					control.BackColor = palette.Card;

					control.ForeColor = palette.Text;

					((RoundedPresetButton)control).FlatAppearance.BorderColor = palette.Border;

					((RoundedPresetButton)control).CheckedBackColor = palette.AccentSoft;

					((RoundedPresetButton)control).SelectedBorderColor = palette.Accent;

				}

				else if (control is Button)

				{

					Button button = (Button)control;

					button.FlatAppearance.BorderColor = palette.Border;

					if (string.Equals(Convert.ToString(button.Tag), "primary", StringComparison.Ordinal))

					{

						button.BackColor = palette.Accent;

						button.ForeColor = Color.White;

						button.FlatAppearance.MouseOverBackColor = palette.AccentHover;

					}

					else

					{

						button.BackColor = palette.CardSecondary;

						button.ForeColor = palette.Text;

						button.FlatAppearance.MouseOverBackColor = palette.AccentSoft;

					}

				}

				else if (control is TextBox || control is NumericUpDown || control is ComboBox)

				{

					control.BackColor = palette.Card;

					control.ForeColor = palette.Text;

					ModernComboBox comboBox = control as ModernComboBox;

					if (comboBox != null)

					{

						comboBox.SelectionBackColor = palette.AccentSoft;

						comboBox.SelectionForeColor = palette.Text;

						comboBox.Invalidate();

					}

				}

				else if (control is CheckBox)

				{

					CheckBox checkBox = (CheckBox)control;

					checkBox.UseVisualStyleBackColor = false;

					checkBox.BackColor = palette.Window;

					checkBox.ForeColor = palette.Text;

				}

				else if (!(control is Label) || control.ForeColor == Color.FromArgb(24, 32, 45))

				{

					control.BackColor = palette.Window;

					control.ForeColor = palette.Text;

				}

				ApplySetupTheme(control, palette);

			}

		}

	}

}

