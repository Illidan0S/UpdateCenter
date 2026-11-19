# UpdateCenter 2.0.0 — M5 Installation Safety & Correctness

8 novembre 2026 — **M5 COMPLETATA nel perimetro offline concordato**, con due correzioni indipendenti. Build e test mirati PASS. Restano limiti espliciti nel trasferimento del consenso al runner; M5 non certifica l'intero confine privilegiato contro la manomissione locale.

## Checkpoint e perimetro

- Partenza verificata: `feature/engine-m4-wua-coverage`, HEAD `e9287c683bec9d9b85031d0df8a075a7b0088f26`, working tree pulito.
- Branch locale M5: `feature/engine-m5-install-safety`. Il commit M5 è quello che introduce questo documento, identificabile con `git log -1 --format=%H -- docs/ENGINE-M5-INSTALL-SAFETY.md`.
- Versione applicativa invariata. Nessuna modifica a WPF, branding, LAN, cataloghi o rilevamento WUA M4; nessuna nuova dipendenza.
- Nessuna installazione, download di pacchetti, interrogazione WUA reale, elevazione UAC o modifica di sistema. Nessun push, merge, tag o release.
- Riutilizzate le skill systematic-debugging, test-driven-development e verification-before-completion. Un solo revisore indipendente GPT-6 Astra Ultra, in sola lettura; nessun rilievo azionabile sulle modifiche finali.

## Percorsi effettivamente verificati

| Passaggio | Percorso reale e controllo | Evidenza M5 |
|---|---|---|
| Selezione e conferma | `MainWindow.RunUpdatesAsync` esegue il preflight, mostra `UpdateConfirmationWindow` e ritorna se `ShowDialog() != true`. Il pulsante di conferma rispetta preflight e consenso richiesto per il rischio. | Lettura mirata del codice; nessuna automazione della finestra o UAC. |
| Preparazione | `MainViewModel.InstallItemsAsync` passa gli elementi al coordinatore; `UpdateCoordinator.RunAsync` respinge elementi con `CanInstall=false` e separa software/runtime dai driver. | Fixture di rifiuto prima dell'avvio del batch; lettura del percorso restante. |
| Trasferimento | `ElevatedUpdateRunner.ToPlanItem` conserva ID, versione, origine e identificatori WUA; `RunBatchAsync` scrive il piano JSON atomicamente sotto la root dati, con nome contenente un token GUID. | Round trip reale `UpdateItem → PlanItem → JSON → WinGet` in root GUID isolata. Il token del nome non autentica il contenuto. |
| Processo e privilegi | Lo stesso eseguibile riceve `--update-runner-user` per il software o `--update-runner-admin` con `runas` per i driver. Il runner controlla percorsi del piano/stato/pausa e, dove richiesto, lo stato amministratore. La cancellazione UAC 1223 viene tradotta in cancellazione. | Verifica statica; fixture del validatore di percorso respinge traversal esterno. Nessun processo elevato avviato. |
| WinGet | Installazione, upgrade e fallback interattivo passano dalla validazione del target e da argomenti separati con `ProcessStartInfo.ArgumentList`. | Esecutori finti verificano ID, versione e origine su tutti e tre gli ingressi; nessun comando WinGet reale. |
| Windows Update | Ricerca sulla sorgente del piano, ricontrollo fresco di UpdateID/revisione e guard condiviso, poi EULA, download e installazione della raccolta selezionata. | Doppi WUA in memoria verificano sorgente/opzioni, blocchi prima di EULA/download/install e risultati individuali. |
| Ritorno all'app | Il runner pubblica stato/heartbeat/esito; il coordinatore legge stato, watchdog e risultati. La policy distingue successo dell'installer, verifica del target e riavvio. | Regressioni esistenti di JSON/watchdog e fixture del publisher: errore finale di persistenza propagato, non completamento silenzioso. |

I test attraversano codice produttivo con delegati e oggetti finti nei punti di esecuzione; non simulano un'installazione completa del sistema operativo. Il runner elevato completo non viene avviato.

