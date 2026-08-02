using System.Windows;
using UpdateCenter.ViewModels;

namespace UpdateCenter;

public partial class AgentSetupWindow : Window
{
    private readonly LocalAgentSetupViewModel _viewModel = new();

    public AgentSetupWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
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
                "Disabilitare la gestione di rete su questo PC? Le regole firewall private verranno rimosse.",
                "Disabilita gestione rete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _viewModel.DisableAsync();
    }

    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Revocare il PC principale attuale? Non potrà più controllare questo dispositivo.",
                "Revoca PC principale",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await _viewModel.RevokeControllerAsync();
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Disinstallare completamente il componente di rete?\n\nVerranno rimossi servizio, autorizzazioni, regole firewall, file e dati del componente. L'app Update Center resterà installata.",
                "Disinstalla componente di rete",
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
