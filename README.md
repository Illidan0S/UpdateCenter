# Update Center

Versione pubblica attuale: **1.0.2** (`v1.0.2`).

Applicazione desktop per Windows 10 e Windows 11 che cerca aggiornamenti software tramite **WinGet** e driver tramite **Windows Update Agent** più un catalogo incorporato e trasparente di metadati verificati dei produttori. L'utente sceglie singolarmente cosa installare.

La sezione **Driver e chipset** crea inoltre un inventario locale dei driver PnP installati, inclusi ID hardware e ID compatibili, associa quando possibile la versione installata a quella proposta e riconosce i componenti CPU/chipset. Le schede dei produttori sono generate soltanto dall'hardware realmente rilevato: un modello preciso riceve la sua pagina precisa; se l'abbinamento non è certo viene mostrato il portale generico ufficiale.

Il controllo Windows Update è automatico. Il catalogo Update Center può proporre un pacchetto esterno soltanto quando contiene URL ufficiale, ID hardware esatto, versione, compatibilità Windows/architettura, SHA-256 e firmatari attesi. Le fonti che non offrono metadati ufficiali interrogabili in sicurezza restano indicate come **controllo manuale ufficiale**, senza inventare un aggiornamento. Le voci duplicate con stesso dispositivo, produttore e versione vengono raggruppate per rendere l'inventario più leggibile.

## Quale file usare

Per installare normalmente il programma usa **`UpdateCenter-Setup-vVERSIONE.exe`** dalla sezione GitHub Releases. È un installer grafico per utente: non richiede il compilatore .NET né privilegi amministrativi, copia l'app in `%LOCALAPPDATA%\Programs\UpdateCenter`, aggiunge il collegamento al menu Start e registra il disinstallatore `.exe` in **App installate** di Windows.

`UpdateCenter-vVERSIONE.exe` è invece l'eseguibile portatile e viene mantenuto anche perché l'aggiornamento automatico dell'app usa questo asset verificato. Può essere avviato direttamente, ma non crea collegamenti né una voce di disinstallazione.

Usa **`CREA-EXE.bat`** soltanto se hai modificato il codice sorgente o vuoi rigenerare personalmente l'eseguibile portatile. È uno strumento per sviluppatori, non un installer distribuito agli utenti.

### Creazione dell'eseguibile

Su Windows 10 o Windows 11 fai doppio clic su `CREA-EXE.bat`.

Lo script:

1. controlla che sia realmente installato un SDK tramite `dotnet --list-sdks` (la sola presenza del runtime non è sufficiente);
2. propone l'installazione ufficiale tramite WinGet se il compilatore manca;
3. compila una versione autonoma per Windows x64;
4. crea `dist\UpdateCenter.exe`.

L'eseguibile pubblicato è **self-contained**: una volta creato, il PC che lo esegue non deve avere .NET installato.

In alternativa agli script, dalla radice del progetto puoi compilare direttamente con:

```powershell
dotnet restore .\UpdateCenter.csproj --runtime win-x64
dotnet publish .\UpdateCenter.csproj --configuration Release --runtime win-x64 --self-contained true --output .\dist
```

La configurazione Release abilita la pubblicazione single-file per `win-x64` senza trimming.

Per creare anche il Setup `.exe` serve Inno Setup Compiler 6 o 7. Dopo `build.ps1` esegui:

```powershell
.\build-installer.ps1 -NoAppBuild
```

Il risultato viene scritto in `installer-dist\UpdateCenter-Setup-vVERSIONE.exe`, insieme al relativo SHA-256. La disinstallazione avviene da **Impostazioni > App > App installate** oppure dal disinstallatore `.exe` creato da Windows. Al termine viene chiesto separatamente se eliminare anche impostazioni, cronologia e log locali.

## Utilizzo

1. Avvia `UpdateCenter.exe`.
2. Premi **Avvia scansione**.
3. Apri **Aggiornamenti** e controlla gli elementi trovati.
4. Seleziona solo quelli desiderati.
5. Premi **Aggiorna elementi selezionati**.
6. Conferma il riepilogo e la richiesta UAC di Windows.

L'app non riavvia il computer senza chiedere conferma.

La pagina **Aggiornamenti** permette di cercare per nome, produttore, versione, fonte o stato e di filtrare software, driver, elementi importanti, standard, facoltativi, selezionati, aggiornamenti con riavvio ed errori. Ogni elemento mostra una priorità chiara e, dopo l'installazione, un esito individuale; gli elementi non riusciti espongono il pulsante **Riprova**.

Prima dell'elevazione amministratore viene mostrato un riepilogo completo con software, driver, aggiornamenti importanti, riavvii, avvisi preliminari e stato del punto di ripristino. La Home conserva inoltre data e ora dell'ultima scansione completata.

