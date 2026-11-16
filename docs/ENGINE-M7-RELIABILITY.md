# UpdateCenter — DataGrid Aggiornamenti e chiusura tecnica M7

Data: 9 novembre 2026 (Europe/Rome). Branch `feature/engine-m6-consent-integrity`.

## Checkpoint e perimetro

Commit copertura autorizzato: `668240be25cf97afa093cf6758c60f8bf21e5e8a`,
`fix(updates): clarify blocked update eligibility and scope`, successivo al checkpoint
UX `c8ca78a1f44254dbb36ec06f0c9f96a255ba8cbc`.
Verificati diff, cinque file pertinenti al rapporto `UPDATE-COVERAGE-NINE-ITEMS.md`,
staging esplicito, `git diff --check` e working tree pulito subito dopo il commit.

Checkpoint M7 autorizzato: `fix(updates): improve grid usability and scan reliability`,
con parent `668240be25cf97afa093cf6758c60f8bf21e5e8a`. Raccoglie esclusivamente
i sei file elencati in questo rapporto. Nessun push, merge, tag, release,
nuovo provider o modifica dei blocchi di ambito. M8 non iniziata.
M6-B resta **WINDOWS VALIDATION PENDING**; prove RED storiche e consenso invariati.

## Chiusura tecnica del 9 novembre 2026

| Categoria | Conclusione |
|---|---|
| Prestazioni misurate nella sessione precedente | Tre scansioni complete: 28,204–44,058 s; WUA 20,565–37,193 s. Campioni e condizioni sono conservati sotto. Nessun nuovo benchmark eseguito per il checkpoint. |
| Miglioramenti dimostrati | Eliminata la cella vuota del DataGrid; azioni raggiungibili alla dimensione minima; nessun fallback GET per la dimensione dell'installer. |
| Risultati non dimostrati | Nessuna accelerazione attribuibile alle modifiche; nessuna garanzia generale sui tempi dei provider o su altri computer. |
| Limiti WUA non risolti | Ricerche COM sequenziali e sincrone, attesa variabile; cancellazione osservata al ritorno della ricerca in corso (7,761 s nel campione già registrato). |

Controllato il diff rispetto al checkpoint copertura: DataGrid, virtualizzazione,
azioni, filtri e ordinamento sono rimasti identici alla versione M7 già verificata.
In questa chiusura sono stati estesi soltanto i test HEAD e questo rapporto;
nessuna ulteriore correzione applicativa necessaria, nessuna modifica della versione.

### Test mirato HEAD, senza rete

Nuovo dispatch test-only `--scan-m7-head`, sempre successivo all'inizializzazione
della root dati GUID. Usa il metodo di produzione `ReadDownloadSizeAsync` con
HttpMessageHandler simulato e corpo che fallisce se qualcuno prova a leggerlo:

- HEAD 200 realmente priva di Content-Length (contenuto di lunghezza non calcolabile);
- HEAD non supportata (405), errore server (500), pagina di errore (404), anche
  con Content-Length che non deve essere interpretato come dimensione installer;
- errore di trasporto e timeout HTTP: dimensione sconosciuta, senza eccezione che
  interrompa la scansione;
- HEAD valida: dimensione nota di 12.345 byte conservata;
- cancellazione esplicita del chiamante: propagata, distinta dal timeout HTTP.

Per ciascuno dei sette scenari di risposta/errore è verificata una sola richiesta
HEAD, nessun GET e nessuna lettura del corpo. Lo stesso risultato attraversa
`MainViewModel.ScanAsync` con provider simulati: raggiunge la fase successiva,
pubblica il candidato, conclude al 100% e libera lo stato busy. Restano
selezionabilità e target esatto; il conteggio dimensioni del preflight distingue
il pacchetto sconosciuto e la validazione dello snapshot runner lo ammette.
Questo non autorizza né esegue un aggiornamento: il consenso M6-B resta necessario.
Identità e metadati installer validi sono dati sintetici; non sono state eseguite
richieste reali di manifest o una nuova scansione WinGet/WUA.

