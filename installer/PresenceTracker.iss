#define MyAppName "Presence Tracker"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Presence Tracker"
#define MyAppExeName "PresenceTracker.exe"
#define MyAppId "{{A7B73C31-0A27-4F6B-A6D8-C0CEB73E0C4D}"
#define MyAppMutex "Local\PresenceTracker.Singleton"
#define MyUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=auto
DisableDirPage=auto
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
CloseApplications=force
RestartApplications=no
Uninstallable=yes
MinVersion=10.0
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
UsePreviousSetupType=yes
UsePreviousLanguage=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked
Name: "autostart"; Description: "Iniciar automaticamente com o Windows"; GroupDescription: "Inicialização:"

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
  IsUpgradeInstall: Boolean;
  PreviousVersionInstalled: string;
  PreviousInstallDir: string;
  OneDriveRoot: string;
  UseOneDriveDataFolders: Boolean;

function InstallConfigPath: string;
begin
  Result := ExpandConstant('{localappdata}\PresenceTracker\config\install.ini');
end;

function DefaultInstallDir: string;
begin
  Result := ExpandConstant('{localappdata}\Programs\{#MyAppName}');
end;

function DefaultLogsFolder: string;
begin
  Result := ExpandConstant('{localappdata}\PresenceTracker\logs');
end;

function DefaultBackupsFolder: string;
begin
  Result := ExpandConstant('{localappdata}\PresenceTracker\backups');
end;

function OneDriveAppRoot: string;
begin
  Result := AddBackslash(OneDriveRoot) + 'Presence Tracker';
end;

function OneDriveLogsFolder: string;
begin
  Result := AddBackslash(OneDriveAppRoot) + 'log';
end;

function OneDriveBackupsFolder: string;
begin
  Result := AddBackslash(OneDriveAppRoot) + 'backup';
end;

