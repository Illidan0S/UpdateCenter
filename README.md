# Update Center

**Ultima release disponibile** · [Update Center v2.0.0](https://github.com/Illidan0S/UpdateCenter/releases/tag/v2.0.0)

**In preparazione · v2.0.1.** Corregge il ritaglio della tabella dopo la scansione, semplifica il riepilogo e mostra le nuove versioni dell’app nella Home. [Note in italiano e inglese](docs/RELEASE-2.0.1.md)

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

La [release v2.0.0](https://github.com/Illidan0S/UpdateCenter/releases/tag/v2.0.0) include Setup e Portable.

- **`UpdateCenter-Setup-v2.0.0.exe`**: installer standard, crea la voce nel menu Start e gestisce gli aggiornamenti dell’installazione.
- **`UpdateCenter-v2.0.0-Portable.exe`**: versione senza installazione, utilizzabile anche da una chiavetta USB e aggiornabile automaticamente mantenendo il proprio nome.

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

- Windows x64. Consulta le edizioni e le build ammesse nelle [note della v2.0.0](https://github.com/Illidan0S/UpdateCenter/releases/tag/v2.0.0).
- WinGet/App Installer aggiornato.
- Connessione Internet.
- Privilegi di amministratore solo per gli elementi che li richiedono.

## Licenza e sviluppo

Il progetto è distribuito con licenza [MIT](LICENSE).

Le istruzioni per compilazione, struttura del progetto, creazione del Setup locale e pubblicazione delle versioni sono disponibili in [docs/SVILUPPO.md](docs/SVILUPPO.md).
