# B2 — selezione installer e recupero scope WinGet

9 novembre 2026. Base: `1f9c4c304a72b1b2934da84391a058f85718a68f`, branch
`feature/engine-m6-consent-integrity`. Applicazione **1.1.5**, .NET 10/WPF invariati.
Questo rapporto integra [il checkpoint di copertura](UPDATE-COVERAGE-NINE-ITEMS.md);
non sostituisce le sue osservazioni storiche o le modifiche A ancora non committate.

## Risultato

**Cinque pacchetti recuperati** nelle fixture esatte e nella nuova rilevazione:
Cursor, LM Studio, Chrome EXE, Inno Setup 7 e VS Code. Sono ammessi al piano con
vincoli verificati nel runner, non dichiarati aggiornati con successo.
Nessun installer, disinstallazione, UAC o download di payload eseguito.

Miniconda user resta bloccata per un motivo sostanziale: il suo installer dichiara
`deny`. L'ammissione storica con consenso specifico mescolava quel comportamento
con `uninstallPrevious` della variante machine e non era corretta per la copia HKCU.

## Diagnosi differenziale

Il confronto eseguito su **identici tredici manifest** riproduce i predicati di
scope pre-B e B. I sei nuovi nomi hanno prima dichiarazione `user` e dichiarazioni
effettive user/machine: pre-B ammetteva i cinque scope locali user e Python con
scope ignoto, B li blocca come ambigui. Per Git/Chrome/VS Code il primo scope user
era già incompatibile con la copia machine; Inno ha ordine inverso. I tre
mono-scope restano incompatibili in entrambi i parser.

Questa è causalità su fixture identiche. Le versioni dei sei nomi non erano
persistite nel rapporto della scansione precedente: sono state ottenute con un
**nuovo inventario in lettura**. Solo Miniconda disponeva anche della medesima
tupla storica documentata. Non si deduce retroattivamente che gli altri cinque
fossero selezionati nella vecchia scansione.

Altri difetti riprodotti prima dei fix: default `install` che nasconde un override
`deny`/rimozione; preferenza x64 che nasconde un candidato x86 compatibile;
comportamento ignoto coperto da un fratello noto; due copie con stessa versione
considerate una verifica riuscita; duplicati locali nascosti da due booleani.
I componenti ARP `SystemComponent` DWORD nonzero sono esclusi come fa WinGet:
le componenti MSI nascoste di EA/.NET non sono copie aggiuntive.

## Cosa garantisce il client

