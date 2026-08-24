# Update Center 1.1.4

- Fix WPF `Refresh()` durante `AddNew/EditItem`.
- Esito installer separato da errori UI e verifica post-installazione.
- Verifica post-installazione WinGet.
- Stato verifica driver: `Verified`, `Failed`, `Unavailable`, `PendingRestart`.
- Ricaricamento completo di inventario e offerte driver dopo l'installazione.
- Rimozione automatica solo degli aggiornamenti verificati.
- Logging con fase, codice, esito installer e verifica.
- Gate contro richieste concorrenti nella stessa UI.
- Firma Authenticode obbligatoria sui tag Release stabili.
- Ponytail e Anthropic-Cybersecurity-Skills restano riferimenti: nessuna nuova dipendenza.
