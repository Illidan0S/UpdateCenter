using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

namespace UpdateCenter;

public partial class UpdateProgressWindow : Window
{
    private bool _canClose;

    public event EventHandler? PresentationChanged;
    public bool IsOperationInProgress => !_canClose;
    public bool IsHiddenOrMinimized => !IsVisible || WindowState == WindowState.Minimized;

    public UpdateProgressWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += UpdateProgressWindow_Closing;
        IsVisibleChanged += (_, _) => PresentationChanged?.Invoke(this, EventArgs.Empty);
        StateChanged += (_, _) => PresentationChanged?.Invoke(this, EventArgs.Empty);
        Loaded += (_, _) => LocalizationService.ApplyTo(this);
    }

    public void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowCompleted(UpdateRunStatus result)
    {
        var succeeded = result.Results.Count(x => x.Success &&
            x.Outcome.Equals(UpdateOutcomes.Completed, StringComparison.Ordinal));
        var handled = result.Results.Count(x => x.Success &&
            !x.Outcome.Equals(UpdateOutcomes.Completed, StringComparison.Ordinal));
        var failed = result.Results.Count(x => !x.Success);

        _canClose = true;
        WindowTitleText.Text = LocalizationService.Text("Aggiornamenti completati", "Updates completed");
        StateTitleText.Text = failed == 0
            ? LocalizationService.Text("Tutto completato", "All done")
            : LocalizationService.Text("Completato con alcuni errori", "Completed with some errors");
        StateDetailText.Text = LocalizationService.IsEnglish
            ? $"{succeeded} updated, {handled} not automatically applicable, {failed} failed."
            : $"{succeeded} aggiornati, {handled} non applicabili automaticamente, {failed} non riusciti.";
        StateIconText.Text = failed == 0 ? "✓" : "!";
        StateIconText.Foreground = failed == 0
            ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
            : (System.Windows.Media.Brush)FindResource("WarningBrush");
        InstallationProgressBar.Value = 100;
        PercentageText.Text = "100%";
        OperationStatusText.Text = result.Message;
        SucceededCountText.Text = succeeded.ToString();
        FailedCountText.Text = failed.ToString();
        ResultCards.Visibility = Visibility.Visible;
        CloseButton.Visibility = Visibility.Visible;
        FooterText.Text = LocalizationService.Text(
            "Puoi chiudere questa finestra e continuare a usare Update Center.",
            "You can close this window and continue using Update Center.");

        var notices = new List<string>();
        if (result.RestorePointRequested && !result.RestorePointCreated)
            notices.Add(LocalizationService.Text(
                "Il punto di ripristino non è stato creato. Controlla Protezione sistema.",
                "The restore point was not created. Check System Protection."));
        if (result.RestartRequired)
        {
            notices.Add(LocalizationService.Text(
                "Windows richiede un riavvio per completare gli aggiornamenti.",
                "Windows must restart to complete the updates."));
            RestartButton.Visibility = Visibility.Visible;
            CloseButton.Content = LocalizationService.Text("Più tardi", "Later");
        }

        if (notices.Count > 0)
        {
            CompletionNoticeText.Text = string.Join(Environment.NewLine, notices);
            CompletionNotice.Visibility = Visibility.Visible;
        }

        BringToFront();
    }

    public void ShowFailure(string title, string detail)
    {
        _canClose = true;
        WindowTitleText.Text = LocalizationService.Text("Aggiornamento non completato", "Update not completed");
        StateTitleText.Text = title;
        StateDetailText.Text = detail;
        StateIconText.Text = "!";
        StateIconText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
        OperationStatusText.Text = LocalizationService.Text(
            "Nessuna ulteriore operazione è in corso.",
            "No further operation is running.");
        FooterText.Text = LocalizationService.Text("Puoi chiudere questa finestra.", "You can close this window.");
        CloseButton.Visibility = Visibility.Visible;
        BringToFront();
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            CompletionNoticeText.Text = LocalizationService.IsEnglish
                ? $"Restart could not be started: {ex.Message}"
                : $"Riavvio non avviato: {ex.Message}";
            CompletionNotice.Visibility = Visibility.Visible;
        }
    }

    private void UpdateProgressWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_canClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_canClose)
            Close();
        else
            Hide();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
