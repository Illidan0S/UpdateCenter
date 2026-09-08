using System.Windows;
using System.Windows.Input;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

namespace UpdateCenter;

public partial class UpdateConfirmationWindow : Window
{
    private readonly bool _preflightCanContinue;
    private readonly bool _requiresRiskConfirmation;
    public bool ExcludeRiskyItems { get; private set; }

    public UpdateConfirmationWindow(
        IReadOnlyList<UpdateItem> items,
        PreflightResult preflight,
        bool restorePointEnabled,
        bool restorePointWillBeCreated)
    {
        InitializeComponent();
        _preflightCanContinue = preflight.CanContinue;
        _requiresRiskConfirmation = items.Any(x => x.RequiresRiskConfirmation);
        Loaded += (_, _) => LocalizationService.ApplyTo(this);
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
        _preflightCanContinue = true;
        _requiresRiskConfirmation = items.Any(x => x.RequiresRiskConfirmation);
        Loaded += (_, _) => LocalizationService.ApplyTo(this);
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmButton.IsEnabled) DialogResult = true;
    }
    private void RiskAcceptanceChanged(object sender, RoutedEventArgs e)
    {
        ConfirmButton.IsEnabled = _preflightCanContinue && RiskAcceptanceCheckBox.IsChecked == true;
        ConfirmButton.Content = !_preflightCanContinue
            ? LocalizationService.Text("Controlli non superati", "Pre-checks failed")
            : ConfirmButton.IsEnabled
                ? LocalizationService.Text("Conferma e aggiorna", "Confirm and update")
                : LocalizationService.Text("Conferma il rischio", "Confirm risk");
    }
    private void ExcludeRiskItems_Click(object sender, RoutedEventArgs e)
    {
        ExcludeRiskyItems = true;
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
