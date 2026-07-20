using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static partial class Launcher
{
	internal sealed class BufferedListView : ListView
	{
		private ThemePalette palette;

		public BufferedListView()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
			BorderStyle = BorderStyle.None;
			OwnerDraw = true;
			DrawColumnHeader += DrawModernColumnHeader;
			DrawItem += delegate(object sender, DrawListViewItemEventArgs eventArgs) { if (View != View.Details) eventArgs.DrawDefault = true; };
			DrawSubItem += DrawModernSubItem;
			HandleCreated += delegate { ApplyNativeListTheme(); };
		}

		public void ApplyPalette(ThemePalette value)
		{
			palette = value;
			BackColor = value.Card;
			ForeColor = value.Text;
			ApplyNativeListTheme();
			Invalidate();
		}

		private void ApplyNativeListTheme()
		{
			if (palette != null) NativeControlTheme.Apply(this, palette.Window.GetBrightness() < 0.45F);
		}

		private void DrawModernColumnHeader(object sender, DrawListViewColumnHeaderEventArgs eventArgs)
		{
			ThemePalette colors = palette ?? ThemePalette.Create(false);
			using (SolidBrush brush = new SolidBrush(colors.CardSecondary)) eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
			using (Pen pen = new Pen(colors.Border)) eventArgs.Graphics.DrawLine(pen, eventArgs.Bounds.Left, eventArgs.Bounds.Bottom - 1, eventArgs.Bounds.Right, eventArgs.Bounds.Bottom - 1);
			Rectangle textBounds = new Rectangle(eventArgs.Bounds.Left + 10, eventArgs.Bounds.Top, Math.Max(1, eventArgs.Bounds.Width - 14), eventArgs.Bounds.Height);
			TextRenderer.DrawText(eventArgs.Graphics, eventArgs.Header.Text, Font, textBounds, colors.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
		}

		private void DrawModernSubItem(object sender, DrawListViewSubItemEventArgs eventArgs)
		{
			ThemePalette colors = palette ?? ThemePalette.Create(false);
			bool selected = eventArgs.Item.Selected;
			using (SolidBrush brush = new SolidBrush(selected ? colors.AccentSoft : colors.Card)) eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
			using (Pen pen = new Pen(Color.FromArgb(selected ? 72 : 42, colors.Border))) eventArgs.Graphics.DrawLine(pen, eventArgs.Bounds.Left, eventArgs.Bounds.Bottom - 1, eventArgs.Bounds.Right, eventArgs.Bounds.Bottom - 1);
			Rectangle textBounds = new Rectangle(eventArgs.Bounds.Left + 10, eventArgs.Bounds.Top, Math.Max(1, eventArgs.Bounds.Width - 14), eventArgs.Bounds.Height);
			TextRenderer.DrawText(eventArgs.Graphics, eventArgs.SubItem.Text, Font, textBounds, colors.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
		}
	}

	internal sealed class ModernComboBox : ComboBox
	{
		public Color SelectionBackColor { get; set; }
		public Color SelectionForeColor { get; set; }
		public Color BorderColor { get; set; }
		public Color ArrowColor { get; set; }
		public Color FocusBorderColor { get; set; }
		public bool UseDarkNativeTheme { get; set; }

		public ModernComboBox()
		{
			DrawMode = DrawMode.OwnerDrawFixed;
			DropDownStyle = ComboBoxStyle.DropDownList;
			FlatStyle = FlatStyle.Flat;
			ItemHeight = 30;
			SelectionBackColor = Color.FromArgb(232, 240, 254);
			SelectionForeColor = Color.FromArgb(25, 31, 40);
			BorderColor = Color.FromArgb(220, 226, 232);
			ArrowColor = Color.FromArgb(82, 94, 108);
			FocusBorderColor = Color.FromArgb(0, 100, 255);
		}

		protected override void OnHandleCreated(EventArgs eventArgs)
		{
			base.OnHandleCreated(eventArgs);
			NativeControlTheme.Apply(this, UseDarkNativeTheme);
		}

		protected override void OnResize(EventArgs eventArgs)
		{
			base.OnResize(eventArgs);
			if (Width <= 1 || Height <= 1) return;
			using (GraphicsPath path = RoundedPanel.CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), Math.Min(9, Height / 3)))
			{
				Region previous = Region;
				Region = new Region(path);
				if (previous != null) previous.Dispose();
			}
		}

		protected override void OnGotFocus(EventArgs eventArgs) { base.OnGotFocus(eventArgs); Invalidate(); }
		protected override void OnLostFocus(EventArgs eventArgs) { base.OnLostFocus(eventArgs); Invalidate(); }

		protected override void OnDrawItem(DrawItemEventArgs eventArgs)
		{
			if (eventArgs.Index < 0 || eventArgs.Index >= Items.Count) return;
			bool selected = (eventArgs.State & DrawItemState.Selected) != 0;
			Color back = selected ? SelectionBackColor : BackColor;
			Color fore = selected ? SelectionForeColor : ForeColor;
			using (SolidBrush brush = new SolidBrush(back)) eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
			if (selected && DroppedDown)
			{
				Rectangle selectionBounds = Rectangle.Inflate(eventArgs.Bounds, -3, -2);
				using (GraphicsPath path = RoundedPanel.CreateRoundedRectangle(selectionBounds, 7))
				using (SolidBrush brush = new SolidBrush(SelectionBackColor)) eventArgs.Graphics.FillPath(brush, path);
			}
			Rectangle textBounds = new Rectangle(eventArgs.Bounds.X + 10, eventArgs.Bounds.Y, Math.Max(1, eventArgs.Bounds.Width - 34), eventArgs.Bounds.Height);
			TextRenderer.DrawText(eventArgs.Graphics, GetItemText(Items[eventArgs.Index]), Font, textBounds, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
			if ((eventArgs.State & DrawItemState.Focus) != 0 && DroppedDown) eventArgs.DrawFocusRectangle();
		}

		protected override void WndProc(ref Message message)
		{
			base.WndProc(ref message);
			if ((message.Msg != 0x000F && message.Msg != 0x0318) || Width <= 4 || Height <= 4) return;
			using (Graphics graphics = Graphics.FromHwnd(Handle))
			using (Pen border = new Pen(Focused ? FocusBorderColor : BorderColor, Focused ? 1.8F : 1F))
			using (Pen arrow = new Pen(Enabled ? ArrowColor : Color.FromArgb(120, ArrowColor), 1.7F))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
				using (GraphicsPath path = RoundedPanel.CreateRoundedRectangle(bounds, Math.Min(8, Height / 3)))
				using (SolidBrush fill = new SolidBrush(Enabled ? BackColor : ControlPaint.Light(BackColor, 0.06F)))
				{
					graphics.FillPath(fill, path);
					graphics.DrawPath(border, path);
				}
				string selectedText = SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : Text;
				Rectangle selectedTextBounds = new Rectangle(11, 1, Math.Max(1, Width - 42), Height - 2);
				TextRenderer.DrawText(graphics, selectedText, Font, selectedTextBounds, Enabled ? ForeColor : Color.FromArgb(135, ForeColor), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
				float centerX = Width - 15F;
				float centerY = Height / 2F;
				arrow.StartCap = LineCap.Round;
				arrow.EndCap = LineCap.Round;
				graphics.DrawLines(arrow, new PointF[] { new PointF(centerX - 4F, centerY - 2F), new PointF(centerX, centerY + 2F), new PointF(centerX + 4F, centerY - 2F) });
			}
		}
	}

	private static class NativeControlTheme
	{
		[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
		private static extern int SetWindowTheme(IntPtr handle, string subAppName, string subIdList);

		public static void Apply(Control control, bool dark)
		{
			if (control == null || control.IsDisposed || !control.IsHandleCreated) return;
			try { SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); }
			catch (DllNotFoundException) { }
			catch (EntryPointNotFoundException) { }
		}
	}

	internal sealed class ModernCheckBox : CheckBox
	{
		private bool mouseOver;
		public Color AccentColor { get; set; }
		public Color BorderColor { get; set; }
		public Color HoverBackColor { get; set; }

		public ModernCheckBox()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
			AccentColor = Color.FromArgb(0, 100, 255);
			BorderColor = Color.FromArgb(140, 148, 158);
			HoverBackColor = Color.FromArgb(232, 240, 254);
			Cursor = Cursors.Hand;
			MinimumSize = new Size(24, 30);
			Padding = new Padding(0, 2, 0, 2);
		}

		protected override void OnMouseEnter(EventArgs eventArgs) { mouseOver = true; Invalidate(); base.OnMouseEnter(eventArgs); }
		protected override void OnMouseLeave(EventArgs eventArgs) { mouseOver = false; Invalidate(); base.OnMouseLeave(eventArgs); }
		protected override void OnCheckedChanged(EventArgs eventArgs) { Invalidate(); base.OnCheckedChanged(eventArgs); }
		protected override void OnEnabledChanged(EventArgs eventArgs) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(eventArgs); }

		protected override void OnPaint(PaintEventArgs eventArgs)
		{
			eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Color parentBack = Parent == null ? SystemColors.Control : Parent.BackColor;
			eventArgs.Graphics.Clear(parentBack);
			int boxSize = Math.Min(20, Math.Max(16, Height - 8));
			Rectangle box = new Rectangle(1, (Height - boxSize) / 2, boxSize, boxSize);
			Color fill = Checked ? AccentColor : mouseOver ? HoverBackColor : BackColor;
			Color border = Enabled ? (Checked ? AccentColor : BorderColor) : Color.FromArgb(110, BorderColor);
			using (GraphicsPath path = RoundedPanel.CreateRoundedRectangle(box, 5))
			using (SolidBrush brush = new SolidBrush(fill))
			using (Pen pen = new Pen(border, Checked ? 1.6F : 1.2F))
			{
				eventArgs.Graphics.FillPath(brush, path);
				eventArgs.Graphics.DrawPath(pen, path);
			}
			if (Checked)
			{
				using (Pen check = new Pen(Color.White, 2F))
				{
					check.StartCap = LineCap.Round;
					check.EndCap = LineCap.Round;
					eventArgs.Graphics.DrawLines(check, new PointF[]
					{
						new PointF(box.Left + 4.5F, box.Top + 10F),
						new PointF(box.Left + 8.5F, box.Top + 14F),
						new PointF(box.Left + 15.5F, box.Top + 6F)
					});
				}
			}
			Rectangle textBounds = new Rectangle(box.Right + 9, 0, Math.Max(1, Width - box.Right - 10), Height);
			Color textColor = Enabled ? ForeColor : Color.FromArgb(140, ForeColor);
			TextRenderer.DrawText(eventArgs.Graphics, Text, Font, textBounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.NoPadding);
			if (Focused && ShowFocusCues)
			{
				Rectangle focus = new Rectangle(textBounds.Left - 2, 3, Math.Max(1, textBounds.Width), Math.Max(1, Height - 7));
				ControlPaint.DrawFocusRectangle(eventArgs.Graphics, focus, ForeColor, parentBack);
			}
		}
	}

	internal sealed class ModernTextBox : TextBox
	{
		private const int EmSetCueBanner = 0x1501;
		private string cueText = string.Empty;
		public event EventHandler CueTextChanged;

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

		public string CueText
		{
			get { return cueText; }
			set
			{
				string next = value ?? string.Empty;
				if (string.Equals(cueText, next, StringComparison.Ordinal)) return;
				cueText = next;
				ApplyCueText();
				EventHandler handler = CueTextChanged;
				if (handler != null) handler(this, EventArgs.Empty);
			}
		}

		public ModernTextBox()
		{
			BorderStyle = BorderStyle.FixedSingle;
		}

		protected override void OnHandleCreated(EventArgs eventArgs)
		{
			base.OnHandleCreated(eventArgs);
			ApplyCueText();
		}

		private void ApplyCueText()
		{
			if (IsHandleCreated) SendMessage(Handle, EmSetCueBanner, new IntPtr(1), cueText);
		}
	}

	internal sealed class ModernTabControl : TabControl
	{
		public Color AccentColor { get; set; }
		public Color SelectedBackColor { get; set; }
		public Color MutedForeColor { get; set; }

		public ModernTabControl()
		{
			DrawMode = TabDrawMode.OwnerDrawFixed;
			Appearance = TabAppearance.Buttons;
			SizeMode = TabSizeMode.Fixed;
			ItemSize = new Size(144, 38);
			Padding = new Point(16, 6);
			AccentColor = Color.FromArgb(0, 100, 255);
			SelectedBackColor = Color.FromArgb(232, 240, 254);
			MutedForeColor = Color.FromArgb(82, 94, 108);
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
		}

		protected override void OnDrawItem(DrawItemEventArgs eventArgs)
		{
			DrawModernTab(eventArgs.Graphics, eventArgs.Index);
		}

		private void DrawModernTab(Graphics graphics, int index)
		{
			bool selected = index == SelectedIndex;
			Rectangle tabBounds = GetTabRect(index);
			using (SolidBrush stripBrush = new SolidBrush(BackColor)) graphics.FillRectangle(stripBrush, tabBounds);
			Rectangle bounds = Rectangle.Inflate(tabBounds, -4, -4);
			using (GraphicsPath path = RoundedPanel.CreateRoundedRectangle(bounds, 10))
			using (SolidBrush brush = new SolidBrush(selected ? SelectedBackColor : BackColor))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.FillPath(brush, path);
			}
			TextRenderer.DrawText(graphics, TabPages[index].Text, Font, bounds, selected ? AccentColor : MutedForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
			if (selected && Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(bounds, -3, -3));
		}

		protected override void OnSelectedIndexChanged(EventArgs eventArgs)
		{
			base.OnSelectedIndexChanged(eventArgs);
			Invalidate();
		}

		protected override void WndProc(ref Message message)
		{
			base.WndProc(ref message);
			if ((message.Msg != 0x000F && message.Msg != 0x0318) || Width <= 1 || Height <= 1) return;
			using (Graphics graphics = Graphics.FromHwnd(Handle))
			using (SolidBrush stripBrush = new SolidBrush(BackColor))
			{
				int stripHeight = Math.Min(Height, ItemSize.Height + 10);
				graphics.FillRectangle(stripBrush, new Rectangle(0, 0, Width, stripHeight));
				for (int index = 0; index < TabPages.Count; index++) DrawModernTab(graphics, index);
			}
		}
	}

	private static RoundedPanel CreateModernTextBoxSurface(TextBox textBox, int cornerRadius)
	{
		if (textBox == null) throw new ArgumentNullException("textBox");
		RoundedPanel surface = new RoundedPanel
		{
			CornerRadius = Math.Max(6, cornerRadius),
			Padding = new Padding(10, 7, 10, 5),
			Tag = "input-surface"
		};
		textBox.BorderStyle = BorderStyle.None;
		textBox.Dock = DockStyle.Fill;
		surface.Controls.Add(textBox);
		ModernTextBox modernTextBox = textBox as ModernTextBox;
		if (modernTextBox != null)
		{
			Label cueLabel = new Label
			{
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true,
				BackColor = Color.Transparent,
				ForeColor = Color.FromArgb(128, 136, 146),
				Tag = "muted",
				Cursor = Cursors.IBeam,
				TabStop = false
			};
			Action refreshCue = delegate
			{
				cueLabel.Text = modernTextBox.CueText;
				cueLabel.Visible = modernTextBox.TextLength == 0 && !string.IsNullOrWhiteSpace(modernTextBox.CueText);
				if (cueLabel.Visible) cueLabel.BringToFront();
			};
			cueLabel.Click += delegate { modernTextBox.Focus(); };
			modernTextBox.TextChanged += delegate { refreshCue(); };
			modernTextBox.Enter += delegate { refreshCue(); };
			modernTextBox.Leave += delegate { refreshCue(); };
			modernTextBox.CueTextChanged += delegate { refreshCue(); };
			surface.Controls.Add(cueLabel);
			refreshCue();
		}
		return surface;
	}

	internal sealed class ModernMetricTable : TableLayoutPanel
	{
		private ThemePalette palette = ThemePalette.Create(false);

		public ModernMetricTable()
		{
			DoubleBuffered = true;
			CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
			Margin = Padding.Empty;
			Padding = new Padding(0, 4, 0, 4);
		}

		public void ApplyPalette(ThemePalette value)
		{
			palette = value;
			BackColor = value.Card;
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs eventArgs)
		{
			base.OnPaint(eventArgs);
			if (RowCount <= 1) return;
			using (Pen pen = new Pen(Color.FromArgb(76, palette.Border)))
			{
				for (int row = 1; row < RowCount; row++)
				{
					int y = Padding.Top + (ClientSize.Height - Padding.Vertical) * row / RowCount;
					eventArgs.Graphics.DrawLine(pen, 10, y, Math.Max(10, ClientSize.Width - 10), y);
				}
			}
		}
	}

	internal sealed class ModernSuggestionList : ListBox
	{
		public Color SelectionBackColor { get; set; }
		public Color BorderColor { get; set; }

		public ModernSuggestionList()
		{
			DrawMode = DrawMode.OwnerDrawFixed;
			ItemHeight = 30;
			IntegralHeight = false;
			BorderStyle = BorderStyle.FixedSingle;
			SelectionBackColor = Color.FromArgb(232, 240, 254);
			BorderColor = Color.FromArgb(220, 226, 232);
		}

		protected override void OnHandleCreated(EventArgs eventArgs)
		{
			base.OnHandleCreated(eventArgs);
			NativeControlTheme.Apply(this, false);
		}

		protected override void OnDrawItem(DrawItemEventArgs eventArgs)
		{
			if (eventArgs.Index < 0 || eventArgs.Index >= Items.Count) return;
			bool selected = (eventArgs.State & DrawItemState.Selected) != 0;
			using (SolidBrush brush = new SolidBrush(selected ? SelectionBackColor : BackColor)) eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
			Rectangle textBounds = new Rectangle(eventArgs.Bounds.Left + 10, eventArgs.Bounds.Top, Math.Max(1, eventArgs.Bounds.Width - 16), eventArgs.Bounds.Height);
			TextRenderer.DrawText(eventArgs.Graphics, Convert.ToString(Items[eventArgs.Index]), Font, textBounds, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
		}
	}

	internal sealed class InlineSuggestionController : IDisposable
	{
		private readonly Form owner;
		private readonly TextBox input;
		private readonly ModernSuggestionList list;
		private readonly Func<string, IEnumerable<string>> provider;
		private readonly bool showAbove;
		private bool applying;

		public InlineSuggestionController(Form ownerForm, TextBox inputBox, bool above, Func<string, IEnumerable<string>> valueProvider, string accessibleName, string accessibleDescription)
		{
			owner = ownerForm;
			input = inputBox;
			showAbove = above;
			provider = valueProvider;
			list = new ModernSuggestionList { Visible = false, TabStop = false, AccessibleName = accessibleName, AccessibleDescription = accessibleDescription };
			owner.Controls.Add(list);
			list.BringToFront();
			input.TextChanged += InputTextChanged;
			input.KeyDown += InputKeyDown;
			list.MouseClick += ListMouseClick;
			owner.Resize += OwnerResized;
			owner.Deactivate += OwnerDeactivated;
		}

		public ModernSuggestionList SuggestionList { get { return list; } }

		public void ApplyPalette(ThemePalette palette)
		{
			list.BackColor = palette.Card;
			list.ForeColor = palette.Text;
			list.SelectionBackColor = palette.AccentSoft;
			list.BorderColor = palette.Border;
			NativeControlTheme.Apply(list, palette.Window.GetBrightness() < 0.45F);
			list.Invalidate();
		}

		private void InputTextChanged(object sender, EventArgs eventArgs)
		{
			if (applying) return;
			RefreshSuggestions();
		}

		private void RefreshSuggestions()
		{
			IEnumerable<string> provided = provider == null ? null : provider(input.Text ?? string.Empty);
			string[] values = provided == null ? new string[0] : provided.Where(delegate(string value) { return !string.IsNullOrWhiteSpace(value); }).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
			list.BeginUpdate();
			try
			{
				list.Items.Clear();
				list.Items.AddRange(values.Cast<object>().ToArray());
				if (list.Items.Count > 0) list.SelectedIndex = 0;
			}
			finally { list.EndUpdate(); }
			list.Visible = values.Length > 0 && input.Focused;
			if (list.Visible) { PositionList(); list.BringToFront(); }
		}

		private void InputKeyDown(object sender, KeyEventArgs eventArgs)
		{
			if (!list.Visible || list.Items.Count == 0) return;
			if (eventArgs.KeyCode == Keys.Down || eventArgs.KeyCode == Keys.Up)
			{
				int delta = eventArgs.KeyCode == Keys.Down ? 1 : -1;
				list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + delta));
				eventArgs.SuppressKeyPress = true;
			}
			else if (eventArgs.KeyCode == Keys.Tab || eventArgs.KeyCode == Keys.Enter)
			{
				ApplySelected();
				eventArgs.SuppressKeyPress = true;
			}
			else if (eventArgs.KeyCode == Keys.Escape)
			{
				list.Visible = false;
				eventArgs.SuppressKeyPress = true;
			}
		}

		private void ListMouseClick(object sender, MouseEventArgs eventArgs) { ApplySelected(); }
		private void OwnerResized(object sender, EventArgs eventArgs) { if (list.Visible) PositionList(); }
		private void OwnerDeactivated(object sender, EventArgs eventArgs) { list.Visible = false; }

		private void ApplySelected()
		{
			if (list.SelectedItem == null) return;
			applying = true;
			try
			{
				input.Text = Convert.ToString(list.SelectedItem);
				input.SelectionStart = input.TextLength;
				list.Visible = false;
				input.Focus();
			}
			finally { applying = false; }
		}

		private void PositionList()
		{
			if (input.IsDisposed || !input.IsHandleCreated) return;
			Point inputTop = owner.PointToClient(input.PointToScreen(Point.Empty));
			int height = Math.Min(8, Math.Max(1, list.Items.Count)) * list.ItemHeight + 2;
			int y = showAbove ? inputTop.Y - height - 4 : inputTop.Y + input.Height + 4;
			list.Bounds = new Rectangle(inputTop.X, Math.Max(4, y), Math.Max(180, input.Width), height);
		}

		public void Dispose()
		{
			input.TextChanged -= InputTextChanged;
			input.KeyDown -= InputKeyDown;
			list.MouseClick -= ListMouseClick;
			owner.Resize -= OwnerResized;
			owner.Deactivate -= OwnerDeactivated;
			if (!list.IsDisposed) list.Dispose();
		}
	}

	private static string[] GetPlayerNameAutoCompleteCandidates(string input, IEnumerable<string> players)
	{
		string prefix = (input ?? string.Empty).Trim();
		if (players == null) return new string[0];
		return players.Where(delegate(string player) { return !string.IsNullOrWhiteSpace(player) && (prefix.Length == 0 || player.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)); }).OrderBy(delegate(string player) { return player; }, StringComparer.OrdinalIgnoreCase).ThenBy(delegate(string player) { return player; }, StringComparer.Ordinal).Take(8).ToArray();
	}

	private static string[] GetManagedCommandAutoCompleteCandidates(string input, IEnumerable<string> players)
	{
		string value = (input ?? string.Empty).TrimStart();
		string[] roots = { "list", "say ", "whitelist list", "whitelist add ", "whitelist remove ", "op ", "deop ", "kick ", "ban ", "pardon ", "gamemode survival ", "gamemode creative ", "save-all", "stop" };
		List<string> result = new List<string>();
		string[] playerRoots = { "whitelist add ", "whitelist remove ", "op ", "deop ", "kick ", "ban ", "pardon ", "gamemode survival ", "gamemode creative " };
		for (int i = 0; i < playerRoots.Length; i++)
		{
			if (!value.StartsWith(playerRoots[i], StringComparison.OrdinalIgnoreCase)) continue;
			string playerPrefix = value.Substring(playerRoots[i].Length);
			if (players != null)
			{
				foreach (string player in players.Where(delegate(string player) { return !string.IsNullOrWhiteSpace(player) && player.StartsWith(playerPrefix, StringComparison.OrdinalIgnoreCase); }).OrderBy(delegate(string player) { return player; }, StringComparer.OrdinalIgnoreCase).ThenBy(delegate(string player) { return player; }, StringComparer.Ordinal)) result.Add(playerRoots[i] + player);
			}
			return result.Take(8).ToArray();
		}
		for (int i = 0; i < roots.Length; i++) if (value.Length > 0 && roots[i].StartsWith(value, StringComparison.OrdinalIgnoreCase) && !string.Equals(roots[i].TrimEnd(), value.TrimEnd(), StringComparison.OrdinalIgnoreCase)) result.Add(roots[i]);
		return result.Take(8).ToArray();
	}

	private static void ApplyModernControlPalette(Control control, ThemePalette palette)
	{
		ModernCheckBox checkBox = control as ModernCheckBox;
		if (checkBox != null)
		{
			checkBox.AccentColor = palette.Accent;
			checkBox.BorderColor = palette.Border;
			checkBox.HoverBackColor = palette.AccentSoft;
			checkBox.BackColor = control.Parent == null ? palette.Window : control.Parent.BackColor;
			checkBox.ForeColor = palette.Text;
			checkBox.Invalidate();
		}
		ModernComboBox comboBox = control as ModernComboBox;
		if (comboBox != null)
		{
			comboBox.BackColor = palette.CardSecondary;
			comboBox.ForeColor = palette.Text;
			comboBox.SelectionBackColor = palette.AccentSoft;
			comboBox.SelectionForeColor = palette.Text;
			comboBox.BorderColor = palette.Border;
			comboBox.ArrowColor = palette.Muted;
			comboBox.FocusBorderColor = palette.Accent;
			comboBox.UseDarkNativeTheme = palette.Window.GetBrightness() < 0.45F;
			NativeControlTheme.Apply(comboBox, comboBox.UseDarkNativeTheme);
			comboBox.Invalidate();
		}
		BufferedListView listView = control as BufferedListView;
		if (listView != null) listView.ApplyPalette(palette);
		ModernTabControl tabControl = control as ModernTabControl;
		if (tabControl != null)
		{
			tabControl.BackColor = palette.Window;
			tabControl.ForeColor = palette.Text;
			tabControl.AccentColor = palette.Accent;
			tabControl.SelectedBackColor = palette.AccentSoft;
			tabControl.MutedForeColor = palette.Muted;
			tabControl.Invalidate();
		}
		ModernMetricTable metricTable = control as ModernMetricTable;
		if (metricTable != null) metricTable.ApplyPalette(palette);
		ModernSuggestionList suggestionList = control as ModernSuggestionList;
		if (suggestionList != null)
		{
			suggestionList.BackColor = palette.Card;
			suggestionList.ForeColor = palette.Text;
			suggestionList.SelectionBackColor = palette.AccentSoft;
			suggestionList.BorderColor = palette.Border;
			NativeControlTheme.Apply(suggestionList, palette.Window.GetBrightness() < 0.45F);
			suggestionList.Invalidate();
		}
	}
}
