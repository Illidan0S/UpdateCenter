# M8-B — dialogo Windows reale, effetti simulati

9 novembre 2026. Branch `feature/engine-m6-consent-integrity`, checkpoint M7
`85a331d08a9ca1830593cae60c5b34b155f5989d`. Rapporto delle prove M8-B,
consolidato nel checkpoint Git locale successivo su autorizzazione dell'utente.

La strategia scelta usa soltanto il Windows host. Nessuna virtualizzazione richiesta,
proposta o configurata. L'assenza di un ambiente virtualizzato non è un difetto.
Il [rapporto M8-A](M8-RELEASE-READINESS.md) conserva le evidenze precedenti e aggiorna
matrice/procedura a questa decisione.

## Prova personale: istruzioni brevi

1. Aprire normalmente, **senza amministratore**:
   `C:\Users\USER\OneDrive\Desktop\UpdateCenter\Source\UpdateCenter\_output\m8-b\test-host\UpdateCenter.ConsentTestHost.exe`.
   Tenere l'intera cartella: l'EXE da solo non contiene il runtime. Non avviare
   `UpdateCenter.exe` per queste prove.
2. Scegliere Italiano/English e uno scenario, poi **Apri dialogo reale**.
   La finestra principale dice «TEST SICURO»; il dialogo è quello reale del prodotto.
3. Provare separatamente **Annulla**, **Escape**, **X**. Il riepilogo deve riportare
   `Cancelled` e zero chiamate simulate per il tentativo rifiutato.
4. Per approvare: leggere il piano, spuntare il rischio e premere **Autorizza questo
   batch**. Senza spunta il pulsante è disabilitato. Nel test host approvare registra
   soltanto parametri: non avvia installer o altri programmi.
5. Provare gli scenari sotto. Il risultato mostra cartella e `evidence.json`:
   confrontare `Attempts[].Snapshot` con `ProviderCalls[].Item`, e le modalità
   `Silent`/`Interactive`. Un mismatch provoca un errore del caso, non un PASS.
6. Chiudere normalmente l'host al termine. I dati restano nella cartella GUID indicata
   in `%TEMP%\UpdateCenter.ConsentTestHost`; impostazioni e cronologia del prodotto
   non vengono lette o modificate.

| Scenario | Risultato da verificare |
|---|---|
| Software + runtime | Due target sintetici: `Test.Software` Upgrade 1.0→2.0 e `Test.Runtime` Install 2.0, origine `test-only`; due chiamate simulate dopo assenso. Si può disattivare silent mode. |
| Driver + ripristino | Identità WUA, revisione 3, servizio e avviso driver visibili. Assenso → richiesta ripristino registrata, poi driver simulato. `RestorePointCreated=false`: nessun ripristino effettivo. Il privilegio amministratore è **simulato**, non UAC. |
| Snapshot modificato | Il file viene cambiato a 9.0 dopo la lettura. Dialogo e provider devono restare su 2.0; `MutatedInputAfterRead=true`. |
| Retry | Tre conferme distinte: tentativo iniziale, preparazione recupero, nuovo batch. Il primo provider restituisce FilesInUse simulato. Annullando il terzo dialogo restano soltanto la prima chiamata software e il recupero simulato. |
| Fallback interattivo | Nuovo consenso con `Interactive=true`, `Silent=false`, anche se silent è selezionato nella finestra di test. |
| Solo recupero | Conferma con testo RECOVERY; nessun processo o servizio viene toccato. |
| Annulla token dopo 5 secondi | Lasciare il dialogo aperto senza approvare: chiusura automatica, `Cancelled`, zero chiamate. È cancellazione **in-process**. |

Non usare il caso con timer per misurare un'attesa lunga. Il normale caso Software
può restare aperto; il runner aggiorna `LastHeartbeatUtc` ogni cinque secondi.

## Isolamento tecnico e confine della prova

Il nuovo progetto autonomo `Tests/UpdateCenter.ConsentTestHost` usa WPF e il framework
già presente, senza nuove librerie o framework di test. Compila tramite link i sorgenti
**invariati** `WpfRunnerConsent`, `RunnerPlanSnapshot`, `RunnerExecutionBoundary` e
`ElevatedUpdateRunner`, oltre ai modelli e ai servizi locali di lingua/persistenza.
Non referenzia né copia l'assembly produttivo e non avvia `App.OnStartup`.

- `NativeBlocks.cs` sostituisce le implementazioni WinGet, WUA, pacchetti driver,
  ripristino e recupero processi/servizi con ingressi che lanciano un errore.
  L'alias `Process` vale anche nei sorgenti collegati: consente solo letture di
  sessione/liveness; avvio, UAC e terminazione sono bloccati.
