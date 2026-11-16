[CmdletBinding()]
param(
    [switch]$NoRestore,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot 'UpdateCenter.csproj'
. (Join-Path $projectRoot 'scripts\BuildPaths.ps1')
$output = Resolve-BuildDirectory $projectRoot $OutputDirectory 'Portable'
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
$packageVersion = if ($null -ne $versionNode) { $versionNode.InnerText.Trim() } else { '' }
if ($packageVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'La versione del progetto deve usare il formato MAJOR.MINOR.PATCH.'
}

$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw '.NET SDK 10 non trovato. Esegui CREA-EXE.bat.'
}

# La CLI cerca global.json a partire dalla directory di lavoro.
Push-Location -LiteralPath $projectRoot
try {
    $versionText = & $dotnetCommand.Source --version
    if ($LASTEXITCODE -ne 0) {
        throw 'Serve .NET SDK 10 stabile come indicato in global.json. Esegui CREA-EXE.bat.'
    }
    Write-Host "Compilazione Update Center con .NET SDK $versionText..." -ForegroundColor Cyan

    New-BuildDirectory $output
    if (-not $NoRestore) {
        & $dotnetCommand.Source restore $project --runtime win-x64 -p:Configuration=Release
        if ($LASTEXITCODE -ne 0) { throw 'Ripristino dei componenti .NET non riuscito.' }
    }

    & $dotnetCommand.Source publish $project `
        -m:1 -p:UseSharedCompilation=false `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $output `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita.' }
} finally {
    Pop-Location
}

$exe = Join-Path $output 'UpdateCenter.exe'
if (-not (Test-Path $exe)) {
    throw 'La compilazione non ha prodotto UpdateCenter.exe.'
}

$readme = @"
UPDATE CENTER $packageVersion

COMPATIBILITA
- Destinazione tecnica Windows x64 dalla build 17763. Il pacchetto include .NET 10 Desktop.
- Avvio e scansione osservati su Windows 11 Pro Insider (build 26320).
- Le release stabili di Windows 11 e le edizioni Windows 10 LTSC elencate da Microsoft per .NET 10 restano da validare per la 2.0.0.
- Per gli aggiornamenti software automatici serve App Installer con WinGet v1.29.290 o v1.30.300.
- UAC con runner elevato, installazione driver tramite Windows Update e aggiornamento dell'app restano da validare sul candidato 2.0.0.

1. Avvia UpdateCenter.exe.
2. Premi "Avvia scansione".
3. Seleziona gli aggiornamenti desiderati.
4. Premi "Aggiorna elementi selezionati" e conferma il riepilogo. Windows chiede l'elevazione UAC quando necessaria.

Fonti utilizzate: WinGet, Windows/Microsoft Update e metadati verificati dei produttori.
I runtime condivisi vengono mostrati negli Aggiornamenti con il filtro Runtime.
Per le GPU NVIDIA viene aperta NVIDIA App se installata; altrimenti viene aperta la pagina ufficiale.
La scansione principale controlla lo storage; ogni unità fisica occupa una riga con i volumi associati e il riepilogo esclude le unità USB.
Update Center non installa utility come Intel DSA, MSI Center, NVIDIA App o strumenti simili.
I pacchetti produttore automatici sono ammessi solo se contengono driver INF e superano
il controllo di dominio ufficiale, ID hardware, Windows/architettura, SHA-256 e firma.
BIOS e firmware restano esclusi dall'installazione automatica.
"@
Set-Content -Path (Join-Path $output 'LEGGIMI.txt') -Value $readme -Encoding UTF8

Write-Host "`nOperazione completata: $exe" -ForegroundColor Green
