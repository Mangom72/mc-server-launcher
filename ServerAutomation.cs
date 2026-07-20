using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static partial class Launcher
{
	private const int AutomationSchemaVersion = 1;
	private const int AutomationFileMaximumBytes = 1048576;
	private static readonly object AutomationFileLock = new object();

	private sealed class ServerAutomationConfiguration
	{
		public int SchemaVersion = AutomationSchemaVersion;
		public bool BackupBeforeStart;
		public bool BackupAfterStop;
		public int RetentionCount = 10;
		public int RetentionDays = 30;
		public long RetentionMaximumBytes = 21474836480L;
		public List<ServerAutomationJob> Jobs = new List<ServerAutomationJob>();
	}

	private sealed class ServerAutomationJob
	{
		public string Id;
		public string Name;
		public string Action;
		public string Command;
		public bool Enabled = true;
		public string ScheduleKind = "interval";
		public int IntervalMinutes = 60;
		public string DailyLocalTime = "04:00";
		public int WarningSeconds = 60;
		public string NextRunUtc;
		public string LastRunUtc;
		public string LastResult;
		public bool Running;
		public string LeaseUtc;
		public int LeaseProcessId;
		public long LeaseProcessStartTicks;
	}

	private sealed class AutomationJobClaim
	{
		public string ServerDirectory;
		public ServerAutomationJob Job;
	}

	private static string GetAutomationConfigurationPath(string serverDirectory)
	{
		return Path.Combine(Path.Combine(Path.GetFullPath(serverDirectory), ".mineharbor"), "automation.json");
	}

	private static ServerAutomationConfiguration ReadServerAutomationConfiguration(string serverDirectory)
	{
		lock (AutomationFileLock)
		{
			return ReadServerAutomationConfigurationUnlocked(serverDirectory);
		}
	}

	private static ServerAutomationConfiguration ReadServerAutomationConfigurationUnlocked(string serverDirectory)
	{
		string path = GetAutomationConfigurationPath(serverDirectory);
		if (!File.Exists(path)) { ServerAutomationConfiguration defaults = new ServerAutomationConfiguration(); defaults.RetentionCount = ReadBackupRetentionCount(serverDirectory); return defaults; }
		FileInfo info = new FileInfo(path);
		if (info.Length <= 0 || info.Length > AutomationFileMaximumBytes)
		{
			throw new InvalidDataException("자동화 설정 파일 크기가 올바르지 않습니다.");
		}
		ServerAutomationConfiguration configuration;
		try
		{
			configuration = new JavaScriptSerializer().Deserialize<ServerAutomationConfiguration>(File.ReadAllText(path));
		}
		catch (Exception exception)
		{
			throw new InvalidDataException("자동화 설정 파일이 손상되었습니다. 원본 파일은 변경하지 않았습니다.", exception);
		}
		ValidateServerAutomationConfiguration(configuration);
		return configuration;
	}

	private static void WriteServerAutomationConfiguration(string serverDirectory, ServerAutomationConfiguration configuration)
	{
		ValidateServerAutomationConfiguration(configuration);
		lock (AutomationFileLock)
		{
			string path = GetAutomationConfigurationPath(serverDirectory);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			WriteJsonAtomic(path, configuration);
			WriteBackupRetentionCount(serverDirectory, configuration.RetentionCount);
		}
	}

	private static void ValidateServerAutomationConfiguration(ServerAutomationConfiguration configuration)
	{
		if (configuration == null || configuration.SchemaVersion != AutomationSchemaVersion)
			throw new InvalidDataException("지원하지 않는 자동화 설정 버전입니다.");
		configuration.RetentionCount = Math.Max(1, Math.Min(200, configuration.RetentionCount));
		configuration.RetentionDays = Math.Max(1, Math.Min(3650, configuration.RetentionDays));
		configuration.RetentionMaximumBytes = Math.Max(104857600L, Math.Min(10995116277760L, configuration.RetentionMaximumBytes));
		if (configuration.Jobs == null) configuration.Jobs = new List<ServerAutomationJob>();
		if (configuration.Jobs.Count > 200) throw new InvalidDataException("예약 작업은 서버당 200개를 넘을 수 없습니다.");
		HashSet<string> identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < configuration.Jobs.Count; i++)
		{
			ServerAutomationJob job = configuration.Jobs[i];
			if (job == null || string.IsNullOrWhiteSpace(job.Id) || job.Id.Length > 80 || !identifiers.Add(job.Id))
				throw new InvalidDataException("예약 작업 식별자가 없거나 중복되었습니다.");
			if (string.IsNullOrWhiteSpace(job.Name) || job.Name.Length > 120)
				throw new InvalidDataException("예약 작업 이름은 1~120자로 입력해야 합니다.");
			if (!IsSupportedAutomationAction(job.Action)) throw new InvalidDataException("지원하지 않는 예약 작업 종류입니다.");
			if (string.Equals(job.Action, "command", StringComparison.OrdinalIgnoreCase)) ValidateScheduledCommand(job.Command);
			if (job.WarningSeconds < 0 || job.WarningSeconds > 3600) throw new InvalidDataException("공지 시간은 0~3600초여야 합니다.");
			if (string.Equals(job.ScheduleKind, "interval", StringComparison.OrdinalIgnoreCase))
			{
				if (job.IntervalMinutes < 1 || job.IntervalMinutes > 525600) throw new InvalidDataException("반복 간격은 1분~365일이어야 합니다.");
			}
			else if (string.Equals(job.ScheduleKind, "daily", StringComparison.OrdinalIgnoreCase))
			{
				DateTime ignored;
				if (!DateTime.TryParseExact(job.DailyLocalTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out ignored))
					throw new InvalidDataException("매일 실행 시각은 HH:mm 형식이어야 합니다.");
			}
			else throw new InvalidDataException("지원하지 않는 예약 방식입니다.");
			DateTime parsed;
			if (!string.IsNullOrEmpty(job.NextRunUtc) && !DateTime.TryParse(job.NextRunUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
				throw new InvalidDataException("다음 실행 시각이 올바르지 않습니다.");
		}
	}

	private static bool IsSupportedAutomationAction(string action)
	{
		return string.Equals(action, "backup", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(action, "start", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(action, "restart", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(action, "command", StringComparison.OrdinalIgnoreCase);
	}

	private static void ValidateScheduledCommand(string command)
	{
		if (string.IsNullOrWhiteSpace(command) || command.Length > 2048 || command.IndexOf('\r') >= 0 || command.IndexOf('\n') >= 0 || command.IndexOf('\0') >= 0)
			throw new InvalidDataException("예약 명령은 줄바꿈 없이 1~2048자로 입력해야 합니다.");
	}

	private static DateTime CalculateNextAutomationRunUtc(ServerAutomationJob job, DateTime afterUtc)
	{
		if (string.Equals(job.ScheduleKind, "interval", StringComparison.OrdinalIgnoreCase))
			return afterUtc.AddMinutes(Math.Max(1, job.IntervalMinutes));
		DateTime localAfter = afterUtc.ToLocalTime();
		DateTime time = DateTime.ParseExact(job.DailyLocalTime, "HH:mm", CultureInfo.InvariantCulture);
		DateTime candidate = new DateTime(localAfter.Year, localAfter.Month, localAfter.Day, time.Hour, time.Minute, 0, DateTimeKind.Local);
		if (candidate <= localAfter) candidate = candidate.AddDays(1);
		return candidate.ToUniversalTime();
	}

	private static List<AutomationJobClaim> ClaimDueAutomationJobs(string serverDirectory, DateTime nowUtc)
	{
		lock (AutomationFileLock)
		{
			ServerAutomationConfiguration configuration = ReadServerAutomationConfigurationUnlocked(serverDirectory);
			List<AutomationJobClaim> claims = new List<AutomationJobClaim>();
			bool changed = false;
			for (int i = 0; i < configuration.Jobs.Count; i++)
			{
				ServerAutomationJob job = configuration.Jobs[i];
				DateTime lease;
				bool legacyLeaseExpired = !DateTime.TryParse(job.LeaseUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out lease) || nowUtc - lease.ToUniversalTime() > TimeSpan.FromMinutes(30);
				bool ownerGone = job.LeaseProcessId > 0 && job.LeaseProcessStartTicks > 0 && !IsAutomationLeaseOwnerAlive(job.LeaseProcessId, job.LeaseProcessStartTicks);
				if (job.Running && (ownerGone || ((job.LeaseProcessId <= 0 || job.LeaseProcessStartTicks <= 0) && legacyLeaseExpired)))
				{
					job.Running = false;
					job.LeaseUtc = null;
					job.LeaseProcessId = 0;
					job.LeaseProcessStartTicks = 0L;
					job.LastResult = "이전 실행 임대가 만료되어 복구됨 / Recovered expired execution lease";
					changed = true;
				}
				if (!job.Enabled || job.Running) continue;
				DateTime next;
				if (string.IsNullOrEmpty(job.NextRunUtc) || !DateTime.TryParse(job.NextRunUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out next))
				{
					job.NextRunUtc = CalculateNextAutomationRunUtc(job, nowUtc).ToString("o", CultureInfo.InvariantCulture);
					changed = true;
					continue;
				}
				if (next.ToUniversalTime() > nowUtc) continue;
				job.Running = true;
				job.LeaseUtc = nowUtc.ToString("o", CultureInfo.InvariantCulture);
				using (System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess())
				{
					job.LeaseProcessId = current.Id;
					job.LeaseProcessStartTicks = current.StartTime.Ticks;
				}
				job.NextRunUtc = CalculateNextAutomationRunUtc(job, nowUtc).ToString("o", CultureInfo.InvariantCulture);
				claims.Add(new AutomationJobClaim { ServerDirectory = Path.GetFullPath(serverDirectory), Job = CloneAutomationJob(job) });
				changed = true;
			}
			if (changed)
			{
				string path = GetAutomationConfigurationPath(serverDirectory);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				WriteJsonAtomic(path, configuration);
			}
			return claims;
		}
	}

	private static void CompleteAutomationJob(AutomationJobClaim claim, DateTime completedUtc, string result)
	{
		lock (AutomationFileLock)
		{
			ServerAutomationConfiguration configuration = ReadServerAutomationConfigurationUnlocked(claim.ServerDirectory);
			ServerAutomationJob match = configuration.Jobs.Find(delegate(ServerAutomationJob item) { return string.Equals(item.Id, claim.Job.Id, StringComparison.OrdinalIgnoreCase); });
			if (match == null) return;
			match.Running = false;
			match.LeaseUtc = null;
			match.LeaseProcessId = 0;
			match.LeaseProcessStartTicks = 0L;
			match.LastRunUtc = completedUtc.ToString("o", CultureInfo.InvariantCulture);
			match.LastResult = string.IsNullOrWhiteSpace(result) ? "완료 / Completed" : result;
			string path = GetAutomationConfigurationPath(claim.ServerDirectory);
			WriteJsonAtomic(path, configuration);
		}
	}

	private static bool IsAutomationLeaseOwnerAlive(int processId, long startTicks)
	{
		try
		{
			using (System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId)) return !process.HasExited && process.StartTime.Ticks == startTicks;
		}
		catch { return false; }
	}

	private static ServerAutomationJob CloneAutomationJob(ServerAutomationJob source)
	{
		return new JavaScriptSerializer().Deserialize<ServerAutomationJob>(new JavaScriptSerializer().Serialize(source));
	}

	private static void PruneServerBackupsWithPolicy(string backupDirectory, int retentionCount, int retentionDays, long retentionMaximumBytes, DateTime nowUtc)
	{
		if (!Directory.Exists(backupDirectory)) return;
		retentionCount = Math.Max(1, Math.Min(200, retentionCount));
		retentionDays = Math.Max(1, Math.Min(3650, retentionDays));
		retentionMaximumBytes = Math.Max(104857600L, retentionMaximumBytes);
		FileInfo[] files = new DirectoryInfo(backupDirectory).GetFiles("server-*.zip");
		Array.Sort(files, delegate(FileInfo left, FileInfo right) { return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc); });
		long keptBytes = 0L;
		for (int i = 0; i < files.Length; i++)
		{
			bool keepNewest = i == 0;
			bool exceedsCount = i >= retentionCount;
			bool exceedsAge = nowUtc - files[i].LastWriteTimeUtc > TimeSpan.FromDays(retentionDays);
			bool exceedsSize = keptBytes > retentionMaximumBytes - files[i].Length;
			if (!keepNewest && (exceedsCount || exceedsAge || exceedsSize)) files[i].Delete();
			else keptBytes = checked(keptBytes + files[i].Length);
		}
	}

	private sealed class AutomationManagerForm : Form
	{
		private readonly string serverDirectory;
		private readonly ListView jobList;
		private readonly CheckBox beforeStartBox;
		private readonly CheckBox afterStopBox;
		private readonly NumericUpDown countBox;
		private readonly NumericUpDown daysBox;
		private readonly NumericUpDown sizeBox;
		private ServerAutomationConfiguration configuration;

		public AutomationManagerForm(string directory)
		{
			serverDirectory = Path.GetFullPath(directory);
			bool korean = IsManagedKorean();
			Text = korean ? "자동 백업 및 일정" : "Backups and schedules";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(880, 600);
			Size = new Size(1040, 680);
			AutoScaleMode = AutoScaleMode.Dpi;
			Font = new Font("Pretendard", 10.5F);
			KeyPreview = true;
			ApplyLauncherWindowIcon(this);

			TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 4, ColumnCount = 1 };
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(root);
			Label heading = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Text = korean ? "서버별 자동화" : "Per-server automation", Margin = new Padding(0, 0, 0, 12) };
			root.Controls.Add(heading, 0, 0);
			jobList = new BufferedListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
			jobList.Columns.Add(korean ? "작업" : "Job", 180);
			jobList.Columns.Add(korean ? "종류" : "Action", 100);
			jobList.Columns.Add(korean ? "일정" : "Schedule", 150);
			jobList.Columns.Add(korean ? "다음 실행" : "Next run", 175);
			jobList.Columns.Add(korean ? "최근 결과" : "Last result", 300);
			root.Controls.Add(jobList, 0, 1);

			FlowLayoutPanel policy = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 10, 0, 4) };
			beforeStartBox = new CheckBox { AutoSize = true, Text = korean ? "시작 전 백업" : "Backup before start", Margin = new Padding(0, 8, 18, 0) };
			afterStopBox = new CheckBox { AutoSize = true, Text = korean ? "종료 후 백업" : "Backup after stop", Margin = new Padding(0, 8, 18, 0) };
			countBox = AddAutomationNumber(policy, korean ? "보존 개수" : "Keep count", 1, 200, 10);
			daysBox = AddAutomationNumber(policy, korean ? "보존 일수" : "Keep days", 1, 3650, 30);
			sizeBox = AddAutomationNumber(policy, korean ? "최대 용량(GB)" : "Maximum size (GB)", 1, 10240, 20);
			policy.Controls.Add(beforeStartBox);
			policy.Controls.Add(afterStopBox);
			root.Controls.Add(policy, 0, 2);

			FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 8, 0, 0) };
			Button add = MultiServerDashboardForm.NewManagedButton(korean ? "추가" : "Add", 90, "primary");
			Button edit = MultiServerDashboardForm.NewManagedButton(korean ? "편집" : "Edit", 90, "secondary");
			Button toggle = MultiServerDashboardForm.NewManagedButton(korean ? "켜기/끄기" : "Enable/disable", 122, "secondary");
			Button run = MultiServerDashboardForm.NewManagedButton(korean ? "지금 실행" : "Run now", 108, "secondary");
			Button remove = MultiServerDashboardForm.NewManagedButton(korean ? "제거" : "Remove", 90, "danger");
			Button save = MultiServerDashboardForm.NewManagedButton(korean ? "설정 저장" : "Save settings", 116, "primary");
			Button refresh = MultiServerDashboardForm.NewManagedButton(korean ? "새로고침" : "Refresh", 104, "secondary");
			add.Click += delegate { EditJob(null); };
			edit.Click += delegate { EditJob(GetSelectedJob()); };
			toggle.Click += delegate { ToggleSelectedJob(); };
			run.Click += delegate { RunSelectedNow(); };
			remove.Click += delegate { RemoveSelectedJob(); };
			save.Click += delegate { SaveConfiguration(); };
			refresh.Click += delegate { Reload(); };
			actions.Controls.AddRange(new Control[] { add, edit, toggle, run, remove, refresh, save });
			root.Controls.Add(actions, 0, 3);
			Shown += delegate { Reload(); };
			jobList.DoubleClick += delegate { EditJob(GetSelectedJob()); };
			ApplySimpleDialogTheme(this);
			ConfigureAccessibleField(jobList, korean ? "예약 작업 목록" : "Scheduled jobs", korean ? "다음 실행 시각과 최근 실행 결과를 표시합니다." : "Shows next run time and latest execution result.");
			ApplyCommonButtonToolTips(this);
		}

		private static NumericUpDown AddAutomationNumber(FlowLayoutPanel panel, string label, decimal minimum, decimal maximum, decimal value)
		{
			panel.Controls.Add(new Label { AutoSize = true, Text = label, Margin = new Padding(8, 10, 4, 0) });
			NumericUpDown box = new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value, Width = 78, Margin = new Padding(0, 5, 8, 0), AccessibleName = label };
			panel.Controls.Add(box);
			return box;
		}

		private void Reload()
		{
			try
			{
				configuration = ReadServerAutomationConfiguration(serverDirectory);
				beforeStartBox.Checked = configuration.BackupBeforeStart;
				afterStopBox.Checked = configuration.BackupAfterStop;
				countBox.Value = configuration.RetentionCount;
				daysBox.Value = configuration.RetentionDays;
				sizeBox.Value = Math.Max(sizeBox.Minimum, Math.Min(sizeBox.Maximum, configuration.RetentionMaximumBytes / 1073741824L));
				RenderJobs();
			}
			catch (Exception exception) { ShowAutomationError(exception); }
		}

		private void RenderJobs()
		{
			jobList.Items.Clear();
			if (configuration == null) return;
			for (int i = 0; i < configuration.Jobs.Count; i++)
			{
				ServerAutomationJob job = configuration.Jobs[i];
				ListViewItem item = new ListViewItem((job.Enabled ? "● " : "○ ") + job.Name) { Tag = job.Id };
				item.SubItems.Add(AutomationActionText(job.Action));
				item.SubItems.Add(string.Equals(job.ScheduleKind, "daily", StringComparison.OrdinalIgnoreCase) ? ManagedText("매일 ", "Daily ") + job.DailyLocalTime : ManagedText("매 ", "Every ") + job.IntervalMinutes + ManagedText("분", " min"));
				item.SubItems.Add(FormatAutomationTime(job.NextRunUtc));
				item.SubItems.Add(string.IsNullOrWhiteSpace(job.LastResult) ? ManagedText("실행 기록 없음", "Never run") : job.LastResult);
				jobList.Items.Add(item);
			}
		}

		private ServerAutomationJob GetSelectedJob()
		{
			if (configuration == null || jobList.SelectedItems.Count == 0) return null;
			string id = Convert.ToString(jobList.SelectedItems[0].Tag);
			return configuration.Jobs.Find(delegate(ServerAutomationJob item) { return string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase); });
		}

		private void EditJob(ServerAutomationJob existing)
		{
			using (AutomationJobForm dialog = new AutomationJobForm(existing))
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				if (existing == null) configuration.Jobs.Add(dialog.Job);
				else
				{
					int index = configuration.Jobs.IndexOf(existing);
					dialog.Job.LastRunUtc = existing.LastRunUtc;
					dialog.Job.LastResult = existing.LastResult;
					dialog.Job.Running = existing.Running;
					dialog.Job.LeaseUtc = existing.LeaseUtc;
					dialog.Job.LeaseProcessId = existing.LeaseProcessId;
					dialog.Job.LeaseProcessStartTicks = existing.LeaseProcessStartTicks;
					if (string.Equals(dialog.Job.ScheduleKind, existing.ScheduleKind, StringComparison.OrdinalIgnoreCase)
						&& dialog.Job.IntervalMinutes == existing.IntervalMinutes
						&& string.Equals(dialog.Job.DailyLocalTime, existing.DailyLocalTime, StringComparison.Ordinal)) dialog.Job.NextRunUtc = existing.NextRunUtc;
					configuration.Jobs[index] = dialog.Job;
				}
				SaveConfiguration();
			}
		}

		private void ToggleSelectedJob() { ServerAutomationJob job = GetSelectedJob(); if (job == null) return; job.Enabled = !job.Enabled; SaveConfiguration(); }
		private void RunSelectedNow() { ServerAutomationJob job = GetSelectedJob(); if (job == null) return; job.Enabled = true; job.NextRunUtc = DateTime.UtcNow.AddSeconds(-1).ToString("o", CultureInfo.InvariantCulture); SaveConfiguration(); }
		private void RemoveSelectedJob() { ServerAutomationJob job = GetSelectedJob(); if (job == null || ShowMineHarborDialog(this, ManagedText("선택한 예약 작업을 제거할까요?", "Remove the selected scheduled job?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; configuration.Jobs.Remove(job); SaveConfiguration(); }

		private void SaveConfiguration()
		{
			try
			{
				configuration.BackupBeforeStart = beforeStartBox.Checked;
				configuration.BackupAfterStop = afterStopBox.Checked;
				configuration.RetentionCount = (int)countBox.Value;
				configuration.RetentionDays = (int)daysBox.Value;
				configuration.RetentionMaximumBytes = checked((long)sizeBox.Value * 1073741824L);
				for (int i = 0; i < configuration.Jobs.Count; i++) if (string.IsNullOrEmpty(configuration.Jobs[i].NextRunUtc)) configuration.Jobs[i].NextRunUtc = CalculateNextAutomationRunUtc(configuration.Jobs[i], DateTime.UtcNow).ToString("o", CultureInfo.InvariantCulture);
				WriteServerAutomationConfiguration(serverDirectory, configuration);
				RenderJobs();
			}
			catch (Exception exception) { ShowAutomationError(exception); }
		}

		private void ShowAutomationError(Exception exception) { ShowMineHarborDialog(this, ManagedText("자동화 설정을 처리하지 못했습니다: ", "Could not process automation settings: ") + exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
	}

	private sealed class AutomationJobForm : Form
	{
		private readonly TextBox nameBox;
		private readonly ComboBox actionBox;
		private readonly ComboBox scheduleBox;
		private readonly NumericUpDown intervalBox;
		private readonly TextBox timeBox;
		private readonly NumericUpDown warningBox;
		private readonly TextBox commandBox;
		private readonly CheckBox enabledBox;
		private readonly string originalId;
		public ServerAutomationJob Job { get; private set; }

		public AutomationJobForm(ServerAutomationJob existing)
		{
			bool korean = IsManagedKorean();
			Text = korean ? "예약 작업" : "Scheduled job";
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ClientSize = new Size(560, 420);
			AutoScaleMode = AutoScaleMode.Dpi;
			Font = new Font("Pretendard", 10.5F);
			TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 9 };
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			Controls.Add(root);
			nameBox = AddAutomationField(root, 0, korean ? "이름" : "Name") as TextBox;
			actionBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
			actionBox.Items.AddRange(korean ? new object[] { "백업", "시작", "종료", "재시작", "명령" } : new object[] { "Backup", "Start", "Stop", "Restart", "Command" }); AddAutomationControl(root, 1, korean ? "작업" : "Action", actionBox);
			scheduleBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList }; scheduleBox.Items.AddRange(korean ? new object[] { "반복 간격", "매일" } : new object[] { "Interval", "Daily" }); AddAutomationControl(root, 2, korean ? "일정 방식" : "Schedule", scheduleBox);
			intervalBox = new NumericUpDown { Dock = DockStyle.Left, Width = 150, Minimum = 1, Maximum = 525600, Value = 60 }; AddAutomationControl(root, 3, korean ? "반복 간격(분)" : "Interval (minutes)", intervalBox);
			timeBox = AddAutomationField(root, 4, korean ? "매일 시각(HH:mm)" : "Daily time (HH:mm)") as TextBox;
			warningBox = new NumericUpDown { Dock = DockStyle.Left, Width = 150, Minimum = 0, Maximum = 3600, Value = 60 }; AddAutomationControl(root, 5, korean ? "사전 공지(초)" : "Warning (seconds)", warningBox);
			commandBox = AddAutomationField(root, 6, korean ? "명령" : "Command") as TextBox;
			enabledBox = new CheckBox { AutoSize = true, Checked = true, Text = korean ? "사용" : "Enabled" }; root.Controls.Add(enabledBox, 1, 7);
			FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
			Button save = MultiServerDashboardForm.NewManagedButton(korean ? "저장" : "Save", 96, "primary"); Button cancel = MultiServerDashboardForm.NewManagedButton(korean ? "취소" : "Cancel", 96, "secondary");
			save.Click += delegate { SaveJob(); }; cancel.DialogResult = DialogResult.Cancel; actions.Controls.Add(save); actions.Controls.Add(cancel); root.Controls.Add(actions, 1, 8);
			AcceptButton = save; CancelButton = cancel;
			originalId = existing == null ? Guid.NewGuid().ToString("N") : existing.Id;
			if (existing != null)
			{
				nameBox.Text = existing.Name; actionBox.SelectedIndex = AutomationActionIndex(existing.Action); scheduleBox.SelectedIndex = string.Equals(existing.ScheduleKind, "daily", StringComparison.OrdinalIgnoreCase) ? 1 : 0; intervalBox.Value = Math.Max(intervalBox.Minimum, Math.Min(intervalBox.Maximum, existing.IntervalMinutes)); timeBox.Text = existing.DailyLocalTime; warningBox.Value = existing.WarningSeconds; commandBox.Text = existing.Command; enabledBox.Checked = existing.Enabled;
			}
			else { actionBox.SelectedIndex = 0; scheduleBox.SelectedIndex = 0; timeBox.Text = "04:00"; }
			actionBox.SelectedIndexChanged += delegate { UpdateJobFieldState(); };
			scheduleBox.SelectedIndexChanged += delegate { UpdateJobFieldState(); };
			UpdateJobFieldState();
			ApplySimpleDialogTheme(this); ApplyCommonButtonToolTips(this);
		}

		private static Control AddAutomationField(TableLayoutPanel root, int row, string label) { TextBox box = new TextBox { Dock = DockStyle.Fill }; AddAutomationControl(root, row, label, box); return box; }
		private static void AddAutomationControl(TableLayoutPanel root, int row, string label, Control control) { control.AccessibleName = label; root.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, row); root.Controls.Add(control, 1, row); }
		private void UpdateJobFieldState()
		{
			intervalBox.Enabled = scheduleBox.SelectedIndex != 1;
			timeBox.Enabled = scheduleBox.SelectedIndex == 1;
			warningBox.Enabled = actionBox.SelectedIndex == 2 || actionBox.SelectedIndex == 3;
			commandBox.Enabled = actionBox.SelectedIndex == 4;
		}
		private void SaveJob()
		{
			try
			{
				string[] actions = { "backup", "start", "stop", "restart", "command" };
				ServerAutomationJob value = new ServerAutomationJob { Id = originalId, Name = nameBox.Text.Trim(), Action = actionBox.SelectedIndex >= 0 ? actions[actionBox.SelectedIndex] : string.Empty, ScheduleKind = scheduleBox.SelectedIndex == 1 ? "daily" : "interval", IntervalMinutes = (int)intervalBox.Value, DailyLocalTime = timeBox.Text.Trim(), WarningSeconds = (int)warningBox.Value, Command = commandBox.Text.Trim(), Enabled = enabledBox.Checked };
				ServerAutomationConfiguration validation = new ServerAutomationConfiguration(); validation.Jobs.Add(value); ValidateServerAutomationConfiguration(validation);
				Job = value; DialogResult = DialogResult.OK; Close();
			}
			catch (Exception exception) { ShowMineHarborDialog(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
		}

		private static int AutomationActionIndex(string action)
		{
			string[] actions = { "backup", "start", "stop", "restart", "command" };
			for (int i = 0; i < actions.Length; i++) if (string.Equals(actions[i], action, StringComparison.OrdinalIgnoreCase)) return i;
			return 0;
		}
	}

	private static string AutomationActionText(string action)
	{
		if (string.Equals(action, "backup", StringComparison.OrdinalIgnoreCase)) return ManagedText("백업", "Backup");
		if (string.Equals(action, "start", StringComparison.OrdinalIgnoreCase)) return ManagedText("시작", "Start");
		if (string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase)) return ManagedText("종료", "Stop");
		if (string.Equals(action, "restart", StringComparison.OrdinalIgnoreCase)) return ManagedText("재시작", "Restart");
		return ManagedText("명령", "Command");
	}

	private static string FormatAutomationTime(string value)
	{
		DateTime parsed;
		return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed) ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) : ManagedText("미정", "Not scheduled");
	}
}
