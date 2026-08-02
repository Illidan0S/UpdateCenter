using System.Windows;
using UpdateCenter.Contracts;
using UpdateCenter.Core;

namespace UpdateCenter;

public partial class ConnectionRequestWindow : Window
{
    private readonly Guid _requestId;
    private readonly AgentLocalClient _client = new(AgentProtocol.ApprovalPipeName);
    private PendingConnectionRequest? _request;

    public ConnectionRequestWindow(Guid requestId)
    {
        InitializeComponent();
        _requestId = requestId;
        Loaded += async (_, _) => await LoadRequestAsync();
    }

    private async Task LoadRequestAsync()
    {
        SetBusy(true);
        try
        {
            var response = await _client.SendAsync(new AgentRequest
            {
                Command = AgentCommands.GetPendingConnectionRequests
            }, TimeSpan.FromSeconds(10));
            if (!response.Success) throw new InvalidOperationException(response.Message);
            _request = response.ConnectionRequests.FirstOrDefault(x => x.RequestId == _requestId);
            if (_request is null)
            {
                StatusText.Text = "La richiesta non è più disponibile oppure è scaduta.";
                AcceptButton.IsEnabled = false;
                RejectButton.Content = "Chiudi";
                return;
            }
            ControllerNameText.Text = _request.ControllerName;
            ControllerAddressText.Text = $"Indirizzo: {_request.RemoteAddress}";
            ControllerFingerprintText.Text = $"Identità: {ShortFingerprint(_request.ControllerCertificateSha256)}";
            ExpiryText.Text = $"Richiesta valida fino alle {_request.ExpiresUtc.ToLocalTime():HH:mm:ss}.";
            StatusText.Text = "Accetta soltanto se riconosci il computer indicato.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Impossibile leggere la richiesta: {ex.GetBaseException().Message}";
            AcceptButton.IsEnabled = false;
            RejectButton.Content = "Chiudi";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Accept_Click(object sender, RoutedEventArgs e) => await DecideAsync(true);

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (_request is null) { Close(); return; }
        await DecideAsync(false);
    }

    private async Task DecideAsync(bool accept)
    {
        if (_request is null) return;
        SetBusy(true);
        try
        {
            var response = await _client.SendAsync(new AgentRequest
            {
                Command = AgentCommands.RespondConnectionRequest,
                ConnectionDecision = new ConnectionRequestDecision
                {
                    RequestId = _request.RequestId,
                    Accept = accept
                }
            }, TimeSpan.FromSeconds(15));
            if (!response.Success) throw new InvalidOperationException(response.Message);
            MessageBox.Show(
                accept
                    ? $"Questo PC è ora collegato a {_request.ControllerName}."
                    : "Richiesta di collegamento rifiutata.",
                "Update Center",
                MessageBoxButton.OK,
                accept ? MessageBoxImage.Information : MessageBoxImage.None);
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Operazione non completata: {ex.GetBaseException().Message}";
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        AcceptButton.IsEnabled = !busy && _request is not null;
        RejectButton.IsEnabled = !busy;
    }

    private static string ShortFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "non disponibile";
        var compact = value.Replace(" ", "", StringComparison.Ordinal);
        var visible = compact.Length <= 24 ? compact : compact[..24];
        return string.Join(":", Enumerable.Range(0, (visible.Length + 3) / 4)
            .Select(index => visible.Substring(index * 4, Math.Min(4, visible.Length - index * 4))));
    }
}
