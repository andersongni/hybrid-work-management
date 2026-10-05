#define MyAppName "Presence Tracker"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Presence Tracker"
#define MyAppExeName "PresenceTracker.exe"
#define MyAppId "{{A7B73C31-0A27-4F6B-A6D8-C0CEB73E0C4D}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no
DisableDirPage=no
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=PresenceTracker-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
SetupIconFile=..\src\PresenceTracker\Assets\PresenceTracker.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
MinVersion=10.0

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked
Name: "autostart"; Description: "Iniciar automaticamente com o Windows"; GroupDescription: "Inicialização:"; Flags: checkedonce

[Files]
Source: "..\artifacts\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Executar {#MyAppName} agora"; Flags: postinstall nowait skipifsilent

[Code]
var
  DataDirsPage: TInputDirWizardPage;

function InstallConfigPath: string;
begin
  Result := ExpandConstant('{localappdata}\PresenceTracker\config\install.ini');
end;

procedure InitializeWizard;
begin
  DataDirsPage := CreateInputDirPage(
    wpSelectTasks,
    'Pastas de dados',
    'Escolha onde o Presence Tracker deve guardar logs e backups.',
    'Os arquivos do programa serão instalados na pasta escolhida (por padrão, na pasta do usuário, sem precisar de administrador).' + #13#10 +
    'Os dados do usuário podem ficar em pastas separadas, que você pode alterar depois nas configurações do aplicativo.',
    False,
    '');
  DataDirsPage.Add('Pasta de logs:');
  DataDirsPage.Add('Pasta de backups:');
  DataDirsPage.Values[0] := ExpandConstant('{localappdata}\PresenceTracker\logs');
  DataDirsPage.Values[1] := ExpandConstant('{localappdata}\PresenceTracker\backups');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  LogsFolder, BackupsFolder: string;
begin
  Result := True;
  if CurPageID = DataDirsPage.ID then
  begin
    LogsFolder := Trim(DataDirsPage.Values[0]);
    BackupsFolder := Trim(DataDirsPage.Values[1]);
    if LogsFolder = '' then
    begin
      MsgBox('Informe uma pasta válida para logs.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if BackupsFolder = '' then
    begin
      MsgBox('Informe uma pasta válida para backups.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if CompareText(LogsFolder, BackupsFolder) = 0 then
    begin
      MsgBox('As pastas de logs e de backups devem ser diferentes.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure WriteInstallConfig;
var
  ConfigPath, StartWithWindows: string;
begin
  ConfigPath := InstallConfigPath;
  ForceDirectories(ExtractFileDir(ConfigPath));
  ForceDirectories(DataDirsPage.Values[0]);
  ForceDirectories(DataDirsPage.Values[1]);

  if WizardIsTaskSelected('autostart') then
    StartWithWindows := '1'
  else
    StartWithWindows := '0';

  SetIniString('Install', 'LogsFolder', DataDirsPage.Values[0], ConfigPath);
  SetIniString('Install', 'BackupsFolder', DataDirsPage.Values[1], ConfigPath);
  SetIniString('Install', 'StartWithWindows', StartWithWindows, ConfigPath);
  SetIniString('Install', 'ApplyDefaults', '1', ConfigPath);
  SetIniString('Install', 'InstallDir', ExpandConstant('{app}'), ConfigPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteInstallConfig;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataRoot: string;
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PresenceTracker');

  if CurUninstallStep = usPostUninstall then
  begin
    DataRoot := ExpandConstant('{localappdata}\PresenceTracker');
    if DirExists(DataRoot) then
    begin
      if MsgBox(
        'Deseja remover também os dados do Presence Tracker neste computador?' + #13#10 + #13#10 +
        'Isso inclui o banco de dados, logs e backups armazenados em:' + #13#10 +
        DataRoot + #13#10 + #13#10 +
        'Pastas personalizadas fora desse diretório não serão apagadas.',
        mbConfirmation,
        MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataRoot, True, True, True);
      end;
    end;
  end;
end;
