using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

namespace UpdateCenter;

public partial class UpdateProgressWindow : Window
{
    private bool _canClose;
    private bool _completionPresented;
    private readonly Dictionary<(string Id, string Kind), OperationRow> _operationRows = [];
    private OperationRow? _activeRow;

    public event EventHandler? PresentationChanged;
    public bool IsOperationInProgress => !_canClose;
    public bool IsHiddenOrMinimized => !IsVisible || WindowState == WindowState.Minimized;

    public UpdateProgressWindow(MainViewModel viewModel, IReadOnlyList<UpdateItem>? items = null)
    {
        InitializeComponent();
        DialogPresentation.Initialize(this, () => Close_Click(this, new RoutedEventArgs()), () => HeaderCloseButton);
        DataContext = viewModel;
        ActivityTitleText.Text = LocalizationService.Text("Preparazione dell'operazione", "Preparing the operation");
        VerifiedCountLabel.Text = LocalizationService.Text("VERIFICATI", "VERIFIED");
        NotApplicableCountLabel.Text = LocalizationService.Text("NON APPLICABILI", "NOT APPLICABLE");
        ApplicabilityReasonLabel.Text = LocalizationService.Text("Motivazione", "Reason");
        foreach (var item in items ?? Array.Empty<UpdateItem>())
            _operationRows.TryAdd(RowKey(item.Id, item.Kind.ToString()), new OperationRow(item));
        OperationItemsGrid.ItemsSource = _operationRows.Values;
        OperationItemsPanel.Visibility = _operationRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_operationRows.Count > 0) OperationItemsGrid.SelectedIndex = 0;
        ShowBatchProgress(0, _operationRows.Count);
        Loaded += (_, _) =>
        {
            viewModel.InstallationProgressChanged -= ShowLiveProgress;
            viewModel.InstallationProgressChanged += ShowLiveProgress;
        };
        Closed += (_, _) => viewModel.InstallationProgressChanged -= ShowLiveProgress;
        Closing += UpdateProgressWindow_Closing;
        IsVisibleChanged += (_, _) => PresentationChanged?.Invoke(this, EventArgs.Empty);
        StateChanged += (_, _) => PresentationChanged?.Invoke(this, EventArgs.Empty);
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
        var summary = UpdateSessionSummary.From(result.Results);
        var succeeded = summary.SucceededCount;
        var handled = summary.NotAutomaticallyApplicableCount;
        var failed = summary.FailedCount;
        var awaitingEvidence = summary.PendingVerificationCount + summary.PendingRestartCount > 0;

