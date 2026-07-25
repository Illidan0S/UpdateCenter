# Sviluppo e pubblicazione

Questa pagina raccoglie le istruzioni tecniche per chi modifica o mantiene Update Center.

## Compilazione locale

Su Windows 10 o Windows 11 esegui `CREA-EXE.bat`. Lo script verifica la presenza di .NET SDK 8, propone l’installazione ufficiale se manca e crea l’eseguibile self-contained in `dist\UpdateCenter.exe`.

In alternativa:

```powershell
dotnet restore .\UpdateCenter.csproj --runtime win-x64
dotnet publish .\UpdateCenter.csproj --configuration Release --runtime win-x64 --self-contained true --output .\dist
```

La configurazione Release pubblica un singolo eseguibile x64 senza richiedere .NET sul PC di destinazione.

## Setup locale opzionale

Il Setup non fa parte delle Release pubbliche. Per crearlo localmente servono Inno Setup Compiler 6 o 7 e una build già presente in `dist`:

```powershell
.\build-installer.ps1 -NoAppBuild
```

Il risultato viene scritto in `installer-dist\UpdateCenter-Setup-vVERSIONE.exe` insieme al relativo SHA-256.

## Test

```powershell
dotnet run --project .\Tests\UpdateCenter.SmokeTests\UpdateCenter.SmokeTests.csproj --configuration Release --no-restore
```

I smoke test verificano, tra gli altri aspetti, versione semantica, impostazioni predefinite, classificazione dei runtime, riepilogo hardware, storage e pausa degli aggiornamenti.

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
4. Crea il tag stabile corrispondente, per esempio `v1.0.7`.
5. Verifica che la Release contenga:
   - `UpdateCenter-vVERSIONE.exe`;
   - `UpdateCenter-vVERSIONE.exe.sha256`;
   - `UpdateCenter-vVERSIONE-Portable.exe`.

Il workflow GitHub verifica versione e artefatti prima di pubblicare la Release. L’eseguibile standard e il suo checksum sono necessari perché la versione precedente possa aggiornarsi in modo sicuro.
