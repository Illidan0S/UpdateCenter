# UpdateCenter: istruzioni di sviluppo

Repository corrente: questa directory, con `UpdateCenter.sln` (9 progetti).
Non lavorare su `Archive`, `Backups`, vecchi ZIP o pacchetti in `Builds`.
La struttura esterna è descritta in `../../LEGGIMI-STRUTTURA.txt` se presente.

## Verifica

Eseguire dalla radice del repository, su Windows x64, con l'SDK di `global.json`:

```powershell
dotnet restore UpdateCenter.sln -p:Configuration=Release
dotnet build UpdateCenter.sln -c Release --no-restore
dotnet run --project Tests/UpdateCenter.NetworkCoreTests -c Release --no-build --no-restore
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore -- --release-audit
dotnet run --project Tests/UpdateCenter.SmokeTests -c Release --no-build --no-restore
```

I test sono harness console: `dotnet test` non dimostra che siano stati eseguiti.
Tutti i modi smoke inizializzano una root dati GUID isolata prima di qualsiasi
I/O: mantenerla obbligatoria anche nei nuovi dispatch. Verificare separatamente
la root predefinita di produzione senza crearla o ripulirla. Lo smoke completo
legge hardware reale e interroga Restart Manager sul proprio helper temporaneo;
il sandbox può bloccare queste API. Non aggirare la protezione dei dati reali:
un'esecuzione nativa richiede isolamento verificato e autorizzazione della sessione.
Distinguere errori ambientali, fallimenti preesistenti e regressioni.
Non dichiarare percentuali di copertura.

`./build.ps1` pubblica Portable self-contained in `dist`; `./build-installer.ps1`
richiede ISCC 6/7 e produce Setup in `installer-dist`. Le directory di output
devono essere nuove: gli script rifiutano riuso, percorsi estranei, junction, symlink
e tag reparse sconosciuti; sono ammessi soltanto i tag Cloud OneDrive verificati.
Per build ripetute scegliere una nuova `-OutputDirectory` sotto `_output`.
La preview LAN crea una nuova sottocartella diretta di `_output/network-preview`;
non installa il servizio. Non avviare script `Agent/install*` per uno smoke test.

## Vincoli

- Leggere `git status` prima di intervenire; preservare modifiche locali e versioni.
- Conservare Windows x64 build 17763+, IT/EN, contratti JSON, impostazioni e cronologia.
- Nessuna installazione reale di software, driver, firmware o aggiornamenti nei test.
- Preservare consenso, UAC, hash/firme, allowlist delle fonti e controlli LAN.
  Firmware/BIOS devono restare esclusi; controllare anche il percorso Windows Update.
- Non introdurre telemetria o dipendenze grafiche prima di valutare Fluent WPF nativo.
- Nessun commit, push, tag, merge o pubblicazione senza consenso esplicito.

## Delega e skill

I quattro revisori sono in `.codex/agents/*.toml`; ereditano modello e permessi
della sessione. Il divieto di modifica è un'istruzione, non un sandbox separato.
Per un audit, assegnare incarichi in sola lettura e consolidare tutti i risultati
prima di modificare codice; con tre slot eseguire il quarto appena se ne libera uno.
Per implementazioni assegnare proprietà esclusiva dei file a un solo agente;
usare un revisore indipendente prima dell'accettazione.

Le tre skill Microsoft sono fissate in `.agents/skills`; provenienza e limiti
sono in `docs/MODERNIZATION-2026-11-04.md`. Le istruzioni dell'utente prevalgono
su commit automatici o comandi di test generici suggeriti dalle skill.
Per XAML usare `.agents/skills/updatecenter-wpf-visual-qa/SKILL.md`.
