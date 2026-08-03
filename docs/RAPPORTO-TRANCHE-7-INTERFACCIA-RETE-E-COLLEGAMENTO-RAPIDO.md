# Tranche 7 — Interfaccia rete e collegamento rapido

## Obiettivo

Rendere la gestione di più dispositivi leggibile, ridimensionabile e più rapida da configurare, eliminando dalla pagina principale il riquadro permanente «Connessione sicura».

## Modifiche realizzate

- L'elenco dispositivi usa tutta la larghezza disponibile.
- L'altezza iniziale dell'elenco è stata aumentata e può essere modificata con `Espandi elenco` / `Riduci elenco`.
- Un separatore trascinabile permette di distribuire liberamente lo spazio tra dispositivi e aggiornamenti.
- Ricerca e associazione sono riunite nel comando `Cerca e collega`.
- Dopo la ricerca viene selezionato automaticamente il primo PC non ancora associato.
- Ogni PC associabile espone il comando contestuale `Collega`.
- Il codice temporaneo viene inserito in una barra compatta mostrata soltanto quando serve; indirizzo e porta sono compilati automaticamente.
- Il tasto Invio conferma il codice, mentre l'indirizzo manuale rimane disponibile come opzione avanzata.
- Scansione e controllo stato sono disponibili direttamente nell'intestazione dell'elenco, senza pulsanti di scansione duplicati.
- I risultati rimangono raggruppati per dispositivo e l'intestazione indica la quantità di software, driver e runtime.
- Le versioni sono presentate come `Installata:` e `Disponibile:` invece che come numeri isolati.
- Il nome tecnico del pacchetto non occupa più una seconda riga nella tabella principale.
- Il template dei gruppi forza l'estensione delle righe a tutta larghezza, evitando colonne compresse sulla sinistra.
- Il pulsante finale usa la dicitura esplicita `Installa N aggiornamenti su N PC`.

## Responsive design

- A larghezze ridotte, i comandi dell'elenco passano su una seconda riga.
- Le colonne conservano larghezze minime leggibili.
- Il tipo viene nascosto solo sui layout più stretti; nome, versione e stato restano disponibili.
- Il separatore e il comando di espansione funzionano anche su finestre compatte.

## Verifiche

- Build Release completa: 0 errori, 0 avvisi.
- Test Network Core: superati.
- Avvio reale WPF verificato a 1440×900.
- Comando `Espandi elenco` verificato tramite automazione UI.
- Layout compatto verificato a 900×700.
- Preview pubblicata in `dist-network-preview`.

## Backup

`Backups/UpdateCenter-before-network-ui-step7-20260802-135812`
