using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static partial class Launcher
{
	private const int ContentManifestSchemaVersion = 1;
	private const int MaximumContentManifestBytes = 4194304;
	private const int MaximumDatapackEntries = 10000;
	private const long MaximumDatapackExpandedBytes = 2147483648L;

	private sealed class ContentManifestModel
	{
		public int SchemaVersion = ContentManifestSchemaVersion;
		public string UpdatedUtc;
		public readonly List<ContentManifestEntry> Items = new List<ContentManifestEntry>();
	}

	private sealed class ContentManifestEntry
	{
		public string Id;
		public string Kind;
		public string Source;
		public string ProjectId;
		public string VersionId;
		public string VersionName;
		public string FileName;
		public string RelativePath;
		public string DisabledRelativePath;
		public string WorldName;
		public string Sha512;
		public string Sha1;
		public bool Managed;
		public bool Active;
		public string InstalledUtc;
		public string[] Dependencies = new string[0];
	}

	private sealed class InstalledContentItem
	{
		public ContentManifestEntry Entry;
		public string DisplayName;
		public string Kind;
		public string Source;
		public string Version;
		public string WorldName;
		public string State;
		public string FullPath;
		public bool Managed;
		public bool Active;
		public bool UpdateAvailable;
		public ModrinthFileInfo UpdateFile;
	}

	private sealed class ContentOperationProgress
	{
		public string Stage;
		public int Percent;
		public string Item;
	}

	private static string GetContentMetadataDirectory(string serverDirectory)
	{
		return Path.Combine(Path.GetFullPath(serverDirectory), ".mineharbor");
	}

	private static string GetContentManifestPath(string serverDirectory)
	{
		return Path.Combine(GetContentMetadataDirectory(serverDirectory), "content-manifest.json");
	}

	private static ContentManifestModel LoadContentManifest(string serverDirectory)
	{
		string path = GetContentManifestPath(serverDirectory);
		ContentManifestModel manifest = new ContentManifestModel();
		if (!File.Exists(path)) return manifest;
		FileInfo file = new FileInfo(path);
		if (file.Length <= 0 || file.Length > MaximumContentManifestBytes)
			throw new InvalidDataException("콘텐츠 manifest 크기가 올바르지 않습니다.");
		Dictionary<string, object> root;
		try
		{
			root = new JavaScriptSerializer { MaxJsonLength = MaximumContentManifestBytes }.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
		}
		catch (Exception exception)
		{
			throw new InvalidDataException("콘텐츠 manifest가 손상되었습니다. 파일을 덮어쓰지 않았습니다.", exception);
		}
		if (root == null || GetJsonInt(root, "schemaVersion") != ContentManifestSchemaVersion)
			throw new InvalidDataException("지원하지 않는 콘텐츠 manifest 형식입니다.");
		manifest.UpdatedUtc = GetJsonString(root, "updatedUtc");
		object[] items = root.ContainsKey("items") ? root["items"] as object[] : null;
		if (items == null) throw new InvalidDataException("콘텐츠 manifest 항목 목록이 없습니다.");
		if (items.Length > 10000) throw new InvalidDataException("콘텐츠 manifest 항목이 지나치게 많습니다.");
		HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> managedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < items.Length; i++)
		{
			Dictionary<string, object> value = items[i] as Dictionary<string, object>;
			if (value == null) throw new InvalidDataException("콘텐츠 manifest 항목 형식이 잘못되었습니다.");
			ContentManifestEntry entry = DeserializeContentEntry(value);
			ValidateContentManifestEntry(serverDirectory, entry);
			if (!ids.Add(entry.Id)) throw new InvalidDataException("콘텐츠 manifest에 중복 ID가 있습니다: " + entry.Id);
			string activePath = entry.Active ? entry.RelativePath : entry.DisabledRelativePath;
			if (!paths.Add(activePath)) throw new InvalidDataException("콘텐츠 manifest에 중복 파일 경로가 있습니다: " + activePath);
			if (IsManagedModrinthEntry(entry) && !managedProjects.Add(GetContentProjectKey(entry.Kind, entry.WorldName, entry.ProjectId))) throw new InvalidDataException("콘텐츠 manifest에 같은 Modrinth 프로젝트가 중복되어 있습니다: " + entry.ProjectId);
			manifest.Items.Add(entry);
		}
		ValidateContentManifestDependencyGraph(manifest);
		return manifest;
	}

	private static void SaveContentManifest(string serverDirectory, ContentManifestModel manifest)
	{
		if (manifest == null) throw new ArgumentNullException("manifest");
		Dictionary<string, object> root = new Dictionary<string, object>();
		root["schemaVersion"] = ContentManifestSchemaVersion;
		root["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		List<object> items = new List<object>();
		HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> managedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < manifest.Items.Count; i++)
		{
			ContentManifestEntry entry = manifest.Items[i];
			ValidateContentManifestEntry(serverDirectory, entry);
			if (!ids.Add(entry.Id)) throw new InvalidDataException("콘텐츠 manifest에 중복 ID가 있습니다: " + entry.Id);
			string activePath = entry.Active ? entry.RelativePath : entry.DisabledRelativePath;
			if (!paths.Add(activePath)) throw new InvalidDataException("콘텐츠 manifest에 중복 파일 경로가 있습니다: " + activePath);
			if (IsManagedModrinthEntry(entry) && !managedProjects.Add(GetContentProjectKey(entry.Kind, entry.WorldName, entry.ProjectId))) throw new InvalidDataException("콘텐츠 manifest에 같은 Modrinth 프로젝트가 중복되어 있습니다: " + entry.ProjectId);
			items.Add(SerializeContentEntry(entry));
		}
		ValidateContentManifestDependencyGraph(manifest);
		root["items"] = items.ToArray();
		string json = new JavaScriptSerializer { MaxJsonLength = MaximumContentManifestBytes }.Serialize(root);
		if (Encoding.UTF8.GetByteCount(json) > MaximumContentManifestBytes) throw new InvalidDataException("콘텐츠 manifest가 안전 크기 제한을 초과했습니다.");
		string directory = GetContentMetadataDirectory(serverDirectory);
		Directory.CreateDirectory(directory);
		string path = GetContentManifestPath(serverDirectory);
		string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			File.WriteAllText(temporary, json, new UTF8Encoding(false));
			ReplaceFile(temporary, path);
		}
		finally
		{
			DeleteFileIfPresent(temporary);
		}
	}

	private static ContentManifestEntry DeserializeContentEntry(Dictionary<string, object> value)
	{
		ContentManifestEntry entry = new ContentManifestEntry();
		entry.Id = GetJsonString(value, "id");
		entry.Kind = GetJsonString(value, "kind");
		entry.Source = GetJsonString(value, "source");
		entry.ProjectId = GetJsonString(value, "projectId");
		entry.VersionId = GetJsonString(value, "versionId");
		entry.VersionName = GetJsonString(value, "versionName");
		entry.FileName = GetJsonString(value, "fileName");
		entry.RelativePath = NormalizeContentRelativePath(GetJsonString(value, "relativePath"));
		entry.DisabledRelativePath = NormalizeContentRelativePath(GetJsonString(value, "disabledRelativePath"));
		entry.WorldName = GetJsonString(value, "worldName");
		entry.Sha512 = GetJsonString(value, "sha512");
		entry.Sha1 = GetJsonString(value, "sha1");
		entry.Managed = GetJsonBoolean(value, "managed");
		entry.Active = GetJsonBoolean(value, "active");
		entry.InstalledUtc = GetJsonString(value, "installedUtc");
		entry.Dependencies = GetJsonStringArray(value, "dependencies");
		return entry;
	}

	private static Dictionary<string, object> SerializeContentEntry(ContentManifestEntry entry)
	{
		Dictionary<string, object> value = new Dictionary<string, object>();
		value["id"] = entry.Id;
		value["kind"] = entry.Kind;
		value["source"] = entry.Source;
		value["projectId"] = entry.ProjectId ?? string.Empty;
		value["versionId"] = entry.VersionId ?? string.Empty;
		value["versionName"] = entry.VersionName ?? string.Empty;
		value["fileName"] = entry.FileName;
		value["relativePath"] = entry.RelativePath;
		value["disabledRelativePath"] = entry.DisabledRelativePath;
		value["worldName"] = entry.WorldName ?? string.Empty;
		value["sha512"] = entry.Sha512 ?? string.Empty;
		value["sha1"] = entry.Sha1 ?? string.Empty;
		value["managed"] = entry.Managed;
		value["active"] = entry.Active;
		value["installedUtc"] = entry.InstalledUtc ?? string.Empty;
		value["dependencies"] = entry.Dependencies ?? new string[0];
		return value;
	}

	private static void ValidateContentManifestEntry(string serverDirectory, ContentManifestEntry entry)
	{
		if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 128 || !IsContentKind(entry.Kind))
			throw new InvalidDataException("콘텐츠 manifest ID 또는 종류가 올바르지 않습니다.");
		if (string.IsNullOrWhiteSpace(entry.Source) || entry.Source.Length > 32 || string.IsNullOrWhiteSpace(entry.FileName) || Path.GetFileName(entry.FileName) != entry.FileName)
			throw new InvalidDataException("콘텐츠 manifest 출처 또는 파일명이 올바르지 않습니다.");
		entry.RelativePath = NormalizeContentRelativePath(entry.RelativePath);
		entry.DisabledRelativePath = NormalizeContentRelativePath(entry.DisabledRelativePath);
		GetSafeContentPath(serverDirectory, entry.RelativePath);
		GetSafeContentPath(serverDirectory, entry.DisabledRelativePath);
		if (entry.Dependencies == null || entry.Dependencies.Length > 256) throw new InvalidDataException("콘텐츠 의존성 목록이 올바르지 않습니다.");
		HashSet<string> dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < entry.Dependencies.Length; i++)
			if (string.IsNullOrWhiteSpace(entry.Dependencies[i]) || entry.Dependencies[i].Length > 128 || !dependencies.Add(entry.Dependencies[i])) throw new InvalidDataException("콘텐츠 의존성 식별자가 올바르지 않습니다.");
		if (!string.IsNullOrEmpty(entry.Sha512) && !IsHexString(entry.Sha512, 128)) throw new InvalidDataException("콘텐츠 SHA-512 값이 올바르지 않습니다.");
		if (!string.IsNullOrEmpty(entry.Sha1) && !IsHexString(entry.Sha1, 40)) throw new InvalidDataException("콘텐츠 SHA-1 값이 올바르지 않습니다.");
	}

	private static bool IsContentKind(string value)
	{
		return string.Equals(value, "plugin", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "mod", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "datapack", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsManagedModrinthEntry(ContentManifestEntry entry)
	{
		return entry != null && entry.Managed && string.Equals(entry.Source, "modrinth", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.ProjectId);
	}

	private static string GetContentProjectKey(string kind, string worldName, string projectId)
	{
		return (kind ?? string.Empty).ToLowerInvariant() + ":" + (worldName ?? string.Empty).ToLowerInvariant() + ":" + (projectId ?? string.Empty).ToLowerInvariant();
	}

	private static void ValidateContentManifestDependencyGraph(ContentManifestModel manifest)
	{
		Dictionary<string, string[]> graph = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < manifest.Items.Count; i++)
		{
			ContentManifestEntry entry = manifest.Items[i];
			if (!IsManagedModrinthEntry(entry)) continue;
			string prefix = (entry.Kind ?? string.Empty).ToLowerInvariant() + ":" + (entry.WorldName ?? string.Empty).ToLowerInvariant() + ":";
			string[] dependencies = new string[entry.Dependencies.Length];
			for (int dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) dependencies[dependencyIndex] = prefix + entry.Dependencies[dependencyIndex].ToLowerInvariant();
			graph[GetContentProjectKey(entry.Kind, entry.WorldName, entry.ProjectId)] = dependencies;
		}
		ValidateContentDependencyGraph(graph);
	}

	private static string NormalizeContentRelativePath(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 1024) throw new InvalidDataException("콘텐츠 상대 경로가 올바르지 않습니다.");
		string normalized = value.Replace('\\', '/').Trim('/');
		if (normalized.Length == 0 || Path.IsPathRooted(normalized) || normalized.IndexOf('\0') >= 0)
			throw new InvalidDataException("콘텐츠 상대 경로가 올바르지 않습니다.");
		string[] parts = normalized.Split('/');
		for (int i = 0; i < parts.Length; i++)
			if (parts[i].Length == 0 || parts[i] == "." || parts[i] == "..") throw new InvalidDataException("콘텐츠 상대 경로가 서버 밖을 가리킵니다.");
		return normalized;
	}

	private static string GetSafeContentPath(string serverDirectory, string relativePath)
	{
		string root = Path.GetFullPath(serverDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string path = Path.GetFullPath(Path.Combine(root, NormalizeContentRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar)));
		if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("콘텐츠 경로가 서버 폴더 밖을 가리킵니다.");
		return path;
	}

	private static List<InstalledContentItem> ScanInstalledContent(string serverDirectory, string serverType)
	{
		ContentManifestModel manifest = LoadContentManifest(serverDirectory);
		List<InstalledContentItem> result = new List<InstalledContentItem>();
		HashSet<string> knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < manifest.Items.Count; i++)
		{
			ContentManifestEntry entry = manifest.Items[i];
			string relative = entry.Active ? entry.RelativePath : entry.DisabledRelativePath;
			string fullPath = GetSafeContentPath(serverDirectory, relative);
			knownPaths.Add(Path.GetFullPath(fullPath));
			InstalledContentItem item = BuildInstalledContentItem(entry, fullPath);
			result.Add(item);
		}

		string contentFolder = GetContentFolderName(serverType);
		if (!string.IsNullOrEmpty(contentFolder)) ScanManualContentDirectory(serverDirectory, contentFolder, contentFolder == "plugins" ? "plugin" : "mod", knownPaths, result, string.Empty);
		List<string> worlds = GetDatapackWorldDirectories(serverDirectory);
		for (int i = 0; i < worlds.Count; i++)
		{
			string datapacks = Path.Combine(worlds[i], "datapacks");
			string worldName = new DirectoryInfo(worlds[i]).Name;
			ScanManualContentDirectory(serverDirectory, GetRelativeContentPath(serverDirectory, datapacks), "datapack", knownPaths, result, worldName);
		}
		return result.OrderBy(delegate(InstalledContentItem item) { return item.Kind; }).ThenBy(delegate(InstalledContentItem item) { return item.DisplayName; }, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	private static InstalledContentItem BuildInstalledContentItem(ContentManifestEntry entry, string fullPath)
	{
		InstalledContentItem item = new InstalledContentItem();
		item.Entry = entry;
		item.DisplayName = Path.GetFileName(entry.FileName);
		item.Kind = entry.Kind;
		item.Source = entry.Managed ? "MineHarbor/" + entry.Source : "Manual";
		item.Version = entry.VersionName;
		item.WorldName = entry.WorldName;
		item.FullPath = fullPath;
		item.Managed = entry.Managed;
		item.Active = entry.Active;
		if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) item.State = "Missing";
		else if (!entry.Active) item.State = "Disabled";
		else if (File.Exists(fullPath) && (!string.IsNullOrEmpty(entry.Sha512) || !string.IsNullOrEmpty(entry.Sha1)) && !VerifyContentFileHash(fullPath, entry.Sha512, entry.Sha1)) item.State = "Modified";
		else item.State = "Active";
		return item;
	}

	private static void ScanManualContentDirectory(string serverDirectory, string relativeDirectory, string kind, HashSet<string> knownPaths, List<InstalledContentItem> result, string worldName)
	{
		string directory = GetSafeContentPath(serverDirectory, NormalizeContentRelativePath(relativeDirectory));
		if (!Directory.Exists(directory)) return;
		FileInfo[] files = new DirectoryInfo(directory).GetFiles();
		for (int i = 0; i < files.Length; i++)
		{
			string extension = files[i].Extension;
			if ((kind == "datapack" && !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) || (kind != "datapack" && !extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))) continue;
			if (knownPaths.Contains(files[i].FullName)) continue;
			result.Add(BuildManualContentItem(serverDirectory, files[i].FullName, kind, worldName));
		}
		if (kind == "datapack")
		{
			DirectoryInfo[] directories = new DirectoryInfo(directory).GetDirectories();
			for (int i = 0; i < directories.Length; i++)
			{
				if (knownPaths.Contains(directories[i].FullName)) continue;
				if (File.Exists(Path.Combine(directories[i].FullName, "pack.mcmeta"))) result.Add(BuildManualContentItem(serverDirectory, directories[i].FullName, kind, worldName));
			}
		}
	}

	private static InstalledContentItem BuildManualContentItem(string serverDirectory, string path, string kind, string worldName)
	{
		ContentManifestEntry entry = new ContentManifestEntry();
		entry.Id = "manual-" + ComputeStableContentId(GetRelativeContentPath(serverDirectory, path));
		entry.Kind = kind;
		entry.Source = "manual";
		entry.FileName = Path.GetFileName(path);
		entry.RelativePath = GetRelativeContentPath(serverDirectory, path);
		entry.DisabledRelativePath = ".mineharbor/disabled/" + kind + "/" + entry.FileName;
		entry.WorldName = worldName;
		entry.Managed = false;
		entry.Active = true;
		entry.InstalledUtc = string.Empty;
		return BuildInstalledContentItem(entry, path);
	}

	private static string GetRelativeContentPath(string serverDirectory, string path)
	{
		Uri root = new Uri(Path.GetFullPath(serverDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
		Uri target = new Uri(Path.GetFullPath(path));
		string relative = Uri.UnescapeDataString(root.MakeRelativeUri(target).ToString()).Replace('\\', '/');
		return NormalizeContentRelativePath(relative);
	}

	private static string ComputeStableContentId(string value)
	{
		using (SHA256 sha = SHA256.Create())
			return ToLowerHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToLowerInvariant()))).Substring(0, 24);
	}

	private static bool VerifyContentFileHash(string path, string sha512Value, string sha1Value)
	{
		if (!File.Exists(path)) return false;
		if (!string.IsNullOrEmpty(sha512Value))
		{
			if (!IsHexString(sha512Value, 128)) return false;
			using (SHA512 sha = SHA512.Create()) using (FileStream stream = File.OpenRead(path)) return string.Equals(ToLowerHex(sha.ComputeHash(stream)), sha512Value, StringComparison.OrdinalIgnoreCase);
		}
		if (!string.IsNullOrEmpty(sha1Value))
		{
			if (!IsHexString(sha1Value, 40)) return false;
			using (SHA1 sha = SHA1.Create()) using (FileStream stream = File.OpenRead(path)) return string.Equals(ToLowerHex(sha.ComputeHash(stream)), sha1Value, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static bool IsHexString(string value, int length)
	{
		if (string.IsNullOrEmpty(value) || value.Length != length) return false;
		for (int i = 0; i < value.Length; i++) if (!Uri.IsHexDigit(value[i])) return false;
		return true;
	}

	private static void SetContentEnabled(string serverDirectory, ContentManifestEntry requestedEntry, bool enabled)
	{
		ContentManifestModel manifest = LoadContentManifest(serverDirectory);
		ContentManifestEntry entry = FindOrAddContentEntry(serverDirectory, manifest, requestedEntry);
		if (entry.Active == enabled) return;
		string source = GetSafeContentPath(serverDirectory, entry.Active ? entry.RelativePath : entry.DisabledRelativePath);
		string destination = GetSafeContentPath(serverDirectory, enabled ? entry.RelativePath : entry.DisabledRelativePath);
		if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("콘텐츠 파일을 찾지 못했습니다.", source);
		if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("활성화 상태를 바꿀 대상 경로가 이미 존재합니다.");
		Directory.CreateDirectory(Path.GetDirectoryName(destination));
		if (File.Exists(source)) File.Move(source, destination); else Directory.Move(source, destination);
		entry.Active = enabled;
		try { SaveContentManifest(serverDirectory, manifest); }
		catch
		{
			if (File.Exists(destination)) File.Move(destination, source); else if (Directory.Exists(destination)) Directory.Move(destination, source);
			entry.Active = !enabled;
			throw;
		}
	}

	private static void RemoveContentItem(string serverDirectory, ContentManifestEntry requestedEntry)
	{
		ContentManifestModel manifest = LoadContentManifest(serverDirectory);
		ContentManifestEntry entry = FindOrAddContentEntry(serverDirectory, manifest, requestedEntry);
		if (IsManagedModrinthEntry(entry))
		{
			for (int i = 0; i < manifest.Items.Count; i++)
			{
				ContentManifestEntry candidate = manifest.Items[i];
				if (ReferenceEquals(candidate, entry) || !string.Equals(candidate.Kind, entry.Kind, StringComparison.OrdinalIgnoreCase) || !string.Equals(candidate.WorldName ?? string.Empty, entry.WorldName ?? string.Empty, StringComparison.OrdinalIgnoreCase)) continue;
				if (Array.Exists(candidate.Dependencies ?? new string[0], delegate(string dependency) { return string.Equals(dependency, entry.ProjectId, StringComparison.OrdinalIgnoreCase); })) throw new InvalidOperationException("다른 설치 콘텐츠가 이 필수 의존성을 사용하고 있어 제거할 수 없습니다: " + candidate.FileName);
			}
		}
		string source = GetSafeContentPath(serverDirectory, entry.Active ? entry.RelativePath : entry.DisabledRelativePath);
		if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("제거할 콘텐츠 파일을 찾지 못했습니다.", source);
		string trashRelative = ".mineharbor/content-trash/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "/" + entry.Kind + "/" + entry.FileName;
		string trash = GetSafeContentPath(serverDirectory, trashRelative);
		Directory.CreateDirectory(Path.GetDirectoryName(trash));
		if (File.Exists(source)) File.Move(source, trash); else Directory.Move(source, trash);
		manifest.Items.Remove(entry);
		try { SaveContentManifest(serverDirectory, manifest); }
		catch
		{
			if (File.Exists(trash)) File.Move(trash, source); else if (Directory.Exists(trash)) Directory.Move(trash, source);
			manifest.Items.Add(entry);
			throw;
		}
	}

	private static ContentManifestEntry FindOrAddContentEntry(string serverDirectory, ContentManifestModel manifest, ContentManifestEntry requestedEntry)
	{
		for (int i = 0; i < manifest.Items.Count; i++) if (string.Equals(manifest.Items[i].Id, requestedEntry.Id, StringComparison.OrdinalIgnoreCase)) return manifest.Items[i];
		ValidateContentManifestEntry(serverDirectory, requestedEntry);
		manifest.Items.Add(requestedEntry);
		return requestedEntry;
	}

	private static List<ModrinthProjectInfo> SearchModrinthProjectsForKind(string query, LauncherOptions options, string kind)
	{
		if (!string.Equals(kind, "datapack", StringComparison.OrdinalIgnoreCase)) return SearchModrinthProjects(query, options);
		object[] facets = new object[]
		{
			new string[] { "versions:" + options.MinecraftVersion },
			new string[] { "project_type:datapack" }
		};
		string url = "https://api.modrinth.com/v2/search?limit=50&index=downloads&facets=" + Uri.EscapeDataString(new JavaScriptSerializer().Serialize(facets));
		if (!string.IsNullOrWhiteSpace(query)) url += "&query=" + Uri.EscapeDataString(query);
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(DownloadModrinthText(url)) as Dictionary<string, object>;
		object[] hits = root != null && root.ContainsKey("hits") ? root["hits"] as object[] : null;
		List<ModrinthProjectInfo> result = new List<ModrinthProjectInfo>();
		if (hits == null) return result;
		for (int i = 0; i < hits.Length; i++)
		{
			Dictionary<string, object> hit = hits[i] as Dictionary<string, object>;
			if (hit == null || !ContainsIgnoreCase(GetJsonStringArray(hit, "versions"), options.MinecraftVersion)) continue;
			string[] allTypes = GetJsonStringArray(hit, "all_project_types");
			if (!ContainsIgnoreCase(allTypes, "datapack") && !string.Equals(GetJsonString(hit, "project_type"), "datapack", StringComparison.OrdinalIgnoreCase)) continue;
			ModrinthProjectInfo project = new ModrinthProjectInfo();
			project.Id = GetJsonString(hit, "project_id");
			project.Slug = GetJsonString(hit, "slug");
			project.Title = GetJsonString(hit, "title");
			project.Description = GetJsonString(hit, "description");
			project.Author = GetJsonString(hit, "author");
			project.ProjectType = "datapack";
			project.ServerSide = GetJsonString(hit, "server_side");
			project.IconUrl = GetJsonString(hit, "icon_url");
			project.Downloads = GetJsonLong(hit, "downloads");
			project.Categories = GetJsonStringArray(hit, "categories");
			project.Versions = GetJsonStringArray(hit, "versions");
			if (!string.IsNullOrEmpty(project.Id)) result.Add(project);
		}
		return result;
	}

	private static ModrinthFileInfo GetCompatibleModrinthFileForKind(string projectId, LauncherOptions options, string kind)
	{
		string loader = string.Equals(kind, "datapack", StringComparison.OrdinalIgnoreCase) ? "datapack" : GetModrinthLoader(options.ServerType);
		string loaderJson = new JavaScriptSerializer().Serialize(new string[] { loader });
		string gameJson = new JavaScriptSerializer().Serialize(new string[] { options.MinecraftVersion });
		string url = "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(projectId) + "/version?include_changelog=false&loaders=" + Uri.EscapeDataString(loaderJson) + "&game_versions=" + Uri.EscapeDataString(gameJson);
		object[] versions = new JavaScriptSerializer().DeserializeObject(DownloadModrinthText(url)) as object[];
		if (versions == null || versions.Length == 0) throw new InvalidDataException("선택한 Minecraft 버전과 로더에 맞는 콘텐츠 파일을 찾지 못했습니다.");
		Dictionary<string, object> selected = null;
		for (int pass = 0; pass < 2 && selected == null; pass++)
		{
			for (int i = 0; i < versions.Length; i++)
			{
				Dictionary<string, object> candidate = versions[i] as Dictionary<string, object>;
				if (candidate == null || !ContainsIgnoreCase(GetJsonStringArray(candidate, "game_versions"), options.MinecraftVersion) || !ContainsIgnoreCase(GetJsonStringArray(candidate, "loaders"), loader)) continue;
				if (pass == 0 && !string.Equals(GetJsonString(candidate, "version_type"), "release", StringComparison.OrdinalIgnoreCase)) continue;
				selected = candidate;
				break;
			}
		}
		if (selected == null) throw new InvalidDataException("설치 가능한 콘텐츠 릴리스를 찾지 못했습니다.");
		object[] files = selected.ContainsKey("files") ? selected["files"] as object[] : null;
		Dictionary<string, object> selectedFile = null;
		if (files != null)
		{
			for (int i = 0; i < files.Length; i++)
			{
				Dictionary<string, object> candidate = files[i] as Dictionary<string, object>;
				if (candidate == null) continue;
				string filename = GetJsonString(candidate, "filename");
				bool correctExtension = string.Equals(kind, "datapack", StringComparison.OrdinalIgnoreCase) ? filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) : filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
				if (!correctExtension) continue;
				if (selectedFile == null) selectedFile = candidate;
				if (GetJsonBoolean(candidate, "primary")) { selectedFile = candidate; break; }
			}
		}
		if (selectedFile == null) throw new InvalidDataException("콘텐츠 다운로드 파일을 찾지 못했습니다.");
		ModrinthFileInfo info = new ModrinthFileInfo();
		info.ProjectId = projectId;
		info.VersionId = GetJsonString(selected, "id");
		info.VersionName = GetJsonString(selected, "version_number");
		info.FileName = GetJsonString(selectedFile, "filename");
		info.Url = GetJsonString(selectedFile, "url");
		info.Size = GetJsonLong(selectedFile, "size");
		Dictionary<string, object> hashes = selectedFile.ContainsKey("hashes") ? selectedFile["hashes"] as Dictionary<string, object> : null;
		info.Sha512 = GetJsonString(hashes, "sha512");
		info.Sha1 = GetJsonString(hashes, "sha1");
		object[] dependencies = selected.ContainsKey("dependencies") ? selected["dependencies"] as object[] : null;
		if (dependencies != null) for (int i = 0; i < dependencies.Length; i++) { Dictionary<string, object> dependency = dependencies[i] as Dictionary<string, object>; if (dependency != null) info.Dependencies.Add(dependency); }
		ValidateManagedModrinthFile(info, kind);
		return info;
	}

	private static void ValidateManagedModrinthFile(ModrinthFileInfo info, string kind)
	{
		if (info == null || string.IsNullOrWhiteSpace(info.FileName) || Path.GetFileName(info.FileName) != info.FileName) throw new InvalidDataException("Modrinth가 안전한 파일명을 제공하지 않았습니다.");
		if (string.Equals(kind, "datapack", StringComparison.OrdinalIgnoreCase))
		{
			if (!info.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("데이터팩은 ZIP 파일만 설치할 수 있습니다.");
		}
		else if (!info.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("플러그인과 모드는 JAR 파일만 설치할 수 있습니다.");
		Uri uri;
		if (!Uri.TryCreate(info.Url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Modrinth CDN 다운로드 주소를 검증하지 못했습니다.");
		if (info.Size <= 0 || info.Size > 536870912L || (!IsHexString(info.Sha512, 128) && !IsHexString(info.Sha1, 40))) throw new InvalidDataException("콘텐츠 파일 크기 또는 해시가 올바르지 않습니다.");
	}

	private static string InstallManagedModrinthContent(string projectId, LauncherOptions options, string kind, string worldName, IProgress<ContentOperationProgress> progress, CancellationToken cancellationToken)
	{
		if (FindManagedModrinthEntry(options.ServerDirectory, projectId, kind, worldName) != null) throw new InvalidOperationException("같은 Modrinth 프로젝트가 이미 설치되어 있습니다.");
		HashSet<string> visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		return InstallManagedModrinthContentRecursive(projectId, options, kind, worldName, visiting, complete, progress, cancellationToken, 0);
	}

	private static string InstallManagedModrinthContentRecursive(string projectId, LauncherOptions options, string kind, string worldName, HashSet<string> visiting, HashSet<string> complete, IProgress<ContentOperationProgress> progress, CancellationToken cancellationToken, int depth)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (depth > 32) throw new InvalidDataException("콘텐츠 의존성 단계가 지나치게 깊습니다.");
		if (complete.Contains(projectId)) return string.Empty;
		if (!visiting.Add(projectId)) throw new InvalidDataException("콘텐츠 의존성 순환을 발견했습니다: " + projectId);
		ReportContentProgress(progress, "metadata", 5, projectId);
		ModrinthFileInfo file = GetCompatibleModrinthFileForKind(projectId, options, kind);
		List<string> dependencyIds = new List<string>();
		for (int i = 0; i < file.Dependencies.Count; i++)
		{
			Dictionary<string, object> dependency = file.Dependencies[i];
			if (!string.Equals(GetJsonString(dependency, "dependency_type"), "required", StringComparison.OrdinalIgnoreCase)) continue;
			string dependencyProject = GetJsonString(dependency, "project_id");
			if (string.IsNullOrEmpty(dependencyProject))
			{
				string dependencyVersion = GetJsonString(dependency, "version_id");
				if (!string.IsNullOrEmpty(dependencyVersion)) dependencyProject = GetProjectIdForModrinthVersion(dependencyVersion);
			}
			if (string.IsNullOrEmpty(dependencyProject)) throw new InvalidDataException("필수 콘텐츠 의존성의 프로젝트를 확인하지 못했습니다.");
			if (!dependencyIds.Contains(dependencyProject, StringComparer.OrdinalIgnoreCase)) dependencyIds.Add(dependencyProject);
			InstallManagedModrinthContentRecursive(dependencyProject, options, kind, worldName, visiting, complete, progress, cancellationToken, depth + 1);
		}
		ContentManifestEntry existing = FindManagedModrinthEntry(options.ServerDirectory, projectId, kind, worldName);
		string installed;
		if (existing != null && string.Equals(existing.VersionId, file.VersionId, StringComparison.OrdinalIgnoreCase))
		{
			installed = GetSafeContentPath(options.ServerDirectory, existing.Active ? existing.RelativePath : existing.DisabledRelativePath);
		}
		else installed = InstallManagedModrinthFile(file, options, kind, worldName, dependencyIds.ToArray(), existing != null, progress, cancellationToken);
		visiting.Remove(projectId);
		complete.Add(projectId);
		return installed;
	}

	private static ContentManifestEntry FindManagedModrinthEntry(string serverDirectory, string projectId, string kind, string worldName)
	{
		ContentManifestModel manifest = LoadContentManifest(serverDirectory);
		for (int i = 0; i < manifest.Items.Count; i++)
		{
			ContentManifestEntry entry = manifest.Items[i];
			if (entry.Managed && string.Equals(entry.Source, "modrinth", StringComparison.OrdinalIgnoreCase)
				&& string.Equals(entry.ProjectId, projectId, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(entry.Kind, kind, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(entry.WorldName ?? string.Empty, worldName ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return entry;
		}
		return null;
	}

	private static string InstallManagedModrinthFile(ModrinthFileInfo file, LauncherOptions options, string kind, string worldName, string[] dependencyIds, bool update, IProgress<ContentOperationProgress> progress, CancellationToken cancellationToken)
	{
		ContentManifestModel manifest = LoadContentManifest(options.ServerDirectory);
		ContentManifestEntry existing = null;
		for (int i = 0; i < manifest.Items.Count; i++)
			if (string.Equals(manifest.Items[i].ProjectId, file.ProjectId, StringComparison.OrdinalIgnoreCase) && string.Equals(manifest.Items[i].Kind, kind, StringComparison.OrdinalIgnoreCase) && string.Equals(manifest.Items[i].WorldName ?? string.Empty, worldName ?? string.Empty, StringComparison.OrdinalIgnoreCase)) { existing = manifest.Items[i]; break; }
		if (existing != null && !update) throw new InvalidOperationException("같은 Modrinth 프로젝트가 이미 설치되어 있습니다.");
		string relativeDirectory = GetContentTargetRelativeDirectory(options, kind, worldName);
		string destinationRelative = NormalizeContentRelativePath(relativeDirectory + "/" + file.FileName);
		string entryId = existing == null ? "modrinth-" + ComputeStableContentId(kind + ":" + file.ProjectId + ":" + (worldName ?? string.Empty)) : existing.Id;
		string disabledRelative = NormalizeContentRelativePath(".mineharbor/disabled/" + kind + "/" + entryId + "-" + file.FileName);
		bool targetActive = existing == null || existing.Active;
		string destination = GetSafeContentPath(options.ServerDirectory, targetActive ? destinationRelative : disabledRelative);
		if (existing == null && (File.Exists(destination) || Directory.Exists(destination))) throw new IOException("같은 이름의 콘텐츠 파일이 이미 존재합니다.");
		Directory.CreateDirectory(Path.GetDirectoryName(destination));
		string temporary = destination + ".download-" + Guid.NewGuid().ToString("N");
		string previous = null;
		string previousOriginalPath = null;
		bool installedNewFile = false;
		try
		{
			ReportContentProgress(progress, "download", 10, file.FileName);
			DownloadModrinthBinaryWithProgress(file.Url, temporary, file.Size, progress, cancellationToken);
			if (!VerifyContentFileHash(temporary, file.Sha512, file.Sha1)) throw new InvalidDataException("다운로드한 콘텐츠의 무결성 검증에 실패했습니다.");
			if (string.Equals(kind, "datapack", StringComparison.OrdinalIgnoreCase)) ValidateDatapackArchive(temporary);
			if (existing != null)
			{
				previousOriginalPath = GetSafeContentPath(options.ServerDirectory, existing.Active ? existing.RelativePath : existing.DisabledRelativePath);
				if (!File.Exists(previousOriginalPath)) throw new FileNotFoundException("업데이트할 기존 콘텐츠 파일을 찾지 못했습니다.", previousOriginalPath);
				string backupRelative = ".mineharbor/content-backups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "/" + existing.FileName;
				previous = GetSafeContentPath(options.ServerDirectory, backupRelative);
				Directory.CreateDirectory(Path.GetDirectoryName(previous));
				File.Move(previousOriginalPath, previous);
			}
			ReplaceFile(temporary, destination);
			installedNewFile = true;
			ContentManifestEntry entry = existing ?? new ContentManifestEntry();
			if (existing == null) manifest.Items.Add(entry);
			entry.Id = entryId;
			entry.Kind = kind;
			entry.Source = "modrinth";
			entry.ProjectId = file.ProjectId;
			entry.VersionId = file.VersionId;
			entry.VersionName = file.VersionName;
			entry.FileName = file.FileName;
			entry.RelativePath = destinationRelative;
			entry.DisabledRelativePath = disabledRelative;
			entry.WorldName = worldName ?? string.Empty;
			entry.Sha512 = file.Sha512;
			entry.Sha1 = file.Sha1;
			entry.Managed = true;
			entry.Active = targetActive;
			entry.InstalledUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
			entry.Dependencies = dependencyIds ?? new string[0];
			SaveContentManifest(options.ServerDirectory, manifest);
			ReportContentProgress(progress, "complete", 100, file.FileName);
			return destination;
		}
		catch
		{
			if (installedNewFile && File.Exists(destination)) DeleteFileIfPresent(destination);
			if (previous != null && previousOriginalPath != null && File.Exists(previous) && !File.Exists(previousOriginalPath)) File.Move(previous, previousOriginalPath);
			throw;
		}
		finally { DeleteFileIfPresent(temporary); }
	}

	private static string GetContentTargetRelativeDirectory(LauncherOptions options, string kind, string worldName)
	{
		if (string.Equals(kind, "datapack", StringComparison.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(worldName) || Path.GetFileName(worldName) != worldName) throw new InvalidDataException("데이터팩을 설치할 월드가 올바르지 않습니다.");
			string world = Path.Combine(options.ServerDirectory, worldName);
			if (!Directory.Exists(world) || !File.Exists(Path.Combine(world, "level.dat"))) throw new DirectoryNotFoundException("선택한 Minecraft 월드를 찾지 못했습니다.");
			return NormalizeContentRelativePath(worldName + "/datapacks");
		}
		string folder = GetContentFolderName(options.ServerType);
		if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException("이 서버 종류는 플러그인 또는 모드 자동 설치를 지원하지 않습니다.");
		return folder;
	}

	private static void DownloadModrinthBinaryWithProgress(string url, string path, long expectedSize, IProgress<ContentOperationProgress> progress, CancellationToken cancellationToken)
	{
		System.Net.HttpWebRequest request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
		request.Method = "GET";
		request.UserAgent = GetLauncherIntegrationUserAgent();
		request.Accept = "application/java-archive,application/zip,application/octet-stream";
		request.Timeout = 120000;
		request.ReadWriteTimeout = 120000;
		request.AllowAutoRedirect = false;
		try
		{
			using (cancellationToken.Register(delegate { try { request.Abort(); } catch { } }))
			using (System.Net.HttpWebResponse response = (System.Net.HttpWebResponse)request.GetResponse())
			{
				if (response.StatusCode != System.Net.HttpStatusCode.OK || response.ResponseUri == null || response.ResponseUri.Scheme != Uri.UriSchemeHttps || !response.ResponseUri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) || response.ContentLength > expectedSize) throw new System.Net.WebException("Modrinth CDN이 정상 응답하지 않았습니다.");
				using (Stream input = response.GetResponseStream())
				using (FileStream output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					byte[] buffer = new byte[131072];
					long total = 0;
					int read;
					while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
					{
						cancellationToken.ThrowIfCancellationRequested();
						output.Write(buffer, 0, read);
						total = checked(total + read);
						if (total > expectedSize) throw new InvalidDataException("콘텐츠 다운로드 크기가 공식 정보보다 큽니다.");
						ReportContentProgress(progress, "download", 10 + (int)Math.Min(80L, total * 80L / expectedSize), Path.GetFileName(path));
					}
					output.Flush(true);
					if (total != expectedSize) throw new InvalidDataException("콘텐츠 다운로드 크기가 공식 정보와 다릅니다.");
				}
			}
		}
		catch (System.Net.WebException)
		{
			if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
			throw;
		}
	}

	private static bool CheckContentUpdate(LauncherOptions options, InstalledContentItem item, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (item == null || item.Entry == null || !item.Managed || !string.Equals(item.Entry.Source, "modrinth", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(item.Entry.ProjectId)) return false;
		ModrinthFileInfo latest = GetCompatibleModrinthFileForKind(item.Entry.ProjectId, options, item.Entry.Kind);
		item.UpdateFile = latest;
		item.UpdateAvailable = !string.Equals(latest.VersionId, item.Entry.VersionId, StringComparison.OrdinalIgnoreCase);
		return item.UpdateAvailable;
	}

	private static string UpdateManagedContent(LauncherOptions options, InstalledContentItem item, IProgress<ContentOperationProgress> progress, CancellationToken cancellationToken)
	{
		if (item == null || item.Entry == null || !item.Managed || !string.Equals(item.Entry.Source, "modrinth", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("MineHarbor가 설치한 Modrinth 콘텐츠만 자동 업데이트할 수 있습니다.");
		ModrinthFileInfo latest = item.UpdateFile ?? GetCompatibleModrinthFileForKind(item.Entry.ProjectId, options, item.Entry.Kind);
		if (string.Equals(latest.VersionId, item.Entry.VersionId, StringComparison.OrdinalIgnoreCase)) return item.FullPath;
		HashSet<string> visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		return InstallManagedModrinthContentRecursive(item.Entry.ProjectId, options, item.Entry.Kind, item.Entry.WorldName, visiting, complete, progress, cancellationToken, 0);
	}

	private static string InstallLocalContentFile(string sourcePath, LauncherOptions options, string kind, string worldName)
	{
		if (!File.Exists(sourcePath)) throw new FileNotFoundException("설치 파일을 찾지 못했습니다.", sourcePath);
		string extension = Path.GetExtension(sourcePath);
		if (string.Equals(kind, "datapack", StringComparison.OrdinalIgnoreCase))
		{
			if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("데이터팩은 ZIP 파일만 설치할 수 있습니다.");
			ValidateDatapackArchive(sourcePath);
		}
		else if (!extension.Equals(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("플러그인과 모드는 JAR 파일만 설치할 수 있습니다.");
		string directory = GetContentTargetRelativeDirectory(options, kind, worldName);
		string relative = NormalizeContentRelativePath(directory + "/" + Path.GetFileName(sourcePath));
		string destination = GetSafeContentPath(options.ServerDirectory, relative);
		if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("같은 이름의 콘텐츠가 이미 설치되어 있습니다.");
		Directory.CreateDirectory(Path.GetDirectoryName(destination));
		File.Copy(sourcePath, destination, false);
		ContentManifestModel manifest = LoadContentManifest(options.ServerDirectory);
		ContentManifestEntry entry = new ContentManifestEntry();
		entry.Id = "local-" + ComputeStableContentId(kind + ":" + relative);
		entry.Kind = kind;
		entry.Source = "local-file";
		entry.FileName = Path.GetFileName(sourcePath);
		entry.RelativePath = relative;
		entry.DisabledRelativePath = NormalizeContentRelativePath(".mineharbor/disabled/" + kind + "/" + entry.Id + "-" + entry.FileName);
		entry.WorldName = worldName ?? string.Empty;
		entry.Sha512 = GetFileSha512(destination);
		entry.Managed = true;
		entry.Active = true;
		entry.InstalledUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		manifest.Items.Add(entry);
		try { SaveContentManifest(options.ServerDirectory, manifest); }
		catch { DeleteFileIfPresent(destination); throw; }
		return destination;
	}

	private static string GetFileSha512(string path)
	{
		using (SHA512 sha = SHA512.Create()) using (FileStream stream = File.OpenRead(path)) return ToLowerHex(sha.ComputeHash(stream));
	}

	private static void ReportContentProgress(IProgress<ContentOperationProgress> progress, string stage, int percent, string item)
	{
		if (progress == null) return;
		ContentOperationProgress value = new ContentOperationProgress();
		value.Stage = stage;
		value.Percent = Math.Max(0, Math.Min(100, percent));
		value.Item = item;
		progress.Report(value);
	}

	private static List<string> GetDatapackWorldDirectories(string serverDirectory)
	{
		List<string> result = new List<string>();
		string root = Path.GetFullPath(serverDirectory);
		if (!Directory.Exists(root)) return result;
		DirectoryInfo[] directories = new DirectoryInfo(root).GetDirectories();
		for (int i = 0; i < directories.Length; i++) if (File.Exists(Path.Combine(directories[i].FullName, "level.dat"))) result.Add(directories[i].FullName);
		string configured = ReadServerProperty(Path.Combine(root, "server.properties"), "level-name");
		if (!string.IsNullOrWhiteSpace(configured))
		{
			string configuredPath = Path.GetFullPath(Path.Combine(root, configured));
			if (configuredPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(configuredPath) && !result.Contains(configuredPath, StringComparer.OrdinalIgnoreCase)) result.Insert(0, configuredPath);
		}
		return result;
	}

	private static string ReadServerProperty(string path, string key)
	{
		if (!File.Exists(path)) return string.Empty;
		string[] lines = File.ReadAllLines(path);
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return line.Substring(key.Length + 1).Trim();
		}
		return string.Empty;
	}

	private static void ValidateDatapackArchive(string path)
	{
		FileInfo file = new FileInfo(path);
		if (!file.Exists || file.Length <= 0 || file.Length > 536870912L) throw new InvalidDataException("데이터팩 ZIP 크기가 허용 범위를 벗어났습니다.");
		using (ZipArchive archive = ZipFile.OpenRead(path))
		{
			if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumDatapackEntries) throw new InvalidDataException("데이터팩 ZIP 항목 수가 올바르지 않습니다.");
			long expanded = 0;
			ZipArchiveEntry metadata = null;
			HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < archive.Entries.Count; i++)
			{
				ZipArchiveEntry entry = archive.Entries[i];
				string name = entry.FullName.Replace('\\', '/');
				if (!IsSafeDatapackEntryPath(name) || !paths.Add(name)) throw new InvalidDataException("데이터팩 ZIP 경로가 안전하지 않습니다.");
				expanded = checked(expanded + entry.Length);
				if (expanded > MaximumDatapackExpandedBytes) throw new InvalidDataException("데이터팩의 총 해제 크기가 허용 범위를 초과했습니다.");
				if (string.Equals(name, "pack.mcmeta", StringComparison.OrdinalIgnoreCase)) metadata = entry;
			}
			if (metadata == null || metadata.Length <= 0 || metadata.Length > 1048576L) throw new InvalidDataException("데이터팩 루트에서 pack.mcmeta를 찾지 못했습니다.");
			using (StreamReader reader = new StreamReader(metadata.Open(), Encoding.UTF8))
			{
				Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
				Dictionary<string, object> pack = root != null && root.ContainsKey("pack") ? root["pack"] as Dictionary<string, object> : null;
				if (pack == null || !pack.ContainsKey("pack_format") || Convert.ToInt32(pack["pack_format"], CultureInfo.InvariantCulture) <= 0) throw new InvalidDataException("pack.mcmeta의 pack_format이 올바르지 않습니다.");
			}
		}
	}

	private static bool IsSafeDatapackEntryPath(string name)
	{
		if (string.IsNullOrEmpty(name) || name.Length > 4096 || name[0] == '/' || name.IndexOf('\0') >= 0 || name.IndexOf(':') >= 0) return false;
		string trimmed = name.EndsWith("/", StringComparison.Ordinal) ? name.Substring(0, name.Length - 1) : name;
		if (trimmed.Length == 0) return false;
		string[] parts = trimmed.Split('/');
		for (int i = 0; i < parts.Length; i++)
			if (parts[i].Length == 0 || parts[i] == "." || parts[i] == "..") return false;
		return true;
	}

	private static void ValidateContentDependencyGraph(Dictionary<string, string[]> graph)
	{
		if (graph == null) throw new ArgumentNullException("graph");
		HashSet<string> complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string node in graph.Keys) VisitContentDependency(node, graph, complete, visiting, 0);
	}

	private static void VisitContentDependency(string node, Dictionary<string, string[]> graph, HashSet<string> complete, HashSet<string> visiting, int depth)
	{
		if (complete.Contains(node)) return;
		if (depth > 64 || !visiting.Add(node)) throw new InvalidDataException("콘텐츠 의존성 순환을 발견했습니다: " + node);
		string[] dependencies;
		if (graph.TryGetValue(node, out dependencies) && dependencies != null)
			for (int i = 0; i < dependencies.Length; i++) if (!string.IsNullOrWhiteSpace(dependencies[i])) VisitContentDependency(dependencies[i], graph, complete, visiting, depth + 1);
		visiting.Remove(node);
		complete.Add(node);
	}

	private static int GetJsonInt(Dictionary<string, object> dictionary, string key)
	{
		return dictionary != null && dictionary.ContainsKey(key) && dictionary[key] != null ? Convert.ToInt32(dictionary[key], CultureInfo.InvariantCulture) : 0;
	}

	private static bool GetJsonBoolean(Dictionary<string, object> dictionary, string key)
	{
		return dictionary != null && dictionary.ContainsKey(key) && dictionary[key] != null && Convert.ToBoolean(dictionary[key], CultureInfo.InvariantCulture);
	}
}