Nel rafforzamento del test il trasporto simulato inizialmente ignorava il token
già annullato: corretto il solo handler di test e verificata la propagazione.
Il servizio applicativo non è stato modificato rispetto alla precedente sessione.

Esito nuovo: otto casi PASS, build Release applicativa/harness senza restore
(zero warning/errori), `git diff --check`. Evidenza test in
`_output/engine-m7/checkpoint/head-tests.log`. I test UI, cleanup processi e
benchmark riportati nelle sezioni successive sono le evidenze precedenti,
non ripetute in questa chiusura.

## DataGrid: causa e modifica

La cella vuota era una vera settima `DataGridTemplateColumn`, senza intestazione,
`Width="Auto"`, `MinWidth="140"`. Conteneva «Apri fonte» e «Riprova»: quando entrambe
le azioni erano collassate, come sulla riga MuseHub, rimaneva una cella vuota
selezionabile. Non era soltanto il riempimento visivo del DataGrid.

In `MainWindow.xaml`, soltanto per `UpdatesGrid`:

- eliminata la colonna separata; le due azioni esistenti sono sotto il testo di Stato;
- Stato usa `1.8*`, minimo 190 DIP, e occupa lo spazio con Nome;
- conservati handler, condizioni di visibilità, blocco durante attività, tooltip,
  checkbox, ordinamento e selezione a riga intera;
- scorrimento verticale a pixel, mantenendo virtualizzazione e recycling.

Quest'ultimo intervento risolve un caso verificato a 720×500 con testo Grande:
la riga con entrambe le azioni è più alta del viewport (102 DIP). Lo scorrimento
per elementi lasciava «Riprova» parzialmente fuori vista anche dopo `BringIntoView`
(bordo inferiore a 127 DIP). Con scorrimento a pixel ogni azione è interamente
raggiungibile. Nessuno stile condiviso o altra pagina è stato modificato.

### Verifiche UI

Harness `--updates-grid`, dati sintetici, root dati GUID isolata. Risorse reali di
App.xaml, MainWindow e template WPF reali; avvio applicativo, timer e azioni esterne
rimossi dal solo harness mediante `VisualQaSafety`, già esistente.

- RED prima della correzione: «A separate empty/focusable action column remains after Status».
- RED per il pulsante tagliato alla dimensione minima; GREEN dopo lo scorrimento a pixel.
- IT/EN, testo Grande, tema Chiaro, larghezze 1538/1240/720 DIP; minimo 720×500.
- 500 righe: numero limitato di container realizzati, virtualizzazione righe/colonne
  e scorrimento all'ultima riga; testi lunghi con ellissi/tooltip.
- Sei celle reali, Stato finale non vuoto; scrollbar verticale e scorrimento
  orizzontale alla larghezza minima; nessun overflow orizzontale superfluo a 1240/1538.
- Binding checkbox e conteggio selezionati; selezione riga, ordinamento/filtro
  CollectionView; azioni visibili/focusabili, inibizione di Riprova durante attività.
- Controllo geometrico dell'intero rettangolo di ciascuna azione dopo `BringIntoView`,
  anche alla dimensione minima.

Sul desktop Windows è stata aperta la fixture sintetica: osservati Stato fino al
bordo destro, selezione della riga MuseHub, freccia Destra senza ulteriore cella,
Tab verso la prima cella della riga successiva, Shift+Tab di ritorno a Stato,
click sulla checkbox e deselezione con Spazio. Nessuna azione di aggiornamento
eseguibile nella fixture. La fixture è stata chiusa al termine.

Rendering WPF salvati e ispezionati in `_output/engine-m7/ui/`:
`grid-after.png`, `grid-1538.png`, `grid-1240.png`, `grid-720.png`,
`grid-720-actions.png`. Sono rendering della fixture reale; non mockup né
attestazioni di scansione/installazione. Gli screenshot del controllo desktop
sono nella sessione. La matrice DPI/temi generale non è stata ripetuta.

