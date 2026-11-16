# UpdateCenter 2.0.0 — M8-A Release Readiness

9 novembre 2026, Europe/Rome. Branch verificato: `feature/engine-m6-consent-integrity`.
HEAD: `85a331d08a9ca1830593cae60c5b34b155f5989d`. Working tree inizialmente pulito.

**Release non ancora pronta. M6-B resta WINDOWS VALIDATION PENDING.**
Aggiornamento M8-B: per decisione dell'utente la validazione procede sul Windows host,
con provider simulati. La virtualizzazione non è un prerequisito e la sua assenza
non è un difetto applicativo. La matrice e la procedura sotto recepiscono questa scelta;
le evidenze M8-A restano conservate. Nuovi risultati in [M8-B](M8-B-HOST-VALIDATION.md).

L'attività M8-A aveva aggiunto soltanto il presente rapporto e le evidenze locali della
verifica applicativa. Nessuna implementazione, build, installazione, riparazione,
elevazione UAC, modifica di versione, VM, pacchetto finale o operazione Git mutativa.
Le scansioni possono scrivere i normali log e risultati dell'applicazione: «sola
lettura» si riferisce a software, driver e configurazione del sistema.

## Compatibilità attuale

Aggiornamento dell’11 novembre 2026 per la linea 2.0.0, dalla baseline `cb13ef8`.
Il blocco della riproducibilità del Portable è chiuso. Due publish Release con
SDK `10.0.404` in checkout isolati hanno prodotto gli stessi cinque file byte per byte.