- `SafetyGuard` controlla l'IL compilato prima della UI: rifiuta chiamate dirette a
  processi, rete, COM, registro, servizi, caricamento dinamico e P/Invoke non ammessi.
  Le sole importazioni native incluse sono le letture WTS del dialogo originale.
  Il controllo è una barriera contro regressioni del codice incluso, **non** una
  sandbox contro sostituzione del binario o iniezione nello stesso processo.
- Il manifest è `asInvoker`; l'avvio manuale rifiuta un token già amministrativo.
  Sono accettati solo avvio senza argomenti oppure `--self-test`: nessuna importazione
  di piani esterni, nessun comando produttivo, nessun flag di assenso automatico.
- La root GUID viene inizializzata prima dell'I/O. I soli effetti reali dell'host
  sono apertura delle finestre e scritture dei propri piani, impostazioni e log di test.
- `RecordingEffects` registra copie dei parametri e li confronta con lo snapshot
  approvato, verificando anche che il token non sia cancellato. Un ingresso nativo
  tentato e bloccato fa fallire il caso. `Success` negli esiti del runner indica
  esclusivamente un risultato simulato; `InstallerSucceeded=false`, `Verified=false`.

Il validatore WinGet del test ammette soltanto le fixture sintetiche. Non qualifica
fonti reali o metadata dei pacchetti. Il retry è orchestrato dall'host attraverso i
veri gate `Run`/`PrepareRecovery`; non prova il coordinatore produttivo o la sua policy
di recupero processi. La cancellazione qui non attraversa processi elevati.

## Verifiche eseguite

| Prova | Esito ed evidenza |
|---|---|
| Build Release dell'host | **PASS**, zero avvisi/errori; `_output/m8-b/host-build.log`. Restore esclusivamente da cache locale già disponibile e sorgente locale vuota: nessun download. |
| Barriera tecnica, fase RED | Fallimento atteso su `Process.Start` prima dell'implementazione del controllo; `_output/m8-b/safety-red.log`. Nessuna chiamata nativa eseguita. |
| Self-test dell'host | **10 gruppi PASS**; `_output/m8-b/host-tests.log`. IL/adapter, root GUID, rifiuti dei sette scenari, target/mode, mismatch, file cambiato, retry negato, driver/ripristino, fallback e cancellazione. Callback automatici usati **solo qui**, non come prova del dialogo. |
| Suite M6-B esistente | **24 PASS / 0 FAIL** con l'harness Release del checkpoint M7; `_output/m8-b/existing-runner-consent.log`. Copre i contratti offline non dimostrabili con la sola interazione del test host; non installazioni reali. |
| Separazione dalla produzione | **PASS**: 70 sorgenti di compilazione del progetto produttivo, zero file test; nessun diff nei sorgenti/progetti/manifest produttivi. Nessun `UpdateCenter.exe` o `UpdateCenter.dll` nella cartella del test host. |
| Revisione mirata | Nessun rilievo bloccante sui soli nuovi file: raggiungibilità effetti nativi, confronto snapshot, cancellazione e retry. Nessun nuovo audit generale. |

### Interazione reale tramite desktop, distinta dai self-test

Windows 11 Pro Insider Preview x64 build 26320.9876, token non elevato, dispatcher
STA reale. Le azioni sotto sono state eseguite con mouse/tastiera sulle finestre vere,
non invocando il callback di assenso. La prova personale dell'utente resta da fare.

| Caso | Esito osservato |
|---|---|
| Software / rischio / tastiera | **PASS**: autorizzazione inizialmente disabilitata; riconoscimento rischio, Tab e Invio → due chiamate simulate, target 2.0 e modalità corretti. |
| Escape | **PASS**: `Cancelled`, una conferma rifiutata, zero chiamate. |
| Focus iniziale | **PASS nella configurazione corrente**: Spazio subito dopo l'apertura attiva Annulla; `Cancelled`, zero chiamate. Non dedotto dalla sola lettura del codice o dal testo UI Automation. |
| Driver / ripristino | **PASS nel contesto simulato**: UpdateID, revisione, servizio, operazione e rischio visibili; ordine ripristino→driver registrato senza effetti reali. |
| Snapshot / inglese | **PASS**: mentre la finestra mostrava 2.0, lettura del file confermava 9.0 e zero chiamate; dopo assenso il provider ha ricevuto 2.0. |
| Retry / Annulla | **PASS**: tre dialoghi reali, ciascuno con checkbox inizialmente vuota; assenso a iniziale/recupero, rifiuto al terzo → nessuna seconda chiamata software. |
| Fallback | **PASS**: dialogo inglese indica interattivo Sì e silent No; una chiamata con gli stessi parametri. |
| Token cancellato in attesa | **PASS**: il vero dialogo si chiude dopo circa cinque secondi; `Cancelled`, zero chiamate. |
| Attesa prolungata e X | **PASS nel perimetro del runner**: dopo 12 min 43 s ancora `AwaitingConsent`, heartbeat aggiornato da 2,93 s, zero risultati e chiamate. Chiusura con X a 12 min 47 s → `Cancelled`, zero chiamate e zero ingressi nativi tentati. |