Riproduzione manuale mirata: aprire Aggiornamenti dopo una scansione, fare click
su Stato della riga MuseHub e premere Destra, Tab, Shift+Tab. Ridurre la finestra
a 720×500; scorrere a destra per Stato. Per provare le due azioni insieme senza
effetti esterni usare `--updates-grid-preview` nel solo eseguibile SmokeTests,
con la riga sintetica QA 0001. Riprova deve risultare completamente raggiungibile
scorrendo verticalmente. Escape chiude la fixture.

## M7: misure reali, senza installazioni

Windows 10.0.26320.0 x64, runtime .NET 10.0.14, SDK 10.0.403, Release.
Harness `--scan-m7-native`: MainViewModel e ScanServices reali su dispatcher STA,
senza finestra principale, dati applicativi GUID sotto TEMP. Diagnostica M1
abilitata soltanto nel processo; tiered compilation disabilitata solo nel processo
di misura. Sequenza: due scansioni complete, cancellazione software,
cancellazione WUA, terza scansione completa dopo le cancellazioni.

Non sono state eseguite installazioni, download di payload, UAC, riparazioni,
riavvii o modifiche ai pacchetti. Le richieste di catalogo/manifest e di intestazioni
HTTP fanno parte della scansione. Cache OS/provider/rete e carico di background
non controllati; nel periodo di misura si sono svolte anche attività di build/QA.
Questi campioni descrivono il comportamento osservato, non un benchmark isolato.

| Campione | Totale (s) | Software (s) | Driver/WUA (s) | Somma Search COM (s) |
|---|---:|---:|---:|---:|
| Baseline precedente UX | 35,809 | 11,126 | 24,423 | 24,019 |
| M7 completo 1 | 42,820 | 14,408 | 28,128 | 27,685 |
| M7 completo 2 | 44,058 | 6,720 | 37,193 | 37,176 |
| M7 completo 3, dopo annullamenti | 28,204 | 7,520 | 20,565 | 20,548 |

Tre scansioni M7: totale mediano 42,820 s, intervallo 28,204–44,058 s;
software mediano 7,520 s; WUA mediano 28,128 s. Nessuna accelerazione attribuibile
alla modifica viene dichiarata: baseline singola, condizioni e processi diversi,
nessun confronto A/B controllato.

I precedenti **24,423 s** sono spiegati da tre ricerche COM sequenziali:
9,274 + 5,901 + 8,843 = **24,019 s, 98,34%** della fase driver. Il residuo è
circa 405 ms, inclusi 376 ms di discovery. Le sorgenti sono configurata, fallback
online e Microsoft Update già registrato. Zero offerte driver rende applicabile
il fallback esistente; non sono state eliminate ricerche per accorciare la scansione.

| Lavoro interno / sottofase | M7 1 (ms) | M7 2 (ms) | M7 3 (ms) |
|---|---:|---:|---:|
| WinGet identificazione CLI | 197,399 | 133,921 | 149,106 |
| WinGet ricerca CLI | 3.924,991 | 1.890,134 | 2.380,158 |
| WinGet inventario CLI | 1.637,481 | 1.764,103 | 2.171,127 |
| Metadati manifest/dimensioni | 7.675,470 | 1.987,391 | 1.874,957 |
| Aggregazione, somma batch | 36,520 | 0,118 | 0,147 |
| Applicazione hardware/storage | 13,348 | 2,069 | 2,065 |
| Persistenza impostazioni isolate | 10,836 | 11,536 | 10,469 |

Le fasi M1 sono annidate e alcune concorrenti: non sommare tutte le durate al totale.
Quick hardware dura circa 3,75–4,13 s e si sovrappone al software. Nessuna
inefficienza di aggregazione proporzionata all'attesa WUA è stata dimostrata.

### Affidabilità e limiti

- Tutte le tre scansioni complete: `success`, 51 risultati software, zero driver
  offerti, tre ricerche WUA riuscite, zero warning, zero fasi pendenti.
