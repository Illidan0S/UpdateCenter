using System.Windows;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

namespace UpdateCenter;

public partial class AgentSetupWindow : Window
{
    private readonly LocalAgentSetupViewModel _viewModel = new();

    public AgentSetupWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) =>
        {
            Title = $"{LocalizationService.Translate("Configura questo PC")} · Update Center";
            LocalizationService.ApplyTo(this);
            await _viewModel.RefreshAsync();
        };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private async void Setup_Click(object sender, RoutedEventArgs e) => await _viewModel.SetupAsync();
    private async void GenerateCode_Click(object sender, RoutedEventArgs e) => await _viewModel.GeneratePairingCodeAsync();
    private async void EnableRequests_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.EnableConnectionRequestsAsync();
    private async void DisableRequests_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.DisableConnectionRequestsAsync();
    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                LocalizationService.Text("Disabilitare la gestione di rete su questo PC? Le regole firewall private verranno rimosse.", "Disable network management on this PC? Private firewall rules will be removed."),
                LocalizationService.Text("Disabilita gestione rete", "Disable network management"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _viewModel.DisableAsync();
    }

    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                LocalizationService.Text("Revocare il PC principale attuale? Non potrà più controllare questo dispositivo.", "Revoke the current controller PC? It will no longer be able to control this device."),
                LocalizationService.Text("Revoca PC principale", "Revoke controller PC"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await _viewModel.RevokeControllerAsync();
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                LocalizationService.Text("Disinstallare completamente il componente di rete?\n\nVerranno rimossi servizio, autorizzazioni, regole firewall, file e dati del componente. L'app Update Center resterà installata.", "Uninstall the network component completely?\n\nThe service, authorizations, firewall rules, files, and component data will be removed. The Update Center app will remain installed."),
                LocalizationService.Text("Disinstalla componente di rete", "Uninstall network component"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await _viewModel.UninstallAsync();
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CanCopyCode) Clipboard.SetText(_viewModel.PairingCode);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
