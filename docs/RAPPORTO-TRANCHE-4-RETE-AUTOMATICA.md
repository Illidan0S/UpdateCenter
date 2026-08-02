# Rapporto tranche 4 — rete automatica e ciclo di vita Agent

## Obiettivo

Eliminare la necessità di cambiare manualmente il profilo di rete Windows e rendere installazione, disattivazione e rimozione dell'Agent comprensibili anche agli utenti non esperti.

## Rete autorizzata automaticamente

- L'abilitazione rileva le interfacce attive dotate di gateway IPv4.
- L'Agent registra identificativo dell'interfaccia, gateway, sottorete e prefisso.
- Ogni richiesta UDP o HTTPS viene accettata soltanto se proviene da una sottorete registrata e la rete registrata è ancora attiva.
- Se il PC cambia rete, la gestione entra in pausa automaticamente senza modificare il profilo Pubblico/Privato di Windows.
- Le regole firewall valgono per qualsiasi profilo Windows, ma sono limitate al solo eseguibile Agent, alle porte necessarie, a `LocalSubnet` e alle interfacce rilevate.
- Edge traversal rimane bloccato.

## Ricerca semplificata

- La ricerca UDP broadcast resta il percorso più rapido.
- In parallelo viene eseguito un probe HTTPS limitato agli indirizzi della LAN locale.
- Per reti più grandi di `/24` il fallback resta limitato al blocco `/24` del Controller.
- La concorrenza è limitata e ogni probe ha un timeout breve.
- L'identità annunciata dall'endpoint HTTPS deve corrispondere all'impronta del certificato effettivamente osservato.
- Loopback e tutti gli indirizzi IPv4 locali vengono esclusi da broadcast, fallback e dispositivi salvati: un PC non elenca mai sé stesso.
- Indirizzo e porta manuali sono conservati esclusivamente nelle opzioni avanzate.

## Ciclo di vita Agent

- `Rendi questo PC gestibile`: installa o aggiorna l'Agent e autorizza la LAN corrente.
- `Disabilita gestione rete`: chiude i listener dopo il riavvio del servizio, annulla codici temporanei e rimuove le regole firewall.
- `Revoca Controller`: elimina l'autorizzazione del Controller senza disinstallare l'Agent.
- `Disinstalla Agent`: rimuove servizio, regole firewall, binari, certificati, associazioni e dati operativi dell'Agent; l'app Update Center non viene rimossa.
- Il pulsante di disinstallazione è disponibile soltanto se l'Agent o i relativi binari installati risultano ancora presenti.

## Limiti

- Una rete ospiti con AP/Client Isolation impedisce intenzionalmente la comunicazione fra dispositivi e non può essere aggirata in modalità solo LAN.
- Il fallback automatico non effettua scansioni oltre il blocco locale massimo previsto.
- Le installazioni remote restano escluse da questa tranche.

## Collaudo reale

- Installazione e abilitazione eseguite su una connessione Ethernet classificata `Public` da Windows.
- Il profilo è rimasto `Public`: Update Center non lo ha modificato.
- Regole verificate su profili Domain/Private/Public con programma Agent, `LocalSubnet`, porte 47381/47382 ed edge traversal bloccato.
- Discovery riuscita contemporaneamente verso l'Agent Ethernet locale e un Agent collegato via Wi-Fi.
- Disabilitazione verificata: servizio conservato, regole rimosse e Agent non più rilevabile.
- Riattivazione/aggiornamento idempotente verificati.
- Disinstallazione verificata: servizio, processi, regole, cartella Program Files e dati ProgramData rimossi.

## Correzioni revoca e reinstallazione Agent

- Se un Agent annuncia `HasController = false`, il Controller elimina automaticamente l'associazione locale durante la discovery.
- Un rifiuto `Unauthorized` non viene più presentato come generico errore di raggiungibilità: lo stato locale viene invalidato e l'utente riceve istruzioni per una nuova associazione.
- L'associazione viene considerata completata soltanto dopo un controllo firmato dello stato remoto.
- Le associazioni sono univoche sia per `AgentId` sia per indirizzo: reinstallare un Agent allo stesso IP sostituisce certificato e record precedenti.
- I dati legacy con più record sullo stesso indirizzo vengono normalizzati scegliendo quello con `PairedUtc` più recente.
- Aggiunto un test di regressione dedicato a sostituzione e rimozione delle associazioni Controller.
- Il caso reale con due certificati Agent sullo stesso IP è stato riprodotto: usando il record più recente, lo stato firmato del GBook è tornato raggiungibile.
- Verificata anche una scansione completa dal pacchetto distribuito verso il GBook: 24 aggiornamenti, 118 driver e 12 runtime, senza avvisi.
