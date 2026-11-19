using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter.ConsentTestHost;

internal enum ConsentScenario { Software, Driver, ChangedFile, Retry, Interactive, Recovery, CancelWaiting }

internal sealed class ConsentAttempt
{
    public int Number { get; set; }
    public DateTime OpenedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedUtc { get; set; }
    public string Decision { get; set; } = "AwaitingConsent";
    public string Presentation { get; set; } = "";
    public bool Silent { get; set; }
    public bool Interactive { get; set; }
    public bool Recovery { get; set; }
    public bool Restore { get; set; }
    public List<PlanItem> Snapshot { get; set; } = [];
}

internal sealed record SimulatedCall(int Attempt, string Operation, PlanItem? Item, bool Silent, bool Interactive);

internal sealed class ScenarioSession
{
    private readonly object _gate = new();
    private readonly IRunnerConsent _dialog;
    private readonly CancellationTokenSource _cancel;
    private readonly RecordingEffects _effects;
    private readonly List<ConsentAttempt> _attempts = [];
    private readonly List<SimulatedCall> _calls = [];
    private readonly List<UpdateRunStatus> _statuses = [];
    private ConsentAttempt? _current;
    private readonly int _nativeAttemptsBefore = NativeBlock.Attempts;
    private readonly ConsentScenario _scenario;
    private readonly bool _silent;
    private bool _failNext;
    private string _planPath = "";
    private UpdatePlan? _input;

    internal ScenarioSession(ConsentScenario scenario, bool silent, IRunnerConsent dialog, CancellationTokenSource cancel)
    {
        _scenario = scenario; _silent = silent; _dialog = dialog; _cancel = cancel;
        DirectoryPath = Path.Combine(AppPaths.DataDirectory, "case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        _effects = new RecordingEffects(this);
    }

    internal string DirectoryPath { get; }
    internal string StatusPath { get; private set; } = "";
    internal string Summary { get; private set; } = "";
    internal IReadOnlyList<SimulatedCall> Calls => _calls;
    internal IReadOnlyList<ConsentAttempt> Attempts => _attempts;
    internal bool MutatedInputAfterRead { get; private set; }

    internal void Run()
    {
        try
        {
            var consent = new CapturingConsent(this);
            if (_scenario == ConsentScenario.Interactive)
            {
                var result = ElevatedUpdateRunner.InstallSoftwareInteractive(SoftwareItem(), consent, _effects, _cancel.Token);
                Summary = result.Outcome;
            }
            else if (_scenario == ConsentScenario.Recovery)
            {
                var preparation = ElevatedUpdateRunner.PrepareRecovery(SoftwareItem(), _silent, consent, _cancel.Token,
                    item => { Record("recovery", ElevatedUpdateRunner.ToPlanItem(item), _silent, false); return new(true, "SIMULATED: no process/service changed"); });
                Summary = preparation.ShouldRetry ? "Recovery simulated" : "Cancelled";
            }
            else
            {
                _failNext = _scenario == ConsentScenario.Retry;
                var first = RunBatch(consent);
                Summary = first.State;
                if (_scenario == ConsentScenario.Retry && first.Results.SingleOrDefault()?.FailureReason == UpdateFailureReasons.FilesInUse)
                {
                    // Exercise the real consent gates, not native process recovery or the process-launching coordinator.
                    var preparation = ElevatedUpdateRunner.PrepareRecovery(SoftwareItem(), _silent, consent, _cancel.Token,
                        item => { Record("recovery", ElevatedUpdateRunner.ToPlanItem(item), _silent, false); return new(true, "SIMULATED recovery"); });
                    Summary = preparation.ShouldRetry ? RunBatch(consent).State : "Cancelled";
                }
            }
            if (NativeBlock.Attempts != _nativeAttemptsBefore)
                throw new InvalidOperationException("A native entry point was attempted and BLOCKED; case is not PASS.");
            Summary += $"; {_attempts.Count} dialog(s); {_calls.Count} simulated call(s); no real installation";
        }
        catch (Exception ex)
        {
            Summary = "ERROR: " + ex.Message;
            throw;
        }
        finally { Save(); }
    }

    private UpdateRunStatus RunBatch(IRunnerConsent consent)
    {
        var id = Guid.NewGuid().ToString("N");
        _planPath = Path.Combine(DirectoryPath, $"update-plan-{id}.json");
        StatusPath = Path.Combine(DirectoryPath, $"update-status-{id}.json");
        _input = new UpdatePlan
        {
            SilentSoftwareInstall = _silent,
            CreateRestorePoint = _scenario == ConsentScenario.Driver,
            StatusFile = StatusPath,
            PauseFile = Path.Combine(DirectoryPath, $"update-pause-{id}.signal"),
            PauseOwnerProcessId = Environment.ProcessId,
            Items = _scenario == ConsentScenario.Driver ? [Driver()] :
                _scenario is ConsentScenario.Retry or ConsentScenario.ChangedFile ? [Software()] : [Software(), Runtime()]
        };
        JsonStorage.WriteAtomic(_planPath, _input);
        ElevatedUpdateRunner.Run(_planPath, _scenario == ConsentScenario.Driver, consent, _effects, _cancel.Token);
        var status = JsonStorage.Read<UpdateRunStatus>(StatusPath) ?? throw new IOException("Runner status missing");
        _statuses.Add(status);
        return status;
    }

    private bool Confirm(RunnerPlanSnapshot snapshot, CancellationToken token)
    {
        var attempt = new ConsentAttempt
        {
            Number = _attempts.Count + 1, Presentation = RunnerConsentPresentation.Describe(snapshot),
            Silent = snapshot.SilentSoftwareInstall, Interactive = snapshot.Interactive,
            Restore = snapshot.CreateRestorePoint, Recovery = snapshot.Recovery,
            Snapshot = snapshot.Items.Select(x => x.ToPlanItem()).ToList()
        };
        lock (_gate) { _current = attempt; _attempts.Add(attempt); Save(); }
        if (_scenario == ConsentScenario.ChangedFile)
        {
            _input!.Items[0].AvailableVersion = "9.0";
            JsonStorage.WriteAtomic(_planPath, _input);
            MutatedInputAfterRead = true;
        }
        if (_scenario == ConsentScenario.CancelWaiting) _cancel.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var approved = _dialog.Confirm(snapshot, token);
            attempt.Decision = approved && !token.IsCancellationRequested ? "Approved" : "RejectedOrCancelled";
            return approved && !token.IsCancellationRequested;
        }
        catch (OperationCanceledException) { attempt.Decision = "Cancelled"; throw; }
        finally { attempt.DecidedUtc = DateTime.UtcNow; Save(); }
    }

