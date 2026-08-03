# Rapporto tranche 10 - stato, revoche, filtri e configurazione

Data: 02/08/2026

## Modifiche completate

- Lo stato di questo PC appare direttamente in Gestione rete; `Connesso a <nome>` è evidenziato in verde.
- Il Controller verifica ogni 12 secondi i dispositivi già autorizzati, con concorrenza limitata a 16 richieste.
- Una revoca elimina l'autorizzazione locale salvata e mostra `Non autorizzato` oppure `Collegato a un altro PC`.
- I risultati precedenti restano consultabili ma diventano non installabili e mostrano `Dispositivo scollegato`.
- Gli errori `Unauthorized` durante stato, scansione o aggiornamento attivano la stessa procedura di revoca.
- Nessun PC spuntato significa nessuna scansione: il pulsante mostra `Scansiona` ed è disabilitato.
- Il filtro per PC, tipo e ricerca delimita anche gli aggiornamenti effettivamente inviati.
- Aggiunti i comandi rapidi `Tutti`, `Nessuno`, `Driver`, `Software` e `Runtime`; agiscono solo sugli elementi mostrati.
- Il pulsante finale indica il numero di aggiornamenti e PC inclusi nell'ambito visibile.
- Rimossa l'azione `Espandi elenco`; il ridimensionamento resta disponibile tramite il separatore trascinabile.
- Configura questo PC presenta prima il collegamento rapido, poi i dettagli, il codice manuale chiuso e infine le azioni di revoca/rimozione.
- Gli stati tecnici e gli errori comuni di Windows vengono presentati in italiano.

## Sicurezza e comportamento

- Un semplice errore di rete non viene interpretato come revoca.
- La revoca viene confermata tramite discovery senza autenticazione e, se necessario, tramite una richiesta firmata.
- I risultati di una scansione precedente non possono essere riutilizzati dopo una revoca; serve un nuovo collegamento e una nuova scansione.
- La pipe accessibile all'utente espone ora anche la sola lettura della configurazione locale, oltre alle decisioni sulle richieste; i comandi amministrativi restano separati.

## Verifiche

- Build Release completa: zero errori, zero avvisi.
- Test Network Core: superati.
- Smoke test: superati, inclusa la conversione degli errori tecnici in messaggi italiani.
- Controllo grafico: Configura questo PC e Gestione rete verificate in esecuzione reale.

## Backup

`Backups/UpdateCenter-before-status-filter-ui-fixes-20260802-171500`

