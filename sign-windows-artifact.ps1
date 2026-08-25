param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Artefatto da firmare non trovato: $Path"
}

$certificateBase64 = $env:WINDOWS_SIGNING_PFX_BASE64
$password = $env:WINDOWS_SIGNING_PFX_PASSWORD
if ([string]::IsNullOrWhiteSpace($certificateBase64) -or
    [string]::IsNullOrWhiteSpace($password)) {
    Write-Warning 'Code signing non configurato: artefatti pubblicati senza firma Authenticode.'
    exit 0
}

$tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [IO.Path]::GetTempPath()
} else {
    $env:RUNNER_TEMP
}
$pfxPath = Join-Path $tempRoot ("updatecenter-signing-" + [Guid]::NewGuid().ToString('N') + '.pfx')

try {
    [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($certificateBase64))

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

    Write-Host "Firma Authenticode verificata: $Path"
}
finally {
    if (Test-Path -LiteralPath $pfxPath) { Remove-Item -LiteralPath $pfxPath -Force }
}