    // Independently compare the parameters handed to the provider seam with the captured
    // presentation snapshot. Fail the case instead of merely logging a mismatch.
    internal void Record(string operation, PlanItem? item, bool silent, bool interactive)
    {
        var shown = _current ?? throw new InvalidOperationException("Effect without a presented snapshot");
        if (shown.Decision != "Approved" || _cancel.IsCancellationRequested)
            throw new InvalidOperationException("Effect without current, uncancelled consent");
        if (operation == "restore")
        {
            if (!shown.Restore) throw new InvalidOperationException("Unexpected restore request");
        }
        else
        {
            var expected = shown.Snapshot.Single(x => x.Id == item!.Id && x.Kind == item.Kind);
            if (JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(item))
                throw new InvalidOperationException("Provider target differs from the displayed snapshot");
            if (operation is "software" or "recovery" && (shown.Silent != silent || shown.Interactive != interactive))
                throw new InvalidOperationException("Provider mode differs from the displayed snapshot");
        }
        _calls.Add(new(shown.Number, operation, item is null ? null : new RunnerPlanItem(item).ToPlanItem(), silent, interactive));
        Save();
    }

    internal void Save()
    {
        lock (_gate)
        {
            JsonStorage.WriteAtomic(Path.Combine(DirectoryPath, "evidence.json"), new
            {
                TestOnly = true, Scenario = _scenario.ToString(), DriverPrivilegeSimulated = _scenario == ConsentScenario.Driver,
                MutatedInputAfterRead, NativeBlockedAttempts = NativeBlock.Attempts - _nativeAttemptsBefore,
                Summary, Attempts = _attempts, ProviderCalls = _calls, RunnerStatuses = _statuses
            });
        }
    }

    internal static PlanItem Software() => new() { Id = "Test.Software", Name = "Software dimostrativo / demo",
        Kind = "Software", Source = "test-only", InstalledVersion = "1.0", AvailableVersion = "2.0", PackageOperation = "Upgrade" };
    private static PlanItem Runtime() => new() { Id = "Test.Runtime", Name = "Runtime dimostrativo / demo",
        Kind = "Runtime", Source = "test-only", InstalledVersion = "Assente / absent", AvailableVersion = "2.0", PackageOperation = "Install" };
    private static UpdateItem SoftwareItem() => new RunnerPlanItem(Software()).ToUpdateItem();
    private static PlanItem Driver() => new() { Id = "c42aaae1-cb2a-469d-8be6-ec88f26f485d", Name = "Driver simulato / simulated driver",
        Kind = "Driver", Source = "Windows Update (fixture)", InstalledVersion = "1.0", AvailableVersion = "2.0",
        PackageOperation = "Upgrade", DriverInstallMode = DriverInstallModes.WindowsUpdate,
        WindowsUpdateId = "c42aaae1-cb2a-469d-8be6-ec88f26f485d", WindowsUpdateRevision = 3,
        WindowsUpdateServerSelection = 3, WindowsUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d" };

    private sealed class CapturingConsent(ScenarioSession owner) : IRunnerConsent
    { public bool Confirm(RunnerPlanSnapshot plan, CancellationToken token) => owner.Confirm(plan, token); }

    private sealed class RecordingEffects(ScenarioSession owner) : IRunnerEffects
    {
        // Synthetic privilege only. Does not change the Windows token or verify UAC behavior.
        public bool IsAdministrator => owner._scenario == ConsentScenario.Driver;
        public (bool Created, string Message) CreateRestorePoint()
        { owner.Record("restore", null, false, false); return (false, "SIMULATED: no restore point created"); }
        public ItemRunResult InstallSoftware(PlanItem item, bool silent, bool interactive)
        { owner.Record("software", item, silent, interactive); return Result(item); }
        public ItemRunResult InstallDriver(PlanItem item, Action<int, string> progress)
        { owner.Record("driver", item, false, false); progress(100, "SIMULATED driver"); return Result(item); }
        private ItemRunResult Result(PlanItem item)
        {
            var fail = owner._failNext; owner._failNext = false;
            return new() { Id = item.Id, Name = item.Name, Kind = item.Kind, Success = !fail,
                InstallerSucceeded = false, Verified = false, VerificationStatus = UpdateVerificationStatuses.NotRun,
                Outcome = fail ? UpdateOutcomes.Failed : UpdateOutcomes.Completed,
                FailureReason = fail ? UpdateFailureReasons.FilesInUse : "", Phase = "simulation-only",
                Message = fail ? "SIMULATED FilesInUse" : "SIMULATED: no installer/provider was run" };
        }
    }
}
