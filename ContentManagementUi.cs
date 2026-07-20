using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static partial class Launcher
{
	private sealed class UnifiedContentManagerForm : Form
	{
		private readonly LauncherOptions options;
		private TabControl tabs;
		private ListView installedList;
		private ListView searchList;
		private TextBox searchBox;
		private ComboBox kindBox;
		private ComboBox worldBox;
		private Label statusLabel;
		private RoundedProgressBar progressBar;
		private Button cancelButton;
		private Button updateButton;
		private Button updateAllButton;
		private Button toggleButton;
		private Button removeButton;
		private Button installButton;
		private Button searchButton;
		private Button localInstallButton;
		private readonly List<InstalledContentItem> installedItems = new List<InstalledContentItem>();
		private readonly List<ModrinthProjectInfo> searchItems = new List<ModrinthProjectInfo>();
		private CancellationTokenSource operationCancellation;
		private bool closing;

		public UnifiedContentManagerForm(LauncherOptions launcherOptions)
		{
			ApplyLauncherWindowIcon(this);
			options = launcherOptions;
			bool korean = IsContentUiKorean();
			Text = korean ? "콘텐츠 관리" : "Content management";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(940, 620);
			Size = new Size(1120, 760);
			Font = new Font("Pretendard", 10.5F);
			AutoScaleMode = AutoScaleMode.Dpi;
			KeyPreview = true;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(24);
			root.ColumnCount = 1;
			root.RowCount = 4;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
			Controls.Add(root);

			Label heading = new Label();
			heading.Text = korean ? "설치된 콘텐츠와 새 콘텐츠를 한곳에서 관리하세요" : "Manage installed and new content in one place";
			heading.Font = new Font("Pretendard", 17F, FontStyle.Bold);
			heading.Dock = DockStyle.Fill;
			heading.TextAlign = ContentAlignment.MiddleLeft;
			root.Controls.Add(heading, 0, 0);

			tabs = new TabControl();
			tabs.Dock = DockStyle.Fill;
			tabs.TabPages.Add(BuildInstalledPage(korean));
			tabs.TabPages.Add(BuildSearchPage(korean));
			root.Controls.Add(tabs, 0, 1);

			progressBar = new RoundedProgressBar();
			progressBar.Dock = DockStyle.Fill;
			progressBar.IsIndeterminate = false;
			progressBar.Value = 0;
			progressBar.AccessibleName = korean ? "콘텐츠 작업 진행률" : "Content operation progress";
			root.Controls.Add(progressBar, 0, 2);

			Panel statusPanel = new Panel();
			statusPanel.Dock = DockStyle.Fill;
			root.Controls.Add(statusPanel, 0, 3);
			statusLabel = new Label();
			statusLabel.Dock = DockStyle.Fill;
			statusLabel.Padding = new Padding(0, 4, 130, 0);
			statusLabel.TextAlign = ContentAlignment.MiddleLeft;
			statusLabel.AutoEllipsis = true;
			statusPanel.Controls.Add(statusLabel);
			cancelButton = NewContentActionButton(korean ? "취소" : "Cancel", 108, "danger");
			cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			cancelButton.Location = new Point(statusPanel.Width - cancelButton.Width, 2);
			cancelButton.Enabled = false;
			cancelButton.Click += delegate { if (operationCancellation != null) operationCancellation.Cancel(); };
			statusPanel.Controls.Add(cancelButton);
			statusPanel.Resize += delegate { cancelButton.Left = statusPanel.ClientSize.Width - cancelButton.Width; };

			Shown += async delegate { await ReloadInstalledAsync(); };
			FormClosing += delegate { closing = true; if (operationCancellation != null) operationCancellation.Cancel(); };
			FormClosed += delegate { if (operationCancellation != null) operationCancellation.Dispose(); };
			ApplySimpleDialogTheme(this);
			ApplyCommonButtonToolTips(this);
			UpdateWorldChoices();
			SetContentStatus(korean ? "설치된 콘텐츠를 읽는 중..." : "Reading installed content...");
		}

		private TabPage BuildInstalledPage(bool korean)
		{
			TabPage page = new TabPage(korean ? "설치됨" : "Installed");
			TableLayoutPanel layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.Padding = new Padding(10);
			layout.RowCount = 2;
			layout.ColumnCount = 1;
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
			page.Controls.Add(layout);
			installedList = new BufferedListView();
			installedList.Dock = DockStyle.Fill;
			installedList.View = View.Details;
			installedList.FullRowSelect = true;
			installedList.HideSelection = false;
			installedList.MultiSelect = true;
			installedList.Columns.Add(korean ? "이름" : "Name", 260);
			installedList.Columns.Add(korean ? "종류" : "Type", 90);
			installedList.Columns.Add(korean ? "관리" : "Managed by", 130);
			installedList.Columns.Add(korean ? "버전" : "Version", 120);
			installedList.Columns.Add(korean ? "상태" : "State", 110);
			installedList.Columns.Add(korean ? "월드" : "World", 140);
			installedList.SelectedIndexChanged += delegate { UpdateInstalledActions(); };
			ConfigureAccessibleField(installedList, korean ? "설치된 콘텐츠 목록" : "Installed content list", korean ? "플러그인, 모드와 데이터팩의 출처, 버전과 상태를 표시합니다." : "Shows source, version, and state for plugins, mods, and datapacks.");
			layout.Controls.Add(installedList, 0, 0);

			FlowLayoutPanel actions = NewContentActionPanel();
			layout.Controls.Add(actions, 0, 1);
			Button refresh = NewContentActionButton(korean ? "새로고침" : "Refresh", 104, "secondary");
			refresh.Click += async delegate { await ReloadInstalledAsync(); };
			actions.Controls.Add(refresh);
			Button check = NewContentActionButton(korean ? "업데이트 확인" : "Check updates", 132, "secondary");
			check.Click += async delegate { await CheckUpdatesAsync(); };
			actions.Controls.Add(check);
			updateButton = NewContentActionButton(korean ? "선택 업데이트" : "Update selected", 132, "primary");
			updateButton.Click += async delegate { await UpdateSelectedAsync(false); };
			actions.Controls.Add(updateButton);
			updateAllButton = NewContentActionButton(korean ? "모두 업데이트" : "Update all", 122, "primary");
			updateAllButton.Click += async delegate { await UpdateSelectedAsync(true); };
			actions.Controls.Add(updateAllButton);
			toggleButton = NewContentActionButton(korean ? "비활성화" : "Disable", 112, "secondary");
			toggleButton.Click += async delegate { await ToggleSelectedAsync(); };
			actions.Controls.Add(toggleButton);
			removeButton = NewContentActionButton(korean ? "제거" : "Remove", 96, "danger");
			removeButton.Click += async delegate { await RemoveSelectedAsync(); };
			actions.Controls.Add(removeButton);
			Button open = NewContentActionButton(korean ? "위치 열기" : "Open location", 112, "secondary");
			open.Click += delegate { OpenSelectedContentLocation(); };
			actions.Controls.Add(open);
			return page;
		}

		private TabPage BuildSearchPage(bool korean)
		{
			TabPage page = new TabPage(korean ? "검색·설치" : "Search & install");
			TableLayoutPanel layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.Padding = new Padding(10);
			layout.ColumnCount = 1;
			layout.RowCount = 3;
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
			page.Controls.Add(layout);

			TableLayoutPanel filters = new TableLayoutPanel();
			filters.Dock = DockStyle.Fill;
			filters.ColumnCount = 4;
			filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
			filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
			filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
			layout.Controls.Add(filters, 0, 0);
			searchBox = new TextBox();
			searchBox.Dock = DockStyle.Fill;
			searchBox.Margin = new Padding(0, 8, 8, 8);
			searchBox.KeyDown += delegate(object sender, KeyEventArgs eventArgs) { if (eventArgs.KeyCode == Keys.Enter) { ObserveContentTask(SearchAsync()); eventArgs.SuppressKeyPress = true; } };
			filters.Controls.Add(searchBox, 0, 0);
			kindBox = new ComboBox();
			kindBox.DropDownStyle = ComboBoxStyle.DropDownList;
			kindBox.Dock = DockStyle.Fill;
			kindBox.Margin = new Padding(0, 8, 8, 8);
			kindBox.Items.Add(korean ? "서버 콘텐츠" : "Server content");
			kindBox.Items.Add(korean ? "데이터팩" : "Datapack");
			kindBox.SelectedIndex = GetContentFolderName(options.ServerType) == null ? 1 : 0;
			kindBox.SelectedIndexChanged += delegate { UpdateWorldChoices(); };
			filters.Controls.Add(kindBox, 1, 0);
			worldBox = new ComboBox();
			worldBox.DropDownStyle = ComboBoxStyle.DropDownList;
			worldBox.Dock = DockStyle.Fill;
			worldBox.Margin = new Padding(0, 8, 8, 8);
			filters.Controls.Add(worldBox, 2, 0);
			searchButton = NewContentActionButton(korean ? "검색" : "Search", 96, "primary");
			searchButton.Dock = DockStyle.Fill;
			searchButton.Margin = new Padding(0, 5, 0, 5);
			searchButton.Click += async delegate { await SearchAsync(); };
			filters.Controls.Add(searchButton, 3, 0);
			ConfigureAccessibleField(searchBox, korean ? "Modrinth 검색어" : "Modrinth search query", korean ? "두 글자 이상의 콘텐츠 이름을 입력하세요." : "Enter a content name with at least two characters.");
			ConfigureAccessibleField(kindBox, korean ? "콘텐츠 종류" : "Content type", korean ? "서버 콘텐츠 또는 데이터팩을 선택합니다." : "Choose server content or a datapack.");
			ConfigureAccessibleField(worldBox, korean ? "데이터팩 월드" : "Datapack world", korean ? "데이터팩을 설치할 월드를 선택합니다." : "Choose the world where the datapack will be installed.");

			searchList = new BufferedListView();
			searchList.Dock = DockStyle.Fill;
			searchList.View = View.Details;
			searchList.FullRowSelect = true;
			searchList.HideSelection = false;
			searchList.MultiSelect = false;
			searchList.Columns.Add(korean ? "이름" : "Name", 260);
			searchList.Columns.Add(korean ? "제작자" : "Author", 160);
			searchList.Columns.Add(korean ? "다운로드" : "Downloads", 120);
			searchList.Columns.Add(korean ? "설명" : "Description", 430);
			searchList.SelectedIndexChanged += delegate { installButton.Enabled = operationCancellation == null && searchList.SelectedIndices.Count == 1; };
			ConfigureAccessibleField(searchList, korean ? "Modrinth 검색 결과" : "Modrinth search results", korean ? "Minecraft 버전과 로더가 호환되는 검색 결과입니다." : "Search results compatible with the selected Minecraft version and loader.");
			layout.Controls.Add(searchList, 0, 1);

			FlowLayoutPanel actions = NewContentActionPanel();
			layout.Controls.Add(actions, 0, 2);
			installButton = NewContentActionButton(korean ? "선택 설치" : "Install selected", 122, "primary");
			installButton.Enabled = false;
			installButton.Click += async delegate { await InstallSelectedAsync(); };
			actions.Controls.Add(installButton);
			localInstallButton = NewContentActionButton(korean ? "파일에서 설치" : "Install from file", 132, "secondary");
			localInstallButton.Click += async delegate { await InstallLocalAsync(); };
			actions.Controls.Add(localInstallButton);
			return page;
		}

		private static FlowLayoutPanel NewContentActionPanel()
		{
			FlowLayoutPanel panel = new FlowLayoutPanel();
			panel.Dock = DockStyle.Fill;
			panel.FlowDirection = FlowDirection.LeftToRight;
			panel.WrapContents = true;
			panel.Padding = new Padding(0, 8, 0, 0);
			return panel;
		}

		private static Button NewContentActionButton(string text, int width, string role)
		{
			Button button = new RoundedButton();
			button.Text = text;
			button.Width = width;
			button.Height = 40;
			button.Tag = role;
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			EnsureButtonContentFits(button);
			return button;
		}

		private async Task ReloadInstalledAsync()
		{
			CancellationToken token;
			if (!BeginContentOperation(IsContentUiKorean() ? "설치된 콘텐츠를 읽는 중..." : "Reading installed content...", out token)) return;
			try
			{
				List<InstalledContentItem> values = await Task.Run(delegate { token.ThrowIfCancellationRequested(); return ScanInstalledContent(options.ServerDirectory, options.ServerType); }, token);
				if (closing) return;
				installedItems.Clear();
				installedItems.AddRange(values);
				RenderInstalledItems();
				EndContentOperation(IsContentUiKorean() ? values.Count + "개 콘텐츠" : values.Count + " content item(s)");
			}
			catch (OperationCanceledException) { EndContentOperation(IsContentUiKorean() ? "작업을 취소했습니다." : "Operation cancelled."); }
			catch (Exception exception) { EndContentOperation((IsContentUiKorean() ? "콘텐츠 목록 오류: " : "Content list error: ") + exception.Message); }
		}

		private async Task SearchAsync()
		{
			string query = searchBox.Text.Trim();
			if (query.Length > 0 && query.Length < 2) { ShowContentMessage(this, "두 글자 이상 검색해 주세요.", "Enter at least two characters.", true); return; }
			string kind = SelectedContentKind();
			if (kind == "datapack" && worldBox.SelectedIndex < 0) { ShowContentMessage(this, "데이터팩을 설치할 월드를 먼저 선택하세요.", "Select a world for the datapack first.", true); return; }
			CancellationToken token;
			if (!BeginContentOperation(IsContentUiKorean() ? "Modrinth를 검색하는 중..." : "Searching Modrinth...", out token)) return;
			try
			{
				List<ModrinthProjectInfo> values = await Task.Run(delegate { token.ThrowIfCancellationRequested(); return SearchModrinthProjectsForKind(query, options, kind); }, token);
				if (closing) return;
				searchItems.Clear(); searchItems.AddRange(values); RenderSearchItems();
				EndContentOperation(IsContentUiKorean() ? values.Count + "개를 찾았습니다." : values.Count + " result(s) found.");
			}
			catch (OperationCanceledException) { EndContentOperation(IsContentUiKorean() ? "검색을 취소했습니다." : "Search cancelled."); }
			catch (Exception exception) { EndContentOperation((IsContentUiKorean() ? "검색 오류: " : "Search error: ") + exception.Message); }
		}

		private async Task InstallSelectedAsync()
		{
			if (searchList.SelectedIndices.Count != 1) return;
			ModrinthProjectInfo project = searchItems[searchList.SelectedIndices[0]];
			bool korean = IsContentUiKorean();
			if (ShowMineHarborDialog(this, korean ? "플러그인, 모드와 데이터팩은 서버 PC의 파일에 접근하거나 서버 동작을 변경할 수 있습니다. 제작자와 출처를 확인한 뒤 설치하세요.\r\n\r\n'" + project.Title + "'을(를) 설치할까요?" : "Plugins, mods, and datapacks can access server files or change server behavior. Verify the author and source before installing.\r\n\r\nInstall '" + project.Title + "'?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
			CancellationToken token;
			if (!BeginContentOperation(korean ? "의존성과 호환성을 확인하는 중..." : "Checking dependencies and compatibility...", out token)) return;
			Progress<ContentOperationProgress> progress = CreateContentProgress();
			try
			{
				string kind = SelectedContentKind(); string world = SelectedWorldName();
				string path = await Task.Run(delegate { return InstallManagedModrinthContent(project.Id, options, kind, world, progress, token); }, token);
				if (closing) return;
				EndContentOperation((korean ? "설치 완료 · 서버를 다시 시작하세요: " : "Installed · restart the server: ") + Path.GetFileName(path));
				await ReloadInstalledAsync();
			}
			catch (OperationCanceledException) { EndContentOperation(korean ? "설치를 취소했습니다." : "Installation cancelled."); }
			catch (Exception exception) { EndContentOperation((korean ? "설치 오류: " : "Installation error: ") + exception.Message); }
		}

		private async Task InstallLocalAsync()
		{
			string kind = SelectedContentKind();
			using (OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Filter = kind == "datapack" ? "Minecraft datapack (*.zip)|*.zip" : "Minecraft content (*.jar)|*.jar";
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				CancellationToken token; if (!BeginContentOperation(IsContentUiKorean() ? "파일을 검사하고 설치하는 중..." : "Validating and installing file...", out token)) return;
				try
				{
					string path = await Task.Run(delegate { token.ThrowIfCancellationRequested(); return InstallLocalContentFile(dialog.FileName, options, kind, SelectedWorldName()); }, token);
					if (closing) return;
					EndContentOperation((IsContentUiKorean() ? "파일 설치 완료: " : "File installed: ") + Path.GetFileName(path));
					await ReloadInstalledAsync();
				}
				catch (OperationCanceledException) { EndContentOperation(IsContentUiKorean() ? "설치를 취소했습니다." : "Installation cancelled."); }
				catch (Exception exception) { EndContentOperation((IsContentUiKorean() ? "파일 설치 오류: " : "File installation error: ") + exception.Message); }
			}
		}

		private async Task CheckUpdatesAsync()
		{
			CancellationToken token; if (!BeginContentOperation(IsContentUiKorean() ? "업데이트를 확인하는 중..." : "Checking for updates...", out token)) return;
			try
			{
				int available = await Task.Run(delegate { int count = 0; for (int i = 0; i < installedItems.Count; i++) { token.ThrowIfCancellationRequested(); if (CheckContentUpdate(options, installedItems[i], token)) count++; } return count; }, token);
				if (closing) return; RenderInstalledItems(); EndContentOperation(IsContentUiKorean() ? "업데이트 가능 " + available + "개" : available + " update(s) available");
			}
			catch (OperationCanceledException) { EndContentOperation(IsContentUiKorean() ? "확인을 취소했습니다." : "Check cancelled."); }
			catch (Exception exception) { EndContentOperation((IsContentUiKorean() ? "업데이트 확인 오류: " : "Update check error: ") + exception.Message); }
		}

		private async Task UpdateSelectedAsync(bool all)
		{
			List<InstalledContentItem> targets = new List<InstalledContentItem>();
			if (all) for (int i = 0; i < installedItems.Count; i++) if (installedItems[i].UpdateAvailable) targets.Add(installedItems[i]);
			else foreach (ListViewItem selected in installedList.SelectedItems) { InstalledContentItem item = selected.Tag as InstalledContentItem; if (item != null && item.UpdateAvailable) targets.Add(item); }
			if (targets.Count == 0) { ShowContentMessage(this, "업데이트 가능한 항목을 먼저 확인하고 선택하세요.", "Check for updates and select an available update first.", true); return; }
			CancellationToken token; if (!BeginContentOperation(IsContentUiKorean() ? "콘텐츠를 업데이트하는 중..." : "Updating content...", out token)) return;
			Progress<ContentOperationProgress> progress = CreateContentProgress();
			try
			{
				await Task.Run(delegate { for (int i = 0; i < targets.Count; i++) { token.ThrowIfCancellationRequested(); UpdateManagedContent(options, targets[i], progress, token); } }, token);
				if (closing) return; EndContentOperation(IsContentUiKorean() ? "업데이트를 완료했습니다." : "Updates completed."); await ReloadInstalledAsync();
			}
			catch (OperationCanceledException) { EndContentOperation(IsContentUiKorean() ? "업데이트를 취소했습니다." : "Update cancelled."); }
			catch (Exception exception) { EndContentOperation((IsContentUiKorean() ? "업데이트 오류: " : "Update error: ") + exception.Message); }
		}

		private async Task ToggleSelectedAsync()
		{
			InstalledContentItem item = SelectedInstalledItem(); if (item == null) return;
			CancellationToken token; if (!BeginContentOperation(IsContentUiKorean() ? "상태를 변경하는 중..." : "Changing state...", out token)) return;
			try { await Task.Run(delegate { token.ThrowIfCancellationRequested(); SetContentEnabled(options.ServerDirectory, item.Entry, !item.Active); }, token); if (closing) return; EndContentOperation(IsContentUiKorean() ? "상태를 변경했습니다." : "State changed."); await ReloadInstalledAsync(); }
			catch (Exception exception) { EndContentOperation((IsContentUiKorean() ? "상태 변경 오류: " : "State change error: ") + exception.Message); }
		}

		private async Task RemoveSelectedAsync()
		{
			InstalledContentItem item = SelectedInstalledItem(); if (item == null) return;
			bool korean = IsContentUiKorean();
			if (ShowMineHarborDialog(this, korean ? "선택한 콘텐츠를 서버에서 제거할까요? 안전을 위해 .mineharbor/content-trash로 이동합니다." : "Remove the selected content from the server? It will be moved to .mineharbor/content-trash for safety.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
			CancellationToken token; if (!BeginContentOperation(korean ? "콘텐츠를 제거하는 중..." : "Removing content...", out token)) return;
			try { await Task.Run(delegate { token.ThrowIfCancellationRequested(); RemoveContentItem(options.ServerDirectory, item.Entry); }, token); if (closing) return; EndContentOperation(korean ? "콘텐츠를 제거했습니다." : "Content removed."); await ReloadInstalledAsync(); }
			catch (Exception exception) { EndContentOperation((korean ? "제거 오류: " : "Removal error: ") + exception.Message); }
		}

		private void RenderInstalledItems()
		{
			installedList.BeginUpdate();
			try
			{
				installedList.Items.Clear();
				for (int i = 0; i < installedItems.Count; i++)
				{
					InstalledContentItem value = installedItems[i];
					ListViewItem item = new ListViewItem(value.DisplayName);
					item.SubItems.Add(ContentKindText(value.Kind)); item.SubItems.Add(value.Source); item.SubItems.Add(string.IsNullOrEmpty(value.Version) ? "—" : value.Version);
					item.SubItems.Add(value.UpdateAvailable ? (IsContentUiKorean() ? "업데이트 가능" : "Update available") : ContentStateText(value.State));
					item.SubItems.Add(string.IsNullOrEmpty(value.WorldName) ? "—" : value.WorldName); item.Tag = value; installedList.Items.Add(item);
				}
			}
			finally { installedList.EndUpdate(); }
			UpdateInstalledActions();
		}

		private void RenderSearchItems()
		{
			searchList.BeginUpdate(); try { searchList.Items.Clear(); for (int i = 0; i < searchItems.Count; i++) { ModrinthProjectInfo value = searchItems[i]; ListViewItem item = new ListViewItem(value.Title); item.SubItems.Add(value.Author); item.SubItems.Add(value.Downloads.ToString("N0", CultureInfo.CurrentCulture)); item.SubItems.Add(value.Description); item.Tag = value; searchList.Items.Add(item); } } finally { searchList.EndUpdate(); }
			installButton.Enabled = operationCancellation == null && searchList.SelectedIndices.Count == 1;
		}

		private void UpdateInstalledActions()
		{
			InstalledContentItem item = SelectedInstalledItem(); bool idle = operationCancellation == null;
			updateButton.Enabled = idle && installedList.SelectedItems.Count > 0;
			updateAllButton.Enabled = idle && installedItems.Exists(delegate(InstalledContentItem value) { return value.UpdateAvailable; });
			toggleButton.Enabled = idle && item != null && item.State != "Missing"; removeButton.Enabled = idle && item != null && item.State != "Missing";
			toggleButton.Text = item != null && !item.Active ? (IsContentUiKorean() ? "활성화" : "Enable") : (IsContentUiKorean() ? "비활성화" : "Disable");
		}

		private InstalledContentItem SelectedInstalledItem()
		{
			return installedList.SelectedItems.Count == 1 ? installedList.SelectedItems[0].Tag as InstalledContentItem : null;
		}

		private void UpdateWorldChoices()
		{
			if (worldBox == null || kindBox == null) return;
			worldBox.Items.Clear();
			List<string> worlds = GetDatapackWorldDirectories(options.ServerDirectory);
			for (int i = 0; i < worlds.Count; i++) worldBox.Items.Add(new DirectoryInfo(worlds[i]).Name);
			worldBox.Enabled = SelectedContentKind() == "datapack";
			if (worldBox.Items.Count > 0) worldBox.SelectedIndex = 0;
		}

		private string SelectedContentKind()
		{
			if (kindBox != null && kindBox.SelectedIndex == 1) return "datapack";
			return string.Equals(GetContentFolderName(options.ServerType), "plugins", StringComparison.OrdinalIgnoreCase) ? "plugin" : "mod";
		}

		private string SelectedWorldName()
		{
			return SelectedContentKind() == "datapack" && worldBox.SelectedItem != null ? Convert.ToString(worldBox.SelectedItem, CultureInfo.InvariantCulture) : string.Empty;
		}

		private bool BeginContentOperation(string status, out CancellationToken token)
		{
			if (operationCancellation != null) { token = CancellationToken.None; return false; }
			operationCancellation = new CancellationTokenSource(); token = operationCancellation.Token;
			cancelButton.Enabled = true; searchButton.Enabled = false; localInstallButton.Enabled = false; installButton.Enabled = false; progressBar.IsIndeterminate = true; SetContentStatus(status); UpdateInstalledActions(); return true;
		}

		private void EndContentOperation(string status)
		{
			if (operationCancellation != null) { operationCancellation.Dispose(); operationCancellation = null; }
			if (closing || IsDisposed) return;
			cancelButton.Enabled = false; searchButton.Enabled = true; localInstallButton.Enabled = true; installButton.Enabled = searchList.SelectedIndices.Count == 1; progressBar.IsIndeterminate = false; progressBar.Value = 0; SetContentStatus(status); UpdateInstalledActions();
		}

		private Progress<ContentOperationProgress> CreateContentProgress()
		{
			return new Progress<ContentOperationProgress>(delegate(ContentOperationProgress value) { if (closing || IsDisposed) return; progressBar.IsIndeterminate = false; progressBar.Value = value.Percent; SetContentStatus(ContentProgressText(value)); });
		}

		private string ContentProgressText(ContentOperationProgress value)
		{
			if (value == null) return string.Empty;
			return (IsContentUiKorean() ? (value.Stage == "download" ? "다운로드" : value.Stage == "complete" ? "완료" : "확인") : (value.Stage == "download" ? "Downloading" : value.Stage == "complete" ? "Complete" : "Checking")) + " · " + value.Item + " · " + value.Percent + "%";
		}

		private void SetContentStatus(string text) { statusLabel.Text = text; statusLabel.AccessibleName = text; }
		private bool IsContentUiKorean() { return string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase); }
		private string ContentKindText(string kind) { if (kind == "plugin") return IsContentUiKorean() ? "플러그인" : "Plugin"; if (kind == "mod") return IsContentUiKorean() ? "모드" : "Mod"; return IsContentUiKorean() ? "데이터팩" : "Datapack"; }
		private string ContentStateText(string state) { if (!IsContentUiKorean()) return state; if (state == "Active") return "활성"; if (state == "Disabled") return "비활성"; if (state == "Modified") return "수동 변경됨"; if (state == "Missing") return "파일 없음"; return state; }

		private void OpenSelectedContentLocation()
		{
			InstalledContentItem item = SelectedInstalledItem(); string path = item != null ? item.FullPath : options.ServerDirectory; string directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path); if (Directory.Exists(directory)) Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
		}

		protected override bool ProcessCmdKey(ref Message message, Keys keyData)
		{
			if (keyData == Keys.F5) { ObserveContentTask(ReloadInstalledAsync()); return true; }
			if (keyData == (Keys.Control | Keys.F)) { tabs.SelectedIndex = 1; searchBox.Focus(); return true; }
			if (keyData == Keys.Delete && tabs.SelectedIndex == 0 && removeButton.Enabled) { ObserveContentTask(RemoveSelectedAsync()); return true; }
			if (keyData == Keys.Escape && operationCancellation != null) { operationCancellation.Cancel(); return true; }
			return base.ProcessCmdKey(ref message, keyData);
		}

		private static async void ObserveContentTask(Task task)
		{
			try { await task; }
			catch (Exception exception) { Console.WriteLine("[Content] UI task failed: " + exception.Message); }
		}
	}
}
