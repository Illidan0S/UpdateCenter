# Rapporto seconda tranche — LAN in sola lettura

Data: 02/08/2026  
Base: Update Center 1.0.8  
Stato: backend e Console tecnica completati; UI WPF non ancora integrata

## Risultato

Un PC Controller può ora trovare nella LAN un Update Center Agent abilitato, associarlo tramite codice monouso e richiedere stato o scansione remota. Non esiste alcun endpoint di installazione remota.

## Funzioni completate

- Discovery IPv4 tramite UDP 47381 su Ethernet o Wi‑Fi.
- Risposta discovery limitata a identità Agent, versione, porta e stato di associazione.
- Aggiunta manuale tramite indirizzo IP nella Network Console.
- Configurazione opt-in; rete disabilitata per impostazione predefinita.
- Regole firewall limitate al profilo Privato e al solo eseguibile Agent.
- API HTTPS su TCP 47382.
- Certificato Agent generato localmente e protetto con DPAPI.
- Pairing con codice casuale di 8 cifre, durata 5 minuti, massimo 5 tentativi e consumo monouso.
- Un solo Controller ammesso; secondo codice bloccato finché il Controller non viene revocato.
- Chiave privata Controller protetta con DPAPI e mai inviata all'Agent.
- Certificato pubblico Controller registrato durante il pairing.
- Pinning SHA‑256 del certificato Agent.
- Richieste firmate RSA con timestamp, nonce e hash del corpo.
- Cache anti-replay limitata e scadenza temporale di 2 minuti.
- Stato remoto, avvio scansione e lettura operazione.
- Nessun comando generico, shell, installazione, riavvio o trasferimento file.

## Collaudi eseguiti

- Servizio reale eseguito come `LocalSystem`.
- Session Helper avviato correttamente nella sessione utente.
- Scansione di servizio: 22 aggiornamenti, 156 driver inventariati, 12 runtime, zero avvisi.
- Servizio rimosso; zero processi Agent/Helper, zero regole firewall e porta 47382 chiusa.
- Discovery LAN: Agent trovato e mostrato prima come `da associare`, poi come `associato`.
- Pairing HTTPS firmato: riuscito.
- Stato remoto firmato: riuscito.
- Scansione HTTPS firmata: 22 aggiornamenti, 156 driver, 12 runtime, zero avvisi.
- Richiesta HTTPS senza firma: respinta con HTTP 403.
- Tentativo di creare un secondo Controller: respinto.
- Revoca Controller e disabilitazione rete: riuscite; porta chiusa.

## Limiti attuali

- La funzione è utilizzabile soltanto dalla Console tecnica, non dalla UI WPF.
- Il test ha usato due processi sullo stesso PC e le interfacce LAN locali; resta necessario un test fisico tra due PC.
- IPv6 non è ancora incluso nel discovery.
- Non esistono ancora cronologia centralizzata e dashboard dispositivi.
- Non è possibile installare aggiornamenti da remoto.

## Prossimo checkpoint

1. test fisico Controller–Agent su due PC Windows 10/11 collegati via Ethernet o Wi‑Fi;
2. integrazione della pagina Gestione rete nella WPF;
3. gestione elenco dispositivi, offline e riconciliazione operazioni;
4. revisione sicurezza prima di progettare l'installazione remota.
