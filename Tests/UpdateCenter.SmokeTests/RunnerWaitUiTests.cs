using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Windows.Threading;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class RunnerWaitUiTests
{
    internal static void Run()
    {
        Exception? error = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) throw error;
    }

    private static void Verify()
    {
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, AutomaticScanInterval = "Off", CheckAppUpdatesAutomatically = false });
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "App.xaml"))) source = source.Parent;
        var root = System.Xml.Linq.XDocument.Load(Path.Combine(source!.FullName, "App.xaml")).Root!;
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new System.Xml.Linq.XElement(ns + "ResourceDictionary",
            root.Attributes().Where(x => x.IsNamespaceDeclaration), root.Element(ns + "Application.Resources")!.Nodes()).ToString());
        var output = Path.Combine(AppContext.BaseDirectory, "qa-progress");
        Directory.CreateDirectory(output);
        foreach (var language in new[] { "it", "en" })
        foreach (var theme in new[] { "Chiaro", "Scuro" })
        {
            LocalizationService.Initialize(language); ThemeService.Apply(theme); TypographyService.Apply("Media");
            SimplifiedCompletionSummary(output, language, theme);
            LiveRowsAndDeferredVerification(output, language, theme);
            NotApplicableSummaryAndDetails(output, language, theme);
            var window = new UpdateProgressWindow(new MainViewModel(null!));
            PlaceOffscreen(window, 720, 520);
            try
            {
                window.Show(); window.UpdateLayout();
                Check(!((ProgressBar)window.FindName("InstallationProgressBar")).IsVisible,
                    "A batch without a known size shows progress.");
                window.ShowCompleted(new UpdateRunStatus { State = "Completed", Total = 1,
                    Message = language == "it" ? "Timeout nella verifica del manifest. Nessun installer avviato." : "Manifest verification timed out. No installer started.",
                    Results = [new() { Id = "Contoso.Tool", Name = "Contoso Tool", Kind = "Software",
                        Outcome = UpdateOutcomes.Failed, Phase = "winget-preflight", ExecutionState = ItemExecutionState.Terminal,
                        TerminalDisposition = ItemTerminalDisposition.Failed }] });
                window.UpdateLayout();
                CheckBatchProgress(window, 100, "1", "1");
                var title = ((TextBlock)window.FindName("StateTitleText")).Text;
                RunnerWaitTests.Check(title == (language == "it" ? "Operazione completata con problemi" : "Operation completed with issues"),
                    "Failed operation has a success title: " + title);
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"failed-preflight-{language}-{theme}.png")); png.Save(file);
            }
            finally { window.ShowFailure("QA", "QA"); window.Close(); }
        }
        app.Shutdown();
        Console.WriteLine("PASS LiveRowsAndDeferredVerification: 0/2, 1/2 in verification, pause, 2/2 with pending verification, mixed outcomes, partial cancellation and interruption");
        Console.WriteLine("PASS CheckBatchProgress: WPF track and fill, IT/EN, light/dark, 720x500, 720x520 and 480x320");
        Console.WriteLine("PASS NotApplicableSummaryAndDetails: separate count, actual reasons and empty details, IT/EN light/dark at 720x500");
        Console.WriteLine("PASS SimplifiedCompletionSummary: verified, deferred, failed and empty batches without zero counters or user verification requests");
    }

    private static void SimplifiedCompletionSummary(string output, string language, string theme)
    {
        var window = new UpdateProgressWindow(new MainViewModel(null!));
        PlaceOffscreen(window, 720, 500);
        try
        {
            window.Show();
            var pending = Enumerable.Range(1, 3).Select(index => new ItemRunResult
            {
                Id = "Fixture." + index, Name = "Package " + index, Kind = "Software", Success = true,
                InstallerSucceeded = true, ExecutionState = ItemExecutionState.Terminal,
                TerminalDisposition = ItemTerminalDisposition.PendingVerification,
                VerificationStatus = UpdateVerificationStatuses.Unavailable
            }).ToList();
            window.ShowCompleted(new UpdateRunStatus { State = "Completed", Total = 3, Results = pending });
            window.UpdateLayout();
            Check(((TextBlock)window.FindName("StateTitleText")).Text == LocalizationService.Text(
                "Installazioni terminate", "Installations finished"),
                "Deferred completion must describe automatic follow-up without requesting user verification.");
            Check(((FrameworkElement)window.FindName("ResultCards")).Visibility == Visibility.Collapsed,
                "Three pending results displayed zero verified/failed cards.");
            Check(!((TextBox)window.FindName("StateDetailText")).Text.Contains("0 "), "Zero result categories remain in the summary.");
            CheckBatchProgress(window, 100, "3", "3");
            Save(window, output, $"summary-pending-{language}-{theme}.png");

            foreach (var item in pending)
            {
                item.Verified = true; item.VerificationStatus = UpdateVerificationStatuses.Verified;
                item.TerminalDisposition = ItemTerminalDisposition.Succeeded;
            }
            window.ShowCompleted(new UpdateRunStatus { State = "Completed", Total = 3, Results = pending });
            window.UpdateLayout();
            Check(!((Border)window.FindName("VerifiedResultCard")).IsVisible,
                "Completion repeats the verified count in a redundant numeric card.");
            Check(((TextBlock)window.FindName("StateTitleText")).Text == LocalizationService.Text(
                "Aggiornamenti completati", "Updates completed") &&
                ((TextBox)window.FindName("StateDetailText")).Text == LocalizationService.Text(
                "3 aggiornamenti installati e verificati", "3 updates installed and verified"), "Fully verified completion lost its actual count.");
            Check(((Border)window.FindName("FailedResultCard")).Visibility == Visibility.Collapsed &&
                  !((Border)window.FindName("VerifiedResultCard")).IsVisible &&
                  ((TextBox)window.FindName("StateDetailText")).IsVisible,
                "Fully verified completion includes empty categories or redundant details.");
            Save(window, output, $"summary-verified-{language}-{theme}.png");
            pending[0].InstallerSucceeded = false;
            window.ShowCompleted(new UpdateRunStatus { State = "Completed", Total = 3, Results = pending });
            Check(((TextBox)window.FindName("StateDetailText")).Text == LocalizationService.Text(
                "3 aggiornamenti completati e verificati", "3 updates completed and verified"),
                "Independent verification was incorrectly presented as a successful installer.");
            pending[0].InstallerSucceeded = true;

            pending[0].Verified = pending[0].Success = false;
            pending[0].TerminalDisposition = ItemTerminalDisposition.Failed;
            pending[0].Outcome = UpdateOutcomes.Failed;
            window.ShowCompleted(new UpdateRunStatus { State = "Completed", Total = 3, Results = pending });
            Check(((TextBlock)window.FindName("StateTitleText")).Text == LocalizationService.Text(
                "Operazione completata con problemi", "Operation completed with issues") &&
                ((TextBox)window.FindName("StateDetailText")).Text == LocalizationService.Text(
                "1 non riuscito · 2 verificati", "1 failed · 2 verified"), "Failure count must be prominent.");
            pending[0].Success = pending[0].RestartRequired = true;
            pending[0].Outcome = UpdateOutcomes.Completed;
            pending[0].VerificationStatus = UpdateVerificationStatuses.PendingRestart;
            pending[0].TerminalDisposition = ItemTerminalDisposition.PendingRestart;
            window.ShowCompleted(new UpdateRunStatus { State = "Completed", Total = 1, Results = [pending[0]] });
            Check(((TextBlock)window.FindName("StateTitleText")).Text == LocalizationService.Text(
                "Installazioni terminate", "Installations finished") &&
                ((TextBox)window.FindName("StateDetailText")).Text == LocalizationService.Text(
                "1 in attesa di riavvio", "1 awaiting restart"),
                "Restart-dependent verification must remain distinct from a generic pending check.");
            pending[0].Success = pending[0].RestartRequired = false;
            pending[0].TerminalDisposition = ItemTerminalDisposition.Cancelled;
            pending[0].Outcome = UpdateOutcomes.Cancelled;
            window.ShowCompleted(new UpdateRunStatus { State = "Cancelled", Total = 1, Results = [pending[0]] });
            Check(((TextBlock)window.FindName("StateTitleText")).Text == LocalizationService.Text(
                "Operazione annullata", "Operation cancelled"), "Cancellation was described as a failure or completion.");
            pending[0].TerminalDisposition = ItemTerminalDisposition.NotAutomaticallyApplicable;
            pending[0].Outcome = UpdateOutcomes.NotApplicable;
            window.ShowCompleted(new UpdateRunStatus { State = "Completed", Total = 1, Results = [pending[0]] });
            Check(((TextBlock)window.FindName("StateTitleText")).Text == LocalizationService.Text(
                "Operazione completata con problemi", "Operation completed with issues") &&
                ((TextBox)window.FindName("StateDetailText")).Text == LocalizationService.Text(
                "1 non applicabile automaticamente", "1 not automatically applicable") &&
                ((Border)window.FindName("FailedResultCard")).Visibility == Visibility.Collapsed,
                "Non-applicable completion must not be presented as an installer error.");
            window.ShowCompleted(new UpdateRunStatus { State = "Completed", Total = 0 });
            Check(((TextBlock)window.FindName("StateTitleText")).Text == LocalizationService.Text(
                "Nessun aggiornamento eseguito", "No updates performed") &&
                  ((FrameworkElement)window.FindName("ResultCards")).Visibility == Visibility.Collapsed,
                "Empty completion displays counters or claims success.");
        }
        finally { window.ShowFailure("QA", "QA"); window.Close(); }
    }

    private static void NotApplicableSummaryAndDetails(string output, string language, string theme)
    {
        const string reason = "The approved target changed before installation.";
        const string diagnostics = "Fixture diagnostic for the changed target.";
        var applicable = Terminal("Verified", ItemTerminalDisposition.Succeeded);
        applicable.Success = applicable.Verified = true;
        var failed = Terminal("Failed", ItemTerminalDisposition.Failed, UpdateOutcomes.Failed);
        var excluded = Terminal("Excluded", ItemTerminalDisposition.NotAutomaticallyApplicable, UpdateOutcomes.ManualRequired);
        excluded.Phase = "winget-preflight";
        excluded.Message = reason;
        excluded.Diagnostics = diagnostics;
        var excludedWithoutReason = Terminal("ExcludedWithoutReason", ItemTerminalDisposition.NotAutomaticallyApplicable, UpdateOutcomes.NotApplicable);
        var deferred = Terminal("Deferred", ItemTerminalDisposition.PendingVerification);
        deferred.Success = true;
        var restart = Terminal("Restart", ItemTerminalDisposition.PendingRestart);
        restart.Success = restart.RestartRequired = true;
        var cancelled = Terminal("Cancelled", ItemTerminalDisposition.Cancelled, UpdateOutcomes.Cancelled);
        var window = new UpdateProgressWindow(new MainViewModel(null!));
        PlaceOffscreen(window, 720, 500);
        try
        {
            window.Show();
            window.ShowCompleted(new UpdateRunStatus
            {
                State = "Completed", Total = 7,
                Results = [applicable, failed, excluded, excludedWithoutReason, deferred, restart, cancelled]
            });
            window.UpdateLayout();
            Check(window.FindName("ResultCards") is FrameworkElement { Visibility: Visibility.Collapsed },
                "NotApplicableSummaryAndDetails: redundant numeric cards remain visible.");
            Check(((TextBlock)window.FindName("NotApplicableCountLabel")).Text ==
                  LocalizationService.Text("NON APPLICABILI", "NOT APPLICABLE") &&
                  ((TextBlock)window.FindName("NotApplicableCountText")).Text == "2" &&
                  ((TextBlock)window.FindName("FailedCountText")).Text == "1" &&
                  ((TextBlock)window.FindName("SucceededCountText")).Text == "1",
                "Not applicable rows must have their own localized count and must not change verified or failed counts.");
            Check(((TextBox)window.FindName("StateDetailText")).Text == LocalizationService.Text(
                "1 non riuscito · 2 non applicabili automaticamente · 1 verifica automatica al prossimo avvio · 1 in attesa di riavvio · 1 annullato · 1 verificato",
                "1 failed · 2 not automatically applicable · 1 automatic check on next launch · 1 awaiting restart · 1 cancelled · 1 verified"),
                "The concise final summary and nonzero cards must preserve all six dispositions.");
            Save(window, output, $"not-applicable-summary-{language}-{theme}.png");

            var grid = (DataGrid)window.FindName("OperationItemsGrid");
            var details = (Expander)window.FindName("OperationItemDetailsExpander");
            var reasonLabel = (TextBlock)window.FindName("ApplicabilityReasonLabel");
            var resultDetails = (TextBox)window.FindName("OperationItemResultDetails");
            var technicalDetails = (TextBox)window.FindName("OperationItemDiagnostics");
            details.IsExpanded = true;
            grid.SelectedIndex = 2;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Check(reasonLabel.IsVisible && reasonLabel.Text == LocalizationService.Text("Motivazione", "Reason") &&
                  resultDetails.Text == reason && technicalDetails.Text == diagnostics,
                "The selected excluded row must show the actual reason and keep its technical details.");
            reasonLabel.BringIntoView();
            window.UpdateLayout();
            Save(window, output, $"not-applicable-details-{language}-{theme}.png");

            grid.SelectedIndex = 3;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Check(!reasonLabel.IsVisible && string.IsNullOrEmpty(resultDetails.Text),
                "A row without a recorded reason must not inherit or invent one.");

            window.ShowCompleted(new UpdateRunStatus { State = "Completed", Total = 2, Results = [applicable, failed] });
            window.UpdateLayout();
            Check(((Border)window.FindName("NotApplicableResultCard")).Visibility == Visibility.Collapsed,
                "A zero Not applicable count must not occupy a summary card.");
        }
        finally { window.ShowFailure("QA", "QA"); window.Close(); }

        static ItemRunResult Terminal(string name, ItemTerminalDisposition disposition, string outcome = UpdateOutcomes.Completed) => new()
        {
            Id = "Fixture." + name, Name = name, Kind = "Software", ExecutionState = ItemExecutionState.Terminal,
            TerminalDisposition = disposition, Outcome = outcome
        };
    }

    private static void LiveRowsAndDeferredVerification(string output, string language, string theme)
    {
        var vm = new MainViewModel(null!);
        var items = new[] { Item("Fixture.First", "First package"), Item("Fixture.Second", "Second package") };
        var targets = items.Select(ElevatedUpdateRunner.ToPlanItem).ToList();
        using var cleanup = new PauseFixture(vm);
        var window = new UpdateProgressWindow(vm, items);
        PlaceOffscreen(window, 720, 500);
        try
        {
            window.Show();
            var grid = (DataGrid)window.FindName("OperationItemsGrid");
            var bar = (ProgressBar)window.FindName("InstallationProgressBar");
            var phase = new UpdateRunStatus { Total = 2, State = "Running", CurrentItemId = items[0].Id,
                CurrentName = items[0].Name, Phase = "winget-installer", CurrentItemStartedUtc = DateTime.UtcNow,
                Message = LocalizationService.Text("Installazione tramite WinGet…", "Installing through WinGet…"),
                ReceivedItems = targets };
            var aggregate = new UpdateRunStatus { Total = 2 };
            void Report() => vm.ApplyInstallationProgress(items, UpdateCoordinator.BuildAggregateProgress(aggregate, phase, 0));
            string Cell(int index)
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                return (grid.Columns[1].GetCellContent(grid.Items[index]) as TextBlock)?.Text ?? "<missing cell>";
            }
            var waiting = LocalizationService.Text("In attesa", "Waiting");
            var running = LocalizationService.Text("In esecuzione", "Running");
            var verifying = LocalizationService.Text("In verifica", "Verifying");
            var verified = LocalizationService.Text("Verificato", "Verified");
            Check(Cell(0) == waiting && Cell(1) == waiting, "New rows should wait for the runner.");
            CheckBatchProgress(window, 0, "0", "2");
            Report();
            Check(Cell(0) == running && Cell(1) == waiting, "The first running phase did not reach the DataGrid.");
            CheckBatchProgress(window, 0, "0", "2");
            phase.Phase = "winget-verification"; Report();
            Check(Cell(0) == verifying && Cell(1) == waiting, "Verification phase was lost or promoted to verified.");

            phase.Results.Add(new ItemRunResult { Id = items[0].Id, Name = items[0].Name, Kind = "Software", Success = true,
                InstallerSucceeded = true, Verified = true, VerificationStatus = UpdateVerificationStatuses.Verified,
                ExecutionState = ItemExecutionState.Terminal, TerminalDisposition = ItemTerminalDisposition.Succeeded, ReportedTarget = targets[0] });
            phase.CurrentIndex = 1; phase.CurrentItemId = items[1].Id; phase.CurrentName = items[1].Name;
            phase.Phase = "winget-installer"; Report();
            Check(Cell(0) == verified && Cell(1) == running, "The first result and second running phase were not shown together.");
            var viewport = (ScrollViewer)window.FindName("ContentScrollViewer");
            var secondStatus = (FrameworkElement)grid.Columns[1].GetCellContent(grid.Items[1]);
            Check(secondStatus.TransformToAncestor(viewport).Transform(new Point(0, secondStatus.ActualHeight)).Y <=
                viewport.ActualHeight - viewport.Padding.Bottom + 1, "The second item state is clipped at 720x500.");
            Check(aggregate.Results.Count == 0 && items[0].Status == waiting, "Presentation mutated the terminal ledger or proposal.");
            CheckBatchProgress(window, 50, "1", "2");
            phase.Phase = "winget-verification";
            phase.Message = LocalizationService.Text("Verifica della versione installata", "Verifying the installed version");
            phase.CurrentItemProgress = 72; Report();
            Check(Cell(1) == verifying && ((TextBox)window.FindName("StateDetailText")).Text == phase.Message,
                "The active verification phase must remain visible alongside batch progress.");
            CheckBatchProgress(window, 50, "1", "2");
            phase.CurrentItemProgress = 0;
            Save(window, output, $"live-{language}-{theme}.png");
            window.Width = 480; window.Height = 320;
            CheckBatchProgress(window, 50, "1", "2");
            Save(window, output, $"live-min-{language}-{theme}.png");
            window.Width = 720; window.Height = 500;

            var pause = (Button)window.FindName("PauseButton");
            Check(((TextBlock)window.FindName("PauseHintText")).Visibility == Visibility.Collapsed, "Idle pause caption repeats the button.");
            Check((string)pause.Content == LocalizationService.Text("Pausa dopo questo aggiornamento", "Pause after this update"), "PauseAtItemBoundary: ambiguous pause control.");
            vm.ToggleUpdatePause(); Report();
            Check(Cell(1) == verifying && vm.IsUpdatePauseRequested && !vm.IsUpdatePaused &&
                vm.UpdatePauseHint == LocalizationService.Text("Pausa richiesta dopo questo aggiornamento.", "Pause requested after this update."),
                "PauseAtItemBoundary: a request must not suspend the current installer.");

            var pending = new ItemRunResult { Id = items[1].Id, Name = items[1].Name, Kind = "Software", Success = true,
                InstallerSucceeded = true, VerificationStatus = UpdateVerificationStatuses.Unavailable,
                ExecutionState = ItemExecutionState.Terminal, TerminalDisposition = ItemTerminalDisposition.PendingVerification, ReportedTarget = targets[1] };
            phase.CurrentItemId = ""; phase.CurrentItemStartedUtc = null; phase.CurrentIndex = 2;
            phase.State = "Paused"; Report();
            CheckBatchProgress(window, 50, "1", "2");
            phase.State = "Cancelled";
            aggregate.Total = 1;
            Report();
            CheckBatchProgress(window, 50, "1", "2");
            aggregate.Total = 2;
            phase.State = "Paused";
            phase.Results.Add(pending); Report();
            Check(Cell(1) == MainViewModel.GetHistoryResultLabel(pending) && vm.IsUpdatePaused &&
                (string)pause.Content == LocalizationService.Text("Riprendi", "Resume") && !bar.IsIndeterminate,
                "PauseAtItemBoundary: batch pause does not leave an installer active.");
            vm.ToggleUpdatePause(); Report();
            Check(vm.IsUpdatePaused && !vm.IsUpdatePauseRequested && !pause.IsEnabled,
                "PauseAtItemBoundary: stale paused snapshot allowed a new pause before resuming.");
            phase.State = "Completed"; Report();
            Check(Cell(1) == MainViewModel.GetHistoryResultLabel(pending), "Unavailable evidence was shown as verified or waiting.");
            CheckBatchProgress(window, 100, "2", "2");
            phase.Results.Remove(pending);
            phase.State = "Cancelled";
            phase.Total = 1;
            window.ShowCompleted(phase);
            CheckBatchProgress(window, 50, "1", "2");
            window.ShowFailure("QA", "QA");
            CheckBatchProgress(window, 50, "1", "2");
            phase.State = "Completed";
            phase.Total = 2;
            phase.Message = LocalizationService.Text("Operazione terminata", "Operation ended");
            phase.Results.Add(pending);
            window.ShowCompleted(phase);
            Check(Cell(0) == verified && Cell(1) == MainViewModel.GetHistoryResultLabel(pending) &&
                ((TextBlock)window.FindName("SucceededCountText")).Text == "1" && ((TextBlock)window.FindName("FailedCountText")).Text == "0" &&
                ((TextBox)window.FindName("StateDetailText")).Text.Contains(LocalizationService.Text("1 verifica automatica al prossimo avvio", "1 automatic check on next launch")),
                "FinalCountersMatchRows: deferred verification changed between the grid and final summary.");
            CheckBatchProgress(window, 100, "2", "2");
            Save(window, output, $"pending-{language}-{theme}.png");
            pending.Verified = true;
            pending.VerificationStatus = UpdateVerificationStatuses.Verified;
            pending.TerminalDisposition = ItemTerminalDisposition.Succeeded;
            window.ShowCompleted(phase);
            Check(Cell(0) == verified && Cell(1) == verified &&
                ((TextBlock)window.FindName("SucceededCountText")).Text == "2" &&
                ((TextBox)window.FindName("StateDetailText")).Text == LocalizationService.Text(
                    "2 aggiornamenti installati e verificati", "2 updates installed and verified"),
                "FinalCountersMatchRows: fully verified batch does not agree with its rows and 100% progress.");
            CheckBatchProgress(window, 100, "2", "2");
            pending.Verified = false;
            pending.Success = false;
            pending.Outcome = UpdateOutcomes.Failed;
            pending.TerminalDisposition = ItemTerminalDisposition.Failed;
            window.ShowCompleted(phase);
            CheckBatchProgress(window, 100, "2", "2");
            Check(Cell(0) == verified && Cell(1) == MainViewModel.GetHistoryResultLabel(pending) &&
                ((TextBlock)window.FindName("SucceededCountText")).Text == "1" &&
                ((TextBlock)window.FindName("FailedCountText")).Text == "1", "Mixed outcomes lost their individual statuses.");
        }
        finally { window.ShowFailure("QA", "QA"); window.Close(); }
    }

    private static void CheckBatchProgress(Window window, int expectedPercent, string processed, string total)
    {
        window.UpdateLayout();
        var bar = (ProgressBar)window.FindName("InstallationProgressBar");
        var percentage = (TextBlock)window.FindName("PercentageText");
        var count = (TextBlock)window.FindName("BatchProgressText");
        Check(bar.IsVisible && !bar.IsIndeterminate && bar.Value == expectedPercent &&
            percentage.Text == $"{expectedPercent}%" && count.Text == LocalizationService.Text(
                $"Batch: {processed} di {total} elaborati", $"Batch: {processed} of {total} processed"),
            $"Expected {expectedPercent}% and {processed}/{total} processed; got value {bar.Value}, indeterminate {bar.IsIndeterminate}, '{percentage.Text}', '{count.Text}'.");
        var completed = !((UpdateProgressWindow)window).IsOperationInProgress;
        Check(completed ? !percentage.IsVisible && !count.IsVisible && bar.Height == 3 : percentage.IsVisible && count.IsVisible,
            "Completion progress must be discreet; live progress must retain its labels.");
        if (completed) Check(System.Windows.Automation.AutomationProperties.GetName(bar) == count.Text,
            "Accessible final progress disagrees with the normalized batch count.");
        var track = (FrameworkElement)bar.Template.FindName("PART_Track", bar);
        var fill = (FrameworkElement)bar.Template.FindName("PART_Indicator", bar);
        Check(track.ActualWidth > 0 && Math.Abs(track.ActualWidth - ((FrameworkElement)bar.Parent).ActualWidth) <= 1 &&
            Math.Abs(fill.ActualWidth - track.ActualWidth * expectedPercent / 100) <= 1,
            $"Incorrect WPF track or fill width at {window.ActualWidth}x{window.ActualHeight} and {expectedPercent}%.");
    }

    private static void PlaceOffscreen(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
    }

    private static UpdateItem Item(string id, string name) => new()
    {
        Id = id, Name = name, Kind = UpdateKind.Software, Source = "winget", InstalledVersion = "1.0", AvailableVersion = "2.0",
        CanInstall = true, Status = LocalizationService.Text("In attesa", "Waiting")
    };

    private static void Save(Window window, string output, string name)
    {
        // Advance only the finite WPF presentation animation before taking a screenshot.
        if (window.FindName("CompletionSummary") is Border summary && summary.HasAnimatedProperties)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
            Check(summary.Opacity == 1 && (summary.RenderTransform as TranslateTransform)?.Y is null or 0,
                "Completion entrance animation did not settle.");
        }
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, name))) png.Save(file);
        if (name.StartsWith("summary-", StringComparison.Ordinal) || name.StartsWith("not-applicable-summary-", StringComparison.Ordinal))
        {
            var width = window.Width; var height = window.Height;
            window.Width = 480; window.Height = 320; window.UpdateLayout();
            var close = (Button)window.FindName("CloseButton");
            var closeOrigin = close.TransformToAncestor(window).Transform(new Point());
            Check(close.IsVisible && closeOrigin.Y >= 0 && closeOrigin.Y + close.ActualHeight <= window.ActualHeight,
                "Close action is clipped in compact completion.");
            var title = (TextBlock)window.FindName("StateTitleText");
            Check(title.ActualWidth > 100 && title.ActualHeight >= title.DesiredSize.Height - 1,
                "Completion title is clipped instead of wrapping at minimum size.");
            var compact = new RenderTargetBitmap(480, 320, 96, 96, PixelFormats.Pbgra32); compact.Render(window);
            var compactPng = new PngBitmapEncoder(); compactPng.Frames.Add(BitmapFrame.Create(compact));
            using (var file = File.Create(Path.Combine(output, "compact-" + name))) compactPng.Save(file);
            window.Width = width; window.Height = height; window.UpdateLayout();
            ((ScrollViewer)window.FindName("ContentScrollViewer")).ScrollToTop();
        }
    }

    private static void Check(bool value, string message) => RunnerWaitTests.Check(value, message);

    private sealed class PauseFixture : IDisposable
    {
        private readonly UpdatePauseController _controller = new();

        internal PauseFixture(MainViewModel vm)
        {
            typeof(MainViewModel).GetField("_activePauseController", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, _controller);
            DialogUiFixture.Set(vm, "IsBusy", true);
        }

        public void Dispose() => _controller.Cleanup();
    }
}
