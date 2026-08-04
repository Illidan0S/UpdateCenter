# Rapporto tranche 12 - riepilogo remoto e riparazione driver

Data: 02/08/2026

## Riepilogo prima dell'installazione

- `Aggiorna elementi selezionati` apre la stessa finestra usata dalla pagina Aggiornamenti.
- Dimensioni note dei pacchetti e spazio libero sono mostrati separatamente per ogni PC.
- I portatili sono elencati per nome, indicando alimentatore o batteria e percentuale quando disponibile.
- Gli aggiornamenti con rimozione preventiva riportano sempre il nome del PC interessato.
- Il Controller può includere i pacchetti rischiosi dopo conferma oppure continuare escludendoli senza tornare alla tabella.
- Il protocollo LAN passa alla versione secondaria 1.3; rimane compatibile con la stessa versione principale.

## Tabella Gestione rete

La tabella visibile usa le stesse colonne principali della pagina Aggiornamenti:
selezione, nome, tipo, priorità, versione attuale, nuova versione e stato. Driver, software e runtime mantengono colori distinti.

## Riparazione driver

- Per errori compatibili, incluso il Codice 31, Update Center associa il problema al driver installato.
- La reinstallazione è disponibile soltanto se Windows espone un INF `oemN.inf` firmato e già registrato.
- La procedura non elimina forzatamente il pacchetto: usa PnPUtil, riavvia il dispositivo quando supportato, riesegue il rilevamento e verifica nuovamente il codice.
- Se non esiste un INF sicuro da reinstallare, la stessa riga avvia la ricerca standard di Update Center tramite Windows Update e catalogo verificato dei produttori.

## Verifica

- Build Release: 0 errori, 0 avvisi.
- Test Network Core: superati.
- Nuovi test: riepilogo per-PC, batteria, dimensioni, rischio associato al PC e validazione INF superati.
- Lo smoke test completo raggiunge il controllo hardware preesistente, che nell'ambiente di test non restituisce driver installati.