- Probe dispatcher ogni 200 ms, priorità Input: gap massimo 244/212/210 ms nelle
  scansioni complete, P95 circa 207 ms. Nessun blocco prolungato del dispatcher
  osservato. Misura del dispatcher/ViewModel, non una misura di input-to-paint
  della finestra completa né una garanzia di fluidità su ogni host.
- Cancellazione software: rientro in 49 ms; stato `cancelled`, UI non busy e
  indicatore indeterminato spento. Due fasi quick erano ancora aperte alla chiusura
  del primo report M1: durata troncata, non prova di processi rimasti attivi.
- Verifica supplementare `--scan-m7-cancel`: sei processi discendenti osservati
  prima della cancellazione, **zero residui dopo 1,5 s**, rientro in 69 ms,
  zero fasi pendenti. Riutilizzato `NativeProcessSnapshot` esistente; nessun
  processo estraneo terminato o configurazione modificata.
- Cancellazione durante WUA: rientro in **7.761 ms**; una sola ricerca avviata,
  nessuna sorgente successiva; dispatcher max 214 ms. `Search` COM sincrona non
  è interrompibile dal token durante la chiamata: il token è osservato al ritorno.
  Il lavoro COM resta sul worker previsto dal provider. Nessuna parallelizzazione
  o terminazione forzata di servizi Windows introdotta.
- Nessun nuovo processo WinGet osservato dopo 1,5 s in tutti e cinque gli scenari
  principali. Il campionamento non prova l'assenza di ogni attività interna dei
  servizi Windows; i PID WinGet sono confrontati con quelli presenti prima.
- Errori/timeout esercitati offline: perdita di una sorgente mantiene gli altri
  risultati; cancellazioni in sette punti non finalizzano la scansione; un processo
  helper controllato viene terminato sia per cancellazione sia per timeout.
  Non sono stati provocati guasti dei provider reali sul computer dell'utente.

Evidenze grezze: `_output/engine-m7/native-20261108-232507.jsonl` e
`native-console.txt`; verifica figli in `native-20261108-233447.jsonl` e
`cancel-children-console.txt`. Baseline storica in
`_output/scan-progress-20261108/host-scan-m1.json`, lasciata invariata.

## Correzione M7 dimostrata: dimensioni senza richiesta del payload

`WinGetManifestSafetyService.ReadDownloadSizeAsync` ripiegava da HEAD a GET
dell'URL installer quando mancava Content-Length, pur usando ResponseHeadersRead.
La richiesta del payload non è necessaria per una dimensione facoltativa.
Ora viene inviata solo HEAD; assenza di lunghezza o risposta non riuscita produce
dimensione sconosciuta (0). Non si presenta la lunghezza di una pagina di errore
come dimensione dell'installer. Identità, versione, scope, classificazione,
integrità e autorizzazione non sono modificati.

Test con HttpMessageHandler simulato, nessuna rete: HEAD senza lunghezza, 405, 500,
HEAD valido con 12.345 byte, 404 con Content-Length di una pagina di errore.
RED con il vecchio fallback; GREEN con la modifica. Il costo massimo eliminato
è una seconda richiesta per URL quando HEAD non dà una dimensione; non è stata
misurata una riduzione temporale attribuibile a questo cambiamento.

## File e riproduzione

- `MainWindow.xaml`: unico file UI di produzione modificato.
- `Services/WinGetManifestSafetyService.cs`: sola lettura della dimensione HTTP.
- `Tests/UpdateCenter.SmokeTests/UpdatesGridTests.cs`: regressioni e preview isolate.
- `Tests/UpdateCenter.SmokeTests/ScanM7Tests.cs`: HTTP simulato, cleanup processi,
  riuso test orchestrazione e misure native M1; verifica mirata HEAD in chiusura.
- `Tests/UpdateCenter.SmokeTests/Program.cs`: dispatch soltanto nei test, sempre
  dopo l'inizializzazione obbligatoria di AppPaths con root GUID.
- Questo rapporto.

