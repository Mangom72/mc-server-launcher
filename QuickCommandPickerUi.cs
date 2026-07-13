using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

internal static partial class Launcher
{
	private sealed class QuickCommandPickerItem
	{
		public QuickCommandDefinition Definition;
		public string CategoryKey;
		public string CategoryName;
		public string GroupKey;
		public string GroupName;
		public string LeafName;
		public int Order;

		public string Path
		{
			get { return CategoryName + " › " + GroupName + " › " + LeafName; }
		}

		public override string ToString()
		{
			return LeafName;
		}
	}

	private sealed class QuickCommandPickerGroup
	{
		public string Key;
		public string Name;
		public int Count;

		public override string ToString()
		{
			return Name;
		}
	}

	private static List<QuickCommandPickerItem> BuildQuickCommandPickerItems(IEnumerable<QuickCommandDefinition> definitions, string serverType)
	{
		List<QuickCommandPickerItem> result = new List<QuickCommandPickerItem>();
		if (definitions == null)
		{
			return result;
		}
		int order = 0;
		foreach (QuickCommandDefinition definition in definitions)
		{
			if (definition == null || !QuickCommandSupportsServer(definition, serverType))
			{
				continue;
			}
			string category = NormalizeQuickCommandPickerCategory(definition.Category);
			string group = GetQuickCommandPickerGroupKey(definition, category);
			QuickCommandPickerItem item = new QuickCommandPickerItem();
			item.Definition = definition;
			item.CategoryKey = category;
			item.CategoryName = GetQuickCommandPickerCategoryName(category);
			item.GroupKey = group;
			item.GroupName = GetQuickCommandPickerGroupName(category, group);
			item.LeafName = GetQuickCommandPickerLeafName(definition, category, group);
			item.Order = order++;
			result.Add(item);
		}
		result.Sort(delegate(QuickCommandPickerItem left, QuickCommandPickerItem right)
		{
			int comparison = GetQuickCommandPickerCategoryOrder(left.CategoryKey).CompareTo(GetQuickCommandPickerCategoryOrder(right.CategoryKey));
			if (comparison != 0) return comparison;
			if (left.CategoryKey == "plugin")
			{
				comparison = string.Compare(left.GroupName, right.GroupName, StringComparison.CurrentCultureIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(left.LeafName, right.LeafName, StringComparison.CurrentCultureIgnoreCase);
				if (comparison != 0) return comparison;
			}
			comparison = GetQuickCommandPickerGroupOrder(left.CategoryKey, left.GroupKey).CompareTo(GetQuickCommandPickerGroupOrder(right.CategoryKey, right.GroupKey));
			if (comparison != 0) return comparison;
			return left.Order.CompareTo(right.Order);
		});
		return result;
	}

	private static string NormalizeQuickCommandPickerCategory(string category)
	{
		string raw = (category ?? string.Empty).Trim();
		if (raw.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase) && raw.Substring("plugin:".Length).Trim().Length > 0) return "plugin";
		string value = raw.ToLowerInvariant();
		return new string[] { "server", "player", "whitelist", "world", "plugin", "info", "user" }.Contains(value) ? value : "user";
	}

	private static string GetQuickCommandPickerCategoryName(string category)
	{
		if (category == "server") return LauncherUiText("서버 관리", "Server");
		if (category == "player") return LauncherUiText("플레이어", "Players");
		if (category == "whitelist") return LauncherUiText("화이트리스트", "Whitelist");
		if (category == "world") return LauncherUiText("월드", "World");
		if (category == "plugin") return LauncherUiText("플러그인", "Plugins");
		if (category == "info") return LauncherUiText("정보", "Information");
		return LauncherUiText("사용자 명령", "User commands");
	}

	private static string GetQuickCommandPickerGroupKey(QuickCommandDefinition definition, string category)
	{
		if (string.Equals(definition.Source, "user", StringComparison.OrdinalIgnoreCase) || category == "user") return "custom";
		if (category == "plugin") return "plugin:" + definition.Category.Substring(definition.Category.IndexOf(':') + 1).Trim();
		string command = NormalizeCommandForSend(definition.Template).ToLowerInvariant();
		if (category == "server")
		{
			if (command == "list" || command.StartsWith("list ", StringComparison.Ordinal)) return "status";
			if (command.StartsWith("save-", StringComparison.Ordinal)) return "save";
			if (command.StartsWith("say ", StringComparison.Ordinal)) return "broadcast";
			return "lifecycle";
		}
		if (category == "player")
		{
			if (command.StartsWith("gamemode ", StringComparison.Ordinal)) return "gamemode";
			if (command.StartsWith("tp ", StringComparison.Ordinal)) return "teleport";
			if (command.StartsWith("give ", StringComparison.Ordinal)) return "items";
			if (command.StartsWith("experience ", StringComparison.Ordinal)) return "experience";
			if (command.StartsWith("effect ", StringComparison.Ordinal)) return "effects";
			if (command.StartsWith("op ", StringComparison.Ordinal) || command.StartsWith("deop ", StringComparison.Ordinal)) return "permissions";
			return "moderation";
		}
		if (category == "whitelist")
		{
			if (command.StartsWith("whitelist add ", StringComparison.Ordinal) || command.StartsWith("whitelist remove ", StringComparison.Ordinal)) return "players";
			return "settings";
		}
		if (category == "world")
		{
			if (command.StartsWith("time ", StringComparison.Ordinal)) return "time";
			if (command.StartsWith("weather ", StringComparison.Ordinal)) return "weather";
			if (command.StartsWith("difficulty ", StringComparison.Ordinal)) return "difficulty";
			if (command.StartsWith("defaultgamemode ", StringComparison.Ordinal)) return "gamemode";
			if (command.StartsWith("gamerule ", StringComparison.Ordinal)) return "rules";
			return "spawn";
		}
		if (category == "info")
		{
			if (command == "help" || command.StartsWith("help ", StringComparison.Ordinal)) return "help";
			if (command == "version" || command == "plugins") return "server-info";
			return "content";
		}
		return "custom";
	}

