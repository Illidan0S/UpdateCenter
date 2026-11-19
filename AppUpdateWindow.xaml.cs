using System.ComponentModel;
using System.Diagnostics;
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
        if (ThemeService.IsDark && !SystemParameters.HighContrast)
            UpdateButton.SetResourceReference(ForegroundProperty, "SurfaceBrush");
        var currentVersion = LocalizationService.Text(
            $"In uso {update.InstalledVersionLabel}", $"Current version {update.InstalledVersionLabel}");
        var package = update.IsInstallerPackage ? "Setup" : "Portable";
        AppVersionDetails.Text = update.DownloadSize > 0
            ? $"{currentVersion} · {package} · {update.DownloadSizeLabel}"
            : $"{currentVersion} · {package}";
        var notes = AppUpdateReleaseNotes.ToPlainText(update.ReleaseNotes, LocalizationService.CurrentLanguage);
        AppReleaseNotes.Text = notes.Length > 0 ? notes : LocalizationService.Text(
            "Le note di questa versione non sono disponibili.", "Release notes are unavailable for this version.");
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
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        DownloadStatus.Text = LocalizationService.Text("Preparazione del download…", "Preparing download…");
        ErrorDetailsPanel.Visibility = Visibility.Collapsed;
        _ = Dispatcher.InvokeAsync(() => ProgressPanel.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);

        var acceptingProgress = true;
        var progress = new Progress<AppUpdateDownloadProgress>(value =>
        {
            if (!acceptingProgress) return;
            DownloadProgress.Value = value.Percentage;
            DownloadStatus.Text = LocalizationService.Translate(value.Message);
        });

        try
        {
            await _service.DownloadAndStartUpdateAsync(_update, progress, CancellationToken.None);
            acceptingProgress = false;
            DownloadStatus.Text = LocalizationService.Text("Aggiornamento verificato. Update Center verrà riavviato…", "Update verified. Update Center will restart…");
            await Task.Delay(350);
            _isBusy = false;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            acceptingProgress = false;
            LogService.Write("Download dell'aggiornamento app non riuscito.", ex);
            DownloadStatus.Text = LocalizationService.Text("Aggiornamento non applicato. Puoi riprovare.",
                "Update not applied. You can try again.");
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

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_isBusy) e.Cancel = true;
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        try
        {
            Process.Start(new ProcessStartInfo(
                AppUpdateService.GetReleasePage(_update.AvailableVersion).AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            LogService.Write("Apertura della release GitHub non riuscita.", ex);
            ProgressPanel.Visibility = Visibility.Visible;
            DownloadProgress.Visibility = Visibility.Collapsed;
            DownloadStatus.Text = LocalizationService.Text("Impossibile aprire GitHub.", "Could not open GitHub.");
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        UpdateButton.IsEnabled = !busy;
        LaterButton.IsEnabled = !busy;
        IgnoreButton.IsEnabled = !busy;
        CloseButton.IsEnabled = !busy;
        ReleasePageButton.IsEnabled = !busy;
    }
}
