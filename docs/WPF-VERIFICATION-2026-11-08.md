# Evidenze della modernizzazione WPF

Consolidamento dei risultati già registrati; nessun test applicativo rieseguito per il checkpoint. Le cartelle indicate sono sotto `C:\Users\USER\OneDrive\Desktop\UpdateCenter\_output`. Le prove storiche dettagliate selezionate sono conservate nell'archivio QA indipendente; non confondere vecchi HEAD/non-commit/consigli di milestone con lo stato finale descritto in `CHECKPOINT-WPF-2026-11-08.md`.

| Milestone | Risultato già osservato | Fonte |
|---|---|---|
| Aspetto | Release 9 progetti: 0 errori/avvisi. Chiaro/scuro/sistema, tre dimensioni testo, IT/EN, tastiera, 720×500, persistenza. Contrasto secondario del pilot misurato; non certificazione globale dell'app. | `wpf-fluent-pilot/qa-results.txt`, `build-release.log` |
| Lingua | `--language-ui` PASS; titolo corrente/sidebar/tooltip/UIA aggiornati, preferenze preservate, IT/EN e temi, riapertura persistente. | `wpf-language-pilot/QA-results.txt`, `language-ui-test.log` |
| Impostazioni | `--settings-ui` PASS: binding, frequenze, salvataggio, scroll conservato al cambio tema, azione Salva raggiungibile; prove reali di persistenza. | `wpf-settings-milestone/QA-results.txt`, `settings-ui-test.log` |
| Home/shell | `--home-shell-ui` PASS: 36 combinazioni lingua/tema/testo/dimensione, sette destinazioni, binding di stato, controlli finestra, persistenza. Corrette etichette lunghe sidebar. | `wpf-home-shell-milestone/QA.txt`, `functional-ui-tests.log` |
| Aggiornamenti/Driver/Cronologia | `--operations-ui` PASS: 0/500 elementi, selezioni/filtri/guardie, busy/retry, riciclo righe e JSON cronologia. `--settings-ui`, `--winget-hardening`, `--firmware-hardening` PASS nelle verifiche della milestone. | `wpf-operations-milestone/QA.txt`, `operations-tests.log` |
| Hardware/LAN | `--hardware-network-ui` PASS: 500 storage/risultati, 50 PC, vuoto/offline/errore/negato, eleggibilità, rischio, busy, fingerprint e nomi accessibili. QuickHardware simulato e NetworkCore PASS; regressioni Operations/Home/Settings PASS. | `wpf-hardware-network-milestone/RAPPORTO.md`, `hardware-network-tests.log`, log regressioni |
| Dialoghi | Release 9 progetti: 0 errori/avvisi; ultime ricompilazioni mirate dopo correzioni UI riuscite. `--dialogs-ui` PASS: IT/EN × temi, testo Grande, 720×500, rischio/preflight/esclusione remota, Escape/Enter/focus, pausa/ripresa/nascondi, esiti distinti, riavvio mostrato solo se richiesto, dettagli, 500 righe virtualizzate, errore self-update con HTTP rifiutato, visibilità errore, QA inerte e stati LAN distinti. | `wpf-dialogs-milestone/build-release.log`, `build-dialog-refinement.log`, `tests-dialogs.log` |

Il test Dialoghi iniziale istanziava `App`, che avviava una finestra produttiva attraverso lo Startup accodato e sottraeva focus; corretto l'host del test caricando soltanto risorse e dispatcher WPF. L'asserzione di ritorno del focus è rimasta e passa. Il codice di produzione non è stato alterato per mascherare quel problema del test.

La revisione specialistica finale limitata ai flussi sensibili ha chiesto `Mode=OneWay` per i due TextBox di stato: corretto. Nessun altro rilievo P0/P1/P2 in quel diff. Sono stati corretti anche gli host QA esterni Operations/Hardware-LAN, che conservavano handler reali: `VisualQaSafety` li scollega prima dell'uso. Usare i sorgenti finali inclusi nell'archivio QA e ricompilarli; non riutilizzare vecchi eseguibili di fixture. Il test della modalità sconosciuta ha restituito exit 2 senza avviare smoke o socket.

Nessun installer, shutdown, UAC reale o comando remoto eseguito nella validazione Dialoghi. Le finestre principali sono state fotografate sul desktop con dati isolati. Il controllo aggiuntivo della pausa è stato interrotto automaticamente da Computer Use per impossibilità di verificare l'URL attivo: non c'è uno screenshot finale della pausa. La semantica pausa/ripresa è verificata dal test.

## Limiti che non sono PASS

- Smoke LAN completo bloccato dal sandbox con socket 10013 nelle milestone UI; non ritentato per aggirare il limite. I PASS storici del 4 novembre sono di un'esecuzione precedente e non attestano LAN reale corrente.
- DPI 150/200%: render simulati di Lingua/Home soltanto; nessuna prova su monitor fisico o passaggio tra monitor. Contrasto elevato e screen reader completo non verificati.
- Nessuna installazione/upgrade/reboot reale, prova UAC o collegamento a un altro PC durante la QA grafica. Restano gate di release.
- Alcuni helper fuori solution hanno registrato NU1900 per il feed NuGet non raggiungibile. Non è un errore della build applicativa. Un log intermedio Dialoghi contiene CS4014; l'ultima build mirata `build-dialog-refinement.log` è a 0 avvisi/0 errori dopo il discard esplicito delle operazioni dispatcher.
- Le panoramiche `settings-full-*.png` compongono cinque screenshot reali; non sono singole catture né mockup. Le altre immagini selezionate sono catture reali; alcuni dettagli minori precedono la rifinitura finale della rispettiva milestone.
- Conservare il rilievo latente sul catalogo driver e i gate firma/LAN/Windows elencati nel checkpoint; non confonderli con regressioni nuove della UI.

## Prove del salvataggio

Le verifiche di questo incarico sono esclusivamente Git/backup: inventario, controllo mirato delle credenziali, ancestry del parent, ZIP letto integralmente con CRC e confronto dei contenuti, bundle verificato e ripristinato in un mirror temporaneo con fsck/riferimenti confrontati, hash SHA-256. Gli esiti effettivi e i conteggi sono in `INTEGRITY.json` nel backup, generato solo dopo l'esecuzione. Nessuna build o suite già superata viene ripetuta per salvare il progetto.
