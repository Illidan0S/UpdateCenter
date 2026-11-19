param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [switch]$VerifyOnly,
    [switch]$AllowUnsignedDevelopment
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Artefatto da firmare non trovato: $Path"
}

$certificateBase64 = $env:WINDOWS_SIGNING_PFX_BASE64
$password = $env:WINDOWS_SIGNING_PFX_PASSWORD
$expectedFingerprint = $env:WINDOWS_SIGNING_CERT_SHA256
if ($AllowUnsignedDevelopment -and ($env:RELEASE_TAG -or $env:GITHUB_REF_TYPE -eq 'tag' -or $env:GITHUB_REF -like 'refs/tags/*')) {
    throw 'Una release con tag richiede la firma.'
}
if ($AllowUnsignedDevelopment -and !$VerifyOnly -and !$certificateBase64 -and !$password -and !$expectedFingerprint) {
    Write-Warning 'Build di sviluppo esplicitamente senza firma.'
    return
}
if ($expectedFingerprint -notmatch '^[0-9a-fA-F]{64}\z') {
    throw 'Configurare WINDOWS_SIGNING_CERT_SHA256 con 64 cifre esadecimali.'
}
. (Join-Path $PSScriptRoot 'scripts\Assert-ArtifactSignature.ps1')
function Test-SignedArtifact {
    $signature = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $Path
    Assert-ArtifactSignature $signature $Path $expectedFingerprint
}
if ($VerifyOnly) { Test-SignedArtifact; return }
if ([string]::IsNullOrWhiteSpace($certificateBase64) -or [string]::IsNullOrWhiteSpace($password)) {
    throw 'Configurare entrambe le credenziali WINDOWS_SIGNING_PFX_BASE64 e WINDOWS_SIGNING_PFX_PASSWORD.'
}

$tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [IO.Path]::GetTempPath()
} else {
    $env:RUNNER_TEMP
}
$pfxPath = Join-Path $tempRoot ("updatecenter-signing-" + [Guid]::NewGuid().ToString('N') + '.pfx')

$certificateBytes = [Convert]::FromBase64String($certificateBase64)
$pfx = [IO.File]::Open($pfxPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    try { $pfx.Write($certificateBytes, 0, $certificateBytes.Length) }
    finally { $pfx.Dispose() }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signtool = Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -File -Recurse |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($null -eq $signtool) { throw 'signtool.exe x64 non trovato nel Windows SDK.' }

    & $signtool.FullName sign /fd SHA256 /td SHA256 /tr 'https://timestamp.digicert.com' /f $pfxPath /p $password $Path
    if ($LASTEXITCODE -ne 0) { throw "Firma Authenticode non riuscita (exit code $LASTEXITCODE)." }

    & $signtool.FullName verify /pa /all $Path
    if ($LASTEXITCODE -ne 0) { throw "Verifica Authenticode non riuscita (exit code $LASTEXITCODE)." }

    Test-SignedArtifact
}
finally {
    [IO.File]::Delete($pfxPath)
}
