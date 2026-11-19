# UpdateCenter 2.0.0 — M1 Scan Engine Baseline

Data: 8 novembre 2026. **M1 COMPLETATA per strumentazione e baseline isolata; prestazioni dei provider reali non misurate.** Nessuna ottimizzazione M2 implementata.

## Stato e perimetro

- Commit iniziale e HEAD finale: `7bf147a11eab8adaf8c2ca4d868f2beff962f7b2`.
- Branch finale: `feature/engine-m1-scan-baseline`, creato dal checkpoint. `feature/wpf-fluent-pilot` resta sullo stesso commit.
- Modifiche locali non committate. Versione 1.1.5, branding, XAML, classificazione, selezione, installazione e privilegi invariati.
- Un esploratore in sola lettura e un revisore indipendente GPT-6 Astra High; implementazione del coordinatore. Nessun audit generale o nuova dipendenza.

| File | Motivazione |
|---|---|
| `Services/ScanDiagnostics.cs` (nuovo) | Sessione diagnostica locale, timer monotoni, fasi concorrenti, contatori e campioni di processo. |
| `ViewModels/MainViewModel.cs` | Strumentazione dei confini provider, attesa quick, aggregazione, applicazione dati, persistenza ed esito. |
| `Services/QuickHardwareDataService.cs` | Rendere visibili timeout/errori che già diventavano risultati null e fallback. |
| `Services/WinGetService.cs` | Tempi identificazione/ricerca/inventario/metadati e conteggi dei filtri esistenti. |
| `Services/WindowsUpdateService.cs` | Tempi per sorgente/ricerca COM, candidati, esclusioni, duplicati e risultati. |
| `Tests/UpdateCenter.SmokeTests/ScanBaselineTests.cs` (nuovo) | Scenari isolati, campioni grezzi, controlli diagnostici e regressioni pertinenti. |
| `Tests/UpdateCenter.SmokeTests/Program.cs` | Dispatch esplicito `--scan-baseline` dopo l'inizializzazione GUID di AppPaths. |
| Questo documento | Risultati, procedura e decisioni. |

## Flusso effettivo

`MainViewModel.ScanAsync` → delegate di `ScanServices`:

1. Avvia QuickHardware: hardware e storage in parallelo, timeout cooperativo locale di 30 s.
2. Avvia/attende WinGet: identificazione CLI → ricerca aggiornamenti → inventario installato → verifica ID/deduplica → soppressioni per versioni → metadati manifest e dimensioni installer.
3. Attende il residuo del task quick; se manca l'hardware, riesegue il provider hardware.
4. Windows Update: sorgente configurata, fallback online quando previsto, eventuale Microsoft Update già registrato. Ricerca COM sincrona, esclusioni conservative, deduplica UpdateID/revisione.
5. Catalogo incorporato se l'hardware è disponibile; applicazione inventario, runtime locali, riuso storage o fallback.
6. Aggregazione per `Kind + Id` senza distinzione maiuscole/minuscole; salvataggio impostazioni e LastScanUtc solo sul percorso completato, anche parziale. Annullamento senza finalizzazione.

Hardware usa PowerShell/CIM e parsing; storage PowerShell/CIM/Get-PhysicalDisk; runtime registro/file locali. I filtri UI non sono rigetti dei provider. `HeadlessScanService`, usato dal SessionHelper, è un percorso distinto senza quick/storage: mappato, non strumentato come sessione né certificato da questi benchmark.

## Contratto diagnostico

Attivazione esclusivamente tramite variabile **del processo** `UPDATECENTER_SCAN_DIAGNOSTICS=1`; normalmente disattivata. Nessun timer/campionamento di processo o report quando disattivata; restano brevi chiamate senza raccolta dati. L'overhead disattivato non è stato quantificato.

