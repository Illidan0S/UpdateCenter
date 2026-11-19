# UpdateCenter 2.0.0 — M6 Installation Plan Integrity & Consent Binding

## M6-B — decisione approvata e implementazione

8 novembre 2026 — **M6-B CODE COMPLETE — WINDOWS VALIDATION PENDING**. Implementazione, verifiche offline e revisione statica finale completate. Il contratto originale M6 rimane distinto e non viene dichiarato soddisfatto.

**Ripresa Windows, 8 novembre 2026:** Fase 1 arrestata per assenza di una VM accessibile. Hyper-V sull'host è attivo ma `Get-VM` restituisce zero VM. Nessuna prova di interazione Windows o integrazione con provider eseguita in questa ripresa; procedura manuale riportata sotto. Lo stato resta **WINDOWS VALIDATION PENDING**.

**Checkpoint Git verificato:** branch `feature/engine-m6-consent-integrity`, HEAD `b6c3f736cd55ccad20240e98b2b3ada8e5fc57a1` (`test(security): preserve M6 consent-integrity RED evidence`), genitore diretto `c0329b9da165d4dbbf38fa454fc20037385a63a8` (baseline applicativa M5). Working tree pulito alla ripresa; le modifiche M6-B sono successive, senza commit. I riferimenti a HEAD M5 nelle sezioni storiche descrivono la fase precedente al checkpoint RED.

**Contratto autorizzato dall'utente:** «Nessuna esecuzione tramite i percorsi controllati senza una nuova conferma fidata del piano effettivo». La conferma nella MainWindow è preliminare; il runner autorizza il proprio snapshot. Non si attesta la corrispondenza al consenso originale, non si consuma un'autorizzazione crittografica e non si impedisce la ripresentazione di un piano: ogni esecuzione deve ottenere una nuova conferma.

**Modello di minaccia:** il soggetto può modificare, sostituire e ricreare i JSON, introdurre flag arbitrari e invocare direttamente il runner. Si assumono fidati eseguibile, memoria e interazione di conferma; sono escluse iniezione nel processo e conferme sintetiche. Un dialogo allo stesso livello di integrità non è una barriera contro malware attivo con gli stessi privilegi. Anche stato, risultati e metadati di reporting sono modificabili dall'utente: non vengono mai accettati come autorizzazioni.

### Confine implementato

- `RunnerPlanSnapshot` legge il JSON una sola volta, controlla presenza dei campi obbligatori e duplicati anche con casing diverso, valida l'intero batch e ne conserva copie private. Valori e collezioni per visualizzazione, provider e recupero derivano da quello snapshot; le copie restituite non consentono di modificarlo. Nessuna rilettura del file dopo la conferma.
- La validazione respinge batch vuoti/duplicati, kind/modalità/operazioni incoerenti, ID/versione/origine WinGet non validi, identità/revisione/sorgente WUA non valide e metadati incompatibili con il provider. Il percorso INF riusa il validatore già esistente, compreso il catalogo incorporato. La versione WUA visualizzata rimane descrittiva: il target eseguibile è UpdateID/revisione/sorgente. Nessuna garanzia nuova sull'endpoint associato a un nome di origine WinGet.
- Il runner utente accetta software/runtime; quello amministratore accetta driver e conserva il controllo dei privilegi. Nessuna elevazione aggiunta al software, nessun cambio di account o scope. I provider M5 mantengono controlli, hash/firme e guard firmware; i due validatori esistenti sono soltanto resi accessibili internamente per il controllo preliminare.
- `WpfRunnerConsent` richiede sessione interattiva attiva e dispatcher disponibile. Il dialogo mostra intero batch, identità, versioni, origini, operazioni, provider, silent mode e richiesta di ripristino; per WUA espone identità e sorgente effettive e per INF anche URL/hash/firmatari/hardware ID. Ogni batch richiede riconoscimento esplicito dei rischi: nessun flag ricevuto può dichiarare affidabili metadati o installer. I nomi ricevuti sono presentati come descrizioni. Chiusura, Escape, rifiuto o impossibilità di confermare non autorizzano alcun effetto di installazione.
- Lo stato iniziale è `AwaitingConsent`, con heartbeat e senza timestamp di installazione. Il watchdog continua a rilevare la perdita di heartbeat, ma non applica timeout o avvisi di progresso installer all'attesa umana. Dopo l'assenso, la transizione di stato deve essere pubblicabile prima degli effetti. Ripristino, provider, EULA, download e installazione restano oltre il controllo del consenso. Prima della conferma vengono scritti soltanto metadati di coordinamento/stato e diagnostica, non effetti preliminari di installazione.

### Batch, retry, recuperi e reporting