Root della sessione di interazione:
`%TEMP%\UpdateCenter.ConsentTestHost\c03c81d901d94d01a681434d3e2732ef`.
Root del caso di attesa:
`%TEMP%\UpdateCenter.ConsentTestHost\f096e599847d4cf5a42badec0d5bef15`.
Screenshot pertinenti in `_output/m8-b/ui` (piano driver IT, snapshot EN, retry rifiutato,
cancellazione del token, attesa prolungata e chiusura). Le evidenze JSON riportano tutte
le decisioni e i parametri; copie durevoli dei nove casi in `_output/m8-b/ui/cases`,
riepilogo in `cases-summary.json` e misura dell'attesa in `wait-observation.json`.
Durante l'attesa non era attivo il watchdog del coordinatore: questa prova dimostra
il dialogo e l'heartbeat del runner, non il coordinamento fra processi. La policy
del watchdog resta coperta dai test offline esistenti.

## Cosa resta non verificato

- **PENDING:** UAC reale/negato e token elevato; annullamento coordinatore–runner fra
  processi; sessione realmente disconnessa/non interattiva. Non eseguiti per mantenere
  i vincoli dell'host. I test offline relativi restano evidenza simulata.
- **PENDING:** matrice completa 1366×768, DPI 100/150/200%, contrasto e combinazioni
  lingua/accessibilità. IT/EN e tastiera sono stati osservati nella configurazione
  corrente, senza modificare lo schermo. Nessuna pretesa di certificare gli altri DPI.
- **BLOCKED nel presente incarico:** download/installazioni WinGet/WUA, ripristino
  reale, riavvio e verifica della versione dopo installazione. Richiedono autorizzazione
  e target esatti in un incarico separato. Zero offerte WUA non è prova d'installazione.
- **PENDING:** compatibilità con altri Windows stabili, esecuzione senza runtime
  preinstallato e ciclo Setup/Portable del futuro candidato. Nessun packaging finale.

M6-B resta **WINDOWS VALIDATION PENDING**, con la parte desktop sopra verificata.
I sette blocchi di ambito e le prove RED storiche rimangono invariati. Durante M8-B
nessun bypass aggiunto alla produzione, commit, cambio di versione o rebranding.

## Riproduzione tecnica e identità della build

Progetto autonomo, escluso dal glob di compilazione produttivo `Tests/**`.
Da repository con i pacchetti già presenti in cache:

```powershell
dotnet build Tests/UpdateCenter.ConsentTestHost/UpdateCenter.ConsentTestHost.csproj -c Release --no-restore -o _output/m8-b/test-host
dotnet _output/m8-b/test-host/UpdateCenter.ConsentTestHost.dll --self-test
dotnet _output/engine-m7/checkpoint/smoke/UpdateCenter.SmokeTests.dll --runner-consent
```

Il restore iniziale ha usato `--source _output/m8-b/local-feed -p:NuGetAudit=false`
con cache esistente `_output/modernization-20261104/dotnet-home/.nuget/packages`;
nessuna sorgente HTTP. CLI home isolata e generazione certificati/aggiornamento PATH
disattivati tramite variabili del solo processo di build.

SHA-256 della build storica usata nelle nove prove desktop:

```text
UpdateCenter.ConsentTestHost.exe  D3D273CD9BAE7ABC5FCC72A3B4315B654FA45C810CC91AB1430DD50B090349B9
UpdateCenter.ConsentTestHost.dll  16030FED029671E889CE59228D179520DEC7A8C56BCD670CE50E50895D52B032
```

Alla chiusura M8-B il passo proposto era la **prova personale dell'utente con questo
host**, senza installazioni. Non è richiesto predisporre altri ambienti per questi casi.

## Verifica del checkpoint locale successivo

Il 9 novembre 2026 sono state ricompilate in Release l'applicazione con l'harness
smoke e l'host separato, senza restore o download: zero errori e avvisi. Output nuovi
in `_output/checkpoint-20261109/app` e `_output/checkpoint-20261109/consent-host`.
Sono PASS i dieci gruppi self-test dell'host ricompilato e i controlli mirati
`--hardware-network-ui` e `--settings-ui`. Log nella stessa directory di checkpoint.
I 24 PASS M6-B e le nove prove desktop restano evidenze precedenti, non rieseguite
per questo checkpoint. La build originale e le relative evidenze restano conservate
in `_output/m8-b`; output, log e schermate sono esclusi dai commit.

Il prossimo incarico indicato dall'utente riguarda esclusivamente leggibilità della
colonna Stato e recupero sicuro degli aggiornamenti WinGet bloccati. Tale lavoro non
è stato iniziato. Le verifiche Windows mancanti e lo stato M6-B sopra restano aperti.
