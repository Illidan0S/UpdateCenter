# Progetto: Gestione rete di Update Center

## Stato del documento

- Tipo: progetto tecnico e stato di avanzamento della preview.
- Base analizzata: Update Center 1.0.8, .NET 8, WPF, Windows 10/11 x64.
- Ambito iniziale: soli PC nella stessa rete locale.
- Stato sviluppo: discovery LAN, associazione sicura, scansioni multiple, installazioni remote con avanzamento e richieste di collegamento approvate dal dipendente completate nella preview del 02/08/2026.

## Obiettivo

Aggiungere a Update Center una pagina opzionale **Gestione rete** dalla quale un amministratore possa:

- trovare nella LAN gli altri PC sui quali è installato e abilitato Update Center Agent;
- associare esplicitamente e in modo sicuro ogni PC;
- vedere disponibilità, versione di Update Center e riepilogo del dispositivo;
- richiedere una scansione remota;
- consultare gli aggiornamenti software, driver e runtime trovati;
- inviare un piano di installazione a uno o più PC;
- seguire stato, avanzamento, errori e richiesta di riavvio;
- consultare una cronologia locale delle operazioni remote;
- revocare in qualsiasi momento l'associazione di un PC.

La funzione rimane disattivata per impostazione predefinita. Nessun PC viene gestito soltanto perché è visibile in rete.

## Strategia di prodotto consigliata

La gestione di rete è destinata soprattutto ad aziende, laboratori, scuole, associazioni e utenti che amministrano più PC. Non deve quindi appesantire l'esperienza dell'utente classico.

La scelta consigliata è **un solo codice sorgente con due modalità di distribuzione**, non due applicazioni sviluppate separatamente:

### Update Center

- edizione normale per l'utente classico;
- interfaccia attuale, scansione e aggiornamenti del solo PC locale;
- installer standard e versione portable;
- nessun servizio Agent installato;
- nessuna porta di rete aperta;
- nessun processo aggiuntivo in background;
- esperienza e requisiti sostanzialmente invariati.

### Update Center Network

- variante opzionale per chi gestisce più computer;
- stessa base applicativa e stessi motori di scansione/installazione;
- include la pagina Gestione rete, il servizio Agent e il Session Helper;
- installer dedicato con scelta dei componenti e regole firewall esplicite;
- configurazione disattivata finché l'amministratore non abilita e associa i dispositivi;
- possibile denominazione futura `Update Center Network` o `Update Center Business`, da decidere prima della preview.

In questo modo una correzione a WinGet, driver, runtime o sicurezza viene sviluppata e testata una volta sola. Creare un fork completamente separato produrrebbe invece duplicazione, versioni divergenti e il rischio che una delle due applicazioni rimanga priva di correzioni importanti.

La separazione deve avvenire a livello di **moduli e pacchetti di installazione**:

```text
Codice condiviso
├─ motori Update Center Core
├─ modelli e controlli di sicurezza
├─ edizione standard: UI locale
└─ variante Network: UI locale + console + Agent + Session Helper
```

Per la prima preview è preferibile produrre un pacchetto separato `Update Center Network PREVIEW`, mantenendo la versione stabile normale fuori dalla sperimentazione. Dopo il collaudo si potrà decidere se conservare due installer oppure offrire nell'installer principale un componente facoltativo **Gestione rete**, non selezionato per impostazione predefinita.

## Principi non negoziabili

1. Nessuna telemetria o server cloud obbligatorio.
2. Nessun comando accettato prima dell'associazione esplicita.
3. Comunicazioni cifrate e identità dei dispositivi verificate.
4. Nessun BIOS, UEFI o firmware installato automaticamente.
5. Nessun riavvio automatico nella prima versione.
6. Gli aggiornamenti vengono comunque selezionati e confermati dall'amministratore.
7. Un PC remoto può disabilitare o revocare la gestione in qualsiasi momento.
8. Log privi di password, codici di associazione, token e chiavi private.
9. Una sola operazione di scansione/installazione per PC alla volta.
10. Compatibilità preservata con l'uso locale e con la versione portable.
11. Codice modulare e scalabile, senza dipendenze dirette tra interfaccia, rete e motori di aggiornamento.
12. Utilizzo limitato e prevedibile di CPU, memoria, disco e rete anche con molti Agent registrati.

