using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal static partial class Launcher
{
	private sealed class JavaRuntimeRequirement
	{
		public int MajorVersion;
		public bool RequiresUserChoice;
		public bool UsedSafeFallback;
		public string ResolutionSource;
		public string Guidance;
	}

	private sealed class CompatibleJavaRuntime
	{
		public string JavaPath;
		public int MajorVersion;
		public bool Is64Bit;
		public bool IsBundled;
		public bool UsedSystemJava;
		public bool RequiresUserChoice;
		public bool UsedSafeFallback;
		public string Source;
		public string Guidance;
	}

	private sealed class AdoptiumRuntimePackage
	{
		public string Link;
		public string Checksum;
		public string Name;
		public string ImageType;
		public long Size;
	}

	private sealed class JavaExecutableProbe
	{
		public bool IsValid;
		public int MajorVersion;
		public bool Is64Bit;
		public string Output;
		public string Error;
	}

	private sealed class MinecraftNumericVersion
	{
		public int First;
		public int Second;
		public int Third;
	}

	private static readonly object RuntimeCompatibilityPreparationLock = new object();
	private static readonly object RuntimeCompatibilityMojangCacheLock = new object();
	private static readonly Dictionary<string, int> RuntimeCompatibilityMojangJavaCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private const string RuntimeCompatibilityUserAgent = "MineHarbor/1.6 (runtime-compatibility)";
	private const int RuntimeCompatibilityMetadataTimeoutMilliseconds = 15000;
	private const int RuntimeCompatibilityDownloadTimeoutMilliseconds = 60000;
	private const long RuntimeCompatibilityMaximumPackageBytes = 1073741824L;
	private const long RuntimeCompatibilityMaximumExtractedBytes = 2147483648L;
	private const int RuntimeCompatibilityMaximumZipEntries = 100000;

	private static readonly string[] RuntimeCompatibilityMojangMetadataHosts = new string[]
	{
		"piston-meta.mojang.com",
		"launchermeta.mojang.com"
	};

	private static readonly string[] RuntimeCompatibilityAdoptiumApiHosts = new string[]
	{
		"api.adoptium.net"
	};

	private static readonly string[] RuntimeCompatibilityAdoptiumDownloadHosts = new string[]
	{
		"api.adoptium.net",
		"github.com",
		"objects.githubusercontent.com",
		"release-assets.githubusercontent.com",
		"github-releases.githubusercontent.com",
		"download.eclipse.org"
	};

	// 기본 통합 진입점입니다. 직접 JAR은 Java 25를 임시 기본값으로 사용하되 선택 필요 상태를 반환합니다.
	private static CompatibleJavaRuntime PrepareCompatibleJavaRuntime(LauncherOptions options, string serversRoot)
	{
		return PrepareCompatibleJavaRuntime(options, serversRoot, 0);
	}

	// 직접 JAR 설정 UI가 추가되면 customJavaMajor에 사용자가 고른 값을 전달할 수 있습니다.
	private static CompatibleJavaRuntime PrepareCompatibleJavaRuntime(LauncherOptions options, string serversRoot, int customJavaMajor)
	{
		if (options == null)
		{
			throw new ArgumentNullException("options");
		}
		if (!Environment.Is64BitOperatingSystem)
		{
			throw new PlatformNotSupportedException("이 런처가 준비하는 Minecraft Java 서버 런타임은 64비트 Windows가 필요합니다.");
		}

		JavaRuntimeRequirement requirement = ResolveCompatibleJavaRequirement(options, customJavaMajor);
		List<string> preparationErrors = new List<string>();
		if (requirement.MajorVersion == 25)
		{
			try
			{
				string bundledJavaPath = FindLegacyJava25Runtime();
				JavaExecutableProbe bundledProbe = ProbeJavaExecutable(bundledJavaPath, requirement.MajorVersion);
				if (!bundledProbe.IsValid)
				{
					throw new InvalidDataException("기존 Java 25 캐시 검증 실패: " + bundledProbe.Error);
				}
				return CreateCompatibleJavaRuntime(requirement, bundledJavaPath, bundledProbe, "기존 Java 25 캐시", false, false);
			}
			catch (Exception ex)
			{
				preparationErrors.Add("기존 Java 25 캐시: " + SummarizeRuntimeCompatibilityError(ex));
			}
		}
		try
		{
			string runtimesRoot = GetCompatibleRuntimesRoot(serversRoot);
			lock (RuntimeCompatibilityPreparationLock)
			{
				CompatibleJavaRuntime cachedRuntime = TryGetCachedCompatibleRuntime(requirement, runtimesRoot);
				if (cachedRuntime != null)
				{
					return cachedRuntime;
				}
				string downloadedJavaPath = PrepareAdoptiumRuntime(requirement.MajorVersion, runtimesRoot);
				JavaExecutableProbe downloadedProbe = ProbeJavaExecutable(downloadedJavaPath, requirement.MajorVersion);
				if (!downloadedProbe.IsValid)
				{
					throw new InvalidDataException("다운로드한 Java 검증 실패: " + downloadedProbe.Error);
				}
				return CreateCompatibleJavaRuntime(requirement, downloadedJavaPath, downloadedProbe, "Eclipse Temurin 캐시", false, false);
			}
		}
		catch (Exception ex2)
		{
			preparationErrors.Add("Eclipse Adoptium 런타임: " + SummarizeRuntimeCompatibilityError(ex2));
		}

		List<string> systemDiagnostics;
		JavaExecutableProbe systemProbe;
		string systemJavaPath = FindCompatibleSystemJava(requirement.MajorVersion, out systemProbe, out systemDiagnostics);
		if (!string.IsNullOrEmpty(systemJavaPath))
		{
			return CreateCompatibleJavaRuntime(requirement, systemJavaPath, systemProbe, "시스템 Java", false, true);
		}

		StringBuilder error = new StringBuilder();
		error.Append("Java ").Append(requirement.MajorVersion).Append(" 64비트 런타임을 준비하지 못했습니다.");
		for (int i = 0; i < preparationErrors.Count; i = checked(i + 1))
		{
			error.AppendLine().Append("- ").Append(preparationErrors[i]);
		}
		if (systemDiagnostics != null && systemDiagnostics.Count > 0)
		{
			error.AppendLine().Append("- 확인한 시스템 Java: ").Append(string.Join("; ", systemDiagnostics.ToArray()));
		}
		else
		{
			error.AppendLine().Append("- PATH, JAVA_HOME 및 일반 설치 위치에서 시스템 Java를 찾지 못했습니다.");
		}
		error.AppendLine().Append("Java ").Append(requirement.MajorVersion).Append(" 64비트를 설치하거나 인터넷 연결 후 다시 시도해 주세요.");
		if (!string.IsNullOrWhiteSpace(requirement.Guidance))
		{
			error.AppendLine().Append(requirement.Guidance);
		}
		throw new InvalidOperationException(error.ToString());
	}

	private static CompatibleJavaRuntime CreateCompatibleJavaRuntime(JavaRuntimeRequirement requirement, string javaPath, JavaExecutableProbe probe, string source, bool isBundled, bool usedSystemJava)
	{
		CompatibleJavaRuntime result = new CompatibleJavaRuntime();
		result.JavaPath = javaPath;
		result.MajorVersion = probe.MajorVersion;
		result.Is64Bit = probe.Is64Bit;
		result.IsBundled = isBundled;
		result.UsedSystemJava = usedSystemJava;
		result.RequiresUserChoice = requirement.RequiresUserChoice;
		result.UsedSafeFallback = requirement.UsedSafeFallback;
		result.Source = source + " / " + requirement.ResolutionSource;
		result.Guidance = requirement.Guidance;
		return result;
	}

	private static JavaRuntimeRequirement ResolveCompatibleJavaRequirement(LauncherOptions options, int customJavaMajor)
	{
		string serverType = string.IsNullOrWhiteSpace(options.ServerType) ? "paper" : options.ServerType.Trim().ToLowerInvariant();
		string minecraftVersion = string.IsNullOrWhiteSpace(options.MinecraftVersion) ? string.Empty : options.MinecraftVersion.Trim();
		bool isCustomRuntime = options.UseManualJar || string.Equals(serverType, "custom", StringComparison.OrdinalIgnoreCase);
		if (isCustomRuntime)
		{
			JavaRuntimeRequirement customRequirement = new JavaRuntimeRequirement();
			if (customJavaMajor == 0)
			{
				customRequirement.MajorVersion = 25;
				customRequirement.RequiresUserChoice = true;
				customRequirement.UsedSafeFallback = true;
				customRequirement.ResolutionSource = "직접 JAR 기본값";
				customRequirement.Guidance = "직접 지정한 JAR의 Java 요구 버전은 자동 확정할 수 없습니다. 설정 화면에서 제작자가 안내한 Java 버전을 선택할 수 있게 연결해야 하며, 현재는 Java 25를 기본값으로 사용합니다.";
				return customRequirement;
			}
			if (customJavaMajor < 8 || customJavaMajor > 30)
			{
				throw new ArgumentOutOfRangeException("customJavaMajor", "직접 JAR의 Java 주 버전은 8부터 30 사이여야 합니다.");
			}
			customRequirement.MajorVersion = customJavaMajor;
			customRequirement.RequiresUserChoice = false;
			customRequirement.UsedSafeFallback = false;
			customRequirement.ResolutionSource = "직접 JAR 사용자 선택";
			customRequirement.Guidance = "직접 JAR 제작자가 요구하는 Java " + customJavaMajor + " 버전과 일치하는지 확인해 주세요.";
			return customRequirement;
		}

		if (string.Equals(serverType, "paper", StringComparison.OrdinalIgnoreCase) || string.Equals(serverType, "purpur", StringComparison.OrdinalIgnoreCase))
		{
			int paperMajor = ResolvePaperFamilyJavaMajor(minecraftVersion);
			if (paperMajor > 0)
			{
				JavaRuntimeRequirement paperRequirement = new JavaRuntimeRequirement();
				paperRequirement.MajorVersion = paperMajor;
				paperRequirement.ResolutionSource = "Paper/Purpur 공식 호환 매트릭스";
				paperRequirement.Guidance = "선택한 " + serverType + " " + minecraftVersion + "에는 Java " + paperMajor + "를 사용합니다.";
				return paperRequirement;
			}
		}

		try
		{
			int metadataMajor = GetMojangMetadataJavaMajor(minecraftVersion);
			JavaRuntimeRequirement metadataRequirement = new JavaRuntimeRequirement();
			metadataRequirement.MajorVersion = metadataMajor;
			metadataRequirement.ResolutionSource = "Mojang version metadata";
			metadataRequirement.Guidance = "Mojang 메타데이터가 지정한 Java " + metadataMajor + "를 사용합니다.";
			return metadataRequirement;
		}
		catch (Exception ex)
		{
			int fallbackMajor = ResolveSafeFallbackJavaMajor(minecraftVersion);
			JavaRuntimeRequirement fallbackRequirement = new JavaRuntimeRequirement();
			fallbackRequirement.MajorVersion = fallbackMajor;
			fallbackRequirement.UsedSafeFallback = true;
			fallbackRequirement.ResolutionSource = "Minecraft 버전 기반 안전 기본값";
			fallbackRequirement.Guidance = "Mojang Java 메타데이터를 확인하지 못해 Java " + fallbackMajor + "를 안전 기본값으로 선택했습니다. 원인: " + SummarizeRuntimeCompatibilityError(ex);
			return fallbackRequirement;
		}
	}

	private static int ResolvePaperFamilyJavaMajor(string minecraftVersion)
	{
		MinecraftNumericVersion version;
		if (!TryParseMinecraftNumericVersion(minecraftVersion, out version))
		{
			return 0;
		}
		if (version.First >= 26 && (version.First > 26 || version.Second >= 1))
		{
			return 25;
		}
		if (version.First != 1)
		{
			return 0;
		}
		if (version.Second == 20 || (version.Second == 21 && version.Third <= 11))
		{
			return 21;
		}
		if (version.Second >= 17 && version.Second <= 19)
		{
			return 17;
		}
		if (version.Second == 16 && version.Third == 5)
		{
			return 16;
		}
		if (version.Second >= 12 && (version.Second < 16 || version.Third <= 4))
		{
			return 11;
		}
		if ((version.Second == 7 && version.Third >= 10) || (version.Second >= 8 && version.Second <= 11))
		{
			return 8;
		}
		return 0;
	}

	private static int ResolveSafeFallbackJavaMajor(string minecraftVersion)
	{
		Match snapshotMatch = Regex.Match(minecraftVersion ?? string.Empty, "^(?<year>[0-9]{2})w", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (snapshotMatch.Success)
		{
			int snapshotYear;
			if (int.TryParse(snapshotMatch.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out snapshotYear))
			{
				if (snapshotYear >= 26)
				{
					return 25;
				}
				if (snapshotYear >= 24)
				{
					return 21;
				}
				if (snapshotYear >= 22)
				{
					return 17;
				}
			}
		}
		MinecraftNumericVersion version;
		if (!TryParseMinecraftNumericVersion(minecraftVersion, out version))
		{
			return 25;
		}
		if (version.First >= 26)
		{
			return 25;
		}
		if (version.First == 1)
		{
			if (version.Second > 20 || (version.Second == 20 && version.Third >= 5))
			{
				return 21;
			}
			if (version.Second >= 18)
			{
				return 17;
			}
			if (version.Second == 17)
			{
				return 16;
			}
			return 8;
		}
		return 25;
	}

	private static bool TryParseMinecraftNumericVersion(string value, out MinecraftNumericVersion version)
	{
		version = null;
		Match match = Regex.Match(value ?? string.Empty, "^(?<first>[0-9]+)(?:\\.(?<second>[0-9]+))?(?:\\.(?<third>[0-9]+))?", RegexOptions.CultureInvariant);
		if (!match.Success)
		{
			return false;
		}
		int first;
		int second = 0;
		int third = 0;
		if (!int.TryParse(match.Groups["first"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out first))
		{
			return false;
		}
		if (match.Groups["second"].Success && !int.TryParse(match.Groups["second"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out second))
		{
			return false;
		}
		if (match.Groups["third"].Success && !int.TryParse(match.Groups["third"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out third))
		{
			return false;
		}
		version = new MinecraftNumericVersion();
		version.First = first;
		version.Second = second;
		version.Third = third;
		return true;
	}

	private static bool IsMinecraftVersionDowngrade(string currentVersion, string targetVersion)
	{
		MinecraftNumericVersion current;
		MinecraftNumericVersion target;
		if (!TryParseMinecraftNumericVersion(currentVersion, out current) || !TryParseMinecraftNumericVersion(targetVersion, out target))
		{
			return false;
		}
		if (target.First != current.First)
		{
			return target.First < current.First;
		}
		if (target.Second != current.Second)
		{
			return target.Second < current.Second;
		}
		return target.Third < current.Third;
	}

	private static int GetMojangMetadataJavaMajor(string minecraftVersion)
	{
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			throw new InvalidDataException("Minecraft 버전이 비어 있습니다.");
		}
		lock (RuntimeCompatibilityMojangCacheLock)
		{
			int cachedMajor;
			if (RuntimeCompatibilityMojangJavaCache.TryGetValue(minecraftVersion, out cachedMajor))
			{
				return cachedMajor;
			}
		}

		string manifestJson = DownloadRuntimeCompatibilityText("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", RuntimeCompatibilityMojangMetadataHosts, 8388608);
		JavaScriptSerializer serializer = CreateRuntimeCompatibilityJsonSerializer();
		Dictionary<string, object> manifest = serializer.DeserializeObject(manifestJson) as Dictionary<string, object>;
		object[] versions = manifest != null && manifest.ContainsKey("versions") ? manifest["versions"] as object[] : null;
		if (versions == null)
		{
			throw new InvalidDataException("Mojang 버전 목록 형식이 올바르지 않습니다.");
		}
		string versionMetadataUrl = null;
		for (int i = 0; i < versions.Length; i = checked(i + 1))
		{
			Dictionary<string, object> item = versions[i] as Dictionary<string, object>;
			if (item != null && item.ContainsKey("id") && item.ContainsKey("url") && string.Equals(Convert.ToString(item["id"], CultureInfo.InvariantCulture), minecraftVersion, StringComparison.OrdinalIgnoreCase))
			{
				versionMetadataUrl = Convert.ToString(item["url"], CultureInfo.InvariantCulture);
				break;
			}
		}
		if (!IsTrustedRuntimeCompatibilityUri(versionMetadataUrl, RuntimeCompatibilityMojangMetadataHosts))
		{
			throw new InvalidDataException("선택한 Minecraft 버전의 신뢰할 수 있는 Mojang 메타데이터 URL을 찾지 못했습니다.");
		}

		string versionJson = DownloadRuntimeCompatibilityText(versionMetadataUrl, RuntimeCompatibilityMojangMetadataHosts, 4194304);
		Dictionary<string, object> versionRoot = serializer.DeserializeObject(versionJson) as Dictionary<string, object>;
		Dictionary<string, object> javaVersion = versionRoot != null && versionRoot.ContainsKey("javaVersion") ? versionRoot["javaVersion"] as Dictionary<string, object> : null;
		if (javaVersion == null || !javaVersion.ContainsKey("majorVersion"))
		{
			throw new InvalidDataException("Mojang 메타데이터에 javaVersion.majorVersion이 없습니다.");
		}
		int major = Convert.ToInt32(javaVersion["majorVersion"], CultureInfo.InvariantCulture);
		if (major < 8 || major > 30)
		{
			throw new InvalidDataException("Mojang 메타데이터가 예상 범위를 벗어난 Java 버전을 반환했습니다: " + major);
		}
		lock (RuntimeCompatibilityMojangCacheLock)
		{
			RuntimeCompatibilityMojangJavaCache[minecraftVersion] = major;
		}
		return major;
	}

	private static JavaScriptSerializer CreateRuntimeCompatibilityJsonSerializer()
	{
		JavaScriptSerializer serializer = new JavaScriptSerializer();
		serializer.MaxJsonLength = 8388608;
		serializer.RecursionLimit = 100;
		return serializer;
	}

	private static string GetCompatibleRuntimesRoot(string serversRoot)
	{
		if (string.IsNullOrWhiteSpace(serversRoot))
		{
			throw new ArgumentException("서버 루트 폴더가 비어 있습니다.", "serversRoot");
		}
		string fullServersRoot = Path.GetFullPath(serversRoot);
		string runtimesRoot = Path.GetFullPath(Path.Combine(fullServersRoot, "runtimes"));
		if (!IsPathWithinRuntimeCompatibilityRoot(runtimesRoot, fullServersRoot))
		{
			throw new InvalidDataException("Java 런타임 캐시 경로가 서버 루트 밖을 가리킵니다.");
		}
		Directory.CreateDirectory(runtimesRoot);
		return runtimesRoot;
	}

	private static CompatibleJavaRuntime TryGetCachedCompatibleRuntime(JavaRuntimeRequirement requirement, string runtimesRoot)
	{
		string targetDirectory = GetCompatibleRuntimeDirectory(runtimesRoot, requirement.MajorVersion);
		if (!Directory.Exists(targetDirectory))
		{
			return null;
		}
		try
		{
			EnsureRuntimeCompatibilityDirectoryIsSafe(targetDirectory, runtimesRoot);
			Dictionary<string, string> marker = ReadRuntimeCompatibilityMarker(Path.Combine(targetDirectory, ".launcher-java-runtime"));
			int markerMajor;
			if (!marker.ContainsKey("major") || !int.TryParse(marker["major"], NumberStyles.None, CultureInfo.InvariantCulture, out markerMajor) || markerMajor != requirement.MajorVersion)
			{
				return null;
			}
			string javaPath = ResolveMarkedRuntimeJavaPath(targetDirectory, marker);
			if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath) || !marker.ContainsKey("java-sha256") || !IsSha256Text(marker["java-sha256"]) || !string.Equals(GetRuntimeCompatibilityFileSha256(javaPath), marker["java-sha256"], StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			JavaExecutableProbe probe = ProbeJavaExecutable(javaPath, requirement.MajorVersion);
			if (!probe.IsValid)
			{
				return null;
			}
			return CreateCompatibleJavaRuntime(requirement, javaPath, probe, "Eclipse Temurin 캐시", false, false);
		}
		catch
		{
			return null;
		}
	}

	private static string GetCompatibleRuntimeDirectory(string runtimesRoot, int majorVersion)
	{
		string directory = Path.GetFullPath(Path.Combine(runtimesRoot, "java-" + majorVersion.ToString(CultureInfo.InvariantCulture)));
		if (!IsPathWithinRuntimeCompatibilityRoot(directory, runtimesRoot))
		{
			throw new InvalidDataException("Java 런타임 경로가 캐시 루트 밖을 가리킵니다.");
		}
		return directory;
	}

	private static string PrepareAdoptiumRuntime(int majorVersion, string runtimesRoot)
	{
		AdoptiumRuntimePackage package = GetAdoptiumRuntimePackage(majorVersion);
		string token = Guid.NewGuid().ToString("N");
		string targetDirectory = GetCompatibleRuntimeDirectory(runtimesRoot, majorVersion);
		string stagingDirectory = Path.Combine(runtimesRoot, ".java-" + majorVersion.ToString(CultureInfo.InvariantCulture) + "-preparing-" + token);
		string previousDirectory = Path.Combine(runtimesRoot, ".java-" + majorVersion.ToString(CultureInfo.InvariantCulture) + "-previous-" + token);
		string temporaryZipPath = Path.Combine(runtimesRoot, ".java-" + majorVersion.ToString(CultureInfo.InvariantCulture) + "-" + token + ".zip");
		EnsureRuntimeCompatibilityDirectoryIsSafe(stagingDirectory, runtimesRoot);
		EnsureRuntimeCompatibilityDirectoryIsSafe(previousDirectory, runtimesRoot);
		bool previousMoved = false;
		try
		{
			Console.WriteLine("Java " + majorVersion + " " + package.ImageType.ToUpperInvariant() + " 런타임을 Eclipse Adoptium에서 내려받는 중...");
			DownloadRuntimeCompatibilityFile(package.Link, temporaryZipPath, RuntimeCompatibilityAdoptiumDownloadHosts, package.Size);
			FileInfo zipInfo = new FileInfo(temporaryZipPath);
			if (zipInfo.Length != package.Size)
			{
				throw new InvalidDataException("Java ZIP 크기가 API 정보와 일치하지 않습니다. 예상 " + package.Size + "바이트, 실제 " + zipInfo.Length + "바이트입니다.");
			}
			if (!string.Equals(GetRuntimeCompatibilityFileSha256(temporaryZipPath), package.Checksum, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("Java ZIP의 SHA-256 검증에 실패했습니다.");
			}

			DeleteRuntimeCompatibilityDirectoryIfPresent(stagingDirectory, runtimesRoot);
			Directory.CreateDirectory(stagingDirectory);
			ExtractRuntimeCompatibilityZip(temporaryZipPath, stagingDirectory);
			string stagingJavaPath = FindRuntimeCompatibilityJavaExecutable(stagingDirectory);
			if (string.IsNullOrEmpty(stagingJavaPath))
			{
				throw new FileNotFoundException("압축을 푼 Java 런타임에서 bin\\java.exe를 찾지 못했습니다.");
			}
			JavaExecutableProbe stagingProbe = ProbeJavaExecutable(stagingJavaPath, majorVersion);
			if (!stagingProbe.IsValid)
			{
				throw new InvalidDataException("압축을 푼 Java 실행 파일 검증 실패: " + stagingProbe.Error);
			}
			WriteRuntimeCompatibilityMarker(stagingDirectory, majorVersion, package, stagingJavaPath);

			if (Directory.Exists(targetDirectory))
			{
				EnsureRuntimeCompatibilityDirectoryIsSafe(targetDirectory, runtimesRoot);
				DeleteRuntimeCompatibilityDirectoryIfPresent(previousDirectory, runtimesRoot);
				Directory.Move(targetDirectory, previousDirectory);
				previousMoved = true;
			}
			Directory.Move(stagingDirectory, targetDirectory);
			Dictionary<string, string> installedMarker = ReadRuntimeCompatibilityMarker(Path.Combine(targetDirectory, ".launcher-java-runtime"));
			string installedJavaPath = ResolveMarkedRuntimeJavaPath(targetDirectory, installedMarker);
			if (string.IsNullOrEmpty(installedJavaPath) || !File.Exists(installedJavaPath))
			{
				throw new FileNotFoundException("준비된 Java 캐시에서 java.exe를 찾지 못했습니다.");
			}
			JavaExecutableProbe installedProbe = ProbeJavaExecutable(installedJavaPath, majorVersion);
			if (!installedProbe.IsValid)
			{
				throw new InvalidDataException("설치한 Java 캐시의 최종 검증 실패: " + installedProbe.Error);
			}
			if (previousMoved)
			{
				try
				{
					DeleteRuntimeCompatibilityDirectoryIfPresent(previousDirectory, runtimesRoot);
				}
				catch
				{
					// 새 런타임 검증이 끝났으므로 이전 캐시 정리 실패는 실행을 막지 않습니다.
				}
			}
			return installedJavaPath;
		}
		catch
		{
			if (previousMoved && Directory.Exists(previousDirectory))
			{
				try
				{
					if (Directory.Exists(targetDirectory))
					{
						DeleteRuntimeCompatibilityDirectoryIfPresent(targetDirectory, runtimesRoot);
					}
					Directory.Move(previousDirectory, targetDirectory);
				}
				catch
				{
					// 원래 예외를 유지하고 복구 실패 흔적은 previous 폴더에 보존합니다.
				}
			}
			throw;
		}
		finally
		{
			DeleteRuntimeCompatibilityFileIfPresent(temporaryZipPath, runtimesRoot);
			try
			{
				DeleteRuntimeCompatibilityDirectoryIfPresent(stagingDirectory, runtimesRoot);
			}
			catch
			{
			}
		}
	}

	private static AdoptiumRuntimePackage GetAdoptiumRuntimePackage(int majorVersion)
	{
		List<string> errors = new List<string>();
		string[] imageTypes = new string[] { "jre", "jdk" };
		for (int i = 0; i < imageTypes.Length; i = checked(i + 1))
		{
			try
			{
				AdoptiumRuntimePackage package = QueryAdoptiumRuntimePackage(majorVersion, imageTypes[i]);
				if (package != null)
				{
					return package;
				}
				errors.Add(imageTypes[i].ToUpperInvariant() + " ZIP 정보 없음");
			}
			catch (Exception ex)
			{
				errors.Add(imageTypes[i].ToUpperInvariant() + ": " + SummarizeRuntimeCompatibilityError(ex));
			}
		}
		throw new InvalidDataException("Adoptium Java " + majorVersion + " Windows x64 ZIP 정보를 찾지 못했습니다. " + string.Join(" / ", errors.ToArray()));
	}

	private static AdoptiumRuntimePackage QueryAdoptiumRuntimePackage(int majorVersion, string imageType)
	{
		string endpoint = "https://api.adoptium.net/v3/assets/feature_releases/" + majorVersion.ToString(CultureInfo.InvariantCulture) + "/ga?architecture=x64&heap_size=normal&image_type=" + Uri.EscapeDataString(imageType) + "&jvm_impl=hotspot&os=windows&page=0&page_size=1&project=jdk&sort_method=DEFAULT&sort_order=DESC&vendor=eclipse";
		string json = DownloadRuntimeCompatibilityText(endpoint, RuntimeCompatibilityAdoptiumApiHosts, 8388608);
		object[] releases = CreateRuntimeCompatibilityJsonSerializer().DeserializeObject(json) as object[];
		if (releases == null)
		{
			throw new InvalidDataException("Adoptium 응답이 배열이 아닙니다.");
		}
		for (int i = 0; i < releases.Length; i = checked(i + 1))
		{
			Dictionary<string, object> release = releases[i] as Dictionary<string, object>;
			object[] binaries = release != null && release.ContainsKey("binaries") ? release["binaries"] as object[] : null;
			if (binaries == null)
			{
				continue;
			}
			for (int j = 0; j < binaries.Length; j = checked(j + 1))
			{
				Dictionary<string, object> binary = binaries[j] as Dictionary<string, object>;
				if (binary == null || !DictionaryValueEquals(binary, "architecture", "x64") || !DictionaryValueEquals(binary, "os", "windows") || !DictionaryValueEquals(binary, "image_type", imageType) || !DictionaryValueEquals(binary, "jvm_impl", "hotspot"))
				{
					continue;
				}
				Dictionary<string, object> packageData = binary.ContainsKey("package") ? binary["package"] as Dictionary<string, object> : null;
				if (packageData == null || !packageData.ContainsKey("link") || !packageData.ContainsKey("checksum") || !packageData.ContainsKey("size"))
				{
					continue;
				}
				AdoptiumRuntimePackage package = new AdoptiumRuntimePackage();
				package.Link = Convert.ToString(packageData["link"], CultureInfo.InvariantCulture);
				package.Checksum = Convert.ToString(packageData["checksum"], CultureInfo.InvariantCulture);
				package.Name = packageData.ContainsKey("name") ? Convert.ToString(packageData["name"], CultureInfo.InvariantCulture) : "java-" + majorVersion + ".zip";
				package.Size = Convert.ToInt64(packageData["size"], CultureInfo.InvariantCulture);
				package.ImageType = imageType;
				if (!IsTrustedRuntimeCompatibilityUri(package.Link, RuntimeCompatibilityAdoptiumDownloadHosts))
				{
					throw new InvalidDataException("Adoptium이 허용되지 않은 다운로드 호스트를 반환했습니다.");
				}
				if (!IsSha256Text(package.Checksum))
				{
					throw new InvalidDataException("Adoptium SHA-256 값 형식이 올바르지 않습니다.");
				}
				if (package.Size < 1048576L || package.Size > RuntimeCompatibilityMaximumPackageBytes)
				{
					throw new InvalidDataException("Adoptium ZIP 크기가 허용 범위를 벗어났습니다: " + package.Size);
				}
				if (!package.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Adoptium 패키지가 ZIP 파일이 아닙니다: " + package.Name);
				}
				return package;
			}
		}
		return null;
	}

	private static bool DictionaryValueEquals(Dictionary<string, object> dictionary, string key, string expected)
	{
		return dictionary.ContainsKey(key) && string.Equals(Convert.ToString(dictionary[key], CultureInfo.InvariantCulture), expected, StringComparison.OrdinalIgnoreCase);
	}

	private static string DownloadRuntimeCompatibilityText(string url, string[] allowedHosts, int maximumBytes)
	{
		using (HttpWebResponse response = OpenRuntimeCompatibilityResponse(url, allowedHosts, RuntimeCompatibilityMetadataTimeoutMilliseconds, true))
		using (Stream responseStream = response.GetResponseStream())
		using (MemoryStream buffer = new MemoryStream())
		{
			if (response.ContentLength > maximumBytes)
			{
				throw new InvalidDataException("메타데이터 응답이 허용 크기를 초과했습니다.");
			}
			byte[] chunk = new byte[32768];
			int total = 0;
			while (true)
			{
				int read = responseStream.Read(chunk, 0, chunk.Length);
				if (read <= 0)
				{
					break;
				}
				total = checked(total + read);
				if (total > maximumBytes)
				{
					throw new InvalidDataException("메타데이터 응답이 허용 크기를 초과했습니다.");
				}
				buffer.Write(chunk, 0, read);
			}
			return Encoding.UTF8.GetString(buffer.ToArray());
		}
	}

	private static void DownloadRuntimeCompatibilityFile(string url, string destinationPath, string[] allowedHosts, long expectedSize)
	{
		using (HttpWebResponse response = OpenRuntimeCompatibilityResponse(url, allowedHosts, RuntimeCompatibilityDownloadTimeoutMilliseconds, false))
		using (Stream responseStream = response.GetResponseStream())
		using (FileStream destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
		{
			if (response.ContentLength >= 0L && response.ContentLength != expectedSize)
			{
				throw new InvalidDataException("다운로드 응답 크기가 Adoptium API 정보와 일치하지 않습니다.");
			}
			byte[] buffer = new byte[1048576];
			long total = 0L;
			while (true)
			{
				int read = responseStream.Read(buffer, 0, buffer.Length);
				if (read <= 0)
				{
					break;
				}
				total = checked(total + read);
				if (total > expectedSize || total > RuntimeCompatibilityMaximumPackageBytes)
				{
					throw new InvalidDataException("Java ZIP 다운로드가 예상 크기를 초과했습니다.");
				}
				destination.Write(buffer, 0, read);
			}
			if (total != expectedSize)
			{
				throw new EndOfStreamException("Java ZIP 다운로드가 완료되기 전에 연결이 끝났습니다.");
			}
			destination.Flush(true);
		}
	}

	private static HttpWebResponse OpenRuntimeCompatibilityResponse(string url, string[] allowedHosts, int timeoutMilliseconds, bool allowContentCompression)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		Uri current;
		if (!Uri.TryCreate(url, UriKind.Absolute, out current))
		{
			throw new InvalidDataException("다운로드 URL 형식이 올바르지 않습니다.");
		}
		for (int redirectCount = 0; redirectCount <= 8; redirectCount = checked(redirectCount + 1))
		{
			if (!IsTrustedRuntimeCompatibilityUri(current.AbsoluteUri, allowedHosts))
			{
				throw new InvalidDataException("허용되지 않은 HTTPS 다운로드 주소입니다: " + current.Host);
			}
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(current);
			request.Method = "GET";
			request.UserAgent = RuntimeCompatibilityUserAgent;
			request.Accept = "application/json, application/octet-stream;q=0.9, */*;q=0.1";
			request.AllowAutoRedirect = false;
			request.AutomaticDecompression = allowContentCompression ? DecompressionMethods.GZip | DecompressionMethods.Deflate : DecompressionMethods.None;
			request.Timeout = timeoutMilliseconds;
			request.ReadWriteTimeout = timeoutMilliseconds;
			request.KeepAlive = false;
			HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			int statusCode = (int)response.StatusCode;
			if (statusCode == 301 || statusCode == 302 || statusCode == 303 || statusCode == 307 || statusCode == 308)
			{
				string location = response.Headers["Location"];
				response.Close();
				if (string.IsNullOrWhiteSpace(location))
				{
					throw new WebException("리디렉션 응답에 Location 헤더가 없습니다.");
				}
				Uri redirected;
				if (!Uri.TryCreate(current, location, out redirected) || !IsTrustedRuntimeCompatibilityUri(redirected.AbsoluteUri, allowedHosts))
				{
					throw new InvalidDataException("리디렉션 대상 호스트가 허용 목록에 없습니다.");
				}
				current = redirected;
				continue;
			}
			if (response.StatusCode != HttpStatusCode.OK)
			{
				response.Close();
				throw new WebException("HTTP " + statusCode);
			}
			return response;
		}
		throw new WebException("다운로드 리디렉션 횟수가 허용 범위를 초과했습니다.");
	}

	private static bool IsTrustedRuntimeCompatibilityUri(string url, string[] allowedHosts)
	{
		Uri uri;
		if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.UserInfo) || (!uri.IsDefaultPort && uri.Port != 443))
		{
			return false;
		}
		for (int i = 0; i < allowedHosts.Length; i = checked(i + 1))
		{
			if (string.Equals(uri.Host, allowedHosts[i], StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void ExtractRuntimeCompatibilityZip(string zipPath, string destinationRoot)
	{
		string fullRoot = Path.GetFullPath(destinationRoot);
		string rootPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		long totalExtracted = 0L;
		int entryCount = 0;
		using (FileStream zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
		using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read, false))
		{
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				entryCount = checked(entryCount + 1);
				if (entryCount > RuntimeCompatibilityMaximumZipEntries)
				{
					throw new InvalidDataException("Java ZIP 항목 수가 허용 범위를 초과했습니다.");
				}
				string relativePath = (entry.FullName ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
				if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
				{
					if (string.IsNullOrWhiteSpace(relativePath))
					{
						continue;
					}
					throw new InvalidDataException("Java ZIP에 절대 경로 항목이 포함되어 있습니다.");
				}
				string destinationPath;
				try
				{
					destinationPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
				}
				catch (Exception ex)
				{
					throw new InvalidDataException("Java ZIP 항목 경로가 올바르지 않습니다: " + SummarizeRuntimeCompatibilityError(ex));
				}
				if (!destinationPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Java ZIP 경로 탈출 항목을 차단했습니다: " + entry.FullName);
				}
				bool isDirectory = string.IsNullOrEmpty(entry.Name) || relativePath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
				if (isDirectory)
				{
					Directory.CreateDirectory(destinationPath);
					continue;
				}
				if (entry.Length < 0L || entry.Length > RuntimeCompatibilityMaximumExtractedBytes)
				{
					throw new InvalidDataException("Java ZIP 항목 크기가 허용 범위를 벗어났습니다.");
				}
				totalExtracted = checked(totalExtracted + entry.Length);
				if (totalExtracted > RuntimeCompatibilityMaximumExtractedBytes)
				{
					throw new InvalidDataException("Java ZIP의 전체 압축 해제 크기가 허용 범위를 초과했습니다.");
				}
				string parent = Path.GetDirectoryName(destinationPath);
				if (!string.IsNullOrEmpty(parent))
				{
					Directory.CreateDirectory(parent);
				}
				using (Stream source = entry.Open())
				using (FileStream destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					CopyRuntimeCompatibilityStream(source, destination, entry.Length);
					destination.Flush(true);
				}
			}
		}
	}

	private static void CopyRuntimeCompatibilityStream(Stream source, Stream destination, long expectedLength)
	{
		byte[] buffer = new byte[1048576];
		long total = 0L;
		while (true)
		{
			int read = source.Read(buffer, 0, buffer.Length);
			if (read <= 0)
			{
				break;
			}
			total = checked(total + read);
			if (total > expectedLength)
			{
				throw new InvalidDataException("Java ZIP 항목이 선언된 크기를 초과했습니다.");
			}
			destination.Write(buffer, 0, read);
		}
		if (total != expectedLength)
		{
			throw new EndOfStreamException("Java ZIP 항목이 선언된 크기보다 짧습니다.");
		}
	}

	private static void WriteRuntimeCompatibilityMarker(string stagingDirectory, int majorVersion, AdoptiumRuntimePackage package, string javaPath)
	{
		string fullStaging = Path.GetFullPath(stagingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullJava = Path.GetFullPath(javaPath);
		if (!fullJava.StartsWith(fullStaging, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("java.exe가 준비 폴더 밖을 가리킵니다.");
		}
		string relativeJava = fullJava.Substring(fullStaging.Length).Replace(Path.DirectorySeparatorChar, '/');
		StringBuilder marker = new StringBuilder();
		marker.Append("major=").Append(majorVersion.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
		marker.Append("source=adoptium\r\n");
		marker.Append("image-type=").Append(package.ImageType).Append("\r\n");
		marker.Append("package-name=").Append(package.Name.Replace("\r", string.Empty).Replace("\n", string.Empty)).Append("\r\n");
		marker.Append("package-size=").Append(package.Size.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
		marker.Append("package-sha256=").Append(package.Checksum.ToLowerInvariant()).Append("\r\n");
		marker.Append("java-relative-path=").Append(relativeJava).Append("\r\n");
		marker.Append("java-sha256=").Append(GetRuntimeCompatibilityFileSha256(javaPath)).Append("\r\n");
		File.WriteAllText(Path.Combine(stagingDirectory, ".launcher-java-runtime"), marker.ToString(), new UTF8Encoding(false));
	}

	private static Dictionary<string, string> ReadRuntimeCompatibilityMarker(string path)
	{
		Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!File.Exists(path))
		{
			return values;
		}
		string[] lines = File.ReadAllLines(path, Encoding.UTF8);
		for (int i = 0; i < lines.Length; i = checked(i + 1))
		{
			int separator = lines[i].IndexOf('=');
			if (separator > 0)
			{
				values[lines[i].Substring(0, separator).Trim()] = lines[i].Substring(separator + 1).Trim();
			}
		}
		return values;
	}

	private static string ResolveMarkedRuntimeJavaPath(string runtimeDirectory, Dictionary<string, string> marker)
	{
		if (marker == null || !marker.ContainsKey("java-relative-path"))
		{
			return null;
		}
		string relative = marker["java-relative-path"].Replace('/', Path.DirectorySeparatorChar);
		if (Path.IsPathRooted(relative))
		{
			return null;
		}
		string fullRoot = Path.GetFullPath(runtimeDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string candidate;
		try
		{
			candidate = Path.GetFullPath(Path.Combine(runtimeDirectory, relative));
		}
		catch
		{
			return null;
		}
		return candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? candidate : null;
	}

	private static string FindRuntimeCompatibilityJavaExecutable(string runtimeDirectory)
	{
		if (!Directory.Exists(runtimeDirectory))
		{
			return null;
		}
		string fullRoot = Path.GetFullPath(runtimeDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string[] candidates = Directory.GetFiles(runtimeDirectory, "java.exe", SearchOption.AllDirectories);
		for (int i = 0; i < candidates.Length; i = checked(i + 1))
		{
			string fullCandidate = Path.GetFullPath(candidates[i]);
			if (fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFileName(Path.GetDirectoryName(fullCandidate)), "bin", StringComparison.OrdinalIgnoreCase))
			{
				return fullCandidate;
			}
		}
		return null;
	}

	private static JavaExecutableProbe ProbeJavaExecutable(string javaPath, int requiredMajor)
	{
		JavaExecutableProbe probe = new JavaExecutableProbe();
		probe.Output = string.Empty;
		if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
		{
			probe.Error = "java.exe 파일이 없습니다.";
			return probe;
		}
		probe.Is64Bit = IsAmd64RuntimeExecutable(javaPath);
		if (!probe.Is64Bit)
		{
			probe.Error = "64비트 x64 java.exe가 아닙니다.";
			return probe;
		}
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo();
			startInfo.FileName = javaPath;
			startInfo.Arguments = "-version";
			startInfo.WorkingDirectory = Path.GetDirectoryName(javaPath);
			startInfo.UseShellExecute = false;
			startInfo.CreateNoWindow = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.RedirectStandardError = true;
			using (Process process = new Process())
			{
				StringBuilder output = new StringBuilder();
				object outputLock = new object();
				process.StartInfo = startInfo;
				process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
				{
					AppendRuntimeCompatibilityProcessOutput(output, outputLock, args.Data);
				};
				process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
				{
					AppendRuntimeCompatibilityProcessOutput(output, outputLock, args.Data);
				};
				if (!process.Start())
				{
					probe.Error = "java -version 프로세스를 시작하지 못했습니다.";
					return probe;
				}
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				if (!process.WaitForExit(10000))
				{
					try
					{
						process.Kill();
					}
					catch
					{
					}
					probe.Error = "java -version 확인 시간이 10초를 초과했습니다.";
					return probe;
				}
				process.WaitForExit();
				lock (outputLock)
				{
					probe.Output = output.ToString();
				}
				if (process.ExitCode != 0)
				{
					probe.Error = "java -version이 오류 코드 " + process.ExitCode + "를 반환했습니다. " + SummarizeRuntimeCompatibilityText(probe.Output);
					return probe;
				}
			}
			probe.MajorVersion = ParseJavaMajorFromVersionOutput(probe.Output);
			if (probe.MajorVersion <= 0)
			{
				probe.Error = "java -version 출력에서 주 버전을 읽지 못했습니다: " + SummarizeRuntimeCompatibilityText(probe.Output);
				return probe;
			}
			if (probe.MajorVersion != requiredMajor)
			{
				probe.Error = "필요한 Java는 " + requiredMajor + "이지만 발견한 Java는 " + probe.MajorVersion + "입니다.";
				return probe;
			}
			probe.IsValid = true;
			return probe;
		}
		catch (Exception ex)
		{
			probe.Error = SummarizeRuntimeCompatibilityError(ex);
			return probe;
		}
	}

	private static void AppendRuntimeCompatibilityProcessOutput(StringBuilder output, object outputLock, string line)
	{
		if (line == null)
		{
			return;
		}
		lock (outputLock)
		{
			if (output.Length < 8192)
			{
				int remaining = 8192 - output.Length;
				output.AppendLine(line.Length <= remaining ? line : line.Substring(0, remaining));
			}
		}
	}

	private static int ParseJavaMajorFromVersionOutput(string output)
	{
		Match match = Regex.Match(output ?? string.Empty, "(?:openjdk|java)\\s+(?:version\\s+)?[\\\"']?(?<version>1\\.[0-9]+|[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (!match.Success)
		{
			return 0;
		}
		string token = match.Groups["version"].Value;
		if (token.StartsWith("1.", StringComparison.Ordinal))
		{
			int legacyMajor;
			return int.TryParse(token.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out legacyMajor) ? legacyMajor : 0;
		}
		int major;
		return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out major) ? major : 0;
	}

	private static bool IsAmd64RuntimeExecutable(string path)
	{
		try
		{
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				if (stream.Length < 64L || reader.ReadUInt16() != 23117)
				{
					return false;
				}
				stream.Position = 60L;
				int peOffset = reader.ReadInt32();
				if (peOffset < 0 || peOffset > stream.Length - 6L)
				{
					return false;
				}
				stream.Position = peOffset;
				return reader.ReadUInt32() == 17744u && reader.ReadUInt16() == 34404;
			}
		}
		catch
		{
			return false;
		}
	}

	private static string FindCompatibleSystemJava(int requiredMajor, out JavaExecutableProbe matchingProbe, out List<string> diagnostics)
	{
		matchingProbe = null;
		diagnostics = new List<string>();
		List<string> candidates = GetSystemJavaCandidates();
		for (int i = 0; i < candidates.Count; i = checked(i + 1))
		{
			JavaExecutableProbe probe = ProbeJavaExecutable(candidates[i], requiredMajor);
			if (probe.IsValid)
			{
				matchingProbe = probe;
				return candidates[i];
			}
			if (diagnostics.Count < 10)
			{
				string detail = probe.MajorVersion > 0 ? "Java " + probe.MajorVersion + (probe.Is64Bit ? " x64" : " 32비트/비호환") : SummarizeRuntimeCompatibilityText(probe.Error);
				diagnostics.Add(candidates[i] + " (" + detail + ")");
			}
		}
		return null;
	}

	private static List<string> GetSystemJavaCandidates()
	{
		List<string> candidates = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddSystemJavaHomeCandidate(Environment.GetEnvironmentVariable("JAVA_HOME"), candidates, seen);

		string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		string[] pathEntries = pathValue.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < pathEntries.Length; i = checked(i + 1))
		{
			string directory = pathEntries[i].Trim().Trim('"');
			if (!string.IsNullOrWhiteSpace(directory))
			{
				AddSystemJavaFileCandidate(Path.Combine(directory, "java.exe"), candidates, seen);
			}
		}

		AddRegistryJavaCandidates(Registry.LocalMachine, @"SOFTWARE\JavaSoft\JDK", candidates, seen);
		AddRegistryJavaCandidates(Registry.LocalMachine, @"SOFTWARE\JavaSoft\Java Development Kit", candidates, seen);
		AddRegistryJavaCandidates(Registry.LocalMachine, @"SOFTWARE\JavaSoft\JRE", candidates, seen);
		AddRegistryJavaCandidates(Registry.LocalMachine, @"SOFTWARE\JavaSoft\Java Runtime Environment", candidates, seen);
		AddRegistryJavaCandidates(Registry.CurrentUser, @"SOFTWARE\JavaSoft\JDK", candidates, seen);
		AddRegistryJavaCandidates(Registry.CurrentUser, @"SOFTWARE\JavaSoft\JRE", candidates, seen);

		AddCommonJavaInstallCandidates(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), candidates, seen);
		AddCommonJavaInstallCandidates(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), candidates, seen);
		return candidates;
	}

	private static void AddRegistryJavaCandidates(RegistryKey root, string keyPath, List<string> candidates, HashSet<string> seen)
	{
		try
		{
			using (RegistryKey key = root.OpenSubKey(keyPath))
			{
				if (key == null)
				{
					return;
				}
				string[] versions = key.GetSubKeyNames();
				for (int i = 0; i < versions.Length; i = checked(i + 1))
				{
					using (RegistryKey versionKey = key.OpenSubKey(versions[i]))
					{
						if (versionKey != null)
						{
							AddSystemJavaHomeCandidate(Convert.ToString(versionKey.GetValue("JavaHome"), CultureInfo.InvariantCulture), candidates, seen);
						}
					}
				}
			}
		}
		catch
		{
		}
	}

	private static void AddCommonJavaInstallCandidates(string programFiles, List<string> candidates, HashSet<string> seen)
	{
		if (string.IsNullOrWhiteSpace(programFiles) || !Directory.Exists(programFiles))
		{
			return;
		}
		string[] vendorDirectories = new string[] { "Java", "Eclipse Adoptium", "Microsoft", "Amazon Corretto", "BellSoft", "Zulu" };
		for (int i = 0; i < vendorDirectories.Length; i = checked(i + 1))
		{
			string vendorRoot = Path.Combine(programFiles, vendorDirectories[i]);
			if (!Directory.Exists(vendorRoot))
			{
				continue;
			}
			AddSystemJavaHomeCandidate(vendorRoot, candidates, seen);
			try
			{
				string[] children = Directory.GetDirectories(vendorRoot);
				for (int j = 0; j < children.Length; j = checked(j + 1))
				{
					AddSystemJavaHomeCandidate(children[j], candidates, seen);
					string[] grandchildren = Directory.GetDirectories(children[j]);
					for (int k = 0; k < grandchildren.Length; k = checked(k + 1))
					{
						AddSystemJavaHomeCandidate(grandchildren[k], candidates, seen);
					}
				}
			}
			catch
			{
			}
		}
	}

	private static void AddSystemJavaHomeCandidate(string javaHome, List<string> candidates, HashSet<string> seen)
	{
		if (!string.IsNullOrWhiteSpace(javaHome))
		{
			AddSystemJavaFileCandidate(Path.Combine(javaHome.Trim().Trim('"'), "bin", "java.exe"), candidates, seen);
		}
	}

	private static void AddSystemJavaFileCandidate(string path, List<string> candidates, HashSet<string> seen)
	{
		try
		{
			string fullPath = Path.GetFullPath(path);
			if (File.Exists(fullPath) && seen.Add(fullPath))
			{
				candidates.Add(fullPath);
			}
		}
		catch
		{
		}
	}

	private static string GetRuntimeCompatibilityFileSha256(string path)
	{
		using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		using (SHA256 sha256 = SHA256.Create())
		{
			byte[] hash = sha256.ComputeHash(stream);
			StringBuilder text = new StringBuilder(hash.Length * 2);
			for (int i = 0; i < hash.Length; i = checked(i + 1))
			{
				text.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
			}
			return text.ToString();
		}
	}

	private static bool IsSha256Text(string value)
	{
		return !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
	}

	private static bool IsPathWithinRuntimeCompatibilityRoot(string candidate, string root)
	{
		string fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) || fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private static void EnsureRuntimeCompatibilityDirectoryIsSafe(string path, string runtimesRoot)
	{
		string fullPath = Path.GetFullPath(path);
		string fullRoot = Path.GetFullPath(runtimesRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase) || !IsPathWithinRuntimeCompatibilityRoot(fullPath, fullRoot))
		{
			throw new InvalidDataException("런타임 작업 경로가 캐시 루트 밖을 가리킵니다.");
		}
		string name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrEmpty(name) || (!name.StartsWith("java-", StringComparison.OrdinalIgnoreCase) && !name.StartsWith(".java-", StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidDataException("런타임 작업 폴더 이름이 허용된 형식이 아닙니다.");
		}
		if (Directory.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
		{
			throw new InvalidDataException("런타임 캐시 폴더가 재분석 지점을 가리켜 작업을 중단했습니다.");
		}
	}

	private static void DeleteRuntimeCompatibilityDirectoryIfPresent(string path, string runtimesRoot)
	{
		if (!Directory.Exists(path))
		{
			return;
		}
		EnsureRuntimeCompatibilityDirectoryIsSafe(path, runtimesRoot);
		DeleteRuntimeCompatibilityTree(path);
	}

	private static void DeleteRuntimeCompatibilityTree(string path)
	{
		string[] files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
		for (int i = 0; i < files.Length; i = checked(i + 1))
		{
			File.SetAttributes(files[i], FileAttributes.Normal);
			File.Delete(files[i]);
		}
		string[] directories = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
		for (int j = 0; j < directories.Length; j = checked(j + 1))
		{
			if ((File.GetAttributes(directories[j]) & FileAttributes.ReparsePoint) != 0)
			{
				Directory.Delete(directories[j], false);
			}
			else
			{
				DeleteRuntimeCompatibilityTree(directories[j]);
			}
		}
		Directory.Delete(path, false);
	}

	private static void DeleteRuntimeCompatibilityFileIfPresent(string path, string runtimesRoot)
	{
		if (!File.Exists(path))
		{
			return;
		}
		if (!IsPathWithinRuntimeCompatibilityRoot(path, runtimesRoot) || !Path.GetFileName(path).StartsWith(".java-", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("임시 런타임 파일 경로가 허용 범위를 벗어났습니다.");
		}
		File.SetAttributes(path, FileAttributes.Normal);
		File.Delete(path);
	}

	private static string SummarizeRuntimeCompatibilityError(Exception exception)
	{
		return exception == null ? "알 수 없는 오류" : SummarizeRuntimeCompatibilityText(exception.Message);
	}

	private static string SummarizeRuntimeCompatibilityText(string text)
	{
		string normalized = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
		if (normalized.Length > 400)
		{
			normalized = normalized.Substring(0, 400) + "...";
		}
		return string.IsNullOrEmpty(normalized) ? "세부 정보 없음" : normalized;
	}
}
