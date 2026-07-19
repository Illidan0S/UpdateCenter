using System.Windows;
using System.Windows.Input;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter;

public partial class UpdateConfirmationWindow : Window
{
    public UpdateConfirmationWindow(
        IReadOnlyList<UpdateItem> items,
        PreflightResult preflight,
        bool restorePointEnabled,
        bool restorePointWillBeCreated)
    {
        InitializeComponent();
        ItemsGrid.ItemsSource = items;

        SummaryText.Text = items.Count == 1
            ? $"1 aggiornamento selezionato"
            : $"{items.Count} aggiornamenti selezionati";
        ImportantCountText.Text = items.Count(x => x.IsImportant).ToString();
        SoftwareCountText.Text = items.Count(x => x.Kind == UpdateKind.Software).ToString();
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmButton.IsEnabled) DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
