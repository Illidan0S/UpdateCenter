# UpdateCenter 2.0.0 — M4 Windows Update Detection Accuracy & Coverage

8 novembre 2026 — **M4 COMPLETATA** per diagnostica e correttezza WUA, verificata con fixture isolate. Nessun aumento di copertura reale dichiarato; classi ammesse e protezioni firmware conservate.

## Checkpoint e perimetro

- Partenza verificata: `feature/engine-m2-dedup`, HEAD M2 `88359bb50178bcac076061999f744f5eacc551a7`, solo rapporto M3 non tracciato.
- Commit locale dedicato M3: **`d4053fbc6086032fc14b65cb86781e75210f5710`**, staging del solo rapporto.
- Branch M4: **`feature/engine-m4-wua-coverage`**, creato da M3. Il commit M4 è quello che introduce questo documento, identificabile con `git log -1 --format=%H -- docs/ENGINE-M4-WUA-COVERAGE.md`.
- Nessun provider esterno, modifica WinGet/WPF/LAN, registrazione di servizi WUA, cambio policy/privilegi, installazione o download reale. Nessun push, merge, tag o release. Checkpoint precedenti conservati.
- Applicate le skill locali systematic-debugging, test-driven-development e verification-before-completion. Implementazione del coordinatore; un solo revisore indipendente GPT-6 Astra Ultra, in sola lettura. Nessun audit generale o benchmark M1/M2 ripetuto.

## File modificati

| File | Motivazione |
|---|---|
| [WindowsUpdateService.cs](<../Services/WindowsUpdateService.cs>) | Esiti di ricerca, warning, fallback, isolamento degli errori di lettura e ragioni strutturate di esclusione. Accumulo diagnostico locale per sorgente. |
| [ScanDiagnostics.cs](<../Services/ScanDiagnostics.cs>) | Rilevare una sessione attiva e comporre nomi di contatori soltanto quando necessaria; schema del report invariato. |
| [WindowsUpdateCoverageTests.cs](<../Tests/UpdateCenter.SmokeTests/WindowsUpdateCoverageTests.cs>) | Nuove fixture WUA, privacy, cancellazione, sorgenti e misura riproducibile. |
| [WindowsUpdateSafetyTests.cs](<../Tests/UpdateCenter.SmokeTests/WindowsUpdateSafetyTests.cs>) | Ricontrollo scan/install anche per SecurityDevices, System e Processor; doppi COM completati con proprietà WUA documentate. |
| [Program.cs](<../Tests/UpdateCenter.SmokeTests/Program.cs>) | Dispatch `--wua-coverage` e `--wua-coverage-measure` dopo l'inizializzazione obbligatoria della root GUID; riuso di ScanHardeningTests senza benchmark generali. |
| Questo documento | Decisioni, prove, misure e limiti. |

## Difetti dimostrati e correzioni

**Esiti WUA ignorati.** Prima, una ricerca con ResultCode diverso da successo poteva essere considerata riuscita e i suoi candidati potevano essere ammessi. La fixture RED riproduce l'errore anche con un'offerta apparentemente valida. Ora il codice accetta i risultati di `Succeeded=2` e `SucceededWithErrors=3`; il secondo resta parziale. Esiti 0/1/4/5, sconosciuti o illeggibili falliscono la sorgente e non ne ammettono i candidati. Se falliscono tutte le sorgenti, viene sollevato un errore, non restituito uno zero riuscito. Sono le semantiche definite da [ISearchResult.ResultCode](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-isearchresult-get_resultcode) e [OperationResultCode](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/ne-wuapi-operationresultcode).

**Fallback soppresso da un risultato parziale.** Prima, bastava un'offerta ammessa dalla sorgente configurata per saltare il servizio pubblico, anche se il risultato era incompleto. Ora il fallback viene saltato soltanto quando la ricerca configurata è completa e contiene almeno un'offerta ammessa. Candidati validi già ottenuti da una sorgente parziale vengono conservati e deduplicati con quelli successivi. La fixture dimostra il recupero di una seconda offerta dal fallback; è un risultato sintetico, non una misura del PC di riferimento.