## Correzione 1 — Conservare il target WinGet approvato

**Problema riprodotto.** Il vincolo `--version` era già presente, ma installazione e percorso interattivo imponevano l'origine `winget` anche quando il piano ne indicava un'altra. Inoltre un errore di corrispondenza nell'upgrade poteva attivare un secondo comando senza origine e successivamente un comando per nome. La fixture rende riuscito soltanto il comando senza origine: prima M5 un'altra sorgente poteva soddisfare l'operazione approvata. Un'origine vuota poteva raggiungere l'esecutore.

**Correzione minima.** Ogni comando di esecuzione mantiene `--id`, `--exact`, `--version` e `--source` del piano. Rimossi i tentativi di esecuzione senza origine o per nome. Le origini mancanti o fuori dal formato supportato vengono respinte prima del comando. La riconciliazione dopo alcuni errori rimane in sola lettura, mediante `list` sullo stesso ID e sulla stessa origine; anche la verifica ordinaria è circoscritta all'origine. Queste opzioni corrispondono ai contratti documentati di [WinGet upgrade](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade) e [WinGet list](https://learn.microsoft.com/en-us/windows/package-manager/winget/list).

**Prima/dopo.** `ApprovedTuple`, `NoSourceOrNameFallback` e `InvalidTargets`: FAIL sul codice precedente, PASS dopo la modifica. Il catalogo simulato passa da 2.0 a 3.0, ma tutti i comandi continuano a richiedere 2.0. Se la versione non è più disponibile, non viene sostituita automaticamente. Se l'inventario circoscritto mostra già 3.0, la policy preesistente può riconoscere il target raggiunto senza eseguire un upgrade a 3.0.

**Conseguenze funzionali.** Alcuni upgrade prima recuperati dai fallback ora falliscono e richiedono una nuova valutazione o intervento manuale. Il formato supportato dell'origine ammette lettere/cifre, trattino, punto e underscore, con primo carattere alfanumerico e massimo 160 caratteri. Origini personalizzate con spazi sono respinte; una nuova scansione non risolve necessariamente tale limitazione. Il nome dell'origine non è un vincolo crittografico sul suo endpoint o sul manifest.

## Correzione 2 — Esiti WUA individuali e prova fresca dell'installazione

**Problemi della stessa catena di verifica.** Il percorso di installazione non controllava l'esito della ricerca preliminare; accettava il successo aggregato/parziale senza verificare il singolo download o installazione; leggeva `IsInstalled` dall'oggetto ottenuto prima dell'installazione. Inoltre l'assenza dalla successiva ricerca degli aggiornamenti applicabili veniva trattata come prova positiva: una fixture con installazione fallita e ricerca vuota produceva successo verificato. La richiesta di riavvio poteva accompagnare un messaggio di completamento fuorviante.

`IUpdate.IsInstalled` descrive lo stato al momento della ricerca, quindi l'oggetto precedente non costituisce una verifica successiva. WUA espone inoltre esiti distinti per ogni elemento tramite `GetUpdateResult`. Fonti: [IsInstalled](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdate-get_isinstalled), [risultato individuale del download](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-idownloadresult-getupdateresult), [risultato individuale dell'installazione](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iinstallationresult-getupdateresult).

**Correzione minima nel solo percorso di installazione.**

- La ricerca preliminare deve avere esito utilizzabile, 2 o 3; restano obbligatori identità/revisione esatte e guard già esistente sul candidato fresco.
- Il download deve avere esito aggregato utilizzabile e risultato del singolo elemento `Succeeded=2`, con HRESULT non negativo, prima di creare l'installer.
- `InstallerSucceeded` richiede esito aggregato utilizzabile, risultato individuale 2 e HRESULT non negativo. La richiesta di riavvio considera risultato aggregato e individuale, senza convertire un fallimento in successo.
- La verifica interroga nuovamente lo stesso UpdateID/revisione, includendo stato installato e non installato mediante OR al livello superiore, come previsto da [IUpdateSearcher.Search](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search). Solo una risposta fresca utilizzabile con `IsInstalled=true` costituisce prova positiva. Assenza, ricerca fallita o stato illeggibile lasciano la verifica indisponibile.

