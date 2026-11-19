# Copertura: nove elementi non selezionati

> Esito successivo: [B2 — recupero scope e garanzie nel runner](UPDATE-COVERAGE-B2.md).
> Cinque recuperi osservati; la precedente ammissione di Miniconda user è corretta
> in blocco per `deny`. I checkpoint e la scansione sotto restano evidenze storiche.

> I paragrafi iniziali conservano il checkpoint dell'8–9 novembre. La sezione
> **Intervento B — 9 novembre 2026** in fondo descrive la verifica successiva,
> le correzioni e i limiti dei conteggi storici.

8 novembre 2026. Branch `feature/engine-m6-consent-integrity`.

## Checkpoint UX

Commit locale **`c8ca78a1f44254dbb36ec06f0c9f96a255ba8cbc`**:
`fix(ui): improve scan progress feedback and animations`.
Genitore verificato `2d8eeec1caa94c377a4a25e675e8ba15570dac70`.
Inclusi soltanto i sette file UX documentati; controllo diff/whitespace superato,
working tree pulito dopo il commit. L'utente ha confermato le cinque prove
manuali; non sono stati ripetuti i test UX o gli audit M4/M5/M6-B.

## Dati realmente disponibili

La finestra originale era ancora aperta: **51 risultati, 42 selezionati**.
Le ricerche temporanee `Aggiornamento manuale` e `Conferma` hanno isolato
rispettivamente sette checkbox disabilitate e due abilitate ma vuote. Il filtro
è stato poi ripristinato, senza cambiare selezioni o avviare operazioni.
L'utente ha confermato di aver usato **Seleziona tutto**, senza deselezioni manuali.

I nomi e le versioni della UI sono stati incrociati con il solo inventario:

```powershell
winget list --source winget --disable-interactivity --nowarn
```

Sono state lette solo le righe dei nove elementi per verificare gli ID. Non è
stato eseguito un comando di upgrade/install/show/download o di riparazione.
Per tutti e nove: provider **WinGet**, nome dell'origine configurata **`winget`**,
operazione applicativa **Upgrade**. Questo accerta il nome dell'origine, non
costituisce una nuova attestazione dell'endpoint o del payload.

**Limite temporale:** la vista corrente dice `Scansione annullata`. Il report M1
più recente conferma `cancelled`, 51 software restituiti, 51 risultati finali
e nessun risultato WUA raggiunto. I due report precedenti sono `success`, con
51 software e zero offerte WUA. La diagnosi riguarda quindi i 51 elementi
software/runtime conservati nella vista, non una nuova scansione driver completa.
M1/M4 conservano contatori e tempi, non identità né cronologia delle selezioni.
Non è servita una nuova persistenza degli inventari: i dati erano ancora nella UI.

## Matrice dei nove elementi

`user` indica l'utente corrente, `machine` l'intero computer. Le frecce degli
ambiti indicano **ambito locale rilevato → prima voce Scope letta dal manifest**,
non un cambio effettivamente eseguito né una scelta definitiva di WinGet.
Nessuno dei nove è escluso per una scelta manuale individuale dell'utente.

