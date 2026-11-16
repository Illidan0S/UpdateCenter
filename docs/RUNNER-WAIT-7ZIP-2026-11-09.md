# Blocco 7-Zip al 12% — diagnosi e correzione delle attese

Baseline: `feature/engine-m6-consent-integrity`, HEAD
`33f5468dbb2d20f161efc0de54b6a5d8a424580e`. Modifiche A/UX/consenso
preesistenti conservate. Nessun commit, reset, stash, push, merge o release.
Nessuna installazione, download di payload installer, modifica DPI o riavvio
eseguiti durante questo intervento.

## Operazione originale: evidenze e limiti

Il caso osservato mostrava il runner in attesa, con heartbeat aggiornato ma
senza avanzamento utile. Il valore visualizzato prima del preflight non
rappresentava progresso dell'installer. Non fu acquisito uno stack gestito:
l'attribuzione al processo originale resta un'inferenza, distinta dalla
riproduzione deterministica descritta sotto.

Nessuna operazione originale fu terminata o ripetuta per la diagnosi.
Identificativi di processo/piano, campionamenti e log della sessione sono
conservati separatamente nel backup locale e non fanno parte del checkpoint.

## Causa riprodotta

`App.OnStartup` chiama sincronicamente il runner sul thread WPF.
`WinGetService.Upgrade` e `RunInteractive` attendono `ValidateAsync` con
`GetAwaiter().GetResult()`. Gli await del guard e della lettura HTTP catturavano
il dispatcher bloccato. Anche la continuazione relativa al timeout HTTP
di 8 secondi poteva rimanere in coda sullo stesso dispatcher.

Un test STA con `DispatcherSynchronizationContext` non pompato, provider
manifest asincrono e nessun effetto ha riprodotto il blocco. Il test è fallito
prima della correzione e passa dopo. L'heartbeat viene pubblicato separatamente,
quindi può continuare durante il deadlock. Il valore 12 era assegnato prima
del preflight e non misurava un progresso dell'installer.

Lo stato originale è coerente con questo deadlock, prima del comando WinGet;
l'attribuzione al processo originale resta un'inferenza senza il suo stack.
Il consenso è già oltre il punto di attesa, mentre un binding nullo non evita
la verifica del manifest ufficiale. Non risultano motivi per indebolire M6-B o B2.

## Correzione

- Await di manifest e guard indipendenti dal dispatcher.
- Attese delimitate esclusivamente per lavoro read-only: preflight 90 secondi,
  verifica finale 75 secondi, riconciliazione 60 secondi. Il timeout cancella
  il token del provider; un provider non cooperativo può completare tardi,
  ma non contiene un avvio installer. I fault tardivi vengono osservati.
- Token propagato al guard, ai comandi, al percorso interattivo e alla verifica;
  controllo immediatamente prima del dispatch e dopo la lettura di verifica.
- Nessun retry installer o fallback introdotto. Le letture di riconciliazione
  preesistenti restano letture. Hash, identità, account, scope, binding,
  snapshot e autorizzazione IPC non sono indeboliti.
- Rifiuto del preflight distinto dal fallimento installer: non entra nella
  verifica che potrebbe trasformarlo erroneamente in successo.
- Timeout della verifica conserva esito, PID/comando e diagnostica dell'installer
  eventualmente già terminato, ma restituisce fallimento e verifica indisponibile.
- `ProcessRunner` controlla il token prima di `Start`; rimossa l'attesa sincrona
  finale ridondante. La scadenza include l'attesa asincrona del processo e dei
  flussi. Il limite installer preesistente di 90 minuti non è ridotto.
- IPC: letture/scritture ordinarie limitate a 30 secondi; attesa umana della
  conferma limitata a 15 minuti. Un timeout durante invio del grant chiude il
  canale e conserva la supervisione: non viene interpretato come prova di zero
  effetti e non determina un nuovo avvio.
- Stato software pubblica preflight, esecuzione e verifica reali, senza 12%
  artificiale. Il warning di stallo arriva anche alla UI senza rinnovare i timestamp.
  Un esito problematico non mostra titolo di successo né 100%.
- Host separato allineato ai nuovi parametri tramite adapter ancora fail-closed;
  nessun provider nativo aggiunto all'host.

## Verifiche

Host effettivo Windows `10.0.26320.0`, SDK 10.0.403 selezionato da `global.json`.
Root GUID isolate per tutti i test. Solo helper innocui nei test di processo;
provider installazione simulati.

