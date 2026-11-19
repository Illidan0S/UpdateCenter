# UpdateCenter — checkpoint finale WPF, 8 novembre 2026

La modernizzazione grafica è conclusa e approvata. Questo checkpoint salva il lavoro; non avvia nuove attività, audit, build o test applicativi.

## Identità e stato

- Repository: `C:\Users\USER\OneDrive\Desktop\UpdateCenter\Source\UpdateCenter`.
- Branch: `feature/wpf-fluent-pilot`.
- Commit del checkpoint: il commit che introduce questo documento, con messaggio `chore(ui): checkpoint Fluent WPF modernization for 2.0`. Ricavarlo con `git log -1 --format=%H -- docs/CHECKPOINT-WPF-2026-11-08.md`. SHA completo e verifica finale sono riportati anche in `CHECKPOINT.json` e `CHECKPOINT-NUOVE-CHAT.md` nel backup indipendente. Non si inserisce lo SHA di un commit dentro il suo stesso contenuto.
- Parent/checkpoint .NET 10: `c0b3b1283f1f28525d62504877ffe4361d18d130`, preservato nella storia. Il branch originale `modernization/net10-v1.2.0` rimane su quel commit.
- **Prossima versione pubblica: 2.0.0, non 1.2.0.** I riferimenti a 1.2.0 nel rapporto storico e nel nome del branch precedente non definiscono più la roadmap.
- Versione tecnica conservata: `1.1.5`; Assembly/FileVersion `1.1.5.0`. Cambiarla soltanto durante la preparazione della release.
- Possibile cambio del nome e dell'identità del prodotto: decisione ancora da prendere prima della pubblicazione. Nessun nome alternativo approvato.
- Un solo commit locale autorizzato per questo salvataggio; nessun push, merge, rebase, tag o release.

## Fondazione tecnica completata

Migrazione a .NET 10 e hardening già completati nel parent. `global.json`: SDK `10.0.100`, roll-forward `latestFeature`, prerelease disabilitate. Solution WPF nativa Windows x64, nove progetti: UpdateCenter, Contracts, Core, RemoteClient, Agent, SessionHelper, NetworkConsole, SmokeTests, NetworkCoreTests. Compatibilità dichiarata Windows build 17763+; validazione reale Windows 10/11 ancora necessaria prima della release.

Il rapporto `docs/MODERNIZATION-2026-11-04.md`, soprattutto la sezione 9, descrive le correzioni di hardening. Le criticità iniziali risolte nella stessa relazione non devono essere riaperte automaticamente.

## Sezioni approvate

1. Impostazioni: Aspetto, Lingua, sicurezza, software, aggiornamento app, avvio/scansioni, notifiche, progetto, salvataggio e scorrimento.
2. Home/Dashboard, barra laterale e intestazione: gerarchia, stato reale, icone, focus, navigazione compatta, versione e controlli finestra. Corretto il taglio delle etichette lunghe.
3. Aggiornamenti, Driver e chipset, Cronologia: tabelle compatte, filtri/selezioni esistenti, versioni, avvisi, stati vuoti, dettagli e virtualizzazione.
4. Hardware e Gestione rete: CPU/GPU/RAM/OS/storage/SMART già disponibili; PC, autorizzazione, attività, risultati e dialoghi LAN. Nessun nuovo sensore; il modello non espone una scheda madre separata.
5. Dialoghi operativi: conferma locale/remota, rischio, recupero processi WinGet, aggiornamento applicazione, avanzamento e risultati. Dettagli tecnici copiabili, footer adattabili, focus iniziale prudente, Escape sugli handler esistenti, nessun Enter globale che autorizzi l'installazione. Avanzamento ed errore app portati nell'area visibile anche con note lunghe.

Persistenza conservata: Aspetto, lingua e frequenza salvano l'intero oggetto preferenze quando cambiano; le checkbox non salvano da sole; Salva impostazioni salva tutto e aggiorna la visibilità delle notifiche. Pausa applicata fra elementi; chiudere l'avanzamento attivo lo nasconde, non annulla l'operazione. Gli esiti derivano dal ledger esistente, non da metriche inventate.

## File e risorse

