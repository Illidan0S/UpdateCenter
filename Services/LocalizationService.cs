using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace UpdateCenter.Services;

public static class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> ItalianToEnglish =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
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
            ["Ignora questa versione"] = "Ignore this version"
        };

    private static readonly IReadOnlyDictionary<string, string> EnglishToItalian =
        ItalianToEnglish
            .Where(x => !x.Key.Equals(x.Value, StringComparison.Ordinal))
            .GroupBy(x => x.Value, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Key, StringComparer.Ordinal);

    public static string CurrentLanguage { get; private set; } = "it";
    public static bool IsEnglish => CurrentLanguage == "en";

    public static void Initialize(string? language) => CurrentLanguage = Normalize(language);

    public static string Normalize(string? language) =>
        language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "it";

    public static string Text(string italian, string english) => IsEnglish ? english : italian;

    public static string Translate(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (IsEnglish)
            return ItalianToEnglish.TryGetValue(text, out var english) ? english : text;
        return EnglishToItalian.TryGetValue(text, out var italian) ? italian : text;
    }

    public static CultureInfo Culture => IsEnglish
        ? CultureInfo.GetCultureInfo("en-US")
        : CultureInfo.GetCultureInfo("it-IT");

    public static void ApplyTo(DependencyObject root)
    {
        LocalizeElement(root);
        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < children; index++)
            ApplyTo(VisualTreeHelper.GetChild(root, index));
    }

    private static void LocalizeElement(DependencyObject element)
    {
        if (element is TextBlock textBlock)
        {
            var textIsBound = BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty);
            var runs = textBlock.Inlines.OfType<Run>().ToList();
            if (!textIsBound && runs.Count > 0)
            {
                foreach (var run in runs.Where(x => !BindingOperations.IsDataBound(x, Run.TextProperty)))
                    run.Text = TranslatePreservingWhitespace(run.Text);
            }
            else if (!textIsBound)
            {
                textBlock.Text = Translate(textBlock.Text);
            }
        }

        if (element is ContentControl contentControl &&
            !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty) &&
            contentControl.Content is string content)
            contentControl.Content = Translate(content);

        if (element is HeaderedContentControl headeredContent && headeredContent.Header is string header)
            headeredContent.Header = Translate(header);

        if (element is HeaderedItemsControl headeredItems && headeredItems.Header is string itemsHeader)
            headeredItems.Header = Translate(itemsHeader);

        var toolTip = ToolTipService.GetToolTip(element);
        if (toolTip is string toolTipText)
            ToolTipService.SetToolTip(element, Translate(toolTipText));

        if (element is DataGrid grid)
        {
            foreach (var column in grid.Columns)
            {
                if (column.Header is string columnHeader)
                    column.Header = Translate(columnHeader);
            }
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
