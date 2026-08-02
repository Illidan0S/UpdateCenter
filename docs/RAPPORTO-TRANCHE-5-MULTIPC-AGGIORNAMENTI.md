# Rapporto tranche 5 — multi-PC e aggiornamenti remoti

## Funzioni

- Selezione esplicita dei PC tramite checkbox.
- Scansione di massimo quattro PC contemporaneamente, con stato separato per dispositivo.
- Risultati conservati separatamente per ogni PC e mostrati quando lo si seleziona.
- Selezione degli aggiornamenti per PC e avvio sul singolo dispositivo o sui PC marcati.
- Avanzamento persistente per operazione, elemento e fase; recuperabile tramite Controlla stato.
- Stato locale in evidenza: `Connesso a NOME-CONTROLLER`.
- Tabelle rete con righe uniformi e contenuti centrati verticalmente.

## Sicurezza

- L'Agent installa esclusivamente elementi presenti in una propria scansione completata da meno di due ore.
- Identificativo e tipo devono corrispondere esattamente al risultato della scansione.
- Elementi non installabili vengono rifiutati dall'Agent.
- Gli elementi rischiosi richiedono una conferma separata nel Controller e nel protocollo.
- Una sola operazione di scansione o aggiornamento può essere attiva su ogni PC.
- Le richieste restano firmate dal certificato del Controller e limitate alla LAN autorizzata.
- Driver e operazioni che richiedono elevazione possono mostrare una conferma UAC sul PC gestito.

## Scalabilità

- Il Controller usa parallelismo limitato a quattro PC.
- Ogni Agent conserva al massimo 256 operazioni e applica la retention esistente.
- I messaggi di avanzamento contengono soltanto stato incrementale e risultato finale.
- Il limite per singola richiesta è 256 aggiornamenti.

## Compatibilità

- Windows 10 e Windows 11 x64.
- Ethernet e Wi-Fi sulla stessa LAN.
- Un solo Controller autorizzato per Agent in questa fase; gli altri PC possono rilevare l'Agent ma lo vedono come già associato e non possono controllarlo.

## Collaudo

- Build Release completa: zero errori e zero avvisi.
- Test protocollo, firma, retention, avanzamento e associazioni superati.
- Test dedicato: un elemento rischioso viene rifiutato senza conferma e accettato soltanto se appartiene alla scansione indicata.
- Smoke test locale di aggiornamenti, driver, runtime e storage superato.
- GUI 1.1.0 avviata e verificata tramite automazione: presenti i comandi di scansione multipla, aggiornamento singolo/multiplo e annullamento.
- Discovery e stato del GBook 1.0.8 restano retrocompatibili; per provare installazione e avanzamento il GBook deve aggiornare l'Agent alla 1.1.0.
- Nessun aggiornamento reale è stato installato sul GBook durante il collaudo, perché la scelta dei pacchetti deve essere esplicita dell'utente.