## Requisiti di scalabilità ed efficienza

La prima versione abilita un solo Controller, ma il codice non deve assumere che esistano pochi PC. Collezioni, API e persistenza devono essere progettate per crescere senza riscritture strutturali.

### Efficienza temporale

- Operazioni di rete e disco completamente asincrone, con `CancellationToken` e timeout espliciti.
- Discovery, controllo stato e lettura risultati eseguiti in parallelo con un limite di concorrenza, mai creando un thread dedicato per ogni PC.
- Lookup di dispositivi e operazioni tramite ID e strutture indicizzate, evitando scansioni ripetute di liste complete nei percorsi frequenti.
- Aggiornamento differenziale dell'interfaccia: cambiano soltanto i dispositivi e le righe interessate.
- Polling adattivo con backoff: frequente durante un'operazione, ridotto sui PC inattivi o offline.
- Paginazione per cronologia, audit e risultati estesi.
- Nessuna scansione completa della rete a ogni aggiornamento dell'interfaccia.

### Efficienza spaziale

- Risposte API con DTO compatti, senza immagini, log completi o dati hardware non richiesti.
- Streaming e paginazione per dati potenzialmente grandi; nessun caricamento in memoria dell'intera cronologia.
- Una sola copia persistente dei risultati per operazione, con riepiloghi separati per la console.
- Limiti espliciti per dimensione delle richieste, numero di risultati e lunghezza dei messaggi diagnostici.
- Retention e rotazione configurabili per audit, operazioni e log.
- Cache con scadenza e dimensione massima; nessuna cache illimitata.
- Download e pacchetti temporanei eliminati secondo regole sicure dopo il completamento.

### Scalabilità architetturale

- `Core`, `Contracts`, `Agent`, `RemoteClient` e UI rimangono separati e testabili indipendentemente.
- Il Controller lavora tramite interfacce e contratti versionati, non conosce dettagli interni dell'Agent.
- La coda usa operazioni persistenti e ID stabili; una disconnessione non occupa risorse attive inutilmente.
- Il limite di installazioni simultanee è configurabile e distinto dal limite delle richieste di stato.
- Il modello dati prevede più controller e ruoli futuri, pur imponendo una sola associazione Controller nell'MVP.
- Nessuno stato globale statico per dispositivi, sessioni o operazioni, salvo servizi esplicitamente thread-safe.
- Dipendenze aggiuntive ridotte al minimo e giustificate per manutenzione, dimensione e sicurezza.

### Obiettivi iniziali verificabili

Questi valori sono budget di progetto da validare sui PC di test, non promesse basate su misure ancora inesistenti:

- dashboard reattiva con almeno 100 Agent registrati;
- discovery e aggiornamento stato senza bloccare il thread WPF;
- massimo 10 richieste di stato concorrenti per impostazione predefinita;
- massimo 2 installazioni concorrenti per impostazione predefinita;
- traffico a riposo ridotto tramite heartbeat adattivo, senza polling continuo aggressivo;
- memoria dell'Agent stabile nel tempo, verificata con test prolungati e senza crescita non limitata;
- cronologia sempre paginata e sottoposta a retention;
- avvio dell'edizione normale non rallentato dai moduli Network, che non vengono caricati quando assenti/disabilitati.

Prima di ogni preview verranno raccolte misure di avvio, memoria, CPU, traffico e tempi di risposta. Una regressione significativa bloccherà la fase successiva finché non sarà spiegata o corretta.

## Ambito della prima versione (MVP)

### Incluso

