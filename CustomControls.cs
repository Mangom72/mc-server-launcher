using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static partial class Launcher
{
	internal sealed class RoundedPanel : Panel
	{
		public Color BorderColor { get; set; }
		public int CornerRadius { get; set; }

		private Size lastSize;
		private GraphicsPath cachedPaintPath = new GraphicsPath();
		private Size lastPaintSize;
		private int lastCornerRadius;

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
			if (Width > 0 && Height > 0 && lastSize != Size)
			{
				lastSize = Size;
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
			if (lastPaintSize != rectangle.Size || lastCornerRadius != CornerRadius)
			{
				SetRoundedRectangle(cachedPaintPath, rectangle, CornerRadius);
				lastPaintSize = rectangle.Size;
				lastCornerRadius = CornerRadius;
			}
			using (SolidBrush brush = new SolidBrush(BackColor))
			using (Pen pen = new Pen(BorderColor))
			{
				eventArgs.Graphics.FillPath(brush, cachedPaintPath);
				eventArgs.Graphics.DrawPath(pen, cachedPaintPath);
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (cachedPaintPath != null)
				{
					cachedPaintPath.Dispose();
					cachedPaintPath = null;
				}
			}
			base.Dispose(disposing);
		}

		internal static void SetRoundedRectangle(GraphicsPath path, Rectangle bounds, int radius)
		{
			path.Reset();
			int diameter = radius * 2;
			if (diameter > 0)
			{
				path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
				path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
				path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
				path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
			}
			else
			{
				path.AddRectangle(bounds);
			}
			path.CloseFigure();
		}

		internal static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
		{
			GraphicsPath path = new GraphicsPath();
			SetRoundedRectangle(path, bounds, radius);
			return path;
		}
	}

	internal enum ButtonIcon
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

	internal sealed class RoundedButton : Button
	{
		private bool mouseOver;
		private bool mouseDown;
		public ButtonIcon IconKind { get; set; }

		private Color currentFill = Color.Empty;
		private GraphicsPath cachedPaintPath = new GraphicsPath();
		private GraphicsPath cachedFocusPath = new GraphicsPath();
		private Size lastPaintSize;
		private readonly Timer animTimer;

		private static int MoveTowards(int current, int target, int step)
		{
			if (current < target) return Math.Min(current + step, target);
			if (current > target) return Math.Max(current - step, target);
			return current;
		}

		private Color GetTargetFill()
		{
			Color fill = BackColor;
			if (!Enabled) fill = ControlPaint.Light(BackColor, 0.12F);
			else if (mouseDown) fill = ControlPaint.Dark(BackColor, 0.08F);
			else if (mouseOver) fill = FlatAppearance.MouseOverBackColor.IsEmpty ? ControlPaint.Light(BackColor, 0.08F) : FlatAppearance.MouseOverBackColor;
			return fill;
		}

		public RoundedButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
			FlatStyle = FlatStyle.Flat;
			FlatAppearance.BorderSize = 0;
			Cursor = Cursors.Hand;
			ResizeRedraw = true;
			IconKind = ButtonIcon.None;
			animTimer = new Timer { Interval = 16 };
			animTimer.Tick += delegate
			{
				Color target = GetTargetFill();
				if (currentFill.IsEmpty) currentFill = target;
				if (currentFill == target)
				{
					animTimer.Stop();
					return;
				}
				int r = MoveTowards(currentFill.R, target.R, 15);
				int g = MoveTowards(currentFill.G, target.G, 15);
				int b = MoveTowards(currentFill.B, target.B, 15);
				int a = MoveTowards(currentFill.A, target.A, 15);
				currentFill = Color.FromArgb(a, r, g, b);
				Invalidate();
			};
		}

		protected override void OnBackColorChanged(EventArgs e)
		{
			base.OnBackColorChanged(e);
			currentFill = GetTargetFill();
			Invalidate();
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (cachedPaintPath != null)
			{
				cachedPaintPath.Dispose();
				cachedPaintPath = null;
			}
			if (cachedFocusPath != null)
			{
				cachedFocusPath.Dispose();
				cachedFocusPath = null;
			}
			Invalidate();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				animTimer.Stop();
				animTimer.Dispose();
				if (cachedPaintPath != null)
				{
					cachedPaintPath.Dispose();
					cachedPaintPath = null;
				}
				if (cachedFocusPath != null)
				{
					cachedFocusPath.Dispose();
					cachedFocusPath = null;
				}
			}
			base.Dispose(disposing);
		}

		protected override void OnPaintBackground(PaintEventArgs eventArgs)
		{
			eventArgs.Graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
		}

		protected override void OnMouseEnter(EventArgs eventArgs)
		{
			mouseOver = true;
			animTimer.Start();
			base.OnMouseEnter(eventArgs);
		}

		protected override void OnMouseLeave(EventArgs eventArgs)
		{
			mouseOver = false;
			mouseDown = false;
			animTimer.Start();
			base.OnMouseLeave(eventArgs);
		}

		protected override void OnMouseDown(MouseEventArgs eventArgs)
		{
			mouseDown = true;
			animTimer.Start();
			base.OnMouseDown(eventArgs);
		}

		protected override void OnMouseUp(MouseEventArgs eventArgs)
		{
			mouseDown = false;
			animTimer.Start();
			base.OnMouseUp(eventArgs);
		}

		protected override void OnEnabledChanged(EventArgs eventArgs)
		{
			Cursor = Enabled ? Cursors.Hand : Cursors.Default;
			animTimer.Start();
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
			
			if (currentFill.IsEmpty) currentFill = GetTargetFill();
			Color fill = currentFill;
			if (cachedPaintPath == null) cachedPaintPath = new GraphicsPath();
			if (cachedFocusPath == null) cachedFocusPath = new GraphicsPath();
			Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
			if (lastPaintSize != bounds.Size)
			{
				RoundedPanel.SetRoundedRectangle(cachedPaintPath, bounds, Math.Min(14, Height / 2));
				Rectangle focusBounds = new Rectangle(3, 3, Math.Max(1, Width - 7), Math.Max(1, Height - 7));
				RoundedPanel.SetRoundedRectangle(cachedFocusPath, focusBounds, Math.Min(11, Height / 2));
				lastPaintSize = bounds.Size;
			}
			using (SolidBrush brush = new SolidBrush(fill))
			{
				eventArgs.Graphics.FillPath(brush, cachedPaintPath);
				string role = Convert.ToString(Tag);
				if (string.Equals(role, "secondary", StringComparison.Ordinal) || string.Equals(role, "ghost", StringComparison.Ordinal))
				{
					Color border = FlatAppearance.BorderColor.IsEmpty ? Color.FromArgb(100, ForeColor) : FlatAppearance.BorderColor;
					using (Pen borderPen = new Pen(border, 1F)) eventArgs.Graphics.DrawPath(borderPen, cachedPaintPath);
				}
			}
			Color contentColor = Enabled ? ForeColor : Color.FromArgb(135, ForeColor);
			DrawButtonContent(eventArgs.Graphics, bounds, contentColor);
			if (Focused && ShowFocusCues)
			{
				using (Pen focusPen = new Pen(SystemColors.Highlight, 2F))
				{
					focusPen.DashStyle = DashStyle.Dot;
					eventArgs.Graphics.DrawPath(focusPen, cachedFocusPath);
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

	internal sealed class RoundedPresetButton : RadioButton
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
}
