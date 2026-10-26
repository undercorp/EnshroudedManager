; Enshrouded Manager — installer (Inno Setup 6).
; Build with build.ps1 at the repository root, which publishes the binaries first.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define ServiceName "EnshroudedManager"
#define Publish "..\publish"

[Setup]
AppId={{6F1C2B7E-4D4A-4B8E-9E57-3C2D7A51E0B1}
AppName=Enshrouded Manager
AppVersion={#AppVersion}
AppVerName=Enshrouded Manager {#AppVersion}
AppPublisher=Enshrouded Manager contributors
AppPublisherURL=https://github.com/undercorp/EnshroudedManager
AppSupportURL=https://github.com/undercorp/EnshroudedManager/issues
AppUpdatesURL=https://github.com/undercorp/EnshroudedManager/releases
DefaultDirName={code:DefaultInstallDir}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=EnshroudedManager-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\EnshroudedManager.Service.exe
UninstallDisplayName=Enshrouded Manager
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
en.CompService=Background service (updates, backups, server control)
fr.CompService=Service en arrière-plan (mises à jour, sauvegardes, pilotage du serveur)
en.CompWeb=Web interface (from any PC or phone on the network)
fr.CompWeb=Interface web (depuis n'importe quel PC ou téléphone du réseau)
en.CompDesktop=Desktop application
fr.CompDesktop=Application de bureau
en.TypeFull=Web interface and desktop application
fr.TypeFull=Interface web et application de bureau
en.TypeCustom=Custom
fr.TypeCustom=Personnalisée
en.NeedOneUi=Choose at least the web interface or the desktop application.
fr.NeedOneUi=Choisissez au moins l'interface web ou l'application de bureau.
en.ServerDirTitle=Server files
fr.ServerDirTitle=Fichiers du serveur
en.ServerDirDesc=Where should the Enshrouded dedicated server be installed?
fr.ServerDirDesc=Où installer le serveur dédié Enshrouded ?
en.ServerDirSub=The server (about 8 GB) is downloaded from Steam into this folder after the installation. Saves are kept there too.
fr.ServerDirSub=Le serveur (environ 8 Go) est téléchargé depuis Steam dans ce dossier après l'installation. Les sauvegardes du monde y sont aussi.
en.UiTitle=Interface access
fr.UiTitle=Accès à l'interface
en.UiDesc=Password and port of Enshrouded Manager
fr.UiDesc=Mot de passe et port d'Enshrouded Manager
en.UiSub=This password protects the web interface and the desktop application (at least 8 characters).
fr.UiSub=Ce mot de passe protège l'interface web et l'application de bureau (8 caractères minimum).
en.UiPort=Web port:
fr.UiPort=Port web :
en.UiPassword=Password:
fr.UiPassword=Mot de passe :
en.UiConfirm=Confirm password:
fr.UiConfirm=Confirmer le mot de passe :
en.GameTitle=Game server
fr.GameTitle=Serveur de jeu
en.GameDesc=Basic settings of your Enshrouded server
fr.GameDesc=Réglages de base de votre serveur Enshrouded
en.GameSub=Everything can be changed later in Enshrouded Manager. Empty passwords are generated randomly.
fr.GameSub=Tout reste modifiable ensuite dans Enshrouded Manager. Les mots de passe laissés vides sont générés au hasard.
en.GameName=Server name:
fr.GameName=Nom du serveur :
en.GameSlots=Max players (1-16):
fr.GameSlots=Joueurs max (1-16) :
en.GamePort=Query port (UDP, forward it on your router):
fr.GamePort=Port de requête (UDP, à rediriger sur votre box) :
en.GameFriend=Password for players (Friend group):
fr.GameFriend=Mot de passe des joueurs (groupe Friend) :
en.GameAdmin=Password for admins (Admin group):
fr.GameAdmin=Mot de passe administrateur (groupe Admin) :
en.ErrPassword=The password must have at least 8 characters.
fr.ErrPassword=Le mot de passe doit faire au moins 8 caractères.
en.ErrConfirm=The two passwords do not match.
fr.ErrConfirm=Les deux mots de passe sont différents.
en.ErrPort=Invalid port number.
fr.ErrPort=Numéro de port invalide.
en.ErrSlots=Max players must be between 1 and 16.
fr.ErrSlots=Le nombre de joueurs doit être entre 1 et 16.
en.ErrName=Please enter a server name.
fr.ErrName=Saisissez un nom de serveur.
en.ErrServerDir=Choose a folder for the server files.
fr.ErrServerDir=Choisissez un dossier pour les fichiers du serveur.
en.StoppingService=Stopping Enshrouded Manager (the game server saves the world)...
fr.StoppingService=Arrêt d'Enshrouded Manager (le serveur de jeu sauvegarde le monde)...
en.ConfiguringService=Configuring the Windows service...
fr.ConfiguringService=Configuration du service Windows...
en.OpenWeb=Open the web interface
fr.OpenWeb=Ouvrir l'interface web
en.OpenDesktop=Start the desktop application
fr.OpenDesktop=Lancer l'application de bureau
en.FinishInfo=The server is now being downloaded from Steam (about 8 GB). Follow the progress in Enshrouded Manager: the server starts by itself when the download is finished.
fr.FinishInfo=Le serveur est maintenant téléchargé depuis Steam (environ 8 Go). Suivez la progression dans Enshrouded Manager : le serveur démarre tout seul à la fin du téléchargement.
en.DeleteData=Also delete the settings, logs and backups of Enshrouded Manager?
fr.DeleteData=Supprimer aussi les réglages, journaux et sauvegardes d'Enshrouded Manager ?
en.DeleteServer=Also delete the game server files, INCLUDING YOUR WORLD SAVES?%n%n%1
fr.DeleteServer=Supprimer aussi les fichiers du serveur de jeu, Y COMPRIS VOS MONDES ?%n%n%1
en.WebShortcut=Enshrouded Manager (web)
fr.WebShortcut=Enshrouded Manager (web)

[Types]
Name: "full"; Description: "{cm:TypeFull}"
Name: "custom"; Description: "{cm:TypeCustom}"; Flags: iscustom

[Components]
Name: "service"; Description: "{cm:CompService}"; Types: full custom; Flags: fixed
Name: "web"; Description: "{cm:CompWeb}"; Types: full
Name: "desktop"; Description: "{cm:CompDesktop}"; Types: full

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Components: desktop

[Files]
Source: "{#Publish}\Service\EnshroudedManager.Service.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: service
Source: "{#Publish}\Service\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion; Components: service
Source: "{#Publish}\Service\*.json"; DestDir: "{app}"; Excludes: "appsettings.json"; Flags: ignoreversion skipifsourcedoesntexist; Components: service
Source: "{#Publish}\Service\wwwroot\*"; DestDir: "{app}\wwwroot"; Flags: ignoreversion recursesubdirs; Components: service
Source: "{#Publish}\Desktop\EnshroudedManager.Desktop.exe"; DestDir: "{app}\Desktop"; Flags: ignoreversion; Components: desktop

[Dirs]
Name: "{app}\data"

[Icons]
Name: "{autoprograms}\Enshrouded Manager"; Filename: "{app}\Desktop\EnshroudedManager.Desktop.exe"; Components: desktop
Name: "{autodesktop}\Enshrouded Manager"; Filename: "{app}\Desktop\EnshroudedManager.Desktop.exe"; Tasks: desktopicon
Name: "{autoprograms}\{cm:WebShortcut}"; Filename: "http://localhost:{code:GetPort}/"; IconFilename: "{app}\EnshroudedManager.Service.exe"; Components: web

[Run]
Filename: "http://localhost:{code:GetPort}/"; Description: "{cm:OpenWeb}"; Flags: postinstall shellexec nowait skipifsilent; Components: web
Filename: "{app}\Desktop\EnshroudedManager.Desktop.exe"; Description: "{cm:OpenDesktop}"; Flags: postinstall nowait skipifsilent runasoriginaluser; Components: desktop

[UninstallRun]
Filename: "{sys}\net.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Enshrouded Manager (web)"""; Flags: runhidden waituntilterminated; RunOnceId: "DelWebRule"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Enshrouded Server (EnshroudedManager)"""; Flags: runhidden waituntilterminated; RunOnceId: "DelGameRule"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM EnshroudedManager.Desktop.exe"; Flags: runhidden waituntilterminated; RunOnceId: "KillDesktop"

[Code]
var
  ServerDirPage: TInputDirWizardPage;
  UiPage: TInputQueryWizardPage;
  GamePage: TInputQueryWizardPage;
  UninstallServerDir: String;

{ ---------- helpers ---------- }

function ServiceImageDir(): String;
var
  ImagePath: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Services\{#ServiceName}', 'ImagePath', ImagePath) then
  begin
    StringChangeEx(ImagePath, '"', '', True);
    Result := ExtractFileDir(ImagePath);
  end;
end;

{ An existing installation (even one made by hand) is updated in place. }
function DefaultInstallDir(Param: String): String;
begin
  Result := ServiceImageDir();
  if Result = '' then
    Result := ExpandConstant('{autopf}\EnshroudedManager');
end;

function ManagerJson(): String;
begin
  Result := ExpandConstant('{app}\data\manager.json');
end;

function IsUpgrade(): Boolean;
begin
  Result := FileExists(ManagerJson());
end;

{ Minimal reader for "Key": value / "Key": "value" in manager.json (written indented by the service). }
function ReadJsonValue(const FileName, Key: String): String;
var
  Lines: TArrayOfString;
  I, P: Integer;
  S: String;
begin
  Result := '';
  if not LoadStringsFromFile(FileName, Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    S := Trim(Lines[I]);
    if Pos('"' + Key + '":', S) = 1 then
    begin
      S := Trim(Copy(S, Length(Key) + 4, MaxInt));
      if (Length(S) > 0) and (S[Length(S)] = ',') then S := Copy(S, 1, Length(S) - 1);
      if (Length(S) >= 2) and (S[1] = '"') then S := Copy(S, 2, Length(S) - 2);
      StringChangeEx(S, '\\', '\', True);
      Result := S;
      Exit;
    end;
  end;
end;

function GetPort(Param: String): String;
begin
  if (UiPage <> nil) and not IsUpgrade() then
    Result := Trim(UiPage.Values[0])
  else
    Result := ReadJsonValue(ManagerJson(), 'Port');
  if Result = '' then Result := '8090';
end;

function JsonString(const S: String): String;
var
  R: String;
begin
  R := S;
  StringChangeEx(R, '\', '\\', True);
  StringChangeEx(R, '"', '\"', True);
  Result := '"' + R + '"';
end;

function JsonBool(B: Boolean): String;
begin
  if B then Result := 'true' else Result := 'false';
end;

function IsValidPort(const S: String): Boolean;
var
  N: Integer;
begin
  N := StrToIntDef(Trim(S), -1);
  Result := (N > 0) and (N < 65536);
end;

function Exec2(const FileName, Params: String): Integer;
begin
  if not Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Result) then
    Result := -1;
end;

function ServiceExists(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\{#ServiceName}');
end;

{ ---------- wizard ---------- }

procedure InitializeWizard();
begin
  ServerDirPage := CreateInputDirPage(wpSelectComponents, CustomMessage('ServerDirTitle'), CustomMessage('ServerDirDesc'),
    CustomMessage('ServerDirSub'), False, '');
  ServerDirPage.Add('');
  { Every value can be given on the command line, for unattended installs:
    /SERVERDIR= /PASSWORD= /PORT= /SERVERNAME= /SLOTS= /QUERYPORT= /FRIENDPASSWORD= /ADMINPASSWORD= }
  ServerDirPage.Values[0] := ExpandConstant('{param:SERVERDIR|}');
  if ServerDirPage.Values[0] = '' then
    ServerDirPage.Values[0] := ExpandConstant('{sd}\EnshroudedServer');

  UiPage := CreateInputQueryPage(ServerDirPage.ID, CustomMessage('UiTitle'), CustomMessage('UiDesc'), CustomMessage('UiSub'));
  UiPage.Add(CustomMessage('UiPassword'), True);
  UiPage.Add(CustomMessage('UiConfirm'), True);
  UiPage.Add(CustomMessage('UiPort'), False);
  UiPage.Values[0] := ExpandConstant('{param:PASSWORD|}');
  UiPage.Values[1] := UiPage.Values[0];
  UiPage.Values[2] := ExpandConstant('{param:PORT|8090}');

  GamePage := CreateInputQueryPage(UiPage.ID, CustomMessage('GameTitle'), CustomMessage('GameDesc'), CustomMessage('GameSub'));
  GamePage.Add(CustomMessage('GameName'), False);
  GamePage.Add(CustomMessage('GameSlots'), False);
  GamePage.Add(CustomMessage('GamePort'), False);
  GamePage.Add(CustomMessage('GameFriend'), False);
  GamePage.Add(CustomMessage('GameAdmin'), False);
  GamePage.Values[0] := ExpandConstant('{param:SERVERNAME|Enshrouded Server}');
  GamePage.Values[1] := ExpandConstant('{param:SLOTS|16}');
  GamePage.Values[2] := ExpandConstant('{param:QUERYPORT|15637}');
  GamePage.Values[3] := ExpandConstant('{param:FRIENDPASSWORD|}');
  GamePage.Values[4] := ExpandConstant('{param:ADMINPASSWORD|}');
end;

{ Values[0..2] of UiPage are password, confirm, port. }
function UiPassword(): String; begin Result := UiPage.Values[0]; end;
function UiPort(): String; begin Result := Trim(UiPage.Values[2]); end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  // The install folder is only known after the folder page, so test the page first.
  if (PageID = ServerDirPage.ID) or (PageID = UiPage.ID) or (PageID = GamePage.ID) then
    Result := IsUpgrade();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectComponents then
  begin
    if not WizardIsComponentSelected('web') and not WizardIsComponentSelected('desktop') then
    begin
      MsgBox(CustomMessage('NeedOneUi'), mbError, MB_OK);
      Result := False;
    end;
  end
  else if CurPageID = ServerDirPage.ID then
  begin
    if Trim(ServerDirPage.Values[0]) = '' then
    begin
      MsgBox(CustomMessage('ErrServerDir'), mbError, MB_OK);
      Result := False;
    end;
  end
  else if CurPageID = UiPage.ID then
  begin
    if Length(UiPassword()) < 8 then
    begin
      MsgBox(CustomMessage('ErrPassword'), mbError, MB_OK);
      Result := False;
    end
    else if UiPassword() <> UiPage.Values[1] then
    begin
      MsgBox(CustomMessage('ErrConfirm'), mbError, MB_OK);
      Result := False;
    end
    else if not IsValidPort(UiPort()) then
    begin
      MsgBox(CustomMessage('ErrPort'), mbError, MB_OK);
      Result := False;
    end;
  end
  else if CurPageID = GamePage.ID then
  begin
    if Trim(GamePage.Values[0]) = '' then
    begin
      MsgBox(CustomMessage('ErrName'), mbError, MB_OK);
      Result := False;
    end
    else if (StrToIntDef(Trim(GamePage.Values[1]), 0) < 1) or (StrToIntDef(Trim(GamePage.Values[1]), 0) > 16) then
    begin
      MsgBox(CustomMessage('ErrSlots'), mbError, MB_OK);
      Result := False;
    end
    else if not IsValidPort(GamePage.Values[2]) then
    begin
      MsgBox(CustomMessage('ErrPort'), mbError, MB_OK);
      Result := False;
    end;
  end;
end;

{ The running service must release its files; stopping it also stops the game server cleanly. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if ServiceExists() then
  begin
    WizardForm.PreparingLabel.Caption := CustomMessage('StoppingService');
    Exec2(ExpandConstant('{sys}\net.exe'), 'stop {#ServiceName}');
  end;
  Exec2(ExpandConstant('{sys}\taskkill.exe'), '/F /IM EnshroudedManager.Desktop.exe');
end;

{ Hands the wizard choices to the service through data\setup.json (deleted by the service once read). }
procedure WriteSetupFile();
var
  Json, Lang: String;
begin
  if ActiveLanguage() = 'fr' then Lang := 'fr' else Lang := 'en';
  Json := '{' + #13#10 + '  "webEnabled": ' + JsonBool(WizardIsComponentSelected('web'));
  { Installations made before the language setting existed get the installer language. }
  if IsUpgrade() and (ReadJsonValue(ManagerJson(), 'Language') = '') then
    Json := Json + ',' + #13#10 + '  "language": ' + JsonString(Lang);
  if not IsUpgrade() then
  begin
    Json := Json + ',' + #13#10 +
      '  "language": ' + JsonString(Lang) + ',' + #13#10 +
      '  "installDir": ' + JsonString(Trim(ServerDirPage.Values[0])) + ',' + #13#10 +
      '  "port": ' + IntToStr(StrToInt(UiPort())) + ',' + #13#10 +
      '  "password": ' + JsonString(UiPassword()) + ',' + #13#10 +
      '  "serverName": ' + JsonString(Trim(GamePage.Values[0])) + ',' + #13#10 +
      '  "slotCount": ' + IntToStr(StrToInt(Trim(GamePage.Values[1]))) + ',' + #13#10 +
      '  "queryPort": ' + IntToStr(StrToInt(Trim(GamePage.Values[2]))) + ',' + #13#10 +
      '  "friendPassword": ' + JsonString(GamePage.Values[3]) + ',' + #13#10 +
      '  "adminPassword": ' + JsonString(GamePage.Values[4]);
  end;
  Json := Json + #13#10 + '}';
  SaveStringToFile(ExpandConstant('{app}\data\setup.json'), Json, False);
end;

procedure ConfigureService();
var
  Exe, Port: String;
begin
  WizardForm.StatusLabel.Caption := CustomMessage('ConfiguringService');
  Exe := ExpandConstant('{app}\EnshroudedManager.Service.exe');
  if not ServiceExists() then
  begin
    Exec2(ExpandConstant('{sys}\sc.exe'), 'create {#ServiceName} binPath= "\"' + Exe + '\"" start= delayed-auto DisplayName= "Enshrouded Manager"');
    Exec2(ExpandConstant('{sys}\sc.exe'), 'description {#ServiceName} "Automatic updates and management of the Enshrouded dedicated server"');
    { Restart the service if it ever crashes (after 10 s, 30 s, 60 s). }
    Exec2(ExpandConstant('{sys}\sc.exe'), 'failure {#ServiceName} reset= 86400 actions= restart/10000/restart/30000/restart/60000');
  end
  else
    Exec2(ExpandConstant('{sys}\sc.exe'), 'config {#ServiceName} binPath= "\"' + Exe + '\""');

  Port := GetPort('');
  Exec2(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall delete rule name="Enshrouded Manager (web)"');
  if WizardIsComponentSelected('web') then
    Exec2(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall add rule name="Enshrouded Manager (web)" dir=in action=allow protocol=TCP localport=' + Port + ' profile=private,domain');

  Exec2(ExpandConstant('{sys}\net.exe'), 'start {#ServiceName}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteSetupFile();
    ConfigureService();
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and not IsUpgrade() then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + CustomMessage('FinishInfo');
end;

{ ---------- uninstall ---------- }

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  DataDir := ExpandConstant('{app}\data');
  if CurUninstallStep = usUninstall then
    { Read before the files go away. }
    UninstallServerDir := ReadJsonValue(DataDir + '\manager.json', 'InstallDir');

  if CurUninstallStep = usPostUninstall then
  begin
    { SteamCMD is only a download tool, re-downloaded when needed. }
    DelTree(ExpandConstant('{app}\steamcmd'), True, True, True);
    if DirExists(DataDir) and (MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(DataDir, True, True, True);
    if (UninstallServerDir <> '') and DirExists(UninstallServerDir) and
       (MsgBox(FmtMessage(CustomMessage('DeleteServer'), [UninstallServerDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(UninstallServerDir, True, True, True);
    RemoveDir(ExpandConstant('{app}'));
  end;
end;