- Windows 10 1809 o successivo e Windows 11 x64.
- Rete locale IPv4; inserimento manuale dell'indirizzo come alternativa al rilevamento.
- Rilevamento dei soli agenti che hanno autorizzato la visibilità in LAN.
- Associazione con codice temporaneo mostrato sul PC remoto.
- Elenco PC online/offline e ultimo contatto.
- Scansione remota di software, driver e runtime usando gli stessi motori locali.
- Installazione degli elementi esplicitamente selezionati.
- Installazioni su un singolo PC o su un gruppo, con coda e limite di concorrenza.
- Stato dell'operazione, risultati per elemento e indicazione del riavvio necessario.
- Annullamento di una scansione e annullamento degli elementi non ancora iniziati.
- Pausa tra due elementi; mai interruzione forzata di un installer in esecuzione.
- Audit locale minimo: chiave del controller, PC, comando, orario ed esito.

### Escluso dall'MVP

- Gestione via Internet o attraversamento NAT.
- Server cloud, account online o pannello web pubblico.
- Integrazione Active Directory/Entra ID.
- Wake-on-LAN.
- Distribuzione iniziale automatica dell'agente su PC che non lo possiedono.
- Trasferimento o caching centralizzato dei pacchetti.
- Riavvio forzato o spegnimento remoto.
- Esecuzione di comandi PowerShell arbitrari.
- Controllo remoto del desktop.
- Aggiornamenti di BIOS, UEFI e firmware.

## Architettura proposta

```text
┌────────────────────────────────────────────────────────────┐
│ PC amministratore                                          │
│ UpdateCenter WPF                                           │
│ └─ pagina Gestione rete                                    │
│    └─ Remote Management Client                             │
└──────────────────────┬─────────────────────────────────────┘
                       │ HTTPS + pinning certificato Agent
                       │ richieste firmate dal Controller e anti-replay
┌──────────────────────▼─────────────────────────────────────┐
│ PC gestito                                                 │
│ UpdateCenter Agent (servizio Windows)                      │
│ ├─ discovery LAN e API                                     │
│ ├─ autorizzazioni, coda, audit e stato                     │
│ ├─ operazioni di sistema/driver                            │
│ └─ canale locale protetto                                  │
│       └─ UpdateCenter Session Helper (utente connesso)      │
│          └─ WinGet e operazioni legate al profilo utente   │
└────────────────────────────────────────────────────────────┘
```

### Perché servono servizio e helper utente

Il servizio Windows garantisce disponibilità, identità stabile, protezione delle chiavi e operazioni amministrative. Tuttavia WinGet e parte dell'inventario software dipendono dal profilo dell'utente. Eseguire tutto come `LocalSystem` potrebbe non vedere o aggiornare correttamente le applicazioni dell'utente.

L'helper viene avviato solo nella sessione dell'utente configurato, comunica con il servizio tramite named pipe con ACL restrittiva ed esegue le sole operazioni previste dal protocollo. Se l'utente richiesto non è connesso, l'agente restituisce uno stato esplicito (`WaitingForUserSession`) e non prova a operare nel profilo sbagliato.

### Componenti

| Progetto | Tipo | Responsabilità |
|---|---|---|
| `UpdateCenter` | WPF esistente | Uso locale e nuova console di gestione rete |
| `UpdateCenter.Contracts` | Class library | DTO versionati, stati, errori e contratti API |
| `UpdateCenter.Core` | Class library Windows | Scansione, creazione piani e coordinamento riutilizzabili |
| `UpdateCenter.Agent` | Worker/servizio Windows | API LAN, pairing, autorizzazioni, coda e audit |
| `UpdateCenter.SessionHelper` | processo utente | Operazioni WinGet legate al profilo configurato |
| `UpdateCenter.RemoteClient` | Class library | Discovery e client HTTPS usato dalla WPF |
| `UpdateCenter.Tests` | test | Contratti, sicurezza, coda e regressioni |

Non è previsto uno spostamento massivo iniziale dei file. I servizi esistenti saranno estratti in `Core` uno alla volta, mantenendo build e test funzionanti a ogni passaggio.

## Struttura prevista del repository

```text
Source/UpdateCenter/
├─ UpdateCenter.sln
├─ UpdateCenter.csproj
├─ Contracts/
│  └─ UpdateCenter.Contracts.csproj
├─ Core/
│  └─ UpdateCenter.Core.csproj
├─ Agent/
│  └─ UpdateCenter.Agent.csproj
├─ SessionHelper/
│  └─ UpdateCenter.SessionHelper.csproj
├─ RemoteClient/
│  └─ UpdateCenter.RemoteClient.csproj
├─ Tests/
│  ├─ UpdateCenter.SmokeTests/
│  ├─ UpdateCenter.Contracts.Tests/
│  └─ UpdateCenter.Agent.Tests/
└─ docs/
   └─ PROGETTO-GESTIONE-RETE.md
```