**Warning e letture fallite nascosti.** Il conteggio [ISearchResult.Warnings](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-isearchresult-get_warnings) viene letto senza registrarne il testo. Warning presenti o illeggibili rendono la sorgente parziale. Un errore su metadati obbligatori esclude il candidato; su metadati opzionali conserva i fallback preesistenti, ma segnala parzialità. Un elemento della raccolta illeggibile non elimina le offerte leggibili successive. Una raccolta Updates nulla/illeggibile fallisce la sorgente e permette il fallback: anche questo difetto è stato riprodotto RED prima della correzione.

**Enumerazione servizi e log.** Un errore nell'enumerazione dei servizi registrati è ora osservabile e produce un warning; le sorgenti già note restano interrogabili. Microsoft Update viene usato solo se già registrato. I warning confluiscono nel contratto esistente `DriverScanResult.SourceWarnings`, già consumato da scansione interattiva e headless. I log distinguono completo/parziale e, nel percorso di scansione, usano etichette fisse e codici numerici al posto dei messaggi arbitrari delle eccezioni COM.

## Diagnostica implementata

Sempre opt-in tramite **`UPDATECENTER_SCAN_DIAGNOSTICS=1` nel processo**. Si riusa il report locale M1 `scan-baseline`, schema 1; nessuna telemetria. I prefissi per sorgente sono `wua.configured`, `wua.online`, `wua.microsoft`; le quantità corrispondenti vengono anche aggregate sotto `wua`.

| Contatore/famiglia | Significato |
|---|---|
| `wua.sources-queried`, `<sorgente>.queried` | Chiamate Search effettivamente avviate; una creazione COM fallita prima di Search non incrementa questi contatori. |
| `wua.sources-skipped`, `<sorgente>.skipped` | Fallback deliberatamente saltato. |
| `wua.microsoft.registered`, `wua.discovery-errors` e fase `wua.discovery` | Servizio MU trovato oppure enumerazione incompleta. Nessun servizio aggiunto. |
| `result.succeeded`, `result.succeeded-with-errors`, `result.failed`, `result.aborted`, `result.invalid` | Esito restituito dalla sorgente; gli stati non finali/sconosciuti/illeggibili rientrano in invalid. |
| `candidates` | Dimensione della raccolta leggibile dopo un esito utilizzabile. Con esito fallito la raccolta non viene usata: valore assente, non zero inventato. |
| `examined` | Posizioni visitate nella raccolta, incluse quelle illeggibili. |
| `excluded` e `excluded.*` | Un motivo primario per candidato escluso: `firmware`, `class-not-allowed`, `invalid-identity`, `incomplete-metadata`, `metadata-read-error`. |
| `rejected-class.*`, `eligible-class.*` | Istogrammi di classi note, con etichette fisse e alias normalizzati. Testi arbitrari confluiscono in `other`; nessuna identità hardware viene registrata. |
| `eligible` | Offerte che superano il guard condiviso; comprende ancora duplicati. |
| `admitted`, `duplicates`, `wua.returned` | Offerte uniche accettate dopo deduplica UpdateID/revisione, duplicati e totale finale. La lista degli aggiornamenti restituiti conserva le identità necessarie all'app, non le riversa nei nuovi log. |
| `<sorgente>.returned` | Compatibilità M1: offerte restituite dalla sorgente dopo i filtri, prima della deduplica globale. |
| `no-offers`, `all-excluded` | Raccolta vuota oppure raccolta non vuota senza offerte ammesse. Non sostituiscono l'esito complete/partial. |
| `complete`, `partial`, `warnings`, `warning-read-errors`, `metadata-read-errors`, `collection-read-errors`, `search-errors` | Completezza e guasti distinti. metadata-read-errors conta candidati con errori, non il numero di getter falliti. |

La valutazione dell'identità WUA precede quella degli altri metadati. Per questi ultimi la priorità diagnostica è errore di lettura, evidenza firmware, classe, ID hardware e altri campi necessari. Ogni candidato ha un solo motivo primario; una classe viene conteggiata se letta durante quella valutazione. Gli istogrammi non identificano i dispositivi né dimostrano la loro applicabilità.

