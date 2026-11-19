param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
. (Join-Path $projectRoot 'scripts\BuildPaths.ps1')
$resolvedOutput = Resolve-BuildDirectory $projectRoot $OutputDirectory 'Preview'
New-BuildDirectory $resolvedOutput
dotnet publish (Join-Path $projectRoot "Agent\UpdateCenter.Agent.csproj") `
    -m:1 -p:UseSharedCompilation=false `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $resolvedOutput
if ($LASTEXITCODE -ne 0) { throw "Pubblicazione Update Center Agent non riuscita." }

dotnet publish (Join-Path $projectRoot "NetworkConsole\UpdateCenter.NetworkConsole.csproj") `
    -m:1 -p:UseSharedCompilation=false `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $resolvedOutput
if ($LASTEXITCODE -ne 0) { throw "Pubblicazione Update Center Network Console non riuscita." }

dotnet publish (Join-Path $projectRoot "UpdateCenter.csproj") `
    -m:1 -p:UseSharedCompilation=false `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    --output $resolvedOutput
if ($LASTEXITCODE -ne 0) { throw "Pubblicazione interfaccia Update Center non riuscita." }

Copy-Item -LiteralPath (Join-Path $projectRoot "Agent\install-agent-preview.ps1") -Destination $resolvedOutput -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "Agent\uninstall-agent-preview.ps1") -Destination $resolvedOutput -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "Agent\enable-network-preview.ps1") -Destination $resolvedOutput -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "Agent\disable-network-preview.ps1") -Destination $resolvedOutput -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "Agent\setup-agent-preview.ps1") -Destination $resolvedOutput -Force

$requiredFiles = @(
    "UpdateCenter.Agent.exe",
    "UpdateCenter.SessionHelper.exe",
    "UpdateCenter.NetworkConsole.exe",
    "UpdateCenter.exe",
    "UpdateCenter.dll",
    "UpdateCenter.Contracts.dll",
    "UpdateCenter.Core.dll",
    "install-agent-preview.ps1",
    "uninstall-agent-preview.ps1",
    "enable-network-preview.ps1",
    "disable-network-preview.ps1",
    "setup-agent-preview.ps1"
)
foreach ($required in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedOutput $required))) {
        throw "Artefatto mancante: $required"
    }
}

$readme = @"
UPDATE CENTER - PREVIEW GESTIONE RETE

PC PRINCIPALE
1. Avvia UpdateCenter.exe normalmente.
2. Apri "Gestione rete".
3. Premi "Cerca PC": i nuovi dispositivi disponibili vengono selezionati automaticamente.
4. Premi "Richiedi collegamento" e verifica per ciascun PC l'impronta SHA-256 mostrata localmente in "Configura questo PC", tramite un canale fidato. Poi attendi l'approvazione sui dispositivi.
5. Usa il pulsante "Codice" come metodo alternativo con il codice temporaneo a 8 cifre e la stessa verifica dell'impronta.
6. Avvia scansioni o aggiornamenti sui PC autorizzati e seguine lo stato dalla tabella.

PC DA CONTROLLARE
1. Avvia UpdateCenter.exe e apri "Gestione rete".
2. Premi "Configura questo PC" e accetta la richiesta UAC.
3. Premi "Rendi questo PC gestibile".
4. Il PC diventa rilevabile. Comunica l'impronta SHA-256 mostrata in questa finestra al PC principale tramite un canale fidato; il solo rilevamento in rete non autentica il PC.
5. Quando compare la notifica, controlla il nome del PC principale e scegli "Consenti" o "Rifiuta".
6. Puoi interrompere le nuove richieste in qualsiasi momento; in alternativa usa il codice monouso a 8 cifre.

Gli script PowerShell restano disponibili nella cartella per diagnostica e rimozione manuale.

Non serve modificare il profilo Pubblico/Privato di Windows. Update Center limita automaticamente
Componente di rete, porte e firewall alla LAN autorizzata. Ethernet e Wi-Fi sono supportati sulla stessa LAN.
Questa preview consente ricerca, richieste di collegamento approvate dall'utente, scansioni concorrenti e aggiornamenti remoti con avanzamento.
Prima dell'installazione mostra dimensioni, spazio e alimentazione separati per PC e permette di escludere i pacchetti con rimozione preventiva.
Gli aggiornamenti possono essere avviati soltanto dai risultati di una scansione recente. Per alcuni driver
Windows pu$([char]0x00F2) mostrare una conferma UAC sul PC gestito.

RIPARAZIONE DRIVER LOCALE
Nella pagina Driver e chipset, i problemi come il Codice 31 possono essere gestiti dalla tabella Diagnosi.
Update Center reinstalla soltanto INF OEM firmati gi$([char]0x00E0) registrati da Windows; se non sono disponibili, avvia la ricerca di un driver verificato.

DISATTIVAZIONE O RIMOZIONE
Apri "Configura questo PC" dall'app e scegli "Disabilita gestione remota" oppure "Disinstalla componente di rete".
Gli script PowerShell equivalenti restano disponibili per la diagnostica.
"@
Set-Content -LiteralPath (Join-Path $resolvedOutput "LEGGIMI-PREVIEW-RETE.txt") -Value $readme -Encoding UTF8

Write-Host "Preview locale creata in: $resolvedOutput"
Write-Host "La gestione LAN è inclusa ma resta disabilitata finché l'utente non la autorizza dall'app."
