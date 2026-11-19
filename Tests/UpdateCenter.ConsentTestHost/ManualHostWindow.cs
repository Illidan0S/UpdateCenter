using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter.ConsentTestHost;

internal sealed class ManualHostWindow : Window
{
    private readonly ComboBox _scenario = new() { SelectedIndex = 0, Margin = new Thickness(0, 6, 0, 10) };
    private readonly ComboBox _language = new() { ItemsSource = new[] { "Italiano", "English" }, SelectedIndex = 0, Width = 140 };
    private readonly CheckBox _silent = new() { Content = "Software silenzioso / silent software", IsChecked = true, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _open = new() { Content = "Apri dialogo reale / Open real dialog", Padding = new Thickness(14, 8, 14, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBox _result = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 130 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _cancel;
    private ScenarioSession? _session;

    internal ManualHostWindow()
    {
        Title = "UpdateCenter — TEST CONSENSO / CONSENT TEST — nessuna installazione";
        Width = 850; Height = 720; MinWidth = 600; MinHeight = 500;
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new DockPanel { Margin = new Thickness(18) };
        var controls = new StackPanel(); DockPanel.SetDock(controls, Dock.Top);
        controls.Children.Add(new TextBlock { Text = "TEST SICURO / SAFE TEST — solo dati sintetici",
            FontSize = 21, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        controls.Children.Add(new TextBlock { Text = "Approva registra soltanto chiamate simulate. Nessun WinGet, WUA, download, ripristino o elevazione.\nApproval only records simulated calls. Driver privileges are simulated; UAC is not tested.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        controls.Children.Add(_language);
        foreach (var label in new[] {
            "Software + runtime (batch)", "Driver + ripristino simulati / simulated restore",
            "Snapshot: file cambia 2.0 → 9.0 / changed file", "Retry: iniziale → recupero → nuovo batch / three confirmations",
            "Fallback interattivo / interactive fallback", "Solo recupero / recovery only",
            "Annulla token dopo 5 secondi / cancel token after 5 seconds" }) _scenario.Items.Add(label);
        controls.Children.Add(_scenario); controls.Children.Add(_silent); controls.Children.Add(_open);
        controls.Children.Add(new TextBlock { Text = "Nel dialogo: controlla il piano, spunta il rischio e autorizza, oppure Annulla / Escape / X.\nIn the dialog: inspect the plan, acknowledge risk and authorize, or Cancel / Escape / X.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        controls.Children.Add(_status); panel.Children.Add(controls); panel.Children.Add(_result); Content = panel;
        _result.Text = "Dati separati / isolated data:\n" + AppPaths.DataDirectory +
            "\n\nNessun caso eseguito / No case run. I log non certificano installazioni reali.";
        _open.Click += async (_, _) => await RunAsync();
        _timer.Tick += (_, _) => PollStatus();
        Closing += (_, _) => _cancel?.Cancel();
        Closed += (_, _) => _timer.Stop();
    }

    private async Task RunAsync()
    {
        _open.IsEnabled = _scenario.IsEnabled = _language.IsEnabled = _silent.IsEnabled = false;
        _cancel = new CancellationTokenSource();
        JsonStorage.SaveSettings(new AppSettings { LanguageMode = _language.SelectedIndex == 0 ? "it" : "en" });
        LocalizationService.Initialize(_language.SelectedIndex == 0 ? "it" : "en");
        _session = new ScenarioSession((ConsentScenario)_scenario.SelectedIndex, _silent.IsChecked == true,
            new WpfRunnerConsent(), _cancel);
        _result.Text = "TEST IN CORSO / TEST RUNNING\n" + _session.DirectoryPath;
        _timer.Start();
        try
        {
            await Task.Run(_session.Run);
            _result.Text = _session.Summary + "\n\n" + _session.DirectoryPath + "\nevidence.json\n\n" +
                string.Join("\n", _session.Attempts.Select(a => $"#{a.Number}: {a.Decision}; items={a.Snapshot.Count}")) +
                "\n\n" + string.Join("\n", _session.Calls.Select(c => $"#{c.Attempt}: {c.Operation} {c.Item?.Id} {c.Item?.AvailableVersion}; silent={c.Silent}; interactive={c.Interactive}"));
        }
        catch (Exception ex) { _result.Text = "ERROR — " + ex.Message + "\n" + _session.DirectoryPath; }
        finally
        {
            _timer.Stop(); PollStatus(); _cancel.Dispose(); _cancel = null;
            _open.IsEnabled = _scenario.IsEnabled = _language.IsEnabled = _silent.IsEnabled = true;
        }
    }

    private void PollStatus()
    {
        if (_session is null) return;
        var status = string.IsNullOrEmpty(_session.StatusPath) ? null : JsonStorage.Read<UpdateRunStatus>(_session.StatusPath);
        _status.Text = status is null ? "Dialogo diretto / direct dialog" :
            $"Runner: {status.State}; heartbeat UTC: {status.LastHeartbeatUtc:HH:mm:ss}; effetti solo simulati / simulated effects only";
    }
}
