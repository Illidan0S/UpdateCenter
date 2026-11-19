[CmdletBinding()]
param(
    [switch]$NoAppBuild,
    [string]$InnoCompilerPath,
    [string]$AppSourceDirectory,
    [string]$OutputDirectory,
    [switch]$SkipChecksum
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot 'UpdateCenter.csproj'
$appBuildScript = Join-Path $projectRoot 'build.ps1'
$installerScript = Join-Path $projectRoot 'installer.iss'
. (Join-Path $projectRoot 'scripts\BuildPaths.ps1')
$appSource = Resolve-BuildDirectory $projectRoot $AppSourceDirectory 'Portable'
$appExecutable = Join-Path $appSource 'UpdateCenter.exe'
$installerOutput = Resolve-BuildDirectory $projectRoot $OutputDirectory 'Setup'

[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
$packageVersion = if ($null -ne $versionNode) { $versionNode.InnerText.Trim() } else { '' }
if ($packageVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'La versione del progetto deve usare il formato MAJOR.MINOR.PATCH.'
}
$unsignedPolicy = $projectXml.SelectSingleNode('/Project/PropertyGroup/UnsignedReleaseVersion')
$compilerOptions = @()
if ($packageVersion -cin @('2.0.0', '2.0.1') -and $unsignedPolicy -and $unsignedPolicy.InnerText -ceq $packageVersion -and
    [string]::IsNullOrEmpty($env:WINDOWS_SIGNING_CERT_SHA256)) {
    $compilerOptions += "/DUnsignedReleaseVersion=$packageVersion"
}

if (-not $NoAppBuild) {
    & $appBuildScript -OutputDirectory $appSource
}

Assert-NoBuildReparsePoint $appExecutable
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "Payload assente: $appExecutable. Esegui prima build.ps1 oppure ometti -NoAppBuild."
}
$payloadVersion = (Get-Item -LiteralPath $appExecutable).VersionInfo
if ($payloadVersion.ProductVersion -ne $packageVersion -or $payloadVersion.FileVersion -ne "$packageVersion.0") {
    throw "Versione payload inattesa: ProductVersion=$($payloadVersion.ProductVersion), FileVersion=$($payloadVersion.FileVersion)."
}
Assert-NoBuildReparsePoint (Join-Path $appSource 'LEGGIMI.txt')

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $knownPaths = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $InnoCompilerPath = $command.Source
    } else {
        $InnoCompilerPath = $knownPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or -not (Test-Path -LiteralPath $InnoCompilerPath)) {
    throw 'Inno Setup Compiler 6 o 7 non trovato. Installalo da https://jrsoftware.org/isdl.php e riprova.'
}

New-BuildDirectory $installerOutput
& $InnoCompilerPath @compilerOptions "/DMyAppVersion=$packageVersion" "/DAppPayloadDir=$appSource" "/O$installerOutput" $installerScript
if ($LASTEXITCODE -ne 0) { throw 'La creazione dell installer EXE non e riuscita.' }

$installer = Join-Path $installerOutput "UpdateCenter-Setup-v$packageVersion.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Il compilatore non ha prodotto $installer."
}

$versionInfo = (Get-Item -LiteralPath $installer).VersionInfo
$installerProductVersion = $versionInfo.ProductVersion.Trim()
if ($installerProductVersion -ne $packageVersion) {
    throw "Versione installer inattesa: $($versionInfo.ProductVersion)"
}

if (!$SkipChecksum) {
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$installer.sha256" -Value "$hash  $(Split-Path -Leaf $installer)" -Encoding ascii
    Write-Host "SHA-256: $hash" -ForegroundColor Green
}

Write-Host "`nInstaller creato: $installer" -ForegroundColor Green