- `MainWindow.xaml` / `.xaml.cs`: pagine, shell e integrazione delle risorse locali.
- `Styles/Shared.xaml`, `Appearance.xaml`, `Settings.xaml`, `Shell.xaml`, `Operations.xaml`, `HardwareNetwork.xaml`, `Dialogs.xaml`: risorse esplicite, Fluent WPF nativo, nessuna libreria UI aggiunta. Dizionari riutilizzati e stili circoscritti, senza sostituzione globale indiscriminata.
- `Services/LocalizationService.cs`: IT/EN e preservazione dei binding durante la localizzazione; `ManagementTextConverter.cs`: presentazione LAN localizzata; `DialogPresentation.cs`: tema, dimensionamento e tastiera dei dialoghi.
- Coppie XAML/code-behind: `AgentFingerprintWindow`, `AgentSetupWindow`, `ConnectionRequestWindow`, `NetworkPairingWindow`, `UpdateConfirmationWindow`, `UpdateProgressWindow`, `AppUpdateWindow`, `WinGetProcessRecoveryWindow`.
- `Tests/UpdateCenter.SmokeTests/`: dispatch in `Program.cs`; `LanguageUiTests`, `SettingsUiTests`, `HomeShellUiTests`, `OperationsUiTests`, `HardwareNetworkUiTests`, `DialogUiTests`; fixture `OperationsUiFixture`, `HardwareNetworkUiFixture`, `DialogUiFixture`; `VisualQaSafety`.
- `VisualQaSafety` è solo test: scollega comandi/handler reali negli host QA interessati. Il test self-update inietta un transport HTTP che rifiuta le richieste. Un flag smoke sconosciuto termina con exit 2, senza ricadere nello smoke generale.
- In questo checkpoint sono aggiunti soltanto questo documento e `docs/WPF-VERIFICATION-2026-11-08.md`; il codice approvato viene salvato senza ulteriori modifiche.

## Skill e revisori conservati

Leggere prima `AGENTS.md`. Skill di progetto già versionate:

- `.agents/skills/updatecenter-wpf-visual-qa/SKILL.md`;
- `.agents/skills/migrate-dotnet8-to-dotnet9/SKILL.md`;
- `.agents/skills/migrate-dotnet9-to-dotnet10/SKILL.md`;
- `.agents/skills/test-gap-analysis/SKILL.md` e relativi riferimenti/licenza.

Profili `.codex/agents/`: `architecture-reviewer.toml`, `security-update-reviewer.toml`, `quality-testing-reviewer.toml`, `wpf-ux-reviewer.toml`. Nomi interni con underscore. Sono revisori in sola lettura per istruzione, ereditano modello e permessi della sessione; non costituiscono sandbox autonomi. Nessun subagente usato per il checkpoint. Le skill globali dell'account e i processi delle vecchie chat non fanno parte del repository né vanno presupposti disponibili nella nuova chat.

## Verifiche già effettuate e limiti

Vedere `WPF-VERIFICATION-2026-11-08.md`: build Release dei nove progetti senza errori/avvisi, harness UI mirati, localizzazione, temi, persistenza, navigazione, tastiera, 720×500, dati vuoti/numerosi e riciclo righe. Controlli su successo, fallimento, annullamento, pausa, negazione e condizioni di abilitazione. Sono risultati delle milestone precedenti, non test rieseguiti durante il checkpoint.

Screenshot reali da host isolati e dati simulati. Le panoramiche complete Impostazioni sono composizioni dichiarate di catture reali; i render DPI 150/200% di Lingua/Home sono simulazioni, non prove su monitor fisici. Le immagini storiche documentano la rispettiva milestone e possono precedere rifiniture successive. Nell'ultima milestone Computer Use ha interrotto l'acquisizione aggiuntiva della pausa perché non poteva verificare l'URL attivo; non considerarla eseguita.

Nessuna regressione residua rilevata nei controlli eseguiti, senza affermare copertura completa. Restano aperti:

- Windows 10/11 reali o VM, DPI fisici 150/200%, cambio monitor, contrasto elevato e screen reader completo; supporto globale al contrasto elevato non dimostrato.
- Installazioni/upgrade reali Setup e Portable, UAC, interruzione/ripresa/riavvio e LAN reale con servizio/consenso. Lo smoke LAN socket 10013 bloccato durante le milestone UI non è un PASS e non va ripetuto nel medesimo sandbox.
- Firma di produzione, identità publisher e transizione dalla 1.1.5 unsigned/diverso certificato: gate documentati in `docs/CODE-SIGNING.md`. Artefatti locali unsigned non sono una release pronta.
- Store Agent: owner non fidato/reparse point rifiutati; migrazioni amministrative e ProgramData da verificare in VM. L'eccezione OneDrive Cloud vale per output di build, non per i segreti Agent. Replay/nonce attraverso riavvio del servizio e associazioni pregresse non sono certificati retroattivamente dalle prove attuali.
- Segnalazione storica S5 fuori dal redesign: `OfficialDriverPackageService.DownloadVerified` chiama `VerifyHash` mentre il writer `FileShare.None` è ancora aperto. Catalogo ufficiale driver attualmente vuoto; mantenere il rilievo latente, da trattare con test isolato prima di abilitarlo. Nessuna correzione introdotta nel checkpoint.