Build Release applicativa e harness: zero errori e warning, senza restore.
Superati `--updates-grid`, `--scan-m7-offline`, `--scan-m7-native`,
`--scan-m7-cancel`; `git diff --check`. Non ripetuti audit generali o test M6.

Comandi dalla radice del repository (output app/test separati):

```powershell
dotnet build UpdateCenter.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:OutDir=_output/engine-m7/app/
dotnet build Tests/UpdateCenter.SmokeTests -c Release --no-restore -m:1 -p:UseSharedCompilation=false "-p:OutDir=$((Get-Location).Path)/_output/engine-m7/smoke-final/"
& ./_output/engine-m7/smoke-final/UpdateCenter.SmokeTests.exe --updates-grid
& ./_output/engine-m7/smoke-final/UpdateCenter.SmokeTests.exe --scan-m7-offline
# Richiedono Windows e autorizzazione a scansioni reali di sola lettura:
$env:DOTNET_TieredCompilation = '0'
& ./_output/engine-m7/smoke-final/UpdateCenter.SmokeTests.exe --scan-m7-native
& ./_output/engine-m7/smoke-final/UpdateCenter.SmokeTests.exe --scan-m7-cancel
```

Build eseguibile:
`C:\Users\USER\OneDrive\Desktop\UpdateCenter\Source\UpdateCenter\_output\engine-m7\app\UpdateCenter.exe`.
Per la verifica utente sono sufficienti scansione, filtri, ordinamento, checkbox,
tastiera e annullamento; non occorre avviare aggiornamenti.

Le evidenze M7 si riferiscono a questo singolo host; l'attesa e la latenza di
cancellazione WUA restano limiti dichiarati del percorso attuale.

Per riprodurre soltanto il controllo di chiusura, senza benchmark o provider reali:

```powershell
& ./_output/engine-m7/checkpoint/smoke/UpdateCenter.SmokeTests.exe --scan-m7-head
```

Build Release del checkpoint:
`C:\Users\USER\OneDrive\Desktop\UpdateCenter\Source\UpdateCenter\_output\engine-m7\checkpoint\app\UpdateCenter.exe`.
La chiusura tecnica M7 riguarda il perimetro misurato e le correzioni documentate;
non risolve i limiti WUA e non conclude la validazione Windows di M6-B.

## Intervento A — dettagli Stato (9 novembre 2026)

Le motivazioni nella colonna Stato erano troncate da `OperationsText`
(`NoWrap`/`CharacterEllipsis`) e leggibili integralmente soltanto tramite tooltip.
Il testo compatto della cella ora è un pulsante accessibile che apre un solo
riquadro WPF `Popup`, vicino alla cella, con titolo completo, motivazione
selezionabile/copiabile, istruzioni di priorità e compatibilità già presenti nel
modello. Le azioni **Apri fonte** e **Riprova** restano nella cella, con le guardie
precedenti. Nessuna nuova libreria, finestra autonoma o modifica al runner.

Il riquadro usa le risorse Fluent, i brush e la tipografia esistenti; area
scorrevole limitata alla finestra e al work area del monitor, conversione pixel/DIP
tramite DPI dell'ancora e posizioni candidate sotto/sopra. Escape e Chiudi
ripristinano il focus sull'ancora ancora valida; clic esterno usa `StaysOpen=false`.
Scroll, cambio pagina/collezione, riciclo o distacco dell'ancora, spostamento,
ridimensionamento e deattivazione della finestra chiudono il riquadro senza
riportare il focus a una riga differente. Il popup mantiene binding all'elemento
corrente, senza snapshot del testo né popup in ogni riga virtualizzata.

Durante il controllo dei PNG reali sono emersi e corretti due ulteriori difetti:
il focus sul testo lungo faceva scorrere via il titolo e la cache della
localizzazione fissava l'intestazione in italiano. Ora il focus iniziale è su
Chiudi e l'intestazione è un binding, verificati da nuove asserzioni.

### Prove eseguite

