# UpdateCenter 2.0.0 — M2 Efficient Scan Aggregation

8 novembre 2026 — **M2 COMPLETATA**: ricerca dei duplicati indicizzata, equivalenza verificata nei percorsi effettivi e beneficio misurato. La crescita dell'intera aggregazione resta elevata per il costo residuo degli inserimenti; nessun intervento M3 avviato.

## Checkpoint e file

- Checkpoint M1 locale: **`b4c7c0739dcbb0563c37bb37297002fba19bc6da`**, branch `feature/engine-m1-scan-baseline`.
- Branch M2: **`feature/engine-m2-dedup`**, creato dal checkpoint M1.
- `feature/wpf-fluent-pilot` invariato a `7bf147a11eab8adaf8c2ca4d868f2beff962f7b2`.
- Il commit M2 è quello che introduce questo documento; ricavabile con `git log -1 --format=%H -- docs/ENGINE-M2-DEDUP.md`. Nessun push, merge, tag o release.

M1 è stata verificata prima del commit: insieme degli otto file conforme al suo report, diff e controllo mirato di pattern di segreti senza rilievi, staging nominativo, build 0 errori/avvisi e harness M1 PASS. Log e artefatti generati sono rimasti esclusi. Il rapporto M1 conserva lo stato storico precedente al suo commit.

| File M2 | Modifica |
|---|---|
| `ViewModels/MainViewModel.cs` | Solo deduplicazione in `AddUpdates`: indice temporaneo e fallback per ID nulli. |
| `Tests/UpdateCenter.SmokeTests/ScanAggregationTests.cs` | Test di caratterizzazione/equivalenza e tre prove supplementari del costo residuo. |
| `Tests/UpdateCenter.SmokeTests/Program.cs` | Dispatch `--scan-aggregation`, dopo la root GUID isolata. |
| Questo documento | Risultati, limiti e prossima decisione. |

Nessuna modifica a diagnostica/fixture M1, provider, filtri applicativi, persistenza, XAML, versione, branding, installazioni o LAN. Un solo revisore indipendente GPT-6 Astra Ultra, in sola lettura; nessun rilievo concreto.

## Algoritmo ed equivalenza

Prima, ogni candidato attraversava `Updates.Any`, con predicato `existing.Kind == item.Kind && existing.Id.Equals(item.Id, OrdinalIgnoreCase)`. Per m elementi già presenti e n candidati, il confronto poteva costare O(n·m + n²).

Ora ogni chiamata ricostruisce dalla collection effettiva un `Dictionary<UpdateKind, HashSet<string>>`, con `StringComparer.OrdinalIgnoreCase`. L'indice viene aggiornato in ordine di arrivo; un'identità già presente scarta il candidato senza sostituire il primo oggetto. La ricerca ha costo atteso O(m+n), memoria temporanea O(m+n); gli inserimenti/notifiche WPF restano gli stessi e non diventano automaticamente lineari. Nessuna cache sopravvive alla chiamata. L'indice viene ricostruito anche nei batch vuoti: piccolo costo aggiuntivo documentato, non ottimizzato ulteriormente.

Un ID nullo mantiene il percorso originale `Any` per il batch: la prima occorrenza può essere accettata; un confronto successivo dello stesso Kind può generare `NullReferenceException`; un'identità valida incontrata prima del nullo conserva lo short-circuit. Non si introducono normalizzazioni, convalide o deduplicazione silenziosa dei null. Questi casi anomali conservano anche la complessità originale. ID vuoti, spazi, caratteri Unicode, NUL incorporati e Kind non definiti seguono il confronto precedente.

I quattro chiamanti effettivi passano risultati provider o liste di refresh sul dispatcher. Nessuno modifica `Updates` durante l'enumerazione del medesimo batch. Non è dichiarata equivalenza per enumerabili/callback arbitrari che mutino la collection durante quel batch; non sono un comportamento dei chiamanti esaminati. Le mutazioni **fra** chiamate, inclusi Clear/rimozione/inizializzazione, sono coperte.

