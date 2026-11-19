// Included only after installer.iss matches an exact, supported unsigned policy.
const
  ReleaseSetupName = 'UpdateCenter-Setup-v{#UnsignedReleaseVersion}.exe';
  ReleaseChecksumPath = '/Illidan0S/UpdateCenter/releases/download/v{#UnsignedReleaseVersion}/UpdateCenter-Setup-v{#UnsignedReleaseVersion}.exe.sha256';
  HexDigits = '0123456789ABCDEF';
  NoProxy = 1;
  SecureRequest = $00800000;
  DisableFeatures = 63;
  NoCookiesRedirectsOrAuthentication = $7;
  QueryNumericStatus = $20000013;
  QueryLocation = 33;

type
  TRedirectBuffer = array[0..4095] of Char;
  TChecksumBuffer = array[0..127] of Byte;

var
  VerifiedSetupSource: TFileStream;

function HttpOpen(Agent: String; AccessType: Cardinal; Proxy, Bypass: UINT_PTR; Flags: Cardinal): UINT_PTR;
  external 'WinHttpOpen@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpSetTimeouts(Session: UINT_PTR; Resolve, Connect, Send, Receive: Integer): Boolean;
  external 'WinHttpSetTimeouts@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpConnect(Session: UINT_PTR; Host: String; Port: Word; Reserved: Cardinal): UINT_PTR;
  external 'WinHttpConnect@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpOpenRequest(Connection: UINT_PTR; Verb, Path: String; Version, Referrer,
  AcceptTypes: UINT_PTR; Flags: Cardinal): UINT_PTR;
  external 'WinHttpOpenRequest@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpSetOption(Request: UINT_PTR; Option: Cardinal; var Value: Cardinal; Size: Cardinal): Boolean;
  external 'WinHttpSetOption@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpSend(Request, Headers: UINT_PTR; HeaderLength: Cardinal; Body: UINT_PTR;
  BodyLength, TotalLength: Cardinal; Context: UINT_PTR): Boolean;
  external 'WinHttpSendRequest@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpReceive(Request, Reserved: UINT_PTR): Boolean;
  external 'WinHttpReceiveResponse@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpStatus(Request: UINT_PTR; Query: Cardinal; Name: UINT_PTR;
  var Value, Size: Cardinal; Index: UINT_PTR): Boolean;
  external 'WinHttpQueryHeaders@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpLocation(Request: UINT_PTR; Query: Cardinal; Name: UINT_PTR;
  var Buffer: TRedirectBuffer; var Size: Cardinal; Index: UINT_PTR): Boolean;
  external 'WinHttpQueryHeaders@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpRead(Request: UINT_PTR; var Buffer: TChecksumBuffer; Capacity: Cardinal;
  var ReadCount: Cardinal): Boolean;
  external 'WinHttpReadData@{sys}\winhttp.dll stdcall delayload setuponly';
function HttpClose(Handle: UINT_PTR): Boolean;
  external 'WinHttpCloseHandle@{sys}\winhttp.dll stdcall delayload setuponly';
function ReadTickCount: Cardinal;
  external 'GetTickCount@kernel32.dll stdcall setuponly';

function ReadAllowedRedirect(Request: UINT_PTR; var Host, Path: String): Boolean;
var
  Buffer: TRedirectBuffer;
  Size: Cardinal;
  Url, Prefix: String;
  Index: Integer;