	private static string GetQuickCommandPickerGroupName(string category, string group)
	{
		if (category == "plugin" && group.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase)) return group.Substring("plugin:".Length).Trim();
		if (group == "custom") return LauncherUiText("사용자 명령", "User commands");
		if (group == "status") return LauncherUiText("상태", "Status");
		if (group == "save") return LauncherUiText("저장", "Saving");
		if (group == "broadcast") return LauncherUiText("공지", "Broadcast");
		if (group == "lifecycle") return LauncherUiText("실행", "Lifecycle");
		if (group == "gamemode") return LauncherUiText(category == "world" ? "기본 게임 모드" : "게임 모드", category == "world" ? "Default game mode" : "Game mode");
		if (group == "teleport") return LauncherUiText("텔레포트", "Teleport");
		if (group == "items") return LauncherUiText("아이템", "Items");
		if (group == "experience") return LauncherUiText("경험치", "Experience");
		if (group == "effects") return LauncherUiText("효과", "Effects");
		if (group == "permissions") return LauncherUiText("권한", "Permissions");
		if (group == "moderation") return LauncherUiText("접속 관리", "Moderation");
		if (group == "players") return LauncherUiText("플레이어", "Players");
		if (group == "settings") return LauncherUiText("설정", "Settings");
		if (group == "time") return LauncherUiText("시간", "Time");
		if (group == "weather") return LauncherUiText("날씨", "Weather");
		if (group == "difficulty") return LauncherUiText("난이도", "Difficulty");
		if (group == "rules") return LauncherUiText("게임 규칙", "Game rules");
		if (group == "spawn") return LauncherUiText("스폰", "Spawn");
		if (group == "help") return LauncherUiText("도움말", "Help");
		if (group == "server-info") return LauncherUiText("서버 정보", "Server information");
		if (group == "content") return LauncherUiText("콘텐츠", "Content");
		return LauncherUiText("기타", "Other");
	}

	private static string GetQuickCommandPickerLeafName(QuickCommandDefinition definition, string category, string group)
	{
		string command = NormalizeCommandForSend(definition.Template);
		if (category == "plugin") return string.IsNullOrWhiteSpace(definition.Name) ? command : definition.Name;
		string[] parts = command.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		string value = parts.Length == 0 ? string.Empty : parts[parts.Length - 1].ToLowerInvariant();
		if (category == "world" && group == "time")
		{
			if (value == "day") return LauncherUiText("낮", "Day");
			if (value == "noon") return LauncherUiText("정오", "Noon");
			if (value == "night") return LauncherUiText("밤", "Night");
			if (value == "midnight") return LauncherUiText("자정", "Midnight");
		}
		if (category == "world" && group == "weather")
		{
			if (value == "clear") return LauncherUiText("맑음", "Clear");
			if (value == "rain") return LauncherUiText("비", "Rain");
			if (value == "thunder") return LauncherUiText("천둥", "Thunder");
		}
		if (category == "world" && group == "difficulty")
		{
			if (value == "peaceful") return LauncherUiText("평화로움", "Peaceful");
			if (value == "easy") return LauncherUiText("쉬움", "Easy");
			if (value == "normal") return LauncherUiText("보통", "Normal");
			if (value == "hard") return LauncherUiText("어려움", "Hard");
		}
		return string.IsNullOrWhiteSpace(definition.Name) ? command : definition.Name;
	}

	private static int GetQuickCommandPickerCategoryOrder(string category)
	{
		int index = Array.IndexOf(new string[] { "server", "player", "whitelist", "world", "plugin", "info", "user" }, category);
		return index < 0 ? 100 : index;
	}

	private static int GetQuickCommandPickerGroupOrder(string category, string group)
	{
		string[] order;
		if (category == "server") order = new string[] { "status", "save", "broadcast", "lifecycle", "custom" };
		else if (category == "player") order = new string[] { "gamemode", "teleport", "items", "experience", "effects", "permissions", "moderation", "custom" };
		else if (category == "whitelist") order = new string[] { "players", "settings", "custom" };
		else if (category == "world") order = new string[] { "time", "weather", "difficulty", "gamemode", "rules", "spawn", "custom" };
		else if (category == "plugin") order = new string[0];
		else if (category == "info") order = new string[] { "help", "server-info", "content", "custom" };
		else order = new string[] { "custom" };
		int index = Array.IndexOf(order, group);
		return index < 0 ? 100 : index;
	}

	private sealed class PickerListBox : ListBox
	{
		private const int WsVerticalScroll = 0x00200000;

		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams parameters = base.CreateParams;
				parameters.Style &= ~WsVerticalScroll;
				return parameters;
			}
		}
	}

	private sealed class RoundedPickerScrollBar : Control
	{
		private readonly ListBox target;
		private readonly ColumnStyle reservedColumn;
		private readonly Color trackColor;
		private readonly Color thumbColor;
		private readonly Color thumbHoverColor;
		private bool dragging;
		private bool hovering;
		private int dragOffset;

		public RoundedPickerScrollBar(ListBox targetList, ThemePalette palette, ColumnStyle scrollColumn)
		{
			target = targetList;
			reservedColumn = scrollColumn;
			bool dark = palette.Window.GetBrightness() < 0.5F;
			trackColor = SystemInformation.HighContrast ? palette.CardSecondary : (dark ? Color.FromArgb(25, 25, 30) : Color.FromArgb(228, 232, 237));
			thumbColor = SystemInformation.HighContrast ? palette.Border : (dark ? Color.FromArgb(92, 94, 103) : Color.FromArgb(151, 162, 175));
			thumbHoverColor = SystemInformation.HighContrast ? palette.Text : (dark ? Color.FromArgb(123, 126, 137) : Color.FromArgb(112, 127, 144));
			BackColor = palette.Card;
			Cursor = Cursors.Hand;
			TabStop = false;
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
			target.MouseWheel += TargetMouseWheel;
			target.SelectedIndexChanged += TargetSelectionChanged;
			target.Resize += TargetScrollChanged;
		}

		public void RefreshScrollState()
		{
			bool needed = GetMaximumTopIndex() > 0;
			if (Visible != needed) Visible = needed;
			float nextWidth = needed ? 18F : 0F;
			if (Math.Abs(reservedColumn.Width - nextWidth) > 0.1F)
			{
				reservedColumn.Width = nextWidth;
				if (Parent != null) Parent.PerformLayout();
			}
			Invalidate();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				target.MouseWheel -= TargetMouseWheel;
				target.SelectedIndexChanged -= TargetSelectionChanged;
				target.Resize -= TargetScrollChanged;
			}
			base.Dispose(disposing);
		}

		protected override void OnPaint(PaintEventArgs eventArgs)
		{
			base.OnPaint(eventArgs);
			Rectangle thumb = GetThumbBounds();
			if (thumb.IsEmpty) return;
			eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle track = GetTrackBounds();
			using (GraphicsPath trackPath = CreateRoundedScrollPath(track))
			using (SolidBrush trackBrush = new SolidBrush(trackColor))
			{
				eventArgs.Graphics.FillPath(trackBrush, trackPath);
			}
			using (GraphicsPath thumbPath = CreateRoundedScrollPath(thumb))
			using (SolidBrush thumbBrush = new SolidBrush(hovering || dragging ? thumbHoverColor : thumbColor))
			{
				eventArgs.Graphics.FillPath(thumbBrush, thumbPath);
			}
		}

		protected override void OnMouseDown(MouseEventArgs eventArgs)
		{
			base.OnMouseDown(eventArgs);
			if (eventArgs.Button != MouseButtons.Left) return;
			Rectangle thumb = GetThumbBounds();
			if (thumb.IsEmpty) return;
			if (thumb.Contains(eventArgs.Location))
			{
				dragging = true;
				dragOffset = eventArgs.Y - thumb.Top;
				Capture = true;
			}
			else
			{
				SetTopIndexFromThumbPosition(eventArgs.Y - thumb.Height / 2);
			}
			Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs eventArgs)
		{
			base.OnMouseMove(eventArgs);
			if (dragging)
			{
				SetTopIndexFromThumbPosition(eventArgs.Y - dragOffset);
				return;
			}
			bool nextHovering = GetThumbBounds().Contains(eventArgs.Location);
			if (hovering == nextHovering) return;
			hovering = nextHovering;
			Invalidate();
		}

		protected override void OnMouseUp(MouseEventArgs eventArgs)
		{
			base.OnMouseUp(eventArgs);
			if (eventArgs.Button != MouseButtons.Left) return;
			dragging = false;
			Capture = false;
			hovering = GetThumbBounds().Contains(eventArgs.Location);
			Invalidate();
		}

		protected override void OnMouseLeave(EventArgs eventArgs)
		{
			base.OnMouseLeave(eventArgs);
			if (dragging) return;
			hovering = false;
			Invalidate();
		}

		protected override void OnMouseWheel(MouseEventArgs eventArgs)
		{
			base.OnMouseWheel(eventArgs);
			int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
			SetTopIndex(target.TopIndex - Math.Sign(eventArgs.Delta) * lines);
		}

		protected override void OnMouseCaptureChanged(EventArgs eventArgs)
		{
			base.OnMouseCaptureChanged(eventArgs);
			if (Capture) return;
			dragging = false;
			Invalidate();
		}

		private void TargetMouseWheel(object sender, MouseEventArgs eventArgs)
		{
			int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
			SetTopIndex(target.TopIndex - Math.Sign(eventArgs.Delta) * lines);
		}

		private void TargetSelectionChanged(object sender, EventArgs eventArgs)
		{
			int selected = target.SelectedIndex;
			int visible = GetVisibleItemCount();
			if (selected >= 0 && selected < target.TopIndex) SetTopIndex(selected);
			else if (selected >= target.TopIndex + visible) SetTopIndex(selected - visible + 1);
			else Invalidate();
		}

		private void TargetScrollChanged(object sender, EventArgs eventArgs)
		{
			RefreshScrollState();
		}

		private Rectangle GetTrackBounds()
		{
			int height = Math.Max(1, ClientSize.Height - 8);
			return new Rectangle(Math.Max(0, (ClientSize.Width - 5) / 2), 4, 5, height);
		}

		private Rectangle GetThumbBounds()
		{
			int maximum = GetMaximumTopIndex();
			if (maximum <= 0 || ClientSize.Height <= 0) return Rectangle.Empty;
			Rectangle track = GetTrackBounds();
			int visible = GetVisibleItemCount();
			int thumbHeight = Math.Max(30, (int)Math.Round(track.Height * Math.Min(1D, (double)visible / target.Items.Count)));
			thumbHeight = Math.Min(track.Height, thumbHeight);
			int travel = Math.Max(0, track.Height - thumbHeight);
			int topIndex = Math.Max(0, Math.Min(maximum, target.TopIndex));
			int top = track.Top + (maximum == 0 ? 0 : (int)Math.Round(travel * (double)topIndex / maximum));
			return new Rectangle(Math.Max(0, (ClientSize.Width - 10) / 2), top, 10, thumbHeight);
		}

		private int GetVisibleItemCount()
		{
			return Math.Max(1, target.ClientSize.Height / Math.Max(1, target.ItemHeight));
		}

		private int GetMaximumTopIndex()
		{
			return Math.Max(0, target.Items.Count - GetVisibleItemCount());
		}

		private void SetTopIndexFromThumbPosition(int thumbTop)
		{
			Rectangle track = GetTrackBounds();
			Rectangle thumb = GetThumbBounds();
			int travel = Math.Max(0, track.Height - thumb.Height);
			int maximum = GetMaximumTopIndex();
			if (travel == 0 || maximum == 0) return;
			int position = Math.Max(0, Math.Min(travel, thumbTop - track.Top));
			SetTopIndex((int)Math.Round(maximum * (double)position / travel));
		}

		private void SetTopIndex(int value)
		{
			if (target.Items.Count == 0) return;
			int next = Math.Max(0, Math.Min(GetMaximumTopIndex(), value));
			if (target.TopIndex != next) target.TopIndex = next;
			Invalidate();
		}

		private static GraphicsPath CreateRoundedScrollPath(Rectangle bounds)
		{
			GraphicsPath path = new GraphicsPath();
			int diameter = Math.Max(1, Math.Min(bounds.Width, bounds.Height));
			if (diameter <= 1)
			{
				path.AddRectangle(bounds);
				return path;
			}
			path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
			path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
			path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
			path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
			path.CloseFigure();
			return path;
		}
	}

	private sealed class QuickCommandPickerForm : Form
	{
		private readonly List<QuickCommandPickerItem> allItems;
		private readonly TextBox searchBox;
		private readonly Label breadcrumbLabel;
		private readonly Label descriptionLabel;
		private readonly Label previewLabel;
		private readonly Label emptyLabel;
		private readonly ListBox categoryList;
		private readonly ListBox groupList;
		private readonly ListBox commandList;
		private readonly Button selectButton;
		private readonly Button cancelButton;
		private readonly List<RoundedPanel> columnPanels = new List<RoundedPanel>();
		private readonly List<RoundedPickerScrollBar> scrollBars = new List<RoundedPickerScrollBar>();
		private readonly ThemePalette palette;
		private bool updating;

		public QuickCommandDefinition SelectedCommand { get; private set; }

		public QuickCommandPickerForm(IEnumerable<QuickCommandDefinition> definitions, string serverType)
		{
			allItems = BuildQuickCommandPickerItems(definitions, serverType);
			bool dark = launcherForm != null && launcherForm.UsesDarkTheme;
			palette = ThemePalette.Create(dark);
			Text = LauncherUiText("빠른 명령 선택", "Choose a quick command");
			ApplyLauncherWindowIcon(this);
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(780, 540);
			Size = new Size(900, 630);
			AutoScaleMode = AutoScaleMode.Dpi;
			Font = new Font("Segoe UI Variable Text", 10F);
			KeyPreview = true;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(24, 20, 24, 18);
			root.ColumnCount = 1;
			root.RowCount = 6;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
			Controls.Add(root);

			Panel header = new Panel();
			header.Dock = DockStyle.Fill;
			root.Controls.Add(header, 0, 0);
			Label title = new Label();
			title.Text = LauncherUiText("원하는 명령을 단계별로 선택하세요", "Choose a command step by step");
			title.Font = new Font("Segoe UI Variable Display Semib", 18F);
			title.AutoSize = true;
			title.Location = new Point(0, 0);
			header.Controls.Add(title);
			Label subtitle = new Label();
			subtitle.Text = LauncherUiText("카테고리와 기능을 고르면 실행할 명령만 간결하게 표시됩니다.", "Pick a category and function to narrow the available commands.");
			subtitle.Tag = "muted";
			subtitle.AutoSize = true;
			subtitle.Location = new Point(2, 36);
			header.Controls.Add(subtitle);

			RoundedPanel searchPanel = new RoundedPanel();
			searchPanel.Dock = DockStyle.Fill;
			searchPanel.Padding = new Padding(14, 9, 14, 7);
			searchPanel.CornerRadius = 14;
			root.Controls.Add(searchPanel, 0, 1);
			Label searchLabel = new Label();
			searchLabel.Text = LauncherUiText("검색", "Search");
			searchLabel.Dock = DockStyle.Left;
			searchLabel.Width = 62;
			searchLabel.TextAlign = ContentAlignment.MiddleLeft;
			searchLabel.Font = new Font("Segoe UI Variable Text Semib", 9.5F);
			searchPanel.Controls.Add(searchLabel);
			searchBox = new TextBox();
			searchBox.BorderStyle = BorderStyle.None;
			searchBox.Dock = DockStyle.Fill;
			searchBox.Font = new Font("Segoe UI Variable Text", 10.5F);
			searchBox.TextChanged += delegate { RebuildCategories(); };
			searchBox.KeyDown += delegate(object sender, KeyEventArgs eventArgs)
			{
				if (eventArgs.KeyCode == Keys.Down)
				{
					categoryList.Focus();
					eventArgs.Handled = true;
				}
			};
			searchPanel.Controls.Add(searchBox);
			searchBox.BringToFront();

			breadcrumbLabel = new Label();
			breadcrumbLabel.Dock = DockStyle.Fill;
			breadcrumbLabel.TextAlign = ContentAlignment.MiddleLeft;
			breadcrumbLabel.Font = new Font("Segoe UI Variable Text Semib", 10F);
			breadcrumbLabel.AutoEllipsis = true;
			root.Controls.Add(breadcrumbLabel, 0, 2);

			TableLayoutPanel body = new TableLayoutPanel();
			body.Dock = DockStyle.Fill;
			body.ColumnCount = 3;
			body.RowCount = 1;
			body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
			body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27F));
			body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F));
			root.Controls.Add(body, 0, 3);

			categoryList = CreatePickerList(46);
			groupList = CreatePickerList(46);
			commandList = CreatePickerList(64);
			body.Controls.Add(CreatePickerColumn(LauncherUiText("1  카테고리", "1  Category"), categoryList, new Padding(0, 0, 8, 0)), 0, 0);
			body.Controls.Add(CreatePickerColumn(LauncherUiText("2  기능", "2  Function"), groupList, new Padding(4, 0, 4, 0)), 1, 0);
			body.Controls.Add(CreatePickerColumn(LauncherUiText("3  명령", "3  Command"), commandList, new Padding(8, 0, 0, 0)), 2, 0);

			categoryList.SelectedIndexChanged += delegate { if (!updating) RebuildGroups(); };
			groupList.SelectedIndexChanged += delegate { if (!updating) RebuildCommands(); };
			commandList.SelectedIndexChanged += delegate { UpdateSelectionDetails(); };
			commandList.DoubleClick += delegate { AcceptSelection(); };
			categoryList.KeyDown += delegate(object sender, KeyEventArgs eventArgs) { HandlePickerListKey(eventArgs, null, groupList, false); };
			groupList.KeyDown += delegate(object sender, KeyEventArgs eventArgs) { HandlePickerListKey(eventArgs, categoryList, commandList, false); };
			commandList.KeyDown += delegate(object sender, KeyEventArgs eventArgs) { HandlePickerListKey(eventArgs, groupList, null, true); };

			Panel details = new Panel();
			details.Dock = DockStyle.Fill;
			details.Padding = new Padding(2, 10, 2, 4);
			root.Controls.Add(details, 0, 4);
			descriptionLabel = new Label();
			descriptionLabel.Dock = DockStyle.Top;
			descriptionLabel.Height = 26;
			descriptionLabel.AutoEllipsis = true;
			details.Controls.Add(descriptionLabel);
			previewLabel = new Label();
			previewLabel.Dock = DockStyle.Top;
			previewLabel.Height = 26;
			previewLabel.Font = new Font("Consolas", 9.5F);
			previewLabel.AutoEllipsis = true;
			details.Controls.Add(previewLabel);
			previewLabel.BringToFront();
			emptyLabel = new Label();
			emptyLabel.Text = LauncherUiText("일치하는 명령이 없습니다. 다른 검색어를 입력해 보세요.", "No matching commands. Try another search.");
			emptyLabel.Dock = DockStyle.Fill;
			emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
			emptyLabel.Visible = false;
			emptyLabel.Tag = "muted";
			body.Controls.Add(emptyLabel, 0, 0);
			body.SetColumnSpan(emptyLabel, 3);
			emptyLabel.BringToFront();

			FlowLayoutPanel actions = new FlowLayoutPanel();
			actions.Dock = DockStyle.Fill;
			actions.FlowDirection = FlowDirection.RightToLeft;
			actions.WrapContents = false;
			actions.Padding = new Padding(0, 6, 0, 0);
			root.Controls.Add(actions, 0, 5);
			selectButton = new RoundedButton();
			selectButton.Text = LauncherUiText("이 명령 선택", "Choose command");
			selectButton.Tag = "primary";
			selectButton.Size = new Size(132, 40);
			selectButton.Margin = new Padding(8, 0, 0, 0);
			selectButton.Click += delegate { AcceptSelection(); };
			actions.Controls.Add(selectButton);
			cancelButton = new RoundedButton();
			cancelButton.Text = LauncherUiText("닫기", "Close");
			cancelButton.Tag = "secondary";
			cancelButton.Size = new Size(92, 40);
			cancelButton.Margin = new Padding(0);
			cancelButton.DialogResult = DialogResult.Cancel;
			actions.Controls.Add(cancelButton);

			AcceptButton = selectButton;
			CancelButton = cancelButton;
			ApplySimpleDialogTheme(this);
			ApplyPickerTheme(searchPanel);
			ConfigureAccessibleField(searchBox, LauncherUiText("빠른 명령 검색", "Search quick commands"), LauncherUiText("명령 이름, 경로 또는 실제 명령으로 검색합니다.", "Search by command name, path, or command text."));
			ConfigureAccessibleField(categoryList, LauncherUiText("명령 카테고리", "Command categories"), LauncherUiText("첫 번째 단계입니다. 오른쪽 화살표로 기능 목록으로 이동합니다.", "First step. Press Right Arrow to move to functions."));
			ConfigureAccessibleField(groupList, LauncherUiText("명령 기능", "Command functions"), LauncherUiText("두 번째 단계입니다. 오른쪽 화살표로 명령 목록으로 이동합니다.", "Second step. Press Right Arrow to move to commands."));
			ConfigureAccessibleField(commandList, LauncherUiText("실행할 명령", "Commands to run"), LauncherUiText("세 번째 단계입니다. 엔터로 선택합니다.", "Third step. Press Enter to choose."));
			RebuildCategories();
			Shown += delegate { searchBox.Focus(); };
		}

		protected override bool ProcessCmdKey(ref Message message, Keys keyData)
		{
			if (keyData == (Keys.Control | Keys.F))
			{
				searchBox.Focus();
				searchBox.SelectAll();
				return true;
			}
			return base.ProcessCmdKey(ref message, keyData);
		}

		private ListBox CreatePickerList(int itemHeight)
		{
			ListBox list = new PickerListBox();
			list.Dock = DockStyle.Fill;
			list.BorderStyle = BorderStyle.None;
			list.IntegralHeight = false;
			list.DrawMode = DrawMode.OwnerDrawFixed;
			list.ItemHeight = itemHeight;
			list.DrawItem += DrawPickerItem;
			return list;
		}

		private Control CreatePickerColumn(string title, ListBox list, Padding margin)
		{
			RoundedPanel card = new RoundedPanel();
			card.Dock = DockStyle.Fill;
			card.Margin = margin;
			card.Padding = new Padding(12, 10, 12, 12);
			card.CornerRadius = 16;
			columnPanels.Add(card);
			TableLayoutPanel layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.ColumnCount = 1;
			layout.RowCount = 2;
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			card.Controls.Add(layout);
			Label label = new Label();
			label.Text = title;
			label.Dock = DockStyle.Fill;
			label.Font = new Font("Segoe UI Variable Text Semib", 10F);
			layout.Controls.Add(label, 0, 0);
			TableLayoutPanel listLayout = new TableLayoutPanel();
			listLayout.Dock = DockStyle.Fill;
			listLayout.Margin = new Padding(0);
			listLayout.Padding = new Padding(0);
			listLayout.ColumnCount = 2;
			listLayout.RowCount = 1;
			listLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			ColumnStyle scrollColumn = new ColumnStyle(SizeType.Absolute, 0F);
			listLayout.ColumnStyles.Add(scrollColumn);
			RoundedPickerScrollBar scrollBar = new RoundedPickerScrollBar(list, palette, scrollColumn);
			scrollBar.Dock = DockStyle.Fill;
			scrollBar.Margin = new Padding(0);
			scrollBars.Add(scrollBar);
			listLayout.Controls.Add(list, 0, 0);
			listLayout.Controls.Add(scrollBar, 1, 0);
			layout.Controls.Add(listLayout, 0, 1);
			return card;
		}

		private void ApplyPickerTheme(RoundedPanel searchPanel)
		{
			BackColor = palette.Window;
			ForeColor = palette.Text;
			ApplyPickerSurfaceBackColor(searchPanel, palette.CardSecondary);
			searchBox.BackColor = palette.CardSecondary;
			searchBox.ForeColor = palette.Text;
			breadcrumbLabel.ForeColor = palette.Accent;
			descriptionLabel.ForeColor = palette.Text;
			previewLabel.ForeColor = palette.Muted;
			emptyLabel.ForeColor = palette.Muted;
			foreach (RoundedPanel panel in columnPanels) ApplyPickerSurfaceBackColor(panel, palette.Card);
			foreach (ListBox list in new ListBox[] { categoryList, groupList, commandList })
			{
				list.BackColor = palette.Card;
				list.ForeColor = palette.Text;
			}
			selectButton.BackColor = palette.Accent;
			selectButton.ForeColor = Color.White;
			cancelButton.BackColor = palette.CardSecondary;
			cancelButton.ForeColor = palette.Text;
			EnsureButtonContentFits(selectButton);
			EnsureButtonContentFits(cancelButton);
		}

		private static void ApplyPickerSurfaceBackColor(Control control, Color backColor)
		{
			control.BackColor = backColor;
			foreach (Control child in control.Controls)
			{
				if (child is Button || child is ListBox || child is TextBox) continue;
				ApplyPickerSurfaceBackColor(child, backColor);
			}
		}

		private List<QuickCommandPickerItem> GetFilteredItems()
		{
			string query = searchBox.Text.Trim();
			if (query.Length == 0) return allItems.ToList();
			return allItems.Where(delegate(QuickCommandPickerItem item)
			{
				string searchable = item.Path + " " + item.Definition.Name + " " + item.Definition.Description + " " + item.Definition.Template;
				return searchable.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
			}).ToList();
		}

		private void RebuildCategories()
		{
			string previous = GetSelectedKey(categoryList);
			List<QuickCommandPickerItem> filtered = GetFilteredItems();
			List<QuickCommandPickerGroup> categories = new List<QuickCommandPickerGroup>();
			foreach (QuickCommandPickerItem item in filtered)
			{
				QuickCommandPickerGroup existing = categories.FirstOrDefault(delegate(QuickCommandPickerGroup value) { return value.Key == item.CategoryKey; });
				if (existing == null)
				{
					existing = new QuickCommandPickerGroup { Key = item.CategoryKey, Name = item.CategoryName };
					categories.Add(existing);
				}
				existing.Count++;
			}
			updating = true;
			categoryList.Items.Clear();
			foreach (QuickCommandPickerGroup category in categories) categoryList.Items.Add(category);
			SelectKey(categoryList, previous);
			updating = false;
			emptyLabel.Visible = categories.Count == 0;
			RebuildGroups();
		}

		private void RebuildGroups()
		{
			string category = GetSelectedKey(categoryList);
			string previous = GetSelectedKey(groupList);
			List<QuickCommandPickerItem> filtered = GetFilteredItems().Where(delegate(QuickCommandPickerItem item) { return item.CategoryKey == category; }).ToList();
			List<QuickCommandPickerGroup> groups = new List<QuickCommandPickerGroup>();
			foreach (QuickCommandPickerItem item in filtered)
			{
				QuickCommandPickerGroup existing = groups.FirstOrDefault(delegate(QuickCommandPickerGroup value) { return value.Key == item.GroupKey; });
				if (existing == null)
				{
					existing = new QuickCommandPickerGroup { Key = item.GroupKey, Name = item.GroupName };
					groups.Add(existing);
				}
				existing.Count++;
			}
			updating = true;
			groupList.Items.Clear();
			foreach (QuickCommandPickerGroup group in groups) groupList.Items.Add(group);
			SelectKey(groupList, previous);
			updating = false;
			RebuildCommands();
		}

		private void RebuildCommands()
		{
			string category = GetSelectedKey(categoryList);
			string group = GetSelectedKey(groupList);
			string previousTemplate = GetSelectedCommandTemplate();
			List<QuickCommandPickerItem> filtered = GetFilteredItems().Where(delegate(QuickCommandPickerItem item) { return item.CategoryKey == category && item.GroupKey == group; }).ToList();
			updating = true;
			commandList.Items.Clear();
			foreach (QuickCommandPickerItem item in filtered) commandList.Items.Add(item);
			int selected = -1;
			for (int i = 0; i < commandList.Items.Count; i++)
			{
				QuickCommandPickerItem item = commandList.Items[i] as QuickCommandPickerItem;
				if (item != null && item.Definition.Template == previousTemplate) { selected = i; break; }
			}
			commandList.SelectedIndex = selected >= 0 ? selected : (commandList.Items.Count > 0 ? 0 : -1);
			updating = false;
			UpdateSelectionDetails();
			foreach (RoundedPickerScrollBar scrollBar in scrollBars) scrollBar.RefreshScrollState();
		}

		private static string GetSelectedKey(ListBox list)
		{
			QuickCommandPickerGroup value = list.SelectedItem as QuickCommandPickerGroup;
			return value == null ? string.Empty : value.Key;
		}

		private static void SelectKey(ListBox list, string key)
		{
			int selected = -1;
			for (int i = 0; i < list.Items.Count; i++)
			{
				QuickCommandPickerGroup value = list.Items[i] as QuickCommandPickerGroup;
				if (value != null && value.Key == key) { selected = i; break; }
			}
			list.SelectedIndex = selected >= 0 ? selected : (list.Items.Count > 0 ? 0 : -1);
		}

		private string GetSelectedCommandTemplate()
		{
			QuickCommandPickerItem item = commandList.SelectedItem as QuickCommandPickerItem;
			return item == null ? string.Empty : item.Definition.Template;
		}

		private void UpdateSelectionDetails()
		{
			QuickCommandPickerItem item = commandList.SelectedItem as QuickCommandPickerItem;
			selectButton.Enabled = item != null;
			if (item == null)
			{
				breadcrumbLabel.Text = LauncherUiText("카테고리 › 기능 › 명령", "Category › Function › Command");
				descriptionLabel.Text = LauncherUiText("명령을 선택하면 설명이 표시됩니다.", "Select a command to see its description.");
				previewLabel.Text = string.Empty;
				return;
			}
			breadcrumbLabel.Text = item.Path;
			descriptionLabel.Text = (item.Definition.Confirm ? LauncherUiText("실행 전 확인 · ", "Confirmation required · ") : string.Empty) + item.Definition.Description;
			previewLabel.Text = "/" + NormalizeCommandForSend(item.Definition.Template);
		}

		private void AcceptSelection()
		{
			QuickCommandPickerItem item = commandList.SelectedItem as QuickCommandPickerItem;
			if (item == null) return;
			SelectedCommand = item.Definition;
			DialogResult = DialogResult.OK;
			Close();
		}

		private void HandlePickerListKey(KeyEventArgs eventArgs, Control previous, Control next, bool accept)
		{
			if (eventArgs.KeyCode == Keys.Left && previous != null)
			{
				previous.Focus();
				eventArgs.Handled = true;
				eventArgs.SuppressKeyPress = true;
			}
			else if ((eventArgs.KeyCode == Keys.Right || eventArgs.KeyCode == Keys.Enter) && next != null)
			{
				next.Focus();
				eventArgs.Handled = true;
				eventArgs.SuppressKeyPress = true;
			}
			else if (eventArgs.KeyCode == Keys.Enter && accept)
			{
				AcceptSelection();
				eventArgs.Handled = true;
				eventArgs.SuppressKeyPress = true;
			}
		}

		private void DrawPickerItem(object sender, DrawItemEventArgs eventArgs)
		{
			ListBox list = sender as ListBox;
			if (list == null || eventArgs.Index < 0 || eventArgs.Index >= list.Items.Count) return;
			bool selected = (eventArgs.State & DrawItemState.Selected) != 0;
			Color background = selected ? palette.AccentSoft : palette.Card;
			Color foreground = palette.Text;
			using (SolidBrush backgroundBrush = new SolidBrush(background)) eventArgs.Graphics.FillRectangle(backgroundBrush, eventArgs.Bounds);
			Rectangle bounds = new Rectangle(eventArgs.Bounds.Left + 10, eventArgs.Bounds.Top + 4, Math.Max(1, eventArgs.Bounds.Width - 20), Math.Max(1, eventArgs.Bounds.Height - 8));
			QuickCommandPickerGroup group = list.Items[eventArgs.Index] as QuickCommandPickerGroup;
			if (group != null)
			{
				string countText = group.Count.ToString();
				int countWidth = Math.Max(22, TextRenderer.MeasureText(eventArgs.Graphics, countText, Font, Size.Empty, TextFormatFlags.NoPadding).Width + 6);
				Rectangle countBounds = new Rectangle(bounds.Right - countWidth, bounds.Top, countWidth, bounds.Height);
				Rectangle textBounds = new Rectangle(bounds.Left, bounds.Top, Math.Max(1, bounds.Width - countWidth - 8), bounds.Height);
				TextRenderer.DrawText(eventArgs.Graphics, group.Name, Font, textBounds, foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
				TextRenderer.DrawText(eventArgs.Graphics, countText, Font, countBounds, palette.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
			}
			else
			{
				QuickCommandPickerItem item = list.Items[eventArgs.Index] as QuickCommandPickerItem;
				if (item != null)
				{
					Rectangle titleBounds = new Rectangle(bounds.Left, bounds.Top, bounds.Width, 25);
					Rectangle commandBounds = new Rectangle(bounds.Left, bounds.Top + 27, bounds.Width, Math.Max(1, bounds.Height - 27));
					using (Font titleFont = new Font(Font, FontStyle.Bold))
					{
						TextRenderer.DrawText(eventArgs.Graphics, item.LeafName, titleFont, titleBounds, item.Definition.Confirm ? palette.Warning : foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
					}
					TextRenderer.DrawText(eventArgs.Graphics, "/" + NormalizeCommandForSend(item.Definition.Template), Font, commandBounds, palette.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
				}
			}
			if ((eventArgs.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(eventArgs.Graphics, Rectangle.Inflate(eventArgs.Bounds, -2, -2), foreground, background);
		}
	}
}