Con enumerazione terminata, `examined = excluded + eligible`; dopo l'aggregazione delle sorgenti utilizzabili, `eligible = admitted + duplicates`. Durante cancellazione o errore non si impongono queste uguaglianze: i contatori descrivono il lavoro arrivato a quel punto. Le chiavi non raggiunte possono essere assenti; non significano automaticamente una fase riuscita con zero risultati.

I contatori si accumulano in un dizionario locale per sorgente, creato solo con diagnostica attiva e riversato su ScanDiagnostics anche in uscita per errore/cancellazione. Quando disattivata non vengono creati accumulatori, istogrammi o nomi per candidato. I normali controlli di correttezza WUA restano sempre attivi.

## Classi e protezioni: esito del Gate B

**Allowlist invariata.** SecurityDevices, System, Processor, Extension e SoftwareComponent restano escluse. Le fixture dimostrano il rifiuto; non provano che il PSP reale debba essere ammesso. Nessun nuovo criterio generale sufficientemente documentato è emerso per distinguere tutti i contenuti di quelle classi in sicurezza.

Le ragioni vengono ora restituite dal medesimo controllo usato da scansione e ricontrollo sul candidato fresco prima dell'installazione. L'insieme ammesso resta equivalente al checkpoint M3: identità valida, metadati necessari, classe ammessa e nessuna evidenza firmware/BIOS/UEFI/GUID firmware. I test verificano che i casi respinti non arrivino a EULA, collection di installazione, downloader o installer simulati. Il caso Display ammesso raggiunge soltanto il doppio in memoria del downloader, senza rete né pacchetti.

Restano invariati firma/hash, versione, consenso, query WUA, esclusione aggiornamenti nascosti e impostazione sugli aggiornamenti potenzialmente sostituiti. Nessuna modifica a policy/WSUS o ai servizi registrati. Le correzioni funzionali del Gate B riguardano esiti, fallback e recupero da letture fallite, non l'ampliamento delle classi.

## Verifiche e log

Artefatti locali esclusi da Git: [cartella engine-m4](<../_output/engine-m4>).

| Verifica | Esito/evidenza |
|---|---|
| Primo TDD | RED atteso: contatori mancanti, esiti ignorati, fallback parziale soppresso, errori metadati/discovery nascosti. `red-build.log` build PASS; `red-test.log` fallimenti delle asserzioni, non errori di compilazione. |
| Raccolta Updates illeggibile | RED mirato in `collection-red-test.log`; corretto prima dell'accettazione. |
| Build finale Release | **PASS, 0 warning/0 errori**, `final-build.log`; progetto smoke e dipendenze, senza restore. |
| Harness M4 | **PASS**, `final-test.log`: 12 gruppi, inclusi safety test esistenti ampliati. Zero offerte; tutti esclusi; offerte ammesse/respinte; 27 etichette di classe ammesse; facoltativi; risultati parziali/warning; esiti falliti; COM; metadati obbligatori/opzionali; raccolta illeggibile; servizi; duplicati; privacy; opt-in; cancellazione prima, dopo Search e durante enumerazione. |
| Regressioni di orchestrazione | **PASS**, ScanHardeningTests con provider finti: snapshot, errori e annullamento nelle fasi, persistenza nella root isolata. Nessun benchmark M1/M2. |
| Sicurezza | Firmware mascherato, classi escluse e metadati insufficienti restano respinti anche al confine di installazione. Nessun COM reale creato dalle fixture. |
| Revisione indipendente | Nessun rilievo azionabile nei cinque file di codice/test, inclusi i contatori finali. Revisione statica; non sostituisce i test del coordinatore. |
| Git | Diff whitespace controllato; staging nominativo dei sei file M4, nessun log/artefatto incluso. |

I log applicativi delle fixture finali sono copiati anche in `final-UpdateCenter-*.log`, oltre alla console. I log intermedi rimangono separati dai risultati finali.

