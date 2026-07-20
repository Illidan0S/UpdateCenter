[CmdletBinding()]
param(
    [switch]$NoAppBuild,
    [string]$InnoCompilerPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot 'UpdateCenter.csproj'
$appBuildScript = Join-Path $projectRoot 'build.ps1'
$installerScript = Join-Path $projectRoot 'installer.iss'
$appExecutable = Join-Path $projectRoot 'dist\UpdateCenter.exe'
$installerOutput = Join-Path $projectRoot 'installer-dist'

[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
$packageVersion = if ($null -ne $versionNode) { $versionNode.InnerText.Trim() } else { '' }
if ($packageVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'La versione del progetto deve usare il formato MAJOR.MINOR.PATCH.'
}

if (-not $NoAppBuild) {
    & $appBuildScript
    if ($LASTEXITCODE -ne 0) { throw 'La compilazione di Update Center non e riuscita.' }
}

if (-not (Test-Path -LiteralPath $appExecutable)) {
    throw 'dist\UpdateCenter.exe non esiste. Esegui prima build.ps1 oppure ometti -NoAppBuild.'
}

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

if (Test-Path -LiteralPath $installerOutput) {
    Remove-Item -LiteralPath $installerOutput -Recurse -Force
}

& $InnoCompilerPath "/DMyAppVersion=$packageVersion" $installerScript
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

$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
Set-Content -LiteralPath "$installer.sha256" -Value "$hash  $(Split-Path -Leaf $installer)" -Encoding ascii

Write-Host "`nInstaller creato: $installer" -ForegroundColor Green
Write-Host "SHA-256: $hash" -ForegroundColor Green
