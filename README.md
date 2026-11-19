# Update Center

**Update Center v2.0.1** mostra le nuove versioni dell’app nella Home e riassume i risultati della scansione in una sola riga. Le tabelle si adattano ai risultati senza dover ridimensionare la finestra. [Note in italiano e inglese](docs/RELEASE-2.0.1.md)

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

Scarica [Update Center v2.0.1](https://github.com/Illidan0S/UpdateCenter/releases/tag/v2.0.1) dalla release ufficiale su GitHub.

- **`UpdateCenter-Setup-v2.0.1.exe`** aggiorna o installa l’app nel profilo Windows e aggiunge una voce nel menu Start.
- **`UpdateCenter-v2.0.1-Portable.exe`** si avvia direttamente, anche da una chiavetta USB.

Gli eseguibili v2.0.1 sono distribuiti senza firma Authenticode. Usa il file **`.sha256`** di ciascun eseguibile per verificarne l’integrità.

### Passaggio diretto da una versione precedente

- **Setup.** Chiudi Update Center e avvia il nuovo Setup con lo stesso account Windows. Mantieni la directory dell’installazione esistente.
- **Portable.** Chiudi Update Center e sostituisci il precedente eseguibile con il nuovo Portable. Puoi mantenere il nome del file e la sua cartella.
- **Dalla 2.0.0.** Usa il Setup interattivo o sostituisci il Portable seguendo i passaggi sopra. Verifica il file con il relativo `.sha256` della release ufficiale. L’aggiornamento integrato della 2.0.0 richiede una firma Authenticode sul nuovo file. [Verifiche e compatibilità del self-update](docs/CODE-SIGNING.md#aggiornamento-dallapp)

Impostazioni e cronologia rimangono in `%LOCALAPPDATA%\UpdateCenter` per lo stesso profilo Windows.

## Utilizzo

1. Avvia l’eseguibile standard o portable.
2. Premi **Avvia scansione**.
3. Apri **Aggiornamenti** e controlla gli elementi trovati.
4. Seleziona solo quelli desiderati.
5. Premi **Aggiorna selezionati** e conferma il riepilogo.

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

- Per la linea 2.0 il target tecnico è Windows x64 dalla build 17763. Il Portable e l'eseguibile incluso nel Setup contengono il runtime .NET 10 Desktop. [Edizioni e build ammesse](https://github.com/Illidan0S/UpdateCenter/releases/tag/v2.0.0)
- Per gli aggiornamenti software automatici la v2.0.1 ammette WinGet `v1.29.290`, `v1.30.300` e `v1.30.310`. Se il client cambia dopo la scansione, occorre ripetere scansione e consenso. [Requisiti WinGet](https://learn.microsoft.com/en-us/windows/package-manager/winget/)
- Connessione Internet.
- Privilegi di amministratore solo per gli elementi che li richiedono.

Avvio e scansione sono stati osservati su Windows 11 Pro Insider x64, build 26320. Le release stabili di Windows 11 in servizio e le edizioni Windows 10 LTSC elencate da Microsoft per .NET 10 sono piattaforme da validare sul candidato. Restano aperte anche le prove di UAC con runner elevato, installazione driver WUA e ciclo Setup/Portable. [Matrice di compatibilità](docs/M8-RELEASE-READINESS.md#compatibilità-attuale) · [Sistemi supportati da .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)

## Licenza e sviluppo

Il progetto è distribuito con licenza [MIT](LICENSE).

Per compilare i sorgenti serve .NET SDK 10 stabile. Le istruzioni per compilazione, test, creazione del Setup e pubblicazione sono disponibili nella [guida di sviluppo](docs/SVILUPPO.md).
