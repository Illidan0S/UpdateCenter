# Riepilogo unico e consenso autorevole — 9 novembre 2026

Baseline: `feature/engine-m6-consent-integrity`, HEAD
`33f5468dbb2d20f161efc0de54b6a5d8a424580e`, con modifiche A/UX già presenti.
Nessun commit creato; indice e stash non modificati. Versione 1.1.5 invariata.

## Causa e flusso implementato

`MainWindow.RunUpdatesAsync` mostrava un `UpdateConfirmationWindow` preliminare,
apriva il progresso e chiamava il coordinatore. Ogni runner, dopo aver letto e
congelato il proprio JSON, mostrava un ulteriore `RunnerConfirmationWindow`.
Un gruppo misto poteva quindi richiedere anche due consensi di runner distinti.

Ora il coordinatore prepara prima tutti i runner necessari, senza autorizzare
ripristino, provider, EULA, download o installazione. Il software resta nel
processo utente, i driver nel processo amministratore. Ogni runner valida e
congela il proprio snapshot, pubblica `AwaitingConsent` e attende sul canale.
Il coordinatore confronta integralmente gli snapshot ricevuti con quelli
previsti in memoria, poi mostra **lo stesso riepilogo WPF esistente**, in modalità
autorevole. Contiene elenco, versioni, provider/origine, binding, rischi, avvisi,
opzioni dei batch e checkbox obbligatoria, sempre visibile nel piè di pagina.

Caso nominale: **riepilogo unico → conferma → avanzamento aggiornamenti**.
La vista progresso viene aperta prima di rilasciare il primo assenso. Non viene
aperto un secondo dialogo di conferma applicativo. L'eventuale UAC Windows resta
necessario e avviene durante la preparazione del runner driver, prima del riepilogo.

## Autorità e garanzie

La collocazione dell'autorità cambia esplicitamente: dalla finestra separata di
ciascun runner al riepilogo unico della UI fidata. Non viene mantenuta la vecchia
regola letterale «un dialogo per runner»; restano consenso fidato prima degli
effetti, snapshot immutabile, controlli M5/B2 e verifiche del runner.

- Nessun booleano, digest o token nel JSON vale come approvazione.
- Pipe creata prima del lancio, prima istanza e connessione unica; ACL sul SID
  reale dell'utente, accesso di rete negato. Non usa `CurrentUserOnly`, che verifica
  anche il livello di elevazione e impedirebbe il caso split-token.
  [Documentazione Microsoft](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0).
- Entrambi i peer verificano il PID ottenuto dal kernel, il processo vivo con
  handle mantenuto aperto, l'immagine eseguibile, la sessione e il SID. La UI accetta
  esclusivamente il processo restituito dal proprio launcher; il runner controlla
  anche l'istante di creazione dell'autorità. Il client usa impersonazione
  `Identification`, senza concedere impersonazione del token elevato.
- Il nome della pipe e gli argomenti CLI identificano il collegamento: da soli
  non autorizzano nulla. Nessuna riconnessione, cache di assenso o consenso
  persistente. Restano fidati eseguibili, memoria e UI, come nel modello M6-B.
- Il gate è monouso, confronta tutti i campi dello snapshot e ricontrolla tutti i
  batch ancora in attesa prima di ciascun rilascio. Il runner rilegge il file solo
  per verificarne la corrispondenza; esegue sempre lo snapshot originale congelato.
- Mutazione, peer diverso, rifiuto, sessione non interattiva o disconnessione
  impediscono nuovi effetti e richiedono di ripetere il flusso. Nessun fallback
  automatico al vecchio dialogo in modalità canale.
- Un errore di trasporto dopo l'invio potrebbe seguire la consegna completa:
  viene mantenuto il monitoraggio del processo, senza presumere che non abbia
  ricevuto il consenso. L'annullamento conserva il percorso preesistente del runner.
- Se il secondo gruppo viene bloccato, i risultati già ottenuti dal primo vengono
  conservati e restituiti alla cronologia; i target non avviati restano quelli
  originariamente confermati, con esito annullato.
- Rerun, recupero e fallback non riutilizzano il consenso precedente. I percorsi
  autonomi CLI mantengono la propria conferma WPF; questo non aggiunge finestre
  al caso nominale della MainWindow.

