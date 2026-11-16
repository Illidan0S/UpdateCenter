#ifndef MyAppVersion
  #error MyAppVersion must be supplied by build-installer.ps1
#endif
#ifndef AppPayloadDir
  #define AppPayloadDir "dist"
#endif
#ifdef UnsignedReleaseVersion
  #if UnsignedReleaseVersion != "2.0.0" || UnsignedReleaseVersion != MyAppVersion
    #error Unsigned release policy is restricted to the exact 2.0.0 build
  #endif
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
; Keep package contents stable across checkouts with different file modification times.
TimeStampsInUTC=yes
TouchDate=2000-01-01
TouchTime=00:00:00
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
Source: "{#AppPayloadDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion restartreplace touch
Source: "{#AppPayloadDir}\LEGGIMI.txt"; DestDir: "{app}"; Flags: ignoreversion touch
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion touch

[InstallDelete]
Type: files; Name: "{app}\INSTALLA.bat"
Type: files; Name: "{app}\UNINSTALLA.bat"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchUpdateCenter}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser; Check: IsSelfUpdate

[CustomMessages]
italian.LaunchUpdateCenter=Avvia Update Center
english.LaunchUpdateCenter=Launch Update Center
italian.RemoveUserData=Vuoi eliminare anche impostazioni, cronologia e log locali di Update Center?
english.RemoveUserData=Do you also want to delete Update Center settings, history, and local logs?
italian.UnsupportedUpdateWindows=Questa edizione, build o architettura di Windows non rientra nella matrice del candidato.
english.UnsupportedUpdateWindows=This Windows edition, build or architecture is outside the candidate's support matrix.
italian.UnverifiedUpdateOwner=Impossibile verificare il proprietario e la directory dell'installazione da aggiornare. L'installazione esistente viene conservata.
english.UnverifiedUpdateOwner=Setup could not verify the owner and directory of the existing installation. The existing installation is preserved.
#ifdef UnsignedReleaseVersion
italian.UnverifiedUpdateChecksum=Impossibile verificare l'installer con il checksum della release ufficiale. L'installazione esistente viene conservata.
english.UnverifiedUpdateChecksum=Setup could not verify this installer against the official release checksum. The existing installation is preserved.
#endif

[Code]
const
  AppUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{AA563914-ED53-47F0-A8E6-92232F72D06C}_is1';
  FileAttributeReparsePoint = $400;
  InvalidFileAttributes = $FFFFFFFF;
  DriveFixed = 3;
  TokenQuery = $0008;
  TokenUserInformation = 1;
  SeFileObject = 1;
  OwnerSecurityInformation = 1;

type
  TTokenUserBuffer = record
    UserSid: UINT_PTR;
    // Space for SID_AND_ATTRIBUTES and a SECURITY_MAX_SID_SIZE SID (68 bytes).
    Remaining: array[0..95] of Byte;
  end;

var
  SelfUpdateDirectory: String;

function ReadProductInfo(Major, Minor, SpMajor, SpMinor: Cardinal; var Sku: Cardinal): Boolean;
  external 'GetProductInfo@kernel32.dll stdcall';
function ReadFileAttributes(FileName: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function ReadDriveType(RootPath: String): Cardinal;
  external 'GetDriveTypeW@kernel32.dll stdcall';
function CurrentProcessHandle: UINT_PTR;
  external 'GetCurrentProcess@kernel32.dll stdcall';
function OpenProcessToken(ProcessHandle: UINT_PTR; Access: Cardinal; var Token: UINT_PTR): Boolean;
  external 'OpenProcessToken@advapi32.dll stdcall';
function ReadTokenUser(Token: UINT_PTR; InfoClass: Cardinal; var Buffer: TTokenUserBuffer;
  BufferSize: Cardinal; var ReturnedSize: Cardinal): Boolean;
  external 'GetTokenInformation@advapi32.dll stdcall';
function ReadFileOwner(FileName: String; ObjectType, SecurityInfo: Cardinal;
  var OwnerSid: UINT_PTR; GroupSid, Dacl, Sacl: UINT_PTR; var Descriptor: UINT_PTR): Cardinal;
  external 'GetNamedSecurityInfoW@advapi32.dll stdcall';
function EqualSid(LeftSid, RightSid: UINT_PTR): Boolean;
  external 'EqualSid@advapi32.dll stdcall';
function FreeSecurityDescriptor(Descriptor: UINT_PTR): UINT_PTR;
  external 'LocalFree@kernel32.dll stdcall';
function CloseNativeHandle(Handle: UINT_PTR): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

#ifdef UnsignedReleaseVersion
#include "scripts\UnsignedSetupChecksum.iss"
#endif

function IsSelfUpdate: Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), '/SELFUPDATE') = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function IsSupportedWindows: Boolean;
var
  Version: TWindowsVersion;
  Sku: Cardinal;
