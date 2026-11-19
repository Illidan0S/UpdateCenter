# Sviluppo e pubblicazione

Questa pagina raccoglie le istruzioni tecniche per chi modifica o mantiene Update Center.

## Compilazione locale

Su Windows x64 esegui `CREA-EXE.bat`. Lo script verifica che `global.json` possa selezionare un SDK .NET 10 stabile (10.0.100 o una feature band 10.0 successiva), propone l’installazione tramite WinGet se manca e crea l’eseguibile self-contained in `dist\UpdateCenter.exe`.

In alternativa:

```powershell
.\build.ps1
```

La configurazione Release pubblica un singolo eseguibile x64 senza richiedere .NET sul PC di destinazione.

Gli output esistenti non vengono eliminati o sovrascritti. Per una nuova build:

```powershell
.\build.ps1 -OutputDirectory .\_output\prove\portable-01
```

Il percorso deve essere locale e sotto `_output`. Junction, symlink e tag reparse sconosciuti sono rifiutati; i placeholder Cloud OneDrive con tag verificato sono ammessi. La destinazione deve essere nuova; un errore conserva i file parziali per la diagnosi.

## Setup locale

Per riprodurre localmente il Setup delle release servono Inno Setup Compiler 6 o 7 e una build già presente in `dist`.

```powershell
.\build-installer.ps1 -NoAppBuild
```

Il risultato viene scritto in `installer-dist\UpdateCenter-Setup-vVERSIONE.exe` insieme al relativo SHA-256.

Per verifiche isolate usare `-AppSourceDirectory` e `-OutputDirectory`, per esempio:

```powershell
.\build-installer.ps1 -NoAppBuild -AppSourceDirectory .\_output\prove\portable-01 -OutputDirectory .\_output\prove\setup-01 -InnoCompilerPath C:\percorso\ISCC.exe
```

Il payload deve avere ProductVersion e FileVersion coerenti con il progetto. Non usare un vecchio `dist` per validare una nuova build. La firma di produzione è descritta in [CODE-SIGNING.md](CODE-SIGNING.md).

La Release Candidate è `2.0.0`. Il confronto byte per byte usa due build con lo stesso SDK, la stessa versione di PowerShell, lo stesso ISCC e gli stessi input. MSBuild normalizza i percorsi Release e il Setup registra una data UTC fissa per i tre file incorporati. Esegui il confronto prima della firma con timestamp.

## Test

Esegui i comandi dalla radice del repository. Tutti i modi smoke isolano i dati in una cartella GUID temporanea prima del primo I/O. Lo smoke completo legge l’hardware locale e usa Restart Manager soltanto sul proprio helper; in un sandbox queste API possono essere indisponibili. Conservare l’isolamento anche quando si autorizza un’esecuzione nativa.

```powershell
dotnet restore .\UpdateCenter.sln -p:Configuration=Release
dotnet build .\UpdateCenter.sln --configuration Release --no-restore
dotnet run --project .\Tests\UpdateCenter.NetworkCoreTests --configuration Release --no-build --no-restore
dotnet run --project .\Tests\UpdateCenter.SmokeTests --configuration Release --no-build --no-restore -- --release-audit
dotnet run --project .\Tests\UpdateCenter.SmokeTests --configuration Release --no-build --no-restore
```

I smoke test verificano, tra gli altri aspetti, versione semantica, impostazioni predefinite, classificazione dei runtime, riepilogo hardware, storage e pausa degli aggiornamenti.

I test Network Core verificano il protocollo locale a messaggi limitati, il lock a operazione singola, la retention e il recupero delle operazioni dopo un riavvio.

## Preview tecnica Network

La gestione LAN è inclusa ma disabilitata per impostazione predefinita. Per produrre la cartella self-contained:

```powershell
.\build-network-preview.ps1
```

Il risultato viene scritto in una nuova cartella GUID sotto `_output/network-preview`, indicata al termine. Una destinazione esplicita deve essere una nuova sottocartella diretta della stessa radice; directory esistenti, sorgenti e reparse point redirezionali o sconosciuti sono rifiutati. I placeholder Cloud OneDrive richiedono un tag verificato. Nessuna pulizia ricorsiva viene eseguita. Gli script inclusi sono:

- `install-agent-preview.ps1`: installa il servizio senza abilitare la rete;
- `enable-network-preview.ps1`: abilita discovery UDP e API HTTPS soltanto sul profilo firewall Privato;
- `disable-network-preview.ps1`: chiude la rete e rimuove le due regole firewall;
- `uninstall-agent-preview.ps1`: disabilita la rete e rimuove il servizio;
- `UpdateCenter.NetworkConsole.exe`: strumento tecnico per discovery, pairing, stato e scansione remota.

La rimozione conserva intenzionalmente binari e dati; la loro cancellazione rimane manuale.

## Struttura del progetto

- `MainWindow.xaml`: interfaccia WPF e pagine dell’app.
- `ViewModels/MainViewModel.cs`: stato, scansione, filtri, aggiornamenti e cronologia.
- `Services/WinGetService.cs`: aggiornamenti software.
- `Services/HardwareInventoryService.cs`: inventario driver e diagnostica PnP.
- `Services/StorageHealthService.cs`: salute delle unità e associazione dei volumi.
- `Services/GameDependencyService.cs`: rilevamento dei runtime condivisi.
- `Services/AppUpdateService.cs`: controllo, download e applicazione sicura degli aggiornamenti dell’app.
- `Services/ElevatedUpdateRunner.cs`: elevazione UAC, punto di ripristino e avanzamento.
- `Assets/driver-catalog.json`: metadati dei driver produttore; non contiene binari o mirror.
- `.github/workflows/release.yml`: compilazione e pubblicazione automatica delle Release.

## Pubblicazione di una versione

1. Aggiorna `Version`, `AssemblyVersion`, `FileVersion` e `InformationalVersion` in `UpdateCenter.csproj`, oltre al fallback visibile nell’interfaccia.
2. Esegui build e smoke test locali.
3. Pubblica le modifiche su `main`.
4. Crea il tag stabile corrispondente dopo l’approvazione della pubblicazione.
5. Verifica i quattro asset della Release `2.0.0`.
   - `UpdateCenter-Setup-v2.0.0.exe`
   - `UpdateCenter-Setup-v2.0.0.exe.sha256`
   - `UpdateCenter-v2.0.0-Portable.exe`
   - `UpdateCenter-v2.0.0-Portable.exe.sha256`

Il workflow GitHub verifica versione, firma e checksum prima di pubblicare la Release. I due eseguibili e i relativi sidecar mantengono i nomi attesi dall’updater 1.1.5.
