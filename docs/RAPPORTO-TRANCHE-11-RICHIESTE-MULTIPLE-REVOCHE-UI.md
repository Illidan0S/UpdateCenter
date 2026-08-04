# Rapporto tranche 11 - richieste multiple, revoche e UI

Data: 02/08/2026

## Modifiche completate

- I nuovi PC non autorizzati trovati dalla ricerca vengono spuntati automaticamente.
- Il comando di collegamento usa tutti e soltanto i PC spuntati e invia le richieste con concorrenza limitata.
- Rendere un PC gestibile abilita stabilmente la ricezione delle richieste; ogni richiesta richiede comunque l'approvazione locale.
- La scelta di disabilitare le richieste viene conservata anche dopo il riavvio.
- Il Controller verifica le autorizzazioni durante la permanenza nella pagina Gestione rete, con ciclo di circa 3 secondi.
- Una revoca elimina l'autorizzazione locale, invalida i risultati precedenti e mostra il dispositivo come non autorizzato.
- Gli stati transitori delle richieste vengono cancellati dopo accettazione o revoca, evitando testi contraddittori.
- Il filtro risultati parte da `Tutti i dispositivi` e non cambia quando si evidenzia un PC.
- Rimane solo il filtro per PC; i comandi di selezione e aggiornamento sono stati spostati in basso come nella pagina Aggiornamenti.
- Gli aggiornamenti con rimozione preventiva restano esclusi da `Seleziona tutto` e usano la stessa avvertenza di rischio della pagina Aggiornamenti.

## Verifica

- Build Release: completata con 0 errori e 0 avvisi.
- Test Network Core: superati.
- Test degli stati autorizzato/revocato: superati prima dei controlli hardware.
- Lo smoke test completo si ferma sul controllo preesistente dell'inventario driver quando l'ambiente di esecuzione non restituisce driver.