**Limite ambientale accessorio:** `dotnet --info` ha segnalato accesso negato al Service Control Manager durante l'enumerazione dei workload; dettaglio in `dotnet-info.log`. Non riprovato con privilegi. SDK 10.0.403 e host 10.0.14 risultano nell'output; build e harness successivi sono completati. Non è una scansione driver eseguita né un fallimento dei test applicativi. Nessuna modifica al sistema o gestione certificati è stata effettuata.

## Overhead misurato

Confronto locale prima/dopo con lo stesso harness e input: 250 offerte sintetiche, 200 ammesse e 50 System escluse, identità deterministiche, inventario vuoto, sorgente configurata riuscita. Ricerca COM e rete simulate; parsing, filtri, aggregazione, diagnostica e log su disco eseguiti realmente.

Release x64, `DOTNET_TieredCompilation=0`; 3 warm-up per modalità esclusi e **20 campioni conservati per modalità**, OFF/ON alternati invertendo l'ordine. Tempo totale della chiamata inclusa chiusura diagnostica/log; allocazioni del thread corrente. Cache disco/OS e carico host non controllati. Mediana dei due valori centrali; P95 nearest-rank, 19° su 20, indicativo.

| Modalità | Mediana prima → dopo (ms) | P95 prima → dopo (ms) | Allocazioni mediane prima → dopo (byte) |
|---|---:|---:|---:|
| Diagnostica OFF | 5,817 → **5,781** | 6,958 → **6,628** | 724.168 → **624.216** |
| Diagnostica ON | 9,081 → **8,751** | 10,257 → **9,958** | 740.976 → **681.152** |

La modalità normalmente disattivata non mostra un aumento nella misura conclusiva; la piccola variazione temporale non dimostra un'accelerazione. A M4, ON costa circa **2,970 ms** e **56.936 byte** in più di OFF su questa fixture, includendo serializzazione e scrittura del report. Non è una previsione della latenza WUA reale.

Una prima versione con aggiornamenti dei contatori per ogni candidato mostrava maggiore costo (`first-after-measure.log`, mediana OFF 7,653 ms, ON 11,606 ms). È stata sostituita dall'accumulo locale e rimisurata per questa ragione. I campioni non sono mescolati o selezionati; `before-measure.log` e `after-measure.log` sono i confronti conclusivi, `summarize.py` produce `measurement-summary.json`. Non si attribuisce tutta la variazione di tempo alla sola modifica dei contatori.

### Riproduzione mirata

Dalla radice Git, senza elevazione, con asset di restore già disponibili; scegliere nuovi nomi dei log per conservare quelli esistenti:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '_output/engine-m1/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_TieredCompilation = '0'
dotnet build Tests/UpdateCenter.SmokeTests -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --wua-coverage
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --wua-coverage-measure
```

La root dati GUID viene inizializzata prima del dispatch; è verificata distinta da quella di produzione. Le variabili sono del processo. Nessuna suite completa che legga hardware reale o avvii sorgenti non pertinenti è stata eseguita.

## Copertura residua e prossimo intervento

M4 risolve cause riproducibili di falsa completezza e di mancato fallback. Rimangono: catalogo incorporato vuoto, classi conservative escluse, targeting/Manual/OEM/policy di WUA, limiti di matching e raggruppamento descritti in M3. La chiamata COM Search rimane sincrona: la cancellazione viene osservata al ritorno, non interrompe la ricerca in corso.

**Non è quantificabile quanto dei 13 contro 0 dipenda da WUA.** La perdita di offerte in presenza di un esito parziale è dimostrata solo in fixture; il rifiuto di certe classi è certo nel codice, ma mancano i candidati WUA reali del PC. Le versioni AMD esistenti non dimostrano compatibilità o necessità e non sono state trasformate in nuove offerte installabili.

**Unica raccomandazione successiva:** una sessione diagnostica autorizzata e circoscritta al solo rilevamento WUA sul PC di riferimento, senza WinGet, download o installazioni, per correlare sorgenti/esiti/esclusioni con l'elenco delle 13 proposte. Va definita come incarico separato; M4 non la esegue. Questa evidenza permetterà di scegliere fra una correzione WUA dimostrata e una fonte ufficiale esterna, senza ampliare preventivamente l'allowlist. Nessuna M5 avviata.
