import re

with open('ModernLauncherGui.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix 1: ApplyTheme DWM synchronization
old_apply_theme = r'''\t\tprivate void ApplyTheme\(\)\s*\{\s*SendMessage\(this\.Handle, WM_SETREDRAW, false, 0\);\s*try\s*\{\s*ThemePalette palette = ThemePalette\.Create\(darkTheme\);\s*BackColor = palette\.Window;\s*ForeColor = palette\.Text;'''
new_apply_theme = '''\t\tprivate void ApplyTheme()
\t\t{
\t\t\tSendMessage(this.Handle, WM_SETREDRAW, false, 0);
\t\t\ttry
\t\t\t{
\t\t\t\tThemePalette palette = ThemePalette.Create(darkTheme);
\t\t\t\tBackColor = palette.Window;
\t\t\t\tForeColor = palette.Text;
\t\t\t\tTitleBarDwm.ApplyTheme(this, palette.Window, palette.Text, palette.Border);'''

if re.search(old_apply_theme, content):
    content = re.sub(old_apply_theme, new_apply_theme, content)
    print("Fix 1 applied.")
else:
    print("Failed to find old_apply_theme")

# Fix 2: Update button layout
old_btn = r'''\t\t\tlauncherUpdateButton = CreateButton\(Localization\.T\("Button\.LauncherUpdate"\), 168\);\s*launcherUpdateButton\.Tag = "ghost";\s*launcherUpdateButton\.Anchor = AnchorStyles\.Top \| AnchorStyles\.Right;\s*SetButtonIcon\(launcherUpdateButton, ButtonIcon\.Upgrade\);'''
new_btn = '''\t\t\tlauncherUpdateButton = CreateButton(Localization.T("Button.LauncherUpdate"), 168);
\t\t\tlauncherUpdateButton.Tag = "ghost";
\t\t\tlauncherUpdateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
\t\t\tlauncherUpdateButton.Top = 4;
\t\t\tSetButtonIcon(launcherUpdateButton, ButtonIcon.Upgrade);'''

if re.search(old_btn, content):
    content = re.sub(old_btn, new_btn, content)
    print("Fix 2 applied.")
else:
    print("Failed to find old_btn")

with open('ModernLauncherGui.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print("ModernLauncherGui.cs updated.")