begin
  Result := False;
  if not IsX64OS then Exit;
  GetWindowsVersionEx(Version);
  if not Version.NTPlatform or (Version.ProductType <> VER_NT_WORKSTATION) or
     (Version.Major <> 10) or (Version.Minor <> 0) then Exit;
  if not ReadProductInfo(Version.Major, Version.Minor, Version.ServicePackMajor,
    Version.ServicePackMinor, Sku) then Exit;

  // Keep the published client support matrix in sync with the Portable updater.
  case Version.Build of
    17763: Result := (Sku = 125) or (Sku = 126);  // Enterprise LTSC 2019
    19044: Result := (Sku = 125) or (Sku = 126) or (Sku = 191);  // LTSC 2021
    26100:  // 24H2 Enterprise, Education and IoT (including LTSC)
      case Sku of
        4, 27, 121, 122, 125, 126, 188, 191: Result := True;
      end;
    26200, 28000:  // 25H2 and 26H1 supported desktop editions
      case Sku of
        4, 27, 48, 49, 99, 100, 101, 121, 122, 161, 162, 164, 165: Result := True;
        188: Result := Version.Build = 26200;
      end;
  end;
end;

function IsLocalPathWithoutReparse(const Path: String): Boolean;
var
  Entry: String;
  Attributes: Cardinal;
