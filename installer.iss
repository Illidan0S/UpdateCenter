#ifndef MyAppVersion
  #error MyAppVersion must be supplied by build-installer.ps1
#endif

#define MyAppName "Update Center"
#define MyAppPublisher "Illidan0S"
#define MyAppExeName "UpdateCenter.exe"
#define MyAppUrl "https://github.com/Illidan0S/UpdateCenter"

[Setup]
AppId={{AA563914-ED53-47F0-A8E6-92232F72D06C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
AppCopyright=Copyright (C) 2026 {#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Installer di {#MyAppName}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\UpdateCenter
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
MinVersion=10.0.17763
OutputDir=installer-dist
OutputBaseFilename=UpdateCenter-Setup-v{#MyAppVersion}
SetupIconFile=Assets\UpdateCenter.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousLanguage=yes
DisableWelcomePage=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "dist\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "dist\LEGGIMI.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\INSTALLA.bat"
Type: files; Name: "{app}\UNINSTALLA.bat"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchUpdateCenter}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
italian.LaunchUpdateCenter=Avvia Update Center
english.LaunchUpdateCenter=Launch Update Center
italian.RemoveUserData=Vuoi eliminare anche impostazioni, cronologia e log locali di Update Center?
english.RemoveUserData=Do you also want to delete Update Center settings, history, and local logs?

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if (not UninstallSilent) and
       (MsgBox(ExpandConstant('{cm:RemoveUserData}'), mbConfirmation, MB_YESNO) = IDYES) then
      DelTree(ExpandConstant('{localappdata}\UpdateCenter'), True, True, True);
  end;
end;