| Ingresso | Comportamento M6-B |
|---|---|
| Batch software / runtime | Conferma unica sull'intero snapshot; WinGet conserva ID/versione/origine/operazione e silent mode. |
| Batch driver | Conferma unica nel runner amministratore, dopo il controllo dei privilegi e prima del ripristino o del provider. UAC resta separato dal consenso al piano. |
| CLI diretta e piano ricreato | Lo stesso ingresso `ElevatedUpdateRunner.Run` richiede sempre conferma; nessun flag CLI/file/ambiente può approvare. |
| Nuovo runner di retry | Nuova conferma del nuovo batch. Nessuna autorizzazione è salvata o riutilizzata. |
| Preparazione recupero | Nuova conferma sul target congelato prima di `PrepareRetry`; restano necessarie le conferme specifiche per chiusure/terminazioni/arresti temporanei di servizi. |
| Fallback interattivo | Il precedente consenso al recupero non basta: il percorso esistente `InstallSoftwareInteractive` passa dallo stesso controllo su uno snapshot con modalità interattiva esplicita. |
| Batch di retry cambiato | Richiede comunque nuova conferma; risultati nuovi o aggiunti sono conservati, senza continuare automaticamente il recupero come se il target fosse invariato. |
| Annullamento | Esiti `Cancelled` distinti da successo/fallimento installer, nessun recupero successivo dopo il rifiuto; risultati già ottenuti conservati. Il launcher ricontrolla il token immediatamente prima di `Process.Start`. Un annullamento successivo a effetti già avvenuti non li annulla retroattivamente. |

`ReceivedItems` nello stato e `ReportedTarget` nei risultati sono estensioni additive di reporting, non credenziali. Collegano cardinalità, eventuali recuperi e cronologia al piano effettivo, anche se diverso dal riepilogo preliminare, inclusi gli esiti infrastrutturali sintetici. Un risultato riferito a un target differente non modifica stato/installabilità della vecchia proposta; al termine, le selezioni preliminari senza un risultato corrispondente tornano disponibili con richiesta di nuova scansione. Il consumatore mantiene compatibilità con gli stati precedenti privi dei nuovi campi. Le scritture sul canale di stato restano prive di autenticazione.

### File modificati

| File | Motivo |
|---|---|
| `Services/RunnerPlanSnapshot.cs`, `Services/RunnerExecutionBoundary.cs` | Snapshot difensivo, validazione completa e dipendenze interne per conferma/effetti. |
| `Services/WpfRunnerConsent.cs` | Conferma esplicita in sessione interattiva, presentazione del piano e rischio conservativo. |
| `Services/ElevatedUpdateRunner.cs`, `Services/WinGetProcessRecoveryService.cs`, `Models/UpdatePlan.cs` | Controllo prima degli effetti, recuperi/fallback, cancellazione, watchdog ed esiti riferiti al target ricevuto. |
| `Services/WinGetService.cs`, `Services/OfficialDriverPackageService.cs` | Riutilizzo interno dei validatori esistenti; nessuna modifica all'esecuzione dei provider. |
| `UpdateConfirmationWindow.xaml`, `UpdateConfirmationWindow.xaml.cs`, `ViewModels/MainViewModel.cs` | Semantica preliminare della prima finestra; attesa, annullamento, proiezione UI e cronologia coerenti. |
| `Tests/UpdateCenter.SmokeTests/RunnerConsentTests.cs`, `Tests/UpdateCenter.SmokeTests/Program.cs` | Accettazione offline separata, dispatch `--runner-consent`. |
| `docs/ENGINE-M6-CONSENT-INTEGRITY.md` | Decisione, checkpoint corretto, separazione dei contratti, evidenze e limiti. |

### Evidenze offline M6-B

Artefatti locali esclusi da Git: `_output/engine-m6-b`.

| Evidenza | Risultato |
|---|---|
| RED iniziale con soli seam per consenso/provider, prima del controllo | `red-build.log`: build riuscita; `red-test.log`: 8 FAIL funzionali attesi. Nessun provider reale chiamato. |
| RED del recupero prima della sua conferma | `recovery-red-test.log`: 16 PASS, 1 FAIL (`RecoveryGate`). |
| RED del target riportato | `results-red-test.log`: 17 PASS, 1 FAIL (`EffectiveResults`). |
| Prima accettazione M6-B | `acceptance-test.log`: 20 gruppi PASS, zero FAIL, prima della revisione indipendente. |
| RED delle correzioni di revisione | `review-red-test.log`: 21 PASS, 3 FAIL su target infrastrutturale e proiezione UI. Il test del token al confine fallback passava già: il difetto riguardava il suo inoltro nel call site produttivo. |
| Accettazione finale M6-B | `final-test.log`: **24 gruppi PASS, zero FAIL**. |
| Regressioni M5 finali | `final-m5-regressions.log`: **16 gruppi PASS**, più regressioni WinGet esistenti. |
| Contratto originale M6 finale | `final-original-red-test.log`: **4 PASS / 15 FAIL intenzionali**, non trasformati in GREEN. Il file delle evidenze originali è invariato. |
| Build Release progetto smoke e dipendenze | `acceptance-build.log`: zero avvisi/errori. |
| Build Release finale dell'intera soluzione | `final-build.log`: zero avvisi/errori, senza restore/download. |
| Diff | `git diff --check` PASS; nessuna modifica alla suite RED originale o alla versione applicativa. |
| Revisione indipendente | Un solo revisore GPT-6 Astra Ultra in sola lettura: tre P2 iniziali, corretti e ricontrollati; nessun ulteriore rilievo azionabile. Riepilogo in `review.txt`. |

