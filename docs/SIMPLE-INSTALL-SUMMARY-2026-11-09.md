# Riepilogo installazione semplificato — 9 novembre 2026

Baseline: branch `feature/engine-m6-consent-integrity`, HEAD
`33f5468dbb2d20f161efc0de54b6a5d8a424580e`, con interventi A, UX e consenso
unificato già presenti e preservati. Versione applicativa 1.1.5 invariata.

## Comportamento

Il riepilogo autorevole locale presenta conteggi, alimentazione, spazio, elenco
programmi con versione attuale/nuova e origine. La protezione del sistema appare
quando il piano richiede il ripristino. L'elenco mantiene lo scrolling e la
virtualizzazione; Annulla e Conferma e aggiorna restano nel footer fisso.

Il riquadro generico di rischio, il testo tecnico grezzo e la checkbox sono stati
rimossi dall'albero del dialogo autorevole. SID, ARP, GUID e dettagli silent/fallback
non vengono proiettati nell'elenco. La descrizione tecnica originale resta interna
in `RunnerConsentPresentation`; nessun metadato viene eliminato dal piano.

Rimozione preventiva, riavvio necessario e incompatibilità segnalata compaiono
brevemente sulla riga interessata. Lo scope già vincolato viene eventualmente
descritto come utente corrente/tutti gli utenti: non viene presentato come un
cambio di scope. Il drift di scope continua a bloccare il runner.

I dati aggiuntivi di presentazione vengono copiati prima di qualsiasi attesa di
preparazione, e abbinati integralmente al target congelato. Non si usano i flag
conservativi generici di `RunnerPlanItem.ToUpdateItem()` per inventare rischi.
I normali dettagli positivi di compatibilità/hardware non vengono presentati
come incompatibilità. I produttori attuali non offrono aggiornamenti noti come
incompatibili: i controlli di installabilità restano applicati prima del piano.

## Consenso e revisione

Solo il click esplicito di Conferma e aggiorna restituisce l'assenso del dialogo
autorevole. Il pulsante è abilitato quando il preflight è valido, senza consenso
preimpostato: all'apertura `DialogResult` è nullo. Focus iniziale su Annulla,
nessun pulsante globale predefinito per Invio; Invio/Spazio confermano soltanto
dal controllo focalizzato.

La decisione è monouso anche nel dialogo. Annulla, Escape e X negano; chiudere la
finestra non autorizza. Gli handler dei controlli legacy rimossi ignorano eventi
sulle istanze autorevoli, impedendo assensi alternativi anche tramite invocazione
programmatica dei vecchi controlli.

Il gate continua a controllare snapshot, rifiuto, mutazioni e consumo; il canale
continua ad autenticare UI e runner. Nessun valore checkbox è inviato al protocollo.
I file di gate, preparazione, canale, snapshot e runner sono identici byte per byte
a quelli presenti all'inizio di questo intervento. Retry e nuovi batch richiedono
nuova autorizzazione. UAC rimane un controllo Windows separato.

Un revisore sicurezza indipendente ha esaminato il flusso e il diff. Ha richiesto
e verificato i guard aggiuntivi sugli handler legacy e controllato la provenienza
del flag di rimozione nella selezione originale. Esito finale: nessun rilievo
bloccante. Nessun file modificato dal revisore.

## Verifiche

Evidenze non tracciate: `_output/simple-summary-20261109/`.

| Verifica mirata | Esito |
|---|---|
| Riproduzione iniziale QA senza checkbox | RED atteso: pulsante non abilitato senza spunta |
| Build Release app e harness smoke | PASS, zero errori/avvisi |
| `--unified-consent-ui` | PASS, 21 gruppi WPF |
| Ordinario 7-Zip, 500 elementi, rischio specifico | PASS, IT/EN × chiaro/scuro |
| Conferma diretta/doppio click, Invio/Spazio focalizzati | PASS |
| Annulla, Escape, X, chiusura | PASS, nessun progresso autorizzato |
| Preflight bloccato e hint associato a piano cambiato | PASS, nessun assenso |
| Controlli legacy distaccati | PASS, nessun percorso alternativo di assenso |
| Software/driver e punto di ripristino, riavvio specifico | PASS |
| Testo grande a 480×500 e 720×500 | PASS, azione visibile |
| `--unified-consent` | PASS, 15 gruppi: gate e 13 scenari con processi separati/effetti simulati |
| `--runner-consent` | PASS, 25 gruppi M6-B |
| `--install-safety` | PASS M5/B2, inclusi 10 gruppi metadata boundaries |
| Ispezione delle immagini WPF finali | PASS, intestazioni/versioni leggibili e nessuna checkbox o testo tecnico |
| `git diff --check` | PASS |

I test WPF usano una `Application` di test e una root GUID temporanea, mai lo
startup produttivo. Esercitano i veri handler e il gate, senza collegare assensi
a effetti installer. I test del canale usano processi separati con effetti
simulati. Nessuna installazione reale, download di payload o modifica ai programmi.

Sono stati eseguiti solo build e test mirati richiesti; non sono stati ripetuti
gli audit storici o i 15 FAIL RED intenzionali. Le immagini sono render della
finestra WPF effettiva con fixture sintetiche, senza cattura del desktop.

**PENDING:** DPI nativi 150/200%, cambio monitor, Narrator, Windows 10 1809 reale
e canale con UAC reale. Nessuna scala/risoluzione o tema globale Windows modificato.
Le API introdotte sono controlli WPF già disponibili nel target esistente;
la compatibilità del target Windows 17763 rimane invariata a livello di build.
M6-B resta WINDOWS VALIDATION PENDING.

## File e Git

Questo intervento modifica esclusivamente:

- `MainWindow.xaml.cs`: copia dei soli hint di presentazione e loro passaggio al riepilogo.
- `UpdateConfirmationWindow.xaml`: template delle righe, identificazione del riquadro ripristino, rimozione del textbox tecnico.
- `UpdateConfirmationWindow.xaml.cs`: riepilogo autorevole pulito e click di consenso monouso.
- `InstallationSummary.cs` (nuovo): proiezione immutabile e hint vincolati al target esatto.
- `Tests/UpdateCenter.SmokeTests/UnifiedConsentUiTests.cs`: QA mirata aggiornata.
- questo rapporto.

Il manifest SHA-256 iniziale e i confronti incrementali sono nella directory
baseline non tracciata. Tutti gli altri file già modificati/non tracciati sono
rimasti identici byte per byte. Indice vuoto, nessun commit creato per non includere
il lavoro precedente; HEAD invariato. Nessun reset, stash, merge, rebase, push,
tag o release eseguito.