## Verifica funzionale

Test scritti prima dell'ottimizzazione e passati contro M1 (`characterization-test.log`). Una mutazione temporanea del solo confronto in case-sensitive è stata rilevata dal test di identità/ordine (`mutation-red-test.log`); ripristinata e verificata con diff vuoto del file prima dell'implementazione finale. Nessuna mutazione residua.

Il nuovo harness verifica:

- Duplicati dello stesso oggetto e distinti, differenze di case, stesso ID con Kind diverso, duplicati fra software/Windows Update/catalogo.
- Ordine degli oggetti e notifiche Add; selezione, dettagli, progresso e priorità del primo candidato.
- Un solo handler per gli accettati, nessuno per gli scartati; aggiornamento della vista filtrata al cambio stato; distacco dopo Clear/scansione successiva; notifiche finali anche con input vuoto.
- Collection già inizializzata, rimozioni e Clear fra chiamate; nessuna sottoscrizione aggiunta agli elementi preesistenti inseriti esternamente.
- Semantica nulla/ID particolari, inclusa eccezione e collection parziale preservata.
- Annullamento con risultati parziali, scansioni ripetute con nuovi oggetti, warning provider, conteggio candidati prima della deduplica e LastScanUtc coerente col disco.

`ScanHardeningTests` è riutilizzato. L'harness M1 aggiunge le sue regressioni di snapshot, cancellazione, firmware, WinGet vincolato, diagnostica e integrità dello stato. Il refresh incrementale è verificato indirettamente con rimozioni/aggregazioni e ispezione del chiamante; non viene invocato direttamente `RefreshDriversAfterUpdateRunAsync`.

## Misure prima/dopo

Prima: `_output/engine-m2/before-baseline.log`, su M1 verificata. Dopo: `after-baseline.log`, con la sola ottimizzazione di produzione. Riepilogo: `comparison.json`, riproducibile con `compare.ps1` nella stessa cartella. Nessun log M1 sovrascritto.

Condizioni identiche al protocollo M1: Release x64, SDK 10.0.403/runtime .NET 10.0.14, Windows kernel 10.0.26320, STA/Dispatcher senza finestra, root GUID TEMP, provider simulati, stessa fixture e ordine scenari. `DOTNET_TieredCompilation=0` nel processo; cache OS/disco non controllata. Ogni esecuzione conserva 20 campioni per scenario e un warm-up escluso: **140 campioni + 7 warm-up prima e altrettanti dopo**. P95 nearest-rank, 19° valore su 20, soltanto indicativo su un host/sessione; nessuna inferenza statistica di popolazione.

| Risultati + duplicati | Aggregazione mediana prima → dopo (ms) | P95 prima → dopo (ms) | Allocazioni processo mediane prima → dopo (byte) |
|---|---:|---:|---:|
| 250 + 25 | 1,101 → 0,609 | 2,033 → 0,812 | 236.288 → 256.600 |
| 1.000 + 100 | 18,904 → 8,250 | 21,612 → 10,844 | 893.576 → 999.400 |

Beneficio dell'aggregazione a 1.000 risultati: **−56,36%, 2,29×**, differenza di mediana 10,654 ms. Scenario piccolo: **−44,67%**. Crescita 1.000/250: **17,17× → 13,54×**; miglioramento limitato, non linearità dell'intera pipeline. Il riferimento storico M1 era 1,220/19,645 ms e 16,1×; la nuova misura prima/dopo è il confronto principale, senza selezionare campioni favorevoli.

| Scenario M1 | Totale mediana prima → dopo (ms) | Totale P95 prima → dopo (ms) |
|---|---:|---:|
| Normale, 250 | 12,023 → 11,610 | 14,796 → 13,777 |
| Grande, 1.000 | 29,937 → 19,879 | 33,525 → 22,579 |
| Quick lento simulato | 49,448 → 46,724 | 51,853 → 51,076 |
| Software indisponibile | 13,775 → 14,868 | 15,194 → 16,635 |
| Warning parziale WUA | 11,649 → 11,403 | 13,428 → 12,829 |
| Annullamento | 0,225 → 0,204 | 0,297 → 0,251 |
| Errore sincrono | 0,193 → 0,240 | 0,223 → 0,369 |

