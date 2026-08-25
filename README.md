# Update Center

Versione pubblica attuale: **1.1.4**.

Rafforzata la gestione degli esiti di installazione: verifica post-installazione per WinGet e driver, aggiornamento coerente degli elenchi e protezione dai refresh WPF durante transazioni di modifica.

Risolto l’errore nella finestra di conferma degli aggiornamenti software: “L’associazione bidirezionale richiede Path o XPath”.

Update Center è un’app desktop per Windows che riunisce aggiornamenti software, driver, runtime e informazioni hardware in un’unica interfaccia chiara. L’utente decide sempre cosa installare: nessun aggiornamento parte automaticamente.

## Funzioni principali

- Aggiornamento controllato dei software tramite WinGet, con verifica più affidabile dell’installazione completata.
- Gestione sicura delle applicazioni aperte che bloccano un aggiornamento, con possibilità di chiuderle e riprovare.
- Possibilità di continuare con l’installer normale quando serve un intervento dell’utente.
- Ricerca di driver compatibili da fonti ufficiali e verificate.
- Gestione più affidabile degli aggiornamenti di driver e chipset.
- Diagnosi dei driver problematici segnalati da Gestione dispositivi.
- Controllo e installazione dei runtime condivisi: DirectX, Visual C++, .NET, Vulkan, PhysX, WebView2 e altri.
- Filtri per software, driver, runtime ed errori.
- Riepilogo prima dell’installazione con spazio richiesto, alimentazione, riavvio e avvisi di sicurezza.
- Classificazione precisa degli aggiornamenti WinGet: la conferma aggiuntiva viene richiesta solo quando l’installer compatibile dichiara la rimozione della versione precedente; errori temporanei e metadati non verificabili restano stati separati.
- Pausa e ripresa di un gruppo di aggiornamenti, senza interrompere l’elemento già in installazione.
- Inventario hardware con CPU, GPU, VRAM, RAM, Windows, driver principali e copia rapida delle informazioni.
- Salute dello storage con unità fisiche, volumi associati, capacità, stato e temperatura quando disponibile.
- Collegamento diretto a NVIDIA App o alla pagina ufficiale per i driver GPU NVIDIA.
- Cronologia degli aggiornamenti, log locali e nessuna telemetria.
- Messaggi più chiari e diagnostica più utile in caso di problemi.
- Controllo automatico delle nuove versioni di Update Center con verifica SHA-256 e autosostituzione sicura anche della portable.
- Gestione dei computer nella rete locale tramite modalità Controller e Agent, con autorizzazione dei dispositivi e revoca immediata dell’accesso.
- Scansione e gestione remota degli aggiornamenti, con avanzamento e riepilogo separati per ciascun computer collegato.

## Download

Le [Release GitHub](https://github.com/Illidan0S/UpdateCenter/releases) includono due eseguibili:

- **`UpdateCenter-Setup-vVERSIONE.exe`**: installer standard, crea la voce nel menu Start e gestisce gli aggiornamenti dell’installazione.
- **`UpdateCenter-vVERSIONE-Portable.exe`**: versione senza installazione, utilizzabile anche da una chiavetta USB e aggiornabile automaticamente mantenendo il proprio nome.

I file **`.sha256`** sono i checksum di sicurezza usati per verificare i download automatici: non sono versioni dell’app.

## Utilizzo

1. Avvia l’eseguibile standard o portable.
2. Premi **Avvia scansione**.
3. Apri **Aggiornamenti** e controlla gli elementi trovati.
4. Seleziona solo quelli desiderati.
5. Premi **Aggiorna elementi selezionati** e conferma il riepilogo.

L’app non riavvia il computer senza chiedere conferma.

## Sicurezza e privacy

- I software vengono aggiornati tramite WinGet.
- I driver vengono proposti solo quando la fonte e la compatibilità sono verificabili.
- BIOS, UEFI e firmware non vengono installati automaticamente.
- I pacchetti driver esterni devono superare controlli di compatibilità, hash e firma.
- I runtime installabili usano la normale selezione, conferma e cronologia degli aggiornamenti.
- Nessuna telemetria e nessun dato personale vengono trasmessi dall’app.
- Log e cronologia restano sul PC in `%LOCALAPPDATA%\UpdateCenter`.

## Requisiti

- Windows 10 x64 versione 1809 (build 17763) o successiva, oppure Windows 11 x64.
- WinGet/App Installer aggiornato.
- Connessione Internet.
- Privilegi di amministratore solo per gli elementi che li richiedono.

## Licenza e sviluppo

Il progetto è distribuito con licenza [MIT](LICENSE).

Le istruzioni per compilazione, struttura del progetto, creazione del Setup locale e pubblicazione delle versioni sono disponibili in [docs/SVILUPPO.md](docs/SVILUPPO.md).
