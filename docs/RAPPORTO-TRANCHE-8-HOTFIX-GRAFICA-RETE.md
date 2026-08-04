# Tranche 8 — Hotfix grafica Gestione rete

## Problemi corretti

### Dispositivi rilevati

- Il modulo di associazione non viene più inserito sotto la riga del PC.
- La comparsa di un nuovo dispositivo non modifica più l'altezza interna della tabella.
- `Collega` apre una finestra modale dedicata, centrata sul Controller.
- La finestra mostra dispositivo, endpoint, codice temporaneo, stato e opzioni avanzate.
- La tabella usa colonne stabili: Dispositivo, Autorizzazione e Attività.
- Le etichette sono state abbreviate in `Autorizzato`, `Non collegato` e `Altro Controller`.
- Le barre di avanzamento compaiono soltanto durante un'operazione realmente attiva.
- Lo scorrimento orizzontale della tabella è disabilitato.

### Aggiornamenti trovati

- Il DataGrid raggruppato responsabile della misurazione errata è stato sostituito visivamente da una lista virtualizzata a griglia.
- Ogni riga e ogni intestazione di gruppo occupano tutta la larghezza disponibile.
- Le colonne sono soltanto: Aggiornamento, Versioni e Stato.
- Il tipo Software/Driver/Runtime è mostrato sotto al nome, senza una colonna compressa.
- La colonna `Conferma` è stata eliminata.
- Gli elementi che richiedono attenzione mostrano `richiede conferma` sotto al nome.
- Quando l'utente seleziona uno di questi elementi, la conferma viene richiesta in una finestra contestuale.
- Il raggruppamento per PC e la virtualizzazione rimangono attivi.

## Verifiche eseguite

- Build Release completa: 0 errori, 0 avvisi.
- Test Network Core: superati.
- Smoke test hardware, driver, runtime e storage: superati.
- Test visuale con 24 aggiornamenti e più dispositivi a 1440×900: superato.
- Test visuale a 900×700: superato, nessuna sovrapposizione.
- Apertura reale della nuova finestra `Collega dispositivo`: verificata.
- I dati artificiali di verifica sono stati rimossi prima della build finale.

## Backup

`Backups/UpdateCenter-before-network-ui-hotfix-20260802-161633`
