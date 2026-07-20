using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static partial class Launcher
{
	private sealed class BackupManifestItem
	{
		public string RelativePath;
		public long Length;
		public string Sha256;
	}

	private sealed class BackupManagerForm : Form
	{
		private readonly string serverDirectory;
		private readonly ListView backupList;
		private readonly NumericUpDown retentionBox;
		private readonly Label statusLabel;
		private readonly Button createButton;
		private readonly Button verifyButton;
		private readonly Button restoreButton;
		private readonly Button importButton;
		private readonly Button exportButton;
		private bool busy;

		public BackupManagerForm(string profileServerDirectory)
		{
			ApplyLauncherWindowIcon(this);
			serverDirectory = Path.GetFullPath(profileServerDirectory);
			bool korean = IsBackupKorean();
			Text = korean ? "백업과 복원" : "Backup and restore";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(800, 540);
			Size = new Size(900, 620);
			Font = new Font("Pretendard", 11F);
			AutoScaleMode = AutoScaleMode.Dpi;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(24);
			root.ColumnCount = 1;
			root.RowCount = 4;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
			Controls.Add(root);

			Panel header = new Panel();
			header.Dock = DockStyle.Fill;
			root.Controls.Add(header, 0, 0);
			Label title = new Label();
			title.Text = korean ? "월드와 설정을 안전하게 보관하세요" : "Keep worlds and settings safe";
			title.Font = new Font("Pretendard", 18F, FontStyle.Bold);
			title.AutoSize = true;
			title.Location = new Point(0, 0);
			header.Controls.Add(title);
			Label retentionLabel = new Label();
			retentionLabel.Text = korean ? "보존 개수" : "Retention";
			retentionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			retentionLabel.AutoSize = true;
			retentionLabel.Location = new Point(header.Width - 180, 13);
			header.Controls.Add(retentionLabel);
			retentionBox = new NumericUpDown();
			retentionBox.Minimum = 3;
			retentionBox.Maximum = 50;
			retentionBox.Value = ReadBackupRetentionCount(serverDirectory);
			retentionBox.Width = 72;
			retentionBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			retentionBox.Location = new Point(header.Width - 72, 9);
			retentionBox.ValueChanged += delegate { WriteBackupRetentionCount(serverDirectory, (int)retentionBox.Value); };
			ConfigureAccessibleField(retentionBox, korean ? "백업 보존 개수" : "Backup retention count", korean ? "자동으로 유지할 최근 백업 개수를 지정합니다." : "Choose how many recent backups to retain automatically.");
			header.Controls.Add(retentionBox);
			header.Resize += delegate
			{
				retentionBox.Left = header.ClientSize.Width - retentionBox.Width;
				retentionLabel.Left = retentionBox.Left - retentionLabel.Width - 12;
			};

			backupList = new BufferedListView();
			backupList.Dock = DockStyle.Fill;
			backupList.View = View.Details;
			backupList.FullRowSelect = true;
			backupList.HideSelection = false;
			backupList.MultiSelect = false;
			ConfigureAccessibleField(backupList, korean ? "백업 목록" : "Backup list", korean ? "백업 시각, 크기와 무결성 상태를 확인하고 작업할 백업을 선택합니다." : "Review backup time, size, and integrity status, then select a backup.");
			backupList.Columns.Add(korean ? "백업" : "Backup", 330);
			backupList.Columns.Add(korean ? "만든 시각" : "Created", 180);
			backupList.Columns.Add(korean ? "크기" : "Size", 110);
			backupList.Columns.Add(korean ? "상태" : "Status", 140);
			backupList.SelectedIndexChanged += delegate { UpdateBackupActions(); };
			root.Controls.Add(backupList, 0, 1);

			statusLabel = new Label();
			statusLabel.Dock = DockStyle.Fill;
			statusLabel.TextAlign = ContentAlignment.MiddleLeft;
			statusLabel.Text = korean ? "복원 전에는 현재 상태도 자동으로 백업합니다." : "The current state is backed up automatically before restore.";
			root.Controls.Add(statusLabel, 0, 2);

			FlowLayoutPanel actions = new FlowLayoutPanel();
			actions.Dock = DockStyle.Fill;
			actions.FlowDirection = FlowDirection.LeftToRight;
			actions.WrapContents = true;
			actions.Padding = new Padding(0, 7, 0, 0);
			root.Controls.Add(actions, 0, 3);
			createButton = NewBackupButton(korean ? "새 백업" : "Create backup", 112, "primary");
			ApplyButtonIcon(createButton, ButtonIcon.Add);
			createButton.Click += delegate { BeginCreateBackup(); };
			actions.Controls.Add(createButton);
			verifyButton = NewBackupButton(korean ? "무결성 검사" : "Verify", 112, "secondary");
			ApplyButtonIcon(verifyButton, ButtonIcon.Check);
			verifyButton.Click += delegate { BeginVerifySelected(); };
			actions.Controls.Add(verifyButton);
			restoreButton = NewBackupButton(korean ? "복원" : "Restore", 96, "danger");
			ApplyButtonIcon(restoreButton, ButtonIcon.Refresh);
			restoreButton.Click += delegate { BeginRestoreSelected(); };
			actions.Controls.Add(restoreButton);
			importButton = NewBackupButton(korean ? "외부 백업 복원" : "Restore external", 132, "secondary");
			ApplyButtonIcon(importButton, ButtonIcon.Download);
			importButton.Click += delegate { SelectExternalBackup(); };
			actions.Controls.Add(importButton);
			exportButton = NewBackupButton(korean ? "내보내기" : "Export", 104, "secondary");
			ApplyButtonIcon(exportButton, ButtonIcon.Upgrade);
			exportButton.Click += delegate { ExportSelected(); };
			actions.Controls.Add(exportButton);
			Button openFolder = NewBackupButton(korean ? "폴더 열기" : "Open folder", 104, "secondary");
			ApplyButtonIcon(openFolder, ButtonIcon.Folder);
			openFolder.Click += delegate
			{
				string directory = GetServerBackupDirectory(serverDirectory);
				Directory.CreateDirectory(directory);
				Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
			};
			actions.Controls.Add(openFolder);
			foreach (Control control in actions.Controls) EnsureButtonContentFits(control as Button);

			Shown += delegate { ReloadBackups(); };
			ApplySimpleDialogTheme(this);
			ApplyCommonButtonToolTips(this);
		}

		private static Button NewBackupButton(string text, int width, string role)
		{
			Button button = new RoundedButton();
			button.Text = text;
			button.Width = width;
			button.Height = 40;
			button.Tag = role;
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			button.Margin = new Padding(0, 0, 8, 0);
			return button;
		}

		private void ReloadBackups()
		{
			backupList.BeginUpdate();
			try
			{
				backupList.Items.Clear();
				string directory = GetServerBackupDirectory(serverDirectory);
				if (!Directory.Exists(directory))
				{
					UpdateBackupActions();
					return;
				}
				FileInfo[] files = new DirectoryInfo(directory).GetFiles("server-*.zip");
				Array.Sort(files, delegate(FileInfo left, FileInfo right) { return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc); });
				for (int i = 0; i < files.Length; i++)
				{
					ListViewItem item = new ListViewItem(files[i].Name);
					item.SubItems.Add(files[i].LastWriteTime.ToString("g", CultureInfo.CurrentCulture));
					item.SubItems.Add(FormatBackupSize(files[i].Length));
					item.SubItems.Add(IsBackupKorean() ? "검사 전" : "Not verified");
					item.Tag = files[i].FullName;
					backupList.Items.Add(item);
				}
			}
			finally
			{
				backupList.EndUpdate();
			}
			UpdateBackupActions();
		}

		private void BeginCreateBackup()
		{
			RunBackupWork(IsBackupKorean() ? "전체 백업을 만드는 중..." : "Creating full backup...", delegate
			{
				string path = CreateComprehensiveServerBackup(serverDirectory, (int)retentionBox.Value, "manual");
				return IsBackupKorean() ? "백업 완료: " + Path.GetFileName(path) : "Backup complete: " + Path.GetFileName(path);
			}, true);
		}

		private void BeginVerifySelected()
		{
			string path = GetSelectedBackupPath();
			if (path == null)
			{
				return;
			}
			RunBackupWork(IsBackupKorean() ? "백업 무결성을 검사하는 중..." : "Verifying backup...", delegate
			{
				VerifyComprehensiveBackup(path, null);
				return IsBackupKorean() ? "무결성 검사를 통과했습니다." : "Backup verification passed.";
			}, false);
		}

		private void BeginRestoreSelected()
		{
			string path = GetSelectedBackupPath();
			if (path != null)
			{
				ConfirmAndRestore(path);
			}
		}

		private void SelectExternalBackup()
		{
			using (OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Filter = "Minecraft server backup (*.zip)|*.zip";
				dialog.CheckFileExists = true;
				if (dialog.ShowDialog(this) == DialogResult.OK)
				{
					ConfirmAndRestore(dialog.FileName);
				}
			}
		}

		private void ConfirmAndRestore(string path)
		{
			string message = IsBackupKorean()
				? "선택한 백업으로 현재 프로필을 복원할까요?\r\n\r\n현재 상태를 먼저 자동 백업한 뒤 교체합니다."
				: "Restore this profile from the selected backup?\r\n\r\nThe current state will be backed up before replacement.";
			if (ShowMineHarborDialog(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			{
				return;
			}
			RunBackupWork(IsBackupKorean() ? "현재 상태를 보관하고 복원하는 중..." : "Backing up current state and restoring...", delegate
			{
				RestoreComprehensiveBackup(serverDirectory, path, (int)retentionBox.Value);
				return IsBackupKorean() ? "복원을 완료했습니다." : "Restore completed.";
			}, true);
		}

		private void ExportSelected()
		{
			string path = GetSelectedBackupPath();
			if (path == null)
			{
				return;
			}
			using (SaveFileDialog dialog = new SaveFileDialog())
			{
				dialog.Filter = "Minecraft server backup (*.zip)|*.zip";
				dialog.FileName = Path.GetFileName(path);
				if (dialog.ShowDialog(this) == DialogResult.OK)
				{
					File.Copy(path, dialog.FileName, true);
					statusLabel.Text = IsBackupKorean() ? "백업을 내보냈습니다." : "Backup exported.";
				}
			}
		}

		private void RunBackupWork(string status, Func<string> work, bool reload)
		{
			if (busy)
			{
				return;
			}
			SetBackupBusy(true, status);
			Thread thread = new Thread((ThreadStart)delegate
			{
				try
				{
					string result = work();
					TryPostToUi(this, (MethodInvoker)delegate
					{
						SetBackupBusy(false, result);
						if (reload)
						{
							ReloadBackups();
						}
					});
				}
				catch (Exception exception)
				{
					TryPostToUi(this, (MethodInvoker)delegate
					{
						SetBackupBusy(false, (IsBackupKorean() ? "작업 실패: " : "Operation failed: ") + exception.Message);
						ShowMineHarborDialog(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
					});
				}
			});
			thread.IsBackground = true;
			thread.Name = "Minecraft 서버 백업 작업";
			thread.Start();
		}

		private void SetBackupBusy(bool value, string status)
		{
			busy = value;
			statusLabel.Text = status;
			createButton.Enabled = !value;
			importButton.Enabled = !value;
			retentionBox.Enabled = !value;
			UpdateBackupActions();
		}

		private void UpdateBackupActions()
		{
			bool selected = backupList.SelectedItems.Count == 1;
			verifyButton.Enabled = !busy && selected;
			restoreButton.Enabled = !busy && selected;
			exportButton.Enabled = !busy && selected;
		}

		private string GetSelectedBackupPath()
		{
			return backupList.SelectedItems.Count == 1 ? Convert.ToString(backupList.SelectedItems[0].Tag, CultureInfo.InvariantCulture) : null;
		}
	}

	private sealed class ProfileManagerForm : Form
	{
		private readonly string serversRoot;
		private readonly ListView profileList;
		private readonly Label statusLabel;
		private readonly List<ManagedProfileRecord> profiles = new List<ManagedProfileRecord>();

		public string SelectedProfileName { get; private set; }

		public ProfileManagerForm(string rootDirectory)
		{
			ApplyLauncherWindowIcon(this);
			serversRoot = Path.GetFullPath(rootDirectory);
			bool korean = IsBackupKorean();
			Text = korean ? "서버 프로필 관리" : "Manage server profiles";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(780, 520);
			Size = new Size(860, 590);
			Font = new Font("Pretendard", 11F);
			AutoScaleMode = AutoScaleMode.Dpi;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(24);
			root.ColumnCount = 1;
			root.RowCount = 4;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
			Controls.Add(root);

			Label heading = new Label();
			heading.Text = korean ? "서버를 만들고 복제하거나 안전하게 보관하세요" : "Create, clone, or safely archive servers";
			heading.Font = new Font("Pretendard", 18F, FontStyle.Bold);
			heading.Dock = DockStyle.Fill;
			root.Controls.Add(heading, 0, 0);

			profileList = new BufferedListView();
			profileList.Dock = DockStyle.Fill;
			profileList.View = View.Details;
			profileList.FullRowSelect = true;
			profileList.HideSelection = false;
			profileList.MultiSelect = false;
			ConfigureAccessibleField(profileList, korean ? "서버 프로필 목록" : "Server profile list", korean ? "서버 종류, 버전, 포트와 폴더를 확인하고 관리할 프로필을 선택합니다." : "Review server type, version, port, and folder, then select a profile to manage.");
			profileList.Columns.Add(korean ? "프로필" : "Profile", 220);
			profileList.Columns.Add(korean ? "종류" : "Type", 110);
			profileList.Columns.Add("Minecraft", 110);
			profileList.Columns.Add(korean ? "포트" : "Port", 80);
			profileList.Columns.Add(korean ? "폴더" : "Folder", 260);
			root.Controls.Add(profileList, 0, 1);

			statusLabel = new Label();
			statusLabel.Dock = DockStyle.Fill;
			statusLabel.TextAlign = ContentAlignment.MiddleLeft;
			root.Controls.Add(statusLabel, 0, 2);

			FlowLayoutPanel actions = new FlowLayoutPanel();
			actions.Dock = DockStyle.Fill;
			actions.FlowDirection = FlowDirection.LeftToRight;
			actions.WrapContents = true;
			actions.Padding = new Padding(0, 7, 0, 0);
			root.Controls.Add(actions, 0, 3);
			Button create = NewProfileButton(korean ? "새 서버" : "New", 96, "primary");
			ApplyButtonIcon(create, ButtonIcon.Add);
			create.Click += delegate { RunProfileAction(CreateProfile); };
			actions.Controls.Add(create);
			Button clone = NewProfileButton(korean ? "복제" : "Clone", 96, "secondary");
			ApplyButtonIcon(clone, ButtonIcon.Copy);
			clone.Click += delegate { RunProfileAction(CloneProfile); };
			actions.Controls.Add(clone);
			Button import = NewProfileButton(korean ? "가져오기" : "Import", 132, "secondary");
			ApplyButtonIcon(import, ButtonIcon.Download);
			import.Click += delegate { RunProfileAction(ImportProfile); };
			actions.Controls.Add(import);
			Button rename = NewProfileButton(korean ? "이름 변경" : "Rename", 104, "secondary");
			ApplyButtonIcon(rename, ButtonIcon.Edit);
			rename.Click += delegate { RunProfileAction(RenameProfile); };
			actions.Controls.Add(rename);
			Button archive = NewProfileButton(korean ? "보관" : "Archive", 96, "danger");
			ApplyButtonIcon(archive, ButtonIcon.Archive);
			archive.Click += delegate { RunProfileAction(ArchiveProfile); };
			actions.Controls.Add(archive);
			Button delete = NewProfileButton(korean ? "삭제" : "Delete", 96, "danger");
			ApplyButtonIcon(delete, ButtonIcon.Trash);
			delete.Click += delegate { RunProfileAction(DeleteProfile); };
			actions.Controls.Add(delete);
			Button trash = NewProfileButton(korean ? "휴지통" : "Trash", 104, "secondary");
			ApplyButtonIcon(trash, ButtonIcon.Trash);
			trash.Click += delegate { OpenServerTrash(); };
			actions.Controls.Add(trash);
			Button activate = NewProfileButton(korean ? "이 서버 선택" : "Set active", 116, "primary");
			ApplyButtonIcon(activate, ButtonIcon.Check);
			activate.Click += delegate { RunProfileAction(ActivateProfile); };
			actions.Controls.Add(activate);

			foreach (Control control in actions.Controls)
			{
				EnsureButtonContentFits(control as Button);
			}

			Shown += delegate { ReloadProfiles(); };
			ApplySimpleDialogTheme(this);
			ApplyCommonButtonToolTips(this);
		}

		private void RunProfileAction(Action action)
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				ShowProfileMessage("프로필 작업을 완료하지 못했습니다: " + exception.Message, "Could not complete the profile operation: " + exception.Message, true);
			}
		}

		private static Button NewProfileButton(string text, int width, string role)
		{
			Button button = new RoundedButton();
			button.Text = text;
			button.Width = width;
			button.Height = 40;
			button.Tag = role;
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			button.Margin = new Padding(0, 0, 8, 0);
			return button;
		}

		private void ReloadProfiles()
		{
			PurgeExpiredServerTrash(serversRoot, DateTime.UtcNow);
			profiles.Clear();
			profiles.AddRange(ReadManagedProfiles(serversRoot));
			string active = ReadActiveProfileName(serversRoot);
			profileList.BeginUpdate();
			try
			{
				profileList.Items.Clear();
				for (int i = 0; i < profiles.Count; i++)
				{
					ManagedProfileRecord profile = profiles[i];
					ListViewItem item = new ListViewItem(string.Equals(profile.Name, active, StringComparison.OrdinalIgnoreCase) ? "★ " + profile.Name : profile.Name);
					item.SubItems.Add(GetServerTypeDisplayName(profile.ServerType));
					item.SubItems.Add(profile.MinecraftVersion);
					item.SubItems.Add(profile.Port.ToString(CultureInfo.InvariantCulture));
					item.SubItems.Add(profile.Directory);
					item.Tag = profile.Name;
					profileList.Items.Add(item);
				}
			}
			finally
			{
				profileList.EndUpdate();
			}
			statusLabel.Text = IsBackupKorean() ? profiles.Count + "개 서버 프로필" : profiles.Count + " server profile(s)";
		}

		private void CreateProfile()
		{
			string name = PromptProfileText(this, IsBackupKorean() ? "새 서버 이름" : "New server name", string.Empty);
			if (string.IsNullOrEmpty(name))
			{
				return;
			}
			string directory = EnsureNewProfileDirectory(name);
			string previous = ReadActiveProfileName(serversRoot);
			try
			{
				WriteActiveProfileName(serversRoot, name);
				LauncherOptions configured = ConfigureServerPropertiesGui(serversRoot, true);
				RemoveSupersededEmptyProfileDirectory(directory, configured.ServerDirectory);
				int suggested = FindAvailableServerPort(serversRoot, 25565, configured.ProfileName);
				SetSimplePropertyValue(Path.Combine(configured.ServerDirectory, "server.properties"), "server-port", suggested.ToString(CultureInfo.InvariantCulture));
				SelectedProfileName = configured.ProfileName;
				ReloadProfiles();
			}
			catch (OperationCanceledException)
			{
				WriteActiveProfileName(serversRoot, previous);
				if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
				{
					Directory.Delete(directory);
				}
			}
			catch (Exception exception)
			{
				WriteActiveProfileName(serversRoot, previous);
				RemoveSupersededEmptyProfileDirectory(directory, null);
				ShowProfileMessage("새 서버를 만들지 못했습니다: " + exception.Message, "Could not create the server: " + exception.Message, true);
			}
		}

		private void CloneProfile()
		{
			ManagedProfileRecord source = GetSelectedProfile();
			if (source == null || !EnsureProfileStopped(source))
			{
				return;
			}
			string name = PromptProfileText(this, IsBackupKorean() ? "복제 서버 이름" : "Cloned server name", source.Name + " 복사본");
			if (string.IsNullOrEmpty(name))
			{
				return;
			}
			string destination = EnsureNewProfileDirectory(name);
			try
			{
				CopyProfileDirectory(source.Directory, destination);
				SetSimplePropertyValue(Path.Combine(destination, ".launcher-properties-configured"), "profile-name", name);
				int port = FindAvailableServerPort(serversRoot, source.Port + 1, name);
				SetSimplePropertyValue(Path.Combine(destination, "server.properties"), "server-port", port.ToString(CultureInfo.InvariantCulture));
				WriteActiveProfileName(serversRoot, name);
				SelectedProfileName = name;
				ReloadProfiles();
				ShowProfileMessage("복제를 완료했습니다. 새 포트는 " + port + "입니다.", "Clone completed on port " + port + ".", false);
			}
			catch
			{
				if (Directory.Exists(destination))
				{
					Directory.Delete(destination, true);
				}
				throw;
			}
		}

		private void ImportProfile()
		{
			using (FolderBrowserDialog dialog = new FolderBrowserDialog())
			{
				dialog.Description = IsBackupKorean() ? "가져올 기존 Minecraft 서버 폴더를 선택하세요." : "Select an existing Minecraft server folder.";
				if (dialog.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}
				string name = PromptProfileText(this, IsBackupKorean() ? "가져온 서버 이름" : "Imported server name", new DirectoryInfo(dialog.SelectedPath).Name);
				if (string.IsNullOrEmpty(name))
				{
					return;
				}
				string destination = EnsureNewProfileDirectory(name);
				try
				{
					CopyProfileDirectory(dialog.SelectedPath, destination);
					WriteActiveProfileName(serversRoot, name);
					SelectedProfileName = name;
					LauncherOptions configured = ConfigureServerPropertiesGui(serversRoot, true);
					MoveImportedProfileToConfiguredDirectory(destination, configured.ServerDirectory);
					int currentPort = ReadConfiguredServerPort(Path.Combine(configured.ServerDirectory, "server.properties"), 25565);
					int port = FindAvailableServerPort(serversRoot, currentPort, configured.ProfileName);
					SetSimplePropertyValue(Path.Combine(configured.ServerDirectory, "server.properties"), "server-port", port.ToString(CultureInfo.InvariantCulture));
					SelectedProfileName = configured.ProfileName;
					ReloadProfiles();
				}
				catch (OperationCanceledException)
				{
					if (Directory.Exists(destination))
					{
						Directory.Delete(destination, true);
					}
				}
				catch (Exception exception)
				{
					if (Directory.Exists(destination))
					{
						Directory.Delete(destination, true);
					}
					ShowProfileMessage("서버를 가져오지 못했습니다: " + exception.Message, "Could not import the server: " + exception.Message, true);
				}
			}
		}

		private void RenameProfile()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null || !EnsureProfileStopped(profile))
			{
				return;
			}
			string name = PromptProfileText(this, IsBackupKorean() ? "새 프로필 이름" : "New profile name", profile.Name);
			if (string.IsNullOrEmpty(name) || string.Equals(name, profile.Name, StringComparison.Ordinal))
			{
				return;
			}
			string destination = GetProfileDirectory(serversRoot, name);
			EnsureSafeProfilePath(serversRoot, destination);
			if (Directory.Exists(destination))
			{
				ShowProfileMessage("같은 이름의 프로필 폴더가 이미 있습니다.", "A profile folder with that name already exists.", true);
				return;
			}
			Directory.Move(profile.Directory, destination);
			SetSimplePropertyValue(Path.Combine(destination, ".launcher-properties-configured"), "profile-name", name);
			if (string.Equals(ReadActiveProfileName(serversRoot), profile.Name, StringComparison.OrdinalIgnoreCase))
			{
				WriteActiveProfileName(serversRoot, name);
			}
			SelectedProfileName = name;
			ReloadProfiles();
		}

		private void ArchiveProfile()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null || !EnsureProfileStopped(profile))
			{
				return;
			}
			if (profiles.Count <= 1)
			{
				ShowProfileMessage("마지막 프로필은 보관할 수 없습니다.", "The last profile cannot be archived.", true);
				return;
			}
			if (ShowMineHarborDialog(this, IsBackupKorean() ? "이 서버를 삭제하지 않고 보관 폴더로 옮길까요?" : "Move this server to the archive without deleting it?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			{
				return;
			}
			string archiveRoot = Path.Combine(serversRoot, "servers-archive");
			Directory.CreateDirectory(archiveRoot);
			string destination = Path.Combine(archiveRoot, ToSafeDirectoryName(profile.Name) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
			EnsurePathInsideRoot(archiveRoot, destination);
			Directory.Move(profile.Directory, destination);
			if (string.Equals(ReadActiveProfileName(serversRoot), profile.Name, StringComparison.OrdinalIgnoreCase))
			{
				string replacement = profiles[0].Name;
				if (string.Equals(replacement, profile.Name, StringComparison.OrdinalIgnoreCase))
				{
					replacement = profiles[1].Name;
				}
				WriteActiveProfileName(serversRoot, replacement);
				SelectedProfileName = replacement;
			}
			ReloadProfiles();
		}

		private void DeleteProfile()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null || !EnsureProfileStopped(profile)) return;
			ShowMineHarborDialog(this, IsBackupKorean() ? "서버 폴더 전체(월드, 플러그인, 모드, 설정)를 휴지통으로 옮기며 30일 동안 복구할 수 있습니다. 별도 백업 폴더는 그대로 유지됩니다. 계속하려면 다음 창에 서버 이름을 입력하세요.\r\n\r\n" + profile.Name : "The entire server folder (worlds, plugins, mods, and settings) will move to Trash and can be restored for 30 days. Separate backups are kept. Enter the server name in the next window to continue.\r\n\r\n" + profile.Name, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			string confirmation = PromptProfileText(this, IsBackupKorean() ? "서버 삭제 확인" : "Confirm server deletion", string.Empty);
			if (!string.Equals(confirmation, profile.Name, StringComparison.Ordinal))
			{
				if (confirmation != null) ShowProfileMessage("서버 이름이 일치하지 않습니다.", "The server name does not match.", true);
				return;
			}
			MoveProfileToServerTrash(serversRoot, profile, DateTime.UtcNow);
			SelectedProfileName = UpdateActiveProfileAfterRemoval(serversRoot, profiles, profile.Name);
			ReloadProfiles();
		}

		private void OpenServerTrash()
		{
			using (ServerTrashForm form = new ServerTrashForm(serversRoot)) form.ShowDialog(this);
			ReloadProfiles();
		}

		private void ActivateProfile()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null)
			{
				return;
			}
			WriteActiveProfileName(serversRoot, profile.Name);
			SelectedProfileName = profile.Name;
			ReloadProfiles();
			DialogResult = DialogResult.OK;
			Close();
		}

		private ManagedProfileRecord GetSelectedProfile()
		{
			if (profileList.SelectedItems.Count != 1)
			{
				return null;
			}
			string name = Convert.ToString(profileList.SelectedItems[0].Tag, CultureInfo.InvariantCulture);
			for (int i = 0; i < profiles.Count; i++)
			{
				if (string.Equals(profiles[i].Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return profiles[i];
				}
			}
			return null;
		}

		private bool EnsureProfileStopped(ManagedProfileRecord profile)
		{
			if (IsLocalTcpPortListening(profile.Port))
			{
				ShowProfileMessage("이 서버의 포트가 사용 중입니다. 서버를 안전하게 종료한 뒤 다시 시도해 주세요.", "This server port is active. Stop the server safely and try again.", true);
				return false;
			}
			return true;
		}

		private string EnsureNewProfileDirectory(string name)
		{
			if (!IsValidProfileName(name))
			{
				throw new InvalidDataException("프로필 이름은 1~48자로 입력해 주세요.");
			}
			string directory = GetProfileDirectory(serversRoot, name);
			EnsureSafeProfilePath(serversRoot, directory);
			if (Directory.Exists(directory))
			{
				throw new IOException("같은 이름의 프로필 폴더가 이미 있습니다.");
			}
			Directory.CreateDirectory(directory);
			return directory;
		}

		private static void RemoveSupersededEmptyProfileDirectory(string originalDirectory, string configuredDirectory)
		{
			if (!Directory.Exists(originalDirectory))
			{
				return;
			}
			if (!string.IsNullOrEmpty(configuredDirectory) && string.Equals(Path.GetFullPath(originalDirectory), Path.GetFullPath(configuredDirectory), StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (Directory.GetFileSystemEntries(originalDirectory).Length == 0)
			{
				Directory.Delete(originalDirectory);
			}
		}

		private static void MoveImportedProfileToConfiguredDirectory(string importedDirectory, string configuredDirectory)
		{
			if (!Directory.Exists(importedDirectory))
			{
				return;
			}
			if (string.Equals(Path.GetFullPath(importedDirectory), Path.GetFullPath(configuredDirectory), StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			string configuredProperties = File.ReadAllText(Path.Combine(configuredDirectory, "server.properties"), Encoding.UTF8);
			string configuredMarker = File.ReadAllText(Path.Combine(configuredDirectory, ".launcher-properties-configured"), Encoding.UTF8);
			Directory.Delete(configuredDirectory, true);
			Directory.Move(importedDirectory, configuredDirectory);
			File.WriteAllText(Path.Combine(configuredDirectory, "server.properties"), configuredProperties, new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(configuredDirectory, ".launcher-properties-configured"), configuredMarker, new UTF8Encoding(false));
		}

		private void ShowProfileMessage(string korean, string english, bool warning)
		{
			ShowMineHarborDialog(this, IsBackupKorean() ? korean : english, Text, MessageBoxButtons.OK, warning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
		}
	}

	private static string CreateComprehensiveServerBackup(string serverDirectory, int retentionCount, string reason)
	{
		string fullServerDirectory = Path.GetFullPath(serverDirectory);
		if (!Directory.Exists(fullServerDirectory))
		{
			throw new DirectoryNotFoundException("백업할 서버 폴더를 찾지 못했습니다.");
		}
		List<string> files = CollectProfileBackupFiles(fullServerDirectory);
		if (files.Count == 0)
		{
			throw new InvalidOperationException("백업할 서버 파일이 없습니다.");
		}
		long totalSize = 0L;
		for (int i = 0; i < files.Count; i++)
		{
			totalSize = checked(totalSize + new FileInfo(files[i]).Length);
		}
		string backupDirectory = GetServerBackupDirectory(fullServerDirectory);
		Directory.CreateDirectory(backupDirectory);
		DriveInfo drive = new DriveInfo(Path.GetPathRoot(backupDirectory));
		long required = checked(totalSize + 104857600L);
		if (drive.AvailableFreeSpace < required)
		{
			throw new IOException("백업에 필요한 여유 공간이 부족합니다. 최소 " + FormatBackupSize(required) + "가 필요합니다.");
		}
		string safeReason = string.Equals(reason, "restore-safety", StringComparison.OrdinalIgnoreCase) ? "pre-restore" : string.Equals(reason, "manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "automatic";
		string finalPath = Path.Combine(backupDirectory, "server-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + safeReason + ".zip");
		string temporaryPath = finalPath + ".준비중";
		List<BackupManifestItem> manifest = new List<BackupManifestItem>();
		try
		{
			using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
			using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				for (int i = 0; i < files.Count; i++)
				{
					manifest.Add(AddProfileFileToArchive(archive, fullServerDirectory, files[i]));
				}
				AddBackupManifest(archive, manifest, safeReason);
			}
			VerifyComprehensiveBackup(temporaryPath, null);
			File.Move(temporaryPath, finalPath);
			PruneServerBackups(backupDirectory, retentionCount);
			return finalPath;
		}
		finally
		{
			DeleteFileIfPresent(temporaryPath);
		}
	}

	private static List<string> CollectProfileBackupFiles(string serverDirectory)
	{
		List<string> result = new List<string>();
		CollectProfileBackupFilesRecursive(serverDirectory, serverDirectory, result, 0);
		return result;
	}

	private static void CollectProfileBackupFilesRecursive(string root, string directory, List<string> result, int depth)
	{
		if (depth > 128)
		{
			throw new InvalidDataException("서버 폴더 단계가 지나치게 깊습니다.");
		}
		DirectoryInfo directoryInfo = new DirectoryInfo(directory);
		if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
		{
			return;
		}
		string relativeDirectory = GetRelativeBackupPath(root, directory);
		if (IsExcludedBackupDirectory(relativeDirectory))
		{
			return;
		}
		FileInfo[] files = directoryInfo.GetFiles();
		for (int i = 0; i < files.Length; i++)
		{
			if ((files[i].Attributes & FileAttributes.ReparsePoint) == 0 && !files[i].Name.EndsWith(".준비중", StringComparison.OrdinalIgnoreCase) && files[i].Name.IndexOf(".다운로드중-", StringComparison.OrdinalIgnoreCase) < 0)
			{
				result.Add(files[i].FullName);
			}
		}
		DirectoryInfo[] directories = directoryInfo.GetDirectories();
		for (int i = 0; i < directories.Length; i++)
		{
			CollectProfileBackupFilesRecursive(root, directories[i].FullName, result, depth + 1);
		}
	}

	private static bool IsExcludedBackupDirectory(string relativeDirectory)
	{
		if (string.IsNullOrEmpty(relativeDirectory))
		{
			return false;
		}
		string first = relativeDirectory.Split(new char[2] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)[0];
		string normalized = relativeDirectory.Replace('\\', '/').Trim('/');
		if (normalized.StartsWith(".mineharbor/content-backups", StringComparison.OrdinalIgnoreCase)
			|| normalized.StartsWith(".mineharbor/content-trash", StringComparison.OrdinalIgnoreCase)) return true;
		return string.Equals(first, "server-backups", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(first, "server-jar-backups", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(first, "configuration-backups", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(first, "content-backups", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(first, "diagnostics", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(first, "logs", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(first, "cache", StringComparison.OrdinalIgnoreCase)
			|| first.StartsWith(".restore-", StringComparison.OrdinalIgnoreCase);
	}

	private static BackupManifestItem AddProfileFileToArchive(ZipArchive archive, string root, string path)
	{
		string relative = GetRelativeBackupPath(root, path).Replace('\\', '/');
		if (string.IsNullOrEmpty(relative) || relative.StartsWith("../", StringComparison.Ordinal) || relative.IndexOf("/../", StringComparison.Ordinal) >= 0)
		{
			throw new InvalidDataException("백업 파일 경로가 서버 폴더 밖을 가리킵니다.");
		}
		ZipArchiveEntry entry = archive.CreateEntry("profile/" + relative, CompressionLevel.Fastest);
		entry.LastWriteTime = File.GetLastWriteTime(path);
		long length = 0L;
		string hash;
		using (SHA256 sha = SHA256.Create())
		using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		using (Stream output = entry.Open())
		{
			byte[] buffer = new byte[131072];
			int read;
			while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer, 0, read);
				sha.TransformBlock(buffer, 0, read, buffer, 0);
				length = checked(length + read);
			}
			sha.TransformFinalBlock(new byte[0], 0, 0);
			hash = ToLowerHex(sha.Hash);
		}
		BackupManifestItem item = new BackupManifestItem();
		item.RelativePath = relative;
		item.Length = length;
		item.Sha256 = hash;
		return item;
	}

	private static void AddBackupManifest(ZipArchive archive, List<BackupManifestItem> items, string reason)
	{
		ZipArchiveEntry entry = archive.CreateEntry("backup-manifest.tsv", CompressionLevel.Optimal);
		using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
		{
			writer.WriteLine("format=2");
			writer.WriteLine("created-utc=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
			writer.WriteLine("reason=" + reason);
			for (int i = 0; i < items.Count; i++)
			{
				string encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(items[i].RelativePath));
				writer.WriteLine(items[i].Sha256 + "\t" + items[i].Length.ToString(CultureInfo.InvariantCulture) + "\t" + encodedPath);
			}
		}
	}

	private static List<BackupManifestItem> VerifyComprehensiveBackup(string backupPath, string extractedRoot)
	{
		const int maximumBackupEntryCount = 100000;
		const long maximumBackupExpandedBytes = 53687091200L;
		List<BackupManifestItem> items;
		using (ZipArchive archive = ZipFile.OpenRead(backupPath))
		{
			if (archive.Entries.Count > maximumBackupEntryCount)
			{
				throw new InvalidDataException("백업 항목 수가 안전 제한을 초과했습니다.");
			}
			items = ReadBackupManifest(archive);
			Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
			long expandedBytes = 0L;
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				if (entry.FullName.StartsWith("profile/", StringComparison.OrdinalIgnoreCase))
				{
					string relative = entry.FullName.Substring(8).Replace('\\', '/');
					ValidateBackupRelativePath(relative);
					if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
					if (entries.ContainsKey(relative)) throw new InvalidDataException("백업에 중복 파일 경로가 있습니다: " + relative);
					expandedBytes = checked(expandedBytes + entry.Length);
					if (expandedBytes > maximumBackupExpandedBytes) throw new InvalidDataException("백업의 총 해제 크기가 안전 제한을 초과했습니다.");
					entries.Add(relative, entry);
				}
			}
			if (entries.Count != items.Count) throw new InvalidDataException("백업 파일 목록이 manifest와 정확히 일치하지 않습니다.");
			for (int i = 0; i < items.Count; i++)
			{
				ZipArchiveEntry entry;
				if (!entries.TryGetValue(items[i].RelativePath, out entry) || entry.Length != items[i].Length)
				{
					throw new InvalidDataException("백업 파일 목록이나 크기가 manifest와 다릅니다: " + items[i].RelativePath);
				}
				using (SHA256 sha = SHA256.Create())
				using (Stream input = entry.Open())
				{
					string hash = ToLowerHex(sha.ComputeHash(input));
					if (!string.Equals(hash, items[i].Sha256, StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidDataException("백업 파일의 SHA-256이 일치하지 않습니다: " + items[i].RelativePath);
					}
				}
			}
		}
		if (!string.IsNullOrEmpty(extractedRoot))
		{
			for (int i = 0; i < items.Count; i++)
			{
				string path = GetSafeBackupDestination(extractedRoot, items[i].RelativePath);
				FileInfo file = new FileInfo(path);
				if (!file.Exists || file.Length != items[i].Length)
				{
					throw new InvalidDataException("복원 staging 파일을 검증하지 못했습니다: " + items[i].RelativePath);
				}
				using (SHA256 sha = SHA256.Create())
				using (FileStream stream = file.OpenRead())
				{
					if (!string.Equals(ToLowerHex(sha.ComputeHash(stream)), items[i].Sha256, StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidDataException("복원 staging 파일의 SHA-256이 일치하지 않습니다: " + items[i].RelativePath);
					}
				}
			}
		}
		return items;
	}

	private static List<BackupManifestItem> ReadBackupManifest(ZipArchive archive)
	{
		ZipArchiveEntry manifestEntry = archive.GetEntry("backup-manifest.tsv");
		if (manifestEntry == null || manifestEntry.Length <= 0 || manifestEntry.Length > 16777216L)
		{
			throw new InvalidDataException("지원되는 백업 manifest를 찾지 못했습니다.");
		}
		List<BackupManifestItem> result = new List<BackupManifestItem>();
		using (StreamReader reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
		{
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				if (line.IndexOf('\t') < 0)
				{
					continue;
				}
				string[] parts = line.Split('\t');
				long length;
				if (parts.Length != 3 || parts[0].Length != 64 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0)
				{
					throw new InvalidDataException("백업 manifest 항목 형식이 잘못되었습니다.");
				}
				string relative;
				try
				{
					relative = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
				}
				catch (FormatException)
				{
					throw new InvalidDataException("백업 manifest 파일 경로를 해석하지 못했습니다.");
				}
				ValidateBackupRelativePath(relative);
				BackupManifestItem item = new BackupManifestItem();
				item.Sha256 = parts[0];
				item.Length = length;
				item.RelativePath = relative;
				result.Add(item);
			}
		}
		if (result.Count == 0)
		{
			throw new InvalidDataException("백업 manifest에 파일이 없습니다.");
		}
		return result;
	}

	private static void RestoreComprehensiveBackup(string serverDirectory, string backupPath, int retentionCount)
	{
		string fullServer = Path.GetFullPath(serverDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string parent = Path.GetDirectoryName(fullServer);
		if (string.IsNullOrEmpty(parent) || !Directory.Exists(fullServer))
		{
			throw new DirectoryNotFoundException("복원할 프로필 폴더를 찾지 못했습니다.");
		}
		VerifyComprehensiveBackup(backupPath, null);
		CreateComprehensiveServerBackup(fullServer, retentionCount, "restore-safety");
		string token = Guid.NewGuid().ToString("N");
		string staging = Path.Combine(parent, ".restore-staging-" + token);
		string previous = Path.Combine(parent, ".restore-previous-" + token);
		EnsurePathInsideRoot(parent, staging);
		EnsurePathInsideRoot(parent, previous);
		Directory.CreateDirectory(staging);
		try
		{
			ExtractComprehensiveBackup(backupPath, staging);
			VerifyComprehensiveBackup(backupPath, staging);
			Directory.Move(fullServer, previous);
			MoveRestorePreservedDirectories(previous, staging);
			try
			{
				Directory.Move(staging, fullServer);
			}
			catch
			{
				MoveRestorePreservedDirectories(staging, previous);
				Directory.Move(previous, fullServer);
				throw;
			}
			try
			{
				Directory.Delete(previous, true);
			}
			catch (Exception ex)
			{
				Console.WriteLine("복원은 완료했지만 이전 폴더를 정리하지 못했습니다: " + ex.Message);
			}
		}
		catch
		{
			if (!Directory.Exists(fullServer) && Directory.Exists(previous))
			{
				Directory.Move(previous, fullServer);
			}
			throw;
		}
		finally
		{
			if (Directory.Exists(staging))
			{
				Directory.Delete(staging, true);
			}
		}
	}

	private static void MoveRestorePreservedDirectories(string sourceRoot, string destinationRoot)
	{
		string[] relativeDirectories =
		{
			"server-backups", "server-jar-backups", "configuration-backups", "content-backups", "diagnostics", "logs", "cache",
			Path.Combine(".mineharbor", "content-backups"), Path.Combine(".mineharbor", "content-trash")
		};
		for (int i = 0; i < relativeDirectories.Length; i++)
		{
			string source = Path.Combine(sourceRoot, relativeDirectories[i]);
			string destination = Path.Combine(destinationRoot, relativeDirectories[i]);
			if (!Directory.Exists(source)) continue;
			Directory.CreateDirectory(Path.GetDirectoryName(destination));
			if (Directory.Exists(destination)) Directory.Delete(destination, true);
			Directory.Move(source, destination);
		}
	}

	private static void ExtractComprehensiveBackup(string backupPath, string staging)
	{
		using (ZipArchive archive = ZipFile.OpenRead(backupPath))
		{
			List<BackupManifestItem> items = ReadBackupManifest(archive);
			Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
			foreach (ZipArchiveEntry candidate in archive.Entries)
			{
				if (!candidate.FullName.StartsWith("profile/", StringComparison.OrdinalIgnoreCase) || candidate.FullName.EndsWith("/", StringComparison.Ordinal))
				{
					continue;
				}
				string key = candidate.FullName.Substring(8).Replace('\\', '/');
				ValidateBackupRelativePath(key);
				if (entries.ContainsKey(key)) throw new InvalidDataException("백업에 중복 파일 경로가 있습니다: " + key);
				entries.Add(key, candidate);
			}
			for (int i = 0; i < items.Count; i++)
			{
				ZipArchiveEntry entry;
				if (!entries.TryGetValue(items[i].RelativePath, out entry)) throw new InvalidDataException("manifest 파일을 백업에서 찾지 못했습니다: " + items[i].RelativePath);
				string relative = items[i].RelativePath.Replace('/', Path.DirectorySeparatorChar);
				string destination = GetSafeBackupDestination(staging, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(destination));
				using (Stream input = entry.Open())
				using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					input.CopyTo(output);
					output.Flush(true);
				}
			}
		}
	}

	private static void ValidateBackupRelativePath(string relative)
	{
		if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf('\0') >= 0)
		{
			throw new InvalidDataException("백업에 안전하지 않은 파일 경로가 있습니다.");
		}
		string normalized = relative.Replace('/', Path.DirectorySeparatorChar);
		string[] parts = normalized.Split(Path.DirectorySeparatorChar);
		for (int i = 0; i < parts.Length; i++)
		{
			if (parts[i] == ".." || parts[i].Length == 0)
			{
				throw new InvalidDataException("백업 파일 경로가 서버 폴더 밖을 가리킵니다.");
			}
		}
	}

	private static string GetSafeBackupDestination(string root, string relative)
	{
		ValidateBackupRelativePath(relative);
		string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string candidate = Path.GetFullPath(Path.Combine(root, relative));
		if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("백업 파일 경로가 복원 폴더 밖을 가리킵니다.");
		}
		return candidate;
	}

	private static void PruneServerBackups(string backupDirectory, int retentionCount)
	{
		retentionCount = Math.Max(3, Math.Min(50, retentionCount));
		FileInfo[] files = new DirectoryInfo(backupDirectory).GetFiles("server-*.zip");
		Array.Sort(files, delegate(FileInfo left, FileInfo right) { return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc); });
		for (int i = retentionCount; i < files.Length; i++)
		{
			files[i].Delete();
		}
	}

	private static int ReadBackupRetentionCount(string serverDirectory)
	{
		Dictionary<string, string> values = ReadSimpleProperties(Path.Combine(serverDirectory, ".launcher-backup-settings"));
		int count;
		return values.ContainsKey("retention-count") && int.TryParse(values["retention-count"], out count) ? Math.Max(3, Math.Min(50, count)) : 10;
	}

	private static void WriteBackupRetentionCount(string serverDirectory, int count)
	{
		SetSimplePropertyValue(Path.Combine(serverDirectory, ".launcher-backup-settings"), "retention-count", Math.Max(3, Math.Min(50, count)).ToString(CultureInfo.InvariantCulture));
	}

	private static string GetServerBackupDirectory(string serverDirectory)
	{
		return Path.Combine(serverDirectory, "server-backups");
	}

	private static string GetRelativeBackupPath(string root, string path)
	{
		string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(path);
		if (string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}
		if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("서버 폴더 밖의 파일은 백업할 수 없습니다.");
		}
		return fullPath.Substring(fullRoot.Length);
	}

	private static string FormatBackupSize(long bytes)
	{
		if (bytes >= 1073741824L)
		{
			return ((double)bytes / 1073741824.0).ToString("0.0", CultureInfo.CurrentCulture) + " GB";
		}
		if (bytes >= 1048576L)
		{
			return ((double)bytes / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB";
		}
		return ((double)bytes / 1024.0).ToString("0.0", CultureInfo.CurrentCulture) + " KB";
	}

	private static bool IsBackupKorean()
	{
		return string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
	}

	private static string PromptProfileText(IWin32Window owner, string title, string initial)
	{
		using (Form form = new Form())
		{
			form.Text = title;
			form.StartPosition = FormStartPosition.CenterParent;
			form.FormBorderStyle = FormBorderStyle.FixedDialog;
			form.MinimizeBox = false;
			form.MaximizeBox = false;
			form.ClientSize = new Size(420, 140);
			form.Font = new Font("Pretendard", 11F);
			TextBox textBox = new TextBox();
			textBox.Location = new Point(24, 28);
			textBox.Size = new Size(372, 30);
			textBox.MaxLength = 48;
			textBox.Text = initial ?? string.Empty;
			form.Controls.Add(textBox);
			ThemePalette palette = ThemePalette.Create(launcherForm != null && launcherForm.UsesDarkTheme);
			Button cancel = CreateMineHarborDialogButton(IsBackupKorean() ? "취소" : "Cancel", 96, "secondary", ButtonIcon.None, palette);
			cancel.DialogResult = DialogResult.Cancel;
			cancel.Location = new Point(196, 78);
			form.Controls.Add(cancel);
			Button ok = CreateMineHarborDialogButton(IsBackupKorean() ? "확인" : "OK", 96, "primary", ButtonIcon.Check, palette);
			ok.DialogResult = DialogResult.OK;
			ok.Location = new Point(300, 78);
			form.Controls.Add(ok);
			form.AcceptButton = ok;
			form.CancelButton = cancel;
			ApplySimpleDialogTheme(form);
			if (form.ShowDialog(owner) != DialogResult.OK)
			{
				return null;
			}
			string value = textBox.Text.Trim();
			if (!IsValidProfileName(value))
			{
				ShowMineHarborDialog(owner, IsBackupKorean() ? "프로필 이름은 1~48자로 입력해 주세요." : "Enter a profile name between 1 and 48 characters.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return null;
			}
			return value;
		}
	}

	private static int FindAvailableServerPort(string serversRoot, int startingPort, string exceptProfile)
	{
		HashSet<int> used = new HashSet<int>();
		List<ManagedProfileRecord> profiles = ReadManagedProfiles(serversRoot);
		for (int i = 0; i < profiles.Count; i++)
		{
			if (!string.Equals(profiles[i].Name, exceptProfile, StringComparison.OrdinalIgnoreCase))
			{
				used.Add(profiles[i].Port);
			}
		}
		int port = Math.Max(1024, Math.Min(65535, startingPort));
		for (int i = 0; i < 64512; i++)
		{
			int candidate = port + i;
			if (candidate > 65535)
			{
				candidate = 1024 + (candidate - 65536);
			}
			if (!used.Contains(candidate) && !IsLocalTcpPortListening(candidate))
			{
				return candidate;
			}
		}
		throw new InvalidOperationException("사용 가능한 서버 포트를 찾지 못했습니다.");
	}

	private static void CopyProfileDirectory(string source, string destination)
	{
		string fullSource = Path.GetFullPath(source);
		string fullDestination = Path.GetFullPath(destination);
		if (string.Equals(fullSource, fullDestination, StringComparison.OrdinalIgnoreCase) || fullDestination.StartsWith(fullSource.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("서버 폴더 안으로 자기 자신을 복사할 수 없습니다.");
		}
		CopyProfileDirectoryRecursive(fullSource, fullDestination, 0);
	}

	private static void CopyProfileDirectoryRecursive(string source, string destination, int depth)
	{
		if (depth > 128)
		{
			throw new InvalidDataException("서버 폴더 단계가 지나치게 깊습니다.");
		}
		DirectoryInfo sourceInfo = new DirectoryInfo(source);
		if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
		{
			return;
		}
		Directory.CreateDirectory(destination);
		FileInfo[] files = sourceInfo.GetFiles();
		for (int i = 0; i < files.Length; i++)
		{
			if ((files[i].Attributes & FileAttributes.ReparsePoint) == 0 && !files[i].Name.EndsWith(".준비중", StringComparison.OrdinalIgnoreCase))
			{
				files[i].CopyTo(Path.Combine(destination, files[i].Name), false);
			}
		}
		DirectoryInfo[] directories = sourceInfo.GetDirectories();
		for (int i = 0; i < directories.Length; i++)
		{
			if (IsExcludedBackupDirectory(directories[i].Name))
			{
				continue;
			}
			CopyProfileDirectoryRecursive(directories[i].FullName, Path.Combine(destination, directories[i].Name), depth + 1);
		}
	}

	private static void SetSimplePropertyValue(string path, string key, string value)
	{
		List<string> lines = new List<string>();
		if (File.Exists(path))
		{
			lines.AddRange(File.ReadAllLines(path, Encoding.UTF8));
		}
		bool replaced = false;
		for (int i = 0; i < lines.Count; i++)
		{
			string trimmed = lines[i].TrimStart();
			if (!trimmed.StartsWith("#", StringComparison.Ordinal) && trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
			{
				lines[i] = key + "=" + value;
				replaced = true;
				break;
			}
		}
		if (!replaced)
		{
			lines.Add(key + "=" + value);
		}
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string temporary = path + ".준비중";
		File.WriteAllLines(temporary, lines.ToArray(), new UTF8Encoding(false));
		ReplaceFile(temporary, path);
	}

	private static void EnsureSafeProfilePath(string serversRoot, string profileDirectory)
	{
		EnsurePathInsideRoot(Path.Combine(serversRoot, "servers"), profileDirectory);
	}

	private static void EnsurePathInsideRoot(string root, string path)
	{
		string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || fullPath.Length <= fullRoot.Length)
		{
			throw new InvalidDataException("대상 경로가 허용된 서버 폴더 밖을 가리킵니다.");
		}
	}
}
