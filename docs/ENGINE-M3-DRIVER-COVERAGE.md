# UpdateCenter 2.0.0 — M3 Driver Detection Coverage Analysis

8 novembre 2026 — Analisi documentale e del solo rilevamento driver. Nessuna implementazione.

## Esito e perimetro

**Lo zero di UpdateCenter significa assenza di offerte sopravvissute alle fonti e ai filtri attuali; non dimostra che tutti i driver installati siano gli ultimi disponibili.** Due limiti sono verificati nel codice: il catalogo ufficiale incorporato non contiene voci e il filtro WUA non ammette classi come `SecurityDevices` e `System`. Questi limiti possono escludere componenti chipset legittimi; non dimostrano che le 13 proposte del concorrente siano tutte applicabili.

**Primo intervento raccomandato:** un pilota di rilevamento da metadati ufficiali AMD/OEM, inizialmente limitato al componente PSP, separato dall'autorizzazione all'installazione. È una scelta tecnica motivata, non un incremento di copertura già misurato. Servono ancora identità del dispositivo e metadati INF per confermare un aggiornamento sul PC osservato.

- Branch verificato: `feature/engine-m2-dedup`.
- HEAD verificato: `88359bb50178bcac076061999f744f5eacc551a7`; working tree inizialmente pulito.
- Riutilizzata la mappatura [M1](<../docs/ENGINE-M1-BASELINE.md>) e il perimetro [M2](<../docs/ENGINE-M2-DEDUP.md>). M2 modifica l'aggregazione, non sorgenti o filtri driver; nessuna evidenza che spieghi il divario osservato.
- Ricerca su fonti primarie, con un solo subagente GPT-6 Astra Ultra per cataloghi e documentazione dei produttori. I conteggi dell'utente non sono stati riprodotti.
- Nessun audit generale, build, test, scansione WUA/hardware, download di driver, installazione, operazione privilegiata o modifica applicativa. Nessun commit, push, merge o release. Le verifiche WPF/M1/M2 non sono state rieseguite.

## 1. Che cosa interroga realmente UpdateCenter

Il percorso M1 arriva a `WindowsUpdateService`, quindi a `OfficialDriverCatalogService` se l'inventario è disponibile. I collegamenti di supporto ai produttori nell'inventario non costituiscono adattatori ai loro cataloghi.

### Windows Update Agent

Riferimento: [WindowsUpdateService.cs](<../Services/WindowsUpdateService.cs>), `BuildSearchSources`, `ConfigureSearcher`, `SearchSource`, `ReadEligibleDriver`.

| Sorgente | Comportamento attuale | Limite |
|---|---|---|
| Servizio configurato, `ServerSelection=0` | Prima ricerca tramite il servizio predefinito del sistema. | Non equivale necessariamente al servizio pubblico: configurazione e policy possono incidere. |
| Windows Update pubblico, `ServerSelection=2` | Fallback online se il predefinito fallisce o non produce offerte accettate. | Viene saltato quando il predefinito ha già prodotto almeno un risultato accettato. Non aggira le policy del sistema. |
| Microsoft Update, `ServerSelection=3` | Interrogato se il servizio `7971f918-a847-4430-9279-4a52d1efe18d` risulta già registrato. | Nessuna registrazione automatica; Microsoft Update non è una ricerca globale nel sito Microsoft Update Catalog. |

La query è `IsInstalled=0 and IsHidden=0 and Type='Driver'`, con `Online=true` e `IncludePotentiallySupersededUpdates=false`. Sono quindi fuori gli aggiornamenti installati, nascosti e quelli potenzialmente sostituiti non restituiti con questa configurazione. La ricerca è sincrona. Eccezioni COM/InvalidOperation vengono annotate per sorgente; se nessuna sorgente completa, il servizio fallisce. Non viene esaminato esplicitamente `ISearchResult.ResultCode`/`Warnings`: una raccolta vuota, da sola, non descrive tutti gli esiti possibili del servizio.

