using UpdateCenter.Models;
using UpdateCenter.Services;
using System.Diagnostics;

internal static class UnifiedConsentTests
{
    internal static async Task RunAsync()
    {
        var plan = RunnerPlanSnapshot.Create(new UpdatePlan { Items = [new PlanItem {
            Id = "Contoso.Tool", Name = "Synthetic tool", Kind = "Software", Source = "winget",
            InstalledVersion = "1.0", AvailableVersion = "2.0", PackageOperation = PackageOperations.Upgrade
        }] }, false);
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            var text = RunnerConsentPresentation.Describe(plan);
            Check(!text.Contains("già confermato") && !text.Contains("already confirmed"),
                "The authoritative summary still describes a duplicate confirmation");
            Check(text.Contains("1.0") && text.Contains("2.0") && text.Contains("winget") && text.Contains("Contoso.Tool"),
                "The authoritative summary omitted the execution target");
        }
        Console.WriteLine("PASS authoritative summary wording IT/EN");
        GateTransitions(plan);
        await RealProcesses();
    }

    private static void GateTransitions(RunnerPlanSnapshot plan)
    {
        var events = new List<string>();
        var gate = new UnifiedConsentGate([plan, plan]);
        Check(gate.Confirm(plans => { Check(plans.Count == 2, "Mixed batch omitted"); events.Add("summary"); return true; },
            () => events.Add("progress"), () => true), "Unchanged plan rejected");
        gate.Consume(0, plan); events.Add("software"); gate.Consume(1, plan); events.Add("driver");
        Check(events.SequenceEqual(new[] { "summary", "progress", "software", "driver" }), "Not one summary then progress before effects");
        ExpectBlocked(() => gate.Consume(0, plan));
        ExpectBlocked(() => gate.Confirm(_ => true, () => throw new Exception("Repeated progress"), () => true));
        var denied = new UnifiedConsentGate([plan]);
        Check(!denied.Confirm(_ => false, () => throw new Exception("Decline started progress"), () => true), "Decline ignored");
        ExpectBlocked(() => denied.Consume(0, plan));
        var stale = new UnifiedConsentGate([plan]); var valid = true;
        ExpectBlocked(() => stale.Confirm(_ => { valid = false; return true; },
            () => throw new Exception("Changed plan opened progress"), () => valid));
        ExpectBlocked(() => stale.Consume(0, plan));
        foreach (var mutate in new Action<UpdatePlan>[] { p => p.Items[0].Id = "Other.Tool",
            p => p.Items[0].InstalledVersion = "0.1", p => p.Items[0].AvailableVersion = "9.0",
            p => p.Items[0].Source = "other-source", p => p.SilentSoftwareInstall = !p.SilentSoftwareInstall,
            p => p.CreateRestorePoint = !p.CreateRestorePoint, p => p.PauseOwnerProcessId++,
            p => p.Items[0].WinGetBinding = Binding() })
        {
            var changed = UnifiedConsentGate.ToPlan(plan); mutate(changed);
            var once = new UnifiedConsentGate([plan]); once.Confirm(_ => true, () => { }, () => true);
            ExpectBlocked(() => once.Consume(0, RunnerPlanSnapshot.Create(changed, false)));
            ExpectBlocked(() => once.Consume(0, plan));
        }
        var scoped = UnifiedConsentGate.ToPlan(plan); scoped.Items[0].WinGetBinding = Binding();
        var original = RunnerPlanSnapshot.Create(scoped, false);
        foreach (var binding in new[] { Binding() with { Scope = "machine" }, Binding() with { AccountSid = "S-1-5-21-2000" },
            Binding() with { LocalId = "Other" }, Binding() with { ManifestSha256 = new string('B', 64) },
            Binding() with { Architecture = "x86" }, Binding() with { InstallerType = "msi" } })
        {
            scoped.Items[0].WinGetBinding = binding;
            var once = new UnifiedConsentGate([original]); once.Confirm(_ => true, () => { }, () => true);
            ExpectBlocked(() => once.Consume(0, RunnerPlanSnapshot.Create(scoped, false)));
        }
        var retry = new UnifiedConsentGate([plan]); ExpectBlocked(() => retry.Consume(0, plan));
        Console.WriteLine("PASS one summary -> progress -> mixed batch; denial, mutation, scope/binding and one-use/retry");
    }

    private static WinGetScopeBinding Binding() => new("user", "ARP\\User\\X64\\Tool", "S-1-5-21-1000",
        "v1.29.290", new string('A', 64), "inno", "x64", "RemovesPreviousVersion");

    private static async Task RealProcesses()
    {
        Console.WriteLine("Isolated data root: " + AppPaths.DataDirectory);
        foreach (var scenario in new[] { "approve", "deny", "mutate-before", "mutate-after", "disconnect", "wrong-owner", "wrong-created", "foreign-client", "mixed", "before-release", "between-batches", "delivery-fault", "cancel-release" })
        {
            var items = new List<UpdateItem> { new() { Id = "Contoso.Tool", Name = "Synthetic tool", Kind = UpdateKind.Software,
                Source = "winget", InstalledVersion = "1.0", AvailableVersion = "2.0", PackageOperation = PackageOperations.Upgrade } };
            var driverId = Guid.NewGuid().ToString();
            if (scenario is "mixed" or "before-release" or "between-batches") items.Add(new() { Id = driverId, Name = "Synthetic driver", Kind = UpdateKind.Driver,
                Source = "Windows Update", InstalledVersion = "1", AvailableVersion = "2", DriverInstallMode = DriverInstallModes.WindowsUpdate,
                WindowsUpdateId = driverId, WindowsUpdateRevision = 1, WindowsUpdateServerSelection = 2 });
            var settings = new AppSettings { CreateRestorePoint = false, SilentSoftwareInstall = true };
            var children = new List<Process>(); var paths = new List<string>();
            Process? Launch(ProcessStartInfo supplied)
            {
                paths.Add(supplied.ArgumentList[1]);
                if (scenario == "mutate-before")
                {
                    var mutable = JsonStorage.Read<UpdatePlan>(paths[^1])!; mutable.Items[0].AvailableVersion = "9.0";
                    JsonStorage.WriteAtomic(paths[^1], mutable);
                }
                var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("--unified-consent-child"); info.ArgumentList.Add(AppPaths.DataDirectory);
                foreach (var arg in supplied.ArgumentList) info.ArgumentList.Add(arg);
                if (scenario == "wrong-owner") info.ArgumentList[6] = "1";
                if (scenario == "wrong-created") info.ArgumentList[7] = "1";
                var child = Process.Start(info)!;
                var observer = Process.GetProcessById(child.Id); _ = observer.SafeHandle;
                children.Add(observer);
                if (scenario == "foreign-client")
                {
                    // Another genuine executable is not the process bound to this grant.
                    var idle = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    idle.ArgumentList.Add("--unified-consent-idle");
                    var expected = Process.Start(idle)!;
                    var expectedObserver = Process.GetProcessById(expected.Id); _ = expectedObserver.SafeHandle;
                    children.Add(expectedObserver); child.Dispose(); return expected;
                }
                return child;
            }
            var pause = new UpdatePauseController();
            var summaries = 0; var progress = 0; var failed = false;
            void MutateDriver()
            {
                var changed = JsonStorage.Read<UpdatePlan>(paths[1])!; changed.Items[0].AvailableVersion = "8.0";
                JsonStorage.WriteAtomic(paths[1], changed);
            }
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                if (scenario == "between-batches")
                {
                    var result = await new UpdateCoordinator().RunAsync(items, settings, pause, status =>
                    {
                        if (status.Results.Count == 1 && status.Results[0].Success) MutateDriver();
                    }, timeout.Token, null, _ => { summaries++; return true; }, () => progress++, Launch);
                    Check(result.State == "Cancelled" && result.Results.Count == 2 && result.Results[0].Success &&
                        result.Results[1].Outcome == UpdateOutcomes.Cancelled && result.Results[1].ReportedTarget!.AvailableVersion == "2",
                        "Revocation of driver batch lost completed software results or adopted changed target");
                    Check(paths.Count(path => File.Exists(path + ".effects")) == 1 && summaries == 1 && progress == 1,
                        "Interrupted mixed run repeated consent or effects");
                    Console.WriteLine("PASS mixed batch revocation preserves completed software and original driver target");
                    continue;
                }
                await using var session = await PreparedUpdateSession.PrepareAsync(items, settings, pause, timeout.Token, Launch);
                Check(!paths.Any(path => File.Exists(path + ".effects")), "Preparation reached effects");
                var confirmed = session.Confirm(plans =>
                {
                    summaries++;
                    Check(plans.Sum(p => p.Items.Count) == items.Count, "Summary lost a prepared target");
                    if (scenario == "mutate-after")
                    {
                        var changed = JsonStorage.Read<UpdatePlan>(paths[0])!; changed.Items[0].AvailableVersion = "8.0";
                        JsonStorage.WriteAtomic(paths[0], changed);
                    }
                    return scenario != "deny";
                }, () => progress++, items, settings);
                if (scenario == "before-release") MutateDriver();
                if (scenario == "cancel-release") timeout.Cancel();
                if (confirmed && scenario != "disconnect")
                    foreach (var admin in new[] { false, true })
                        if (session.Batch(admin) is { } batch)
                        {
                            if (scenario == "delivery-fault")
                                await session.ReleaseAsync(batch, timeout.Token, async (stream, message, ct) =>
                                {
                                    await RunnerConsentChannel.WriteAsync(stream, message, ct);
                                    throw new IOException("Synthetic transport failure after complete delivery");
                                });
                            else await session.ReleaseAsync(batch, timeout.Token);
                            await batch.Process!.WaitForExitAsync(timeout.Token);
                            Check(batch.Process.ExitCode == 0, "Approved simulated runner failed");
                        }
                if (confirmed && scenario != "disconnect")
                    ExpectBlocked(() => session.ReleaseAsync(session.Batch(false)!, timeout.Token).GetAwaiter().GetResult());
                if (confirmed && scenario == "disconnect") await session.DisposeAsync();
            }
            catch (OperationCanceledException) when (scenario == "cancel-release")
            { failed = true; }
            catch (Exception ex) when (scenario is "mutate-before" or "mutate-after" or "wrong-owner" or "wrong-created" or "foreign-client" or "before-release")
            { failed = true; Console.WriteLine("Blocked " + scenario + ": " + ex.GetType().Name); }
            finally
            {
                pause.Cleanup();
                foreach (var child in children)
                {
                    using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await child.WaitForExitAsync(exitTimeout.Token);
                    child.Dispose();
                }
            }
            var expectedEffects = scenario is "approve" or "mixed" or "delivery-fault" ? items.Count : 0;
            Check(paths.Count(path => File.Exists(path + ".effects")) == expectedEffects, "Unexpected effects in " + scenario);
            Check(summaries == (scenario is "mutate-before" or "wrong-owner" or "wrong-created" or "foreign-client" ? 0 : 1), "Extra/missing summary in " + scenario);
            Check(progress == (scenario is "approve" or "mixed" or "disconnect" or "before-release" or "delivery-fault" or "cancel-release" ? 1 : 0), "Unexpected progress in " + scenario);
            Check(failed == (scenario is "mutate-before" or "mutate-after" or "wrong-owner" or "wrong-created" or "foreign-client" or "before-release" or "cancel-release"), "Missing refusal in " + scenario);
            Console.WriteLine("PASS native pipe / separate simulated runner: " + scenario);
        }
    }

    internal static void Child(string[] args)
    {
        var full = Path.GetFullPath(args[1]); var info = new DirectoryInfo(full);
        Check(info.Parent?.FullName == Path.GetFullPath(Path.Combine(Path.GetTempPath(), "UpdateCenter.SmokeTests")) &&
            Guid.TryParseExact(info.Name, "N", out _), "Child requires isolated GUID root");
        AppPaths.InitializeRoot(full);
        var path = args[3]; var admin = args[2] == "--update-runner-admin";
        Check(Path.GetDirectoryName(Path.GetFullPath(path)) == full, "Plan outside isolated data root");
        var consent = new PipeRunnerConsent(args[5], int.Parse(args[6]), long.Parse(args[7]), path, admin);
        Environment.ExitCode = ElevatedUpdateRunner.Run(path, admin, consent, new SimulatedEffects(path), default);
        if (Environment.ExitCode == 0)
            ExpectBlocked(() => consent.Confirm(RunnerPlanSnapshot.Read(path, admin), default));
        if (Environment.ExitCode != 0 && JsonStorage.Read<UpdatePlan>(path) is { } rejected)
            Console.WriteLine("Simulated child refused: " + JsonStorage.Read<UpdateRunStatus>(rejected.StatusFile)?.Message);
    }

    private sealed class SimulatedEffects(string path) : IRunnerEffects
    {
        public bool IsAdministrator => true;
        public (bool Created, string Message) CreateRestorePoint() => throw new Exception("Unexpected restore point");
        public ItemRunResult InstallDriver(PlanItem item, Action<int, string> progress) => Result(item);
        public ItemRunResult InstallSoftware(PlanItem item, bool silent, bool interactive) => Result(item);
        private ItemRunResult Result(PlanItem item)
        {
            Check(item.AvailableVersion is "2.0" or "2", "Mutated target reached effect");
            File.WriteAllText(path + ".effects", item.Id);
            return new() { Id = item.Id, Name = item.Name, Kind = item.Kind, Success = true, InstallerSucceeded = true,
                Verified = true, VerificationStatus = UpdateVerificationStatuses.Verified };
        }
    }

    private static void ExpectBlocked(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid/replayed consent was accepted");
    }

    internal static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
