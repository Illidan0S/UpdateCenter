using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace UpdateCenter.Services;

public static class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> ItalianToEnglish =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Completato · da verificare"] = "Completed · verify",
            ["Unità fisica"] = "Physical drive",
            ["Processore non rilevato"] = "Processor not detected",
            ["Riuscito"] = "Succeeded",
            ["Fallito"] = "Failed",
            ["Manuale"] = "Manual",
            ["Non applicabile"] = "Not applicable",
            ["Completato · verifica richiesta"] = "Completed · verification required",
            ["Nessun aggiornamento è stato eseguito."] = "No updates were performed.",
            ["Tutti gli aggiornamenti selezionati sono terminati."] = "All selected updates have finished.",
            ["Operazione terminata: alcuni aggiornamenti richiedono attenzione."] = "Operation completed: some updates require attention.",
            ["La risposta WinGet non contiene una versione installata verificabile."] = "The WinGet response does not contain a verifiable installed version.",
            ["Verifica ambigua: entry duplicate o versione non determinabile. Controlla le installazioni presenti."] = "Verification is ambiguous: duplicate entries or an undetermined version. Check the installed applications.",
            ["CONTROLLO PC"] = "PC CONTROL",
            ["Home"] = "Home",
            ["Aggiornamenti"] = "Updates",
            ["Hardware"] = "Hardware",
            ["Gaming"] = "Gaming",
            ["Driver e chipset"] = "Drivers and chipset",
            ["Cronologia"] = "History",
            ["Impostazioni"] = "Settings",
            ["Informazioni"] = "About",
            ["Controllo locale"] = "Local checks",
            ["Nessuna telemetria"] = "No telemetry",
            ["Mantieni il PC aggiornato"] = "Keep your PC up to date",
            ["Controlla software e driver da fonti ufficiali, poi scegli tu cosa installare."] = "Check software and drivers from trusted sources, then choose what to install.",
            ["Avvia scansione"] = "Start scan",
            ["Annulla"] = "Cancel",
            ["STATO"] = "STATUS",
            ["Avanzamento"] = "Progress",
            ["Elementi trovati"] = "Items found",
            ["Selezionati"] = "Selected",
            ["ULTIMA SCANSIONE"] = "LAST SCAN",
            ["Vedi aggiornamenti"] = "View updates",
            ["Cerca aggiornamenti"] = "Search updates",
            ["Tutti"] = "All",
            ["Tipo: tutti"] = "Type: all",
            ["Tipo: software"] = "Type: software",
            ["Tipo: driver"] = "Type: driver",
            ["Stato: tutti"] = "Status: all",
            ["Software"] = "Software",
            ["Driver"] = "Driver",
            ["Importanti"] = "Important",
            ["Standard"] = "Standard",
            ["Facoltativi"] = "Optional",
            ["Riavvio richiesto"] = "Restart required",
            ["Errori"] = "Errors",
            ["Nuova scansione"] = "New scan",
            ["Nome"] = "Name",
            ["Tipo"] = "Type",
            ["Priorità"] = "Priority",
            ["Versione attuale"] = "Current version",
            ["Nuova versione"] = "New version",
            ["Stato"] = "Status",
            ["Da aggiornare"] = "Update available",
            ["In attesa"] = "Waiting",
            ["Aggiornato"] = "Updated",
            ["Errore"] = "Error",
            ["Pronto per la scansione"] = "Ready to scan",
            ["Premi Avvia scansione per iniziare."] = "Select Start scan to begin.",
            ["Riprova"] = "Retry",
            ["Dettagli"] = "Details",
            ["Seleziona tutto"] = "Select all",
            ["Deseleziona tutto"] = "Clear selection",
            ["Aggiorna elementi selezionati"] = "Update selected items",
            ["Panoramica hardware"] = "Hardware overview",
            ["Aggiorna dati"] = "Refresh data",
            ["Copia riepilogo"] = "Copy summary",
            ["Apri Gestione attività"] = "Open Task Manager",
            ["Processore"] = "Processor",
            ["Scheda video"] = "Graphics card",
            ["Memoria"] = "Memory",
            ["Schermo"] = "Display",
            ["Sistema"] = "System",
            ["Sensori temperatura"] = "Temperature sensors",
            ["Inventario driver installati"] = "Installed driver inventory",
            ["Cerca dispositivi"] = "Search devices",
            ["Con aggiornamenti"] = "With updates",
            ["CPU e chipset"] = "CPU and chipset",
            ["Grafica"] = "Graphics",
            ["Audio"] = "Audio",
            ["Rete"] = "Network",
            ["Gestione rete"] = "Network",
            ["Trova e controlla i PC autorizzati nella rete locale"] = "Find and control authorized PCs on the local network",
            ["Scansione multipla · aggiornamenti remoti"] = "Multi-PC scan · remote updates",
            ["Configura questo PC"] = "Configure this PC",
            ["Dispositivi rilevati"] = "Discovered devices",
            ["Seleziona uno o più PC da controllare"] = "Select one or more PCs to control",
            ["Cerca PC"] = "Find PCs",
            ["Controlla stato"] = "Check status",
            ["Autorizzazione"] = "Authorization",
            ["Attività"] = "Activity",
            ["Spunta i PC da gestire. Usa Richiedi per il collegamento rapido oppure Codice come metodo alternativo."] = "Select the PCs to manage. Use Request for quick pairing or Code as an alternative.",
            ["Codice (8 cifre)"] = "Code (8 digits)",
            ["Associa"] = "Pair",
            ["Opzioni avanzate · indirizzo manuale"] = "Advanced options · manual address",
            ["Indirizzo IP"] = "IP address",
            ["Porta"] = "Port",
            ["PC evidenziato"] = "Highlighted PC",
            ["PC selezionato"] = "Selected PC",
            ["Aggiorna questo PC"] = "Update this PC",
            ["Risultati dell'ultima scansione"] = "Last scan results",
            ["Installata"] = "Installed",
            ["Disponibile"] = "Available",
            ["Conferma rischio"] = "Risk confirmation",
            ["Trascina per ingrandire o ridurre l'elenco dei dispositivi"] = "Drag to expand or shrink the device list",
            ["Spunta uno o più dispositivi per abilitare le operazioni."] = "Select one or more devices to enable operations.",
            ["Aggiornamenti trovati"] = "Updates found",
            ["Nessun PC selezionato"] = "No PC selected",
            ["Nessuna scansione remota eseguita."] = "No remote scan has been run.",
            ["Pronto. Cerca i PC con il componente di rete Update Center."] = "Ready. Find PCs with the Update Center network component.",
            ["Controllo dello stato di questo PC..."] = "Checking this PC's status...",
            ["Scansiona"] = "Scan",
            ["Scansiona 1 PC"] = "Scan 1 PC",
            ["Richiedi collegamento"] = "Request connection",
            ["Connessione sicura"] = "Secure connection",
            ["Associazione sicura"] = "Secure pairing",
            ["In attesa"] = "Waiting",
            ["Richiedi"] = "Request",
            ["Codice"] = "Code",
            ["Autorizzato"] = "Authorized",
            ["Collegato a un altro PC"] = "Connected to another PC",
            ["Pronto a collegarsi"] = "Ready to connect",
            ["Non autorizzato"] = "Not authorized",
            ["Rilevato"] = "Discovered",
            ["Salvato"] = "Saved",
            ["Raggiungibile"] = "Reachable",
            ["Operazione attiva"] = "Operation active",
            ["Non raggiungibile"] = "Unreachable",
            ["Operazione in corso"] = "Operation in progress",
            ["Pronto"] = "Ready",
            ["Collegamento revocato"] = "Connection revoked",
            ["In coda"] = "Queued",
            ["In corso"] = "In progress",
            ["Completata"] = "Completed",
            ["Completata con avvisi"] = "Completed with warnings",
            ["Annullata"] = "Cancelled",
            ["Non riuscita"] = "Failed",
            ["Da aggiornare"] = "Update available",
            ["Aggiornato"] = "Updated",
            ["Richiesta"] = "Required",
            ["Importante"] = "Important",
            ["Facoltativo"] = "Optional",
            ["Verifica"] = "Verify",
            ["Solo verifica"] = "Verify only",
            ["Dispositivo scollegato"] = "Device disconnected",
            ["Amministratore"] = "Administrator",
            ["Stato di questo PC"] = "This PC's status",
            ["Aggiorna stato"] = "Refresh status",
            ["Installa / aggiorna componente"] = "Install / update component",
            ["Collegamento rapido"] = "Quick connection",
            ["CONSIGLIATO"] = "RECOMMENDED",
            ["1. Il PC principale cerca i dispositivi.  2. Invia la richiesta ai PC selezionati.  3. Approva la notifica che comparirà qui."] = "1. The controller PC finds devices.  2. Send the request to selected PCs.  3. Approve the notification shown here.",
            ["Consenti richieste"] = "Allow requests",
            ["Interrompi richieste"] = "Stop requests",
            ["Dettagli della rete"] = "Network details",
            ["Gestione LAN"] = "LAN management",
            ["Rete autorizzata"] = "Authorized network",
            ["Area locale"] = "Local area",
            ["PC principale"] = "Controller PC",
            ["ID componente"] = "Component ID",
            ["Metodo alternativo: codice manuale"] = "Alternative method: manual code",
            ["Usalo soltanto se la richiesta di collegamento non è disponibile."] = "Use it only if a connection request is unavailable.",
            ["Codice monouso a 8 cifre"] = "One-time 8-digit code",
            ["Genera codice"] = "Generate code",
            ["Copia codice"] = "Copy code",
            ["Gestione e rimozione"] = "Management and removal",
            ["Queste azioni interrompono o rimuovono la gestione remota di questo PC."] = "These actions stop or remove remote management from this PC.",
            ["Revoca il PC principale"] = "Revoke controller PC",
            ["Disabilita gestione remota"] = "Disable remote management",
            ["Disinstalla componente di rete"] = "Uninstall network component",
            ["Update Center non modifica il profilo di rete di Windows. Il componente accetta richieste solo dalla rete locale autorizzata e limita automaticamente le regole firewall al programma, alla sottorete e alle interfacce correnti."] = "Update Center does not change the Windows network profile. The component accepts requests only from the authorized local network and automatically limits firewall rules to the program, subnet, and current interfaces.",
            ["Nessuno"] = "None",
            ["Disabilitata"] = "Disabled",
            ["Attiva sulla rete corrente"] = "Active on the current network",
            ["In pausa: il PC non è sulla rete autorizzata"] = "Paused: this PC is not on the authorized network",
            ["Non collegato a un PC principale"] = "Not connected to a controller PC",
            ["Gestione remota disabilitata"] = "Remote management disabled",
            ["Richieste di collegamento abilitate"] = "Connection requests enabled",
            ["Richieste automatiche disabilitate"] = "Automatic requests disabled",
            ["Nessun codice attivo"] = "No active code",
            ["Nessuna rete configurata"] = "No network configured",
            ["Dispositivo"] = "Device",
            ["Categoria"] = "Category",
            ["Produttore"] = "Manufacturer",
            ["Versione installata"] = "Installed version",
            ["Stato fonti ufficiali"] = "Trusted source status",
            ["Controlli e supporto produttore"] = "Manufacturer checks and support",
            ["disponibili"] = "available",
            ["Controlli manuali del produttore"] = "Manual manufacturer checks",
            ["Apri pagina ufficiale"] = "Open official page",
            ["Risultato"] = "Result",
            ["Da"] = "From",
            ["A"] = "To",
            ["Data"] = "Date",
            ["Cancella cronologia"] = "Clear history",
            ["Apri cartella log"] = "Open log folder",
            ["Dettaglio attività"] = "Activity details",
            ["Copia dettaglio"] = "Copy details",
            ["Controlla il comportamento di scansione e installazione"] = "Control scanning and installation behavior",
            ["Sicurezza"] = "Security",
            ["Crea un punto di ripristino per driver e aggiornamenti importanti"] = "Create a restore point for drivers and important updates",
            ["Gestisci spazio in Windows"] = "Manage space in Windows",
            ["Richiedi a WinGet installazioni silenziose quando supportate"] = "Ask WinGet for silent installs when supported",
            ["Includi programmi la cui versione installata non è riconoscibile"] = "Include programs whose installed version cannot be detected",
            ["Aggiornamenti di Update Center"] = "Update Center updates",
            ["Controlla automaticamente gli aggiornamenti"] = "Automatically check for updates",
            ["ULTIMO CONTROLLO"] = "LAST CHECK",
            ["Controlla ora"] = "Check now",
            ["Aspetto"] = "Appearance",
            ["Chiaro"] = "Light",
            ["Scuro"] = "Dark",
            ["Dimensione del testo"] = "Text size",
            ["Piccola"] = "Small",
            ["Media"] = "Medium",
            ["Grande"] = "Large",
            ["Avvio e scansioni automatiche"] = "Startup and automatic scans",
            ["Avvia automaticamente la scansione all'apertura"] = "Automatically scan at startup",
            ["Frequenza scansione"] = "Scan frequency",
            ["Disattivata"] = "Disabled",
            ["Ogni giorno"] = "Daily",
            ["Ogni settimana"] = "Weekly",
            ["Notifiche"] = "Notifications",
            ["Avvisami quando vengono trovati aggiornamenti"] = "Notify me when updates are found",
            ["Lingua"] = "Language",
            ["Italiano"] = "Italian",
            ["Inglese"] = "English",
            ["Salva impostazioni"] = "Save settings",
            ["Ideato e sviluppato da"] = "Designed and developed by",
            ["Repository del progetto"] = "Project repository",
            ["Apri GitHub"] = "Open GitHub",
            ["Privacy"] = "Privacy",
            ["Update Center non raccoglie dati personali e non include telemetria."] = "Update Center does not collect personal data and includes no telemetry.",
            ["Licenza"] = "License",
            ["La licenza del progetto non è stata ancora scelta."] = "The project license has not been selected yet.",
            ["Informazioni sul progetto"] = "Project information",
            ["Chiudi"] = "Close",
            ["Aggiorna ora"] = "Update now",
            ["Aggiornamento di Update Center"] = "Update Center update",
            ["AGGIORNAMENTI VERIFICATI"] = "VERIFIED UPDATES",
            ["DRIVER DA AGGIORNARE"] = "DRIVERS TO UPDATE",
            ["CONTROLLI MANUALI"] = "MANUAL CHECKS",
            ["Controlli manuali e supporto ufficiale"] = "Manual checks and official support",
            ["Installazione aggiornamenti"] = "Installing updates",
            ["Aggiornamento in corso"] = "Update in progress",
            ["Avanzamento installazione"] = "Installation progress",
            ["RIUSCITI"] = "SUCCEEDED",
            ["NON RIUSCITI"] = "FAILED",
            ["L'operazione può richiedere alcuni minuti."] = "The operation may take a few minutes.",
            ["Riavvia ora"] = "Restart now",
            ["ALIMENTAZIONE"] = "POWER",
            ["Apri log"] = "Open logs",
            ["Avvisi prima di continuare"] = "Warnings before continuing",
            ["Caratteristiche hardware"] = "Hardware specifications",
            ["Salute dello storage"] = "Storage health",
            ["Unità fisica"] = "Physical drive",
            ["Volumi"] = "Volumes",
            ["Capacità"] = "Capacity",
            ["Salute"] = "Health",
            ["Temperatura"] = "Temperature",
            ["Dipendenze dei giochi"] = "Gaming dependencies",
            ["I componenti installabili vengono aggiunti agli Aggiornamenti e usano la stessa selezione, conferma e cronologia di software e driver."] = "Installable components are added to Updates and use the same selection, confirmation and history as software and drivers.",
            ["Componente"] = "Component",
            ["Architettura"] = "Architecture",
            ["Versione rilevata"] = "Detected version",
            ["Stato e azione"] = "Status and action",
            ["Apri"] = "Open",
            ["Filtra gli aggiornamenti"] = "Filter updates",
            ["Diagnosi driver problematici"] = "Problem driver diagnostics",
            ["Problema"] = "Problem",
            ["Azione consigliata"] = "Recommended action",
            ["Nessuna riparazione viene eseguita automaticamente: questa sezione mostra soltanto problemi confermati da Windows."] = "No repair is performed automatically: this section only shows problems confirmed by Windows.",
            ["Filtra per tipo di aggiornamento"] = "Filter by update type",
            ["Filtra per priorità o stato"] = "Filter by priority or status",
            ["Cerca nei dispositivi"] = "Search devices",
            ["Cerca per dispositivo, categoria, produttore, versione o ID hardware"] = "Search by device, category, manufacturer, version or hardware ID",
            ["Cerca per nome, produttore, versione, fonte o stato"] = "Search by name, manufacturer, version, source or status",
            ["COMPUTER"] = "COMPUTER",
            ["Conferma e aggiorna"] = "Confirm and update",
            ["Controlla gli elementi: l'installazione inizierà solo dopo la tua conferma."] = "Review the items: installation starts only after your confirmation.",
            ["CONTROLLI PRODUTTORE"] = "MANUFACTURER CHECKS",
            ["Copia CPU, GPU, temperature, utilizzo, RAM, schermo e versione di Windows in un formato leggibile."] = "Copy CPU, GPU, temperatures, usage, RAM, display and Windows version in a readable format.",
            ["Copia CPU, GPU, VRAM, RAM, Windows, versioni dei driver CPU/GPU e unità interne, escludendo i dispositivi USB."] = "Copy CPU, GPU, VRAM, RAM, Windows, CPU/GPU driver versions, and internal drives, excluding USB devices.",
            ["Copia informazioni hardware"] = "Copy hardware information",
            ["CORE E THREAD"] = "CORES AND THREADS",
            ["Dettagli completi dopo 1 secondo"] = "Full details after 1 second",
            ["È disponibile una nuova versione"] = "A new version is available",
            ["Esiti, versioni e spiegazioni delle operazioni eseguite"] = "Results, versions and explanations for completed operations",
            ["Esito"] = "Result",
            ["FREQUENZA"] = "REFRESH RATE",
            ["Il controllo usa esclusivamente le Release stabili ufficiali su GitHub e viene eseguito al massimo una volta ogni 24 ore."] = "The check uses stable GitHub releases and runs at most once every 24 hours.",
            ["Il download proviene dalla Release stabile ufficiale e verrà verificato con SHA-256."] = "The download comes from the stable release and is verified with SHA-256.",
            ["IMPORTANTI"] = "IMPORTANT",
            ["Informazioni selezionabili e utilizzo aggiornato automaticamente."] = "Selectable information with automatically refreshed usage.",
            ["INSTALLATA"] = "INSTALLED",
            ["La scansione periodica viene eseguita mentre Update Center è aperto oppure al successivo avvio, se è scaduta. L'installazione richiede sempre la tua conferma."] = "Scheduled scans run while Update Center is open or at the next start when due. Installation always requires your confirmation.",
            ["Le notifiche sono locali e non richiedono account o servizi di telemetria."] = "Notifications are local and require no account or telemetry service.",
            ["Le temperature compaiono solo se firmware o driver espongono i sensori a Windows. Nessun driver di monitoraggio viene installato."] = "Temperatures appear only when firmware or drivers expose sensors to Windows. No monitoring driver is installed.",
            ["Le versioni preview non vengono richieste: WinGet usa il canale stabile previsto dal pacchetto."] = "Preview versions are not requested: WinGet uses the package's stable channel.",
            ["MEMORIA RAM"] = "RAM",
            ["Non sono richiesti privilegi amministrativi nell'installazione per utente."] = "Per-user installation does not require administrator privileges.",
            ["Note della Release"] = "Release notes",
            ["NUOVA"] = "NEW",
            ["Piccola corrisponde alla precedente Media; Media e Grande aumentano progressivamente tutti i testi."] = "Small matches the previous Medium; Medium and Large progressively enlarge all text.",
            ["Più tardi"] = "Later",
            ["Prima di continuare risolvi questi problemi"] = "Resolve these issues before continuing",
            ["Protezione del sistema"] = "System protection",
            ["Registro aggiornamenti"] = "Update history",
            ["Riavvio"] = "Restart",
            ["Riepilogo completo"] = "Full summary",
            ["Mostra avanzamento"] = "Show progress",
            ["Nascondi avanzamento"] = "Hide progress",
            ["Riduci a icona"] = "Minimize",
            ["Riepilogo prima dell'installazione"] = "Review before installation",
            ["RISOLUZIONE"] = "RESOLUTION",
            ["Scansioni, impostazioni, cronologia e log restano memorizzati localmente sul computer."] = "Scans, settings, history and logs remain stored locally on the computer.",
            ["Scegli il tema oppure segui automaticamente quello delle app di Windows."] = "Choose a theme or automatically follow the Windows app theme.",
            ["SCHEDA VIDEO"] = "GRAPHICS CARD",
            ["SCHEDE VIDEO RILEVATE"] = "DETECTED GRAPHICS CARDS",
            ["GPU monitorata:"] = "Monitored GPU:",
            ["SCHERMO"] = "DISPLAY",
            ["SISTEMA OPERATIVO"] = "OPERATING SYSTEM",
            ["SOFTWARE"] = "SOFTWARE",
            ["SPAZIO SU DISCO"] = "DISK SPACE",
            ["TEMPERATURA CPU (CORE)"] = "CPU TEMPERATURE (CORE)",
            ["TEMPERATURA GPU"] = "GPU TEMPERATURE",
            ["Update Center per Windows 10 e Windows 11"] = "Update Center for Windows 10 and Windows 11",