**Facoltativi:** la query non impone `IsAssigned=1`, `BrowseOnly=0` o `AutoSelectOnWebSites=1`. Nel codice `BrowseOnly` e `AutoSelectOnWebSites` determinano classificazione e preselezione dopo l'ammissione; non eliminano il candidato. L'importanza può prevalere sullo stato facoltativo. Microsoft documenta `BrowseOnly=1` come aggiornamento facoltativo e, se omesso, `DeploymentAction` come Installation. Non c'è evidenza che aggiungere semplicemente `BrowseOnly=1` ampli l'insieme già cercato. [Contratto WUA Search](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search).

La distribuzione Windows distingue comunque Automatic e Manual: da Windows 10 2004 i driver Manual sono accessibili attraverso gli aggiornamenti facoltativi, mentre la ricerca automatica segue regole diverse. Questa distinzione non prova un difetto della query dell'app: occorre confrontare gli UpdateID restituiti da WUA e dalla UI sullo stesso sistema. [Regole Microsoft di distribuzione](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/understanding-windows-update-automatic-and-optional-rules-for-driver-distribution).

I filtri successivi richiedono:

1. UpdateID GUID valido e revisione positiva.
2. Classe ammessa, Hardware ID e titolo non vuoti, modello e descrizione leggibili.
3. Assenza di `firmware`, `bios`, `uefi` e del GUID della classe firmware nei campi esaminati.

La lista ammette classi di periferiche quali Display, Net, Media, USB e storage. **Non ammette `SecurityDevices`, `System`, `Processor`, `Extension`, `SoftwareComponent`, né classi sconosciute.** È una protezione conservativa intenzionale: non va rimossa in blocco. `ReadEligibleDriver` viene richiamato anche sul candidato fresco prima dell'installazione, prima di EULA/download.

Il fallback saltato non spiega, da solo, uno zero finale in una scansione completata: per saltarlo deve essere già sopravvissuta almeno un'offerta del servizio predefinito. M1 conta candidati, esclusioni e risultati per sorgente, ma non identifica la specifica ragione di ogni esclusione.

### Catalogo ufficiale incorporato

[driver-catalog.json](<../Assets/driver-catalog.json>) contiene schema 1, catalogVersion `1.0.2`, **`entries: []`**. Non interroga automaticamente AMD, Intel, NVIDIA o OEM e oggi non può aggiungere aggiornamenti.

[OfficialDriverCatalogService.cs](<../Services/OfficialDriverCatalogService.cs>) verifica schema, build/architettura, URL HTTPS su domini ammessi, presenza/formato dell'hash SHA-256 e dei firmatari dichiarati, esclusione firmware e tipo `zip-inf`/`cab-inf`; richiede corrispondenza di ID e versione numerica superiore. Durante la scansione non verifica crittograficamente un payload. Non accetta direttamente un installer chipset EXE. Microsoft non è fra i vendor della sua mappa domini: il Catalog richiederebbe un adattatore/policy espliciti.

Questi controlli sono necessari, ma il catalogo vuoto non è una validazione pratica della copertura o dell'installabilità di futuri pacchetti. Non basta inserire URL e versioni in JSON per risolvere M3.

## 2. Identità, INF, ranking e firma

[HardwareInventoryService.cs](<../Services/HardwareInventoryService.cs>) acquisisce Hardware ID, Compatible ID, DeviceID, INF installato, versione, data, provider, classe e `IsSigned`. Raggruppa però i driver per nome/versione/provider, conserva la prima riga e incrementa `Quantity`: gli ID e gli INF delle altre istanze possono andare persi. Il contatore UI usa il numero delle righe raggruppate. I **166** dichiarati non sono quindi una misura della copertura né necessariamente 166 istanze PnP indipendenti.

Nel percorso WUA, `FindBestMatch` usa nome/modello, prefisso DeviceID, token e vendor per associare l'offerta all'inventario. Non confronta direttamente le liste HardwareIds/CompatibleIds. Un mancato match non scarta l'offerta WUA: può peggiorarne l'attribuzione, ma non spiega automaticamente zero offerte. La versione disponibile è estratta dal titolo o sostituita dalla data; l'app si affida all'applicabilità WUA, senza un proprio confronto INF completo.

