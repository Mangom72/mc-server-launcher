using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal static partial class Launcher
{
	private static bool ManagedChildMode;
	private static string ManagedProfileOverride;

	private sealed class ManagedProfileRecord
	{
		public string Name;
		public string Directory;
		public string ServerType;
		public string MinecraftVersion;
		public int Port;
		public int MemoryGb;
	}

	private sealed class ManagedServerSession
	{
		public readonly object SyncRoot = new object();
		public ManagedProfileRecord Profile;
		public Process Process;
		public string Status;
		public string Address;
		public DateTime StartedUtc;
		public bool StopRequested;
		public bool RestartEnabled;
		public readonly List<string> Lines = new List<string>();
		public readonly HashSet<string> Players = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public readonly Queue<DateTime> CrashTimes = new Queue<DateTime>();

		public void AddLine(string line)
		{
			lock (SyncRoot)
			{
				Lines.Add(line ?? string.Empty);
				if (Lines.Count > 8000)
				{
					Lines.RemoveRange(0, 1000);
				}
				ParseManagedServerLine(this, line ?? string.Empty);
			}
		}

		public string[] SnapshotLines()
		{
			lock (SyncRoot)
			{
				return Lines.ToArray();
			}
		}
	}

	private sealed class MultiServerDashboardForm : Form
	{
		private readonly string serversRoot;
		private readonly bool mainServerBusy;
		private readonly ListView serverList;
		private readonly Button startButton;
		private readonly Button stopButton;
		private readonly Button consoleButton;
		private readonly Button activateButton;
		private readonly Button refreshButton;
		private readonly Button createButton;
		private readonly Button cloneButton;
		private readonly Button importButton;
		private readonly Button renameButton;
		private readonly Button archiveButton;
		private readonly Button deleteButton;
		private readonly Button trashButton;
		private readonly CheckBox restartBox;
		private readonly Label summaryLabel;
		private readonly Dictionary<string, ManagedServerSession> sessions = new Dictionary<string, ManagedServerSession>(StringComparer.OrdinalIgnoreCase);
		private readonly List<ManagedProfileRecord> profiles = new List<ManagedProfileRecord>();
		private readonly System.Windows.Forms.Timer refreshTimer;
		private bool closingAfterStop;

		public MultiServerDashboardForm(string rootDirectory)
			: this(rootDirectory, false)
		{
		}

		public MultiServerDashboardForm(string rootDirectory, bool mainServerIsBusy)
		{
			ApplyLauncherWindowIcon(this);
			serversRoot = rootDirectory;
			mainServerBusy = mainServerIsBusy;
			bool korean = IsManagedKorean();
			Text = korean ? "서버 관리" : "Server management";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(980, 620);
			Size = new Size(1080, 720);
			Font = new Font("Segoe UI Variable Text", 9.5F);
			AutoScaleMode = AutoScaleMode.Dpi;
			KeyPreview = true;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(24);
			root.ColumnCount = 1;
			root.RowCount = 5;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
			Controls.Add(root);

			Panel header = new Panel();
			header.Dock = DockStyle.Fill;
			root.Controls.Add(header, 0, 0);
			Label heading = new Label();
			heading.Text = korean ? "모든 서버를 한곳에서 관리하세요" : "Manage all servers in one place";
			heading.Font = new Font("Segoe UI Variable Display Semib", 18F);
			heading.AutoSize = true;
			heading.Location = new Point(0, 0);
			header.Controls.Add(heading);
			Label hint = new Label();
			hint.Text = korean ? "서버를 만들고 정리하거나, 선택한 서버를 바로 실행할 수 있습니다." : "Create and organize profiles, or run the selected server directly.";
			hint.AutoSize = true;
			hint.Location = new Point(2, 38);
			header.Controls.Add(hint);

			serverList = new BufferedListView();
			serverList.Dock = DockStyle.Fill;
			serverList.View = View.Details;
			serverList.FullRowSelect = true;
			serverList.HideSelection = false;
			serverList.MultiSelect = false;
			serverList.Columns.Add(korean ? "프로필" : "Profile", 180);
			serverList.Columns.Add(korean ? "상태" : "Status", 110);
			serverList.Columns.Add(korean ? "종류" : "Type", 90);
			serverList.Columns.Add("Minecraft", 96);
			serverList.Columns.Add(korean ? "포트" : "Port", 70);
			serverList.Columns.Add(korean ? "메모리" : "Memory", 72);
			serverList.Columns.Add(korean ? "접속자" : "Players", 70);
			serverList.Columns.Add(korean ? "주소" : "Address", 300);
			serverList.SelectedIndexChanged += delegate { UpdateActions(); };
			serverList.DoubleClick += delegate { OpenSelectedConsole(); };
			root.Controls.Add(serverList, 0, 1);

			summaryLabel = new Label();
			summaryLabel.Dock = DockStyle.Fill;
			summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
			root.Controls.Add(summaryLabel, 0, 2);

			Panel actions = new Panel();
			actions.Dock = DockStyle.Fill;
			root.Controls.Add(actions, 0, 3);
			startButton = NewManagedButton(korean ? "시작" : "Start", 94, "primary");
			ApplyButtonIcon(startButton, ButtonIcon.Play);
			startButton.Location = new Point(0, 7);
			startButton.Click += delegate { StartSelected(); };
			actions.Controls.Add(startButton);
			EnsureButtonContentFits(startButton);
			stopButton = NewManagedButton(korean ? "안전 종료" : "Stop safely", 112, "danger");
			ApplyButtonIcon(stopButton, ButtonIcon.Stop);
			stopButton.Location = new Point(startButton.Right + 8, 7);
			stopButton.Click += delegate { StopSelected(); };
			actions.Controls.Add(stopButton);
			EnsureButtonContentFits(stopButton);
			consoleButton = NewManagedButton(korean ? "콘솔" : "Console", 94, "secondary");
			ApplyButtonIcon(consoleButton, ButtonIcon.Console);
			consoleButton.Location = new Point(stopButton.Right + 8, 7);
			consoleButton.Click += delegate { OpenSelectedConsole(); };
			actions.Controls.Add(consoleButton);
			EnsureButtonContentFits(consoleButton);
			restartBox = new CheckBox();
			restartBox.Text = korean ? "충돌 시 자동 재시작" : "Restart after crash";
			restartBox.AutoSize = true;
			restartBox.Location = new Point(consoleButton.Right + 18, 17);
			actions.Controls.Add(restartBox);

			FlowLayoutPanel profileActions = new FlowLayoutPanel();
			profileActions.Dock = DockStyle.Fill;
			profileActions.FlowDirection = FlowDirection.LeftToRight;
			profileActions.WrapContents = false;
			profileActions.Padding = new Padding(0, 4, 0, 0);
			root.Controls.Add(profileActions, 0, 4);
			createButton = NewManagedButton(korean ? "새 서버" : "New", 92, "primary");
			ApplyButtonIcon(createButton, ButtonIcon.Add);
			createButton.Click += delegate { RunProfileAction(CreateProfile); };
			profileActions.Controls.Add(createButton);
			EnsureButtonContentFits(createButton);
			cloneButton = NewManagedButton(korean ? "복제" : "Clone", 86, "secondary");
			ApplyButtonIcon(cloneButton, ButtonIcon.Copy);
			cloneButton.Click += delegate { RunProfileAction(CloneProfile); };
			profileActions.Controls.Add(cloneButton);
			EnsureButtonContentFits(cloneButton);
			importButton = NewManagedButton(korean ? "가져오기" : "Import", 96, "secondary");
			ApplyButtonIcon(importButton, ButtonIcon.Download);
			importButton.Click += delegate { RunProfileAction(ImportProfile); };
			profileActions.Controls.Add(importButton);
			EnsureButtonContentFits(importButton);
			renameButton = NewManagedButton(korean ? "이름 변경" : "Rename", 100, "secondary");
			ApplyButtonIcon(renameButton, ButtonIcon.Edit);
			renameButton.Click += delegate { RunProfileAction(RenameProfile); };
			profileActions.Controls.Add(renameButton);
			EnsureButtonContentFits(renameButton);
			archiveButton = NewManagedButton(korean ? "보관" : "Archive", 86, "danger");
			ApplyButtonIcon(archiveButton, ButtonIcon.Archive);
			archiveButton.Click += delegate { RunProfileAction(ArchiveProfile); };
			profileActions.Controls.Add(archiveButton);
			EnsureButtonContentFits(archiveButton);
			deleteButton = NewManagedButton(korean ? "삭제" : "Delete", 86, "danger");
			ApplyButtonIcon(deleteButton, ButtonIcon.Trash);
			deleteButton.Click += delegate { RunProfileAction(DeleteProfile); };
			profileActions.Controls.Add(deleteButton);
			EnsureButtonContentFits(deleteButton);
			trashButton = NewManagedButton(korean ? "휴지통" : "Trash", 96, "secondary");
			ApplyButtonIcon(trashButton, ButtonIcon.Trash);
			trashButton.Click += delegate { OpenServerTrash(); };
			profileActions.Controls.Add(trashButton);
			EnsureButtonContentFits(trashButton);
			activateButton = NewManagedButton(korean ? "기본 서버로" : "Set active", 116, "secondary");
			ApplyButtonIcon(activateButton, ButtonIcon.Check);
			activateButton.Click += delegate { ActivateSelected(); };
			profileActions.Controls.Add(activateButton);
			EnsureButtonContentFits(activateButton);
			refreshButton = NewManagedButton(korean ? "새로고침" : "Refresh", 100, "secondary");
			ApplyButtonIcon(refreshButton, ButtonIcon.Refresh);
			refreshButton.Click += delegate { ReloadProfiles(); };
			profileActions.Controls.Add(refreshButton);
			EnsureButtonContentFits(refreshButton);

			refreshTimer = new System.Windows.Forms.Timer();
			refreshTimer.Interval = 1000;
			refreshTimer.Tick += delegate { RenderProfiles(); };
			refreshTimer.Start();
			Shown += delegate { ReloadProfiles(); };
			FormClosing += OnDashboardClosing;
			FormClosed += delegate { refreshTimer.Stop(); };
			ApplySimpleDialogTheme(this);
			ConfigureAccessibleField(serverList, korean ? "서버 목록" : "Server list", korean ? "상태, 버전, 포트와 접속 주소를 확인하고 서버를 선택합니다." : "Review status, version, port, and address, then select a server.");
			ApplyCommonButtonToolTips(this);
		}

		protected override bool ProcessCmdKey(ref Message message, Keys keyData)
		{
			if (keyData == Keys.F5)
			{
				ReloadProfiles();
				return true;
			}
			if (keyData == (Keys.Control | Keys.N) && createButton.Enabled)
			{
				RunProfileAction(CreateProfile);
				return true;
			}
			if (keyData == Keys.Enter && serverList.Focused && consoleButton.Enabled)
			{
				OpenSelectedConsole();
				return true;
			}
			return base.ProcessCmdKey(ref message, keyData);
		}

		public static Button NewManagedButton(string text, int width, string role)
		{
			Button button = new RoundedButton();
			button.Text = text;
			button.Width = width;
			button.Height = 40;
			button.Tag = role;
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			return button;
		}

		private void ReloadProfiles()
		{
			PurgeExpiredServerTrash(serversRoot, DateTime.UtcNow);
			string selected = GetSelectedProfileName();
			if (string.IsNullOrEmpty(selected))
			{
				selected = ReadActiveProfileName(serversRoot);
			}
			profiles.Clear();
			profiles.AddRange(ReadManagedProfiles(serversRoot));
			RenderProfiles();
			if (!string.IsNullOrEmpty(selected))
			{
				SelectProfile(selected);
			}
			if (serverList.SelectedIndices.Count == 0 && serverList.Items.Count > 0)
			{
				serverList.Items[0].Selected = true;
				serverList.Items[0].Focused = true;
			}
		}

		private void RenderProfiles()
		{
			string selected = GetSelectedProfileName();
			string active = ReadActiveProfileName(serversRoot);
			serverList.BeginUpdate();
			try
			{
				while (serverList.Items.Count > profiles.Count)
				{
					serverList.Items.RemoveAt(serverList.Items.Count - 1);
				}
				for (int i = 0; i < profiles.Count; i++)
				{
					ManagedProfileRecord profile = profiles[i];
					ManagedServerSession session;
					sessions.TryGetValue(profile.Name, out session);
					string status = GetManagedStatus(session);
					if (session == null && IsLocalTcpPortListening(profile.Port))
					{
						status = ManagedText("다른 창에서 실행 중", "Running in another window");
					}
					int players = 0;
					string address = GetLocalConnectionAddress(profile.Port);
					if (session != null)
					{
						lock (session.SyncRoot)
						{
							players = session.Players.Count;
							if (!string.IsNullOrEmpty(session.Address))
							{
								address = session.Address;
							}
						}
					}
					string displayName = string.Equals(profile.Name, active, StringComparison.OrdinalIgnoreCase) ? "★ " + profile.Name : profile.Name;
					ListViewItem item = i < serverList.Items.Count ? serverList.Items[i] : null;
					if (item == null || !string.Equals(Convert.ToString(item.Tag), profile.Name, StringComparison.OrdinalIgnoreCase))
					{
						if (item != null)
						{
							serverList.Items.RemoveAt(i);
						}
						item = new ListViewItem();
						item.Tag = profile.Name;
						serverList.Items.Insert(i, item);
					}
					while (item.SubItems.Count < 8) item.SubItems.Add(string.Empty);
					item.Text = displayName;
					item.SubItems[1].Text = status;
					item.SubItems[2].Text = GetServerTypeDisplayName(profile.ServerType);
					item.SubItems[3].Text = profile.MinecraftVersion;
					item.SubItems[4].Text = profile.Port.ToString(CultureInfo.InvariantCulture);
					item.SubItems[5].Text = profile.MemoryGb.ToString(CultureInfo.InvariantCulture) + " GB";
					item.SubItems[6].Text = players.ToString(CultureInfo.InvariantCulture);
					item.SubItems[7].Text = address;
				}
			}
			finally
			{
				serverList.EndUpdate();
			}
			if (!string.IsNullOrEmpty(selected))
			{
				SelectProfile(selected);
			}
			int runningCount = 0;
			int assignedMemory = 0;
			foreach (ManagedServerSession session in sessions.Values)
			{
				if (IsManagedSessionRunning(session))
				{
					runningCount++;
					assignedMemory += session.Profile.MemoryGb;
				}
			}
			summaryLabel.Text = IsManagedKorean()
				? "실행 중 " + runningCount + "개 · 할당 메모리 " + assignedMemory + "GB / 시스템 " + GetTotalPhysicalMemoryGb() + "GB"
				: runningCount + " running · " + assignedMemory + "GB allocated / " + GetTotalPhysicalMemoryGb() + "GB system";
			if (profiles.Count == 0)
			{
				summaryLabel.Text = ManagedText("서버가 없습니다. 새 서버를 눌러 시작하세요.", "No servers yet. Select New to get started.");
			}
			if (mainServerBusy)
			{
				summaryLabel.Text += ManagedText(" · 메인 서버 종료 후 프로필 변경 가능", " · stop the main server to edit profiles");
			}
			UpdateActions();
			summaryLabel.AccessibleName = summaryLabel.Text;
		}

		private void RunProfileAction(Action action)
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				ShowManagedMessage("프로필 작업을 완료하지 못했습니다: " + exception.Message, "Could not complete the profile operation: " + exception.Message, true);
			}
		}

		private void CreateProfile()
		{
			string name = PromptProfileText(this, ManagedText("새 서버 이름", "New server name"), string.Empty);
			if (string.IsNullOrEmpty(name)) return;
			string directory = EnsureNewProfileDirectory(name);
			string previous = ReadActiveProfileName(serversRoot);
			try
			{
				WriteActiveProfileName(serversRoot, name);
				LauncherOptions configured = ConfigureServerPropertiesGui(serversRoot, true);
				RemoveSupersededEmptyProfileDirectory(directory, configured.ServerDirectory);
				int port = FindAvailableServerPort(serversRoot, 25565, configured.ProfileName);
				SetSimplePropertyValue(Path.Combine(configured.ServerDirectory, "server.properties"), "server-port", port.ToString(CultureInfo.InvariantCulture));
				ReloadProfiles();
				SelectProfile(configured.ProfileName);
			}
			catch (OperationCanceledException)
			{
				WriteActiveProfileName(serversRoot, previous);
				RemoveSupersededEmptyProfileDirectory(directory, null);
			}
			catch
			{
				WriteActiveProfileName(serversRoot, previous);
				RemoveSupersededEmptyProfileDirectory(directory, null);
				throw;
			}
		}

		private void CloneProfile()
		{
			ManagedProfileRecord source = GetSelectedProfile();
			if (source == null || !EnsureProfileStopped(source)) return;
			string suffix = IsManagedKorean() ? " 복사본" : " Copy";
			string name = PromptProfileText(this, ManagedText("복제 서버 이름", "Cloned server name"), source.Name + suffix);
			if (string.IsNullOrEmpty(name)) return;
			string destination = EnsureNewProfileDirectory(name);
			try
			{
				CopyProfileDirectory(source.Directory, destination);
				SetSimplePropertyValue(Path.Combine(destination, ".launcher-properties-configured"), "profile-name", name);
				int port = FindAvailableServerPort(serversRoot, source.Port + 1, name);
				SetSimplePropertyValue(Path.Combine(destination, "server.properties"), "server-port", port.ToString(CultureInfo.InvariantCulture));
				WriteActiveProfileName(serversRoot, name);
				ReloadProfiles();
				SelectProfile(name);
				ShowManagedMessage("복제를 완료했습니다. 새 포트는 " + port + "입니다.", "Clone completed on port " + port + ".", false);
			}
			catch
			{
				if (Directory.Exists(destination)) Directory.Delete(destination, true);
				throw;
			}
		}

		private void ImportProfile()
		{
			using (FolderBrowserDialog dialog = new FolderBrowserDialog())
			{
				dialog.Description = ManagedText("가져올 기존 Minecraft 서버 폴더를 선택하세요.", "Select an existing Minecraft server folder.");
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				string name = PromptProfileText(this, ManagedText("가져온 서버 이름", "Imported server name"), new DirectoryInfo(dialog.SelectedPath).Name);
				if (string.IsNullOrEmpty(name)) return;
				string destination = EnsureNewProfileDirectory(name);
				try
				{
					CopyProfileDirectory(dialog.SelectedPath, destination);
					WriteActiveProfileName(serversRoot, name);
					LauncherOptions configured = ConfigureServerPropertiesGui(serversRoot, true);
					MoveImportedProfileToConfiguredDirectory(destination, configured.ServerDirectory);
					int currentPort = ReadConfiguredServerPort(Path.Combine(configured.ServerDirectory, "server.properties"), 25565);
					int port = FindAvailableServerPort(serversRoot, currentPort, configured.ProfileName);
					SetSimplePropertyValue(Path.Combine(configured.ServerDirectory, "server.properties"), "server-port", port.ToString(CultureInfo.InvariantCulture));
					ReloadProfiles();
					SelectProfile(configured.ProfileName);
				}
				catch (OperationCanceledException)
				{
					if (Directory.Exists(destination)) Directory.Delete(destination, true);
				}
				catch
				{
					if (Directory.Exists(destination)) Directory.Delete(destination, true);
					throw;
				}
			}
		}

		private void RenameProfile()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null || !EnsureProfileStopped(profile)) return;
			string name = PromptProfileText(this, ManagedText("새 프로필 이름", "New profile name"), profile.Name);
			if (string.IsNullOrEmpty(name) || string.Equals(name, profile.Name, StringComparison.Ordinal)) return;
			if (!IsValidProfileName(name)) throw new InvalidDataException(ManagedText("프로필 이름은 1~48자로 입력해 주세요.", "Enter a profile name between 1 and 48 characters."));
			string destination = GetProfileDirectory(serversRoot, name);
			EnsureSafeProfilePath(serversRoot, destination);
			if (Directory.Exists(destination)) throw new IOException(ManagedText("같은 이름의 프로필 폴더가 이미 있습니다.", "A profile folder with that name already exists."));
			Directory.Move(profile.Directory, destination);
			SetSimplePropertyValue(Path.Combine(destination, ".launcher-properties-configured"), "profile-name", name);
			if (string.Equals(ReadActiveProfileName(serversRoot), profile.Name, StringComparison.OrdinalIgnoreCase)) WriteActiveProfileName(serversRoot, name);
			sessions.Remove(profile.Name);
			ReloadProfiles();
			SelectProfile(name);
		}

		private void ArchiveProfile()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null || !EnsureProfileStopped(profile)) return;
			if (profiles.Count <= 1)
			{
				ShowManagedMessage("마지막 프로필은 보관할 수 없습니다.", "The last profile cannot be archived.", true);
				return;
			}
			if (MessageBox.Show(this, ManagedText("이 서버를 삭제하지 않고 보관 폴더로 옮길까요?", "Move this server to the archive without deleting it?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
			string archiveRoot = Path.Combine(serversRoot, "servers-archive");
			Directory.CreateDirectory(archiveRoot);
			string destination = Path.Combine(archiveRoot, ToSafeDirectoryName(profile.Name) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
			EnsurePathInsideRoot(archiveRoot, destination);
			Directory.Move(profile.Directory, destination);
			sessions.Remove(profile.Name);
			if (string.Equals(ReadActiveProfileName(serversRoot), profile.Name, StringComparison.OrdinalIgnoreCase))
			{
				string replacement = string.Equals(profiles[0].Name, profile.Name, StringComparison.OrdinalIgnoreCase) ? profiles[1].Name : profiles[0].Name;
				WriteActiveProfileName(serversRoot, replacement);
			}
			ReloadProfiles();
		}

		private void DeleteProfile()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null || !EnsureProfileStopped(profile)) return;
			MessageBox.Show(this, ManagedText("서버 폴더 전체(월드, 플러그인, 모드, 설정)를 휴지통으로 옮기며 30일 동안 복구할 수 있습니다. 별도 백업 폴더는 그대로 유지됩니다. 계속하려면 다음 창에 서버 이름을 입력하세요.\r\n\r\n" + profile.Name, "The entire server folder (worlds, plugins, mods, and settings) will move to Trash and can be restored for 30 days. Separate backups are kept. Enter the server name in the next window to continue.\r\n\r\n" + profile.Name), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			string confirmation = PromptProfileText(this, ManagedText("서버 삭제 확인", "Confirm server deletion"), string.Empty);
			if (!string.Equals(confirmation, profile.Name, StringComparison.Ordinal))
			{
				if (confirmation != null) ShowManagedMessage("서버 이름이 일치하지 않습니다.", "The server name does not match.", true);
				return;
			}
			MoveProfileToServerTrash(serversRoot, profile, DateTime.UtcNow);
			sessions.Remove(profile.Name);
			UpdateActiveProfileAfterRemoval(serversRoot, profiles, profile.Name);
			ReloadProfiles();
		}

		private void OpenServerTrash()
		{
			using (ServerTrashForm form = new ServerTrashForm(serversRoot)) form.ShowDialog(this);
			ReloadProfiles();
		}

		private bool EnsureProfileStopped(ManagedProfileRecord profile)
		{
			ManagedServerSession session;
			if ((sessions.TryGetValue(profile.Name, out session) && IsManagedSessionRunning(session)) || IsLocalTcpPortListening(profile.Port))
			{
				ShowManagedMessage("실행 중인 서버는 변경할 수 없습니다. 안전하게 종료한 뒤 다시 시도해 주세요.", "A running server cannot be changed. Stop it safely and try again.", true);
				return false;
			}
			return true;
		}

		private string EnsureNewProfileDirectory(string name)
		{
			if (!IsValidProfileName(name)) throw new InvalidDataException(ManagedText("프로필 이름은 1~48자로 입력해 주세요.", "Enter a profile name between 1 and 48 characters."));
			string directory = GetProfileDirectory(serversRoot, name);
			EnsureSafeProfilePath(serversRoot, directory);
			if (Directory.Exists(directory)) throw new IOException(ManagedText("같은 이름의 프로필 폴더가 이미 있습니다.", "A profile folder with that name already exists."));
			Directory.CreateDirectory(directory);
			return directory;
		}

		private static void RemoveSupersededEmptyProfileDirectory(string originalDirectory, string configuredDirectory)
		{
			if (!Directory.Exists(originalDirectory)) return;
			if (!string.IsNullOrEmpty(configuredDirectory) && string.Equals(Path.GetFullPath(originalDirectory), Path.GetFullPath(configuredDirectory), StringComparison.OrdinalIgnoreCase)) return;
			if (Directory.GetFileSystemEntries(originalDirectory).Length == 0) Directory.Delete(originalDirectory);
		}

		private static void MoveImportedProfileToConfiguredDirectory(string importedDirectory, string configuredDirectory)
		{
			if (!Directory.Exists(importedDirectory) || string.Equals(Path.GetFullPath(importedDirectory), Path.GetFullPath(configuredDirectory), StringComparison.OrdinalIgnoreCase)) return;
			string configuredProperties = File.ReadAllText(Path.Combine(configuredDirectory, "server.properties"), Encoding.UTF8);
			string configuredMarker = File.ReadAllText(Path.Combine(configuredDirectory, ".launcher-properties-configured"), Encoding.UTF8);
			Directory.Delete(configuredDirectory, true);
			Directory.Move(importedDirectory, configuredDirectory);
			File.WriteAllText(Path.Combine(configuredDirectory, "server.properties"), configuredProperties, new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(configuredDirectory, ".launcher-properties-configured"), configuredMarker, new UTF8Encoding(false));
		}

		private void StartSelected()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile != null)
			{
				StartSession(profile, false);
			}
		}

		private void StartSession(ManagedProfileRecord profile, bool automaticRestart)
		{
			ManagedServerSession existing;
			if (sessions.TryGetValue(profile.Name, out existing) && IsManagedSessionRunning(existing))
			{
				return;
			}
			if (!File.Exists(Path.Combine(profile.Directory, ".launcher-properties-configured")))
			{
				ShowManagedMessage("이 프로필은 설정을 먼저 완료해야 합니다.", "Complete this profile's setup first.", true);
				return;
			}
			if (IsPortUsedByManagedSession(profile.Port, profile.Name) || IsLocalTcpPortListening(profile.Port))
			{
				ShowManagedMessage("포트 " + profile.Port + "을(를) 다른 프로그램이나 서버가 사용 중입니다.", "Port " + profile.Port + " is already in use.", true);
				return;
			}
			int usedMemory = GetRunningManagedMemory(profile.Name);
			int safeMaximum = GetSafeMemoryMaximumGb();
			if (checked(usedMemory + profile.MemoryGb) > safeMaximum)
			{
				ShowManagedMessage("동시에 실행할 서버의 메모리 합계가 안전 상한 " + safeMaximum + "GB를 넘습니다.", "Combined server memory exceeds the safe limit of " + safeMaximum + "GB.", true);
				return;
			}
			string eulaPath = Path.Combine(profile.Directory, "eula.txt");
			if (!EulaIsAccepted(eulaPath))
			{
				if (!AskForEulaGui())
				{
					return;
				}
				File.WriteAllText(eulaPath, "eula=true\r\n", new UTF8Encoding(false));
			}

			ManagedServerSession session = existing ?? new ManagedServerSession();
			session.Profile = profile;
			session.Status = automaticRestart ? ManagedText("재시작 중", "Restarting") : ManagedText("시작 중", "Starting");
			session.Address = GetLocalConnectionAddress(profile.Port);
			session.StartedUtc = DateTime.UtcNow;
			session.StopRequested = false;
			session.RestartEnabled = restartBox.Checked;
			session.Players.Clear();
			session.AddLine("[Launcher] " + session.Status + ": " + profile.Name);
			sessions[profile.Name] = session;
		try
		{
				ProcessStartInfo startInfo = new ProcessStartInfo();
				startInfo.FileName = AssemblyLocation();
				startInfo.Arguments = "--managed-profile " + QuoteCommandLineArgument(profile.Name);
				startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
				startInfo.UseShellExecute = false;
				startInfo.CreateNoWindow = true;
				startInfo.RedirectStandardInput = true;
				startInfo.RedirectStandardOutput = true;
				startInfo.RedirectStandardError = true;
				Process process = new Process();
				process.StartInfo = startInfo;
				process.EnableRaisingEvents = true;
				process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
				{
					if (eventArgs.Data != null)
					{
						session.AddLine(eventArgs.Data);
					}
				};
				process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
				{
					if (eventArgs.Data != null)
					{
						session.AddLine(eventArgs.Data);
					}
				};
				process.Exited += delegate { OnManagedSessionExited(session); };
				if (!process.Start())
				{
					throw new InvalidOperationException("관리 서버 프로세스를 시작하지 못했습니다.");
				}
				session.Process = process;
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
			}
			catch (Exception exception)
			{
				session.Status = ManagedText("시작 실패", "Start failed");
				session.AddLine("[Launcher] " + exception.Message);
				ShowManagedMessage("서버를 시작하지 못했습니다: " + exception.Message, "Could not start server: " + exception.Message, true);
			}
			RenderProfiles();
		}

		private void OnManagedSessionExited(ManagedServerSession session)
		{
			int exitCode = -1;
			try
			{
				exitCode = session.Process.ExitCode;
			}
			catch
			{
			}
			session.AddLine("[Launcher] process-exit=" + exitCode.ToString(CultureInfo.InvariantCulture));
			bool restart;
			lock (session.SyncRoot)
			{
				session.Status = session.StopRequested || exitCode == 0 ? ManagedText("꺼짐", "Stopped") : ManagedText("충돌", "Crashed");
				session.Players.Clear();
				restart = !session.StopRequested && exitCode != 0 && session.RestartEnabled;
				if (restart)
				{
					DateTime now = DateTime.UtcNow;
					while (session.CrashTimes.Count > 0 && now - session.CrashTimes.Peek() > TimeSpan.FromMinutes(10))
					{
						session.CrashTimes.Dequeue();
					}
					session.CrashTimes.Enqueue(now);
					if (session.CrashTimes.Count >= 3)
					{
						restart = false;
						session.Status = ManagedText("반복 충돌로 중단", "Stopped after crash loop");
						session.AddLine("[Launcher] " + ManagedText("10분 안에 3회 충돌하여 자동 재시작을 중단했습니다.", "Automatic restart stopped after 3 crashes in 10 minutes."));
					}
				}
			}
			if (!IsDisposed)
			{
				try
				{
					TryPostToUi(this, (MethodInvoker)delegate { RenderProfiles(); });
				}
				catch
				{
				}
			}
			if (restart)
			{
				Thread thread = new Thread((ThreadStart)delegate
				{
					Thread.Sleep(5000);
					if (!IsDisposed)
					{
						try
						{
							TryPostToUi(this, (MethodInvoker)delegate { StartSession(session.Profile, true); });
						}
						catch
						{
						}
					}
				});
				thread.IsBackground = true;
				thread.Name = "서버 충돌 재시작 대기";
				thread.Start();
			}
		}

		private void StopSelected()
		{
			ManagedServerSession session = GetSelectedSession();
			if (session == null || !IsManagedSessionRunning(session))
			{
				return;
			}
			session.StopRequested = true;
			session.Status = ManagedText("안전 종료 중", "Stopping safely");
			try
			{
				session.Process.StandardInput.WriteLine("stop");
				session.Process.StandardInput.Flush();
			}
			catch (Exception exception)
			{
				ShowManagedMessage("종료 명령을 보내지 못했습니다: " + exception.Message, "Could not send stop command: " + exception.Message, true);
			}
			RenderProfiles();
		}

		private void OpenSelectedConsole()
		{
			ManagedServerSession session = GetSelectedSession();
			if (session == null)
			{
				return;
			}
			using (ManagedConsoleForm form = new ManagedConsoleForm(session))
			{
				form.ShowDialog(this);
			}
		}

		private void ActivateSelected()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null)
			{
				return;
			}
			WriteActiveProfileName(serversRoot, profile.Name);
			RenderProfiles();
			ShowManagedMessage("기본 프로필을 '" + profile.Name + "'(으)로 바꿨습니다.", "Active profile changed to '" + profile.Name + "'.", false);
		}

		private void OnDashboardClosing(object sender, FormClosingEventArgs eventArgs)
		{
			if (closingAfterStop || !HasRunningManagedSessions())
			{
				return;
			}
			DialogResult result = MessageBox.Show(this,
				ManagedText("실행 중인 서버를 모두 안전하게 종료한 뒤 창을 닫을까요?", "Stop all running servers safely before closing?"),
				ManagedText("멀티 서버 종료", "Close multi-server dashboard"),
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Question);
			if (result != DialogResult.Yes)
			{
				eventArgs.Cancel = true;
				return;
			}
			eventArgs.Cancel = true;
			closingAfterStop = true;
			foreach (ManagedServerSession session in sessions.Values)
			{
				if (IsManagedSessionRunning(session))
				{
					session.StopRequested = true;
					try
					{
						session.Process.StandardInput.WriteLine("stop");
						session.Process.StandardInput.Flush();
					}
					catch
					{
					}
				}
			}
			Thread waiter = new Thread((ThreadStart)delegate
			{
				DateTime deadline = DateTime.UtcNow.AddSeconds(30.0);
				while (DateTime.UtcNow < deadline && HasRunningManagedSessions())
				{
					Thread.Sleep(250);
				}
				if (!IsDisposed)
				{
					TryPostToUi(this, (MethodInvoker)delegate
					{
						if (HasRunningManagedSessions())
						{
							closingAfterStop = false;
							ShowManagedMessage("30초 안에 종료되지 않은 서버가 있습니다. 콘솔을 확인한 뒤 다시 시도해 주세요.", "A server did not stop within 30 seconds. Check its console and try again.", true);
						}
						else
						{
							FormClosing -= OnDashboardClosing;
							Close();
						}
					});
				}
			});
			waiter.IsBackground = true;
			waiter.Name = "멀티 서버 안전 종료 대기";
			waiter.Start();
		}

		private bool HasRunningManagedSessions()
		{
			foreach (ManagedServerSession session in sessions.Values)
			{
				if (IsManagedSessionRunning(session))
				{
					return true;
				}
			}
			return false;
		}

		private bool IsPortUsedByManagedSession(int port, string exceptProfile)
		{
			foreach (ManagedServerSession session in sessions.Values)
			{
				if (!string.Equals(session.Profile.Name, exceptProfile, StringComparison.OrdinalIgnoreCase) && session.Profile.Port == port && IsManagedSessionRunning(session))
				{
					return true;
				}
			}
			return false;
		}

		private int GetRunningManagedMemory(string exceptProfile)
		{
			int total = 0;
			foreach (ManagedServerSession session in sessions.Values)
			{
				if (!string.Equals(session.Profile.Name, exceptProfile, StringComparison.OrdinalIgnoreCase) && IsManagedSessionRunning(session))
				{
					total = checked(total + session.Profile.MemoryGb);
				}
			}
			return total;
		}

		private ManagedProfileRecord GetSelectedProfile()
		{
			string name = GetSelectedProfileName();
			for (int i = 0; i < profiles.Count; i++)
			{
				if (string.Equals(profiles[i].Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return profiles[i];
				}
			}
			return null;
		}

		private ManagedServerSession GetSelectedSession()
		{
			ManagedServerSession session;
			return sessions.TryGetValue(GetSelectedProfileName() ?? string.Empty, out session) ? session : null;
		}

		private string GetSelectedProfileName()
		{
			return serverList.SelectedItems.Count == 1 ? Convert.ToString(serverList.SelectedItems[0].Tag, CultureInfo.InvariantCulture) : null;
		}

		private void SelectProfile(string name)
		{
			foreach (ListViewItem item in serverList.Items)
			{
				if (string.Equals(Convert.ToString(item.Tag, CultureInfo.InvariantCulture), name, StringComparison.OrdinalIgnoreCase))
				{
					item.Selected = true;
					item.Focused = true;
					item.EnsureVisible();
					break;
				}
			}
		}

		private void UpdateActions()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			ManagedServerSession session = GetSelectedSession();
			bool running = IsManagedSessionRunning(session);
			bool runningElsewhere = profile != null && !running && IsLocalTcpPortListening(profile.Port);
			startButton.Enabled = profile != null && !running && !runningElsewhere;
			stopButton.Enabled = running;
			consoleButton.Enabled = session != null;
			createButton.Enabled = !mainServerBusy;
			importButton.Enabled = !mainServerBusy;
			activateButton.Enabled = profile != null && !mainServerBusy;
			cloneButton.Enabled = profile != null && !mainServerBusy && !running && !runningElsewhere;
			renameButton.Enabled = profile != null && !mainServerBusy && !running && !runningElsewhere;
			archiveButton.Enabled = profile != null && !mainServerBusy && !running && !runningElsewhere && profiles.Count > 1;
			deleteButton.Enabled = profile != null && !mainServerBusy && !running && !runningElsewhere;
			trashButton.Enabled = !mainServerBusy;
		}

		private void ShowManagedMessage(string korean, string english, bool warning)
		{
			MessageBox.Show(this, IsManagedKorean() ? korean : english, Text, MessageBoxButtons.OK, warning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
		}
	}

	private sealed class ManagedConsoleForm : Form
	{
		private readonly ManagedServerSession session;
		private readonly RichTextBox outputBox;
		private readonly TextBox commandBox;
		private readonly TextBox searchBox;
		private readonly ComboBox filterBox;
		private readonly System.Windows.Forms.Timer timer;
		private int renderedHash;

		public ManagedConsoleForm(ManagedServerSession managedSession)
		{
			ApplyLauncherWindowIcon(this);
			session = managedSession;
			Text = session.Profile.Name + " · " + ManagedText("콘솔", "Console");
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(760, 500);
			Size = new Size(900, 650);
			Font = new Font("Segoe UI Variable Text", 9.5F);

			Panel toolbar = new Panel();
			toolbar.Dock = DockStyle.Top;
			toolbar.Height = 44;
			toolbar.Padding = new Padding(8, 7, 8, 5);
			Controls.Add(toolbar);
			searchBox = new TextBox();
			searchBox.Dock = DockStyle.Fill;
			searchBox.TextChanged += delegate { RenderConsole(); };
			toolbar.Controls.Add(searchBox);
			filterBox = new ModernComboBox();
			filterBox.DropDownStyle = ComboBoxStyle.DropDownList;
			filterBox.Width = 144;
			filterBox.Dock = DockStyle.Right;
			filterBox.Items.AddRange(new object[4] { ManagedText("전체", "All"), ManagedText("일반 경고", "Warnings"), ManagedText("호환성", "Compatibility"), ManagedText("오류", "Errors") });
			filterBox.SelectedIndex = 0;
			filterBox.SelectedIndexChanged += delegate { RenderConsole(); };
			toolbar.Controls.Add(filterBox);

			outputBox = new RichTextBox();
			outputBox.Dock = DockStyle.Fill;
			outputBox.ReadOnly = true;
			outputBox.Font = new Font("Consolas", 9F);
			outputBox.WordWrap = false;
			outputBox.DetectUrls = true;
			Controls.Add(outputBox);

			Panel commandPanel = new Panel();
			commandPanel.Dock = DockStyle.Bottom;
			commandPanel.Height = 48;
			commandPanel.Padding = new Padding(8);
			Controls.Add(commandPanel);
			commandBox = new TextBox();
			commandBox.Dock = DockStyle.Fill;
			commandBox.KeyDown += delegate(object sender, KeyEventArgs eventArgs)
			{
				if (eventArgs.KeyCode == Keys.Enter)
				{
					SendManagedCommand();
					eventArgs.SuppressKeyPress = true;
				}
			};
			commandPanel.Controls.Add(commandBox);
			Button send = MultiServerDashboardForm.NewManagedButton(ManagedText("전송", "Send"), 90, "primary");
			send.Dock = DockStyle.Right;
			send.Click += delegate { SendManagedCommand(); };
			commandPanel.Controls.Add(send);
			EnsureButtonContentFits(send);

			timer = new System.Windows.Forms.Timer();
			timer.Interval = 500;
			timer.Tick += delegate { RenderConsoleIfChanged(); };
			timer.Start();
			FormClosed += delegate { timer.Stop(); };
			Shown += delegate { RenderConsole(); };
			ApplySimpleDialogTheme(this);
			ApplyCommonButtonToolTips(this);
			ThemePalette palette = ThemePalette.Create(launcherForm != null && launcherForm.UsesDarkTheme);
			outputBox.BackColor = palette.Console;
			outputBox.ForeColor = Color.FromArgb(215, 225, 235);
		}

		private void RenderConsoleIfChanged()
		{
			string[] lines = session.SnapshotLines();
			int hash = lines.Length == 0 ? 0 : lines.Length * 397 ^ lines[lines.Length - 1].GetHashCode();
			if (hash != renderedHash)
			{
				RenderConsole(lines);
			}
			commandBox.Enabled = IsManagedSessionRunning(session);
		}

		private void RenderConsole()
		{
			RenderConsole(session.SnapshotLines());
		}

		private void RenderConsole(string[] lines)
		{
			string search = searchBox.Text.Trim();
			int filterIndex = filterBox.SelectedIndex;
			List<string> visibleLines = new List<string>();
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i] ?? string.Empty;
				if (!ConsoleLineMatchesFilter(line, filterIndex))
				{
					continue;
				}
				if (!string.IsNullOrEmpty(search) && line.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				visibleLines.Add(line);
			}
			RichTextUpdateState state = BeginStableRichTextUpdate(outputBox);
			try
			{
				outputBox.Clear();
				for (int i = 0; i < visibleLines.Count; i++)
				{
					AppendManagedConsoleLine(visibleLines[i]);
				}
			}
			finally
			{
				EndStableRichTextUpdate(outputBox, state);
			}
			renderedHash = lines.Length == 0 ? 0 : lines.Length * 397 ^ lines[lines.Length - 1].GetHashCode();
		}

		private void AppendManagedConsoleLine(string line)
		{
			ConsoleLineKind kind = ClassifyConsoleLine(line);
			Color color = Color.FromArgb(215, 225, 235);
			if (kind == ConsoleLineKind.Error) color = Color.FromArgb(255, 117, 117);
			else if (kind == ConsoleLineKind.Warning) color = Color.FromArgb(255, 190, 92);
			else if (kind == ConsoleLineKind.Compatibility) color = Color.FromArgb(112, 184, 255);
			outputBox.SelectionColor = color;
			outputBox.AppendText(line + Environment.NewLine);
		}

		private void SendManagedCommand()
		{
			string command = commandBox.Text.Trim();
			if (command.Length == 0 || !IsManagedSessionRunning(session))
			{
				return;
			}
			try
			{
				session.Process.StandardInput.WriteLine(command);
				session.Process.StandardInput.Flush();
				commandBox.Clear();
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}
	}

	private static bool TryRunManagedProfileMode(string[] args, out int exitCode)
	{
		exitCode = 0;
		if (args == null || args.Length != 2 || !string.Equals(args[0], "--managed-profile", StringComparison.Ordinal))
		{
			return false;
		}
		string profile = args[1] == null ? string.Empty : args[1].Trim();
		if (!IsValidProfileName(profile))
		{
			exitCode = 2;
			return true;
		}
		ManagedChildMode = true;
		ManagedProfileOverride = profile;
		StartManagedChildInputRelay();
		exitCode = Run();
		return true;
	}

	private static void StartManagedChildInputRelay()
	{
		Thread relay = new Thread((ThreadStart)delegate
		{
			while (ManagedChildMode)
			{
				string line;
				try
				{
					line = Console.ReadLine();
				}
				catch
				{
					break;
				}
				if (line == null)
				{
					break;
				}
				SendServerCommand(line);
			}
		});
		relay.IsBackground = true;
		relay.Name = "관리 서버 명령 전달";
		relay.Start();
	}

	private static List<ManagedProfileRecord> ReadManagedProfiles(string serversRoot)
	{
		List<ManagedProfileRecord> result = new List<ManagedProfileRecord>();
		string serversDirectory = Path.Combine(serversRoot, "servers");
		if (!Directory.Exists(serversDirectory))
		{
			return result;
		}
		string rootFull = Path.GetFullPath(serversDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string[] directories = Directory.GetDirectories(serversDirectory, "*", SearchOption.TopDirectoryOnly);
		for (int i = 0; i < directories.Length; i++)
		{
			DirectoryInfo info = new DirectoryInfo(directories[i]);
			string full = Path.GetFullPath(info.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || (info.Attributes & FileAttributes.ReparsePoint) != 0)
			{
				continue;
			}
			Dictionary<string, string> values = ReadSimpleProperties(Path.Combine(info.FullName, ".launcher-properties-configured"));
			string name = values.ContainsKey("profile-name") && IsValidProfileName(values["profile-name"]) ? values["profile-name"] : info.Name;
			int memory;
			if (!values.ContainsKey("memory-gb") || !int.TryParse(values["memory-gb"], out memory) || memory < 2)
			{
				memory = ChooseMaximumMemoryGb();
			}
			ManagedProfileRecord record = new ManagedProfileRecord();
			record.Name = name;
			record.Directory = info.FullName;
			record.ServerType = values.ContainsKey("server-type") ? NormalizeServerType(values["server-type"]) : "paper";
			record.MinecraftVersion = values.ContainsKey("minecraft-version") ? values["minecraft-version"] : "26.2";
			record.Port = ReadConfiguredServerPort(Path.Combine(info.FullName, "server.properties"), 25565);
			record.MemoryGb = memory;
			result.Add(record);
		}
		result.Sort(delegate(ManagedProfileRecord left, ManagedProfileRecord right) { return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase); });
		return result;
	}

	private static void ParseManagedServerLine(ManagedServerSession session, string line)
	{
		if (line.IndexOf("Done (", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("For help, type \"help\"", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			session.Status = ManagedText("온라인", "Online");
		}
		if (line.IndexOf("Stopping server", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			session.Status = ManagedText("안전 종료 중", "Stopping safely");
		}
		Match success = Regex.Match(line, @"\[외부 접속 확인\]\s*성공:\s*외부에서\s+([^\s]+)에\s+접속", RegexOptions.IgnoreCase);
		if (success.Success)
		{
			session.Address = success.Groups[1].Value;
		}
		Match joined = Regex.Match(line, @"\b([A-Za-z0-9_]{3,16}) joined the game\b", RegexOptions.IgnoreCase);
		if (joined.Success)
		{
			session.Players.Add(joined.Groups[1].Value);
		}
		Match left = Regex.Match(line, @"\b([A-Za-z0-9_]{3,16}) left the game\b", RegexOptions.IgnoreCase);
		if (left.Success)
		{
			session.Players.Remove(left.Groups[1].Value);
		}
	}

	private static bool IsManagedSessionRunning(ManagedServerSession session)
	{
		if (session == null || session.Process == null)
		{
			return false;
		}
		try
		{
			return !session.Process.HasExited;
		}
		catch
		{
			return false;
		}
	}

	private static string GetManagedStatus(ManagedServerSession session)
	{
		if (session == null)
		{
			return ManagedText("꺼짐", "Stopped");
		}
		lock (session.SyncRoot)
		{
			return string.IsNullOrEmpty(session.Status) ? ManagedText("꺼짐", "Stopped") : session.Status;
		}
	}

	private static string ManagedText(string korean, string english)
	{
		return IsManagedKorean() ? korean : english;
	}

	private static bool IsManagedKorean()
	{
		return string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
	}

	private static string AssemblyLocation()
	{
		return System.Reflection.Assembly.GetExecutingAssembly().Location;
	}
}