["Update Center usa Windows/Microsoft Update e metadati verificati con collegamenti diretti ai produttori. Se una fonte ufficiale non è interrogabile in modo sicuro, viene indicato un controllo manuale senza installare altre app."] = "Update Center uses Windows/Microsoft Update and verified metadata with direct manufacturer links. If a source cannot be queried safely, a manual check is shown without installing other apps.",
            ["Vedi update"] = "View updates",
            ["Verrà mostrata la richiesta amministratore di Windows."] = "The Windows administrator prompt will be shown.",
            ["Viene richiesto un solo punto per l'intero gruppo. I soli aggiornamenti software non ne creano uno; lo spazio è gestito da Protezione sistema di Windows."] = "Only one restore point is requested for the whole group. Software-only updates do not create one; space is managed by Windows System Protection.",
            ["VRAM IN USO"] = "VRAM IN USE",
            ["MEMORIA VIDEO PRINCIPALE"] = "PRIMARY VIDEO MEMORY",
            ["DETTAGLIO MEMORIA PER GPU"] = "MEMORY DETAILS BY GPU",
            ["MEMORIA VIDEO IN USO"] = "VIDEO MEMORY IN USE",
            ["Ignora questa versione"] = "Ignore this version",
            ["Da verificare"] = "Needs verification",
            ["da verificare"] = "needs verification",
            ["Completato · da verificare"] = "Completed · verify",
            ["Non verificato"] = "Unverified",
            ["Scansione"] = "Scanning",
            ["Aggiornamento"] = "Updating",
            ["Attenzione"] = "Attention",
            ["Componente di rete non disponibile"] = "Network component unavailable",
            ["Verifica richiesta"] = "Verification required",
            ["Runtime installato con WinGet."] = "Runtime installed with WinGet.",
            ["Software aggiornato con WinGet."] = "Software updated with WinGet.",
            ["Verifica post-installazione non richiesta per questo esito."] = "Post-installation verification not required for this outcome.",
            ["Identificativo WinGet non valido durante la verifica post-installazione."] = "Invalid WinGet identifier during post-installation verification.",
            ["Verifica post-installazione non disponibile."] = "Post-installation verification not available.",
            ["Il pacchetto non risulta installato dopo l'operazione."] = "The package does not appear to be installed after the operation.",
            ["WinGet non ha permesso di verificare lo stato installato dopo l'operazione."] = "WinGet could not verify the installed state after the operation.",
            ["Il driver non è più applicabile ed è verificato da Windows Update."] = "The driver is no longer applicable and is verified by Windows Update.",
            ["La verifica finale richiede il riavvio."] = "Final verification requires a restart.",
            ["Dopo una nuova scansione il driver risulta ancora applicabile."] = "After a new scan the driver is still applicable.",
            ["Lo stato post-installazione non è tecnicamente verificabile."] = "Post-installation state is not technically verifiable.",
            ["Driver installato e verificato da Windows Update."] = "Driver installed and verified by Windows Update.",
            ["Windows Update ha completato l'installazione; la verifica finale richiede il riavvio."] = "Windows Update completed the installation; final verification requires a restart.",
            ["Windows Update segnala installazione completata, ma lo stato installato non è ancora verificabile."] = "Windows Update reports installation completed, but the installed state is not yet verifiable.",
            ["PnPUtil ha restituito un errore, ma la versione target del driver risulta installata."] = "PnPUtil returned an error, but the target driver version is installed.",
            ["Verifica finale dell'inventario non disponibile."] = "Final inventory verification not available.",
            ["La verifica dell'inventario verrà completata dopo il riavvio."] = "Inventory verification will be completed after restart.",
            ["Il dispositivo aggiornato non è stato ritrovato nell'inventario hardware."] = "Updated device was not found in the hardware inventory.",
            ["Versione driver verificata nell'inventario hardware."] = "Driver version verified in hardware inventory.",
            ["La nuova versione non è ancora visibile; verifica da completare dopo il riavvio."] = "The new version is not yet visible; verification to be completed after restart.",
            ["Verifica finale rinviata al riavvio."] = "Final verification postponed until restart.",
            ["Questo pacchetto non supporta l'aggiornamento automatico con la tecnologia di installazione corrente. Usa l'installer ufficiale del produttore."] = "This package does not support automatic updates with the current installation technology. Use the manufacturer's official installer.",
            ["Creazione del punto di ripristino…"] = "Creating restore point…",
            ["Avvio dell'aggiornamento software con WinGet..."] = "Starting software update with WinGet...",
            ["Il canale di stato dell'aggiornamento non è disponibile."] = "The update status channel is not available.",
            ["Punto di ripristino non creato; gli aggiornamenti continueranno. Verifica che Protezione sistema sia attiva."] = "Restore point not created; updates will continue. Check that System Protection is enabled.",
            ["Aggiornamenti in pausa. Premi Riprendi in Update Center per continuare."] = "Updates paused. Select Resume in Update Center to continue.",
            ["Ripresa degli aggiornamenti."] = "Resuming updates.",
            ["Operazione interrotta da un errore infrastrutturale controllato."] = "Operation stopped by a controlled infrastructure error.",
            ["Aggiornamento interrotto da un errore infrastrutturale controllato."] = "Update stopped by a controlled infrastructure error.",
            ["Il processo di aggiornamento non ha restituito uno stato."] = "The update process did not return a status.",
            ["Il runner si è interrotto per un errore infrastrutturale del canale di stato."] = "The runner stopped due to an infrastructure error in the status channel.",
            ["Nessun dettaglio disponibile per questa operazione."] = "No details available for this operation.",
            ["Diagnostica tecnica"] = "Technical diagnostics",
            ["Dettaglio copiato negli appunti."] = "Details copied to clipboard.",
            ["Copia non riuscita."] = "Copy failed.",
            ["errore non specificato"] = "unspecified error",
            ["tempo di attesa scaduto"] = "timed out",
            ["il componente di rete non risponde"] = "the network component is not responding",
            ["dispositivo non raggiungibile sulla rete locale"] = "device unreachable on the local network",
            ["Restart Manager non è disponibile e nessun processo è attribuibile con certezza al pacchetto."] = "Restart Manager is unavailable and no process can be safely attributed to the package.",
            ["L'esito non è classificato come file in uso."] = "The outcome is not classified as files in use.",
            ["L'installer segnala ancora file in uso, ma Windows non permette di identificare in sicurezza il processo responsabile. Riavvia il PC o chiudi manualmente le applicazioni interessate e riprova."] = "The installer still reports files in use, but Windows cannot safely identify the responsible process. Restart the PC or manually close the affected applications and retry.",
            ["Stato alimentazione non disponibile."] = "Power status not available.",
            ["Spazio disponibile non verificato."] = "Available space not verified.",
            ["Stato alimentazione non determinato da Windows."] = "Power status not determined by Windows.",
            ["Alimentazione a batteria"] = "Battery power",
            ["Alimentatore collegato"] = "Power adapter connected",
            ["Uno o più driver sono risultati informativi e non possono essere installati automaticamente."] = "One or more drivers are informational only and cannot be installed automatically.",
            ["Update Center richiede Windows 10 versione 1809 (build 17763) o successiva."] = "Update Center requires Windows 10 version 1809 (build 17763) or later.",
            ["Nessuna connessione di rete rilevata."] = "No network connection detected.",
            ["Batteria troppo bassa per aggiornare driver o componenti importanti. Collega l'alimentatore."] = "Battery too low to update drivers or important components. Connect the power adapter.",
            ["Lo spazio sul disco di sistema è sufficiente ma ridotto."] = "System disk space is sufficient but limited.",
            ["Non è stato possibile verificare lo spazio disponibile."] = "Could not verify available disk space.",
            ["Il PC usa la batteria. È consigliato collegare l'alimentatore prima di aggiornare driver o componenti importanti."] = "The PC is on battery. Connecting the power adapter before updating drivers or important components is recommended.",
            ["Controlli non superati"] = "Pre-checks failed",
            ["Correggi i problemi indicati e riprova."] = "Resolve the indicated issues and retry.",
            ["Conferma il rischio"] = "Confirm risk",
            ["La conferma aggiuntiva è necessaria per gli installer con rimozione preventiva."] = "Additional confirmation is required for installers with prior removal.",
            ["Verrà richiesto un solo punto di ripristino per l'intero gruppo prima di installare driver o aggiornamenti importanti."] = "A single restore point will be requested for the whole group before installing drivers or important updates.",
            ["Non necessario: il gruppo contiene soltanto aggiornamenti software non classificati come importanti."] = "Not needed: the group contains only software updates not classified as important.",
            ["Disattivato nelle Impostazioni."] = "Disabled in Settings.",
            ["PACCHETTI / SPAZIO PER PC"] = "PACKAGES / SPACE PER PC",
            ["Gli aggiornamenti vengono eseguiti separatamente su ogni PC. Le protezioni configurate localmente restano applicate sul relativo dispositivo."] = "Updates run separately on each PC. Locally configured protections remain applied on the respective device.",
            ["L'avvio e l'avanzamento resteranno separati per ciascun PC."] = "Startup and progress will remain separate for each PC.",
            ["Puoi includere gli elementi rischiosi oppure continuare escludendoli."] = "You can include risky items or continue excluding them.",
            ["Aggiornamenti completati"] = "Updates completed",
            ["Nessun aggiornamento eseguito"] = "No updates performed",
            ["Completato con alcuni problemi"] = "Completed with some issues",
            ["Tutto completato"] = "All done",
            ["Aggiornamento non completato"] = "Update not completed",
            ["Nessuna ulteriore operazione è in corso."] = "No further operation is running.",
            ["Puoi chiudere questa finestra."] = "You can close this window.",
            ["Il punto di ripristino non è stato creato. Controlla Protezione sistema."] = "The restore point was not created. Check System Protection.",
            ["Windows richiede un riavvio per completare gli aggiornamenti."] = "Windows requires a restart to complete updates.",
            ["Puoi chiudere questa finestra e continuare a usare Update Center."] = "You can close this window and continue using Update Center.",
            ["Aggiornamenti in pausa"] = "Updates paused",
            ["Aggiornamento annullato"] = "Update cancelled",
            ["Aggiornamento non avviato"] = "Update not started",
            ["In attesa dell'installazione."] = "Waiting for installation.",
            ["Operazione annullata prima dell'installazione."] = "Operation cancelled before installation.",
            ["Ripresa richiesta."] = "Resume requested.",
            ["Pausa richiesta: l'elemento corrente terminerà prima di fermarsi."] = "Pause requested: the current item will finish before stopping.",
            ["Conferma la richiesta di Controllo account utente di Windows."] = "Confirm the Windows User Account Control prompt if requested.",
            ["Puoi avviare una nuova scansione."] = "You can start a new scan.",
            ["Controllo automatico annullato."] = "Automatic check cancelled.",
            ["Scansione annullata"] = "Scan cancelled",
            ["Reinstalla driver"] = "Reinstall driver",
            ["Cerca driver"] = "Search driver",
            ["Riparazione"] = "Repair",
            ["Sano"] = "Healthy",
            ["Stato non disponibile"] = "Status not available",
            ["Nessun volume con lettera"] = "No lettered volumes",
            ["Opzionale non rilevato"] = "Optional not detected",
            ["Non rilevato"] = "Not detected",
            ["Selezionabile negli aggiornamenti"] = "Selectable in updates",
            ["Controllo ufficiale"] = "Official check",
            ["Solo diagnosi"] = "Diagnosis only",
            ["Nessuna proposta verificata dalle fonti ufficiali"] = "No verified offer from official sources",
            ["Aggiornamento disponibile"] = "Update available",
            ["problemi attivi segnalati da Gestione dispositivi"] = "active issues reported by Device Manager",
            ["Origine monitoraggio:"] = "Monitoring source:",
            ["unità fisiche · "] = "physical drives · ",
            ["volumi"] = "volumes",
            ["dispositivi · "] = "devices · ",
            ["Installata: "] = "Installed: ",
            ["Disponibile: "] = "Available: ",
            ["La riparazione usa soltanto il pacchetto INF già registrato da Windows e richiede conferma amministratore. Nessun driver viene eliminato forzatamente."] = "Repair uses only the INF package already registered by Windows and requires administrator confirmation. No driver is forcefully deleted.",
            ["Tutti gli aggiornamenti selezionati sono terminati."] = "All selected updates finished.",
            ["Operazione terminata: alcuni aggiornamenti richiedono attenzione."] = "Operation finished: some updates require attention.",
            ["Nessun aggiornamento è stato eseguito."] = "No updates were performed.",
            ["Nessuna scansione remota disponibile."] = "No remote scan available.",
            ["1 aggiornamento selezionato"] = "1 update selected",
            ["1 aggiornamento remoto selezionato"] = "1 remote update selected",
            ["Driver riparato"] = "Driver repaired",
            ["Driver ancora da controllare"] = "Driver still needs checking",
            ["Riparazione driver"] = "Driver repair",
            ["Riparazione driver con Windows"] = "Windows driver repair",
            ["Riparazione in corso…"] = "Repairing…",
            ["Verifica del driver riparato in corso…"] = "Verifying repaired driver…",
            ["Windows non segnala problemi attivi nei dispositivi."] = "Windows reports no active device issues.",
            ["Ricerca driver"] = "Driver search",
            ["Protezione sistema non aperta"] = "System Protection not opened",
            ["Collegamento non aperto"] = "Link not opened",
            ["Gestione attività non aperta"] = "Task Manager not opened",
            ["Copia non riuscita"] = "Copy failed",
            ["Raccolta informazioni…"] = "Gathering info…",
            ["Raccolta delle informazioni hardware locali…"] = "Gathering local hardware information…",
            ["Riepilogo hardware copiato negli appunti."] = "Hardware summary copied to clipboard.",
            ["Cancellare la cronologia visibile? I log tecnici resteranno disponibili."] = "Clear visible history? Technical logs will remain available.",
            ["Cancella cronologia"] = "Clear history",
            ["Dopo aver escluso gli aggiornamenti con rimozione preventiva non rimangono elementi da installare."] = "After excluding updates with prior removal, no items remain to install.",
            ["Impossibile individuare l'eseguibile di Update Center."] = "Cannot locate the Update Center executable.",
            ["Gestione rete"] = "Network management",
            ["Non collegato a un PC principale"] = "Not connected to a primary PC",
            ["Gestione remota disabilitata"] = "Remote management disabled",
            ["Richieste automatiche disabilitate"] = "Automatic requests disabled",
            ["Nessun codice attivo"] = "No active code",
            ["Nessuna rete configurata"] = "No network configured",
            ["Controllo della configurazione locale..."] = "Checking local configuration...",
            ["Disabilitata"] = "Disabled",
            ["Attiva sulla rete corrente"] = "Active on current network",
            ["In pausa: il PC non è sulla rete autorizzata"] = "Paused: PC is not on the authorized network",
            ["Nessun PC portatile rilevato tra quelli selezionati."] = "No laptops detected among the selected PCs.",
            ["Il runner non ha potuto inizializzare il canale di stato protetto."] = "The runner could not initialize the protected status channel.",
            ["Il driver non è più applicabile ed è verificato da Windows Update."] = "The driver is no longer applicable and is verified by Windows Update.",
            ["Dopo una nuova scansione il driver risulta ancora applicabile."] = "After rescanning, the driver is still applicable.",
            ["Lo stato post-installazione non è tecnicamente verificabile."] = "Post-installation state is not technically verifiable.",
            ["La versione attesa non risulta installata nell'inventario hardware."] = "The expected version does not appear installed in the hardware inventory.",
            ["L'aggiornamento non è più proposto da Windows Update. Esegui una nuova scansione."] = "The update is no longer offered by Windows Update. Run a new scan.",
            ["⌂   Home"] = "⌂   Home",
            ["↓   Aggiornamenti"] = "↓   Updates",
            ["▣   Driver e chipset"] = "▣   Drivers and chipset",
            ["▤   Hardware"] = "▤   Hardware",
            ["◷   Cronologia"] = "◷   History",
            ["⌘   Gestione rete"] = "⌘   Network",
            ["⚙   Impostazioni"] = "⚙   Settings",
            ["Apri fonte"] = "Open source",
            ["CPU/chipset"] = "CPU/chipset",
            ["DRIVER DA CONTROLLARE"] = "DRIVERS TO CHECK",
            ["Dimensione"] = "Size",
            [" unità fisiche · "] = " physical drives · ",
            [" volumi"] = " volumes",
            [" problemi attivi segnalati da Gestione dispositivi"] = " active problems reported by Device Manager",
            [" disponibili"] = " available",
            [" dispositivi · "] = " devices · ",
            [" CPU/chipset"] = " CPU/chipset",
            [" · richiede conferma"] = " · confirmation required",
            ["  • Riavvio"] = "  • Restart",
            ["Disponibile:"] = "Available:",
            ["Installata:"] = "Installed:",
            ["MEMORIA VIDEO"] = "VIDEO MEMORY",
            ["Unità"] = "Drive",
            ["Utilizzo"] = "Usage",
            ["Versioni"] = "Versions",
            ["dispositivi ·"] = "devices ·",
            ["unità fisiche ·"] = "physical drives ·",
            ["· richiede conferma"] = "· confirmation required",
            ["• Riavvio"] = "• Restart",
            ["Salute dello storage non ancora controllata."] = "Storage health not checked yet.",
            ["Esegui una scansione per controllare la salute dello storage."] = "Run a scan to check storage health.",
            ["Processore non ancora rilevato"] = "Processor not detected yet",
            ["Rilevamento in corso…"] = "Detecting…",
            ["Identificazione in corso…"] = "Identifying…",
            ["Preparazione del monitoraggio…"] = "Preparing monitoring…",
            ["MEMORIA GPU IN USO"] = "GPU MEMORY IN USE",
            ["Contatori GPU di Windows"] = "Windows GPU counters",
            ["Non esposta dal driver o da Windows"] = "Not exposed by driver or Windows",
            ["Non esposta da Windows/firmware"] = "Not exposed by Windows/firmware",
            ["Non esposta dal driver video"] = "Not exposed by display driver",
            ["Fonte ufficiale"] = "Official source",
            ["Produttore ufficiale"] = "Official manufacturer",
            ["Rilevato dall'hardware del PC"] = "Detected from PC hardware",
            ["Problema rilevato"] = "Issue detected",
            ["Apri Gestione dispositivi per verificare il dispositivo."] = "Open Device Manager to check the device.",
            ["Apri Gestione dispositivi per controllare dettagli e azioni consigliate da Windows."] = "Open Device Manager to check details and recommended actions from Windows.",
            ["Ricerca tramite fonti verificate"] = "Search via verified sources",
            ["Il dispositivo non può essere avviato"] = "This device cannot start",
            ["Risorse hardware insufficienti"] = "Insufficient hardware resources",
            ["Driver da reinstallare"] = "Driver must be reinstalled",
            ["Dispositivo disabilitato"] = "Device disabled",
            ["Dispositivo non presente o configurato male"] = "Device not present or misconfigured",
            ["Driver mancante"] = "Missing driver",
            ["Windows non riesce a caricare il driver"] = "Windows cannot load the driver",
            ["Driver disabilitato nel registro"] = "Driver disabled in registry",
            ["Driver non caricabile o danneggiato"] = "Driver cannot be loaded or corrupted",
            ["Il dispositivo ha segnalato un problema"] = "The device reported a problem",
            ["Driver bloccato per incompatibilità"] = "Driver blocked due to incompatibility",
            ["Firma digitale non verificabile"] = "Digital signature cannot be verified",
            ["Critico"] = "Critical",
            ["Dispositivo sconosciuto"] = "Unknown device",
            ["Apri controllo ufficiale"] = "Open official check",
            ["Supporto driver AMD"] = "AMD driver support",
            ["Supporto driver Intel"] = "Intel driver support",
            ["Controllo driver GPU NVIDIA"] = "NVIDIA GPU driver check",
            ["Installa NVIDIA App"] = "Install NVIDIA App",
            ["Apri NVIDIA App"] = "Open NVIDIA App",
            ["Driver facoltativi Windows"] = "Windows optional drivers",
            ["Apri Windows Update"] = "Open Windows Update",
            ["Apre la sezione ufficiale di Windows Update dedicata agli aggiornamenti facoltativi."] = "Opens the official Windows Update section for optional updates.",
            ["Controllo applicabilità eseguito da Windows"] = "Applicability check performed by Windows",
            ["Controllo ufficiale per chipset AMD e grafica Radeon, senza installare strumenti di rilevamento."] = "Official check for AMD chipset and Radeon graphics, without installing detection tools.",
            ["Download Center ufficiale per i componenti Intel rilevati; Update Center non installa Intel DSA."] = "Official Download Center for detected Intel components; Update Center does not install Intel DSA.",
            ["Hardware AMD rilevato tramite produttore o ID PCI"] = "AMD hardware detected via vendor or PCI ID",
            ["Hardware Intel rilevato tramite produttore o ID PCI"] = "Intel hardware detected via vendor or PCI ID",
            ["GPU NVIDIA rilevata tramite ID PCI"] = "NVIDIA GPU detected via PCI ID",
            ["NVIDIA App non è installata: apre la pagina ufficiale per scaricarla e gestire i driver Game Ready o Studio."] = "NVIDIA App is not installed: opens official page to download it and manage Game Ready or Studio drivers.",
            ["NVIDIA App è installata: il pulsante la apre direttamente per controllare e installare i driver Game Ready o Studio."] = "NVIDIA App is installed: the button opens it directly to check and install Game Ready or Studio drivers.",
            ["Supporto Logitech G HUB"] = "Logitech G HUB support",
            ["Pagina ufficiale per i componenti virtuali G HUB già presenti; nessuna app viene installata da Update Center."] = "Official page for existing G HUB virtual components; no apps installed by Update Center.",
            ["Apri supporto Logitech"] = "Open Logitech support",
            ["Componente G HUB rilevato per nome e produttore"] = "G HUB component detected by name and vendor",
            ["Driver e firmware specifici per il modello, forniti direttamente dal produttore del PC."] = "Model-specific drivers and firmware provided directly by PC manufacturer.",
            ["Configura questo PC · Update Center"] = "Configure this PC · Update Center",
            ["Controlla il collegamento e scegli chi può gestire gli aggiornamenti"] = "Check connection and choose who can manage updates",
            ["Lettura della configurazione del componente di rete..."] = "Reading network component configuration...",
            ["Il PC è gestibile esclusivamente dalla rete locale autorizzata."] = "The PC is manageable exclusively from the authorized local network.",
            ["Gestione sospesa automaticamente perché la rete corrente è diversa da quella autorizzata."] = "Management suspended automatically because the current network differs from the authorized one.",
            ["Il componente è installato, ma la gestione remota è disabilitata."] = "The component is installed, but remote management is disabled.",
            ["Il componente è presente, ma questa finestra non dispone dei privilegi amministrativi."] = "The component is present, but this window does not have administrative privileges.",
            ["Installazione e abilitazione del componente di rete..."] = "Installing and enabling the network component...",
            ["Componente configurato. Lettura dello stato..."] = "Component configured. Reading status...",
            ["Generazione del codice temporaneo..."] = "Generating temporary code...",
            ["Inserisci questo codice sul PC principale entro 5 minuti."] = "Enter this code on the main PC within 5 minutes.",
            ["Abilitazione delle richieste di collegamento..."] = "Enabling connection requests...",
            ["Questo PC è rilevabile e può ricevere richieste di collegamento. Ogni richiesta deve essere approvata qui."] = "This PC is discoverable and can receive connection requests. Each request must be approved here.",
            ["Disabilitazione delle richieste di collegamento..."] = "Disabling connection requests...",
            ["Questo PC non accetta più nuove richieste di collegamento."] = "This PC no longer accepts new connection requests.",
            ["Disabilitazione della gestione di rete..."] = "Disabling network management...",
            ["Revoca del PC principale..."] = "Revoking main PC...",
            ["PC principale revocato. Questo dispositivo non accetterà più i suoi comandi."] = "Main PC revoked. This device will no longer accept its commands.",
            ["Rimozione completa del componente di rete..."] = "Completely removing network component...",
            ["Componente di rete disinstallato. Update Center locale non è stato rimosso."] = "Network component uninstalled. Local Update Center was not removed.",
            ["Componente di rete non raggiungibile"] = "Network component unreachable",
            ["Caricamento..."] = "Loading...",
            ["Consenti"] = "Allow",
            ["Rifiuta"] = "Decline",
            ["PC PRINCIPALE"] = "MAIN PC",
            ["Richiesta di collegamento"] = "Connection request",
            ["Richiesta di collegamento · Update Center"] = "Connection request · Update Center",
            ["Un altro computer vuole gestire gli aggiornamenti di questo PC."] = "Another computer wants to manage updates on this PC.",
            ["Se accetti, il PC principale potrà:"] = "If you accept, the main PC will be able to:",
            ["• controllare lo stato di Update Center"] = "• check Update Center status",
            ["• eseguire scansioni di software e driver"] = "• run software and driver scans",
            ["• installare gli aggiornamenti selezionati e seguirne l'avanzamento"] = "• install selected updates and track progress",
            ["Puoi revocare il collegamento in qualsiasi momento da Configura questo PC."] = "You can revoke the connection at any time from Configure this PC.",
            ["La richiesta non è più disponibile oppure è scaduta."] = "The request is no longer available or has expired.",
            ["Accetta soltanto se riconosci il computer indicato."] = "Accept only if you recognize the indicated computer.",
            ["Richiesta di collegamento rifiutata."] = "Connection request rejected.",
            ["Autorizza questo PC a essere gestito dal Controller."] = "Authorize this PC to be managed by the Controller.",
            ["Codice temporaneo"] = "Temporary code",
            ["Collega"] = "Connect",
            ["Collega dispositivo"] = "Connect device",
            ["Connessione protetta"] = "Secure connection",
            ["Inserisci le 8 cifre mostrate da Update Center sul dispositivo da collegare."] = "Enter the 8 digits shown by Update Center on the device to connect.",
            ["Opzioni avanzate"] = "Advanced options",
            ["DOWNLOAD"] = "DOWNLOAD",
            ["Aggiornamento Update Center"] = "Update Center Update",
            ["Aggiornamenti con rimozione preventiva"] = "Updates with prior removal",
            ["Conferma aggiornamenti"] = "Confirm updates",
            ["Continua senza questi aggiornamenti"] = "Continue without these updates",
            ["Ho compreso il rischio e voglio eseguire comunque questi aggiornamenti"] = "I understand the risk and want to perform these updates anyway",
            ["Questi installer possono rimuovere la versione funzionante prima di installare quella nuova. Se l'installazione fallisce, il programma potrebbe dover essere reinstallato manualmente:"] = "These installers may remove the working version before installing the new one. If installation fails, the program may need to be reinstalled manually:",
            ["Non dichiarata"] = "Not declared",
            ["Aggiornamento standard."] = "Standard update.",
            ["Aggiornamento facoltativo secondo la fonte ufficiale."] = "Optional update according to the official source.",
            ["Aggiornamento obbligatorio o di sicurezza secondo la fonte ufficiale."] = "Mandatory or security update according to the official source.",
            ["L'installer può rimuovere la versione funzionante prima di installare quella nuova."] = "The installer may remove the working version before installing the new one.",
            ["Lettura delle caratteristiche hardware…"] = "Reading hardware specifications…",
            ["Monitoraggio temporaneamente non disponibile."] = "Monitoring temporarily unavailable."
        };

    private static readonly (string Italian, string English)[] HistoryDetailReplacements =
    [
        (" non è applicabile a questo PC secondo WinGet. La segnalazione da ", " is not applicable to this PC according to WinGet. Reporting from "),
        (" resterà esclusa finché una delle due versioni non cambia. Dettaglio: ", " will remain excluded until one of the two versions changes. Details: "),
        (" richiede un aggiornamento manuale perché il pacchetto installato e quello nuovo non supportano un upgrade automatico compatibile. Dettaglio: ", " requires a manual update because the installed and new packages do not support a compatible automatic upgrade. Details: "),
        (" è stato aggiornato e verificato da ", " was updated and verified from "),
        (" usando ", " using "),
        (" Per completare l'operazione è richiesto il riavvio di Windows.", " A Windows restart is required to complete the operation."),
        (" Non è richiesto alcun riavvio.", " No restart is required."),
        (" Dettaglio tecnico: ", " Technical details: "),
        ("La verifica finale richiede il riavvio.", "Final verification requires a restart."),
        ("L'installer è terminato, ma la verifica finale non ha confermato l'aggiornamento.", "The installer finished, but final verification did not confirm the update."),
        ("L'installer è terminato, ma la verifica finale non è disponibile.", "The installer finished, but final verification is not available."),
        ("L'aggiornamento di ", "Update of "),
        (" non è riuscito. L'elemento resta disponibile per un nuovo tentativo. Motivo: ", " failed. The item remains available for retry. Reason: "),
        ("versione precedente non rilevata", "previous version not detected"),
        ("versione più recente disponibile", "latest available version"),
        ("fonte di aggiornamento configurata", "configured update source"),
        ("Nessun dettaglio tecnico aggiuntivo.", "No additional technical details."),
        ("WinGet ha completato l'installer, ma la verifica post-installazione non è riuscita. ", "WinGet completed the installer, but post-installation verification failed. "),
        ("WinGet ha completato l'installer, ma la verifica post-installazione non è riuscita.", "WinGet completed the installer, but post-installation verification failed."),
        ("Versione installata verificata: ", "Verified installed version: "),
        ("Versione installata verificata:", "Verified installed version:"),
        ("La versione installata (", "The installed version ("),
        (") non raggiunge quella attesa (", ") does not match the expected version ("),
        ("Driver installato e verificato da Windows Update.", "Driver installed and verified by Windows Update."),
        ("Windows Update ha completato l'installazione; la verifica finale richiede il riavvio.", "Windows Update completed the installation; final verification requires a restart."),
        ("Windows Update segnala installazione completata, ma lo stato installato non è ancora verificabile.", "Windows Update reports installation completed, but the installed state is not yet verifiable."),
        ("Driver INF ufficiale installato e verificato", "Official INF driver installed and verified"),
        ("Nessuna app del produttore è stata eseguita.", "No manufacturer app was run."),
        ("Driver INF ufficiale installato", "Official INF driver installed"),
        ("Verifica finale dell'inventario non disponibile.", "Final inventory verification not available."),
        ("La verifica dell'inventario verrà completata dopo il riavvio.", "Inventory verification will be completed after restart."),
        ("Il dispositivo aggiornato non è stato ritrovato nell'inventario hardware.", "Updated device was not found in the hardware inventory."),
        ("Versione driver verificata nell'inventario hardware.", "Driver version verified in hardware inventory."),
        ("La nuova versione non è ancora visibile; verifica da completare dopo il riavvio.", "The new version is not yet visible; verification to be completed after restart."),
        ("Verifica finale rinviata al riavvio.", "Final verification postponed until restart."),
        ("PnPUtil ha restituito un errore, ma la versione target del driver risulta installata.", "PnPUtil returned an error, but the target driver version is installed."),
        ("Verifica post-installazione non disponibile.", "Post-installation verification not available."),
        ("Verifica post-installazione non richiesta per questo esito.", "Post-installation verification not required for this outcome."),
        ("Il pacchetto non risulta installato dopo l'operazione.", "The package does not appear to be installed after the operation."),
        ("WinGet non ha permesso di verificare lo stato installato dopo l'operazione.", "WinGet could not verify the installed state after the operation."),
        ("La risposta WinGet non contiene una versione installata verificabile.", "The WinGet response does not contain a verifiable installed version."),
        ("Verifica ambigua: entry duplicate o versione non determinabile. Controlla le installazioni presenti.", "Verification is ambiguous: duplicate entries or an undetermined version. Check the installed applications."),
        ("In attesa dell'installazione.", "Waiting for installation."),
        ("Operazione annullata prima dell'installazione.", "Operation cancelled before installation."),
        ("Punto di ripristino non creato; gli aggiornamenti continueranno. Verifica che Protezione sistema sia attiva.", "Restore point not created; updates will continue. Check that System Protection is enabled."),
        ("Operazione interrotta da un errore infrastrutturale controllato.", "Operation stopped by a controlled infrastructure error."),
        ("Aggiornamento interrotto da un errore infrastrutturale controllato.", "Update stopped by a controlled infrastructure error."),
        ("Il runner si è interrotto per un errore infrastrutturale del canale di stato.", "The runner stopped due to an infrastructure error in the status channel."),
        ("Il processo di aggiornamento non ha restituito uno stato.", "The update process did not return a status."),
        ("Il driver non è più applicabile ed è verificato da Windows Update.", "The driver is no longer applicable and is verified by Windows Update."),
        ("Dopo una nuova scansione il driver risulta ancora applicabile.", "After rescanning, the driver is still applicable."),
        ("Lo stato post-installazione non è tecnicamente verificabile.", "Post-installation state is not technically verifiable."),
        ("La versione attesa non risulta installata nell'inventario hardware.", "The expected version does not appear installed in the hardware inventory."),
        ("L'aggiornamento non è più proposto da Windows Update. Esegui una nuova scansione.", "The update is no longer offered by Windows Update. Run a new scan."),
        ("Identificativo WinGet non valido durante la verifica post-installazione.", "Invalid WinGet identifier during post-installation verification."),
        ("Nessun dispositivo compatibile trovato nella verifica post-installazione.", "No compatible device found in post-installation verification."),
        ("risulta già aggiornato alla versione", "is already updated to version"),
        ("La scansione precedente non era più attuale.", "The previous scan was no longer current."),
        (" a ", " to ")
    ];

    public static string TranslateHistoryDetails(string details)
    {
        if (string.IsNullOrWhiteSpace(details)) return details;
        var result = details;
        if (IsEnglish)
        {
            foreach (var (it, en) in HistoryDetailReplacements)
                result = result.Replace(it, en, StringComparison.Ordinal);
        }
        else
        {
            foreach (var (it, en) in HistoryDetailReplacements)
                result = result.Replace(en, it, StringComparison.Ordinal);
        }
        return result;
    }

    private static readonly IReadOnlyDictionary<string, string> EnglishToItalian =
        ItalianToEnglish
            .Where(x => !x.Key.Equals(x.Value, StringComparison.Ordinal))
            .GroupBy(x => x.Value, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Key, StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, string> DynamicItalianToEnglish = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> DynamicEnglishToItalian = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<DependencyObject, LocalizationRootCache> RootCaches = new();

    public static string CurrentLanguage { get; private set; } = "it";
    public static bool IsEnglish => CurrentLanguage == "en";

    public static void Initialize(string? language) => CurrentLanguage = Normalize(language);

    public static string Normalize(string? language) =>
        language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "it";

    public static string Text(string italian, string english)
    {
        if (!string.IsNullOrEmpty(italian) && !string.IsNullOrEmpty(english) &&
            !italian.Equals(english, StringComparison.Ordinal))
        {
            DynamicItalianToEnglish[italian] = english;
            DynamicEnglishToItalian[english] = italian;
        }
        return IsEnglish ? english : italian;
    }

    public static string Translate(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (IsEnglish)
        {
            if (DynamicItalianToEnglish.TryGetValue(text, out var dynamicEnglish)) return dynamicEnglish;
            if (ItalianToEnglish.TryGetValue(text, out var english)) return english;
            if (text.StartsWith("Aggiornamento: ", StringComparison.Ordinal))
                return "Updating: " + text["Aggiornamento: ".Length..];
            if (text.StartsWith("Aggiornamento di ", StringComparison.Ordinal) && text.EndsWith("…", StringComparison.Ordinal))
                return "Updating " + text["Aggiornamento di ".Length..^1] + "…";
            if (text.StartsWith("Scansione di ", StringComparison.Ordinal) && text.EndsWith("…", StringComparison.Ordinal))
                return "Scanning " + text["Scansione di ".Length..^1] + "…";
            if (text.StartsWith("Verifica di ", StringComparison.Ordinal) && text.EndsWith("…", StringComparison.Ordinal))
                return "Verifying " + text["Verifica di ".Length..^1] + "…";
            if (text.StartsWith("Installazione di ", StringComparison.Ordinal) && text.EndsWith("…", StringComparison.Ordinal))
                return "Installing " + text["Installazione di ".Length..^1] + "…";
            if (text.StartsWith("Apri supporto ", StringComparison.Ordinal))
                return "Open " + text["Apri supporto ".Length..] + " support";
            if (text.StartsWith("Componente di rete non raggiungibile: ", StringComparison.Ordinal))
                return "Network component unreachable: " + Translate(text["Componente di rete non raggiungibile: ".Length..]);
            if (text.StartsWith("Configurazione non riuscita: ", StringComparison.Ordinal))
                return "Configuration failed: " + Translate(text["Configurazione non riuscita: ".Length..]);
            if (text.StartsWith("Codice non generato: ", StringComparison.Ordinal))
                return "Code not generated: " + Translate(text["Codice non generato: ".Length..]);
            if (text.StartsWith("Richieste non abilitate: ", StringComparison.Ordinal))
                return "Requests not enabled: " + Translate(text["Richieste non abilitate: ".Length..]);
            if (text.StartsWith("Richieste non disabilitate: ", StringComparison.Ordinal))
                return "Requests not disabled: " + Translate(text["Richieste non disabilitate: ".Length..]);
            if (text.StartsWith("Disabilitazione non riuscita: ", StringComparison.Ordinal))
                return "Disabling failed: " + Translate(text["Disabilitazione non riuscita: ".Length..]);
            if (text.StartsWith("Revoca non riuscita: ", StringComparison.Ordinal))
                return "Revocation failed: " + Translate(text["Revoca non riuscita: ".Length..]);
            if (text.StartsWith("Disinstallazione non riuscita: ", StringComparison.Ordinal))
                return "Uninstallation failed: " + Translate(text["Disinstallazione non riuscita: ".Length..]);
            if (text.StartsWith("Valido fino alle ", StringComparison.Ordinal) && text.EndsWith("; monouso", StringComparison.Ordinal))
                return "Valid until " + text["Valido fino alle ".Length..^"; monouso".Length] + "; single use";
            if (text.StartsWith("Configurazione ibrida · ", StringComparison.Ordinal) && text.EndsWith(" GPU rilevate", StringComparison.Ordinal))
                return "Hybrid configuration · " + text["Configurazione ibrida · ".Length..^" GPU rilevate".Length] + " GPUs detected";
            if (text.Contains(" GPU rilevate · ", StringComparison.Ordinal))
            {
                var split = text.Split(" GPU rilevate · ", 2, StringSplitOptions.None);
                return split[0] + " GPUs detected · " + Translate(split[1]);
            }
            if (text.Contains(" — GPU dedicata", StringComparison.Ordinal) ||
                text.Contains(" — GPU integrata", StringComparison.Ordinal) ||
                text.Contains(" — GPU virtuale", StringComparison.Ordinal) ||
                text.Contains(" — tipo non determinato", StringComparison.Ordinal))
                return text.Replace(" — GPU dedicata", " — dedicated GPU", StringComparison.Ordinal)
                    .Replace(" — GPU integrata", " — integrated GPU", StringComparison.Ordinal)
                    .Replace(" — GPU virtuale", " — virtual GPU", StringComparison.Ordinal)
                    .Replace(" — tipo non determinato", " — undetermined type", StringComparison.Ordinal);
            if (text.EndsWith(" dedicati", StringComparison.Ordinal))
                return text[..^" dedicati".Length] + " dedicated";
            if (text.Contains(" · versione ", StringComparison.Ordinal))
                return text.Replace(" · versione ", " · version ", StringComparison.Ordinal);
            return text;
        }
        if (DynamicEnglishToItalian.TryGetValue(text, out var dynamicItalian)) return dynamicItalian;
        if (EnglishToItalian.TryGetValue(text, out var italian)) return italian;
        if (text.StartsWith("Updating: ", StringComparison.Ordinal))
            return "Aggiornamento: " + text["Updating: ".Length..];
        if (text.StartsWith("Open ") && text.EndsWith(" support", StringComparison.Ordinal))
            return "Apri supporto " + text["Open ".Length..^" support".Length];
        if (text.StartsWith("Network component unreachable: ", StringComparison.Ordinal))
            return "Componente di rete non raggiungibile: " + Translate(text["Network component unreachable: ".Length..]);
        if (text.StartsWith("Configuration failed: ", StringComparison.Ordinal))
            return "Configurazione non riuscita: " + Translate(text["Configuration failed: ".Length..]);
        if (text.StartsWith("Code not generated: ", StringComparison.Ordinal))
            return "Codice non generato: " + Translate(text["Code not generated: ".Length..]);
        if (text.StartsWith("Requests not enabled: ", StringComparison.Ordinal))
            return "Richieste non abilitate: " + Translate(text["Requests not enabled: ".Length..]);
        if (text.StartsWith("Requests not disabled: ", StringComparison.Ordinal))
            return "Richieste non disabilitate: " + Translate(text["Requests not disabled: ".Length..]);
        if (text.StartsWith("Disabling failed: ", StringComparison.Ordinal))
            return "Disabilitazione non riuscita: " + Translate(text["Disabling failed: ".Length..]);
        if (text.StartsWith("Revocation failed: ", StringComparison.Ordinal))
            return "Revoca non riuscita: " + Translate(text["Revocation failed: ".Length..]);
        if (text.StartsWith("Uninstallation failed: ", StringComparison.Ordinal))
            return "Disinstallazione non riuscita: " + Translate(text["Uninstallation failed: ".Length..]);
        if (text.StartsWith("Valid until ", StringComparison.Ordinal) && text.EndsWith("; single use", StringComparison.Ordinal))
            return "Valido fino alle " + text["Valid until ".Length..^"; single use".Length] + "; monouso";
        if (text.StartsWith("Hybrid configuration · ", StringComparison.Ordinal) && text.EndsWith(" GPUs detected", StringComparison.Ordinal))
            return "Configurazione ibrida · " + text["Hybrid configuration · ".Length..^" GPUs detected".Length] + " GPU rilevate";
        if (text.Contains(" GPUs detected · ", StringComparison.Ordinal))
        {
            var split = text.Split(" GPUs detected · ", 2, StringSplitOptions.None);
            return split[0] + " GPU rilevate · " + Translate(split[1]);
        }
        if (text.Contains(" — dedicated GPU", StringComparison.Ordinal) ||
            text.Contains(" — integrated GPU", StringComparison.Ordinal) ||
            text.Contains(" — virtual GPU", StringComparison.Ordinal) ||
            text.Contains(" — undetermined type", StringComparison.Ordinal))
            return text.Replace(" — dedicated GPU", " — GPU dedicata", StringComparison.Ordinal)
                .Replace(" — integrated GPU", " — GPU integrata", StringComparison.Ordinal)
                .Replace(" — virtual GPU", " — GPU virtuale", StringComparison.Ordinal)
                .Replace(" — undetermined type", " — tipo non determinato", StringComparison.Ordinal);
        if (text.EndsWith(" dedicated", StringComparison.Ordinal))
            return text[..^" dedicated".Length] + " dedicati";
        if (text.Contains(" · version ", StringComparison.Ordinal))
            return text.Replace(" · version ", " · versione ", StringComparison.Ordinal);
        return text;
    }

    public static CultureInfo Culture => IsEnglish
        ? CultureInfo.GetCultureInfo("en-US")
        : CultureInfo.GetCultureInfo("it-IT");

    public static void ApplyTo(DependencyObject root)
    {
        if (root == null) return;

        // IMPORTANT: do not walk the live visual tree on every language switch.
        // MainWindow contains large virtualized lists/grids; traversing template visuals and
        // generated item containers caused noticeable UI/PC stalls in the past.
        // Discover only the static/logical localization surface once and remember each
        // element's original XAML text. Subsequent IT <-> EN switches only assign cached
        // values, so they neither traverse the UI nor depend on reverse-translation guesses.
        RootCaches.GetValue(root, BuildCache).Apply();
    }

    private static LocalizationRootCache BuildCache(DependencyObject root)
    {
        var targets = new List<StaticLocalizationTarget>();
        var visited = new HashSet<DependencyObject>();
        CollectLogicalLocalizationTargets(root, targets, visited);
        return new LocalizationRootCache(targets);
    }

    private static void CollectLogicalLocalizationTargets(
        DependencyObject root,
        List<StaticLocalizationTarget> targets,
        HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root)) return;

        CollectElementTargets(root, targets);

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject depChild)
                CollectLogicalLocalizationTargets(depChild, targets, visited);
        }

        // Static ComboBox/ListBox items declared in XAML are not always exposed through the
        // logical children collection until their popup/template is realized. Localize only
        // static items; never enumerate ItemsSource/data rows.
        if (root is ItemsControl { ItemsSource: null } itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is DependencyObject depItem)
                    CollectLogicalLocalizationTargets(depItem, targets, visited);
            }
        }

        if (root is Popup { Child: DependencyObject popupChild })
            CollectLogicalLocalizationTargets(popupChild, targets, visited);
    }

    private static void CollectElementTargets(DependencyObject element, List<StaticLocalizationTarget> targets)
    {
        if (element is Window window && !string.IsNullOrEmpty(window.Title))
            targets.Add(new StaticLocalizationTarget(window.Title, value => window.Title = value));

        if (element is TextBlock textBlock)
        {
            if (!BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty) && !string.IsNullOrEmpty(textBlock.Text))
                targets.Add(new StaticLocalizationTarget(textBlock.Text, value => textBlock.Text = value));

            foreach (var run in textBlock.Inlines.OfType<Run>()
                         .Where(x => !BindingOperations.IsDataBound(x, Run.TextProperty)))
            {
                if (!string.IsNullOrEmpty(run.Text))
                    targets.Add(new StaticLocalizationTarget(run.Text, value => run.Text = value, preserveWhitespace: true));
            }
        }

        if (element is ContentControl contentControl &&
            !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty) &&
            contentControl.Content is string content)
            targets.Add(new StaticLocalizationTarget(content, value => contentControl.Content = value));

        if (element is HeaderedContentControl headeredContent && headeredContent.Header is string header)
            targets.Add(new StaticLocalizationTarget(header, value => headeredContent.Header = value));

        if (element is HeaderedItemsControl headeredItems && headeredItems.Header is string itemsHeader)
            targets.Add(new StaticLocalizationTarget(itemsHeader, value => headeredItems.Header = value));

        if (ToolTipService.GetToolTip(element) is string toolTipText)
            targets.Add(new StaticLocalizationTarget(toolTipText, value => ToolTipService.SetToolTip(element, value)));

        if (element is DataGrid grid)
        {
            foreach (var column in grid.Columns)
            {
                if (column.Header is string columnHeader)
                {
                    var capturedColumn = column;
                    targets.Add(new StaticLocalizationTarget(columnHeader, value => capturedColumn.Header = value));
                }
            }
        }
    }

    private sealed class LocalizationRootCache(IReadOnlyList<StaticLocalizationTarget> targets)
    {
        public void Apply()
        {
            foreach (var target in targets) target.Apply();
        }
    }

    private sealed class StaticLocalizationTarget(string italianText, Action<string> setter, bool preserveWhitespace = false)
    {
        private readonly string _italianText = italianText;
        private readonly Action<string> _setter = setter;
        private readonly bool _preserveWhitespace = preserveWhitespace;

        public void Apply()
        {
            if (!IsEnglish)
            {
                _setter(_italianText);
                return;
            }

            _setter(_preserveWhitespace
                ? TranslatePreservingWhitespace(_italianText)
                : Translate(_italianText));
        }
    }

    private static string TranslatePreservingWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var leading = text.Length - text.TrimStart().Length;
        var trailing = text.Length - text.TrimEnd().Length;
        var coreLength = text.Length - leading - trailing;
        if (coreLength <= 0) return text;
        var core = text.Substring(leading, coreLength);
        return text[..leading] + Translate(core) + text[(text.Length - trailing)..];
    }
}
