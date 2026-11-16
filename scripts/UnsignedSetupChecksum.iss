// Included in [Code] only for the exact unsigned 2.0.0 build.
const
  ReleaseSetupName = 'UpdateCenter-Setup-v2.0.0.exe';
  ReleaseChecksumPath = '/Illidan0S/UpdateCenter/releases/download/v2.0.0/UpdateCenter-Setup-v2.0.0.exe.sha256';
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

function ReadChecksumResponse(Request: UINT_PTR; var Body: String): Boolean;
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
    if (Count > SizeOf(Buffer)) or (Length(Body) + Count > 68 + Length(ReleaseSetupName)) then Exit;
    for Index := 0 to Integer(Count) - 1 do
    begin
      if (Buffer[Index] <> 10) and (Buffer[Index] <> 13) and
         ((Buffer[Index] < 32) or (Buffer[Index] > 126)) then Exit;
      Body := Body + Chr(Buffer[Index]);
    end;
  until Count = 0;
  Result := True;
end;

function ReadOfficialChecksum(var Checksum: String): Boolean;
var
  Session, Connection, Request: UINT_PTR;
  Disabled, Size, Status: Cardinal;
  Host, Path, Body: String;
  Hop, Index: Integer;
begin
  Result := False;
  Host := 'github.com';
  Path := ReleaseChecksumPath;
  Session := HttpOpen('UpdateCenter-Setup/2.0.0', NoProxy, 0, 0, 0);
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
            if not ReadChecksumResponse(Request, Body) then Exit;
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

  // Match a single GitHub workflow checksum line and its exact asset name.
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

function VerifyOfficialSetupChecksum(const FileName: String): Boolean;
var
  ExpectedHash, FileVersion: String;
  Source: TFileStream;
begin
  Result := False;
  try
    if (CompareText(ExtractFileName(FileName), ReleaseSetupName) <> 0) or
       not ReadOfficialChecksum(ExpectedHash) then Exit;
    // Retain a read handle denying writes and deletion until Setup finishes.
    Source := TFileStream.Create(FileName, fmOpenRead or fmShareDenyWrite);
    try
      if (Source.Size <= 0) or (Source.Size > 512 * 1024 * 1024) or
         not GetVersionNumbersString(FileName, FileVersion) or
         (FileVersion <> '2.0.0.0') or
         (CompareText(GetSHA256OfFile(FileName), ExpectedHash) <> 0) then Exit;
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
