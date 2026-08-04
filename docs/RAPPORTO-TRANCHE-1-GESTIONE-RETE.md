# Rapporto prima tranche — Gestione rete

Data: 02/08/2026  
Base: Update Center 1.0.8  
Stato: preview tecnica locale, nessun listener di rete

> Aggiornamento: il collaudo amministrativo indicato come passo successivo è stato completato nella seconda tranche; vedere `RAPPORTO-TRANCHE-2-LAN-SOLA-LETTURA.md`.

## Risultato

La prima tranche prepara i confini modulari e dimostra una scansione attraverso la catena locale Agent → Session Helper → motori Update Center esistenti. L'app WPF stabile continua a compilare e gli smoke test storici restano verdi.

## Componenti aggiunti

- `UpdateCenter.Contracts`: protocollo e DTO indipendenti dalla UI.
- `UpdateCenter.Core`: framing JSON limitato, client pipe, lock e registro operazioni.
- `UpdateCenter.Agent`: host compatibile con servizio Windows, coda singola e persistenza.
- `UpdateCenter.SessionHelper`: scansione nel profilo dell'utente interattivo.
- `UpdateCenter.NetworkCoreTests`: test del nuovo nucleo.
- `UpdateCenter.sln`: compilazione coordinata dei sette progetti.
- `build-network-preview.ps1`: pubblicazione self-contained x64.

## Sicurezza e limiti già applicati

- Listener LAN assente e stato `NetworkListenerEnabled = false`.
- Named pipe locale; in modalità servizio l'accesso di controllo è limitato a `LocalSystem` e amministratori.
- Pipe Agent–Helper limitata a `LocalSystem` e al SID dell'utente interattivo.
- Messaggi con prefisso di lunghezza e massimo 8 MiB.
- Massimo 10 client locali contemporanei.
- Una sola scansione/installazione per macchina alla volta.
- Nessun comando shell arbitrario nel contratto.
- Massimo 256 operazioni persistite; recupero delle operazioni interrotte come fallite dopo il riavvio.
- In servizio, il Session Helper viene avviato nella sessione console attiva, evitando WinGet sotto `LocalSystem`.

## Verifiche eseguite

- Build Release completa: superata, zero warning e zero errori.
- Smoke test Update Center 1.0.8: superati.
- Test Network Core: superati.
- Publish self-contained della preview: superato.
- Probe stato Agent: versione 1.0.8, macchina corretta, rete disabilitata.
- Scansione end-to-end Agent–Helper: completata e risultato persistito.
- Arresto del processo Agent usato per il test: completato.

La scansione end-to-end nel sandbox Codex ha riportato due avvisi ambientali: `winget.exe` non accessibile e inventario CIM non leggibile. Il flusso ha comunque completato i 12 controlli runtime, serializzato il risultato e chiuso l'operazione come `CompletedWithWarnings`. La stessa disponibilità va verificata fuori dal sandbox nella prova amministrativa del servizio.

## Artefatto locale

La cartella `dist-network-preview` contiene Agent, Session Helper, runtime self-contained e script di installazione/rimozione. Non crea regole firewall e non abilita rete.

Lo script di rimozione elimina soltanto il servizio e conserva binari e dati, rendendo l'operazione recuperabile e ispezionabile.

## Non ancora eseguito

- Installazione effettiva del servizio nel sistema operativo.
- Prova del percorso `LocalSystem` → sessione utente su un'installazione reale.
- Test fisico su Windows 10; il target resta `net8.0-windows10.0.17763.0`.
- Discovery Ethernet/Wi‑Fi, HTTPS, pairing e controller.
- Lettura remota o installazioni remote.
- Modifiche alla UI WPF per la pagina Gestione rete.

## Prossimo checkpoint

Prima di iniziare la rete, eseguire su autorizzazione esplicita il collaudo amministrativo reversibile:

1. installare il servizio dalla preview;
2. verificare stato e scansione con utente connesso;
3. verificare il comportamento senza sessione utente;
4. arrestare e rimuovere il servizio;
5. controllare che Update Center normale resti invariato.

Solo dopo questo checkpoint si potrà iniziare la tranche sola lettura con discovery e pairing.