I nuovi test attraversano il vero `Run`, il vero ingresso del fallback e la preparazione del recupero, sostituendo conferma e soli effetti esterni con dipendenze interne. Sono esercitati: approvazione del batch, rifiuto/assenza/errore/cancellazione, alterazione/ricreazione/replay, mutazione del file durante la conferma, copie difensive, metadati di rischio manipolati, schema incompleto/nullo/duplicato, vincoli driver/privilegi, nuovo consenso al retry, stato di attesa, canale non pubblicabile, cancellazione al launcher, risultati effettivi e cardinalità. Le asserzioni nelle callback vengono conservate anche se il runner intercetta l'eccezione, evitando falsi PASS. Il test CLI copre l'ingresso condiviso chiamato da `App.OnStartup`, non avvia un eseguibile o UAC.

La suite storica `--consent-integrity` continua a descrivere il vecchio confine di ammissione del file: non attraversa la nuova conferma e non misura l'accettazione M6-B. Le sue 15 evidenze RED restano consultabili e inalterate.

I tre P2 chiusi riguardavano: `ReportedTarget` assente negli errori infrastrutturali sintetici; token del coordinatore non inoltrato al fallback; risultati di target differenti applicati alle vecchie proposte e selezioni non eseguite lasciate in attesa. Il ricontrollo indipendente è statico: i risultati dei comandi sono evidenze dell'esecuzione principale, non test eseguiti dal revisore.

