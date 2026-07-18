using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static partial class Launcher
{
	private static bool ToolUsesEnglish
	{
		get
		{
			return string.Equals(Localization.CurrentLanguage, Localization.English, StringComparison.OrdinalIgnoreCase);
		}
	}

	private static string ToolText(string korean, string english)
	{
		return ToolUsesEnglish ? english : korean;
	}

	private static Label CreateToolLabel(string text, float size, bool bold)
	{
		Label label = new Label();
		label.Text = text;
		label.AutoSize = true;
		label.Font = new Font("Pretendard", size, bold ? FontStyle.Bold : FontStyle.Regular);
		return label;
	}

	private static Button CreateToolButton(string text, string role)
	{
		Button button = new RoundedButton();
		button.Text = text;
		button.Height = 42;
		button.Dock = DockStyle.Fill;
		button.Margin = new Padding(5);
		button.Tag = role;
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderSize = 0;
		return button;
	}

	private static TextBox CreateToolValueBox()
	{
		TextBox box = new TextBox();
		box.ReadOnly = true;
		box.BorderStyle = BorderStyle.None;
		box.Font = new Font("Pretendard", 11F, FontStyle.Bold);
		box.TabStop = false;
		return box;
	}

	private static void ApplyToolTheme(Control parent, ThemePalette palette)
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
			else if (control is TextBox || control is RichTextBox)
			{
				control.BackColor = palette.CardSecondary;
				control.ForeColor = palette.Text;
			}
			else
			{
				string role = Convert.ToString(control.Tag);
				control.BackColor = control.Parent == null ? palette.Window : control.Parent.BackColor;
				if (string.Equals(role, "muted", StringComparison.Ordinal))
				{
					control.ForeColor = palette.Muted;
				}
				else if (string.Equals(role, "warning", StringComparison.Ordinal))
				{
					control.ForeColor = palette.Warning;
				}
				else if (string.Equals(role, "danger-text", StringComparison.Ordinal))
				{
					control.ForeColor = palette.Danger;
				}
				else
				{
					control.ForeColor = palette.Text;
				}
			}
			ApplyToolTheme(control, palette);
		}
	}

	private sealed class PlayerManagementForm : Form
	{
		private readonly Func<string, bool> sendCommand;
		private readonly ThemePalette palette;
		private readonly Label headingLabel;
		private readonly Label descriptionLabel;
		private readonly Label authenticationWarningLabel;
		private readonly Label playerNameLabel;
		private readonly Label playerNameHintLabel;
		private readonly TextBox playerNameBox;
		private readonly Label actionsLabel;
		private readonly Label statusLabel;
		private readonly Button whitelistAddButton;
		private readonly Button whitelistRemoveButton;
		private readonly Button opButton;
		private readonly Button deopButton;
		private readonly Button kickButton;
		private readonly Button banButton;
		private readonly Button pardonButton;
		private readonly Button closeButton;

		public PlayerManagementForm(Func<string, bool> commandSender)
		{
			ApplyLauncherWindowIcon(this);
			if (commandSender == null)
			{
				throw new ArgumentNullException("commandSender");
			}
			sendCommand = commandSender;
			bool dark = launcherForm != null && launcherForm.UsesDarkTheme;
			palette = ThemePalette.Create(dark);

			Text = ToolText("플레이어 관리", "Player management");
			Font = new Font("Pretendard", 11F);
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			AutoScaleMode = AutoScaleMode.Dpi;
			ClientSize = new Size(736, 620);
			BackColor = palette.Window;
			ForeColor = palette.Text;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(24, 20, 24, 18);
			root.ColumnCount = 1;
			root.RowCount = 6;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
			Controls.Add(root);

			Panel header = new Panel();
			header.Dock = DockStyle.Fill;
			headingLabel = CreateToolLabel(string.Empty, 21F, true);
			headingLabel.Location = new Point(2, 0);
			header.Controls.Add(headingLabel);
			descriptionLabel = CreateToolLabel(string.Empty, 9.5F, false);
			descriptionLabel.Tag = "muted";
			descriptionLabel.Location = new Point(4, 42);
			header.Controls.Add(descriptionLabel);
			root.Controls.Add(header, 0, 0);

			RoundedPanel warningCard = new RoundedPanel();
			warningCard.Dock = DockStyle.Fill;
			warningCard.Margin = new Padding(0, 0, 0, 12);
			warningCard.Padding = new Padding(18, 12, 18, 10);
			warningCard.CornerRadius = 17;
			warningCard.Tag = "surface";
			authenticationWarningLabel = CreateToolLabel(string.Empty, 9F, true);
			authenticationWarningLabel.Tag = "warning";
			authenticationWarningLabel.Dock = DockStyle.Fill;
			authenticationWarningLabel.AutoSize = false;
			authenticationWarningLabel.TextAlign = ContentAlignment.MiddleLeft;
			warningCard.Controls.Add(authenticationWarningLabel);
			root.Controls.Add(warningCard, 0, 1);

			RoundedPanel playerCard = new RoundedPanel();
			playerCard.Dock = DockStyle.Fill;
			playerCard.Margin = new Padding(0, 0, 0, 12);
			playerCard.Padding = new Padding(18, 13, 18, 12);
			playerCard.CornerRadius = 20;
			playerNameLabel = CreateToolLabel(string.Empty, 9F, true);
			playerNameLabel.Location = new Point(18, 13);
			playerCard.Controls.Add(playerNameLabel);
			playerNameBox = new TextBox();
			playerNameBox.Location = new Point(18, 40);
			playerNameBox.Size = new Size(300, 26);
			playerNameBox.MaxLength = 16;
			playerNameBox.Font = new Font("Pretendard", 11F);
			playerCard.Controls.Add(playerNameBox);
			playerNameHintLabel = CreateToolLabel(string.Empty, 8.5F, false);
			playerNameHintLabel.Tag = "muted";
			playerNameHintLabel.Location = new Point(336, 44);
			playerCard.Controls.Add(playerNameHintLabel);
			root.Controls.Add(playerCard, 0, 2);

			RoundedPanel actionsCard = new RoundedPanel();
			actionsCard.Dock = DockStyle.Fill;
			actionsCard.Margin = new Padding(0, 0, 0, 10);
			actionsCard.Padding = new Padding(14, 42, 14, 12);
			actionsCard.CornerRadius = 22;
			actionsLabel = CreateToolLabel(string.Empty, 11F, true);
			actionsLabel.Location = new Point(18, 14);
			actionsCard.Controls.Add(actionsLabel);
			TableLayoutPanel actionsGrid = new TableLayoutPanel();
			actionsGrid.Dock = DockStyle.Fill;
			actionsGrid.ColumnCount = 2;
			actionsGrid.RowCount = 4;
			actionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			actionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			for (int row = 0; row < 4; row++)
			{
				actionsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
			}
			actionsCard.Controls.Add(actionsGrid);

			whitelistAddButton = CreateToolButton(string.Empty, "primary");
			whitelistAddButton.Click += delegate { ConfirmAndSend("whitelist add", "whitelist-add"); };
			actionsGrid.Controls.Add(whitelistAddButton, 0, 0);
			whitelistRemoveButton = CreateToolButton(string.Empty, "secondary");
			whitelistRemoveButton.Click += delegate { ConfirmAndSend("whitelist remove", "whitelist-remove"); };
			actionsGrid.Controls.Add(whitelistRemoveButton, 1, 0);
			opButton = CreateToolButton(string.Empty, "primary");
			opButton.Click += delegate { ConfirmAndSend("op", "op"); };
			actionsGrid.Controls.Add(opButton, 0, 1);
			deopButton = CreateToolButton(string.Empty, "secondary");
			deopButton.Click += delegate { ConfirmAndSend("deop", "deop"); };
			actionsGrid.Controls.Add(deopButton, 1, 1);
			kickButton = CreateToolButton(string.Empty, "danger");
			kickButton.Click += delegate { ConfirmAndSend("kick", "kick"); };
			actionsGrid.Controls.Add(kickButton, 0, 2);
			banButton = CreateToolButton(string.Empty, "danger");
			banButton.Click += delegate { ConfirmAndSend("ban", "ban"); };
			actionsGrid.Controls.Add(banButton, 1, 2);
			pardonButton = CreateToolButton(string.Empty, "secondary");
			pardonButton.Click += delegate { ConfirmAndSend("pardon", "pardon"); };
			actionsGrid.Controls.Add(pardonButton, 0, 3);
			actionsGrid.SetColumnSpan(pardonButton, 2);
			root.Controls.Add(actionsCard, 0, 3);

			statusLabel = CreateToolLabel(string.Empty, 9F, false);
			statusLabel.Dock = DockStyle.Fill;
			statusLabel.AutoSize = false;
			statusLabel.Tag = "muted";
			statusLabel.TextAlign = ContentAlignment.MiddleLeft;
			root.Controls.Add(statusLabel, 0, 4);

			Panel footer = new Panel();
			footer.Dock = DockStyle.Fill;
			closeButton = CreateToolButton(string.Empty, "secondary");
			closeButton.Dock = DockStyle.None;
			closeButton.Size = new Size(112, 42);
			closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			closeButton.Location = new Point(footer.Width - closeButton.Width, 5);
			closeButton.DialogResult = DialogResult.Cancel;
			footer.Controls.Add(closeButton);
			footer.Resize += delegate { closeButton.Left = Math.Max(0, footer.ClientSize.Width - closeButton.Width); };
			root.Controls.Add(footer, 0, 5);
			CancelButton = closeButton;

			ApplyLanguage();
			ApplyToolTheme(this, palette);
			ApplyCommonButtonToolTips(this);
			authenticationWarningLabel.ForeColor = palette.Warning;
			statusLabel.ForeColor = palette.Muted;
			Shown += delegate { playerNameBox.Focus(); };
		}

		protected override void OnActivated(EventArgs eventArgs)
		{
			base.OnActivated(eventArgs);
			ApplyLanguage();
		}

		private void ApplyLanguage()
		{
			Text = ToolText("플레이어 관리", "Player management");
			headingLabel.Text = ToolText("플레이어를 간단하게 관리하세요", "Manage players without commands");
			descriptionLabel.Text = ToolText("닉네임을 입력하고 원하는 작업을 선택하세요.", "Enter a player name, then choose an action.");
			authenticationWarningLabel.Text = ToolText(
				"보안 안내 · online-mode=false에서는 다른 사람이 닉네임을 사칭할 수 있습니다. 온라인 인증을 켠 상태에서 사용하세요.",
				"Security · With online-mode=false, another person can impersonate a username. Keep online authentication enabled.");
			playerNameLabel.Text = ToolText("Minecraft 닉네임", "Minecraft username");
			playerNameHintLabel.Text = ToolText("영문, 숫자, 밑줄만 · 3~16자", "Letters, numbers, underscore · 3–16 characters");
			actionsLabel.Text = ToolText("작업 선택", "Choose an action");
			whitelistAddButton.Text = ToolText("화이트리스트 추가", "Add to whitelist");
			whitelistRemoveButton.Text = ToolText("화이트리스트 제거", "Remove from whitelist");
			opButton.Text = ToolText("OP 권한 주기", "Grant OP");
			deopButton.Text = ToolText("OP 권한 회수", "Revoke OP");
			kickButton.Text = ToolText("서버에서 내보내기", "Kick from server");
			banButton.Text = ToolText("접속 차단", "Ban player");
			pardonButton.Text = ToolText("접속 차단 해제", "Pardon player");
			closeButton.Text = ToolText("닫기", "Close");
			ApplyCommonButtonToolTips(this);
			if (string.IsNullOrEmpty(statusLabel.Text))
			{
				statusLabel.Text = ToolText("서버가 실행 중일 때 명령을 전송할 수 있습니다.", "Commands can be sent while the server is running.");
			}
		}

		private void ConfirmAndSend(string commandPrefix, string actionKey)
		{
			string playerName = playerNameBox.Text.Trim();
			if (!IsValidPlayerName(playerName))
			{
				ShowMineHarborDialog(this,
					ToolText("닉네임은 영문, 숫자, 밑줄만 사용해 3~16자로 입력해 주세요.", "Enter a 3–16 character username using only letters, numbers, and underscores."),
					ToolText("닉네임 확인", "Check username"),
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning);
				playerNameBox.Focus();
				return;
			}

			string command = commandPrefix + " " + playerName;
			bool destructive = actionKey == "whitelist-remove" || actionKey == "deop" || actionKey == "kick" || actionKey == "ban";
			string actionName = GetActionName(actionKey);
			DialogResult confirmation = ShowMineHarborDialog(this,
				ToolText(
					playerName + " 플레이어에게 '" + actionName + "' 작업을 실행할까요?\r\n\r\n전송 명령: " + command,
					"Run '" + actionName + "' for " + playerName + "?\r\n\r\nCommand: " + command),
				ToolText("작업 확인", "Confirm action"),
				MessageBoxButtons.YesNo,
				destructive ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
			if (confirmation != DialogResult.Yes)
			{
				return;
			}

			try
			{
				if (sendCommand(command))
				{
					statusLabel.ForeColor = palette.Success;
					statusLabel.Text = ToolText("명령을 서버에 전송했습니다: ", "Command sent: ") + command;
				}
				else
				{
					statusLabel.ForeColor = palette.Warning;
					statusLabel.Text = ToolText("서버가 실행 중인지 확인해 주세요. 명령을 전송하지 못했습니다.", "Make sure the server is running. The command was not sent.");
				}
			}
			catch (Exception exception)
			{
				statusLabel.ForeColor = palette.Danger;
				statusLabel.Text = ToolText("명령 전송 중 오류가 발생했습니다: ", "Could not send the command: ") + exception.Message;
			}
		}

		private static bool IsValidPlayerName(string value)
		{
			if (string.IsNullOrEmpty(value) || value.Length < 3 || value.Length > 16)
			{
				return false;
			}
			for (int index = 0; index < value.Length; index++)
			{
				char character = value[index];
				bool valid = (character >= 'a' && character <= 'z') ||
					(character >= 'A' && character <= 'Z') ||
					(character >= '0' && character <= '9') || character == '_';
				if (!valid)
				{
					return false;
				}
			}
			return true;
		}

		private static string GetActionName(string actionKey)
		{
			if (actionKey == "whitelist-add") return ToolText("화이트리스트 추가", "add to whitelist");
			if (actionKey == "whitelist-remove") return ToolText("화이트리스트 제거", "remove from whitelist");
			if (actionKey == "op") return ToolText("OP 권한 주기", "grant OP");
			if (actionKey == "deop") return ToolText("OP 권한 회수", "revoke OP");
			if (actionKey == "kick") return ToolText("서버에서 내보내기", "kick");
			if (actionKey == "ban") return ToolText("접속 차단", "ban");
			return ToolText("접속 차단 해제", "pardon");
		}
	}

	private sealed class NetworkToolsForm : Form
	{
		private const string PlayitOfficialUrl = "https://playit.gg/docs";
		private const string PublicIpLookupUrl = "https://portchecker.io/api/me";

		private readonly string serverDirectory;
		private readonly int serverPort;
		private readonly string javaPath;
		private readonly Action recheckExternalAccess;
		private readonly bool cgnatPossible;
		private readonly bool firewallCheckNeeded;
		private readonly string automaticResult;
		private readonly ThemePalette palette;
		private readonly Label headingLabel;
		private readonly Label descriptionLabel;
		private readonly Label safetyNoticeLabel;
		private readonly Label localIpv4TitleLabel;
		private readonly Label localIpv4ValueLabel;
		private readonly Label gatewayTitleLabel;
		private readonly Label gatewayValueLabel;
		private readonly Label publicIpTitleLabel;
		private readonly Label publicIpValueLabel;
		private readonly Label lanAddressTitleLabel;
		private readonly Label friendAddressTitleLabel;
		private readonly TextBox lanAddressBox;
		private readonly TextBox friendAddressBox;
		private readonly Button copyLanButton;
		private readonly Button copyFriendButton;
		private readonly Button refreshButton;
		private readonly Button recheckButton;
		private readonly Button copyGatewayButton;
		private readonly Button routerButton;
		private readonly Button clearUpnpButton;
		private readonly Button playitButton;
		private readonly Label guideTitleLabel;
		private readonly RichTextBox guideBox;
		private readonly Label statusLabel;
		private readonly Button closeButton;
		private NetworkDetails networkDetails;
		private string publicIpAddress;
		private bool publicIpLoading;
		private int publicIpRequestId;

		public NetworkToolsForm(string profileServerDirectory, int port, string selectedJavaPath, Action externalRecheckAction)
			: this(profileServerDirectory, port, selectedJavaPath, externalRecheckAction, null, false, true, null)
		{
		}

		public NetworkToolsForm(string profileServerDirectory, int port, string selectedJavaPath, Action externalRecheckAction, string initialPublicIp, bool detectedCgnat, bool needsFirewallCheck, string upnpResult)
		{
			ApplyLauncherWindowIcon(this);
			if (port < 1 || port > 65535)
			{
				throw new ArgumentOutOfRangeException("port");
			}
			serverDirectory = string.IsNullOrWhiteSpace(profileServerDirectory) ? ToolText("알 수 없음", "Unknown") : profileServerDirectory;
			serverPort = port;
			javaPath = string.IsNullOrWhiteSpace(selectedJavaPath) ? ToolText("알 수 없음", "Unknown") : selectedJavaPath;
			recheckExternalAccess = externalRecheckAction;
			publicIpAddress = initialPublicIp;
			cgnatPossible = detectedCgnat;
			firewallCheckNeeded = needsFirewallCheck;
			automaticResult = upnpResult;
			bool dark = launcherForm != null && launcherForm.UsesDarkTheme;
			palette = ThemePalette.Create(dark);

			Text = ToolText("네트워크와 외부 접속", "Network and external access");
			Font = new Font("Pretendard", 11F);
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			AutoScaleMode = AutoScaleMode.Dpi;
			Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
			Size = new Size(Math.Min(940, Math.Max(800, workingArea.Width - 80)), Math.Min(820, Math.Max(650, workingArea.Height - 80)));
			MinimumSize = new Size(800, 650);
			BackColor = palette.Window;
			ForeColor = palette.Text;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Padding = new Padding(24, 20, 24, 16);
			root.ColumnCount = 1;
			root.RowCount = 7;
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
			Controls.Add(root);

			Panel header = new Panel();
			header.Dock = DockStyle.Fill;
			headingLabel = CreateToolLabel(string.Empty, 21F, true);
			headingLabel.Location = new Point(2, 0);
			header.Controls.Add(headingLabel);
			descriptionLabel = CreateToolLabel(string.Empty, 9.5F, false);
			descriptionLabel.Tag = "muted";
			descriptionLabel.Location = new Point(4, 42);
			header.Controls.Add(descriptionLabel);
			root.Controls.Add(header, 0, 0);

			RoundedPanel safetyCard = new RoundedPanel();
			safetyCard.Dock = DockStyle.Fill;
			safetyCard.Margin = new Padding(0, 0, 0, 10);
			safetyCard.Padding = new Padding(18, 10, 18, 8);
			safetyCard.CornerRadius = 17;
			safetyCard.Tag = "surface";
			safetyNoticeLabel = CreateToolLabel(string.Empty, 9F, true);
			safetyNoticeLabel.Tag = "warning";
			safetyNoticeLabel.Dock = DockStyle.Fill;
			safetyNoticeLabel.AutoSize = false;
			safetyNoticeLabel.TextAlign = ContentAlignment.MiddleLeft;
			safetyCard.Controls.Add(safetyNoticeLabel);
			root.Controls.Add(safetyCard, 0, 1);

			RoundedPanel networkCard = new RoundedPanel();
			networkCard.Dock = DockStyle.Fill;
			networkCard.Margin = new Padding(0, 0, 0, 10);
			networkCard.Padding = new Padding(12);
			networkCard.CornerRadius = 21;
			TableLayoutPanel networkGrid = new TableLayoutPanel();
			networkGrid.Dock = DockStyle.Fill;
			networkGrid.ColumnCount = 3;
			networkGrid.RowCount = 2;
			networkGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
			networkGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
			networkGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
			networkGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
			networkGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
			networkCard.Controls.Add(networkGrid);
			localIpv4TitleLabel = CreateNetworkTitleLabel();
			gatewayTitleLabel = CreateNetworkTitleLabel();
			publicIpTitleLabel = CreateNetworkTitleLabel();
			localIpv4ValueLabel = CreateNetworkValueLabel();
			gatewayValueLabel = CreateNetworkValueLabel();
			publicIpValueLabel = CreateNetworkValueLabel();
			networkGrid.Controls.Add(localIpv4TitleLabel, 0, 0);
			networkGrid.Controls.Add(gatewayTitleLabel, 1, 0);
			networkGrid.Controls.Add(publicIpTitleLabel, 2, 0);
			networkGrid.Controls.Add(localIpv4ValueLabel, 0, 1);
			networkGrid.Controls.Add(gatewayValueLabel, 1, 1);
			networkGrid.Controls.Add(publicIpValueLabel, 2, 1);
			root.Controls.Add(networkCard, 0, 2);

			RoundedPanel addressCard = new RoundedPanel();
			addressCard.Dock = DockStyle.Fill;
			addressCard.Margin = new Padding(0, 0, 0, 10);
			addressCard.Padding = new Padding(18, 14, 18, 12);
			addressCard.CornerRadius = 21;
			TableLayoutPanel addressGrid = new TableLayoutPanel();
			addressGrid.Dock = DockStyle.Fill;
			addressGrid.ColumnCount = 2;
			addressGrid.RowCount = 2;
			addressGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			addressGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			addressGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
			addressGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
			addressCard.Controls.Add(addressGrid);
			lanAddressTitleLabel = CreateToolLabel(string.Empty, 9F, true);
			lanAddressTitleLabel.Dock = DockStyle.Top;
			friendAddressTitleLabel = CreateToolLabel(string.Empty, 9F, true);
			friendAddressTitleLabel.Dock = DockStyle.Top;
			addressGrid.Controls.Add(lanAddressTitleLabel, 0, 0);
			addressGrid.Controls.Add(friendAddressTitleLabel, 1, 0);
			Panel lanValuePanel = new Panel();
			lanValuePanel.Dock = DockStyle.Fill;
			lanValuePanel.Padding = new Padding(0, 8, 8, 0);
			lanAddressBox = CreateToolValueBox();
			lanAddressBox.Dock = DockStyle.Fill;
			copyLanButton = CreateToolButton(string.Empty, "secondary");
			copyLanButton.Dock = DockStyle.Right;
			copyLanButton.Width = 104;
			copyLanButton.Margin = new Padding(8, 0, 0, 0);
			copyLanButton.Click += delegate { CopyAddress(lanAddressBox.Text); };
			lanValuePanel.Controls.Add(lanAddressBox);
			lanValuePanel.Controls.Add(copyLanButton);
			addressGrid.Controls.Add(lanValuePanel, 0, 1);
			Panel friendValuePanel = new Panel();
			friendValuePanel.Dock = DockStyle.Fill;
			friendValuePanel.Padding = new Padding(8, 8, 0, 0);
			friendAddressBox = CreateToolValueBox();
			friendAddressBox.Dock = DockStyle.Fill;
			copyFriendButton = CreateToolButton(string.Empty, "primary");
			copyFriendButton.Dock = DockStyle.Right;
			copyFriendButton.Width = 104;
			copyFriendButton.Margin = new Padding(8, 0, 0, 0);
			copyFriendButton.Click += delegate { CopyAddress(friendAddressBox.Text); };
			friendValuePanel.Controls.Add(friendAddressBox);
			friendValuePanel.Controls.Add(copyFriendButton);
			addressGrid.Controls.Add(friendValuePanel, 1, 1);
			root.Controls.Add(addressCard, 0, 3);

			TableLayoutPanel actionGrid = new TableLayoutPanel();
			actionGrid.Dock = DockStyle.Fill;
			actionGrid.ColumnCount = 6;
			actionGrid.RowCount = 1;
			for (int column = 0; column < 6; column++)
			{
				actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
			}
			refreshButton = CreateToolButton(string.Empty, "secondary");
			refreshButton.Click += delegate { RefreshNetworkInformation(); };
			actionGrid.Controls.Add(refreshButton, 0, 0);
			recheckButton = CreateToolButton(string.Empty, "primary");
			recheckButton.Click += delegate { RequestExternalRecheck(); };
			recheckButton.Enabled = recheckExternalAccess != null;
			actionGrid.Controls.Add(recheckButton, 1, 0);
			copyGatewayButton = CreateToolButton(string.Empty, "secondary");
			copyGatewayButton.Click += delegate
			{
				CopyAddress(networkDetails == null ? null : networkDetails.Gateway);
			};
			actionGrid.Controls.Add(copyGatewayButton, 2, 0);
			routerButton = CreateToolButton(string.Empty, "secondary");
			routerButton.Click += delegate { OpenRouterPage(); };
			actionGrid.Controls.Add(routerButton, 3, 0);
			
			clearUpnpButton = CreateToolButton(string.Empty, "secondary");
			clearUpnpButton.Click += async delegate {
				clearUpnpButton.Enabled = false;
				statusLabel.ForeColor = palette.Muted;
				statusLabel.Text = ToolText("이전 실행에서 남은 UPnP 매핑을 확인하고 있습니다…", "Checking stale UPnP mappings from previous runs…");
				try
				{
					UpnpCleanupResult result = await ClearAllMineHarborUpnpMappingsAsync();
					if (result.TimedOut || !string.IsNullOrWhiteSpace(result.Error))
					{
						statusLabel.ForeColor = palette.Danger;
						statusLabel.Text = ToolText("UPnP 정리를 완료하지 못했습니다: ", "Could not complete UPnP cleanup: ") + result.Error;
						ShowMineHarborDialog(this, statusLabel.Text, ToolText("UPnP 정리", "Clear UPnP"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return;
					}
					statusLabel.ForeColor = palette.Success;
					statusLabel.Text = ToolText("이전 실행에서 남은 포트 매핑을 안전하게 정리했습니다: ", "Safely cleared stale mappings from previous runs: ") + result.ClearedCount;
					ShowMineHarborDialog(this, statusLabel.Text, ToolText("UPnP 정리", "Clear UPnP"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				finally { if (!IsDisposed) clearUpnpButton.Enabled = true; }
			};
			actionGrid.Controls.Add(clearUpnpButton, 4, 0);

			playitButton = CreateToolButton(string.Empty, "secondary");
			playitButton.Click += delegate { OpenWebPage(PlayitOfficialUrl); };
			actionGrid.Controls.Add(playitButton, 5, 0);
			root.Controls.Add(actionGrid, 0, 4);

			RoundedPanel guideCard = new RoundedPanel();
			guideCard.Dock = DockStyle.Fill;
			guideCard.Margin = new Padding(0, 4, 0, 8);
			guideCard.Padding = new Padding(16, 42, 16, 14);
			guideCard.CornerRadius = 21;
			guideTitleLabel = CreateToolLabel(string.Empty, 11F, true);
			guideTitleLabel.Location = new Point(18, 14);
			guideCard.Controls.Add(guideTitleLabel);
			guideBox = new RichTextBox();
			guideBox.Dock = DockStyle.Fill;
			guideBox.ReadOnly = true;
			guideBox.BorderStyle = BorderStyle.None;
			guideBox.DetectUrls = true;
			guideBox.Font = new Font("Pretendard", 11F);
			guideBox.LinkClicked += delegate(object sender, LinkClickedEventArgs eventArgs) { OpenWebPage(eventArgs.LinkText); };
			guideCard.Controls.Add(guideBox);
			root.Controls.Add(guideCard, 0, 5);

			Panel footer = new Panel();
			footer.Dock = DockStyle.Fill;
			statusLabel = CreateToolLabel(string.Empty, 9F, false);
			statusLabel.Tag = "muted";
			statusLabel.AutoSize = false;
			statusLabel.TextAlign = ContentAlignment.MiddleLeft;
			statusLabel.Location = new Point(2, 4);
			statusLabel.Size = new Size(footer.Width - 128, 42);
			statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			footer.Controls.Add(statusLabel);
			closeButton = CreateToolButton(string.Empty, "secondary");
			closeButton.Dock = DockStyle.None;
			closeButton.Size = new Size(112, 42);
			closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			closeButton.Location = new Point(footer.Width - closeButton.Width, 4);
			closeButton.DialogResult = DialogResult.Cancel;
			footer.Controls.Add(closeButton);
			footer.Resize += delegate
			{
				closeButton.Left = Math.Max(0, footer.ClientSize.Width - closeButton.Width);
				statusLabel.Width = Math.Max(100, closeButton.Left - 12);
			};
			root.Controls.Add(footer, 0, 6);
			CancelButton = closeButton;

			ApplyLanguage();
			ApplyToolTheme(this, palette);
			ApplyCommonButtonToolTips(this);
			safetyNoticeLabel.ForeColor = palette.Warning;
			statusLabel.ForeColor = palette.Muted;
			Shown += delegate { RefreshNetworkInformation(); };
			FormClosed += delegate { Interlocked.Increment(ref publicIpRequestId); };
		}

		protected override void OnActivated(EventArgs eventArgs)
		{
			base.OnActivated(eventArgs);
			ApplyLanguage();
		}

		private static Label CreateNetworkTitleLabel()
		{
			Label label = CreateToolLabel(string.Empty, 8.5F, true);
			label.Dock = DockStyle.Fill;
			label.AutoSize = false;
			label.Tag = "muted";
			label.TextAlign = ContentAlignment.BottomCenter;
			return label;
		}

		private static Label CreateNetworkValueLabel()
		{
			Label label = CreateToolLabel(string.Empty, 12F, true);
			label.Dock = DockStyle.Fill;
			label.AutoSize = false;
			label.TextAlign = ContentAlignment.TopCenter;
			return label;
		}

		private void ApplyLanguage()
		{
			Text = ToolText("네트워크와 외부 접속", "Network and external access");
			headingLabel.Text = ToolText("친구가 접속하는 데 필요한 정보", "Everything friends need to connect");
			descriptionLabel.Text = ToolText("LAN 주소와 외부 주소를 구분하고, 현재 PC에 맞는 설정값을 확인하세요.", "Keep LAN and public addresses separate, and review settings for this PC.");
			safetyNoticeLabel.Text = ToolText(
				"안전 안내 · 기존 외부 접속이 실패한 경우에만 서버 실행 중 UPnP를 시도하며, 이 화면은 설정을 변경하지 않습니다.",
				"Safety · UPnP is attempted only after existing external access fails; this screen does not change settings.");
			localIpv4TitleLabel.Text = ToolText("이 PC의 LAN IPv4", "This PC's LAN IPv4");
			gatewayTitleLabel.Text = ToolText("기본 게이트웨이", "Default gateway");
			publicIpTitleLabel.Text = ToolText("현재 공인 주소", "Current public address");
			lanAddressTitleLabel.Text = ToolText("같은 공유기 안에서 사용할 주소", "Address for the same home network");
			friendAddressTitleLabel.Text = ToolText("친구용 주소 · 포트 개방 확인 필요", "Address for friends · port check required");
			copyLanButton.Text = ToolText("주소 복사", "Copy");
			copyFriendButton.Text = ToolText("주소 복사", "Copy");
			refreshButton.Text = ToolText("정보 새로고침", "Refresh info");
			recheckButton.Text = ToolText("외부 접속 재검사", "Recheck access");
			copyGatewayButton.Text = ToolText("게이트웨이 복사", "Copy gateway");
			routerButton.Text = ToolText("공유기 설정 열기", "Open router page");
			clearUpnpButton.Text = ToolText("UPnP 정리", "Clear UPnP");
			playitButton.Text = ToolText("Playit.gg 연결", "Connect playit");
			guideTitleLabel.Text = ToolText("포트포워딩 안내", "Port-forwarding guide");
			closeButton.Text = ToolText("닫기", "Close");
			UpdateNetworkValues();
			UpdateGuideText();
			ApplyCommonButtonToolTips(this);
		}

		private void RefreshNetworkInformation()
		{
			networkDetails = GetNetworkDetails();
			publicIpAddress = null;
			publicIpLoading = true;
			int requestId = Interlocked.Increment(ref publicIpRequestId);
			UpdateNetworkValues();
			UpdateGuideText();
			statusLabel.ForeColor = palette.Muted;
			statusLabel.Text = ToolText("공인 주소를 확인하고 있습니다…", "Checking the public address…");

			Thread lookupThread = new Thread((ThreadStart)delegate
			{
				string result = null;
				bool failed = false;
				try
				{
					ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
					string candidate = DownloadText(PublicIpLookupUrl).Trim();
					IPAddress parsedAddress;
					if (!IPAddress.TryParse(candidate, out parsedAddress) || IPAddress.IsLoopback(parsedAddress))
					{
						throw new InvalidDataException("Invalid public address response.");
					}
					result = candidate;
				}
				catch
				{
					failed = true;
				}
				if (requestId != publicIpRequestId || IsDisposed || !IsHandleCreated)
				{
					return;
				}
				try
				{
					BeginInvoke((MethodInvoker)delegate
					{
						if (requestId != publicIpRequestId || IsDisposed)
						{
							return;
						}
						publicIpLoading = false;
						publicIpAddress = result;
						UpdateNetworkValues();
						UpdateGuideText();
						if (failed)
						{
							statusLabel.ForeColor = palette.Warning;
							statusLabel.Text = ToolText("공인 주소를 확인하지 못했습니다. 서버 실행에는 영향을 주지 않습니다.", "The public address could not be checked. Server operation is unaffected.");
						}
						else
						{
							statusLabel.ForeColor = palette.Muted;
							statusLabel.Text = ToolText("공인 주소만 확인했습니다. 실제 외부 접속 가능 여부는 재검사 버튼으로 확인하세요.", "Only the public address was found. Use Recheck access to verify reachability.");
						}
					});
				}
				catch (InvalidOperationException)
				{
					// 창이 닫히는 도중에는 결과 갱신을 생략합니다.
				}
			});
			lookupThread.IsBackground = true;
			lookupThread.Name = "공인 주소 확인";
			lookupThread.Start();
		}

		private void UpdateNetworkValues()
		{
			string unavailable = ToolText("확인되지 않음", "Unavailable");
			string loading = ToolText("확인 중…", "Checking…");
			string localIpv4 = networkDetails == null || string.IsNullOrWhiteSpace(networkDetails.LocalIpv4) ? null : networkDetails.LocalIpv4;
			string gateway = networkDetails == null || string.IsNullOrWhiteSpace(networkDetails.Gateway) ? null : networkDetails.Gateway;
			localIpv4ValueLabel.Text = localIpv4 ?? unavailable;
			gatewayValueLabel.Text = gateway ?? unavailable;
			publicIpValueLabel.Text = publicIpLoading ? loading : (string.IsNullOrWhiteSpace(publicIpAddress) ? unavailable : publicIpAddress);
			lanAddressBox.Text = string.IsNullOrWhiteSpace(localIpv4) ? unavailable : FormatHostPort(localIpv4, serverPort);
			friendAddressBox.Text = publicIpLoading ? loading : (string.IsNullOrWhiteSpace(publicIpAddress) ? unavailable : FormatHostPort(publicIpAddress, serverPort));
			copyLanButton.Enabled = !string.IsNullOrWhiteSpace(localIpv4);
			copyFriendButton.Enabled = !string.IsNullOrWhiteSpace(publicIpAddress) && !publicIpLoading;
			routerButton.Enabled = !string.IsNullOrWhiteSpace(gateway);
			copyGatewayButton.Enabled = !string.IsNullOrWhiteSpace(gateway);
		}

		private void UpdateGuideText()
		{
			if (guideBox == null)
			{
				return;
			}
			string unknown = ToolText("확인되지 않음", "Unavailable");
			string localIpv4 = networkDetails == null || string.IsNullOrWhiteSpace(networkDetails.LocalIpv4) ? unknown : networkDetails.LocalIpv4;
			string gateway = networkDetails == null || string.IsNullOrWhiteSpace(networkDetails.Gateway) ? unknown : networkDetails.Gateway;
			string macAddress = networkDetails == null || string.IsNullOrWhiteSpace(networkDetails.MacAddress) ? unknown : networkDetails.MacAddress;
			string publicAddress = string.IsNullOrWhiteSpace(publicIpAddress) ? (publicIpLoading ? ToolText("확인 중", "Checking") : unknown) : publicIpAddress;
			StringBuilder builder = new StringBuilder();
			if (ToolUsesEnglish)
			{
				builder.AppendLine("MANUAL PORT FORWARDING EXAMPLE");
				builder.AppendLine("Service name: Minecraft Server");
				builder.AppendLine("External port: " + serverPort);
				builder.AppendLine("Internal port: " + serverPort);
				builder.AppendLine("Internal IP address: " + localIpv4);
				builder.AppendLine("Protocol: TCP");
				builder.AppendLine("Default gateway: " + gateway);
				builder.AppendLine("Router page: " + (gateway == unknown ? unknown : "http://" + gateway));
				builder.AppendLine("Windows Firewall check: " + (firewallCheckNeeded ? "Required" : "Allow rule detected"));
				builder.AppendLine("Double NAT / CGNAT: " + (cgnatPossible ? "Possible" : "Not detected; compare the router WAN address"));
				builder.AppendLine("MAC address for DHCP reservation: " + macAddress);
				builder.AppendLine("Public address: " + publicAddress);
				builder.AppendLine("Server profile folder: " + serverDirectory);
				builder.AppendLine("Java executable: " + javaPath);
				if (!string.IsNullOrWhiteSpace(automaticResult)) builder.AppendLine("Automatic check result: " + automaticResult);
				builder.AppendLine();
				builder.AppendLine("ROUTER STEPS");
				builder.AppendLine("1. Open " + (gateway == unknown ? "your router administration page" : "http://" + gateway) + ".");
				builder.AppendLine("2. Sign in and find Port Forwarding, NAT, or Virtual Server.");
				builder.AppendLine("3. Add and enable a TCP rule using the values above.");
				builder.AppendLine("4. Reserve the LAN IPv4 for this MAC address so it does not change.");
				builder.AppendLine("5. In Windows Security, allow the selected Java executable on private networks.");
				builder.AppendLine("6. Start the server, then use Recheck access.");
				builder.AppendLine();
				builder.AppendLine("IMPORTANT");
				builder.AppendLine("• A public address alone does not prove that the port is reachable.");
				builder.AppendLine("• Never disable the whole firewall and do not use DMZ just for Minecraft.");
				builder.AppendLine("• A private WAN address or a WAN address different from the value above can indicate double NAT or carrier-grade NAT.");
				builder.AppendLine("• If port forwarding is unavailable, the playit.gg button opens its official no-port-forwarding guide.");
				builder.AppendLine("• The launcher never overwrites or removes an existing router mapping. Only mappings created by this server session are removed at shutdown.");
			}
			else
			{
				builder.AppendLine("수동 포트포워딩 설정 예시");
				builder.AppendLine("서비스 이름: Minecraft Server");
				builder.AppendLine("외부 포트: " + serverPort);
				builder.AppendLine("내부 포트: " + serverPort);
				builder.AppendLine("내부 IP 주소: " + localIpv4);
				builder.AppendLine("프로토콜: TCP");
				builder.AppendLine("기본 게이트웨이: " + gateway);
				builder.AppendLine("공유기 관리 페이지: " + (gateway == unknown ? unknown : "http://" + gateway));
				builder.AppendLine("Windows 방화벽 확인 필요 여부: " + (firewallCheckNeeded ? "확인 필요" : "허용 규칙 감지됨"));
				builder.AppendLine("이중 NAT 또는 CGNAT 가능성: " + (cgnatPossible ? "가능성 있음" : "현재 감지되지 않음 · 공유기 WAN 주소 비교 필요"));
				builder.AppendLine("DHCP 예약용 MAC 주소: " + macAddress);
				builder.AppendLine("공인 주소: " + publicAddress);
				builder.AppendLine("서버 프로필 폴더: " + serverDirectory);
				builder.AppendLine("Java 실행 파일: " + javaPath);
				if (!string.IsNullOrWhiteSpace(automaticResult)) builder.AppendLine("자동 처리 결과: " + automaticResult);
				builder.AppendLine();
				builder.AppendLine("공유기 설정 순서");
				builder.AppendLine("1. " + (gateway == unknown ? "공유기 관리자 페이지를 엽니다." : "http://" + gateway + " 를 엽니다."));
				builder.AppendLine("2. 로그인한 뒤 포트포워딩, NAT 또는 가상 서버 메뉴를 찾습니다.");
				builder.AppendLine("3. 위 값대로 TCP 규칙을 추가하고 활성화합니다.");
				builder.AppendLine("4. 내부 IPv4가 바뀌지 않도록 MAC 주소에 DHCP 예약을 설정합니다.");
				builder.AppendLine("5. Windows 보안에서 선택된 Java 실행 파일의 개인 네트워크 접근을 허용합니다.");
				builder.AppendLine("6. 서버를 시작한 뒤 외부 접속 재검사 버튼을 누릅니다.");
				builder.AppendLine();
				builder.AppendLine("꼭 확인하세요");
				builder.AppendLine("• 공인 주소가 표시되어도 해당 포트가 실제로 열렸다는 뜻은 아닙니다.");
				builder.AppendLine("• Minecraft 때문에 방화벽 전체를 끄거나 DMZ를 사용하지 마세요.");
				builder.AppendLine("• 공유기의 WAN 주소가 사설 주소이거나 위 공인 주소와 다르면 이중 공유기 또는 통신사 CGNAT일 수 있습니다.");
				builder.AppendLine("• 포트포워딩이 어렵다면 playit.gg 버튼에서 공식 터널 안내를 확인할 수 있습니다.");
				builder.AppendLine("• 기존 공유기 매핑은 덮어쓰거나 삭제하지 않습니다. 이번 서버 실행에서 런처가 만든 매핑만 서버 종료 시 삭제합니다.");
			}
			guideBox.Text = builder.ToString();
		}

		private void CopyAddress(string address)
		{
			if (string.IsNullOrWhiteSpace(address) || address == ToolText("확인되지 않음", "Unavailable") || address == ToolText("확인 중…", "Checking…"))
			{
				return;
			}
			try
			{
				Clipboard.SetText(address);
				statusLabel.ForeColor = palette.Success;
				statusLabel.Text = ToolText("주소를 복사했습니다: ", "Address copied: ") + address;
			}
			catch (Exception exception)
			{
				statusLabel.ForeColor = palette.Danger;
				statusLabel.Text = ToolText("주소를 복사하지 못했습니다: ", "Could not copy the address: ") + exception.Message;
			}
		}

		private void RequestExternalRecheck()
		{
			if (recheckExternalAccess == null)
			{
				statusLabel.ForeColor = palette.Warning;
				statusLabel.Text = ToolText("이 화면을 연 위치에서는 외부 재검사를 시작할 수 없습니다.", "External recheck is unavailable from this screen.");
				return;
			}
			try
			{
				recheckExternalAccess();
				statusLabel.ForeColor = palette.Success;
				statusLabel.Text = ToolText("외부 접속 재검사를 요청했습니다. 메인 화면 또는 콘솔에서 결과를 확인하세요.", "External recheck requested. Review the result in the main window or console.");
			}
			catch (Exception exception)
			{
				statusLabel.ForeColor = palette.Danger;
				statusLabel.Text = ToolText("외부 재검사를 시작하지 못했습니다: ", "Could not start the external recheck: ") + exception.Message;
			}
		}

		private void OpenRouterPage()
		{
			string gateway = networkDetails == null ? null : networkDetails.Gateway;
			IPAddress parsedAddress;
			if (string.IsNullOrWhiteSpace(gateway) || !IPAddress.TryParse(gateway, out parsedAddress) || parsedAddress.AddressFamily != AddressFamily.InterNetwork)
			{
				statusLabel.ForeColor = palette.Warning;
				statusLabel.Text = ToolText("기본 게이트웨이를 확인하지 못했습니다.", "The default gateway could not be found.");
				return;
			}
			OpenWebPage("http://" + gateway);
		}

		private void OpenWebPage(string url)
		{
			try
			{
				ProcessStartInfo startInfo = new ProcessStartInfo();
				startInfo.FileName = url;
				startInfo.UseShellExecute = true;
				Process.Start(startInfo);
				statusLabel.ForeColor = palette.Muted;
				statusLabel.Text = ToolText("브라우저에서 페이지를 열었습니다.", "The page was opened in your browser.");
			}
			catch (Exception exception)
			{
				statusLabel.ForeColor = palette.Danger;
				statusLabel.Text = ToolText("페이지를 열지 못했습니다: ", "Could not open the page: ") + exception.Message;
			}
		}

		private static string FormatHostPort(string host, int port)
		{
			if (string.IsNullOrWhiteSpace(host))
			{
				return string.Empty;
			}
			return host.IndexOf(':') >= 0 ? "[" + host + "]:" + port : host + ":" + port;
		}
	}
}