I nomi delle cartelle potranno essere corretti prima della fase 1, ma la separazione delle responsabilità deve rimanere.

## Rilevamento nella rete

1. L'agente resta invisibile finché l'utente non abilita **Consenti gestione da altri Update Center nella rete locale**.
2. La console invia periodicamente un messaggio UDP multicast/broadcast di discovery sulla sola rete privata.
3. L'agente risponde con dati non sensibili: protocollo, ID casuale del dispositivo, nome visualizzato, versione app, porta API e stato di associazione.
4. Nessun inventario hardware, nome utente o elenco software viene inviato prima del pairing.
5. È sempre disponibile **Aggiungi tramite indirizzo IP** se multicast/broadcast è filtrato.

Porte proposte, configurabili prima del rilascio:

- UDP `47381`: discovery;
- TCP `47382`: API HTTPS dell'agente.

Le regole firewall vengono aggiunte solo dall'installer dell'agente, limitate al profilo **Privato**. La portable può agire da console, ma non installa silenziosamente un servizio o regole firewall.

## Associazione e sicurezza

### Flusso di pairing

1. Sul PC da gestire l'utente apre Update Center e abilita la gestione rete.
2. L'app mostra un codice monouso, valido 5 minuti, e il nome del controller che sta chiedendo l'accesso.
3. Sulla console l'amministratore seleziona il PC e inserisce il codice.
4. Il Controller registra nell'Agent il proprio certificato pubblico; la chiave privata non lascia mai il Controller.
5. Il PC remoto mostra la conferma finale e l'elenco dei permessi concessi.
6. Da quel momento l'API richiede autenticazione reciproca; il solo indirizzo IP non concede alcun accesso.

### Protezioni previste

- TLS 1.2 o superiore e pinning del certificato Agent dopo il pairing.
- Ogni richiesta protetta è firmata RSA dal Controller e include timestamp e nonce monouso anti-replay.
- Chiavi private protette con Windows DPAPI e ACL accessibili al solo servizio/account previsto; l'Agent conserva soltanto il certificato pubblico del Controller.
- Codice pairing derivato casualmente, a scadenza breve, monouso e con limite tentativi.
- Richieste con ID univoco, timestamp, scadenza e protezione anti-replay.
- Allowlist dei controller associati; revoca locale immediata.
- Nessun endpoint per eseguire shell, percorsi o argomenti arbitrari.
- Validazione stretta dei DTO, dimensioni massime e rate limiting.
- Download driver invariato: HTTPS, hash previsto, firma e compatibilità hardware.
- Audit append-only con rotazione e conservazione iniziale di 30 giorni.
- L'agente ascolta solo sulle interfacce consentite e rifiuta reti con profilo Pubblico.

La revisione di sicurezza deve precedere le installazioni remote: la modalità sola lettura viene completata e collaudata prima di abilitare qualsiasi comando mutabile.

## Modello di autorizzazione

Ogni controller associato riceve permessi espliciti:

- `ViewStatus`: stato e riepilogo PC;
- `RunScan`: avvio scansione;
- `ViewUpdates`: lettura risultati;
- `InstallUpdates`: invio di piani composti esclusivamente da elementi restituiti dall'ultima scansione valida;
- `ViewHistory`: lettura cronologia remota.

Nell'MVP non esistono permessi per shell, file arbitrari, modifica utenti, riavvio forzato o firmware. Il PC gestito può impostare la modalità **sola lettura**.

## Contratto operativo

Le API saranno versionate sotto `/api/v1`. Le operazioni lunghe sono asincrone:

1. la console invia la richiesta;
2. l'agente valida permessi, versione del protocollo e precondizioni;
3. l'agente restituisce un `operationId`;
4. la console interroga lo stato con polling moderato;
5. il risultato resta disponibile per un periodo limitato e viene registrato nell'audit.

