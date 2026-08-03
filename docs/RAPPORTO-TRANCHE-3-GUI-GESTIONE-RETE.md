# Rapporto tranche 3 — interfaccia Gestione rete

## Obiettivo

Rendere le funzioni LAN della preview utilizzabili direttamente dall'interfaccia WPF di Update Center, lasciando fuori dall'ambito l'installazione remota.

## Funzioni integrate

- Ricerca degli Agent via broadcast IPv4 sulla LAN (Ethernet e Wi-Fi).
- Elenco dei PC rilevati e degli Agent già associati salvati localmente.
- Inserimento manuale di indirizzo IP e porta.
- Associazione tramite codice temporaneo di 8 cifre.
- Riutilizzo del certificato Controller protetto con DPAPI e del pin del certificato Agent.
- Controllo dello stato e della raggiungibilità del PC selezionato.
- Avvio e monitoraggio di una scansione remota.
- Visualizzazione in sola lettura degli aggiornamenti software, driver e runtime rilevati.
- Aggiornamento sicuro dell'indirizzo di un Agent già noto quando cambia IP, solo se ID e impronta del certificato coincidono.
- Configurazione del PC Agent dall'app tramite una finestra amministrativa separata con UAC.
- Installazione/abilitazione dell'Agent, generazione del codice, revoca del Controller e disabilitazione della rete dalla GUI.

## Limiti intenzionali

- Un solo Controller autorizzato per Agent.
- Nessuna installazione, riavvio o modifica remota.
- Nessuna gestione multi-Controller in questa tranche.
- La configurazione iniziale dell'Agent resta un'operazione locale amministrativa, ora avviabile dalla GUI.

## Compatibilità

- Windows 10 x64 1809 o successivo.
- Windows 11 x64.
- Profilo di rete Windows Privato.

## Verifica eseguita

- Compilazione Release dell'intera soluzione: 0 errori, 0 avvisi.
- Test Network Core superati.
- Smoke test completi superati fuori sandbox, compreso l'inventario hardware reale.
- Avvio e reattività verificati sia per la console Controller sia per la finestra Agent.
- Preview rigenerata: circa 194 MiB, senza duplicazione del runtime WPF.
- Al termine: nessun servizio, processo o regola firewall di test lasciato attivo.
- Prova reale della nuova GUI consigliata con due PC sulla stessa LAN; il motore sottostante era già stato collaudato end-to-end nella tranche precedente.
