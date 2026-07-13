using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static partial class Launcher
{
	private static LauncherForm launcherForm;
	private static readonly object ServerProcessLock = new object();
	private static System.Diagnostics.Process currentServerProcess;
	private static int currentServerStopRequested;
	private static string currentSelectedJavaPath;
	private static volatile bool launcherUpdateCheckCompleted;
	private static Icon launcherWindowIcon;
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, ToolTip> FormToolTips = new System.Runtime.CompilerServices.ConditionalWeakTable<Form, ToolTip>();
	private const int WmSetRedraw = 0x000B;
	private const int EmGetScrollPos = 0x04DD;
	private const int EmSetScrollPos = 0x04DE;

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private struct RichTextScrollPoint
	{
		public int X;
		public int Y;
	}

	private sealed class RichTextUpdateState
	{
		public bool FollowTail;
		public int SelectionStart;
		public int SelectionLength;
		public RichTextScrollPoint Scroll;
	}

	private enum ConsoleLineKind
	{
		Information,
		Warning,
		Compatibility,
		Error
	}

	private static ConsoleLineKind ClassifyConsoleLine(string line)
	{
		string value = line ?? string.Empty;
		if (value.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("Caused by", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return ConsoleLineKind.Error;
		}
		if (value.IndexOf("Advanced terminal features are not available", StringComparison.OrdinalIgnoreCase) >= 0 ||
			value.IndexOf("terminally deprecated method in sun.misc.Unsafe", StringComparison.OrdinalIgnoreCase) >= 0 ||
			value.IndexOf("sun.misc.Unsafe::", StringComparison.OrdinalIgnoreCase) >= 0 ||
			value.IndexOf("Please consider reporting this to the maintainers", StringComparison.OrdinalIgnoreCase) >= 0 ||
			value.IndexOf("will be removed in a future release", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return ConsoleLineKind.Compatibility;
		}
		if (value.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0 || value.TrimStart().StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
		{
			return ConsoleLineKind.Warning;
		}
		return ConsoleLineKind.Information;
	}

	private static bool ConsoleLineMatchesFilter(string line, int filterIndex)
	{
		if (filterIndex <= 0)
		{
			return true;
		}
		ConsoleLineKind kind = ClassifyConsoleLine(line);
		return (filterIndex == 1 && kind == ConsoleLineKind.Warning) ||
			(filterIndex == 2 && kind == ConsoleLineKind.Compatibility) ||
			(filterIndex == 3 && kind == ConsoleLineKind.Error);
	}

	[System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
	private static extern IntPtr SendRichTextMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

	[System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
	private static extern IntPtr SendRichTextPointMessage(IntPtr window, int message, IntPtr wParam, ref RichTextScrollPoint point);

	private static RichTextUpdateState BeginStableRichTextUpdate(RichTextBox box)
	{
		RichTextUpdateState state = new RichTextUpdateState();
		state.FollowTail = IsRichTextAtBottom(box);
		state.SelectionStart = box.SelectionStart;
		state.SelectionLength = box.SelectionLength;
		if (box.IsHandleCreated)
		{
			SendRichTextPointMessage(box.Handle, EmGetScrollPos, IntPtr.Zero, ref state.Scroll);
			SendRichTextMessage(box.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
		}
		box.SuspendLayout();
		return state;
	}

	private static void EndStableRichTextUpdate(RichTextBox box, RichTextUpdateState state)
	{
		try
		{
			if (state.FollowTail)
			{
				box.SelectionStart = box.TextLength;
				box.SelectionLength = 0;
				box.ScrollToCaret();
			}
			else
			{
				box.SelectionStart = Math.Min(state.SelectionStart, box.TextLength);
				box.SelectionLength = Math.Min(state.SelectionLength, Math.Max(0, box.TextLength - box.SelectionStart));
				if (box.IsHandleCreated)
				{
					SendRichTextPointMessage(box.Handle, EmSetScrollPos, IntPtr.Zero, ref state.Scroll);
				}
			}
		}
		finally
		{
			box.ResumeLayout();
			if (box.IsHandleCreated)
			{
				SendRichTextMessage(box.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
				box.Invalidate();
			}
		}
	}

	private static bool IsRichTextAtBottom(RichTextBox box)
	{
		if (box == null || box.TextLength == 0 || box.ClientSize.Height <= 2)
		{
			return true;
		}
		int y = Math.Max(1, box.ClientSize.Height - 3);
		int lastLeft = box.GetCharIndexFromPosition(new Point(2, y));
		int lastMiddle = box.GetCharIndexFromPosition(new Point(Math.Max(2, box.ClientSize.Width / 2), y));
		return Math.Max(lastLeft, lastMiddle) >= Math.Max(0, box.TextLength - 2);
	}

	private static void ApplyCommonButtonToolTips(Form form)
	{
		if (form == null || form.IsDisposed)
		{
			return;
		}
		ToolTip toolTip;
		if (!FormToolTips.TryGetValue(form, out toolTip))
		{
			toolTip = new ToolTip();
			toolTip.InitialDelay = 350;
			toolTip.ReshowDelay = 100;
			toolTip.AutoPopDelay = 7000;
			FormToolTips.Add(form, toolTip);
		}
		toolTip.RemoveAll();
		ApplyCommonButtonToolTipsRecursive(form, toolTip);
	}

	private static void ApplyCommonButtonToolTipsRecursive(Control parent, ToolTip toolTip)
	{
		foreach (Control control in parent.Controls)
		{
			if (control is ButtonBase)
			{
				string accessibleName = (control.Text ?? string.Empty).Replace("&", string.Empty).Trim();
				if (!string.IsNullOrEmpty(accessibleName))
				{
					control.AccessibleName = accessibleName;
				}
				string description = GetCommonButtonDescription(control.Text);
				if (!string.IsNullOrEmpty(description))
				{
					toolTip.SetToolTip(control, description);
					control.AccessibleDescription = description;
				}
			}
			if (control.HasChildren)
			{
				ApplyCommonButtonToolTipsRecursive(control, toolTip);
			}
		}
	}

	private static void ConfigureAccessibleField(Control control, string name, string description)
	{
		if (control == null)
		{
			return;
		}
		control.AccessibleName = name ?? string.Empty;
		control.AccessibleDescription = description ?? string.Empty;
	}

	private static int GetMarqueeAnimationSpeed()
	{
		return SystemInformation.IsMenuAnimationEnabled || SystemInformation.IsMinimizeRestoreAnimationEnabled ? 28 : 0;
	}

	private static void ApplyButtonIcon(Button button, ButtonIcon icon)
	{
		RoundedButton roundedButton = button as RoundedButton;
		if (roundedButton != null)
		{
			roundedButton.IconKind = icon;
		}
	}

	private static void EnsureButtonContentFits(Button button)
	{
		if (button == null)
		{
			return;
		}
		RoundedButton roundedButton = button as RoundedButton;
		bool hasIcon = roundedButton != null && roundedButton.IconKind != ButtonIcon.None;
		Size measured = TextRenderer.MeasureText(button.Text ?? string.Empty, button.Font, new Size(4096, Math.Max(1, button.Height)), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		int requiredWidth = measured.Width + (hasIcon ? 46 : 24);
		if (button.Width < requiredWidth)
		{
			button.Width = requiredWidth;
		}
		if (button.MinimumSize.Width < requiredWidth)
		{
			button.MinimumSize = new Size(requiredWidth, button.MinimumSize.Height);
		}
	}

	private static string GetCommonButtonDescription(string text)
	{
		string value = (text ?? string.Empty).Replace("&", string.Empty).Trim().ToLowerInvariant();
		bool korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
		if (value.Contains("서버 시작") || value == "start" || value == "start server") return korean ? "선택한 프로필의 서버를 시작합니다. 단축키: F5" : "Start the selected server profile. Shortcut: F5";
		if (value.Contains("서버 종료") || value.Contains("안전하게 종료") || value == "stop" || value == "stop safely") return korean ? "월드를 저장하고 서버를 종료합니다. 단축키: Shift+F5" : "Save the world and stop the server. Shortcut: Shift+F5";
		if (value == "설정" || value == "settings") return korean ? "서버 종류와 게임 설정을 변경합니다. 단축키: Ctrl+," : "Change the server type and game settings. Shortcut: Ctrl+,";
		if (value == "런처 업데이트" || value == "launcher update") return korean ? "지금 시점의 최신 런처를 새로 확인합니다." : "Check for the latest launcher again right now.";
		if (value.Contains("업글") || value.Contains("업데이트") || value.Contains("upgrade") || value.Contains("update")) return korean ? "서버 실행 파일을 최신 호환 빌드로 갱신합니다." : "Update the server file to the latest compatible build.";
		if (value.Contains("콘솔") || value.Contains("console")) return korean ? "서버 로그와 명령 입력창을 열거나 닫습니다. 단축키: Ctrl+K" : "Open or close server logs and command input. Shortcut: Ctrl+K";
		if (value.Contains("서버 관리") || value == "servers" || value.Contains("server management")) return korean ? "프로필 관리와 멀티 서버 기능을 엽니다." : "Open profiles and multi-server tools.";
		if (value.Contains("백업") || value.Contains("backup")) return korean ? "서버 전체를 백업하거나 복원합니다." : "Back up or restore the complete server.";
		if (value == "콘텐츠" || value.Contains("content")) return korean ? "호환 플러그인과 모드를 찾아 설치합니다." : "Find and install compatible plugins or mods.";
		if (value.Contains("플레이어") || value.Contains("player")) return korean ? "접속자와 권한·차단 목록을 관리합니다." : "Manage players, permissions, and bans.";
		if (value.Contains("네트워크") || value.Contains("network")) return korean ? "접속 주소와 포트포워딩 상태를 확인합니다." : "Check addresses and port forwarding.";
		if (value.Contains("진단") || value.Contains("diagnostic") || value == "diagnose") return korean ? "개인정보를 가린 문제 진단 파일을 만듭니다." : "Create a privacy-redacted diagnostic bundle.";
		if (value == "새 서버" || value == "new") return korean ? "새 서버 프로필을 만듭니다." : "Create a new server profile.";
		if (value == "복제" || value == "clone") return korean ? "선택한 서버를 새 프로필로 복사합니다." : "Copy the selected server into a new profile.";
		if (value.Contains("가져오기") || value.Contains("import")) return korean ? "기존 서버 폴더나 백업을 가져옵니다." : "Import an existing server folder or backup.";
		if (value.Contains("이름 변경") || value == "rename") return korean ? "선택한 프로필 이름을 바꿉니다." : "Rename the selected profile.";
		if (value == "보관" || value == "archive") return korean ? "선택한 서버를 안전하게 보관합니다." : "Safely archive the selected server.";
		if (value.Contains("이 서버 선택") || value.Contains("set active")) return korean ? "선택한 프로필을 기본 서버로 사용합니다." : "Use the selected profile as the default server.";
		if (value.Contains("검색") || value == "search") return korean ? "입력한 이름으로 호환 항목을 검색합니다." : "Search compatible content by name.";
		if (value.Contains("설치") || value.Contains("install")) return korean ? "선택한 항목과 필수 의존성을 설치합니다." : "Install the selected item and required dependencies.";
		if (value.Contains("폴더 열기") || value.Contains("open content folder")) return korean ? "플러그인 또는 모드 폴더를 엽니다." : "Open the plugin or mod folder.";
		if (value.Contains("저장") || value == "save") return korean ? "현재 설정을 저장합니다." : "Save the current settings.";
		if (value.Contains("취소") || value == "cancel") return korean ? "변경하지 않고 닫습니다." : "Close without saving changes.";
		if (value.Contains("복사") || value.Contains("copy")) return korean ? "표시된 값을 클립보드에 복사합니다." : "Copy the displayed value to the clipboard.";
		if (value.Contains("새로고침") || value.Contains("refresh")) return korean ? "현재 정보를 다시 불러옵니다." : "Refresh the current information.";
		if (value.Contains("재검사") || value.Contains("recheck")) return korean ? "외부 접속 가능 여부를 다시 확인합니다." : "Recheck external reachability.";
		if (value.Contains("공유기") || value.Contains("router")) return korean ? "공유기 관리 페이지를 엽니다." : "Open the router administration page.";
		if (value.Contains("playit")) return korean ? "포트포워딩 대안 안내를 엽니다." : "Open the port-forwarding alternative guide.";
		if (value == "전송" || value == "send") return korean ? "입력한 서버 명령을 전송합니다." : "Send the entered server command.";
		if (value.Contains("화이트리스트 추가") || value.Contains("add to whitelist")) return korean ? "플레이어를 화이트리스트에 추가합니다." : "Add the player to the whitelist.";
		if (value.Contains("화이트리스트 제거") || value.Contains("remove from whitelist")) return korean ? "플레이어를 화이트리스트에서 제거합니다." : "Remove the player from the whitelist.";
		if (value.Contains("op 권한 주기") || value.Contains("grant op")) return korean ? "플레이어에게 OP 권한을 줍니다." : "Grant operator permission to the player.";
		if (value.Contains("op 권한 회수") || value.Contains("revoke op")) return korean ? "플레이어의 OP 권한을 회수합니다." : "Revoke operator permission from the player.";
		if (value.Contains("서버에서 내보내기") || value.Contains("kick from server")) return korean ? "플레이어를 서버에서 내보냅니다." : "Kick the player from the server.";
		if (value.Contains("접속 차단 해제") || value.Contains("pardon")) return korean ? "플레이어의 접속 차단을 해제합니다." : "Remove the player's ban.";
		if (value == "접속 차단" || value.Contains("ban player")) return korean ? "플레이어의 서버 접속을 차단합니다." : "Ban the player from the server.";
		if (value == "찾기" || value == "browse") return korean ? "파일을 선택합니다." : "Choose a file.";
		if (value == "내보내기" || value == "export") return korean ? "선택한 백업을 다른 위치에 복사합니다." : "Copy the selected backup to another location.";
		if (value.Contains("다크") || value.Contains("라이트") || value.Contains("dark") || value.Contains("light")) return korean ? "화면 테마를 전환합니다." : "Switch the interface theme.";
		if (value == "english" || value == "한국어") return korean ? "화면 언어를 전환합니다." : "Switch the interface language.";
		if (value == "닫기" || value == "close") return korean ? "이 창을 닫습니다." : "Close this window.";
		if (value == "다음에" || value == "later") return korean ? "지금은 저장하지 않고 닫습니다." : "Close without saving for now.";
		if (controlTextLooksLikePreset(value)) return korean ? "이 프리셋으로 주요 서버 설정을 채웁니다." : "Fill common server settings with this preset.";
		return string.Empty;
	}

	private static bool controlTextLooksLikePreset(string value)
	{
		return value.Contains("야생") || value.Contains("크리에이티브 월드") || value.Contains("survival") || value.Contains("creative world") || value == "직접 설정" || value == "custom";
	}

	[System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
	private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

	private static void ApplyLauncherTaskbarIdentity()
	{
		try
		{
			SetCurrentProcessExplicitAppUserModelID("MineHarbor.MinecraftServerLauncher");
		}
		catch
		{
			// 구형 Windows에서 작업표시줄 식별자를 지원하지 않아도 실행은 계속합니다.
		}
	}

	private static void ApplyLauncherWindowIcon(Form form)
	{
		if (form == null)
		{
			return;
		}
		try
		{
			if (launcherWindowIcon == null)
			{
				using (Icon extracted = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location))
				{
					if (extracted != null)
					{
						launcherWindowIcon = (Icon)extracted.Clone();
					}
				}
			}
			if (launcherWindowIcon != null)
			{
				form.Icon = launcherWindowIcon;
			}
		}
		catch
		{
			// 아이콘을 읽지 못해도 서버 실행 기능은 유지합니다.
		}
	}

	private static bool TryPostToUi(Control control, MethodInvoker action)
	{
		if (control == null || action == null || control.IsDisposed || control.Disposing || !control.IsHandleCreated)
		{
			return false;
		}
		try
		{
			control.BeginInvoke(action);
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			// 창이 닫히는 순간과 겹친 결과 갱신은 안전하게 생략합니다.
			return false;
		}
	}

	private static class Localization
	{
		public const string Korean = "ko";
		public const string English = "en";
		public static string CurrentLanguage = DetectDefaultLanguage();

		private static readonly Dictionary<string, string> KoreanTexts = new Dictionary<string, string>
		{
			{ "App.Title", "MineHarbor — Minecraft Server Launcher" },
			{ "App.HeaderTitle", "내 MineHarbor 서버" },
			{ "App.Subtitle", "복잡한 설정 없이, 버튼 한 번으로 시작해 보세요" },
			{ "App.Error", "오류" },
			{ "Theme.Dark", "다크 모드" },
			{ "Theme.Light", "라이트 모드" },
			{ "Language.Changed", "언어를 한국어로 바꿨습니다." },
			{ "Status.Off", "서버 꺼짐" },
			{ "Status.Preparing", "서버 준비 중" },
			{ "Status.Error", "오류 발생" },
			{ "Status.Starting", "서버 시작 중" },
			{ "Status.Ready", "접속 준비 완료" },
			{ "Status.Stopping", "서버 종료 중" },
			{ "Address.Title", "친구에게 이 주소를 보내세요" },
			{ "Address.Empty", "서버를 시작하면 표시됩니다" },
			{ "Address.Copy", "복사" },
			{ "Address.Copied", "접속 주소를 복사했습니다." },
			{ "Button.Start", "서버 시작" },
			{ "Button.Stop", "서버 종료" },
			{ "Button.Settings", "설정" },
			{ "Button.Upgrade", "업데이트" },
			{ "Button.ConsoleOpen", "콘솔" },
			{ "Button.ConsoleClose", "콘솔 닫기" },
			{ "Button.Send", "전송" },
			{ "Button.Profiles", "서버 프로필" },
			{ "Button.MultiServer", "멀티 서버" },
			{ "Button.ServerManagement", "서버 관리" },
			{ "Button.Backup", "백업" },
			{ "Button.Content", "콘텐츠" },
			{ "Button.Players", "플레이어" },
			{ "Button.Network", "네트워크" },
			{ "Button.Diagnostics", "진단" },
			{ "Button.LauncherUpdate", "런처 업데이트" },
			{ "Notice.LauncherLatest", "현재 최신 런처를 사용하고 있습니다." },
			{ "Notice.LauncherDeferred", "런처 업데이트를 나중으로 미뤘습니다." },
			{ "Main.ControlSection", "서버 제어" },
			{ "Main.ToolSection", "관리 도구" },
			{ "Console.Search", "콘솔 검색" },
			{ "Console.All", "전체" },
			{ "Console.Warning", "일반 경고" },
			{ "Console.Compatibility", "호환성" },
			{ "Console.Error", "오류" },
			{ "Console.Wrap", "줄 바꿈" },
			{ "Features", "호환 Java · 멀티 서버 · 전체 백업/복원 · Modrinth 콘텐츠 · UPnP 외부 접속 · 안전한 자동 OP" },
			{ "Notice.Ready", "서버를 열 준비가 되었어요." },
			{ "Notice.InitialSaved", "최초 설정을 저장했습니다. 서버 시작을 누르세요." },
			{ "Notice.InitialCanceled", "최초 설정은 서버를 시작할 때 다시 진행할 수 있습니다." },
			{ "Notice.SettingsFailed", "설정을 저장하지 못했습니다: {0}" },
			{ "Notice.CheckingFiles", "업데이트와 서버 파일을 확인하고 있습니다." },
			{ "Notice.StartFailed", "서버를 시작하지 못했습니다. 콘솔에서 오류를 확인해 주세요." },
			{ "Notice.StartCanceled", "서버 시작을 취소했습니다." },
			{ "Notice.ServerStopped", "서버가 종료되었습니다." },
			{ "Notice.ServerError", "서버가 오류로 종료되었습니다. 콘솔에서 원인을 확인해 주세요." },
			{ "Notice.WaitingServer", "서버가 접속을 받을 때까지 기다리고 있습니다." },
			{ "Notice.LocalReady", "로컬 접속이 준비됐습니다. 외부 접속을 확인하고 있습니다." },
			{ "Notice.Stopping", "월드를 안전하게 저장한 뒤 종료하고 있습니다." },
			{ "Notice.NoServer", "실행 중인 서버가 없습니다." },
			{ "Notice.StopBeforeSettings", "서버를 종료한 뒤 설정을 변경해 주세요." },
			{ "Notice.StopBeforeUpgrade", "서버를 종료한 뒤 업그레이드해 주세요." },
			{ "Notice.SettingsSaved", "서버 설정을 저장했습니다." },
			{ "Notice.SettingsCanceled", "설정 변경을 취소했습니다." },
			{ "Notice.UpgradeStarted", "서버 파일 업그레이드를 시작했습니다." },
			{ "Notice.UpgradeDone", "서버 파일 업그레이드를 완료했습니다." },
			{ "Notice.UpgradeFailed", "서버 파일 업그레이드를 완료하지 못했습니다: {0}" },
			{ "Notice.CloseAfterWork", "현재 작업이 끝난 뒤 런처를 닫습니다." },
			{ "Console.SetupCanceled", "서버 설정이 취소되었습니다." },
			{ "Console.LauncherError", "런처 오류: {0}" },
			{ "Thread.Start", "서버 시작 작업" },
			{ "Eula.Text", "Minecraft 서버를 실행하려면 Minecraft EULA에 동의해야 합니다.\r\n\r\n약관: https://aka.ms/MinecraftEULA\r\n\r\n약관을 읽었으며 동의하시겠습니까?" },
			{ "Eula.Title", "Minecraft EULA" },
			{ "Close.Question", "서버가 실행 중입니다. 안전하게 종료한 뒤 런처를 닫으시겠습니까?" },
			{ "Close.Title", "서버 종료" },
			{ "Setup.Title.New", "최초 서버 설정" },
			{ "Setup.Title.Edit", "서버 설정" },
			{ "Setup.Heading.New", "어떤 서버로 시작할까요?" },
			{ "Setup.Heading.Edit", "어떤 설정을 바꿀까요?" },
			{ "Setup.Description", "원하는 방식을 고르면 나머지는 알아서 준비해 드려요." },
			{ "Setup.Runtime", "서버 실행 파일" },
			{ "Setup.ProfileName", "서버 프로필 이름" },
			{ "Setup.ServerType", "서버 종류" },
			{ "Setup.MinecraftVersion", "Minecraft 버전" },
			{ "Setup.IncludeSnapshots", "스냅샷 포함" },
			{ "Setup.UseManualJar", "서버 JAR 직접 지정" },
			{ "Setup.BrowseJar", "찾기" },
			{ "Setup.JavaVersion", "JAR용 Java" },
			{ "Setup.Quick", "빠른 설정" },
			{ "Setup.Basic", "기본 정보" },
			{ "Setup.Rules", "서버 규칙" },
			{ "Setup.ServerName", "서버 이름을 알려주세요" },
			{ "Setup.MaxPlayers", "최대 인원" },
			{ "Setup.Port", "서버 포트" },
			{ "Setup.Memory", "서버 메모리" },
			{ "Setup.MemoryHint", "GB   권장 {0}GB · 시스템 {1}GB" },
			{ "Setup.GameMode", "게임 모드" },
			{ "Setup.Difficulty", "난이도" },
			{ "Setup.More", "더 세밀하게 설정하기" },
			{ "Setup.CloseMore", "세부 설정 닫기" },
			{ "Setup.Pvp", "PvP 허용" },
			{ "Setup.Whitelist", "화이트리스트 사용" },
			{ "Setup.CommandBlock", "명령 블록 허용" },
			{ "Setup.OnlineMode", "정품 계정 인증" },
			{ "Setup.PaperUpdate", "자동 업데이트" },
			{ "Setup.ViewDistance", "시야 거리" },
			{ "Setup.SimulationDistance", "시뮬레이션 거리" },
			{ "Setup.Owner", "서버 소유자" },
			{ "Setup.Cancel", "다음에" },
			{ "Setup.CancelEdit", "취소" },
			{ "Setup.Save", "이대로 저장" },
			{ "Setup.SaveEdit", "변경사항 저장" },
			{ "Setup.ValidationSummary", "표시된 입력값을 확인해 주세요." },
			{ "Setup.VersionLoading", "버전 목록 불러오는 중…" },
			{ "Setup.VersionReady", "버전 목록 준비 완료" },
			{ "Setup.VersionFallback", "네트워크 문제로 기본 목록 사용 중" },
			{ "Setup.ValidateTitle", "설정 확인" },
			{ "Setup.ServerNameRequired", "서버 이름을 입력해 주세요." },
			{ "Setup.OwnerInvalid", "서버 소유자 이름은 영문, 숫자, 밑줄을 사용해 3~16자로 입력해 주세요." },
			{ "Setup.ProfileInvalid", "서버 프로필 이름은 1~48자로 입력해 주세요." },
			{ "Setup.ManualJarMissing", "직접 지정할 .jar 파일을 선택해 주세요." },
			{ "Setup.SecurityTitle", "보안 경고" },
			{ "Setup.OnlineModeWarning", "정품 계정 인증을 끄면 다른 사람이 타인의 닉네임을 사칭할 수 있습니다. 그래도 끄시겠습니까?" },
			{ "Setup.CompatibilityTitle", "호환성 확인" },
			{ "Setup.CompatibilityWarning", "서버 종류나 Minecraft 버전을 바꾸면 기존 월드/플러그인/모드가 호환되지 않을 수 있습니다. 서버 시작 또는 업그레이드 전에 자동 백업을 만들고 계속합니다. 저장하시겠습니까?" },
			{ "Setup.WorldTitle", "기존 월드 확인" },
			{ "Setup.WorldWarning", "월드 유형 변경은 이미 생성된 월드에 적용되지 않습니다. 새 월드를 만들 때만 적용됩니다. 설정을 저장하시겠습니까?" },
			{ "Setup.DowngradeTitle", "월드 다운그레이드 차단" },
			{ "Setup.DowngradeWarning", "현재 월드는 더 최신 Minecraft 버전에서 열렸기 때문에 구버전으로 안전하게 내릴 수 없습니다. 기존 월드를 보호하기 위해 변경을 차단했습니다. 서버 프로필에서 새 서버를 만든 뒤 원하는 구버전을 선택해 주세요." },
			{ "Preset.Peaceful", "평화로움 야생" },
			{ "Preset.Easy", "쉬움 야생" },
			{ "Preset.Normal", "보통 야생" },
			{ "Preset.Hard", "어려움 야생" },
			{ "Preset.Hardcore", "하드코어 야생" },
			{ "Preset.CreativeNormal", "크리에이티브 월드\r\n일반 지형" },
			{ "Preset.CreativeFlat", "크리에이티브 월드\r\n평지" },
			{ "Preset.Custom", "직접 설정" },
			{ "GameMode.Survival", "서바이벌" },
			{ "GameMode.Creative", "크리에이티브" },
			{ "GameMode.Adventure", "모험" },
			{ "GameMode.Spectator", "관전자" },
			{ "Difficulty.Peaceful", "평화로움" },
			{ "Difficulty.Easy", "쉬움" },
			{ "Difficulty.Normal", "보통" },
			{ "Difficulty.Hard", "어려움" },
			{ "Hardcore", "하드코어" }
		};

		private static readonly Dictionary<string, string> EnglishTexts = new Dictionary<string, string>
		{
			{ "App.Title", "MineHarbor — Minecraft Server Launcher" },
			{ "App.HeaderTitle", "My MineHarbor Server" },
			{ "App.Subtitle", "Start your server with one button, without the setup noise." },
			{ "App.Error", "Error" },
			{ "Theme.Dark", "Dark mode" },
			{ "Theme.Light", "Light mode" },
			{ "Language.Changed", "Language changed to English." },
			{ "Status.Off", "Server off" },
			{ "Status.Preparing", "Preparing server" },
			{ "Status.Error", "Error" },
			{ "Status.Starting", "Starting server" },
			{ "Status.Ready", "Ready to join" },
			{ "Status.Stopping", "Stopping server" },
			{ "Address.Title", "Send this address to your friends" },
			{ "Address.Empty", "Shown after the server starts" },
			{ "Address.Copy", "Copy" },
			{ "Address.Copied", "Connection address copied." },
			{ "Button.Start", "Start" },
			{ "Button.Stop", "Stop" },
			{ "Button.Settings", "Settings" },
			{ "Button.Upgrade", "Update" },
			{ "Button.ConsoleOpen", "Console" },
			{ "Button.ConsoleClose", "Close console" },
			{ "Button.Send", "Send" },
			{ "Button.Profiles", "Profiles" },
			{ "Button.MultiServer", "Multi-server" },
			{ "Button.ServerManagement", "Servers" },
			{ "Button.Backup", "Backups" },
			{ "Button.Content", "Content" },
			{ "Button.Players", "Players" },
			{ "Button.Network", "Network" },
			{ "Button.Diagnostics", "Diagnose" },
			{ "Button.LauncherUpdate", "Launcher update" },
			{ "Notice.LauncherLatest", "You are using the latest launcher." },
			{ "Notice.LauncherDeferred", "Launcher update postponed." },
			{ "Main.ControlSection", "Server controls" },
			{ "Main.ToolSection", "Management tools" },
			{ "Console.Search", "Search console" },
			{ "Console.All", "All" },
			{ "Console.Warning", "Warnings" },
			{ "Console.Compatibility", "Compatibility" },
			{ "Console.Error", "Errors" },
			{ "Console.Wrap", "Word wrap" },
			{ "Features", "Compatible Java · multi-server · full backup/restore · Modrinth content · UPnP external access · safe owner auto-OP" },
			{ "Notice.Ready", "Ready to open your server." },
			{ "Notice.InitialSaved", "Initial setup saved. Press Start server." },
			{ "Notice.InitialCanceled", "Initial setup will run again when you start the server." },
			{ "Notice.SettingsFailed", "Could not save settings: {0}" },
			{ "Notice.CheckingFiles", "Checking updates and server files." },
			{ "Notice.StartFailed", "Could not start the server. Check the console for details." },
			{ "Notice.StartCanceled", "Server start was canceled." },
			{ "Notice.ServerStopped", "Server stopped." },
			{ "Notice.ServerError", "Server stopped with an error. Check the console for details." },
			{ "Notice.WaitingServer", "Waiting until the server accepts connections." },
			{ "Notice.LocalReady", "Local connection is ready. Checking external access." },
			{ "Notice.Stopping", "Saving the world safely before stopping." },
			{ "Notice.NoServer", "No server is running." },
			{ "Notice.StopBeforeSettings", "Stop the server before changing settings." },
			{ "Notice.StopBeforeUpgrade", "Stop the server before upgrading files." },
			{ "Notice.SettingsSaved", "Server settings saved." },
			{ "Notice.SettingsCanceled", "Settings change canceled." },
			{ "Notice.UpgradeStarted", "Server file upgrade started." },
			{ "Notice.UpgradeDone", "Server file upgrade completed." },
			{ "Notice.UpgradeFailed", "Could not complete server file upgrade: {0}" },
			{ "Notice.CloseAfterWork", "The launcher will close after the current task finishes." },
			{ "Console.SetupCanceled", "Server setup was canceled." },
			{ "Console.LauncherError", "Launcher error: {0}" },
			{ "Thread.Start", "Server start task" },
			{ "Eula.Text", "To run a Minecraft server, you must agree to the Minecraft EULA.\r\n\r\nTerms: https://aka.ms/MinecraftEULA\r\n\r\nHave you read and agreed to the terms?" },
			{ "Eula.Title", "Minecraft EULA" },
			{ "Close.Question", "The server is running. Stop it safely before closing the launcher?" },
			{ "Close.Title", "Stop server" },
			{ "Setup.Title.New", "Initial server setup" },
			{ "Setup.Title.Edit", "Server settings" },
			{ "Setup.Heading.New", "What kind of server should we start?" },
			{ "Setup.Heading.Edit", "Which settings should we change?" },
			{ "Setup.Description", "Pick a preset and the launcher will handle the rest." },
			{ "Setup.Runtime", "Server executable" },
			{ "Setup.ProfileName", "Server profile name" },
			{ "Setup.ServerType", "Server type" },
			{ "Setup.MinecraftVersion", "Minecraft version" },
			{ "Setup.IncludeSnapshots", "Snapshots" },
			{ "Setup.UseManualJar", "Use a custom server JAR" },
			{ "Setup.BrowseJar", "Browse" },
			{ "Setup.JavaVersion", "JAR Java" },
			{ "Setup.Quick", "Quick setup" },
			{ "Setup.Basic", "Basic info" },
			{ "Setup.Rules", "Server rules" },
			{ "Setup.ServerName", "Server name" },
			{ "Setup.MaxPlayers", "Max players" },
			{ "Setup.Port", "Server port" },
			{ "Setup.Memory", "Server memory" },
			{ "Setup.MemoryHint", "GB   Recommended {0}GB · System {1}GB" },
			{ "Setup.GameMode", "Game mode" },
			{ "Setup.Difficulty", "Difficulty" },
			{ "Setup.More", "More settings" },
			{ "Setup.CloseMore", "Hide details" },
			{ "Setup.Pvp", "Allow PvP" },
			{ "Setup.Whitelist", "Use whitelist" },
			{ "Setup.CommandBlock", "Allow command blocks" },
			{ "Setup.OnlineMode", "Online authentication" },
			{ "Setup.PaperUpdate", "Auto update" },
			{ "Setup.ViewDistance", "View distance" },
			{ "Setup.SimulationDistance", "Simulation distance" },
			{ "Setup.Owner", "Server owner" },
			{ "Setup.Cancel", "Later" },
			{ "Setup.CancelEdit", "Cancel" },
			{ "Setup.Save", "Save" },
			{ "Setup.SaveEdit", "Save changes" },
			{ "Setup.ValidationSummary", "Review the highlighted field." },
			{ "Setup.VersionLoading", "Loading version list…" },
			{ "Setup.VersionReady", "Version list ready" },
			{ "Setup.VersionFallback", "Using the built-in list because the network is unavailable" },
			{ "Setup.ValidateTitle", "Check settings" },
			{ "Setup.ServerNameRequired", "Enter a server name." },
			{ "Setup.OwnerInvalid", "Server owner must be 3-16 characters using letters, numbers, and underscores." },
			{ "Setup.ProfileInvalid", "Enter a server profile name between 1 and 48 characters." },
			{ "Setup.ManualJarMissing", "Select a .jar file to use as the server executable." },
			{ "Setup.SecurityTitle", "Security warning" },
			{ "Setup.OnlineModeWarning", "Disabling online authentication allows nickname impersonation. Continue anyway?" },
			{ "Setup.CompatibilityTitle", "Compatibility check" },
			{ "Setup.CompatibilityWarning", "Changing the server type or Minecraft version can break existing worlds, plugins, or mods. The launcher will create a backup before start or upgrade. Save anyway?" },
			{ "Setup.WorldTitle", "Existing world check" },
			{ "Setup.WorldWarning", "Changing the world type does not affect an existing world. It only applies to newly generated worlds. Save anyway?" },
			{ "Setup.DowngradeTitle", "World downgrade blocked" },
			{ "Setup.DowngradeWarning", "This world was opened by a newer Minecraft version and cannot be downgraded safely. The change was blocked to protect the world. Create a new server profile and choose the older version there." },
			{ "Preset.Peaceful", "Peaceful survival" },
			{ "Preset.Easy", "Easy survival" },
			{ "Preset.Normal", "Normal survival" },
			{ "Preset.Hard", "Hard survival" },
			{ "Preset.Hardcore", "Hardcore survival" },
			{ "Preset.CreativeNormal", "Creative world\r\nNormal terrain" },
			{ "Preset.CreativeFlat", "Creative world\r\nFlat" },
			{ "Preset.Custom", "Custom" },
			{ "GameMode.Survival", "Survival" },
			{ "GameMode.Creative", "Creative" },
			{ "GameMode.Adventure", "Adventure" },
			{ "GameMode.Spectator", "Spectator" },
			{ "Difficulty.Peaceful", "Peaceful" },
			{ "Difficulty.Easy", "Easy" },
			{ "Difficulty.Normal", "Normal" },
			{ "Difficulty.Hard", "Hard" },
			{ "Hardcore", "Hardcore" }
		};

		public static string DetectDefaultLanguage()
		{
			try
			{
				return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, Korean, StringComparison.OrdinalIgnoreCase) ? Korean : English;
			}
			catch
			{
				return Korean;
			}
		}

		public static string NormalizeLanguage(string value)
		{
			return string.Equals(value, English, StringComparison.OrdinalIgnoreCase) ? English : Korean;
		}

		public static string ToggleLanguage()
		{
			CurrentLanguage = string.Equals(CurrentLanguage, Korean, StringComparison.OrdinalIgnoreCase) ? English : Korean;
			return CurrentLanguage;
		}

		public static string LanguageButtonText()
		{
			return string.Equals(CurrentLanguage, Korean, StringComparison.OrdinalIgnoreCase) ? "English" : "한국어";
		}

		public static string T(string key)
		{
			Dictionary<string, string> source = string.Equals(CurrentLanguage, Korean, StringComparison.OrdinalIgnoreCase) ? KoreanTexts : EnglishTexts;
			string value;
			if (source.TryGetValue(key, out value))
			{
				return value;
			}
			if (KoreanTexts.TryGetValue(key, out value))
			{
				return value;
			}
			return key;
		}

		public static string F(string key, params object[] args)
		{
			return string.Format(CultureInfo.CurrentCulture, T(key), args);
		}
	}

	private static int RunGuiApplication()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		ApplyLauncherTaskbarIdentity();
		launcherForm = new LauncherForm();
		if (!string.IsNullOrEmpty(PendingLauncherUpdateDirectory))
		{
			launcherForm.Shown += delegate
			{
				string updateDirectory = PendingLauncherUpdateDirectory;
				PendingLauncherUpdateDirectory = null;
				ConfirmLauncherUpdateStarted(updateDirectory);
			};
		}
		Console.SetOut(new LauncherTextWriter());
		Console.SetError(new LauncherTextWriter());
		Application.Run(launcherForm);
		launcherForm = null;
		return 0;
	}

	private static bool IsGuiMode()
	{
		return launcherForm != null;
	}

	private static void ShowLauncherMessage(string message, bool error)
	{
		MessageBox.Show(message, error ? Localization.T("App.Error") : Localization.T("App.Title"), MessageBoxButtons.OK, error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
	}

	private static void RequestLauncherClose()
	{
		LauncherForm form = launcherForm;
		if (form != null)
		{
			form.RequestClose();
		}
	}

	private static void SetLauncherConnectionAddress(string address)
	{
		LauncherForm form = launcherForm;
		if (form != null)
		{
			form.SetConnectionAddress(address);
		}
	}

	private static void ShowLauncherNotice(string message, bool warning)
	{
		LauncherForm form = launcherForm;
		if (form != null)
		{
			form.ShowNotice(message, warning);
		}
	}

	private static void ReportLauncherLoading(string message, int percent)
	{
		LauncherForm form = launcherForm;
		if (form != null)
		{
			form.SetLoadingState(message, true, percent);
		}
	}

	private static string LauncherUiText(string korean, string english)
	{
		return string.Equals(Localization.CurrentLanguage, Localization.English, StringComparison.OrdinalIgnoreCase) ? english : korean;
	}

	private static void FinishLauncherLoading()
	{
		LauncherForm form = launcherForm;
		if (form != null)
		{
			form.SetLoadingState(string.Empty, false, -1);
		}
	}

	private static string GetLocalConnectionAddress(int port)
	{
		NetworkDetails details = GetNetworkDetails();
		string ip = string.IsNullOrEmpty(details.LocalIpv4) ? "127.0.0.1" : details.LocalIpv4;
		return ip + ":" + port;
	}

	private static void NotifyServerStarted(int port)
	{
		LauncherForm form = launcherForm;
		if (form != null)
		{
			form.ServerStarted(GetLocalConnectionAddress(port));
		}
	}

	private static void NotifyServerReady(int port)
	{
		LauncherForm form = launcherForm;
		if (form != null)
		{
			form.ServerReady(GetLocalConnectionAddress(port));
		}
	}

	private static void SetCurrentServerProcess(System.Diagnostics.Process process)
	{
		lock (ServerProcessLock)
		{
			currentServerProcess = process;
			Interlocked.Exchange(ref currentServerStopRequested, 0);
		}
	}

	private static void ClearCurrentServerProcess(System.Diagnostics.Process process)
	{
		lock (ServerProcessLock)
		{
			if (object.ReferenceEquals(currentServerProcess, process))
			{
				currentServerProcess = null;
			}
		}
	}

	private static bool SendServerCommand(string command)
	{
		if (string.IsNullOrWhiteSpace(command))
		{
			return false;
		}
		lock (ServerProcessLock)
		{
			if (currentServerProcess == null || currentServerProcess.HasExited)
			{
				return false;
			}
			currentServerProcess.StandardInput.WriteLine(command);
			currentServerProcess.StandardInput.Flush();
			if (string.Equals(command.Trim(), "stop", StringComparison.OrdinalIgnoreCase))
			{
				Interlocked.Exchange(ref currentServerStopRequested, 1);
			}
			return true;
		}
	}

	private static bool AskForEulaGui()
	{
		DialogResult result = MessageBox.Show(
			Localization.T("Eula.Text"),
			Localization.T("Eula.Title"),
			MessageBoxButtons.YesNo,
			MessageBoxIcon.Question);
		return result == DialogResult.Yes;
	}

	private static LauncherOptions ConfigureServerPropertiesGui(string serversRootDirectory, bool forceDialog)
	{
		Directory.CreateDirectory(serversRootDirectory);
		string activeProfileName = ReadActiveProfileName(serversRootDirectory);
		string serverDirectory = GetProfileDirectory(serversRootDirectory, activeProfileName);
		Directory.CreateDirectory(serverDirectory);
		string markerPath = Path.Combine(serverDirectory, ".launcher-properties-configured");
		Dictionary<string, string> launcherProperties = ReadSimpleProperties(markerPath);
		int recommendedMemory = ChooseMaximumMemoryGb();
		int maximumMemory = GetSafeMemoryMaximumGb();
		LauncherOptions currentOptions = launcherProperties.Count > 0
			? ReadLauncherOptionsFromProperties(serversRootDirectory, launcherProperties, activeProfileName, recommendedMemory, true)
			: new LauncherOptions();
		if (launcherProperties.Count == 0)
		{
			currentOptions.ProfileName = activeProfileName;
			currentOptions.ServerDirectory = serverDirectory;
			currentOptions.ServerType = "paper";
			currentOptions.MinecraftVersion = "26.2";
			currentOptions.IncludeSnapshots = false;
			currentOptions.UseManualJar = false;
			currentOptions.ManualJarPath = string.Empty;
			currentOptions.CustomJavaMajor = 25;
			currentOptions.MemoryGb = recommendedMemory;
			currentOptions.AutoUpdate = true;
			currentOptions.OwnerName = GetDefaultOwnerName();
		}
		bool validOptions = currentOptions.MemoryGb >= 2
			&& currentOptions.MemoryGb <= maximumMemory
			&& IsValidOwnerName(currentOptions.OwnerName)
			&& IsValidProfileName(currentOptions.ProfileName);

		if (validOptions && !forceDialog)
		{
			return currentOptions;
		}

		ServerSettings settings = LoadServerSettings(currentOptions.ServerDirectory, currentOptions);
		ServerSettings selected = null;
		while (selected == null)
		{
			ServerSetupForm setup = null;
			DialogResult dialogResult = DialogResult.Cancel;
			Action showDialog = delegate
			{
				setup = new ServerSetupForm(settings, recommendedMemory, maximumMemory, File.Exists(markerPath), Directory.Exists(Path.Combine(currentOptions.ServerDirectory, "world")));
				dialogResult = setup.ShowDialog(launcherForm);
			};
			if (launcherForm != null && launcherForm.InvokeRequired)
			{
				launcherForm.Invoke(showDialog);
			}
			else
			{
				showDialog();
			}
			if (dialogResult != DialogResult.OK || setup == null)
			{
				throw new OperationCanceledException("서버 설정이 취소되었습니다.");
			}

			ServerSettings candidate = setup.SelectedSettings;
			LauncherOptions candidateOptions = CreateLauncherOptionsFromSettings(serversRootDirectory, candidate);
			bool changesDirectory = !string.Equals(Path.GetFullPath(candidateOptions.ServerDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(currentOptions.ServerDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
			if (changesDirectory && Directory.Exists(candidateOptions.ServerDirectory) && Directory.GetFileSystemEntries(candidateOptions.ServerDirectory).Length > 0)
			{
				MessageBox.Show(launcherForm, Localization.CurrentLanguage == Localization.Korean ? "같은 이름의 서버 프로필이 이미 있습니다. 다른 이름을 입력해 주세요." : "A server profile with that name already exists. Choose another name.", Localization.CurrentLanguage == Localization.Korean ? "프로필 이름 중복" : "Duplicate profile name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				settings = candidate;
				continue;
			}
			selected = candidate;
		}
		if (Directory.Exists(currentOptions.ServerDirectory)
			&& File.Exists(Path.Combine(currentOptions.ServerDirectory, "server.properties"))
			&& (!string.Equals(selected.ServerType, currentOptions.ServerType, StringComparison.OrdinalIgnoreCase) || !string.Equals(selected.MinecraftVersion, currentOptions.MinecraftVersion, StringComparison.OrdinalIgnoreCase)))
		{
			CreateServerBackup(currentOptions.ServerDirectory);
		}
		LauncherOptions selectedOptions = CreateLauncherOptionsFromSettings(serversRootDirectory, selected);
		bool movesProfileDirectory = !string.Equals(Path.GetFullPath(selectedOptions.ServerDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(currentOptions.ServerDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
		if (movesProfileDirectory && Directory.Exists(currentOptions.ServerDirectory))
		{
			if (Directory.Exists(selectedOptions.ServerDirectory))
			{
				Directory.Delete(selectedOptions.ServerDirectory);
			}
			Directory.Move(currentOptions.ServerDirectory, selectedOptions.ServerDirectory);
		}
		currentOptions = selectedOptions;
		Directory.CreateDirectory(currentOptions.ServerDirectory);
		string propertiesPath = Path.Combine(currentOptions.ServerDirectory, "server.properties");
		BackupConfigurationFile(propertiesPath);
		ApplyServerProperties(propertiesPath, selected);
		WriteLauncherOptions(Path.Combine(currentOptions.ServerDirectory, ".launcher-properties-configured"), currentOptions);
		WriteActiveProfileName(serversRootDirectory, currentOptions.ProfileName);
		Console.WriteLine("서버 설정을 저장했습니다: " + propertiesPath);
		if (selected.WhiteList)
		{
			Console.WriteLine("화이트리스트가 켜져 있습니다. 콘솔에서 whitelist add 플레이어이름 명령으로 사용자를 추가하세요.");
		}
		return currentOptions;
	}

	private static ServerSettings LoadServerSettings(string serverDirectory, LauncherOptions options)
	{
		ServerSettings settings = new ServerSettings();
		settings.ProfileName = options.ProfileName;
		settings.ServerType = NormalizeServerType(options.ServerType);
		settings.MinecraftVersion = options.MinecraftVersion;
		settings.IncludeSnapshots = options.IncludeSnapshots;
		settings.UseManualJar = options.UseManualJar;
		settings.ManualJarPath = options.ManualJarPath;
		settings.CustomJavaMajor = options.CustomJavaMajor >= 8 && options.CustomJavaMajor <= 30 ? options.CustomJavaMajor : 25;
		settings.PresetName = "보통 야생";
		settings.GameMode = "survival";
		settings.Difficulty = "normal";
		settings.LevelType = "minecraft:normal";
		settings.MaxPlayers = 20;
		settings.Motd = "A Minecraft Server";
		settings.ServerPort = 25565;
		settings.Pvp = true;
		settings.WhiteList = false;
		settings.Hardcore = false;
		settings.ViewDistance = 32;
		settings.SimulationDistance = 32;
		settings.CommandBlock = true;
		settings.OnlineMode = true;
		settings.MemoryGb = options.MemoryGb;
		settings.AutoUpdate = options.AutoUpdate;
		settings.OwnerName = options.OwnerName;

		Dictionary<string, string> values = ReadSimpleProperties(Path.Combine(serverDirectory, "server.properties"));
		settings.GameMode = GetStringProperty(values, "gamemode", settings.GameMode);
		settings.Difficulty = GetStringProperty(values, "difficulty", settings.Difficulty);
		settings.LevelType = GetStringProperty(values, "level-type", settings.LevelType);
		settings.Motd = GetStringProperty(values, "motd", settings.Motd).Replace("\\n", " ").Replace("\\\\", "\\");
		settings.MaxPlayers = GetIntegerProperty(values, "max-players", settings.MaxPlayers, 1, 1000);
		settings.ServerPort = GetIntegerProperty(values, "server-port", settings.ServerPort, 1, 65535);
		settings.ViewDistance = GetIntegerProperty(values, "view-distance", settings.ViewDistance, 3, 32);
		settings.SimulationDistance = GetIntegerProperty(values, "simulation-distance", settings.SimulationDistance, 3, 32);
		settings.Pvp = GetBooleanProperty(values, "pvp", settings.Pvp);
		settings.WhiteList = GetBooleanProperty(values, "white-list", settings.WhiteList);
		settings.Hardcore = GetBooleanProperty(values, "hardcore", settings.Hardcore);
		settings.CommandBlock = GetBooleanProperty(values, "enable-command-block", settings.CommandBlock);
		settings.OnlineMode = GetBooleanProperty(values, "online-mode", settings.OnlineMode);
		settings.PresetName = DetectPresetName(settings);
		return settings;
	}

	private static string GetStringProperty(Dictionary<string, string> values, string key, string fallback)
	{
		return values.ContainsKey(key) && !string.IsNullOrEmpty(values[key]) ? values[key] : fallback;
	}

	private static int GetIntegerProperty(Dictionary<string, string> values, string key, int fallback, int minimum, int maximum)
	{
		int value;
		return values.ContainsKey(key) && int.TryParse(values[key], out value) && value >= minimum && value <= maximum ? value : fallback;
	}

	private static bool GetBooleanProperty(Dictionary<string, string> values, string key, bool fallback)
	{
		bool value;
		return values.ContainsKey(key) && bool.TryParse(values[key], out value) ? value : fallback;
	}

	private static string DetectPresetName(ServerSettings settings)
	{
		if (settings.Hardcore && settings.GameMode == "survival" && settings.Difficulty == "hard")
		{
			return "하드코어 야생";
		}
		if (settings.GameMode == "creative")
		{
			return settings.LevelType == "minecraft:flat" ? "크리에이티브 월드 (평지)" : "크리에이티브 월드 (일반 지형)";
		}
		if (settings.GameMode == "survival")
		{
			if (settings.Difficulty == "peaceful") return "평화로움 야생";
			if (settings.Difficulty == "easy") return "쉬움 야생";
			if (settings.Difficulty == "normal") return "보통 야생";
			if (settings.Difficulty == "hard") return "어려움 야생";
		}
		return "직접 설정";
	}

	private sealed class LauncherTextWriter : TextWriter
	{
		private readonly object sync = new object();
		private readonly StringBuilder pending = new StringBuilder();

		public override Encoding Encoding
		{
			get { return new UTF8Encoding(false); }
		}

		public override void Write(char value)
		{
			lock (sync)
			{
				if (value == '\n' || value == '\r')
				{
					if (pending.Length > 0)
					{
						FlushPending();
					}
					return;
				}
				pending.Append(value);
			}
		}

		public override void Write(string value)
		{
			if (value == null)
			{
				return;
			}
			foreach (char character in value)
			{
				Write(character);
			}
		}

		public override void WriteLine(string value)
		{
			lock (sync)
			{
				if (!string.IsNullOrEmpty(value))
				{
					pending.Append(value);
				}
				FlushPending();
			}
		}

		private void FlushPending()
		{
			string line = pending.ToString();
			pending.Length = 0;
			LauncherForm form = launcherForm;
			if (form != null)
			{
				form.AppendConsole(line);
			}
		}
	}

	private sealed class ThemePalette
	{
		public Color Window;
		public Color Card;
		public Color CardSecondary;
		public Color Text;
		public Color Muted;
		public Color Border;
		public Color Accent;
		public Color AccentHover;
		public Color AccentSoft;
		public Color Danger;
		public Color DangerSoft;
		public Color Success;
		public Color Warning;
		public Color Console;

		public static ThemePalette Create(bool dark)
		{
			ThemePalette palette = new ThemePalette();
			if (SystemInformation.HighContrast)
			{
				palette.Window = SystemColors.Window;
				palette.Card = SystemColors.Control;
				palette.CardSecondary = SystemColors.Window;
				palette.Text = SystemColors.WindowText;
				palette.Muted = SystemColors.GrayText;
				palette.Border = SystemColors.WindowText;
				palette.Accent = SystemColors.Highlight;
				palette.AccentHover = SystemColors.HotTrack;
				palette.AccentSoft = SystemColors.Control;
				palette.Danger = SystemColors.HotTrack;
				palette.DangerSoft = SystemColors.Control;
				palette.Success = SystemColors.Highlight;
				palette.Warning = SystemColors.HotTrack;
				palette.Console = SystemColors.Window;
				return palette;
			}
			if (dark)
			{
				palette.Window = Color.FromArgb(23, 23, 28);
				palette.Card = Color.FromArgb(32, 32, 38);
				palette.CardSecondary = Color.FromArgb(42, 43, 50);
				palette.Text = Color.FromArgb(242, 244, 246);
				palette.Muted = Color.FromArgb(174, 180, 188);
				palette.Border = Color.FromArgb(58, 59, 68);
				palette.Console = Color.FromArgb(15, 16, 20);
			}
			else
			{
				palette.Window = Color.FromArgb(246, 247, 249);
				palette.Card = Color.White;
				palette.CardSecondary = Color.FromArgb(242, 244, 246);
				palette.Text = Color.FromArgb(25, 31, 40);
				palette.Muted = Color.FromArgb(82, 94, 108);
				palette.Border = Color.FromArgb(220, 226, 232);
				palette.Console = Color.FromArgb(25, 28, 34);
			}
			palette.Accent = Color.FromArgb(0, 100, 255);
			palette.AccentHover = Color.FromArgb(0, 78, 214);
			palette.AccentSoft = dark ? Color.FromArgb(38, 61, 96) : Color.FromArgb(232, 240, 254);
			palette.Danger = dark ? Color.FromArgb(255, 113, 128) : Color.FromArgb(197, 40, 61);
			palette.DangerSoft = dark ? Color.FromArgb(78, 40, 46) : Color.FromArgb(255, 235, 238);
			palette.Success = dark ? Color.FromArgb(49, 214, 155) : Color.FromArgb(8, 122, 85);
			palette.Warning = dark ? Color.FromArgb(255, 176, 103) : Color.FromArgb(165, 78, 0);
			return palette;
		}
	}

	private sealed partial class LauncherForm : Form
	{
		private readonly RoundedPanel statusPill;
		private readonly Label statusLabel;
		private readonly Label statusDot;
		private readonly Label noticeLabel;
		private readonly Panel loadingPanel;
		private readonly Label loadingDetailLabel;
		private readonly ProgressBar loadingProgress;
		private readonly TextBox addressBox;
		private readonly Button copyButton;
		private readonly Button startButton;
		private readonly Button stopButton;
		private readonly Button settingsButton;
		private readonly Button upgradeButton;
		private readonly Button consoleButton;
		private readonly Button profilesButton;
		private readonly Button multiServerButton;
		private readonly Button backupButton;
		private readonly Button contentButton;
		private readonly Button playersButton;
		private readonly Button networkButton;
		private readonly Button diagnosticsButton;
		private readonly Button themeButton;
		private readonly Button languageButton;
		private readonly Button launcherUpdateButton;
		private readonly Panel consolePanel;
		private readonly RichTextBox consoleBox;
		private readonly TextBox commandBox;
		private readonly Button sendButton;
		private readonly TextBox consoleSearchBox;
		private readonly ComboBox consoleFilterBox;
		private readonly CheckBox consoleWrapBox;
		private readonly List<string> consoleHistory = new List<string>();
		private readonly Dictionary<Control, string> localizedControls = new Dictionary<Control, string>();
		private readonly Dictionary<string, Form> modelessToolWindows = new Dictionary<string, Form>(StringComparer.Ordinal);
		private readonly HashSet<string> modelessToolsBlockingServerChanges = new HashSet<string>(StringComparer.Ordinal);
		private readonly ConcurrentQueue<string> consoleQueue = new ConcurrentQueue<string>();
		private readonly System.Windows.Forms.Timer consoleTimer;
		private int queuedConsoleLines;
		private bool darkTheme;
		private bool statusWarning;
		private bool noticeWarning;
		private bool workflowRunning;
		private bool serverRunning;
		private bool closeAfterStop;
		private bool startupInitializing;
		private bool launcherUpdateChecking;
		private readonly string themePath;
		private readonly string languagePath;
		private string statusTextKey;
		private string noticeTextKey;

		public bool UsesDarkTheme
		{
			get { return darkTheme; }
		}

		public LauncherForm()
		{
			ApplyLauncherWindowIcon(this);
			themePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paper26.2Server", "launcher-ui.properties");
			languagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paper26.2Server", "launcher-language.properties");
			darkTheme = LoadTheme();
			Localization.CurrentLanguage = LoadLanguage();
			Text = Localization.T("App.Title") + " " + BuildVersionInfo.DisplayVersion;
			Font = new Font("Segoe UI Variable Text", 10F);
			StartPosition = FormStartPosition.CenterScreen;
			MinimumSize = new Size(940, 720);
			Size = new Size(1120, 760);
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = true;
			KeyPreview = true;
			AutoScaleMode = AutoScaleMode.Dpi;
			consoleTimer = new System.Windows.Forms.Timer();
			consoleTimer.Interval = 100;
			consoleTimer.Tick += delegate { FlushConsoleQueue(); };
			consoleTimer.Start();

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(36, 28, 36, 30);
			root.ColumnCount = 1;
			root.RowCount = 4;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 322F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			Controls.Add(root);

			Panel header = new Panel();
			header.Dock = DockStyle.Fill;
			Label title = new Label();
			Localize(title, "App.HeaderTitle");
			title.Font = new Font("Segoe UI Variable Display Semib", 22F);
			title.AutoSize = true;
			title.Location = new Point(0, 0);
			header.Controls.Add(title);
			Label subtitle = new Label();
			Localize(subtitle, "App.Subtitle");
			subtitle.AutoSize = true;
			subtitle.Location = new Point(2, 44);
			subtitle.Tag = "muted";
			header.Controls.Add(subtitle);
			languageButton = CreateButton(Localization.LanguageButtonText(), 86);
			languageButton.Tag = "ghost";
			languageButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			languageButton.Location = new Point(header.Width - 184, 4);
			languageButton.Click += delegate
			{
				Localization.ToggleLanguage();
				SaveLanguage();
				ApplyLocalization();
				ShowNoticeKey("Language.Changed", false);
			};
			header.Controls.Add(languageButton);
			launcherUpdateButton = CreateButton(Localization.T("Button.LauncherUpdate"), 168);
			launcherUpdateButton.Tag = "ghost";
			launcherUpdateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			SetButtonIcon(launcherUpdateButton, ButtonIcon.Upgrade);
			launcherUpdateButton.Click += delegate { CheckLauncherUpdateNow(); };
			header.Controls.Add(launcherUpdateButton);
			themeButton = CreateButton(string.Empty, 110);
			themeButton.Tag = "ghost";
			themeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			themeButton.Location = new Point(header.Width - 110, 4);
			themeButton.Click += delegate { darkTheme = !darkTheme; SaveTheme(); ApplyTheme(); ApplyLocalization(); };
			header.Controls.Add(themeButton);
			header.Resize += delegate
			{
				themeButton.Left = header.ClientSize.Width - themeButton.Width;
				languageButton.Left = themeButton.Left - languageButton.Width - 8;
				launcherUpdateButton.Left = languageButton.Left - launcherUpdateButton.Width - 8;
			};
			root.Controls.Add(header, 0, 0);

			RoundedPanel card = new RoundedPanel();
			card.Dock = DockStyle.Fill;
			card.Padding = new Padding(28, 24, 28, 24);
			card.CornerRadius = 28;
			card.Tag = "main-card";
			root.Controls.Add(card, 0, 1);

			statusPill = new RoundedPanel();
			statusPill.Location = new Point(24, 18);
			statusPill.Size = new Size(150, 36);
			statusPill.CornerRadius = 18;
			statusPill.Tag = "surface";
			card.Controls.Add(statusPill);
			statusDot = new Label();
			statusDot.Text = "●";
			statusDot.Font = new Font("Segoe UI", 10F);
			statusDot.AutoSize = true;
			statusDot.Location = new Point(14, 9);
			statusPill.Controls.Add(statusDot);
			statusLabel = new Label();
			statusTextKey = "Status.Off";
			statusLabel.Text = Localization.T(statusTextKey);
			statusLabel.Font = new Font("Segoe UI Variable Text Semib", 10F);
			statusLabel.AutoSize = true;
			statusLabel.Location = new Point(36, 8);
			statusPill.Controls.Add(statusLabel);

			RoundedPanel addressSurface = new RoundedPanel();
			addressSurface.Location = new Point(24, 66);
			addressSurface.Size = new Size(760, 74);
			addressSurface.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			addressSurface.CornerRadius = 20;
			addressSurface.Tag = "surface";
			card.Controls.Add(addressSurface);
			Label addressTitle = new Label();
			Localize(addressTitle, "Address.Title");
			addressTitle.Font = new Font("Segoe UI Variable Text Semib", 8.5F);
			addressTitle.AutoSize = true;
			addressTitle.Location = new Point(18, 12);
			addressTitle.Tag = "muted";
			addressSurface.Controls.Add(addressTitle);
			addressBox = new TextBox();
			addressBox.ReadOnly = true;
			addressBox.Text = Localization.T("Address.Empty");
			addressBox.BorderStyle = BorderStyle.None;
			addressBox.Font = new Font("Segoe UI Variable Text Semib", 11F);
			addressBox.Location = new Point(18, 38);
			addressBox.Size = new Size(600, 24);
			addressBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			addressSurface.Controls.Add(addressBox);
			copyButton = CreateButton(Localization.T("Address.Copy"), 104);
			SetButtonIcon(copyButton, ButtonIcon.Copy);
			Localize(copyButton, "Address.Copy");
			copyButton.Tag = "secondary";
			copyButton.Enabled = false;
			copyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			copyButton.Location = new Point(addressSurface.Width - 118, 17);
			copyButton.Click += delegate
			{
				try
				{
					if (copyButton.Enabled)
					{
						Clipboard.SetText(addressBox.Text);
						ShowNoticeKey("Address.Copied", false);
					}
				}
				catch (Exception exception)
				{
					ShowNotice((Localization.CurrentLanguage == Localization.Korean ? "주소를 복사하지 못했습니다: " : "Could not copy the address: ") + exception.Message, true);
				}
			};
			addressSurface.Controls.Add(copyButton);

			Label controlSectionLabel = new Label();
			Localize(controlSectionLabel, "Main.ControlSection");
			controlSectionLabel.Font = new Font("Segoe UI Variable Text Semib", 8.5F);
			controlSectionLabel.AutoSize = true;
			controlSectionLabel.Location = new Point(24, 148);
			controlSectionLabel.Tag = "muted";
			card.Controls.Add(controlSectionLabel);

			TableLayoutPanel primaryActions = new TableLayoutPanel();
			primaryActions.Location = new Point(18, 164);
			primaryActions.Size = new Size(card.ClientSize.Width - 36, 54);
			primaryActions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			primaryActions.ColumnCount = 5;
			primaryActions.RowCount = 1;
			for (int column = 0; column < 5; column++) primaryActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
			primaryActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			card.Controls.Add(primaryActions);

			startButton = CreateButton(Localization.T("Button.Start"), 152);
			SetButtonIcon(startButton, ButtonIcon.Play);
			Localize(startButton, "Button.Start");
			startButton.Tag = "primary";
			startButton.Dock = DockStyle.Fill;
			startButton.Margin = new Padding(6, 5, 6, 5);
			startButton.Click += delegate { StartWorkflow(); };
			primaryActions.Controls.Add(startButton, 0, 0);
			stopButton = CreateButton(Localization.T("Button.Stop"), 136);
			SetButtonIcon(stopButton, ButtonIcon.Stop);
			Localize(stopButton, "Button.Stop");
			stopButton.Tag = "danger";
			stopButton.Dock = DockStyle.Fill;
			stopButton.Margin = new Padding(6, 5, 6, 5);
			stopButton.Enabled = false;
			stopButton.Click += delegate { StopServer(); };
			primaryActions.Controls.Add(stopButton, 1, 0);
			settingsButton = CreateButton(Localization.T("Button.Settings"), 92);
			SetButtonIcon(settingsButton, ButtonIcon.Settings);
			Localize(settingsButton, "Button.Settings");
			settingsButton.Tag = "secondary";
			settingsButton.Dock = DockStyle.Fill;
			settingsButton.Margin = new Padding(6, 5, 6, 5);
			settingsButton.Click += delegate { OpenSettings(); };
			primaryActions.Controls.Add(settingsButton, 2, 0);
			upgradeButton = CreateButton(Localization.T("Button.Upgrade"), 118);
			SetButtonIcon(upgradeButton, ButtonIcon.Upgrade);
			Localize(upgradeButton, "Button.Upgrade");
			upgradeButton.Tag = "secondary";
			upgradeButton.Dock = DockStyle.Fill;
			upgradeButton.Margin = new Padding(6, 5, 6, 5);
			upgradeButton.Click += delegate { UpgradeServerFiles(); };
			primaryActions.Controls.Add(upgradeButton, 3, 0);
			consoleButton = CreateButton(Localization.T("Button.ConsoleOpen"), 126);
			SetButtonIcon(consoleButton, ButtonIcon.Console);
			consoleButton.Tag = "secondary";
			consoleButton.Dock = DockStyle.Fill;
			consoleButton.Margin = new Padding(6, 5, 6, 5);
			consoleButton.Click += delegate { ToggleConsole(); };
			primaryActions.Controls.Add(consoleButton, 4, 0);

			Label toolSectionLabel = new Label();
			Localize(toolSectionLabel, "Main.ToolSection");
			toolSectionLabel.Font = new Font("Segoe UI Variable Text Semib", 8.5F);
			toolSectionLabel.AutoSize = true;
			toolSectionLabel.Location = new Point(24, 222);
			toolSectionLabel.Tag = "muted";
			card.Controls.Add(toolSectionLabel);

			TableLayoutPanel toolActions = new TableLayoutPanel();
			toolActions.Location = new Point(18, 238);
			toolActions.Size = new Size(card.ClientSize.Width - 36, 54);
			toolActions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			toolActions.ColumnCount = 6;
			toolActions.RowCount = 1;
			for (int column = 0; column < 6; column++) toolActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.6667F));
			toolActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			card.Controls.Add(toolActions);

			profilesButton = CreateButton(Localization.T("Button.ServerManagement"), 134);
			SetButtonIcon(profilesButton, ButtonIcon.Server);
			Localize(profilesButton, "Button.ServerManagement");
			profilesButton.Tag = "secondary";
			profilesButton.Dock = DockStyle.Fill;
			profilesButton.Margin = new Padding(6, 5, 6, 5);
			profilesButton.Click += delegate { RunUiAction(OpenServerManagement); };
			toolActions.Controls.Add(profilesButton, 0, 0);
			multiServerButton = CreateButton(Localization.T("Button.MultiServer"), 112);
			multiServerButton.Tag = "secondary";
			multiServerButton.Visible = false;
			backupButton = CreateButton(Localization.T("Button.Backup"), 110);
			SetButtonIcon(backupButton, ButtonIcon.Backup);
			Localize(backupButton, "Button.Backup");
			backupButton.Tag = "secondary";
			backupButton.Dock = DockStyle.Fill;
			backupButton.Margin = new Padding(6, 5, 6, 5);
			backupButton.Click += delegate { RunUiAction(OpenBackupManager); };
			toolActions.Controls.Add(backupButton, 1, 0);
			contentButton = CreateButton(Localization.T("Button.Content"), 100);
			SetButtonIcon(contentButton, ButtonIcon.Content);
			Localize(contentButton, "Button.Content");
			contentButton.Tag = "secondary";
			contentButton.Dock = DockStyle.Fill;
			contentButton.Margin = new Padding(6, 5, 6, 5);
			contentButton.Click += delegate { RunUiAction(OpenContentManager); };
			toolActions.Controls.Add(contentButton, 2, 0);
			playersButton = CreateButton(Localization.T("Button.Players"), 100);
			SetButtonIcon(playersButton, ButtonIcon.Players);
			Localize(playersButton, "Button.Players");
			playersButton.Tag = "secondary";
			playersButton.Dock = DockStyle.Fill;
			playersButton.Margin = new Padding(6, 5, 6, 5);
			playersButton.Click += delegate { RunUiAction(OpenPlayerManager); };
			toolActions.Controls.Add(playersButton, 3, 0);
			networkButton = CreateButton(Localization.T("Button.Network"), 110);
			SetButtonIcon(networkButton, ButtonIcon.Network);
			Localize(networkButton, "Button.Network");
			networkButton.Tag = "secondary";
			networkButton.Dock = DockStyle.Fill;
			networkButton.Margin = new Padding(6, 5, 6, 5);
			networkButton.Click += delegate { RunUiAction(OpenNetworkTools); };
			toolActions.Controls.Add(networkButton, 4, 0);
			diagnosticsButton = CreateButton(Localization.T("Button.Diagnostics"), 110);
			SetButtonIcon(diagnosticsButton, ButtonIcon.Diagnostics);
			Localize(diagnosticsButton, "Button.Diagnostics");
			diagnosticsButton.Tag = "secondary";
			diagnosticsButton.Dock = DockStyle.Fill;
			diagnosticsButton.Margin = new Padding(6, 5, 6, 5);
			diagnosticsButton.Click += delegate { CreateDiagnostics(); };
			toolActions.Controls.Add(diagnosticsButton, 5, 0);
			Label featureList = new Label();
			Localize(featureList, "Features");
			featureList.Font = new Font("Segoe UI Variable Text", 9F);
			featureList.AutoSize = false;
			featureList.Location = new Point(26, 278);
			featureList.Size = new Size(740, 22);
			featureList.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
			featureList.Visible = false;
			card.Resize += delegate
			{
				addressSurface.Width = card.ClientSize.Width - 48;
				primaryActions.Width = card.ClientSize.Width - 36;
				toolActions.Width = card.ClientSize.Width - 36;
				copyButton.Left = addressSurface.ClientSize.Width - copyButton.Width - 14;
				addressBox.Width = copyButton.Left - addressBox.Left - 10;
				featureList.Width = card.ClientSize.Width - 52;
			};

			Panel noticeArea = new Panel();
			noticeArea.Dock = DockStyle.Fill;
			root.Controls.Add(noticeArea, 0, 2);
			noticeLabel = new Label();
			noticeLabel.Dock = DockStyle.Top;
			noticeLabel.Height = 30;
			noticeLabel.TextAlign = ContentAlignment.MiddleLeft;
			noticeLabel.AutoEllipsis = true;
			noticeTextKey = "Notice.Ready";
			noticeLabel.Text = Localization.T(noticeTextKey);
			noticeLabel.Font = new Font("Segoe UI Variable Text", 9.5F);
			noticeArea.Controls.Add(noticeLabel);
			loadingPanel = new Panel();
			loadingPanel.Dock = DockStyle.Bottom;
			loadingPanel.Height = 28;
			loadingPanel.Visible = true;
			noticeArea.Controls.Add(loadingPanel);
			loadingDetailLabel = new Label();
			loadingDetailLabel.Dock = DockStyle.Fill;
			loadingDetailLabel.TextAlign = ContentAlignment.MiddleLeft;
			loadingDetailLabel.Text = Localization.CurrentLanguage == Localization.Korean ? "런처 화면을 준비하고 있습니다…" : "Preparing the launcher…";
			loadingPanel.Controls.Add(loadingDetailLabel);
			loadingProgress = new ProgressBar();
			loadingProgress.Dock = DockStyle.Right;
			loadingProgress.Width = 220;
			loadingProgress.Style = ProgressBarStyle.Marquee;
			loadingProgress.MarqueeAnimationSpeed = GetMarqueeAnimationSpeed();
			loadingPanel.Controls.Add(loadingProgress);

			Panel workspacePanel = new Panel();
			workspacePanel.Dock = DockStyle.Fill;
			workspacePanel.Padding = new Padding(0, 8, 0, 0);
			root.Controls.Add(workspacePanel, 0, 3);

			consolePanel = new Panel();
			consolePanel.Dock = DockStyle.Fill;
			consolePanel.Padding = new Padding(0, 0, 12, 0);
			consolePanel.Visible = false;
			workspacePanel.Controls.Add(consolePanel);
			InitializeQuickCommandPanel(workspacePanel);
			consoleBox = new RichTextBox();
			consoleBox.Dock = DockStyle.Fill;
			consoleBox.ReadOnly = true;
			consoleBox.BorderStyle = BorderStyle.None;
			consoleBox.Font = new Font("Consolas", 9F);
			consoleBox.WordWrap = false;
			consoleBox.DetectUrls = true;
			consolePanel.Controls.Add(consoleBox);
			Panel consoleToolbar = new Panel();
			consoleToolbar.Dock = DockStyle.Top;
			consoleToolbar.Height = 40;
			consoleToolbar.Padding = new Padding(0, 0, 0, 8);
			consolePanel.Controls.Add(consoleToolbar);
			consoleSearchBox = new TextBox();
			consoleSearchBox.Width = 240;
			consoleSearchBox.Dock = DockStyle.Left;
			consoleSearchBox.TextChanged += delegate { RebuildConsoleView(); };
			consoleToolbar.Controls.Add(consoleSearchBox);
			consoleFilterBox = new ModernComboBox();
			consoleFilterBox.DropDownStyle = ComboBoxStyle.DropDownList;
			consoleFilterBox.Width = 136;
			consoleFilterBox.Dock = DockStyle.Left;
			consoleFilterBox.Items.AddRange(new object[] { Localization.T("Console.All"), Localization.T("Console.Warning"), Localization.T("Console.Compatibility"), Localization.T("Console.Error") });
			consoleFilterBox.SelectedIndex = 0;
			consoleFilterBox.SelectedIndexChanged += delegate { RebuildConsoleView(); };
			consoleToolbar.Controls.Add(consoleFilterBox);
			consoleWrapBox = new CheckBox();
			Localize(consoleWrapBox, "Console.Wrap");
			consoleWrapBox.Width = 100;
			consoleWrapBox.Dock = DockStyle.Left;
			consoleWrapBox.CheckedChanged += delegate { consoleBox.WordWrap = consoleWrapBox.Checked; };
			consoleToolbar.Controls.Add(consoleWrapBox);
			Panel commandPanel = new Panel();
			commandPanel.Dock = DockStyle.Bottom;
			commandPanel.Height = 40;
			commandPanel.Padding = new Padding(0, 8, 0, 0);
			consolePanel.Controls.Add(commandPanel);
			commandBox = new TextBox();
			commandBox.Dock = DockStyle.Fill;
			commandBox.Enabled = false;
			commandBox.TextChanged += delegate { sendButton.Enabled = serverRunning && !string.IsNullOrWhiteSpace(commandBox.Text); };
			commandBox.KeyDown += delegate(object sender, KeyEventArgs eventArgs)
			{
				if (eventArgs.KeyCode == Keys.Enter)
				{
					SendCommandFromBox();
					eventArgs.SuppressKeyPress = true;
				}
			};
			commandPanel.Controls.Add(commandBox);
			sendButton = CreateButton(Localization.T("Button.Send"), 86);
			SetButtonIcon(sendButton, ButtonIcon.Send);
			Localize(sendButton, "Button.Send");
			sendButton.Tag = "primary";
			sendButton.Dock = DockStyle.Right;
			sendButton.Enabled = false;
			sendButton.Click += delegate { SendCommandFromBox(); };
			commandPanel.Controls.Add(sendButton);

			FormClosing += OnLauncherClosing;
			FormClosed += delegate { consoleTimer.Stop(); };
			Shown += delegate { BeginInvoke((MethodInvoker)BeginStartupInitialization); };
			startButton.Enabled = false;
			playersButton.Enabled = false;
			ConfigureAccessibleField(addressBox, Localization.T("Address.Title"), Localization.CurrentLanguage == Localization.Korean ? "친구가 서버에 접속할 때 사용하는 주소입니다." : "The address friends use to join the server.");
			ConfigureAccessibleField(consoleSearchBox, Localization.T("Console.Search"), Localization.CurrentLanguage == Localization.Korean ? "표시된 콘솔 로그를 검색합니다." : "Search the visible console log.");
			ConfigureAccessibleField(consoleFilterBox, Localization.T("Console.All"), Localization.CurrentLanguage == Localization.Korean ? "일반 경고, 호환성 안내와 오류를 구분해 표시합니다." : "Separate actionable warnings, compatibility notices, and errors.");
			ConfigureAccessibleField(commandBox, Localization.CurrentLanguage == Localization.Korean ? "서버 명령" : "Server command", Localization.CurrentLanguage == Localization.Korean ? "실행 중인 서버에 보낼 명령을 입력합니다." : "Enter a command to send to the running server.");
			ApplyTheme();
			ApplyLocalization();
		}

		protected override bool ProcessCmdKey(ref Message message, Keys keyData)
		{
			if (keyData == Keys.F5 && startButton.Enabled)
			{
				StartWorkflow();
				return true;
			}
			if (keyData == (Keys.Shift | Keys.F5) && stopButton.Enabled)
			{
				StopServer();
				return true;
			}
			if (keyData == (Keys.Control | Keys.Oemcomma) && settingsButton.Enabled)
			{
				OpenSettings();
				return true;
			}
			if (keyData == (Keys.Control | Keys.K))
			{
				if (!consolePanel.Visible)
				{
					ToggleConsole();
				}
				consoleSearchBox.Focus();
				consoleSearchBox.SelectAll();
				return true;
			}
			if (keyData == Keys.Escape && consolePanel.Visible)
			{
				ToggleConsole();
				return true;
			}
			return base.ProcessCmdKey(ref message, keyData);
		}

		private static Button CreateButton(string text, int width)
		{
			Button button = new RoundedButton();
			button.Text = text;
			button.Width = width;
			button.Height = 44;
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			button.Cursor = Cursors.Hand;
			return button;
		}

		private static void SetButtonIcon(Button button, ButtonIcon icon)
		{
			RoundedButton roundedButton = button as RoundedButton;
			if (roundedButton != null)
			{
				roundedButton.IconKind = icon;
			}
		}

		private void Localize(Control control, string key)
		{
			localizedControls[control] = key;
			control.Text = Localization.T(key);
		}

		private string LoadLanguage()
		{
			try
			{
				if (File.Exists(languagePath))
				{
					string text = File.ReadAllText(languagePath, Encoding.UTF8).Trim();
					return Localization.NormalizeLanguage(text);
				}
			}
			catch
			{
			}
			return Localization.DetectDefaultLanguage();
		}

		private void SaveLanguage()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(languagePath));
				File.WriteAllText(languagePath, Localization.CurrentLanguage, new UTF8Encoding(false));
			}
			catch
			{
			}
		}

		private void ApplyLocalization()
		{
			Text = Localization.T("App.Title") + " " + BuildVersionInfo.DisplayVersion;
			foreach (KeyValuePair<Control, string> entry in localizedControls)
			{
				if (!entry.Key.IsDisposed)
				{
					entry.Key.Text = Localization.T(entry.Value);
				}
			}
			if (themeButton != null)
			{
				themeButton.Text = darkTheme ? Localization.T("Theme.Light") : Localization.T("Theme.Dark");
			}
			if (languageButton != null)
			{
				languageButton.Text = Localization.LanguageButtonText();
			}
			if (launcherUpdateButton != null)
			{
				launcherUpdateButton.Text = Localization.T("Button.LauncherUpdate");
			}
			if (consoleButton != null)
			{
				consoleButton.Text = consolePanel.Visible ? Localization.T("Button.ConsoleClose") : Localization.T("Button.ConsoleOpen");
			}
			if (statusTextKey != null)
			{
				SetStatusText(Localization.T(statusTextKey), statusWarning);
			}
			if (noticeTextKey != null)
			{
				SetNoticeText(Localization.T(noticeTextKey), noticeWarning);
			}
			if (addressBox != null && addressBox.Text.IndexOf(":") < 0)
			{
				addressBox.Text = Localization.T("Address.Empty");
				copyButton.Enabled = false;
			}
			if (consoleFilterBox != null)
			{
				int selectedFilter = Math.Max(0, consoleFilterBox.SelectedIndex);
				consoleFilterBox.Items.Clear();
				consoleFilterBox.Items.AddRange(new object[] { Localization.T("Console.All"), Localization.T("Console.Warning"), Localization.T("Console.Compatibility"), Localization.T("Console.Error") });
				consoleFilterBox.SelectedIndex = Math.Min(selectedFilter, consoleFilterBox.Items.Count - 1);
			}
			ConfigureAccessibleField(addressBox, Localization.T("Address.Title"), Localization.CurrentLanguage == Localization.Korean ? "친구가 서버에 접속할 때 사용하는 주소입니다." : "The address friends use to join the server.");
			ConfigureAccessibleField(consoleSearchBox, Localization.T("Console.Search"), Localization.CurrentLanguage == Localization.Korean ? "표시된 콘솔 로그를 검색합니다." : "Search the visible console log.");
			ConfigureAccessibleField(consoleFilterBox, Localization.T("Console.All"), Localization.CurrentLanguage == Localization.Korean ? "일반 경고, 호환성 안내와 오류를 구분해 표시합니다." : "Separate actionable warnings, compatibility notices, and errors.");
			ApplyQuickCommandLocalization();
			ApplyCommonButtonToolTips(this);
		}

		private void InitialSetupIfNeeded()
		{
			string serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);
			string activeProfile = ReadActiveProfileName(serversRoot);
			string serverDirectory = GetProfileDirectory(serversRoot, activeProfile);
			string marker = Path.Combine(serverDirectory, ".launcher-properties-configured");
			if (File.Exists(marker))
			{
				int port = ReadConfiguredServerPort(Path.Combine(serverDirectory, "server.properties"), 25565);
				SetConnectionAddress(GetLocalConnectionAddress(port));
				return;
			}
			try
			{
				Directory.CreateDirectory(serversRoot);
				LauncherOptions options = ConfigureServerPropertiesGui(serversRoot, true);
				int port = ReadConfiguredServerPort(Path.Combine(options.ServerDirectory, "server.properties"), 25565);
				SetConnectionAddress(GetLocalConnectionAddress(port));
				ShowNoticeKey("Notice.InitialSaved", false);
			}
			catch (OperationCanceledException)
			{
				ShowNoticeKey("Notice.InitialCanceled", true);
			}
			catch (Exception exception)
			{
				ShowNotice(Localization.F("Notice.SettingsFailed", exception.Message), true);
			}
		}

		private void BeginStartupInitialization()
		{
			if (startupInitializing)
			{
				return;
			}
			if (!EnsureDataRootSelected(this, AppDomain.CurrentDomain.BaseDirectory))
			{
				SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "데이터 위치 선택을 취소해 런처를 종료합니다." : "Data location selection was canceled. Closing the launcher.", false, 0);
				BeginInvoke((MethodInvoker)Close);
				return;
			}
			string updateResult = ConsumeLauncherUpdateResult();
			if (!string.IsNullOrEmpty(updateResult)) ShowNotice(updateResult, true);
			startupInitializing = true;
			SetStartupControlsEnabled(false);
			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "런처 업데이트를 확인하고 있습니다…" : "Checking launcher updates…", true, -1);
			Thread worker = new Thread((ThreadStart)delegate
			{
				bool updateStarted = false;
				try
				{
					try
					{
						updateStarted = StartApprovedLauncherUpdateIfAvailable();
						if (!updateStarted)
						{
							launcherUpdateCheckCompleted = true;
						}
					}
					catch (Exception exception)
					{
						launcherUpdateCheckCompleted = true;
						LauncherUpdateStageException stageException = exception as LauncherUpdateStageException;
						string stage = stageException == null ? "unknown" : stageException.Stage;
						string prefix;
						if (Localization.CurrentLanguage == Localization.Korean)
						{
							prefix = stage == "connection" ? "업데이트 서버 연결 실패: " : stage == "metadata" ? "업데이트 정보 오류: " : stage == "download" ? "업데이트 다운로드 실패: " : stage == "hash" ? "업데이트 해시 불일치: " : "업데이트 실패: ";
						}
						else
						{
							prefix = stage == "connection" ? "Update server unavailable: " : stage == "metadata" ? "Invalid update metadata: " : stage == "download" ? "Update download failed: " : stage == "hash" ? "Update hash mismatch: " : "Update failed: ";
						}
						ShowNotice(prefix + exception.Message + (Localization.CurrentLanguage == Localization.Korean ? " · 서버는 계속 실행할 수 있습니다." : " · You can continue using the server."), true);
					}
					if (updateStarted)
					{
						RequestLauncherClose();
						return;
					}
					PurgeExpiredServerTrash(GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory), DateTime.UtcNow);
					SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "서버 프로필과 설정을 확인하고 있습니다…" : "Checking server profiles and settings…", true, 70);
					if (IsDisposed || Disposing || !IsHandleCreated)
					{
						return;
					}
					try
					{
						Invoke((MethodInvoker)InitialSetupIfNeeded);
					}
					catch (ObjectDisposedException)
					{
					}
					catch (InvalidOperationException)
					{
						// 시작 준비 중 창이 닫히면 최초 설정 표시를 생략합니다.
					}
				}
				finally
				{
					if (!updateStarted && !IsDisposed)
					{
						TryPostToUi(this, (MethodInvoker)delegate
						{
							startupInitializing = false;
							SetStartupControlsEnabled(true);
							SetLoadingState(string.Empty, false, -1);
						});
					}
				}
			});
			worker.IsBackground = true;
			worker.Name = "런처 시작 준비";
			worker.Start();
		}

		private void SetStartupControlsEnabled(bool enabled)
		{
			startButton.Enabled = enabled;
			settingsButton.Enabled = enabled;
			upgradeButton.Enabled = enabled;
			profilesButton.Enabled = enabled;
			multiServerButton.Enabled = enabled;
			backupButton.Enabled = enabled;
			contentButton.Enabled = enabled;
			networkButton.Enabled = enabled;
			diagnosticsButton.Enabled = enabled;
			playersButton.Enabled = enabled && serverRunning;
			launcherUpdateButton.Enabled = enabled && !launcherUpdateChecking;
			UpdateQuickCommandControls();
		}

		private void CheckLauncherUpdateNow()
		{
			if (launcherUpdateChecking || startupInitializing)
			{
				return;
			}
			launcherUpdateChecking = true;
			launcherUpdateButton.Enabled = false;
			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "런처 최신 버전을 새로 확인하고 있습니다…" : "Checking for the latest launcher again…", true, -1);
			Thread worker = new Thread((ThreadStart)delegate
			{
				bool updateStarted = false;
				bool updateAvailable = false;
				try
				{
					updateStarted = StartApprovedLauncherUpdateIfAvailable(true, out updateAvailable);
					launcherUpdateCheckCompleted = true;
					if (updateStarted)
					{
						RequestLauncherClose();
						return;
					}
					ShowNoticeKey(updateAvailable ? "Notice.LauncherDeferred" : "Notice.LauncherLatest", false);
				}
				catch (Exception exception)
				{
					launcherUpdateCheckCompleted = true;
					LauncherUpdateStageException stageException = exception as LauncherUpdateStageException;
					string stage = stageException == null ? "unknown" : stageException.Stage;
					string prefix = Localization.CurrentLanguage == Localization.Korean
						? (stage == "connection" ? "업데이트 서버 연결 실패: " : stage == "metadata" ? "업데이트 정보 오류: " : stage == "download" ? "업데이트 다운로드 실패: " : stage == "hash" ? "업데이트 해시 불일치: " : "업데이트 실패: ")
						: (stage == "connection" ? "Update server unavailable: " : stage == "metadata" ? "Invalid update metadata: " : stage == "download" ? "Update download failed: " : stage == "hash" ? "Update hash mismatch: " : "Update failed: ");
					ShowNotice(prefix + exception.Message, true);
				}
				finally
				{
					if (!updateStarted && !IsDisposed)
					{
						TryPostToUi(this, (MethodInvoker)delegate
						{
							launcherUpdateChecking = false;
							launcherUpdateButton.Enabled = true;
							SetLoadingState(string.Empty, false, -1);
						});
					}
				}
			});
			worker.IsBackground = true;
			worker.Name = "런처 업데이트 수동 확인";
			worker.Start();
		}

		public void SetLoadingState(string message, bool active, int percent)
		{
			if (IsDisposed)
			{
				return;
			}
			if (InvokeRequired)
			{
				TryPostToUi(this, (MethodInvoker)delegate { SetLoadingState(message, active, percent); });
				return;
			}
			loadingPanel.Visible = active;
			loadingDetailLabel.Text = message ?? string.Empty;
			loadingDetailLabel.AccessibleName = loadingDetailLabel.Text;
			loadingProgress.AccessibleName = loadingDetailLabel.Text;
			UseWaitCursor = active;
			if (!active)
			{
				return;
			}
			if (percent >= 0 && percent <= 100)
			{
				loadingProgress.Style = ProgressBarStyle.Continuous;
				loadingProgress.Value = percent;
			}
			else
			{
				loadingProgress.Style = ProgressBarStyle.Marquee;
				loadingProgress.MarqueeAnimationSpeed = GetMarqueeAnimationSpeed();
			}
		}

		private void StartWorkflow()
		{
			if (workflowRunning)
			{
				return;
			}
			if (!EnsureNoBlockingToolWindow()) return;
			workflowRunning = true;
			SetStatusKey("Status.Preparing", false);
			startButton.Enabled = false;
			settingsButton.Enabled = false;
			upgradeButton.Enabled = false;
			ShowNoticeKey("Notice.CheckingFiles", false);
			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "서버 실행 준비를 시작합니다…" : "Preparing to start the server…", true, -1);
			Thread worker = new Thread((ThreadStart)delegate
			{
				int result = 1;
				bool canceled = false;
				try
				{
					result = Run();
				}
				catch (OperationCanceledException)
				{
					AppendConsole(Localization.T("Console.SetupCanceled"));
					result = 0;
					canceled = true;
				}
				catch (Exception exception)
				{
					AppendConsole(Localization.F("Console.LauncherError", exception.Message));
					ShowNoticeKey("Notice.StartFailed", true);
				}
				finally
				{
					WorkflowFinished(result, canceled);
				}
			});
			worker.IsBackground = true;
			worker.Name = Localization.T("Thread.Start");
			worker.Start();
		}

		private void WorkflowFinished(int exitCode, bool canceled)
		{
			if (IsDisposed)
			{
				return;
			}
			if (InvokeRequired)
			{
				TryPostToUi(this, (MethodInvoker)delegate { WorkflowFinished(exitCode, canceled); });
				return;
			}
			workflowRunning = false;
			serverRunning = false;
			SetStatusKey(exitCode == 0 ? "Status.Off" : "Status.Error", exitCode != 0);
			startButton.Enabled = true;
			stopButton.Enabled = false;
			settingsButton.Enabled = true;
			upgradeButton.Enabled = true;
			playersButton.Enabled = false;
			commandBox.Enabled = false;
			sendButton.Enabled = false;
			UpdateQuickCommandControls();
			SetLoadingState(string.Empty, false, -1);
			string upnpCleanup = ConsumeUpnpCleanupStatus();
			if (canceled)
			{
				ShowNoticeKey("Notice.StartCanceled", false);
			}
			else if (exitCode == 0)
			{
				if (string.IsNullOrEmpty(upnpCleanup))
				{
					ShowNoticeKey("Notice.ServerStopped", false);
				}
				else
				{
					ShowNotice(Localization.T("Notice.ServerStopped") + " " + upnpCleanup, upnpCleanup.IndexOf("실패", StringComparison.Ordinal) >= 0);
				}
			}
			else
			{
				string analysis = AnalyzeServerFailure(consoleHistory);
				ShowNotice(Localization.T("Notice.ServerError") + " " + analysis + (string.IsNullOrEmpty(upnpCleanup) ? string.Empty : " " + upnpCleanup), true);
			}
			if (closeAfterStop)
			{
				FormClosing -= OnLauncherClosing;
				Close();
			}
		}

		public void ServerStarted(string address)
		{
			if (InvokeRequired)
			{
				TryPostToUi(this, (MethodInvoker)delegate { ServerStarted(address); });
				return;
			}
			serverRunning = true;
			SetStatusKey("Status.Starting", false);
			SetConnectionAddress(address);
			stopButton.Enabled = true;
			playersButton.Enabled = true;
			commandBox.Enabled = true;
			sendButton.Enabled = !string.IsNullOrWhiteSpace(commandBox.Text);
			UpdateQuickCommandControls();
			ShowNoticeKey("Notice.WaitingServer", false);
			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "서버 프로세스가 포트를 열 때까지 기다리고 있습니다…" : "Waiting for the server process to open its port…", true, -1);
		}

		public void ServerReady(string address)
		{
			if (InvokeRequired)
			{
				TryPostToUi(this, (MethodInvoker)delegate { ServerReady(address); });
				return;
			}
			SetStatusKey("Status.Ready", false);
			SetConnectionAddress(address);
			ShowNoticeKey("Notice.LocalReady", false);
			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "외부 접속 경로를 확인하고 있습니다…" : "Checking external connectivity…", true, -1);
		}

		private void StopServer()
		{
			if (SendServerCommand("stop"))
			{
				stopButton.Enabled = false;
				commandBox.Enabled = false;
				sendButton.Enabled = false;
				SetStatusKey("Status.Stopping", false);
				ShowNoticeKey("Notice.Stopping", false);
			}
		}

		private void SendCommandFromBox()
		{
			string command = commandBox.Text.Trim();
			if (string.IsNullOrEmpty(command))
			{
				commandBox.Focus();
				return;
			}
			if (SendServerCommand(command))
			{
				commandBox.Clear();
			}
			else
			{
				ShowNoticeKey("Notice.NoServer", true);
			}
		}

		private void OpenSettings()
		{
			if (!EnsureNoBlockingToolWindow()) return;
			if (workflowRunning || serverRunning)
			{
				ShowNoticeKey("Notice.StopBeforeSettings", true);
				return;
			}
			string serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);
			try
			{
				Directory.CreateDirectory(serversRoot);
				LauncherOptions options = ConfigureServerPropertiesGui(serversRoot, true);
				int port = ReadConfiguredServerPort(Path.Combine(options.ServerDirectory, "server.properties"), 25565);
				SetConnectionAddress(GetLocalConnectionAddress(port));
				ShowNoticeKey("Notice.SettingsSaved", false);
			}
			catch (OperationCanceledException)
			{
				ShowNoticeKey("Notice.SettingsCanceled", false);
			}
			catch (Exception exception)
			{
				ShowNotice(Localization.F("Notice.SettingsFailed", exception.Message), true);
			}
		}

		private LauncherOptions ReadActiveLauncherOptions(out string serversRoot, out string serverDirectory)
		{
			serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);
			string activeProfile = ReadActiveProfileName(serversRoot);
			serverDirectory = GetProfileDirectory(serversRoot, activeProfile);
			Dictionary<string, string> properties = ReadSimpleProperties(Path.Combine(serverDirectory, ".launcher-properties-configured"));
			if (properties.Count == 0)
			{
				throw new InvalidOperationException(Localization.CurrentLanguage == Localization.Korean ? "먼저 서버 설정을 저장해 주세요." : "Save the server settings first.");
			}
			return ReadLauncherOptionsFromProperties(serversRoot, properties, activeProfile, 4, true);
		}

		private void RunUiAction(Action action)
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				string prefix = Localization.CurrentLanguage == Localization.Korean ? "작업을 완료하지 못했습니다: " : "Could not complete the action: ";
				ShowNotice(prefix + exception.Message, true);
			}
		}

		private void ShowModelessToolWindow(string key, Func<Form> factory, bool blocksServerChanges, Action onClosed)
		{
			Form existing;
			if (modelessToolWindows.TryGetValue(key, out existing) && existing != null && !existing.IsDisposed)
			{
				if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
				existing.Show();
				existing.BringToFront();
				existing.Activate();
				return;
			}
			if (blocksServerChanges && modelessToolsBlockingServerChanges.Count > 0)
			{
				ShowNotice(Localization.CurrentLanguage == Localization.Korean ? "다른 서버 관리 창을 먼저 닫아 주세요." : "Close the other server management window first.", true);
				ActivateFirstBlockingToolWindow();
				return;
			}
			Form form = factory();
			if (form == null) throw new InvalidOperationException("기능 창을 만들지 못했습니다.");
			modelessToolWindows[key] = form;
			if (blocksServerChanges) modelessToolsBlockingServerChanges.Add(key);
			form.FormClosed += delegate
			{
				Form tracked;
				if (modelessToolWindows.TryGetValue(key, out tracked) && object.ReferenceEquals(tracked, form)) modelessToolWindows.Remove(key);
				modelessToolsBlockingServerChanges.Remove(key);
				if (!IsDisposed && !Disposing && onClosed != null) RunUiAction(onClosed);
			};
			form.Show(this);
			form.BringToFront();
		}

		private bool EnsureNoBlockingToolWindow()
		{
			if (modelessToolsBlockingServerChanges.Count == 0) return true;
			ShowNotice(Localization.CurrentLanguage == Localization.Korean ? "열려 있는 서버 관리 창을 닫은 뒤 진행해 주세요." : "Close the open server management window before continuing.", true);
			ActivateFirstBlockingToolWindow();
			return false;
		}

		private void ActivateFirstBlockingToolWindow()
		{
			foreach (string key in modelessToolsBlockingServerChanges)
			{
				Form form;
				if (!modelessToolWindows.TryGetValue(key, out form) || form == null || form.IsDisposed) continue;
				if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
				form.BringToFront();
				form.Activate();
				break;
			}
		}

		private bool RequireStoppedServer()
		{
			if (!workflowRunning && !serverRunning)
			{
				return true;
			}
			ShowNoticeKey("Notice.StopBeforeSettings", true);
			return false;
		}

		private void OpenServerManagement()
		{
			string serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);
			Directory.CreateDirectory(serversRoot);
			ShowModelessToolWindow("server-management", delegate { return new MultiServerDashboardForm(serversRoot, workflowRunning || serverRunning); }, true, delegate
			{
				string activeProfile = ReadActiveProfileName(serversRoot);
				string directory = GetProfileDirectory(serversRoot, activeProfile);
				int port = ReadConfiguredServerPort(Path.Combine(directory, "server.properties"), 25565);
				SetConnectionAddress(GetLocalConnectionAddress(port));
			});
		}

		private void OpenBackupManager()
		{
			if (!RequireStoppedServer())
			{
				return;
			}
			string root;
			string directory;
			ReadActiveLauncherOptions(out root, out directory);
			ShowModelessToolWindow("backup", delegate { return new BackupManagerForm(directory); }, true, null);
		}

		private void OpenContentManager()
		{
			if (!RequireStoppedServer())
			{
				return;
			}
			string root;
			string directory;
			LauncherOptions options = ReadActiveLauncherOptions(out root, out directory);
			ShowModelessToolWindow("content", delegate { return new ContentManagerForm(options); }, true, null);
		}

		private void OpenPlayerManager()
		{
			if (!serverRunning)
			{
				ShowNoticeKey("Notice.NoServer", true);
				return;
			}
			ShowModelessToolWindow("players", delegate { return new PlayerManagementForm(SendServerCommand); }, false, null);
		}

		private void OpenNetworkTools()
		{
			string root;
			string directory;
			ReadActiveLauncherOptions(out root, out directory);
			int port = ReadConfiguredServerPort(Path.Combine(directory, "server.properties"), 25565);
			Action recheck = delegate
			{
				if (!serverRunning)
				{
					ShowNoticeKey("Notice.NoServer", true);
					return;
				}
				Thread thread = new Thread((ThreadStart)delegate
				{
					RecheckExternalReachabilityOnly(port);
				});
				thread.IsBackground = true;
				thread.Name = "외부 접속 다시 확인";
				thread.Start();
			};
			ShowModelessToolWindow("network", delegate { return new NetworkToolsForm(directory, port, currentSelectedJavaPath, recheck); }, false, null);
		}

		private void CreateDiagnostics()
		{
			try
			{
				string root;
				string directory;
				LauncherOptions options = ReadActiveLauncherOptions(out root, out directory);
				string path = CreateDiagnosticBundle(directory, options);
				DialogResult result = MessageBox.Show(this, (Localization.CurrentLanguage == Localization.Korean ? "개인정보를 가린 진단 묶음을 만들었습니다.\r\n\r\n" : "Created a redacted diagnostic bundle.\r\n\r\n") + path, Localization.T("Button.Diagnostics"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				ShowNotice(Localization.CurrentLanguage == Localization.Korean ? "진단 묶음을 만들었습니다." : "Diagnostic bundle created.", false);
			}
			catch (Exception exception)
			{
				ShowNotice((Localization.CurrentLanguage == Localization.Korean ? "진단 묶음을 만들지 못했습니다: " : "Could not create the diagnostic bundle: ") + exception.Message, true);
			}
		}

		private void UpgradeServerFiles()
		{
			if (!EnsureNoBlockingToolWindow()) return;
			if (workflowRunning || serverRunning)
			{
				ShowNoticeKey("Notice.StopBeforeUpgrade", true);
				return;
			}
			workflowRunning = true;
			startButton.Enabled = false;
			settingsButton.Enabled = false;
			upgradeButton.Enabled = false;
			SetStatusKey("Status.Preparing", false);
			ShowNoticeKey("Notice.UpgradeStarted", false);
			SetLoadingState(Localization.CurrentLanguage == Localization.Korean ? "서버 업그레이드 정보를 확인하고 있습니다…" : "Checking server upgrade information…", true, -1);
			Thread worker = new Thread((ThreadStart)delegate
			{
				try
				{
					string serversRoot = GetServersRootDirectory(AppDomain.CurrentDomain.BaseDirectory);
					string activeProfile = ReadActiveProfileName(serversRoot);
					string serverDirectory = GetProfileDirectory(serversRoot, activeProfile);
					Dictionary<string, string> properties = ReadSimpleProperties(Path.Combine(serverDirectory, ".launcher-properties-configured"));
					if (properties.Count == 0)
					{
						ConfigureServerPropertiesGui(serversRoot, true);
						activeProfile = ReadActiveProfileName(serversRoot);
						serverDirectory = GetProfileDirectory(serversRoot, activeProfile);
						properties = ReadSimpleProperties(Path.Combine(serverDirectory, ".launcher-properties-configured"));
					}
					LauncherOptions options = ReadLauncherOptionsFromProperties(serversRoot, properties, activeProfile, ChooseMaximumMemoryGb(), true);
					CompatibleJavaRuntime compatibleJava = PrepareCompatibleJavaRuntime(options, serversRoot, options.CustomJavaMajor);
					string javaPath = compatibleJava.JavaPath;
					ServerRuntime runtime = PrepareServerRuntime(options.ServerDirectory, options, javaPath, false);
					UpgradeServerRuntime(options.ServerDirectory, options, javaPath, runtime, true);
					TryPostToUi(this, (MethodInvoker)delegate
					{
						workflowRunning = false;
						startButton.Enabled = true;
						settingsButton.Enabled = true;
						upgradeButton.Enabled = true;
						SetStatusKey("Status.Off", false);
						ShowNoticeKey("Notice.UpgradeDone", false);
						SetLoadingState(string.Empty, false, -1);
					});
				}
				catch (Exception exception)
				{
					TryPostToUi(this, (MethodInvoker)delegate
					{
						workflowRunning = false;
						startButton.Enabled = true;
						settingsButton.Enabled = true;
						upgradeButton.Enabled = true;
						SetStatusKey("Status.Error", true);
						ShowNotice(Localization.F("Notice.UpgradeFailed", exception.Message), true);
						SetLoadingState(string.Empty, false, -1);
					});
				}
			});
			worker.IsBackground = true;
			worker.Name = Localization.T("Button.Upgrade");
			worker.Start();
		}

		private void ToggleConsole()
		{
			consolePanel.Visible = !consolePanel.Visible;
			consoleButton.Text = consolePanel.Visible ? Localization.T("Button.ConsoleClose") : Localization.T("Button.ConsoleOpen");
			int maximumHeight = Math.Max(720, Screen.FromControl(this).WorkingArea.Height - 40);
			Size = new Size(Math.Max(Width, consolePanel.Visible ? 1120 : 940), Math.Min(Math.Max(Height, 720), maximumHeight));
			if (consolePanel.Visible)
			{
				consoleBox.SelectionStart = consoleBox.TextLength;
				consoleBox.ScrollToCaret();
			}
		}

		private void OnLauncherClosing(object sender, FormClosingEventArgs eventArgs)
		{
			if (!workflowRunning && !serverRunning)
			{
				return;
			}
			DialogResult result = MessageBox.Show(Localization.T("Close.Question"), Localization.T("Close.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (result != DialogResult.Yes)
			{
				eventArgs.Cancel = true;
				return;
			}
			eventArgs.Cancel = true;
			closeAfterStop = true;
			if (serverRunning)
			{
				StopServer();
			}
			else
			{
				ShowNoticeKey("Notice.CloseAfterWork", false);
			}
		}

		public void RequestClose()
		{
			if (IsDisposed)
			{
				return;
			}
			if (InvokeRequired)
			{
				TryPostToUi(this, (MethodInvoker)RequestClose);
				return;
			}
			FormClosing -= OnLauncherClosing;
			Close();
		}

		public void AppendConsole(string line)
		{
			if (IsDisposed)
			{
				return;
			}
			consoleQueue.Enqueue(line ?? string.Empty);
			int count = Interlocked.Increment(ref queuedConsoleLines);
			while (count > 10000)
			{
				string ignored;
				if (!consoleQueue.TryDequeue(out ignored))
				{
					break;
				}
				count = Interlocked.Decrement(ref queuedConsoleLines);
			}
		}

		private void FlushConsoleQueue()
		{
			if (IsDisposed || consoleBox == null)
			{
				return;
			}
			string line;
			List<string> appended = new List<string>();
			int count = 0;
			while (count < 500 && consoleQueue.TryDequeue(out line))
			{
				Interlocked.Decrement(ref queuedConsoleLines);
				consoleHistory.Add(line ?? string.Empty);
				appended.Add(line ?? string.Empty);
				count++;
			}
			if (count == 0)
			{
				return;
			}
			bool trimmed = consoleHistory.Count > 10000;
			if (trimmed)
			{
				consoleHistory.RemoveRange(0, consoleHistory.Count - 10000);
			}
			bool unfiltered = consoleSearchBox != null && string.IsNullOrWhiteSpace(consoleSearchBox.Text) && consoleFilterBox != null && consoleFilterBox.SelectedIndex <= 0;
			if (unfiltered && !trimmed)
			{
				RichTextUpdateState state = BeginStableRichTextUpdate(consoleBox);
				try
				{
					for (int i = 0; i < appended.Count; i++)
					{
						AppendStyledConsoleLine(appended[i]);
					}
				}
				finally
				{
					EndStableRichTextUpdate(consoleBox, state);
				}
			}
			else
			{
				RebuildConsoleView();
			}
		}

		private void RebuildConsoleView()
		{
			if (consoleBox == null || consoleBox.IsDisposed)
			{
				return;
			}
			string search = consoleSearchBox == null ? string.Empty : consoleSearchBox.Text.Trim();
			int filterIndex = consoleFilterBox == null ? 0 : consoleFilterBox.SelectedIndex;
			RichTextUpdateState state = BeginStableRichTextUpdate(consoleBox);
			try
			{
				consoleBox.Clear();
				for (int i = 0; i < consoleHistory.Count; i++)
				{
					string line = consoleHistory[i] ?? string.Empty;
					if (!string.IsNullOrEmpty(search) && line.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}
					if (!ConsoleLineMatchesFilter(line, filterIndex))
					{
						continue;
					}
					AppendStyledConsoleLine(line);
				}
			}
			finally
			{
				EndStableRichTextUpdate(consoleBox, state);
			}
		}

		private void AppendStyledConsoleLine(string line)
		{
			Color normal = Color.FromArgb(215, 225, 235);
			Color color = normal;
			ConsoleLineKind kind = ClassifyConsoleLine(line);
			if (kind == ConsoleLineKind.Error)
			{
				color = Color.FromArgb(255, 117, 117);
			}
			else if (kind == ConsoleLineKind.Warning)
			{
				color = Color.FromArgb(255, 190, 92);
			}
			else if (kind == ConsoleLineKind.Compatibility)
			{
				color = Color.FromArgb(112, 184, 255);
			}
			consoleBox.SelectionColor = color;
			consoleBox.AppendText(line + Environment.NewLine);
			consoleBox.SelectionColor = normal;
		}

		public void SetConnectionAddress(string address)
		{
			if (InvokeRequired)
			{
				TryPostToUi(this, (MethodInvoker)delegate { SetConnectionAddress(address); });
				return;
			}
			addressBox.Text = string.IsNullOrWhiteSpace(address) ? Localization.T("Address.Empty") : address;
			copyButton.Enabled = !string.IsNullOrWhiteSpace(address) && address.IndexOf(":", StringComparison.Ordinal) >= 0;
			addressBox.AccessibleDescription = copyButton.Enabled
				? (Localization.CurrentLanguage == Localization.Korean ? "복사할 수 있는 서버 접속 주소입니다." : "Server connection address ready to copy.")
				: (Localization.CurrentLanguage == Localization.Korean ? "서버가 준비되면 접속 주소가 표시됩니다." : "The connection address appears when the server is ready.");
		}

		public void ShowNotice(string message, bool warning)
		{
			if (IsDisposed)
			{
				return;
			}
			if (InvokeRequired)
			{
				TryPostToUi(this, (MethodInvoker)delegate { ShowNotice(message, warning); });
				return;
			}
			noticeTextKey = null;
			SetNoticeText(message, warning);
		}

		private void ShowNoticeKey(string key, bool warning)
		{
			if (IsDisposed)
			{
				return;
			}
			if (InvokeRequired)
			{
				TryPostToUi(this, (MethodInvoker)delegate { ShowNoticeKey(key, warning); });
				return;
			}
			noticeTextKey = key;
			SetNoticeText(Localization.T(key), warning);
		}

		private void SetNoticeText(string text, bool warning)
		{
			noticeWarning = warning;
			noticeLabel.Text = text;
			noticeLabel.AccessibleName = text;
			noticeLabel.ForeColor = warning ? ThemePalette.Create(darkTheme).Warning : ThemePalette.Create(darkTheme).Muted;
		}

		private void SetStatus(string text, bool warning)
		{
			statusTextKey = null;
			SetStatusText(text, warning);
		}

		private void SetStatusKey(string key, bool warning)
		{
			statusTextKey = key;
			SetStatusText(Localization.T(key), warning);
		}

		private void SetStatusText(string text, bool warning)
		{
			ThemePalette palette = ThemePalette.Create(darkTheme);
			statusWarning = warning;
			statusLabel.Text = text;
			statusLabel.AccessibleName = text;
			statusPill.Width = Math.Min(430, Math.Max(150, statusLabel.PreferredWidth + 58));
			statusDot.ForeColor = warning ? palette.Warning : (serverRunning ? palette.Success : palette.Muted);
		}

		private bool LoadTheme()
		{
			try
			{
				return File.Exists(themePath) && File.ReadAllText(themePath, Encoding.UTF8).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		private void SaveTheme()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(themePath));
				File.WriteAllText(themePath, darkTheme ? "dark" : "light", new UTF8Encoding(false));
			}
			catch
			{
			}
		}

		private void ApplyTheme()
		{
			ThemePalette palette = ThemePalette.Create(darkTheme);
			BackColor = palette.Window;
			ForeColor = palette.Text;
			ApplyThemeRecursive(this, palette);
			consoleBox.BackColor = palette.Console;
			consoleBox.ForeColor = Color.FromArgb(215, 225, 235);
			addressBox.BackColor = palette.CardSecondary;
			addressBox.ForeColor = palette.Text;
			commandBox.BackColor = palette.CardSecondary;
			commandBox.ForeColor = palette.Text;
			consoleSearchBox.BackColor = palette.CardSecondary;
			consoleSearchBox.ForeColor = palette.Text;
			consoleFilterBox.BackColor = palette.CardSecondary;
			consoleFilterBox.ForeColor = palette.Text;
			ApplyQuickCommandTheme();
			ModernComboBox consoleFilter = consoleFilterBox as ModernComboBox;
			if (consoleFilter != null)
			{
				consoleFilter.SelectionBackColor = palette.AccentSoft;
				consoleFilter.SelectionForeColor = palette.Text;
				consoleFilter.Invalidate();
			}
			themeButton.Text = darkTheme ? Localization.T("Theme.Light") : Localization.T("Theme.Dark");
			SetStatusText(statusLabel.Text, statusWarning);
			Invalidate(true);
		}

		private static void ApplyThemeRecursive(Control parent, ThemePalette palette)
		{
			foreach (Control control in parent.Controls)
			{
				RoundedPanel roundedPanel = control as RoundedPanel;
				if (roundedPanel != null)
				{
					bool surface = string.Equals(Convert.ToString(roundedPanel.Tag), "surface", StringComparison.Ordinal);
					roundedPanel.BackColor = surface ? palette.CardSecondary : palette.Card;
					roundedPanel.BorderColor = surface ? roundedPanel.BackColor : palette.Border;
				}
				else if (control is Button)
				{
					Button button = (Button)control;
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
				else if (control is Label && string.Equals(Convert.ToString(control.Tag), "muted", StringComparison.Ordinal))
				{
					control.BackColor = control.Parent == null ? palette.Window : control.Parent.BackColor;
					control.ForeColor = palette.Muted;
				}
				else if (!(control is TextBox) && !(control is RichTextBox))
				{
					control.BackColor = parent is LauncherForm ? palette.Window : control.Parent.BackColor;
					control.ForeColor = palette.Text;
				}
				ApplyThemeRecursive(control, palette);
			}
		}
	}

	private sealed class RoundedPanel : Panel
	{
		public Color BorderColor { get; set; }
		public int CornerRadius { get; set; }

		public RoundedPanel()
		{
			DoubleBuffered = true;
			BorderColor = Color.Gray;
			CornerRadius = 18;
			ResizeRedraw = true;
		}

		protected override void OnResize(EventArgs eventArgs)
		{
			base.OnResize(eventArgs);
			if (Width > 0 && Height > 0)
			{
				using (GraphicsPath path = CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), CornerRadius))
				{
					Region previousRegion = Region;
					Region = new Region(path);
					if (previousRegion != null)
					{
						previousRegion.Dispose();
					}
				}
			}
		}

		protected override void OnPaint(PaintEventArgs eventArgs)
		{
			eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle rectangle = new Rectangle(0, 0, Width - 1, Height - 1);
			using (GraphicsPath path = CreateRoundedRectangle(rectangle, CornerRadius))
			using (SolidBrush brush = new SolidBrush(BackColor))
			using (Pen pen = new Pen(BorderColor))
			{
				eventArgs.Graphics.FillPath(brush, path);
				eventArgs.Graphics.DrawPath(pen, path);
			}
		}

		internal static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
		{
			GraphicsPath path = new GraphicsPath();
			int diameter = radius * 2;
			path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
			path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
			path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
			path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
			path.CloseFigure();
			return path;
		}
	}

	private sealed class BufferedListView : ListView
	{
		public BufferedListView()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
		}
	}

	private sealed class ModernComboBox : ComboBox
	{
		public Color SelectionBackColor { get; set; }
		public Color SelectionForeColor { get; set; }

		public ModernComboBox()
		{
			DrawMode = DrawMode.OwnerDrawFixed;
			DropDownStyle = ComboBoxStyle.DropDownList;
			FlatStyle = FlatStyle.Flat;
			ItemHeight = 24;
			SelectionBackColor = Color.FromArgb(232, 240, 254);
			SelectionForeColor = Color.FromArgb(25, 31, 40);
		}

		protected override void OnDrawItem(DrawItemEventArgs eventArgs)
		{
			if (eventArgs.Index < 0)
			{
				return;
			}
			bool selected = (eventArgs.State & DrawItemState.Selected) == DrawItemState.Selected;
			Color back = selected ? SelectionBackColor : BackColor;
			Color fore = selected ? SelectionForeColor : ForeColor;
			using (SolidBrush brush = new SolidBrush(back))
			{
				eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
			}
			Rectangle textBounds = new Rectangle(eventArgs.Bounds.X + 8, eventArgs.Bounds.Y, Math.Max(1, eventArgs.Bounds.Width - 12), eventArgs.Bounds.Height);
			TextRenderer.DrawText(eventArgs.Graphics, GetItemText(Items[eventArgs.Index]), Font, textBounds, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
			if ((eventArgs.State & DrawItemState.Focus) == DrawItemState.Focus)
			{
				eventArgs.DrawFocusRectangle();
			}
			base.OnDrawItem(eventArgs);
		}
	}

	private enum ButtonIcon
	{
		None,
		Play,
		Stop,
		Settings,
		Upgrade,
		Console,
		Server,
		Backup,
		Content,
		Players,
		Network,
		Diagnostics,
		Copy,
		Send,
		Search,
		Folder,
		Download,
		Add,
		Edit,
		Archive,
		Trash,
		Check,
		Refresh
	}

	private sealed class RoundedButton : Button
	{
		private bool mouseOver;
		private bool mouseDown;
		public ButtonIcon IconKind { get; set; }

		public RoundedButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			FlatStyle = FlatStyle.Flat;
			FlatAppearance.BorderSize = 0;
			Cursor = Cursors.Hand;
			ResizeRedraw = true;
			IconKind = ButtonIcon.None;
		}

		protected override void OnPaintBackground(PaintEventArgs eventArgs)
		{
			eventArgs.Graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
		}

		protected override void OnMouseEnter(EventArgs eventArgs)
		{
			mouseOver = true;
			Invalidate();
			base.OnMouseEnter(eventArgs);
		}

		protected override void OnMouseLeave(EventArgs eventArgs)
		{
			mouseOver = false;
			mouseDown = false;
			Invalidate();
			base.OnMouseLeave(eventArgs);
		}

		protected override void OnMouseDown(MouseEventArgs eventArgs)
		{
			mouseDown = true;
			Invalidate();
			base.OnMouseDown(eventArgs);
		}

		protected override void OnMouseUp(MouseEventArgs eventArgs)
		{
			mouseDown = false;
			Invalidate();
			base.OnMouseUp(eventArgs);
		}

		protected override void OnEnabledChanged(EventArgs eventArgs)
		{
			Cursor = Enabled ? Cursors.Hand : Cursors.Default;
			Invalidate();
			base.OnEnabledChanged(eventArgs);
		}

		protected override void OnGotFocus(EventArgs eventArgs)
		{
			base.OnGotFocus(eventArgs);
			Invalidate();
		}

		protected override void OnLostFocus(EventArgs eventArgs)
		{
			base.OnLostFocus(eventArgs);
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs eventArgs)
		{
			eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			eventArgs.Graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
			Color fill = BackColor;
			if (!Enabled)
			{
				fill = ControlPaint.Light(BackColor, 0.12F);
			}
			else if (mouseDown)
			{
				fill = ControlPaint.Dark(BackColor, 0.08F);
			}
			else if (mouseOver)
			{
				fill = FlatAppearance.MouseOverBackColor.IsEmpty ? ControlPaint.Light(BackColor, 0.08F) : FlatAppearance.MouseOverBackColor;
			}
			Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
			using (GraphicsPath path = RoundedPanel.CreateRoundedRectangle(bounds, Math.Min(14, Height / 2)))
			using (SolidBrush brush = new SolidBrush(fill))
			{
				eventArgs.Graphics.FillPath(brush, path);
				string role = Convert.ToString(Tag);
				if (string.Equals(role, "secondary", StringComparison.Ordinal) || string.Equals(role, "ghost", StringComparison.Ordinal))
				{
					Color border = FlatAppearance.BorderColor.IsEmpty ? Color.FromArgb(45, ForeColor) : FlatAppearance.BorderColor;
					using (Pen borderPen = new Pen(border, 1F)) eventArgs.Graphics.DrawPath(borderPen, path);
				}
			}
			Color contentColor = Enabled ? ForeColor : Color.FromArgb(135, ForeColor);
			DrawButtonContent(eventArgs.Graphics, bounds, contentColor);
			if (Focused && ShowFocusCues)
			{
				Rectangle focusBounds = new Rectangle(3, 3, Math.Max(1, Width - 7), Math.Max(1, Height - 7));
				using (GraphicsPath focusPath = RoundedPanel.CreateRoundedRectangle(focusBounds, Math.Min(11, Height / 2)))
				using (Pen focusPen = new Pen(SystemColors.Highlight, 2F))
				{
					focusPen.DashStyle = DashStyle.Dot;
					eventArgs.Graphics.DrawPath(focusPen, focusPath);
				}
			}
		}

		private void DrawButtonContent(Graphics graphics, Rectangle bounds, Color color)
		{
			if (IconKind == ButtonIcon.None)
			{
				TextRenderer.DrawText(graphics, Text, Font, bounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
				return;
			}
			Size textSize = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, bounds.Width - 34), bounds.Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
			int iconSize = 16;
			int gap = 7;
			int groupWidth = Math.Min(bounds.Width - 16, iconSize + gap + textSize.Width);
			int start = bounds.Left + Math.Max(8, (bounds.Width - groupWidth) / 2);
			Rectangle iconBounds = new Rectangle(start, bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize);
			Rectangle textBounds = new Rectangle(iconBounds.Right + gap, bounds.Top, Math.Max(1, bounds.Right - iconBounds.Right - gap - 6), bounds.Height);
			DrawVectorIcon(graphics, IconKind, iconBounds, color);
			TextRenderer.DrawText(graphics, Text, Font, textBounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
		}

		private static void DrawVectorIcon(Graphics graphics, ButtonIcon icon, Rectangle bounds, Color color)
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			float left = bounds.Left + 1.5F;
			float top = bounds.Top + 1.5F;
			float right = bounds.Right - 1.5F;
			float bottom = bounds.Bottom - 1.5F;
			float centerX = (left + right) / 2F;
			float centerY = (top + bottom) / 2F;
			using (Pen pen = new Pen(color, 1.7F))
			using (SolidBrush brush = new SolidBrush(color))
			{
				pen.StartCap = LineCap.Round;
				pen.EndCap = LineCap.Round;
				pen.LineJoin = LineJoin.Round;
				switch (icon)
				{
					case ButtonIcon.Play:
						graphics.FillPolygon(brush, new PointF[] { new PointF(left + 3, top + 1), new PointF(right - 1, centerY), new PointF(left + 3, bottom - 1) });
						break;
					case ButtonIcon.Stop:
						graphics.DrawRectangle(pen, left + 2, top + 2, right - left - 4, bottom - top - 4);
						break;
					case ButtonIcon.Settings:
						for (int row = 0; row < 3; row++)
						{
							float y = top + 3 + row * 4.5F;
							float knob = row == 1 ? centerX + 2 : centerX - 2;
							graphics.DrawLine(pen, left, y, right, y);
							graphics.FillEllipse(brush, knob - 1.8F, y - 1.8F, 3.6F, 3.6F);
						}
						break;
					case ButtonIcon.Upgrade:
						graphics.DrawLine(pen, centerX, top + 1, centerX, bottom - 3);
						graphics.DrawLines(pen, new PointF[] { new PointF(left + 3, top + 5), new PointF(centerX, top + 1), new PointF(right - 3, top + 5) });
						graphics.DrawLine(pen, left + 1, bottom - 1, right - 1, bottom - 1);
						break;
					case ButtonIcon.Console:
						graphics.DrawRectangle(pen, left, top + 1, right - left, bottom - top - 2);
						graphics.DrawLines(pen, new PointF[] { new PointF(left + 3, top + 5), new PointF(left + 6, centerY), new PointF(left + 3, bottom - 5) });
						graphics.DrawLine(pen, left + 8, bottom - 4, right - 3, bottom - 4);
						break;
					case ButtonIcon.Server:
						for (int row = 0; row < 2; row++)
						{
							float y = top + row * 7;
							graphics.DrawRectangle(pen, left, y, right - left, 5.5F);
							graphics.FillEllipse(brush, left + 2, y + 1.7F, 2.2F, 2.2F);
						}
						break;
					case ButtonIcon.Backup:
						graphics.DrawArc(pen, left + 1, top + 1, right - left - 2, bottom - top - 2, 35, 285);
						graphics.DrawLines(pen, new PointF[] { new PointF(left, top + 3), new PointF(left + 1, top + 8), new PointF(left + 5, top + 5) });
						break;
					case ButtonIcon.Content:
						graphics.DrawPolygon(pen, new PointF[] { new PointF(centerX, top), new PointF(right, top + 4), new PointF(right, bottom - 3), new PointF(centerX, bottom), new PointF(left, bottom - 3), new PointF(left, top + 4) });
						graphics.DrawLine(pen, left, top + 4, centerX, top + 8);
						graphics.DrawLine(pen, right, top + 4, centerX, top + 8);
						graphics.DrawLine(pen, centerX, top + 8, centerX, bottom);
						break;
					case ButtonIcon.Players:
						graphics.DrawEllipse(pen, left + 2, top, 5.5F, 5.5F);
						graphics.DrawEllipse(pen, right - 7, top + 2, 5, 5);
						graphics.DrawArc(pen, left, top + 6, 10, 8, 190, 160);
						graphics.DrawArc(pen, right - 9, top + 7, 8, 7, 195, 150);
						break;
					case ButtonIcon.Network:
						graphics.DrawEllipse(pen, left, top, right - left, bottom - top);
						graphics.DrawEllipse(pen, centerX - 3.5F, top, 7, bottom - top);
						graphics.DrawLine(pen, left + 1, centerY, right - 1, centerY);
						break;
					case ButtonIcon.Diagnostics:
						graphics.DrawLines(pen, new PointF[] { new PointF(left, centerY), new PointF(left + 3, centerY), new PointF(left + 5, top + 3), new PointF(left + 8, bottom - 3), new PointF(left + 10, centerY), new PointF(right, centerY) });
						break;
					case ButtonIcon.Copy:
						graphics.DrawRectangle(pen, left + 4, top, right - left - 4, bottom - top - 4);
						graphics.DrawRectangle(pen, left, top + 4, right - left - 4, bottom - top - 4);
						break;
					case ButtonIcon.Send:
						graphics.DrawPolygon(pen, new PointF[] { new PointF(left, top + 2), new PointF(right, centerY), new PointF(left, bottom - 2), new PointF(left + 3, centerY), new PointF(left, top + 2) });
						graphics.DrawLine(pen, left + 3, centerY, right - 2, centerY);
						break;
					case ButtonIcon.Search:
						graphics.DrawEllipse(pen, left, top, 9.5F, 9.5F);
						graphics.DrawLine(pen, left + 8, top + 8, right, bottom);
						break;
					case ButtonIcon.Folder:
						graphics.DrawPolygon(pen, new PointF[] { new PointF(left, top + 4), new PointF(left + 5, top + 4), new PointF(left + 7, top + 1), new PointF(right, top + 1), new PointF(right, bottom - 1), new PointF(left, bottom - 1) });
						break;
					case ButtonIcon.Download:
						graphics.DrawLine(pen, centerX, top, centerX, bottom - 4);
						graphics.DrawLines(pen, new PointF[] { new PointF(left + 3, centerY), new PointF(centerX, bottom - 4), new PointF(right - 3, centerY) });
						graphics.DrawLine(pen, left + 1, bottom, right - 1, bottom);
						break;
					case ButtonIcon.Add:
						graphics.DrawEllipse(pen, left, top, right - left, bottom - top);
						graphics.DrawLine(pen, centerX, top + 3, centerX, bottom - 3);
						graphics.DrawLine(pen, left + 3, centerY, right - 3, centerY);
						break;
					case ButtonIcon.Edit:
						graphics.DrawLine(pen, left + 2, bottom - 2, right - 2, top + 2);
						graphics.DrawLines(pen, new PointF[] { new PointF(left + 1, bottom), new PointF(left + 5, bottom - 1), new PointF(left + 2, bottom - 4), new PointF(left + 1, bottom) });
						break;
					case ButtonIcon.Archive:
						graphics.DrawRectangle(pen, left, top + 3, right - left, bottom - top - 3);
						graphics.DrawLine(pen, left, top, right, top);
						graphics.DrawLine(pen, centerX - 2, top + 7, centerX + 2, top + 7);
						break;
					case ButtonIcon.Trash:
						graphics.DrawRectangle(pen, left + 3, top + 4, right - left - 6, bottom - top - 4);
						graphics.DrawLine(pen, left + 1, top + 3, right - 1, top + 3);
						graphics.DrawLine(pen, centerX - 3, top, centerX + 3, top);
						graphics.DrawLine(pen, centerX - 2, top + 7, centerX - 2, bottom - 3);
						graphics.DrawLine(pen, centerX + 2, top + 7, centerX + 2, bottom - 3);
						break;
					case ButtonIcon.Check:
						graphics.DrawLines(pen, new PointF[] { new PointF(left + 1, centerY), new PointF(centerX - 1, bottom - 2), new PointF(right, top + 2) });
						break;
					case ButtonIcon.Refresh:
						graphics.DrawArc(pen, left + 1, top + 1, right - left - 2, bottom - top - 2, 35, 285);
						graphics.DrawLines(pen, new PointF[] { new PointF(right - 1, top + 1), new PointF(right - 1, top + 6), new PointF(right - 6, top + 3) });
						break;
				}
			}
		}
	}

	private sealed class RoundedPresetButton : RadioButton
	{
		private bool mouseOver;
		public Color CheckedBackColor { get; set; }
		public Color SelectedBorderColor { get; set; }

		public RoundedPresetButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			Appearance = Appearance.Button;
			FlatStyle = FlatStyle.Flat;
			FlatAppearance.BorderSize = 0;
			TextAlign = ContentAlignment.MiddleCenter;
			Cursor = Cursors.Hand;
			CheckedBackColor = Color.FromArgb(232, 240, 254);
			SelectedBorderColor = Color.FromArgb(49, 130, 246);
		}

		protected override void OnPaintBackground(PaintEventArgs eventArgs)
		{
			eventArgs.Graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
		}

		protected override void OnMouseEnter(EventArgs eventArgs)
		{
			mouseOver = true;
			Invalidate();
			base.OnMouseEnter(eventArgs);
		}

		protected override void OnMouseLeave(EventArgs eventArgs)
		{
			mouseOver = false;
			Invalidate();
			base.OnMouseLeave(eventArgs);
		}

		protected override void OnCheckedChanged(EventArgs eventArgs)
		{
			Invalidate();
			base.OnCheckedChanged(eventArgs);
		}

		protected override void OnGotFocus(EventArgs eventArgs)
		{
			base.OnGotFocus(eventArgs);
			Invalidate();
		}

		protected override void OnLostFocus(EventArgs eventArgs)
		{
			base.OnLostFocus(eventArgs);
			Invalidate();
		}

		protected override void OnEnabledChanged(EventArgs eventArgs)
		{
			Cursor = Enabled ? Cursors.Hand : Cursors.Default;
			Invalidate();
			base.OnEnabledChanged(eventArgs);
		}

		protected override void OnPaint(PaintEventArgs eventArgs)
		{
			eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			eventArgs.Graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
			Color fill = Checked ? CheckedBackColor : BackColor;
			if (mouseOver && !Checked)
			{
				fill = ControlPaint.Light(BackColor, 0.06F);
			}
			Color border = Checked ? SelectedBorderColor : FlatAppearance.BorderColor;
			Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
			using (GraphicsPath path = RoundedPanel.CreateRoundedRectangle(bounds, 14))
			using (SolidBrush brush = new SolidBrush(fill))
			using (Pen pen = new Pen(border, Checked ? 2F : 1F))
			using (SolidBrush textBrush = new SolidBrush(ForeColor))
			{
				eventArgs.Graphics.FillPath(brush, path);
				eventArgs.Graphics.DrawPath(pen, path);
				StringFormat format = new StringFormat();
				format.Alignment = StringAlignment.Center;
				format.LineAlignment = StringAlignment.Center;
				eventArgs.Graphics.DrawString(Text, Font, textBrush, bounds, format);
				format.Dispose();
			}
			if (Checked)
			{
				Rectangle indicator = new Rectangle(Math.Max(4, Width - 24), 7, 16, 16);
				using (SolidBrush indicatorBrush = new SolidBrush(SelectedBorderColor))
				using (Pen checkPen = new Pen(Color.White, 1.8F))
				{
					checkPen.StartCap = LineCap.Round;
					checkPen.EndCap = LineCap.Round;
					eventArgs.Graphics.FillEllipse(indicatorBrush, indicator);
					eventArgs.Graphics.DrawLines(checkPen, new PointF[]
					{
						new PointF(indicator.Left + 4, indicator.Top + 8),
						new PointF(indicator.Left + 7, indicator.Top + 11),
						new PointF(indicator.Left + 12, indicator.Top + 5)
					});
				}
			}
			if (Focused && ShowFocusCues)
			{
				Rectangle focusBounds = new Rectangle(4, 4, Math.Max(1, Width - 9), Math.Max(1, Height - 9));
				using (GraphicsPath focusPath = RoundedPanel.CreateRoundedRectangle(focusBounds, 11))
				using (Pen focusPen = new Pen(SystemColors.Highlight, 2F))
				{
					focusPen.DashStyle = DashStyle.Dot;
					eventArgs.Graphics.DrawPath(focusPen, focusPath);
				}
			}
		}
	}

	private sealed class ServerSetupForm : Form
	{
		private readonly ServerSettings source;
		private readonly int maximumMemory;
		private readonly Dictionary<string, RadioButton> presetButtons = new Dictionary<string, RadioButton>();
		private readonly TextBox profileBox;
		private readonly ComboBox serverTypeBox;
		private readonly ComboBox versionBox;
		private readonly Label versionStatusLabel;
		private readonly CheckBox includeSnapshotsBox;
		private readonly CheckBox manualJarBox;
		private readonly TextBox manualJarPathBox;
		private readonly ComboBox customJavaBox;
		private readonly TextBox motdBox;
		private readonly NumericUpDown playersBox;
		private readonly NumericUpDown portBox;
		private readonly NumericUpDown memoryBox;
		private readonly ComboBox gameModeBox;
		private readonly ComboBox difficultyBox;
		private readonly CheckBox hardcoreBox;
		private readonly CheckBox pvpBox;
		private readonly CheckBox whitelistBox;
		private readonly NumericUpDown viewBox;
		private readonly NumericUpDown simulationBox;
		private readonly CheckBox commandBlockBox;
		private readonly CheckBox onlineModeBox;
		private readonly CheckBox autoUpdateBox;
		private readonly TextBox ownerBox;
		private readonly Panel advancedPanel;
		private readonly Panel customPanel;
		private readonly Button advancedButton;
		private readonly ErrorProvider validationErrors;
		private readonly ToolTip setupToolTip;
		private readonly Panel body;
		private readonly Label validationLabel;
		private readonly ThemePalette setupPalette;
		private readonly Label rulesLabel;
		private readonly bool worldExists;
		private string selectedPreset;
		private int versionLoadRequest;
		private static readonly object VersionCacheLock = new object();
		private static readonly Dictionary<string, CachedVersionChoices> VersionCache = new Dictionary<string, CachedVersionChoices>(StringComparer.OrdinalIgnoreCase);

		private sealed class CachedVersionChoices
		{
			public string[] Values;
			public DateTime LoadedUtc;
		}

		public ServerSettings SelectedSettings { get; private set; }

		public ServerSetupForm(ServerSettings current, int recommendedMemory, int safeMaximumMemory, bool editing, bool existingWorld)
		{
			ApplyLauncherWindowIcon(this);
			source = current;
			maximumMemory = safeMaximumMemory;
			worldExists = existingWorld;
			Text = Localization.T(editing ? "Setup.Title.Edit" : "Setup.Title.New");
			Font = new Font("Segoe UI Variable Text", 9.5F);
			AutoScaleMode = AutoScaleMode.Dpi;
			StartPosition = FormStartPosition.CenterParent;
			Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
			int setupWidth = Math.Min(790, Math.Max(520, workingArea.Width - 30));
			int setupHeight = Math.Min(860, Math.Max(600, workingArea.Height - 30));
			Size = new Size(setupWidth, setupHeight);
			MinimumSize = new Size(Math.Min(790, setupWidth), Math.Min(600, setupHeight));
			AutoScroll = false;
			validationErrors = new ErrorProvider();
			validationErrors.ContainerControl = this;
			validationErrors.BlinkStyle = ErrorBlinkStyle.NeverBlink;
			setupToolTip = new ToolTip();
			setupToolTip.AutoPopDelay = 8000;
			bool dark = launcherForm != null && launcherForm.UsesDarkTheme;
			setupPalette = ThemePalette.Create(dark);
			BackColor = setupPalette.Window;
			ForeColor = setupPalette.Text;

			body = new Panel();
			body.Location = new Point(26, 20);
			body.Size = new Size(Math.Max(320, ClientSize.Width - 52), Math.Max(400, ClientSize.Height - 92));
			body.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			body.AutoScroll = true;
			body.AutoScrollMinSize = new Size(0, 704);
			Controls.Add(body);

			Label title = NewLabel(Localization.T(editing ? "Setup.Heading.Edit" : "Setup.Heading.New"), 21F, true);
			title.Location = new Point(0, 0);
			body.Controls.Add(title);
			Label description = NewLabel(Localization.T("Setup.Description"), 9.5F, false);
			description.ForeColor = setupPalette.Muted;
			description.Location = new Point(2, 38);
			body.Controls.Add(description);

			Label runtimeLabel = NewLabel(Localization.T("Setup.Runtime"), 12F, true);
			runtimeLabel.Location = new Point(0, 72);
			body.Controls.Add(runtimeLabel);
			Label profileLabel = NewLabel(Localization.T("Setup.ProfileName"), 9F, true);
			profileLabel.Location = new Point(0, 106);
			body.Controls.Add(profileLabel);
			profileBox = NewTextBox();
			profileBox.Location = new Point(0, 128);
			profileBox.Size = new Size(180, 28);
			profileBox.MaxLength = 48;
			profileBox.Text = current.ProfileName;
			profileBox.TabIndex = 0;
			body.Controls.Add(profileBox);
			Label serverTypeLabel = NewLabel(Localization.T("Setup.ServerType"), 9F, true);
			serverTypeLabel.Location = new Point(200, 106);
			body.Controls.Add(serverTypeLabel);
			serverTypeBox = NewCombo(GetServerTypeLabels());
			serverTypeBox.Location = new Point(200, 128);
			serverTypeBox.Width = 170;
			serverTypeBox.TabIndex = 1;
			body.Controls.Add(serverTypeBox);
			Label versionLabel = NewLabel(Localization.T("Setup.MinecraftVersion"), 9F, true);
			versionLabel.Location = new Point(390, 106);
			body.Controls.Add(versionLabel);
			versionBox = NewCombo(new string[] { string.IsNullOrWhiteSpace(current.MinecraftVersion) ? "26.2" : current.MinecraftVersion });
			versionBox.Location = new Point(390, 128);
			versionBox.Width = 150;
			versionBox.TabIndex = 2;
			body.Controls.Add(versionBox);
			includeSnapshotsBox = NewCheckBox(Localization.T("Setup.IncludeSnapshots"), current.IncludeSnapshots);
			includeSnapshotsBox.AutoSize = false;
			includeSnapshotsBox.Size = new Size(148, 38);
			includeSnapshotsBox.Location = new Point(552, 120);
			includeSnapshotsBox.TabIndex = 3;
			body.Controls.Add(includeSnapshotsBox);
			manualJarBox = NewCheckBox(Localization.T("Setup.UseManualJar"), current.UseManualJar);
			manualJarBox.Location = new Point(0, 178);
			manualJarBox.TabIndex = 4;
			body.Controls.Add(manualJarBox);
			manualJarPathBox = NewTextBox();
			manualJarPathBox.Location = new Point(180, 176);
			manualJarPathBox.Size = new Size(250, 28);
			manualJarPathBox.Text = current.ManualJarPath;
			manualJarPathBox.TabIndex = 5;
			body.Controls.Add(manualJarPathBox);
			Button browseJar = NewFlatButton(Localization.T("Setup.BrowseJar"), 92);
			ApplyButtonIcon(browseJar, ButtonIcon.Folder);
			browseJar.Tag = "secondary";
			browseJar.Location = new Point(442, 170);
			browseJar.TabIndex = 6;
			browseJar.Click += delegate
			{
				using (OpenFileDialog dialog = new OpenFileDialog())
				{
					dialog.Filter = "JAR files (*.jar)|*.jar|All files (*.*)|*.*";
					dialog.Title = Localization.T("Setup.UseManualJar");
					if (dialog.ShowDialog(this) == DialogResult.OK)
					{
						manualJarPathBox.Text = dialog.FileName;
						manualJarBox.Checked = true;
					}
				}
			};
			body.Controls.Add(browseJar);
			Label customJavaLabel = NewLabel(Localization.T("Setup.JavaVersion"), 8.5F, true);
			customJavaLabel.Location = new Point(555, 158);
			body.Controls.Add(customJavaLabel);
			customJavaBox = NewCombo(new string[] { "Java 8", "Java 11", "Java 16", "Java 17", "Java 21", "Java 25" });
			customJavaBox.Width = 110;
			customJavaBox.Location = new Point(555, 180);
			customJavaBox.TabIndex = 7;
			int[] customJavaValues = new int[] { 8, 11, 16, 17, 21, 25 };
			customJavaBox.SelectedIndex = 5;
			for (int customJavaIndex = 0; customJavaIndex < customJavaValues.Length; customJavaIndex++)
			{
				if (customJavaValues[customJavaIndex] == current.CustomJavaMajor)
				{
					customJavaBox.SelectedIndex = customJavaIndex;
					break;
				}
			}
			body.Controls.Add(customJavaBox);
			versionStatusLabel = NewLabel(string.Empty, 8F, false);
			versionStatusLabel.Location = new Point(390, 158);
			versionStatusLabel.MaximumSize = new Size(150, 36);
			versionStatusLabel.ForeColor = setupPalette.Muted;
			body.Controls.Add(versionStatusLabel);

			Label quickSetup = NewLabel(Localization.T("Setup.Quick"), 12F, true);
			quickSetup.Location = new Point(0, 214);
			body.Controls.Add(quickSetup);

			TableLayoutPanel presetGrid = new TableLayoutPanel();
			presetGrid.Location = new Point(0, 238);
			presetGrid.Size = new Size(700, 136);
			presetGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			presetGrid.ColumnCount = 4;
			presetGrid.RowCount = 2;
			for (int i = 0; i < 4; i++) presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
			presetGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
			presetGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
			body.Controls.Add(presetGrid);
			AddPreset(presetGrid, "survival-peaceful", Localization.T("Preset.Peaceful"), 0);
			AddPreset(presetGrid, "survival-easy", Localization.T("Preset.Easy"), 1);
			AddPreset(presetGrid, "survival-normal", Localization.T("Preset.Normal"), 2);
			AddPreset(presetGrid, "survival-hard", Localization.T("Preset.Hard"), 3);
			AddPreset(presetGrid, "survival-hardcore", Localization.T("Preset.Hardcore"), 4);
			AddPreset(presetGrid, "creative-normal", Localization.T("Preset.CreativeNormal"), 5);
			AddPreset(presetGrid, "creative-flat", Localization.T("Preset.CreativeFlat"), 6);
			AddPreset(presetGrid, "custom", Localization.T("Preset.Custom"), 7);

			Label basics = NewLabel(Localization.T("Setup.Basic"), 12F, true);
			basics.Location = new Point(0, 386);
			body.Controls.Add(basics);
			Label motdLabel = NewLabel(Localization.T("Setup.ServerName"), 9F, true);
			motdLabel.Location = new Point(0, 416);
			body.Controls.Add(motdLabel);
			motdBox = NewTextBox();
			motdBox.Location = new Point(0, 438);
			motdBox.Size = new Size(340, 28);
			motdBox.MaxLength = 200;
			motdBox.Text = current.Motd;
			motdBox.TabIndex = 16;
			body.Controls.Add(motdBox);

			Label playersLabel = NewLabel(Localization.T("Setup.MaxPlayers"), 9F, true);
			playersLabel.Location = new Point(360, 416);
			body.Controls.Add(playersLabel);
			playersBox = NewNumber(1, 1000, current.MaxPlayers);
			playersBox.Location = new Point(360, 438);
			playersBox.TabIndex = 17;
			body.Controls.Add(playersBox);
			Label portLabel = NewLabel(Localization.T("Setup.Port"), 9F, true);
			portLabel.Location = new Point(490, 416);
			body.Controls.Add(portLabel);
			portBox = NewNumber(1, 65535, current.ServerPort);
			portBox.Location = new Point(490, 438);
			portBox.TabIndex = 18;
			body.Controls.Add(portBox);
			Label memoryLabel = NewLabel(Localization.T("Setup.Memory"), 9F, true);
			memoryLabel.Location = new Point(0, 478);
			body.Controls.Add(memoryLabel);
			memoryBox = NewNumber(2, safeMaximumMemory, Math.Min(Math.Max(current.MemoryGb, 2), safeMaximumMemory));
			memoryBox.Location = new Point(0, 500);
			memoryBox.TabIndex = 19;
			body.Controls.Add(memoryBox);
			Label memoryHint = NewLabel(Localization.F("Setup.MemoryHint", recommendedMemory, GetTotalPhysicalMemoryGb()), 9F, false);
			memoryHint.ForeColor = setupPalette.Muted;
			memoryHint.Location = new Point(112, 504);
			body.Controls.Add(memoryHint);

			customPanel = new Panel();
			customPanel.Location = new Point(0, 538);
			customPanel.Size = new Size(700, 54);
			customPanel.Visible = false;
			body.Controls.Add(customPanel);
			Label gameModeLabel = NewLabel(Localization.T("Setup.GameMode"), 9F, true);
			gameModeLabel.Location = new Point(0, 0);
			customPanel.Controls.Add(gameModeLabel);
			gameModeBox = NewCombo(new string[] { Localization.T("GameMode.Survival"), Localization.T("GameMode.Creative"), Localization.T("GameMode.Adventure"), Localization.T("GameMode.Spectator") });
			gameModeBox.Location = new Point(0, 22);
			gameModeBox.TabIndex = 20;
			customPanel.Controls.Add(gameModeBox);
			Label difficultyLabel = NewLabel(Localization.T("Setup.Difficulty"), 9F, true);
			difficultyLabel.Location = new Point(180, 0);
			customPanel.Controls.Add(difficultyLabel);
			difficultyBox = NewCombo(new string[] { Localization.T("Difficulty.Peaceful"), Localization.T("Difficulty.Easy"), Localization.T("Difficulty.Normal"), Localization.T("Difficulty.Hard") });
			difficultyBox.Location = new Point(180, 22);
			difficultyBox.TabIndex = 21;
			customPanel.Controls.Add(difficultyBox);
			hardcoreBox = NewCheckBox(Localization.T("Hardcore"), current.Hardcore);
			hardcoreBox.Location = new Point(370, 24);
			hardcoreBox.TabIndex = 22;
			customPanel.Controls.Add(hardcoreBox);

			advancedButton = NewFlatButton(Localization.T("Setup.More"), 188);
			advancedButton.Tag = "secondary";
			advancedButton.Visible = false;
			rulesLabel = NewLabel(Localization.T("Setup.Rules"), 12F, true);
			rulesLabel.Location = new Point(0, 596);
			body.Controls.Add(rulesLabel);
			advancedPanel = new Panel();
			advancedPanel.Location = new Point(0, 618);
			advancedPanel.Size = new Size(700, 84);
			advancedPanel.Visible = true;
			body.Controls.Add(advancedPanel);
			pvpBox = NewCheckBox(Localization.T("Setup.Pvp"), current.Pvp);
			pvpBox.Location = new Point(0, 0);
			pvpBox.TabIndex = 23;
			advancedPanel.Controls.Add(pvpBox);
			whitelistBox = NewCheckBox(Localization.T("Setup.Whitelist"), current.WhiteList);
			whitelistBox.Location = new Point(140, 0);
			whitelistBox.TabIndex = 24;
			advancedPanel.Controls.Add(whitelistBox);
			commandBlockBox = NewCheckBox(Localization.T("Setup.CommandBlock"), current.CommandBlock);
			commandBlockBox.Location = new Point(310, 0);
			commandBlockBox.TabIndex = 25;
			advancedPanel.Controls.Add(commandBlockBox);
			gameModeBox.SelectedIndexChanged += delegate
			{
				if (selectedPreset == "custom" && gameModeBox.SelectedIndex == 1)
				{
					commandBlockBox.Checked = true;
				}
			};
			onlineModeBox = NewCheckBox(Localization.T("Setup.OnlineMode"), current.OnlineMode);
			onlineModeBox.Location = new Point(470, 0);
			onlineModeBox.TabIndex = 26;
			advancedPanel.Controls.Add(onlineModeBox);
			autoUpdateBox = NewCheckBox(Localization.T("Setup.PaperUpdate"), current.AutoUpdate);
			autoUpdateBox.Location = new Point(0, 36);
			autoUpdateBox.TabIndex = 27;
			advancedPanel.Controls.Add(autoUpdateBox);
			Label viewLabel = NewLabel(Localization.T("Setup.ViewDistance"), 9F, true);
			viewLabel.Location = new Point(210, 34);
			advancedPanel.Controls.Add(viewLabel);
			viewBox = NewNumber(3, 32, current.ViewDistance);
			viewBox.Location = new Point(210, 56);
			viewBox.TabIndex = 28;
			advancedPanel.Controls.Add(viewBox);
			Label simulationLabel = NewLabel(Localization.T("Setup.SimulationDistance"), 9F, true);
			simulationLabel.Location = new Point(340, 34);
			advancedPanel.Controls.Add(simulationLabel);
			simulationBox = NewNumber(3, 32, current.SimulationDistance);
			simulationBox.Location = new Point(340, 56);
			simulationBox.TabIndex = 29;
			advancedPanel.Controls.Add(simulationBox);
			Label ownerLabel = NewLabel(Localization.T("Setup.Owner"), 9F, true);
			ownerLabel.Location = new Point(500, 34);
			advancedPanel.Controls.Add(ownerLabel);
			ownerBox = NewTextBox();
			ownerBox.Location = new Point(500, 56);
			ownerBox.Size = new Size(180, 28);
			ownerBox.MaxLength = 16;
			ownerBox.Text = current.OwnerName;
			ownerBox.TabIndex = 30;
			advancedPanel.Controls.Add(ownerBox);

			validationLabel = NewLabel(string.Empty, 9F, false);
			validationLabel.Location = new Point(26, ClientSize.Height - 58);
			validationLabel.Size = new Size(Math.Max(180, ClientSize.Width - 310), 44);
			validationLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
			validationLabel.AutoSize = false;
			validationLabel.TextAlign = ContentAlignment.MiddleLeft;
			validationLabel.ForeColor = setupPalette.Warning;
			validationLabel.AccessibleRole = AccessibleRole.Alert;
			Controls.Add(validationLabel);

			Button cancel = NewFlatButton(Localization.T(editing ? "Setup.CancelEdit" : "Setup.Cancel"), 100);
			cancel.Tag = "secondary";
			cancel.Location = new Point(ClientSize.Width - 244, ClientSize.Height - 54);
			cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			cancel.DialogResult = DialogResult.Cancel;
			cancel.TabIndex = 40;
			Controls.Add(cancel);
			Button save = NewFlatButton(Localization.T(editing ? "Setup.SaveEdit" : "Setup.Save"), 132);
			ApplyButtonIcon(save, ButtonIcon.Check);
			save.Tag = "primary";
			save.Location = new Point(ClientSize.Width - 132, ClientSize.Height - 54);
			save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			save.BackColor = setupPalette.Accent;
			save.ForeColor = Color.White;
			save.Click += SaveSettings;
			save.TabIndex = 39;
			Controls.Add(save);
			AcceptButton = save;
			CancelButton = cancel;

			SelectRuntimeValues(current);
			serverTypeBox.SelectedIndexChanged += delegate
			{
				RefreshVersionChoices(null);
				UpdateManualJarControls();
			};
			includeSnapshotsBox.CheckedChanged += delegate { RefreshVersionChoices(null); };
			manualJarBox.CheckedChanged += delegate { UpdateManualJarControls(); };
			Shown += delegate { BeginVersionChoiceLoad(null); };
			UpdateManualJarControls();
			SelectInitialPreset(current.PresetName);
			SelectComboValues(current);
			ConfigureSetupAccessibility();
			RegisterValidationReset(profileBox);
			RegisterValidationReset(motdBox);
			RegisterValidationReset(manualJarPathBox);
			RegisterValidationReset(ownerBox);
			ApplySetupTheme(this, setupPalette);
			ApplyCommonButtonToolTips(this);
			 save.BackColor = setupPalette.Accent;
			save.ForeColor = Color.White;
		}

		private void ConfigureSetupAccessibility()
		{
			bool korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
			ConfigureAccessibleField(profileBox, Localization.T("Setup.ProfileName"), korean ? "서버 목록과 데이터 폴더를 구분하는 이름입니다." : "Name used to identify this server profile and its data folder.");
			ConfigureAccessibleField(serverTypeBox, Localization.T("Setup.ServerType"), korean ? "Paper, Purpur, Fabric, Vanilla 또는 직접 JAR을 선택합니다." : "Choose Paper, Purpur, Fabric, Vanilla, or a custom JAR.");
			ConfigureAccessibleField(versionBox, Localization.T("Setup.MinecraftVersion"), korean ? "새 서버에서 사용할 Minecraft 버전입니다." : "Minecraft version used by the server.");
			ConfigureAccessibleField(includeSnapshotsBox, Localization.T("Setup.IncludeSnapshots"), korean ? "정식 버전 외에 스냅샷과 프리릴리즈도 버전 목록에 표시합니다." : "Also show snapshots and pre-releases in the version list.");
			ConfigureAccessibleField(manualJarPathBox, Localization.T("Setup.UseManualJar"), korean ? "직접 실행할 서버 JAR 파일 경로입니다." : "Path to the custom server JAR.");
			ConfigureAccessibleField(customJavaBox, Localization.T("Setup.JavaVersion"), korean ? "직접 JAR 제작자가 요구한 Java 버전입니다." : "Java version required by the custom JAR author.");
			ConfigureAccessibleField(autoUpdateBox, Localization.T("Setup.PaperUpdate"), korean ? "서버 시작 전에 선택한 종류와 버전의 최신 빌드를 확인합니다." : "Check for the latest build of the selected server type and version before start.");
			ConfigureAccessibleField(motdBox, Localization.T("Setup.ServerName"), korean ? "서버 목록에 표시되는 소개 문구입니다." : "Description shown in the Minecraft server list.");
			ConfigureAccessibleField(playersBox, Localization.T("Setup.MaxPlayers"), korean ? "동시에 접속할 수 있는 최대 인원입니다." : "Maximum number of simultaneous players.");
			ConfigureAccessibleField(portBox, Localization.T("Setup.Port"), korean ? "서버가 사용할 TCP 포트입니다." : "TCP port used by the server.");
			ConfigureAccessibleField(memoryBox, Localization.T("Setup.Memory"), korean ? "서버에 할당할 최대 메모리입니다." : "Maximum memory allocated to the server.");
			ConfigureAccessibleField(gameModeBox, Localization.T("Setup.GameMode"), string.Empty);
			ConfigureAccessibleField(difficultyBox, Localization.T("Setup.Difficulty"), string.Empty);
			ConfigureAccessibleField(viewBox, Localization.T("Setup.ViewDistance"), korean ? "플레이어 주변에서 전송할 청크 범위입니다." : "Chunk radius sent around each player.");
			ConfigureAccessibleField(simulationBox, Localization.T("Setup.SimulationDistance"), korean ? "몹과 레드스톤이 동작하는 청크 범위입니다." : "Chunk radius where mobs and redstone remain active.");
			ConfigureAccessibleField(ownerBox, Localization.T("Setup.Owner"), korean ? "자동 OP를 받을 Minecraft 사용자 이름입니다." : "Minecraft username that receives owner operator access.");
		}

		private void RegisterValidationReset(Control control)
		{
			control.TextChanged += delegate
			{
				if (!string.IsNullOrEmpty(validationErrors.GetError(control)))
				{
					validationErrors.SetError(control, string.Empty);
					validationLabel.Text = string.Empty;
				}
			};
		}

		private void ShowValidationError(Control control, string message)
		{
			validationErrors.SetError(control, message);
			validationLabel.Text = message;
			validationLabel.AccessibleName = message;
			validationLabel.ForeColor = setupPalette.Warning;
			body.ScrollControlIntoView(control);
			control.Focus();
		}

		private void SelectRuntimeValues(ServerSettings current)
		{
			string[] values = GetServerTypeValues();
			serverTypeBox.SelectedIndex = FindIndexOrDefault(values, NormalizeServerType(current.ServerType), 0);
			ApplyFastVersionChoices(string.IsNullOrWhiteSpace(current.MinecraftVersion) ? "26.2" : current.MinecraftVersion);
		}

		private void RefreshVersionChoices(string preferredVersion)
		{
			ApplyFastVersionChoices(preferredVersion);
			if (IsHandleCreated)
			{
				BeginVersionChoiceLoad(preferredVersion);
			}
		}

		private void ApplyFastVersionChoices(string preferredVersion)
		{
			string[] values = GetServerTypeValues();
			string serverType = values[Math.Max(0, Math.Min(serverTypeBox.SelectedIndex, values.Length - 1))];
			string previous = preferredVersion;
			if (string.IsNullOrWhiteSpace(previous) && versionBox.SelectedItem != null)
			{
				previous = Convert.ToString(versionBox.SelectedItem);
			}
			string cacheKey = serverType + "|" + includeSnapshotsBox.Checked.ToString();
			string[] versions = null;
			lock (VersionCacheLock)
			{
				CachedVersionChoices cached;
				if (VersionCache.TryGetValue(cacheKey, out cached) && DateTime.UtcNow - cached.LoadedUtc < TimeSpan.FromMinutes(10.0))
				{
					versions = cached.Values;
				}
			}
			if (versions == null)
			{
				List<string> fallback = new List<string>(GetFallbackMinecraftVersions(includeSnapshotsBox.Checked));
				if (!string.IsNullOrWhiteSpace(previous) && fallback.FindIndex(delegate(string item) { return string.Equals(item, previous, StringComparison.OrdinalIgnoreCase); }) < 0)
				{
					fallback.Insert(0, previous);
				}
				versions = fallback.ToArray();
			}
			ApplyVersionChoices(versions, previous);
		}

		private void ApplyVersionChoices(string[] versions, string preferredVersion)
		{
			string previous = preferredVersion;
			if (string.IsNullOrWhiteSpace(previous) && versionBox.SelectedItem != null)
			{
				previous = Convert.ToString(versionBox.SelectedItem);
			}
			versionBox.Items.Clear();
			versionBox.Items.AddRange(versions);
			int selected = FindIndexOrDefault(versions, previous, 0);
			if (versionBox.Items.Count > 0)
			{
				versionBox.SelectedIndex = selected;
			}
		}

		private void BeginVersionChoiceLoad(string preferredVersion)
		{
			string[] serverTypeValues = GetServerTypeValues();
			string serverType = serverTypeValues[Math.Max(0, Math.Min(serverTypeBox.SelectedIndex, serverTypeValues.Length - 1))];
			bool includeSnapshots = includeSnapshotsBox.Checked;
			string previous = preferredVersion;
			if (string.IsNullOrWhiteSpace(previous) && versionBox.SelectedItem != null)
			{
				previous = Convert.ToString(versionBox.SelectedItem);
			}
			string cacheKey = serverType + "|" + includeSnapshots.ToString();
			lock (VersionCacheLock)
			{
				CachedVersionChoices cached;
				if (VersionCache.TryGetValue(cacheKey, out cached) && DateTime.UtcNow - cached.LoadedUtc < TimeSpan.FromMinutes(10.0))
				{
					versionStatusLabel.Text = Localization.T("Setup.VersionReady");
					versionStatusLabel.ForeColor = setupPalette.Muted;
					setupToolTip.SetToolTip(versionStatusLabel, string.Empty);
					return;
				}
			}
			int request = Interlocked.Increment(ref versionLoadRequest);
			versionStatusLabel.Text = Localization.T("Setup.VersionLoading");
			versionStatusLabel.ForeColor = setupPalette.Muted;
			setupToolTip.SetToolTip(versionStatusLabel, string.Empty);
			Thread worker = new Thread((ThreadStart)delegate
			{
				string[] loaded = null;
				Exception failure = null;
				try
				{
					loaded = GetMinecraftVersionChoices(serverType, includeSnapshots);
					lock (VersionCacheLock)
					{
						CachedVersionChoices entry = new CachedVersionChoices();
						entry.Values = loaded;
						entry.LoadedUtc = DateTime.UtcNow;
						VersionCache[cacheKey] = entry;
					}
				}
				catch (Exception exception)
				{
					failure = exception;
				}
				if (IsDisposed || !IsHandleCreated)
				{
					return;
				}
				TryPostToUi(this, (MethodInvoker)delegate
				{
					if (request != versionLoadRequest || IsDisposed)
					{
						return;
					}
					if (loaded != null && loaded.Length > 0)
					{
						string currentSelection = versionBox.SelectedItem == null ? previous : Convert.ToString(versionBox.SelectedItem);
						ApplyVersionChoices(loaded, currentSelection);
						versionStatusLabel.Text = Localization.T("Setup.VersionReady");
						versionStatusLabel.ForeColor = setupPalette.Muted;
						setupToolTip.SetToolTip(versionStatusLabel, string.Empty);
					}
					else
					{
						versionStatusLabel.Text = Localization.T("Setup.VersionFallback");
						versionStatusLabel.ForeColor = setupPalette.Warning;
						setupToolTip.SetToolTip(versionStatusLabel, failure == null ? string.Empty : failure.Message);
					}
					versionStatusLabel.AccessibleName = versionStatusLabel.Text;
				});
			});
			worker.IsBackground = true;
			worker.Name = "Minecraft 버전 목록";
			worker.Start();
		}

		private void UpdateManualJarControls()
		{
			string[] values = GetServerTypeValues();
			string serverType = values[Math.Max(0, Math.Min(serverTypeBox.SelectedIndex, values.Length - 1))];
			bool customServer = serverType == "custom";
			if (customServer)
			{
				manualJarBox.Checked = true;
			}
			manualJarBox.Enabled = !customServer;
			includeSnapshotsBox.Enabled = !customServer;
			versionBox.Enabled = !customServer;
			bool enabled = manualJarBox.Checked || customServer;
			manualJarPathBox.Enabled = enabled;
			customJavaBox.Enabled = enabled;
			autoUpdateBox.Enabled = !customServer && !manualJarBox.Checked;
			setupToolTip.SetToolTip(manualJarBox, customServer ? (Localization.CurrentLanguage == Localization.Korean ? "직접 JAR 서버에는 JAR 파일 지정이 필수입니다." : "A JAR file is required for a custom server.") : string.Empty);
			setupToolTip.SetToolTip(versionBox, customServer ? (Localization.CurrentLanguage == Localization.Korean ? "직접 JAR은 파일 제작자가 안내한 Minecraft 버전을 사용합니다." : "Use the Minecraft version specified by the custom JAR author.") : string.Empty);
			setupToolTip.SetToolTip(autoUpdateBox, autoUpdateBox.Enabled ? string.Empty : (Localization.CurrentLanguage == Localization.Korean ? "직접 지정한 JAR은 런처가 자동으로 교체하지 않습니다." : "The launcher does not replace a manually selected JAR."));
		}

		private void AddPreset(TableLayoutPanel grid, string key, string text, int index)
		{
			RadioButton button = new RoundedPresetButton();
			button.Text = text;
			button.TextAlign = ContentAlignment.MiddleCenter;
			button.Dock = DockStyle.Fill;
			button.Margin = new Padding(6);
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 232);
			button.FlatAppearance.CheckedBackColor = Color.FromArgb(220, 231, 255);
			button.Tag = key;
			button.TabIndex = 8 + index;
			button.CheckedChanged += delegate
			{
				if (button.Checked)
				{
					selectedPreset = key;
					PresetChanged();
				}
			};
			presetButtons[key] = button;
			grid.Controls.Add(button, index % 4, index / 4);
		}

		private void SelectInitialPreset(string name)
		{
			string key = "custom";
			if (name == "평화로움 야생") key = "survival-peaceful";
			else if (name == "쉬움 야생") key = "survival-easy";
			else if (name == "보통 야생") key = "survival-normal";
			else if (name == "어려움 야생") key = "survival-hard";
			else if (name == "하드코어 야생") key = "survival-hardcore";
			else if (name == "크리에이티브 월드 (일반 지형)") key = "creative-normal";
			else if (name == "크리에이티브 월드 (평지)") key = "creative-flat";
			presetButtons[key].Checked = true;
		}

		private void SelectComboValues(ServerSettings settings)
		{
			gameModeBox.SelectedIndex = settings.GameMode == "creative" ? 1 : settings.GameMode == "adventure" ? 2 : settings.GameMode == "spectator" ? 3 : 0;
			difficultyBox.SelectedIndex = settings.Difficulty == "peaceful" ? 0 : settings.Difficulty == "easy" ? 1 : settings.Difficulty == "hard" ? 3 : 2;
		}

		private void PresetChanged()
		{
			bool custom = selectedPreset == "custom";
			bool creative = selectedPreset == "creative-normal" || selectedPreset == "creative-flat";
			customPanel.Visible = custom;
			rulesLabel.Top = custom ? 596 : 548;
			advancedPanel.Top = custom ? 618 : 570;
			body.AutoScrollMinSize = new Size(0, custom ? 704 : 656);
			commandBlockBox.Checked = creative || (custom && gameModeBox != null && gameModeBox.SelectedIndex == 1) || source.CommandBlock;
			commandBlockBox.Enabled = !creative;
		}

		private void SaveSettings(object sender, EventArgs eventArgs)
		{
			validationErrors.Clear();
			validationLabel.Text = string.Empty;
			if (!IsValidProfileName(profileBox.Text.Trim()))
			{
				ShowValidationError(profileBox, Localization.T("Setup.ProfileInvalid"));
				return;
			}
			if (string.IsNullOrWhiteSpace(motdBox.Text))
			{
				ShowValidationError(motdBox, Localization.T("Setup.ServerNameRequired"));
				return;
			}
			string[] serverTypeValues = GetServerTypeValues();
			string selectedServerType = serverTypeValues[Math.Max(0, Math.Min(serverTypeBox.SelectedIndex, serverTypeValues.Length - 1))];
			bool useManualJar = manualJarBox.Checked || selectedServerType == "custom";
			if (useManualJar && (string.IsNullOrWhiteSpace(manualJarPathBox.Text) || !File.Exists(manualJarPathBox.Text.Trim('"', ' '))))
			{
				ShowValidationError(manualJarPathBox, Localization.T("Setup.ManualJarMissing"));
				return;
			}
			if (!IsValidOwnerName(ownerBox.Text.Trim()))
			{
				advancedPanel.Visible = true;
				advancedButton.Text = Localization.T("Setup.CloseMore");
				ShowValidationError(ownerBox, Localization.T("Setup.OwnerInvalid"));
				return;
			}
			if (!onlineModeBox.Checked)
			{
				DialogResult risk = MessageBox.Show(Localization.T("Setup.OnlineModeWarning"), Localization.T("Setup.SecurityTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
				if (risk != DialogResult.Yes)
				{
					onlineModeBox.Checked = true;
					return;
				}
			}
			ServerSettings settings = new ServerSettings();
			settings.ProfileName = profileBox.Text.Trim();
			settings.ServerType = selectedServerType;
			settings.MinecraftVersion = versionBox.SelectedItem == null ? "26.2" : Convert.ToString(versionBox.SelectedItem);
			settings.IncludeSnapshots = includeSnapshotsBox.Checked;
			settings.UseManualJar = useManualJar;
			settings.ManualJarPath = useManualJar ? manualJarPathBox.Text.Trim('"', ' ') : string.Empty;
			settings.CustomJavaMajor = new int[] { 8, 11, 16, 17, 21, 25 }[Math.Max(0, customJavaBox.SelectedIndex)];
			if (selectedPreset == "custom")
			{
				settings.PresetName = "직접 설정";
				settings.GameMode = new string[] { "survival", "creative", "adventure", "spectator" }[gameModeBox.SelectedIndex];
				settings.Difficulty = new string[] { "peaceful", "easy", "normal", "hard" }[difficultyBox.SelectedIndex];
				settings.LevelType = "minecraft:normal";
				settings.Hardcore = hardcoreBox.Checked;
			}
			else
			{
				ApplyServerPreset(settings, selectedPreset);
			}
			settings.MaxPlayers = (int)playersBox.Value;
			settings.Motd = motdBox.Text.Trim();
			settings.ServerPort = (int)portBox.Value;
			settings.MemoryGb = (int)memoryBox.Value;
			settings.Pvp = pvpBox.Checked;
			settings.WhiteList = whitelistBox.Checked;
			settings.ViewDistance = (int)viewBox.Value;
			settings.SimulationDistance = (int)simulationBox.Value;
			settings.CommandBlock = settings.GameMode == "creative" || commandBlockBox.Checked;
			settings.OnlineMode = onlineModeBox.Checked;
			settings.AutoUpdate = autoUpdateBox.Enabled && autoUpdateBox.Checked;
			settings.OwnerName = ownerBox.Text.Trim();
			if (worldExists && IsMinecraftVersionDowngrade(source.MinecraftVersion, settings.MinecraftVersion))
			{
				MessageBox.Show(Localization.T("Setup.DowngradeWarning"), Localization.T("Setup.DowngradeTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			if ((!string.Equals(settings.ServerType, source.ServerType, StringComparison.OrdinalIgnoreCase) || !string.Equals(settings.MinecraftVersion, source.MinecraftVersion, StringComparison.OrdinalIgnoreCase)) && worldExists)
			{
				DialogResult compatibilityWarning = MessageBox.Show(Localization.T("Setup.CompatibilityWarning"), Localization.T("Setup.CompatibilityTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
				if (compatibilityWarning != DialogResult.Yes)
				{
					return;
				}
			}
			if (worldExists && !string.Equals(settings.LevelType, source.LevelType, StringComparison.OrdinalIgnoreCase))
			{
				DialogResult worldWarning = MessageBox.Show(Localization.T("Setup.WorldWarning"), Localization.T("Setup.WorldTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
				if (worldWarning != DialogResult.Yes)
				{
					return;
				}
			}
			SelectedSettings = settings;
			DialogResult = DialogResult.OK;
			Close();
		}

		private static Label NewLabel(string text, float size, bool semibold)
		{
			Label label = new Label();
			label.Text = text;
			label.Font = new Font(semibold ? "Segoe UI Variable Text Semib" : "Segoe UI Variable Text", size);
			label.AutoSize = true;
			return label;
		}

		private static TextBox NewTextBox()
		{
			TextBox box = new TextBox();
			box.BorderStyle = BorderStyle.FixedSingle;
			return box;
		}

		private static NumericUpDown NewNumber(int minimum, int maximum, int value)
		{
			NumericUpDown box = new NumericUpDown();
			box.Minimum = minimum;
			box.Maximum = maximum;
			box.Value = Math.Min(Math.Max(value, minimum), maximum);
			box.Width = 110;
			return box;
		}

		private static ComboBox NewCombo(string[] values)
		{
			ComboBox box = new ModernComboBox();
			box.Items.AddRange(values);
			box.Width = 160;
			box.SelectedIndex = 0;
			return box;
		}

		private static CheckBox NewCheckBox(string text, bool value)
		{
			CheckBox box = new CheckBox();
			box.Text = text;
			box.Checked = value;
			box.AutoSize = true;
			return box;
		}

		private static Button NewFlatButton(string text, int width)
		{
			Button button = new RoundedButton();
			button.Text = text;
			button.Width = width;
			button.Height = 42;
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			return button;
		}

		private static void ApplySetupTheme(Control parent, ThemePalette palette)
		{
			foreach (Control control in parent.Controls)
			{
				if (control is RoundedPresetButton)
				{
					control.BackColor = palette.Card;
					control.ForeColor = palette.Text;
					((RoundedPresetButton)control).FlatAppearance.BorderColor = palette.Border;
					((RoundedPresetButton)control).CheckedBackColor = palette.AccentSoft;
					((RoundedPresetButton)control).SelectedBorderColor = palette.Accent;
				}
				else if (control is Button)
				{
					Button button = (Button)control;
					button.FlatAppearance.BorderColor = palette.Border;
					if (string.Equals(Convert.ToString(button.Tag), "primary", StringComparison.Ordinal))
					{
						button.BackColor = palette.Accent;
						button.ForeColor = Color.White;
						button.FlatAppearance.MouseOverBackColor = palette.AccentHover;
					}
					else
					{
						button.BackColor = palette.CardSecondary;
						button.ForeColor = palette.Text;
						button.FlatAppearance.MouseOverBackColor = palette.AccentSoft;
					}
				}
				else if (control is TextBox || control is NumericUpDown || control is ComboBox)
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
				else if (control is CheckBox)
				{
					CheckBox checkBox = (CheckBox)control;
					checkBox.UseVisualStyleBackColor = false;
					checkBox.BackColor = palette.Window;
					checkBox.ForeColor = palette.Text;
				}
				else if (!(control is Label) || control.ForeColor == Color.FromArgb(24, 32, 45))
				{
					control.BackColor = palette.Window;
					control.ForeColor = palette.Text;
				}
				ApplySetupTheme(control, palette);
			}
		}
	}
}