**Prima/dopo.** Le fixture su esiti di ricerca, download individuale, installazione individuale, assenza, oggetto obsoleto e riavvio riproducono FAIL nel log RED; tutte PASS nel log finale. Aggiunti casi con HRESULT negativo anche quando il risultato individuale dichiara successo.

La policy comune non è stata riscritta: un'operazione WUA dichiarata riuscita ma senza stato fresco disponibile può restare `Success=true, Verified=false`; una prova fresca positiva può confermare il target anche dopo un errore dell'installer, per esempio se già raggiunto da un'altra operazione. Questi stati non provano chi abbia installato i byte. Il fallimento dell'installer più l'assenza del candidato non produce più una verifica positiva.

## Matrice delle prove e protezioni conservate

| Caso richiesto | Prova disponibile ed esito |
|---|---|
| Versione WinGet cambiata | PASS: round trip del piano, catalogo finto 3.0, comandi fissati a 2.0; riutilizzate le regressioni `WinGetExecutionTests` sulle versioni esatte/non valide. |
| ID/origine diversa | PASS: nessuna esecuzione senza origine o per nome; riga installata con altro ID non verifica il target; install/upgrade/interattivo preservano l'origine approvata. |
| Metadati incompleti/alterati | PASS per origini/ID/versioni non validi e metadati WUA obbligatori mancanti. Una sostituzione sintatticamente valida del piano resta invece accettata dal confine attuale: limite riprodotto e rinviato. |
| WUA modificato/non applicabile | PASS: ID o revisione diversa respinti prima di EULA/download; nessun candidato corrispondente autorizza l'installazione. Risposta fresca non installata impedisce la verifica positiva; risposta vuota non dimostra installazione. |
| Firmware mascherato | PASS: indizi nel titolo, modello BIOS e Hardware ID UEFI respinti; riutilizzati `WindowsUpdateSafetyTests`, incluse le classi conservativamente escluse. Nessun ampliamento della allowlist. |
| Annullamento/consenso | PASS offline per recupero WinGet rifiutato, senza retry/interattivo, ed elemento non installabile. Conferma iniziale e cancellazione UAC verificate solo nel codice. La cancellazione già richiesta prima del lancio del batch resta un limite distinto, descritto sotto. |
| Payload/hash/firma | PASS per propagazione di errori sintetici WinGet e HRESULT negativi WUA, prima dell'installazione dove previsto. Assenza di flag di bypass hash/force nei comandi provati. Nessuna verifica crittografica reale di payload eseguita. |
| Exit code e riavvio | PASS: WinGet 0 con verifica positiva/negativa, errore 1, 3010 e 1641; WUA risultato aggregato/individuale, HRESULT, stato fresco e riavvio. Un riavvio necessario non è una verifica completata. |
| Persistenza e runner | PASS: retry atomico per sharing violation, errore finale del publisher propagato, regressioni `JsonStorageHardeningTests` e `StatusWatchdogHardeningTests`, percorso esterno respinto. |

Le verifiche native di integrità/autenticità rimangono affidate a WinGet e WUA nei rispettivi percorsi. M5 prova come l'applicazione tratta alcuni errori e preserva i controlli, senza attribuirle una verifica autonoma del manifest o del certificato che non possiede. Il percorso OfficialInf non è stato modificato o rivalidato, essendo fuori dal perimetro richiesto.

## Validazione riproducibile e log

Artefatti locali esclusi da Git: [cartella engine-m5](<../_output/engine-m5>).