begin
  Result := False;
  if (Length(Path) < 3) or (Copy(Path, 2, 2) <> ':\') or
     (Pos(':', Copy(Path, 3, Length(Path))) <> 0) then Exit;
  if ReadDriveType(Copy(Path, 1, 3)) <> DriveFixed then Exit;
  Entry := RemoveBackslashUnlessRoot(ExpandFileName(Path));
  repeat
    Attributes := ReadFileAttributes(Entry);
    if (Attributes = InvalidFileAttributes) or
       ((Attributes and FileAttributeReparsePoint) <> 0) then Exit;
    if Length(Entry) = 3 then Break;
    Entry := ExtractFileDir(Entry);
  until Length(Entry) < 3;
  Result := Length(Entry) = 3;
end;

function IsOwnedByTokenUser(const Path: String; TokenSid: UINT_PTR): Boolean;
var
  OwnerSid, Descriptor: UINT_PTR;
begin
  Result := False;
  if ReadFileOwner(Path, SeFileObject, OwnerSecurityInformation, OwnerSid, 0, 0, 0,
    Descriptor) <> 0 then Exit;
  try
    if OwnerSid <> 0 then Result := EqualSid(OwnerSid, TokenSid);
  finally
    FreeSecurityDescriptor(Descriptor);
  end;
end;

function ReadRegisteredDirectory(var Directory: String): Boolean;
var
  AppPath, UninstallCommand, Uninstaller, UninstallName, Exe, FileVersion, DisplayVersion: String;
begin
  Result := False;
  if not RegQueryStringValue(HKCU32, AppUninstallKey, 'Inno Setup: App Path', AppPath) or
     not RegQueryStringValue(HKCU32, AppUninstallKey, 'InstallLocation', Directory) or
     not RegQueryStringValue(HKCU32, AppUninstallKey, 'UninstallString', UninstallCommand) or
     not RegQueryStringValue(HKCU32, AppUninstallKey, 'DisplayVersion', DisplayVersion) then Exit;
  if not IsLocalPathWithoutReparse(AppPath) or not IsLocalPathWithoutReparse(Directory) then Exit;
  Directory := RemoveBackslashUnlessRoot(ExpandFileName(Directory));
  if not DirExists(Directory) or (Length(Directory) <= 3) or
     (CompareText(RemoveBackslashUnlessRoot(ExpandFileName(AppPath)), Directory) <> 0) then Exit;
  Exe := AddBackslash(Directory) + '{#MyAppExeName}';
  if not FileExists(Exe) or not IsLocalPathWithoutReparse(Exe) or
     not GetVersionNumbersString(Exe, FileVersion) or (FileVersion <> DisplayVersion + '.0') then Exit;
  Uninstaller := RemoveQuotes(UninstallCommand);
  UninstallName := ExtractFileName(Uninstaller);
  if (CompareText(Copy(UninstallName, 1, 5), 'unins') <> 0) or
     (CompareText(ExtractFileExt(UninstallName), '.exe') <> 0) or
     (CompareText(ExtractFileDir(Uninstaller), Directory) <> 0) or
     not FileExists(Uninstaller) or not IsLocalPathWithoutReparse(Uninstaller) then Exit;
  Result := FileExists(ChangeFileExt(Uninstaller, '.dat')) and
    IsLocalPathWithoutReparse(ChangeFileExt(Uninstaller, '.dat'));
end;

function VerifySelfUpdate(var Directory: String): Boolean;
var
  SourceExe, ExpectedSource: String;
  Token: UINT_PTR;
  User: TTokenUserBuffer;
  ReturnedSize: Cardinal;
begin
  Result := False;
  if IsAdminInstallMode then Exit;
  SourceExe := ExpandConstant('{srcexe}');
  ExpectedSource := ExpandConstant('{localappdata}\UpdateCenter\Updates\UpdateCenter-Setup-v{#MyAppVersion}.exe');
  // The original updater passes only /SELFUPDATE. Bind its staged file to this
  // token's profile and owner SID before accepting the per-user registration.
  if (CompareText(SourceExe, ExpectedSource) <> 0) or not FileExists(SourceExe) or
     not IsLocalPathWithoutReparse(SourceExe) or not ReadRegisteredDirectory(Directory) then Exit;
  if not OpenProcessToken(CurrentProcessHandle, TokenQuery, Token) then Exit;
  try
    if not ReadTokenUser(Token, TokenUserInformation, User, SizeOf(User), ReturnedSize) then Exit;
    if User.UserSid = 0 then Exit;
    Result := IsOwnedByTokenUser(SourceExe, User.UserSid) and
      IsOwnedByTokenUser(Directory, User.UserSid);
  finally
    CloseNativeHandle(Token);
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := False;
  if not IsSupportedWindows then
  begin
    SuppressibleMsgBox(CustomMessage('UnsupportedUpdateWindows'), mbCriticalError, MB_OK, IDOK);
    Exit;
  end;
  if IsSelfUpdate and not VerifySelfUpdate(SelfUpdateDirectory) then
  begin
    SuppressibleMsgBox(CustomMessage('UnverifiedUpdateOwner'), mbCriticalError, MB_OK, IDOK);
    Exit;
  end;
#ifdef UnsignedReleaseVersion
  if IsSelfUpdate and not VerifyOfficialSetupChecksum(ExpandConstant('{srcexe}')) then
  begin
    SuppressibleMsgBox(CustomMessage('UnverifiedUpdateChecksum'), mbCriticalError, MB_OK, IDOK);
    Exit;
  end;
#endif
  Result := True;
end;

#ifdef UnsignedReleaseVersion
procedure DeinitializeSetup;
begin
  VerifiedSetupSource.Free;
end;
#endif

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Directory: String;
begin
  Result := '';
  if not IsSelfUpdate then Exit;
  if not VerifySelfUpdate(Directory) or (CompareText(Directory, SelfUpdateDirectory) <> 0) or
     (CompareText(RemoveBackslashUnlessRoot(ExpandFileName(WizardDirValue)), Directory) <> 0) then
    Result := CustomMessage('UnverifiedUpdateOwner');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if (not UninstallSilent) and
       (MsgBox(ExpandConstant('{cm:RemoveUserData}'), mbConfirmation, MB_YESNO) = IDYES) then
      DelTree(ExpandConstant('{localappdata}\UpdateCenter'), True, True, True);
  end;
end;
