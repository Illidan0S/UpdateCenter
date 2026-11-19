$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $projectRoot 'scripts\BuildPaths.ps1')

if (!(Test-BuildCloudTag '9000601A') -or !(Test-BuildCloudTag '9000001A') -or
    (Test-BuildCloudTag 'A0000003') -or (Test-BuildCloudTag 'A000000C') -or
    (Test-BuildCloudTag '80000018') -or (Test-BuildCloudTag 'unknown')) {
    throw 'Cloud placeholders must be distinguished from path-redirection and unknown tags.'
}
Assert-NoBuildReparsePoint $projectRoot

# Files created by this fixture are retained; no recursive cleanup or publisher runs.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('UpdateCenter-build-paths-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'Agent'
$null = New-Item -ItemType Directory -Path $source
$marker = Join-Path $source 'source-marker.txt'
[IO.File]::WriteAllText($marker, 'preserve source')

foreach ($requested in @($fixture, (Split-Path -Parent $fixture), $source,
    (Join-Path $fixture '.git'), (Join-Path $fixture '_output\network-preview'),
    (Join-Path $fixture '_output\network-preview\..\Agent'),
    ($fixture + '-other\preview'), '\\server\share\preview')) {
    $rejected = $false
    try { $null = Resolve-BuildDirectory $fixture $requested 'Preview' }
    catch { $rejected = $true }
    if (!$rejected) { throw "Unsafe preview path accepted: $requested" }
}

$output = Resolve-BuildDirectory $fixture '' 'Preview'
$expectedParent = Join-Path $fixture '_output\network-preview'
if ([IO.Path]::GetDirectoryName($output) -ne $expectedParent -or (Test-Path -LiteralPath $output)) {
    throw 'The default preview output is not a fresh direct child.'
}
New-BuildDirectory $output
$artifact = Join-Path $output 'artifact-marker.txt'
[IO.File]::WriteAllText($artifact, 'preserve artifact')
$rejected = $false
try { New-BuildDirectory $output } catch { $rejected = $true }
if (!$rejected -or [IO.File]::ReadAllText($marker) -ne 'preserve source' -or
    [IO.File]::ReadAllText($artifact) -ne 'preserve artifact') {
    throw 'Reusing output changed existing files.'
}
Write-Output 'PASS build output paths: boundaries, traversal, fresh destination and preservation.'
