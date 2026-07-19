# Update Center

Versione pubblica iniziale: **1.0.0** (`v1.0.0`).

Applicazione desktop per Windows 10 e Windows 11 che cerca aggiornamenti software tramite **WinGet** e aggiornamenti driver tramite **Windows Update Agent**. L'utente sceglie singolarmente cosa installare.

La sezione **Driver e chipset** crea inoltre un inventario locale dei driver PnP installati, associa quando possibile la versione installata a quella proposta da Windows Update e riconosce i componenti CPU/chipset. Per i controlli che richiedono il catalogo del produttore espone collegamenti esclusivamente agli strumenti ufficiali AMD, Intel, NVIDIA e agli aggiornamenti facoltativi di Windows.

Il controllo Windows Update è automatico: i componenti CPU/chipset mostrano direttamente se è disponibile un aggiornamento oppure se Windows Update non ne ha rilevato alcuno. Le voci duplicate con stesso dispositivo, produttore e versione vengono raggruppate per rendere l'inventario più leggibile.

## Quale file usare

Il pacchetto binario pubblicato nella GitHub Release include già `dist\UpdateCenter.exe`, quindi normalmente conviene usare **`INSTALLA.bat`**: copia l'eseguibile in `%LOCALAPPDATA%\Programs\UpdateCenter`, aggiunge il collegamento al menu Start e avvia l'app. Non richiede il compilatore .NET. Il repository sorgente non versiona `dist`, eseguibili o ZIP.

Usa **`CREA-EXE.bat`** soltanto se hai modificato il codice sorgente o vuoi rigenerare personalmente l'eseguibile. Questo file non installa l'app: controlla o propone .NET 8 SDK, compila il progetto e sostituisce `dist\UpdateCenter.exe`. Terminata la compilazione puoi eseguire `INSTALLA.bat`.

In breve: per usare il programma scegli `INSTALLA.bat`; per ricompilarlo scegli `CREA-EXE.bat`.

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

Se vuoi anche copiarlo nella cartella Programmi del tuo profilo e aggiungerlo al menu Start, esegui `INSTALLA.bat` dopo la compilazione. Non sono necessari privilegi amministrativi per questa copia; l'UAC viene richiesto solo quando installi gli aggiornamenti selezionati.

Per rimuovere l'app esegui **`dist\UNINSTALLA.bat`**. Nel pacchetto è presente una sola copia del disinstallatore. Dopo una conferma, chiude Update Center ed elimina la copia installata, il collegamento Start, `%LOCALAPPDATA%\UpdateCenter`, i componenti temporanei dell'eseguibile e l'intera cartella estratta dal file ZIP con tutti i file contenuti. La cartella del progetto viene cancellata soltanto se il disinstallatore ne riconosce la struttura, per evitare rimozioni accidentali. I punti di ripristino gestiti da Windows non vengono eliminati.

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

La pagina **Cronologia** usa descrizioni semplici per indicare esito, versioni, fonte e necessità di riavvio. I dettagli lunghi vengono abbreviati nella tabella; mantenendo il puntatore sul testo per un secondo si apre un pannello con il contenuto completo, selezionabile e copiabile.

## Fonti e comportamento

- Software: comando ufficiale `winget upgrade`, un pacchetto alla volta.
- Driver: aggiornamenti firmati e compatibili offerti da Windows Update al PC specifico.
- CPU/chipset: inventario delle componenti di sistema e accesso al controllo ufficiale AMD o Intel rilevato sul PC.
- Versioni preview: non vengono richieste dall'app.
- Ripristino: se l'opzione è attiva, l'app richiede un solo punto di ripristino per l'intero gruppo soltanto quando sono presenti driver o aggiornamenti importanti. I gruppi composti unicamente da software WinGet non creano punti. Windows può rifiutare la richiesta se Protezione sistema è disattivata o per le proprie regole. Dalle Impostazioni è possibile aprire direttamente il pannello Windows che mostra e limita lo spazio utilizzato.
- Log e cronologia: `%LOCALAPPDATA%\UpdateCenter`.
- Pulizia: i file temporanei di installazione vengono eliminati al termine; eventuali residui più vecchi di un giorno e i log più vecchi di 30 giorni vengono rimossi all'avvio. Ogni log giornaliero è limitato a 2 MB.
- Controlli preliminari: prima dell'installazione la finestra di riepilogo verifica alimentazione e spazio libero. Driver e aggiornamenti importanti vengono bloccati con batteria al 25% o inferiore; l'operazione viene inoltre bloccata quando lo spazio è inferiore alla stima minima necessaria.

Non è tecnicamente possibile garantire che *ogni* programma installato sia aggiornabile: WinGet può gestire soltanto i programmi che riesce ad associare a un pacchetto disponibile. Analogamente, l'app mostra solo i driver che Windows Update ritiene applicabili al computer.

Update Center non usa il catalogo proprietario di Driver Easy e non presenta come aggiornamento un pacchetto che Windows o il produttore non hanno confermato per il PC. L'inventario può quindi mostrare gli stessi dispositivi, mentre il numero degli aggiornamenti disponibili può essere inferiore.

## Sicurezza

- Nessun catalogo driver di terze parti.
- Argomenti dei processi separati, senza concatenare input in una shell.
- Una sola elevazione UAC per la fase di installazione.
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
- `Services/ElevatedUpdateRunner.cs`: elevazione UAC, punto di ripristino e avanzamento.
- `Models`: modelli dati e piano di aggiornamento.
- `Assets/PackageRemoval.template`: modello interno dal quale `build.ps1` genera l'unica copia del disinstallatore, `dist\UNINSTALLA.bat`.
- `App.xaml` e `MainWindow.xaml`: risorse grafiche, stili e pagine dell'interfaccia WPF.
- `build.ps1`: restore e publish self-contained/single-file in `dist`.
- `CREA-EXE.bat`: controllo/installazione dell'SDK .NET 8 e avvio della compilazione.
- `INSTALLA.bat`: installazione per utente dalla cartella `dist` in `%LOCALAPPDATA%\Programs\UpdateCenter`.

## Aggiornamento automatico dell'app

Nella prima pubblicazione del solo sorgente il controllo delle nuove versioni dell'app non è ancora attivo. È previsto tramite GitHub Releases stabili del repository `Illidan0S/UpdateCenter`, con verifica SHA-256 e aggiornamento della stessa installazione. La funzione verrà dichiarata disponibile soltanto dopo la verifica completa di compilazione, rollback e pacchetto Release.