Si riusa `LogService.WriteEvent`: una riga `operation=scan-baseline | phase=complete`, JSON in `details`, nei log locali esistenti. Schema 1: `Outcome`, `TotalMs`, `PendingPhases`, `Process`, `Counts`, `Phases`. Nessuna telemetria di rete. Etichette fisse e numeri; esclusi nomi/ID pacchetti, hardware, endpoint, messaggi di eccezione e credenziali. Questo vincolo riguarda il nuovo payload, non cambia il contenuto dei log applicativi preesistenti.

- `Stopwatch.GetTimestamp/GetElapsedTime`: millisecondi monotoni, offset d'inizio per ogni intervallo. Totale misurato direttamente; **non è la somma delle fasi**. Provider quick, WinGet e sottofasi possono sovrapporsi. `wait.quick-hardware` è solo l'attesa residua, `wait.wua-search` il tempo trascorso nella chiamata COM, non il solo tempo di rete.
- Esiti: `success`, `partial`, `cancelled`, `error`. Errori recuperati visibili nelle fasi rendono la diagnosi parziale senza cambiare il risultato applicativo. Un normale WinGet “nessun aggiornamento” con exit nonzero resta successo. I provider ancora attivi alla chiusura sono `incomplete`, con durata troncata e conteggio esplicito; una conclusione tardiva non riscrive il report.
- Contatori: risultati delle sette delegate; candidati parse WinGet, inventario, scarti combinati non verificati/duplicati, soppressioni; candidati/risultati per sorgente WUA, esclusioni conservative e duplicati; duplicati dell'aggregazione, riuso snapshot, timeout/errori quick, warning e risultati finali. Contatori assenti significano **fase non osservata**, non necessariamente zero.
- CPU: delta di `Process.TotalProcessorTime`, ms. Allocazioni: delta di `GC.GetTotalAllocatedBytes(true)`, byte. Working set e memoria privata: byte all'inizio/fine, **non picchi**. Tutto riguarda il processo corrente, include attività concorrente/strumentazione, esclude processi figli e servizi Windows; nessuna attribuzione ai provider. I campioni risorse sono vicini ai confini, non simultanei al timer; serializzazione/scrittura del report sono fuori da TotalMs. CPU osservata a passi di 15,625 ms: zero non significa assenza di lavoro.
- Successo significa assenza di problemi **osservati**: errori interni assorbiti da manifest, getter COM, enumerazione sorgenti, registro o script storage non sono tutti esposti. Il report non attesta completezza universale. Il limite log preesistente di 2 MiB/giorno può impedire ulteriori report; nessun errore diagnostico deve sostituire l'esito applicativo.

## Misurazioni

Fonte della tabella: `_output/engine-m1/measurement-baseline.log`; riepilogo riproducibile in `summary.json`, generato da `summarize.ps1` nella stessa directory. Runtime .NET 10.0.14, SDK 10.0.403, processo x64, Windows kernel 10.0.26320. Release, STA/Dispatcher WPF senza finestra, root dati GUID sotto TEMP; impostazioni/cronologia sintetiche. Nessun provider esterno, rete o installer reale.

**Produzione reale eseguita:** orchestrazione, collection/view WPF, deduplica, scrittura atomica su disco locale e diagnostica. **Simulati:** risultati/guasti/ritardi provider. Ogni scenario ha un warm-up esplicito escluso e **20 campioni conservati** (140 + 7 warm-up); ordine fisso. Tiered compilation disattivata solo nel processo di misura per ridurre variazioni JIT. Cache OS/disco non controllata: nessuna etichetta “calda/fredda”.

Mediana = media del 10°/11° campione ordinato; P95 nearest-rank = 19° su 20. P95 soltanto indicativo, campione piccolo e singolo host/sessione; non SLA o confronto statistico di ottimizzazioni.