| Nome nella scansione | ID verificato | Installata → disponibile | Stato e causa della mancata selezione | Gestione attuale / categoria |
| --- | --- | --- | --- | --- |
| Comet | `Perplexity.Comet` | `140.1.7339.21342` → `156.0.8078.132` | Aggiornamento manuale; `user → machine`; `CanInstall=false` | **Manuale/bloccato** |
| Git | `Git.Git` | `2.55.0.3` → `2.56.0.4` | Aggiornamento manuale; `machine → user`; `CanInstall=false` | **Manuale/bloccato** |
| Google Chrome | `Google.Chrome.EXE` | `156.0.8078.80` → `157.0.8103.39` | Aggiornamento manuale; `machine → user`; `CanInstall=false` | **Manuale/bloccato** |
| Hytale Launcher | `HypixelStudios.Hytale` | `2026.01.13-e6eb932` → `2026.10.26-7b63bde` | Aggiornamento manuale; `machine → user`; `CanInstall=false` | **Manuale/bloccato** |
| Inno Setup 7.0.2 | `JRSoftware.InnoSetup.7` | `7.0.2` → `7.1.1` | Aggiornamento manuale; `user → machine`; metadati comportamento non verificati; `CanInstall=false` | **Manuale/bloccato** |
| Microsoft Visual Studio Code | `Microsoft.VisualStudioCode` | `1.141.0` → `1.143.1` | Aggiornamento manuale; `machine → user`; metadati comportamento non verificati; `CanInstall=false` | **Manuale/bloccato** |
| Modrinth App | `Modrinth.ModrinthApp` | `0.10.3` → `0.23.1` | Aggiornamento manuale; `machine → user`; metadati comportamento non verificati; `CanInstall=false` | **Manuale/bloccato** |
| EA app | `ElectronicArts.EADesktop` | `13.778.0.6291` → `13.822.0.6335` | Conferma richiesta; metadati classificati `uninstallPrevious`; esclusa da Seleziona tutto; `CanInstall=true` | **Già gestibile, condizionato alla conferma specifica del rischio** |
| Miniconda3 py312_24.5.0-0 (Python 3.12.4 64-bit) | `Anaconda.Miniconda3` | `py312_24.5.0-0` → `py314_26.9.4-0` | Conferma richiesta; metadati classificati `uninstallPrevious`; esclusa da Seleziona tutto; `CanInstall=true` | **Già gestibile, condizionato alla conferma specifica del rischio** |

EA e Miniconda non sono nuovi recuperi: il flusso esistente permette la selezione
individuale e mantiene le conferme preliminari e quella autorevole M6-B nel runner.
La diagnosi non ha selezionato né eseguito questi pacchetti. La rimozione preventiva
dichiarata dall'installer è un rischio da valutare esplicitamente, non un permesso
per introdurre un fallback automatico disinstallazione/reinstallazione.

## Percorsi alternativi e rischi da verificare

Le seguenti sono **possibilità guidate da verificare**, non modalità nuove abilitate
né installer già validati. Ogni alternativa deve conservare ID, versione esatta,
account, architettura e ambito; un'offerta diversa richiede un nuovo piano e un nuovo
consenso. Integrità, firma/hash e risultato finale non sono stati provati su payload
reali in questo incarico. Non è sufficiente aprire un link alla versione “latest”.

| Elemento | Percorso alternativo possibile e soluzione proposta | Rischio residuo specifico |
| --- | --- | --- |
| Comet | Verificare un aggiornamento in-place della copia utente, con ID/versione della tabella; mantenere il blocco automatico fino a prova dell'installer compatibile. | Seconda copia machine e associazione dei profili a una copia/account diverso. |
| Git | Verificare la variante machine dello stesso `Git.Git` e versione, oppure un percorso ufficiale che conservi quella installazione. | Duplicazione user/machine, PATH e strumenti che continuano a usare la copia precedente. |
| Chrome | Verificare la variante machine di **`Google.Chrome.EXE`** per la versione indicata. | Passare a `Google.Chrome`, a un ID MSI o alla copia utente sostituirebbe il target; profili/policy e ambito devono restare coerenti. |
| Hytale | Verificare un percorso ufficiale compatibile con l'installazione machine e con l'esatta build del launcher. | Il percorso per utente potrebbe creare un'altra copia; un updater che propone una build diversa non soddisfa questo piano. |
| Inno Setup | Verificare l'installer user della linea **`JRSoftware.InnoSetup.7`**, versione `7.1.1`. | Cambio ambito o linea del pacchetto, percorsi del compilatore e compatibilità degli script non verificati. |
| VS Code | Verificare un installer machine della versione `1.143.1` per il medesimo ID; eventuale aggiornamento interno solo se mantiene questi vincoli. | Confondere System/User installer, doppia copia, profilo/estensioni e versione effettiva. |
| Modrinth | Verificare un percorso ufficiale che aggiorni la copia machine alla versione indicata. | Duplicazione della copia utente e associazione delle istanze/profili; conservazione dei dati non provata. |
| EA app | Il percorso guidato esistente richiede scelta individuale, valutazione del rischio e consenso del runner. Valutare prima gli effetti della rimozione dichiarata. | Continuità dell'app/account, conservazione dei dati e raggiungimento della versione esatta non verificati con un'installazione. |
| Miniconda | Valutare prima ambienti e dipendenze Python; usare il percorso esistente solo dopo consenso specifico e verifica del rischio. Una migrazione guidata separata può essere preferibile, ma non viene automatizzata. | Target `py314` rispetto a `py312`, compatibilità degli ambienti e rimozione della distribuzione precedente; nessuna conservazione degli ambienti dimostrata. |