Le API di identificazione delle pipe sono disponibili da Windows Vista;
non è stata introdotta una dipendenza da Windows 11.
[Requisiti dell'API](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid).

## Verifica

Evidenze locali non tracciate: `_output/unified-consent-20261109/`.
SDK/configurazione del repository, Release, Windows x64.

| Verifica | Esito |
|---|---|
| Build soluzione e harness smoke | PASS, zero errori/avvisi |
| `--unified-consent` | PASS: gate/presentazione e 13 scenari con processi separati ed effetti simulati |
| Caso misto, unico riepilogo e progresso prima degli effetti | PASS |
| Mutazioni prima/durante/dopo conferma e fra batch | PASS; nessun target modificato eseguito |
| Rifiuto, disconnessione, owner errato, creation-time errato, client estraneo | PASS |
| Riutilizzo assenso, nuovo gate senza consenso, annullamento | PASS |
| Errore di invio dopo consegna completa | PASS; risultato reale del runner simulato conservato |
| `--runner-consent` | PASS, 25 gruppi M6-B |
| `--install-safety` | PASS M5 e B2, inclusi i 10 gruppi metadata boundaries |
| Smoke completo | PASS, inclusa integrazione Restart Manager sul proprio helper |
| `--release-audit` | PASS |
| NetworkCore | PASS |
| `--unified-consent-ui` | PASS, WPF reale IT/EN, chiaro/scuro, 720×500; nessun collegamento a installer |
| Verifica immagini WPF finali | PASS: checkbox leggibile e persistente, dettagli scorrevoli, azioni visibili |
| Build host M8 e `--self-test` | PASS, zero errori/avvisi, 10 gruppi |
| `git diff --check` | PASS |

Il primo test di presentazione ha riprodotto il testo che richiedeva una seconda
conferma. Nel test generale era presente una fixture OBS senza `Source`, pur
asserendo `winget`: aggiunta l'origine esplicita alla sola fixture, mantenendo
immutato il codice produttivo che rifiuta fallback di origine. Nella QA visiva
è stato corretto il contenuto della checkbox per il suo template esistente.

Una prima esecuzione da un OutDir condiviso da tutta la soluzione ha mescolato
runtime self-contained e framework-dependent, impedendo l'avvio degli harness.
Le verifiche finali usano l'output dedicato smoke e il runtime installato per
NetworkCore; nessun componente è stato installato per risolvere il problema.

L'host M8 compila sorgenti collegati e richiedeva l'allineamento ai nuovi tipi;
mancavano inoltre tipi B2/UX introdotti prima di questo intervento. I suoi nuovi
adapter **solo test** rifiutano il canale nativo, la preparazione e il rilascio;
non incorporano la pipe produttiva né fanno riferimento all'assembly produttivo.
Il controllo IL e i blocchi degli effetti rimangono attivi. Questo host non
certifica il nuovo canale: lo esercitano i nuovi test smoke separati.

**PENDING:** collegamento reale UI standard ↔ runner elevato con UAC; sessione
disconnessa e matrice Windows/DPI nativa completa. SID differenti sono rifiutati
dal canale: il caso con credenziali di altro account non viene assunto funzionante.
M6-B resta WINDOWS VALIDATION PENDING. I 15 FAIL RED storici non sono stati
modificati né riclassificati. Nessuna installazione reale o download di payload,
nessun cambiamento di DPI/risoluzione, nessun push/merge/tag/release.

## File di questo intervento

Produzione:

- `App.xaml.cs`
- `MainWindow.xaml.cs` (solo flusso di avvio, rispetto al working tree iniziale)
- `ViewModels/MainViewModel.cs`
- `UpdateConfirmationWindow.xaml` e `UpdateConfirmationWindow.xaml.cs`
- `Services/ElevatedUpdateRunner.cs`
- `Services/WpfRunnerConsent.cs`
- `Services/RunnerExecutionBoundary.cs` (commento sul confine)
- `Services/PreparedUpdateSession.cs` (nuovo)
- `Services/RunnerConsentChannel.cs` (nuovo)
- `Services/UnifiedConsentGate.cs` (nuovo)

Test e documentazione:

- `Tests/UpdateCenter.SmokeTests/Program.cs`
- `Tests/UpdateCenter.SmokeTests/UnifiedConsentTests.cs` (nuovo)
- `Tests/UpdateCenter.SmokeTests/UnifiedConsentUiTests.cs` (nuovo)
- `Tests/UpdateCenter.ConsentTestHost/UpdateCenter.ConsentTestHost.csproj`
- `Tests/UpdateCenter.ConsentTestHost/HostSafetyTests.cs`
- `Tests/UpdateCenter.ConsentTestHost/ConsentChannelBlocks.cs` (nuovo)
- questo rapporto.

Le modifiche A/UX preesistenti rimangono nel working tree. Il confronto con gli
snapshot iniziali dei file condivisi conferma l'intervento circoscritto al flusso
di aggiornamento e al dispatch/fixture dei test. Nessun commit creato per evitare
di includere il lavoro preesistente; indice vuoto, stash preesistente conservato.
