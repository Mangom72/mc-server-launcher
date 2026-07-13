using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text.RegularExpressions;

internal static class LauncherTests
{
	private static int passed;
	private static Type launcher;

	[STAThread]
	private static int Main(string[] args)
	{
		string temporary = Path.Combine(Path.GetTempPath(), "mc-launcher-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(temporary);
		try
		{
			Assembly assembly = Assembly.LoadFrom(args[0]);
			launcher = assembly.GetType("Launcher", true);
			TestVersionSeparation(assembly);
			TestProductVersionParsing();
			TestUpdateMetadataParsing();
			TestLauncherUpdatePreferences(temporary);
			TestLauncherUpdateDirectoryCompatibility();
			TestHashAndReplacement(temporary);
			TestMockedDownload(temporary);
			TestDataLocations(temporary);
			TestSetupLayoutAndValidation();
			TestUxAccessibility();
			TestPlayerButtonLifecycle();
			TestModelessToolWindows();
			TestJavaSelection();
			TestConsoleClassificationAndLaunchArguments();
			TestBackupRestoreAndProfileCopy(temporary);
			TestManagedServerPortAndNetworkStatus(temporary);
			TestServerTrashLifecycle(temporary);
			TestUpnpOwnershipRules();
			TestModrinthHash(temporary);
			TestDiagnosticRedaction(temporary);
			TestQuickCommandsAndBridge(temporary);
			Console.WriteLine("PASSED=" + passed);
			return 0;
		}
		catch (Exception exception)
		{
			Exception error = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
			Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message);
			Console.Error.WriteLine(error.StackTrace);
			return 1;
		}
		finally
		{
			SetStaticField("StorageSettingsPathOverride", null);
			SetStaticField("LauncherUpdatePreferencesPathOverride", null);
			SetStaticField("BridgeArtifactOverridePath", null);
			SetStaticField("BridgeInstallFailureAfterBackup", false);
			if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
		}
	}

