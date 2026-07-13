#ifndef MyAppVersion
  #error MyAppVersion is required
#endif
#ifndef MyBuildNumber
  #error MyBuildNumber is required
#endif
#ifndef SourceExe
  #error SourceExe is required
#endif
#ifndef ProjectRoot
  #error ProjectRoot is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif

[Setup]
AppId={{B855A383-8B1F-46A6-A39E-4C7D529C57C1}
AppName=MineHarbor — Minecraft Server Launcher
AppVersion={#MyAppVersion}
AppVerName=MineHarbor — Minecraft Server Launcher v{#MyAppVersion} (build {#MyBuildNumber})
AppPublisher=Mangom72
AppPublisherURL=https://github.com/Mangom72/mc-server-launcher
AppSupportURL=https://github.com/Mangom72/mc-server-launcher/issues
AppUpdatesURL=https://github.com/Mangom72/mc-server-launcher/releases/latest
DefaultDirName={autopf}\MineHarbor
DefaultGroupName=MineHarbor
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=MineHarbor-Setup-v{#MyAppVersion}
SetupIconFile={#ProjectRoot}\launcher-icon.ico
UninstallDisplayIcon={app}\MineHarbor.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
Uninstallable=yes
VersionInfoVersion={#MyBuildNumber}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription=MineHarbor — Minecraft Server Launcher installer
VersionInfoProductName=MineHarbor — Minecraft Server Launcher
LicenseFile={#ProjectRoot}\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "MineHarbor.exe"; Flags: ignoreversion
Source: "{#ProjectRoot}\obj\installed.mode"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\Minecraft-Server-Launcher.exe"
Type: files; Name: "{autodesktop}\Minecraft Server Launcher.lnk"
Type: files; Name: "{autoprograms}\Minecraft Server Launcher\Minecraft Server Launcher.lnk"

[Icons]
Name: "{group}\MineHarbor"; Filename: "{app}\MineHarbor.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\MineHarbor"; Filename: "{app}\MineHarbor.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\MineHarbor.exe"; Description: "{cm:LaunchProgram,MineHarbor}"; Flags: nowait postinstall skipifsilent
