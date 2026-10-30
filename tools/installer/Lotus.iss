; Lotus installer (Inno Setup 6). Built by tools\Build-Installer.ps1 from the self-contained publish output.
; Per-user install, no administrator: same folder the in-app installer uses (%LOCALAPPDATA%\Programs\Lotus).
; Player data (%LOCALAPPDATA%\Lotus, Documents\Lotus) is never touched, not even on uninstall.

#ifndef AppVersion
  #define AppVersion "0.1.0-alpha"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\dist\Lotus"
#endif
#ifndef SetupName
  #define SetupName "Lotus-Setup"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.1.0.0"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\dist"
#endif

[Setup]
AppId={{7B3E2C1A-5D4F-4E8B-9A6C-1F0D2E3B4A5C}
AppName=Lotus
AppVersion={#AppVersion}
AppVerName=Lotus {#AppVersion}
AppPublisher=Joeafricano
AppCopyright=Lotus · código aberto · feito por Joeafricano com o Claude
AppComments=Seu PC pronto para jogar: otimização reversível, Meus Jogos, Game list, Lotus Score e ferramentas.
DefaultDirName={localappdata}\Programs\Lotus
DisableDirPage=yes
DefaultGroupName=Lotus
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename={#SetupName}-{#AppVersion}
SetupIconFile=..\..\src\Lotus.App\Assets\Lotus.ico
UninstallDisplayIcon={app}\Lotus.App.exe
UninstallDisplayName=Lotus
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
; Lotus look: forced dark mode in the app's plum, the crystal lotus on the side, the gold logo and the ambient art behind the pages.
; Artwork is made from the app's concept art by tools\installer\Make-InstallerArt.ps1.
WizardStyle=modern dark hidebevels
WizardBackColor=#1A0F18
WizardBackImageFile=art\background.png
WizardBackImageOpacity=110
WizardImageFile=art\wizard-100.png,art\wizard-150.png,art\wizard-200.png,art\wizard-250.png
WizardImageBackColor=#1A0F18
WizardSmallImageFile=art\small-100.png,art\small-150.png,art\small-200.png,art\small-250.png
WizardSizePercent=110
; The welcome page shows the lotus art and says install or update.
DisableWelcomePage=no
; A running Lotus (often hidden in the tray) is closed by [Code] below before files are touched.
CloseApplications=force
RestartApplications=no
ShowLanguageDialog=auto
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoDescription=Instalador do Lotus

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
pt.DesktopIcon=Criar atalho na Área de Trabalho
en.DesktopIcon=Create a desktop shortcut
pt.Launch=Abrir o Lotus agora
en.Launch=Open Lotus now
pt.Welcome=Seu PC pronto para jogar. Nada no Windows muda sem você confirmar, e tudo que o Lotus mexe pode ser desfeito.
pt.UpdateWelcome=Este assistente vai atualizar o Lotus para a versão [ver]. Seus jogos, diário, conquistas e configurações continuam como estão; não é preciso desinstalar nada.%n%nSe o Lotus estiver aberto, ele será fechado e você pode abri-lo de novo no final.
en.Welcome=Your PC ready to play. Nothing in Windows changes without your confirmation, and everything Lotus changes can be undone.
en.UpdateWelcome=This will update Lotus to version [ver]. Your games, diary, achievements and settings stay as they are; there is nothing to uninstall.%n%nIf Lotus is open it will be closed, and you can open it again at the end.

[Messages]
pt.WelcomeLabel1=Bem-vindo ao Lotus
en.WelcomeLabel1=Welcome to Lotus
pt.FinishedHeadingLabel=Tudo pronto. Bom jogo!
en.FinishedHeadingLabel=All set. Have a great game!
pt.FinishedLabel=O Lotus está instalado. Seus dados de jogador ficam guardados no seu PC e nada no Windows muda sem você confirmar.
en.FinishedLabel=Lotus is installed. Your player data stays on your PC and nothing in Windows changes without your confirmation.
pt.FinishedLabelNoIcons=O Lotus está instalado. Seus dados de jogador ficam guardados no seu PC e nada no Windows muda sem você confirmar.
en.FinishedLabelNoIcons=Lotus is installed. Your player data stays on your PC and nothing in Windows changes without your confirmation.
pt.WelcomeLabel2=Este assistente vai instalar o [name/ver] neste PC.%n%nSeu PC pronto para jogar. Nada no Windows muda sem você confirmar, e tudo que o Lotus mexe pode ser desfeito.%n%nVersão alpha: obrigado por testar!
en.WelcomeLabel2=This will install [name/ver] on this PC.%n%nYour PC ready to play. Nothing in Windows changes without your confirmation, and everything Lotus changes can be undone.%n%nAlpha version: thanks for testing!

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Lotus"; Filename: "{app}\Lotus.App.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Lotus"; Filename: "{app}\Lotus.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Lotus.App.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent
; Update started from inside Lotus (/relaunch=1): open Lotus again once the files are in place.
Filename: "{app}\Lotus.App.exe"; Flags: nowait; Check: RelaunchAfterUpdate

[UninstallRun]
; Removes the "start with Windows" entry if the player turned it on (HKCU Run value written by Lotus).
Filename: "{sys}\reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Lotus /f"; Flags: runhidden; RunOnceId: "LotusRun"

[Code]
// In-app update: Lotus runs this setup silently with /relaunch=1 and expects to be opened again afterwards.
function RelaunchAfterUpdate(): Boolean;
begin
  Result := WizardSilent() and (ExpandConstant('{param:relaunch|0}') = '1');
end;

// Lotus keeps running in the tray when its window is closed, so the installer and the uninstaller close it first
// (otherwise its files are in use). Only Lotus.App.exe is touched.
procedure CloseLotus();
var Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Lotus.App.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(600);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseLotus();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseLotus();
  Result := True;
end;

// Update: when Lotus is already installed for this user, the wizard says "update", keeps the earlier choices
// (shortcuts) and goes straight to installing. Files are replaced in place; player data is never touched.
function IsUpdate(): Boolean;
begin
  Result := RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7B3E2C1A-5D4F-4E8B-9A6C-1F0D2E3B4A5C}_is1');
end;

procedure InitializeWizard();
var Text: String;
begin
  if IsUpdate() then
  begin
    Text := CustomMessage('UpdateWelcome');
    StringChangeEx(Text, '[ver]', '{#AppVersion}', True);
    WizardForm.WelcomeLabel2.Caption := Text;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := IsUpdate() and ((PageID = wpSelectTasks) or (PageID = wpReady));
end;
