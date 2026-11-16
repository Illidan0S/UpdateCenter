# Scansione: progresso e indicatori di attività

## Checkpoint e ambito

- Branch verificato: `feature/engine-m6-consent-integrity`.
- HEAD iniziale verificato: `b6c3f736cd55ccad20240e98b2b3ada8e5fc57a1`.
- Commit locale M6-B: `2d8eeec1caa94c377a4a25e675e8ba15570dac70`,
  `feat(security): bind runner execution to explicit plan consent`.
  Staging esplicito dei 14 file pertinenti, `git diff --check` superato,
  working tree pulito immediatamente dopo il commit.
- Checkpoint UX autorizzato dall'utente dopo la verifica manuale di scansione,
  indicatore animato, fluidità, completamento e annullamento (8 novembre 2026).
  M6-B resta **WINDOWS VALIDATION PENDING**; nessuna modifica alle prove RED storiche.

## Causa ed evidenze

Il ViewModel assegnava percentuali fisse 2/12/38/68/84/90/100. Il 68%
precedeva `WindowsUpdateService.ScanDriversAsync`, che esegue ricerche COM
sincrone sul proprio worker esistente senza esporre avanzamento misurabile.
Non era una percentuale di elementi elaborati. WinGet, inventario, catalogo,
runtime e storage non forniscono un denominatore complessivo affidabile.

Scansione reale di sola lettura sull'host, 8 novembre 2026, diagnostica M1
`UPDATECENTER_SCAN_DIAGNOSTICS=1` limitata al processo avviato:

| Intervallo M1 | Durata |
| --- | ---: |
| Scansione completa | 35,809 s |
| Software | 11,126 s |
| Inventario rapido, in parallelo | 4,404 s |
| Windows Update / driver | 24,423 s |
| Tre attese `wait.wua-search` | 9,274 / 5,901 / 8,843 s |
| Aggregazione software | 2,518 ms |
| Aggregazioni driver e catalogo | 0,346 / 0,217 ms |
| Salvataggio finale | 16,417 ms |

Esito M1 `success`, nessuna fase pendente, nessun avviso, 51 risultati.
La lunga attesa coincide quindi con la fase precedentemente etichettata 68%.
Durante l'attesa reale la finestra ha risposto a Tab mostrando il focus.
Non è stato riprodotto un blocco del dispatcher causato da WUA o aggregazione.
L'elaborazione WinGet dopo gli await poteva invece riprendere sul dispatcher:
ora le continuazioni della scansione usano `ConfigureAwait(false)`.

## Correzione

- Scansione indeterminata con fase esplicita; nessuna percentuale intermedia
  inventata, né stima temporale. Il contatore degli elementi resta reale.
- Fasi software, inventario, driver, catalogo, aggregazione, runtime/storage e
  finalizzazione; rilascio del dispatcher tra le fasi per disegno e input.
- `100%` soltanto dopo pubblicazione, salvataggio e uscita dallo stato occupato.
  In caso di errore del salvataggio viene ripristinata la data precedente,
  senza dichiarare completamento. Avvisi e risultati parziali sono espliciti.
- Annullamento richiesto distinto da annullamento effettivo: il provider COM
  può dover terminare la chiamata sincrona prima di osservare il token.
- Impulso WPF leggero a 30 fps, senza timer applicativi; clock rimosso quando
  la barra è nascosta, disabilitata o determinata. Geometria determinata invariata.
- La prova visiva ha rilevato anche lo stile Fluent locale della Home che
  mascherava il template. Home e riepilogo operazioni ora usano esplicitamente
  lo stile di avanzamento dell'applicazione, con regressione sul vero XAML Home.
- Su richiesta dell'utente, messaggi di attività e completamento IT/EN senza
  nomi dei provider: ricerca aggiornamenti software/driver e risultati pronti
  per la verifica. Nessun redesign o dipendenza aggiuntiva.

## Verifica e limiti

Ambiente: Windows x64, build `10.0.26320.0`, target
`net10.0-windows10.0.17763.0`, build applicativa ancora `1.1.5`.

Nuovi test prima RED, poi superati con `--scan-progress`: attesa lunga e input
dispatcher, fasi e pubblicazione finale (anche scansione ripetuta), risultato
parziale, errore reale di condivisione sul file impostazioni isolato,
cancellazione alla finalizzazione, continuazioni WinGet e animazione WPF reale
con controllo dello scope Home. Superate anche le 10 regressioni esistenti di
orchestrazione richiamate dal dispatch. Tutti i provider di questi test sono
simulati e i dati sono in una directory GUID temporanea.

Build Release dell'intera soluzione: zero errori e zero avvisi. Le schermate
native della Home, prima e dopo, sono state osservate nella sessione: barra
visibile e messaggi aggiornati nella build finale a 1240×790. Non è una
validazione della matrice completa di DPI, temi o consenso M6-B.

Evidenze locali sotto `_output/scan-progress-20261108/`: `release-build.txt`,
`scan-progress.txt`, `host-scan-m1.json` (solo report M1 strutturato).
La scansione reale non costituisce verifica di installazione o di consenso:
nessuna installazione, aggiornamento driver, elevazione UAC o riavvio eseguito.
I tempi dei provider dipendono da rete, cache e configurazione Windows;
l'annullamento COM non è immediato. Il threading WUA è rimasto invariato.

## Prova manuale

Avviare normalmente `bin/Release/net10.0-windows10.0.17763.0/win-x64/UpdateCenter.exe`.
Provare Avvia scansione, Annulla, navigazione, tastiera, consultazione e filtri
dei risultati. Per questa prova evitare aggiornamento/installazione degli
elementi, riparazioni e configurazione dei componenti di sistema.
