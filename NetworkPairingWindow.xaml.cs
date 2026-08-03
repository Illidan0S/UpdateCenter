using System.Windows;
using System.Windows.Input;
using UpdateCenter.ViewModels;

namespace UpdateCenter;

public partial class NetworkPairingWindow : Window
{
    private readonly NetworkManagementViewModel _viewModel;

    public NetworkPairingWindow(NetworkManagementViewModel viewModel, NetworkAgentItem agent)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.SelectedAgent = agent;
        DataContext = _viewModel;
        Loaded += (_, _) => PairingCodeBox.Focus();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void PairingCodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !_viewModel.CanPair) return;
        e.Handled = true;
        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (!_viewModel.CanPair) return;
        await _viewModel.PairAsync();
        if (_viewModel.SelectedAgent?.IsPaired == true) DialogResult = true;
    }
}
