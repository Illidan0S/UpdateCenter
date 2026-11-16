using System.Windows.Threading;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class RunnerWaitTests
{
    internal static void Run()
    {
        foreach (var interactive in new[] { false, true })
        {
            var effects = 0;
            Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
            {
                Interlocked.Increment(ref effects);
                return Task.FromResult(new ProcessResult(0, "simulated", ""));
            }
            var guard = new WinGetScopeGuard(Execute, async (_, _, token) =>
            {
                await Task.Delay(30, token).ConfigureAwait(false);
                return null;
            }, () => "S-1-5-21-1000", (_, _) => "machine", "x64");
            OnBlockedDispatcher(() =>
            {
                var result = interactive ? WinGetService.RunInteractive(Item(), Execute, guard)
                    : WinGetService.Upgrade(Item(), true, Execute, guard);
                Check(!result.Success && effects == 0, "Unavailable manifest reached installer dispatch.");
            });
            Console.WriteLine($"PASS asynchronous manifest returns on blocked WPF dispatcher; interactive={interactive}; zero effects");
        }
        CancelledPreflightDoesNotWaitForProvider();
        ReadOnlyDeadlineAndLateCompletion();
        RejectedPreflightCannotBecomeSuccess();
        CancelledProcessMustNotStart();
        VerificationTimeoutPreservesInstallerOutcome();
        ProcessDeadlineAndOutput();
        PhaseAndCancellationFence();
        IpcDeadline();
        LateManifestCannotInstall();
        RunnerPublishesPreflightFailure();
        CancelledVerificationCannotReturnSuccess();
        StalledWarningReachesUi();
    }

    private const string Manifest = "PackageIdentifier: Contoso.Tool\nPackageVersion: 2.0\nInstallerType: exe\nScope: machine\nUpgradeBehavior: install\nInstallers:\n- Architecture: x64\n  InstallerUrl: https://example.com/tool.exe\n  InstallerSha256: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n";

    private static void LateManifestCannotInstall()
    {
        foreach (var cancelled in new[] { false, true })
        foreach (var interactive in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var late = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new ManualResetEventSlim();
            var calls = 0;
            Task<ProcessResult> Execute(IReadOnlyList<string> _, CancellationToken __, TimeSpan ___)
            { Interlocked.Increment(ref calls); return Task.FromResult(new ProcessResult(0, "", "")); }
            var guard = new WinGetScopeGuard(Execute, (_, _, _) => { entered.Set(); return late.Task; },
                () => "S-1-5-21-1000", (_, _) => "machine", "x64", TimeSpan.FromMilliseconds(150));
            var running = Task.Run(() => interactive ? WinGetService.RunInteractive(Item(), Execute, guard, cancellation.Token)
                : WinGetService.Upgrade(Item(), true, Execute, guard, cancellation.Token));
            Check(entered.Wait(1000), "Manifest provider never entered.");
            if (cancelled) cancellation.Cancel();
            try { running.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); throw new Exception("Blocked preflight returned success."); }
            catch (OperationCanceledException) when (cancelled) { }
            catch (TimeoutException) when (!cancelled) { Check(running.IsCompleted, "Only the test deadline fired."); }
            late.SetResult(Manifest);
            Thread.Sleep(100); // Allow a deliberately late read-only completion to run.
            Check(calls == 0, "Late manifest completion launched an installer after the caller had returned.");
        }
        // Prove this fixture would have reached the real command boundary on timely completion.
        var timelyCalls = 0;
        Task<ProcessResult> Timely(IReadOnlyList<string> args, CancellationToken __, TimeSpan ___)
        {
            if (args[0] == "--version") return Task.FromResult(new ProcessResult(0, "v1.29.290", ""));
            if (args[0] == "list") return Task.FromResult(new ProcessResult(0,
                "Contoso Tool [Contoso.Tool]\nVersion: 1.0\nLocal Identifier: ARP\\Machine\\X64\\Contoso\nInstalled Scope: machine\nInstalled Architecture: x64\n", ""));
            timelyCalls++; return Task.FromResult(new ProcessResult(0, "", ""));
        }
        var timely = new WinGetScopeGuard(Timely, (_, _, _) => Task.FromResult<string?>(Manifest),
            () => "S-1-5-21-1000", (_, _) => "machine", "x64");
        Check(WinGetService.Upgrade(Item(), true, Timely, timely).Success && timelyCalls == 1, "Fixture did not reach command boundary.");
        Console.WriteLine("PASS real Upgrade/Interactive timeout and cancellation: late valid manifest cannot install; timely control dispatches exactly once");
    }

    private static void RunnerPublishesPreflightFailure()
    {
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(AppPaths.DataDirectory, $"update-plan-{id}.json");
        var statusPath = Path.Combine(AppPaths.DataDirectory, $"update-status-{id}.json");
        JsonStorage.WriteAtomic(path, new UpdatePlan { Items = [Item()], StatusFile = statusPath,
            PauseFile = Path.Combine(AppPaths.DataDirectory, $"update-pause-{id}.signal"), PauseOwnerProcessId = Environment.ProcessId });
        foreach (var cancelled in new[] { false, true })
        {
            var exit = ElevatedUpdateRunner.Run(path, false, new Consent(), new FailedRead(cancelled), default);
            var state = JsonStorage.Read<UpdateRunStatus>(statusPath)!;
            Check(exit != 0 && state.State == (cancelled ? "Cancelled" : "Failed") && !state.Results.Any(x => x.Success),
                "Runner turned pre-effect timeout/cancellation into completion.");
            Check(state.Phase == (cancelled ? "cancelled" : "winget-preflight"), "Runner lost failure phase.");
        }
        Console.WriteLine("PASS runner persists failure/cancellation and real phase; zero installer effects");
    }

    private sealed class Consent : IRunnerConsent
    { public bool Confirm(RunnerPlanSnapshot plan, CancellationToken token) => true; }
    private sealed class FailedRead(bool cancelled) : IRunnerEffects
    {
        public bool IsAdministrator => false;
        public (bool Created, string Message) CreateRestorePoint() => throw new Exception("Unexpected restore point");
        public ItemRunResult InstallDriver(PlanItem item, Action<int, string> progress) => throw new Exception("Unexpected driver");
        public ItemRunResult InstallSoftware(PlanItem item, bool silent, bool interactive) =>
            throw (cancelled ? new OperationCanceledException("simulated preflight cancellation") : new TimeoutException("simulated preflight deadline"));
    }

    private static void CancelledVerificationCannotReturnSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        try
        {
            WinGetService.VerifyInstallation(Item(), (_, _) =>
            {
                cancellation.Cancel();
                return (new ProcessResult(0, "", ""), [new WinGetPackageRow("Contoso Tool", "Contoso.Tool", "2.0", "", "winget")]);
            }, maxAttempts: 1, token: cancellation.Token);
            throw new Exception("Cancellation raced with a successful verification and was lost.");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS cancellation after read cannot return verified success");
    }

    private static void StalledWarningReachesUi()
    {
        var batch = new UpdateRunStatus { Message = "phase", Phase = "winget-installer", State = "Running" };
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            var shown = UpdateCoordinator.BuildAggregateProgress(new(), batch, 0, stalled: true);
            Check(shown.Message.Contains(language == "it" ? "Nessun progresso" : "No recent progress") &&
                shown.LastProgressUtc == batch.LastProgressUtc && batch.Message == "phase",
                "Warning did not reach UI or falsified observed progress.");
        }
        LocalizationService.Initialize("it");
        Console.WriteLine("PASS stall warning projected IT/EN without renewing progress");
    }

    private static void IpcDeadline()
    {
        using var stream = new NeverReadStream();
        try
        {
            RunnerConsentChannel.ReadAsync(stream, default, TimeSpan.FromMilliseconds(80)).GetAwaiter().GetResult();
            throw new Exception("Stalled consent frame returned authorization.");
        }
        catch (TimeoutException) { }
        using var cancel = new CancellationTokenSource(80);
        try
        {
            RunnerConsentChannel.ReadAsync(stream, cancel.Token, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            throw new Exception("Cancelled consent frame returned authorization.");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS IPC stalled frame deadline/cancellation; no authorization returned");
    }

    private sealed class NeverReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(new TaskCompletionSource<int>().Task);
    }

    private static void VerificationTimeoutPreservesInstallerOutcome()
    {
        var result = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(Item(), new(0, "installer finished", ""),
            _ => throw new TimeoutException("simulated read-only verification timeout"));
        Check(!result.Success && result.InstallerSucceeded && !result.Verified && result.Outcome == UpdateOutcomes.Failed &&
            result.Phase == "winget-verification" && result.VerificationStatus == UpdateVerificationStatuses.Unavailable,
            "Verification timeout hid installer outcome or became success.");
        Console.WriteLine("PASS verification timeout stays failed and retains completed installer evidence");
    }

    private static void ProcessDeadlineAndOutput()
    {
        var output = ProcessRunner.RunAsync(Environment.ProcessPath!, ["--runner-output-helper"],
            timeout: TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Check(output.Success && output.StandardOutput.Contains("OUTPUT-END") && output.StandardError.Contains("ERROR-END"),
            "Process exit lost redirected output.");
        try
        {
            ProcessRunner.RunAsync(Environment.ProcessPath!, ["--runner-delay-helper"],
                timeout: TimeSpan.FromMilliseconds(250)).GetAwaiter().GetResult();
            throw new Exception("Helper exceeded process deadline.");
        }
        catch (TimeoutException) { }
        Console.WriteLine("PASS harmless helper deadline and complete stdout/stderr; no installer involved");
    }

    private static void PhaseAndCancellationFence()
    {
        using var cancel = new CancellationTokenSource();
        var phases = new List<string>();
        var calls = 0;
        try
        {
            WinGetService.Upgrade(Item(), true, (_, _, _) => { calls++; return Task.FromResult(new ProcessResult(0, "", "")); },
                token: cancel.Token, phase: phase => { phases.Add(phase); if (phase == "winget-installer") cancel.Cancel(); });
            throw new Exception("Cancellation at dispatch fence was ignored.");
        }
        catch (OperationCanceledException) { }
        Check(phases.SequenceEqual(new[] { "winget-preflight", "winget-installer" }) && calls == 0,
            "Cancellation crossed installer boundary or phase order is wrong.");
        Console.WriteLine("PASS cancellation checked immediately before dispatch; real phases, zero effects");
    }

    private static void RejectedPreflightCannotBecomeSuccess()
    {
        var guard = new WinGetScopeGuard((_, _, _) => throw new Exception("Unexpected command"),
            (_, _, _) => Task.FromResult<string?>(null), () => "S-1-5-21-1000", (_, _) => "machine", "x64");
        var rejected = WinGetService.Upgrade(Item(), true, (_, _, _) => throw new Exception("Unexpected installer"), guard);
        var verifications = 0;
        var result = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(Item(), rejected, _ =>
        {
            verifications++;
            return new() { Verified = true, IsDefinitive = true, Status = UpdateVerificationStatuses.Verified };
        });
        Check(!result.Success && !result.InstallerSucceeded && !result.Verified && verifications == 0,
            "Rejected preflight became success through post-install reconciliation.");
        Console.WriteLine("PASS rejected preflight stays failed, without verification or installation");
    }

    private static void ReadOnlyDeadlineAndLateCompletion()
    {
        var late = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        try
        {
            var pending = RunnerReadOnlyWait.RunAsync(token => { received = token; return late.Task; },
                default, TimeSpan.FromMilliseconds(80), "test-preflight");
            Check(Task.WhenAny(pending, Task.Delay(1000)).GetAwaiter().GetResult() == pending,
                "Read-only deadline left the caller blocked indefinitely.");
            pending.GetAwaiter().GetResult();
            throw new Exception("Read-only deadline did not fail.");
        }
        catch (TimeoutException) { }
        Check(received.IsCancellationRequested, "Deadline did not propagate cancellation to provider.");
        late.SetResult(1);
        Console.WriteLine("PASS bounded read-only deadline cancels provider; late completion cannot dispatch effects");
    }

    private static void CancelledProcessMustNotStart()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try
        {
            ProcessRunner.RunAsync("nonexistent-must-not-be-started.exe", [], cancel.Token).GetAwaiter().GetResult();
            throw new Exception("Pre-cancelled process returned success.");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS pre-cancelled process does not reach native Start");
    }

    private static void CancelledPreflightDoesNotWaitForProvider()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var never = new TaskCompletionSource<string?>();
        var guard = new WinGetScopeGuard((_, _, _) => throw new Exception("Unexpected command"),
            (_, _, _) => never.Task, () => "S-1-5-21-1000", (_, _) => "machine", "x64");
        var pending = guard.ValidateAsync(Item(), cancel.Token);
        Check(Task.WhenAny(pending, Task.Delay(1000)).GetAwaiter().GetResult() == pending,
            "Cancellation did not bound a non-cooperative read-only manifest provider.");
        try { pending.GetAwaiter().GetResult(); throw new Exception("Cancellation was swallowed."); }
        catch (OperationCanceledException) { }
        never.SetResult(null);
        Console.WriteLine("PASS preflight cancellation reaches caller without waiting for provider");
    }

    internal static PlanItem Item() => new()
    {
        Id = "Contoso.Tool", Name = "Contoso Tool", Kind = "Software", Source = "winget",
        InstalledVersion = "1.0", AvailableVersion = "2.0", PackageOperation = PackageOperations.Upgrade
    };

    private static void OnBlockedDispatcher(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); } catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Check(thread.Join(TimeSpan.FromSeconds(3)), "Preflight deadlocked: continuation waits on the blocked WPF dispatcher.");
        if (failure is not null) throw failure;
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