| Esecuzione | Esito e artefatto |
|---|---|
| Prima compilazione delle fixture | FAIL di solo setup test: `using` su tipo non IDisposable e parametro non usato. Registrato in `red-build.log`, corretto prima della prova funzionale; non contato come RED del prodotto. |
| Build delle fixture pronte | PASS, zero avvisi/errori: `red-ready-build.log`. |
| Prova sul comportamento precedente | RED funzionale atteso: `red-test.log`, con discrepanze WinGet e falsi esiti WUA sopra descritti. |
| Prima correzione verificata | PASS: `green-build.log`, `green-test.log`. |
| Aggiunta regressioni del confine/persistenza | PASS: `acceptance-build.log`, `acceptance-test.log`. |
| Stato finale | PASS, build Release zero avvisi/errori: `final-build.log`. Harness: 16 gruppi PASS più regressioni WinGet esistenti, `final-test.log`. L'osservazione del piano modificabile è separata dai PASS di sicurezza. |
| Log applicativi delle fixture | `acceptance-UpdateCenter-2026-11-08.log`, `final-UpdateCenter-2026-11-08.log`. |
| Revisione indipendente | Sola lettura di modifiche, test ed evidenze, compresa l'ultima fixture HRESULT: nessun rilievo azionabile. Resoconto in `review.txt`. |
| Diff | `git diff --check` PASS; nessun cambio di versione o file estraneo al perimetro. |

Comandi dalla radice del repository, con dipendenze già ripristinate e senza restore/download:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '_output/engine-m1/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_TieredCompilation = '0'
dotnet build Tests/UpdateCenter.SmokeTests -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --install-safety
git diff --check
```

Il dispatch inizializza obbligatoriamente una root GUID temporanea prima di qualsiasi I/O e verifica che differisca dalla root di produzione. La build include il progetto applicativo e le dipendenze del progetto smoke; non è una nuova build/publish di tutti i deliverable. Non eseguiti audit generale, smoke hardware completo o benchmark già documentati.

## Limiti residui e raccomandazione M6

1. **Consenso e piano modificabile — riprodotto offline.** La fixture scrive un piano con versione 2.0, lo sostituisce con versione 9.0, invoca il vero validatore di percorso e rilegge 9.0. Il percorso è valido ma non esiste un legame immutabile con il contenuto approvato. La scrittura atomica previene JSON parziali, non autentica l'autore o il consenso. Nessuna esecuzione del piano alterato o prova di escalation effettuata; il rischio dipende dall'accesso locale ai file e dal modello di minaccia.
2. **Cancellazione prima dell'avvio — evidenza statica.** In `RunBatchAsync` il controllo del token nel polling viene dopo `Process.Start`. Non è stato eseguito un batch reale già cancellato, perché avrebbe potuto avviare un processo. È distinto dal rifiuto della finestra e dal consenso al recupero, che sono controllati prima dei rispettivi percorsi.
3. **Identità del provider e stato finale.** `--source` vincola un nome configurato, non il contenuto del catalogo; `winget.exe` viene risolto dal percorso di ricerca dei processi senza una nuova attestazione del publisher. Il piano non contiene un digest del manifest WinGet approvato. WUA identifica l'offerta tramite UpdateID/revisione, non tramite la sola versione visualizzata. Risultati freschi indisponibili rimangono non verificati. Inoltre il helper preesistente WUA `Failed` lascia il valore predefinito di `Outcome` pur impostando `Success=false`: da valutare con i consumatori, senza terza correzione M5.

**Un solo primo intervento raccomandato per M6:** rendere verificabile il trasferimento del piano approvato al runner, definendo prima il modello di minaccia e il legame fra consenso, contenuto e processo destinatario. Partire dalla fixture di sostituzione già disponibile e aggiungere rifiuto prima di qualsiasi esecuzione per piano alterato, consenso assente o richiesta già cancellata. Un semplice hash accanto al JSON, modificabile dallo stesso soggetto, non risolve il problema. Nessuna implementazione M6 iniziata.

Una successiva validazione in VM isolata dovrà verificare comportamento reale COM/WUA, prompt e annullamento UAC, sorgenti WinGet configurate, codici installer e riavvio su snapshot ripristinabili, con autorizzazione separata alle installazioni. I PASS offline di M5 non sostituiscono tale validazione.