Il totale sintetico a 1.000 migliora del 33,60%; **non è un'accelerazione misurata di WinGet/Windows Update**. Le piccole variazioni negli scenari senza risultati non dimostrano causalità: l'aggregazione del provider indisponibile passa da 0,0060 a 0,00695 ms; il suo totale include I/O/log/persistenza variabili. Lo scenario errore termina prima di `AddUpdates`. Nessun campione scartato.

Tradeoff: allocazioni di processo mediane +20.312 byte a 250 e +105.824 byte a 1.000 (+8,6%/+11,8% circa). Sono delta dell'intera scansione, non memoria residente o misura esclusiva dell'indice. L'indice temporaneo richiede allocazioni per batch, senza ritenzione persistente. Non si dichiara assenza di regressione allocativa.

## Costo residuo e limiti

Prove supplementari distinte dal benchmark M1, 20 campioni + warm-up ciascuna, stesso dispatcher e diagnostica attiva (`final-equivalence-probes.log`, `probe-summary.json`):

| Una chiamata AddUpdates, 1.000 elementi | Mediana / P95 (ms) | Allocazioni mediane del thread (byte) |
|---|---:|---:|
| Inserimento di 1.000 nuovi + 100 duplicati | 7,813 / 10,684 | 397.408 |
| Collection già popolata, 1.100 soli duplicati | 0,204 / 0,411 | 108.264 |
| Collection già popolata, batch vuoto/refresh | 0,075 / 0,131 | 81.688 |

Timer monotono attorno alla chiamata sincrona; allocazioni del solo thread misurate senza await. Gli scenari mantengono i filtri WPF. Il residuo è circoscritto al percorso con nuovi inserimenti e relative sottoscrizioni/notifiche/aggiornamento della view; non alla sola ricerca indicizzata o al refresh finale. Queste prove non isolano un singolo metodo interno WPF. Non sono stati introdotti batching, sospensione notifiche o modifiche ai binding per migliorare il numero.

Obiettivo sotto 10 ms raggiunto per la mediana a 1.000, non per il P95. La crescita complessiva resta alta; null ID conserva il fallback lento. Nessuna regressione funzionale rilevata nei controlli eseguiti, nessuna pretesa di copertura universale o misura di scansioni reali.

## Comandi, esiti e prossima decisione

Prima di ogni comando .NET sono state impostate esclusivamente nel processo:

```powershell
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '_output/engine-m1/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
dotnet build Tests/UpdateCenter.SmokeTests -c Release --no-restore -m:1 -p:UseSharedCompilation=false
$env:DOTNET_TieredCompilation = '0'
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --scan-aggregation
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --scan-baseline
git diff --check
```

Build M1, caratterizzazione, build ottimizzata e build finale: **PASS, 0 errori/0 warning**. Mutazione case-sensitive: **FAIL atteso**, poi rimossa. Equivalenza finale: **PASS** (`final-equivalence-probes.log`). Benchmark/regressioni M1 prima e dopo: **PASS** (`before-baseline.log`, `after-baseline.log`). L'ultima aggiunta riguarda solo le prove supplementari nel nuovo harness; fixture e codice di produzione della misura dopo sono invariati. Diff/staging verificati nominativamente prima dei commit.

Nessuna installazione, scansione reale, download o modifica a certificati/UAC/antivirus/ACL/configurazione di sistema. L'anomalia HTTPS SDK di M1 resta soltanto documentata, senza tentativi di gestione certificati. Nessuna nuova dipendenza, restore, suite UI/LAN generale o audit architetturale.

**Raccomandazione per un eventuale M3:** investigare separatamente il costo delle notifiche di inserimento e della vista WPF, definendo prima le invarianti di binding/eventi e misure di reattività. Il presente risultato non autorizza batching o modifiche della persistenza. M3 richiede un nuovo incarico.
