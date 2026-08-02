# Rapporto tranche 6 — redesign Gestione rete

## Obiettivo

Riorganizzare la pagina dando priorità ai risultati, eliminando i comandi duplicati e mantenendo una visualizzazione leggibile su finestre grandi e compatte.

## Nuova gerarchia

- Due riquadri superiori: `PC rilevati` e `Associazione/Connessione sicura`.
- Una sola barra delle azioni, con testo contestuale come `Scansiona GBOOKTIZIANO` o `Scansiona 3 PC`.
- La tabella dei risultati occupa tutta la larghezza della pagina.
- `Aggiorna selezionati` è presente una sola volta nell'intestazione dei risultati.
- Il pannello di associazione si riduce automaticamente allo stato di connessione quando il PC è già autorizzato.

## Risultati multi-PC

- `Tutti i PC` mostra risultati raggruppati per dispositivo tramite gruppi comprimibili.
- Il selettore consente di filtrare un singolo PC.
- Evidenziare un PC nella tabella superiore apre automaticamente i suoi risultati, se disponibili.
- Selezioni, conferme di rischio, esiti e avanzamento restano legati al rispettivo Agent.
- L'azione finale indica numero di elementi e PC coinvolti.

## Responsive

- Layout ampio: i due riquadri superiori sono affiancati.
- Sotto 1050 px: i riquadri vengono impilati.
- Sotto 900 px: filtri secondari e colonne meno importanti vengono nascosti; restano dispositivo, aggiornamento, versione disponibile, rischio e stato.
- Sotto 740 px di altezza: la barra di stato secondaria viene nascosta per dare spazio alla tabella.
- Il pannello di un PC già associato è compatto anche su schermi piccoli.
- Verifica visiva eseguita a 1240×790 e 800×700 senza sovrapposizioni o pulsanti duplicati.

## Prestazioni

- Un'unica collection aggregata alimenta la vista filtrata.
- Raggruppamento e filtri utilizzano `ICollectionView`.
- Virtualizzazione e riciclo delle righe restano attivi anche con il raggruppamento.
