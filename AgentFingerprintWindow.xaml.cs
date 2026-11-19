using System.Windows;
using UpdateCenter.RemoteClient;
using UpdateCenter.Services;

namespace UpdateCenter;

public partial class AgentFingerprintWindow : Window
{
    private AgentFingerprintWindow(string endpoint)
    {
        InitializeComponent();
        MainWindow.UpdateSettingsCardTheme(this, "HardwareNetwork");
        EndpointText.Text = endpoint;
        LocalizationService.ApplyTo(this);
        Loaded += (_, _) => FingerprintBox.Focus();
    }

    public static string? Prompt(Window owner, string endpoint)
    {
        var dialog = new AgentFingerprintWindow(endpoint) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.FingerprintBox.Text.Trim() : null;
    }

    private void Fingerprint_Changed(object sender, RoutedEventArgs e)
    {
        if (ConfirmButton is not null)
            ConfirmButton.IsEnabled = RemoteAgentClient.IsValidFingerprint(FingerprintBox.Text);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