begin
  Result := False;
  Size := SizeOf(Buffer);
  if not HttpLocation(Request, QueryLocation, 0, Buffer, Size, 0) then Exit;
  if (Size mod 2 <> 0) or (Size div 2 >= 4096) then Exit;
  Url := '';
  for Index := 0 to Integer(Size div 2) - 1 do
  begin
    if (Buffer[Index] < #33) or (Buffer[Index] > #126) or
       (Buffer[Index] = '\') or (Buffer[Index] = '#') then Exit;
    Url := Url + Buffer[Index];
  end;

  // Match the whole authority, including the following slash. WinHttpConnect
  // takes the approved host separately and always uses HTTPS port 443.
  Host := 'release-assets.githubusercontent.com';
  Prefix := 'https://' + Host + '/';
  if Pos(Prefix, Url) <> 1 then
  begin
    Host := 'objects.githubusercontent.com';
    Prefix := 'https://' + Host + '/';
    if Pos(Prefix, Url) <> 1 then Exit;
  end;
  Path := Copy(Url, Length(Prefix), Length(Url));
  Result := Length(Path) > 1;
end;

function ReadTextResponse(Request: UINT_PTR; MaxBytes: Integer; var Body: String): Boolean;
var
  Buffer: TChecksumBuffer;
  Count, Started: Cardinal;
  Index: Integer;
begin
  Result := False;
  Body := '';
  Started := ReadTickCount;
  repeat
    if ReadTickCount - Started > 10000 then Exit;
    if not HttpRead(Request, Buffer, SizeOf(Buffer), Count) then Exit;
    if (Count > SizeOf(Buffer)) or (Length(Body) + Count > MaxBytes) then Exit;
    for Index := 0 to Integer(Count) - 1 do
    begin
      if (Buffer[Index] <> 9) and (Buffer[Index] <> 10) and (Buffer[Index] <> 13) and
         ((Buffer[Index] < 32) or (Buffer[Index] > 126)) then Exit;
      Body := Body + Chr(Buffer[Index]);
    end;
  until Count = 0;
  Result := True;
end;

function ReadOfficialText(const AssetPath: String; MaxBytes: Integer; var Body: String): Boolean;
var
  Session, Connection, Request: UINT_PTR;
  Disabled, Size, Status: Cardinal;
  Host, Path: String;
  Hop: Integer;
begin
  Result := False;
  Host := 'github.com';
  Body := '';
  Path := AssetPath;
  Session := HttpOpen('UpdateCenter-Setup/{#UnsignedReleaseVersion}', NoProxy, 0, 0, 0);
  if Session = 0 then Exit;
  try
    if not HttpSetTimeouts(Session, 4000, 4000, 4000, 4000) then Exit;
    for Hop := 0 to 1 do
    begin
      Connection := HttpConnect(Session, Host, 443, 0);
      if Connection = 0 then Exit;
      try
        Request := HttpOpenRequest(Connection, 'GET', Path, 0, 0, 0, SecureRequest);
        if Request = 0 then Exit;
        try
          Disabled := NoCookiesRedirectsOrAuthentication;
          Size := SizeOf(Status);
          if not HttpSetOption(Request, DisableFeatures, Disabled, SizeOf(Disabled)) or
             not HttpSend(Request, 0, 0, 0, 0, 0, 0) or
             not HttpReceive(Request, 0) or
             not HttpStatus(Request, QueryNumericStatus, 0, Status, Size, 0) then Exit;
          if Status = 200 then
          begin
            if not ReadTextResponse(Request, MaxBytes, Body) then Exit;
            Break;
          end;
          if (Hop <> 0) or (Status <> 302) or not ReadAllowedRedirect(Request, Host, Path) then Exit;
        finally
          HttpClose(Request);
        end;
      finally
        HttpClose(Connection);
      end;
    end;
  finally
    HttpClose(Session);
  end;

  Result := Body <> '';
end;

function ReadOfficialChecksum(var Checksum, SidecarHash: String; var SidecarSize: Integer): Boolean;
var
  Body: String;
  Index: Integer;
begin
  Result := False;
  if not ReadOfficialText(ReleaseChecksumPath, 68 + Length(ReleaseSetupName), Body) then Exit;

  // Match a single GitHub workflow checksum line and its exact asset name.
  SidecarSize := Length(Body);
  SidecarHash := GetSHA256OfString(AnsiString(Body));
  if Copy(Body, Length(Body) - 1, 2) = #13#10 then
    SetLength(Body, Length(Body) - 2)
  else if Copy(Body, Length(Body), 1) = #10 then
    SetLength(Body, Length(Body) - 1);
  if (Length(Body) <> 66 + Length(ReleaseSetupName)) or
     (Copy(Body, 65, Length(Body)) <> '  ' + ReleaseSetupName) then Exit;
  for Index := 1 to 64 do
    if Pos(Body[Index], '0123456789abcdefABCDEF') = 0 then Exit;
  Checksum := Copy(Body, 1, 64);
  Result := True;
end;

// Pure PowerShell predicate, exercised without HTTP by the Setup release fixtures.
function ReleaseMetadataPolicy: String;
begin
  Result :=
    'if (-not $json.TrimStart().StartsWith(''{'')) { return $false }; ' +
    '$release = Microsoft.PowerShell.Utility\ConvertFrom-Json -InputObject $json; ' +
    '$root = ''https://github.com/Illidan0S/UpdateCenter/releases/''; ' +
    '$tag = ''v'' + $targetVersion; ' +
    'if ($release -is [array] -or $release.tag_name -isnot [string] -or $release.tag_name -cne $tag -or ' +
    '$release.draft -isnot [bool] -or $release.draft -or $release.prerelease -isnot [bool] -or $release.prerelease -or ' +
    '$release.html_url -isnot [string] -or $release.html_url -cne ($root + ''tag/'' + $tag) -or $release.assets -isnot [array]) { return $false }; ' +
    '$name = ''UpdateCenter-Setup-'' + $tag + ''.exe''; ' +
    '$expectedAssets = @(@($name, $setupHash, $setupSize), @(($name + ''.sha256''), $sidecarHash, $sidecarSize)); ' +
    'if ($targetVersion -ceq ''2.0.0'') { ' +
    'if ($installedVersion -cnotin @(''1.1.4.0'', ''1.1.5.0'')) { return $false } ' +
    '} else { ' +
    'if ($targetVersion -cnotmatch ''\A(?:0|[1-9][0-9]{0,4})(?:\.(?:0|[1-9][0-9]{0,4})){2}\z'' -or ' +
    '$installedVersion -cnotmatch ''\A(?:0|[1-9][0-9]{0,4})(?:\.(?:0|[1-9][0-9]{0,4})){2}\.0\z'' -or $installedVersion -ceq ''2.0.1.0'' -or ' +
    '[version]$installedVersion -lt [version]''2.0.0.0'' -or [version]($targetVersion + ''.0'') -le [version]''2.0.1.0'' -or ' +
    '[version]$installedVersion -ge [version]($targetVersion + ''.0'')) { return $false }; ' +
    'if ($markerSize -le 0 -or $markerSize -gt 4096 -or $markerJson.Length -ne $markerSize -or ' +
    '-not $markerJson.TrimStart().StartsWith(''{'') -or $markerJson.Contains(''\'') -or $markerJson.Split([char]34).Length -ne 19) { return $false }; ' +
    '$marker = Microsoft.PowerShell.Utility\ConvertFrom-Json -InputObject $markerJson; ' +
    '$keys = @($marker.psobject.Properties.Name); ' +
    'if ($keys.Count -ne 5) { return $false }; ' +
    'foreach ($key in @(''schema'',''repository'',''tag'',''portableSha256'',''setupSha256'')) { if ($keys -cnotcontains $key) { return $false } }; ' +
    'if (($marker.schema -isnot [int] -and $marker.schema -isnot [long]) -or $marker.schema -ne 1 -or $marker.repository -isnot [string] -or ' +
    '$marker.repository -cne ''Illidan0S/UpdateCenter'' -or $marker.tag -isnot [string] -or $marker.tag -cne $tag -or ' +
    '$marker.setupSha256 -isnot [string] -or $marker.setupSha256 -cne $setupHash.ToUpperInvariant() -or ' +
    '$marker.portableSha256 -isnot [string] -or $marker.portableSha256 -cnotmatch ''\A[A-F0-9]{64}\z'') { return $false }; ' +
    '$expectedAssets += ,@((''UpdateCenter-'' + $tag + ''-update.json''), $markerHash, $markerSize); ' +
    '$expectedAssets += ,@((''UpdateCenter-'' + $tag + ''-Portable.exe''), $marker.portableSha256.ToLowerInvariant(), $null); ' +
    '}; ' +
    'foreach ($expected in $expectedAssets) { ' +
    '$matchesFound = 0; foreach ($asset in $release.assets) { ' +
    'if ($asset.name -isnot [string] -or $asset.name -cne $expected[0]) { continue }; $matchesFound++; ' +
    'if ($asset.state -isnot [string] -or $asset.state -cne ''uploaded'' -or ' +
    '$asset.browser_download_url -isnot [string] -or $asset.browser_download_url -cne ($root + ''download/'' + $tag + ''/'' + $expected[0]) -or ' +
    '$asset.digest -isnot [string] -or $asset.digest -cne (''sha256:'' + $expected[1]) -or ' +
    '($asset.size -isnot [long] -and $asset.size -isnot [int]) -or $asset.size -le 0 -or $asset.size -gt 536870912 -or ' +
    '($null -ne $expected[2] -and $asset.size -ne $expected[2])) { return $false } }; ' +
    'if ($matchesFound -ne 1) { return $false } }; return $true';
