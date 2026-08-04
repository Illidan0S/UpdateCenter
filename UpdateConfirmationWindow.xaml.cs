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
            ? $"1 aggiornamento selezionato"
            : $"{items.Count} aggiornamenti selezionati";
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
            ConfirmButton.Content = "Controlli non superati";
            FooterInfoText.Text = "Correggi i problemi indicati e riprova.";
        }

        if (_requiresRiskConfirmation)
        {
            RiskItemsList.ItemsSource = items.Where(x => x.RequiresRiskConfirmation).Select(x => x.Name).ToList();
            RiskConfirmationPanel.Visibility = Visibility.Visible;
            if (_preflightCanContinue)
            {
                ConfirmButton.IsEnabled = false;
                ConfirmButton.Content = "Conferma il rischio";
                FooterInfoText.Text = "La conferma aggiuntiva è necessaria per gli installer con rimozione preventiva.";
            }
        }

        RestorePointText.Text = restorePointWillBeCreated
            ? "Verrà richiesto un solo punto di ripristino per l'intero gruppo prima di installare driver o aggiornamenti importanti."
            : restorePointEnabled
                ? "Non necessario: il gruppo contiene soltanto aggiornamenti software non classificati come importanti."
                : "Disattivato nelle Impostazioni.";

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
        DiskHeadingText.Text = "PACCHETTI / SPAZIO PER PC";

        SummaryText.Text = items.Count == 1
            ? "1 aggiornamento remoto selezionato"
            : $"{items.Count} aggiornamenti remoti selezionati su {items.Select(x => x.AgentId).Distinct().Count()} PC";
        ImportantCountText.Text = items.Count(x => x.IsImportant).ToString();
        SoftwareCountText.Text = items.Count(x => x.Kind is "Software" or "Runtime").ToString();
        DriverCountText.Text = items.Count(x => x.Kind == "Driver").ToString();

        var remoteSummary = RemoteUpdateConfirmationService.Build(items, agents);
        PowerStatusText.Text = remoteSummary.PowerStatus;
        DiskStatusText.Text = remoteSummary.DiskStatus;

        RestorePointText.Text = "Gli aggiornamenti vengono eseguiti separatamente su ogni PC. " +
                               "Le protezioni configurate localmente restano applicate sul relativo dispositivo.";
        FooterInfoText.Text = "L'avvio e l'avanzamento resteranno separati per ciascun PC.";

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
            ConfirmButton.Content = "Conferma il rischio";
            FooterInfoText.Text = "Puoi includere gli elementi rischiosi oppure continuare escludendoli.";
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
            ? "Controlli non superati"
            : ConfirmButton.IsEnabled ? "Conferma e aggiorna" : "Conferma il rischio";
    }
    private void ExcludeRiskItems_Click(object sender, RoutedEventArgs e)
    {
        ExcludeRiskyItems = true;
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