## Diagnosi del controllo scope

`WinGetInstalledScopeService.Detect` confronta DisplayName/DisplayVersion nel
registro HKLM/HKCU. `WinGetManifestSafetyService.ParseInstallerScope` prende il
primo match `Scope` nell'intero manifest: non seleziona la variante per architettura,
tipo o ambito dell'installazione. `ApplyScopeCompatibility` blocca quando i due
valori non vuoti differiscono. L'ordine delle voci può quindi produrre un blocco
conservativo anche se esistesse un'altra variante compatibile.

È dimostrato il limite del parser, **non** che una variante compatibile esista per
ognuno dei sette target reali. I manifest della scansione non sono persistiti e
non sono stati nuovamente scaricati. Non viene attribuito al provider un cambio
di scope che l'app ha soltanto inferito da metadati parziali.

Per recuperare realmente un caso occorre provare la variante esatta compatibile
e far rispettare il vincolo di scope anche in piano, consenso e comando del runner.
L'attuale comando conserva ID/versione/origine ma non impone `--scope`.
Rimuovere semplicemente il blocco o considerare compatibile un manifest ambiguo
non dimostrerebbe il rispetto dell'ambito. Questo ampliamento non è implementato.

## Piccole correzioni applicate

1. **Priorità/descrizione coerenti con l'installabilità.** Per Inno Setup, VS Code
   e Modrinth il flag dei metadati sconosciuti prevaleva sul blocco e il tooltip
   dichiarava che l'elemento poteva essere selezionato. Ora `CanInstall=false`
   prevale nella sola presentazione: `Solo verifica` / `Review only`, istruzione
   manuale coerente. Anche la combinazione blocco+rischio non promette sblocco
   attraverso la conferma. I flag e le regole di selezione restano invariati.
2. **Motivo scope qualificato, IT/EN.** Il testo dichiara “Ambito non confermato”,
   distingue copia rilevata e voce di metadati, segnala possibili installer multipli
   e mantiene il blocco fino alla verifica di versione esatta e stesso ambito.

File applicativi modificati: `Models/UpdateItem.cs`,
`Services/WinGetManifestSafetyService.cs`. Test dedicato:
`Tests/UpdateCenter.SmokeTests/UpdateEligibilityTests.cs`, dispatch
`--update-eligibility` in `Program.cs`. Nessuna nuova modalità d'installazione,
provider, dipendenza, flag di bypass, modifica a M4/M5/M6-B o selezione reale.

## Conteggi dopo le modifiche

- **42/51** attualmente selezionati, secondo la scelta Seleziona tutto dell'utente.
- **44/51** ammessi dal criterio applicativo `CanInstall`: i 42 più EA e Miniconda,
  che richiedono scelta individuale e conferma del rischio.
- **7/51** restano manuali/bloccati per ambito non confermato.
- **0** ulteriori pacchetti sbloccati dalle modifiche; nessun recupero aggiuntivo
  sicuro dimostrato. Possibilità di recupero dei sette da verificare per target.
- **0 installazioni reali eseguite o validate**: 44 significa ammissibilità al
  percorso, non 44 upgrade garantiti. Preflight, disponibilità del target,
  applicabilità, consenso M6-B e verifica finale possono ancora fermare l'esecuzione.

Correzioni approvate dall'utente e checkpoint copertura autorizzato il 9 novembre 2026.
M6-B resta **WINDOWS VALIDATION PENDING**. Nessun push, merge, tag, release o M8.

## Validazione delle sole modifiche nuove

Prima della correzione, il dispatch dedicato ha riprodotto i difetti di precedenza
e attribuzione del messaggio in entrambe le lingue. Dopo la correzione sono
superati tre gruppi offline:

