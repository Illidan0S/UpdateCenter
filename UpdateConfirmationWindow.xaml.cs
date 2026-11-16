using System.Windows;
using System.Windows.Input;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

namespace UpdateCenter;

public partial class UpdateConfirmationWindow : Window
{
    private readonly bool _preflightCanContinue;
    private readonly bool _preliminary;
    private readonly bool _requiresRiskConfirmation;
    public bool ExcludeRiskyItems { get; private set; }

    private bool _decisionMade;
    private readonly bool _authoritative;

    internal UpdateConfirmationWindow(IReadOnlyList<RunnerPlanSnapshot> plans, PreflightResult preflight,
        IReadOnlyList<InstallationSummaryHint>? hints = null)
    {
        InitializeComponent();
        DialogPresentation.Initialize(this, () => Cancel_Click(this, new RoutedEventArgs()), () => CancelButton);
        _authoritative = true;
        _preflightCanContinue = preflight.CanContinue && plans.Count > 0;
        var frozen = plans.SelectMany(plan => plan.Items).ToArray();
        if (hints is not null && (hints.Count != frozen.Length || frozen.Any(item => hints.Count(hint => hint.Matches(item)) != 1)))
            throw UnifiedConsentGate.RestartRequired();
        var rows = frozen.Select(item => InstallationSummaryRow.From(item, hints?.Single(hint => hint.Matches(item)))).ToArray();
        // The remote/preliminary risk controls are not part of the local dialog.
        ((System.Windows.Controls.Panel)RiskConfirmationPanel.Parent).Children.Remove(RiskConfirmationPanel);
        SummaryText.Text = LocalizationService.Text($"{rows.Length} aggiornamenti selezionati", $"{rows.Length} updates selected");
        if (rows.Length == 1) SummaryText.Text = LocalizationService.Text("1 aggiornamento selezionato", "1 update selected");
        ImportantCountText.Text = rows.Count(row => row.IsImportant).ToString();
        SoftwareCountText.Text = frozen.Count(item => item.Kind is nameof(UpdateKind.Software) or nameof(UpdateKind.Runtime)).ToString();
        DriverCountText.Text = frozen.Count(item => item.Kind == nameof(UpdateKind.Driver)).ToString();
        ConfirmationPurposeText.Text = LocalizationService.Text("Controlla gli aggiornamenti, poi conferma per avviare l'installazione.",
            "Review the updates, then confirm to start installation.");
        PowerStatusText.Text = preflight.PowerStatus;
        DiskStatusText.Text = preflight.DiskStatus;
        RestorePointPanel.Visibility = plans.Any(plan => plan.CreateRestorePoint) ? Visibility.Visible : Visibility.Collapsed;
        RestorePointText.Text = LocalizationService.Text("Prima dell'installazione verrà richiesto un punto di ripristino.",
            "A restore point will be requested before installation.");
        if (preflight.BlockingIssues.Count > 0)
        {
            BlockingList.ItemsSource = preflight.BlockingIssues.ToArray();
            BlockingPanel.Visibility = Visibility.Visible;
        }
        if (preflight.Warnings.Count > 0)
        {
            WarningsList.ItemsSource = preflight.Warnings.ToArray();
            WarningsPanel.Visibility = Visibility.Visible;
        }
        FooterInfoText.Text = LocalizationService.Text("L'installazione inizierà dopo la conferma.", "Installation will start after confirmation.");
        ItemsGrid.Columns.Clear();
        ItemsGrid.Columns.Add(new System.Windows.Controls.DataGridTemplateColumn
        {
            Header = LocalizationService.Text("Programma", "App"), CellTemplate = (DataTemplate)ItemsGrid.FindResource("InstallationProgramCell"),
            Width = new System.Windows.Controls.DataGridLength(2, System.Windows.Controls.DataGridLengthUnitType.Star), MinWidth = 150
        });
        foreach (var (header, binding) in new[] {
            (LocalizationService.Text("Versione attuale", "Current version"), "InstalledVersion"),
            (LocalizationService.Text("Nuova versione", "New version"), "AvailableVersion") })
            ItemsGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Star), MinWidth = 85,
                ElementStyle = (Style)ItemsGrid.FindResource("OperationsText") });
        ItemsGrid.ItemsSource = rows;
        ConfirmButton.IsEnabled = _preflightCanContinue;
        ConfirmButton.Content = _preflightCanContinue ? LocalizationService.Text("Conferma e aggiorna", "Confirm and update")
            : LocalizationService.Text("Controlli non superati", "Pre-checks failed");
    }
    public UpdateConfirmationWindow(
        IReadOnlyList<UpdateItem> items,
        PreflightResult preflight,
        bool restorePointEnabled,
        bool restorePointWillBeCreated)
    {
        InitializeComponent();
        DialogPresentation.Initialize(this, () => Cancel_Click(this, new RoutedEventArgs()), () => CancelButton);
        _preliminary = true;
        ConfirmationPurposeText.Text = LocalizationService.Text(
            "Riepilogo preliminare. Ogni batch richiederà una nuova conferma finale nel runner sul piano effettivo.",
            "Preliminary summary. Each batch requires a new final confirmation in the runner for its effective plan.");
        ConfirmButton.Content = LocalizationService.Text("Continua alla conferma finale", "Continue to final confirmation");
        _preflightCanContinue = preflight.CanContinue;
        _requiresRiskConfirmation = items.Any(x => x.RequiresRiskConfirmation);
        ItemsGrid.ItemsSource = items;

        SummaryText.Text = items.Count == 1
            ? LocalizationService.Text("1 aggiornamento selezionato", "1 update selected")
            : LocalizationService.Text($"{items.Count} aggiornamenti selezionati", $"{items.Count} updates selected");
        ImportantCountText.Text = items.Count(x => x.IsImportant).ToString();
        SoftwareCountText.Text = items.Count(x => x.Kind is UpdateKind.Software or UpdateKind.Runtime).ToString();
        DriverCountText.Text = items.Count(x => x.Kind == UpdateKind.Driver).ToString();
        PowerStatusText.Text = preflight.PowerStatus;
        DiskStatusText.Text = preflight.DiskStatus;

        if (!preflight.CanContinue)
        {
            BlockingList.ItemsSource = preflight.BlockingIssues;
            BlockingPanel.Visibility = Visibility.Visible;
            ConfirmButton.IsEnabled = false;
            ConfirmButton.Content = LocalizationService.Text("Controlli non superati", "Pre-checks failed");
            FooterInfoText.Text = LocalizationService.Text("Correggi i problemi indicati e riprova.", "Resolve the indicated issues and retry.");
        }

        if (_requiresRiskConfirmation)
        {
            RiskItemsList.ItemsSource = items.Where(x => x.RequiresRiskConfirmation).Select(x => x.Name).ToList();
            RiskConfirmationPanel.Visibility = Visibility.Visible;
            if (_preflightCanContinue)
            {
                ConfirmButton.IsEnabled = false;
                ConfirmButton.Content = LocalizationService.Text("Conferma il rischio", "Confirm risk");
                FooterInfoText.Text = LocalizationService.Text("La conferma aggiuntiva è necessaria per gli installer con rimozione preventiva.", "Additional confirmation is required for installers with prior removal.");
            }
        }

        RestorePointText.Text = restorePointWillBeCreated
            ? LocalizationService.Text("Verrà richiesto un solo punto di ripristino per l'intero gruppo prima di installare driver o aggiornamenti importanti.", "A single restore point will be requested for the whole group before installing drivers or important updates.")
            : restorePointEnabled
                ? LocalizationService.Text("Non necessario: il gruppo contiene soltanto aggiornamenti software non classificati come importanti.", "Not needed: the group contains only software updates not classified as important.")
                : LocalizationService.Text("Disattivato nelle Impostazioni.", "Disabled in Settings.");

        if (preflight.Warnings.Count > 0)
        {
            WarningsList.ItemsSource = preflight.Warnings;
            WarningsPanel.Visibility = Visibility.Visible;
        }
    }

    public UpdateConfirmationWindow(
        IReadOnlyList<RemoteUpdateSelectionItem> items,
        IReadOnlyList<NetworkAgentItem> agents)
    {
        InitializeComponent();
        DialogPresentation.Initialize(this, () => Cancel_Click(this, new RoutedEventArgs()), () => CancelButton);
        _preflightCanContinue = true;
        _requiresRiskConfirmation = items.Any(x => x.RequiresRiskConfirmation);
        ItemsGrid.ItemsSource = items;
        DeviceColumn.Visibility = Visibility.Visible;
        DiskHeadingText.Text = LocalizationService.Text("PACCHETTI / SPAZIO PER PC", "PACKAGES / SPACE PER PC");

        var pcCount = items.Select(x => x.AgentId).Distinct().Count();
        SummaryText.Text = items.Count == 1
            ? LocalizationService.Text("1 aggiornamento remoto selezionato", "1 remote update selected")
            : LocalizationService.Text($"{items.Count} aggiornamenti remoti selezionati su {pcCount} PC", $"{items.Count} remote updates selected on {pcCount} PCs");
        ImportantCountText.Text = items.Count(x => x.IsImportant).ToString();
        SoftwareCountText.Text = items.Count(x => x.Kind is "Software" or "Runtime").ToString();
        DriverCountText.Text = items.Count(x => x.Kind == "Driver").ToString();

        var remoteSummary = RemoteUpdateConfirmationService.Build(items, agents);
        PowerStatusText.Text = remoteSummary.PowerStatus;
        DiskStatusText.Text = remoteSummary.DiskStatus;

        RestorePointText.Text = LocalizationService.Text(
            "Gli aggiornamenti vengono eseguiti separatamente su ogni PC. Le protezioni configurate localmente restano applicate sul relativo dispositivo.",
            "Updates run separately on each PC. Locally configured protections remain applied on the respective device.");
        FooterInfoText.Text = LocalizationService.Text(
            "L'avvio e l'avanzamento resteranno separati per ciascun PC.",
            "Startup and progress will remain separate for each PC.");

        if (remoteSummary.Warnings.Count > 0)
        {
            WarningsList.ItemsSource = remoteSummary.Warnings;
            WarningsPanel.Visibility = Visibility.Visible;
        }

        if (_requiresRiskConfirmation)
        {
            RiskItemsList.ItemsSource = remoteSummary.RiskItems;
            RiskConfirmationPanel.Visibility = Visibility.Visible;
            ExcludeRiskItemsButton.Visibility = Visibility.Visible;
            ConfirmButton.IsEnabled = false;
            ConfirmButton.Content = LocalizationService.Text("Conferma il rischio", "Confirm risk");
            FooterInfoText.Text = LocalizationService.Text("Puoi includere gli elementi rischiosi oppure continuare escludendoli.", "You can include risky items or continue excluding them.");
        }
    }

    private void ItemsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        DialogGridScroll.ForwardAtBoundary(ItemsGrid, ContentScrollViewer, e);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_decisionMade || !ConfirmButton.IsEnabled || (_authoritative && !_preflightCanContinue)) return;
        _decisionMade = true;
        ConfirmButton.IsEnabled = false;
        DialogResult = true;
    }
    private void RiskAcceptanceChanged(object sender, RoutedEventArgs e)
    {
        if (_authoritative || _decisionMade) return;
        ConfirmButton.IsEnabled = _preflightCanContinue && RiskAcceptanceCheckBox.IsChecked == true;
        ConfirmButton.Content = !_preflightCanContinue
            ? LocalizationService.Text("Controlli non superati", "Pre-checks failed")
            : ConfirmButton.IsEnabled
                ? (_preliminary
                ? LocalizationService.Text("Continua alla conferma finale", "Continue to final confirmation")
                : LocalizationService.Text("Conferma e aggiorna", "Confirm and update"))
                : LocalizationService.Text("Conferma il rischio", "Confirm risk");
    }
    private void ExcludeRiskItems_Click(object sender, RoutedEventArgs e)
    {
        if (_authoritative || _decisionMade || !_preflightCanContinue) return;
        _decisionMade = true;
        ExcludeRiskyItems = true;
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_decisionMade) return;
        _decisionMade = true;
        DialogResult = false;
    }
}
