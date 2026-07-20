using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;

internal static class PublicAutoUpdateTests
{
	private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

	private static int Main(string[] args)
	{
		if (args.Length != 3)
		{
			Console.Error.WriteLine("사용법: PublicAutoUpdate.Tests.exe <이전 런처> <update.json> <다운로드 대상>");
			return 2;
		}

		try
		{
			// 실제 런처의 Main과 같은 네트워크 초기화 뒤 업데이트 루틴을 호출합니다.
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			ServicePointManager.DefaultConnectionLimit = 64;
			string sourceLauncher = Path.GetFullPath(args[0]);
			string metadataPath = Path.GetFullPath(args[1]);
			string destination = Path.GetFullPath(args[2]);
			if (!File.Exists(sourceLauncher) || !File.Exists(metadataPath)) throw new FileNotFoundException("이전 런처 또는 업데이트 메타데이터를 찾을 수 없습니다.");
			if (File.Exists(destination)) throw new IOException("다운로드 대상 파일은 검증 전에 존재하지 않아야 합니다.");
			Directory.CreateDirectory(Path.GetDirectoryName(destination));

			Assembly assembly = Assembly.LoadFrom(sourceLauncher);
			Type launcher = assembly.GetType("Launcher", true);
			MethodInfo parse = RequireMethod(launcher, "ParseLauncherUpdateMetadata");
			MethodInfo isNewer = RequireMethod(launcher, "IsLauncherUpdateNewer");
			MethodInfo download = RequireMethod(launcher, "DownloadLauncherUpdate");
			object asset = Invoke(parse, null, new object[] { File.ReadAllText(metadataPath) });

			FileVersionInfo sourceVersion = FileVersionInfo.GetVersionInfo(sourceLauncher);
			bool updateAvailable = (bool)Invoke(isNewer, null, new object[] { asset, sourceVersion.ProductVersion.Trim(), sourceVersion.FileVersion.Trim() });
			if (!updateAvailable) throw new InvalidOperationException("이전 공개 런처가 새 릴리스를 업데이트로 판정하지 않았습니다.");

			Invoke(download, null, new object[] { asset, destination });
			long expectedSize = Convert.ToInt64(ReadMember(asset, "Size"));
			string expectedHash = Convert.ToString(ReadMember(asset, "Sha256"));
			string expectedProduct = Convert.ToString(ReadMember(asset, "ProductVersion"));
			string expectedBuild = Convert.ToString(ReadMember(asset, "BuildNumber"));
			FileInfo downloaded = new FileInfo(destination);
			if (!downloaded.Exists || downloaded.Length != expectedSize) throw new InvalidDataException("자동 업데이트 다운로드 크기가 메타데이터와 다릅니다.");
			if (!string.Equals(ComputeSha256(destination), expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("자동 업데이트 다운로드 SHA-256이 메타데이터와 다릅니다.");
			FileVersionInfo downloadedVersion = FileVersionInfo.GetVersionInfo(destination);
			if (!string.Equals(downloadedVersion.ProductVersion.Trim(), expectedProduct, StringComparison.Ordinal) || !string.Equals(downloadedVersion.FileVersion.Trim(), expectedBuild, StringComparison.Ordinal)) throw new InvalidDataException("자동 업데이트 다운로드의 Windows 버전 리소스가 메타데이터와 다릅니다.");

			Console.WriteLine("PUBLIC_AUTO_UPDATE_OK={0}->{1}", sourceVersion.ProductVersion.Trim(), expectedProduct);
			return 0;
		}
		catch (Exception exception)
		{
			Exception error = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
			Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message);
			return 1;
		}
	}

	private static MethodInfo RequireMethod(Type type, string name)
	{
		MethodInfo method = type.GetMethod(name, StaticPrivate);
		if (method == null) throw new MissingMethodException(type.FullName, name);
		return method;
	}

	private static object Invoke(MethodInfo method, object target, object[] args)
	{
		return method.Invoke(target, args);
	}

	private static object ReadMember(object target, string name)
	{
		Type type = target.GetType();
		FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field != null) return field.GetValue(target);
		PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property != null) return property.GetValue(target, null);
		throw new MissingMemberException(type.FullName, name);
	}

	private static string ComputeSha256(string path)
	{
		using (SHA256 sha = SHA256.Create())
		using (FileStream stream = File.OpenRead(path))
		{
			return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
		}
	}
}