Riproduzione, dalla radice del repository con dipendenze già disponibili e una home CLI isolata:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '_output/engine-m1/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_TieredCompilation = '0'
dotnet build UpdateCenter.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --runner-consent
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --install-safety
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --consent-integrity
# Quest'ultimo comando DEVE restare non-zero: evidenze del contratto originale.
git diff --check
```

### Limiti e validazione Windows pendente

- Nessuna installazione/download reale, COM/WUA reale, UAC, ripristino, chiusura di processi o arresto di servizi eseguiti. I seam interni non sono selezionabili in produzione tramite dati o argomenti.
- Da verificare in VM isolata: dialogo nei due runner e nel fallback, sessione assente/disconnessa, UAC negato, chiusura/Escape, focus e tastiera, IT/EN, contrasto elevato, 1366×768 e DPI 100/150/200%, heartbeat durante la conferma, pausa/annullamento attraverso il processo elevato, integrazione con i provider e con il riavvio. La nuova finestra è stata verificata staticamente; nessuno screenshot reale o test di interazione Windows viene rivendicato.
- Nessuna attestazione del consenso originale, nessuna anti-replay persistente, nessuna autenticazione nuova dei risultati. Sono invariati i limiti M5 dei cataloghi/provider e delle verifiche fresche non disponibili. Non si dichiara protezione contro manipolazione della memoria/UI fidate.
- Nessun broker, servizio permanente, protocollo crittografico, dipendenza, modifica dei privilegi/certificati/configurazioni, commit/push/tag/release o lavoro M7/M8/rebranding.

### Preparazione Windows — nuove evidenze dell'ambiente

Controlli in sola lettura dell'8 novembre 2026; riepilogo in `_output/engine-m6-b-windows/environment-check.txt`.

| Controllo | Nuova evidenza |
|---|---|
| Branch / HEAD | `feature/engine-m6-consent-integrity` / `b6c3f736cd55ccad20240e98b2b3ada8e5fc57a1`; implementazione M6-B ancora nel working tree, preservata. |
| Inventario nel sandbox degli strumenti | Cmdlet Hyper-V presenti, servizio non visibile e query CIM non accessibile. Questo esito non è stato interpretato come assenza di VM. |
| Inventario dal contesto host, senza elevazione UAC | Servizio `vmms` **Running**; `Get-VM`: **0 VM**. Non esiste quindi un guest Hyper-V sul quale verificare snapshot, desktop e account. |
| Altri gestori accessibili | Nessun processo VM/RDP corrispondente ai nomi controllati; `VBoxManage`, `vmrun`, `qemu-system-x86_64` non risolti. Eseguibili VirtualBox/VMware assenti nei tre percorsi standard verificati. Non è un inventario di ogni disco o di eventuali VM remote non collegate. |
| Ambiente utilizzabile | Nessuna VM Windows isolata accessibile e verificabile con snapshot, desktop interattivo e account standard/amministratore. Nessuna VM creata/avviata; nessuna configurazione dell'host modificata. |
| Test Windows / difetti | **Nessun test Windows eseguito; nessun difetto nuovo riprodotto o corretto.** Nessuna schermata dell'host viene presentata come evidenza di una VM. Build, suite offline e revisione precedenti non sono state ripetute. |

### Procedura manuale minima per la VM

La procedura seguente è preparata, **non eseguita**. Tutti i passaggi operativi valgono esclusivamente dentro una VM usa e getta già disponibile. La presenza del sistema Windows host non soddisfa questo prerequisito.

1. **Ambiente e copia esatta.** Registrare nome/ID della VM, build Windows x64, hypervisor, snapshot di partenza e modalità di ripristino, tipo di sessione, account/SID e token standard/elevato. Servono account standard e amministratore. Copiare il working tree M6-B completo, inclusi i nuovi file non tracciati: il solo checkout di HEAD non contiene l'implementazione. Copiare l'intera directory Release già costruita in `C:\M6B\app`, non soltanto l'apphost EXE; confrontare i SHA-256 almeno di `UpdateCenter.exe` e `UpdateCenter.dll` fra sorgente e VM. Registrare separatamente identità/versione dell'eventuale harness. Nessun download/installazione di strumenti fa parte di questa procedura.
2. **Due percorsi distinti.** Il binario di produzione può essere usato qui per verificare CLI, visualizzazione e uscite negative, scegliendo sempre **Annulla / Escape / X**, mai l'assenso. Per provare l'assenso senza effetti occorre un host manuale nel solo progetto di test: thread STA, vera `Application`/dispatcher WPF, vero `WpfRunnerConsent`, vero `ElevatedUpdateRunner.Run` con `IRunnerEffects` simulato. `IsAdministrator` deve leggere il token Windows effettivo nei casi di elevazione; le altre operazioni devono soltanto registrare parametri e contatori. Il consenso deve provenire dal dialogo reale e dall'operatore, senza callback che restituiscano automaticamente `true`. **Questo host manuale non è presente nel checkout:** `--runner-consent` simula anche la conferma. Va predisposto esclusivamente nei test quando la VM è disponibile; in questa ripresa non è stato implementato né inventato un comando per eseguirlo. I casi positivi restano non eseguibili finché manca tale prerequisito.
3. **Fixture riproducibile per la CLI negativa.** Nella VM e nell'account usa e getta, usare il blocco seguente. Ripeterlo con `$driverCase = $false` e `$true`, generando ogni volta un nuovo GUID. I valori sono sintetici, non pacchetti da installare. Nel caso driver viene mostrato UAC; il consenso UAC non autorizza l'installazione. Provare separatamente token amministratore limitato e account standard con credenziali amministrative: registrare eventuali differenze di profilo/root dati senza spostare i file per nascondere un errore del percorso reale.

```powershell
# SOLO NELLA VM. Questo percorso usa provider reali: terminare sempre con Annulla/Escape/X.
$driverCase = $false
$exe = 'C:\M6B\app\UpdateCenter.exe'
$dataRoot = Join-Path $env:LOCALAPPDATA 'UpdateCenter'
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$caseId = [Guid]::NewGuid().ToString('N')
$planPath = Join-Path $dataRoot "update-plan-$caseId.json"
$statusPath = Join-Path $dataRoot "update-status-$caseId.json"
$item = [ordered]@{
    Id='Contoso.Tool'; Name='M6-B fixture'; Kind='Software'; Source='winget'
    InstalledVersion='1.0'; AvailableVersion='2.0'; PackageOperation='Upgrade'
    DriverInstallMode=''; WindowsUpdateId=$null; WindowsUpdateRevision=0
    WindowsUpdateServerSelection=0; WindowsUpdateServiceId=''
    ExpectedSignerSubjects=@(); CompatibleHardwareIds=@()
}
if ($driverCase) {
    $item.Id = 'c42aaae1-cb2a-469d-8be6-ec88f26f485d'
    $item.WindowsUpdateId = $item.Id
    $item.Kind = 'Driver'; $item.Source = 'Windows Update'
    $item.DriverInstallMode = 'WindowsUpdate'; $item.WindowsUpdateRevision = 3
}
$plan = [ordered]@{
    CreateRestorePoint=$false; SilentSoftwareInstall=$true
    StatusFile=$statusPath; PauseFile=(Join-Path $dataRoot "update-pause-$caseId.signal")
    PauseOwnerProcessId=$PID; Items=@($item)
}
[IO.File]::WriteAllText($planPath, ($plan | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$runnerMode = if ($driverCase) { '--update-runner-admin' } else { '--update-runner-user' }
$launch = @{ FilePath=$exe; ArgumentList=@($runnerMode, ('"{0}"' -f $planPath))
    Wait=$true; PassThru=$true; WindowStyle='Normal' }
if ($driverCase) { $launch.Verb='RunAs' }
$runner = Start-Process @launch
$runner.ExitCode
if (Test-Path -LiteralPath $statusPath) { Get-Content -LiteralPath $statusPath }
```

4. **Matrice di interazione.** Per ogni caso conservare JSON originale, snapshot mostrato, decisione effettiva dell'operatore, PID/account, stato/heartbeat, esito e parametri del provider simulato. Usare una root dati GUID separata in ciascun processo del test host, inizializzata prima di qualsiasi I/O; mai un override della root nel binario di produzione. Ogni riga positiva richiede il test host del punto 2, non la CLI di produzione appena mostrata.

| Caso | Azione reale nella VM | Criterio osservabile |
|---|---|---|
| Software e driver | Aprire il dialogo e confrontare tutti gli elementi con la fixture; nel test host approvare un batch alla volta. Ripetere silent on/off e ripristino on/off, quest'ultimo soltanto simulato. | Contatore effetti zero prima dell'assenso; dopo, identico numero di item e identici ID/versione/origine/operazione/provider/parametri; per WUA anche UpdateID/revisione/ServerSelection/ServiceID. |
| Rischio e snapshot | Lasciare assenti o impostare a `false` i flag di rischio non attendibili; sostituire il file con versione `9.0` mentre il dialogo mostra `2.0`, quindi approvare nel test host. | Avvisi e riconoscimento del rischio ancora richiesti; il provider simulato riceve `2.0`. Nuova invocazione sul file cambiato mostra `9.0` e richiede nuovo assenso. |
| Uscite negative | In esecuzioni separate: Annulla, Escape, X, token cancellato prima del dialogo e durante l'attesa. | Nessun ripristino/EULA/download/installazione/recupero; contatori simulati zero, stato/esito coerente. Stato e diagnostica sono gli unici I/O consentiti. |
| CLI e replay | Invocare direttamente entrambi i runner; ripresentare una copia del piano dopo una precedente esecuzione simulata. | Nessun avvio automatico; nuova conferma ogni volta. La CLI reale va provata soltanto con uscite negative. |
| Retry e fallback | Nel test host simulare `FilesInUse`, attraversare la vera policy di recupero e il fallback con `WpfRunnerConsent`; cambiare target o modalità fra tentativi. | Nuova conferma per preparazione recupero, nuovo runner e fallback; nessun ampliamento senza nuovo assenso. Cancellare il token del coordinatore mentre il fallback attende. |
| Attesa e watchdog | Nel test host tenere aperto il dialogo per oltre 12 minuti, raccogliendo heartbeat ogni 5 secondi; nessun item avviato. | `AwaitingConsent`, heartbeat fresco, nessun avviso di progresso o timeout installer. Una simulazione delle date già provata offline non sostituisce questa attesa reale. |
| Cancellazione fra processi | Usare un coordinatore e un vero processo figlio del test host, entrambi con effetti simulati; cancellare durante `AwaitingConsent`, nei casi utente ed elevato. | Figlio non lasciato in attesa autorizzabile dopo la cancellazione; nessun effetto. Annotare eventuali accessi negati al processo elevato come esito da risolvere, non come PASS del token in-process. |
| UAC | Negare UAC; separatamente consentire la sola elevazione e rifiutare il dialogo del piano. Per assenso al piano utilizzare solo il test host con effetti simulati. | Negazione UAC senza installazione; finestra elevata reale, token rilevato correttamente e consenso al piano separato. |
| Sessione assente/disconnessa | Eseguire il test host in una sessione non interattiva/disconnessa reale; disconnettere anche una sessione già in attesa e poi riconnetterla. | Nessun assenso implicito o effetto durante l'assenza di interazione; impossibilità di confermare gestita in sicurezza. Non simulare soltanto il valore del controllo WTS. |
| Visuale essenziale | IT/EN × DPI 100/150/200%, risoluzione 1366×768, cambiando soltanto il guest; Tab/Shift+Tab/Spazio/Invio/Escape. | Elenco e azioni raggiungibili, nessun dato operativo troncato, focus iniziale su Annulla, checkbox inizialmente vuota, autorizzazione disabilitata finché manca il riconoscimento. Salvare screenshot dei casi pertinenti. |

5. **Provider reali, successivamente e senza installazioni.** Registrare versione/percorso di WinGet e sorgenti configurate, enumerare i pacchetti già installati e confrontare ID/versione/origine. Per WUA usare soltanto sessione/searcher e ricerca di offerte, registrando sorgente, UpdateID e revisione e mantenendo l'esclusione firmware. Non usare `InstallDriver` come scorciatoia di enumerazione: il metodo prosegue verso EULA/download/installazione. Non accettare EULA né chiamare downloader/installer. Se la lettura richiede aggiornamenti delle sorgenti o altri download, fermarsi e distinguere questo requisito dalla sola enumerazione. Qualunque installazione o download di payload richiede un piano separato con target esatto, sorgente, effetti, snapshot di ripristino, rischi e risultati attesi, seguito dall'autorizzazione esplicita.
6. **Raccolta e chiusura.** Per ogni riga indicare PASS/FAIL/NON ESEGUITO, ambiente, hash del binario, schermate, log e motivo degli eventuali limiti. Ripristinare lo snapshot della VM secondo il piano concordato. Dichiarare `M6-B VERIFIED` solo quando sono soddisfatti tutti i criteri pertinenti; nessun PASS offline viene riclassificato come interazione Windows.

**Conclusione di questa ripresa:** test Windows superati: **nessuno**; casi 1–10 e integrazione WinGet/WUA reale: **non eseguiti per mancanza di VM**. Host manuale con dialogo reale/effetti simulati ancora da predisporre nella fase di esecuzione. Nessun difetto riprodotto, nessuna modifica applicativa, nessun commit. Stato invariato: **WINDOWS VALIDATION PENDING**.

---

## Registro storico M6 RED — precedente alla scelta B

Le sezioni seguenti conservano analisi ed evidenze del contratto originale. Le indicazioni «bloccata», «nessuna implementazione» e «HEAD M5» si riferiscono a quella fase, non al codice M6-B descritto sopra.

8 novembre 2026 — **M6 BLOCCATA**. Il difetto di collegamento fra piano e consenso è confermato. Non è stata identificata una correzione minimale che soddisfi tutti i requisiti richiesti riutilizzando soltanto i meccanismi locali attuali. Nessun hash, token o flag JSON è stato aggiunto come presunta prova del consenso.

## Checkpoint, modifiche e decisione

- Checkpoint verificato: `feature/engine-m5-install-safety`, HEAD `c0329b9da165d4dbbf38fa454fc20037385a63a8`, working tree iniziale pulito.
- Creato il branch locale `feature/engine-m6-consent-integrity` sullo stesso HEAD.
- Aggiunti soltanto questo rapporto, `ConsentIntegrityEvidenceTests.cs` e il dispatch opt-in `--consent-integrity` nel progetto smoke. Nessun file applicativo, versione, schema JSON, provider o percorso di esecuzione modificato.
- Nessun nuovo commit: l'autorizzazione al commit era subordinata al superamento delle verifiche; i requisiti di accettazione M6 restano RED. Le modifiche sono lasciate nel working tree, senza staging.
- Applicate TDD e verification-before-completion: prima le prove del difetto; nessuna implementazione sostitutiva dopo l'esito progettuale negativo. Un solo revisore indipendente GPT-6 Astra Ultra ha valutato in sola lettura la proposta di modificare il confine, prima dell'implementazione, concordando sul blocco. Non si tratta di una certificazione di una correzione realizzata.

Il blocco deriva dai requisiti di fiducia, non da errori di compilazione, mancanza di permessi degli strumenti o necessità di avviare installazioni reali.

## Percorso mirato e garanzie esistenti

| Passaggio | Riscontro nel codice |
|---|---|
| Selezione e conferma | `MainWindow.RunUpdatesAsync` mostra `UpdateConfirmationWindow` dopo il preflight. Se `ShowDialog() != true`, ritorna senza avviare l'operazione. La conferma del rischio è già gestita dalla finestra. |
| Passaggio alla VM | `MainViewModel.InstallItemsAsync` riceve la lista degli oggetti, impedisce esecuzioni concorrenti e passa lista/impostazioni al coordinatore. Nessun oggetto di autorizzazione accompagna questa chiamata. |
| Generazione piano | `UpdateCoordinator.RunBatchAsync`, in `Services/ElevatedUpdateRunner.cs`, ricostruisce `PlanItem` tramite `ToPlanItem` e legge le impostazioni al momento del batch. Il piano contiene anche modalità silenziosa, richiesta di ripristino e canali di stato/pausa. |
| Persistenza | `JsonStorage.WriteAtomic` protegge dalla pubblicazione di un JSON parziale e gestisce errori transitori. Il file resta nella root dati dell'utente. Un nome GUID evita collisioni, ma non attesta un'approvazione. |
| Avvio | Il coordinatore passa al processo il tipo di runner e il percorso del piano. Per i driver usa `runas`. `App.OnStartup` accetta gli stessi argomenti anche da un'invocazione diretta. |
| Ingresso runner | `ElevatedUpdateRunner.Run` valida il percorso, deserializza il JSON e valida i percorsi di stato e pausa. Non confronta il contenuto con una selezione approvata e non dispone di un registro di autorizzazioni consumate o revocate. |
| Prima degli effetti | Sono preservati controllo amministratore e controlli dei provider. La richiesta di ripristino viene letta dal piano prima del ciclo degli elementi; i controlli WinGet/WUA non dimostrano il consenso a quel piano. |
| Esecuzione | Dopo la deserializzazione il runner usa l'oggetto in memoria, senza rileggere il piano per ogni elemento. Una sostituzione successiva del file non modifica da sola quello snapshot. |

Le proprietà principali di `UpdateItem`, quali ID e versione, sono `init`; ciò riduce modifiche accidentali attraverso quel modello, ma non protegge il JSON ricevuto da un altro processo. Inoltre `ToPlanItem` conserva riferimenti alle liste di firmatari/Hardware ID e `PlanItem` non conserva `RequiresRiskConfirmation`. Questi riscontri non sono presentati come exploit eseguiti: mostrano perché il semplice riuso del modello o della finestra non equivale a un'autorizzazione immutabile completa.

## Modello di minaccia

Si assume un altro processo con i diritti dell'utente che può leggere, modificare, sostituire o ricreare i file nella root dati e avviare UpdateCenter con argomenti arbitrari. Non si assume la compromissione dell'eseguibile, del kernel o di un account amministratore; non occorre modificare la memoria della UI per dimostrare che il runner accetta un piano senza prova della sua conferma originale.

Per un runner amministratore restano necessari i privilegi previsti da Windows: non è stato dimostrato né tentato un bypass UAC. L'autorizzazione a elevare un eseguibile non identifica automaticamente la lista JSON approvata nell'app. Microsoft distingue i confini di sicurezza dalle difese aggiuntive e classifica UAC fra queste ultime; documenta inoltre il passaggio fra token standard e amministratore. Fonti: [criteri di sicurezza Windows](https://www.microsoft.com/en-us/msrc/windows-security-servicing-criteria), [funzionamento di UAC](https://learn.microsoft.com/en-us/windows-server/security/user-account-control/how-user-account-control-works).

L'obiettivo M6 comprende due proprietà diverse:

1. Il contenuto letto coincide con la selezione e i parametri originariamente approvati.
2. L'autorizzazione proviene da un consenso valido, vale per questa esecuzione e non può essere ricreata o riutilizzata tramite gli stessi file modificabili.

Un confronto con un digest fornito dal chiamante può aiutare la prima proprietà durante un avvio legittimo. Non dimostra la seconda contro un chiamante che costruisce sia il piano sia il digest. Un modello limitato alla corruzione accidentale o alla sola modifica del file durante un lancio fidato consentirebbe una correzione più piccola, ma sarebbe una garanzia più debole del requisito richiesto.

## Evidenze offline: cosa provano e cosa non provano

La modalità `--consent-integrity` usa una root GUID isolata. Richiama i veri validatori di percorso del runner tramite reflection, il vero `ToPlanItem` e la vera lettura/scrittura JSON. Dopo l'ammissione al confine del file usa soltanto un contatore in memoria al posto delle operazioni. **Non chiama `ElevatedUpdateRunner.Run`, WinGet, COM/WUA, UAC o il ripristino di sistema.**

È una prova del contratto mancante all'ingresso, corroborata dalla lettura del percorso reale; non è un test end-to-end del runner. I marcatori simulati raggiunti non dimostrano che un provider installerebbe effettivamente un pacchetto. In particolare, un elemento incompleto può essere respinto successivamente dalle protezioni M5: questo non crea retroattivamente una prova di consenso.

| Caso finale | Esito rispetto al requisito M6 |
|---|---|
| Piano originale con ID/versione attesi | PASS: ammesso con un solo marcatore simulato. Il termine “autorizzato” descrive la fixture, non una prova riconosciuta dal codice attuale. |
| Modifica di ID, versione, origine, tipo o operazione | 5 FAIL: il contenuto modificato supera l'ingresso corrente. |
| Modifica di installazione silenziosa o richiesta di ripristino | 2 FAIL: i parametri sostituiti vengono deserializzati senza confronto con quelli approvati. |
| Aggiunta di un aggiornamento | FAIL: vengono ammessi due elementi invece di quello iniziale. |
| JSON malformato | PASS: lettura rifiutata, zero marcatori. |
| Elemento JSON incompleto con percorsi validi | FAIL al confine di ammissione: i valori mancanti ricevono i default del modello. Non è una prova di superamento dei controlli del provider. |
| Sostituzione atomica prima del passaggio al runner | FAIL: lo stesso percorso porta alla nuova versione. |
| Sostituzione dopo la lettura | PASS: l'oggetto già deserializzato conserva la versione originale. |
| Seconda ammissione dello stesso piano; ricreazione da una copia | 2 FAIL: nessuna autorizzazione consumata viene riconosciuta. La fixture non simula la fine di un'installazione reale; prova il riuso dell'ingresso. La normale pulizia del file non autentica una copia ricreata. |
| Consenso assente, negato o annullato, con file introdotto direttamente | 3 FAIL: il contratto del runner non contiene un'informazione che distingua questi casi. Il normale ritorno dalla finestra annullata resta corretto; non è stato bypassato o automatizzato il dialogo. |
| Percorso esterno alla root | PASS: rifiutato, zero marcatori. |

Totale finale: **4 PASS, 15 FAIL attesi sul contratto richiesto**. Nessun risultato GREEN M6 e nessuna dichiarazione di difetto corretto. Le prime prove includevano anche tre mutazioni di metadati driver inattivi nella fixture software: eliminate dalla suite finale perché non rappresentavano cambiamenti delle operazioni di quella fixture. Il primo log RED è conservato, ma il conteggio sopra si riferisce a `final-red-test.log`.

## Soluzioni considerate e motivo della mancata implementazione

| Soluzione | Garanzia utile | Requisito che resta insoddisfatto |
|---|---|---|
| Hash/flag di approvazione accanto al JSON | Rilevazione di alcuni errori accidentali | Lo stesso autore può sostituire entrambi. Nessuna autenticazione del consenso. |
| Digest in argv, generato dallo snapshot della UI | Confronto con il contenuto atteso nell'avvio legittimo | Il runner accetta comunque un nuovo avvio con piano e digest coerenti costruiti da un altro chiamante. |
| File consumato, nonce o scadenza nella stessa root | Riduzione di doppi utilizzi accidentali | Copia, ricreazione o sostituzione dello stato modificabile; il nonce non acquista un'autorità dal solo fatto di essere casuale. |
| Snapshot e confronto prima del lancio | Stabilità dei dati del coordinatore | Non autentica il contenuto ricevuto né l'autorizzazione al confine del runner. |
| Seconda conferma nel runner sullo snapshot letto una sola volta | Nuovo consenso prima degli effetti, se la presentazione è completa e il processo è fidato | Non prova corrispondenza al consenso originale e non rifiuta automaticamente un piano alterato o riprodotto: lo sottopone a nuova autorizzazione. Occorre anche preservare i metadati di rischio mancanti dal piano. |

Una soluzione completa richiede un'autorità di consenso e uno stato/canale protetti rispetto al soggetto che può ricreare il piano, con verifica di provenienza, consumo e revoca; non basta aggiungere una pipe con un nome casuale o controllare il solo PID. Oppure occorre cambiare esplicitamente il contratto funzionale: la conferma autorevole viene data nel runner dopo la lettura del piano. Nessuna delle due scelte è un dettaglio di serializzazione da introdurre implicitamente sotto il vincolo “nessuna nuova architettura”.

Non è una dimostrazione che ogni possibile soluzione futura sia impossibile. È il motivo concreto per cui le correzioni minime valutate non soddisfano insieme integrità, consenso e anti-replay nel modello dichiarato. Si applica quindi l'istruzione dell'utente di documentare il blocco invece di introdurre protezioni apparenti.

## Build, regressioni e revisione

Log locali in [engine-m6](<../_output/engine-m6>), esclusi da Git:

| Controllo | Esito |
|---|---|
| Build Release del progetto smoke e dipendenze | PASS, zero avvisi/errori; `build.log` e verifica finale `final-build.log`. |
| Accettazione del confine M6 | RED, 4 PASS / 15 FAIL nella versione finale; `final-red-test.log`. I fallimenti sono il risultato richiesto dei test prima della correzione, non errori ambientali. |
| Regressioni M5 pertinenti | PASS: 16 gruppi più regressioni WinGet esistenti; `m5-regressions.log`. Riconfermati firma/hash tramite errori sintetici, target esatti, guard firmware/WUA, esiti individuali, riavvio, persistenza e watchdog nel perimetro offline M5. |
| Revisione progettuale indipendente | Nessuna soluzione minimale completa identificata; motivazione registrata in `review.txt`. Nessun revisore ha modificato file o avviato operazioni. |
| `git diff --check` | PASS. Diff limitato a harness di test e rapporto; codice applicativo invariato. |
| Commit | Non eseguito: accettazione M6 non superata. HEAD resta `c0329b9da165d4dbbf38fa454fc20037385a63a8`. |

Riproduzione dalla radice del repository, con dipendenze già disponibili:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '_output/engine-m1/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_TieredCompilation = '0'
dotnet build Tests/UpdateCenter.SmokeTests -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --consent-integrity
# Il comando precedente termina non-zero: requisiti M6 ancora insoddisfatti.
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --install-safety
git diff --check
```

Nessun audit generale, nuovo framework, installazione, download, scansione hardware, elevazione o modifica di sistema. Nessun push, merge, tag o release. Restano invariati cancellazione, pausa, recupero dello stato, riavvio e tutte le protezioni M5; non vengono dichiarati risolti i loro limiti già documentati.

## Raccomandazione successiva

Per sbloccare M6 serve prima decidere dove risiede l'autorità del consenso e se si accetta una conferma finale indipendente nel runner oppure un canale/stato di autorizzazione protetto. Non è stata scelta implicitamente una garanzia più debole.

**Un solo successivo intervento di affidabilità raccomandato, dopo tale decisione:** impedire l'avvio del processo quando la richiesta è già cancellata, con un test su un launcher simulato immediatamente prima di `Process.Start`. Non estendere quel lavoro a timeout o recupero; nessuna milestone successiva è iniziata.