Versione nativa verificata: **v1.29.290**. Sorgente Microsoft fissato al commit
[`4e875302f931552ca1314c38b14ed66814d6206b`](https://github.com/microsoft/winget-cli/tree/4e875302f931552ca1314c38b14ed66814d6206b).

- `--scope` seleziona HKCU/HKLM **e** filtra lo scope degli installer tradizionali;
  non è soltanto un suggerimento. Scope ignoto non supera il requisito esplicito.
  [WorkflowBase](https://github.com/microsoft/winget-cli/blob/4e875302f931552ca1314c38b14ed66814d6206b/src/AppInstallerCLICore/Workflows/WorkflowBase.cpp#L547-L560),
  [ManifestComparator](https://github.com/microsoft/winget-cli/blob/4e875302f931552ca1314c38b14ed66814d6206b/src/AppInstallerCommonCore/Manifest/ManifestComparator.cpp#L333-L503).
- `--installer-type` vincola il tipo del candidato; per B2 sono ammesse soltanto
  tecnologie tradizionali, non archivi/font/portable. `--architecture`, quando
  disponibile da metadati installati nativi, impone il valore esatto e non include
  neutral. Il segmento X86/X64 dell'identificatore ARP è la vista del registro,
  **non** prova dell'architettura eseguibile: Chrome ne è un esempio reale.
  [Filtri](https://github.com/microsoft/winget-cli/blob/4e875302f931552ca1314c38b14ed66814d6206b/src/AppInstallerCommonCore/Manifest/ManifestComparator.cpp#L218-L279),
  [architettura](https://github.com/microsoft/winget-cli/blob/4e875302f931552ca1314c38b14ed66814d6206b/src/AppInstallerCommonCore/Architecture.cpp#L273-L284).
- `list --details` espone l'identità locale. Le version key mantengono l'identità
  ARP anche con versioni uguali; l'inventario emette tutte le copie. B2 non deduplica
  quelle righe per autorizzare un aggiornamento.
  [CompositeSource](https://github.com/microsoft/winget-cli/blob/4e875302f931552ca1314c38b14ed66814d6206b/src/AppInstallerRepositoryCore/CompositeSource.cpp#L565-L629),
  [inventario](https://github.com/microsoft/winget-cli/blob/4e875302f931552ca1314c38b14ed66814d6206b/src/AppInstallerCLICore/Workflows/WorkflowBase.cpp#L1166-L1177).
- `--version` fissa la versione, non l'hash dei byte eseguiti. La CLI rilegge
  l'origine e verifica il payload secondo il proprio catalogo. L'hash B2 è una
  **testimonianza di freschezza** tra scansione e runner, non un pin del payload.
  Una modifica successiva al controllo resta nel modello di fiducia del provider
  già dichiarato da M5/M6-B.
  [UpdateFlow](https://github.com/microsoft/winget-cli/blob/4e875302f931552ca1314c38b14ed66814d6206b/src/AppInstallerCLICore/Workflows/UpdateFlow.cpp#L347-L366).

Non si impone una migrazione COM: l'API pubblica dell'installer non espone
hash/URL/UpgradeBehavior e il repository non dispone della relativa proiezione.
`LocalManifestFiles` risulta disabilitato e non è stato abilitato. La mancanza di
un pin assoluto del manifest non è stata usata per bloccare indiscriminatamente
scope e ricette che il percorso esistente può effettivamente vincolare.

## Contratto implementato

`WinGetInstallerMetadata` legge un sottoinsieme esplicito delle mappe YAML, con
ereditarietà/override e separazione delle voci. Forme non supportate, merge,
chiavi complesse e escape non interpretati non autorizzano il recupero. Vengono
considerate tutte le architetture compatibili, oppure quella nativa esplicitamente
vincolata; `deny` non può diventare consenso per effetto di un altro installer.

Il recupero richiede un'indicazione locale non ambigua e una risposta WinGet
dettagliata univoca con stesso nome/versione/scope. Non si inventa l'identità
WinGet dal solo DisplayName. `WinGetScopeBinding` trasporta:

- scope, identificatore locale ARP e SID dell'account;
- versione del client studiata e hash dei metadati esaminati;
- tipo installer, architettura solo quando attestata da WinGet e classe di rischio.

Il binding segue `UpdateItem → ToPlanItem → RunnerPlanSnapshot → consenso →
comando → verifica`. Il record immutabile, lo schema JSON obbligatorio dei suoi
campi, la presentazione e il confronto dei risultati mantengono gli stessi dati.
Nessun campo è un token di autorizzazione: M6-B richiede ancora nuovo consenso.

Prima di ogni esecuzione silenziosa/interattiva si ricontrollano client, SID,
metadati, copia locale e versione installata; i flag scope/tipo/architettura
applicabili accompagnano ID/versione/origine. Cambiamenti e duplicati fermano il
comando. Rimuovere il binding non abilita il vecchio percorso per un manifest
multi-scope. I nomi dell'origine sono confrontati senza distinzione maiuscole,
come nel client Microsoft. Nessun fallback `install` per un upgrade vincolato.

La verifica finale richiede la stessa identità locale, scope/account e versione
raggiunta. Un cambio della chiave ARP non viene certificato come successo. Quando
l'architettura iniziale era ignota, metadati nuovi possono comparire dopo l'upgrade:
non erano un vincolo promesso. L'applicabilità e l'esecuzione reale restano al
provider; non si promette la conservazione di un'architettura iniziale ignota.

Il recupero automatico dei processi resta **manuale per gli elementi vincolati**:
il vecchio provider cerca nomi normalizzati attraverso più scope. Non viene
invocato per questi elementi; un retry interattivo mantiene consenso e vincoli.

Per l'origine ufficiale, metadati mancanti/non interpretabili possono ora fermare
anche un piano legacy prima dell'effetto: consentirli permetterebbe di eludere il
vincolo rimuovendo il binding. Questa disponibilità dipende dalla lettura dei
metadati; non è una garanzia di disponibilità offline.

## Matrice delle tredici versioni esatte

Origine osservata: `winget`. RECOVERED indica ammissione verificata al percorso
vincolato, osservata anche nella rilevazione finale; **zero upgrade reali provati**.

| Pacchetto | Installata → disponibile | Esito | Motivazione |
| --- | --- | --- | --- |
| `Anysphere.Cursor` | 3.25.6 → 3.27.12 | **RECOVERED** | user; payload User/System distinti; inno; install; architettura lasciata alla compatibilità nativa perché non attestata |
| `AnywhereLabs.DSHDesktop` | 2.0.9 → 2.0.22-next | **BLOCKED** | user/nullsoft; il ProductCode della nuova offerta è diverso dalla chiave locale; continuità non attestata |
| `ElementLabs.LMStudio` | 0.4.23+1 → 0.4.28+2 | **RECOVERED** | user/x64/nullsoft; `/currentuser`, ProductCode coerente, install |
| `Anaconda.Miniconda3` | py312_24.5.0-0 → py314_26.9.4-0 | **BLOCKED** | copia user; variante user `deny`, non autorizzabile con il rischio della variante machine |
| `Opera.OperaGX` | 138.0.6044.65 → 139.0.6053.42 | **BLOCKED** | due chiavi HKCU e due copie omonime; nessuna identità univoca |
| `Python.Python.3.13` | 3.13.15 → 3.13.17 | **BLOCKED** | versione ARP bundle 3.13.15150.0, componenti HKLM e bundle HKCU; correlazione locale stretta non soddisfatta, ProductCode target differente |
| `Git.Git` | 2.55.0.3 → 2.56.0.4 | **BLOCKED** | ricette user/machine con stesso payload e argomenti, elevatesSelf; il solo cambio etichetta non dimostra lo scope fisico |
| `Google.Chrome.EXE` | 156.0.8078.80 → 157.0.8103.39 | **RECOVERED** | machine/x64/exe; `--system-level`, identità locale coerente |
| `JRSoftware.InnoSetup.7` | 7.0.2 → 7.1.1 | **RECOVERED** | user/inno; `/CURRENTUSER`; comportamento non dichiarato, quindi non preselezionato |
| `Microsoft.VisualStudioCode` | 1.141.0 → 1.143.1 | **RECOVERED** | machine/x64/inno; payload System distinto; comportamento non dichiarato, quindi non preselezionato |
| `Perplexity.Comet` | 140.1.7339.21342 → 156.0.8078.132 | **BLOCKED** | copia user, offerta solo machine |
| `HypixelStudios.Hytale` | 2026.01.13-e6eb932 → 2026.10.26-7b63bde | **BLOCKED** | copia machine, offerta solo user |
| `Modrinth.ModrinthApp` | 0.10.3 → 0.23.1 | **BLOCKED** | copia machine, offerta solo user |

EA app resta **CONDITIONAL** nella nuova rilevazione: scelta individuale,
conferma specifica e consenso autorevole. Nessuna eccezione hardcoded per ID è
stata aggiunta al codice. Gli ID delle tabelle compaiono soltanto nelle fixture.

## Conteggi e limiti del confronto

| Evidenza | Rilevati | Preselezionati | Collettivi | Individuali | Bloccati | Consenso specifico |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Scansione post-B documentata | 52 | 26 | 38 | 39 | 13 | 1 |
| Fixture B2 sulla stessa composizione controllata | 52 | 29 | 43 | 44 | 8 | 1 |
| Nuova rilevazione WinGet finale | 51 | 24 | 40 | 41 | 10 | 1 |

La nuova rilevazione usa il provider applicativo reale con `IncludeUnknownVersions=false`
(come le impostazioni di produzione lette), in una root GUID temporanea. Non ha
aperto GUI o rifatto test DPI/accessibilità. WUA non è stato interrogato nuovamente;
lo zero WUA della baseline resta un fatto storico. Le impostazioni e la finestra
originale non sono state sostituite.

I cinque recuperi sono osservati nominativamente. I due blocchi aggiuntivi esterni
alla matrice sono `OpenMedia.4KVideoDownloader` e `Python.Launcher`: l'inventario
restituisce più righe per lo stesso ID. Non sono contati come una copia unica.
Il totale 51 e le classi di preselezione provengono da un inventario successivo:
non attribuiamo tutta la differenza rispetto a 52/26 al parser. La nuova
preselezione non è aumentata; l'ammissibilità individuale osservata è 41 anziché 39.

Un primo passaggio intermedio aveva 13 blocchi, comprendendo erroneamente i
componenti SystemComponent di EA e dei runtime 8/9. Il difetto è stato riprodotto
in RED e corretto prima della seconda rilevazione, usata nella tabella finale.

## Verifica e revisione

Build Release soluzione, applicazione e harness: **0 errori, 0 avvisi**. Harness console,
nessun `dotnet test` usato per simulare esecuzioni. Esiti finali:

| Requirement | Evidence |
| --- | --- |
| Confronto identico dei tredici manifest | `ExactManifestDifferential` |
| Miniconda e deny/rimozione | `MinicondaDenyCannotBecomeConsent`, `DenyReasonSurvivesScopeSummary` |
| Ereditarietà, override, tipo/architetture compatibili e ignoti | `InheritedBehaviorHonorsOverrides`, `CompatibleArchitecturesAreNotPreferences`, `UnknownVariantIsNotSafe`, `ScopedRecipeRecovery` |
| YAML non supportato, chiavi/escape/merge | `UnsupportedShapeCannotAttestSafety`, `UnsupportedYamlCannotHideRisk`, `UnsupportedManifestDoesNotDowngrade` |
| HKCU/HKLM, copie duplicate e componenti nascosti | `LocalCopiesStayAmbiguous`, `InstalledInventoryKeepsAmbiguity`, `DuplicateCopiesAreNotVerified` |
| Manifest/offerta/ID/versione/origine/SID/client mutati | `ScopedExecutionAndDrift`; zero comandi installer per i casi negativi |
| Vincoli nel consenso, schema incompleto, file mutato e rifiuto | `ScopedBindingConsent`; snapshot immutabile e nessun effetto dopo rifiuto |
| Collettivo/individuale e rischio | fixture B2 in `ScopedRecipeRecovery`, `--update-eligibility` |
| Verifica copia e architettura inizialmente ignota | `ScopedExecutionAndDrift`, `PreviouslyUnknownArchitectureCanBeReportedAfterUpgrade` |
| Regressioni M5/M6-B e risultato | `--install-safety` PASS; `--runner-consent` **25 PASS, 0 FAIL**; `--release-audit` PASS |

Sono stati osservati RED reali prima dei fix, compresi i rilievi della revisione.
Il messaggio storico M5 `OBSERVED deferred limit` resta documentato; M6-B verifica
separatamente il confine autorevole. I 15 FAIL RED storici intenzionali non sono
stati rimossi o reinterpretati. La revisione indipendente di sicurezza ha chiuso
i rilievi su casing dell'origine, forme YAML e recupero dei processi; nessun
rilievo P1/P2 aperto. M6-B resta WINDOWS VALIDATION PENDING per le prove reali.

Evidenze locali, escluse dal commit: `_output/b2-20261109/`. Manifest ufficiali
copiati come fixture in `Tests/UpdateCenter.SmokeTests/Fixtures/WinGetB2/`; URL e
SHA256 acquisiti sono nel relativo `README.md` e in `evidence/manifests.json`. Rilevazione finale:
`inventory-20261109-193737.json`; log `native-inventory-final.txt` e `final-*.txt`.
Le fixture non contengono payload. Le skill dotnet richieste sono state consultate
in sola lettura; nessuna installazione di tool, pacchetto, SDK o skill.

## Riproduzione mirata

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '_output/b2-20261109/dotnet-home'
$out = Join-Path (Get-Location) '_output/b2-20261109/smoke/'
dotnet build Tests/UpdateCenter.SmokeTests -c Release --no-restore -m:1 -p:UseSharedCompilation=false "-p:OutDir=$out"
& (Join-Path $out 'UpdateCenter.SmokeTests.exe') --winget-hardening
& (Join-Path $out 'UpdateCenter.SmokeTests.exe') --update-eligibility
& (Join-Path $out 'UpdateCenter.SmokeTests.exe') --install-safety
& (Join-Path $out 'UpdateCenter.SmokeTests.exe') --runner-consent
& (Join-Path $out 'UpdateCenter.SmokeTests.exe') --release-audit
# Facoltativo, sola rilevazione nativa WinGet in root GUID:
& (Join-Path $out 'UpdateCenter.SmokeTests.exe') --winget-hardening --b2-read-only-inventory
git diff --check
```

## Separazione Git

Le modifiche A restano fuori commit. `Program.cs` condivide solo una correzione B2
all'aspettativa storica x64/x86, separata dal dispatch A: staging del solo hunk B2.
Il rapporto storico riceve soltanto il rimando a questo documento; la sua sezione
di scansione preesistente resta non committata. Gli altri sei file A mantengono
gli hash iniziali. Nessun push, merge, rebase, tag, release, rebranding o modifica
alla versione. Il precedente stash non è stato letto né applicato.
