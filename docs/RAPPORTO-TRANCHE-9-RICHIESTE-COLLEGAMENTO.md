# Rapporto tranche 9 - richieste di collegamento e risultati leggibili

Data: 02/08/2026

## Collegamento rapido approvato dal dipendente

- Il PC da gestire può abilitare per 15 minuti la ricezione delle richieste.
- Il Controller rileva questa disponibilità tramite discovery LAN e può inviare la richiesta a un PC oppure a più PC selezionati.
- Sul PC del dipendente Update Center mostra una finestra con nome, indirizzo e identità del Controller, permessi richiesti e scadenza.
- L'utente può accettare o rifiutare. Nessuna richiesta viene approvata automaticamente.
- Dopo l'accettazione il PC mostra chiaramente `Connesso a <nome Controller>`.
- Il codice monouso a 8 cifre resta disponibile come metodo alternativo.
- Resta valido il vincolo di un solo Controller autorizzato per Agent; il protocollo è predisposto per future estensioni dei ruoli.

## Scalabilità

- Non è imposto un limite fisso al numero totale di PC salvati dal Controller.
- Scansioni e aggiornamenti usano al massimo quattro PC contemporaneamente; gli altri vengono accodati per evitare picchi di CPU, memoria e rete.
- Nell'associazione multipla il limite a quattro riguarda solo l'invio iniziale. L'attesa delle decisioni degli utenti avviene in parallelo, quindi 20-30 notifiche non vengono bloccate dalle prime quattro.
- Le liste lunghe usano virtualizzazione e raccolte indicizzate per ID e indirizzo.
- Ogni Agent accetta al massimo 50 richieste pendenti, applica rate limiting per indirizzo e conserva gli stati terminali soltanto per un periodo breve.

## Interfaccia Gestione rete

- Rimossa l'apertura automatica del vecchio dialogo con codice dopo la ricerca.
- Il comando per riga usa `Richiedi` quando il PC ha abilitato la modalità rapida e `Codice` negli altri casi.
- Ogni riga mostra lo stato della richiesta: invio, attesa, accettazione, rifiuto, scadenza o errore.
- L'elenco dispositivi può essere ampliato fino al 70% dell'altezza disponibile e mantiene lo scorrimento virtualizzato.
- I risultati rimangono raggruppati per PC.
- Le colonne visibili sono `Aggiornamento`, `Versione attuale`, `Nuova versione` e `Stato`.
- Gli elementi sono ordinati, all'interno di ogni PC, come Driver, Software, Runtime e poi alfabeticamente.
- Driver, Software e Runtime hanno colori distinti; la colonna di conferma non è presente.

## Sicurezza

- La richiesta presenta il certificato pubblico del Controller e ne verifica l'identificativo.
- Il Controller blocca il certificato HTTPS osservato dell'Agent durante tutto il flusso.
- Il token di polling è casuale a 256 bit e viene conservato dall'Agent soltanto come hash SHA-256.
- L'approvazione locale usa una pipe separata che espone esclusivamente lettura e decisione delle richieste, senza accesso ai comandi amministrativi dell'Agent.
- La funzione continua a rispettare la LAN autorizzata senza cambiare il profilo di rete di Windows.

## Verifiche

- Build Release dell'intera soluzione: completata senza errori e senza avvisi.
- Test Network Core: superati.
- Smoke test applicativi: superati.
- Controllo grafico con dati temporanei su finestra compatta e massimizzata: colonne allineate, gruppi separati e colori leggibili.
- I dati e il codice usati esclusivamente per il controllo grafico sono stati rimossi prima della build finale.