	private static void TestVersionSeparation(Assembly assembly)
	{
		Type info = assembly.GetType("BuildVersionInfo", true);
		string product = Convert.ToString(info.GetField("ProductVersion").GetRawConstantValue());
		string build = Convert.ToString(info.GetField("BuildNumber").GetRawConstantValue());
		object[] productParse = { product, null };
		Equal(true, Invoke("TryParseProductVersion", productParse), "제품 버전 형식");
		Version buildVersion;
		Equal(true, Version.TryParse(build, out buildVersion), "빌드 번호 형식");
		if (string.Equals(product, build, StringComparison.Ordinal)) throw new InvalidOperationException("제품 버전과 빌드 번호가 분리되지 않았습니다.");
		Equal(buildVersion, assembly.GetName().Version, "어셈블리 빌드 번호");
		object[] productAttributes = assembly.GetCustomAttributes(typeof(AssemblyProductAttribute), false);
		AssemblyProductAttribute productName = (AssemblyProductAttribute)productAttributes[0];
		Equal("MineHarbor — Minecraft Server Launcher", productName.Product, "제품 표시 이름");
		foreach (string resourceName in assembly.GetManifestResourceNames())
		{
			if (resourceName.IndexOf("paper.jar", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidOperationException("Paper 서버 JAR이 실행 파일에 남아 있습니다.");
			if (resourceName.IndexOf("java25", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidOperationException("Java 25 런타임이 실행 파일에 남아 있습니다.");
		}
		Pass();
	}

	private static void TestProductVersionParsing()
	{
		object[] valid = { "1.2.3", null };
		Equal(true, Invoke("TryParseProductVersion", valid), "정상 제품 버전");
		Equal(new Version(1, 2, 3), valid[1], "제품 버전 값");
		foreach (string invalid in new string[] { "1.2", "1.2.3.4", "v1.2.3", "1.2.x", "" })
		{
			object[] values = { invalid, null };
			Equal(false, Invoke("TryParseProductVersion", values), "잘못된 제품 버전 차단");
		}
		Pass();
	}

	private static void TestUpdateMetadataParsing()
	{
		string hash = new string('a', 64);
		string json = "{\"version\":\"0.4.2\",\"build\":\"26.2.45.30\",\"download_url\":\"https://github.com/Mangom72/mc-server-launcher/releases/download/v0.4.2/Minecraft-Server-Launcher.exe\",\"primary_download_url\":\"https://github.com/Mangom72/mc-server-launcher/releases/download/v0.4.2/MineHarbor.exe\",\"sha256\":\"" + hash + "\",\"size\":2097152,\"release_notes\":\"test\",\"minimum_supported_version\":\"0.1.0\"}";
		object metadata = Invoke("ParseLauncherUpdateMetadata", new object[] { json });
		Equal("0.4.2", Convert.ToString(GetField(metadata, "ProductVersion")), "업데이트 제품 버전");
		Equal("26.2.45.30", Convert.ToString(GetField(metadata, "BuildNumber")), "업데이트 빌드");
		Equal("https://github.com/Mangom72/mc-server-launcher/releases/download/v0.4.2/MineHarbor.exe", Convert.ToString(GetField(metadata, "Url")), "MineHarbor 기본 업데이트 자산");
		string legacyJson = json.Replace(",\"primary_download_url\":\"https://github.com/Mangom72/mc-server-launcher/releases/download/v0.4.2/MineHarbor.exe\"", string.Empty);
		object legacyMetadata = Invoke("ParseLauncherUpdateMetadata", new object[] { legacyJson });
		Equal("https://github.com/Mangom72/mc-server-launcher/releases/download/v0.4.2/Minecraft-Server-Launcher.exe", Convert.ToString(GetField(legacyMetadata, "Url")), "기존 업데이트 메타데이터 호환");
		Equal(true, Invoke("IsLauncherUpdateNewer", new object[] { metadata, "0.4.1", "26.2.45.29" }), "새 제품 버전 판별");
		Equal(false, Invoke("IsLauncherUpdateNewer", new object[] { metadata, "0.4.2", "26.2.45.30" }), "최신 버전 판별");
		ExpectFailure(delegate { Invoke("ParseLauncherUpdateMetadata", new object[] { "{}" }); }, "누락된 업데이트 메타데이터");
		ExpectFailure(delegate { Invoke("ParseLauncherUpdateMetadata", new object[] { json.Replace(hash, "bad") }); }, "잘못된 업데이트 해시");
		ExpectFailure(delegate { Invoke("ParseLauncherUpdateMetadata", new object[] { json.Replace("https://github.com/Mangom72/", "http://example.com/") }); }, "허용되지 않은 업데이트 주소");
		string compactJson = json.Replace("2097152", "300000");
		Equal("0.4.2", Convert.ToString(GetField(Invoke("ParseLauncherUpdateMetadata", new object[] { compactJson }), "ProductVersion")), "경량 런처 업데이트 허용");
		ExpectFailure(delegate { Invoke("ParseLauncherUpdateMetadata", new object[] { json.Replace("2097152", "200000") }); }, "비정상적으로 작은 런처 업데이트 차단");
		Pass();
	}

	private static void TestLauncherUpdatePreferences(string root)
	{
		string path = Path.Combine(root, "launcher-update-preferences.properties");
		SetStaticField("LauncherUpdatePreferencesPathOverride", path);
		Type type = launcher.GetNestedType("LauncherReleaseAsset", BindingFlags.NonPublic);
		object first = Activator.CreateInstance(type, true);
		SetPublic(first, "ProductVersion", "1.3.0");
		SetPublic(first, "BuildNumber", "26.2.45.35");
		Invoke("SetLauncherUpdateIgnored", new object[] { first, true });
		Equal(true, Invoke("IsLauncherUpdateIgnored", new object[] { first }), "선택한 업데이트 다시 알리지 않기 저장");
		object next = Activator.CreateInstance(type, true);
		SetPublic(next, "ProductVersion", "1.4.0");
		SetPublic(next, "BuildNumber", "26.2.45.36");
		Equal(false, Invoke("IsLauncherUpdateIgnored", new object[] { next }), "다음 업데이트는 다시 표시");
		Invoke("SetLauncherUpdateIgnored", new object[] { first, false });
		Equal(false, File.Exists(path), "업데이트 알림 숨김 해제");
		Pass();
	}

	private static void TestLauncherUpdateDirectoryCompatibility()
	{
		string current = Path.Combine(Path.GetTempPath(), "MineHarborLauncherUpdate", Guid.NewGuid().ToString("N"));
		string legacy = Path.Combine(Path.GetTempPath(), "Paper26.2LauncherUpdate", Guid.NewGuid().ToString("N"));
		string unrelated = Path.Combine(Path.GetTempPath(), "UntrustedLauncherUpdate", Guid.NewGuid().ToString("N"));
		Equal(true, Invoke("IsSafeLauncherUpdateDirectory", new object[] { current }), "현재 런처 업데이트 임시 경로");
		Equal(true, Invoke("IsSafeLauncherUpdateDirectory", new object[] { legacy }), "v0.4.2 업데이트 임시 경로 호환");
		Equal(false, Invoke("IsSafeLauncherUpdateDirectory", new object[] { unrelated }), "허용되지 않은 업데이트 임시 경로 차단");
		Equal(false, Invoke("IsSafeLauncherUpdateDirectory", new object[] { Path.Combine(Path.GetTempPath(), "MineHarborLauncherUpdate") }), "업데이트 루트 자체 차단");
		Pass();
	}

	private static void TestHashAndReplacement(string root)
	{
		string source = Path.Combine(root, "new.exe");
		string target = Path.Combine(root, "old.exe");
		File.WriteAllText(source, "new launcher", Encoding.UTF8);
		File.WriteAllText(target, "old launcher", Encoding.UTF8);
		string hash;
		using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source))).Replace("-", string.Empty);
		Invoke("ReplaceLauncherFileOnce", new object[] { source, target, hash });
		Equal(File.ReadAllText(source), File.ReadAllText(target), "검증된 런처 교체");
		File.WriteAllText(target, "old launcher", Encoding.UTF8);
		ExpectFailure(delegate { Invoke("ReplaceLauncherFileOnce", new object[] { source, target, new string('0', 64) }); }, "해시 불일치 교체 차단");
		Pass();
	}

	private static void TestMockedDownload(string root)
	{
		byte[] payload = Encoding.UTF8.GetBytes("verified mocked launcher download");
		string completeUrl = StartSingleResponseServer(payload, payload.Length);
		object asset = CreateReleaseAsset(completeUrl, payload.Length);
		string complete = Path.Combine(root, "complete.download");
		Invoke("DownloadLauncherUpdate", new object[] { asset, complete });
		Equal(payload.Length, File.ReadAllBytes(complete).Length, "모킹된 정상 다운로드");

		string interruptedUrl = StartSingleResponseServer(payload, payload.Length + 20);
		object interruptedAsset = CreateReleaseAsset(interruptedUrl, payload.Length + 20);
		string interrupted = Path.Combine(root, "interrupted.download");
		ExpectFailure(delegate { Invoke("DownloadLauncherUpdate", new object[] { interruptedAsset, interrupted }); }, "중단된 다운로드");
		Pass();
	}

	private static void TestServerTrashLifecycle(string root)
	{
		string serversRoot = Path.Combine(root, "trash-lifecycle");
		string profileDirectory = Path.Combine(serversRoot, "servers", "삭제 테스트");
		Directory.CreateDirectory(profileDirectory);
		File.WriteAllText(Path.Combine(profileDirectory, "world.dat"), "world", Encoding.UTF8);
		Type profileType = launcher.GetNestedType("ManagedProfileRecord", BindingFlags.NonPublic);
		object profile = Activator.CreateInstance(profileType, true);
		SetPublic(profile, "Name", "삭제 테스트");
		SetPublic(profile, "Directory", profileDirectory);
		DateTime deletedUtc = DateTime.UtcNow.AddMinutes(-1);
		object trashed = Invoke("MoveProfileToServerTrash", new object[] { serversRoot, profile, deletedUtc });
		string trashedDirectory = Convert.ToString(GetField(trashed, "Directory"));
		Equal(false, Directory.Exists(profileDirectory), "서버를 휴지통으로 이동");
		Equal(true, File.Exists(Path.Combine(trashedDirectory, ".mineharbor-trash.json")), "휴지통 메타데이터 기록");
		IList records = (IList)Invoke("ReadServerTrashRecords", new object[] { serversRoot });
		Equal(1, records.Count, "휴지통 항목 불러오기");
		DateTime expiresUtc = (DateTime)GetField(records[0], "ExpiresUtc");
		if (Math.Abs((expiresUtc - deletedUtc).TotalDays - 30.0) > 0.01) throw new InvalidOperationException("휴지통 보관 기간이 30일이 아닙니다.");
		Invoke("RestoreServerTrashRecord", new object[] { serversRoot, records[0], "복구 테스트" });
		string restoredDirectory = Path.Combine(serversRoot, "servers", "복구 테스트");
		Equal(true, File.Exists(Path.Combine(restoredDirectory, "world.dat")), "휴지통 서버 복구");
		Equal(false, File.Exists(Path.Combine(restoredDirectory, ".mineharbor-trash.json")), "복구 후 휴지통 메타데이터 제거");

		SetPublic(profile, "Name", "복구 테스트");
		SetPublic(profile, "Directory", restoredDirectory);
		object expired = Invoke("MoveProfileToServerTrash", new object[] { serversRoot, profile, DateTime.UtcNow.AddDays(-31) });
		string expiredDirectory = Convert.ToString(GetField(expired, "Directory"));
		string unknownDirectory = Path.Combine(serversRoot, "servers-trash", "metadata-없는-폴더");
		Directory.CreateDirectory(unknownDirectory);
		Equal(1, Invoke("PurgeExpiredServerTrash", new object[] { serversRoot, DateTime.UtcNow }), "만료 서버 자동 삭제 개수");
		Equal(false, Directory.Exists(expiredDirectory), "30일 지난 서버 자동 삭제");
		Equal(true, Directory.Exists(unknownDirectory), "메타데이터 없는 폴더 보존");
		Invoke("WriteActiveProfileName", new object[] { serversRoot, "복구 테스트" });
		IList remainingProfiles = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(profileType));
		remainingProfiles.Add(profile);
		Equal(null, Invoke("UpdateActiveProfileAfterRemoval", new object[] { serversRoot, remainingProfiles, "복구 테스트" }), "마지막 서버 삭제 허용");
		Equal(false, File.Exists(Path.Combine(serversRoot, ".active-server-profile")), "마지막 서버 삭제 후 활성 프로필 해제");
		Type trashFormType = launcher.GetNestedType("ServerTrashForm", BindingFlags.NonPublic);
		using (Form trashForm = (Form)Activator.CreateInstance(trashFormType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { serversRoot }, null))
		{
			ListView trashList = (ListView)GetPrivateField(trashFormType, trashForm, "trashList");
			Button restoreButton = (Button)GetPrivateField(trashFormType, trashForm, "restoreButton");
			Button deleteButton = (Button)GetPrivateField(trashFormType, trashForm, "deleteButton");
			Equal(4, trashList.Columns.Count, "휴지통 목록 정보 열");
			if (string.IsNullOrEmpty(restoreButton.Text) || string.IsNullOrEmpty(deleteButton.Text)) throw new InvalidOperationException("휴지통 작업 버튼 문구가 없습니다.");
		}
		Pass();
	}

	private static object CreateReleaseAsset(string url, long size)
	{
		Type type = launcher.GetNestedType("LauncherReleaseAsset", BindingFlags.NonPublic);
		object asset = Activator.CreateInstance(type, true);
		type.GetField("Url", BindingFlags.Instance | BindingFlags.Public).SetValue(asset, url);
		type.GetField("Size", BindingFlags.Instance | BindingFlags.Public).SetValue(asset, size);
		return asset;
	}