| Verifica | Esito |
| --- | --- |
| Restore soluzione su host | PASS; tentativi sandbox senza diagnostica terminati con 1, ripetizione nativa riuscita |
| Build Release soluzione | PASS, 0 errori / 0 avvisi |
| Build host separato | PASS, 0 errori / 0 avvisi; adapter aggiornati dopo errore iniziale di compilazione |
| `--runner-waits` e inclusione nello smoke completo | 14 gruppi PASS |
| RED/GREEN deadlock dispatcher | FAIL atteso prima, PASS dopo |
| RED/GREEN cancellazione provider non cooperativo | FAIL atteso prima, PASS dopo |
| Deadline read-only | Mutazione senza deadline rilevata RED; deadline ripristinata GREEN |
| RED/GREEN avvio con token già annullato | FAIL atteso prima, PASS dopo |
| RED/GREEN preflight rifiutato trasformato in successo | FAIL atteso prima, PASS dopo |
| RED/GREEN cancellazione contemporanea alla verifica riuscita | FAIL atteso prima, PASS dopo |
| Manifest valido tardivo dopo timeout/cancel, Upgrade e Interactive | PASS, zero comandi; controllo tempestivo esattamente un comando simulato |
| Esito runner su timeout/cancel prima degli effetti | PASS, fase e fallimento/annullamento persistiti |
| IPC bloccato, timeout e cancellazione | PASS, nessuna autorizzazione restituita |
| `--install-safety`, incluse regressioni B2 e target WinGet | PASS |
| `--runner-consent` | 25 PASS, 0 FAIL |
| `--unified-consent`, processi e pipe reali con effetti simulati | PASS |
| `--release-audit` | PASS |
| Smoke completo finale | PASS, include i nuovi test |
| NetworkCore | PASS |
| Host `--self-test` | 10 gruppi PASS, exit 0 acquisito attendendo il processo |
| WPF errore IT/EN, chiaro/scuro, 720×500 | PASS; render WPF locali esaminati, nessuna cattura schermo continua |
| `git diff --check` | PASS |
| Windows 10 1809 reale, installazione reale corretta, UAC/installer | PENDING, non eseguiti |

I 15 FAIL RED storici di `--consent-integrity` restano intenzionali e invariati:
ChangedId, ChangedVersion, ChangedSource, ChangedKind, ChangedOperation,
ChangedSilentParameter, ChangedRestoreParameter, AddedUnapprovedItem,
IncompleteItemRejectedWithoutOperations, AtomicReplacementBeforeHandoff,
ConsumedPlanReplay, RecreatedPreviousPlan, ConsentAbsent, ConsentDenied,
ConsentCancelled. Questa fixture dimostra il vecchio limite dell'ammissione da file,
non esegue il runner M6-B. Il suo exit non-zero non è riportato come PASS.

Log RED/GREEN, build, test e render sono in `_output/hang-7zip-20261109`.
Revisione indipendente in sola lettura completata: nessun blocco di sicurezza
nel delta dopo il controllo aggiunto sulla cancellazione durante la verifica.

## Limiti prima di una nuova installazione

Non avviare una seconda operazione mentre l'originale non ha esito.
La nuova build non è stata avviata sul profilo reale e non ha installato 7-Zip.
La matrice Windows 10/11 reale resta incompleta; il target minimo 17763 è invariato
e non sono state introdotte nuove API Win32. Il codice legacy di annullamento
del coordinatore e di terminazione al limite assoluto resta esistente, non usato
per il recupero dell'originale. Non è stato aggiunto un pulsante di cancellazione
che prometta un arresto cooperativo non supportato.

La nuova scadenza di consenso riguarda anche un runner di un batch misto che
attende il completamento del batch precedente: oltre 15 minuti quel consenso
preparato scade in modo conservativo e richiede di ripetere il flusso.

## File dell'intervento

Produzione: `Services/ElevatedUpdateRunner.cs`, `Services/RunnerExecutionBoundary.cs`,
`Services/RunnerReadOnlyWait.cs` (nuovo), `Services/WinGetScopeGuard.cs`,
`Services/WinGetManifestSafetyService.cs`, `Services/WinGetService.cs`,
`Services/ProcessRunner.cs`, `Services/RunnerConsentChannel.cs`,
`Services/PreparedUpdateSession.cs`, `UpdateProgressWindow.xaml.cs`.

Test: `Tests/UpdateCenter.SmokeTests/Program.cs`, `RunnerWaitTests.cs` e
`RunnerWaitUiTests.cs` (nuovi nella stessa cartella),
`Tests/UpdateCenter.ConsentTestHost/NativeBlocks.cs` e
`Tests/UpdateCenter.ConsentTestHost/UpdateCenter.ConsentTestHost.csproj`.
Questo rapporto è il nuovo documento dell'intervento.

Working tree intenzionalmente non pulito, indice non modificato. Nessun commit
creato; le modifiche preesistenti A/UX/consenso rimangono nello stesso working tree.