- RED iniziale: `--update-status-ui` fallisce perché manca l'azione accessibile
  della cella. RED visuale aggiuntivo: intestazione rimasta nella lingua precedente.
- GREEN `--update-status-ui`: WPF reale su dati sintetici e root GUID; attivazione
  UIA/Click, testo completo e aggiornamento live, intestazione IT/EN, chiaro/scuro
  con il percorso reale di cambio dizionario, testo Grande, 720×500, titolo
  inizialmente visibile, scroll interno, Escape/focus, Chiudi, cambio elemento,
  scroll/riciclo di 500 righe, cambio pagina, azioni source/retry e guardia busy.
- `--updates-grid`: PASS; sei colonne, azioni, selezione, ricerca, ordinamento,
  scorrimento e virtualizzazione a 1538/1240/720 DIP.
- `--operations-ui`: PASS; IT/EN, chiaro/scuro, vuoto/500 righe, testo Grande,
  1240×790/720×500, filtri e guardie di selezione.
- Build Release integrata applicazione/harness `--no-restore`: zero errori e
  avvisi. Un tentativo intermedio ha incontrato il file exe bloccato dalla
  fixture aperta; chiusa la fixture, la build è passata.
- `git diff --check`: PASS. Nessuna scansione, download installer o installazione.

Log e PNG del **Popup.Child realmente renderizzato**, con suffissi distinti
`-long`/`-updated`: `_output/status-flyout-20261109/` (esclusi dal commit).
Comando: `UpdateCenter.SmokeTests.exe --update-status-ui`; preview desktop inerte:
`--update-status-ui-preview`. Nella preview restano abilitati soltanto gli handler
del riquadro Stato; le azioni eseguibili sono rimosse dal fixture test-only.

**Limiti:** il test automatizzato usa UIA ed eventi WPF, non certifica clic fisici
o input hardware. DPI nativi 150/200%, passaggio tra monitor, lettore di schermo e
posizionamento a tutti i bordi monitor restano da verificare sul desktop. Le prove
a testo Grande e 720×500 non sostituiscono questa matrice. Il risultato non è una
certificazione completa DPI/accessibilità né una validazione M8 finale.

Verifica desktop CUA aggiuntiva del coordinatore sulla fixture isolata a 720×500,
testo Grande: apertura con mouse, Escape con ritorno focus, riapertura con Invio,
clic esterno e scorrimento mouse osservati. La prova ha anche rilevato che la
navigazione dal testo readonly non faceva scorrere il contenitore esterno. Un
nuovo test RED lo riproduce con PageDown; il fix instrada PageDown/PageUp e
Home/End (anche Ctrl+Home/Ctrl+End) al contenitore del riquadro, preservando Shift
per la selezione del testo. Il test GREEN verifica gli offset WPF reali, il
raggiungimento delle ultime istruzioni e il ripristino dell'inizio alla riapertura.
I PNG `-end` mostrano effettivamente la fine della motivazione e le istruzioni.
La successiva verifica CUA del fix conferma Ctrl+Fine fino a `END OF REASON`
e alle istruzioni finali, Ctrl+Home fino al titolo ed Escape con ritorno focus.
La fixture è stata chiusa; nessuna finestra di prova lasciata aperta.
Le prove desktop riguardano il monitor host; la matrice DPI nativi 150/200%
resta non eseguita. A resta
implementata e verificata per le prove documentate, senza PASS complessivo della
matrice DPI e senza checkpoint locale A fino alla validazione restante.

### Prove native successive — 9 novembre 2026

Le prove aggiuntive documentate hanno verificato leggibilità, navigazione fino
alla fine del testo e ritorno del focus alle scale native esaminate. La matrice
completa dei bordi, il passaggio fra monitor e Narrator restano non verificati.
Questi risultati non costituiscono una certificazione completa di accessibilità.
I dettagli operativi della sessione personale sono conservati soltanto nel
backup locale. Lo stato di consolidamento corrente è descritto in
`CONSOLIDATION-R1-R2-2026-11-10.md`.
