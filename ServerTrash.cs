using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static partial class Launcher
{
	private const int ServerTrashRetentionDays = 30;
	private const string ServerTrashDirectoryName = "servers-trash";
	private const string ServerTrashMetadataFileName = ".mineharbor-trash.json";

	private sealed class ServerTrashRecord
	{
		public string ProfileName;
		public string Directory;
		public DateTime DeletedUtc;
		public DateTime ExpiresUtc;
	}

	private static string GetServerTrashRoot(string serversRoot)
	{
		return Path.Combine(Path.GetFullPath(serversRoot), ServerTrashDirectoryName);
	}

	private static ServerTrashRecord MoveProfileToServerTrash(string serversRoot, ManagedProfileRecord profile, DateTime utcNow)
	{
		if (profile == null || !IsValidProfileName(profile.Name)) throw new InvalidDataException("삭제할 서버 프로필 정보가 올바르지 않습니다.");
		EnsureSafeProfilePath(serversRoot, profile.Directory);
		if (!Directory.Exists(profile.Directory)) throw new DirectoryNotFoundException("삭제할 서버 폴더를 찾을 수 없습니다.");
		string trashRoot = GetServerTrashRoot(serversRoot);
		Directory.CreateDirectory(trashRoot);
		string folderName = ToSafeDirectoryName(profile.Name) + "-" + utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		string destination = Path.Combine(trashRoot, folderName);
		EnsurePathInsideRoot(trashRoot, destination);
		ServerTrashRecord record = new ServerTrashRecord();
		record.ProfileName = profile.Name;
		record.Directory = destination;
		record.DeletedUtc = utcNow;
		record.ExpiresUtc = utcNow.AddDays(ServerTrashRetentionDays);
		Directory.Move(profile.Directory, destination);
		try
		{
			WriteServerTrashMetadata(record);
		}
		catch
		{
			if (Directory.Exists(destination) && !Directory.Exists(profile.Directory)) Directory.Move(destination, profile.Directory);
			throw;
		}
		return record;
	}

	private static void WriteServerTrashMetadata(ServerTrashRecord record)
	{
		Dictionary<string, object> metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		metadata["profile_name"] = record.ProfileName;
		metadata["deleted_utc"] = record.DeletedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
		metadata["expires_utc"] = record.ExpiresUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
		string json = new JavaScriptSerializer().Serialize(metadata);
		string path = Path.Combine(record.Directory, ServerTrashMetadataFileName);
		string temporary = path + ".준비중";
		File.WriteAllText(temporary, json, new UTF8Encoding(false));
		ReplaceFile(temporary, path);
	}

	private static List<ServerTrashRecord> ReadServerTrashRecords(string serversRoot)
	{
		List<ServerTrashRecord> records = new List<ServerTrashRecord>();
		string trashRoot = GetServerTrashRoot(serversRoot);
		if (!Directory.Exists(trashRoot)) return records;
		string[] directories = Directory.GetDirectories(trashRoot, "*", SearchOption.TopDirectoryOnly);
		for (int i = 0; i < directories.Length; i++)
		{
			ServerTrashRecord record;
			if (TryReadServerTrashRecord(trashRoot, directories[i], out record)) records.Add(record);
		}
		records.Sort(delegate(ServerTrashRecord left, ServerTrashRecord right) { return right.DeletedUtc.CompareTo(left.DeletedUtc); });
		return records;
	}

	private static bool TryReadServerTrashRecord(string trashRoot, string directory, out ServerTrashRecord record)
	{
		record = null;
		try
		{
			EnsurePathInsideRoot(trashRoot, directory);
			DirectoryInfo info = new DirectoryInfo(directory);
			if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
			string metadataPath = Path.Combine(info.FullName, ServerTrashMetadataFileName);
			if (!File.Exists(metadataPath)) return false;
			Dictionary<string, object> metadata = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(metadataPath, Encoding.UTF8)) as Dictionary<string, object>;
			if (metadata == null || !metadata.ContainsKey("profile_name") || !metadata.ContainsKey("deleted_utc") || !metadata.ContainsKey("expires_utc")) return false;
			string profileName = Convert.ToString(metadata["profile_name"], CultureInfo.InvariantCulture);
			DateTime deletedUtc;
			DateTime expiresUtc;
			if (!IsValidProfileName(profileName) || !DateTime.TryParse(Convert.ToString(metadata["deleted_utc"], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out deletedUtc) || !DateTime.TryParse(Convert.ToString(metadata["expires_utc"], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expiresUtc)) return false;
			if (expiresUtc.ToUniversalTime() < deletedUtc.ToUniversalTime() || expiresUtc.ToUniversalTime() > deletedUtc.ToUniversalTime().AddDays(ServerTrashRetentionDays).AddMinutes(1)) return false;
			record = new ServerTrashRecord();
			record.ProfileName = profileName;
			record.Directory = info.FullName;
			record.DeletedUtc = deletedUtc.ToUniversalTime();
			record.ExpiresUtc = expiresUtc.ToUniversalTime();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static int PurgeExpiredServerTrash(string serversRoot, DateTime utcNow)
	{
		List<ServerTrashRecord> records = ReadServerTrashRecords(serversRoot);
		int removed = 0;
		for (int i = 0; i < records.Count; i++)
		{
			if (records[i].ExpiresUtc > utcNow.ToUniversalTime()) continue;
			try
			{
				PermanentlyDeleteServerTrashRecord(serversRoot, records[i]);
				removed++;
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
		return removed;
	}

	private static void RestoreServerTrashRecord(string serversRoot, ServerTrashRecord record, string restoreName)
	{
		if (record == null || !IsValidProfileName(restoreName)) throw new InvalidDataException("복구할 서버 이름이 올바르지 않습니다.");
		string trashRoot = GetServerTrashRoot(serversRoot);
		EnsurePathInsideRoot(trashRoot, record.Directory);
		string destination = GetProfileDirectory(serversRoot, restoreName);
		EnsureSafeProfilePath(serversRoot, destination);
		if (Directory.Exists(destination)) throw new IOException("같은 이름의 서버가 이미 있습니다.");
		Directory.Move(record.Directory, destination);
		try
		{
			string metadataPath = Path.Combine(destination, ServerTrashMetadataFileName);
			if (File.Exists(metadataPath)) File.Delete(metadataPath);
			SetSimplePropertyValue(Path.Combine(destination, ".launcher-properties-configured"), "profile-name", restoreName);
		}
		catch
		{
			if (Directory.Exists(destination) && !Directory.Exists(record.Directory))
			{
				Directory.Move(destination, record.Directory);
				WriteServerTrashMetadata(record);
			}
			throw;
		}
	}

	private static void PermanentlyDeleteServerTrashRecord(string serversRoot, ServerTrashRecord record)
	{
		if (record == null) throw new ArgumentNullException("record");
		string trashRoot = GetServerTrashRoot(serversRoot);
		EnsurePathInsideRoot(trashRoot, record.Directory);
		DirectoryInfo info = new DirectoryInfo(record.Directory);
		if (!info.Exists) return;
		if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("연결된 폴더는 영구 삭제할 수 없습니다.");
		DeleteDirectoryTreeWithoutFollowingLinks(info, 0);
	}

	private static void DeleteDirectoryTreeWithoutFollowingLinks(DirectoryInfo directory, int depth)
	{
		if (depth > 128) throw new InvalidDataException("삭제할 서버 폴더 단계가 지나치게 깊습니다.");
		FileInfo[] files = directory.GetFiles();
		for (int i = 0; i < files.Length; i++)
		{
			if ((files[i].Attributes & FileAttributes.ReadOnly) != 0) files[i].Attributes &= ~FileAttributes.ReadOnly;
			files[i].Delete();
		}
		DirectoryInfo[] children = directory.GetDirectories();
		for (int i = 0; i < children.Length; i++)
		{
			// 연결점과 심볼릭 링크는 대상 경로를 따라가지 않고 링크 자체만 제거합니다.
			if ((children[i].Attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(children[i].FullName, false);
			else DeleteDirectoryTreeWithoutFollowingLinks(children[i], depth + 1);
		}
		directory.Delete(false);
	}

	private static string UpdateActiveProfileAfterRemoval(string serversRoot, IList<ManagedProfileRecord> profiles, string removedProfileName)
	{
		if (!string.Equals(ReadActiveProfileName(serversRoot), removedProfileName, StringComparison.OrdinalIgnoreCase)) return ReadActiveProfileName(serversRoot);
		for (int i = 0; i < profiles.Count; i++)
		{
			if (!string.Equals(profiles[i].Name, removedProfileName, StringComparison.OrdinalIgnoreCase))
			{
				WriteActiveProfileName(serversRoot, profiles[i].Name);
				return profiles[i].Name;
			}
		}
		string activePath = Path.Combine(serversRoot, ".active-server-profile");
		if (File.Exists(activePath)) File.Delete(activePath);
		return null;
	}

	private sealed class ServerTrashForm : Form
	{
		private readonly string serversRoot;
		private readonly ListView trashList;
		private readonly Label statusLabel;
		private readonly Button restoreButton;
		private readonly Button deleteButton;
		private readonly List<ServerTrashRecord> records = new List<ServerTrashRecord>();

		public ServerTrashForm(string rootDirectory)
		{
			ApplyLauncherWindowIcon(this);
			serversRoot = Path.GetFullPath(rootDirectory);
			bool korean = IsBackupKorean();
			Text = korean ? "서버 휴지통" : "Server trash";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(760, 500);
			Size = new Size(860, 580);
			Font = new Font("Segoe UI Variable Text", 9.5F);
			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(24);
			root.ColumnCount = 1;
			root.RowCount = 4;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
			Controls.Add(root);
			Label heading = new Label();
			heading.Text = korean ? "삭제한 서버는 30일 동안 보관됩니다" : "Deleted servers are kept for 30 days";
			heading.Font = new Font("Segoe UI Variable Display Semib", 18F);
			heading.Dock = DockStyle.Fill;
			root.Controls.Add(heading, 0, 0);
			trashList = new BufferedListView();
			trashList.Dock = DockStyle.Fill;
			trashList.View = View.Details;
			trashList.FullRowSelect = true;
			trashList.HideSelection = false;
			trashList.MultiSelect = false;
			trashList.Columns.Add(korean ? "서버" : "Server", 220);
			trashList.Columns.Add(korean ? "삭제 시각" : "Deleted", 180);
			trashList.Columns.Add(korean ? "자동 삭제" : "Auto-delete", 180);
			trashList.Columns.Add(korean ? "남은 기간" : "Remaining", 120);
			trashList.SelectedIndexChanged += delegate { UpdateActions(); };
			root.Controls.Add(trashList, 0, 1);
			statusLabel = new Label();
			statusLabel.Dock = DockStyle.Fill;
			statusLabel.TextAlign = ContentAlignment.MiddleLeft;
			root.Controls.Add(statusLabel, 0, 2);
			FlowLayoutPanel actions = new FlowLayoutPanel();
			actions.Dock = DockStyle.Fill;
			actions.FlowDirection = FlowDirection.LeftToRight;
			actions.WrapContents = false;
			actions.Padding = new Padding(0, 7, 0, 0);
			root.Controls.Add(actions, 0, 3);
			restoreButton = MultiServerDashboardForm.NewManagedButton(korean ? "복구" : "Restore", 104, "primary");
			ApplyButtonIcon(restoreButton, ButtonIcon.Refresh);
			restoreButton.Click += delegate { RunTrashAction(RestoreSelected); };
			actions.Controls.Add(restoreButton);
			deleteButton = MultiServerDashboardForm.NewManagedButton(korean ? "영구 삭제" : "Delete forever", 126, "danger");
			ApplyButtonIcon(deleteButton, ButtonIcon.Trash);
			deleteButton.Click += delegate { RunTrashAction(DeleteSelectedForever); };
			actions.Controls.Add(deleteButton);
			Button refreshButton = MultiServerDashboardForm.NewManagedButton(korean ? "새로고침" : "Refresh", 108, "secondary");
			ApplyButtonIcon(refreshButton, ButtonIcon.Refresh);
			refreshButton.Click += delegate { ReloadTrash(); };
			actions.Controls.Add(refreshButton);
			Button closeButton = MultiServerDashboardForm.NewManagedButton(korean ? "닫기" : "Close", 96, "secondary");
			closeButton.Click += delegate { Close(); };
			actions.Controls.Add(closeButton);
			foreach (Control control in actions.Controls) EnsureButtonContentFits(control as Button);
			Shown += delegate { ReloadTrash(); };
			ApplySimpleDialogTheme(this);
			ApplyCommonButtonToolTips(this);
		}

		private void ReloadTrash()
		{
			int purged = PurgeExpiredServerTrash(serversRoot, DateTime.UtcNow);
			records.Clear();
			records.AddRange(ReadServerTrashRecords(serversRoot));
			trashList.BeginUpdate();
			try
			{
				trashList.Items.Clear();
				for (int i = 0; i < records.Count; i++)
				{
					ServerTrashRecord record = records[i];
					TimeSpan remaining = record.ExpiresUtc - DateTime.UtcNow;
					int days = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
					ListViewItem item = new ListViewItem(record.ProfileName);
					item.SubItems.Add(record.DeletedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
					item.SubItems.Add(record.ExpiresUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
					item.SubItems.Add(IsBackupKorean() ? days + "일" : days + " day(s)");
					item.Tag = record;
					trashList.Items.Add(item);
				}
			}
			finally
			{
				trashList.EndUpdate();
			}
			statusLabel.Text = IsBackupKorean() ? records.Count + "개 보관 중" : records.Count + " item(s)";
			if (purged > 0) statusLabel.Text += IsBackupKorean() ? " · 만료된 " + purged + "개 자동 삭제" : " · " + purged + " expired item(s) removed";
			UpdateActions();
		}

		private void UpdateActions()
		{
			bool selected = trashList.SelectedItems.Count == 1;
			restoreButton.Enabled = selected;
			deleteButton.Enabled = selected;
		}

		private ServerTrashRecord GetSelectedTrashRecord()
		{
			return trashList.SelectedItems.Count == 1 ? trashList.SelectedItems[0].Tag as ServerTrashRecord : null;
		}

		private void RestoreSelected()
		{
			ServerTrashRecord record = GetSelectedTrashRecord();
			if (record == null) return;
			string restoreName = record.ProfileName;
			if (Directory.Exists(GetProfileDirectory(serversRoot, restoreName)))
			{
				restoreName = PromptProfileText(this, IsBackupKorean() ? "복구할 서버의 새 이름" : "New name for restored server", restoreName + " 복구");
				if (string.IsNullOrEmpty(restoreName)) return;
			}
			RestoreServerTrashRecord(serversRoot, record, restoreName);
			if (ReadManagedProfiles(serversRoot).Count == 1) WriteActiveProfileName(serversRoot, restoreName);
			ReloadTrash();
			MessageBox.Show(this, IsBackupKorean() ? "서버를 복구했습니다." : "The server was restored.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		private void DeleteSelectedForever()
		{
			ServerTrashRecord record = GetSelectedTrashRecord();
			if (record == null) return;
			MessageBox.Show(this, IsBackupKorean() ? "이 작업은 되돌릴 수 없습니다. 계속하려면 다음 창에 서버 이름을 입력하세요.\r\n\r\n" + record.ProfileName : "This cannot be undone. Enter the server name in the next window to continue.\r\n\r\n" + record.ProfileName, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			string confirmation = PromptProfileText(this, IsBackupKorean() ? "영구 삭제 확인" : "Confirm permanent deletion", string.Empty);
			if (!string.Equals(confirmation, record.ProfileName, StringComparison.Ordinal))
			{
				if (confirmation != null) MessageBox.Show(this, IsBackupKorean() ? "서버 이름이 일치하지 않습니다." : "The server name does not match.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			PermanentlyDeleteServerTrashRecord(serversRoot, record);
			ReloadTrash();
		}

		private void RunTrashAction(Action action)
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, IsBackupKorean() ? "휴지통 작업을 완료하지 못했습니다: " + exception.Message : "Could not complete the trash operation: " + exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}
	}
}
