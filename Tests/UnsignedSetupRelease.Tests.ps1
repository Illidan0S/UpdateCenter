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
if ($commands.Count -ne 1 -or $commands[0].GetCommandName() -cne 'Microsoft.PowerShell.Utility\ConvertFrom-Json') {
    throw 'The release predicate must use only the system JSON parser.'
}

$setupHash = 'a' * 64
$sidecarHash = 'b' * 64
$setupSize = 300
$sidecarSize = 98
$name = 'UpdateCenter-Setup-v2.0.1.exe'
$root = 'https://github.com/Illidan0S/UpdateCenter/releases/'
$cases = @('valid', 'wrongTag', 'draft', 'prerelease', 'missingDraft', 'stringDraft', 'otherPage',
    'noExecutable', 'duplicateExecutable', 'foreignAsset', 'wrongDigest', 'wrongSize',
    'noSidecar', 'duplicateSidecar', 'wrongSidecarDigest', 'wrongSidecarSize', 'unuploaded', 'wrongRoot', 'invalidJson')
foreach ($case in $cases) {
    $exe = @{ name = $name; state = 'uploaded'; size = $setupSize; digest = "sha256:$setupHash";
        browser_download_url = $root + 'download/v2.0.1/' + $name }
    $side = @{ name = $name + '.sha256'; state = 'uploaded'; size = $sidecarSize; digest = "sha256:$sidecarHash";
        browser_download_url = $exe.browser_download_url + '.sha256' }
    $release = @{ tag_name = 'v2.0.1'; html_url = $root + 'tag/v2.0.1'; draft = $false; prerelease = $false;
        body = 'Text mentioning "draft": true and {"digest":"untrusted"}'; assets = @($exe, $side) }
    switch ($case) {
        wrongTag { $release.tag_name = 'v2.0.2' }
        draft { $release.draft = $true }
        prerelease { $release.prerelease = $true }
        missingDraft { $release.Remove('draft') }
        stringDraft { $release.draft = 'false' }
        otherPage { $release.html_url = $root + 'tag/v2.0.0' }
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
    if ($accepted -isnot [bool] -or $accepted -ne ($case -eq 'valid')) { throw "Unexpected admission for $case." }
    Write-Output "PASS $case"
}
