#define MyAppName "Presence Tracker"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Presence Tracker"
#define MyAppExeName "PresenceTracker.exe"

[Setup]
AppId={{A7B73C31-0A27-4F6B-A6D8-C0CEB73E0C4D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Presence Tracker
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=PresenceTracker-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\PresenceTracker\Assets\PresenceTracker.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Dirs]
Name: "{localappdata}\PresenceTracker"; Flags: uninsneveruninstall
Name: "{localappdata}\PresenceTracker\backups"; Flags: uninsneveruninstall
Name: "{localappdata}\PresenceTracker\logs"; Flags: uninsneveruninstall
Name: "{localappdata}\PresenceTracker\config"; Flags: uninsneveruninstall

[Files]
Source: "..\artifacts\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Presence Tracker"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Presence Tracker"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PresenceTracker"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Executar Presence Tracker"; Flags: postinstall nowait skipifsilent