1. Precedenza di blocco su priorità/tooltip, compresi rischio e metadati ignoti,
   IT/EN; selezione e retry restano impediti per l'elemento bloccato.
2. Descrizione qualificata dello scope; mismatch ancora bloccato e caso con
   stesso scope invariato.
3. Batch simulato di 51 elementi: 42 selezionati, 44 ammissibili, 7 bloccati;
   selezione individuale senza cancellare il flag rischio e deselezione manuale
   senza alterare l'installabilità. Dipendenze dei provider simulate e mai invocate;
   persistenza confinata alla root GUID del harness.

Build Release della soluzione e delle uscite app/test: zero errori e zero avvisi.
Un primo avvio del test dalla directory comune di output della soluzione aveva
una collisione `WindowsBase` tra runtime dei progetti: risolta producendo app e
harness in directory separate; non era una regressione applicativa. Il successivo
avvio del harness è passato. Nessun pacchetto scaricato o installato.

Comandi riproducibili dalla radice, con restore già disponibile:

```powershell
$appOutput = Join-Path (Get-Location) '_output/update-eligibility-20261108/app/'
$testOutput = Join-Path (Get-Location) '_output/update-eligibility-20261108/smoke/'
dotnet build UpdateCenter.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false "-p:OutDir=$appOutput"
dotnet build Tests/UpdateCenter.SmokeTests -c Release --no-restore -m:1 -p:UseSharedCompilation=false "-p:OutDir=$testOutput"
& (Join-Path $testOutput 'UpdateCenter.SmokeTests.exe') --update-eligibility
git diff --check
```

Log in `_output/update-eligibility-20261108/`: `release-build.txt`,
`app-build.txt`, `test-build.txt`, `eligibility-tests.txt`.
Build applicativa di revisione: `_output/update-eligibility-20261108/app/UpdateCenter.exe`.
La finestra originale non è stata chiusa o sostituita, per conservare i risultati
e le selezioni della scansione osservata. Le nuove descrizioni sono nella build
di revisione, non nel processo già aperto.

## Intervento B — 9 novembre 2026

Base verificata: `ef96f649ee156eaaaf6313d90375c5ee5a463834`, albero pulito.
Nessuna nuova scansione. Letture native limitate a `winget --version` e
`winget upgrade --help`: client **v1.29.290**. Acquisiti esclusivamente i sette
manifest YAML ufficiali delle versioni della tabella; nessun URL installer richiesto.

### Perché 31 → 42 → 51, e perché 44

`UpdateItem` nasce selezionato. `WinGetService.ParseUpgradeTable` distingue
Software/Runtime; inventario, deduplica e soppressione precedono i metadati.
`WinGetManifestSafetyService.ApplySafetyClassification` conserva la selezione
per `install`, la toglie per comportamento ignoto, rischio `uninstallPrevious`
o `deny`; soltanto `deny` e il controllo scope disabilitano qui l'installabilità.
`MainViewModel.AddUpdates` deduplica per `(Kind, Id)` senza riclassificare.

La checkbox usa `CanInstall`; il suo setter impedisce la selezione se falso.
`SelectedItems`, `SelectedCount` e `CanUpdate` richiedono `CanInstall && IsSelected`.
`SetAllSelected(true)` aggiunge i `CanInstall && !RequiresRiskConfirmation`
su tutta la raccolta, anche oltre il filtro visivo. La selezione individuale
di un elemento rischioso conserva il flag e **non costituisce consenso**.

| Classe | Preselezione WinGet | Singola | Aggiunta collettiva | Accesso al piano |
| --- | --- | --- | --- | --- |
| Comportamento verificato, nessun blocco | Sì | Sì | Sì | Preflight e consenso runner |
| Comportamento ignoto, nessun blocco | No | Sì | Sì | Stesse verifiche; non certificato sicuro dal flag UX |
| Rimozione precedente dichiarata | No | Sì | No | Conferma specifica preliminare e consenso M6-B |
| Blocco noto | No | No | No | Rifiutato dal preflight e dal coordinatore |

