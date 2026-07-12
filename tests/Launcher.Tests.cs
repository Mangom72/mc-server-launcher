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
			TestHashAndReplacement(temporary);
			TestMockedDownload(temporary);
			TestDataLocations(temporary);
			TestSetupLayoutAndValidation();
			TestUxAccessibility();
			TestJavaSelection();
			TestBackupRestoreAndProfileCopy(temporary);
			TestUpnpOwnershipRules();
			TestModrinthHash(temporary);
			TestDiagnosticRedaction(temporary);
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
		string json = "{\"version\":\"0.3.2\",\"build\":\"26.2.45.26\",\"download_url\":\"https://github.com/Mangom72/mc-server-launcher/releases/download/v0.3.2/Minecraft-Server-Launcher.exe\",\"sha256\":\"" + hash + "\",\"size\":2097152,\"release_notes\":\"test\",\"minimum_supported_version\":\"0.1.0\"}";
		object metadata = Invoke("ParseLauncherUpdateMetadata", new object[] { json });
		Equal("0.3.2", Convert.ToString(GetField(metadata, "ProductVersion")), "업데이트 제품 버전");
		Equal("26.2.45.26", Convert.ToString(GetField(metadata, "BuildNumber")), "업데이트 빌드");
		Equal(true, Invoke("IsLauncherUpdateNewer", new object[] { metadata, "0.3.1", "26.2.45.25" }), "새 제품 버전 판별");
		Equal(false, Invoke("IsLauncherUpdateNewer", new object[] { metadata, "0.3.2", "26.2.45.26" }), "최신 버전 판별");
		ExpectFailure(delegate { Invoke("ParseLauncherUpdateMetadata", new object[] { "{}" }); }, "누락된 업데이트 메타데이터");
		ExpectFailure(delegate { Invoke("ParseLauncherUpdateMetadata", new object[] { json.Replace(hash, "bad") }); }, "잘못된 업데이트 해시");
		ExpectFailure(delegate { Invoke("ParseLauncherUpdateMetadata", new object[] { json.Replace("https://github.com/Mangom72/", "http://example.com/") }); }, "허용되지 않은 업데이트 주소");
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
		string installedExe = Path.Combine(installedDirectory, "Minecraft-Server-Launcher.exe");
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
			if (body == null || body.AutoScrollMinSize.Height < 700) throw new InvalidOperationException("작은 화면 스크롤 영역이 없습니다.");
			Equal(0, body.AutoScrollMinSize.Width, "설정 화면 가로 스크롤 방지");
			NumericUpDown port = (NumericUpDown)GetPrivateField(formType, form, "portBox");
			NumericUpDown memory = (NumericUpDown)GetPrivateField(formType, form, "memoryBox");
			Equal(1m, port.Minimum, "포트 최소값");
			Equal(65535m, port.Maximum, "포트 최대값");
			Equal(2m, memory.Minimum, "메모리 최소값");
			if (string.IsNullOrEmpty(port.AccessibleName)) throw new InvalidOperationException("포트 입력의 접근성 이름이 없습니다.");
			Label validation = (Label)GetPrivateField(formType, form, "validationLabel");
			Equal(AccessibleRole.Alert, validation.AccessibleRole, "설정 오류 접근성 역할");

			ComboBox serverType = (ComboBox)GetPrivateField(formType, form, "serverTypeBox");
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
		}
		string startDescription = Convert.ToString(Invoke("GetCommonButtonDescription", new object[] { "서버 시작하기" }));
		if (startDescription.IndexOf("F5", StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidOperationException("시작 단축키 안내가 없습니다.");
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

	private static void TestUpnpOwnershipRules()
	{
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
		public int RemoveCount { get; private set; }
		public FakeMappingCollection(FakeMapping value) { mapping = value; }
		public FakeMapping this[int port, string protocol]
		{
			get { return mapping != null && mapping.ExternalPort == port && string.Equals(mapping.Protocol, protocol, StringComparison.OrdinalIgnoreCase) ? mapping : null; }
		}
		public object Add(int externalPort, string protocol, int internalPort, string internalClient, bool enabled, string description)
		{
			mapping = new FakeMapping(externalPort, protocol, internalPort, internalClient, description);
			return mapping;
		}
		public void Remove(int externalPort, string protocol) { RemoveCount++; mapping = null; }
		public IEnumerator GetEnumerator() { return new object[] { mapping }.GetEnumerator(); }
	}
}
