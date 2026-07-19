using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static partial class Launcher
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private sealed class MemoryStatusEx
	{
		public uint Length;

		public uint MemoryLoad;

		public ulong TotalPhysical;

		public ulong AvailablePhysical;

		public ulong TotalPageFile;

		public ulong AvailablePageFile;

		public ulong TotalVirtual;

		public ulong AvailableVirtual;

		public ulong AvailableExtendedVirtual;

		public MemoryStatusEx()
		{
			Length = checked((uint)Marshal.SizeOf(typeof(MemoryStatusEx)));
		}
	}

	private sealed class ServerSettings
	{
		public string ProfileName;

		public string ServerType;

		public string MinecraftVersion;

		public bool IncludeSnapshots;

		public bool UseManualJar;

		public string ManualJarPath;

		public int CustomJavaMajor;

		public string PresetName;

		public string GameMode;

		public string Difficulty;

		public string LevelType;

		public int MaxPlayers;

		public string Motd;

		public int ServerPort;

		public bool Pvp;

		public bool WhiteList;

		public bool Hardcore;

		public int ViewDistance;

		public int SimulationDistance;

		public bool CommandBlock;

		public bool OnlineMode;

		public int MemoryGb;

		public bool AutoUpdate;

		public string OwnerName;
	}

	private sealed class LauncherOptions
	{
		public string ServerDirectory;

		public string ProfileName;

		public string ServerType;

		public string MinecraftVersion;

		public bool IncludeSnapshots;

		public bool UseManualJar;

		public string ManualJarPath;

		public int CustomJavaMajor;

		public int MemoryGb;

		public bool AutoUpdate;

		public string OwnerName;
	}

	private sealed class ServerRuntime
	{
		public string JarPath;

		public string BatchPath;

		public bool PreparedLatest;
	}

	private sealed class PaperBuildInfo
	{
		public int Build;

		public string Channel;

		public string Name;

		public string Url;

		public string Sha256;

		public long Size;
	}

	private sealed class ServerDownloadInfo
	{
		public string Name;

		public string Url;

		public string Sha1;

		public string Sha256;

		public long Size;

		public string BuildLabel;
	}

	private sealed class LauncherReleaseAsset
	{
		public string Url;

		public string Sha256;

		public long Size;

		public Version Version;

		public string ProductVersion;

		public string BuildNumber;

		public string ReleaseNotes;
		public string ReleaseNotesEn;

		public string MinimumSupportedVersion;
	}

	private sealed class LauncherUpdateStageException : Exception
	{
		public string Stage { get; private set; }

		public LauncherUpdateStageException(string stage, string message, Exception innerException)
			: base(message, innerException)
		{
			Stage = stage;
		}
	}

	private sealed class NetworkDetails
	{
		public string AdapterName;

		public string LocalIpv4;

		public string SubnetMask;

		public string Gateway;

		public string MacAddress;
	}

	private const long AdminPluginJarSize = 3873L;

	private const string AdminPluginJarSha256 = "627bba2a51ae1e7e77acde106d06ff4293a67a5811ddf25f9b072500dd649b67";

	private const string MutexName = "Local\\MineHarbor.MinecraftServerLauncher";

	private const string PaperDownloadPage = "https://papermc.io/downloads/paper";

	private const string PaperBuildsApi = "https://fill.papermc.io/v3/projects/paper/versions/26.2/builds";

	private const string PaperUserAgent = "Paper-26.2-Server-Launcher/1.3 (local-use; https://docs.papermc.io/)";

	private const string GenericServerUserAgent = "MineHarbor/1.5";

	private const string DefaultProfileName = "기본 서버";

	private const string DefaultServerType = "paper";

	private const string DefaultMinecraftVersion = "26.2";

	private const string MultiServerRootDirectoryName = "Minecraft-Servers-Data";

	private const string LauncherReleaseAssetName = "MineHarbor.exe";

	private const string LauncherUpdatePreferencesFileName = "launcher-update-preferences.properties";

	// 테스트가 실제 사용자 업데이트 설정을 건드리지 않도록 경로만 교체하는 내부 지점입니다.
	private static string LauncherUpdatePreferencesPathOverride = null;

	private const string LegacyLauncherReleaseAssetName = "Minecraft-Server-Launcher.exe";

	private const string LauncherUpdateUserAgent = "MineHarbor/1.6";

	private const string LauncherUpdateDirectoryName = "MineHarborLauncherUpdate";

	private const string LegacyLauncherUpdateDirectoryName = "Paper26.2LauncherUpdate";

	private const string LauncherUpdateMetadataAssetName = "update.json";

	private static string PendingLauncherUpdateDirectory;

	private static string GetLauncherLatestReleaseApi()
	{
		return "https://api.github.com/repos/" + GetLauncherReleaseRepositoryPath() + "/releases/latest";
	}

	private static string GetLauncherUpdateMetadataUrl()
	{
		return "https://github.com/" + GetLauncherReleaseRepositoryPath() + "/releases/latest/download/" + LauncherUpdateMetadataAssetName;
	}

	private static string GetLauncherReleaseRepositoryPath()
	{
		return "Mangom72/MineHarbor";
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx([In][Out] MemoryStatusEx buffer);

	[STAThread]
	private static int Main(string[] args)
	{
		try
		{
			Console.OutputEncoding = new UTF8Encoding(false);
			Console.InputEncoding = new UTF8Encoding(false);
		}
		catch (IOException)
		{
			// GUI 실행 파일에는 별도의 콘솔 핸들이 없을 수 있습니다.
		}
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		ServicePointManager.DefaultConnectionLimit = 64;
		if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.Ordinal))
		{
			Console.WriteLine(BuildVersionInfo.DisplayVersion);
			return 0;
		}
		if (args.Length == 5 && string.Equals(args[0], "--apply-launcher-update", StringComparison.Ordinal))
		{
			return ApplyDownloadedLauncherUpdate(args[1], args[2], args[3], args[4]);
		}
		if (args.Length == 2 && string.Equals(args[0], "--cleanup-launcher-update", StringComparison.Ordinal))
		{
			CleanupLauncherUpdateDirectory(args[1]);
		}
		if (args.Length == 2 && string.Equals(args[0], "--confirm-launcher-update", StringComparison.Ordinal))
		{
			if (IsSafeLauncherUpdateDirectory(args[1])) PendingLauncherUpdateDirectory = Path.GetFullPath(args[1]);
		}
		int managedExitCode;
		if (TryRunManagedProfileMode(args, out managedExitCode))
		{
			return managedExitCode;
		}
		bool createdNew;
		using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
		{
			if (!createdNew)
			{
				ShowLauncherMessage("이미 실행 중인 MineHarbor가 있습니다.", true);
				return 1;
			}
			try
			{
				return RunGuiApplication();
			}
			catch (Exception ex)
			{
				ShowLauncherMessage("런처에서 처리하지 못한 오류가 발생했습니다.\r\n\r\n" + ex.Message, true);
				return 1;
			}
			finally
			{
				mutex.ReleaseMutex();
			}
		}
	}

	private static bool StartApprovedLauncherUpdateIfAvailable()
	{
		bool updateAvailable;
		return StartApprovedLauncherUpdateIfAvailable(false, out updateAvailable);
	}

	private static bool StartApprovedLauncherUpdateIfAvailable(bool manualRequest, out bool updateAvailable)
	{
		Console.WriteLine("런처 최신 버전을 확인하는 중...");
		ReportLauncherLoading(LauncherUiText("런처 최신 버전을 확인하고 있습니다…", "Checking for launcher updates…"), 10);
		LauncherReleaseAsset latestLauncherReleaseAsset = GetLatestLauncherReleaseAsset();
		if (!IsLauncherUpdateNewer(latestLauncherReleaseAsset, BuildVersionInfo.ProductVersion, BuildVersionInfo.BuildNumber))
		{
			updateAvailable = false;
			Console.WriteLine("런처가 최신 버전입니다.");
			return false;
		}
		updateAvailable = true;
		if (!manualRequest && IsLauncherUpdateIgnored(latestLauncherReleaseAsset))
		{
			Console.WriteLine("사용자가 이 런처 버전의 자동 업데이트 알림을 숨겼습니다.");
			return false;
		}
		if (!ConfirmLauncherUpdate(latestLauncherReleaseAsset))
		{
			Console.WriteLine("사용자가 런처 업데이트를 나중으로 미뤘습니다.");
			return false;
		}
		string location = Assembly.GetExecutingAssembly().Location;
		Console.WriteLine("승인된 런처 업데이트를 내려받는 중...");
		ReportLauncherLoading(LauncherUiText("새 런처를 내려받고 있습니다…", "Downloading the new launcher…"), 15);
		string text = Path.Combine(Path.GetTempPath(), LauncherUpdateDirectoryName);
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text2);
		string text3 = Path.Combine(text2, LauncherReleaseAssetName);
		try
		{
			EnsureUpdateDiskSpace(text2, latestLauncherReleaseAsset.Size);
			try
			{
				DownloadLauncherUpdate(latestLauncherReleaseAsset, text3);
			}
			catch (Exception exception)
			{
				throw new LauncherUpdateStageException("download", "런처 업데이트 파일을 다운로드하지 못했습니다.", exception);
			}
			FileInfo fileInfo2 = new FileInfo(text3);
			if (!fileInfo2.Exists || fileInfo2.Length != latestLauncherReleaseAsset.Size)
			{
				throw new LauncherUpdateStageException("download", "내려받은 런처의 파일 크기가 업데이트 정보와 다릅니다.", null);
			}
			if (!HashMatches(text3, latestLauncherReleaseAsset.Sha256))
			{
				throw new LauncherUpdateStageException("hash", "내려받은 런처의 SHA-256 무결성 검증에 실패했습니다.", null);
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			processStartInfo.FileName = text3;
			processStartInfo.Arguments = "--apply-launcher-update " + QuoteCommandLineArgument(location) + " " + QuoteCommandLineArgument(latestLauncherReleaseAsset.Sha256) + " " + QuoteCommandLineArgument(text2) + " " + Process.GetCurrentProcess().Id;
			processStartInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
			bool requiresElevation = RequiresLauncherUpdateElevation(location);
			processStartInfo.UseShellExecute = requiresElevation;
			if (requiresElevation) processStartInfo.Verb = "runas";
			processStartInfo.CreateNoWindow = false;
			Process.Start(processStartInfo);
			return true;
		}
		catch
		{
			DeleteDirectoryIfPresent(text2);
			throw;
		}
	}

	private static bool ConfirmLauncherUpdate(LauncherReleaseAsset asset)
	{
		Func<bool> ask = delegate
		{
			bool korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
			string notes = string.IsNullOrWhiteSpace(asset.ReleaseNotes) ? (korean ? "변경 사항이 제공되지 않았습니다." : "No release notes were provided.") : asset.ReleaseNotes.Trim();
			if (notes.Length > 3000) notes = notes.Substring(0, 3000) + "…";
			Version currentProduct;
			Version minimumProduct;
			string compatibilityNotice = string.Empty;
			if (TryParseProductVersion(BuildVersionInfo.ProductVersion, out currentProduct) && TryParseProductVersion(asset.MinimumSupportedVersion, out minimumProduct) && currentProduct.CompareTo(minimumProduct) < 0)
			{
				compatibilityNotice = korean ? "주의: 현재 버전은 새 버전의 최소 지원 범위보다 오래됐습니다." : "Warning: the current version is older than the new release's supported upgrade range.";
			}
			using (Form dialog = new Form())
			{
				ApplyLauncherWindowIcon(dialog);
				dialog.Text = korean ? "런처 업데이트" : "Launcher update";
				dialog.StartPosition = FormStartPosition.CenterParent;
				dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
				dialog.MinimizeBox = false;
				dialog.MaximizeBox = false;
				dialog.ShowInTaskbar = false;
				dialog.ClientSize = new Size(580, 490);
				dialog.Font = new Font("Pretendard", 11F);
				bool dark = launcherForm != null && launcherForm.UsesDarkTheme;
				Color window = dark ? Color.FromArgb(20, 21, 26) : Color.FromArgb(248, 249, 252);
				Color surface = dark ? Color.FromArgb(31, 32, 39) : Color.White;
				Color textColor = dark ? Color.FromArgb(244, 246, 250) : Color.FromArgb(25, 31, 40);
				Color muted = dark ? Color.FromArgb(171, 176, 188) : Color.FromArgb(91, 99, 113);
				dialog.BackColor = window;

				Label heading = new Label();
				heading.Text = korean ? "새 런처를 사용할 수 있어요" : "A new launcher is available";
				heading.Font = new Font(dialog.Font.FontFamily, 18F, FontStyle.Bold);
				heading.ForeColor = textColor;
				heading.AutoSize = true;
				heading.Location = new Point(24, 22);
				dialog.Controls.Add(heading);

				Label summary = new Label();
				summary.Text = korean
					? "v" + BuildVersionInfo.ProductVersion + "  →  v" + asset.ProductVersion + "  ·  " + FormatLauncherFileSize(asset.Size)
					: "v" + BuildVersionInfo.ProductVersion + "  →  v" + asset.ProductVersion + "  ·  " + FormatLauncherFileSize(asset.Size);
				summary.ForeColor = muted;
				summary.AutoSize = true;
				summary.Location = new Point(27, 63);
				dialog.Controls.Add(summary);

				Label notesLabel = new Label();
				notesLabel.Text = korean ? "주요 변경 사항" : "What's new";
				notesLabel.ForeColor = textColor;
				notesLabel.AutoSize = true;
				notesLabel.Location = new Point(24, 98);
				dialog.Controls.Add(notesLabel);

				RichTextBox notesBox = new RichTextBox();
				notesBox.ReadOnly = true;
				notesBox.BorderStyle = BorderStyle.None;
				notesBox.BackColor = surface;
				notesBox.ForeColor = textColor;
				notesBox.Location = new Point(24, 124);
				notesBox.Size = new Size(532, 238);
				notesBox.Text = notes;
				notesBox.TabStop = true;
				dialog.Controls.Add(notesBox);

				Label compatibility = new Label();
				compatibility.Text = compatibilityNotice;
				compatibility.ForeColor = Color.FromArgb(230, 143, 38);
				compatibility.AutoSize = true;
				compatibility.Location = new Point(24, 369);
				compatibility.Visible = !string.IsNullOrEmpty(compatibilityNotice);
				dialog.Controls.Add(compatibility);

				CheckBox ignoreBox = new CheckBox();
				ignoreBox.Name = "launcherUpdateIgnoreCheckBox";
				ignoreBox.Text = korean ? "이 버전은 다시 보지 않기" : "Don't show this version again";
				ignoreBox.ForeColor = textColor;
				ignoreBox.BackColor = window;
				ignoreBox.AutoSize = true;
				ignoreBox.Checked = IsLauncherUpdateIgnored(asset);
				ignoreBox.Location = new Point(24, 401);
				dialog.Controls.Add(ignoreBox);

				ThemePalette palette = ThemePalette.Create(dark);
				Button later = CreateLauncherUpdateDialogButton(korean ? "나중에" : "Later", 112, "secondary", ButtonIcon.None, palette);
				later.DialogResult = DialogResult.No;
				later.Location = new Point(288, 433);
				dialog.Controls.Add(later);

				Button update = CreateLauncherUpdateDialogButton(korean ? "지금 업데이트" : "Update now", 148, "primary", ButtonIcon.Upgrade, palette);
				update.DialogResult = DialogResult.Yes;
				update.Location = new Point(408, 433);
				dialog.Controls.Add(update);
				dialog.AcceptButton = update;
				dialog.CancelButton = later;

				DialogResult result = dialog.ShowDialog(launcherForm);
				SetLauncherUpdateIgnored(asset, result != DialogResult.Yes && ignoreBox.Checked);
				return result == DialogResult.Yes;
			}
		};
		LauncherForm form = launcherForm;
		if (form != null && !form.IsDisposed && form.IsHandleCreated && form.InvokeRequired)
		{
			return (bool)form.Invoke(ask);
		}
		return ask();
	}

	private static Button CreateLauncherUpdateDialogButton(string text, int width, string role, ButtonIcon icon, ThemePalette palette)
	{
		return CreateMineHarborDialogButton(text, width, role, icon, palette);
	}

	private static string GetLauncherUpdatePreferencesPath()
	{
		if (!string.IsNullOrEmpty(LauncherUpdatePreferencesPathOverride)) return Path.GetFullPath(LauncherUpdatePreferencesPathOverride);
		return Path.Combine(GetLauncherUserDataDirectory(), LauncherUpdatePreferencesFileName);
	}

	private static bool IsLauncherUpdateIgnored(LauncherReleaseAsset asset)
	{
		if (asset == null) return false;
		Dictionary<string, string> values = ReadSimpleProperties(GetLauncherUpdatePreferencesPath());
		string product;
		string build;
		return values.TryGetValue("ignored-version", out product) && values.TryGetValue("ignored-build", out build)
			&& string.Equals(product, asset.ProductVersion, StringComparison.Ordinal)
			&& string.Equals(build, asset.BuildNumber, StringComparison.Ordinal);
	}

	private static void SetLauncherUpdateIgnored(LauncherReleaseAsset asset, bool ignored)
	{
		string path = GetLauncherUpdatePreferencesPath();
		if (!ignored)
		{
			try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { Console.WriteLine("[Launcher] 무시된 버전 정보 삭제 실패: " + ex.Message); }
			return;
		}
		if (asset == null) return;
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string temporary = path + ".tmp";
		try
		{
			File.WriteAllText(temporary, "ignored-version=" + asset.ProductVersion + "\r\nignored-build=" + asset.BuildNumber + "\r\n", new UTF8Encoding(false));
			if (File.Exists(path)) File.Delete(path);
			File.Move(temporary, path);
		}
		finally
		{
			try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception ex) { Console.WriteLine("[Launcher] 임시 무시된 버전 정보 삭제 실패: " + ex.Message); }
		}
	}

	private static bool IsLauncherUpdateNewer(LauncherReleaseAsset asset, string currentProductText, string currentBuildText)
	{
		Version currentProduct;
		Version latestProduct;
		Version currentBuild;
		if (asset == null || !TryParseProductVersion(currentProductText, out currentProduct) || !TryParseProductVersion(asset.ProductVersion, out latestProduct) || !Version.TryParse(currentBuildText, out currentBuild) || asset.Version == null)
		{
			throw new LauncherUpdateStageException("metadata", "제품 또는 빌드 버전 정보를 비교할 수 없습니다.", null);
		}
		int productComparison = latestProduct.CompareTo(currentProduct);
		return productComparison > 0 || productComparison == 0 && asset.Version.CompareTo(currentBuild) > 0;
	}

	private static string FormatLauncherFileSize(long size)
	{
		return (size / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " MB";
	}

	private static bool TryParseProductVersion(string value, out Version version)
	{
		version = null;
		if (string.IsNullOrWhiteSpace(value)) return false;
		string[] parts = value.Split('.');
		if (parts.Length != 3) return false;
		int major;
		int minor;
		int patch;
		if (!int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor) || !int.TryParse(parts[2], out patch) || major < 0 || minor < 0 || patch < 0) return false;
		version = new Version(major, minor, patch);
		return true;
	}

	private static LauncherReleaseAsset GetLatestLauncherReleaseAsset()
	{
		try
		{
			return GetLauncherReleaseAssetFromMetadata();
		}
		catch (WebException exception)
		{
			HttpWebResponse response = exception.Response as HttpWebResponse;
			if (response != null && response.StatusCode == HttpStatusCode.NotFound)
			{
				return GetLegacyLatestLauncherReleaseAsset();
			}
			throw new LauncherUpdateStageException("connection", "업데이트 확인 서버에 연결할 수 없습니다.", exception);
		}
		catch (LauncherUpdateStageException)
		{
			throw;
		}
		catch (Exception exception)
		{
			throw new LauncherUpdateStageException("metadata", "업데이트 메타데이터가 올바르지 않습니다.", exception);
		}
	}

	private static LauncherReleaseAsset GetLauncherReleaseAssetFromMetadata()
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GetLauncherUpdateMetadataUrl());
		request.Method = "GET";
		request.UserAgent = LauncherUpdateUserAgent;
		request.Accept = "application/json";
		request.Timeout = 20000;
		request.ReadWriteTimeout = 20000;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		string input;
		using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
		using (Stream stream = response.GetResponseStream())
		using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
		{
			if (response.StatusCode != HttpStatusCode.OK) throw new WebException("업데이트 메타데이터 서버가 HTTP " + (int)response.StatusCode + "을 반환했습니다.");
			input = reader.ReadToEnd();
		}
		return ParseLauncherUpdateMetadata(input);
	}

	private static LauncherReleaseAsset ParseLauncherUpdateMetadata(string input)
	{
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(input) as Dictionary<string, object>;
		if (root == null) throw new InvalidDataException("업데이트 메타데이터가 JSON 객체가 아닙니다.");
		string productVersion = root.ContainsKey("version") ? Convert.ToString(root["version"]) : string.Empty;
		string build = root.ContainsKey("build") ? Convert.ToString(root["build"]) : string.Empty;
		string url = root.ContainsKey("primary_download_url") ? Convert.ToString(root["primary_download_url"]) : root.ContainsKey("download_url") ? Convert.ToString(root["download_url"]) : string.Empty;
		string sha = root.ContainsKey("sha256") ? Convert.ToString(root["sha256"]) : string.Empty;
		string notes = root.ContainsKey("release_notes") ? Convert.ToString(root["release_notes"]) : string.Empty;
		string notesEn = root.ContainsKey("release_notes_en") ? Convert.ToString(root["release_notes_en"]) : string.Empty;
		string minimum = root.ContainsKey("minimum_supported_version") ? Convert.ToString(root["minimum_supported_version"]) : string.Empty;
		long size = root.ContainsKey("size") ? Convert.ToInt64(root["size"]) : 0;
		Version product;
		Version minimumVersion;
		Version buildVersion;
		if (!TryParseProductVersion(productVersion, out product) || !TryParseProductVersion(minimum, out minimumVersion) || !Version.TryParse(build, out buildVersion)) throw new InvalidDataException("업데이트 버전 정보가 올바르지 않습니다.");
		if (!IsValidSha256(sha) || size <= 0 || size > 1073741824 || !IsAllowedLauncherDownloadUrl(url)) throw new InvalidDataException("업데이트 다운로드 정보가 안전하지 않습니다.");
		LauncherReleaseAsset asset = new LauncherReleaseAsset();
		asset.Url = url;
		asset.Sha256 = sha;
		asset.Size = size;
		asset.Version = buildVersion;
		asset.ProductVersion = productVersion;
		asset.BuildNumber = build;
		asset.ReleaseNotes = notes;
		asset.ReleaseNotesEn = notesEn;
		asset.MinimumSupportedVersion = minimum;
		return asset;
	}

	private static LauncherReleaseAsset GetLegacyLatestLauncherReleaseAsset()
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(GetLauncherLatestReleaseApi());
		httpWebRequest.Method = "GET";
		httpWebRequest.UserAgent = LauncherUpdateUserAgent;
		httpWebRequest.Accept = "application/vnd.github+json";
		httpWebRequest.Headers["X-GitHub-Api-Version"] = "2022-11-28";
		httpWebRequest.Timeout = 20000;
		httpWebRequest.ReadWriteTimeout = 20000;
		httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		string input;
		using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
		{
			if (httpWebResponse.StatusCode != HttpStatusCode.OK)
			{
				throw new WebException("GitHub 릴리스 API가 HTTP " + (int)httpWebResponse.StatusCode + "을 반환했습니다.");
			}
			using (Stream stream = httpWebResponse.GetResponseStream())
			{
				using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8))
				{
					input = streamReader.ReadToEnd();
				}
			}
		}
		Dictionary<string, object> dictionary = new JavaScriptSerializer().DeserializeObject(input) as Dictionary<string, object>;
		string releaseTag = ((dictionary != null && dictionary.ContainsKey("tag_name")) ? Convert.ToString(dictionary["tag_name"]) : string.Empty);
		if (releaseTag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
		{
			releaseTag = releaseTag.Substring(1);
		}
		Version result;
		if (!Version.TryParse(releaseTag, out result))
		{
			throw new InvalidDataException("GitHub 최신 릴리스의 버전 정보를 인식하지 못했습니다.");
		}
		object[] array = ((dictionary != null && dictionary.ContainsKey("assets")) ? (dictionary["assets"] as object[]) : null);
		if (array == null)
		{
			throw new InvalidDataException("GitHub 최신 릴리스에서 파일 목록을 찾지 못했습니다.");
		}
		object[] array2 = array;
		object[] array3 = array2;
		foreach (object obj in array3)
		{
			Dictionary<string, object> dictionary2 = obj as Dictionary<string, object>;
			if (dictionary2 != null && dictionary2.ContainsKey("name") && (string.Equals(Convert.ToString(dictionary2["name"]), LauncherReleaseAssetName, StringComparison.Ordinal) || string.Equals(Convert.ToString(dictionary2["name"]), LegacyLauncherReleaseAssetName, StringComparison.Ordinal)))
			{
				string a = (dictionary2.ContainsKey("state") ? Convert.ToString(dictionary2["state"]) : string.Empty);
				string url = (dictionary2.ContainsKey("browser_download_url") ? Convert.ToString(dictionary2["browser_download_url"]) : string.Empty);
				string text = (dictionary2.ContainsKey("digest") ? Convert.ToString(dictionary2["digest"]) : string.Empty);
				long num = (dictionary2.ContainsKey("size") ? Convert.ToInt64(dictionary2["size"]) : 0);
				if (!string.Equals(a, "uploaded", StringComparison.OrdinalIgnoreCase) || !text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("GitHub 릴리스 파일의 상태 또는 SHA-256 정보를 검증하지 못했습니다.");
				}
				string text2 = text.Substring(7);
				if (!IsValidSha256(text2) || num <= 0 || num > 1073741824 || !IsAllowedLauncherDownloadUrl(url))
				{
					throw new InvalidDataException("GitHub 릴리스 파일의 다운로드 정보를 검증하지 못했습니다.");
				}
				LauncherReleaseAsset launcherReleaseAsset = new LauncherReleaseAsset();
				launcherReleaseAsset.Url = url;
				launcherReleaseAsset.Sha256 = text2;
				launcherReleaseAsset.Size = num;
				launcherReleaseAsset.Version = result;
				launcherReleaseAsset.ProductVersion = BuildVersionInfo.ProductVersion;
				launcherReleaseAsset.BuildNumber = result.ToString();
				launcherReleaseAsset.ReleaseNotes = LauncherUiText("기존 릴리스 형식에서 제공된 업데이트입니다.", "This update uses the legacy release format.");
				launcherReleaseAsset.MinimumSupportedVersion = BuildVersionInfo.MinimumSupportedVersion;
				return launcherReleaseAsset;
			}
		}
		throw new InvalidDataException("GitHub 최신 릴리스에 " + LauncherReleaseAssetName + " 파일이 없습니다.");
	}

	private static bool IsAllowedLauncherDownloadUrl(string url)
	{
		Uri result;
		string requiredPrefix = "/" + GetLauncherReleaseRepositoryPath() + "/releases/download/";
		if (Uri.TryCreate(url, UriKind.Absolute, out result) && result.Scheme == Uri.UriSchemeHttps && result.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && result.AbsolutePath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return result.AbsolutePath.EndsWith("/" + LauncherReleaseAssetName, StringComparison.OrdinalIgnoreCase) || result.AbsolutePath.EndsWith("/" + LegacyLauncherReleaseAssetName, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static bool IsValidSha256(string value)
	{
		if (string.IsNullOrEmpty(value) || value.Length != 64)
		{
			return false;
		}
		for (int i = 0; i < value.Length; i = checked(i + 1))
		{
			if (!Uri.IsHexDigit(value[i]))
			{
				return false;
			}
		}
		return true;
	}

	private static void EnsureUpdateDiskSpace(string updateDirectory, long updateSize)
	{
		string pathRoot = Path.GetPathRoot(Path.GetFullPath(updateDirectory));
		DriveInfo driveInfo = new DriveInfo(pathRoot);
		long num = checked(updateSize + 104857600);
		if (driveInfo.AvailableFreeSpace < num)
		{
			throw new IOException("런처 업데이트에 필요한 여유 공간이 부족합니다. 최소 " + Math.Ceiling((double)num / 1073741824.0).ToString("0.0") + "GB를 확보하세요.");
		}
	}

	private static void DownloadLauncherUpdate(LauncherReleaseAsset asset, string destinationPath)
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(asset.Url);
		httpWebRequest.Method = "GET";
		httpWebRequest.UserAgent = LauncherUpdateUserAgent;
		httpWebRequest.Accept = "application/octet-stream";
		httpWebRequest.Timeout = 120000;
		httpWebRequest.ReadWriteTimeout = 120000;
		httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
		{
			if (httpWebResponse.StatusCode != HttpStatusCode.OK)
			{
				throw new WebException("런처 다운로드 서버가 HTTP " + (int)httpWebResponse.StatusCode + "을 반환했습니다.");
			}
			using (Stream stream = httpWebResponse.GetResponseStream())
			{
				using (FileStream fileStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					byte[] array = new byte[1048576];
					long num = 0L;
					int num2 = -1;
					int num3;
					while ((num3 = stream.Read(array, 0, array.Length)) > 0)
					{
						fileStream.Write(array, 0, num3);
						num = checked(num + num3);
						int num4 = (int)Math.Min(100L, checked(num * 100) / asset.Size);
						if (num4 >= num2 + 10 || num4 == 100)
						{
							num2 = num4;
							Console.Write("\r런처 업데이트 다운로드: " + num4 + "%   ");
							ReportLauncherLoading(LauncherUiText("새 런처 다운로드 ", "Launcher download ") + num4 + "%", 15 + num4 * 75 / 100);
						}
						if (num > asset.Size)
						{
							throw new InvalidDataException("런처 다운로드 크기가 GitHub 릴리스 정보와 다릅니다.");
						}
					}
					if (num != asset.Size)
					{
						throw new InvalidDataException("런처 다운로드가 완료되기 전에 연결이 종료되었습니다.");
					}
					fileStream.Flush(true);
					Console.WriteLine();
				}
			}
		}
	}

	private static int ApplyDownloadedLauncherUpdate(string targetPath, string expectedHash, string updateDirectory, string parentProcessId)
	{
		string backupPath = null;
		string targetFullPath = null;
		string updateFullPath = null;
		bool backupCreated = false;
		try
		{
			string fullPath = Path.GetFullPath(updateDirectory);
			updateFullPath = fullPath;
			string fullPath2 = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
			targetFullPath = Path.GetFullPath(targetPath);
			if (!IsSafeLauncherUpdateDirectory(fullPath) || !string.Equals(Path.GetDirectoryName(fullPath2), fullPath, StringComparison.OrdinalIgnoreCase) || !IsValidSha256(expectedHash) || !string.Equals(Path.GetExtension(targetFullPath), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(targetFullPath))
			{
				throw new InvalidDataException("런처 업데이트 적용 인수가 안전하지 않습니다.");
			}
			if (!HashMatches(fullPath2, expectedHash))
			{
				throw new InvalidDataException("업데이트 실행 파일의 SHA-256 무결성 검증에 실패했습니다.");
			}
			int result;
			if (!int.TryParse(parentProcessId, out result) || result <= 0)
			{
				throw new InvalidDataException("기존 런처 프로세스 정보를 확인하지 못했습니다.");
			}
			try
			{
				using (Process process = Process.GetProcessById(result))
				{
					process.WaitForExit(30000);
				}
			}
			catch (ArgumentException)
			{
			}
			backupPath = Path.Combine(fullPath, "previous-launcher.exe");
			if (File.Exists(targetFullPath))
			{
				File.Copy(targetFullPath, backupPath, true);
				backupCreated = true;
			}
			Exception innerException = null;
			bool flag = false;
			for (int i = 0; i < 60; i = checked(i + 1))
			{
				try
				{
					ReplaceLauncherFileOnce(fullPath2, targetFullPath, expectedHash);
					flag = true;
				}
				catch (Exception ex2)
				{
					innerException = ex2;
					Thread.Sleep(500);
					continue;
				}
				break;
			}
			if (!flag)
			{
				if (File.Exists(backupPath))
				{
					File.Copy(backupPath, targetFullPath, true);
				}
				throw new IOException("기존 런처 파일을 새 버전으로 교체하지 못했습니다.", innerException);
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			processStartInfo.FileName = targetFullPath;
			processStartInfo.Arguments = "--confirm-launcher-update " + QuoteCommandLineArgument(fullPath);
			processStartInfo.WorkingDirectory = Path.GetDirectoryName(targetFullPath);
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = false;
			Process launched = Process.Start(processStartInfo);
			string confirmation = Path.Combine(fullPath, "launch-confirmed");
			DateTime deadline = DateTime.UtcNow.AddSeconds(20.0);
			while (DateTime.UtcNow < deadline && !File.Exists(confirmation))
			{
				if (launched.HasExited) break;
				Thread.Sleep(200);
			}
			if (!File.Exists(confirmation))
			{
				throw new IOException("새 버전 실행 확인을 받지 못했습니다.");
			}
			return 0;
		}
		catch (Exception ex3)
		{
			Console.WriteLine();
			Console.WriteLine("런처 자동 업데이트 적용에 실패했습니다.");
			Console.WriteLine(ex3.Message);
			if (backupCreated && !string.IsNullOrEmpty(backupPath) && !string.IsNullOrEmpty(targetFullPath) && File.Exists(backupPath))
			{
				try
				{
					File.Copy(backupPath, targetFullPath, true);
					Console.WriteLine("기존 런처 복구에 성공했습니다.");
					WriteLauncherUpdateResult("restore-success", ex3.Message);
					Process.Start(new ProcessStartInfo { FileName = targetFullPath, Arguments = string.IsNullOrEmpty(updateFullPath) ? string.Empty : "--cleanup-launcher-update " + QuoteCommandLineArgument(updateFullPath), WorkingDirectory = Path.GetDirectoryName(targetFullPath), UseShellExecute = true });
				}
				catch (Exception restoreException)
				{
					Console.WriteLine("기존 런처 복구에도 실패했습니다: " + restoreException.Message);
					WriteLauncherUpdateResult("restore-failed", ex3.Message + " / " + restoreException.Message);
				}
			}
			else if (!string.IsNullOrEmpty(targetFullPath) && File.Exists(targetFullPath))
			{
				WriteLauncherUpdateResult("replacement-failed", ex3.Message);
				try
				{
					Process.Start(new ProcessStartInfo { FileName = targetFullPath, Arguments = string.IsNullOrEmpty(updateFullPath) ? string.Empty : "--cleanup-launcher-update " + QuoteCommandLineArgument(updateFullPath), WorkingDirectory = Path.GetDirectoryName(targetFullPath), UseShellExecute = true });
				}
				catch
				{
				}
			}
			return 1;
		}
	}

	private static void ReplaceLauncherFileOnce(string sourcePath, string targetPath, string expectedHash)
	{
		if (!File.Exists(sourcePath) || !IsValidSha256(expectedHash)) throw new InvalidDataException("교체할 런처 파일 정보가 올바르지 않습니다.");
		File.Copy(sourcePath, targetPath, true);
		if (!HashMatches(targetPath, expectedHash)) throw new InvalidDataException("교체된 런처의 SHA-256 검증에 실패했습니다.");
	}

	private static void ConfirmLauncherUpdateStarted(string updateDirectory)
	{
		if (!IsSafeLauncherUpdateDirectory(updateDirectory)) return;
		try
		{
			File.WriteAllText(Path.Combine(updateDirectory, "launch-confirmed"), BuildVersionInfo.DisplayVersion, new UTF8Encoding(false));
			Thread cleanup = new Thread((ThreadStart)delegate
			{
				Thread.Sleep(1500);
				CleanupLauncherUpdateDirectory(updateDirectory);
			});
			cleanup.IsBackground = true;
			cleanup.Name = "런처 업데이트 임시 파일 정리";
			cleanup.Start();
		}
		catch
		{
		}
	}

	private static bool IsInstalledLauncherPath(string launcherPath)
	{
		try
		{
			string directory = Path.GetDirectoryName(Path.GetFullPath(launcherPath));
			if (File.Exists(Path.Combine(directory, "installed.mode"))) return true;
			string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			return IsPathInsideDirectory(directory, programFiles) || IsPathInsideDirectory(directory, programFilesX86);
		}
		catch
		{
			return false;
		}
	}

	private static bool RequiresLauncherUpdateElevation(string launcherPath)
	{
		try
		{
			string directory = Path.GetDirectoryName(Path.GetFullPath(launcherPath));
			return IsPathInsideDirectory(directory, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) || IsPathInsideDirectory(directory, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
		}
		catch
		{
			return false;
		}
	}

	private static bool IsPathInsideDirectory(string path, string root)
	{
		if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
		string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
	}

	private static void CleanupLauncherUpdateDirectory(string updateDirectory)
	{
		if (!IsSafeLauncherUpdateDirectory(updateDirectory))
		{
			return;
		}
		for (int i = 0; i < 20; i = checked(i + 1))
		{
			if (!Directory.Exists(updateDirectory))
			{
				break;
			}
			try
			{
				Thread.Sleep(250);
				Directory.Delete(updateDirectory, true);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	private static bool IsSafeLauncherUpdateDirectory(string path)
	{
		try
		{
			string text2 = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			return IsLauncherUpdateDirectoryUnderRoot(text2, LauncherUpdateDirectoryName) || IsLauncherUpdateDirectoryUnderRoot(text2, LegacyLauncherUpdateDirectoryName);
		}
		catch
		{
			return false;
		}
	}

	private static bool IsLauncherUpdateDirectoryUnderRoot(string normalizedPath, string rootDirectoryName)
	{
		string text = Path.GetFullPath(Path.Combine(Path.GetTempPath(), rootDirectoryName)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return normalizedPath.StartsWith(text, StringComparison.OrdinalIgnoreCase) && normalizedPath.Length > text.Length;
	}

	private static string QuoteCommandLineArgument(string value)
	{
		// cmd.exe 환경변수 확장(%VAR%)과 특수문자 주입을 방지합니다.
		string escaped = value.Replace("\"", "\\\"");
		escaped = escaped.Replace("%", "%%");
		return "\"" + escaped + "\"";
	}

	private static int Run()
	{
		Console.WriteLine("MineHarbor — Minecraft Server Launcher " + BuildVersionInfo.DisplayVersion);
		Console.WriteLine("제품 버전: v" + BuildVersionInfo.ProductVersion + " · 빌드 번호: " + BuildVersionInfo.BuildNumber);
		Console.WriteLine("================================");
		try
		{
			if (!ManagedChildMode && !launcherUpdateCheckCompleted && StartApprovedLauncherUpdateIfAvailable())
			{
				Console.WriteLine("런처 업데이트를 적용한 뒤 자동으로 다시 시작합니다.");
				RequestLauncherClose();
				return 0;
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine();
			launcherUpdateCheckCompleted = true;
			LauncherUpdateStageException stageEx = ex as LauncherUpdateStageException;
			string stage = stageEx != null ? stageEx.Stage : "unknown";
			if (string.Equals(stage, "hash", StringComparison.Ordinal))
			{
				Console.WriteLine("런처 업데이트 파일의 무결성 검증에 실패했습니다: " + ex.Message);
				Console.WriteLine("안전을 위해 서버를 시작하지 않습니다. 다시 실행해 주세요.");
				PauseBeforeExit();
				return 1;
			}
			Console.WriteLine("런처 업데이트를 확인하거나 적용하지 못했지만 서버 실행은 계속할 수 있습니다.");
			Console.WriteLine(ex.Message);
			ShowLauncherNotice(LauncherUiText("업데이트 확인 실패 · 기존 버전으로 계속합니다.", "Update check failed · continuing with current version."), true);
		}
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		ReportLauncherLoading(LauncherUiText("서버 데이터 폴더를 확인하고 있습니다…", "Checking the server data folder…"), 15);
		string text = GetServersRootDirectory(baseDirectory);
		try
		{
			Directory.CreateDirectory(text);
		}
		catch (Exception ex2)
		{
			Console.WriteLine("서버 목록 폴더를 만들 수 없습니다: " + ex2.Message);
			Console.WriteLine("이 EXE를 문서나 바탕 화면처럼 쓰기 가능한 위치로 옮겨 주세요.");
			PauseBeforeExit();
			return 1;
		}
		ReportLauncherLoading(LauncherUiText("활성 서버 프로필 설정을 읽고 있습니다…", "Reading the active server profile…"), 25);
		LauncherOptions launcherOptions = ConfigureServerPropertiesOnce(text);
		string serverDirectory = launcherOptions.ServerDirectory;
		Directory.CreateDirectory(serverDirectory);
		string path = Path.Combine(serverDirectory, "eula.txt");
		if (!EulaIsAccepted(path) && !AskForEula(path))
		{
			PauseBeforeExit();
			return 1;
		}
		Console.WriteLine("서버 버전에 맞는 Java 런타임을 확인하는 중...");
		ReportLauncherLoading(LauncherUiText("서버 버전에 맞는 Java를 확인하고 있습니다…", "Checking compatible Java…"), 40);
		CompatibleJavaRuntime compatibleJava = PrepareCompatibleJavaRuntime(launcherOptions, text, launcherOptions.CustomJavaMajor);
		string text4 = compatibleJava.JavaPath;
		Console.WriteLine("Java " + compatibleJava.MajorVersion + " 64비트 사용: " + compatibleJava.Source);
		if (!string.IsNullOrWhiteSpace(compatibleJava.Guidance))
		{
			Console.WriteLine(compatibleJava.Guidance);
		}
		RemoveLegacyBundledOwnerPlugin(serverDirectory);
		ReportLauncherLoading(LauncherUiText("서버 실행 파일을 준비하고 있습니다…", "Preparing the server executable…"), 60);
		ServerRuntime serverRuntime = PrepareServerRuntime(serverDirectory, launcherOptions, text4, false);
		if (launcherOptions.AutoUpdate && !serverRuntime.PreparedLatest)
		{
			ReportLauncherLoading(LauncherUiText("서버 파일 업데이트를 확인하고 있습니다…", "Checking server file updates…"), 75);
			UpgradeServerRuntime(serverDirectory, launcherOptions, text4, serverRuntime, false);
		}
		EnsureCommandBridgeChoiceAndInstallation(text, launcherOptions);
		int memoryGb = launcherOptions.MemoryGb;
		Console.WriteLine();
		Console.WriteLine("서버 프로필: " + launcherOptions.ProfileName);
		Console.WriteLine("서버 종류: " + GetServerTypeDisplayName(launcherOptions.ServerType));
		Console.WriteLine("Minecraft 버전: " + launcherOptions.MinecraftVersion);
		Console.WriteLine("서버 데이터: " + serverDirectory);
		Console.WriteLine("최대 메모리: " + memoryGb + "GB");
		Console.WriteLine("서버를 시작합니다. 서버를 끌 때는 콘솔에 stop을 입력하세요.");
		Console.WriteLine();
		int serverPort = ReadConfiguredServerPort(Path.Combine(serverDirectory, "server.properties"), 25565);
		SetLauncherConnectionAddress(GetLocalConnectionAddress(serverPort));
		Dictionary<string, string> serverProperties = ReadSimpleProperties(Path.Combine(serverDirectory, "server.properties"));
		bool onlineMode = !serverProperties.ContainsKey("online-mode") || !string.Equals(serverProperties["online-mode"], "false", StringComparison.OrdinalIgnoreCase);
		currentSelectedJavaPath = text4;
		int num;
		CommandBridgeSession commandBridge = null;
		try
		{
			commandBridge = StartCommandBridgeSessionIfInstalled(serverDirectory, launcherOptions.ProfileName);
			ReportLauncherLoading(LauncherUiText("서버 프로세스를 시작하고 있습니다…", "Starting the server process…"), 90);
			num = LaunchServer(text4, serverRuntime.JarPath, serverRuntime.BatchPath, serverDirectory, memoryGb, serverPort, launcherOptions.OwnerName, onlineMode, launcherOptions.ServerType, launcherOptions.MinecraftVersion);
		}
		finally
		{
			if (commandBridge != null)
			{
				commandBridge.Dispose();
				ClearActiveCommandBridge(commandBridge);
			}
			currentSelectedJavaPath = null;
		}
		Console.WriteLine();
		if (num == 0)
		{
			Console.WriteLine("서버가 정상적으로 종료되었습니다.");
		}
		else
		{
			Console.WriteLine("서버가 오류 코드 " + num + "로 종료되었습니다. 위 로그를 확인하세요.");
		}
		PauseBeforeExit();
		if (num != 0)
		{
			return 1;
		}
		return 0;
	}

	private static LauncherOptions ConfigureServerPropertiesOnce(string serversRootDirectory)
	{
		if (IsGuiMode())
		{
			return ConfigureServerPropertiesGui(serversRootDirectory, false);
		}
		string activeProfileName = ReadActiveProfileName(serversRootDirectory);
		string serverDirectory = GetProfileDirectory(serversRootDirectory, activeProfileName);
		Directory.CreateDirectory(serverDirectory);
		string path = Path.Combine(serverDirectory, ".launcher-properties-configured");
		Dictionary<string, string> dictionary = ReadSimpleProperties(path);
		int num = ChooseMaximumMemoryGb();
		int safeMemoryMaximumGb = GetSafeMemoryMaximumGb();
		if (dictionary.Count > 0)
		{
			LauncherOptions configuredOptions = ReadLauncherOptionsFromProperties(serversRootDirectory, dictionary, activeProfileName, num, false);
			bool flag = false;
			int result;
			if (!dictionary.ContainsKey("memory-gb") || !int.TryParse(dictionary["memory-gb"], out result) || result < 2 || result > safeMemoryMaximumGb)
			{
				Console.WriteLine();
				Console.WriteLine("[추가 런처 설정]");
				PrintPhysicalMemoryInformation(num, safeMemoryMaximumGb);
				result = AskInteger("서버에 할당할 최대 메모리(GB)", num, 2, safeMemoryMaximumGb);
				configuredOptions.MemoryGb = result;
				flag = true;
			}
			bool result2;
			if (!dictionary.ContainsKey("auto-update") || !bool.TryParse(dictionary["auto-update"], out result2))
			{
				configuredOptions.AutoUpdate = AskAutoUpdate(configuredOptions.ServerType, configuredOptions.MinecraftVersion);
				flag = true;
			}
			string ownerName = dictionary.ContainsKey("owner-name") ? dictionary["owner-name"] : string.Empty;
			if (!IsValidOwnerName(ownerName))
			{
				configuredOptions.OwnerName = GetDefaultOwnerName();
				flag = true;
			}
			if (flag)
			{
				WriteLauncherOptions(Path.Combine(configuredOptions.ServerDirectory, ".launcher-properties-configured"), configuredOptions);
			}
			WriteActiveProfileName(serversRootDirectory, configuredOptions.ProfileName);
			return configuredOptions;
		}
		ServerSettings serverSettings;
		while (true)
		{
			Console.WriteLine();
			Console.WriteLine("[최초 실행 서버 설정]");
			Console.WriteLine("Enter를 누르면 표시된 기본값을 사용합니다.");
			serverSettings = new ServerSettings();
			serverSettings.ProfileName = AskProfileName("서버 프로필 이름", DefaultProfileName);
			serverSettings.ServerType = AskChoice("서버 실행 파일", GetServerTypeLabels(), GetServerTypeValues(), 0);
			serverSettings.IncludeSnapshots = AskBoolean("스냅샷/프리릴리즈 버전도 목록에 포함할까요?", false);
			string[] minecraftVersions = GetMinecraftVersionChoices(serverSettings.ServerType, serverSettings.IncludeSnapshots);
			int defaultVersionIndex = FindIndexOrDefault(minecraftVersions, DefaultMinecraftVersion, 0);
			serverSettings.MinecraftVersion = AskChoice("Minecraft 버전", minecraftVersions, minecraftVersions, defaultVersionIndex);
			serverSettings.UseManualJar = AskBoolean("서버 JAR 파일을 직접 지정할까요?", false);
			serverSettings.ManualJarPath = serverSettings.UseManualJar ? AskExistingJarPath("서버 JAR 파일 경로") : string.Empty;
			string text = AskChoice("서버 프리셋", new string[8] { "평화로움 야생", "쉬움 야생", "보통 야생", "어려움 야생", "하드코어 야생", "크리에이티브 월드 (일반 지형)", "크리에이티브 월드 (평지)", "직접 설정" }, new string[8] { "survival-peaceful", "survival-easy", "survival-normal", "survival-hard", "survival-hardcore", "creative-normal", "creative-flat", "custom" }, 2);
			bool flag2 = string.Equals(text, "custom", StringComparison.Ordinal);
			bool flag3 = string.Equals(text, "creative-normal", StringComparison.Ordinal) || string.Equals(text, "creative-flat", StringComparison.Ordinal);
			if (flag2)
			{
				serverSettings.PresetName = "직접 설정";
				serverSettings.GameMode = AskChoice("게임 모드", new string[4] { "서바이벌", "크리에이티브", "모험", "관전자" }, new string[4] { "survival", "creative", "adventure", "spectator" }, 0);
				serverSettings.Difficulty = AskChoice("난이도", new string[4] { "평화로움", "쉬움", "보통", "어려움" }, new string[4] { "peaceful", "easy", "normal", "hard" }, 1);
				serverSettings.LevelType = "minecraft:normal";
				serverSettings.Hardcore = AskBoolean("하드코어 모드를 사용할까요?", false);
			}
			else
			{
				ApplyServerPreset(serverSettings, text);
			}
			serverSettings.MaxPlayers = AskInteger("최대 접속 인원", 20, 1, 1000);
			serverSettings.Motd = AskText("서버 이름(MOTD)", "A Minecraft Server", 200);
			serverSettings.ServerPort = AskInteger("서버 포트", 25565, 1, 65535);
			serverSettings.Pvp = AskBoolean("PvP를 허용할까요?", true);
			serverSettings.WhiteList = AskBoolean("화이트리스트를 사용할까요?", false);
			serverSettings.ViewDistance = AskInteger("시야 거리(청크)", 32, 3, 32);
			serverSettings.SimulationDistance = AskInteger("시뮬레이션 거리(청크)", 32, 3, 32);
			if (flag3 || string.Equals(serverSettings.GameMode, "creative", StringComparison.OrdinalIgnoreCase))
			{
				serverSettings.CommandBlock = true;
				Console.WriteLine("크리에이티브 월드는 명령 블록을 자동으로 허용합니다.");
			}
			else
			{
				serverSettings.CommandBlock = AskBoolean("명령 블록을 허용할까요?", true);
			}
			serverSettings.OnlineMode = AskBoolean("정품 계정 온라인 인증을 사용할까요?", true);
			if (!serverSettings.OnlineMode)
			{
				Console.WriteLine("주의: 온라인 인증을 끄면 다른 사람이 타인의 닉네임을 사칭할 수 있습니다.");
				if (!AskBoolean("위험을 이해했으며 온라인 인증을 끌까요?", false))
				{
					serverSettings.OnlineMode = true;
				}
			}
			PrintPhysicalMemoryInformation(num, safeMemoryMaximumGb);
			serverSettings.MemoryGb = AskInteger("서버에 할당할 최대 메모리(GB)", num, 2, safeMemoryMaximumGb);
			serverSettings.AutoUpdate = AskAutoUpdate(serverSettings.ServerType, serverSettings.MinecraftVersion);
			serverSettings.OwnerName = GetDefaultOwnerName();
			PrintSettingsSummary(serverSettings);
			if (AskBoolean("이 설정을 저장할까요?", true))
			{
				break;
			}
			Console.WriteLine("설정을 처음부터 다시 입력합니다.");
		}
		LauncherOptions launcherOptions = CreateLauncherOptionsFromSettings(serversRootDirectory, serverSettings);
		Directory.CreateDirectory(launcherOptions.ServerDirectory);
		string text2 = Path.Combine(launcherOptions.ServerDirectory, "server.properties");
		BackupConfigurationFile(text2);
		ApplyServerProperties(text2, serverSettings);
		WriteLauncherOptions(Path.Combine(launcherOptions.ServerDirectory, ".launcher-properties-configured"), launcherOptions);
		WriteActiveProfileName(serversRootDirectory, launcherOptions.ProfileName);
		Console.WriteLine("설정을 저장했습니다: " + text2);
		if (serverSettings.WhiteList)
		{
			Console.WriteLine("서버가 켜진 뒤 콘솔에서 whitelist add 플레이어이름 명령으로 사용자를 추가하세요.");
		}
		return launcherOptions;
	}

	private static string FindLegacyJava25Runtime()
	{
		string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paper26.2Server");
		string text3 = Path.Combine(text2, "Java25");
		string text4 = FindJava(text3);
		if (text4 != null)
		{
			return text4;
		}
		throw new FileNotFoundException("기존 Java 25 캐시가 없습니다.");
	}

	private static void ApplyServerPreset(ServerSettings settings, string preset)
	{
		settings.GameMode = "survival";
		settings.LevelType = "minecraft:normal";
		settings.Hardcore = false;
		switch (preset)
		{
		case "survival-peaceful":
			settings.PresetName = "평화로움 야생";
			settings.Difficulty = "peaceful";
			break;
		case "survival-easy":
			settings.PresetName = "쉬움 야생";
			settings.Difficulty = "easy";
			break;
		case "survival-normal":
			settings.PresetName = "보통 야생";
			settings.Difficulty = "normal";
			break;
		case "survival-hard":
			settings.PresetName = "어려움 야생";
			settings.Difficulty = "hard";
			break;
		case "survival-hardcore":
			settings.PresetName = "하드코어 야생";
			settings.Difficulty = "hard";
			settings.Hardcore = true;
			break;
		case "creative-normal":
			settings.PresetName = "크리에이티브 월드 (일반 지형)";
			settings.GameMode = "creative";
			settings.Difficulty = "peaceful";
			break;
		case "creative-flat":
			settings.PresetName = "크리에이티브 월드 (평지)";
			settings.GameMode = "creative";
			settings.Difficulty = "peaceful";
			settings.LevelType = "minecraft:flat";
			break;
		default:
			throw new InvalidDataException("선택한 서버 프리셋을 인식할 수 없습니다.");
		}
	}

	private static string GetServersRootDirectory(string baseDirectory)
	{
		return ResolveServersRootDirectory(baseDirectory);
	}

	private static string ReadActiveProfileName(string serversRootDirectory)
	{
		if (!string.IsNullOrEmpty(ManagedProfileOverride) && IsValidProfileName(ManagedProfileOverride))
		{
			return ManagedProfileOverride;
		}
		Dictionary<string, string> values = ReadSimpleProperties(Path.Combine(serversRootDirectory, ".active-server-profile"));
		if (values.ContainsKey("profile-name") && IsValidProfileName(values["profile-name"]))
		{
			return values["profile-name"];
		}
		string defaultProfileDirectory = GetProfileDirectory(serversRootDirectory, "기본 서버");
		if (File.Exists(Path.Combine(defaultProfileDirectory, ".launcher-properties-configured")))
		{
			return "기본 서버";
		}
		return "기본 서버";
	}

	private static void WriteActiveProfileName(string serversRootDirectory, string profileName)
	{
		Directory.CreateDirectory(serversRootDirectory);
		string contents = "profile-name=" + profileName + "\r\n";
		File.WriteAllText(Path.Combine(serversRootDirectory, ".active-server-profile"), contents, new UTF8Encoding(false));
	}

	private static string GetProfileDirectory(string serversRootDirectory, string profileName)
	{
		return Path.Combine(Path.Combine(serversRootDirectory, "servers"), ToSafeDirectoryName(profileName));
	}

	private static string ToSafeDirectoryName(string value)
	{
		StringBuilder builder = new StringBuilder();
		string trimmed = string.IsNullOrWhiteSpace(value) ? "기본 서버" : value.Trim();
		char[] invalid = Path.GetInvalidFileNameChars();
		for (int i = 0; i < trimmed.Length; i = checked(i + 1))
		{
			char character = trimmed[i];
			bool bad = false;
			for (int j = 0; j < invalid.Length; j = checked(j + 1))
			{
				if (character == invalid[j])
				{
					bad = true;
					break;
				}
			}
			builder.Append(bad ? '_' : character);
		}
		string safe = builder.ToString().Trim();
		return safe.Length == 0 ? "기본 서버" : safe;
	}

	private static bool IsValidProfileName(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 48)
		{
			return false;
		}
		string trimmed = value.Trim();
		if (string.Equals(trimmed, ".", StringComparison.Ordinal) || string.Equals(trimmed, "..", StringComparison.Ordinal) || trimmed.EndsWith(".", StringComparison.Ordinal))
		{
			return false;
		}
		for (int i = 0; i < trimmed.Length; i++)
		{
			if (char.IsControl(trimmed[i]))
			{
				return false;
			}
		}
		string safe = ToSafeDirectoryName(trimmed);
		string baseName = safe.Split('.')[0];
		string[] reservedNames = new string[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
		for (int i = 0; i < reservedNames.Length; i++)
		{
			if (string.Equals(baseName, reservedNames[i], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return safe.Length > 0;
	}

	private static string AskProfileName(string title, string defaultValue)
	{
		while (true)
		{
			string value = AskText(title, defaultValue, 48).Trim();
			if (IsValidProfileName(value))
			{
				return value;
			}
			Console.WriteLine("프로필 이름은 1~48자이며 Windows 파일명에 쓸 수 있어야 합니다.");
		}
	}

	private static string AskExistingJarPath(string title)
	{
		while (true)
		{
			Console.Write(title + ": ");
			string value = (Console.ReadLine() ?? string.Empty).Trim('"', ' ');
			if (File.Exists(value) && string.Equals(Path.GetExtension(value), ".jar", StringComparison.OrdinalIgnoreCase))
			{
				return Path.GetFullPath(value);
			}
			Console.WriteLine("존재하는 .jar 파일 경로를 입력해 주세요.");
		}
	}

	private static string[] GetServerTypeLabels()
	{
		return new string[7] { "Paper", "Vanilla(Minecraft 기본)", "Purpur", "Fabric", "Forge", "NeoForge", "직접 JAR 지정" };
	}

	private static string[] GetServerTypeValues()
	{
		return new string[7] { "paper", "vanilla", "purpur", "fabric", "forge", "neoforge", "custom" };
	}

	private static string NormalizeServerType(string value)
	{
		string text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
		switch (text)
		{
		case "vanilla":
		case "purpur":
		case "fabric":
		case "forge":
		case "neoforge":
		case "custom":
		case "paper":
			return text;
		default:
			return "paper";
		}
	}

	private static string GetServerTypeDisplayName(string value)
	{
		switch (NormalizeServerType(value))
		{
		case "vanilla":
			return "Vanilla(Minecraft 기본)";
		case "purpur":
			return "Purpur";
		case "fabric":
			return "Fabric";
		case "forge":
			return "Forge";
		case "neoforge":
			return "NeoForge";
		case "custom":
			return "직접 JAR";
		default:
			return "Paper";
		}
	}

	private static LauncherOptions CreateLauncherOptionsFromSettings(string serversRootDirectory, ServerSettings settings)
	{
		LauncherOptions options = new LauncherOptions();
		options.ProfileName = IsValidProfileName(settings.ProfileName) ? settings.ProfileName.Trim() : "기본 서버";
		options.ServerDirectory = GetProfileDirectory(serversRootDirectory, options.ProfileName);
		options.ServerType = NormalizeServerType(settings.ServerType);
		options.MinecraftVersion = string.IsNullOrWhiteSpace(settings.MinecraftVersion) ? "26.2" : settings.MinecraftVersion.Trim();
		options.IncludeSnapshots = settings.IncludeSnapshots;
		options.UseManualJar = settings.UseManualJar || options.ServerType == "custom";
		options.ManualJarPath = settings.ManualJarPath ?? string.Empty;
		options.CustomJavaMajor = settings.CustomJavaMajor >= 8 && settings.CustomJavaMajor <= 30 ? settings.CustomJavaMajor : 25;
		options.MemoryGb = settings.MemoryGb;
		options.AutoUpdate = settings.AutoUpdate;
		options.OwnerName = IsValidOwnerName(settings.OwnerName) ? settings.OwnerName : GetDefaultOwnerName();
		return options;
	}

	private static LauncherOptions ReadLauncherOptionsFromProperties(string serversRootDirectory, Dictionary<string, string> properties, string fallbackProfileName, int fallbackMemory, bool preferConfiguredDirectory)
	{
		LauncherOptions options = new LauncherOptions();
		string profileName = properties.ContainsKey("profile-name") ? properties["profile-name"] : fallbackProfileName;
		options.ProfileName = IsValidProfileName(profileName) ? profileName.Trim() : "기본 서버";
		options.ServerDirectory = GetProfileDirectory(serversRootDirectory, options.ProfileName);
		options.ServerType = properties.ContainsKey("server-type") ? NormalizeServerType(properties["server-type"]) : "paper";
		options.MinecraftVersion = properties.ContainsKey("minecraft-version") && !string.IsNullOrWhiteSpace(properties["minecraft-version"]) ? properties["minecraft-version"].Trim() : "26.2";
		bool includeSnapshots;
		options.IncludeSnapshots = properties.ContainsKey("include-snapshots") && bool.TryParse(properties["include-snapshots"], out includeSnapshots) && includeSnapshots;
		bool useManualJar;
		options.UseManualJar = properties.ContainsKey("use-manual-jar") && bool.TryParse(properties["use-manual-jar"], out useManualJar) && useManualJar;
		options.ManualJarPath = properties.ContainsKey("manual-jar-path") ? properties["manual-jar-path"] : string.Empty;
		int customJavaMajor;
		options.CustomJavaMajor = properties.ContainsKey("custom-java-major") && int.TryParse(properties["custom-java-major"], out customJavaMajor) && customJavaMajor >= 8 && customJavaMajor <= 30 ? customJavaMajor : 25;
		int memory;
		options.MemoryGb = properties.ContainsKey("memory-gb") && int.TryParse(properties["memory-gb"], out memory) ? memory : fallbackMemory;
		bool autoUpdate;
		options.AutoUpdate = properties.ContainsKey("auto-update") && bool.TryParse(properties["auto-update"], out autoUpdate) && autoUpdate;
		string ownerName = properties.ContainsKey("owner-name") ? properties["owner-name"] : string.Empty;
		options.OwnerName = IsValidOwnerName(ownerName) ? ownerName : GetDefaultOwnerName();
		return options;
	}

	private static int FindIndexOrDefault(string[] values, string desired, int fallback)
	{
		for (int i = 0; i < values.Length; i = checked(i + 1))
		{
			if (string.Equals(values[i], desired, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return fallback >= 0 && fallback < values.Length ? fallback : 0;
	}

	private static string[] GetMinecraftVersionChoices(bool includeSnapshots)
	{
		return GetMinecraftVersionChoices("paper", includeSnapshots);
	}

	private static string[] GetMinecraftVersionChoices(string serverType, bool includeSnapshots)
	{
		try
		{
			switch (NormalizeServerType(serverType))
			{
			case "paper":
				return GetPaperVersionChoices(includeSnapshots);
			case "purpur":
				return GetPurpurVersionChoices(includeSnapshots);
			case "fabric":
				return GetFabricVersionChoices(includeSnapshots);
			case "forge":
				return GetForgeVersionChoices(includeSnapshots);
			case "neoforge":
				return GetMojangVersionChoices(includeSnapshots);
			default:
				return GetMojangVersionChoices(includeSnapshots);
			}
		}
		catch
		{
			return GetFallbackMinecraftVersions(includeSnapshots);
		}
	}

	private static string[] GetFallbackMinecraftVersions(bool includeSnapshots)
	{
		if (includeSnapshots)
		{
			return new string[7] { "26.2", "26.1.2", "1.21.11", "1.21.10", "1.21.8", "1.20.6", "1.20.4" };
		}
		return new string[7] { "26.2", "26.1.2", "1.21.11", "1.21.10", "1.21.8", "1.20.6", "1.20.4" };
	}

	private static bool IsSnapshotLikeVersion(string version)
	{
		if (string.IsNullOrWhiteSpace(version))
		{
			return false;
		}
		string text = version.ToLowerInvariant();
		return text.IndexOf("snapshot", StringComparison.Ordinal) >= 0 || text.IndexOf("-pre", StringComparison.Ordinal) >= 0 || text.IndexOf("-rc", StringComparison.Ordinal) >= 0 || text.IndexOf("w", StringComparison.Ordinal) >= 0;
	}

	private static void AddVersion(List<string> versions, string version, bool includeSnapshots)
	{
		if (string.IsNullOrWhiteSpace(version))
		{
			return;
		}
		version = version.Trim();
		if (!includeSnapshots && IsSnapshotLikeVersion(version))
		{
			return;
		}
		for (int i = 0; i < versions.Count; i = checked(i + 1))
		{
			if (string.Equals(versions[i], version, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		versions.Add(version);
	}

	private static string[] GetPaperVersionChoices(bool includeSnapshots)
	{
		string input = DownloadTextWithUserAgent("https://fill.papermc.io/v3/projects/paper", GenericServerUserAgent);
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(input) as Dictionary<string, object>;
		Dictionary<string, object> groups = root != null && root.ContainsKey("versions") ? root["versions"] as Dictionary<string, object> : null;
		List<string> versions = new List<string>();
		if (groups != null)
		{
			foreach (KeyValuePair<string, object> group in groups)
			{
				object[] items = group.Value as object[];
				if (items == null)
				{
					continue;
				}
				for (int i = 0; i < items.Length; i = checked(i + 1))
				{
					AddVersion(versions, Convert.ToString(items[i]), includeSnapshots);
				}
			}
		}
		return versions.Count > 0 ? versions.ToArray() : GetFallbackMinecraftVersions(includeSnapshots);
	}

	private static string[] GetPurpurVersionChoices(bool includeSnapshots)
	{
		string input = DownloadTextWithUserAgent("https://api.purpurmc.org/v2/purpur", GenericServerUserAgent);
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(input) as Dictionary<string, object>;
		object[] items = root != null && root.ContainsKey("versions") ? root["versions"] as object[] : null;
		List<string> versions = new List<string>();
		if (items != null)
		{
			for (int i = items.Length - 1; i >= 0; i = checked(i - 1))
			{
				AddVersion(versions, Convert.ToString(items[i]), includeSnapshots);
			}
		}
		return versions.Count > 0 ? versions.ToArray() : GetFallbackMinecraftVersions(includeSnapshots);
	}

	private static string[] GetFabricVersionChoices(bool includeSnapshots)
	{
		string input = DownloadTextWithUserAgent("https://meta.fabricmc.net/v2/versions/game", GenericServerUserAgent);
		object[] items = new JavaScriptSerializer().DeserializeObject(input) as object[];
		List<string> versions = new List<string>();
		if (items != null)
		{
			for (int i = 0; i < items.Length; i = checked(i + 1))
			{
				Dictionary<string, object> item = items[i] as Dictionary<string, object>;
				if (item == null || !item.ContainsKey("version"))
				{
					continue;
				}
				bool stable = item.ContainsKey("stable") && Convert.ToBoolean(item["stable"]);
				if (includeSnapshots || stable)
				{
					AddVersion(versions, Convert.ToString(item["version"]), includeSnapshots);
				}
			}
		}
		return versions.Count > 0 ? versions.ToArray() : GetFallbackMinecraftVersions(includeSnapshots);
	}

	private static string[] GetForgeVersionChoices(bool includeSnapshots)
	{
		string input = DownloadTextWithUserAgent("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", GenericServerUserAgent);
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(input) as Dictionary<string, object>;
		Dictionary<string, object> promotions = root != null && root.ContainsKey("promos") ? root["promos"] as Dictionary<string, object> : null;
		List<string> versions = new List<string>();
		if (promotions != null)
		{
			foreach (KeyValuePair<string, object> promo in promotions)
			{
				int split = promo.Key.LastIndexOf('-');
				if (split > 0)
				{
					AddVersion(versions, promo.Key.Substring(0, split), includeSnapshots);
				}
			}
		}
		return versions.Count > 0 ? versions.ToArray() : GetFallbackMinecraftVersions(includeSnapshots);
	}

	private static string[] GetMojangVersionChoices(bool includeSnapshots)
	{
		string input = DownloadTextWithUserAgent("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", GenericServerUserAgent);
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(input) as Dictionary<string, object>;
		object[] items = root != null && root.ContainsKey("versions") ? root["versions"] as object[] : null;
		List<string> versions = new List<string>();
		if (items != null)
		{
			for (int i = 0; i < items.Length; i = checked(i + 1))
			{
				Dictionary<string, object> item = items[i] as Dictionary<string, object>;
				if (item == null || !item.ContainsKey("id"))
				{
					continue;
				}
				string type = item.ContainsKey("type") ? Convert.ToString(item["type"]) : string.Empty;
				if (includeSnapshots || string.Equals(type, "release", StringComparison.OrdinalIgnoreCase))
				{
					AddVersion(versions, Convert.ToString(item["id"]), includeSnapshots);
				}
			}
		}
		return versions.Count > 0 ? versions.ToArray() : GetFallbackMinecraftVersions(includeSnapshots);
	}

	private static bool AskAutoUpdate(string serverType, string minecraftVersion)
	{
		Console.WriteLine();
		Console.WriteLine("서버 자동 업데이트는 선택한 서버 종류와 Minecraft 버전 안에서만 적용됩니다.");
		Console.WriteLine("플러그인 호환 문제가 생길 수 있으므로 업데이트 후 서버 로그를 확인하고 정기적으로 백업하세요.");
		return AskBoolean("최신 " + GetServerTypeDisplayName(serverType) + " " + minecraftVersion + " 서버 파일을 자동으로 내려받을까요?", false);
	}

	private static void PrintPhysicalMemoryInformation(int recommendedMemory, int maximumMemory)
	{
		Console.WriteLine();
		Console.WriteLine("감지된 시스템 메모리: 약 " + GetTotalPhysicalMemoryGb() + "GB");
		Console.WriteLine("권장 서버 메모리: " + recommendedMemory + "GB (Windows용 메모리는 별도로 남깁니다.)");
		Console.WriteLine("입력 가능한 안전 상한: " + maximumMemory + "GB");
	}

	private static Dictionary<string, string> ReadSimpleProperties(string path)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!File.Exists(path))
		{
			return dictionary;
		}
		string[] array = File.ReadAllLines(path, Encoding.UTF8);
		string[] array2 = array;
		string[] array3 = array2;
		string[] array4 = array3;
		foreach (string text in array4)
		{
			string text2 = text.Trim();
			if (text2.Length != 0 && text2[0] != '#' && text2[0] != '!')
			{
				int num = text2.IndexOf('=');
				if (num > 0)
				{
					dictionary[text2.Substring(0, num).Trim()] = text2.Substring(checked(num + 1)).Trim();
				}
			}
		}
		return dictionary;
	}

	private static void WriteLauncherOptions(string path, LauncherOptions options)
	{
		string contents = "launcher-settings-version=6\r\nprofile-name=" + options.ProfileName + "\r\nserver-type=" + options.ServerType + "\r\nminecraft-version=" + options.MinecraftVersion + "\r\ninclude-snapshots=" + options.IncludeSnapshots.ToString().ToLowerInvariant() + "\r\nuse-manual-jar=" + options.UseManualJar.ToString().ToLowerInvariant() + "\r\nmanual-jar-path=" + options.ManualJarPath + "\r\ncustom-java-major=" + options.CustomJavaMajor + "\r\nmemory-gb=" + options.MemoryGb + "\r\nauto-update=" + options.AutoUpdate.ToString().ToLowerInvariant() + "\r\nowner-name=" + options.OwnerName + "\r\n";
		File.WriteAllText(path, contents, new UTF8Encoding(false));
	}

	private static string GetDefaultOwnerName()
	{
		string userName = Environment.UserName;
		if (IsValidOwnerName(userName))
		{
			return userName;
		}
		return "ServerOwner";
	}

	private static bool IsValidOwnerName(string value)
	{
		if (string.IsNullOrEmpty(value) || value.Length < 3 || value.Length > 16)
		{
			return false;
		}
		for (int i = 0; i < value.Length; i = checked(i + 1))
		{
			char character = value[i];
			bool asciiLetter = character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z';
			bool asciiDigit = character >= '0' && character <= '9';
			if (!asciiLetter && !asciiDigit && character != '_')
			{
				return false;
			}
		}
		return true;
	}

	private static string AskChoice(string title, string[] labels, string[] values, int defaultIndex)
	{
		checked
		{
			int result;
			while (true)
			{
				Console.WriteLine();
				Console.WriteLine(title + ":");
				for (int i = 0; i < labels.Length; i++)
				{
					string text = ((i == defaultIndex) ? " (기본)" : string.Empty);
					Console.WriteLine("  " + (i + 1) + ". " + labels[i] + text);
				}
				Console.Write("번호 입력: ");
				string text2 = Console.ReadLine();
				if (string.IsNullOrWhiteSpace(text2))
				{
					return values[defaultIndex];
				}
				if (int.TryParse(text2.Trim(), out result) && result >= 1 && result <= values.Length)
				{
					break;
				}
				Console.WriteLine("목록에 있는 번호를 입력해 주세요.");
			}
			return values[result - 1];
		}
	}

	private static int AskInteger(string title, int defaultValue, int minimum, int maximum)
	{
		int result;
		while (true)
		{
			Console.Write(title + " [기본: " + defaultValue + ", 범위: " + minimum + "~" + maximum + "]: ");
			string text = Console.ReadLine();
			if (string.IsNullOrWhiteSpace(text))
			{
				return defaultValue;
			}
			if (int.TryParse(text.Trim(), out result) && result >= minimum && result <= maximum)
			{
				break;
			}
			Console.WriteLine(minimum + "부터 " + maximum + " 사이의 숫자를 입력해 주세요.");
		}
		return result;
	}

	private static string AskText(string title, string defaultValue, int maximumLength)
	{
		string text;
		while (true)
		{
			Console.Write(title + " [기본: " + defaultValue + "]: ");
			text = Console.ReadLine();
			if (string.IsNullOrEmpty(text))
			{
				return defaultValue;
			}
			if (text.Length <= maximumLength)
			{
				break;
			}
			Console.WriteLine(maximumLength + "자 이하로 입력해 주세요.");
		}
		return text;
	}

	private static bool AskBoolean(string title, bool defaultValue)
	{
		while (true)
		{
			Console.Write(title + (defaultValue ? " [Y/n]: " : " [y/N]: "));
			string text = Console.ReadLine();
			if (string.IsNullOrWhiteSpace(text))
			{
				break;
			}
			switch (text.Trim().ToLowerInvariant())
			{
			case "y":
			case "yes":
			case "예":
			case "네":
			case "true":
			case "1":
				return true;
			case "n":
			case "no":
			case "아니요":
			case "아니오":
			case "false":
			case "0":
				return false;
			}
			Console.WriteLine("Y 또는 N으로 입력해 주세요.");
		}
		return defaultValue;
	}

	private static void PrintSettingsSummary(ServerSettings settings)
	{
		Console.WriteLine();
		Console.WriteLine("[설정 요약]");
		Console.WriteLine("서버 프로필: " + settings.ProfileName);
		Console.WriteLine("서버 종류: " + GetServerTypeDisplayName(settings.ServerType));
		Console.WriteLine("Minecraft 버전: " + settings.MinecraftVersion);
		Console.WriteLine("스냅샷 표시: " + ToKoreanBoolean(settings.IncludeSnapshots));
		Console.WriteLine("직접 JAR: " + ToKoreanBoolean(settings.UseManualJar));
		Console.WriteLine("프리셋: " + settings.PresetName);
		Console.WriteLine("게임 모드: " + GameModeToKorean(settings.GameMode));
		Console.WriteLine("난이도: " + DifficultyToKorean(settings.Difficulty));
		Console.WriteLine("월드 유형: " + LevelTypeToKorean(settings.LevelType));
		Console.WriteLine("최대 인원: " + settings.MaxPlayers);
		Console.WriteLine("서버 이름: " + settings.Motd);
		Console.WriteLine("포트: " + settings.ServerPort);
		Console.WriteLine("PvP: " + ToKoreanBoolean(settings.Pvp));
		Console.WriteLine("화이트리스트: " + ToKoreanBoolean(settings.WhiteList));
		Console.WriteLine("하드코어: " + ToKoreanBoolean(settings.Hardcore));
		Console.WriteLine("시야 거리: " + settings.ViewDistance);
		Console.WriteLine("시뮬레이션 거리: " + settings.SimulationDistance);
		Console.WriteLine("명령 블록: " + ToKoreanBoolean(settings.CommandBlock));
		Console.WriteLine("온라인 인증: " + ToKoreanBoolean(settings.OnlineMode));
		Console.WriteLine("최대 서버 메모리: " + settings.MemoryGb + "GB");
		Console.WriteLine("서버 자동 업데이트: " + ToKoreanBoolean(settings.AutoUpdate));
		Console.WriteLine("서버 소유자: " + settings.OwnerName);
	}

	private static string GameModeToKorean(string value)
	{
		switch (value)
		{
		case "survival":
			return "서바이벌";
		case "creative":
			return "크리에이티브";
		case "adventure":
			return "모험";
		case "spectator":
			return "관전자";
		default:
			return value;
		}
	}

	private static string DifficultyToKorean(string value)
	{
		switch (value)
		{
		case "peaceful":
			return "평화로움";
		case "easy":
			return "쉬움";
		case "normal":
			return "보통";
		case "hard":
			return "어려움";
		default:
			return value;
		}
	}

	private static string LevelTypeToKorean(string value)
	{
		if (string.Equals(value, "minecraft:flat", StringComparison.OrdinalIgnoreCase))
		{
			return "평지";
		}
		return "일반 지형";
	}

	private static string ToKoreanBoolean(bool value)
	{
		if (!value)
		{
			return "사용 안 함";
		}
		return "사용";
	}

	private static void ApplyServerProperties(string path, ServerSettings settings)
	{
		List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
		list.Add(new KeyValuePair<string, string>("gamemode", settings.GameMode));
		list.Add(new KeyValuePair<string, string>("difficulty", settings.Difficulty));
		list.Add(new KeyValuePair<string, string>("level-type", settings.LevelType));
		list.Add(new KeyValuePair<string, string>("max-players", settings.MaxPlayers.ToString()));
		list.Add(new KeyValuePair<string, string>("motd", EscapePropertyValue(settings.Motd)));
		list.Add(new KeyValuePair<string, string>("server-port", settings.ServerPort.ToString()));
		list.Add(new KeyValuePair<string, string>("pvp", settings.Pvp.ToString().ToLowerInvariant()));
		list.Add(new KeyValuePair<string, string>("white-list", settings.WhiteList.ToString().ToLowerInvariant()));
		list.Add(new KeyValuePair<string, string>("enforce-whitelist", settings.WhiteList.ToString().ToLowerInvariant()));
		list.Add(new KeyValuePair<string, string>("hardcore", settings.Hardcore.ToString().ToLowerInvariant()));
		list.Add(new KeyValuePair<string, string>("view-distance", settings.ViewDistance.ToString()));
		list.Add(new KeyValuePair<string, string>("simulation-distance", settings.SimulationDistance.ToString()));
		list.Add(new KeyValuePair<string, string>("enable-command-block", settings.CommandBlock.ToString().ToLowerInvariant()));
		list.Add(new KeyValuePair<string, string>("online-mode", settings.OnlineMode.ToString().ToLowerInvariant()));
		List<KeyValuePair<string, string>> list2 = list;
		string[] array = (File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8) : new string[1] { "# MineHarbor에서 생성한 설정" });
		List<string> list3 = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] array2 = array;
		string[] array3 = array2;
		string[] array4 = array3;
		string[] array5 = array4;
		string[] array6 = array5;
		foreach (string text in array6)
		{
			string text2 = null;
			foreach (KeyValuePair<string, string> item in list2)
			{
				if (IsPropertyLine(text, item.Key))
				{
					text2 = item.Key;
					if (!hashSet.Contains(item.Key))
					{
						list3.Add(item.Key + "=" + item.Value);
						hashSet.Add(item.Key);
					}
					break;
				}
			}
			if (text2 == null)
			{
				list3.Add(text);
			}
		}
		foreach (KeyValuePair<string, string> item2 in list2)
		{
			if (!hashSet.Contains(item2.Key))
			{
				list3.Add(item2.Key + "=" + item2.Value);
			}
		}
		string text3 = path + ".준비중";
		DeleteFileIfPresent(text3);
		File.WriteAllLines(text3, list3.ToArray(), new UTF8Encoding(false));
		try
		{
			if (File.Exists(path))
			{
				try
				{
					File.Replace(text3, path, null);
					return;
				}
				catch
				{
					File.Copy(text3, path, true);
					File.Delete(text3);
					return;
				}
			}
			File.Move(text3, path);
		}
		finally
		{
			DeleteFileIfPresent(text3);
		}
	}

	private static bool IsPropertyLine(string line, string key)
	{
		string text = line.TrimStart();
		if (text.Length == 0 || text[0] == '#' || text[0] == '!')
		{
			return false;
		}
		if (!text.StartsWith(key, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (text.Length == key.Length)
		{
			return true;
		}
		char c = text[key.Length];
		if (c != '=' && c != ':')
		{
			return char.IsWhiteSpace(c);
		}
		return true;
	}

	private static string EscapePropertyValue(string value)
	{
		return value.Replace("\\", "\\\\").Replace("\r", string.Empty).Replace("\n", "\\n");
	}

	private static void RemoveLegacyBundledOwnerPlugin(string serverDirectory)
	{
		string pluginPath = Path.Combine(Path.Combine(serverDirectory, "plugins"), "ServerOwner.jar");
		try
		{
			FileInfo fileInfo = new FileInfo(pluginPath);
			if (fileInfo.Exists && fileInfo.Length == AdminPluginJarSize && HashMatches(pluginPath, AdminPluginJarSha256))
			{
				fileInfo.Delete();
				Console.WriteLine("서버 종류에 종속된 이전 자동 OP 플러그인을 제거했습니다.");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("이전 자동 OP 플러그인을 정리하지 못했습니다: " + ex.Message);
		}
	}

	private static ServerRuntime PrepareServerRuntime(string serverDirectory, LauncherOptions options, string javaPath, bool forceDownload)
	{
		string serverType = NormalizeServerType(options.ServerType);
		if (options.UseManualJar || string.Equals(serverType, "custom", StringComparison.Ordinal))
		{
			return PrepareManualJarRuntime(serverDirectory, options);
		}
		if (string.Equals(serverType, "forge", StringComparison.Ordinal))
		{
			return PrepareForgeRuntime(serverDirectory, options, javaPath, forceDownload);
		}
		if (string.Equals(serverType, "neoforge", StringComparison.Ordinal))
		{
			return PrepareNeoForgeRuntime(serverDirectory, options, javaPath, forceDownload);
		}
		string jarPath = GetManagedServerJarPath(serverDirectory, options);
		bool preparedLatest = forceDownload || !IsLikelyPaperJar(jarPath);
		if (preparedLatest)
		{
			DownloadManagedServerJar(serverDirectory, options, jarPath);
		}
		ServerRuntime runtime = new ServerRuntime();
		runtime.JarPath = jarPath;
		runtime.PreparedLatest = preparedLatest;
		return runtime;
	}

	private static void UpgradeServerRuntime(string serverDirectory, LauncherOptions options, string javaPath, ServerRuntime runtime, bool forced)
	{
		string serverType = NormalizeServerType(options.ServerType);
		if (options.UseManualJar || string.Equals(serverType, "custom", StringComparison.Ordinal))
		{
			Console.WriteLine("직접 지정한 JAR은 런처가 자동으로 교체하지 않습니다.");
			return;
		}
		if (string.Equals(serverType, "forge", StringComparison.Ordinal))
		{
			Console.WriteLine("Forge 서버 파일을 최신 권장/최신 빌드로 확인합니다.");
			PrepareForgeRuntime(serverDirectory, options, javaPath, true);
			return;
		}
		if (string.Equals(serverType, "neoforge", StringComparison.Ordinal))
		{
			Console.WriteLine("NeoForge 서버 파일을 최신 안정 빌드로 확인합니다.");
			PrepareNeoForgeRuntime(serverDirectory, options, javaPath, true);
			return;
		}
		string jarPath = runtime != null && !string.IsNullOrEmpty(runtime.JarPath) ? runtime.JarPath : GetManagedServerJarPath(serverDirectory, options);
		DownloadManagedServerJar(serverDirectory, options, jarPath);
	}

	private static ServerRuntime PrepareManualJarRuntime(string serverDirectory, LauncherOptions options)
	{
		string sourcePath = options.ManualJarPath;
		string jarPath = Path.Combine(serverDirectory, "server-custom.jar");
		if (!string.IsNullOrWhiteSpace(sourcePath))
		{
			sourcePath = sourcePath.Trim('"', ' ');
		}
		if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
		{
			if (IsLikelyPaperJar(jarPath))
			{
				ServerRuntime existing = new ServerRuntime();
				existing.JarPath = jarPath;
				return existing;
			}
			throw new FileNotFoundException("직접 지정한 서버 JAR 파일을 찾을 수 없습니다.", sourcePath);
		}
		if (!string.Equals(Path.GetExtension(sourcePath), ".jar", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("직접 지정한 서버 파일은 .jar 확장자여야 합니다.");
		}
		string sourceFull = Path.GetFullPath(sourcePath);
		string destinationFull = Path.GetFullPath(jarPath);
		if (!string.Equals(sourceFull, destinationFull, StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("직접 지정한 서버 JAR을 프로필 폴더로 복사하는 중...");
			string temporaryPath = jarPath + ".복사중";
			DeleteFileIfPresent(temporaryPath);
			File.Copy(sourceFull, temporaryPath, true);
			if (!IsLikelyPaperJar(temporaryPath))
			{
				DeleteFileIfPresent(temporaryPath);
				throw new InvalidDataException("지정한 파일이 실행 가능한 JAR 형식이 아닙니다.");
			}
			ReplaceFile(temporaryPath, jarPath);
		}
		ServerRuntime runtime = new ServerRuntime();
		runtime.JarPath = jarPath;
		return runtime;
	}

	private static string GetManagedServerJarPath(string serverDirectory, LauncherOptions options)
	{
		string safeVersion = ToSafeDirectoryName(options.MinecraftVersion).Replace(' ', '-');
		return Path.Combine(serverDirectory, "server-" + NormalizeServerType(options.ServerType) + "-" + safeVersion + ".jar");
	}

	private static void DownloadManagedServerJar(string serverDirectory, LauncherOptions options, string jarPath)
	{
		string serverType = NormalizeServerType(options.ServerType);
		ServerDownloadInfo download = GetServerDownloadInfo(serverType, options.MinecraftVersion);
		if (download == null || string.IsNullOrWhiteSpace(download.Url))
		{
			throw new InvalidDataException(GetServerTypeDisplayName(serverType) + " " + options.MinecraftVersion + " 다운로드 정보를 찾지 못했습니다.");
		}
		string temporaryPath = jarPath + ".다운로드중";
		DeleteFileIfPresent(temporaryPath);
		try
		{
			Console.WriteLine(GetServerTypeDisplayName(serverType) + " " + options.MinecraftVersion + " 서버 파일을 내려받는 중...");
			DownloadFileWithUserAgent(download.Url, temporaryPath, GenericServerUserAgent);
			ValidateDownloadedServerFile(temporaryPath, download);
			if (File.Exists(jarPath))
			{
				string oldHash = GetFileSha256(jarPath);
				string newHash = GetFileSha256(temporaryPath);
				if (string.Equals(oldHash, newHash, StringComparison.OrdinalIgnoreCase))
				{
					DeleteFileIfPresent(temporaryPath);
					Console.WriteLine("현재 서버 파일이 이미 최신입니다.");
					WriteServerBuildMetadata(serverDirectory, options, download, newHash);
					return;
				}
				CreateServerBackup(serverDirectory);
				BackupManagedServerJar(serverDirectory, jarPath, serverType, options.MinecraftVersion);
			}
			ReplaceFile(temporaryPath, jarPath);
			WriteServerBuildMetadata(serverDirectory, options, download, GetFileSha256(jarPath));
			Console.WriteLine("서버 파일 준비 완료: " + Path.GetFileName(jarPath));
		}
		finally
		{
			DeleteFileIfPresent(temporaryPath);
		}
	}

	private static void ValidateDownloadedServerFile(string path, ServerDownloadInfo download)
	{
		FileInfo fileInfo = new FileInfo(path);
		if (!fileInfo.Exists || fileInfo.Length < 1048576)
		{
			throw new InvalidDataException("다운로드한 서버 파일 크기가 비정상적으로 작습니다.");
		}
		if (download.Size > 0 && fileInfo.Length != download.Size)
		{
			throw new InvalidDataException("다운로드 크기가 공식 정보와 다릅니다.");
		}
		if (!string.IsNullOrWhiteSpace(download.Sha256) && !HashMatches(path, download.Sha256))
		{
			throw new InvalidDataException("다운로드한 서버 JAR의 SHA-256 검증에 실패했습니다.");
		}
		if (!string.IsNullOrWhiteSpace(download.Sha1) && !string.Equals(GetFileSha1(path), download.Sha1, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("다운로드한 서버 JAR의 SHA-1 검증에 실패했습니다.");
		}
		if (!IsLikelyPaperJar(path))
		{
			throw new InvalidDataException("다운로드한 파일이 실행 가능한 JAR 형식이 아닙니다.");
		}
	}

	private static ServerDownloadInfo GetServerDownloadInfo(string serverType, string minecraftVersion)
	{
		switch (NormalizeServerType(serverType))
		{
		case "vanilla":
			return GetVanillaServerDownloadInfo(minecraftVersion);
		case "purpur":
			return GetPurpurServerDownloadInfo(minecraftVersion);
		case "fabric":
			return GetFabricServerDownloadInfo(minecraftVersion);
		case "paper":
			PaperBuildInfo paper = GetLatestPaperBuild(minecraftVersion);
			ServerDownloadInfo paperInfo = new ServerDownloadInfo();
			paperInfo.Name = paper.Name;
			paperInfo.Url = paper.Url;
			paperInfo.Sha256 = paper.Sha256;
			paperInfo.Size = paper.Size;
			paperInfo.BuildLabel = "build-" + paper.Build + "-" + paper.Channel;
			return paperInfo;
		default:
			throw new InvalidDataException("지원하지 않는 서버 종류입니다: " + serverType);
		}
	}

	private static ServerDownloadInfo GetVanillaServerDownloadInfo(string minecraftVersion)
	{
		string input = DownloadTextWithUserAgent("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", GenericServerUserAgent);
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(input) as Dictionary<string, object>;
		object[] versions = root != null && root.ContainsKey("versions") ? root["versions"] as object[] : null;
		string versionUrl = null;
		if (versions != null)
		{
			for (int i = 0; i < versions.Length; i = checked(i + 1))
			{
				Dictionary<string, object> item = versions[i] as Dictionary<string, object>;
				if (item != null && item.ContainsKey("id") && item.ContainsKey("url") && string.Equals(Convert.ToString(item["id"]), minecraftVersion, StringComparison.OrdinalIgnoreCase))
				{
					versionUrl = Convert.ToString(item["url"]);
					break;
				}
			}
		}
		if (!IsAllowedDownloadHost(versionUrl, new string[2] { "piston-meta.mojang.com", "launchermeta.mojang.com" }))
		{
			throw new InvalidDataException("Mojang 버전 메타데이터 주소를 검증하지 못했습니다.");
		}
		string versionJson = DownloadTextWithUserAgent(versionUrl, GenericServerUserAgent);
		Dictionary<string, object> versionRoot = new JavaScriptSerializer().DeserializeObject(versionJson) as Dictionary<string, object>;
		Dictionary<string, object> downloads = versionRoot != null && versionRoot.ContainsKey("downloads") ? versionRoot["downloads"] as Dictionary<string, object> : null;
		Dictionary<string, object> server = downloads != null && downloads.ContainsKey("server") ? downloads["server"] as Dictionary<string, object> : null;
		if (server == null || !server.ContainsKey("url"))
		{
			throw new InvalidDataException("Mojang 서버 다운로드 정보를 찾지 못했습니다.");
		}
		ServerDownloadInfo info = new ServerDownloadInfo();
		info.Name = "minecraft-server-" + minecraftVersion + ".jar";
		info.Url = Convert.ToString(server["url"]);
		info.Sha1 = server.ContainsKey("sha1") ? Convert.ToString(server["sha1"]) : string.Empty;
		info.Size = server.ContainsKey("size") ? Convert.ToInt64(server["size"]) : 0L;
		info.BuildLabel = "vanilla-" + minecraftVersion;
		if (!IsAllowedDownloadHost(info.Url, new string[2] { "piston-data.mojang.com", "launcher.mojang.com" }))
		{
			throw new InvalidDataException("Mojang 서버 다운로드 주소를 검증하지 못했습니다.");
		}
		return info;
	}

	private static ServerDownloadInfo GetPurpurServerDownloadInfo(string minecraftVersion)
	{
		string input = DownloadTextWithUserAgent("https://api.purpurmc.org/v2/purpur/" + Uri.EscapeDataString(minecraftVersion), GenericServerUserAgent);
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(input) as Dictionary<string, object>;
		Dictionary<string, object> builds = root != null && root.ContainsKey("builds") ? root["builds"] as Dictionary<string, object> : null;
		string latest = null;
		if (builds != null && builds.ContainsKey("latest"))
		{
			latest = Convert.ToString(builds["latest"]);
		}
		if (string.IsNullOrWhiteSpace(latest))
		{
			throw new InvalidDataException("Purpur 최신 빌드 정보를 찾지 못했습니다.");
		}
		ServerDownloadInfo info = new ServerDownloadInfo();
		info.Name = "purpur-" + minecraftVersion + "-" + latest + ".jar";
		info.Url = "https://api.purpurmc.org/v2/purpur/" + Uri.EscapeDataString(minecraftVersion) + "/" + Uri.EscapeDataString(latest) + "/download";
		info.BuildLabel = "build-" + latest;
		return info;
	}

	private static ServerDownloadInfo GetFabricServerDownloadInfo(string minecraftVersion)
	{
		string loaderInput = DownloadTextWithUserAgent("https://meta.fabricmc.net/v2/versions/loader/" + Uri.EscapeDataString(minecraftVersion), GenericServerUserAgent);
		object[] loaders = new JavaScriptSerializer().DeserializeObject(loaderInput) as object[];
		string loaderVersion = null;
		if (loaders != null)
		{
			for (int i = 0; i < loaders.Length; i = checked(i + 1))
			{
				Dictionary<string, object> entry = loaders[i] as Dictionary<string, object>;
				Dictionary<string, object> loader = entry != null && entry.ContainsKey("loader") ? entry["loader"] as Dictionary<string, object> : null;
				if (loader != null && loader.ContainsKey("version"))
				{
					loaderVersion = Convert.ToString(loader["version"]);
					break;
				}
			}
		}
		if (string.IsNullOrWhiteSpace(loaderVersion))
		{
			throw new InvalidDataException("Fabric Loader 정보를 찾지 못했습니다.");
		}
		string installerInput = DownloadTextWithUserAgent("https://meta.fabricmc.net/v2/versions/installer", GenericServerUserAgent);
		object[] installers = new JavaScriptSerializer().DeserializeObject(installerInput) as object[];
		string installerVersion = null;
		if (installers != null)
		{
			for (int i = 0; i < installers.Length; i = checked(i + 1))
			{
				Dictionary<string, object> installer = installers[i] as Dictionary<string, object>;
				if (installer != null && installer.ContainsKey("version") && (!installer.ContainsKey("stable") || Convert.ToBoolean(installer["stable"])))
				{
					installerVersion = Convert.ToString(installer["version"]);
					break;
				}
			}
			if (installerVersion == null && installers.Length > 0)
			{
				Dictionary<string, object> first = installers[0] as Dictionary<string, object>;
				if (first != null && first.ContainsKey("version"))
				{
					installerVersion = Convert.ToString(first["version"]);
				}
			}
		}
		if (string.IsNullOrWhiteSpace(installerVersion))
		{
			throw new InvalidDataException("Fabric Installer 정보를 찾지 못했습니다.");
		}
		ServerDownloadInfo info = new ServerDownloadInfo();
		info.Name = "fabric-server-" + minecraftVersion + "-" + loaderVersion + ".jar";
		info.Url = "https://meta.fabricmc.net/v2/versions/loader/" + Uri.EscapeDataString(minecraftVersion) + "/" + Uri.EscapeDataString(loaderVersion) + "/" + Uri.EscapeDataString(installerVersion) + "/server/jar";
		info.BuildLabel = "loader-" + loaderVersion + "-installer-" + installerVersion;
		return info;
	}

	private static ServerRuntime PrepareForgeRuntime(string serverDirectory, LauncherOptions options, string javaPath, bool forceInstall)
	{
		string runBat = Path.Combine(serverDirectory, "run.bat");
		if (!forceInstall && File.Exists(runBat))
		{
			ServerRuntime runtime = new ServerRuntime();
			runtime.BatchPath = runBat;
			return runtime;
		}
		string forgeJar = FindForgeServerJar(serverDirectory);
		if (!forceInstall && forgeJar != null)
		{
			ServerRuntime runtime2 = new ServerRuntime();
			runtime2.JarPath = forgeJar;
			return runtime2;
		}
		string forgeVersion = GetForgeBuildVersion(options.MinecraftVersion);
		string installerUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/" + Uri.EscapeDataString(options.MinecraftVersion + "-" + forgeVersion) + "/forge-" + Uri.EscapeDataString(options.MinecraftVersion + "-" + forgeVersion) + "-installer.jar";
		if (!IsAllowedDownloadHost(installerUrl, new string[1] { "maven.minecraftforge.net" }))
		{
			throw new InvalidDataException("Forge 다운로드 주소를 검증하지 못했습니다.");
		}
		string installerPath = Path.Combine(serverDirectory, "forge-installer-" + ToSafeDirectoryName(options.MinecraftVersion + "-" + forgeVersion) + ".jar");
		string temporaryPath = installerPath + ".다운로드중";
		DeleteFileIfPresent(temporaryPath);
		try
		{
			Console.WriteLine("Forge Installer를 내려받는 중...");
			string[] allowedHosts = new string[1] { "maven.minecraftforge.net" };
			DownloadFileWithUserAgent(installerUrl, temporaryPath, GenericServerUserAgent, allowedHosts);
			string checksumText = DownloadTextWithUserAgent(installerUrl + ".sha256", GenericServerUserAgent).Trim();
			string expectedSha256 = checksumText.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
			if (expectedSha256.Length != 64 || !HashMatches(temporaryPath, expectedSha256))
			{
				throw new InvalidDataException("Forge Installer의 SHA-256 검증에 실패했습니다.");
			}
			ReplaceFile(temporaryPath, installerPath);
		}
		finally
		{
			DeleteFileIfPresent(temporaryPath);
		}
		Console.WriteLine("Forge 서버 설치를 실행합니다. 네트워크 상태에 따라 시간이 걸릴 수 있습니다.");
		RunForgeInstaller(javaPath, installerPath, serverDirectory);
		if (File.Exists(runBat))
		{
			ServerRuntime runtime3 = new ServerRuntime();
			runtime3.BatchPath = runBat;
			return runtime3;
		}
		forgeJar = FindForgeServerJar(serverDirectory);
		if (forgeJar != null)
		{
			ServerRuntime runtime4 = new ServerRuntime();
			runtime4.JarPath = forgeJar;
			return runtime4;
		}
		throw new InvalidDataException("Forge 설치는 끝났지만 실행 파일(run.bat 또는 forge server jar)을 찾지 못했습니다. 프로필 폴더의 Forge 설치 로그를 확인하세요.");
	}

	private static ServerRuntime PrepareNeoForgeRuntime(string serverDirectory, LauncherOptions options, string javaPath, bool forceInstall)
	{
		string runBat = Path.Combine(serverDirectory, "run.bat");
		if (!forceInstall && File.Exists(runBat))
		{
			ServerRuntime existing = new ServerRuntime();
			existing.BatchPath = runBat;
			return existing;
		}
		string neoForgeVersion = GetNeoForgeBuildVersion(options.MinecraftVersion);
		string fileName = "neoforge-" + neoForgeVersion + "-installer.jar";
		string installerUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/" + Uri.EscapeDataString(neoForgeVersion) + "/" + fileName;
		if (!IsAllowedDownloadHost(installerUrl, new string[1] { "maven.neoforged.net" }))
		{
			throw new InvalidDataException("NeoForge 다운로드 주소를 검증하지 못했습니다.");
		}
		string installerPath = Path.Combine(serverDirectory, fileName);
		string temporaryPath = installerPath + ".다운로드중";
		DeleteFileIfPresent(temporaryPath);
		try
		{
			Console.WriteLine("NeoForge Installer " + neoForgeVersion + "을 내려받는 중...");
			DownloadFileWithUserAgent(installerUrl, temporaryPath, GetLauncherIntegrationUserAgent());
			string checksumText = DownloadTextWithUserAgent(installerUrl + ".sha256", GetLauncherIntegrationUserAgent()).Trim();
			string expectedSha256 = checksumText.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
			if (expectedSha256.Length != 64 || !HashMatches(temporaryPath, expectedSha256))
			{
				throw new InvalidDataException("NeoForge Installer의 SHA-256 검증에 실패했습니다.");
			}
			ReplaceFile(temporaryPath, installerPath);
		}
		finally
		{
			DeleteFileIfPresent(temporaryPath);
		}
		Console.WriteLine("NeoForge 서버 설치를 실행합니다. 네트워크 상태에 따라 시간이 걸릴 수 있습니다.");
		RunForgeInstaller(javaPath, installerPath, serverDirectory);
		if (!File.Exists(runBat))
		{
			throw new InvalidDataException("NeoForge 설치는 끝났지만 run.bat 파일을 찾지 못했습니다. 프로필 폴더의 설치 로그를 확인하세요.");
		}
		ServerRuntime runtime = new ServerRuntime();
		runtime.BatchPath = runBat;
		return runtime;
	}

	private static string GetNeoForgeBuildVersion(string minecraftVersion)
	{
		string metadata = DownloadTextWithUserAgent("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", GetLauncherIntegrationUserAgent());
		string prefix = minecraftVersion != null && minecraftVersion.StartsWith("1.", StringComparison.Ordinal) ? minecraftVersion.Substring(2) + "." : (minecraftVersion ?? string.Empty) + ".";
		System.Text.RegularExpressions.MatchCollection matches = System.Text.RegularExpressions.Regex.Matches(metadata, "<version>([^<]+)</version>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		string latestStable = null;
		string latestAny = null;
		for (int i = 0; i < matches.Count; i = checked(i + 1))
		{
			string version = matches[i].Groups[1].Value.Trim();
			if (!version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			latestAny = version;
			if (version.IndexOf("beta", StringComparison.OrdinalIgnoreCase) < 0 && version.IndexOf("alpha", StringComparison.OrdinalIgnoreCase) < 0)
			{
				latestStable = version;
			}
		}
		string selected = latestStable ?? latestAny;
		if (string.IsNullOrWhiteSpace(selected))
		{
			throw new InvalidDataException("선택한 Minecraft 버전에 맞는 NeoForge 빌드를 찾지 못했습니다.");
		}
		if (latestStable == null)
		{
			Console.WriteLine("안정 NeoForge 빌드가 없어 최신 시험 빌드 " + selected + "을 사용합니다.");
		}
		return selected;
	}

	private static string GetForgeBuildVersion(string minecraftVersion)
	{
		string input = DownloadTextWithUserAgent("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", GenericServerUserAgent);
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(input) as Dictionary<string, object>;
		Dictionary<string, object> promotions = root != null && root.ContainsKey("promos") ? root["promos"] as Dictionary<string, object> : null;
		if (promotions == null)
		{
			throw new InvalidDataException("Forge 빌드 정보를 찾지 못했습니다.");
		}
		string recommendedKey = minecraftVersion + "-recommended";
		string latestKey = minecraftVersion + "-latest";
		if (promotions.ContainsKey(recommendedKey))
		{
			return Convert.ToString(promotions[recommendedKey]);
		}
		if (promotions.ContainsKey(latestKey))
		{
			return Convert.ToString(promotions[latestKey]);
		}
		throw new InvalidDataException("선택한 Minecraft 버전의 Forge 빌드를 찾지 못했습니다.");
	}

	private static void RunForgeInstaller(string javaPath, string installerPath, string serverDirectory)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo();
		processStartInfo.FileName = javaPath;
		processStartInfo.Arguments = "-jar \"" + installerPath + "\" --installServer";
		processStartInfo.WorkingDirectory = serverDirectory;
		processStartInfo.UseShellExecute = false;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		using (Process process = Process.Start(processStartInfo))
		{
			process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
			{
				if (eventArgs.Data != null)
				{
					Console.WriteLine(eventArgs.Data);
				}
			};
			process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
			{
				if (eventArgs.Data != null)
				{
					Console.WriteLine(eventArgs.Data);
				}
			};
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			if (!process.WaitForExit(600000))
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
				throw new TimeoutException("Forge 설치가 10분 안에 끝나지 않았습니다.");
			}
			if (process.ExitCode != 0)
			{
				throw new InvalidDataException("Forge Installer가 오류 코드 " + process.ExitCode + "로 종료되었습니다.");
			}
		}
	}

	private static string FindForgeServerJar(string serverDirectory)
	{
		if (!Directory.Exists(serverDirectory))
		{
			return null;
		}
		string[] files = Directory.GetFiles(serverDirectory, "forge-*.jar", SearchOption.TopDirectoryOnly);
		for (int i = 0; i < files.Length; i = checked(i + 1))
		{
			string name = Path.GetFileName(files[i]);
			if (name.IndexOf("installer", StringComparison.OrdinalIgnoreCase) < 0 && IsLikelyPaperJar(files[i]))
			{
				return files[i];
			}
		}
		return null;
	}

	private static bool IsAllowedDownloadHost(string url, string[] allowedHosts)
	{
		Uri result;
		if (!Uri.TryCreate(url, UriKind.Absolute, out result) || result.Scheme != Uri.UriSchemeHttps)
		{
			return false;
		}
		for (int i = 0; i < allowedHosts.Length; i = checked(i + 1))
		{
			if (result.Host.Equals(allowedHosts[i], StringComparison.OrdinalIgnoreCase) || result.Host.EndsWith("." + allowedHosts[i], StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void DownloadFileWithUserAgent(string url, string destinationPath, string userAgent)
	{
		Uri source = new Uri(url);
		DownloadFileWithUserAgent(url, destinationPath, userAgent, new string[1] { source.Host });
	}

	private static void DownloadFileWithUserAgent(string url, string destinationPath, string userAgent, string[] allowedHosts)
	{
		Uri current = new Uri(url);
		for (int redirect = 0; redirect <= 5; redirect++)
		{
			if (!IsAllowedDownloadHost(current.AbsoluteUri, allowedHosts)) throw new InvalidDataException("다운로드 호스트를 검증하지 못했습니다.");
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(current);
			httpWebRequest.Method = "GET";
			httpWebRequest.UserAgent = userAgent;
			httpWebRequest.Timeout = 60000;
			httpWebRequest.ReadWriteTimeout = 60000;
			httpWebRequest.AllowAutoRedirect = false;
			httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
			{
				if (httpWebResponse.StatusCode == HttpStatusCode.MovedPermanently || httpWebResponse.StatusCode == HttpStatusCode.Redirect || httpWebResponse.StatusCode == HttpStatusCode.RedirectMethod || httpWebResponse.StatusCode == HttpStatusCode.TemporaryRedirect)
				{
					string location = httpWebResponse.Headers[HttpResponseHeader.Location];
					Uri next;
					if (string.IsNullOrWhiteSpace(location) || !Uri.TryCreate(current, location, out next) || !IsAllowedDownloadHost(next.AbsoluteUri, allowedHosts)) throw new InvalidDataException("허용되지 않은 다운로드 리디렉션입니다.");
					current = next;
					continue;
				}
				if (httpWebResponse.StatusCode != HttpStatusCode.OK) throw new WebException("HTTP " + (int)httpWebResponse.StatusCode);
				if (httpWebResponse.ContentLength > 536870912L) throw new InvalidDataException("다운로드 파일이 안전 크기 제한을 초과했습니다.");
				using (Stream stream = httpWebResponse.GetResponseStream())
				using (FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					byte[] buffer = new byte[131072];
					long total = 0L;
					int read;
					while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
					{
						total = checked(total + read);
						if (total > 536870912L) throw new InvalidDataException("다운로드 파일이 안전 크기 제한을 초과했습니다.");
						fileStream.Write(buffer, 0, read);
					}
					fileStream.Flush(true);
				}
				return;
			}
		}
		throw new InvalidDataException("다운로드 리디렉션 횟수가 안전 제한을 초과했습니다.");
	}

	private static void BackupManagedServerJar(string serverDirectory, string jarPath, string serverType, string minecraftVersion)
	{
		if (!File.Exists(jarPath))
		{
			return;
		}
		string backupDirectory = Path.Combine(serverDirectory, "server-jar-backups");
		Directory.CreateDirectory(backupDirectory);
		string name = "server-" + serverType + "-" + ToSafeDirectoryName(minecraftVersion) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jar";
		File.Copy(jarPath, Path.Combine(backupDirectory, name), false);
		FileInfo[] files = new DirectoryInfo(backupDirectory).GetFiles("server-*.jar");
		Array.Sort(files, (FileInfo left, FileInfo right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));
		for (int i = 5; i < files.Length; i = checked(i + 1))
		{
			files[i].Delete();
		}
	}

	private static void WriteServerBuildMetadata(string serverDirectory, LauncherOptions options, ServerDownloadInfo download, string sha256)
	{
		string contents = "server-type=" + NormalizeServerType(options.ServerType) + "\r\nminecraft-version=" + options.MinecraftVersion + "\r\nname=" + download.Name + "\r\nbuild=" + download.BuildLabel + "\r\nurl=" + download.Url + "\r\nsha256=" + sha256 + "\r\n";
		File.WriteAllText(Path.Combine(serverDirectory, ".server-launcher-build"), contents, new UTF8Encoding(false));
	}

	private static string GetFileSha1(string path)
	{
		using (FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			using (SHA1 sHA = SHA1.Create())
			{
				byte[] array = sHA.ComputeHash(inputStream);
				StringBuilder stringBuilder = new StringBuilder(checked(array.Length * 2));
				for (int i = 0; i < array.Length; i = checked(i + 1))
				{
					stringBuilder.Append(array[i].ToString("x2"));
				}
				return stringBuilder.ToString();
			}
		}
	}

	private static bool IsLikelyPaperJar(string path)
	{
		try
		{
			FileInfo fileInfo = new FileInfo(path);
			if (!fileInfo.Exists || fileInfo.Length < 10485760)
			{
				return false;
			}
			using (ZipArchive zipArchive = ZipFile.OpenRead(path))
			{
				return zipArchive.GetEntry("META-INF/MANIFEST.MF") != null;
			}
		}
		catch
		{
			return false;
		}
	}

	private static PaperBuildInfo GetLatestPaperBuild(string minecraftVersion)
	{
		string input = DownloadTextWithUserAgent("https://fill.papermc.io/v3/projects/paper/versions/" + Uri.EscapeDataString(minecraftVersion) + "/builds", GetLauncherIntegrationUserAgent());
		object[] array = new JavaScriptSerializer().DeserializeObject(input) as object[];
		if (array == null || array.Length == 0)
		{
			throw new InvalidDataException("PaperMC API에서 " + minecraftVersion + " 빌드 정보를 찾지 못했습니다.");
		}
		PaperBuildInfo paperBuildInfo = null;
		PaperBuildInfo fallbackBuildInfo = null;
		object[] array2 = array;
		object[] array3 = array2;
		object[] array4 = array3;
		object[] array5 = array4;
		foreach (object obj in array5)
		{
			Dictionary<string, object> dictionary = obj as Dictionary<string, object>;
			if (dictionary == null || !dictionary.ContainsKey("id") || !dictionary.ContainsKey("downloads"))
			{
				continue;
			}
			string channel = dictionary.ContainsKey("channel") ? Convert.ToString(dictionary["channel"]) : "UNKNOWN";
			int num = Convert.ToInt32(dictionary["id"]);
			Dictionary<string, object> dictionary2 = dictionary["downloads"] as Dictionary<string, object>;
			if (dictionary2 != null && dictionary2.ContainsKey("server:default"))
			{
				Dictionary<string, object> dictionary3 = dictionary2["server:default"] as Dictionary<string, object>;
				Dictionary<string, object> dictionary4 = ((dictionary3 != null && dictionary3.ContainsKey("checksums")) ? (dictionary3["checksums"] as Dictionary<string, object>) : null);
				if (dictionary3 != null && dictionary4 != null && dictionary3.ContainsKey("url") && dictionary4.ContainsKey("sha256"))
				{
					PaperBuildInfo candidate = new PaperBuildInfo();
					candidate.Build = num;
					candidate.Channel = channel;
					candidate.Name = (dictionary3.ContainsKey("name") ? Convert.ToString(dictionary3["name"]) : ("paper-" + minecraftVersion + "-" + num + ".jar"));
					candidate.Url = Convert.ToString(dictionary3["url"]);
					candidate.Sha256 = Convert.ToString(dictionary4["sha256"]);
					candidate.Size = (dictionary3.ContainsKey("size") ? Convert.ToInt64(dictionary3["size"]) : 0);
					if (fallbackBuildInfo == null || num > fallbackBuildInfo.Build)
					{
						fallbackBuildInfo = candidate;
					}
					if (string.Equals(channel, "STABLE", StringComparison.OrdinalIgnoreCase) && (paperBuildInfo == null || num > paperBuildInfo.Build))
					{
						paperBuildInfo = candidate;
					}
				}
			}
		}
		if (paperBuildInfo == null && fallbackBuildInfo != null)
		{
			paperBuildInfo = fallbackBuildInfo;
			Console.WriteLine("Paper " + minecraftVersion + "에는 안정(STABLE) 빌드가 없어 최신 " + paperBuildInfo.Channel + " 빌드 " + paperBuildInfo.Build + "을 사용합니다.");
		}
		if (paperBuildInfo == null || !IsAllowedPaperDownloadUrl(paperBuildInfo.Url) || paperBuildInfo.Sha256.Length != 64)
		{
			throw new InvalidDataException("PaperMC API의 다운로드 정보를 검증하지 못했습니다.");
		}
		return paperBuildInfo;
	}

	private static bool IsAllowedPaperDownloadUrl(string url)
	{
		Uri result;
		if (Uri.TryCreate(url, UriKind.Absolute, out result) && result.Scheme == Uri.UriSchemeHttps)
		{
			if (!result.Host.Equals("fill-data.papermc.io", StringComparison.OrdinalIgnoreCase))
			{
				return result.Host.EndsWith(".papermc.io", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	private static void CreateServerBackup(string serverDirectory)
	{
		CreateComprehensiveServerBackup(serverDirectory, ReadBackupRetentionCount(serverDirectory), "automatic");
	}

	private static void CreateLegacyServerBackup(string serverDirectory)
	{
		List<string> directories = new List<string>();
		foreach (string name in new string[2] { "plugins", "config" })
		{
			string path = Path.Combine(serverDirectory, name);
			if (Directory.Exists(path))
			{
				directories.Add(path);
			}
		}
		foreach (string path2 in Directory.GetDirectories(serverDirectory, "*", SearchOption.TopDirectoryOnly))
		{
			if (File.Exists(Path.Combine(path2, "level.dat")) && !directories.Contains(path2))
			{
				directories.Add(path2);
			}
		}
		List<string> files = new List<string>();
		foreach (string name2 in new string[9] { "server.properties", "server-icon.png", "bukkit.yml", "spigot.yml", "permissions.yml", "ops.json", "whitelist.json", "banned-ips.json", "banned-players.json" })
		{
			string path3 = Path.Combine(serverDirectory, name2);
			if (File.Exists(path3))
			{
				files.Add(path3);
			}
		}
		if (directories.Count == 0 && files.Count == 0)
		{
			return;
		}
		long totalSize = 0L;
		foreach (string directory in directories)
		{
			totalSize = checked(totalSize + GetBackupDirectorySize(directory));
		}
		foreach (string file in files)
		{
			totalSize = checked(totalSize + new FileInfo(file).Length);
		}
		string backupDirectory = Path.Combine(serverDirectory, "server-backups");
		Directory.CreateDirectory(backupDirectory);
		long requiredSpace = checked(totalSize + 104857600L);
		DriveInfo driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(backupDirectory)));
		if (driveInfo.AvailableFreeSpace < requiredSpace)
		{
			throw new IOException("서버 백업에 필요한 여유 공간이 부족합니다. 최소 " + FormatByteSize(requiredSpace) + "가 필요합니다.");
		}
		string backupPath = Path.Combine(backupDirectory, "server-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".zip");
		string temporaryPath = backupPath + ".준비중";
		Console.WriteLine("서버 변경 전 백업을 만드는 중...");
		try
		{
			using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
			using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				foreach (string directory2 in directories)
				{
					AddDirectoryToBackup(archive, serverDirectory, directory2);
				}
				foreach (string file2 in files)
				{
					AddFileToBackup(archive, serverDirectory, file2);
				}
			}
			File.Move(temporaryPath, backupPath);
		}
		finally
		{
			DeleteFileIfPresent(temporaryPath);
		}
		FileInfo[] backupFiles = new DirectoryInfo(backupDirectory).GetFiles("server-*.zip");
		Array.Sort(backupFiles, (FileInfo left, FileInfo right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));
		for (int i = 3; i < backupFiles.Length; i = checked(i + 1))
		{
			backupFiles[i].Delete();
		}
		Console.WriteLine("서버 백업 완료: " + backupPath);
	}

	private static long GetBackupDirectorySize(string directory)
	{
		long size = 0L;
		foreach (string file in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
		{
			FileInfo fileInfo = new FileInfo(file);
			if ((fileInfo.Attributes & FileAttributes.ReparsePoint) == 0)
			{
				size = checked(size + fileInfo.Length);
			}
		}
		foreach (string childDirectory in Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly))
		{
			DirectoryInfo directoryInfo = new DirectoryInfo(childDirectory);
			if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) == 0)
			{
				size = checked(size + GetBackupDirectorySize(childDirectory));
			}
		}
		return size;
	}

	private static void AddDirectoryToBackup(ZipArchive archive, string serverDirectory, string directory)
	{
		foreach (string file in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
		{
			FileInfo fileInfo = new FileInfo(file);
			if ((fileInfo.Attributes & FileAttributes.ReparsePoint) == 0)
			{
				AddFileToBackup(archive, serverDirectory, file);
			}
		}
		foreach (string childDirectory in Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly))
		{
			DirectoryInfo directoryInfo = new DirectoryInfo(childDirectory);
			if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) == 0)
			{
				AddDirectoryToBackup(archive, serverDirectory, childDirectory);
			}
		}
	}

	private static void AddFileToBackup(ZipArchive archive, string serverDirectory, string filePath)
	{
		string serverRoot = Path.GetFullPath(serverDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(filePath);
		if (!fullPath.StartsWith(serverRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("백업 대상이 서버 폴더 밖을 가리킵니다.");
		}
		string entryName = fullPath.Substring(serverRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
		ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
		using (Stream destination = entry.Open())
		using (FileStream source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
		{
			source.CopyTo(destination, 1048576);
		}
	}

	private static string FormatByteSize(long bytes)
	{
		double value = bytes;
		string[] units = new string[4] { "B", "KB", "MB", "GB" };
		int unit = 0;
		while (value >= 1024.0 && unit < units.Length - 1)
		{
			value /= 1024.0;
			unit = checked(unit + 1);
		}
		return value.ToString("0.##") + units[unit];
	}

	private static void BackupConfigurationFile(string path)
	{
		if (!File.Exists(path))
		{
			return;
		}
		string backupDirectory = Path.Combine(Path.GetDirectoryName(path), "configuration-backups");
		Directory.CreateDirectory(backupDirectory);
		string backupPath = Path.Combine(backupDirectory, "server.properties-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak");
		File.Copy(path, backupPath, false);
		FileInfo[] files = new DirectoryInfo(backupDirectory).GetFiles("server.properties-*.bak");
		Array.Sort(files, (FileInfo left, FileInfo right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));
		for (int i = 5; i < files.Length; i = checked(i + 1))
		{
			files[i].Delete();
		}
	}

	private static void ReplaceFile(string sourcePath, string destinationPath)
	{
		if (File.Exists(destinationPath))
		{
			try
			{
				File.Replace(sourcePath, destinationPath, null);
				return;
			}
			catch
			{
				File.Copy(sourcePath, destinationPath, true);
				File.Delete(sourcePath);
				return;
			}
		}
		File.Move(sourcePath, destinationPath);
	}

	private static string GetFileSha256(string path)
	{
		using (FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			using (SHA256 sHA = SHA256.Create())
			{
				byte[] array = sHA.ComputeHash(inputStream);
				StringBuilder stringBuilder = new StringBuilder(checked(array.Length * 2));
				byte[] array2 = array;
				byte[] array3 = array2;
				byte[] array4 = array3;
				byte[] array5 = array4;
				foreach (byte b in array5)
				{
					stringBuilder.Append(b.ToString("x2"));
				}
				return stringBuilder.ToString();
			}
		}
	}

	private static string DownloadTextWithUserAgent(string url, string userAgent)
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
		httpWebRequest.Method = "GET";
		httpWebRequest.UserAgent = userAgent;
		httpWebRequest.Timeout = 15000;
		httpWebRequest.ReadWriteTimeout = 15000;
		httpWebRequest.AllowAutoRedirect = false;
		httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
		{
			if (httpWebResponse.ContentLength > 8388608L) throw new InvalidDataException("응답이 안전 크기 제한을 초과했습니다.");
			using (Stream stream = httpWebResponse.GetResponseStream())
			{
				using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8))
				{
					if (httpWebResponse.StatusCode != HttpStatusCode.OK)
					{
						throw new WebException("HTTP " + (int)httpWebResponse.StatusCode);
					}
					return ReadLimitedText(streamReader, 8388608);
				}
			}
		}
	}

	private static bool HashMatches(string path, string expectedHash)
	{
		using (FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			using (SHA256 sHA = SHA256.Create())
			{
				byte[] array = sHA.ComputeHash(inputStream);
				StringBuilder stringBuilder = new StringBuilder(checked(array.Length * 2));
				byte[] array2 = array;
				byte[] array3 = array2;
				byte[] array4 = array3;
				byte[] array5 = array4;
				byte[] array6 = array5;
				foreach (byte b in array6)
				{
					stringBuilder.Append(b.ToString("x2"));
				}
				return string.Equals(stringBuilder.ToString(), expectedHash, StringComparison.OrdinalIgnoreCase);
			}
		}
	}

	private static bool EulaIsAccepted(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return false;
			}
			string[] lines = File.ReadAllLines(path, Encoding.UTF8);
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("!", StringComparison.Ordinal))
				{
					continue;
				}
				int separator = line.IndexOf('=');
				if (separator > 0 && string.Equals(line.Substring(0, separator).Trim(), "eula", StringComparison.OrdinalIgnoreCase))
				{
					bool accepted;
					return bool.TryParse(line.Substring(separator + 1).Trim(), out accepted) && accepted;
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private static bool AskForEula(string path)
	{
		if (IsGuiMode())
		{
			bool accepted = AskForEulaGui();
			if (!accepted)
			{
				Console.WriteLine("EULA에 동의하지 않아 서버를 실행하지 않습니다.");
				return false;
			}
			File.WriteAllText(path, "eula=true\r\n", new UTF8Encoding(false));
			return true;
		}
		Console.WriteLine();
		Console.WriteLine("Minecraft 서버를 실행하려면 Minecraft EULA에 동의해야 합니다.");
		Console.WriteLine("EULA: https://aka.ms/MinecraftEULA");
		Console.Write("위 약관을 읽고 동의한다면 '동의' 또는 'Y'를 입력하세요: ");
		string a = Console.ReadLine();
		if (!string.Equals(a, "동의", StringComparison.OrdinalIgnoreCase) && !string.Equals(a, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(a, "yes", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("EULA에 동의하지 않아 서버를 실행하지 않습니다.");
			return false;
		}
		File.WriteAllText(path, "eula=true\r\n", new UTF8Encoding(false));
		return true;
	}

	private static string FindJava(string runtimeDirectory)
	{
		if (!Directory.Exists(runtimeDirectory))
		{
			return null;
		}
		try
		{
			string markerPath = Path.Combine(runtimeDirectory, ".launcher-java-sha256");
			if (!File.Exists(markerPath) || !string.Equals(File.ReadAllText(markerPath, Encoding.ASCII).Trim(), "3404a8be08f0fdbbd24c9bbdda79ba1ded87b264a833247b2124ac45da1c16e0", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			string[] files = Directory.GetFiles(runtimeDirectory, "java.exe", SearchOption.AllDirectories);
			string[] array = files;
			string[] array2 = array;
			string[] array3 = array2;
			string[] array4 = array3;
			foreach (string text in array4)
			{
				if (string.Equals(Path.GetFileName(Path.GetDirectoryName(text)), "bin", StringComparison.OrdinalIgnoreCase))
				{
					return text;
				}
			}
		}
		catch
		{
			return null;
		}
		return null;
	}

	private static int ChooseMaximumMemoryGb()
	{
		MemoryStatusEx memoryStatusEx = new MemoryStatusEx();
		if (!GlobalMemoryStatusEx(memoryStatusEx))
		{
			return 4;
		}
		ulong num = memoryStatusEx.TotalPhysical / 1073741824;
		if ((long)num <= 23L && (long)num >= 0L)
		{
			ulong num2 = num;
			if ((long)num2 <= 23L && (long)num2 >= 0L)
			{
				ulong num3 = num2;
				if ((long)num3 <= 23L && (long)num3 >= 0L)
				{
					ulong num4 = num3;
					if ((long)num4 <= 23L && (long)num4 >= 0L)
					{
						switch (num4)
						{
						case 16uL:
						case 17uL:
						case 18uL:
						case 19uL:
						case 20uL:
						case 21uL:
						case 22uL:
						case 23uL:
							return 6;
						case 8uL:
						case 9uL:
						case 10uL:
						case 11uL:
						case 12uL:
						case 13uL:
						case 14uL:
						case 15uL:
							return 4;
						case 6uL:
						case 7uL:
							return 3;
						case 0uL:
						case 1uL:
						case 2uL:
						case 3uL:
						case 4uL:
						case 5uL:
							return 2;
						}
					}
				}
			}
		}
		return 8;
	}

	private static int GetTotalPhysicalMemoryGb()
	{
		MemoryStatusEx memoryStatusEx = new MemoryStatusEx();
		if (!GlobalMemoryStatusEx(memoryStatusEx))
		{
			return 8;
		}
		ulong num = memoryStatusEx.TotalPhysical / 1073741824;
		if (num < 2)
		{
			return 2;
		}
		if (num > 1024)
		{
			return 1024;
		}
		return checked((int)num);
	}

	private static int GetSafeMemoryMaximumGb()
	{
		int totalPhysicalMemoryGb = GetTotalPhysicalMemoryGb();
		return Math.Max(2, checked(totalPhysicalMemoryGb - 2));
	}

	private static int ReadConfiguredServerPort(string propertiesPath, int defaultPort)
	{
		try
		{
			if (!File.Exists(propertiesPath))
			{
				return defaultPort;
			}
			string[] array = File.ReadAllLines(propertiesPath, Encoding.UTF8);
			string[] array2 = array;
			string[] array3 = array2;
			string[] array4 = array3;
			string[] array5 = array4;
			foreach (string text in array5)
			{
				if (IsPropertyLine(text, "server-port"))
				{
					int num = text.IndexOf('=');
					if (num < 0)
					{
						num = text.IndexOf(':');
					}
					int result;
					if (num >= 0 && int.TryParse(text.Substring(checked(num + 1)).Trim(), out result) && result >= 1 && result <= 65535)
					{
						return result;
					}
				}
			}
			return defaultPort;
		}
		catch
		{
			return defaultPort;
		}
	}

	private static int LaunchServer(string javaPath, string jarPath, string batchPath, string workingDirectory, int memoryGb, int serverPort, string ownerName, bool onlineMode, string serverType, string minecraftVersion)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo();
		if (!string.IsNullOrEmpty(batchPath))
		{
			ApplyBatchRuntimeMemory(workingDirectory, memoryGb);
			processStartInfo.FileName = "cmd.exe";
			processStartInfo.Arguments = "/d /s /c \"\"%MCSL_BATCH_PATH%\"\"";
			processStartInfo.EnvironmentVariables["MCSL_BATCH_PATH"] = batchPath;
		}
		else
		{
			processStartInfo.FileName = javaPath;
			string launchJarPath = GetLaunchJarPath(jarPath, workingDirectory);
			processStartInfo.Arguments = "-Xms1G -Xmx" + memoryGb + "G -jar \"" + launchJarPath + "\"" + GetServerConsoleArgument(serverType, minecraftVersion);
		}
		processStartInfo.WorkingDirectory = workingDirectory;
		processStartInfo.UseShellExecute = false;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.RedirectStandardInput = true;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		ProcessStartInfo startInfo = processStartInfo;

		using (Process process = Process.Start(startInfo))
		{
			SetCurrentServerProcess(process);

			// Job Object를 자식 서버 프로세스에 할당하여 런처 종료 시 고아 프로세스를 방지합니다.
			int errorCode;
			if (!ChildProcessTracker.TryAssignProcess(process, out errorCode))
			{
				Console.WriteLine("[경고] 자식 프로세스를 Job Object에 등록하지 못했습니다. 런처 비정상 종료 시 서버가 남을 수 있습니다. (Win32 Error: " + errorCode + ")");
			}

			try 
			{ 
				Console.WriteLine("[Launcher] child-pid=" + process.Id + "," + process.StartTime.Ticks); 
			} 
			catch (Exception ex) { Console.WriteLine("[Launcher] child-pid 기록 실패: " + ex.Message); }

			int ownerCommandSent = 0;
			int serverReachedReadyState = 0;
			int unsupportedArgumentDetected = 0;
			process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
			{
				if (eventArgs.Data != null)
				{
					Console.WriteLine(eventArgs.Data);
					if (eventArgs.Data.IndexOf("Done (", StringComparison.OrdinalIgnoreCase) >= 0 || eventArgs.Data.IndexOf("For help, type", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						Interlocked.Exchange(ref serverReachedReadyState, 1);
					}
					if (eventArgs.Data.IndexOf("not a recognized option", StringComparison.OrdinalIgnoreCase) >= 0 || eventArgs.Data.IndexOf("Unrecognized option", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						Interlocked.Exchange(ref unsupportedArgumentDetected, 1);
					}
					if (onlineMode && IsValidOwnerName(ownerName) && eventArgs.Data.IndexOf(ownerName + " joined the game", StringComparison.OrdinalIgnoreCase) >= 0 && Interlocked.CompareExchange(ref ownerCommandSent, 1, 0) == 0)
					{
						if (SendServerCommand("op " + ownerName))
						{
							Console.WriteLine("서버 소유자에게 OP 권한을 적용했습니다.");
						}
						else
						{
							Interlocked.Exchange(ref ownerCommandSent, 0);
						}
					}
				}
			};
			process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
			{
				if (eventArgs.Data != null)
				{
					Console.WriteLine(eventArgs.Data);
					if (eventArgs.Data.IndexOf("not a recognized option", StringComparison.OrdinalIgnoreCase) >= 0 || eventArgs.Data.IndexOf("Unrecognized option", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						Interlocked.Exchange(ref unsupportedArgumentDetected, 1);
					}
				}
			};
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			NotifyServerStarted(serverPort);
			ManualResetEvent serverStopped = new ManualResetEvent(false);
			Thread thread = new Thread((ThreadStart)delegate
			{
				try
				{
					RunExternalAccessPipeline(serverPort, javaPath, workingDirectory, serverStopped);
				}
				catch (Exception exception)
				{
					Console.WriteLine("[외부 접속] 자동 처리 오류: " + exception.Message);
					ShowLauncherNotice(LauncherUiText("UPnP 매핑 실패", "UPnP mapping failed"), true);
				}
			});
			thread.IsBackground = true;
			thread.Name = "외부 접속 확인";
			ConfigureExternalAccessThread(thread);
			thread.Start();
			process.WaitForExit();
			serverStopped.Set();
			bool externalThreadCompleted = thread.Join(45000);
			if (!externalThreadCompleted)
			{
				Console.WriteLine("[UPnP] 서버 종료 후 매핑 정리가 제한 시간 안에 끝나지 않았습니다.");
				ShowLauncherNotice(LauncherUiText("포트 매핑 삭제 실패", "Could not remove the port mapping"), true);
			}
			else
			{
				serverStopped.Dispose();
			}
			int exitCode = process.ExitCode;
			if (exitCode == 0 && Volatile.Read(ref currentServerStopRequested) == 0 && Volatile.Read(ref serverReachedReadyState) == 0)
			{
				if (Volatile.Read(ref unsupportedArgumentDetected) != 0)
				{
					Console.WriteLine("서버 버전이 지원하지 않는 실행 인수가 감지됐습니다. 서버 종류별 호환 인수를 확인하세요.");
				}
				else
				{
					Console.WriteLine("서버가 준비 완료 전에 종료됐습니다. 마지막 콘솔 로그를 확인하세요.");
				}
				ShowLauncherNotice(LauncherUiText("서버가 준비 완료 전에 종료되었습니다. 콘솔을 확인해 주세요.", "The server exited before it was ready. Check the console."), true);
				exitCode = 2;
			}
			ClearCurrentServerProcess(process);
			return exitCode;
		}
	}

	private static string GetLaunchJarPath(string jarPath, string workingDirectory)
	{
		string fullJarPath = Path.GetFullPath(jarPath);
		string jarDirectory = Path.GetDirectoryName(fullJarPath);
		string fullWorkingDirectory = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (string.Equals(jarDirectory == null ? string.Empty : jarDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fullWorkingDirectory, StringComparison.OrdinalIgnoreCase))
		{
			// 구형 Paperclip은 Java 11에서 한글이 포함된 절대 JAR 경로를 런처 에이전트로 불러오지 못합니다.
			return Path.GetFileName(fullJarPath);
		}
		return fullJarPath;
	}

	private static void ApplyBatchRuntimeMemory(string workingDirectory, int memoryGb)
	{
		string argumentsPath = Path.Combine(workingDirectory, "user_jvm_args.txt");
		if (!File.Exists(argumentsPath))
		{
			return;
		}
		string[] lines = File.ReadAllLines(argumentsPath, Encoding.UTF8);
		string maximum = "-Xmx" + memoryGb.ToString(System.Globalization.CultureInfo.InvariantCulture) + "G";
		string minimum = "-Xms1G";
		bool hasMaximum = false;
		bool hasMinimum = false;
		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].TrimStart();
			if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
			{
				continue;
			}
			if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"(?i)(?<!\S)-Xmx\S+"))
			{
				hasMaximum = true;
				lines[i] = System.Text.RegularExpressions.Regex.Replace(lines[i], @"(?i)(?<!\S)-Xmx\S+", maximum);
			}
			if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"(?i)(?<!\S)-Xms\S+"))
			{
				hasMinimum = true;
				lines[i] = System.Text.RegularExpressions.Regex.Replace(lines[i], @"(?i)(?<!\S)-Xms\S+", minimum);
			}
		}
		List<string> updatedLines = new List<string>(lines);
		if (!hasMaximum)
		{
			updatedLines.Add(maximum);
		}
		if (!hasMinimum)
		{
			updatedLines.Add(minimum);
		}
		File.WriteAllLines(argumentsPath, updatedLines.ToArray(), new UTF8Encoding(false));
	}

	private static string GetServerConsoleArgument(string serverType, string minecraftVersion)
	{
		string normalized = NormalizeServerType(serverType);
		if (string.Equals(normalized, "vanilla", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "fabric", StringComparison.OrdinalIgnoreCase))
		{
			return " nogui";
		}
		if ((string.Equals(normalized, "paper", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "purpur", StringComparison.OrdinalIgnoreCase)) && SupportsModernPaperConsoleArguments(minecraftVersion))
		{
			// GUI가 표준 입출력을 직접 관리하므로 JLine과 별도 서버 GUI를 사용하지 않습니다.
			return " --nojline --nogui";
		}
		// 구버전 Paper/Purpur은 지원하지 않는 인수로 종료될 수 있어 기본 콘솔 모드를 유지합니다.
		return string.Empty;
	}

	private static bool SupportsModernPaperConsoleArguments(string minecraftVersion)
	{
		MinecraftNumericVersion version;
		if (!TryParseMinecraftNumericVersion(minecraftVersion, out version))
		{
			return false;
		}
		return version.First >= 26 || (version.First == 1 && version.Second >= 13);
	}

	private static bool IsLocalTcpPortListening(int port)
	{
		try
		{
			foreach (IPEndPoint endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
			{
				if (endpoint.Port == port)
				{
					return true;
				}
			}
		}
		catch
		{
			return false;
		}
		return false;
	}

	private static void PrintPortForwardingGuide(string publicIp, int serverPort, string javaPath)
	{
		NetworkDetails networkDetails = GetNetworkDetails();
		Console.WriteLine();
		Console.WriteLine("[현재 PC에 맞춘 포트포워딩 설정 정보]");
		Console.WriteLine("서비스 이름: Minecraft Server");
		Console.WriteLine("외부 포트: " + serverPort);
		Console.WriteLine("내부 포트: " + serverPort);
		Console.WriteLine("내부 IP 주소: " + ValueOrUnknown(networkDetails.LocalIpv4));
		Console.WriteLine("프로토콜: TCP");
		Console.WriteLine("서브넷 마스크: " + ValueOrUnknown(networkDetails.SubnetMask));
		Console.WriteLine("기본 게이트웨이: " + ValueOrUnknown(networkDetails.Gateway));
		Console.WriteLine("공유기 관리 페이지: " + (string.IsNullOrEmpty(networkDetails.Gateway) ? "확인되지 않음" : "http://" + networkDetails.Gateway));
		Console.WriteLine("Windows 방화벽 확인 필요 여부: " + (HasLikelyWindowsFirewallAllowRule(serverPort, 6) ? "허용 규칙 감지됨" : "확인 필요"));
		Console.WriteLine("이중 NAT 또는 CGNAT 가능성: 공유기 WAN 주소가 아래 공인 IP와 다른지 확인 필요");
		Console.WriteLine("네트워크 어댑터: " + ValueOrUnknown(networkDetails.AdapterName));
		Console.WriteLine("MAC 주소(DHCP 예약용): " + ValueOrUnknown(networkDetails.MacAddress));
		Console.WriteLine("현재 공인 IP: " + ValueOrUnknown(publicIp));
		Console.WriteLine("친구가 입력할 주소: " + ValueOrUnknown(publicIp) + ":" + serverPort);
		Console.WriteLine("같은 집 네트워크에서 입력할 주소: " + ValueOrUnknown(networkDetails.LocalIpv4) + ":" + serverPort);
		Console.WriteLine("Java 실행 파일: " + javaPath);
		Console.WriteLine();
		Console.WriteLine("[공유기 설정 순서]");
		if (!string.IsNullOrEmpty(networkDetails.Gateway))
		{
			Console.WriteLine("1) 웹 브라우저에서 http://" + networkDetails.Gateway + " 를 엽니다.");
		}
		else
		{
			Console.WriteLine("1) 명령 프롬프트에서 ipconfig를 실행해 기본 게이트웨이를 찾고 브라우저로 엽니다.");
		}
		Console.WriteLine("2) 공유기 관리자 계정으로 로그인합니다.");
		Console.WriteLine("3) 포트포워딩, NAT, 가상 서버 중 하나의 메뉴를 엽니다.");
		Console.WriteLine("4) 위 값대로 TCP 규칙을 추가하고 사용/활성화한 뒤 저장합니다.");
		Console.WriteLine("5) 내부 IP가 바뀌지 않도록 DHCP 예약 메뉴에서 " + ValueOrUnknown(networkDetails.MacAddress) + "을(를) " + ValueOrUnknown(networkDetails.LocalIpv4) + "에 고정합니다.");
		Console.WriteLine();
		Console.WriteLine("[Windows 방화벽]");
		Console.WriteLine("Windows 보안 > 방화벽 및 네트워크 보호 > 방화벽을 통해 앱 허용에서 위 Java 실행 파일의 개인 네트워크를 허용하세요.");
		Console.WriteLine("또는 PowerShell을 관리자 권한으로 열어 다음 명령을 실행하세요:");
		Console.WriteLine("New-NetFirewallRule -DisplayName 'Minecraft Paper 26.2 TCP " + serverPort + "' -Direction Inbound -Action Allow -Protocol TCP -LocalPort " + serverPort + " -Profile Private");
		Console.WriteLine();
		Console.WriteLine("[그래도 실패할 때]");
		Console.WriteLine("- 공유기 상태 페이지의 WAN/인터넷 IP가 현재 공인 IP와 같은지 비교하세요.");
		Console.WriteLine("- WAN IP가 10.x, 100.64~100.127.x, 172.16~172.31.x, 192.168.x이거나 공인 IP와 다르면 이중 공유기 또는 통신사 CGNAT일 수 있습니다.");
		Console.WriteLine("- 이중 공유기라면 상위 공유기에도 같은 TCP 포트를 전달하거나 하위 공유기를 브리지 모드로 설정하세요.");
		Console.WriteLine("- CGNAT이면 통신사에 공인 IPv4 제공 가능 여부를 문의해야 합니다.");
		Console.WriteLine("- 외부 포트와 내부 포트를 다르게 설정했다면 자동 검사는 서버 포트와 같은 외부 포트만 확인하므로 두 포트를 같게 맞추는 것이 간단합니다.");
		Console.WriteLine("- DMZ 설정이나 방화벽 전체 해제는 보안상 사용하지 마세요.");
		Console.WriteLine("설정을 바꾼 뒤 서버를 다시 실행하면 외부 접속을 자동으로 재검사합니다.");
	}

	private static NetworkDetails GetNetworkDetails()
	{
		NetworkDetails result = new NetworkDetails();
		IPAddress iPAddress = null;
		try
		{
			using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
			{
				socket.Connect("8.8.8.8", 65530);
				iPAddress = ((IPEndPoint)socket.LocalEndPoint).Address;
			}
		}
		catch
		{
			iPAddress = null;
		}
		NetworkInterface networkInterface = null;
		NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
		NetworkInterface[] array = allNetworkInterfaces;
		NetworkInterface[] array2 = array;
		NetworkInterface[] array3 = array2;
		foreach (NetworkInterface networkInterface2 in array3)
		{
			if (networkInterface2.OperationalStatus != OperationalStatus.Up || networkInterface2.NetworkInterfaceType == NetworkInterfaceType.Loopback)
			{
				continue;
			}
			IPInterfaceProperties iPProperties = networkInterface2.GetIPProperties();
			UnicastIPAddressInformation unicastIPAddressInformation = null;
			foreach (UnicastIPAddressInformation unicastAddress in iPProperties.UnicastAddresses)
			{
				if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
				{
					unicastIPAddressInformation = unicastAddress;
					if (iPAddress != null && unicastAddress.Address.Equals(iPAddress) && IsPreferredLanAdapter(networkInterface2))
					{
						return BuildNetworkDetails(networkInterface2, iPProperties, unicastIPAddressInformation);
					}
				}
			}
			if (networkInterface == null && unicastIPAddressInformation != null && iPProperties.GatewayAddresses.Count > 0 && IsPreferredLanAdapter(networkInterface2))
			{
				networkInterface = networkInterface2;
			}
		}
		if (networkInterface != null)
		{
			IPInterfaceProperties iPProperties2 = networkInterface.GetIPProperties();
			foreach (UnicastIPAddressInformation unicastAddress2 in iPProperties2.UnicastAddresses)
			{
				if (unicastAddress2.Address.AddressFamily == AddressFamily.InterNetwork)
				{
					return BuildNetworkDetails(networkInterface, iPProperties2, unicastAddress2);
				}
			}
		}
		return result;
	}

	private static bool IsPreferredLanAdapter(NetworkInterface adapter)
	{
		if (adapter == null || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel || adapter.NetworkInterfaceType == NetworkInterfaceType.Ppp) return false;
		string identity = ((adapter.Name ?? string.Empty) + " " + (adapter.Description ?? string.Empty)).ToLowerInvariant();
		string[] virtualMarkers = new string[] { "vpn", "virtual", "hyper-v", "vmware", "virtualbox", "tap-", "tailscale", "zerotier", "wintun" };
		for (int i = 0; i < virtualMarkers.Length; i++) if (identity.Contains(virtualMarkers[i])) return false;
		return true;
	}

	private static NetworkDetails BuildNetworkDetails(NetworkInterface adapter, IPInterfaceProperties properties, UnicastIPAddressInformation address)
	{
		NetworkDetails networkDetails = new NetworkDetails();
		networkDetails.AdapterName = adapter.Name;
		networkDetails.LocalIpv4 = address.Address.ToString();
		networkDetails.SubnetMask = ((address.IPv4Mask != null) ? address.IPv4Mask.ToString() : null);
		foreach (GatewayIPAddressInformation gatewayAddress in properties.GatewayAddresses)
		{
			if (gatewayAddress.Address.AddressFamily == AddressFamily.InterNetwork)
			{
				networkDetails.Gateway = gatewayAddress.Address.ToString();
				break;
			}
		}
		byte[] addressBytes = adapter.GetPhysicalAddress().GetAddressBytes();
		if (addressBytes.Length > 0)
		{
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < addressBytes.Length; i = checked(i + 1))
			{
				if (i > 0)
				{
					stringBuilder.Append('-');
				}
				stringBuilder.Append(addressBytes[i].ToString("X2"));
			}
			networkDetails.MacAddress = stringBuilder.ToString();
		}
		return networkDetails;
	}

	private static string ValueOrUnknown(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return "확인되지 않음";
	}

	private static string DownloadText(string url)
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
		httpWebRequest.Method = "GET";
		httpWebRequest.UserAgent = GetLauncherIntegrationUserAgent();
		httpWebRequest.Timeout = 15000;
		httpWebRequest.ReadWriteTimeout = 15000;
		httpWebRequest.AllowAutoRedirect = false;
		httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
		{
			if (httpWebResponse.ContentLength > 65536L) throw new InvalidDataException("외부 검사 응답이 안전 크기 제한을 초과했습니다.");
			using (Stream stream = httpWebResponse.GetResponseStream())
			{
				using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8))
				{
					if (httpWebResponse.StatusCode != HttpStatusCode.OK)
					{
						throw new WebException("HTTP " + (int)httpWebResponse.StatusCode);
					}
					return ReadLimitedText(streamReader, 65536);
				}
			}
		}
	}

	private static string ReadLimitedText(TextReader reader, int maximumCharacters)
	{
		StringBuilder builder = new StringBuilder();
		char[] buffer = new char[4096];
		int read;
		while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
		{
			if (builder.Length + read > maximumCharacters) throw new InvalidDataException("응답이 안전 크기 제한을 초과했습니다.");
			builder.Append(buffer, 0, read);
		}
		return builder.ToString();
	}

	private static void DeleteFileIfPresent(string path)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private static void DeleteDirectoryIfPresent(string path)
	{
		if (Directory.Exists(path))
		{
			Directory.Delete(path, true);
		}
	}

	private static void PauseBeforeExit()
	{
		if (IsGuiMode() || ManagedChildMode)
		{
			return;
		}
		Console.WriteLine();
		Console.Write("창을 닫으려면 Enter 키를 누르세요.");
		Console.ReadLine();
	}
}
