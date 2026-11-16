# UpdateCenter — audit e modernizzazione del 4 novembre 2026

Rapporto sul repository locale `Source/UpdateCenter`, non su una copia scaricata da GitHub.
Le evidenze di audit si riferiscono al commit iniziale `2e3ed78`; la validazione finale della migrazione è indicata separatamente.

## 1. Stato iniziale e perimetro

- Working tree pulita su `main`, allineata al riferimento locale `origin/main`; nessun fetch usato per presumere lo stato remoto.
- HEAD `2e3ed78` (`test: stabilize heartbeat smoke test`), successivo a `24a6166` (`release: UpdateCenter 1.1.5`).
- Remote: `https://github.com/Illidan0S/UpdateCenter.git`. Branch locale creato: `modernization/net10-v1.2.0`.
- `UpdateCenter.sln` contiene 9 progetti: GUI WPF, Contracts, Core, RemoteClient, Agent, SessionHelper, NetworkConsole e due harness di test.
- SDK installati: `8.0.424`, `10.0.403`; nessun `global.json` iniziale. Tutti i progetti inizialmente su .NET 8; minimo Windows conservato: `10.0.17763.0`.
- GUI e componenti eseguibili distribuiti self-contained `win-x64`; Setup Inno e Portable hanno flussi distinti. Versioni applicative esistenti da conservare, senza anticipare la numerazione 1.2.0.
- Nessun aggiornamento, driver, servizio Agent o firmware installato per l'audit. Nessun commit, push, tag, merge o pubblicazione autorizzato da questo incarico.

## 2. Skill: selezione, provenienza e limiti

Inventario personale rilevante: `astra-team`, `systematic-debugging`, `test-driven-development`, `verification-before-completion`; disponibili anche `ai-research`, `design-taste-frontend`, `external-model-reviewers`, `find-skills` e le skill di sistema `openai-docs`, `skill-creator`, `skill-installer`. Non risultavano skill di progetto preesistenti nel repository corrente.
Sono state applicate le skill di orchestrazione, creazione/installazione e consultazione della documentazione; la diagnostica ha seguito `systematic-debugging`. La verifica conclusiva richiede evidenze fresche.

