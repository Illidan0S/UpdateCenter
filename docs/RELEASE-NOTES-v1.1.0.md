# Update Center v1.1.0

Rispetto alla release v1.0.8:

- aggiunta la gestione di rete LAN con Controller e Agent per rilevare, associare e controllare più PC autorizzati;
- aggiunte scansioni concorrenti e aggiornamenti remoti con avanzamento separato per ogni computer;
- aggiunto il riepilogo prima dell'installazione remoto, con spazio libero, dimensioni dei pacchetti, alimentazione e avvisi per ogni PC;
- migliorata la tabella Gestione rete con gruppi per PC, filtri, selezione rapida e colonne omogenee con Aggiornamenti;
- aggiunte richieste di collegamento approvabili dal PC gestito, revoca immediata e stato “Connesso a …”;
- aggiunto il rilevamento automatico del cambio di ambito WinGet, evitando installazioni duplicate come nel caso Hytale Machine/User;
- aggiunta la quarantena persistente degli aggiornamenti WinGet non applicabili, rivalutata quando cambia una versione;
- aggiunta la diagnosi e la riparazione guidata dei driver problematici con INF OEM firmati, riavvio del dispositivo e verifica finale;
- aggiunto il ripristino sicuro del dispositivo senza cancellazione forzata del pacchetto driver;
- migliorati layout, colonne, dimensioni e comportamento su schermi piccoli e grandi;
- aggiornato il protocollo Agent per trasmettere batteria, alimentazione, spazio libero, dimensioni e dettagli degli aggiornamenti remoti;
- mantenuta la compatibilità con Windows 10 versione 1809 o successiva e Windows 11 x64;
- mantenuti aggiornamenti automatici dell'app con verifica SHA-256 per installer e versione portable.

La release include:

- `UpdateCenter-Setup-v1.1.0.exe`: installer standard;
- `UpdateCenter-v1.1.0-Portable.exe`: versione portable.