function FirstExistingDir(const Candidates: array of string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to GetArrayLength(Candidates) - 1 do
  begin
    if (Trim(Candidates[I]) <> '') and DirExists(Trim(Candidates[I])) then
    begin
      Result := Trim(Candidates[I]);
      Exit;
    end;
  end;
end;

function ResolveOneDriveRoot: string;
var
  Candidate: string;
  Candidates: array of string;
begin
  SetArrayLength(Candidates, 5);
  Candidates[0] := GetEnv('OneDriveConsumer');
  Candidates[1] := GetEnv('OneDrive');
  Candidates[2] := GetEnv('OneDriveCommercial');

  Candidate := '';
  RegQueryStringValue(HKCU, 'Software\Microsoft\OneDrive\Accounts\Personal', 'UserFolder', Candidate);
  Candidates[3] := Candidate;

  Candidate := '';
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\OneDrive', 'UserFolder', Candidate) then
    RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\OneDrive', 'UserFolder', Candidate);
  Candidates[4] := Candidate;

  Result := FirstExistingDir(Candidates);
end;

function QueryUninstallString(RootKey: Integer): string;
begin
  Result := '';
  RegQueryStringValue(RootKey, '{#MyUninstallKey}', 'UninstallString', Result);
end;

function QueryDisplayVersion(RootKey: Integer): string;
begin
  Result := '';
  RegQueryStringValue(RootKey, '{#MyUninstallKey}', 'DisplayVersion', Result);
end;

function QueryInstallLocation(RootKey: Integer): string;
begin
  Result := '';
  RegQueryStringValue(RootKey, '{#MyUninstallKey}', 'InstallLocation', Result);
end;

procedure ResolvePreviousInstallInfo;
begin
  PreviousVersionInstalled := QueryDisplayVersion(HKCU);
  if PreviousVersionInstalled = '' then
    PreviousVersionInstalled := QueryDisplayVersion(HKLM);
  if PreviousVersionInstalled = '' then
    PreviousVersionInstalled := 'desconhecida';

  PreviousInstallDir := QueryInstallLocation(HKCU);
  if PreviousInstallDir = '' then
    PreviousInstallDir := QueryInstallLocation(HKLM);
  if PreviousInstallDir = '' then
    PreviousInstallDir := GetIniString('Install', 'InstallDir', DefaultInstallDir, InstallConfigPath);
  if PreviousInstallDir = '' then
    PreviousInstallDir := DefaultInstallDir;
end;

function DetectExistingInstall: Boolean;
begin
  if (QueryUninstallString(HKCU) <> '') or (QueryUninstallString(HKLM) <> '') then
  begin
    Result := True;
    Exit;
  end;

  if FileExists(AddBackslash(DefaultInstallDir) + '{#MyAppExeName}') then
  begin
    Result := True;
    Exit;
  end;

  if FileExists(InstallConfigPath) then
  begin
    Result := True;
    Exit;
  end;

  Result := False;
end;

function IsAppRunning: Boolean;
begin
  Result := CheckForMutexes('{#MyAppMutex}');
end;

procedure CloseRunningApp;
var
  ResultCode: Integer;
  Attempt: Integer;
begin
  if not IsAppRunning then
    Exit;

  { O app minimiza para a bandeja no WM_CLOSE; encerra o processo para liberar o EXE. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /T /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  for Attempt := 1 to 10 do
  begin
    if not IsAppRunning then
      Exit;
    Sleep(200);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  CloseRunningApp;
  IsUpgradeInstall := DetectExistingInstall;
  if IsUpgradeInstall then
    ResolvePreviousInstallInfo;
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
begin
  NeedsRestart := False;
  CloseRunningApp;
  if IsAppRunning then
    Result := 'Não foi possível encerrar o Presence Tracker automaticamente. Feche o aplicativo pela bandeja do sistema (Sair) e execute o instalador novamente.'
  else
    Result := '';
end;

procedure ApplyUpgradeCaptions(CurPageID: Integer);
begin
  if not IsUpgradeInstall then
    Exit;

  WizardForm.Caption := 'Atualização — Presence Tracker';

  case CurPageID of
    wpWelcome:
      begin
        WizardForm.WelcomeLabel1.Caption := 'Atualização do Presence Tracker';
        WizardForm.WelcomeLabel2.Caption :=
          'Já existe uma instalação do Presence Tracker neste computador.' + #13#10 + #13#10 +
          'Versão instalada: ' + PreviousVersionInstalled + #13#10 +
          'Versão deste pacote: {#MyAppVersion}' + #13#10 + #13#10 +
          'Este assistente irá atualizar a instalação existente.' + #13#10 +
          'Seu banco de dados, logs, backups e configurações serão preservados.' + #13#10 + #13#10 +
          'Pasta atual: ' + PreviousInstallDir + #13#10 + #13#10 +
          'Clique em Avançar para continuar.';
      end;
    wpSelectTasks:
      begin
        WizardForm.SelectTasksLabel.Caption :=
          'Confirme as opções da atualização. As escolhas da instalação anterior foram mantidas quando possível.';
      end;
    wpReady:
      begin
        WizardForm.ReadyLabel.Caption :=
          'Tudo pronto para atualizar o Presence Tracker.' + #13#10 + #13#10 +
          'Os arquivos do programa serão substituídos. Seus dados não serão apagados.' + #13#10 + #13#10 +
          'Clique em Atualizar para continuar.';
        WizardForm.NextButton.Caption := 'Atualizar';
      end;
    wpInstalling:
      begin
        WizardForm.StatusLabel.Caption := 'Atualizando o Presence Tracker...';
      end;
    wpFinished:
      begin
        WizardForm.FinishedHeadingLabel.Caption := 'Atualização concluída';
        WizardForm.FinishedLabel.Caption :=
          'O Presence Tracker foi atualizado com sucesso.' + #13#10 + #13#10 +
          'Versão anterior: ' + PreviousVersionInstalled + #13#10 +
          'Versão atual: {#MyAppVersion}' + #13#10 + #13#10 +
          'Clique em Concluir para sair do assistente.';
      end;
  else
    begin
      if CurPageID = DataDirsPage.ID then
      begin
        DataDirsPage.Caption := 'Pastas de dados (atualização)';
        DataDirsPage.Description := 'As pastas atuais serão mantidas, a menos que você as altere.';
      end;
    end;
  end;
end;

procedure InitializeWizard;
var
  ConfigPath, LogsFolder, BackupsFolder, ExistingLogs, ExistingBackups: string;
begin
  UseOneDriveDataFolders := False;
  OneDriveRoot := ResolveOneDriveRoot;

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

  ConfigPath := InstallConfigPath;
  ExistingLogs := GetIniString('Install', 'LogsFolder', '', ConfigPath);
  ExistingBackups := GetIniString('Install', 'BackupsFolder', '', ConfigPath);

  LogsFolder := ExistingLogs;
  BackupsFolder := ExistingBackups;
  if LogsFolder = '' then
    LogsFolder := DefaultLogsFolder;
  if BackupsFolder = '' then
    BackupsFolder := DefaultBackupsFolder;

  { Oferece OneDrive apenas em instalação nova sem pastas já configuradas. }
  if (not IsUpgradeInstall) and (OneDriveRoot <> '') and (ExistingLogs = '') and (ExistingBackups = '') then
  begin
    if MsgBox(
      'Detectamos o OneDrive neste computador:' + #13#10 +
      OneDriveRoot + #13#10 + #13#10 +
      'Deseja criar a pasta "Presence Tracker" no OneDrive, com as subpastas "log" e "backup", ' +
      'e usá-las como padrão para logs e backups?' + #13#10 + #13#10 +
      'Isso ajuda a sincronizar esses arquivos entre seus dispositivos.' + #13#10 + #13#10 +
      'Você poderá alterar essas pastas depois, se preferir.',
      mbConfirmation,
      MB_YESNO) = IDYES then
    begin
      UseOneDriveDataFolders := True;
      LogsFolder := OneDriveLogsFolder;
      BackupsFolder := OneDriveBackupsFolder;
      ForceDirectories(OneDriveAppRoot);
      ForceDirectories(LogsFolder);
      ForceDirectories(BackupsFolder);
    end;
  end;

  DataDirsPage.Values[0] := LogsFolder;
  DataDirsPage.Values[1] := BackupsFolder;

  if UseOneDriveDataFolders then
  begin
    DataDirsPage.Caption := 'Pastas de dados (OneDrive)';
    DataDirsPage.Description :=
      'As pastas padrão foram definidas no OneDrive. Você pode confirmá-las ou escolher outros locais.';
  end;

  if IsUpgradeInstall then
  begin
    WizardForm.Caption := 'Atualização — Presence Tracker';
    ApplyUpgradeCaptions(wpWelcome);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  ApplyUpgradeCaptions(CurPageID);
  { Sempre marcar inicialização com o Windows como padrão visível. }
  if CurPageID = wpSelectTasks then
    WizardSelectTasks('autostart');
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  DataInfo: string;
begin
  DataInfo :=
    'Pasta de logs:' + NewLine +
    Space + DataDirsPage.Values[0] + NewLine + NewLine +
    'Pasta de backups:' + NewLine +
    Space + DataDirsPage.Values[1];
  if UseOneDriveDataFolders then
    DataInfo := DataInfo + NewLine + NewLine +
      'Armazenamento de dados:' + NewLine +
      Space + 'OneDrive (' + OneDriveAppRoot + ')';

  if IsUpgradeInstall then
  begin
    Result :=
      'Tipo de operação:' + NewLine +
      Space + 'Atualização (instalação existente será mantida e os arquivos serão substituídos)' + NewLine + NewLine +
      'Versão instalada:' + NewLine +
      Space + PreviousVersionInstalled + NewLine + NewLine +
      'Nova versão:' + NewLine +
      Space + '{#MyAppVersion}' + NewLine + NewLine +
      MemoDirInfo + NewLine + NewLine +
      MemoGroupInfo;
    if MemoTasksInfo <> '' then
      Result := Result + NewLine + NewLine + MemoTasksInfo;
  end
  else
  begin
    Result :=
      'Tipo de operação:' + NewLine +
      Space + 'Nova instalação' + NewLine + NewLine +
      MemoDirInfo + NewLine + NewLine +
      MemoGroupInfo + NewLine + NewLine +
      DataInfo;
    if MemoTasksInfo <> '' then
      Result := Result + NewLine + NewLine + MemoTasksInfo;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  { Em atualização, mantém as pastas de dados já configuradas. }
  if IsUpgradeInstall and (PageID = DataDirsPage.ID) then
    Result := True;
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

  if IsUpgradeInstall then
  begin
    { Atualização: só sincroniza o diretório do app; não reaplica defaults que sobrescreveriam preferências. }
    SetIniString('Install', 'InstallDir', ExpandConstant('{app}'), ConfigPath);
    SetIniString('Install', 'ApplyDefaults', '0', ConfigPath);
    Exit;
  end;

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
  if CurStep = ssInstall then
  begin
    CloseRunningApp;
    if IsUpgradeInstall then
      WizardForm.StatusLabel.Caption := 'Atualizando arquivos do Presence Tracker...';
  end;
  if CurStep = ssPostInstall then
    WriteInstallConfig;
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningApp;
  if IsAppRunning then
  begin
    MsgBox(
      'Não foi possível encerrar o Presence Tracker automaticamente.' + #13#10 + #13#10 +
      'Feche o aplicativo pela bandeja do sistema (Sair) e execute a desinstalação novamente.',
      mbError,
      MB_OK);
    Result := False;
  end
  else
    Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataRoot: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseRunningApp;
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PresenceTracker');
  end;

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
