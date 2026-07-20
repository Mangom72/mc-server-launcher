using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static partial class Launcher
{
	private sealed class ModrinthProjectInfo
	{
		public string Id;
		public string Slug;
		public string Title;
		public string Description;
		public string Author;
		public string ProjectType;
		public string ServerSide;
		public string IconUrl;
		public long Downloads;
		public string[] Categories;
		public string[] Versions;
	}

	private sealed class ModrinthFileInfo
	{
		public string ProjectId;
		public string VersionId;
		public string VersionName;
		public string FileName;
		public string Url;
		public long Size;
		public string Sha512;
		public string Sha1;
		public List<Dictionary<string, object>> Dependencies = new List<Dictionary<string, object>>();
	}

	private sealed class ContentManagerForm : Form
	{
		private readonly LauncherOptions options;
		private readonly TextBox searchBox;
		private readonly ListView resultList;
		private readonly Label statusLabel;
		private readonly Button searchButton;
		private readonly Button installButton;
		private readonly Button openFolderButton;
		private readonly PictureBox projectIcon;
		private readonly Label projectTitleLabel;
		private readonly Label projectMetaLabel;
		private readonly Label projectDescriptionLabel;
		private readonly List<ModrinthProjectInfo> projects = new List<ModrinthProjectInfo>();
		private bool busy;
		private int iconLoadRequest;
		private Image currentProjectImage;

		public ContentManagerForm(LauncherOptions launcherOptions)
		{
			ApplyLauncherWindowIcon(this);
			options = launcherOptions;
			bool korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
			Text = korean ? "서버 콘텐츠" : "Server content";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(900, 580);
			Size = new Size(1060, 680);
			Font = new Font("Pretendard", 11F);
			AutoScaleMode = AutoScaleMode.Dpi;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(24);
			root.RowCount = 5;
			root.ColumnCount = 1;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
			Controls.Add(root);

			Label heading = new Label();
			bool pluginContent = string.Equals(GetContentFolderName(options.ServerType), "plugins", StringComparison.OrdinalIgnoreCase);
			heading.Text = korean
				? (pluginContent ? "인기 플러그인" : "인기 모드")
				: (pluginContent ? "Popular plugins" : "Popular mods");
			heading.Font = new Font("Pretendard", 17F, FontStyle.Bold);
			heading.Dock = DockStyle.Fill;
			heading.TextAlign = ContentAlignment.MiddleLeft;
			root.Controls.Add(heading, 0, 0);

			Panel searchPanel = new Panel();
			searchPanel.Dock = DockStyle.Fill;
			root.Controls.Add(searchPanel, 0, 1);
			searchBox = new TextBox();
			searchBox.Font = new Font("Pretendard", 11F);
			searchBox.Location = new Point(0, 8);
			searchBox.Size = new Size(570, 32);
			searchBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			searchBox.KeyDown += delegate(object sender, KeyEventArgs eventArgs)
			{
				if (eventArgs.KeyCode == Keys.Enter)
				{
					BeginSearch();
					eventArgs.SuppressKeyPress = true;
				}
			};
			searchPanel.Controls.Add(searchBox);
			searchButton = NewContentButton(korean ? "검색" : "Search", 96);
			ApplyButtonIcon(searchButton, ButtonIcon.Search);
			searchButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			searchButton.Location = new Point(searchPanel.Width - 96, 4);
			searchButton.Click += delegate { BeginSearch(); };
			searchPanel.Controls.Add(searchButton);
			searchPanel.Resize += delegate
			{
				searchButton.Left = searchPanel.ClientSize.Width - searchButton.Width;
				searchBox.Width = Math.Max(120, searchButton.Left - 12);
			};

			SplitContainer contentSplit = new SplitContainer();
			contentSplit.Dock = DockStyle.Fill;
			contentSplit.Size = new Size(980, 420);
			contentSplit.Panel1MinSize = 420;
			contentSplit.Panel2MinSize = 250;
			contentSplit.SplitterDistance = 610;
			root.Controls.Add(contentSplit, 0, 2);

			resultList = new BufferedListView();
			resultList.Dock = DockStyle.Fill;
			resultList.View = View.Details;
			resultList.FullRowSelect = true;
			resultList.HideSelection = false;
			resultList.MultiSelect = false;
			resultList.Columns.Add(korean ? "이름" : "Name", 250);
			resultList.Columns.Add(korean ? "제작자" : "Author", 135);
			resultList.Columns.Add(korean ? "다운로드" : "Downloads", 105);
			resultList.SelectedIndexChanged += delegate
			{
				installButton.Enabled = !busy && resultList.SelectedIndices.Count == 1;
				UpdateProjectDetails();
			};
			resultList.DoubleClick += delegate { BeginInstallSelected(); };
			contentSplit.Panel1.Controls.Add(resultList);

			TableLayoutPanel details = new TableLayoutPanel();
			details.Dock = DockStyle.Fill;
			details.Padding = new Padding(20, 8, 8, 8);
			details.ColumnCount = 1;
			details.RowCount = 4;
			details.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));
			details.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			details.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
			details.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			contentSplit.Panel2.Controls.Add(details);
			projectIcon = new PictureBox();
			projectIcon.Size = new Size(104, 104);
			projectIcon.Dock = DockStyle.Left;
			projectIcon.SizeMode = PictureBoxSizeMode.Zoom;
			details.Controls.Add(projectIcon, 0, 0);
			projectTitleLabel = new Label();
			projectTitleLabel.Dock = DockStyle.Fill;
			projectTitleLabel.Font = new Font("Pretendard", 14F, FontStyle.Bold);
			projectTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
			projectTitleLabel.AutoEllipsis = true;
			details.Controls.Add(projectTitleLabel, 0, 1);
			projectMetaLabel = new Label();
			projectMetaLabel.Dock = DockStyle.Fill;
			projectMetaLabel.TextAlign = ContentAlignment.MiddleLeft;
			projectMetaLabel.AutoEllipsis = true;
			details.Controls.Add(projectMetaLabel, 0, 2);
			projectDescriptionLabel = new Label();
			projectDescriptionLabel.Dock = DockStyle.Fill;
			projectDescriptionLabel.Padding = new Padding(0, 8, 4, 0);
			projectDescriptionLabel.TextAlign = ContentAlignment.TopLeft;
			details.Controls.Add(projectDescriptionLabel, 0, 3);
			ClearProjectDetails();

			statusLabel = new Label();
			statusLabel.Dock = DockStyle.Fill;
			statusLabel.TextAlign = ContentAlignment.MiddleLeft;
			statusLabel.Text = BuildContentHint(korean);
			root.Controls.Add(statusLabel, 0, 3);

			Panel actions = new Panel();
			actions.Dock = DockStyle.Fill;
			root.Controls.Add(actions, 0, 4);
			openFolderButton = NewContentButton(korean ? "설치 폴더 열기" : "Open content folder", 148);
			ApplyButtonIcon(openFolderButton, ButtonIcon.Folder);
			openFolderButton.Location = new Point(0, 5);
			openFolderButton.Click += delegate { OpenContentFolder(); };
			actions.Controls.Add(openFolderButton);
			installButton = NewContentButton(korean ? "선택 항목 설치" : "Install selected", 148);
			ApplyButtonIcon(installButton, ButtonIcon.Download);
			installButton.Tag = "primary";
			installButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			installButton.Enabled = false;
			installButton.Location = new Point(actions.Width - 148, 5);
			installButton.Click += delegate { BeginInstallSelected(); };
			actions.Controls.Add(installButton);
			actions.Resize += delegate { installButton.Left = actions.ClientSize.Width - installButton.Width; };

			Shown += delegate
			{
				BeginPopularLoad();
				searchBox.Focus();
			};
			FormClosed += delegate
			{
				Interlocked.Increment(ref iconLoadRequest);
				if (currentProjectImage != null)
				{
					currentProjectImage.Dispose();
					currentProjectImage = null;
				}
			};
			ApplySimpleDialogTheme(this);
			AcceptButton = searchButton;
			ConfigureAccessibleField(searchBox, korean ? "콘텐츠 검색" : "Content search", korean ? "플러그인 또는 모드 이름을 입력하세요." : "Enter a plugin or mod name.");
			ConfigureAccessibleField(resultList, korean ? "검색 결과" : "Search results", korean ? "항목을 선택하면 오른쪽에서 설명을 확인할 수 있습니다." : "Select an item to review its details on the right.");
			projectIcon.AccessibleName = korean ? "선택한 콘텐츠 아이콘" : "Selected content icon";
			ApplyCommonButtonToolTips(this);
		}

		private static Button NewContentButton(string text, int width)
		{
			Button button = new RoundedButton();
			button.Text = text;
			button.Width = width;
			button.Height = 40;
			button.Tag = "secondary";
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			return button;
		}

		private string BuildContentHint(bool korean)
		{
			string folder = GetContentFolderName(options.ServerType);
			if (folder == null)
			{
				return korean ? "이 서버 종류는 자동 콘텐츠 설치를 지원하지 않습니다." : "Automatic content installation is not available for this server type.";
			}
			return korean
				? options.MinecraftVersion + " · " + GetServerTypeDisplayName(options.ServerType) + " 호환 항목만 표시합니다."
				: options.MinecraftVersion + " · only compatible " + GetServerTypeDisplayName(options.ServerType) + " content is shown.";
		}

		private void BeginSearch()
		{
			string query = searchBox.Text.Trim();
			if (query.Length < 2)
			{
				ShowContentMessage(this, "두 글자 이상 검색해 주세요.", "Enter at least two characters.", true);
				return;
			}
			BeginProjectLoad(query, false);
		}

		private void BeginPopularLoad()
		{
			BeginProjectLoad(string.Empty, true);
		}

		private void BeginProjectLoad(string query, bool popular)
		{
			if (busy)
			{
				return;
			}
			if (GetContentFolderName(options.ServerType) == null)
			{
				ShowContentMessage(this, "이 서버 종류는 Modrinth 자동 설치를 지원하지 않습니다.", "Automatic Modrinth installation is not available for this server type.", true);
				return;
			}
			SetBusy(true, popular ? (IsKoreanContent() ? "인기 항목을 불러오는 중..." : "Loading popular content...") : (IsKoreanContent() ? "검색 중..." : "Searching..."));
			Thread thread = new Thread((ThreadStart)delegate
			{
				try
				{
					List<ModrinthProjectInfo> found = SearchModrinthProjects(query, options);
					TryPostToUi(this, (MethodInvoker)delegate
					{
						projects.Clear();
						projects.AddRange(found);
						RenderProjects();
						SetBusy(false, popular ? (IsKoreanContent() ? "인기 항목 " + found.Count + "개" : found.Count + " popular item(s)") : (IsKoreanContent() ? found.Count + "개를 찾았습니다." : found.Count + " result(s) found."));
					});
				}
				catch (Exception exception)
				{
					TryPostToUi(this, (MethodInvoker)delegate { SetBusy(false, (IsKoreanContent() ? "검색 실패: " : "Search failed: ") + exception.Message); });
				}
			});
			thread.IsBackground = true;
			thread.Name = popular ? "Modrinth 인기 콘텐츠" : "Modrinth 콘텐츠 검색";
			thread.Start();
		}

		private void RenderProjects()
		{
			resultList.BeginUpdate();
			try
			{
				resultList.Items.Clear();
				for (int i = 0; i < projects.Count; i++)
				{
					ModrinthProjectInfo project = projects[i];
					ListViewItem item = new ListViewItem(project.Title);
					item.SubItems.Add(project.Author);
					item.SubItems.Add(project.Downloads.ToString("N0", CultureInfo.CurrentCulture));
					item.Tag = i;
					resultList.Items.Add(item);
				}
			}
			finally
			{
				resultList.EndUpdate();
			}
		}

		private void ClearProjectDetails()
		{
			projectTitleLabel.Text = IsKoreanContent() ? "항목을 선택하세요" : "Select an item";
			projectMetaLabel.Text = string.Empty;
			projectDescriptionLabel.Text = IsKoreanContent() ? "선택하면 아이콘과 설명이 여기에 표시됩니다." : "Select an item to see its icon and description.";
			ReplaceProjectImage(null);
		}

		private void UpdateProjectDetails()
		{
			if (resultList.SelectedIndices.Count != 1)
			{
				Interlocked.Increment(ref iconLoadRequest);
				ClearProjectDetails();
				return;
			}
			int index = resultList.SelectedIndices[0];
			if (index < 0 || index >= projects.Count)
			{
				ClearProjectDetails();
				return;
			}
			ModrinthProjectInfo project = projects[index];
			projectTitleLabel.Text = project.Title;
			projectMetaLabel.Text = project.Author + " · " + project.Downloads.ToString("N0", CultureInfo.CurrentCulture) + (IsKoreanContent() ? "회 다운로드" : " downloads");
			projectDescriptionLabel.Text = project.Description;
			BeginProjectIconLoad(project.IconUrl);
		}

		private void BeginProjectIconLoad(string iconUrl)
		{
			int request = Interlocked.Increment(ref iconLoadRequest);
			ReplaceProjectImage(null);
			if (string.IsNullOrWhiteSpace(iconUrl))
			{
				return;
			}
			Thread thread = new Thread((ThreadStart)delegate
			{
				Image loaded = null;
				try
				{
					loaded = DownloadModrinthImage(iconUrl);
				}
				catch
				{
				}
				if (request != iconLoadRequest || IsDisposed)
				{
					if (loaded != null) loaded.Dispose();
					return;
				}
				Image completed = loaded;
				if (!TryPostToUi(this, (MethodInvoker)delegate
				{
					if (request == iconLoadRequest && !IsDisposed)
					{
						ReplaceProjectImage(completed);
						completed = null;
					}
				}))
				{
					if (completed != null) completed.Dispose();
				}
			});
			thread.IsBackground = true;
			thread.Name = "Modrinth 콘텐츠 아이콘";
			thread.Start();
		}

		private void ReplaceProjectImage(Image image)
		{
			Image previous = currentProjectImage;
			currentProjectImage = image;
			projectIcon.Image = image;
			if (previous != null && !object.ReferenceEquals(previous, image))
			{
				previous.Dispose();
			}
		}

		private void BeginInstallSelected()
		{
			if (busy || resultList.SelectedIndices.Count != 1)
			{
				return;
			}
			int index = resultList.SelectedIndices[0];
			if (index < 0 || index >= projects.Count)
			{
				return;
			}
			ModrinthProjectInfo project = projects[index];
			bool korean = IsKoreanContent();
			string warning = korean
				? "플러그인과 모드는 서버 PC의 파일에 접근할 수 있습니다. 제작자와 출처를 신뢰할 수 있는지 확인한 뒤 설치하세요.\r\n\r\n'" + project.Title + "'을(를) 설치할까요?"
				: "Plugins and mods can access files on the server PC. Verify that you trust the author and source before installing.\r\n\r\nInstall '" + project.Title + "'?";
			if (ShowMineHarborDialog(this, warning, korean ? "콘텐츠 설치 확인" : "Confirm installation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			{
				return;
			}
			SetBusy(true, korean ? "호환 버전과 필수 의존성을 확인하는 중..." : "Checking compatible version and required dependencies...");
			Thread thread = new Thread((ThreadStart)delegate
			{
				try
				{
					string installedPath = InstallModrinthProject(project.Id, options, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
					TryPostToUi(this, (MethodInvoker)delegate
					{
						SetBusy(false, (IsKoreanContent() ? "설치 완료 · 서버를 다시 시작하세요: " : "Installed · restart the server: ") + Path.GetFileName(installedPath));
					});
				}
				catch (Exception exception)
				{
					TryPostToUi(this, (MethodInvoker)delegate
					{
						SetBusy(false, (IsKoreanContent() ? "설치하지 못했습니다. 다시 시도해 주세요: " : "Installation failed. Try again: ") + exception.Message);
					});
				}
			});
			thread.IsBackground = true;
			thread.Name = "Modrinth 콘텐츠 설치";
			thread.Start();
		}

		private void SetBusy(bool value, string status)
		{
			busy = value;
			searchButton.Enabled = !value;
			searchBox.Enabled = !value;
			openFolderButton.Enabled = !value;
			installButton.Enabled = !value && resultList.SelectedIndices.Count == 1;
			statusLabel.Text = status;
			statusLabel.AccessibleName = status;
			UseWaitCursor = value;
		}

		private bool IsKoreanContent()
		{
			return string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
		}

		private void OpenContentFolder()
		{
			string folderName = GetContentFolderName(options.ServerType);
			if (folderName == null)
			{
				folderName = string.Empty;
			}
			string path = Path.Combine(options.ServerDirectory, folderName);
			Directory.CreateDirectory(path);
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
		}
	}

	private static string GetContentFolderName(string serverType)
	{
		string normalized = NormalizeServerType(serverType);
		if (normalized == "paper" || normalized == "purpur")
		{
			return "plugins";
		}
		if (normalized == "fabric" || normalized == "forge" || normalized == "neoforge")
		{
			return "mods";
		}
		return null;
	}

	private static string GetModrinthLoader(string serverType)
	{
		string normalized = NormalizeServerType(serverType);
		if (normalized == "purpur")
		{
			return "paper";
		}
		return normalized;
	}

	private static List<ModrinthProjectInfo> SearchModrinthProjects(string query, LauncherOptions options)
	{
		string loader = GetModrinthLoader(options.ServerType);
		List<string> loaderFacets = new List<string>();
		loaderFacets.Add("categories:" + loader);
		if (loader == "paper")
		{
			loaderFacets.Add("categories:bukkit");
			loaderFacets.Add("categories:spigot");
			loaderFacets.Add("categories:purpur");
		}
		object[] facets = new object[]
		{
			new string[] { "versions:" + options.MinecraftVersion },
			loaderFacets.ToArray()
		};
		string facetJson = new JavaScriptSerializer().Serialize(facets);
		string url = "https://api.modrinth.com/v2/search?limit=50&index=downloads&facets=" + Uri.EscapeDataString(facetJson);
		if (!string.IsNullOrWhiteSpace(query))
		{
			url += "&query=" + Uri.EscapeDataString(query);
		}
		string json = DownloadModrinthText(url);
		Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
		object[] hits = root != null && root.ContainsKey("hits") ? root["hits"] as object[] : null;
		List<ModrinthProjectInfo> result = new List<ModrinthProjectInfo>();
		if (hits == null)
		{
			return result;
		}
		for (int i = 0; i < hits.Length; i++)
		{
			Dictionary<string, object> hit = hits[i] as Dictionary<string, object>;
			if (hit == null)
			{
				continue;
			}
			string serverSide = GetJsonString(hit, "server_side");
			string[] versions = GetJsonStringArray(hit, "versions");
			string[] categories = GetJsonStringArray(hit, "categories");
			string projectType = GetJsonString(hit, "project_type");
			if (string.Equals(serverSide, "unsupported", StringComparison.OrdinalIgnoreCase) || !ContainsIgnoreCase(versions, options.MinecraftVersion))
			{
				continue;
			}
			bool loaderMatches = ContainsIgnoreCase(categories, loader);
			if ((loader == "paper") && (ContainsIgnoreCase(categories, "bukkit") || ContainsIgnoreCase(categories, "spigot") || ContainsIgnoreCase(categories, "purpur")))
			{
				loaderMatches = true;
			}
			if (!loaderMatches)
			{
				continue;
			}
			ModrinthProjectInfo project = new ModrinthProjectInfo();
			project.Id = GetJsonString(hit, "project_id");
			project.Slug = GetJsonString(hit, "slug");
			project.Title = GetJsonString(hit, "title");
			project.Description = GetJsonString(hit, "description");
			project.Author = GetJsonString(hit, "author");
			project.ProjectType = projectType;
			project.ServerSide = serverSide;
			project.IconUrl = GetJsonString(hit, "icon_url");
			project.Downloads = GetJsonLong(hit, "downloads");
			project.Categories = categories;
			project.Versions = versions;
			if (!string.IsNullOrEmpty(project.Id))
			{
				result.Add(project);
			}
		}
		return result;
	}

	private static string InstallModrinthProject(string projectId, LauncherOptions options, HashSet<string> visited, int depth)
	{
		if (depth > 12)
		{
			throw new InvalidDataException("콘텐츠 의존성 단계가 지나치게 깊습니다.");
		}
		if (string.IsNullOrEmpty(projectId) || !visited.Add(projectId))
		{
			return string.Empty;
		}
		ModrinthFileInfo file = GetCompatibleModrinthFile(projectId, options);
		for (int i = 0; i < file.Dependencies.Count; i++)
		{
			Dictionary<string, object> dependency = file.Dependencies[i];
			if (!string.Equals(GetJsonString(dependency, "dependency_type"), "required", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string dependencyProject = GetJsonString(dependency, "project_id");
			if (string.IsNullOrEmpty(dependencyProject))
			{
				string dependencyVersion = GetJsonString(dependency, "version_id");
				if (!string.IsNullOrEmpty(dependencyVersion))
				{
					dependencyProject = GetProjectIdForModrinthVersion(dependencyVersion);
				}
			}
			if (!string.IsNullOrEmpty(dependencyProject))
			{
				InstallModrinthProject(dependencyProject, options, visited, depth + 1);
			}
		}
		return DownloadAndInstallModrinthFile(file, options);
	}

	private static ModrinthFileInfo GetCompatibleModrinthFile(string projectId, LauncherOptions options)
	{
		string loaderJson = new JavaScriptSerializer().Serialize(new string[1] { GetModrinthLoader(options.ServerType) });
		string gameJson = new JavaScriptSerializer().Serialize(new string[1] { options.MinecraftVersion });
		string url = "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(projectId) + "/version?loaders=" + Uri.EscapeDataString(loaderJson) + "&game_versions=" + Uri.EscapeDataString(gameJson);
		object[] versions = new JavaScriptSerializer().DeserializeObject(DownloadModrinthText(url)) as object[];
		if (versions == null || versions.Length == 0)
		{
			throw new InvalidDataException("선택한 서버 버전과 로더에 맞는 콘텐츠 파일을 찾지 못했습니다.");
		}
		Dictionary<string, object> selected = null;
		for (int pass = 0; pass < 2 && selected == null; pass++)
		{
			for (int i = 0; i < versions.Length; i++)
			{
				Dictionary<string, object> candidate = versions[i] as Dictionary<string, object>;
				if (candidate == null)
				{
					continue;
				}
				string type = GetJsonString(candidate, "version_type");
				if (pass == 0 && !string.Equals(type, "release", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				selected = candidate;
				break;
			}
		}
		if (selected == null)
		{
			throw new InvalidDataException("설치 가능한 콘텐츠 릴리스를 찾지 못했습니다.");
		}
		object[] files = selected.ContainsKey("files") ? selected["files"] as object[] : null;
		Dictionary<string, object> selectedFile = null;
		if (files != null)
		{
			for (int i = 0; i < files.Length; i++)
			{
				Dictionary<string, object> candidateFile = files[i] as Dictionary<string, object>;
				if (candidateFile == null)
				{
					continue;
				}
				if (selectedFile == null || (candidateFile.ContainsKey("primary") && Convert.ToBoolean(candidateFile["primary"], CultureInfo.InvariantCulture)))
				{
					selectedFile = candidateFile;
				}
				if (candidateFile.ContainsKey("primary") && Convert.ToBoolean(candidateFile["primary"], CultureInfo.InvariantCulture))
				{
					break;
				}
			}
		}
		if (selectedFile == null)
		{
			throw new InvalidDataException("콘텐츠 다운로드 파일을 찾지 못했습니다.");
		}
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
		if (dependencies != null)
		{
			for (int i = 0; i < dependencies.Length; i++)
			{
				Dictionary<string, object> dependency = dependencies[i] as Dictionary<string, object>;
				if (dependency != null)
				{
					info.Dependencies.Add(dependency);
				}
			}
		}
		ValidateModrinthFileInfo(info);
		return info;
	}

	private static string GetProjectIdForModrinthVersion(string versionId)
	{
		string json = DownloadModrinthText("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId));
		Dictionary<string, object> version = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
		return GetJsonString(version, "project_id");
	}

	private static void ValidateModrinthFileInfo(ModrinthFileInfo info)
	{
		Uri uri;
		if (string.IsNullOrWhiteSpace(info.FileName) || !info.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(info.FileName) != info.FileName)
		{
			throw new InvalidDataException("Modrinth가 안전한 JAR 파일명을 제공하지 않았습니다.");
		}
		if (!Uri.TryCreate(info.Url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Modrinth CDN 다운로드 주소를 검증하지 못했습니다.");
		}
		if (info.Size <= 0 || info.Size > 536870912L)
		{
			throw new InvalidDataException("콘텐츠 파일 크기가 허용 범위를 벗어났습니다.");
		}
		if ((string.IsNullOrEmpty(info.Sha512) || info.Sha512.Length != 128) && (string.IsNullOrEmpty(info.Sha1) || info.Sha1.Length != 40))
		{
			throw new InvalidDataException("콘텐츠 파일의 무결성 해시를 확인하지 못했습니다.");
		}
	}

	private static string DownloadAndInstallModrinthFile(ModrinthFileInfo file, LauncherOptions options)
	{
		string folderName = GetContentFolderName(options.ServerType);
		if (folderName == null)
		{
			throw new InvalidOperationException("이 서버 종류는 콘텐츠 자동 설치를 지원하지 않습니다.");
		}
		string folder = Path.Combine(options.ServerDirectory, folderName);
		Directory.CreateDirectory(folder);
		string destination = Path.Combine(folder, file.FileName);
		string temporary = destination + ".다운로드중-" + Guid.NewGuid().ToString("N");
		try
		{
			DownloadModrinthBinary(file.Url, temporary, file.Size);
			if (!VerifyModrinthHash(temporary, file))
			{
				throw new InvalidDataException("다운로드한 콘텐츠의 무결성 검증에 실패했습니다.");
			}
			if (File.Exists(destination))
			{
				string backupDirectory = Path.Combine(options.ServerDirectory, "content-backups");
				Directory.CreateDirectory(backupDirectory);
				string backupName = Path.GetFileNameWithoutExtension(file.FileName) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".jar";
				File.Copy(destination, Path.Combine(backupDirectory, backupName), false);
			}
			ReplaceFile(temporary, destination);
			WriteModrinthMetadata(options.ServerDirectory, file);
			return destination;
		}
		finally
		{
			DeleteFileIfPresent(temporary);
		}
	}

	private static string DownloadModrinthText(string url)
	{
		Uri uri;
		if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Modrinth API 주소가 안전하지 않습니다.");
		}
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
		request.Method = "GET";
		request.UserAgent = GetLauncherIntegrationUserAgent();
		request.Accept = "application/json";
		request.Timeout = 20000;
		request.ReadWriteTimeout = 20000;
		request.AllowAutoRedirect = false;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
		{
			if (response.StatusCode != HttpStatusCode.OK || response.ResponseUri == null || response.ResponseUri.Scheme != Uri.UriSchemeHttps || !response.ResponseUri.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase) || response.ContentLength > 8388608L)
			{
				throw new WebException("Modrinth API가 정상 응답하지 않았습니다.");
			}
			using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
			{
				return ReadLimitedText(reader, 8388608);
			}
		}
	}

	private static void DownloadModrinthBinary(string url, string path, long expectedSize)
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.Method = "GET";
		request.UserAgent = GetLauncherIntegrationUserAgent();
		request.Accept = "application/java-archive,application/octet-stream";
		request.Timeout = 120000;
		request.ReadWriteTimeout = 120000;
		request.AllowAutoRedirect = false;
		using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
		{
			if (response.StatusCode != HttpStatusCode.OK || response.ResponseUri == null || response.ResponseUri.Scheme != Uri.UriSchemeHttps || !response.ResponseUri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) || response.ContentLength > expectedSize)
			{
				throw new WebException("Modrinth CDN이 정상 응답하지 않았습니다.");
			}
			using (Stream input = response.GetResponseStream())
			using (FileStream output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				byte[] buffer = new byte[131072];
				long total = 0;
				int read;
				while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					output.Write(buffer, 0, read);
					total = checked(total + read);
					if (total > expectedSize)
					{
						throw new InvalidDataException("콘텐츠 다운로드 크기가 공식 정보보다 큽니다.");
					}
				}
				output.Flush(true);
				if (total != expectedSize)
				{
					throw new InvalidDataException("콘텐츠 다운로드 크기가 공식 정보와 다릅니다.");
				}
			}
		}
	}

	private static Image DownloadModrinthImage(string url)
	{
		if (url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
		{
			url = "https://wsrv.nl/?url=" + Uri.EscapeDataString(url) + "&w=256&h=256&fit=inside&output=png";
		}
		Uri uri;
		if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidDataException("Modrinth 이미지 주소를 검증하지 못했습니다.");
		}
		if (!uri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("wsrv.nl", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Modrinth 이미지 주소(호스트)를 검증하지 못했습니다.");
		}
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
		request.Method = "GET";
		request.UserAgent = GetLauncherIntegrationUserAgent();
		request.Accept = "image/png,image/jpeg,image/webp,image/*";
		request.Timeout = 15000;
		request.ReadWriteTimeout = 15000;
		request.MaximumAutomaticRedirections = 3;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
		{
			if (response.StatusCode != HttpStatusCode.OK || response.ResponseUri == null || response.ResponseUri.Scheme != Uri.UriSchemeHttps || response.ContentLength > 8388608L)
			{
				throw new WebException("Modrinth 이미지 응답을 검증하지 못했습니다.");
			}
			if (!response.ResponseUri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) && !response.ResponseUri.Host.Equals("wsrv.nl", StringComparison.OrdinalIgnoreCase))
			{
				throw new WebException("Modrinth 이미지 응답 주소를 검증하지 못했습니다.");
			}
			using (Stream input = response.GetResponseStream())
			using (MemoryStream buffer = new MemoryStream())
			{
				byte[] block = new byte[32768];
				int total = 0;
				int read;
				while ((read = input.Read(block, 0, block.Length)) > 0)
				{
					total = checked(total + read);
					if (total > 8388608)
					{
						throw new InvalidDataException("Modrinth 아이콘 크기가 허용 범위를 초과했습니다.");
					}
					buffer.Write(block, 0, read);
				}
				buffer.Position = 0;
				using (Image decoded = Image.FromStream(buffer, true, true))
				{
					long pixels = checked((long)decoded.Width * (long)decoded.Height);
					if (decoded.Width < 1 || decoded.Height < 1 || decoded.Width > 4096 || decoded.Height > 4096 || pixels > 16777216L) throw new InvalidDataException("Modrinth 아이콘 해상도가 허용 범위를 초과했습니다.");
					return new Bitmap(decoded);
				}
			}
		}
	}

	private static bool VerifyModrinthHash(string path, ModrinthFileInfo file)
	{
		if (!string.IsNullOrEmpty(file.Sha512) && file.Sha512.Length == 128)
		{
			using (SHA512 sha512 = SHA512.Create())
			using (FileStream stream = File.OpenRead(path))
			{
				return string.Equals(ToLowerHex(sha512.ComputeHash(stream)), file.Sha512, StringComparison.OrdinalIgnoreCase);
			}
		}
		using (SHA1 sha1 = SHA1.Create())
		using (FileStream stream = File.OpenRead(path))
		{
			return string.Equals(ToLowerHex(sha1.ComputeHash(stream)), file.Sha1, StringComparison.OrdinalIgnoreCase);
		}
	}

	private static void WriteModrinthMetadata(string serverDirectory, ModrinthFileInfo file)
	{
		string directory = Path.Combine(serverDirectory, ".launcher-content-metadata");
		Directory.CreateDirectory(directory);
		string safeId = Regex.Replace(file.ProjectId ?? string.Empty, "[^A-Za-z0-9_-]", string.Empty);
		if (safeId.Length == 0)
		{
			return;
		}
		string contents = "source=modrinth\r\nproject-id=" + safeId + "\r\nversion-id=" + (file.VersionId ?? string.Empty) + "\r\nversion=" + (file.VersionName ?? string.Empty) + "\r\nfile=" + file.FileName + "\r\nsha512=" + (file.Sha512 ?? string.Empty) + "\r\nsha1=" + (file.Sha1 ?? string.Empty) + "\r\n";
		File.WriteAllText(Path.Combine(directory, safeId + ".properties"), contents, new UTF8Encoding(false));
	}

	private static string GetLauncherIntegrationUserAgent()
	{
		return "Mangom72-MineHarbor/" + BuildVersionInfo.ProductVersion + " (+https://github.com/" + GetLauncherReleaseRepositoryPath() + ")";
	}

	private static string GetJsonString(Dictionary<string, object> dictionary, string key)
	{
		return dictionary != null && dictionary.ContainsKey(key) && dictionary[key] != null ? Convert.ToString(dictionary[key], CultureInfo.InvariantCulture) : string.Empty;
	}

	private static long GetJsonLong(Dictionary<string, object> dictionary, string key)
	{
		if (dictionary == null || !dictionary.ContainsKey(key) || dictionary[key] == null)
		{
			return 0L;
		}
		try
		{
			return Convert.ToInt64(dictionary[key], CultureInfo.InvariantCulture);
		}
		catch
		{
			return 0L;
		}
	}

	private static string[] GetJsonStringArray(Dictionary<string, object> dictionary, string key)
	{
		object[] values = dictionary != null && dictionary.ContainsKey(key) ? dictionary[key] as object[] : null;
		if (values == null)
		{
			return new string[0];
		}
		string[] result = new string[values.Length];
		for (int i = 0; i < values.Length; i++)
		{
			result[i] = Convert.ToString(values[i], CultureInfo.InvariantCulture);
		}
		return result;
	}

	private static bool ContainsIgnoreCase(string[] values, string expected)
	{
		if (values == null || string.IsNullOrEmpty(expected))
		{
			return false;
		}
		for (int i = 0; i < values.Length; i++)
		{
			if (string.Equals(values[i], expected, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static string ToLowerHex(byte[] bytes)
	{
		StringBuilder builder = new StringBuilder(bytes.Length * 2);
		for (int i = 0; i < bytes.Length; i++)
		{
			builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
		}
		return builder.ToString();
	}

	private static void ShowContentMessage(IWin32Window owner, string korean, string english, bool warning)
	{
		bool isKorean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
		ShowMineHarborDialog(owner, isKorean ? korean : english, isKorean ? "서버 콘텐츠" : "Server content", MessageBoxButtons.OK, warning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
	}

	private static void ApplySimpleDialogTheme(Control root)
	{
		bool dark = launcherForm != null && launcherForm.UsesDarkTheme;
		ThemePalette palette = ThemePalette.Create(dark);
		root.BackColor = palette.Window;
		root.ForeColor = palette.Text;
		ApplySimpleDialogThemeRecursive(root, palette);
	}

	private static void ApplySimpleDialogThemeRecursive(Control parent, ThemePalette palette)
	{
		foreach (Control control in parent.Controls)
		{
			ApplyModernControlPalette(control, palette);
			if (control is Button)
			{
				Button button = control as Button;
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
					button.BackColor = palette.CardSecondary;
					button.ForeColor = palette.Text;
					button.FlatAppearance.MouseOverBackColor = palette.AccentSoft;
				}
			}
			else if (control is TextBox || control is ListView || control is DataGridView || control is NumericUpDown || control is ComboBox || control is ModernMetricTable)
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
			else
			{
				control.BackColor = parent.BackColor;
				control.ForeColor = palette.Text;
			}
			ApplySimpleDialogThemeRecursive(control, palette);
		}
	}

	private static string CreateDiagnosticBundle(string serverDirectory, LauncherOptions options)
	{
		string diagnosticsDirectory = Path.Combine(serverDirectory, "diagnostics");
		Directory.CreateDirectory(diagnosticsDirectory);
		string path = Path.Combine(diagnosticsDirectory, "server-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
		string temporary = path + ".준비중";
		try
		{
			using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
			using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				AddDiagnosticText(archive, "system-summary.txt", BuildSystemSummary(options));
				AddDiagnosticFileIfPresent(archive, serverDirectory, Path.Combine(serverDirectory, "server.properties"), "server.properties");
				AddDiagnosticFileIfPresent(archive, serverDirectory, Path.Combine(serverDirectory, ".launcher-properties-configured"), "launcher-profile.properties");
				AddDiagnosticFileIfPresent(archive, serverDirectory, Path.Combine(serverDirectory, ".launcher-server-runtime"), "launcher-runtime.properties");
				AddDiagnosticFileIfPresent(archive, serverDirectory, Path.Combine(serverDirectory, "logs", "latest.log"), "logs/latest.log");
				string crashDirectory = Path.Combine(serverDirectory, "crash-reports");
				if (Directory.Exists(crashDirectory))
				{
					FileInfo[] reports = new DirectoryInfo(crashDirectory).GetFiles("*.txt");
					Array.Sort(reports, delegate(FileInfo left, FileInfo right) { return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc); });
					for (int i = 0; i < reports.Length && i < 3; i++)
					{
						AddDiagnosticFileIfPresent(archive, serverDirectory, reports[i].FullName, "crash-reports/" + reports[i].Name);
					}
				}
			}
			File.Move(temporary, path);
			return path;
		}
		finally
		{
			DeleteFileIfPresent(temporary);
		}
	}

	private static string BuildSystemSummary(LauncherOptions options)
	{
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("MineHarbor — Minecraft Server Launcher diagnostic summary");
		builder.AppendLine("created-utc=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
		builder.AppendLine("launcher-product-version=" + BuildVersionInfo.ProductVersion);
		builder.AppendLine("launcher-build-number=" + BuildVersionInfo.BuildNumber);
		builder.AppendLine("launcher-install-mode=" + (IsInstalledLauncherPath(Assembly.GetExecutingAssembly().Location) ? "installed" : "portable"));
		builder.AppendLine("os=" + Environment.OSVersion.VersionString);
		builder.AppendLine("is-64bit-os=" + Environment.Is64BitOperatingSystem.ToString().ToLowerInvariant());
		builder.AppendLine("is-64bit-process=" + Environment.Is64BitProcess.ToString().ToLowerInvariant());
		builder.AppendLine("logical-processors=" + Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
		builder.AppendLine("physical-memory-gb=" + GetTotalPhysicalMemoryGb().ToString(CultureInfo.InvariantCulture));
		builder.AppendLine("server-type=" + NormalizeServerType(options.ServerType));
		builder.AppendLine("minecraft-version=" + (options.MinecraftVersion ?? string.Empty));
		builder.AppendLine("memory-gb=" + options.MemoryGb.ToString(CultureInfo.InvariantCulture));
		return builder.ToString();
	}

	private static void AddDiagnosticFileIfPresent(ZipArchive archive, string serverDirectory, string sourcePath, string entryName)
	{
		if (!File.Exists(sourcePath))
		{
			return;
		}
		FileInfo info = new FileInfo(sourcePath);
		if (info.Length > 16777216L || (info.Attributes & FileAttributes.ReparsePoint) != 0)
		{
			return;
		}
		string text = File.ReadAllText(sourcePath, Encoding.UTF8);
		AddDiagnosticText(archive, entryName, RedactDiagnosticText(text, serverDirectory));
	}

	private static void AddDiagnosticText(ZipArchive archive, string entryName, string text)
	{
		ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
		using (Stream stream = entry.Open())
		using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
		{
			writer.Write(text ?? string.Empty);
		}
	}

	private static string RedactDiagnosticText(string text, string serverDirectory)
	{
		string result = text ?? string.Empty;
		string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (!string.IsNullOrEmpty(userProfile))
		{
			result = result.Replace(userProfile, "%USERPROFILE%");
		}
		if (!string.IsNullOrEmpty(serverDirectory))
		{
			result = result.Replace(serverDirectory, "%SERVER_DIRECTORY%");
		}
		result = Regex.Replace(result, "(?im)^(owner-name|rcon\\.password|resource-pack-prompt|server-ip)\\s*=.*$", "$1=<redacted>");
		result = Regex.Replace(result, "(?<![0-9])(?:[0-9]{1,3}\\.){3}[0-9]{1,3}(?![0-9])", "<ip-redacted>");
		return result;
	}
	internal enum ServerFailureAction
	{
		None,
		OpenSettings,
		OpenContentManager,
		OpenJavaDownload
	}

	private static string AnalyzeServerFailure(IEnumerable<string> lines, out ServerFailureAction action)
	{
		action = ServerFailureAction.None;
		StringBuilder builder = new StringBuilder();
		foreach (string line in lines)
		{
			builder.AppendLine(line ?? string.Empty);
		}
		string log = builder.ToString();
		bool korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
		if (log.IndexOf("UnsupportedClassVersionError", StringComparison.OrdinalIgnoreCase) >= 0 || log.IndexOf("only recognizes class file versions", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			action = ServerFailureAction.OpenSettings;
			return korean ? "서버 버전과 Java 버전이 맞지 않습니다. 호환 Java를 다시 준비해 주세요." : "The server and Java versions do not match. Reinstall the compatible Java runtime.";
		}
		if (log.IndexOf("not a recognized option", StringComparison.OrdinalIgnoreCase) >= 0 || log.IndexOf("Unrecognized option", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return korean ? "이 구버전 서버가 지원하지 않는 실행 옵션이 사용됐습니다. 서버 종류별 호환 실행 인수를 확인해 주세요." : "This legacy server does not support one of the launch arguments. Check the server-type-specific arguments.";
		}
		if (log.IndexOf("agent library failed to init", StringComparison.OrdinalIgnoreCase) >= 0 || log.IndexOf("Java 에이전트를 로드하는 중 오류", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return korean ? "구형 Paperclip이 서버 JAR 경로를 처리하지 못했습니다. 최신 런처로 다시 실행해 주세요." : "Legacy Paperclip could not process the server JAR path. Run it again with the latest launcher.";
		}
		if (log.IndexOf("Address already in use", StringComparison.OrdinalIgnoreCase) >= 0 || log.IndexOf("bind failed", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return korean ? "선택한 포트를 다른 프로그램이나 서버가 사용 중입니다." : "The selected port is already used by another program or server.";
		}
		if (log.IndexOf("OutOfMemoryError", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			action = ServerFailureAction.OpenSettings;
			return korean ? "서버 메모리가 부족합니다. 메모리 할당이나 시야·시뮬레이션 거리를 조정해 주세요." : "The server ran out of memory. Adjust memory or view/simulation distance.";
		}
		if (log.IndexOf("UnknownDependencyException", StringComparison.OrdinalIgnoreCase) >= 0 || log.IndexOf("missing dependency", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			action = ServerFailureAction.OpenContentManager;
			return korean ? "플러그인의 필수 의존성이 빠졌습니다. 콘솔에 표시된 의존 플러그인을 설치해 주세요." : "A required plugin dependency is missing. Install the dependency named in the console.";
		}
		if (log.IndexOf("Incompatible mods found", StringComparison.OrdinalIgnoreCase) >= 0 || log.IndexOf("Mod resolution encountered an incompatible mod set", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			action = ServerFailureAction.OpenContentManager;
			return korean ? "현재 Minecraft·로더 버전과 맞지 않는 모드가 있습니다." : "One or more mods are incompatible with the selected Minecraft or loader version.";
		}
		if (log.IndexOf("world was created by an incompatible version", StringComparison.OrdinalIgnoreCase) >= 0 || log.IndexOf("newer version", StringComparison.OrdinalIgnoreCase) >= 0 && log.IndexOf("world", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return korean ? "더 최신 버전에서 열린 월드는 다운그레이드할 수 없습니다. 백업을 복원하거나 원래 버전을 사용하세요." : "A world opened in a newer version cannot be downgraded. Restore a backup or use the original version.";
		}
		return korean ? "콘솔의 마지막 ERROR와 crash-reports를 확인해 주세요." : "Check the last ERROR in the console and the crash-reports folder.";
	}
}