end;

function VerifyReleaseMetadata(const SetupHash, SidecarHash, InstalledVersion: String; SetupSize: Int64; SidecarSize: Integer): Boolean;
var
  Script, MarkerBody, MarkerHash, MarkerHex, MarkerArgs: String;
  ExitCode, Index: Integer;
begin
  Result := False;
  // The version was read from the PE fixed version and checked against ARP.
  // Admit only digits and dots into the static command's quoted argument.
  if (Length(InstalledVersion) = 0) or (Length(InstalledVersion) > 23) then Exit;
  for Index := 1 to Length(InstalledVersion) do
    if Pos(InstalledVersion[Index], '0123456789.') = 0 then Exit;
  MarkerArgs := '';
#ifdef ApprovedUnsignedUpdateTag
  if not ReadOfficialText('/Illidan0S/UpdateCenter/releases/download/v{#UnsignedReleaseVersion}/UpdateCenter-v{#UnsignedReleaseVersion}-update.json',
    4096, MarkerBody) then Exit;
  MarkerHash := GetSHA256OfString(AnsiString(MarkerBody));
  // Only hex digits reach the process command line, including for untrusted JSON.
  MarkerHex := '';
  for Index := 1 to Length(MarkerBody) do
    MarkerHex := MarkerHex + HexDigits[(Ord(MarkerBody[Index]) div 16) + 1] + HexDigits[(Ord(MarkerBody[Index]) mod 16) + 1];
  MarkerArgs :=
    '$markerHash = ''' + LowerCase(MarkerHash) + '''; $markerSize = ' + IntToStr(Length(MarkerBody)) + '; ' +
    '$hex = ''' + MarkerHex + '''; $chars = [char[]]::new($markerSize); ' +
    'for ($i = 0; $i -lt $markerSize; $i++) { $chars[$i] = [char][Convert]::ToByte($hex.Substring($i*2,2),16) }; ' +
    '$markerJson = [string]::new($chars); ';
#endif
  Script :=
    '$ErrorActionPreference = ''Stop''; $env:PSModulePath = $PSHOME + ''\Modules''; ' +
    '$setupHash = ''' + LowerCase(SetupHash) + '''; $sidecarHash = ''' + LowerCase(SidecarHash) + '''; ' +
    '$targetVersion = ''{#UnsignedReleaseVersion}''; ' + MarkerArgs +
    '$installedVersion = ''' + InstalledVersion + '''; ' +
    '$setupSize = ' + IntToStr(SetupSize) + '; $sidecarSize = ' + IntToStr(SidecarSize) + '; ' +
    'try { ' +
    '[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; ' +
    '$request = [Net.WebRequest]::CreateHttp(''https://api.github.com/repos/Illidan0S/UpdateCenter/releases/tags/v{#UnsignedReleaseVersion}''); ' +
    '$request.Proxy = $null; $request.AllowAutoRedirect = $false; $request.Timeout = 10000; $request.ReadWriteTimeout = 4000; ' +
    '$request.UserAgent = ''UpdateCenter-Setup/{#UnsignedReleaseVersion}''; $request.Accept = ''application/vnd.github+json''; ' +
    '$response = $request.GetResponse(); if ([int]$response.StatusCode -ne 200 -or $response.ContentLength -gt 1048576) { exit 1 }; ' +
    '$stream = $response.GetResponseStream(); $bytes = [IO.MemoryStream]::new(); $buffer = [byte[]]::new(4096); ' +
    '$timer = [Diagnostics.Stopwatch]::StartNew(); ' +
    'while (($count = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) { ' +
    'if ($timer.ElapsedMilliseconds -gt 10000 -or $bytes.Length + $count -gt 1048576) { exit 1 }; $bytes.Write($buffer, 0, $count) }; ' +
    '$response.Close(); $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes.ToArray()); ' +
    'if (& { ' + ReleaseMetadataPolicy + ' }) { exit 0 } else { exit 1 } } catch { exit 1 }';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoLogo -NoProfile -NonInteractive -Command "' + Script + '"',
    ExpandConstant('{sys}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function VerifyOfficialSetupChecksum(const FileName, InstalledVersion: String): Boolean;
var
  ExpectedHash, SidecarHash, FileVersion: String;
  SidecarSize: Integer;
  Source: TFileStream;
begin
  Result := False;
  try
    if CompareText(ExtractFileName(FileName), ReleaseSetupName) <> 0 then Exit;
    // Retain a read handle denying writes and deletion until Setup finishes.
    Source := TFileStream.Create(FileName, fmOpenRead or fmShareDenyWrite);
    try
      if (Source.Size <= 0) or (Source.Size > 512 * 1024 * 1024) or
         not GetVersionNumbersString(FileName, FileVersion) or
         (FileVersion <> '{#UnsignedReleaseVersion}.0') or
         not ReadOfficialChecksum(ExpectedHash, SidecarHash, SidecarSize) or
         (CompareText(GetSHA256OfFile(FileName), ExpectedHash) <> 0) then Exit;
      if not VerifyReleaseMetadata(ExpectedHash, SidecarHash, InstalledVersion, Source.Size, SidecarSize) then Exit;
      VerifiedSetupSource := Source;
      Source := nil;
      Result := True;
    finally
      if Source <> nil then Source.Free;
    end;
  except
    Result := False;
    Log('The official Setup checksum could not be verified.');
  end;
end;
