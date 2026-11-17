# UpdateCenter 2.0.0

[Italiano](#italiano) · [English](#english) · [Confronto con la 1.1.5 / Compare with 1.1.5](https://github.com/Illidan0S/UpdateCenter/compare/v1.1.5...v2.0.0)

## Italiano

UpdateCenter 2.0.0 passa da .NET 8 a .NET 10, rinnova l’interfaccia e rende più chiaro cosa è possibile aggiornare, quali operazioni richiedono conferma e quali installazioni sono state effettivamente verificate.

### Nuove funzionalità

- **Novità per singolo aggiornamento.** Il pannello della pagina Aggiornamenti mostra le note o i riferimenti del provider per la versione rilevata, quando disponibili.
- **Aggiornamenti gestibili separati dagli altri.** Gli elementi selezionabili compaiono per primi. La sezione espandibile **Non gestibili automaticamente** mostra gli altri elementi e le ragioni del blocco.
- **Ripresa delle verifiche WinGet.** L’app riprende le verifiche pendenti supportate all’avvio o durante una scansione successiva. La cronologia distingue una verifica pendente da un aggiornamento verificato.

### Miglioramenti

- **Interfaccia WPF con stile Fluent.** Navigazione, tabelle e finestre di dialogo più coerenti in italiano e inglese, nei temi chiaro e scuro. Migliorati il ridimensionamento della finestra principale e la leggibilità nei layout compatti.
- **Scansione più leggibile.** Fasi esplicite, indicazione dell’attività, annullamento e avvisi per risultati parziali. I risultati vengono deduplicati e l’avanzamento mostra percentuali solo quando sono misurabili.
- **Selezione e installazione.** Gli aggiornamenti ordinari idonei sono preselezionati, con conferme specifiche per le operazioni che le richiedono anche usando **Seleziona tutto**. Gli stati per elemento, la pausa dopo l’operazione in corso e il riepilogo con dettagli rendono più semplice seguire il gruppo di aggiornamenti.
- **Verifica degli esiti.** I controlli successivi all’installazione usano le informazioni disponibili su identità e versione. La ricerca driver tramite Windows Update distingue esiti completi, parziali ed errori, e la cronologia riporta gli esiti e le verifiche disponibili.

### Sicurezza

- **Consenso legato al piano.** L’esecuzione richiede la conferma del piano presentato e ne ricontrolla i dati prima di avviare gli installer. Per un nuovo tentativo o il passaggio alla modalità interattiva è richiesta la conferma prevista per l’operazione.
- **Controlli WinGet e driver.** Verifiche più rigorose su identità, versione, origine, installer, ambito utente o macchina, architettura e dipendenze. I controlli bloccano i downgrade e i percorsi non verificabili. Firmware, BIOS e UEFI restano esclusi dall’installazione automatica.
- **Self-update Setup e Portable.** Il Setup verifica il checksum e la continuità dell’installazione per lo stesso utente. Il Portable verifica l’asset ufficiale prima di sostituire il file, attende la conferma dell’avvio e conserva il backup per il ripristino in caso di errore.
- **Distribuzione 2.0.0 senza firma Authenticode.** L’eccezione vale per questa versione. I file `.sha256` verificano l’integrità del download. Per autenticare il publisher Windows usa una firma Authenticode attendibile. SmartScreen può mostrare un avviso per questi eseguibili. [Policy di firma e aggiornamento](https://github.com/Illidan0S/UpdateCenter/blob/v2.0.0/docs/CODE-SIGNING.md)

### Compatibilità e limiti noti

- **Windows client x64.** Setup e Portable includono .NET 10 Desktop. Setup e self-update ammettono le edizioni desktop Windows 11 previste dalla policy dalla build 26200, incluse build Insider come la 26320. Sono previste anche specifiche edizioni Enterprise, Education e LTSC/IoT della build 26100 e le edizioni Windows 10 LTSC ammesse sulle build 17763 e 19044. Windows 10 Home/Pro è fuori matrice. L’ammissione di una build non certifica la compatibilità su ogni edizione o canale Insider. [Matrice tecnica e verifiche precedenti (M8)](https://github.com/Illidan0S/UpdateCenter/blob/v2.0.0/docs/M8-RELEASE-READINESS.md#compatibilità-attuale) · [Sistemi supportati da .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- **Aggiornamenti software.** Serve App Installer con WinGet `v1.29.290` o `v1.30.300`. L’installazione automatica resta bloccata quando la versione del client o il percorso di installazione non soddisfano i controlli.
- **Anteprima LAN.** Il Controller fa parte dell’app desktop. Il servizio Agent e gli strumenti di anteprima si compilano separatamente e non sono inclusi negli asset Setup e Portable. [Guida di sviluppo](https://github.com/Illidan0S/UpdateCenter/blob/v2.0.0/docs/SVILUPPO.md#preview-tecnica-network)
- **Aggiornamento dalla 1.1.5.** Il ciclo completo mediante il pulsante di aggiornamento integrato, sia per Setup sia per Portable, resta da provare sulla release stabile. Il Setup richiede la continuità dello stesso account Windows. Il Portable legacy richiede un nome dell’eseguibile ammesso e una posizione compatibile con la sostituzione.

### Verifiche documentate

- Sono passati build Release, test Network Core e release audit. Sono state verificate versione e SHA-256 dei pacchetti 2.0.0 prodotti prima della pubblicazione. Le prove mirate sulla matrice Setup e self-update usano dati Windows simulati e controlli statici. [Verifiche GitHub Actions](https://github.com/Illidan0S/UpdateCenter/actions/workflows/release.yml)
- Su Windows 11 Insider x64 build 26320 è stato osservato l’avvio del Portable. Nello stesso ambiente, il manutentore ha confermato che l’aggiornamento manuale da 1.1.5 tramite Setup ha conservato impostazioni e cronologia. Restano da completare le prove native su Windows 11 stabile e Windows 10 LTSC, sul runner elevato via UAC e sull’installazione di un driver Windows Update non firmware.

### Download

Scegli `UpdateCenter-Setup-v2.0.0.exe` per l’installazione per utente oppure `UpdateCenter-v2.0.0-Portable.exe` per l’uso senza installazione. Ciascun file è accompagnato dal proprio `.sha256`. [GitHub Releases](https://github.com/Illidan0S/UpdateCenter/releases)

---

## English

UpdateCenter 2.0.0 moves from .NET 8 to .NET 10, refreshes the interface, and makes it easier to see what can be updated, which operations need confirmation, and which installations have been verified.

### New features

- **Release notes for individual updates.** A panel on the Updates page shows provider notes or references for the detected version when available.
- **Separate views for manageable and blocked updates.** Selectable updates appear first. Expand **Not automatically manageable** to see the remaining items and why they are blocked.
- **Resumable WinGet verification.** The app resumes supported pending checks at startup or during a later scan. History distinguishes pending verification from a verified update.

### Improvements

- **WPF interface with Fluent styling.** Navigation, tables, and dialogs are more consistent in English and Italian and in light and dark themes. The main window is easier to resize, and compact layouts are easier to read.
- **Clearer scans.** Named phases, activity feedback, cancellation, and warnings for partial results. Duplicate results are combined, and progress percentages appear only when measurable.
- **Selection and installation.** Eligible ordinary updates are preselected. Operations requiring separate approval still prompt for it when using **Select all**. Per-item status, pause after the current operation, and a summary with details make it easier to follow a batch.
- **Outcome verification.** Post-installation checks use available identity and version evidence. Windows Update driver searches distinguish complete, partial, and failed results, and history records available outcomes and verification details.

### Security

- **Consent tied to the plan.** Execution requires approval of the plan shown to the user and checks its details again before launching installers. A retry or a switch to interactive mode requires the confirmation applicable to that operation.
- **WinGet and driver safeguards.** Stricter checks cover identity, version, source, installer, user or machine scope, architecture, and dependencies. Downgrades and paths that cannot be verified are blocked. Firmware, BIOS, and UEFI remain excluded from automatic installation.
- **Setup and Portable self-update.** Setup checks the checksum and verifies that the existing installation belongs to the same Windows user. Portable checks the official asset before replacement, waits for startup confirmation, and retains a backup for recovery if startup fails.
- **2.0.0 ships without Authenticode.** The exception applies to this version. The `.sha256` files verify download integrity. Windows uses a trusted Authenticode signature to authenticate the publisher. SmartScreen may warn about these executables. [Signing and update policy](https://github.com/Illidan0S/UpdateCenter/blob/v2.0.0/docs/CODE-SIGNING.md)

### Compatibility and known limitations

- **x64 Windows clients.** Setup and Portable bundle .NET 10 Desktop. Setup and self-update admit the Windows 11 desktop editions covered by the policy from build 26200 onward, including Insider builds such as 26320. They also admit specific Enterprise, Education, and LTSC/IoT editions of build 26100, and eligible Windows 10 LTSC editions on builds 17763 and 19044. Windows 10 Home/Pro is outside the matrix. Admission of a build does not establish compatibility across all editions or Insider channels. [Technical matrix and earlier verification (M8)](https://github.com/Illidan0S/UpdateCenter/blob/v2.0.0/docs/M8-RELEASE-READINESS.md#compatibilità-attuale) · [.NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- **Software updates.** App Installer with WinGet `v1.29.290` or `v1.30.300` is required. Automatic installation remains blocked when the client version or installation path fails the checks.
- **LAN preview.** The desktop app includes the Controller. The Agent service and preview tools are built separately and are outside the Setup and Portable assets. [Development guide](https://github.com/Illidan0S/UpdateCenter/blob/v2.0.0/docs/SVILUPPO.md#preview-tecnica-network)
- **Upgrading from 1.1.5.** The full in-app update path for both Setup and Portable still needs to be tested against the public stable release. Setup requires the same Windows account. The legacy Portable requires an accepted executable name and a location that permits replacement.

### Documented verification

- The Release build, Network Core tests, and release audit passed. Version metadata and SHA-256 checksums were verified on the 2.0.0 packages built before publication. Targeted Setup and self-update platform checks used synthetic Windows inputs and static checks. [GitHub Actions verification](https://github.com/Illidan0S/UpdateCenter/actions/workflows/release.yml)
- Portable startup was observed on x64 Windows 11 Insider build 26320. In the same environment, the maintainer confirmed that a manual Setup upgrade from 1.1.5 preserved settings and history. Native validation on stable Windows 11 and Windows 10 LTSC remains pending, as do UAC elevation of the runner and installation of a non-firmware driver through Windows Update.

### Download

Choose `UpdateCenter-Setup-v2.0.0.exe` for per-user installation or `UpdateCenter-v2.0.0-Portable.exe` for use without installation. Each file has a matching `.sha256` checksum. [GitHub Releases](https://github.com/Illidan0S/UpdateCenter/releases)
