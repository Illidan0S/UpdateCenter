[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ne 5) { throw 'Run this fixture using Windows PowerShell 5.1, as Setup does.' }

# Read only the production JSON predicate. The network and Setup entry points are never called.
$file = Join-Path $PSScriptRoot '..\scripts\UnsignedSetupChecksum.iss'
$source = Get-Content -LiteralPath $file -Raw
$match = [regex]::Match($source, '(?ms)^function ReleaseMetadataPolicy: String;\s+begin\s+Result :=\s*(?<body>.*?)^end;')
if (-not $match.Success) { throw 'The Setup verification predicate was not found.' }
$parts = foreach ($line in ($match.Groups['body'].Value.Trim() -split '\r?\n')) {
    if ($line.Trim() -notmatch "^'(?<literal>(?:[^']|'')*)'\s*(?:\+|;)$") { throw "Unsupported Pascal literal: $line" }
    $Matches.literal.Replace("''", "'")
}
$policy = [scriptblock]::Create(($parts -join ''))
$commands = @($policy.Ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true))
if ($commands.Count -ne 2 -or @($commands | Where-Object { $_.GetCommandName() -cne 'Microsoft.PowerShell.Utility\ConvertFrom-Json' }).Count -gt 0) {
    throw 'The release predicate must use only the system JSON parser.'
}

$setupHash = 'a' * 64
$sidecarHash = 'b' * 64
$setupSize = 300
$sidecarSize = 98
$targetVersion = '2.0.0'
$name = 'UpdateCenter-Setup-v2.0.0.exe'
$root = 'https://github.com/Illidan0S/UpdateCenter/releases/'
$cases = @('valid', 'previous114', 'previous113', 'sameVersion', 'downgrade', 'wrongTag', 'draft', 'prerelease', 'missingDraft', 'stringDraft', 'otherPage',
    'noExecutable', 'duplicateExecutable', 'foreignAsset', 'wrongDigest', 'wrongSize',
    'noSidecar', 'duplicateSidecar', 'wrongSidecarDigest', 'wrongSidecarSize', 'unuploaded', 'wrongRoot', 'invalidJson')
foreach ($case in $cases) {
    $installedVersion = '1.1.5.0'
    $exe = @{ name = $name; state = 'uploaded'; size = $setupSize; digest = "sha256:$setupHash";
        browser_download_url = $root + 'download/v2.0.0/' + $name }
    $side = @{ name = $name + '.sha256'; state = 'uploaded'; size = $sidecarSize; digest = "sha256:$sidecarHash";
        browser_download_url = $exe.browser_download_url + '.sha256' }
    $release = @{ tag_name = 'v2.0.0'; html_url = $root + 'tag/v2.0.0'; draft = $false; prerelease = $false;
        body = 'Text mentioning "draft": true and {"digest":"untrusted"}'; assets = @($exe, $side) }
    switch ($case) {
        previous114 { $installedVersion = '1.1.4.0' }
        previous113 { $installedVersion = '1.1.3.0' }
        sameVersion { $installedVersion = '2.0.0.0' }
        downgrade { $installedVersion = '2.0.1.0' }
        wrongTag { $release.tag_name = 'v2.0.1' }
        draft { $release.draft = $true }
        prerelease { $release.prerelease = $true }
        missingDraft { $release.Remove('draft') }
        stringDraft { $release.draft = 'false' }
        otherPage { $release.html_url = $root + 'tag/v2.0.1' }
        noExecutable { $release.assets = @($side) }
        duplicateExecutable { $release.assets += $exe }
        foreignAsset { $exe.browser_download_url = 'https://example.invalid/setup' }
        wrongDigest { $exe.digest = $side.digest }
        wrongSize { $exe.size++ }
        noSidecar { $release.assets = @($exe) }
        duplicateSidecar { $release.assets += $side }
        wrongSidecarDigest { $side.digest = $exe.digest }
        wrongSidecarSize { $side.size = "$sidecarSize" }
        unuploaded { $side.state = 'new' }
    }
    $json = $release | ConvertTo-Json -Compress -Depth 4
    if ($case -eq 'wrongRoot') { $json = '[' + $json + ']' }
    if ($case -eq 'invalidJson') { $json = '{broken' }
    try { $accepted = & $policy } catch { $accepted = $false }
    if ($accepted -isnot [bool] -or $accepted -ne ($case -in @('valid', 'previous114'))) { throw "Unexpected admission for $case." }
    Write-Output "PASS $case"
}

