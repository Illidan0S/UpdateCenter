using System.Windows;
using System.Windows.Input;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter;

public partial class AppUpdateWindow : Window
{
    private readonly AppUpdateInfo _update;
    private readonly AppUpdateService _service;
    private bool _isBusy;

    public AppUpdateWindow(AppUpdateInfo update, AppUpdateService service)
    {
        InitializeComponent();
        DialogPresentation.Initialize(this, () => Later_Click(this, new RoutedEventArgs()), () => LaterButton);
        _update = update;
        _service = service;
        DataContext = update;
    }

    public bool IgnoreRequested { get; private set; }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isBusy && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        SetBusy(true);
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        DownloadStatus.Text = LocalizationService.Text("Preparazione del download…", "Preparing download…");
        ErrorDetailsPanel.Visibility = Visibility.Collapsed;
        _ = Dispatcher.InvokeAsync(() => ProgressPanel.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);

        var progress = new Progress<AppUpdateDownloadProgress>(value =>
        {
            DownloadProgress.Value = value.Percentage;
            DownloadStatus.Text = LocalizationService.Translate(value.Message);
        });

        try
        {
            await _service.DownloadAndStartUpdateAsync(_update, progress, CancellationToken.None);
            DownloadStatus.Text = LocalizationService.Text("Aggiornamento verificato. Update Center verrà riavviato…", "Update verified. Update Center will restart…");
            await Task.Delay(350);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LogService.Write("Download dell'aggiornamento app non riuscito.", ex);
            DownloadStatus.Text = LocalizationService.Text("Aggiornamento non applicato: ", "Update not applied: ") + ex.Message;
            ErrorDetailsText.Text = ex.ToString();
            ErrorDetailsPanel.Visibility = Visibility.Visible;
            _ = Dispatcher.InvokeAsync(() => ErrorDetailsPanel.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
            DownloadProgress.Value = 0;
            SetBusy(false);
        }
    }

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        IgnoreRequested = true;
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBusy) Close();
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        UpdateButton.IsEnabled = !busy;
        LaterButton.IsEnabled = !busy;
        IgnoreButton.IsEnabled = !busy;
        CloseButton.IsEnabled = !busy;
    }
}