Nel catalogo incorporato, il match è esatto come stringa normalizzata, ma appiattisce Hardware ID, Compatible ID e DeviceID in un insieme. Non distingue specificità, ordine o ranking; sceglie il primo dispositivo corrispondente. `DriverDateUtc` e `OemPreferred` esistono nel [modello](<../Models/OfficialDriverCatalog.cs>) ma non partecipano alla scelta.

Per un nuovo rilevatore servono invece:

- Identità per istanza conservata separatamente dalla presentazione raggruppata; ID completi, inclusi SUBSYS/REV quando presenti, e loro ordine/tipologia.
- Match con la sezione Models effettivamente applicabile dell'INF, incluse decorazioni di architettura e TargetOSVersion; vincoli OEM/piattaforma e dipendenze del componente.
- Confronto con il driver installato: Windows privilegia il rank migliore, poi, a parità di rank, la data `DriverVer`, quindi la versione. Un numero maggiore non autorizza a sostituire un driver più specifico. [Selezione dei pacchetti](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/overview-of-the-driver-selection-process).
- Il rank comprende firma, feature score e qualità del match degli identificatori; un valore più basso è migliore. Non va simulato con il solo nome del dispositivo. [Ranking PnP](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/how-windows-ranks-driver-packages).
- Distinzione fra provenienza HTTPS, integrità hash, firma valida del catalogo, appartenenza dei file al catalogo e compatibilità. Il solo `IsSigned` del driver installato non verifica il candidato. Un CAT firmato contiene gli hash dei file del pacchetto: una modifica ai file invalida la firma del pacchetto. [Cataloghi e firme Microsoft](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/catalog-files).

Il percorso esistente [OfficialDriverPackageService.cs](<../Services/OfficialDriverPackageService.cs>) cerca gli ID nel testo INF e controlla firma CAT/firmatario; non è un interprete completo delle sezioni Models né una prova di ranking PnP. Questa osservazione delimita il suo riuso: **il nuovo rilevamento non deve trasformarsi automaticamente in autorizzazione di installazione**. Nessuna modifica a questo percorso è proposta per M3.

## 3. Fonti integrabili e loro valore effettivo

