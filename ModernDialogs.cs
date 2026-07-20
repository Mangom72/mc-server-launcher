using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static partial class Launcher
{
	private static DialogResult ShowMineHarborDialog(string message, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		return ShowMineHarborDialog(launcherForm, message, caption, buttons, icon);
	}

	private static DialogResult ShowMineHarborDialog(IWin32Window owner, string message, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		Control dispatcher = owner as Control;
		if ((dispatcher == null || dispatcher.IsDisposed) && launcherForm != null && !launcherForm.IsDisposed)
		{
			dispatcher = launcherForm;
		}
		if (dispatcher != null && dispatcher.IsHandleCreated && dispatcher.InvokeRequired)
		{
			return (DialogResult)dispatcher.Invoke(new Func<DialogResult>(delegate { return ShowMineHarborDialog(owner, message, caption, buttons, icon); }));
		}
		bool dark = ResolveMineHarborDialogDarkTheme(owner);
		using (MineHarborMessageDialog dialog = new MineHarborMessageDialog(message, caption, buttons, icon, dark))
		{
			Control ownerControl = owner as Control;
			if (ownerControl != null && (ownerControl.IsDisposed || !ownerControl.IsHandleCreated)) owner = null;
			return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
		}
	}

	private static bool ResolveMineHarborDialogDarkTheme(IWin32Window owner)
	{
		if (launcherForm != null && !launcherForm.IsDisposed) return launcherForm.UsesDarkTheme;
		Control control = owner as Control;
		if (control == null) return false;
		Color color = control.BackColor;
		return color.R * 299 + color.G * 587 + color.B * 114 < 128000;
	}

	private static Button CreateMineHarborDialogButton(string text, int width, string role, ButtonIcon icon, ThemePalette palette)
	{
		RoundedButton button = new RoundedButton();
		button.Text = text;
		button.Size = new Size(width, 44);
		button.Tag = role;
		button.IconKind = icon;
		button.FlatAppearance.BorderColor = palette.Border;
		button.AccessibleName = text;
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
		return button;
	}

	private static bool ShowTimedDestructiveConfirmation(IWin32Window owner, string itemName)
	{
		bool dark = ResolveMineHarborDialogDarkTheme(owner);
		using (TimedDestructiveConfirmDialog dialog = new TimedDestructiveConfirmDialog(itemName, dark))
		{
			return owner == null ? dialog.ShowDialog() == DialogResult.OK : dialog.ShowDialog(owner) == DialogResult.OK;
		}
	}

	private static int GetTimedConfirmationRemainingSeconds(DateTime startedUtc, DateTime nowUtc, int delaySeconds)
	{
		if (delaySeconds <= 0) return 0;
		double elapsed = Math.Max(0.0, (nowUtc.ToUniversalTime() - startedUtc.ToUniversalTime()).TotalSeconds);
		return Math.Max(0, (int)Math.Ceiling(delaySeconds - elapsed));
	}

	private sealed class TimedDestructiveConfirmDialog : Form
	{
		private const int ConfirmationDelaySeconds = 3;
		private readonly bool korean;
		private readonly Button deleteButton;
		private readonly System.Windows.Forms.Timer countdownTimer;
		private DateTime startedUtc;

		public TimedDestructiveConfirmDialog(string itemName, bool dark)
		{
			korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
			ThemePalette palette = ThemePalette.Create(dark);
			ApplyLauncherWindowIcon(this);
			Text = korean ? "영구 삭제 확인" : "Confirm permanent deletion";
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MinimizeBox = false;
			MaximizeBox = false;
			ShowInTaskbar = false;
			ClientSize = new Size(580, 250);
			MinimumSize = Size;
			MaximumSize = Size;
			Font = new Font("Pretendard", 11F);
			BackColor = palette.Window;
			ForeColor = palette.Text;
			AutoScaleMode = AutoScaleMode.Dpi;

			Label heading = new Label
			{
				Text = korean ? "이 서버를 완전히 삭제할까요?" : "Delete this server permanently?",
				Location = new Point(28, 24),
				Size = new Size(524, 34),
				Font = new Font("Pretendard", 17F, FontStyle.Bold),
				ForeColor = palette.Text,
				AutoEllipsis = true
			};
			Controls.Add(heading);

			Label description = new Label
			{
				Text = (korean ? "되돌릴 수 없습니다. 내용을 확인하는 동안 삭제 버튼이 잠시 잠깁니다.\r\n서버: " : "This cannot be undone. The delete button is briefly locked for review.\r\nServer: ") + (itemName ?? string.Empty),
				Location = new Point(30, 70),
				Size = new Size(520, 78),
				ForeColor = palette.Muted,
				AutoEllipsis = false
			};
			Controls.Add(description);

			Button cancelButton = CreateMineHarborDialogButton(korean ? "취소" : "Cancel", 104, "secondary", ButtonIcon.None, palette);
			cancelButton.Location = new Point(306, 180);
			cancelButton.DialogResult = DialogResult.Cancel;
			Controls.Add(cancelButton);

			deleteButton = CreateMineHarborDialogButton(string.Empty, 150, "danger", ButtonIcon.Trash, palette);
			deleteButton.Location = new Point(418, 180);
			deleteButton.DialogResult = DialogResult.OK;
			deleteButton.Enabled = false;
			deleteButton.AccessibleDescription = korean ? "3초 후 사용할 수 있습니다." : "Available after three seconds.";
			Controls.Add(deleteButton);
			AcceptButton = deleteButton;
			CancelButton = cancelButton;

			countdownTimer = new System.Windows.Forms.Timer { Interval = 100 };
			countdownTimer.Tick += delegate { UpdateCountdown(); };
			Shown += delegate { startedUtc = DateTime.UtcNow; UpdateCountdown(); countdownTimer.Start(); };
			FormClosed += delegate { countdownTimer.Stop(); countdownTimer.Dispose(); };
		}

		private void UpdateCountdown()
		{
			int remaining = GetTimedConfirmationRemainingSeconds(startedUtc, DateTime.UtcNow, ConfirmationDelaySeconds);
			deleteButton.Text = remaining > 0 ? (korean ? "영구 삭제 (" : "Delete forever (") + remaining + ")" : (korean ? "영구 삭제" : "Delete forever");
			deleteButton.AccessibleName = deleteButton.Text;
			deleteButton.Enabled = remaining == 0;
			if (remaining == 0) countdownTimer.Stop();
		}
	}

	private sealed class MineHarborMessageDialog : Form
	{
		private readonly MessageBoxButtons buttonKind;

		public MineHarborMessageDialog(string message, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, bool dark)
		{
			buttonKind = buttons;
			ThemePalette palette = ThemePalette.Create(dark);
			ApplyLauncherWindowIcon(this);
			Text = string.IsNullOrWhiteSpace(caption) ? Localization.T("App.Title") : caption;
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MinimizeBox = false;
			MaximizeBox = false;
			ShowInTaskbar = false;
			Font = new Font("Pretendard", 11F);
			BackColor = palette.Window;
			ForeColor = palette.Text;
			KeyPreview = true;
			AutoScaleMode = AutoScaleMode.Dpi;

			Label heading = new Label();
			heading.Text = Text;
			heading.Font = new Font("Pretendard", 17F, FontStyle.Bold);
			heading.ForeColor = palette.Text;
			heading.AutoSize = false;
			heading.Location = new Point(80, 24);
			heading.Size = new Size(476, 34);
			heading.AutoEllipsis = true;
			heading.AccessibleName = Text;
			Controls.Add(heading);

			MineHarborDialogIcon iconView = new MineHarborDialogIcon();
			iconView.Kind = icon;
			iconView.Palette = palette;
			iconView.Location = new Point(24, 25);
			iconView.Size = new Size(40, 40);
			iconView.AccessibleName = GetDialogIconAccessibleName(icon);
			Controls.Add(iconView);

			string safeMessage = message ?? string.Empty;
			Size measured = TextRenderer.MeasureText(safeMessage, Font, new Size(476, 260), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
			int messageHeight = Math.Max(64, Math.Min(240, measured.Height + 14));
			RichTextBox messageBox = new RichTextBox();
			messageBox.ReadOnly = true;
			messageBox.BorderStyle = BorderStyle.None;
			messageBox.BackColor = palette.Window;
			messageBox.ForeColor = palette.Text;
			messageBox.Location = new Point(80, 72);
			messageBox.Size = new Size(476, messageHeight);
			messageBox.Text = safeMessage;
			messageBox.ScrollBars = measured.Height + 14 > messageHeight ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None;
			messageBox.DetectUrls = true;
			messageBox.AccessibleName = safeMessage;
			Controls.Add(messageBox);

			int buttonTop = messageBox.Bottom + 24;
			CreateDialogButtons(buttons, palette, buttonTop);
			ClientSize = new Size(580, buttonTop + 68);
			MinimumSize = Size;
			MaximumSize = Size;
		}

		private void CreateDialogButtons(MessageBoxButtons buttons, ThemePalette palette, int top)
		{
			bool korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
			int right = 556;
			if (buttons == MessageBoxButtons.OK)
			{
				Button ok = AddDialogButton(korean ? "확인" : "OK", 112, "primary", ButtonIcon.Check, DialogResult.OK, palette, ref right, top);
				AcceptButton = ok;
				CancelButton = ok;
				return;
			}
			if (buttons == MessageBoxButtons.YesNoCancel)
			{
				Button cancel = AddDialogButton(korean ? "취소" : "Cancel", 104, "secondary", ButtonIcon.None, DialogResult.Cancel, palette, ref right, top);
				Button no = AddDialogButton(korean ? "아니요" : "No", 104, "secondary", ButtonIcon.None, DialogResult.No, palette, ref right, top);
				Button yes = AddDialogButton(korean ? "예" : "Yes", 112, "primary", ButtonIcon.Check, DialogResult.Yes, palette, ref right, top);
				AcceptButton = yes;
				CancelButton = cancel;
				return;
			}
			Button noButton = AddDialogButton(korean ? "아니요" : "No", 104, "secondary", ButtonIcon.None, DialogResult.No, palette, ref right, top);
			Button yesButton = AddDialogButton(korean ? "예" : "Yes", 112, "primary", ButtonIcon.Check, DialogResult.Yes, palette, ref right, top);
			AcceptButton = yesButton;
			CancelButton = noButton;
		}

		private Button AddDialogButton(string text, int width, string role, ButtonIcon icon, DialogResult result, ThemePalette palette, ref int right, int top)
		{
			Button button = CreateMineHarborDialogButton(text, width, role, icon, palette);
			right -= width;
			button.Location = new Point(right, top);
			button.DialogResult = result;
			Controls.Add(button);
			right -= 8;
			return button;
		}

		protected override void OnFormClosing(FormClosingEventArgs eventArgs)
		{
			if (DialogResult == DialogResult.None)
			{
				DialogResult = buttonKind == MessageBoxButtons.OK ? DialogResult.OK : buttonKind == MessageBoxButtons.YesNo ? DialogResult.No : DialogResult.Cancel;
			}
			base.OnFormClosing(eventArgs);
		}
	}

	private sealed class MineHarborDialogIcon : Control
	{
		public MessageBoxIcon Kind { get; set; }
		public ThemePalette Palette { get; set; }

		public MineHarborDialogIcon()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
		}

		protected override void OnPaint(PaintEventArgs eventArgs)
		{
			base.OnPaint(eventArgs);
			ThemePalette palette = Palette ?? ThemePalette.Create(false);
			Color color = Kind == MessageBoxIcon.Error ? palette.Danger : Kind == MessageBoxIcon.Warning ? palette.Warning : palette.Accent;
			eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			RectangleF bounds = new RectangleF(3, 3, Width - 7, Height - 7);
			using (Pen pen = new Pen(color, 2.2F))
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(28, color)))
			{
				eventArgs.Graphics.FillEllipse(brush, bounds);
				eventArgs.Graphics.DrawEllipse(pen, bounds);
				string symbol = Kind == MessageBoxIcon.Question ? "?" : Kind == MessageBoxIcon.Warning || Kind == MessageBoxIcon.Error ? "!" : "i";
				using (Font font = new Font("Pretendard", 17F, FontStyle.Bold))
				{
					TextRenderer.DrawText(eventArgs.Graphics, symbol, font, Rectangle.Round(bounds), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
				}
			}
		}
	}

	private static string GetDialogIconAccessibleName(MessageBoxIcon icon)
	{
		bool korean = string.Equals(Localization.CurrentLanguage, Localization.Korean, StringComparison.OrdinalIgnoreCase);
		if (icon == MessageBoxIcon.Error) return korean ? "오류" : "Error";
		if (icon == MessageBoxIcon.Warning) return korean ? "경고" : "Warning";
		if (icon == MessageBoxIcon.Question) return korean ? "질문" : "Question";
		return korean ? "안내" : "Information";
	}
}