Il requisito tecnico comune è Windows x64, build 17763 o successiva. Le versioni
e le edizioni effettivamente supportate da Microsoft dipendono dal ciclo di vita
di Windows e dalla matrice .NET 10. [Matrice .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) · [Ciclo di vita Windows 10](https://learn.microsoft.com/en-us/windows/release-health/release-information) · [Ciclo di vita Windows 11](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)

| Piattaforma x64 | Prerequisiti documentati | Evidenza UpdateCenter |
|---|---|---|
| Windows 11 Pro Insider Beta, build 26320 | Canale di anteprima: compatibilità da validare, non supporto stabile certificato. Setup e self-update ammettono le edizioni desktop previste dalla policy dalla build 26200 in avanti, senza whitelist delle singole flight; invariati x64, client ed eccezioni LTSC/IoT. | Avvio e scansione osservati su questo host. Consenso desktop provato con effetti simulati. Avvio controllato del Portable self-contained verificato. Restano da provare il Setup corretto e la migrazione completa con la nuova RC. |
| Windows 11 stabile | Release in servizio elencate nella matrice Microsoft di .NET 10, per esempio 25H2 (build 26200). App Installer necessario per WinGet. | Da validare sul candidato, partendo da una release stabile x64 in servizio. |
| Windows 10 Enterprise LTSC 2019 (1809, build 17763), Enterprise LTSC 2021 e IoT Enterprise LTSC 2021 (21H2, build 19044) | Edizioni in servizio elencate da Microsoft per .NET 10. La disponibilità di App Installer va verificata per edizione e PC. | Da validare per le edizioni che saranno dichiarate. |
| Windows 10 Home/Pro, altre edizioni fuori dalla matrice .NET 10, build precedenti a 17763 | Il supporto documentato richiede sia la build minima sia un’edizione in servizio nella matrice .NET 10. | Fuori dalla matrice di supporto dichiarabile per la 2.0.0. |

Il Portable e il payload del Setup includono .NET 10 Desktop. Le funzioni di
aggiornamento automatico WinGet richiedono App Installer e uno dei client
`v1.29.290` o `v1.30.300`. La ricerca WUA già osservata ha restituito zero offerte
driver ammesse. UAC e runner elevato, esito di installazione driver WUA, ciclo
Setup/Portable 2.0.0 e matrice DPI completa restano da validare. [Requisiti WinGet](https://learn.microsoft.com/en-us/windows/package-manager/winget/) · [Evidenze e limiti M8-B](M8-B-HOST-VALIDATION.md#cosa-resta-non-verificato)

### Prove native minime ancora necessarie

1. **Piattaforme.** Avviare il candidato e completare una sola scansione in un profilo di prova su Windows 11 stabile x64 e sulla più vecchia edizione LTSC che si intende dichiarare. Per ogni ulteriore edizione dichiarata ripetere solo avvio e riscontro delle fonti. Accorpare, dove possibile, l’avvio su un PC già privo di runtime/SDK .NET 10 e la verifica del comportamento in assenza di App Installer. Usare un client WinGet ammesso dove si dichiara il percorso software.
2. **UAC e WUA.** Sullo stesso host autorizzato scegliere una sola offerta driver applicabile e non firmware. Verificare il rifiuto UAC e l’annullamento in attesa di consenso senza dispatch. Nel caso approvato verificare che il token del runner sia elevato e registrare l’identità esatta dell’offerta (UpdateID, revisione, servizio e dispositivo). Confrontare esito WUA e nuova lettura del driver, annotando l’eventuale riavvio richiesto. Verificare il rifiuto in sessione non interattiva se il percorso pubblicato la prevede. Se manca un’offerta idonea, la prova resta aperta.
3. **WinGet.** Riutilizzare gli esiti reali documentati per i percorsi già esercitati. Per un percorso dichiarato privo di un esito completo, inclusa l’eventuale modalità interattiva, usare un solo pacchetto non critico con ID, versione e ambito esatti. Verificare dispatch dopo consenso, esito, nuova lettura del target e cronologia.
4. **Setup e Portable.** Usare il candidato 2.0.0 firmato e con checksum verificati. Provare un’installazione Setup per utente e una transizione da 1.1.5 per ciascuno dei due canali, in profili o cartelle di prova. Verificare avvio, versione, impostazioni, cronologia e disinstallazione Setup con conservazione dei dati. Esercitare l’aggiornamento offerto all’utente, compreso il passaggio manuale quando richiesto dalla politica del firmatario. Per i casi ancora scoperti, verificare il rifiuto dell’aggiornamento in-app e il recupero da un errore controllato di sostituzione, con versione precedente avviabile e dati intatti. [Firma e aggiornamento dall’app](CODE-SIGNING.md#aggiornamento-dallapp)

Le prove con effetti richiedono autorizzazione sui PC fisici, sugli artefatti e
sui target esatti. Gli esiti negativi già coperti da fixture restano utilizzabili.

## Matrice di readiness

Questa matrice conserva le decisioni e le evidenze storiche M8-A/B.

PASS significa verificato nel perimetro indicato; PENDING significa prova o decisione
ancora necessaria; BLOCKED significa che manca un prerequisito concreto per eseguirla.
La severità riguarda il rischio di distribuire senza chiudere la riga.

| Requisito / stato | Evidenza già disponibile | Verifica ancora necessaria | Rischio | Bloccante 2.0.0 | Procedura minima |
|---|---|---|---|---|---|
| **PASS — regressioni offline M1–M7** | Rapporti conclusivi; M6-B 24 gruppi, regressioni M5 16 gruppi; M7 otto casi HEAD e build Release senza errori/avvisi. | Nessuna ripetizione generale richiesta. Non attestano installazioni reali. | Basso nel perimetro provato | No | Conservare log e contratti; ripetere soltanto i controlli interessati da eventuali modifiche successive. |
| **PASS — uso locale senza installare, su questo host** | Avvio della build M7, annullamento, scansione completa, risultati, filtri, cronologia e navigazione osservati il 9/11; dettagli sotto. | Questa prova non copre altri Windows, consenso runner o installazioni. | Basso nel perimetro provato | No | Nessun altro giro identico necessario. |
| **PASS — dialogo reale M6-B con effetti simulati** | Host separato M8-B: stessi sorgenti del dialogo, snapshot e runner; provider nativi esclusi e bloccati. Assenso/rifiuto e confronto dei parametri provati sul desktop. | Prova personale dell'utente con la build consegnata; dettagli e limiti nel rapporto M8-B. | Basso nel perimetro simulato | No | Avviare solo `UpdateCenter.ConsentTestHost.exe`, scegliere il caso e usare il dialogo reale. |
| **PENDING — integrazione Windows M6-B completa** | 24 gruppi offline; prove desktop M8-B senza effetti reali. | UAC/token elevato, coordinatore/figlio reali, sessione disconnessa e matrice DPI completa non attestati dal test host. | Alto | **Sì** | Conservare le prove offline; registrare i limiti incompatibili con l'incarico senza modificare il sistema né equiparare il token simulato a UAC. |
| **BLOCKED nel presente incarico — aggiornamento software reale** | Enumerazione WinGet reale riuscita; identità/versione/origine, errori e verifica finale coperti offline da M5/M6-B. | Catena reale selezione → consenso → provider → versione installata → UI/cronologia, inclusa modalità interattiva se distribuita. | Alto | **Sì** | Richiedere un incarico separato con pacchetto esatto, effetti e piano di recupero approvati. Nessuna installazione autorizzata ora. |
| **BLOCKED nel presente incarico — aggiornamento driver reale WUA** | Ricerca reale riuscita, ma zero offerte ammesse; protezioni e risultati per elemento provati offline. | Download/installazione di un'offerta esatta, risultati WUA e verifica fresca dopo l'operazione. Zero offerte non prova questo percorso. | Alto | **Sì**, per distribuire la funzione di aggiornamento driver | Solo dopo un piano separato autorizzato per un'offerta non firmware realmente applicabile. Non forzare hardware o cataloghi. |
| **PENDING — Windows dichiarati supportati** | Target tecnico x64 build 17763+; avvio reale su Windows 11 Pro **Insider Preview**, build 26320.9876. | Windows stabile corrente e versione/edizione più vecchia che si intende supportare; coerenza con supporto .NET/Windows. | Alto | **Sì**, per l'attuale promessa generica Windows 10 1809+ | Definire la matrice effettiva; provare su ambienti già disponibili i suoi estremi. Correggere eventualmente la dichiarazione di compatibilità, senza dedurla dal solo TFM. |
| **PENDING — esecuzione senza runtime/SDK preinstallato** | Output M7 self-contained: runtime .NET/WindowsDesktop 10.0.14 e DLL native presenti; avvio riuscito. L'host ha già .NET installato. | Avvio del futuro Portable su Windows compatibile senza runtime preinstallato; comportamento se WinGet manca. | Alto | **Sì** | L'host attuale non dimostra questo caso. Usare in futuro un PC fisico già disponibile con i prerequisiti richiesti, senza disinstallare dipendenze dall'host. |
| **PENDING — candidato distribuibile coerente** | Build M7 1.1.5, vecchio Portable 1.1.5 e Setup 1.1.4: sono artefatti differenti, non un candidato 2.0.0. Script di produzione già presenti. | Nome deciso separatamente, versione, asset, checksum, provenienza dal commit e istruzioni coerenti; perimetro di eventuali componenti LAN. | Alto | **Sì** | Solo nell'incarico successivo di packaging: produrre in directory nuove, registrare commit/hash, verificare nomi/versioni e contenuto; non riutilizzare il vecchio Setup. |
| **PENDING — ciclo di distribuzione e aggiornamento dell'app** | Script Setup/Portable e aggiornamento interno esistenti; checksum del vecchio Setup coerente. | Installazione, upgrade da versione precedente, conservazione impostazioni/cronologia, avvio e disinstallazione del candidato; sostituzione Portable se offerta. | Alto | **Sì**, per i canali distribuiti | Prova autorizzata con i futuri artefatti esatti e backup/piano di recupero, incluso rifiuto/errore dell'aggiornamento interno. Fuori da M8-B. |
| **PENDING — identità del distributore** | EXE M7, Portable e Setup esistenti risultano `NotSigned`; checksum non equivale a firma del publisher. | Scelta esplicita fra distribuzione non firmata dichiarata e firma; controllo delle segnalazioni Windows sul candidato. | Medio | No, se politica non firmata esplicita e canale/checksum verificati; sì se si promette un publisher verificato | Definire la politica prima della pubblicazione. Non creare certificati né cambiare trust dell'host. |
| **PASS — limiti noti accettati come tali** | Sette elementi bloccati per ambito, catalogo driver limitato, latenza COM WUA e annullamento differito documentati. | Comunicare questi limiti nelle note della release; nessun obbligo di ampliare copertura o ottimizzare ancora. | Medio, dichiarato | **No** | Conservare i blocchi e distinguere aggiornamenti rilevati, selezionabili e realmente installati. |

## Evidenze riutilizzate, senza rieseguire le suite

- [M1](ENGINE-M1-BASELINE.md): diagnostica e fasi della scansione;
  [M2](ENGINE-M2-DEDUP.md): equivalenza della deduplicazione;
  [M3](ENGINE-M3-DRIVER-COVERAGE.md): limiti della copertura driver;
  [M4](ENGINE-M4-WUA-COVERAGE.md): risultati parziali/errori e guardie WUA.
- [M5](ENGINE-M5-INSTALL-SAFETY.md): target esatto, errori, verifiche successive;
  [M6-B](ENGINE-M6-CONSENT-INTEGRITY.md): 24 gruppi offline e revisione conclusa;
  i **15 FAIL RED del contratto M6 originale restano intenzionali e invariati**.
- [M7](ENGINE-M7-RELIABILITY.md): DataGrid e cancellazione/processi già verificati;
  tre scansioni storiche 28,204–44,058 s, di cui WUA 20,565–37,193 s.
  Nessuna accelerazione nuova rivendicata e nessun nuovo benchmark.
  Letti anche i log finali pertinenti, incluso
  `_output/engine-m7/checkpoint/app-build.log` e `head-tests.log`.
- [UX](SCAN-PROGRESS-UX.md) e [copertura](UPDATE-COVERAGE-NINE-ITEMS.md):
  prove manuali già accettate e distinzione selezione/installabilità.
  Comet, Git, Chrome EXE, Hytale, Inno Setup 7, VS Code e Modrinth restano i sette
  blocchi noti per ambito; non sono regressioni né sette installazioni fallite.
  I precedenti 44 elementi ammissibili su 51 non equivalgono a 44 upgrade dimostrati.

## Nuova verifica applicativa Windows, senza installazioni

Ambiente: Windows 11 Pro Insider Preview x64 `10.0.26320`, UBR `9876`, sessione
interattiva e token non amministrativo. Finestra 1240×790, lingua italiana.
WinGet rilevato dall'app: `v1.29.290`. Nessun cambio DPI, lingua o impostazioni.

Eseguibile verificato e avviato direttamente:
`_output/engine-m7/checkpoint/app/UpdateCenter.exe`, PID osservato `41020`.
Il primo tentativo con il launcher desktop aveva aperto la copia già installata
in `%LOCALAPPDATA%\Programs\UpdateCenter`: quel tentativo e la sua scansione
automatica sono esclusi dall'evidenza del checkpoint. L'avvio diretto della build
M7 ha poi aperto correttamente la finestra; non è stato riscontrato un difetto di avvio.

| Percorso | Esito osservato |
|---|---|
| Avvio / annullamento | Scansione automatica secondo le impostazioni esistenti; Annulla durante ricerca driver → «Annullamento in corso» → «Scansione annullata», senza falso 100%; nuova scansione possibile. |
| Scansione completa successiva | Risultati pubblicati e Home al 100%, ultima scansione 09:36: **51 risultati = 49 software + 2 runtime, 0 driver**. Tre ricerche WUA complete con zero offerte ammesse nel log. |
| Filtri | `MuseHub` → una riga con versioni 2.8.2.2197 / 3.5.0.2378; testo vuoto → 51. Categoria Driver → zero e stato vuoto leggibile; tasto Home sulla categoria → Tutti e 51. |
| Reattività / navigazione | Cronologia accessibile durante la sequenza di annullamento; Hardware e Aggiornamenti accessibili durante la scansione. Aperte anche Driver e chipset, Impostazioni e Gestione rete; nessuna azione di riparazione, configurazione, collegamento o installazione. |
| Cronologia | 58 operazioni preesistenti visualizzate; nessuna cancellazione. Le vecchie righe «Riuscito» non certificano il motore M5/M6-B attuale. |

La selezione automatica attuale era **31**, senza premere Seleziona tutto né cambiare
checkbox: non è confrontabile con i precedenti 42 ottenuti con Seleziona tutto e non
è stata usata per riaprire la diagnosi dei sette blocchi. I sette collegamenti manuali
della pagina driver sono inoltre un conteggio diverso dai sette software bloccati.

Evidenze locali: `_output/m8-readiness/20261109/home-completed.jpg`,
`home-completed-ui.txt` e `application-log-excerpt.txt`; log applicativo originale
`%LOCALAPPDATA%\UpdateCenter\Logs\UpdateCenter-2026-11-09.log`, sequenza della build
M7 dalle 09:34:52 e scansione completa dalle 09:35:36 alle 09:36:20 circa.
Nessun dialogo runner M6-B aperto, nessun assenso a installazioni, nessun payload
installer richiesto intenzionalmente. La sola navigazione LAN non è una prova remota.

## Procedura minima M6-B, aggiornata alla scelta M8-B

1. Avviare sul Windows host, senza amministratore, l'EXE separato indicato nel
   [rapporto M8-B](M8-B-HOST-VALIDATION.md). I dati sono sintetici e la root è GUID.
2. Per rifiuto, Escape, X **e assenso** usare esclusivamente questo host: i provider
   reali non sono inclusi e gli ingressi nativi falliscono. Non usare la CLI di
   produzione per prove positive. Il dialogo e il dispatcher sono reali.
3. Confrontare snapshot e chiamate registrate: ID/versioni/origini/operazioni,
   silent mode, ripristino e rischio; per WUA anche UpdateID/revisione/servizio.
   Provare file modificato, retry/recupero/fallback e token cancellato durante l'attesa.
4. Distinguere rigorosamente privilegio driver simulato da token Windows elevato,
   cancellazione in-process da coordinamento fra processi, presentazione da
   installazione. I casi incompatibili con i vincoli restano non verificati.
5. L'assenso con provider reali resta un'attività diversa, con possibili EULA,
   download e installazioni: vietata in questo incarico, richiede un piano separato.

Conservare per caso binari/hash, ambiente, snapshot presentato, decisione umana,
parametri ricevuti dagli effetti simulati, log, schermate ed esito. Un PASS del dialogo
non è un PASS di installazione. Dichiarare `M6-B VERIFIED` soltanto dopo tutti i criteri
Windows pertinenti del rapporto M6; i casi non eseguibili restano espliciti.

## Installazioni realmente indispensabili: piano successivo, non eseguito

| Percorso | Minimo significativo e controlli |
|---|---|
| WinGet | Un pacchetto non critico già presente sul PC autorizzato, ID e versione esatti, origine verificata e stesso scope/account. Confrontare riepilogo e piano runner con comando effettivo, exit code, enumerazione finale e cronologia. Per la modalità interattiva distribuita serve anche l'installer reale, senza forzare errori sul sistema. |
| WUA | Una sola offerta driver non firmware realmente applicabile al PC autorizzato, identificata da UpdateID, revisione, servizio e dispositivo. Verificare selezione fresca, risultato per elemento/HRESULT, nuova verifica installazione e indicazione del riavvio. Nessun riavvio implicito; se indispensabile, richiederlo nel piano separato. |

Prima di ciascuna prova registrare nome/ID, versioni iniziale/finale, provider/origine,
scope o identità WUA, effetti attesi, EULA/ripristino, eventuale riavvio, rischi,
backup/piano di recupero e criterio di successo. **Nessun pacchetto o driver dell'host è autorizzato
come candidato.** Il target esatto richiede un futuro incarico esplicito, non viene
inventato ora. Se manca un'offerta WUA adatta, la distribuzione della relativa
funzione resta non qualificata; zero offerte non è un motivo per disattivare le guardie.

Errori di integrità, mismatch d'identità/versione, indisponibilità delle fonti e
tentativi duplicati restano coperti dai test simulati esistenti: non occorre provocare
installazioni corrotte. Retry/fallback e rifiuti vanno esercitati con interazione reale
ed effetti simulati. Non sono necessarie installazioni dei 51 risultati, dei sette
bloccati, firmware o pacchetti OEM aggiuntivi per dimostrare questi due percorsi.

## Artefatti e prerequisiti di distribuzione

| Artefatto esistente | Riscontro |
|---|---|
| `_output/engine-m7/checkpoint/app` | Build applicativa 1.1.5, EXE + DLL + runtime self-contained; non distribuire soltanto l'apphost EXE. Avvio effettivo verificato. |
| `dist/UpdateCenter.exe` | Portable single-file 1.1.5 preesistente; non qualificato come binario del checkpoint M7. |
| `installer-dist/UpdateCenter-Setup-v1.1.4.exe` | Setup precedente 1.1.4; hash coerente con il file `.sha256` esistente, ma versione e contenuto non sono il candidato 2.0.0. Non avviato. |

SHA-256 rilevati:

```text
M7 UpdateCenter.exe  17A21116286AD07E36D14F3C0D7687EBEC113492230D6618C6CB0B4162EA5C34
M7 UpdateCenter.dll  7FAE3D05033567181F7CE5C9BC0A8AC6294A06B412D96106219C4687A2F308EE
dist EXE             CB63F8F337913AD2DAB821194CDEC916A6E4927ACFD529187CF74AF9382AD78E
Setup 1.1.4          4EE337BF0643C2BE8751170C887EA067B768E231F280D4793E104B3E0CDB1A85
```

`UpdateCenter.csproj` usa `net10.0-windows10.0.17763.0`, RID `win-x64`; versione
ancora 1.1.5, invariata. `runtimeconfig.json` include .NET e WindowsDesktop 10.0.14;
presenti `coreclr.dll`, `hostfxr.dll`, `WindowsBase.dll`, `PresentationFramework.dll`
e il file `.deps.json`. Manifest `asInvoker` e DPI PerMonitorV2. `build.ps1` pubblica
self-contained single-file senza trimming; `build-installer.ps1` verifica la versione
EXE e richiede Inno Setup 6/7. ISCC 7 è presente sull'host, non è stato eseguito.
Il Setup usa privilegi minimi e destinazione per utente; i progetti Agent/SessionHelper
non sono inclusi automaticamente nel solo EXE desktop: l'eventuale distribuzione LAN
deve specificare i suoi componenti, senza presentare questa prova locale come qualifica LAN.

WinGet/App Installer resta una dipendenza esterna; WUA dipende dai servizi/policy e
dalle offerte Windows. Il runtime incluso evita di richiedere .NET separatamente
per il self-contained, non elimina questi prerequisiti.
[Documentazione WinGet](https://learn.microsoft.com/en-us/windows/package-manager/winget/).

Il minimo tecnico build 17763 non certifica ogni edizione Windows 10 successiva:
il supporto attuale .NET su Windows 10 è legato a edizioni/versioni LTSC/Enterprise
supportate. L'host Insider non sostituisce prove su Windows stabile.
[Supporto Windows .NET](https://learn.microsoft.com/en-us/dotnet/core/install/windows),
[matrice .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).
Il Setup ammette `x64compatible`, mentre l'app è win-x64: ARM64/emulazione non sono
qualificati da questa sessione e non vanno aggiunti alla promessa di compatibilità.

## Passo proposto alla chiusura M8-A/M8-B

**Prova personale dell'utente con la build M8-B del test host**, seguendo le istruzioni
brevi del [rapporto M8-B](M8-B-HOST-VALIDATION.md). La precedente proposta di un ambiente
virtualizzato è superata dalla decisione dell'utente, non è un blocco applicativo.
Packaging, nome definitivo e installazioni reali restano attività successive separate.

Aggiornamento per il checkpoint locale successivo: build e controlli mirati sono
registrati nel [rapporto M8-B](M8-B-HOST-VALIDATION.md). L'utente ha indicato come
futuro incarico esclusivamente la leggibilità della colonna Stato e il recupero
sicuro degli aggiornamenti WinGet bloccati; nessuna implementazione di tale attività
è stata avviata. La matrice di readiness e `WINDOWS VALIDATION PENDING` restano validi.
