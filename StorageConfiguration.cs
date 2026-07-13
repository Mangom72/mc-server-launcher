using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static partial class Launcher
{
	private const string StorageSettingsFileName = "launcher-storage.properties";
	private const string StorageModeUser = "user";
	private const string StorageModePortable = "portable";
	private const string StorageModeCustom = "custom";
	private const string LauncherUpdateResultFileName = "launcher-update-result.properties";
	private const string UserDataDirectoryName = "MineHarbor";
	private const string LegacyUserDataDirectoryName = "MinecraftServerLauncher";
	// 테스트가 실제 사용자 설정 파일을 건드리지 않도록 경로만 교체하는 내부 지점입니다.
	private static string StorageSettingsPathOverride = null;

	private sealed class DataStorageSettings
	{
		public string Mode;
		public string CustomPath;
	}

	private static string GetLauncherUserDataDirectory()
	{
		string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string current = Path.Combine(localApplicationData, UserDataDirectoryName);
		string legacy = Path.Combine(localApplicationData, LegacyUserDataDirectoryName);
		// 기존 설치의 서버와 설정을 자동 이동하지 않고, 발견한 예전 데이터 폴더를 그대로 사용합니다.
		if (!Directory.Exists(current) && Directory.Exists(legacy)) return legacy;
		return current;
	}

	private static string GetStorageSettingsPath()
	{
		if (!string.IsNullOrEmpty(StorageSettingsPathOverride)) return Path.GetFullPath(StorageSettingsPathOverride);
		return Path.Combine(GetLauncherUserDataDirectory(), StorageSettingsFileName);
	}

	private static void WriteLauncherUpdateResult(string status, string message)
	{
		try
		{
			string directory = GetLauncherUserDataDirectory();
			Directory.CreateDirectory(directory);
			string safeMessage = (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
			File.WriteAllText(Path.Combine(directory, LauncherUpdateResultFileName), "status=" + status + "\r\nmessage=" + safeMessage + "\r\n", new UTF8Encoding(false));
		}
		catch
		{
		}
	}

	private static string ConsumeLauncherUpdateResult()
	{
		string path = Path.Combine(GetLauncherUserDataDirectory(), LauncherUpdateResultFileName);
		if (!File.Exists(path)) return null;
		try
		{
			var values = ReadSimpleProperties(path);
			string status = values.ContainsKey("status") ? values["status"] : string.Empty;
			string detail = values.ContainsKey("message") ? values["message"] : string.Empty;
			if (status == "restore-success") return LauncherUiText("새 버전 실행에 실패해 기존 런처를 복구했습니다. ", "The new version did not start, so the previous launcher was restored. ") + detail;
			if (status == "restore-failed") return LauncherUiText("런처 교체와 기존 버전 복구가 모두 실패했습니다. 설치 파일로 복구해 주세요. ", "Both replacement and rollback failed. Repair the launcher with the installer. ") + detail;
			if (status == "replacement-failed") return LauncherUiText("런처 파일 교체에 실패해 기존 버전을 계속 실행합니다. ", "Launcher replacement failed, so the existing version is still running. ") + detail;
			return null;
		}
		finally
		{
			try { File.Delete(path); } catch { }
		}
	}

	private static bool EnsureDataRootSelected(IWin32Window owner, string baseDirectory)
	{
		DataStorageSettings current;
		string resolved;
		string error;
		if (TryReadDataStorageSettings(baseDirectory, out current, out resolved, out error))
		{
			return true;
		}
		using (DataLocationForm dialog = new DataLocationForm(baseDirectory, current, error))
		{
			if (dialog.ShowDialog(owner) != DialogResult.OK)
			{
				return false;
			}
			SaveDataStorageSettings(dialog.SelectedMode, dialog.SelectedCustomPath);
			return true;
		}
	}

	private static string ResolveServersRootDirectory(string baseDirectory)
	{
		DataStorageSettings settings;
		string resolved;
		string error;
		if (TryReadDataStorageSettings(baseDirectory, out settings, out resolved, out error))
		{
			return resolved;
		}
		string portable = Path.Combine(Path.GetFullPath(baseDirectory), MultiServerRootDirectoryName);
		if (Directory.Exists(portable))
		{
			return portable;
		}
		return GetLauncherUserDataDirectory();
	}

	private static bool TryReadDataStorageSettings(string baseDirectory, out DataStorageSettings settings, out string resolvedPath, out string error)
	{
		settings = new DataStorageSettings();
		resolvedPath = null;
		error = null;
		string settingsPath = GetStorageSettingsPath();
		if (!File.Exists(settingsPath))
		{
			return false;
		}
		try
		{
			var values = ReadSimpleProperties(settingsPath);
			settings.Mode = values.ContainsKey("mode") ? values["mode"] : string.Empty;
			settings.CustomPath = values.ContainsKey("custom-path") ? values["custom-path"] : string.Empty;
			string candidate;
			if (string.Equals(settings.Mode, StorageModeUser, StringComparison.OrdinalIgnoreCase))
			{
				candidate = GetLauncherUserDataDirectory();
			}
			else if (string.Equals(settings.Mode, StorageModePortable, StringComparison.OrdinalIgnoreCase))
			{
				candidate = Path.Combine(Path.GetFullPath(baseDirectory), MultiServerRootDirectoryName);
			}
			else if (string.Equals(settings.Mode, StorageModeCustom, StringComparison.OrdinalIgnoreCase))
			{
				candidate = settings.CustomPath;
			}
			else
			{
				error = "저장된 데이터 위치 방식이 올바르지 않습니다.";
				return false;
			}
			return TryValidateDataRoot(candidate, out resolvedPath, out error);
		}
		catch (Exception exception)
		{
			error = exception.Message;
			return false;
		}
	}

	private static void SaveDataStorageSettings(string mode, string customPath)
	{
		if (!string.Equals(mode, StorageModeUser, StringComparison.Ordinal) &&
			!string.Equals(mode, StorageModePortable, StringComparison.Ordinal) &&
			!string.Equals(mode, StorageModeCustom, StringComparison.Ordinal))
		{
			throw new InvalidDataException("데이터 저장 위치 방식을 인식할 수 없습니다.");
		}
		Directory.CreateDirectory(GetLauncherUserDataDirectory());
		string path = GetStorageSettingsPath();
		string temporary = path + ".tmp";
		string contents = "storage-settings-version=1\r\nmode=" + mode + "\r\ncustom-path=" + (customPath ?? string.Empty) + "\r\n";
		File.WriteAllText(temporary, contents, new UTF8Encoding(false));
		if (File.Exists(path))
		{
			File.Replace(temporary, path, null);
		}
		else
		{
			File.Move(temporary, path);
		}
	}

	private static bool TryValidateDataRoot(string candidate, out string normalized, out string error)
	{
		normalized = null;
		error = null;
		if (string.IsNullOrWhiteSpace(candidate))
		{
			error = "데이터를 저장할 폴더를 선택해 주세요.";
			return false;
		}
		try
		{
			normalized = Path.GetFullPath(candidate.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (normalized.Length == 0 || string.Equals(normalized, Path.GetPathRoot(normalized).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
			{
				error = "드라이브 최상위 폴더는 데이터 위치로 사용할 수 없습니다.";
				return false;
			}
			if (IsDangerousDataRoot(normalized))
			{
				error = "Windows 또는 프로그램 시스템 폴더는 데이터 위치로 사용할 수 없습니다.";
				return false;
			}
			Directory.CreateDirectory(normalized);
			if ((File.GetAttributes(normalized) & FileAttributes.ReparsePoint) != 0)
			{
				error = "연결 또는 재분석 지점 폴더는 데이터 위치로 사용할 수 없습니다.";
				return false;
			}
			string probe = Path.Combine(normalized, ".launcher-write-test-" + Guid.NewGuid().ToString("N"));
			try
			{
				File.WriteAllText(probe, "ok", new UTF8Encoding(false));
				using (FileStream stream = new FileStream(probe, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					if (stream.Length != 2) throw new IOException("쓰기 확인 파일을 읽지 못했습니다.");
				}
			}
			finally
			{
				if (File.Exists(probe)) File.Delete(probe);
			}
			return true;
		}
		catch (Exception exception)
		{
			error = "선택한 폴더를 사용할 수 없습니다: " + exception.Message;
			return false;
		}
	}

	private static bool IsDangerousDataRoot(string path)
	{
		string[] protectedRoots = new string[]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.Windows),
			Environment.GetFolderPath(Environment.SpecialFolder.System),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
		};
		for (int i = 0; i < protectedRoots.Length; i++)
		{
			if (string.IsNullOrEmpty(protectedRoots[i])) continue;
			string protectedPath = Path.GetFullPath(protectedRoots[i]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (string.Equals(path, protectedPath, StringComparison.OrdinalIgnoreCase) || path.StartsWith(protectedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private sealed class DataLocationForm : Form
	{
		private readonly string baseDirectory;
		private readonly RadioButton userOption;
		private readonly RadioButton portableOption;
		private readonly RadioButton customOption;
		private readonly TextBox customPathBox;
		private readonly Label validationLabel;

		public string SelectedMode { get; private set; }
		public string SelectedCustomPath { get; private set; }

		public DataLocationForm(string executableDirectory, DataStorageSettings current, string initialError)
		{
			ApplyLauncherWindowIcon(this);
			baseDirectory = Path.GetFullPath(executableDirectory);
			bool korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
			Text = korean ? "서버 데이터 위치" : "Server data location";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(700, 500);
			Size = new Size(760, 560);
			Font = new Font("Segoe UI Variable Text", 10F);
			AutoScaleMode = AutoScaleMode.Dpi;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(28);
			root.ColumnCount = 1;
			root.RowCount = 7;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
			Controls.Add(root);

			Label heading = new Label();
			heading.Text = korean ? "서버 데이터를 어디에 저장할까요?" : "Where should server data be stored?";
			heading.Font = new Font("Segoe UI Variable Display Semib", 18F);
			heading.Dock = DockStyle.Top;
			heading.Height = 38;
			Label hint = new Label();
			hint.Text = korean ? "기존 데이터는 이동하거나 삭제하지 않습니다." : "Existing data is never moved or deleted automatically.";
			hint.Dock = DockStyle.Bottom;
			hint.Height = 26;
			Panel header = new Panel();
			header.Dock = DockStyle.Fill;
			header.Controls.Add(heading);
			header.Controls.Add(hint);
			root.Controls.Add(header, 0, 0);

			string userPath = GetLauncherUserDataDirectory();
			string portablePath = Path.Combine(baseDirectory, MultiServerRootDirectoryName);
			userOption = CreateLocationOption(korean ? "사용자 데이터 폴더" : "User data folder", userPath);
			portableOption = CreateLocationOption(korean ? "Portable 데이터 폴더" : "Portable data folder", portablePath + (Directory.Exists(portablePath) ? (korean ? " · 기존 데이터 발견" : " · existing data found") : string.Empty));
			customOption = CreateLocationOption(korean ? "사용자 지정 폴더" : "Custom folder", korean ? "직접 선택한 안전한 폴더를 사용합니다." : "Use a safe folder selected by you.");
			root.Controls.Add(userOption.Parent, 0, 1);
			root.Controls.Add(portableOption.Parent, 0, 2);
			root.Controls.Add(customOption.Parent, 0, 3);

			Panel customPanel = new Panel();
			customPanel.Dock = DockStyle.Fill;
			customPathBox = new TextBox();
			customPathBox.Location = new Point(34, 8);
			customPathBox.Width = 560;
			customPathBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
			customPanel.Controls.Add(customPathBox);
			Button browse = new RoundedButton();
			ApplyButtonIcon(browse, ButtonIcon.Folder);
			browse.Text = korean ? "찾기" : "Browse";
			browse.Tag = "secondary";
			browse.Size = new Size(92, 38);
			browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			browse.Location = new Point(customPanel.Width - 92, 2);
			browse.Click += delegate { BrowseCustomPath(); };
			customPanel.Controls.Add(browse);
			customPanel.Resize += delegate
			{
				browse.Left = customPanel.ClientSize.Width - browse.Width;
				customPathBox.Width = Math.Max(160, browse.Left - customPathBox.Left - 12);
			};
			root.Controls.Add(customPanel, 0, 4);

			validationLabel = new Label();
			validationLabel.Dock = DockStyle.Fill;
			validationLabel.ForeColor = Color.FromArgb(220, 70, 80);
			validationLabel.TextAlign = ContentAlignment.TopLeft;
			validationLabel.Text = initialError ?? string.Empty;
			root.Controls.Add(validationLabel, 0, 5);

			Panel actions = new Panel();
			actions.Dock = DockStyle.Fill;
			Button save = new RoundedButton();
			ApplyButtonIcon(save, ButtonIcon.Check);
			save.Text = korean ? "이 위치 사용" : "Use this location";
			save.Tag = "primary";
			save.Size = new Size(150, 42);
			save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			save.Click += delegate { SaveSelection(); };
			actions.Controls.Add(save);
			Button cancel = new RoundedButton();
			cancel.Text = korean ? "종료" : "Exit";
			cancel.Tag = "secondary";
			cancel.Size = new Size(100, 42);
			cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
			actions.Controls.Add(cancel);
			actions.Resize += delegate
			{
				save.Left = actions.ClientSize.Width - save.Width;
				cancel.Left = save.Left - cancel.Width - 10;
			};
			root.Controls.Add(actions, 0, 6);

			customOption.CheckedChanged += delegate { UpdateCustomControls(); };
			if (current != null && string.Equals(current.Mode, StorageModeCustom, StringComparison.OrdinalIgnoreCase))
			{
				customOption.Checked = true;
				customPathBox.Text = current.CustomPath ?? string.Empty;
			}
			else if (current != null && string.Equals(current.Mode, StorageModePortable, StringComparison.OrdinalIgnoreCase))
			{
				portableOption.Checked = true;
			}
			else if (Directory.Exists(portablePath))
			{
				portableOption.Checked = true;
			}
			else
			{
				userOption.Checked = true;
			}
			UpdateCustomControls();
			AcceptButton = save;
			CancelButton = cancel;
			ApplySimpleDialogTheme(this);
			ApplyCommonButtonToolTips(this);
		}

		private static RadioButton CreateLocationOption(string title, string detail)
		{
			Panel panel = new Panel();
			panel.Dock = DockStyle.Fill;
			RadioButton option = new RadioButton();
			option.Text = title + Environment.NewLine + detail;
			option.AutoSize = false;
			option.Dock = DockStyle.Fill;
			option.Padding = new Padding(10, 5, 8, 5);
			panel.Controls.Add(option);
			return option;
		}

		private void UpdateCustomControls()
		{
			customPathBox.Enabled = customOption.Checked;
		}

		private void BrowseCustomPath()
		{
			using (FolderBrowserDialog dialog = new FolderBrowserDialog())
			{
				dialog.Description = IsBackupKorean() ? "Minecraft 서버 데이터를 저장할 폴더를 선택하세요." : "Select a folder for Minecraft server data.";
				if (Directory.Exists(customPathBox.Text)) dialog.SelectedPath = customPathBox.Text;
				if (dialog.ShowDialog(this) == DialogResult.OK)
				{
					customPathBox.Text = dialog.SelectedPath;
					customOption.Checked = true;
				}
			}
		}

		private void SaveSelection()
		{
			string mode = userOption.Checked ? StorageModeUser : portableOption.Checked ? StorageModePortable : StorageModeCustom;
			string candidate = mode == StorageModeUser ? GetLauncherUserDataDirectory() : mode == StorageModePortable ? Path.Combine(baseDirectory, MultiServerRootDirectoryName) : customPathBox.Text;
			string normalized;
			string error;
			if (!TryValidateDataRoot(candidate, out normalized, out error))
			{
				validationLabel.Text = error;
				return;
			}
			SelectedMode = mode;
			SelectedCustomPath = mode == StorageModeCustom ? normalized : string.Empty;
			DialogResult = DialogResult.OK;
			Close();
		}
	}
}
