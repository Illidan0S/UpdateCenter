# UpdateCenter

**Versione 2.0.0.** UpdateCenter è un’app desktop per Windows che rileva e gestisce aggiornamenti di software, driver e runtime da fonti verificate. L’app mostra quali aggiornamenti può installare e perché altri richiedono un intervento manuale. L’installazione richiede l’approvazione dell’utente.

[GitHub Releases](https://github.com/Illidan0S/UpdateCenter/releases) · [Note della 2.0.0 in italiano e inglese](docs/RELEASE-2.0.0.md)

## Novità dalla 1.1.5

- Runtime **.NET 10 Desktop** incluso e interfaccia WPF aggiornata, in italiano e inglese, con temi chiaro e scuro.
- Scansione con fasi visibili, annullamento e risultati deduplicati. Gli aggiornamenti non selezionabili sono raccolti in una sezione espandibile con le motivazioni.
- Preselezione degli aggiornamenti ordinari idonei, conferme specifiche per le operazioni che le richiedono e note di versione quando il provider le rende disponibili.
- Stato per elemento, pausa dopo l’operazione in corso e riepilogo finale. L’app verifica l’esito dopo l’installazione e riprende le verifiche WinGet pendenti che può recuperare.
- Controlli più rigorosi su identità, versione, ambito di installazione, dipendenze e consenso. Verifiche aggiuntive durante il self-update di Setup e Portable.

## Funzioni principali

- Aggiornamenti software tramite WinGet e gestione dei runtime condivisi, tra cui .NET, Visual C++, DirectX, Vulkan, PhysX e WebView2 quando rilevabili e gestibili.
- Ricerca di driver e chipset tramite Windows Update e fonti ufficiali, diagnostica dei dispositivi e collegamento alle risorse NVIDIA per le GPU compatibili.
- Inventario hardware con CPU, GPU, VRAM, RAM, Windows e driver. Salute, capacità e temperatura delle unità quando disponibili.
- Filtri, cronologia e log locali. Gestione delle applicazioni che bloccano un aggiornamento e possibilità di proseguire con l’installer interattivo nei casi previsti.
- Riepilogo prima dell’installazione con spazio, alimentazione, riavvio e avvisi pertinenti. Controllo delle nuove versioni di UpdateCenter con verifica dei download.

La **gestione LAN** è un’anteprima tecnica. L’app desktop include il Controller, mentre il servizio Agent e gli strumenti di anteprima si compilano separatamente. [Guida di sviluppo](docs/SVILUPPO.md#preview-tecnica-network)

## Download e aggiornamento

| Pacchetto | Checksum |
|---|---|
| `UpdateCenter-Setup-v2.0.0.exe` | `UpdateCenter-Setup-v2.0.0.exe.sha256` |
| `UpdateCenter-v2.0.0-Portable.exe` | `UpdateCenter-v2.0.0-Portable.exe.sha256` |

Usa il **Setup** per installare l’app nel tuo profilo Windows oppure il **Portable** per eseguirla senza installazione. Scarica l’eseguibile e il checksum dalla stessa release ufficiale. [GitHub Releases](https://github.com/Illidan0S/UpdateCenter/releases)

**Dalla 1.1.5.** Il manutentore ha confermato che l’aggiornamento manuale tramite Setup 2.0.0, nello stesso profilo su Windows 11 Insider x64 build 26320, ha conservato impostazioni e cronologia. La prova completa tramite il pulsante di aggiornamento integrato nella 1.1.5 resta da eseguire sulla release stabile, per Setup e Portable. Il self-update Portable richiede un nome dell’eseguibile ammesso e una cartella che consenta la sostituzione.

## Utilizzo

1. Premi **Avvia scansione**.
2. Apri **Aggiornamenti**. Espandi **Non gestibili automaticamente** per vedere i motivi di esclusione.
3. Rivedi la preselezione o usa **Seleziona tutto**. L’app chiede le conferme specifiche necessarie.
4. Premi **Aggiorna elementi selezionati**, leggi il riepilogo e conferma. Al termine, controlla gli esiti e le eventuali verifiche pendenti.

## Requisiti e compatibilità

- **Windows client x64.** Setup e Portable includono .NET 10 Desktop. Setup e self-update controllano anche build ed edizione. Su Windows 11 le edizioni desktop ammesse partono dalla build 26200, con eccezioni specifiche per la 26100. Su Windows 10 sono ammesse specifiche edizioni LTSC delle build 17763 e 19044. Windows 10 Home/Pro è fuori matrice. [Matrice tecnica e verifiche precedenti (M8)](docs/M8-RELEASE-READINESS.md#compatibilità-attuale) · [Sistemi supportati da .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- **WinGet.** Per gli aggiornamenti software automatici serve App Installer con WinGet `v1.29.290` o `v1.30.300`. [Requisiti WinGet](https://learn.microsoft.com/en-us/windows/package-manager/winget/)
- **Connessione Internet** per le ricerche e i download. Alcune installazioni richiedono la conferma UAC.

L’ammissione tecnica di build Insider come la 26320 lascia aperta la verifica sulle altre configurazioni. Restano da completare le prove native su Windows 11 stabile e Windows 10 LTSC, sul runner elevato via UAC e sull’installazione di un driver Windows Update non firmware.

## Sicurezza e privacy

- Il consenso è legato al piano mostrato. L’app ricontrolla i dati prima dell’installazione e blocca le operazioni non verificabili. BIOS, UEFI e firmware sono esclusi dall’installazione automatica.
- **La 2.0.0 è distribuita senza firma Authenticode.** I file `.sha256` verificano l’integrità del download. L’autenticazione del publisher richiede una firma, e Windows SmartScreen può mostrare un avviso. [Policy di firma e aggiornamento](docs/CODE-SIGNING.md)
- Impostazioni, cronologia e log restano in `%LOCALAPPDATA%\UpdateCenter`. L’app non raccoglie telemetria.

## Licenza e sviluppo

Il progetto usa la licenza [MIT](LICENSE). Per compilare i sorgenti serve .NET SDK 10. [Guida a build, test e packaging](docs/SVILUPPO.md)
