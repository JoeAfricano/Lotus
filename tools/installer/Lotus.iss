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
AppComments=Seu PC pronto para jogar: otimização reversível, Meus Jogos, Game list, LOTUS SCORE e ferramentas.
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
WizardStyle=modern
WizardSizePercent=110
; A running Lotus (often hidden in the tray) is closed by [Code] below before files are touched.
CloseApplications=force
RestartApplications=no
ShowLanguageDialog=auto
VersionInfoVersion=0.1.0.0
VersionInfoProductVersion=0.1.0.0
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
en.Welcome=Your PC ready to play. Nothing in Windows changes without your confirmation, and everything Lotus changes can be undone.

[Messages]
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

[UninstallRun]
; Removes the "start with Windows" entry if the player turned it on (HKCU Run value written by Lotus).
Filename: "{sys}\reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Lotus /f"; Flags: runhidden; RunOnceId: "LotusRun"

[Code]
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
