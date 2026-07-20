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
            ["ALIMENTAZIONE"] = "POWER",
            ["Apri log"] = "Open logs",
            ["Avvisi prima di continuare"] = "Warnings before continuing",
            ["Caratteristiche hardware"] = "Hardware specifications",
            ["Cerca nei dispositivi"] = "Search devices",
            ["Cerca per dispositivo, categoria, produttore, versione o ID hardware"] = "Search by device, category, manufacturer, version or hardware ID",
            ["Cerca per nome, produttore, versione, fonte o stato"] = "Search by name, manufacturer, version, source or status",
            ["COMPUTER"] = "COMPUTER",
            ["Conferma e aggiorna"] = "Confirm and update",
            ["Controlla gli elementi: l'installazione inizierà solo dopo la tua conferma."] = "Review the items: installation starts only after your confirmation.",
            ["CONTROLLI PRODUTTORE"] = "MANUFACTURER CHECKS",
            ["Copia CPU, GPU, temperature, utilizzo, RAM, schermo e versione di Windows in un formato leggibile."] = "Copy CPU, GPU, temperatures, usage, RAM, display and Windows version in a readable format.",
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
            ["Riepilogo prima dell'installazione"] = "Review before installation",
            ["RISOLUZIONE"] = "RESOLUTION",
            ["Scansioni, impostazioni, cronologia e log restano memorizzati localmente sul computer."] = "Scans, settings, history and logs remain stored locally on the computer.",
            ["Scegli il tema oppure segui automaticamente quello delle app di Windows."] = "Choose a theme or automatically follow the Windows app theme.",
            ["SCHEDA VIDEO"] = "GRAPHICS CARD",
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