        _canClose = true;
        OperationItemsGrid.ItemsSource = result.Results.Select(item => new OperationRow(item)).ToList();
        OperationItemsPanel.Visibility = result.Results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (result.Results.Count > 0) OperationItemsGrid.SelectedIndex = 0;
        SetCompletedCloseLabel();
        WindowTitleText.Text = "Update Center";
        StateTitleText.Text = summary.ProcessedTerminalCount == 0
            ? LocalizationService.Text("Nessun aggiornamento eseguito", "No updates performed")
            : failed + handled > 0
                ? LocalizationService.Text("Operazione completata con problemi", "Operation completed with issues")
            : awaitingEvidence
                ? LocalizationService.Text("Installazioni terminate", "Installations finished")
            : summary.CancelledCount > 0
                ? LocalizationService.Text("Operazione annullata", "Operation cancelled")
                : LocalizationService.Text("Aggiornamenti completati", "Updates completed");
        Title = StateTitleText.Text;
        StateDetailText.Text = CompletionDescription(summary, result.Results.All(x => x.InstallerSucceeded));
        StateDetailText.Visibility = StateDetailText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        StateDetailText.Margin = new Thickness(0, 6, 0, 0);
        CompletionSummary.Padding = new Thickness(16);
        CompletionSummary.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        CompletionSummary.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        CompletionSummary.BorderThickness = new Thickness(1);
        StateIcon.Visibility = Visibility.Visible;
        StateIconText.Text = !summary.HasProblems && !awaitingEvidence && summary.ProcessedTerminalCount > 0 ? "✓" : "!";
        StateIconText.Foreground = !summary.HasProblems && !awaitingEvidence && summary.ProcessedTerminalCount > 0
            ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
            : (System.Windows.Media.Brush)FindResource("WarningBrush");
        ActivityTitleText.Text = LocalizationService.Text("Avanzamento del batch", "Batch progress");
        ShowBatchProgress(summary.ProcessedTerminalCount, result.Total);
        OperationStatusText.Text = LocalizationService.Translate(result.Message);
        SucceededCountText.Text = succeeded.ToString();
        FailedCountText.Text = failed.ToString();
        NotApplicableCountText.Text = handled.ToString();
        // Counts stay intact; the compact subtitle is their only summary presentation.
        VerifiedResultCard.Visibility = FailedResultCard.Visibility = NotApplicableResultCard.Visibility = Visibility.Collapsed;
        ResultCards.Visibility = Visibility.Collapsed;
        ActivityHeader.Visibility = OperationStatusText.Visibility = BatchProgressText.Visibility = Visibility.Collapsed;
        ActivityContainer.Background = Brushes.Transparent;
        ActivityContainer.Padding = new Thickness(0);
        ActivityContainer.Margin = new Thickness(0, 10, 0, 4);
        InstallationProgressBar.Height = 3;
        InstallationProgressBar.Margin = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(InstallationProgressBar,
            BatchProgressText.Text);
        PauseButton.Visibility = Visibility.Collapsed;
        PauseHintText.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        FooterText.Visibility = Visibility.Visible;
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
        AnimateCompletion();
    }

    private static string CompletionDescription(UpdateSessionSummary summary, bool installersSucceeded)
    {
        if (summary.ProcessedTerminalCount == 0) return "";
        if (summary.SucceededCount == summary.ProcessedTerminalCount)
        {
            if (!installersSucceeded)
                return summary.SucceededCount == 1
                    ? LocalizationService.Text("1 aggiornamento completato e verificato", "1 update completed and verified")
                    : LocalizationService.Text($"{summary.SucceededCount} aggiornamenti completati e verificati",
                        $"{summary.SucceededCount} updates completed and verified");
            return summary.SucceededCount == 1
                ? LocalizationService.Text("1 aggiornamento installato e verificato", "1 update installed and verified")
                : LocalizationService.Text($"{summary.SucceededCount} aggiornamenti installati e verificati",
                    $"{summary.SucceededCount} updates installed and verified");
        }
        var parts = new List<string>();
        void Add(int count, string singularIt, string pluralIt, string singularEn, string pluralEn)
        {
            if (count > 0) parts.Add(LocalizationService.Text($"{count} {(count == 1 ? singularIt : pluralIt)}",
                $"{count} {(count == 1 ? singularEn : pluralEn)}"));
        }
        Add(summary.FailedCount, "non riuscito", "non riusciti", "failed", "failed");
        Add(summary.NotAutomaticallyApplicableCount, "non applicabile automaticamente", "non applicabili automaticamente", "not automatically applicable", "not automatically applicable");
        Add(summary.PendingVerificationCount, "verifica automatica al prossimo avvio", "verifiche automatiche al prossimo avvio", "automatic check on next launch", "automatic checks on next launch");
        Add(summary.PendingRestartCount, "in attesa di riavvio", "in attesa di riavvio", "awaiting restart", "awaiting restart");
        Add(summary.CancelledCount, "annullato", "annullati", "cancelled", "cancelled");
        Add(summary.SucceededCount, "verificato", "verificati", "verified", "verified");
        return string.Join(" · ", parts);
    }

    private void AnimateCompletion()
    {
        if (_completionPresented) return;
        _completionPresented = true;
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) return;
        var duration = TimeSpan.FromMilliseconds(180);
        CompletionSummary.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop });
        var offset = new TranslateTransform();
        CompletionSummary.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, duration)
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    public void ShowFailure(string title, string detail)
    {
        _canClose = true;
        if (_activeRow is { HasResult: false }) _activeRow.SetUnavailable();
        ActivityTitleText.Text = LocalizationService.Text("Stato del batch", "Batch status");
        SetCompletedCloseLabel();
        WindowTitleText.Text = LocalizationService.Text("Aggiornamento non completato", "Update not completed");
        StateTitleText.Text = title;
        StateDetailText.Text = detail;
        StateDetailText.Visibility = Visibility.Visible;
        StateIcon.Visibility = Visibility.Visible;
        StateIconText.Text = "!";
        StateIconText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
        OperationStatusText.Text = LocalizationService.Text(
            "Nessuna ulteriore operazione è in corso.",
            "No further operation is running.");
        FooterText.Visibility = Visibility.Visible;
        FooterText.Text = LocalizationService.Text("Puoi chiudere questa finestra.", "You can close this window.");
        PauseButton.Visibility = Visibility.Collapsed;
        PauseHintText.Visibility = Visibility.Collapsed;
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

    private void SetCompletedCloseLabel()
    {
        HeaderCloseButton.ToolTip = LocalizationService.Translate("Chiudi");
        System.Windows.Automation.AutomationProperties.SetName(HeaderCloseButton, LocalizationService.Translate("Chiudi"));
    }

    private void ShowLiveProgress(UpdateRunStatus status)
    {
        if (_canClose) return;
        foreach (var result in status.Results.Concat(status.BatchResults))
        {
            if (_operationRows.TryGetValue(RowKey(result.Id, result.Kind), out var row) &&
                (result.ReportedTarget is null || row.Matches(result.ReportedTarget)))
                row.SetResult(result);
        }

        var hasActiveItem = status.State == "Running" && status.CurrentItemStartedUtc.HasValue;
        var verifying = status.Phase == "winget-verification";
        var previous = _activeRow;
        _activeRow = null;
        if (hasActiveItem && status.CurrentTarget is { } target &&
            _operationRows.TryGetValue(RowKey(target.Id, target.Kind), out var activeRow) && activeRow.Matches(target))
        {
            _activeRow = activeRow;
            activeRow.SetPhase(verifying, status.Message);
        }
        if (previous is { HasResult: false } && previous != _activeRow) previous.SetUnavailable();

        ActivityTitleText.Text = status.State == "Paused"
            ? LocalizationService.Text("Batch in pausa", "Batch paused")
            : LocalizationService.Text("Avanzamento del batch", "Batch progress");
        ShowBatchProgress(_operationRows.Values.Count(row => row.HasResult), status.Total);
    }

    private void ShowBatchProgress(int processed, int total)
    {
        // An interrupted runner can report a total based only on the results it returned.
        total = Math.Max(total, _operationRows.Count);
        var visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        var percent = total > 0 ? Math.Min(processed, total) * 100d / total : 0;
        InstallationProgressBar.Value = percent;
        InstallationProgressBar.Visibility = visibility;
        PercentageText.Text = $"{Math.Floor(percent):0}%";
        PercentageText.Visibility = visibility;
        BatchProgressText.Visibility = visibility;
        BatchProgressText.Text = LocalizationService.Text(
            $"Batch: {processed} di {total} elaborati", $"Batch: {processed} of {total} processed");
    }

    private static (string Id, string Kind) RowKey(string id, string kind) =>
        (id.ToUpperInvariant(), kind.ToUpperInvariant());

    // Presentation only. A runner result can be replaced by a retry before it reaches history.
    private sealed class OperationRow : INotifyPropertyChanged
    {
        private readonly UpdateItem? _proposal;
        public OperationRow(UpdateItem item)
        {
            _proposal = item;
            Name = item.Name;
        }

        public OperationRow(ItemRunResult result)
        {
            Name = result.Name;
            SetResult(result);
        }

        public string Name { get; }
        public string Status { get; private set; } = LocalizationService.Text("In attesa", "Waiting");
        public string ResultDetails { get; private set; } = "";
        public string Diagnostics { get; private set; } = "";
        public bool HasApplicabilityReason { get; private set; }
        public bool HasResult { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Matches(PlanItem target) => _proposal is not null && RunnerPlanItem.Matches(_proposal, target);

        public void SetResult(ItemRunResult result)
        {
            var status = result.Success && result.Verified
                ? LocalizationService.Text("Verificato", "Verified")
                : MainViewModel.GetHistoryResultLabel(result);
            var details = LocalizationService.Translate(result.Message);
            var hasApplicabilityReason = result.TerminalDisposition == ItemTerminalDisposition.NotAutomaticallyApplicable &&
                                         !string.IsNullOrWhiteSpace(details);
            Update(status, details, result.Diagnostics, hasResult: true, hasApplicabilityReason);
        }

        public void SetPhase(bool verifying, string message) => Update(
            verifying ? LocalizationService.Text("In verifica", "Verifying") : LocalizationService.Text("In esecuzione", "Running"),
            LocalizationService.Translate(message), "", hasResult: false);

        public void SetUnavailable() => Update(
            LocalizationService.Text("Stato non disponibile", "Status unavailable"), "", "", hasResult: false);

        private void Update(string status, string details, string diagnostics, bool hasResult, bool hasApplicabilityReason = false)
        {
            HasResult = hasResult;
            if (Status == status && ResultDetails == details && Diagnostics == diagnostics &&
                HasApplicabilityReason == hasApplicabilityReason) return;
            Status = status;
            ResultDetails = details;
            Diagnostics = diagnostics;
            HasApplicabilityReason = hasApplicabilityReason;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
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

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.ToggleUpdatePause();
    }

    private void OperationItemsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        DialogGridScroll.ForwardAtBoundary(OperationItemsGrid, ContentScrollViewer, e);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
