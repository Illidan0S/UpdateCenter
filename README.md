# Update Center

Versione pubblica attuale: **1.0.7**.

Update Center è un’app desktop per Windows che riunisce aggiornamenti software, driver, runtime e informazioni hardware in un’unica interfaccia chiara. L’utente decide sempre cosa installare: nessun aggiornamento parte automaticamente.

## Funzioni principali

- Aggiornamento controllato dei software tramite WinGet.
- Ricerca di driver compatibili da fonti ufficiali e verificate.
- Diagnosi dei driver problematici segnalati da Gestione dispositivi.
- Controllo e installazione dei runtime condivisi: DirectX, Visual C++, .NET, Vulkan, PhysX, WebView2 e altri.
- Filtri per software, driver, runtime ed errori.
- Riepilogo prima dell’installazione con spazio richiesto, alimentazione, riavvio e avvisi di sicurezza.
- Pausa e ripresa di un gruppo di aggiornamenti, senza interrompere l’elemento già in installazione.
- Inventario hardware con CPU, GPU, VRAM, RAM, Windows, driver principali e copia rapida delle informazioni.
- Salute dello storage con unità fisiche, volumi associati, capacità, stato e temperatura quando disponibile.
- Collegamento diretto a NVIDIA App o alla pagina ufficiale per i driver GPU NVIDIA.
- Cronologia degli aggiornamenti, log locali e nessuna telemetria.
- Controllo automatico delle nuove versioni di Update Center con verifica SHA-256 e autosostituzione sicura anche della portable.

## Download

Le [Release GitHub](https://github.com/Illidan0S/UpdateCenter/releases) includono due eseguibili:

- **`UpdateCenter-vVERSIONE.exe`**: versione standard, compatibile con l’aggiornamento automatico senza dover rinominare il file.
- **`UpdateCenter-vVERSIONE-Portable.exe`**: versione senza installazione, utilizzabile anche da una chiavetta USB e aggiornabile automaticamente mantenendo il proprio nome.

Il file **`UpdateCenter-vVERSIONE.exe.sha256`** è la firma di sicurezza usata per verificare il download automatico: non è una terza versione dell’app.

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