La schermata **Hardware** mostra CPU, core/thread, GPU, VRAM, RAM, risoluzione, frequenza dello schermo, modello del PC e versione di Windows. I dati statici vengono letti separatamente tramite WMI, API Windows e registro, con fallback indipendenti: un componente non disponibile non nasconde più tutti gli altri. Utilizzo CPU, RAM e GPU vengono aggiornati ogni tre secondi mentre la schermata è aperta. Le temperature vengono mostrate soltanto se il firmware, il driver NVIDIA oppure un provider sensori già presente le espone a Windows; l'app non installa driver di monitoraggio. Tutti i valori principali sono selezionabili e il riepilogo completo può essere copiato negli appunti. È incluso un collegamento rapido a Gestione attività.

Il tema può essere impostato su **Sistema**, **Chiaro** o **Scuro**; alla prima installazione sono preselezionati **Chiaro** e testo **Medio**. In modalità Sistema l'app segue il tema delle applicazioni Windows anche quando cambia durante l'esecuzione. La dimensione del testo può essere impostata su **Piccola**, **Media** o **Grande**. La finestra è ridimensionabile da bordi e angoli; sotto gli 820 pixel la navigazione passa automaticamente alla modalità compatta a icone.

La pagina **Cronologia** usa descrizioni semplici per indicare esito, versioni, fonte e necessità di riavvio. I dettagli lunghi vengono abbreviati nella tabella; mantenendo il puntatore sul testo per un secondo si apre un pannello con il contenuto completo, selezionabile e copiabile, inclusa la diagnostica WinGet quando disponibile.

L'interfaccia può essere usata in italiano o inglese. Le Impostazioni consentono inoltre notifiche locali e scansioni giornaliere o settimanali mentre Update Center è aperto, oppure al successivo avvio quando la scansione è scaduta. L'installazione non parte mai automaticamente.

## Fonti e comportamento

- Software: comando ufficiale `winget upgrade`, un pacchetto alla volta. Prima dell'installazione viene ricontrollata la corrispondenza; i pacchetti per utente restano nel contesto dell'utente e WinGet può ritentare senza vincolo di sorgente o tramite un nome esatto e univoco.
- Driver Microsoft: aggiornamenti firmati e compatibili offerti da Windows Update al PC specifico.
- Driver produttore: soltanto pacchetti ZIP/CAB composti da driver INF e autorizzati dal catalogo incorporato. Prima dell'installazione vengono ricontrollati dominio ufficiale, ID hardware, Windows/architettura, SHA-256 e firma Authenticode del catalogo `.cat`.
- CPU/chipset: inventario delle componenti di sistema e accesso al controllo ufficiale AMD o Intel rilevato sul PC.
- BIOS, UEFI e firmware: esclusi dall'installazione automatica.
- Versioni preview: non vengono richieste dall'app.
- Ripristino: se l'opzione è attiva, l'app richiede un solo punto di ripristino per l'intero gruppo soltanto quando sono presenti driver o aggiornamenti importanti. I gruppi composti unicamente da software WinGet non creano punti. Windows può rifiutare la richiesta se Protezione sistema è disattivata o per le proprie regole. Dalle Impostazioni è possibile aprire direttamente il pannello Windows che mostra e limita lo spazio utilizzato.
- Log e cronologia: `%LOCALAPPDATA%\UpdateCenter`.
- Pulizia: i file temporanei di installazione vengono eliminati al termine; eventuali residui più vecchi di un giorno e i log più vecchi di 30 giorni vengono rimossi all'avvio. Ogni log giornaliero è limitato a 2 MB.
- Controlli preliminari: prima dell'installazione la finestra di riepilogo verifica alimentazione e spazio libero. Driver e aggiornamenti importanti vengono bloccati con batteria al 25% o inferiore; l'operazione viene inoltre bloccata quando lo spazio è inferiore alla stima minima necessaria.

Non è tecnicamente possibile garantire che *ogni* programma installato sia aggiornabile: WinGet può gestire soltanto i programmi che riesce ad associare a un pacchetto disponibile. Per i driver, il controllo automatico è possibile soltanto quando Windows Update o il produttore espongono metadati verificabili; negli altri casi viene offerta la pagina ufficiale corretta per il controllo manuale.

Update Center non usa il catalogo proprietario di Driver Easy e non presenta come aggiornamento un pacchetto che Windows o il produttore non hanno confermato per il PC. L'inventario può quindi mostrare gli stessi dispositivi, mentre il numero degli aggiornamenti disponibili può essere inferiore.

## Sicurezza