I **31** iniziali sono un dato riferito dall'utente. Il report esistente
`_output/scan-progress-20261108/host-scan-m1.json` conta 51 candidati WinGet,
383 righe inventario, zero duplicati/soppressi, 51 risultati e zero WUA/catalogo.
Non conserva i nomi dei 51 né le selezioni iniziali. Per questo percorso, gli
**11** selezionabili ma non preselezionati sono spiegati dal ramo dei metadati
comportamentali ignoti; è una ricostruzione dal codice, **non una nuova
osservazione nominativa**. Non è possibile attribuire i nomi agli 11 o certificare
retroattivamente la loro classificazione individuale.

**42 = 31 + 11; 44 = 42 + EA + Miniconda; 51 = 44 + 7 bloccati.**
42/51 e i nove nomi sono osservazioni del checkpoint precedente. WUA seleziona
secondo obbligatorietà/`BrowseOnly`/`AutoSelectOnWebSites`; il catalogo driver
ufficiale propone elementi facoltativi non preselezionati. Queste regole non
spiegano gli 11 di questa specifica scansione, che contiene solo WinGet.

### Matrice identificabile e classificazione dei sette

Origine per tutte le righe: `winget`; ID/versioni installate e disponibili sono
nella matrice storica sopra. Qui “piano” significa ammissibilità applicativa,
non successo garantito. La preselezione dei nove è dedotta dai predicati.

| Elemento | Preselezione | Singola | Aggiunto da tutti | Piano | Esito e motivo verificato |
| --- | --- | --- | --- | --- | --- |
| Comet | No | No | No | No | **BLOCKED**: solo machine, locale user; exe x64, comportamento install |
| Git | No | No | No | No | **BLOCKED**: user/machine x64 e arm64, inno, install; variante non vincolata nel runner |
| Chrome EXE | No | No | No | No | **BLOCKED**: user/machine x86/x64/arm64, exe, install; machine usa `--system-level`, non basta condividere payload |
| Hytale Launcher | No | No | No | No | **BLOCKED**: solo user, locale machine; nullsoft x64, install, destinazione LocalAppData |
| Inno Setup 7 | No | No | No | No | **BLOCKED**: user/machine x64, inno; `/CURRENTUSER` e `/ALLUSERS`, comportamento non dichiarato, variante non vincolata |
| VS Code | No | No | No | No | **BLOCKED**: user/machine x64 e arm64, inno; payload System/User distinti, comportamento non dichiarato, variante non vincolata |
| Modrinth App | No | No | No | No | **BLOCKED**: solo user, locale machine; nullsoft x64, comportamento non dichiarato, destinazione LocalAppData |
| EA app | No | Sì | No | Con consenso specifico | **CONDITIONAL**: rimozione precedente, percorso individuale già supportato |
| Miniconda | No | Sì | No | Con consenso specifico | **CONDITIONAL**: rimozione precedente e cambio linea Python, percorso individuale già supportato |
| Altri 42, nomi non persistiti | 31 riferiti, 11 no per ricostruzione | Sì nel checkpoint | Sì nel checkpoint | Con consenso runner | Evidenza insufficiente per una matrice nominativa; nessun nome inventato |

I tre blocchi a scope unico sono coerenti con i manifest attuali della versione
esatta. Per gli altri quattro è dimostrato il **difetto di attribuzione della
prima voce**, non un percorso SAFE-FIX completo. Nessun recupero autorizzato.
La valutazione vale per questi ID/versioni, non per future versioni o altri ID.

### Evidenze e limite dell'esecuzione