| Scenario | Esito | Totale mediana / P95 (ms) | Min–max (ms) | Allocazioni processo mediane (byte) |
|---|---|---:|---:|---:|
| 250 software + 25 duplicati | success | 12,814 / 13,803 | 11,468–13,815 | 236.288 |
| 1.000 software + 100 duplicati | success | 31,517 / 33,375 | 29,015–33,418 | 893.576 |
| Quick ritardato: gate + `Task.Delay(25)` | success | 48,857 / 51,723 | 43,659–51,938 | 243.696 |
| Software indisponibile (IOException simulata) | partial | 14,693 / 15,374 | 13,037–15,380 | 38.288 |
| Windows Update con warning simulato | partial | 12,789 / 13,747 | 11,311–14,745 | 236.760 |
| Annullamento durante software | cancelled | 0,237 / 0,260 | 0,209–0,264 | 8.384 |
| Errore sincrono all'avvio quick | error | 0,216 / 0,240 | 0,183–0,260 | 6.512 |

| Intervallo osservato | 250 risultati | 1.000 risultati | Quick lento |
|---|---:|---:|---:|
| Aggregazione, mediana ms | 1,220 | 19,645 | 1,174 |
| Persistenza impostazioni, mediana ms | 11,241 | 11,281 | 11,722 |
| Provider quick completo, mediana ms | 0,001 | 0,001 | 36,690 |
| Attesa residua quick, mediana ms | <0,001 | <0,001 | 35,572 |
| CPU processo, mediana ms | 0* | 31,250 | 0* |
| Working set inizio/fine, mediane byte | 93.984.768 / 94.224.384 | 118.953.984 / 119.867.392 | 136.138.752 / 136.138.752 |
| Memoria privata inizio/fine, mediane byte | 41.408.512 / 41.639.936 | 66.365.440 / 67.250.176 | 83.103.744 / 83.103.744 |

*Granularità CPU indicata sopra. Le mediane memoria descrivono lo stato cumulativo di un processo riutilizzato, non il costo netto attribuibile allo scenario. Solo le chiamate `aggregation.updates` disgiunte nella stessa scansione sono sommate prima della mediana; nessuna somma dei provider sovrapposti. Il delay di 25 ms è un minimo schedulato, non una durata attesa esatta.

## Verifica e riproduzione

