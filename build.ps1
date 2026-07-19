[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot 'UpdateCenter.csproj'
$output = Join-Path $projectRoot 'dist'
$uninstallerTemplate = Join-Path $projectRoot 'Assets\PackageRemoval.template'
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
$packageVersion = if ($null -ne $versionNode) { $versionNode.InnerText.Trim() } else { '' }
if ($packageVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'La versione del progetto deve usare il formato MAJOR.MINOR.PATCH.'
}

$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw '.NET 8 SDK non trovato. Esegui CREA-EXE.bat.'
}

$sdkLines = @(& $dotnetCommand.Source --list-sdks 2>$null)
if ($LASTEXITCODE -ne 0 -or $sdkLines.Count -eq 0) {
    throw 'Il comando dotnet è presente, ma non risulta installato alcun SDK. Esegui nuovamente CREA-EXE.bat.'
}

$compatibleSdks = @($sdkLines | ForEach-Object {
    $versionToken = ($_ -split '\s+')[0]
    $parsedVersion = $null
    if ([Version]::TryParse($versionToken, [ref]$parsedVersion) -and $parsedVersion.Major -ge 8) {
        $parsedVersion
    }
})

if ($compatibleSdks.Count -eq 0) {
    throw "Serve .NET SDK 8 o successivo. SDK rilevati: $($sdkLines -join ', ')"
}

$versionText = ($compatibleSdks | Sort-Object -Descending | Select-Object -First 1).ToString()

Write-Host "Compilazione Update Center con .NET SDK $versionText..." -ForegroundColor Cyan

if (Test-Path $output) {
    Remove-Item -Path $output -Recurse -Force
}
New-Item -Path $output -ItemType Directory | Out-Null

if (-not $NoRestore) {
    & $dotnetCommand.Source restore $project --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Ripristino dei componenti .NET non riuscito.' }
}

& $dotnetCommand.Source publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita.' }

$exe = Join-Path $output 'UpdateCenter.exe'
if (-not (Test-Path $exe)) {
    throw 'La compilazione non ha prodotto UpdateCenter.exe.'
}

$readme = @"
UPDATE CENTER $packageVersion

COMPATIBILITA
- Windows 10 x64 versione 1809 (build 17763) o successiva.
- Windows 11 x64.

1. Avvia UpdateCenter.exe.
2. Premi "Avvia scansione".
3. Seleziona gli aggiornamenti desiderati.
4. Premi "Aggiorna elementi selezionati" e conferma la richiesta UAC.

Fonti utilizzate: WinGet, Windows/Microsoft Update e metadati verificati dei produttori.
Update Center non installa utility come Intel DSA, MSI Center, NVIDIA App o strumenti simili.
I pacchetti produttore automatici sono ammessi solo se contengono driver INF e superano
il controllo di dominio ufficiale, ID hardware, Windows/architettura, SHA-256 e firma.
BIOS e firmware restano esclusi dall'installazione automatica.
"@
Set-Content -Path (Join-Path $output 'LEGGIMI.txt') -Value $readme -Encoding UTF8
Copy-Item -LiteralPath $uninstallerTemplate -Destination (Join-Path $output 'UNINSTALLA.bat') -Force

Write-Host "`nOperazione completata: $exe" -ForegroundColor Green
