using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class InstallationOutcomeTests
{
    private const int NotApplicable = unchecked((int)0x8A15002B);
    private const int TechnologyMismatch = unchecked((int)0x8A15008E);
    private const string EnglishMismatch = "The install technology of the newer version specified is different from the current version installed. Please uninstall the package and install the newer version.";
    private const string ItalianMismatch = "La tecnologia di installazione della versione più recente specificata è diversa dalla versione corrente installata. Disinstallare il pacchetto e installare la versione più recente.";
    private const string Command = "winget upgrade --id Audacity.Audacity --exact --version 4.0.2.263060742 --source winget --accept-package-agreements --accept-source-agreements --nowarn --disable-interactivity --silent";

    internal static void Run()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production data root selected");
        Console.WriteLine("Isolated data root: " + AppPaths.DataDirectory);
        var failures = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, AutomaticScanInterval = "Off", CheckAppUpdatesAutomatically = false });
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
                foreach (var test in new Action[] { MixedBatch, Diagnostics, Preflight, RetryAndFiltering, CancellationAndVerification })
                {
                    try { test(); Console.WriteLine("PASS " + test.Method.Name); }
                    catch (Exception ex) { failures.Add(test.Method.Name + ": " + ex); Console.WriteLine("FAIL " + failures[^1]); }
                }
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS installation outcomes: simulated WinGet, terminal ledger, WPF summary, item state and persisted history IT/EN; no installs/scans.");
    }

    private static PlanItem Audacity() => new() { Id = "Audacity.Audacity", Name = "Audacity", Kind = "Software", Source = "winget", InstalledVersion = "3.7.8", AvailableVersion = "4.0.2.263060742" };
    private static UpdateItem Proposal(PlanItem item) => new() { Id = item.Id, Name = item.Name, Kind = UpdateKind.Software, Source = item.Source, InstalledVersion = item.InstalledVersion, AvailableVersion = item.AvailableVersion, CanInstall = true };
    private static UpdateVerificationResult Verified() => new() { IsDefinitive = true, Verified = true, Status = UpdateVerificationStatuses.Verified };
    private static ItemRunResult Complete(ItemRunResult result)
    {
        var state = new ItemExecutionStateMachine(); state.BeginInitialAttempt(); return state.Complete(result);
    }
    private static ItemRunResult Evaluate(PlanItem plan, ProcessResult process, Func<PlanItem, UpdateVerificationResult>? verification = null) =>
        Complete(ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan, process, verification ?? (_ => new() { IsDefinitive = true, Status = UpdateVerificationStatuses.Failed })));

    private static void MixedBatch()
    {
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            var old = new HistoryEntry { Timestamp = new DateTime(2025, 1, 1), Name = "Legacy", Result = "Non applicabile", Details = "Saved historical detail", Diagnostics = "legacy raw output" };
            JsonStorage.SaveHistory([old]);
            var plan = Audacity(); var proposal = Proposal(plan);
            var success = Evaluate(new() { Id = "Fixture.Bionic", Name = "Bionic", Kind = "Software" }, new(0, "", ""), _ => Verified());
            var audacity = Evaluate(plan, new(NotApplicable, EnglishMismatch, "", Command, 123, TimeSpan.FromSeconds(1)), _ => throw new Exception("Unexpected verification of applicability rejection"));
            var ledger = new UpdateSessionResultLedger(); ledger.Record(success); ledger.Record(audacity);
            var summary = UpdateSessionSummary.From(ledger.Results);
            Check(summary.ProcessedTerminalCount == 2 && summary.SucceededCount == 1 && summary.FailedCount == 1 && summary.NotAutomaticallyApplicableCount == 0,
                $"Audacity batch miscounted: {summary}");
            Check(audacity.Outcome == "Failed" && !audacity.Success && audacity.FailureReason == "InstallerTechnologyMismatch", "Failed attempt lost its technical cause");
            Check(audacity.ResultCode == NotApplicable && audacity.Diagnostics.Contains(Command) && audacity.Diagnostics.Contains(EnglishMismatch) && audacity.Diagnostics.Contains("0x8A15002B"), "Raw command/output/exit code lost");
            var frozen = JsonSerializer.Serialize(audacity);
            Check(WinGetApplicabilityStore.ExcludeSuppressed([proposal]).Count == 0, "Known version pair not suppressed");
            var nextPlan = Audacity(); nextPlan.AvailableVersion = "4.1"; var next = Proposal(nextPlan);
            Check(WinGetApplicabilityStore.ExcludeSuppressed([next]).Count == 1 && JsonSerializer.Serialize(audacity) == frozen, "Future suppression changed terminal result or suppressed a new version");
            var vm = new MainViewModel(); vm.Updates.Add(proposal);
            vm.ApplyRunResults([proposal], [audacity], isFinal: true);
            var failedLabel = language == "it" ? "Non riuscito" : "Failed";
            Check(proposal.Status == failedLabel && !proposal.CanInstall && !proposal.CanRetry && vm.Updates.Contains(proposal), "Item state disagrees with failed but suppressed attempt");
            var status = new UpdateRunStatus { State = "Completed", Results = [success, audacity] };
            typeof(MainViewModel).GetMethod("SaveRunToHistory", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [new[] { proposal }, status]);
            var history = JsonStorage.LoadHistory(); var saved = history.Single(x => x.Name == "Audacity");
            Check(saved.DisplayResult == failedLabel && saved.Diagnostics == audacity.Diagnostics && saved.FromVersion == "3.7.8" && saved.ToVersion == plan.AvailableVersion, "History disagrees with terminal result");
            Check(history.Single(x => x.Name == "Legacy").Result == old.Result && history.Single(x => x.Name == "Legacy").Details == old.Details, "Existing history was reclassified");
            Check(!saved.Details.Contains(language == "it" ? "nuovo tentativo" : "retry", StringComparison.OrdinalIgnoreCase), "Suppressed failure offers a retry");
            foreach (var theme in new[] { "Chiaro", "Scuro" })
            {
                ThemeService.Apply(theme);
                ShowSummary(vm, status, window =>
                {
                    Check(Text(window, "SucceededCountText") == "1" && Text(window, "FailedCountText") == "1", "WPF counters disagree");
                    var grid = (DataGrid)window.FindName("OperationItemsGrid");
                    Check((string)grid.Items[1].GetType().GetProperty("Status")!.GetValue(grid.Items[1])! == failedLabel, "Operation row disagrees");
                    Capture(window, language + "-" + theme);
                });
            }
            LocalizationService.Initialize(language == "it" ? "en" : "it");
            Check(saved.DisplayDetails.Contains(language == "it" ? "installation technology" : "tecnologia di installazione"), "Persisted reason does not follow IT/EN switch");
        }
    }

    private static void Diagnostics()
    {
        foreach (var (code, output, reason) in new[] {
            (NotApplicable, "", "NotApplicable"), (NotApplicable, "unrecognized locale", "NotApplicable"),
            (NotApplicable, EnglishMismatch, "InstallerTechnologyMismatch"), (NotApplicable, ItalianMismatch, "InstallerTechnologyMismatch"),
            (TechnologyMismatch, "", "InstallerTechnologyMismatch"),
            (unchecked((int)0x8A150114), "", "UpgradeNotSupported"), (1, "Generic WinGet error", ""),
            (1, EnglishMismatch, ""), (NotApplicable, "Install technology metadata unavailable", "NotApplicable") })
        {
            foreach (var stderr in new[] { false, true })
            {
                var result = Evaluate(Audacity(), new(code, stderr ? "" : output, stderr ? output : ""));
                Check(result.Outcome == "Failed" && result.TerminalDisposition == ItemTerminalDisposition.Failed && result.FailureReason == reason, $"Wrong cause/outcome: 0x{code:X8}, {reason}");
                Check(result.ResultCode == code && !result.Message.Contains("questo PC") && !result.Message.Contains("hardware"), "Unsupported compatibility inference");
            }
        }
        Check(WinGetService.ClassifyOutcome(new(NotApplicable, EnglishMismatch, "")) == "NotApplicable", "Generic code was redefined as specific technology error");
        Check(WinGetService.ClassifyFailureReason(new(0, EnglishMismatch, ""), true) == "", "Output overrode successful verification");
    }

    private static void Preflight()
    {
        var result = Evaluate(Audacity(), new ProcessResult(1, "", "Binding changed; rescan required") { PreflightRejected = true }, _ => throw new Exception("Preflight reached verification"));
        var summary = UpdateSessionSummary.From([result]);
        Check(!result.Success && !result.InstallerSucceeded && summary.NotAutomaticallyApplicableCount == 1 && summary.FailedCount == 0, "Preflight exclusion counted as attempted installation failure");
        Check(result.Message.Contains("Binding changed"), "Preflight reason lost");
        var invalid = Audacity(); invalid.AvailableVersion = "Unknown";
        var calls = 0;
        var rejected = WinGetService.Upgrade(invalid, true, (_, _, _) => { calls++; throw new Exception("Invalid target dispatched"); });
        Check(calls == 0 && rejected.PreflightRejected && Evaluate(invalid, rejected).TerminalDisposition == ItemTerminalDisposition.NotAutomaticallyApplicable, "Pre-launch rejection lost its provenance");
    }

    private static void CancellationAndVerification()
    {
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            var cancelled = Complete(new() { Id = "Cancelled", Outcome = "Cancelled" });
            var pending = Evaluate(Audacity(), new(3010, "", ""), _ => new() { Status = "PendingRestart" });
            Check(pending.Success && !pending.Verified && pending.VerificationStatus == "PendingRestart" && !MainViewModel.ShouldRemoveCompletedUpdate(pending), "Pending verification promoted or lost");
            var failedVerification = Evaluate(Audacity(), new(0, "", ""));
            Check(!failedVerification.Success && failedVerification.TerminalDisposition == ItemTerminalDisposition.Failed, "Installer exit zero bypassed evidence");
            var verifiedDespiteExit = Evaluate(Audacity(), new(1, "", ""), _ => Verified());
            Check(verifiedDespiteExit.Success && verifiedDespiteExit.Verified, "Existing definitive verification policy changed");
            ShowSummary(new MainViewModel(), new() { Results = [cancelled, pending] }, window =>
            {
                Check(Text(window, "FailedCountText") == "0" && Text(window, "StateDetailText").Contains(language == "it" ? "1 annullato" : "1 cancelled"), "Cancellation folded into failures");
                Check(Text(window, "StateDetailText").Contains(language == "it" ? "1 in attesa di riavvio" : "1 awaiting restart"), "Unverified completion lost");
            });
        }
    }

    private static void RetryAndFiltering()
    {
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            var plan = Audacity(); var item = Proposal(plan);
            var vm = new MainViewModel(); vm.Updates.Add(item);
            var failed = Evaluate(plan, new(1, "Generic WinGet error", ""));
            vm.ApplyRunResults([item], [failed], isFinal: true);
            vm.UpdateTypeFilter = "Errori";
            Check(item.CanRetry && vm.UpdatesView.Cast<UpdateItem>().Single() == item, "Failed label broke retry or error filter");
            Check(MainViewModel.GetHistoryResultLabel(failed) == item.Status, "Generic failure labels disagree");
        }
    }

    private static void Capture(Window window, string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UpdateCenter.sln"))) directory = directory.Parent;
        var output = Path.Combine(directory!.FullName, "_output", "outcome-consistency-20261110", "screenshots");
        Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); png.Save(file);
    }

    private static void ShowSummary(MainViewModel vm, UpdateRunStatus status, Action<UpdateProgressWindow> verify)
    {
        var window = new UpdateProgressWindow(vm); VisualQaSafety.Attach(window);
        try
        {
            window.ShowCompleted(status);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Check(window.ActualWidth > 0 && window.ActualHeight > 0, "Summary window did not lay out");
            verify(window);
        }
        finally { window.Close(); }
    }
    private static string Text(Window window, string name) => window.FindName(name) switch
    {
        TextBlock block => block.Text,
        TextBox box => box.Text,
        _ => throw new InvalidOperationException("Missing text: " + name)
    };
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