Dalla radice Git, PowerShell, senza elevazione:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '_output/engine-m1/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
dotnet build Tests/UpdateCenter.SmokeTests -c Release --no-restore -m:1 -p:UseSharedCompilation=false
$env:DOTNET_TieredCompilation = '0'
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --scan-baseline
git diff --check
```

Usare una nuova destinazione log per nuove misure; conservare l'output intero, ambiente e tutti i campioni. Il test abilita/ripristina la variabile diagnostica e inizializza AppPaths prima di qualsiasi I/O. Non serve avviare l'app produttiva. Gli asset di restore erano già disponibili; nessun restore/download NuGet.

- Baseline pre-modifica: comando harness `--quality-hardening`, PASS (`baseline-quality.log`).
- TDD: `red-test.log` rileva l'assenza del report; `source-counts-red-test.log` l'assenza delle chiavi per sorgente; `no-updates-red-test.log` riproduce la falsa diagnosi parziale; `persistence-red-test.log` rileva l'intervallo mancante. Fallimenti attesi, non regressioni lasciate aperte.
- Build conclusiva dei progetti interessati e dipendenze: **PASS, 0 warning/0 errori**, `measurement-build.log`.
- Harness conclusivo: **PASS**, `measurement-baseline.log`. Verifica opt-in, esiti, conteggi, privacy, intervalli entro il totale, sovrapposizione/attesa residua, task tardivi e ripristino contesto. Storia preservata byte-per-byte e LastScanUtc coerente con disco; cancellati/falliti non persistono successo.
- Inclusi nello stesso modo: `ScanHardeningTests` (snapshot freschi, avvio tardivo, annullamento nelle sette fasi), `QuickHardwareHardeningTests`, `WindowsUpdateSafetyTests` e `WinGetExecutionTests`. WUA/installer e WinGet sono fixture; nessuna installazione/download reale. Test diretti aggiunti per conteggi WUA, firmware escluso e codici WinGet no-updates.
- Verifica finale diff e revisione indipendente mirata; nessun rilievo residuo. Nessuna suite UI/LAN generale o migrazione ripetuta. I log `green-*`, `final-*`, `acceptance-*` conservano le esecuzioni intermedie; non sono mescolati alla tabella conclusiva.

## Tre problemi prioritari e prossima decisione

1. **Aggregazione quadratica, evidenza misurata.** `MainViewModel.AddUpdates` esegue `Updates.Any` per candidato. Con 4× elementi l'intervallo reale di aggregazione della fixture passa da 1,220 a 19,645 ms (circa 16,1×); differenza 18,425 ms su questo host. L'intervallo include anche collection/view/notifiche, quindi non attribuisce tutto il costo alla sola ricerca. Primo intervento proposto: indice temporaneo per `(Kind, Id case-insensitive)` preservando ordine, selezioni e handler; confrontare gli stessi campioni e regressioni prima di accettarlo. Nessun guadagno reale WinGet/WUA stimato.
2. **Persistenza sincrona sul dispatcher.** `persist.settings` misura circa 11,2 ms nelle fixture completate; il salvataggio usa già flush/sostituzione atomica. È un costo locale concreto, non prova di una regressione o autorizzazione ad allentare la durabilità. Nessuna causa assegnata a OneDrive/antivirus e nessuna modifica proposta prima di una misura dedicata dell'I/O.
3. **Cancellazione WUA non interrompe Search.** `WindowsUpdateService.SearchSource` controlla il token prima della chiamata sincrona `searcher.Search` e durante l'elaborazione successiva; non passa una cancellazione/deadline alla COM. Evidenza statica, latenza reale sconosciuta. Impatto: una richiesta di annullamento può attendere il ritorno della ricerca. La baseline misura quel tratto ma non lo corregge né attribuisce un timeout inventato.

M2 resta da autorizzare; proposta iniziale circoscritta al punto 1.

## Limiti, scansioni reali e anomalia ambientale

**Scansione completa reale non eseguita:** WinGet richiama manifest via HTTP e, per conoscere la dimensione, può effettuare un GET all'URL installer (`WinGetManifestSafetyService.ReadDownloadSizeAsync`). CLI scan/list accettano accordi sorgenti; WUA opera online. Non è garantibile l'assenza di trasferimenti pacchetto o modifiche alle cache/sorgenti sotto i divieti correnti. Non sono stati rimossi controlli metadati per far passare il benchmark.

Procedura manuale sicura immediatamente disponibile: eseguire i comandi isolati sopra, verificare il PASS e conservare il log completo; non premere Avvia scansione nell'app produttiva per ottenere una presunta baseline innocua. Per misure future dei provider reali, predisporre prima una VM/profilo sacrificabile senza elevazione, root dati isolata e un percorso di acquisizione di cui siano verificabili gli effetti di rete. Se il percorso può ancora inviare GET ai pacchetti o accettare accordi, **fermarsi**: serve un incarico separato per quel perimetro. Nessun numero simulato va usato come baseline real-world. Non sono state validate cache fredde/calde, rete, hardware reale, LAN, altri OS o tempi delle installazioni.

**Anomalia documentata:** nel primo comando `dotnet run --quality-hardening`, con una home CLI nuova, l'SDK ha stampato «È stato installato un certificato di sviluppo HTTPS ASP.NET Core» (`baseline-quality.log`). La generazione non era intenzionale e contrasta con il vincolo sui certificati; lo store effettivo non è stato ispezionato. Tutti i comandi successivi hanno `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`. Nessun comando di trust, rimozione o altra gestione certificati è stato eseguito; non si dichiara che quell'effetto sia stato annullato. Nessuna modifica UAC, antivirus, ACL di produzione o configurazione globale è stata eseguita deliberatamente.