Endpoint logici minimi:

| Metodo | Endpoint | Funzione |
|---|---|---|
| `GET` | `/api/v1/status` | Stato, versione e capacità del PC |
| `POST` | `/api/v1/scans` | Avvia una scansione |
| `GET` | `/api/v1/scans/{id}` | Stato e risultati della scansione |
| `POST` | `/api/v1/installations` | Valida e accoda un piano selezionato |
| `GET` | `/api/v1/operations/{id}` | Avanzamento e risultati |
| `POST` | `/api/v1/operations/{id}/pause` | Pausa tra elementi |
| `POST` | `/api/v1/operations/{id}/resume` | Riprende la coda |
| `POST` | `/api/v1/operations/{id}/cancel` | Annulla ciò che non è iniziato |
| `GET` | `/api/v1/history` | Cronologia limitata e paginata |

Il piano remoto non può contenere comandi. Contiene soltanto ID opachi di elementi presenti in una scansione recente, più le opzioni già ammesse da Update Center. L'agente ricostruisce e rivalida localmente il `UpdatePlan` prima dell'esecuzione.

## Stato dei PC e delle operazioni

Stati PC iniziali:

- `Online`;
- `Busy`;
- `WaitingForUserSession`;
- `RestartRequired`;
- `VersionIncompatible`;
- `Offline`;
- `Unauthorized`.

Stati operazione iniziali:

- `Queued`;
- `Scanning`;
- `AwaitingConfirmation`;
- `Installing`;
- `Paused`;
- `Completed`;
- `CompletedWithErrors`;
- `Cancelled`;
- `Failed`.

Una disconnessione non equivale automaticamente a un errore: la console conserva l'`operationId` e riconcilia lo stato quando il PC torna raggiungibile.

## Interfaccia proposta

Nuova voce principale **Gestione rete**, visibile ma con funzione spenta inizialmente.

### Vista dispositivi

- ricerca e filtro;
- nome PC, stato, Windows, versione agente, ultimo contatto;
- aggiornamenti disponibili per categoria;
- indicazione di operazione attiva e riavvio richiesto;
- selezione multipla;
- azioni: Scansiona, Mostra aggiornamenti, Aggiorna selezionati, Revoca.

### Dettaglio PC

- riepilogo hardware essenziale;
- utente/profilo gestito senza esporre dati non necessari;
- risultati dell'ultima scansione;
- selezione aggiornamenti con gli stessi avvisi dell'app locale;
- avanzamento e diagnostica;
- cronologia del dispositivo.

### Aggiornamento di gruppo

Prima dell'invio viene mostrato un riepilogo per PC. Gli elementi non presenti o non applicabili su un dispositivo vengono esclusi e segnalati. Valore iniziale consigliato: massimo 2 PC in installazione contemporanea, configurabile tra 1 e 5.

## Dati locali

Percorsi proposti:

```text
%ProgramData%\UpdateCenter\Agent\
├─ agent-settings.json
├─ controllers.json        (metadati pubblici, nessuna chiave privata in chiaro)
├─ operations\
├─ audit\
└─ certificates\          (materiale protetto da DPAPI/ACL)

%LOCALAPPDATA%\UpdateCenter\
├─ settings.json           (preferenze console)
├─ managed-devices.json    (dispositivi conosciuti e certificati pubblici)
└─ RemoteHistory\
```

I formati avranno una versione schema e migrazioni esplicite. Le scritture saranno atomiche, seguendo il modello già usato dall'app.

## Compatibilità tra versioni

- Discovery dichiara `protocolMajor` e `protocolMinor`.
- Major diverso: gestione bloccata con messaggio di aggiornamento richiesto.
- Minor diverso: funzionalità negoziate tramite elenco `capabilities`.
- La console non assume che tutti i PC supportino le stesse funzioni.
- L'aggiornamento automatico di Update Center Agent non entra nell'MVP; viene progettato separatamente dopo la gestione base.

## Piano di implementazione

### Fase 0 — Baseline e separazione della logica