Fonte Microsoft: [dotnet/skills](https://github.com/dotnet/skills/tree/9837da685079d576feb27f9dd4f87631d3d94107), commit verificato e fissato `9837da685079d576feb27f9dd4f87631d3d94107`, [licenza MIT](https://github.com/dotnet/skills/blob/9837da685079d576feb27f9dd4f87631d3d94107/LICENSE).
Installazione selettiva nel repository tramite il helper ufficiale `skill-installer`, con riferimenti originali; licenza conservata in `.agents/skills/dotnet-skills-LICENSE.txt`. Nessun pacchetto completo o modifica globale delle skill personali.

| Skill valutata | Decisione e motivazione |
|---|---|
| `migrate-dotnet8-to-dotnet9` | Installata da `plugins/dotnet-upgrade/skills/`: revisione delle incompatibilità attraversate. Non implica installare SDK 9 o rilasciare una versione intermedia. |
| `migrate-dotnet9-to-dotnet10` | Installata dallo stesso plugin: WPF/WinForms, hosting, JSON, crittografia, interop e deployment pertinenti. Le due checklist accompagnano il retarget diretto 8→10 con SDK 10. |
| `test-gap-analysis` | Installata da `plugins/dotnet-test/skills/`: adatta all'analisi di comportamenti scoperti anche con harness console. Nessuna mutazione eseguita; non si dichiarano mutanti sopravvissuti empiricamente. |
| `csharp-expert` | Esaminata, non installata: routing generale duplicato dal coordinamento esplicito e dagli specialisti già scelti. |
| `csharp-refactoring` | Esaminata, non installata: il retarget è un upgrade, non un refactoring strutturale. Da rivalutare per l'estrazione headless in 2.0. |
| `run-tests` | Esaminata, non installata: le suite attuali sono eseguibili custom senza VSTest/MTP o adapter; usare i comandi reali del repository. |
| Diagnostica MSBuild | Non installata: i primi problemi erano permessi/cache e DNS NuGet del sandbox, non un guasto MSBuild del progetto. |

Le tre skill Microsoft sono in `.agents/skills/` e saranno scoperte dal turno successivo; in questo turno file e riferimenti sono stati letti esplicitamente. Le prescrizioni generiche delle skill su commit automatici e `dotnet test` cedono ai vincoli dell'utente e al runner verificato.
Creata `.agents/skills/updatecenter-wpf-visual-qa/SKILL.md`: Fluent nativo, screenshot comparabili, IT/EN, focus, contrasto, DPI e stati operativi. Il validatore `quick_validate.py` la accetta.

## 3. Subagenti configurati e realmente usati

Creati quattro file standalone TOML sotto `.codex/agents/`, secondo la [documentazione OpenAI sui subagenti](https://learn.chatgpt.com/docs/agent-configuration/subagents). Tutti i quattro TOML sono stati analizzati con `tomllib` di Python 3.11; ereditano modello e permessi, senza simulare un sandbox separato.

| Profilo persistente | Incarico effettivo in questa sessione | Risultato principale |
|---|---|---|
| `architecture-reviewer.toml` | `architecture_audit`, explorer integrato | Cache hardware obsoleta, cancellazione assorbita, prerequisiti SDK/CI; nessun impedimento architetturale intrinseco a .NET 10. |
| `security-update-reviewer.toml` | `security_audit`, explorer integrato | Firmware WU, bootstrap TLS LAN, versione WinGet, approvazione locale, verifica INF e ACL. |
| `quality-testing-reviewer.toml` | `quality_audit`, explorer integrato | Cancellazione directory sorgenti, firma stabile permissiva, suite CI mancanti, isolamento e payload installer. |
| `wpf-ux-reviewer.toml` | `wpf_audit`, explorer integrato | Localizzazione dei template, focus, nomi accessibili, contrasto e dimensioni rigide; proposta Fluent separata. |

I primi tre audit sono partiti in parallelo; il quarto appena liberato uno dei tre slot disponibili oltre al coordinatore. Tutti in sola lettura, senza build o avvio app, e consolidati prima di modificare codice applicativo.
La migrazione è stata eseguita dal worker dedicato `net10_implementation`, selezionato effettivamente come `gpt-6-astra` con effort `high`. `migration_review`, explorer indipendente, ha approvato il diff statico senza nuovi difetti rilevati; `audit_report` ha consolidato il documento e il revisore qualità ha diagnosticato separatamente il fallimento JSON. I nuovi profili persistenti non sono presentati come già caricati nel runtime corrente. Per la scoperta automatica aprire in Codex la radice Git `Source/UpdateCenter`.
`AGENTS.md` raccoglie comandi reali, limiti di sicurezza, proprietà esclusiva dei file e revisione indipendente.

## 4. Rilievi consolidati

Nessun P0 confermato. **Confermato** significa percorso dimostrato dal sorgente; non significa installazione distruttiva o attacco eseguito. **Probabile** richiede verifica dell'ambiente distribuito. Le righe identificano lo snapshot iniziale.

| ID / priorità | Evidenza, impatto e riproduzione sicura | Intervento proporzionato |
|---|---|---|
| S1 · P1 confermato | `Services/WindowsUpdateService.cs`, `SearchSource:114,139–163` legge `DriverClass` ma accetta tutti i risultati Driver, anche selezionandoli; `InstallDriver:270–320` non esclude firmware. Un caso sintetico `DriverClass=Firmware` attraversa i rami ammessi, contro `README.md:56`. Microsoft distribuisce [firmware come pacchetti driver WU](https://learn.microsoft.com/en-us/windows-hardware/drivers/bringup/firmware-update). | Classificazione condivisa ed esclusione firmware sia in scansione sia al confine privilegiato; fixture BIOS/firmware, nessuna installazione reale. |
| S2 · P1 confermato | `RemoteClient/RemoteAgentClient.cs`, `PairAsync:25–29,43–45` e `RequestConnectionAsync:68–72,85–88`: certificato TLS inizialmente accettato e confrontato soltanto con un fingerprint dichiarato dallo stesso endpoint. Un intermediario LAN può sostituire l'identità Controller durante il bootstrap; il pinning successivo non autentica il primo contatto. | Fingerprint/QR verificato fuori dalla discovery o protocollo che leghi codice e chiavi. Test in rete isolata; non basta firmare una discovery con una chiave ancora sconosciuta. |
| S3 · P1 confermato | `Services/WinGetManifestSafetyService.cs`, `ApplyAsync:43` valuta `AvailableVersion`; `Services/WinGetService.cs`, `Install:111–118`, `BuildInteractiveArguments:192–202`, `RunUpgrade:301–319` omettono `--version`. Se la fonte avanza tra scansione ed esecuzione si installa una versione non ispezionata. | Fissare la versione selezionata, oppure rivalutare manifest e riconfermare rischi variati prima dell'esecuzione. |
| Q1 · P1 confermato | `build-network-preview.ps1:11–19` autorizza qualsiasi discendente del progetto, poi lo cancella ricorsivamente. La directory sorgente `Agent` soddisfa la guardia e verrebbe rimossa prima del publish alla riga 22. Scenario analizzato, **mai eseguito**. | Output ammessi sotto una radice dedicata; rifiutare sorgenti, metadata Git e reparse point prima di qualsiasi cancellazione. |
| Q2 · P1 confermato | `docs/CODE-SIGNING.md:3` richiede Authenticode stabile; `sign-windows-artifact.ps1:12–17` termina con successo senza segreti. `.github/workflows/release.yml:83–104` non impone una firma valida al rilascio. | Firma obbligatoria per release reali, modalità unsigned esplicita per PR/dev; verifica firma prima degli hash. Non servono segreti nella working tree. |
| A1 · P1 confermato | `ViewModels/MainViewModel.cs`, `GetOrStartQuickHardwareData:752` conserva il primo Task; `ScanAsync:294,334–335,428` riutilizza hardware/storage iniziali. Il refresh post-install a `:900` aggiorna un campo che la scansione successiva non legge. Una seconda scansione dopo cambio driver/dischi può riproporre dati vecchi. | Cache soltanto per avvio; snapshot fresco per scansione esplicita e invalidazione dopo installazione/riparazione. |
| A2 · P2 confermato | `MainViewModel.ScanAsync:324,338,357,379,419,430` assorbe `OperationCanceledException`; `:450–452` può salvare `LastScanUtc` e `_hasCurrentScan=true` dopo `CancelScan:472–476`. Anche `QuickHardwareDataService.CaptureAsync:48–56` assorbe cancellazione. | Propagare cancellazione richiesta, verificarla tra fasi e prima della finalizzazione; test d'interruzione con servizi simulati. |
| S4 · P2 confermato | `Agent/LocalPipeFactory.cs`, `CreateApprovalPipe:37–41` concede accesso a Builtin Users; `ConnectionApprovalWorker.HandleAsync:62–68` espone richieste e accetta decisioni senza controllo utente/sessione in `Decide:81–94`. Un altro utente locale può approvare un Controller pendente senza il dialogo previsto. Non è dimostrata esecuzione SYSTEM arbitraria. | Autorizzare la sessione prevista e legare la risposta all'istanza di approvazione; restringere i diritti pipe. |
| S5 · P2 confermato, latente | `Services/OfficialDriverPackageService.cs`, `DownloadVerified:214` mantiene aperto il writer `FileShare.None` quando `:234` richiama `VerifyHash`, che riapre a `:242`: sharing violation Windows. `Assets/driver-catalog.json:4` è attualmente vuoto, quindi blocca l'abilitazione futura del catalogo. | Chiudere il writer prima di riaprire, oppure calcolare SHA durante il download; fixture locale. |
| S6 · P2 probabile | `Agent/AgentDataPaths.cs:15` usa ProgramData; `AgentNetworkSettingsStore.SaveLocked:355–358` non imposta ACL private e `EnsureCertificateLocked:240` usa DPAPI LocalMachine. Su questo host ProgramData eredita lettura Users; la directory Agent distribuita non esisteva e non è stata verificata. | Creare/controllare ACL SYSTEM/Administrators prima dei segreti e ripararle nell'installer; verificare accesso da utente standard. |
| Q3 · P2 confermato | CI iniziale `release.yml:65–77` costruisce l'app e lancia soltanto smoke standard. `--release-audit` è separato (`Tests/UpdateCenter.SmokeTests/Program.cs:14–17`); NetworkCore, Agent, Helper e Console non erano coperti dal gate. | Restore/build solution e tre invocazioni esplicite degli harness; verifica finale della modifica CI separata dall'esecuzione GitHub. |
| Q4 · P2 confermato | Smoke `Program.cs:208` e logging richiamano `AppPaths.EnsureCreated`; `Services/AppPaths.cs:29–61` pulisce file reali vecchi sotto LocalApplicationData. I test leggono anche inventario driver reale (`Program.cs:1530–1538`). | Data root iniettabile e separazione fixture/integrazione hardware. Fino ad allora VM/profilo usa e getta o sandbox che nega scritture reali; non elevare i test. |
| Q5 · P2 confermato | `build-installer.ps1:27–28,56,64–67`, con `-NoAppBuild`, controlla esistenza del payload ma valida soltanto la versione del wrapper. Un EXE vecchio può essere confezionato con versione nuova; nessun mismatch corrente del payload dimostrato. | Verificare FileVersion/ProductVersion del payload prima di ISCC, poi manifest/hash degli artefatti. |
| U1 · P2 confermato statico | `MainWindow.xaml:301,376,392,893,971,1006,1568,1594,1599` contiene testi IT in template/formati; `LocalizationService.cs:876–882,902–945` visita albero logico/statico e salta testo associato. Nessun hook dei template in `MainWindow.xaml.cs:76,436`. | Testi via risorse/proprietà localizzate aggiornabili, mantenendo la cache efficiente. Verificare righe generate in EN. |
| U2 · P2 confermato statico | Focus rimosso senza sostituto in `App.xaml:212,290,550`; chrome a glifi senza nomi espliciti in `MainWindow.xaml:73–75,1966`, navigazione solo icone sotto 900 DIP (`MainWindow.xaml.cs:453,540–554`). | Focus visibile e nomi Automation localizzati, contestuali alle righe; test tastiera e UIA/Narrator. |
| U3 · P2 confermato sui colori | Bianco sull'accento scuro `#7B7FF7`: 3,38:1 (`App.xaml:97`, `ThemeService.cs:22`); warning `#E69028` su `#FFF2DD`: 2,27:1 (`App.xaml:34,60`); verde su bianco: 2,99:1. Nessuna gestione dedicata contrasto elevato in `ThemeService:11–68`. | Correggere coppie semantiche e colori di sistema, preservando testo/icone oltre al colore. |
| U4 · P2 rischio statico | Dialogo fisso 760×650 e footer nello stesso Grid cell (`UpdateConfirmationWindow.xaml:4–6,163–170`); card alte 134 e descrizioni max 30 (`MainWindow.xaml:826,837,858–860`), righe LAN 60 (`:1505,1725`). `TypographyService:15–18` non scala queste dimensioni. Clipping non provato con screenshot. | Auto/MinHeight, colonne separate per azioni e dialogo ridimensionabile entro work area; verifica DPI/testo grande. |

Opportunità P2, non impedimenti al primo retarget: estrarre i servizi headless dalla GUI (`SessionHelper/UpdateCenter.SessionHelper.csproj:17`; `SessionHelper/Program.cs:37,61`); introdurre injection mirata nei ViewModel (`MainViewModel.cs:12–21,56–74,627`); testare direttamente replay, revoca e rifiuto certificati di `Agent/SignedRequestVerifier.cs`; legare `release_tag` manuale al commit effettivamente costruito. Il self-update verifica HTTPS/GitHub e SHA (`Services/AppUpdateService.cs:147–179`), ma una firma del publisher atteso darebbe autenticazione indipendente della provenienza.

## 5. Migrazione: decisioni e modifiche locali

Il worker ha proprietà esclusiva dei 9 `.csproj`, `global.json`, bootstrap/build/CI e documentazione di sviluppo interessata. Il coordinatore mantiene configurazioni e rapporto; nessuna modifica concorrente agli stessi file.
Modifiche effettuate: tutti i 9 TFM sono `net10.0`/`net10.0-windows10.0.17763.0`; `global.json` seleziona SDK stabile 10 da 10.0.100 con `rollForward=latestFeature` e `allowPrerelease=false` (risolto 10.0.403). I riferimenti esistenti a `Microsoft.Extensions.Hosting.WindowsServices` e `System.Security.Cryptography.ProtectedData` sono allineati a 10.0.14, verificato su NuGet. Aggiornati `build.ps1`, `CREA-EXE.bat`, workflow, README e `docs/SVILUPPO.md`. CI ripristina la configurazione Release, costruisce tutta la solution ed esegue i tre modi degli harness. Aggiunto `/_output/` a `.gitignore` per cache, binari e prove locali. Versioni applicative, manifest DPI/minimo Windows, comportamento, C# e XAML sono invariati. Nessun redesign o refactoring applicativo incluso.

Compatibilità da tenere distinta:

- [Microsoft supporta .NET 10 su Windows 10 1809 LTSC/Enterprise e Windows 11](https://learn.microsoft.com/en-us/dotnet/core/install/windows). Il target tecnico 17763 non dimostra copertura di ogni edizione Home/Pro o macchina Windows 10 fuori supporto.
- WPF è supportato; il [Fluent nativo .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) amplia i controlli e corregge access key/HighContrast, ma non impone di sostituire subito i template custom.
- Esaminare i breaking change di entrambe le versioni attraversate: XAML DynamicResource/Grid, ambiguità WPF/WinForms, JSON e ricerca DLL in single-file. Non basta cambiare il TFM.
- I costruttori byte/PFX `X509Certificate2` in Agent/RemoteClient generano `SYSLIB0057` dalla versione 9. Usare eventualmente loader DER/PFX specifici, preservando scope delle chiavi; [obsolescenza documentata](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib0057), non errore runtime già provato.
- `BackgroundService.ExecuteAsync` cambia ordinamento di avvio in .NET 10. Il percorso esaminato ripristina operazioni prima del proprio listener; nessun difetto certo di startup individuato, ma avvio/arresto servizio resta da esercitare in VM.

## 6. Evidenze di build e test

Log locali in `_output/modernization-20261104/`, esclusi dalla consegna di sorgenti. `DOTNET_CLI_HOME` è stato portato in quella directory; telemetria CLI disattivata. I primi tentativi fallivano per home non scrivibile e DNS NuGet (`NU1301`); restore/build riusciti dopo l'escalation autorizzata, senza installare SDK.
Baseline ottenuta con SDK 10.0.403 compilando ancora i target .NET 8; non è una baseline con il compilatore SDK 8.

| Comando dalla radice | Esito baseline e prova |
|---|---|
| `dotnet build UpdateCenter.sln -c Release --nologo --no-incremental -m:1` | Exit 0, 0 avvisi, 0 errori; tutti i 9 progetti. `baseline-build.log`. |
| `dotnet run --project Tests/UpdateCenter.NetworkCoreTests -c Release --no-build --no-restore` | Exit 0, messaggio «Test Network Core superati». `baseline-network.log`. |
| `dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --release-audit` | Exit 0, `PASS release-audit`. `baseline-release-audit.log`. |
| `dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore` | Exit `-532462766`, eccezione a `Program.cs:200`: `invalidReads=1; final=100` nel controllo JSON concorrente. `baseline-smoke.log`. Fallimento preesistente alla migrazione; causa non ancora dimostrata. |

Sono harness console senza conteggi standard di casi o copertura percentuale. Un `dotnet test` senza casi scoperti non sostituirebbe queste prove.

### Validazione finale osservata

| Verifica | Risultato e prova |
|---|---|
| Stesso comando build, TFM .NET 10 | Exit 0, tutti i 9 progetti, 0 errori e 8 avvisi: `net10-build.log`. |
| Stesso comando NetworkCore | Exit 0, `net10-network.log`. |
| Stesso comando release audit | Exit 0, `PASS release-audit`, `net10-release-audit.log`. |
| Stesso comando smoke completo | Exit `-532462766`, stessa asserzione `Program.cs:200`, ora `invalidReads=2; final=100`: `net10-smoke.log`. La suite non è verde e i controlli successivi al fallimento non sono verificati. |
| Revisione indipendente | `migration_review`: nessun nuovo difetto statico. JSON/TOML, 9 XML progetto, sintassi PowerShell, riferimenti skill e `git diff --check` validi; YAML workflow analizzato. GitHub Actions non eseguita da questa sessione. |
| Portable | Publish exit 0; `portable/UpdateCenter.exe`, self-contained/single-file x64, ProductVersion 1.1.5, 198.607.774 byte. Avvio GUI reale riuscito. `publish-portable.log`, checksum e `artifacts.json`. |
| LAN | Tre publish exit 0, Agent/SessionHelper/Console/GUI e librerie presenti; runtimeconfig di tutti gli eseguibili indica net10.0 e framework inclusi 10.0.14. `publish-agent.log`, `publish-console.log`, `publish-lan-ui.log`. Nessun servizio installato/avviato, pairing o installazione remota esercitati. |
| Setup | ISCC non presente nel PATH o nei percorsi Inno 6/7 previsti: Setup non compilato né installato. `installer.iss` e script esaminati staticamente; non è stato impacchettato un vecchio `dist`. |

Gli 8 avvisi sono 7 `SYSLIB0057` nei loader certificati Agent/RemoteClient e un `WFO0003` relativo al manifest DPI misto WPF/WinForms. Non sono stati soppressi: la sostituzione dei loader richiede prove PFX/chiavi e rimuovere il manifest senza verifica potrebbe alterare il DPI WPF. Restano nella milestone 1.2.0.

Comandi di publish effettivamente usati (output sotto `_output/modernization-20261104`; nessuna vecchia cartella dist rimossa):

```powershell
dotnet publish UpdateCenter.csproj -c Release -r win-x64 --self-contained true --no-restore -m:1 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o _output/modernization-20261104/portable
dotnet publish Agent/UpdateCenter.Agent.csproj -c Release -r win-x64 --self-contained true --no-restore -m:1 -o _output/modernization-20261104/lan
dotnet publish NetworkConsole/UpdateCenter.NetworkConsole.csproj -c Release -r win-x64 --self-contained true --no-restore -m:1 -o _output/modernization-20261104/lan
dotnet publish UpdateCenter.csproj -c Release -r win-x64 --self-contained true --no-restore -m:1 -p:PublishSingleFile=false -p:PublishTrimmed=false -o _output/modernization-20261104/lan
```

Le pubblicazioni sono equivalenti ai passi degli script, che non sono stati eseguiti integralmente sui dist preesistenti. Il primo publish senza `-m:1` è terminato con exit 1 e log vuoto nel sandbox; la stessa pubblicazione con MSBuild a nodo singolo è riuscita. I binari sono build locali **non firmate**, non una release pronta alla distribuzione. SHA-256 Portable: `7B2699111DD7736BA720899F5D163760ABCCA6D03BD5651D85A6C1888E4B2229`.

Diagnosi mirata del fallimento smoke: fixture C# isolata, con le stesse opzioni di lettura e sostituzione atomica ma senza `AppPaths`, ha eseguito 6.000 letture/2.000 sostituzioni: 1 lettura nulla, 0 eccezioni JSON, 0 errori writer e tutti i risultati finali corretti. Ha catturato errori transitori file non trovato e una sharing violation all'ultimo dei tre tentativi. Questo supporta un problema di disponibilità temporanea, **non dimostra corruzione JSON**; l'eccezione originale è assorbita da `JsonStorage.Read` e non è stata osservata. Il ruolo di sandbox, antivirus e OneDrive non è isolato. Non è una prova di regressione .NET 10. Dettagli in `atomic-diagnostic.txt`. Da testare anche il ramo `ElevatedUpdateRunner.cs:911–918`, che può interpretare una lettura nulla dopo 75 secondi dall'avvio come assenza di stato e terminare il runner anche dopo letture precedenti riuscite.

Screenshot reali acquisiti con `computer-use` sul desktop Windows 11 build 26220: finestra 1240×790, stesso monitor, lingua IT, tema chiaro e impostazioni invariate. Confrontati Home, Aggiornamenti vuoti, Impostazioni e LAN senza dispositivi. Prima: build net8; dopo: Portable single-file net10. Nessuna differenza visiva rilevante osservata nei quattro stati; non è una verifica pixel-perfect. Il controllo automatico app mostra indisponibilità nel sandbox. Nessuna scansione/installazione o modifica impostazioni avviata.

| Schermata | Prima | Dopo |
|---|---|---|
| Home | [PNG](../_output/modernization-20261104/before-home.png) | [PNG](../_output/modernization-20261104/after-home.png) |
| Aggiornamenti | [PNG](../_output/modernization-20261104/before-updates.png) | [PNG](../_output/modernization-20261104/after-updates.png) |
| Impostazioni | [PNG](../_output/modernization-20261104/before-settings.png) | [PNG](../_output/modernization-20261104/after-settings.png) |
| LAN | [PNG](../_output/modernization-20261104/before-lan.png) | [PNG](../_output/modernization-20261104/after-lan.png) |

Non esercitati: Windows 10 reale/VM, matrice DPI/EN/scuro/contrasto, schermate popolate e aggiornamento in corso, UAC, autoaggiornamento completo Setup/Portable, servizio LAN e installazioni. Il retarget e gli artefatti locali sono verificati nei limiti indicati; la release 1.2.0 resta subordinata a questi gate e ai P1 aperti.

## 7. Proposta WPF e procedura di accettazione

Mantenere WPF. Prima del rollout visivo provare Fluent nativo su un dialogo non distruttivo o sui controlli Aspetto delle Impostazioni, limitando gli stili impliciti che maschererebbero il tema. Conservare ViewModel, handler e tema attuale come fallback.

| Schermata | Primo intervento proposto dopo la parità funzionale |
|---|---|
| Dashboard | Conservare scan/cancel/progresso e ultimo controllo; rendere chiari vuoto, in corso ed errore. Il layout responsive esiste già (`MainWindow.xaml.cs:450–545`). |
| Aggiornamenti e runtime | Localizzare le righe generate; focus e nomi per selezione, fonte e retry; mantenere conferme e stato riavvio. Runtime è un filtro, non una pagina separata. |
| Driver e hardware | Distinguere inventario, diagnosi e azione verificata; rendere flessibili le card e i valori lunghi, senza perdere copia e supporto ufficiale. |
| Cronologia | Apertura/chiusura/copia dei dettagli da tastiera; popup entro area utile e stato vuoto leggibile. |
| Impostazioni | Pilot Fluent per Aspetto, focus sulle scelte e associazione delle etichette; IT/EN e testo grande preservati. |
| LAN | Nomi accessibili per riga, testi EN nei template e stati offline/non autorizzato/in attesa; nessuna modifica al consenso per comodità visiva. |

Confrontare screenshot reali dello stesso stato/dati, dimensioni, lingua e tema: 1366×768 e 1920×1080, finestra iniziale e minima 720×500, DPI 100/150/200%, tre dimensioni testo, IT/EN, chiaro/scuro/contrasto Windows. Includere vuoto, dati lunghi, errore, scansione, pausa e aggiornamento simulato; conferme, dettagli cronologia e autorizzazione LAN con fixture. Tastiera, UIA/Narrator e cambio monitor richiedono prove separate dalle immagini. Non cambiare impostazioni globali del PC per completare la matrice.

## 8. Roadmap e decisioni

1. **1.2.0 · fondazione:** completare retarget e publish coordinato dei componenti; gate solution + tutte le modalità harness; riprodurre/isolare il fallimento JSON baseline. Accettazione: nessuna nuova regressione, avvisi espliciti e artefatti coerenti.
2. **1.2.0 · confini di sicurezza:** S1 firmware, S2 bootstrap LAN, S3 versione WinGet; Q1 output non distruttivi e Q2 firma stabile obbligatoria. Accettazione con fixture e rete isolata, prima di una release pubblica.
3. **1.2.0 · affidabilità:** A1 snapshot freschi, A2 cancellazione, Q4 isolamento dati/test hardware, Q5 payload installer; S4 approvazione locale e verifica ACL S6. Test d'interruzione e riavvio in VM.
4. **1.2.0 · accessibilità mirata:** U1–U4, senza redesign generale; pilot Fluent soltanto dopo screenshot di parità e test tastiera/IT-EN.
5. **2.0 · architettura:** libreria Windows headless condivisa, injection mirata, separazione UI/orchestrazione e contratti compatibili; nessuna riscrittura completa.
6. **2.0 · LAN e interfaccia:** integrazioni TLS/replay/revoca/riavvio e matrice OS in VM; rollout Fluent progressivo per superficie, con fallback e prove visive confrontabili.

Richiedono decisioni successive: infrastruttura/identità di firma stabile; scelta del bootstrap di fiducia LAN; disponibilità di VM/profili per Windows 10/11, UAC, installazione e aggiornamento interrotto. Queste non impediscono le modifiche locali autorizzate, ma delimitano ciò che può essere dichiarato verificato o rilasciabile.

## 9. Hardening e ripresa dopo il riavvio del 4 novembre 2026

Questa sezione aggiorna il checkpoint precedente; i risultati delle sezioni 1–8 restano lo storico della migrazione.
Branch verificato: `modernization/net10-v1.2.0`; HEAD invariato `2e3ed78`, modifiche locali conservate, versione GUI 1.1.5. Dopo il riavvio nessun subagente o processo di compilazione precedente risultava attivo. La ripresa ha usato controlli e comandi sequenziali, senza nuovi subagenti paralleli, nuove installazioni o modifiche a Bitdefender. Nessun nuovo blocco antivirus o crash host osservato. Nessun commit, push, merge, tag o pubblicazione.

A fine verifica `Get-Process` non rileva processi dotnet/MSBuild/ISCC residui. `tasklist` non è utilizzabile nella sessione strumenti (errore «Nome utente o password non corretta»); non sono state cambiate credenziali o protezioni per risolverlo.

### Lavoro recuperato

Sono state riutilizzate le consegne dei revisori Security e Quality, il coordinamento/review mirato `sol_hardening_coordinator` (GPT-6.1 Sol Ultra) e le implementazioni GPT-6 Astra `astra_build_hardening`, `astra_update_safety`, `astra_quality_hardening`, `astra_lan_hardening`. Nessun nuovo audit generale. I log red/green sono conservati in `_output/hardening-20261104`.

| Problema | Correzione effettiva / file principali |
|---|---|
| Q1, output preview | `scripts/BuildPaths.ps1`, `build-network-preview.ps1`: nessuna cancellazione ricorsiva; nuova destinazione dedicata, rifiuto di riuso, traversal e redirezioni. Anche `build.ps1` e `build-installer.ps1` usano output nuovi. |
| S1, firmware | `Services/WindowsUpdateService.cs`: stessa classificazione conservativa nei risultati e sul candidato COM fresco prima di EULA/download/installazione. Firmware/BIOS/UEFI, classi sconosciute o metadati incompleti sono esclusi. Alcuni driver legittimi non riconosciuti possono quindi non comparire. |
| S3, WinGet | `Services/WinGetService.cs`: target esplicito `--version` in installazione, aggiornamento, interattivo e fallback; target non verificabili respinti prima del dispatch. `--version` identifica il formato della CLI, senza attestare il publisher dell'eseguibile né ogni versione storica. |
| JSON e watchdog | `Services/JsonStorage.cs`, `Services/ElevatedUpdateRunner.cs`: finestra di lettura transitoria limitata a 750 ms; una lettura mancante non azzera l'ultimo heartbeat reale. Nessuna cache globale di risultati obsoleti. |
| A1/A2 | `ViewModels/MainViewModel.cs`, `ViewModels/ScanServices.cs`, `Services/QuickHardwareDataService.cs`: snapshot nuovo per scansione, anteprima tardiva non sovrascrive risultati successivi, cancellazione propagata e nessuna finalizzazione della scansione annullata. |
| Isolamento test | `Services/AppPaths.cs`, harness Smoke: root GUID inizializzata prima di ogni I/O; root di produzione verificata separatamente senza toccarne i dati. L'asserzione JSON originale non è stata indebolita. |
| S2, TLS iniziale | `RemoteClient/RemoteAgentClient.cs`, dialogo `AgentFingerprintWindow`, call site UI/CLI: impronta SHA-256 richiesta fuori banda, per dispositivo, prima di inviare codice o certificato Controller. Nessuna impronta discovery usata come fonte di fiducia. |
| S4, approvazione locale | `Agent/ApprovalIdentity.cs`, `ConnectionApprovalWorker.cs`, `ConnectionRequestManager.cs`, `InteractiveSessionLauncher.cs`: SID/sessione ottenuti dal kernel e richiesta assegnata alla sessione interattiva; elenco/decisione negati ad altri utenti/sessioni. Il canale amministratore resta privilegiato. |
| Diritti pipe | `Agent/LocalPipeFactory.cs`, `Core/AgentLocalClient.cs`, `SessionHelper/Program.cs`: i client ordinari non ricevono `CreateNewInstance`; accesso specifico ReadWrite e identificazione del client. Il processo server conserva i propri diritti di creazione. |
| S6, dati Agent | `Agent/AgentSecretDirectory.cs`, `AgentNetworkSettingsStore.cs`: DACL privata prima di caricare chiavi, controllo del proprietario di directory/JSON/tmp, protezione anche del padre applicativo `%ProgramData%\UpdateCenter` per lo store del servizio, rifiuto di reparse point, nessun reset silenzioso di configurazione illeggibile. Proprietari non fidati causano un arresto controllato, senza acquisizione forzata della proprietà. Un'eventuale esposizione storica delle chiavi non viene annullata da questa correzione. |
| Firme LAN | `Agent/SignedRequestVerifier.cs`: timestamp fuori intervallo rifiutati senza eccezione; test reali del verificatore per chiave errata, corpo/percorso alterati, replay, scadenza e revoca. |
| Q2, autenticità release | `sign-windows-artifact.ps1`, `scripts/Assert-ArtifactSignature.ps1`, workflow e `Services/ReleasePublisherVerifier.cs`: release senza identità attesa/firma valida/timestamp bloccata; sviluppo unsigned esplicito. Il self-update verifica il publisher rispetto all'app firmata corrente anche prima della sostituzione Portable. |
| Q5, Setup | `build-installer.ps1`, `installer.iss`: source/output espliciti e versione del payload verificata prima di ISCC. |

Gli otto warning comparsi nella migrazione sono classificati: sette `SYSLIB0057` corretti con loader DER/PFX specifici in Agent/RemoteClient; `WFO0003` è la diagnostica WinForms del progetto WPF con NotifyIcon, soppressa puntualmente al posto del vecchio WFAC010 mantenendo il manifest DPI. Le build dopo la ripresa riportano **0 errori, 0 warning**.

### Verifiche dopo il riavvio

| Prova | Esito / log |
|---|---|
| Solution Release, `--no-restore --no-incremental -m:1 -p:UseSharedCompilation=false` | PASS, 0 errori/0 warning, ultima build `resume-build-closed.log`. |
| Timestamp fuori intervallo, prima della correzione | Fallimento controllato (exit 1, nessun crash host), `resume-authorization-red.log`. |
| `SmokeTests.exe --lan-hardening` | PASS: pin prima dei due POST, pin invalidi senza identità/rete, PFX riaperto, firme/replay/revoca, SID/sessione, ACL e pipe nativo; `resume-lan-green.log`. |
| Proprietario non fidato dei segreti | RED riprodotto con descriptor isolato (`resume-owner-red.log`), poi GREEN nello smoke finale: SYSTEM/Administrators ammessi, owner estraneo rifiutato. Nessun cambio di proprietà o permessi ai dati reali. |
| `SmokeTests.exe --winget-hardening` | PASS, nessun vero processo WinGet avviato; `resume-winget-green.log`. |
| Network Core Tests | PASS, `resume-network-core.log`. |
| `SmokeTests.exe --release-audit` | PASS, `resume-release-audit.log`. |
| `Tests/BuildOutputPaths.Tests.ps1` | PASS; guardie percorsi, destinazione nuova, conservazione sorgenti/output, distinzione tag Cloud; `resume-build-paths.log`. La vecchia fixture PowerShell non presente dopo il blocco non è stata rieseguita: il workflow ora usa questa regressione semplice, senza mock dei comandi di firma o creazione junction. Restano conservate le prove precedenti di packaging/firma. |
| `build.ps1 -NoRestore -OutputDirectory .\_output\hardening-20261104\resume-portable` | PASS, Portable self-contained; `resume-portable.log`. |
| `build-installer.ps1 -NoAppBuild -AppSourceDirectory .\_output\hardening-20261104\resume-portable -OutputDirectory .\_output\hardening-20261104\resume-setup -InnoCompilerPath .\_output\hardening-20261104\tools\inno-7.1.1\ISCC.exe` | PASS, Setup 1.1.5 compilato, **non installato**; `resume-setup.log`. Inno 7.1.1 portable aveva firma Windows Valid, Pyrsys B.V. |
| `build-network-preview.ps1 -OutputDirectory .\_output\network-preview\resume-closed-20261104` | PASS, include l'ultima protezione ACL/owner Agent e le istruzioni di verifica dell'impronta; nessun servizio installato/avviato; `resume-preview-closed.log`. |
| Smoke completo finale | PASS dopo l'ultima correzione Agent: quality, TLS, autorizzazione LAN/owner/ACL/pipe, autenticità release, Restart Manager e harness originale; `resume-smoke-closed.log`. Anche la prima esecuzione dopo il riavvio era PASS (`resume-smoke-final.log`). |
| Diff e versioni | `git diff --check` PASS (soli avvisi di conversione LF/CRLF); Portable ProductVersion 1.1.5 / FileVersion 1.1.5.0. |

Il primo publish è stato respinto dalla guardia filesystem: Desktop OneDrive è un placeholder `IO_REPARSE_TAG_CLOUD_6` (`0x9000601A`), non un collegamento redirezionale. La guardia ora verifica il tag con lo strumento Windows di sola lettura e ammette soltanto i tag Cloud documentati; junction, symlink e tag sconosciuti restano respinti. Nessuna pulizia ricorsiva reintrodotta. [Tag Microsoft](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-point-tags).

Il problema JSON è corretto e la verifica concorrente originale è conservata: 20 ripetizioni del codice reale, 6.000 letture/2.000 sostituzioni, nessuna lettura invalida e indice finale 100 (`quality-concurrent-20.log`). La causa osservata era indisponibilità transitoria durante sostituzione del file, non corruzione dimostrata né regressione attribuita a .NET 10.

Il controllo owner completa la DACL: in Windows il proprietario può modificarne i permessi, quindi restringere soltanto gli ACE non elimina il controllo di un proprietario preesistente non fidato. Lo store del servizio ammette SYSTEM/Administrators; lo store console ammette anche l'utente corrente. La migrazione di un percorso reale rifiutato richiede riparazione amministrativa da verificare in VM, senza cancellare chiavi o configurazione. [Proprietà e controllo accessi Windows](https://learn.microsoft.com/windows/security/identity-protection/access-control/access-control).

Artefatti locali conservati: Portable in `_output/hardening-20261104/resume-portable`, Setup in `_output/hardening-20261104/resume-setup/UpdateCenter-Setup-v1.1.5.exe`, preview in `_output/network-preview/resume-closed-20261104`. SHA-256 Setup: `F4B69C0276C1A69AA799F5F1AA87C54205CF45FE3B0699D3C296A14F0C98ACE1`. L'ultima modifica è confinata all'Agent e alle istruzioni preview; il payload GUI Portable/Setup verificato non ne dipende.

### Gate di release

`READY_FOR_1.2.0_RELEASE = false`. I P1 individuati hanno correzioni locali e prove mirate; non è ancora autorizzata né preparata una release di produzione firmata. Servono l'identità reale del publisher e la configurazione di `WINDOWS_SIGNING_CERT_SHA256`/segreti, più validazione su VM dei flussi servizio/UAC, upgrade Setup/Portable e interruzione/riavvio Windows 10/11. Le build locali restano unsigned. Le installazioni precedenti unsigned o con diverso certificato richiedono la transizione manuale documentata in `docs/CODE-SIGNING.md`.

I test non provano la sicurezza contro malware nella stessa sessione autorizzata, non riautenticano retroattivamente associazioni LAN pregresse e non dimostrano segretezza storica delle vecchie chiavi. I nonce restano in memoria: il comportamento attraverso riavvio servizio richiede la validazione isolata già prevista. La directory dei segreti Agent mantiene il rifiuto di tutti i reparse point: la modalità console con `AgentData` sotto Desktop OneDrive può quindi essere rifiutata; l'eccezione Cloud riguarda soltanto gli output di build. Il percorso del servizio in ProgramData e l'eventuale migrazione di owner non fidati restano da provare in VM. Le opportunità UI e il catalogo INF latente restano fuori da questa ripresa mirata.
