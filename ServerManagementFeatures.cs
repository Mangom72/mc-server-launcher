using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static partial class Launcher
{
	private sealed class ServerStatusSnapshot
	{
		public string Status;
		public string Uptime;
		public string Cpu;
		public string Memory;
		public string JavaVersion;
		public string Players;
		public string ServerSize;
		public string WorldSize;
		public string BackupSize;
		public string ExternalAccess;
		public string NextAutomation;
		public string TickHealth;
		public string[] RecentProblems;
	}

	private sealed partial class MultiServerDashboardForm
	{
		private Button contentManagerButton;
		private Button backupManagerButton;
		private Button automationManagerButton;
		private Button statusDashboardButton;
		private CancellationTokenSource managementCancellation = new CancellationTokenSource();
		private DateTime nextAutomationPollUtc = DateTime.MinValue;
		private bool automationPollRunning;
		private readonly HashSet<string> profilesStarting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, SemaphoreSlim> automationExecutionLocks = new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

		private void InitializeManagementFeatures(FlowLayoutPanel actions, bool korean)
		{
			contentManagerButton = NewManagedButton(korean ? "콘텐츠" : "Content", 94, "secondary");
			backupManagerButton = NewManagedButton(korean ? "백업" : "Backups", 90, "secondary");
			automationManagerButton = NewManagedButton(korean ? "일정" : "Schedules", 94, "secondary");
			statusDashboardButton = NewManagedButton(korean ? "대시보드" : "Dashboard", 106, "secondary");
			contentManagerButton.Click += delegate { OpenSelectedContentManager(); };
			backupManagerButton.Click += delegate { OpenSelectedBackupManager(); };
			automationManagerButton.Click += delegate { OpenSelectedAutomationManager(); };
			statusDashboardButton.Click += delegate { OpenSelectedStatusDashboard(); };
			actions.Controls.Add(contentManagerButton);
			actions.Controls.Add(backupManagerButton);
			actions.Controls.Add(automationManagerButton);
			actions.Controls.Add(statusDashboardButton);
			EnsureButtonContentFits(contentManagerButton);
			EnsureButtonContentFits(backupManagerButton);
			EnsureButtonContentFits(automationManagerButton);
			EnsureButtonContentFits(statusDashboardButton);
		}

		private void DisposeManagementFeatures()
		{
			if (managementCancellation != null)
			{
				managementCancellation.Cancel();
				managementCancellation.Dispose();
				managementCancellation = null;
			}
		}

		private LauncherOptions GetSelectedContentOptions()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null) return null;
			return new LauncherOptions { ServerDirectory = profile.Directory, ProfileName = profile.Name, ServerType = profile.ServerType, MinecraftVersion = profile.MinecraftVersion, MemoryGb = profile.MemoryGb };
		}

		private void UpdateManagementFeatureActions(ManagedProfileRecord profile, bool running)
		{
			if (contentManagerButton == null) return;
			contentManagerButton.Enabled = profile != null && !running && !mainServerBusy;
			backupManagerButton.Enabled = profile != null && !running && !mainServerBusy;
			automationManagerButton.Enabled = profile != null;
			statusDashboardButton.Enabled = profile != null;
		}

		private void OpenSelectedContentManager()
		{
			LauncherOptions options = GetSelectedContentOptions();
			if (options == null) return;
			using (UnifiedContentManagerForm form = new UnifiedContentManagerForm(options)) form.ShowDialog(this);
		}

		private void OpenSelectedBackupManager()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null) return;
			using (BackupManagerForm form = new BackupManagerForm(profile.Directory)) form.ShowDialog(this);
		}

		private void OpenSelectedAutomationManager()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null) return;
			using (AutomationManagerForm form = new AutomationManagerForm(profile.Directory)) form.ShowDialog(this);
			nextAutomationPollUtc = DateTime.MinValue;
		}

		private void OpenSelectedStatusDashboard()
		{
			ManagedProfileRecord profile = GetSelectedProfile();
			if (profile == null) return;
			ManagedServerSession session;
			sessions.TryGetValue(profile.Name, out session);
			using (ServerStatusDashboardForm form = new ServerStatusDashboardForm(profile, session)) form.ShowDialog(this);
		}

		private async void StartSessionWithPreBackup(ManagedProfileRecord profile, bool automaticRestart)
		{
			if (profile == null || profilesStarting.Contains(profile.Name)) return;
			profilesStarting.Add(profile.Name);
			try
			{
				ServerAutomationConfiguration configuration = ReadServerAutomationConfiguration(profile.Directory);
				if (configuration.BackupBeforeStart)
				{
					await CreatePolicyBackupAsync(profile.Directory, configuration, "before-start", managementCancellation.Token);
				}
				if (!IsDisposed && managementCancellation != null && !managementCancellation.IsCancellationRequested) StartSession(profile, automaticRestart);
			}
			catch (OperationCanceledException) { }
			catch (Exception exception)
			{
				if (!IsDisposed) ShowManagedMessage("시작 전 작업을 완료하지 못했습니다: " + exception.Message, "Could not complete the pre-start operation: " + exception.Message, true);
			}
			finally { profilesStarting.Remove(profile.Name); }
		}

		private void HandlePostStopBackup(ManagedServerSession session)
		{
			if (session == null || !session.StopRequested || managementCancellation == null || managementCancellation.IsCancellationRequested) return;
			ObservePostStopBackupAsync(session);
		}

		private async void ObservePostStopBackupAsync(ManagedServerSession session)
		{
			try
			{
				ServerAutomationConfiguration configuration = ReadServerAutomationConfiguration(session.Profile.Directory);
				if (!configuration.BackupAfterStop) return;
				string path = await CreatePolicyBackupAsync(session.Profile.Directory, configuration, "after-stop", managementCancellation.Token);
				session.AddLine("[Launcher] " + ManagedText("종료 후 백업 완료: ", "Post-stop backup completed: ") + Path.GetFileName(path));
			}
			catch (OperationCanceledException) { }
			catch (Exception exception) { session.AddLine("[Launcher] " + ManagedText("종료 후 백업 실패: ", "Post-stop backup failed: ") + exception.Message); }
		}

		private async Task<string> CreatePolicyBackupAsync(string serverDirectory, ServerAutomationConfiguration configuration, string reason, CancellationToken cancellationToken)
		{
			return await Task.Run(delegate
			{
				cancellationToken.ThrowIfCancellationRequested();
				string path = CreateComprehensiveServerBackup(serverDirectory, configuration.RetentionCount, reason);
				cancellationToken.ThrowIfCancellationRequested();
				PruneServerBackupsWithPolicy(GetServerBackupDirectory(serverDirectory), configuration.RetentionCount, configuration.RetentionDays, configuration.RetentionMaximumBytes, DateTime.UtcNow);
				return path;
			}, cancellationToken);
		}

		private async void ScheduleManagedRestart(ManagedServerSession session)
		{
			if (managementCancellation == null || managementCancellation.IsCancellationRequested) return;
			try
			{
				await Task.Delay(5000, managementCancellation.Token);
				if (!IsDisposed && !managementCancellation.IsCancellationRequested) StartSessionWithPreBackup(session.Profile, true);
			}
			catch (OperationCanceledException) { }
		}

		private void ProcessAutomationTimerTick()
		{
			if (automationPollRunning || managementCancellation == null || managementCancellation.IsCancellationRequested || DateTime.UtcNow < nextAutomationPollUtc) return;
			nextAutomationPollUtc = DateTime.UtcNow.AddSeconds(5);
			ObserveAutomationPollAsync();
		}

		private async void ObserveAutomationPollAsync()
		{
			automationPollRunning = true;
		try
		{
			for (int i = 0; i < profiles.Count; i++)
			{
				try
				{
					List<AutomationJobClaim> claims = ClaimDueAutomationJobs(profiles[i].Directory, DateTime.UtcNow);
					for (int claimIndex = 0; claimIndex < claims.Count; claimIndex++) ObserveManagedAutomationJobAsync(claims[claimIndex]);
				}
				catch (InvalidDataException exception) { summaryLabel.Text = profiles[i].Name + " · " + ManagedText("자동화 설정 오류: ", "Automation configuration error: ") + exception.Message; }
			}
			await Task.Yield();
		}
		catch (OperationCanceledException) { }
		catch (Exception exception) { if (!IsDisposed) summaryLabel.Text = ManagedText("예약 작업 검사 실패: ", "Schedule check failed: ") + exception.Message; }
		finally { automationPollRunning = false; }
	}

		private async void ObserveManagedAutomationJobAsync(AutomationJobClaim claim)
		{
			SemaphoreSlim gate;
			if (!automationExecutionLocks.TryGetValue(claim.ServerDirectory, out gate))
			{
				gate = new SemaphoreSlim(1, 1);
				automationExecutionLocks[claim.ServerDirectory] = gate;
			}
			bool entered = false;
			try
			{
				await gate.WaitAsync(managementCancellation.Token);
				entered = true;
				await ExecuteAutomationJobAsync(claim, managementCancellation.Token);
			}
			catch (OperationCanceledException)
			{
				try { CompleteAutomationJob(claim, DateTime.UtcNow, ManagedText("취소됨", "Cancelled")); } catch { }
			}
			finally { if (entered) gate.Release(); }
		}

		private async Task ExecuteAutomationJobAsync(AutomationJobClaim claim, CancellationToken cancellationToken)
		{
			string result = ManagedText("실행 결과 없음", "No execution result");
			try
			{
				ManagedProfileRecord profile = profiles.Find(delegate(ManagedProfileRecord item) { return string.Equals(Path.GetFullPath(item.Directory), claim.ServerDirectory, StringComparison.OrdinalIgnoreCase); });
				if (profile == null) throw new InvalidOperationException("예약 작업의 서버 프로필을 찾지 못했습니다.");
				ServerAutomationConfiguration configuration = ReadServerAutomationConfiguration(profile.Directory);
				ManagedServerSession session;
				sessions.TryGetValue(profile.Name, out session);
				if (string.Equals(claim.Job.Action, "backup", StringComparison.OrdinalIgnoreCase))
				{
					bool pausedSaves = session != null && IsManagedSessionRunning(session);
					try
					{
						if (pausedSaves) { SendManagedCommand(session, "save-off"); SendManagedCommand(session, "save-all flush"); await Task.Delay(1000, cancellationToken); }
						string path = await CreatePolicyBackupAsync(profile.Directory, configuration, "scheduled", cancellationToken);
						result = ManagedText("백업 완료: ", "Backup completed: ") + Path.GetFileName(path);
					}
					finally { if (pausedSaves && IsManagedSessionRunning(session)) SendManagedCommand(session, "save-on"); }
				}
				else if (string.Equals(claim.Job.Action, "start", StringComparison.OrdinalIgnoreCase))
				{
					if (session != null && IsManagedSessionRunning(session)) result = ManagedText("이미 실행 중", "Already running");
					else { if (configuration.BackupBeforeStart) await CreatePolicyBackupAsync(profile.Directory, configuration, "before-start", cancellationToken); StartSession(profile, false); result = ManagedText("시작 요청 완료", "Start requested"); }
				}
				else if (string.Equals(claim.Job.Action, "stop", StringComparison.OrdinalIgnoreCase))
				{
					if (session == null || !IsManagedSessionRunning(session)) result = ManagedText("이미 중지됨", "Already stopped");
					else { await AnnounceAutomationActionAsync(session, claim.Job.WarningSeconds, ManagedText("서버가 곧 종료됩니다.", "Server will stop soon."), cancellationToken); SendManagedStop(session, false); result = ManagedText("종료 요청 완료", "Stop requested"); }
				}
				else if (string.Equals(claim.Job.Action, "restart", StringComparison.OrdinalIgnoreCase))
				{
					if (session == null || !IsManagedSessionRunning(session)) { StartSessionWithPreBackup(profile, false); result = ManagedText("중지 상태에서 시작 요청 완료", "Start requested from stopped state"); }
					else { await AnnounceAutomationActionAsync(session, claim.Job.WarningSeconds, ManagedText("서버가 곧 재시작됩니다.", "Server will restart soon."), cancellationToken); session.ScheduledRestartRequested = true; SendManagedStop(session, true); result = ManagedText("재시작 요청 완료", "Restart requested"); }
				}
				else
				{
					ValidateScheduledCommand(claim.Job.Command);
					if (session == null || !IsManagedSessionRunning(session)) throw new InvalidOperationException("예약 명령을 실행할 서버가 꺼져 있습니다.");
					SendManagedCommand(session, claim.Job.Command);
					result = ManagedText("명령 전송 완료", "Command sent");
				}
			}
			catch (OperationCanceledException) { result = ManagedText("취소됨", "Cancelled"); throw; }
			catch (Exception exception) { result = ManagedText("실패: ", "Failed: ") + exception.Message; }
			finally
			{
				CompleteAutomationJob(claim, DateTime.UtcNow, result);
			}
		}

		private static async Task AnnounceAutomationActionAsync(ManagedServerSession session, int warningSeconds, string message, CancellationToken cancellationToken)
		{
			if (warningSeconds <= 0) return;
			SendManagedCommand(session, "say " + message + " " + warningSeconds.ToString(CultureInfo.InvariantCulture) + "s");
			await Task.Delay(TimeSpan.FromSeconds(warningSeconds), cancellationToken);
		}

		private static void SendManagedCommand(ManagedServerSession session, string command)
		{
			if (session == null || !IsManagedSessionRunning(session)) throw new InvalidOperationException("서버가 실행 중이 아닙니다.");
			lock (session.SyncRoot)
			{
				session.Process.StandardInput.WriteLine(command);
				session.Process.StandardInput.Flush();
			}
		}

		private static void SendManagedStop(ManagedServerSession session, bool scheduledRestart)
		{
			session.StopRequested = true;
			session.ScheduledRestartRequested = scheduledRestart;
			session.Status = scheduledRestart ? ManagedText("예약 재시작 중", "Scheduled restart") : ManagedText("예약 종료 중", "Scheduled stop");
			SendManagedCommand(session, "stop");
		}
	}

	private sealed partial class LauncherForm
	{
		private Button mainScheduleButton;
		private Button mainDashboardButton;
		private System.Windows.Forms.Timer mainAutomationTimer;
		private CancellationTokenSource mainAutomationCancellation;
		private readonly SemaphoreSlim mainAutomationGate = new SemaphoreSlim(1, 1);
		private bool mainAutomationPolling;
		private bool mainScheduledRestart;
		private bool mainStartHookRunning;
		public bool DashboardServerRunning { get { return serverRunning; } }
		public string[] DashboardConsoleLines { get { return consoleHistory.ToArray(); } }
		public string DashboardAddress { get { return addressBox == null ? string.Empty : addressBox.Text; } }

		private void InitializeMainAutomation()
		{
			mainAutomationCancellation = new CancellationTokenSource();
			mainAutomationTimer = new System.Windows.Forms.Timer { Interval = 5000 };
			mainAutomationTimer.Tick += delegate { PollMainAutomationAsync(); };
			Shown += delegate { if (mainAutomationTimer != null) mainAutomationTimer.Start(); };
		}

		private void DisposeMainAutomation()
		{
			if (mainAutomationTimer != null) { mainAutomationTimer.Stop(); mainAutomationTimer.Dispose(); mainAutomationTimer = null; }
			if (mainAutomationCancellation != null) { mainAutomationCancellation.Cancel(); mainAutomationCancellation.Dispose(); mainAutomationCancellation = null; }
		}

		private void OpenMainAutomationManager()
		{
			string root;
			string directory;
			ReadActiveLauncherOptions(out root, out directory);
			using (AutomationManagerForm form = new AutomationManagerForm(directory)) form.ShowDialog(this);
		}

		private void OpenMainStatusDashboard()
		{
			string root;
			string directory;
			LauncherOptions options = ReadActiveLauncherOptions(out root, out directory);
			ManagedProfileRecord profile = new ManagedProfileRecord { Name = options.ProfileName, Directory = options.ServerDirectory, ServerType = options.ServerType, MinecraftVersion = options.MinecraftVersion, Port = ReadConfiguredServerPort(Path.Combine(options.ServerDirectory, "server.properties"), 25565), MemoryGb = options.MemoryGb };
			using (ServerStatusDashboardForm form = new ServerStatusDashboardForm(profile, null, true)) form.ShowDialog(this);
		}

		private async void StartWorkflowWithAutomationBackup()
		{
			if (mainStartHookRunning || workflowRunning || serverRunning) return;
			mainStartHookRunning = true;
			try
			{
				string root;
				string directory;
				ReadActiveLauncherOptions(out root, out directory);
				ServerAutomationConfiguration configuration = ReadServerAutomationConfiguration(directory);
				if (configuration.BackupBeforeStart) await CreateStandalonePolicyBackupAsync(directory, configuration, "before-start", mainAutomationCancellation.Token);
				if (!IsDisposed && mainAutomationCancellation != null && !mainAutomationCancellation.IsCancellationRequested) StartWorkflow();
			}
			catch (OperationCanceledException) { }
			catch (Exception exception) { if (!IsDisposed) ShowNotice(ManagedText("시작 전 백업 실패: ", "Pre-start backup failed: ") + exception.Message, true); }
			finally { mainStartHookRunning = false; }
		}

		private async void PollMainAutomationAsync()
		{
			if (mainAutomationPolling || mainAutomationCancellation == null || mainAutomationCancellation.IsCancellationRequested) return;
			mainAutomationPolling = true;
			try
			{
				string root;
				string directory;
				ReadActiveLauncherOptions(out root, out directory);
				List<AutomationJobClaim> claims = ClaimDueAutomationJobs(directory, DateTime.UtcNow);
				for (int i = 0; i < claims.Count; i++) ObserveMainAutomationJobAsync(claims[i]);
				await Task.Yield();
			}
			catch (InvalidOperationException) { }
			catch (InvalidDataException exception) { ShowNotice(ManagedText("자동화 설정 오류: ", "Automation configuration error: ") + exception.Message, true); }
			catch (Exception exception) { Console.WriteLine("[Automation] " + exception.Message); }
			finally { mainAutomationPolling = false; }
		}

		private async void ObserveMainAutomationJobAsync(AutomationJobClaim claim)
		{
			string result = ManagedText("취소됨", "Cancelled");
			bool entered = false;
			try
			{
				await mainAutomationGate.WaitAsync(mainAutomationCancellation.Token);
				entered = true;
				ServerAutomationConfiguration configuration = ReadServerAutomationConfiguration(claim.ServerDirectory);
				if (string.Equals(claim.Job.Action, "backup", StringComparison.OrdinalIgnoreCase))
				{
					bool pausedSaves = serverRunning;
					try
					{
						if (pausedSaves) { SendServerCommand("save-off"); SendServerCommand("save-all flush"); await Task.Delay(1000, mainAutomationCancellation.Token); }
						string path = await CreateStandalonePolicyBackupAsync(claim.ServerDirectory, configuration, "scheduled", mainAutomationCancellation.Token);
						result = ManagedText("백업 완료: ", "Backup completed: ") + Path.GetFileName(path);
					}
					finally { if (pausedSaves && serverRunning) SendServerCommand("save-on"); }
				}
				else if (string.Equals(claim.Job.Action, "start", StringComparison.OrdinalIgnoreCase))
				{
					if (serverRunning || workflowRunning) result = ManagedText("이미 실행 중", "Already running");
					else { StartWorkflowWithAutomationBackup(); result = ManagedText("시작 요청 완료", "Start requested"); }
				}
				else if (string.Equals(claim.Job.Action, "command", StringComparison.OrdinalIgnoreCase))
				{
					ValidateScheduledCommand(claim.Job.Command);
					if (!serverRunning || !SendServerCommand(claim.Job.Command)) throw new InvalidOperationException("예약 명령을 실행할 서버가 꺼져 있습니다.");
					result = ManagedText("명령 전송 완료", "Command sent");
				}
				else
				{
					bool restart = string.Equals(claim.Job.Action, "restart", StringComparison.OrdinalIgnoreCase);
					if (!serverRunning)
					{
						if (restart) StartWorkflowWithAutomationBackup();
						result = restart ? ManagedText("중지 상태에서 시작 요청 완료", "Start requested from stopped state") : ManagedText("이미 중지됨", "Already stopped");
					}
					else
					{
						if (claim.Job.WarningSeconds > 0)
						{
							SendServerCommand("say " + (restart ? ManagedText("서버가 곧 재시작됩니다. ", "Server will restart soon. ") : ManagedText("서버가 곧 종료됩니다. ", "Server will stop soon. ")) + claim.Job.WarningSeconds.ToString(CultureInfo.InvariantCulture) + "s");
							await Task.Delay(TimeSpan.FromSeconds(claim.Job.WarningSeconds), mainAutomationCancellation.Token);
						}
						mainScheduledRestart = restart;
						if (!SendServerCommand("stop")) throw new InvalidOperationException("서버에 종료 명령을 보내지 못했습니다.");
						result = restart ? ManagedText("재시작 요청 완료", "Restart requested") : ManagedText("종료 요청 완료", "Stop requested");
					}
				}
			}
			catch (OperationCanceledException) { result = ManagedText("취소됨", "Cancelled"); }
			catch (Exception exception) { result = ManagedText("실패: ", "Failed: ") + exception.Message; }
			finally
			{
				if (entered) mainAutomationGate.Release();
				try { CompleteAutomationJob(claim, DateTime.UtcNow, result); } catch (Exception exception) { Console.WriteLine("[Automation] 결과 저장 실패: " + exception.Message); }
			}
		}

		private async void HandleMainWorkflowFinishedAutomation(int exitCode, bool canceled)
		{
			if (canceled || closeAfterStop || mainAutomationCancellation == null || mainAutomationCancellation.IsCancellationRequested) return;
			bool restart = mainScheduledRestart;
			mainScheduledRestart = false;
			try
			{
				string root;
				string directory;
				ReadActiveLauncherOptions(out root, out directory);
				ServerAutomationConfiguration configuration = ReadServerAutomationConfiguration(directory);
				if (Interlocked.CompareExchange(ref currentServerStopRequested, 0, 0) != 0 && configuration.BackupAfterStop) await CreateStandalonePolicyBackupAsync(directory, configuration, "after-stop", mainAutomationCancellation.Token);
				if (restart && !IsDisposed) StartWorkflowWithAutomationBackup();
			}
			catch (OperationCanceledException) { }
			catch (Exception exception) { if (!IsDisposed) ShowNotice(ManagedText("종료 후 자동화 실패: ", "Post-stop automation failed: ") + exception.Message, true); }
		}

		private static async Task<string> CreateStandalonePolicyBackupAsync(string serverDirectory, ServerAutomationConfiguration configuration, string reason, CancellationToken cancellationToken)
		{
			return await Task.Run(delegate
			{
				cancellationToken.ThrowIfCancellationRequested();
				string path = CreateComprehensiveServerBackup(serverDirectory, configuration.RetentionCount, reason);
				PruneServerBackupsWithPolicy(GetServerBackupDirectory(serverDirectory), configuration.RetentionCount, configuration.RetentionDays, configuration.RetentionMaximumBytes, DateTime.UtcNow);
				return path;
			}, cancellationToken);
		}
	}

	private sealed class ServerStatusDashboardForm : Form
	{
		private readonly ManagedProfileRecord profile;
		private readonly ManagedServerSession session;
		private readonly bool currentMainServer;
		private readonly TableLayoutPanel values;
		private readonly TextBox problemsBox;
		private readonly Label refreshedLabel;
		private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
		private readonly System.Windows.Forms.Timer timer;
		private bool refreshing;

		public ServerStatusDashboardForm(ManagedProfileRecord selectedProfile, ManagedServerSession selectedSession)
			: this(selectedProfile, selectedSession, false)
		{
		}

		public ServerStatusDashboardForm(ManagedProfileRecord selectedProfile, ManagedServerSession selectedSession, bool useCurrentMainServer)
		{
			profile = selectedProfile;
			session = selectedSession;
			currentMainServer = useCurrentMainServer;
			bool korean = IsManagedKorean();
			Text = korean ? "서버 대시보드 - " + profile.Name : "Server dashboard - " + profile.Name;
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(760, 620);
			Size = new Size(920, 720);
			AutoScaleMode = AutoScaleMode.Dpi;
			Font = new Font("Pretendard", 10.5F);
			ApplyLauncherWindowIcon(this);
			TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4 };
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 70)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 30)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(root);
			Label heading = new Label { Text = korean ? "실시간 상태와 진단" : "Live status and diagnostics", AutoSize = true, Font = new Font("Pretendard", 17F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 14) };
			root.Controls.Add(heading, 0, 0);
			values = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, RowCount = 12, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
			values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
			string[] names = korean ? new[] { "상태", "가동 시간", "CPU", "메모리", "Java", "접속 플레이어", "서버 용량", "월드 용량", "백업 용량", "외부 접속", "다음 예약", "TPS / MSPT" } : new[] { "Status", "Uptime", "CPU", "Memory", "Java", "Online players", "Server size", "World size", "Backup size", "External access", "Next schedule", "TPS / MSPT" };
			for (int i = 0; i < names.Length; i++)
			{
				values.Controls.Add(new Label { Text = names[i], Dock = DockStyle.Fill, Padding = new Padding(8), AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, i);
				values.Controls.Add(new Label { Text = ManagedText("수집 중…", "Collecting…"), Dock = DockStyle.Fill, Padding = new Padding(8), AutoSize = true, AccessibleName = names[i] }, 1, i);
			}
			root.Controls.Add(values, 0, 1);
			problemsBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = korean ? "최근 경고와 오류" : "Recent warnings and errors" };
			root.Controls.Add(problemsBox, 0, 2);
			refreshedLabel = new Label { Dock = DockStyle.Fill, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
			root.Controls.Add(refreshedLabel, 0, 3);
			timer = new System.Windows.Forms.Timer { Interval = 3000 };
			timer.Tick += delegate { RefreshSnapshotAsync(); };
			Shown += delegate { timer.Start(); RefreshSnapshotAsync(); };
			FormClosed += delegate { timer.Stop(); timer.Dispose(); cancellation.Cancel(); cancellation.Dispose(); };
			ApplySimpleDialogTheme(this);
		}

		private async void RefreshSnapshotAsync()
		{
			if (refreshing || IsDisposed || cancellation.IsCancellationRequested) return;
			refreshing = true;
			try
			{
				ServerStatusSnapshot snapshot = await CollectServerStatusForModeAsync(profile, session, currentMainServer, cancellation.Token);
				if (IsDisposed || cancellation.IsCancellationRequested) return;
				string[] displayed = { snapshot.Status, snapshot.Uptime, snapshot.Cpu, snapshot.Memory, snapshot.JavaVersion, snapshot.Players, snapshot.ServerSize, snapshot.WorldSize, snapshot.BackupSize, snapshot.ExternalAccess, snapshot.NextAutomation, snapshot.TickHealth };
				for (int i = 0; i < displayed.Length; i++) values.GetControlFromPosition(1, i).Text = displayed[i];
				problemsBox.Text = snapshot.RecentProblems.Length == 0 ? ManagedText("최근 경고 또는 오류가 없습니다.", "No recent warnings or errors.") : string.Join(Environment.NewLine, snapshot.RecentProblems);
				refreshedLabel.Text = ManagedText("최근 갱신: ", "Last refreshed: ") + DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
			}
			catch (OperationCanceledException) { }
			catch (Exception exception) { if (!IsDisposed) refreshedLabel.Text = ManagedText("상태 수집 실패: ", "Status collection failed: ") + exception.Message; }
			finally { refreshing = false; }
		}
	}

	private static async Task<ServerStatusSnapshot> CollectServerStatusAsync(ManagedProfileRecord profile, ManagedServerSession session, CancellationToken cancellationToken)
	{
		return await CollectServerStatusForModeAsync(profile, session, false, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<ServerStatusSnapshot> CollectServerStatusForModeAsync(ManagedProfileRecord profile, ManagedServerSession session, bool currentMainServer, CancellationToken cancellationToken)
	{
		LauncherForm mainForm = currentMainServer ? launcherForm : null;
		bool mainRunning = mainForm != null && mainForm.DashboardServerRunning;
		string[] mainLines = mainForm == null ? new string[0] : mainForm.DashboardConsoleLines;
		string mainAddress = mainForm == null ? string.Empty : mainForm.DashboardAddress;
		return await Task.Run(delegate
		{
			cancellationToken.ThrowIfCancellationRequested();
			ServerStatusSnapshot result = new ServerStatusSnapshot();
			bool running = currentMainServer ? mainRunning : IsManagedSessionRunning(session);
			result.Status = running ? (currentMainServer ? ManagedText("온라인", "Online") : GetManagedStatus(session)) : ManagedText("꺼짐", "Stopped");
			Process javaProcess = currentMainServer ? TryGetCurrentJavaProcess() : TryGetManagedJavaProcess(session);
			result.Uptime = running && javaProcess != null ? FormatStatusDuration(DateTime.Now - javaProcess.StartTime) : running && session != null ? FormatStatusDuration(DateTime.UtcNow - session.StartedUtc) : ManagedText("지원되지 않음(서버 꺼짐)", "Unavailable (server stopped)");
			try
			{
				if (javaProcess == null)
				{
					result.Cpu = result.Memory = result.JavaVersion = ManagedText("지원되지 않음", "Unsupported");
				}
				else
				{
					TimeSpan firstCpu = javaProcess.TotalProcessorTime;
					DateTime firstTime = DateTime.UtcNow;
					cancellationToken.WaitHandle.WaitOne(250);
					cancellationToken.ThrowIfCancellationRequested();
					javaProcess.Refresh();
					double wallSeconds = Math.Max(0.001, (DateTime.UtcNow - firstTime).TotalSeconds);
					double cpu = (javaProcess.TotalProcessorTime - firstCpu).TotalSeconds / wallSeconds / Math.Max(1, Environment.ProcessorCount) * 100.0;
					result.Cpu = Math.Max(0.0, Math.Min(100.0, cpu)).ToString("0.0", CultureInfo.CurrentCulture) + "%";
					result.Memory = FormatBackupSize(javaProcess.WorkingSet64);
					try { result.JavaVersion = javaProcess.MainModule.FileVersionInfo.ProductVersion; } catch { result.JavaVersion = ManagedText("확인 권한 없음", "Permission unavailable"); }
					if (string.IsNullOrWhiteSpace(result.JavaVersion)) result.JavaVersion = ManagedText("확인할 수 없음", "Unavailable");
				}
			}
			finally { if (javaProcess != null) javaProcess.Dispose(); }
			if (currentMainServer)
			{
				CommandBridgeSession bridge = GetActiveCommandBridge();
				result.Players = bridge != null && bridge.Connected ? bridge.Players.Length.ToString(CultureInfo.InvariantCulture) + (bridge.Players.Length == 0 ? string.Empty : " · " + string.Join(", ", bridge.Players)) : ManagedText("브리지 미연결", "Bridge disconnected");
				result.ExternalAccess = GetExternalAccessStatus(mainLines, mainAddress);
				result.TickHealth = GetMainTickHealthStatus(bridge, running);
				result.RecentProblems = GetRecentProblems(mainLines);
			}
			else lock (session == null ? new object() : session.SyncRoot)
			{
				result.Players = session == null ? ManagedText("지원되지 않음", "Unsupported") : session.Players.Count.ToString(CultureInfo.InvariantCulture) + (session.Players.Count == 0 ? string.Empty : " · " + string.Join(", ", session.Players.ToArray()));
				result.ExternalAccess = GetExternalAccessStatus(session);
				result.TickHealth = GetTickHealthStatus(session);
				result.RecentProblems = GetRecentManagedProblems(session);
			}
			result.ServerSize = TryFormatDirectorySize(profile.Directory, cancellationToken);
			result.WorldSize = TryFormatWorldSize(profile.Directory, cancellationToken);
			result.BackupSize = TryFormatDirectorySize(GetServerBackupDirectory(profile.Directory), cancellationToken);
			result.NextAutomation = GetNextAutomationText(profile.Directory);
			return result;
		}, cancellationToken).ConfigureAwait(false);
	}

	private static Process TryGetCurrentJavaProcess()
	{
		try
		{
			lock (ServerProcessLock)
			{
				if (currentServerProcess == null || currentServerProcess.HasExited) return null;
				Process copy = Process.GetProcessById(currentServerProcess.Id);
				if (copy.HasExited || copy.StartTime != currentServerProcess.StartTime) { copy.Dispose(); return null; }
				return copy;
			}
		}
		catch { return null; }
	}

	private static Process TryGetManagedJavaProcess(ManagedServerSession session)
	{
		if (session == null || session.ChildPid <= 0) return null;
		try
		{
			Process process = Process.GetProcessById(session.ChildPid);
			if (session.ChildStartTime > 0 && process.StartTime.Ticks != session.ChildStartTime) { process.Dispose(); return null; }
			if (process.HasExited) { process.Dispose(); return null; }
			return process;
		}
		catch { return null; }
	}

	private static string FormatStatusDuration(TimeSpan value)
	{
		if (value < TimeSpan.Zero) value = TimeSpan.Zero;
		return string.Format(CultureInfo.CurrentCulture, ManagedText("{0}일 {1:00}:{2:00}:{3:00}", "{0}d {1:00}:{2:00}:{3:00}"), (int)value.TotalDays, value.Hours, value.Minutes, value.Seconds);
	}

	private static string TryFormatDirectorySize(string directory, CancellationToken cancellationToken)
	{
		try { return Directory.Exists(directory) ? FormatBackupSize(CalculateSafeDirectorySize(directory, cancellationToken)) : ManagedText("없음", "Not found"); }
		catch (UnauthorizedAccessException) { return ManagedText("권한 없음", "Permission unavailable"); }
		catch (IOException) { return ManagedText("사용 중", "Unavailable while in use"); }
	}

	private static long CalculateSafeDirectorySize(string directory, CancellationToken cancellationToken)
	{
		long total = 0L;
		Stack<string> pending = new Stack<string>(); pending.Push(Path.GetFullPath(directory));
		while (pending.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string current = pending.Pop();
			DirectoryInfo currentInfo = new DirectoryInfo(current);
			if ((currentInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;
			foreach (string file in Directory.GetFiles(current)) total = checked(total + new FileInfo(file).Length);
			foreach (string child in Directory.GetDirectories(current)) pending.Push(child);
		}
		return total;
	}

	private static string TryFormatWorldSize(string serverDirectory, CancellationToken cancellationToken)
	{
		try
		{
			long total = 0L;
				List<string> worlds = GetDatapackWorldDirectories(serverDirectory);
			for (int i = 0; i < worlds.Count; i++) total = checked(total + CalculateSafeDirectorySize(worlds[i], cancellationToken));
			return FormatBackupSize(total);
		}
		catch { return ManagedText("확인할 수 없음", "Unavailable"); }
	}

	private static string GetNextAutomationText(string serverDirectory)
	{
		try
		{
			ServerAutomationConfiguration configuration = ReadServerAutomationConfiguration(serverDirectory);
			ServerAutomationJob nextJob = null; DateTime nextTime = DateTime.MaxValue;
			for (int i = 0; i < configuration.Jobs.Count; i++)
			{
				DateTime candidate;
				if (configuration.Jobs[i].Enabled && DateTime.TryParse(configuration.Jobs[i].NextRunUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out candidate) && candidate.ToUniversalTime() < nextTime) { nextJob = configuration.Jobs[i]; nextTime = candidate.ToUniversalTime(); }
			}
			return nextJob == null ? ManagedText("예약 없음", "No scheduled jobs") : nextJob.Name + " · " + nextTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
		}
		catch (InvalidDataException) { return ManagedText("설정 손상", "Configuration corrupted"); }
	}

	private static string GetExternalAccessStatus(ManagedServerSession session)
	{
		if (session == null) return ManagedText("지원되지 않음", "Unsupported");
		for (int i = session.Lines.Count - 1; i >= 0 && i >= session.Lines.Count - 1000; i--)
		{
			string line = session.Lines[i];
			if (IsManagedExternalAccessFailureLine(line)) return ManagedText("확인 실패", "Verification failed");
			if (line.IndexOf("[외부 접속 확인] 성공", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("UPnP 매핑 성공", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("기존 포트포워딩 정상", StringComparison.OrdinalIgnoreCase) >= 0) return ManagedText("확인됨 · ", "Verified · ") + session.Address;
		}
		return ManagedText("아직 확인되지 않음", "Not verified yet");
	}

	private static string GetExternalAccessStatus(string[] lines, string address)
	{
		for (int i = lines.Length - 1; i >= 0 && i >= lines.Length - 1000; i--)
		{
			if (IsManagedExternalAccessFailureLine(lines[i])) return ManagedText("확인 실패", "Verification failed");
			if (lines[i].IndexOf("[외부 접속 확인] 성공", StringComparison.OrdinalIgnoreCase) >= 0 || lines[i].IndexOf("UPnP 매핑 성공", StringComparison.OrdinalIgnoreCase) >= 0 || lines[i].IndexOf("기존 포트포워딩 정상", StringComparison.OrdinalIgnoreCase) >= 0) return ManagedText("확인됨 · ", "Verified · ") + address;
		}
		return ManagedText("아직 확인되지 않음", "Not verified yet");
	}

	private static string GetTickHealthStatus(ManagedServerSession session)
	{
		if (session == null || !IsManagedSessionRunning(session)) return ManagedText("지원되지 않음(서버 꺼짐)", "Unavailable (server stopped)");
		if (!session.MetricsAvailable) return ManagedText("브리지 미연결 또는 미지원", "Bridge disconnected or unsupported");
		if (DateTime.UtcNow - session.MetricsReceivedUtc > TimeSpan.FromSeconds(15)) return ManagedText("브리지 연결 끊김", "Bridge disconnected");
		return string.Format(CultureInfo.CurrentCulture, "TPS {0:0.00} / {1:0.00} / {2:0.00} · MSPT {3:0.00}", session.Tps1, session.Tps5, session.Tps15, session.Mspt);
	}

	private static string GetMainTickHealthStatus(CommandBridgeSession bridge, bool running)
	{
		if (!running) return ManagedText("지원되지 않음(서버 꺼짐)", "Unavailable (server stopped)");
		if (bridge == null || !bridge.Connected) return ManagedText("브리지 연결 끊김", "Bridge disconnected");
		if (!bridge.MetricsAvailable) return ManagedText("서버가 TPS/MSPT를 지원하지 않음", "Server does not support TPS/MSPT");
		return string.Format(CultureInfo.CurrentCulture, "TPS {0:0.00} / {1:0.00} / {2:0.00} · MSPT {3:0.00}", bridge.Tps1, bridge.Tps5, bridge.Tps15, bridge.Mspt);
	}

	private static string[] GetRecentManagedProblems(ManagedServerSession session)
	{
		if (session == null) return new string[0];
		List<string> result = new List<string>();
		for (int i = session.Lines.Count - 1; i >= 0 && result.Count < 12; i--)
		{
			string line = session.Lines[i];
			if (line.IndexOf("warn", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("실패", StringComparison.OrdinalIgnoreCase) >= 0) result.Add(line.Length > 600 ? line.Substring(0, 600) : line);
		}
		result.Reverse(); return result.ToArray();
	}

	private static string[] GetRecentProblems(string[] lines)
	{
		List<string> result = new List<string>();
		for (int i = lines.Length - 1; i >= 0 && result.Count < 12; i--)
		{
			string line = lines[i] ?? string.Empty;
			if (line.IndexOf("warn", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("실패", StringComparison.OrdinalIgnoreCase) >= 0) result.Add(line.Length > 600 ? line.Substring(0, 600) : line);
		}
		result.Reverse(); return result.ToArray();
	}
}