# Run the same predicate with the opt-in marker and immutable input bytes.
$targetVersion = '2.0.3'
$name = 'UpdateCenter-Setup-v2.0.3.exe'
$portableName = 'UpdateCenter-v2.0.3-Portable.exe'
$markerName = 'UpdateCenter-v2.0.3-update.json'
$portableHash = 'c' * 64
foreach ($case in @('optIn','fromApproved','fromLegacy','from201','same','downgrade','missingMarker','duplicateMarker','foreignMarker','wrongMarkerHash','wrongMarkerSize','wrongPortableHash','foreignPortable','duplicatePortable','wrongMarkerRepo','wrongMarkerTag','unsignedMarkerSchema','extraMarkerField','lowercaseMarkerHash','wrongMarkerSetup','badMarkerJson','duplicateMarkerKey','oversizedPortable')) {
    $installedVersion = '2.0.0.0'
    $marker = [ordered]@{schema=1;repository='Illidan0S/UpdateCenter';tag='v2.0.3';portableSha256=$portableHash.ToUpperInvariant();setupSha256=$setupHash.ToUpperInvariant()}
    switch ($case) {
        fromApproved { $installedVersion = '2.0.2.0' }
        fromLegacy { $installedVersion = '1.1.5.0' }
        from201 { $installedVersion = '2.0.1.0' }
        same { $installedVersion = '2.0.3.0' }
        downgrade { $installedVersion = '2.0.4.0' }
        wrongMarkerRepo { $marker.repository='other/UpdateCenter' }
        wrongMarkerTag { $marker.tag='v2.0.4' }
        unsignedMarkerSchema { $marker.schema='1' }
        extraMarkerField { $marker.Add('extra',0) }
        lowercaseMarkerHash { $marker.setupSha256=$setupHash }
        wrongMarkerSetup { $marker.setupSha256='D' * 64 }
    }
    $markerJson = $marker | ConvertTo-Json -Compress
    if ($case -eq 'badMarkerJson') { $markerJson='{' }
    if ($case -eq 'duplicateMarkerKey') { $markerJson = $markerJson.Replace('"schema":1', '"schema":0,"schema":1') }
    $markerBytes = [Text.Encoding]::UTF8.GetBytes($markerJson)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $markerHash=[BitConverter]::ToString($sha.ComputeHash($markerBytes)).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() }
    $markerSize = $markerBytes.Length
    $tag = "v$targetVersion"
    $assets = @(foreach ($entry in @(@($name,$setupHash,$setupSize),@(($name+'.sha256'),$sidecarHash,$sidecarSize),@($markerName,$markerHash,$markerSize),@($portableName,$portableHash,123))) {
        @{name=$entry[0];digest=('sha256:'+$entry[1]);size=$entry[2];state='uploaded';browser_download_url=($root+'download/'+$tag+'/'+$entry[0])}
    })
    switch ($case) {
        missingMarker { $assets=@($assets[0],$assets[1],$assets[3]) }
        duplicateMarker { $assets += $assets[2] }
        foreignMarker { $assets[2].browser_download_url='https://example.invalid/update.json' }
        wrongMarkerHash { $assets[2].digest='sha256:'+('f'*64) }
        wrongMarkerSize { $assets[2].size++ }
        wrongPortableHash { $assets[3].digest='sha256:'+('f'*64) }
        foreignPortable { $assets[3].browser_download_url='https://example.invalid/portable' }
        duplicatePortable { $assets += $assets[3] }
        oversizedPortable { $assets[3].size=536870913 }
    }
    $json=@{tag_name=$tag;html_url=$root+'tag/'+$tag;draft=$false;prerelease=$false;assets=$assets} | ConvertTo-Json -Compress -Depth 4
    try { $accepted = & $policy } catch { $accepted=$false }
    if ($accepted -isnot [bool] -or $accepted -ne ($case -in @('optIn','fromApproved'))) { throw "Unexpected opt-in admission for $case." }
    Write-Output "PASS $case"
}