	private static string StartSingleResponseServer(byte[] body, int declaredLength)
	{
		TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		Thread server = new Thread(delegate()
		{
			try
			{
				using (TcpClient client = listener.AcceptTcpClient())
				using (NetworkStream stream = client.GetStream())
				{
					byte[] request = new byte[4096];
					stream.Read(request, 0, request.Length);
					byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: " + declaredLength + "\r\nConnection: close\r\n\r\n");
					stream.Write(header, 0, header.Length);
					stream.Write(body, 0, body.Length);
					stream.Flush();
				}
			}
			finally { listener.Stop(); }
		});
		server.IsBackground = true;
		server.Start();
		return "http://127.0.0.1:" + port + "/launcher.exe";
	}

	private static void TestDataLocations(string root)
	{
		string settingsPath = Path.Combine(root, "storage.properties");
		SetStaticField("StorageSettingsPathOverride", settingsPath);
		string custom = Path.Combine(root, "custom-data");
		object[] validate = { custom, null, null };
		Equal(true, Invoke("TryValidateDataRoot", validate), "사용자 지정 경로");
		Equal(Path.GetFullPath(custom), validate[1], "사용자 지정 경로 정규화");

		Invoke("SaveDataStorageSettings", new object[] { "custom", custom });
		Type settingsType = launcher.GetNestedType("DataStorageSettings", BindingFlags.NonPublic);
		object[] read = { root, null, null, null };
		Equal(true, Invoke("TryReadDataStorageSettings", read), "저장 위치 다시 불러오기");
		Equal(Path.GetFullPath(custom), read[2], "저장된 사용자 지정 위치");

		File.Delete(settingsPath);
		string portable = Path.Combine(root, "Minecraft-Servers-Data");
		Directory.CreateDirectory(portable);
		string sentinel = Path.Combine(portable, "keep.txt");
		File.WriteAllText(sentinel, "keep");
		Equal(Path.GetFullPath(portable), Invoke("ResolveServersRootDirectory", new object[] { root }), "기존 Portable 데이터 우선 사용");
		Equal(true, File.Exists(sentinel), "기존 데이터 보존");

		object[] driveRoot = { Path.GetPathRoot(root), null, null };
		Equal(false, Invoke("TryValidateDataRoot", driveRoot), "드라이브 루트 차단");
		object[] windows = { Environment.GetFolderPath(Environment.SpecialFolder.Windows), null, null };
		Equal(false, Invoke("TryValidateDataRoot", windows), "Windows 폴더 차단");
		string fileInsteadOfFolder = Path.Combine(root, "not-a-folder");
		File.WriteAllText(fileInsteadOfFolder, "x");
		object[] unavailable = { fileInsteadOfFolder, null, null };
		Equal(false, Invoke("TryValidateDataRoot", unavailable), "사용 불가능한 경로 차단");
		string installedDirectory = Path.Combine(root, "installed");
		Directory.CreateDirectory(installedDirectory);
		string installedExe = Path.Combine(installedDirectory, "MineHarbor.exe");
		File.WriteAllText(installedExe, "test");
		File.WriteAllText(Path.Combine(installedDirectory, "installed.mode"), "installed");
		Equal(true, Invoke("IsInstalledLauncherPath", new object[] { installedExe }), "설치형 경로 식별");
		Pass();
	}

	private static void TestSetupLayoutAndValidation()
	{
		Type settingsType = launcher.GetNestedType("ServerSettings", BindingFlags.NonPublic);
		object settings = Activator.CreateInstance(settingsType, true);
		SetPublic(settings, "ProfileName", "테스트 서버");
		SetPublic(settings, "ServerType", "paper");
		SetPublic(settings, "MinecraftVersion", "1.21.11");
		SetPublic(settings, "Motd", "A Minecraft Server");
		SetPublic(settings, "MaxPlayers", 20);
		SetPublic(settings, "ServerPort", 25565);
		SetPublic(settings, "MemoryGb", 4);
		SetPublic(settings, "ViewDistance", 32);
		SetPublic(settings, "SimulationDistance", 32);
		SetPublic(settings, "OnlineMode", true);
		SetPublic(settings, "AutoUpdate", true);
		SetPublic(settings, "CustomJavaMajor", 21);
		SetPublic(settings, "PresetName", "보통 야생");
		SetPublic(settings, "GameMode", "survival");
		SetPublic(settings, "Difficulty", "normal");
		SetPublic(settings, "LevelType", "minecraft:normal");

		Type formType = launcher.GetNestedType("ServerSetupForm", BindingFlags.NonPublic);
		using (Form form = (Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { settings, 4, 8, false, false }, null))
		{
			Panel body = null;
			foreach (Control control in form.Controls) if (control is Panel && ((Panel)control).AutoScroll) body = (Panel)control;
			if (body == null || body.AutoScrollMinSize.Height < 650) throw new InvalidOperationException("작은 화면 스크롤 영역이 없습니다.");
			Equal(0, body.AutoScrollMinSize.Width, "설정 화면 가로 스크롤 방지");
			if (form.MinimumSize.Width < 790) throw new InvalidOperationException("설정 화면 최소 폭이 콘텐츠보다 작습니다.");
			CheckBox snapshots = (CheckBox)GetPrivateField(formType, form, "includeSnapshotsBox");
			if (snapshots.Right > 700) throw new InvalidOperationException("스냅샷 옵션이 설정 콘텐츠 폭을 벗어납니다.");
			NumericUpDown port = (NumericUpDown)GetPrivateField(formType, form, "portBox");
			NumericUpDown memory = (NumericUpDown)GetPrivateField(formType, form, "memoryBox");
			Equal(1m, port.Minimum, "포트 최소값");
			Equal(65535m, port.Maximum, "포트 최대값");
			Equal(2m, memory.Minimum, "메모리 최소값");
			if (string.IsNullOrEmpty(port.AccessibleName)) throw new InvalidOperationException("포트 입력의 접근성 이름이 없습니다.");
			Label validation = (Label)GetPrivateField(formType, form, "validationLabel");
			Equal(AccessibleRole.Alert, validation.AccessibleRole, "설정 오류 접근성 역할");

			ComboBox serverType = (ComboBox)GetPrivateField(formType, form, "serverTypeBox");
			Equal("ModernComboBox", serverType.GetType().Name, "테마 대응 서버 종류 선택 상자");
			Equal(DrawMode.OwnerDrawFixed, serverType.DrawMode, "서버 종류 선택 상자 오너 드로우");
			Label rules = (Label)GetPrivateField(formType, form, "rulesLabel");
			Equal(548, rules.Top, "기본 프리셋 서버 규칙 위치");
			serverType.SelectedIndex = serverType.Items.Count - 1;
			CheckBox manual = (CheckBox)GetPrivateField(formType, form, "manualJarBox");
			ComboBox version = (ComboBox)GetPrivateField(formType, form, "versionBox");
			CheckBox automatic = (CheckBox)GetPrivateField(formType, form, "autoUpdateBox");
			Equal(false, manual.Enabled, "직접 JAR 필수 설정 표시");
			Equal(false, version.Enabled, "직접 JAR 버전 비활성화");
			Equal(false, automatic.Enabled, "직접 JAR 자동 업데이트 비활성화");

			serverType.SelectedIndex = 0;
			version.Items.Clear();
			version.Items.Add("1.20.1");
			version.SelectedIndex = 0;
			formType.GetMethod("ApplyVersionChoices", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { new string[] { "1.21.11", "1.20.1" }, null });
			Equal("1.20.1", Convert.ToString(version.SelectedItem), "백그라운드 버전 선택 유지");
		}
		Pass();
	}

	private static void TestUxAccessibility()
	{
		Type paletteType = launcher.GetNestedType("ThemePalette", BindingFlags.NonPublic);
		object palette = paletteType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { false });
		Color window = (Color)paletteType.GetField("Window", BindingFlags.Instance | BindingFlags.Public).GetValue(palette);
		Color muted = (Color)paletteType.GetField("Muted", BindingFlags.Instance | BindingFlags.Public).GetValue(palette);
		if (ContrastRatio(window, muted) < 4.5) throw new InvalidOperationException("라이트 모드 보조 텍스트 대비가 4.5:1보다 낮습니다.");