Fonti lette il 9 novembre: [help upgrade Microsoft](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade)
e [flusso upgrade del client v1.29.290](https://github.com/microsoft/winget-cli/blob/v1.29.290/src/AppInstallerCLICore/Workflows/UpdateFlow.cpp).
`--scope` è documentato come filtro dell'installazione esistente. Il client
seleziona l'installer tramite comparatore/applicabilità anche per versione esatta;
l'help da solo non attesta la variante finale.

| Manifest esatto (link ufficiale) | SHA256 del testo acquisito |
| --- | --- |
| [Comet 156.0.8078.132](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/Perplexity/Comet/156.0.8078.132/Perplexity.Comet.installer.yaml) | `C64A89AF2169C8448AD06A00BE7C1B71537B0A301FA8645E3EEECCDACDEF1619` |
| [Git 2.56.0.4](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/Git/Git/2.56.0.4/Git.Git.installer.yaml) | `661AF7ADECDC1104FF06434E0B697BC8FB5E5F4A389795EA48865A466BCDD954` |
| [Chrome EXE 157.0.8103.39](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/Google/Chrome/EXE/157.0.8103.39/Google.Chrome.EXE.installer.yaml) | `5A1D567CFE35B766634BC2C5987289A6BAC5A4CCCC9F352BD57299D6CB8AD21B` |
| [Hytale 2026.10.26-7b63bde](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/h/HypixelStudios/Hytale/2026.10.26-7b63bde/HypixelStudios.Hytale.installer.yaml) | `3F0C09ABAB90890069D1D381276D4E491E8E4C727DBCA04487931DA5977864EF` |
| [Inno Setup 7.1.1](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/j/JRSoftware/InnoSetup/7/7.1.1/JRSoftware.InnoSetup.7.installer.yaml) | `95037F1F57655780D66F07638B41F2B0326477328F00FC7634C562C8D4E0C1B5` |
| [VS Code 1.143.1](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/VisualStudioCode/1.143.1/Microsoft.VisualStudioCode.installer.yaml) | `49FA5278BBDEA1FE7175C8446967CCB940CC7A27262CE687C93F3C008245D1A4` |
| [Modrinth 0.23.1](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Modrinth/ModrinthApp/0.23.1/Modrinth.ModrinthApp.installer.yaml) | `0C4A2054A5E05F4226C9DEAD2EE212303795D7363833C78154D6DD49638CCC2E` |

Hash del testo salvato, **non** attestazione/hash di un payload scaricato. Copie
locali escluse dal commit in `_output/coverage-ab-20261109/evidence/`.
I valori comuni `InstallerType`, `Scope`, `UpgradeBehavior` si ereditano nelle
voci; scope/switch locali distinguono le varianti. Nessuna inferenza dal solo nome.

`WinGetInstalledScopeService` correla DisplayName/DisplayVersion nel registro:
non attesta l'ID e restituisce vuoto per corrispondenza doppia/assente. `ToPlanItem`
non trasporta scope, architettura, tipo, hash/timestamp del manifest o account
attestato. Il runner vincola ID/versione/origine e M6-B congela il piano e raccoglie
consenso autorevole, ma **non riverifica scope o freschezza del manifest**.
Il batch software non impone elevazione; l'installer può richiederla. L'elevazione
non dimostra l'ambito né l'identità della copia. Servirebbe un contratto end-to-end
verificabile prima di recuperare i quattro multi-scope; aggiungere solo `--scope`
o accettare una variante per architettura/tipo nel parser non risolve il problema.

### Correzioni circoscritte

1. `SetAllSelected(true)` preserva le scelte individuali con rischio già fatte;
   non aggiunge un elemento rischioso non scelto. Deseleziona tutti azzera tutto.
   Conteggi/stato vengono ricalcolati anche quando `CanInstall` cambia.
2. `ParseInstallerScope` confronta tutte le dichiarazioni esplicite: un valore
   uniforme resta quel valore; valori diversi/non validi diventano `ambiguous`.
   Il consumer blocca l'ambiguità anche con scope locale ignoto. Non seleziona
   un installer per architettura/tipo; l'ordine delle voci non concede permessi.
   Scope completamente assente resta ignoto secondo il comportamento precedente;
   non è una prova di compatibilità, e non riabilita elementi già bloccati.
3. Cinque contatori aggregati nel sistema diagnostico già esistente registrano
   selezione iniziale, idoneità collettiva, rischio individuale, blocchi e metadati
   ignoti per righe effettivamente aggiunte. Nessun nome/ID o nuovo inventario.
   Il contatore dei metadati ignoti può sovrapporsi a rischio/blocco: non va
   sommato alle altre classi come se fosse una partizione disgiunta.

La correzione scope è conservativa: può bloccare anche elementi prima ammessi
perché la **prima** voce coincideva con lo scope locale. Mancano i manifest degli
altri 42 per quantificare l'effetto. **31/42/44/51 non sono una previsione della
prossima scansione.** La fixture controllata riproduce questi numeri; aggiungendo
individualmente EA/Miniconda e poi Seleziona tutti si conservano 43/44 selezioni,
senza rimuovere il consenso richiesto. Nessun nuovo pacchetto reale sbloccato.

### Verifica del nuovo intervento B

- RED osservato prima dei fix: perdita della scelta individuale rischiosa e
  contatori mancanti; parser mixed/invalid rappresentato come scope scelto.
- `--update-eligibility`: PASS, fixture 31/42/51, 44 ammissibili, selezione
  individuale/rischio, bulk, deselezione, notifiche e nuovo blocco, contatori
  JSON con deduplica e nessuna identità persistita.
- `--winget-hardening`: PASS, forme ridotte dei sette manifest, ordine invertito,
  scope ereditato/ripetuto/assente/non valido, architettura/tipo non vincolabili,
  nessuna riabilitazione di un elemento rifiutato; target ID/versione/origine.
- `--install-safety`: PASS M5, preflight e risultati WinGet/WUA simulati,
  nessun fallback a identità/origine diversa, nessun effetto dopo consenso negato.
  Il messaggio storico `OBSERVED deferred limit` riguarda il vecchio test di
  mutazione del file senza eseguire il runner; non è un nuovo FAIL. Il confine
  autorevole successivo è verificato separatamente con M6-B.
- `--runner-consent`: **24 PASS, 0 FAIL**, snapshot immutabile, nuovo consenso
  dopo ricreazione/retry, annullamento, validazione tuple, driver/elevazione,
  rifiuto o errore prima degli effetti. I 15 FAIL RED storici non rieseguiti.
- Build Release applicativa e harness: zero errori/avvisi. Nessun restore,
  nuovo tool/dipendenza, installer o upgrade eseguito.
- Revisore indipendente: nessun rilievo bloccante B; verificata monotonia del
  rifiuto e conservazione del consenso. Evidenziato l'effetto non quantificabile
  sugli altri multi-scope, riportato sopra.

Log locali esclusi dai commit: `_output/coverage-ab-20261109/`,
`_output/selection-20261109/`, `_output/selection-red-test.txt` e
`_output/status-flyout-20261109/`. Comandi riproducibili con build isolata:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '_output/coverage-ab-20261109/dotnet-home'
$output = Join-Path (Get-Location) '_output/coverage-ab-20261109/smoke/'
dotnet build Tests/UpdateCenter.SmokeTests -c Release --no-restore -m:1 -p:UseSharedCompilation=false "-p:OutDir=$output"
& (Join-Path $output 'UpdateCenter.SmokeTests.exe') --update-eligibility
& (Join-Path $output 'UpdateCenter.SmokeTests.exe') --winget-hardening
& (Join-Path $output 'UpdateCenter.SmokeTests.exe') --install-safety
& (Join-Path $output 'UpdateCenter.SmokeTests.exe') --runner-consent
git diff --check
```

M8 finale e installazioni reali restano fuori incarico. Non sono validate nuove
garanzie di freschezza/account/scope a runtime: proprio questa mancanza impedisce
lo sblocco. Identità/versione applicativa 1.1.5 e contratti di rilascio invariati.

## Interpretazione dei risultati dopo B

Le osservazioni successive hanno confermato che rilevati, preselezionati,
selezionabili collettivamente e idonei individualmente sono conteggi distinti.
I metadati ignoti possono sovrapporsi a tali categorie e non dimostrano da soli
un blocco. Variazioni del catalogo o aggiornamenti autonomi impediscono di
attribuire una differenza di conteggio al parser senza una fotografia nominativa.

I blocchi per scope richiedono una copia locale univoca e metadati vincolabili:
non si devono cambiare scope, account o tecnologia per aumentare il conteggio.
Gli inventari nominativi e le osservazioni della sessione locale sono conservati
nel backup e negli artefatti diagnostici esclusi dal checkpoint. La successiva
raccolta R3 deve mantenere timestamp, versione della build e motivazioni per riga.