Obiettivo: preparare il codice esistente senza cambiare l'esperienza utente.

- creare la solution e i progetti `Contracts` e `Core`;
- definire interfacce per scansione, inventario e installazione;
- estrarre gradualmente dal `MainViewModel` il coordinamento non grafico;
- mantenere invariato `ElevatedUpdateRunner` finché i test non coprono il nuovo confine;
- aggiungere test di regressione su scansione, conversione `UpdateItem` → `UpdatePlan` e risultati;
- documentare quali operazioni richiedono contesto utente o amministratore.

Uscita: app locale equivalente alla 1.0.8 e tutti i test verdi.

### Fase 1 — Agente locale senza rete

Obiettivo: validare servizio, helper utente e coda senza superficie LAN.

- creare `UpdateCenter.Agent` come servizio Windows;
- creare il canale named pipe con ACL e protocollo versionato;
- creare `SessionHelper` e gestione della sessione utente configurata;
- implementare lock globale, coda, persistenza e recupero dopo crash;
- collegare scansione e piano in modalità locale di test;
- aggiungere installer/disinstaller reversibile del servizio in build di sviluppo.

Uscita: scansione e installazione controllata sullo stesso PC tramite agente, rete disabilitata.

### Fase 2 — Discovery, pairing e sola lettura

Obiettivo: rendere visibili e consultabili PC autorizzati, senza installazioni remote.

- creare `RemoteClient`;
- implementare discovery e aggiunta IP manuale;
- implementare certificati, pairing, pinning, revoca e rate limiting;
- esporre solo stato e capacità;
- creare pagina WPF Gestione rete e dettaglio PC;
- verificare profili firewall e comportamento su rete Pubblica/Privata.

Uscita: PC associabili e consultabili; nessun endpoint remoto può modificare il sistema.

### Fase 3 — Scansione remota

Obiettivo: ottenere risultati completi e coerenti con la scansione locale.

- API operazioni asincrone;
- scansione software nel profilo configurato tramite helper;
- scansione driver/runtime tramite componente corretto;
- serializzazione DTO indipendente dai modelli WPF;
- annullamento, timeout, riconnessione e diagnostica;
- test con utente connesso, disconnesso e più utenti.

Uscita: la console mostra gli stessi aggiornamenti che il PC remoto vede localmente, salvo differenze motivate e registrate.

### Fase 4 — Installazione remota su singolo PC

Obiettivo: installare in sicurezza un piano esplicitamente selezionato.

- permesso `InstallUpdates` separato;
- piano composto da ID opachi e legato a scansione/TTL;
- rivalidazione locale di disponibilità, sorgente, hash, firma e compatibilità;
- avanzamento, pausa, ripresa e annullamento sicuro;
- gestione perdita connessione e recupero dello stato;
- conferme di rischio equivalenti a quelle locali;
- nessun riavvio remoto.

Uscita: installazione completa e auditabile su un PC associato.

### Fase 5 — Gruppi e operatività

Obiettivo: gestire più PC senza sovraccaricare rete o interrompere il lavoro.

- selezione multipla e riepilogo per dispositivo;
- limite concorrenza, coda e retry manuale;
- filtri online/offline/esito/riavvio;
- cronologia centralizzata locale;
- esportazione report privo di segreti;
- localizzazione italiana/inglese e accessibilità.

Uscita: aggiornamento controllato di un piccolo gruppo di PC LAN.

### Fase 6 — Hardening e rilascio preview

Obiettivo: produrre una preview installabile e reversibile.

- threat modeling e revisione di tutti gli endpoint;
- test di replay, brute force pairing, certificato errato e controller revocato;
- test firewall su rete Pubblica/Privata e cambio profilo;
- test crash/riavvio durante scansione e installazione;
- test di upgrade/downgrade del protocollo;
- firme artefatti, checksum, installazione/rimozione servizio;
- documentazione utente, privacy e risoluzione problemi;
- build `PREVIEW` separata dalla stabile.

Uscita: preview limitata a tester consapevoli; funzione ancora opt-in.

## Strategia di test

### Test automatici