		Type buttonType = launcher.GetNestedType("RoundedButton", BindingFlags.NonPublic);
		using (Button button = (Button)Activator.CreateInstance(buttonType, true))
		{
			button.Enabled = false;
			Equal(Cursors.Default, button.Cursor, "비활성 버튼 커서");
			Type iconType = launcher.GetNestedType("ButtonIcon", BindingFlags.NonPublic);
			PropertyInfo iconProperty = buttonType.GetProperty("IconKind", BindingFlags.Instance | BindingFlags.Public);
			object play = Enum.Parse(iconType, "Play");
			iconProperty.SetValue(button, play, null);
			Equal("Play", Convert.ToString(iconProperty.GetValue(button, null)), "버튼 벡터 아이콘 상태");
		}
		MethodInfo ensureButtonContentFits = launcher.GetMethod("EnsureButtonContentFits", BindingFlags.Static | BindingFlags.NonPublic);
		Type managedIconType = launcher.GetNestedType("ButtonIcon", BindingFlags.NonPublic);
		PropertyInfo managedIconProperty = buttonType.GetProperty("IconKind", BindingFlags.Instance | BindingFlags.Public);
		object managedIcon = Enum.Parse(managedIconType, "Server");
		string[][] managementLabels = new string[][]
		{
			new string[] { "시작", "안전 종료", "콘솔", "새 서버", "복제", "가져오기", "이름 변경", "보관", "삭제", "휴지통", "영구 삭제", "기본 서버로", "새로고침", "이 서버 선택" },
			new string[] { "Start", "Stop safely", "Console", "New", "Clone", "Import", "Rename", "Archive", "Delete", "Trash", "Delete forever", "Set active", "Refresh" }
		};
		foreach (string[] labels in managementLabels)
		{
			foreach (string label in labels)
			{
				using (Button button = (Button)Activator.CreateInstance(buttonType, true))
				{
					button.Text = label;
					button.Width = 86;
					button.Height = 40;
					button.Font = new Font("Segoe UI Variable Text", 9.5F);
					managedIconProperty.SetValue(button, managedIcon, null);
					ensureButtonContentFits.Invoke(null, new object[] { button });
					Size measured = TextRenderer.MeasureText(button.Text, button.Font, new Size(4096, button.Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
					if (measured.Width + 46 > button.Width) throw new InvalidOperationException("서버 관리 버튼 문구가 잘립니다: " + label);
					if (button.MinimumSize.Width < measured.Width + 46) throw new InvalidOperationException("서버 관리 버튼 최소 폭이 보존되지 않습니다: " + label);
				}
			}
		}
		string startDescription = Convert.ToString(Invoke("GetCommonButtonDescription", new object[] { "서버 시작" }));
		if (startDescription.IndexOf("F5", StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidOperationException("시작 단축키 안내가 없습니다.");

		Type localizationType = launcher.GetNestedType("Localization", BindingFlags.NonPublic);
		FieldInfo languageField = localizationType.GetField("CurrentLanguage", BindingFlags.Static | BindingFlags.Public);
		object originalLanguage = languageField.GetValue(null);
		Type formType = launcher.GetNestedType("LauncherForm", BindingFlags.NonPublic);
		using (Form form = (Form)Activator.CreateInstance(formType, true))
		{
			MethodInfo applyLocalization = formType.GetMethod("ApplyLocalization", BindingFlags.Instance | BindingFlags.NonPublic);
			foreach (string language in new string[] { "ko", "en" })
			{
				languageField.SetValue(null, language);
				applyLocalization.Invoke(form, null);
				form.PerformLayout();
				foreach (string fieldName in new string[] { "startButton", "stopButton", "settingsButton", "upgradeButton", "consoleButton", "profilesButton", "backupButton", "contentButton", "playersButton", "networkButton", "diagnosticsButton", "launcherUpdateButton" })
				{
					Button action = (Button)GetPrivateField(formType, form, fieldName);
					Size measured = TextRenderer.MeasureText(action.Text, action.Font, new Size(Math.Max(1, action.Width - 45), action.Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
					if (measured.Width > action.Width - 45) throw new InvalidOperationException(language + " 버튼 문구가 표시 폭을 넘습니다: " + action.Text);
				}
			}
		}
		languageField.SetValue(null, originalLanguage);
		Pass();
	}

	private static void TestPlayerButtonLifecycle()
	{
		Type formType = launcher.GetNestedType("LauncherForm", BindingFlags.NonPublic);
		using (Form form = (Form)Activator.CreateInstance(formType, true))
		{
			Button playersButton = (Button)GetPrivateField(formType, form, "playersButton");
			Equal(false, playersButton.Enabled, "서버 시작 전 플레이어 버튼 비활성화");
			formType.GetMethod("ServerStarted", BindingFlags.Instance | BindingFlags.Public).Invoke(form, new object[] { "127.0.0.1:25565" });
			Equal(true, playersButton.Enabled, "서버 시작 후 플레이어 버튼 활성화");
			formType.GetMethod("WorkflowFinished", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { 0, false });
			Equal(false, playersButton.Enabled, "서버 종료 후 플레이어 버튼 비활성화");
		}
		Pass();
	}

	private static void TestModelessToolWindows()
	{
		Type formType = launcher.GetNestedType("LauncherForm", BindingFlags.NonPublic);
		using (Form owner = (Form)Activator.CreateInstance(formType, true))
		{
			MethodInfo showTool = formType.GetMethod("ShowModelessToolWindow", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo ensureSafe = formType.GetMethod("EnsureNoBlockingToolWindow", BindingFlags.Instance | BindingFlags.NonPublic);
			int created = 0;
			int closed = 0;
			Form child = null;
			Func<Form> factory = delegate
			{
				created++;
				child = new Form();
				child.Text = "모델리스 테스트";
				return child;
			};
			Action onClosed = delegate { closed++; };
			showTool.Invoke(owner, new object[] { "test-tool", factory, true, onClosed });
			Equal(true, owner.Enabled, "기능 창을 연 뒤 메인 창 활성 상태 유지");
			Equal(true, child.Visible, "기능 창 모델리스 표시");
			Equal(1, created, "기능 창 최초 생성");
			showTool.Invoke(owner, new object[] { "test-tool", factory, true, onClosed });
			Equal(1, created, "같은 기능 창 중복 생성 방지");
			Equal(false, ensureSafe.Invoke(owner, null), "관리 창이 열린 동안 서버 변경 차단");
			child.Close();
			Application.DoEvents();
			Equal(1, closed, "기능 창 종료 후 콜백 실행");
			Equal(true, ensureSafe.Invoke(owner, null), "기능 창 종료 후 서버 변경 허용");
		}
		Pass();
	}

	private static double ContrastRatio(Color first, Color second)
	{
		double lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
		double darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
		return (lighter + 0.05) / (darker + 0.05);
	}

	private static double RelativeLuminance(Color color)
	{
		double red = LinearChannel(color.R / 255.0);
		double green = LinearChannel(color.G / 255.0);
		double blue = LinearChannel(color.B / 255.0);
		return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
	}

	private static double LinearChannel(double value)
	{
		return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
	}

	private static void TestJavaSelection()
	{
		Equal(11, Invoke("ResolvePaperFamilyJavaMajor", new object[] { "1.12.2" }), "구버전 Paper Java");
		Equal(17, Invoke("ResolvePaperFamilyJavaMajor", new object[] { "1.19.4" }), "1.19 Paper Java");
		Equal(21, Invoke("ResolvePaperFamilyJavaMajor", new object[] { "1.21.11" }), "1.21 Paper Java");
		Equal(25, Invoke("ResolvePaperFamilyJavaMajor", new object[] { "26.2" }), "26.2 Paper Java");
		Pass();
	}

	private static void TestConsoleClassificationAndLaunchArguments()
	{
		string terminal = "ServerMain WARN Advanced terminal features are not available in this environment";
		string unsafeWarning = "WARNING: sun.misc.Unsafe::objectFieldOffset will be removed in a future release";
		Equal("Compatibility", Convert.ToString(Invoke("ClassifyConsoleLine", new object[] { terminal })), "터미널 호환성 경고 분류");
		Equal("Compatibility", Convert.ToString(Invoke("ClassifyConsoleLine", new object[] { unsafeWarning })), "Unsafe 호환성 경고 분류");
		Equal("Warning", Convert.ToString(Invoke("ClassifyConsoleLine", new object[] { "Server thread WARN Plugin warning" })), "일반 경고 분류");
		Equal("Error", Convert.ToString(Invoke("ClassifyConsoleLine", new object[] { "Caused by: java.lang.IllegalStateException" })), "오류 분류");
		Equal(true, Invoke("ConsoleLineMatchesFilter", new object[] { terminal, 2 }), "호환성 필터 포함");
		Equal(false, Invoke("ConsoleLineMatchesFilter", new object[] { terminal, 1 }), "일반 경고에서 호환성 경고 제외");
		Equal(" --nojline --nogui", Convert.ToString(Invoke("GetServerConsoleArgument", new object[] { "paper", "26.2" })), "최신 Paper GUI 콘솔 인수");
		Equal(string.Empty, Convert.ToString(Invoke("GetServerConsoleArgument", new object[] { "paper", "1.12.2" })), "구버전 Paper 콘솔 인수 보존");
		Pass();
	}

	private static void TestBackupRestoreAndProfileCopy(string root)
	{
		string profile = Path.Combine(root, "backup-profile");
		Directory.CreateDirectory(Path.Combine(profile, "world"));
		File.WriteAllText(Path.Combine(profile, "world", "level.dat"), "world-v1");
		File.WriteAllText(Path.Combine(profile, "server.properties"), "server-port=25565");
		string backup = Convert.ToString(Invoke("CreateComprehensiveServerBackup", new object[] { profile, 3, "test" }));
		File.WriteAllText(Path.Combine(profile, "server.properties"), "server-port=25566");
		Invoke("RestoreComprehensiveBackup", new object[] { profile, backup, 3 });
		Equal("server-port=25565", File.ReadAllText(Path.Combine(profile, "server.properties")), "백업 복원");

		string clone = Path.Combine(root, "backup-profile-clone");
		Invoke("CopyProfileDirectory", new object[] { profile, clone });
		Equal(true, File.Exists(Path.Combine(clone, "world", "level.dat")), "프로필 복제");
		Pass();
	}

	private static void TestManagedServerPortAndNetworkStatus(string root)
	{
		string profileDirectory = Path.Combine(root, "managed-port-profile");
		Directory.CreateDirectory(profileDirectory);
		File.WriteAllText(Path.Combine(profileDirectory, "server.properties"), "server-port=25565\r\n", new UTF8Encoding(false));
		Type profileType = launcher.GetNestedType("ManagedProfileRecord", BindingFlags.NonPublic);
		object profile = Activator.CreateInstance(profileType, true);
		SetPublic(profile, "Name", "포트 테스트");
		SetPublic(profile, "Directory", profileDirectory);
		SetPublic(profile, "Port", 25565);
		Type dashboardType = launcher.GetNestedType("MultiServerDashboardForm", BindingFlags.NonPublic);
		MethodInfo updatePort = dashboardType.GetMethod("UpdateManagedProfilePort", BindingFlags.Static | BindingFlags.NonPublic);
		updatePort.Invoke(null, new object[] { profile, 25566 });
		Equal(25566, GetField(profile, "Port"), "멀티 서버 자동 포트 변경 상태");
		Equal(25566, Convert.ToInt32(Invoke("ReadConfiguredServerPort", new object[] { Path.Combine(profileDirectory, "server.properties"), 25565 })), "멀티 서버 자동 포트 변경 저장");

		Type sessionType = launcher.GetNestedType("ManagedServerSession", BindingFlags.NonPublic);
		object session = Activator.CreateInstance(sessionType, true);
		SetPublic(session, "Profile", profile);
		Invoke("ParseManagedServerLine", new object[] { session, "[외부 접속] 외부 접속 실패" });
		Equal(Convert.ToString(Invoke("ManagedText", new object[] { "접속 불가", "Unreachable" })), Convert.ToString(GetField(session, "Status")), "포트포워딩 실패 상태");
		Invoke("ParseManagedServerLine", new object[] { session, "[외부 접속] UPnP 매핑 성공 · 203.0.113.10:25566" });
		Equal(Convert.ToString(Invoke("ManagedText", new object[] { "온라인", "Online" })), Convert.ToString(GetField(session, "Status")), "외부 접속 복구 상태");
		Equal("203.0.113.10:25566", Convert.ToString(GetField(session, "Address")), "외부 접속 주소 갱신");
		Pass();
	}

	private static void TestUpnpOwnershipRules()
	{
		Thread upnpThread = new Thread((ThreadStart)delegate { });
		Invoke("ConfigureExternalAccessThread", new object[] { upnpThread });
		Equal(ApartmentState.STA, upnpThread.GetApartmentState(), "UPnP COM 스레드 STA 설정");

		Type attemptType = launcher.GetNestedType("UpnpMappingAttempt", BindingFlags.NonPublic);
		object attempt = Activator.CreateInstance(attemptType, true);
		FakeMapping conflicting = new FakeMapping(25565, "TCP", 25565, "192.168.0.99", "Other service");
		FakeMappingCollection collection = new FakeMappingCollection(conflicting);
		attemptType.GetField("Collection", BindingFlags.Instance | BindingFlags.Public).SetValue(attempt, collection);
		using (ManualResetEvent stopped = new ManualResetEvent(false))
		{
			Equal(false, Invoke("TryAddSingleUpnpMapping", new object[] { attempt, 25565, 25565, "TCP", "192.168.0.10", "Minecraft test", stopped }), "UPnP 포트 충돌 차단");
		}
		Equal(true, GetField(attempt, "PortConflict"), "UPnP 충돌 상태");

		object ownedAttempt = Activator.CreateInstance(attemptType, true);
		FakeMapping owned = new FakeMapping(25566, "TCP", 25566, "192.168.0.10", "Minecraft owned");
		FakeMappingCollection ownedCollection = new FakeMappingCollection(owned);
		attemptType.GetField("Collection", BindingFlags.Instance | BindingFlags.Public).SetValue(ownedAttempt, ownedCollection);
		Type recordType = launcher.GetNestedType("CreatedUpnpMapping", BindingFlags.NonPublic);
		object record = Activator.CreateInstance(recordType, true);
		SetPublic(record, "ExternalPort", 25566);
		SetPublic(record, "InternalPort", 25566);
		SetPublic(record, "Protocol", "TCP");
		SetPublic(record, "InternalClient", "192.168.0.10");
		SetPublic(record, "Description", "Minecraft owned");
		((IList)GetField(ownedAttempt, "Created")).Add(record);
		Invoke("DeleteCreatedUpnpMappings", new object[] { ownedAttempt });
		Equal(1, ownedCollection.RemoveCount, "런처 소유 UPnP 매핑 삭제");

		object recoveredAttempt = Activator.CreateInstance(attemptType, true);
		FakeMappingCollection recoveredCollection = new FakeMappingCollection(null, true);
		attemptType.GetField("Collection", BindingFlags.Instance | BindingFlags.Public).SetValue(recoveredAttempt, recoveredCollection);
		using (ManualResetEvent stopped = new ManualResetEvent(false))
		{
			Equal(true, Invoke("TryAddSingleUpnpMapping", new object[] { recoveredAttempt, 25567, 25567, "TCP", "192.168.0.10", "MineHarbor recovered", stopped }), "응답 손실 후 생성된 UPnP 매핑 회수");
		}
		Equal(1, ((IList)GetField(recoveredAttempt, "Created")).Count, "회수한 매핑 소유권 기록");
		Invoke("DeleteCreatedUpnpMappings", new object[] { recoveredAttempt });
		Equal(1, recoveredCollection.RemoveCount, "응답 손실 후 회수한 매핑 삭제");
		Pass();
	}

	private static void TestModrinthHash(string root)
	{
		string file = Path.Combine(root, "modrinth.jar");
		File.WriteAllText(file, "modrinth verified content", Encoding.UTF8);
		string sha1;
		string sha512;
		using (SHA1 algorithm = SHA1.Create()) sha1 = BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(file))).Replace("-", string.Empty).ToLowerInvariant();
		using (SHA512 algorithm = SHA512.Create()) sha512 = BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(file))).Replace("-", string.Empty).ToLowerInvariant();
		Type fileType = launcher.GetNestedType("ModrinthFileInfo", BindingFlags.NonPublic);
		object info = Activator.CreateInstance(fileType, true);
		SetPublic(info, "Sha1", sha1);
		SetPublic(info, "Sha512", sha512);
		Equal(true, Invoke("VerifyModrinthHash", new object[] { file, info }), "Modrinth 해시 검증");
		SetPublic(info, "Sha512", new string('0', 128));
		Equal(false, Invoke("VerifyModrinthHash", new object[] { file, info }), "Modrinth 해시 불일치 차단");
		Pass();
	}

	private static void TestDiagnosticRedaction(string root)
	{
		string server = Path.Combine(root, "server");
		Directory.CreateDirectory(server);
		string input = "owner-name=SecretOwner\nserver-ip=192.168.0.10\nrcon.password=secret\npath=" + server;
		string redacted = Convert.ToString(Invoke("RedactDiagnosticText", new object[] { input, server }));
		if (redacted.Contains("SecretOwner") || redacted.Contains("192.168.0.10") || redacted.Contains("secret") || redacted.Contains(server)) throw new InvalidOperationException("진단 정보 민감값이 남아 있습니다.");
		Pass();
	}

	private static void TestQuickCommandsAndBridge(string root)
	{
		IEnumerable builtIns = (IEnumerable)Invoke("GetBuiltInQuickCommands", new object[0]);
		List<string> templates = new List<string>();
		foreach (object item in builtIns) templates.Add(Convert.ToString(GetField(item, "Template")));
		if (templates.Count < 45 || !templates.Contains("list") || !templates.Contains("save-all flush") || !templates.Contains("whitelist off") || !templates.Contains("datapack list")) throw new InvalidOperationException("기본 빠른 명령 목록이 완전하지 않습니다.");
		Type pickerLocalizationType = launcher.GetNestedType("Localization", BindingFlags.NonPublic);
		FieldInfo pickerLanguageField = pickerLocalizationType.GetField("CurrentLanguage", BindingFlags.Static | BindingFlags.Public);
		object originalPickerLanguage = pickerLanguageField.GetValue(null);
		try
		{
			pickerLanguageField.SetValue(null, "ko");
			IEnumerable koreanBuiltIns = (IEnumerable)Invoke("GetBuiltInQuickCommands", new object[0]);
			IEnumerable koreanPickerItems = (IEnumerable)Invoke("BuildQuickCommandPickerItems", new object[] { koreanBuiltIns, "paper" });
			object hardPickerItem = FindQuickCommandPickerItem(koreanPickerItems, "difficulty hard");
			Equal("world", Convert.ToString(GetField(hardPickerItem, "CategoryKey")), "빠른 명령 월드 카테고리");
			Equal("difficulty", Convert.ToString(GetField(hardPickerItem, "GroupKey")), "난이도 명령 그룹화");
			Equal("어려움", Convert.ToString(GetField(hardPickerItem, "LeafName")), "난이도 공식 한글 명칭");
			Equal("월드 › 난이도 › 어려움", Convert.ToString(GetField(hardPickerItem, "CategoryName")) + " › " + Convert.ToString(GetField(hardPickerItem, "GroupName")) + " › " + Convert.ToString(GetField(hardPickerItem, "LeafName")), "빠른 명령 계층 경로");
			int weatherCount = 0;
			int difficultyCount = 0;
			foreach (object pickerItem in koreanPickerItems)
			{
				string group = Convert.ToString(GetField(pickerItem, "GroupKey"));
				if (group == "weather") weatherCount++;
				if (group == "difficulty") difficultyCount++;
			}
			Equal(3, weatherCount, "날씨 명령 그룹 개수");
			Equal(4, difficultyCount, "난이도 명령 그룹 개수");
			Dictionary<string, object> bridgeMessage = new Dictionary<string, object>();
			bridgeMessage["commands"] = new object[]
			{
				new Dictionary<string, object> { { "name", "home" }, { "usage", "/home" }, { "description", "Return home" }, { "plugin", "EssentialsX" } },
				new Dictionary<string, object> { { "name", "version" }, { "usage", "/version" }, { "description", "Server version" }, { "plugin", "" } }
			};
			IEnumerable parsedBridgeCommands = (IEnumerable)Invoke("ParseBridgeCommands", new object[] { bridgeMessage });
			object homeSuggestion = FindSuggestion(parsedBridgeCommands, "home");
			Equal("EssentialsX", Convert.ToString(GetField(homeSuggestion, "Plugin")), "브리지 플러그인 소유자 보존");
			IEnumerable bridgeDefinitions = (IEnumerable)Invoke("BuildBridgeQuickCommandDefinitions", new object[] { parsedBridgeCommands });
			int bridgeDefinitionCount = 0; foreach (object ignored in bridgeDefinitions) bridgeDefinitionCount++;
			Equal(1, bridgeDefinitionCount, "소유자 없는 서버 명령 플러그인 목록 중복 차단");
			IEnumerable pluginPickerItems = (IEnumerable)Invoke("BuildQuickCommandPickerItems", new object[] { bridgeDefinitions, "paper" });
			object homePickerItem = FindQuickCommandPickerItem(pluginPickerItems, "home");
			Equal("plugin", Convert.ToString(GetField(homePickerItem, "CategoryKey")), "외부 명령 플러그인 카테고리");
			Equal("EssentialsX", Convert.ToString(GetField(homePickerItem, "GroupName")), "외부 명령 플러그인별 그룹화");
			Equal("플러그인 › EssentialsX › home", Convert.ToString(GetField(homePickerItem, "CategoryName")) + " › " + Convert.ToString(GetField(homePickerItem, "GroupName")) + " › " + Convert.ToString(GetField(homePickerItem, "LeafName")), "외부 명령 계층 경로");
			pickerLanguageField.SetValue(null, "en");
			IEnumerable englishBuiltIns = (IEnumerable)Invoke("GetBuiltInQuickCommands", new object[0]);
			IEnumerable englishPickerItems = (IEnumerable)Invoke("BuildQuickCommandPickerItems", new object[] { englishBuiltIns, "paper" });
			object englishHardPickerItem = FindQuickCommandPickerItem(englishPickerItems, "difficulty hard");
			Equal("World › Difficulty › Hard", Convert.ToString(GetField(englishHardPickerItem, "CategoryName")) + " › " + Convert.ToString(GetField(englishHardPickerItem, "GroupName")) + " › " + Convert.ToString(GetField(englishHardPickerItem, "LeafName")), "영어 빠른 명령 계층 경로");
		}
		finally
		{
			pickerLanguageField.SetValue(null, originalPickerLanguage);
		}

		Type definitionType = launcher.GetNestedType("QuickCommandDefinition", BindingFlags.NonPublic);
		Type definitionListType = typeof(List<>).MakeGenericType(definitionType);
		IList userCommands = (IList)Activator.CreateInstance(definitionListType);
		object command = Activator.CreateInstance(definitionType, true);
		SetPublic(command, "Id", "test-command");
		SetPublic(command, "Name", "테스트 지급");
		SetPublic(command, "Description", "테스트 설명");
		SetPublic(command, "Category", "user");
		SetPublic(command, "Template", "give {online-player} {item} {count}");
		SetPublic(command, "Confirm", false);
		SetPublic(command, "ServerTypes", new string[] { "paper" });
		object[] valid = { command, null };
		Equal(true, Invoke("ValidateUserQuickCommand", valid), "사용자 명령 템플릿 검증");
		userCommands.Add(command);
		string dataRoot = Path.Combine(root, "quick-command-data");
		Invoke("SaveUserQuickCommands", new object[] { dataRoot, userCommands });
		IEnumerable loaded = (IEnumerable)Invoke("LoadUserQuickCommands", new object[] { dataRoot });
		int loadedCount = 0; foreach (object ignored in loaded) loadedCount++;
		Equal(1, loadedCount, "사용자 명령 저장과 로드");
		SetPublic(command, "Description", "수정된 설명");
		Invoke("SaveUserQuickCommands", new object[] { dataRoot, userCommands });
		userCommands.Clear();
		Invoke("SaveUserQuickCommands", new object[] { dataRoot, userCommands });
		loaded = (IEnumerable)Invoke("LoadUserQuickCommands", new object[] { dataRoot });
		loadedCount = 0; foreach (object ignored in loaded) loadedCount++;
		Equal(0, loadedCount, "사용자 명령 삭제");
		SetPublic(command, "Template", "say {unsupported}");
		object[] invalid = { command, null };
		Equal(false, Invoke("ValidateUserQuickCommand", invalid), "지원하지 않는 템플릿 매개변수 차단");

		object parse = Invoke("ParseCommandInput", new object[] { "say \"hello world\"", 10 });
		IList tokens = (IList)GetField(parse, "Tokens");
		Equal(2, tokens.Count, "따옴표 명령 토큰 처리");
		IEnumerable suggestions = (IEnumerable)Invoke("GetLocalQuickCommandSuggestions", new object[] { "gam", 3, "paper", Activator.CreateInstance(definitionListType), new string[] { "Alex" }, new string[0] });
		Equal(true, SuggestionContains(suggestions, "gamemode"), "루트 명령 자동완성");
		suggestions = (IEnumerable)Invoke("GetLocalQuickCommandSuggestions", new object[] { "gamemode c", 10, "paper", Activator.CreateInstance(definitionListType), new string[] { "Alex" }, new string[0] });
		Equal(true, SuggestionContains(suggestions, "creative"), "하위 명령 자동완성");
		suggestions = (IEnumerable)Invoke("GetLocalQuickCommandSuggestions", new object[] { "op A", 4, "paper", Activator.CreateInstance(definitionListType), new string[] { "Alex" }, new string[0] });
		Equal(true, SuggestionContains(suggestions, "Alex"), "온라인 플레이어 후보");
		suggestions = (IEnumerable)Invoke("GetLocalQuickCommandSuggestions", new object[] { "sa", 2, "paper", Activator.CreateInstance(definitionListType), new string[0], new string[] { "say custom recent" } });
		object historySuggestion = FindSuggestion(suggestions, "say custom recent");
		Equal(2, Convert.ToInt32(GetField(historySuggestion, "ReplaceLength")), "명령 기록 전체 입력 교체");
		Equal("say hello", Convert.ToString(Invoke("NormalizeCommandForSend", new object[] { "/say hello\r\n" })), "전송 전 슬래시 제거");
		Equal(true, Invoke("RequiresQuickCommandConfirmation", new object[] { "whitelist off", Activator.CreateInstance(definitionListType) }), "위험 명령 확인");
		Equal(false, Invoke("CanSendQuickCommand", new object[] { false, "list" }), "서버 미실행 명령 차단");
		Equal(true, Invoke("IsSuggestionGenerationCurrent", new object[] { 4, 4 }), "최신 자동완성 응답 허용");
		Equal(false, Invoke("IsSuggestionGenerationCurrent", new object[] { 3, 4 }), "오래된 자동완성 응답 무시");
		Equal(1, Invoke("GetNextQuickCommandSuggestionIndex", new object[] { 0, 3, false }), "탭으로 다음 자동완성 후보 이동");
		Equal(0, Invoke("GetNextQuickCommandSuggestionIndex", new object[] { 2, 3, false }), "마지막 후보에서 첫 후보로 순환");
		Equal(2, Invoke("GetNextQuickCommandSuggestionIndex", new object[] { 0, 3, true }), "Shift+Tab으로 이전 후보 이동");
		Equal(-1, Invoke("GetNextQuickCommandSuggestionIndex", new object[] { -1, 0, false }), "빈 자동완성 목록 처리");
		Type suggestionType = launcher.GetNestedType("QuickCommandSuggestion", BindingFlags.NonPublic);
		Type suggestionListType = typeof(List<>).MakeGenericType(suggestionType);
		IList bridgeSuggestions = (IList)Activator.CreateInstance(suggestionListType);
		IList localSuggestions = (IList)Activator.CreateInstance(suggestionListType);
		bridgeSuggestions.Add(Invoke("NewSuggestion", new object[] { "live", "live", "", "bridge", false }));
		localSuggestions.Add(Invoke("NewSuggestion", new object[] { "user", "user", "", "user", false }));
		localSuggestions.Add(Invoke("NewSuggestion", new object[] { "builtin", "builtin", "", "builtin", false }));
		IList mergedSuggestions = (IList)Invoke("MergeQuickCommandSuggestions", new object[] { bridgeSuggestions, localSuggestions, 10 });
		Equal("bridge", Convert.ToString(GetField(mergedSuggestions[0], "Source")), "실시간 후보 우선 정렬");
		Equal("user", Convert.ToString(GetField(mergedSuggestions[1], "Source")), "사용자 후보 다음 정렬");
		object[] historyArguments = { new List<string>(new string[] { "list", "save-all" }), -1, -1, null };
		Equal("save-all", Convert.ToString(Invoke("GetQuickCommandHistoryValue", historyArguments)), "명령 기록 탐색");

		Equal(true, Invoke("IsCommandBridgeSupported", new object[] { "paper", "1.13" }), "Paper 브리지 최소 버전");
		Equal(true, Invoke("IsCommandBridgeSupported", new object[] { "purpur", "26.2" }), "Purpur 브리지 지원");
		Equal(false, Invoke("IsCommandBridgeSupported", new object[] { "vanilla", "1.21.11" }), "지원하지 않는 서버 브리지 제안 차단");
		Equal(true, Invoke("IsMinecraftVersionInBridgeRange", new object[] { "1.21.11", "1.13", "26.2" }), "브리지 Minecraft 호환 범위");
		Equal(false, Invoke("IsMinecraftVersionInBridgeRange", new object[] { "1.12.2", "1.13", "26.2" }), "브리지 최소 버전 차단");
		string profileA = Path.Combine(root, "bridge-a");
		string profileB = Path.Combine(root, "bridge-b");
		Directory.CreateDirectory(profileA); Directory.CreateDirectory(profileB);
		Invoke("WriteBridgeChoice", new object[] { profileA, "install" });
		Invoke("WriteBridgeChoice", new object[] { profileB, "skip" });
		Equal("install", Convert.ToString(GetField(Invoke("ReadBridgeChoice", new object[] { profileA }), "Choice")), "프로필별 브리지 설치 동의");
		Equal("skip", Convert.ToString(GetField(Invoke("ReadBridgeChoice", new object[] { profileB }), "Choice")), "프로필별 브리지 거절 분리");
		Invoke("WriteBridgeDefaultPreference", new object[] { dataRoot, "install" });
		object defaultPreference = Invoke("ReadBridgeDefaultPreference", new object[] { dataRoot });
		Equal(true, GetField(defaultPreference, "HasDefault"), "새 프로필 브리지 기본 선택 저장");
		Equal("install", Convert.ToString(GetField(defaultPreference, "Choice")), "브리지 기본 동의 값");

		string firstJar = Path.Combine(root, "bridge-first.jar");
		string secondJar = Path.Combine(root, "bridge-second.jar");
		CreateFakeBridgeJar(firstJar, "first");
		CreateFakeBridgeJar(secondJar, "second");
		string firstHash = FileSha256(firstJar);
		Equal(false, Invoke("ValidateCommandBridgeArtifact", new object[] { firstJar, new FileInfo(firstJar).Length, new string('0', 64) }), "브리지 JAR 해시 불일치 차단");
		SetStaticField("BridgeArtifactOverridePath", firstJar);
		Invoke("InstallOrUpdateCommandBridge", new object[] { profileA, "paper", "1.21.11" });
		string installedJar = Convert.ToString(Invoke("GetBridgeJarPath", new object[] { profileA }));
		Equal(firstHash, FileSha256(installedJar), "검증된 브리지 설치");
		SetStaticField("BridgeArtifactOverridePath", secondJar);
		File.AppendAllText(installedJar, "changed");
		ExpectFailure(delegate { Invoke("InstallOrUpdateCommandBridge", new object[] { profileA, "paper", "1.21.11" }); }, "사용자가 변경한 관리 JAR 덮어쓰기 차단");
		File.Copy(firstJar, installedJar, true);
		SetStaticField("BridgeInstallFailureAfterBackup", true);
		ExpectFailure(delegate { Invoke("InstallOrUpdateCommandBridge", new object[] { profileA, "paper", "1.21.11" }); }, "브리지 업데이트 실패 주입");
		SetStaticField("BridgeInstallFailureAfterBackup", false);
		Equal(firstHash, FileSha256(installedJar), "업데이트 실패 시 기존 브리지 복구");
		string bridgeData = Path.Combine(profileA, "plugins", "MinecraftServerLauncherCommandBridge");
		Directory.CreateDirectory(bridgeData); File.WriteAllText(Path.Combine(bridgeData, "keep.txt"), "keep");
		Invoke("RemoveManagedCommandBridge", new object[] { profileA, false });
		Equal(true, File.Exists(Path.Combine(bridgeData, "keep.txt")), "브리지 제거 시 사용자 데이터 보존");

		string sessionDirectory = Path.Combine(root, "bridge-session");
		Directory.CreateDirectory(sessionDirectory);
		Type sessionType = launcher.GetNestedType("CommandBridgeSession", BindingFlags.NonPublic);
		object session = Activator.CreateInstance(sessionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { sessionDirectory, "profile-test" }, null);
		try
		{
			TcpListener listener = (TcpListener)sessionType.GetField("listener", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
			IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
			Equal(true, IPAddress.IsLoopback(endpoint.Address), "브리지 리스너 루프백 바인딩");
			using (TcpClient client = new TcpClient())
			{
				client.Connect(IPAddress.Loopback, endpoint.Port);
				using (StreamWriter writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false), 1024, true))
				using (StreamReader reader = new StreamReader(client.GetStream(), Encoding.UTF8, false, 1024, true))
				{
					writer.AutoFlush = true;
					writer.WriteLine("{\"type\":\"hello\",\"id\":\"bad\",\"token\":\"wrong-token\",\"profile\":\"profile-test\",\"protocol\":1}");
					string response = reader.ReadLine();
					if (response == null || response.IndexOf("handshake-rejected", StringComparison.Ordinal) < 0) throw new InvalidOperationException("브리지 토큰 불일치가 거부되지 않았습니다.");
				}
			}
		}
		finally { ((IDisposable)session).Dispose(); }
		ExpectFailure(delegate { Invoke("DeserializeBridgeObject", new object[] { new string('x', 65537) }); }, "과도한 브리지 요청 차단");
		Equal(true, Invoke("BridgeTokenEquals", new object[] { "same-token", "same-token" }), "고정 시간 토큰 비교");
		Equal(false, Invoke("BridgeTokenEquals", new object[] { "same-token", "other-token" }), "토큰 불일치 거부");
		Pass();
	}

	private static bool SuggestionContains(IEnumerable suggestions, string value)
	{
		foreach (object item in suggestions) if (string.Equals(Convert.ToString(GetField(item, "Value")), value, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	private static object FindQuickCommandPickerItem(IEnumerable items, string template)
	{
		foreach (object item in items)
		{
			object definition = GetField(item, "Definition");
			if (string.Equals(Convert.ToString(GetField(definition, "Template")), template, StringComparison.OrdinalIgnoreCase)) return item;
		}
		throw new InvalidOperationException("빠른 명령 선택 항목을 찾지 못했습니다: " + template);
	}

	private static object FindSuggestion(IEnumerable suggestions, string value)
	{
		foreach (object item in suggestions) if (string.Equals(Convert.ToString(GetField(item, "Value")), value, StringComparison.OrdinalIgnoreCase)) return item;
		throw new InvalidOperationException("자동완성 후보를 찾지 못했습니다: " + value);
	}

	private static void CreateFakeBridgeJar(string path, string marker)
	{
		if (File.Exists(path)) File.Delete(path);
		using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
		{
			using (StreamWriter writer = new StreamWriter(archive.CreateEntry("plugin.yml").Open())) writer.Write("name: TestBridge\nmain: test.CommandBridgePlugin\n" + marker);
			using (Stream stream = archive.CreateEntry("test/CommandBridgePlugin.class").Open()) { byte[] bytes = Encoding.UTF8.GetBytes(marker); stream.Write(bytes, 0, bytes.Length); }
		}
	}

	private static string FileSha256(string path)
	{
		using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant();
	}

	private static object Invoke(string name, object[] arguments)
	{
		MethodInfo method = launcher.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
		if (method == null) throw new MissingMethodException(name);
		return method.Invoke(null, arguments);
	}

	private static object GetField(object instance, string name) { return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(instance); }
	private static object GetPrivateField(Type type, object instance, string name) { return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
	private static void SetStaticField(string name, object value) { launcher.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value); }
	private static void SetPublic(object instance, string name, object value) { instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public).SetValue(instance, value); }
	private static void Pass() { passed++; }
	private static void Equal(object expected, object actual, string name) { if (!object.Equals(expected, actual)) throw new InvalidOperationException(name + ": expected=" + expected + ", actual=" + actual); }
	private static void ExpectFailure(Action action, string name)
	{
		try { action(); }
		catch { return; }
		throw new InvalidOperationException(name + " 검사가 실패를 차단하지 못했습니다.");
	}

	public sealed class FakeMapping
	{
		public int ExternalPort { get; private set; }
		public string Protocol { get; private set; }
		public int InternalPort { get; private set; }
		public string InternalClient { get; private set; }
		public string Description { get; private set; }
		public string ExternalIPAddress { get { return "203.0.113.10"; } }

		public FakeMapping(int externalPort, string protocol, int internalPort, string internalClient, string description)
		{
			ExternalPort = externalPort;
			Protocol = protocol;
			InternalPort = internalPort;
			InternalClient = internalClient;
			Description = description;
		}
	}

	public sealed class FakeMappingCollection : IEnumerable
	{
		private FakeMapping mapping;
		private bool throwAfterAddOnce;
		public int RemoveCount { get; private set; }
		public FakeMappingCollection(FakeMapping value) : this(value, false) { }
		public FakeMappingCollection(FakeMapping value, bool throwAfterAdd) { mapping = value; throwAfterAddOnce = throwAfterAdd; }
		public FakeMapping this[int port, string protocol]
		{
			get { return mapping != null && mapping.ExternalPort == port && string.Equals(mapping.Protocol, protocol, StringComparison.OrdinalIgnoreCase) ? mapping : null; }
		}
		public object Add(int externalPort, string protocol, int internalPort, string internalClient, bool enabled, string description)
		{
			mapping = new FakeMapping(externalPort, protocol, internalPort, internalClient, description);
			if (throwAfterAddOnce)
			{
				throwAfterAddOnce = false;
				throw new InvalidOperationException("공유기가 매핑 생성 후 응답을 잃었습니다.");
			}
			return mapping;
		}
		public void Remove(int externalPort, string protocol) { RemoveCount++; mapping = null; }
		public IEnumerator GetEnumerator() { return new object[] { mapping }.GetEnumerator(); }
	}
}