## Sicurezza da preservare

Firmware/BIOS/UEFI esclusi anche sul candidato Windows Update fresco; versione WinGet esplicita e target non verificabili rifiutati; sorgenti consentite, hash/firme/publisher e consenso invariati. Non modificare TLS/pinning fuori banda, certificati, ACL, owner, SID/sessione, autorizzazioni, replay/revoca, pipe, protocolli o comandi remoti senza incarico specifico. Non aggirare reparse point/OneDrive, UAC, sandbox o antivirus. AppPaths dei test deve essere GUID isolato prima di I/O. Niente installer reali, servizi, firmware o PC remoti per verificare la grafica. I test sono harness console: `dotnet test` non prova che siano stati eseguiti.

## Backup e ripristino

Destinazione indipendente: `C:\Users\USER\UpdateCenterBackups\wpf-ui-2026-11-08`.

- `UpdateCenter-all-refs.bundle`: storia raggiungibile e riferimenti locali/remoti/tag, compreso lo stash storico, senza applicarlo. Non include configurazione Git, credenziali, hook, reflog o oggetti irraggiungibili.
- `UpdateCenter-source.zip`: albero completo del commit, ottenuto con `git archive`, inclusi skill/profili/documenti versionati; nessun binario o dato di esecuzione.
- `UpdateCenter-qa-evidence.zip`: selezione ispezionata di immagini, rapporti/log pertinenti e sorgenti degli host QA Operations/Hardware-LAN/Dialoghi resi inerti; nessun bin/obj, cache, fixture JSON generata, chiave o certificato. Gli host restano evidenze da ricostruire nei percorsi originali sotto `_output`, non eseguibili distribuiti.
- `CHECKPOINT-NUOVE-CHAT.md`, `CHECKPOINT.json`, `SHA256SUMS.txt`, `INTEGRITY.json`, `QA-MANIFEST.json`, `SOURCE-MANIFEST.json`: handoff con SHA finale, inventario, hash ed esiti effettivi. Questi file sono generati dopo il commit.

Ripristino in una directory **nuova**, fuori OneDrive, senza sovrascrivere l'originale:

```powershell
$backup = 'C:\Users\USER\UpdateCenterBackups\wpf-ui-2026-11-08'
$checkpoint = (Get-Content -LiteralPath "$backup\CHECKPOINT.json" -Raw | ConvertFrom-Json).commit
git clone --mirror "$backup\UpdateCenter-all-refs.bundle" C:\Users\USER\UpdateCenterRestore.git
git -C C:\Users\USER\UpdateCenterRestore.git fsck --full
git clone -b feature/wpf-fluent-pilot C:\Users\USER\UpdateCenterRestore.git C:\Users\USER\UpdateCenterRestore
git -C C:\Users\USER\UpdateCenterRestore rev-parse HEAD
# Il risultato deve coincidere con $checkpoint. Confrontare anche gli hash in SHA256SUMS.txt.
```

Il mirror conserva tutti i riferimenti; il checkout è la copia di lavoro. In alternativa estrarre lo ZIP sorgente in una destinazione nuova: contiene `UpdateCenter/`, ma non `.git`. Le evidenze si estraggono separatamente. Il bundle non contiene URL di remote/configurazioni con credenziali: riconfigurare l'eventuale remoto solo quando richiesto. Per dettagli dei controlli realmente effettuati consultare `INTEGRITY.json`.

## Roadmap concordata — non avviata

1. Ottimizzazione misurabile delle scansioni.
2. Miglioramento del rilevamento e della deduplicazione dei pacchetti.
3. Maggiore efficienza e affidabilità delle installazioni.
4. Sicurezza delle sorgenti, dei download e degli aggiornamenti.
5. Gestione robusta di interruzioni, errori, annullamenti e riavvii.
6. Prestazioni CPU/RAM, concorrenza e reattività UI.
7. Verifiche reali su Windows 10/11, DPI, accessibilità, UAC e LAN.
8. Preparazione della versione **2.0.0**, installer, firma e aggiornamento dalla **1.1.5**.
9. Valutazione di un nuovo nome e dell'identità del prodotto.

Per riprendere: verificare stato/branch/SHA e leggere questi due documenti più `AGENTS.md`; scegliere poi una priorità con l'utente. Non ricominciare redesign approvati, migrazione o audit generali. Non interpretare la roadmap come autorizzazione a implementarla automaticamente.