- Nessun catalogo driver commerciale o mirror di terze parti; il catalogo incorporato contiene solo metadati e URL ufficiali, mai i file dei driver.
- Nessuna utility del produttore viene installata o eseguita: i pacchetti con `.exe`, `.msi` o script vengono rifiutati.
- I pacchetti driver esterni devono contenere INF compatibili e cataloghi `.cat` con firma Authenticode valida.
- Argomenti dei processi separati, senza concatenare input in una shell.
- Il software viene aggiornato nel contesto dell'utente; l'elevazione UAC viene usata soltanto per driver e operazioni che richiedono realmente privilegi amministrativi.
- Piano di aggiornamento limitato alla cartella dati dell'app.
- Nessuna telemetria e nessun dato personale trasmesso dall'app.

## Requisiti

- Windows 10 x64 versione 1809 (build 17763) o successiva, oppure Windows 11 x64.
- WinGet/App Installer aggiornato.
- Connessione Internet.
- Windows Update attivo.
- Privilegi di amministratore per installare gli elementi selezionati.

Windows 10 standard ha terminato il supporto Microsoft il 14 ottobre 2025. L'app resta tecnicamente compatibile; per un sistema protetto è consigliato Windows 11 oppure Windows 10 22H2 aderente al programma ESU.

## Struttura

- `MainWindow.xaml`: interfaccia WPF moderna.
- `ViewModels/MainViewModel.cs`: stato, scansione, filtri e cronologia.
- `Services/WinGetService.cs`: scansione e aggiornamento software.
- `Services/WindowsUpdateService.cs`: ricerca e installazione driver.
- `Services/OfficialDriverCatalogService.cs`: confronto esatto con il catalogo trasparente dei produttori.
- `Services/OfficialDriverPackageService.cs`: download e installazione protetta dei soli pacchetti INF verificati.
- `Services/ElevatedUpdateRunner.cs`: elevazione UAC, punto di ripristino e avanzamento.
- `Models`: modelli dati e piano di aggiornamento.
- `Assets/driver-catalog.json`: metadati incorporati dei driver produttore; non ospita binari né mirror.
- `App.xaml` e `MainWindow.xaml`: risorse grafiche, stili e pagine dell'interfaccia WPF.
- `build.ps1`: restore e publish self-contained/single-file in `dist`.
- `build-installer.ps1` e `installer.iss`: generazione del Setup grafico `.exe` con installazione e disinstallazione per utente.
- `CREA-EXE.bat`: controllo/installazione dell'SDK .NET 8 e avvio della compilazione.
- `Tests/UpdateCenter.SmokeTests`: controlli senza dipendenze esterne per versioni semantiche e impostazioni predefinite dell'updater.

## Aggiornamento automatico dell'app

Il controllo automatico è attivo per impostazione predefinita e interroga, senza token, la Release stabile più recente del repository pubblico `Illidan0S/UpdateCenter`. Draft e prerelease vengono ignorate. Il controllo in background non blocca l'interfaccia e viene effettuato al massimo una volta ogni 24 ore; nelle Impostazioni può essere disattivato o avviato manualmente con **Controlla ora**.

Quando è disponibile una versione più recente, una finestra coerente con l'app mostra versione installata, nuova versione, note e dimensione. È possibile scegliere **Aggiorna ora**, **Più tardi** oppure **Ignora questa versione**.

L'eseguibile viene scaricato in `%LOCALAPPDATA%\UpdateCenter\Updates` e installato soltanto dopo la verifica SHA-256. Il nuovo eseguibile attende la chiusura del processo precedente, conserva una sola copia temporanea di sicurezza, sostituisce `UpdateCenter.exe` nella stessa cartella e riavvia l'app. Se la sostituzione o il riavvio falliscono, ripristina automaticamente il vecchio eseguibile. Download, backup e file intermedi vengono rimossi dopo l'avvio riuscito; l'esito resta nei log. L'installazione in `%LOCALAPPDATA%\Programs\UpdateCenter` non richiede privilegi amministrativi.

## GitHub Actions e pubblicazione delle versioni

Il workflow `.github/workflows/release.yml` compila su Windows con .NET 8, genera sia l'eseguibile portatile sia il Setup grafico `.exe`, verifica versione e contenuto, genera i rispettivi SHA-256 e carica gli artefatti. Con un tag stabile `vMAJOR.MINOR.PATCH` crea la Release soltanto dopo il superamento di tutti i controlli.

Per una versione futura:

1. aggiorna `Version`, `AssemblyVersion`, `FileVersion` e `InformationalVersion` in `UpdateCenter.csproj` e il fallback visibile nell'interfaccia;
2. esegui `build.ps1` e `build-installer.ps1 -NoAppBuild`, quindi verifica localmente app, installazione e disinstallazione;
3. pubblica le modifiche su `main` e attendi il completamento positivo del workflow;
4. crea e pubblica il tag corrispondente, per esempio `v1.0.1`;
5. verifica che la Release contenga Setup `.exe`, eseguibile portatile versionato e i rispettivi file `.sha256`.