- serializzazione e compatibilità di ogni DTO;
- validazione permessi e stato pairing;
- scadenza codice, limite tentativi e anti-replay;
- selezione del profilo utente corretto;
- coda, lock, pausa e cancellazione;
- rifiuto di ID non appartenenti alla scansione;
- rifiuto di scansioni scadute o modificate;
- persistenza e recupero dopo interruzione;
- migrazioni dei file di configurazione;
- redazione dei segreti nei log.

### Matrice manuale minima

- Windows 10 e Windows 11;
- stessa subnet e subnet con discovery bloccato ma IP manuale disponibile;
- firewall Privato e Pubblico;
- controller amministratore e controller revocato;
- utente connesso, bloccato, disconnesso e cambio utente;
- PC offline durante un'operazione;
- WinGet assente/non aggiornato;
- driver con e senza riavvio;
- versioni protocollo compatibili e incompatibili;
- installazione standard e console portable.

## Criteri di accettazione dell'MVP

L'MVP è accettabile soltanto se:

1. un agente non abilitato non risponde al discovery;
2. un controller non associato non può leggere inventario né avviare operazioni;
3. la revoca interrompe l'accettazione di nuove richieste dal controller;
4. la scansione WinGet usa il profilo configurato e segnala chiaramente la sua assenza;
5. il piano remoto non consente comandi o percorsi arbitrari;
6. l'agente rivalida ogni elemento prima dell'installazione;
7. una perdita di rete non interrompe forzatamente l'installer corrente;
8. il riavvio resta una decisione esplicita sul PC;
9. rete Pubblica significa API non raggiungibile per impostazione predefinita;
10. installazione e rimozione dell'agente non danneggiano l'uso locale;
11. nessun segreto appare in log, cronologia o report;
12. app locale e smoke test esistenti non regrediscono.

## Rischi principali e mitigazioni

| Rischio | Impatto | Mitigazione |
|---|---|---|
| WinGet eseguito nel profilo sbagliato | Inventario/aggiornamenti errati | Session helper e profilo esplicito |
| Comandi remoti non autorizzati | Compromissione del PC | mTLS/pinning, allowlist, permessi e pairing fisico |
| Piano alterato o non più valido | Installazione errata | ID opachi, TTL e rivalidazione locale completa |
| Discovery bloccato dalla rete | PC non trovato | Inserimento IP manuale |
| Perdita di connessione | Stato ambiguo | Operazioni persistenti e riconciliazione tramite ID |
| Due installazioni contemporanee | Corruzione/conflitti | Lock macchina e coda unica |
| Differenze tra versioni | Errori di protocollo | Versionamento e negoziazione capacità |
| Superficie di attacco del servizio | Rischio elevato | API minima, nessuna shell, rate limit e hardening prima delle mutazioni |
| Regole firewall troppo ampie | Esposizione fuori LAN | Solo profilo Privato e interfacce autorizzate |

## Decisioni richieste prima di iniziare

Per avviare la fase 0 è richiesta l'approvazione di queste scelte:

1. **Ambito LAN soltanto** per la prima versione.
2. **Agente installato come servizio Windows**; la portable resta principalmente console.
3. **Helper nella sessione utente** per garantire correttezza WinGet.
4. **Pairing fisico con codice temporaneo e conferma sul PC remoto**.
5. **Nessun riavvio remoto nell'MVP**.
6. **Prima release sola lettura**, seguita dalle installazioni solo dopo la revisione di sicurezza.
7. **Massimo iniziale di 2 installazioni simultanee** nella gestione di gruppo.
8. **Porte proposte 47381/UDP e 47382/TCP**, modificabili prima del rilascio.

## Ordine di approvazione consigliato

- Approvazione A: architettura e confini dell'MVP.
- Approvazione B: esperienza pairing e permessi.
- Approvazione C: mockup della pagina Gestione rete.
- Approvazione D: fase 0 e refactoring senza comportamento nuovo.
- Approvazione E: preview sola lettura.
- Approvazione F: abilitazione installazioni remote.

Ogni approvazione produce una build o un documento verificabile. Nessuna fase successiva viene considerata implicitamente autorizzata dall'approvazione della precedente.