| Fonte | Copertura/metadati utili | Integrazione ragionevole e limite |
|---|---|---|
| WUA attuale | Offerte applicabili secondo servizio, distribuzione e policy Windows. | Conservare come fonte primaria. Non promette il driver numericamente più recente di ogni produttore. |
| Microsoft Update Catalog | Ricerca per modello, produttore, classe e Hardware ID completo; pubblicazioni non necessariamente offerte al PC. | Arricchimento e riscontro di identità/revisioni. Non individuata nelle fonti consultate un'API pubblica documentata per un motore universale di ricerca; non assumere che HTML/endpoint interni siano un contratto stabile. [Documentazione Catalog](https://learn.microsoft.com/en-us/windows-server/administration/windows-server-update-services/manage/wsus-and-the-catalog-site). |
| AMD | Release note, matrici di piattaforma/OS e versioni dei componenti dei bundle chipset. | Buona fonte iniziale curata; la matrice del bundle non sostituisce gli ID e le condizioni INF del singolo componente. [Note chipset AMD](https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-8-09-28-1124.html). |
| Intel | Download Center e copertura dichiarata di Driver & Support Assistant. | DSA non supporta ogni prodotto ed è un'applicazione di scansione. Nessun catalogo universale/API pubblica documentata individuato in questa ricerca; niente dipendenza da endpoint dedotti dal sito. [Intel DSA](https://www.intel.com/content/www/us/en/support/articles/000056649/software/software-applications.html). |
| NVIDIA | Pagine ufficiali di driver e prodotti supportati. | Possibile arricchimento mirato GPU. Una risposta sul forum sviluppatori del 2023 dichiarava assente un'API supportata della pagina download: evidenza datata, non prova definitiva dello stato 2026. Contratto e disponibilità vanno verificati prima di implementare. [Forum sviluppatori NVIDIA](https://forums.developer.nvidia.com/t/how-to-detect-nvidia-gpu-and-jump-to-driver-website-programmatically/251542). |
| Dell | Indice `CatalogIndexPC.cab`, cataloghi per SystemID. | Risolvere il catalogo della piattaforma dall'indice. Il precedente CatalogPC trimestrale non ha la stessa freschezza dei cataloghi piattaforma, aggiornati due volte a settimana. [Documentazione Dell](https://www.dell.com/support/kbdoc/en-in/000453493/dell-client-commercial-pcs-update-catalog-cadences). |
| HP | Interfaccia pubblicata `Get-SoftpaqList`: piattaforma/OS, versioni, URL e metadati CVA; output strutturati. | Metadati e payload hanno opzioni distinte. Verificata l'interfaccia nel modulo pubblicato, non la disponibilità attuale degli endpoint; il portale sviluppatori non è stato raggiungibile. [Modulo HP SoftPaq](https://www.powershellgallery.com/packages/HP.Softpaq/1.6.2/Content/HP.Softpaq.psm1). |
| Lenovo | Pacchetti con versione, severità e regole di applicabilità. | `Get-LnvUpdate` scarica temporaneamente i file durante la verifica di applicabilità/firma: non è una query puramente metadati e non va eseguito entro questi vincoli. [Documentazione Lenovo](https://docs.lenovocdrt.com/guides/lcu/functions/get-lnvupdate/). |

Le API Microsoft Hardware Dashboard rivolte ai produttori espongono metadati dei loro invii: non vanno confuse con una ricerca pubblica di tutti i driver. [Driver package metadata](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-package-metadata).

Un pacchetto può essere ufficiale, compatibile e non offerto da WUA, per differenze di canale, targeting o policy. Per rilevarlo in sicurezza serve un manifesto verificabile con fonte/revisione, identità del componente, applicabilità e provenienza delle prove. Una release note senza ID produce soltanto **“versione ufficiale esistente, compatibilità da verificare”**. Non incrementa il conteggio degli aggiornamenti validi. Preferenza OEM e confronto con alternative generiche devono essere espliciti, senza scavalcare policy aziendali.

## 4. Caso AMD PSP e Provisioning Packages

Le note AMD del bundle [8.02.18.557](https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-8-02-18-557.html) elencano PSP **5.44.0.0**. Quelle del bundle [8.09.28.1124](https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-8-09-28-1124.html) elencano PSP **5.46.0.0** e **AMD PPM Provisioning File Driver 8.0.0.65**; distinguono **AMD Provisioning for OEM 1.0.0.6**. La [pagina AMD B550](https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/b550.html) data il bundle 8.09.28.1124 al **21/10/2026**, prima dell'osservazione dell'utente. B550 è una prova di pubblicazione, non il chipset attribuito al suo PC.

Ne consegue che le due versioni proposte hanno un riscontro ufficiale. Non sono verificati il match dell'etichetta “AMD Provisioning Packages” con PPM, le date INF 16/03/2026 e 04/06/2026, né l'applicabilità a “AMD PSP 11.0 Device”. Versione del bundle, versione del componente, data di pubblicazione e data INF sono grandezze separate.

**Ipotesi PSP concreta:** se WUA restituisce quel candidato con `DriverClass=SecurityDevices`, UpdateCenter lo scarta sempre con il filtro attuale, indipendentemente dalla versione. Se WUA non lo restituisce, quel filtro non è la causa dell'assenza iniziale; il catalogo incorporato vuoto impedisce comunque il recupero da una fonte alternativa. Non sono stati acquisiti l'UpdateID esatto, il candidato COM né l'INF della proposta, quindi non si attribuisce con certezza il caso a uno dei due rami.

PSP designa qui il driver Windows del dispositivo: ciò non autorizza firmware PSP, BIOS/UEFI o capsule firmware. Nomi e appartenenza al bundle AMD non bastano a distinguere tutti i tipi di contenuto.

### Evidenze disponibili e dati mancanti

| Dato | Stato al termine di M3 | Ancora necessario |
|---|---|---|
| 166 righe, 0 aggiornamenti, 13 proposte concorrenti | Dichiarazione dell'utente del 08/11/2026, non riprodotta. | Elenco completo delle 13 righe, opzioni/versione dello scanner, data e canale delle proposte. |
| PSP installato 5.44.0.0, proposta 5.46.0.0 | Versioni corroborate dalle note AMD; stato installato riferito dall'utente. | Hardware/Compatible ID ordinati, classe/ClassGUID, INF installato e candidato, provider, firma, build/architettura, CPU/chipset e modello OEM. |
| Date PSP 16/03 e 04/06/2026 | Riferite dall'utente, non confermate come `DriverVer`. | Metadati INF originali e significato della data mostrata da ciascuna UI. |
| Provisioning 8.0.0.65 | Esiste la versione PPM ufficiale; equivalenza locale non stabilita. | Identità del componente installato, versione attuale e relazione col bundle. |
| WUA esclude SecurityDevices/System | Verificato staticamente; rifiuto prima della creazione dell'offerta. | Esiti per sorgente e classe/identità del candidato reale, se restituito. |
| Catalogo incorporato senza voci | Verificato al checkpoint. | Manifesti ufficiali curati con condizioni di applicabilità dimostrabili. |
| Configurazione WUA del PC | Non ispezionata in M3. | Servizi registrati, policy rilevanti, warning/ResultCode e aggiornamenti facoltativi presenti. |
| Compatibilità/firma/rank dei 13 candidati | Non verificati. | Prova per candidato; distinguere insufficienza di evidenza da incompatibilità. |

La raccolta futura deve essere mirata e consensuale, con seriali/identificatori personali rimossi dagli artefatti condivisi. La diagnostica M1 resta numerica: un eventuale export di identità richiede un percorso locale esplicito, senza introdurre telemetria.

## 5. Interpretazione del divario 13 contro 0

| Possibile causa | Evidenza e peso |
|---|---|
| Fonti più ampie nel concorrente | Plausibile: UpdateCenter ha WUA e zero voci vendor. Non è stato ispezionato il database del concorrente. |
| Classi chipset escluse | Meccanismo verificato, particolarmente pertinente al PSP; incidenza reale ancora da misurare. |
| Targeting/Manual/OEM/policy | Plausibile: pubblicazione di un pacchetto e offerta WUA locale sono eventi diversi. |
| Componenti dello stesso bundle contati separatamente | Plausibile per chipset: più componenti possono avere versioni proprie. Non equivalgono necessariamente ad altrettante installazioni indipendenti. |
| Versioni alternative o match troppo generici | Possibile, non dimostrato: un driver più recente può avere rank peggiore, SUBSYS diverso o dipendenze non soddisfatte. |
| Raggruppamento dell'inventario e attribuzione euristica | Limiti verificati, rilevanti al matching; non provano da soli il mancato ritorno di offerte WUA. |
| Fonte parziale/non disponibile | Possibile senza log del caso. Distinguere zero candidati da zero candidati ammessi e da scansione incompleta. |

La classificazione futura deve separare: **aggiornamento applicabile**, **componente di bundle**, **facoltativo applicabile**, **versione alternativa**, **escluso per incompatibilità/protezione**, **non verificabile con i dati disponibili**. “Facoltativo” non significa falso positivo; “più nuovo” non significa necessario. Un'offerta duplicata fra fonti non è un secondo aggiornamento. La deduplica M2 per Kind/Id non conosce queste relazioni semantiche.

## 6. Cinque proposte ordinate

Ordine per impatto atteso sui casi validi, sicurezza e costo; nessun incremento numerico è stimabile senza le prove mancanti.

| Priorità | Proposta | Impatto atteso | Sicurezza e complessità |
|---|---|---|---|
| **1** | **Pilota AMD/OEM di sola rilevazione, inizialmente PSP**, con manifesto curato e provenienza verificabile; risultati distinti per livello di prova. | Alto sul limite di fonte: può trovare un pacchetto legittimo non offerto da WUA. Il primo caso concreto è PSP, non tutti i 13. | Basso rischio operativo senza payload/installazione; complessità medio-alta per qualificare metadati INF e OEM. Nessun allargamento dell'allowlist WUA. |
| **2** | Conservare tutte le istanze e costruire matching per ID/INF applicabile, mantenendo il raggruppamento soltanto nella presentazione. | Alto per precisione e copertura degli ID oggi persi; non aggiunge da solo una fonte. | Basso rischio operativo, complessità medio-alta. La parte strettamente necessaria al PSP è prerequisito del pilota; generalizzazione successiva. |
| **3** | Estendere la diagnostica M1 con ragioni numeriche di esclusione, esito WUA e confronto controllato dei facoltativi. | Indiretto, ma rende distinguibili mancata offerta, filtro e errore con costo ridotto. | Complessità bassa/media; nessuna registrazione MU o modifica WSUS/policy automatica. Un'eventuale rivalutazione di classi richiede prove e incarico separato. |
| **4** | Adattatori di metadati OEM per piattaforma, poi riscontri mirati Microsoft Catalog. | Potenzialmente alto su PC OEM supportati; preserva specificità del modello. | Complessità alta per formati, applicabilità e manutenzione. Escludere BIOS/firmware prima di generare candidati; non invocare strumenti che scaricano payload implicitamente. |
| **5** | Modello bundle/componenti e identità comune fra fonti; successiva estensione curata Intel/NVIDIA. | Evita conteggi gonfiati e prepara copertura più ampia; guadagno iniziale meno documentato. | Complessità media/alta; versioni separate, nessuno scraping universale assunto affidabile. |

**Contratto del solo primo intervento da autorizzare:** input per istanza e manifesto PSP con fonte, versione/data del componente, condizioni INF/OS/OEM e prove di integrità/firma del riferimento verificato offline. Output consultivo, senza generare piani installabili. Informazione incompleta resta “da verificare”; solo il match completo, senza peggioramento di compatibilità e con avanzamento di versione, conta come aggiornamento valido. L'esistenza di 5.46.0.0 nelle note AMD non soddisfa da sola il contratto.

Non basta popolare il JSON attuale, che genera già elementi `OfficialInfPackage` installabili. Il pilota deve mantenere invariati consenso, controlli hash/firma/versione, esclusioni firmware e ricontrollo WUA fresco. **Non è raccomandato aggiungere indiscriminatamente `System` o `SecurityDevices` all'allowlist.**

## 7. Valutazione concreta di Craft Anything

Il [README del progetto](https://github.com/storytold/craft-anything) descrive una modalità Clean Room basata su comportamento e superfici visibili e una modalità Deep con accesso agli interni. Dichiara anche che Clean Room **non è un sandbox del sistema operativo** e che l'adattatore Codex non è stato verificato in un'installazione reale. Sono dichiarazioni del progetto, non proprietà provate in questa analisi.

Il [catalogo degli strumenti](https://raw.githubusercontent.com/storytold/craft-anything/main/docs/tools.md) include acquisizione finestre, walkthrough, confronto catture e gestione/esportazione di evidenze. Potrebbe aiutare a registrare, su VM autorizzata, le 13 righe, le etichette, i dettagli visibili, le selezioni predefinite e i conteggi prima/dopo l'espansione dei bundle.

**Utilità attuale modesta:** screenshot/esportazione manuale e una matrice di confronto bastano per questo campione. Il tool non fornisce il catalogo ufficiale mancante, non certifica compatibilità e non rivela legittimamente le regole interne del concorrente osservandone solo la UI. Adottarlo ora aggiungerebbe preparazione e superficie operativa senza risolvere il principale limite di rilevamento.

Un eventuale uso successivo richiede incarico distinto, revisione della versione fissata e ambiente isolato. Soltanto interazioni visibili consentite: niente modalità Deep, decompilazione, lettura di database/codice proprietario o intercettazione di traffico. Nessuna installazione, integrazione o esecuzione di Craft Anything è stata effettuata; valutata la documentazione pubblica disponibile alla data del rapporto, non certificata una release.

## 8. Piano riproducibile, non eseguito

### Fixture offline

Estendere in un incarico successivo l'harness console esistente, con AppPaths su root GUID prima di qualsiasi I/O, COM/provider finti e clock/input fissati. Registrare commit, hash delle fixture, output atteso e risultati. Nessun test deve chiamare downloader, installer, EULA o sorgenti reali.

| Fixture | Asserzione richiesta |
|---|---|
| WUA: default vuoto/con risultati/errore; MU presente/assente | Sorgenti effettive e fallback conformi; parziale distinto da zero completo. |
| WUA: BrowseOnly, AutoSelectOnWebSites, importanza, nascosti/sostituiti | Classificazione/preselezione coerenti; nessuna falsa promessa di recupero dei facoltativi modificando solo un flag. |
| PSP classificato SecurityDevices e classi System/Extension/SoftwareComponent | Filtro attuale invariato; candidato consultivo alternativo separato e non installabile. |
| Firmware/BIOS/UEFI/GUID, metadati mancanti, identità invalida | Rifiuto conservativo, incluso candidato modificato fra rilevamento e ricontrollo; zero chiamate di installazione. |
| Due istanze con stesso nome/versione/provider e ID/INF differenti | Nessun ID perso; match sulla corretta istanza, senza prefissi o semplice nome. |
| INF sintetici: ID completo/generico, SUBSYS, decorazioni OS/architettura | Nessun match nella sezione sbagliata; evidenza incompleta classificata non verificabile. |
| Versione maggiore con rank peggiore; date/versioni uguali o inferiori | Nessuna promozione automatica; mantenere i controlli di versione esistenti. |
| Bundle con PSP e PPM; stesso componente in due fonti | Versione bundle distinta dal componente; conteggi componenti/pacchetti separati e duplicati riconosciuti. |
| Manifesto alterato, firma assente/non valida, hash errato, vincolo OEM mancante | Nessun aggiornamento valido e nessun piano installabile. Le fixture del rilevatore simulano gli esiti di fiducia, non certificano pacchetti reali. |

I casi nuovi devono integrare le fixture di `WindowsUpdateSafetyTests` e M1 senza ripetere audit UI/LAN o benchmark M2 non pertinenti. La verifica crittografica reale del riferimento richiede una successiva acquisizione autorizzata del materiale; non va sostituita con un mock presentato come prova.

### Validazione successiva in VM

1. Preparare snapshot Windows x64 rappresentativi del minimo supportato e delle versioni in uso, annotando build, policy, servizi WUA e stato iniziale. Prima fase: input registrati/fixture, senza driver aggiunti al Driver Store.
2. Con un incarico dedicato, confrontare baseline e nuovo rilevatore sugli stessi input e sulle stesse revisioni dei metadati; misurare tempo, memoria, richieste e byte. Una sola acquisizione per sorgente/piattaforma, riusata nella scansione, evita richieste per ciascuna delle 166 righe.
3. Eventuali query WUA reali solo con autorizzazione separata e senza modifica di servizi/policy. Confrontare UpdateID/revisioni, classi e facoltativi; acquisizioni di pacchetti, staging e installazioni restano fuori da questa fase.
4. Usare una tabella di adjudication delle 13 proposte: fonte ufficiale, dispositivo, componente/bundle, ID/INF, OS/OEM, firma/integrità, rank/versione, decisione e motivazione. Il denominatore utile è l'insieme degli aggiornamenti confermati, non il numero commerciale 13.
5. Una VM ordinaria non riproduce necessariamente il PSP del PC fisico: valida logica e regressioni, non l'applicabilità finale su quell'hardware. Questa richiede una successiva verifica mirata su hardware rappresentativo, senza presumere che il passthrough sia disponibile o autorizzato.

Accettazione del pilota: nessun nuovo falso positivo nelle fixture negative; almeno un caso reale aggiuntivo documentato prima di dichiarare aumento di copertura; nessuna riduzione delle protezioni; stato “da verificare” escluso dal conteggio dei validi. Non è fissato un obiettivo artificiale di 13 aggiornamenti e non è autorizzata alcuna implementazione da questo rapporto.
