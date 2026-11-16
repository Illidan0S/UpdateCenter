using System.Reflection;
using System.Runtime.InteropServices;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class InstallationSafetyTests
{
    internal static async Task RunAsync()
    {
        Check(Path.GetFullPath(AppPaths.DataDirectory) != Path.GetFullPath(AppPaths.ResolveDefaultRoot()), "Production root selected");
        Console.WriteLine("Isolated data root: " + AppPaths.DataDirectory);
        var failures = new List<string>();
        foreach (var test in new Action[] { ApprovedTuple, NoSourceOrNameFallback, InvalidTargets,
            WinGetResults, WuaFreshGuard, WuaSearchOutcome, WuaDownloadOutcome, WuaInstallOutcome,
            WuaAbsenceIsNotInstallation, WuaFreshInstalledEvidence, WuaRestart, RunnerPersistence,
            WindowsUpdateSafetyTests.Run, JsonStorageHardeningTests.Run, StatusWatchdogHardeningTests.Run })
        {
            try { test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception ex) { failures.Add(test.Method.Name + ": " + ex.Message); Console.WriteLine("FAIL " + failures[^1]); }
        }
        try { await DeclinedRecoveryAsync(); Console.WriteLine("PASS DeclinedRecovery"); }
        catch (Exception ex) { failures.Add("DeclinedRecovery: " + ex.Message); }
        ObservePlanTrustLimit();
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        WinGetExecutionTests.Run();
        Console.WriteLine("PASS existing WinGet exact-version regressions");
        Console.WriteLine("PASS M5 installation safety: offline delegates only; no installer, UAC or real WUA.");
    }

    private static PlanItem Software(string source = "approved-source") => new()
    {
        Id = "Contoso.Tool", Name = "Contoso Tool", Kind = "Software", Source = source,
        InstalledVersion = "1.0", AvailableVersion = "2.0", PackageOperation = PackageOperations.Upgrade
    };

    private static void ApprovedTuple()
    {
        var item = new UpdateItem
        {
            Id = "Contoso.Tool", Name = "Contoso Tool", Kind = UpdateKind.Software,
            Source = "approved-source", InstalledVersion = "1.0", AvailableVersion = "2.0"
        };
        var path = Path.Combine(AppPaths.DataDirectory, "update-plan-approved.json");
        JsonStorage.WriteAtomic(path, new UpdatePlan { Items = [ElevatedUpdateRunner.ToPlanItem(item)] });
        var plan = JsonStorage.Read<UpdatePlan>(path)!.Items.Single();
        var calls = new List<string[]>();
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
        {
            calls.Add(args.ToArray());
            // Catalog now offers 3.0; only the explicit approved 2.0 is permitted to execute.
            return Task.FromResult(new ProcessResult(1, "Available version changed to 3.0", "Target unavailable"));
        }
        WinGetService.Install(plan, true, Execute);
        WinGetService.Upgrade(plan, false, Execute);
        WinGetService.RunInteractive(plan, Execute);
        Check(calls.Count == 3, "Unavailable target triggered an extra execution");
        foreach (var args in calls)
        {
            Check(Value(args, "--id") == item.Id && Value(args, "--version") == "2.0" &&
                Value(args, "--source") == "approved-source" && args.Contains("--exact"), "Approved execution tuple changed");
            Check(!args.Contains("--ignore-security-hash") && !args.Contains("--force") && !args.Contains("--name"),
                "Execution bypassed integrity or identity guards");
        }
    }

    private static void NoSourceOrNameFallback()
    {
        var calls = new List<string[]>();
        var item = Software("winget");
        Task<ProcessResult> Execute(IReadOnlyList<string> arguments, CancellationToken token, TimeSpan timeout)
        {
            var args = arguments.ToArray(); calls.Add(args);
            if (args[0] == "list") return Task.FromResult(new ProcessResult(0, "", ""));
            return Task.FromResult(Value(args, "--source") == "winget"
                ? new ProcessResult(1, "", "No installed package found matching input criteria")
                : new ProcessResult(0, "Different source with same ID accepted", ""));
        }
        var result = WinGetService.Upgrade(item, true, Execute);
        Check(!result.Success, "A different source satisfied the approved update");
        Check(calls.Count(a => a[0] == "upgrade") == 1 && calls.All(a => Value(a, "--source") == "winget") &&
            calls.All(a => !a.Contains("--name")), "Fallback weakened source/ID binding");
        calls.Clear();
        var current = WinGetService.Upgrade(item, true, (args, _, _) =>
        {
            calls.Add(args.ToArray());
            return Task.FromResult(args[0] == "list"
                ? new ProcessResult(0, "Name          Id            Version  Source\n-------------------------------------------\nContoso Tool  Contoso.Tool  3.0      winget\n", "")
                : new ProcessResult(1, "", "No installed package found matching input criteria"));
        });
        Check(current.Success && calls.Count == 2 && calls.Count(a => a[0] == "upgrade") == 1 &&
            Value(calls[0], "--version") == "2.0" && calls.All(a => Value(a, "--source") == "winget"),
            "Already-installed target reconciliation lost binding or executed a newer version");
    }

    private static void InvalidTargets()
    {
        foreach (var source in new[] { "", "   ", "source with spaces", "--source", "source\nother" })
        {
            var calls = 0;
            Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
            { calls++; return Task.FromResult(new ProcessResult(0, "", "")); }
            var item = Software(source);
            Check(!WinGetService.Install(item, true, Execute).Success && !WinGetService.Upgrade(item, true, Execute).Success &&
                !WinGetService.RunInteractive(item, Execute).Success && calls == 0, "Unverifiable origin executed: " + source);
        }
        var invalid = Software(); invalid.Id = "Contoso.Tool;other";
        var invoked = false;
        var bad = WinGetService.Install(invalid, true, (_, _, _) => { invoked = true; throw new InvalidOperationException(); });
        Check(!bad.Success && !invoked, "Malformed package identity reached executor");
    }

    private static void WinGetResults()
    {
        var item = Software("winget");
        foreach (var (code, status, verified, succeeded, reboot) in new[]
        {
            (0, UpdateVerificationStatuses.Verified, true, true, false),
            (0, UpdateVerificationStatuses.Failed, false, false, false),
            (1, UpdateVerificationStatuses.Unavailable, false, false, false),
            (3010, UpdateVerificationStatuses.PendingRestart, false, true, true),
            (1641, UpdateVerificationStatuses.PendingRestart, false, true, true)
        })
        {
            var result = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(item,
                new ProcessResult(code, "", code == 1 ? "Payload hash/signature validation failed" : ""),
                _ => new() { IsDefinitive = status != UpdateVerificationStatuses.Unavailable, Verified = verified, Status = status });
            Check(result.Success == succeeded && result.Verified == verified && result.RestartRequired == reboot,
                "Installer/verification/reboot reconciliation failed for " + code);
        }
        var wrongId = WinGetService.VerifyInstallation(item, (_, _) => (new ProcessResult(0, "", ""),
            new List<WinGetPackageRow> { new("Other", "Contoso.Other", "2.0", "", "winget") }), maxAttempts: 1);
        Check(!wrongId.Verified, "Installed row from another identity verified the target");
    }

    private static void WuaFreshGuard()
    {
        foreach (var edit in new Action<M5WuaOffer>[]
        {
            x => x.Identity.UpdateID = Guid.NewGuid().ToString(), x => x.Identity.RevisionNumber++,
            x => x.Title = "Display firmware 2.0", x => x.DriverModel = "BIOS",
            x => x.DriverHardwareID = "UEFI\\RES_123", x => x.DriverClass = "System", x => x.Description = null!
        })
        {
            var env = new M5WuaSession(); var plan = env.Plan(); edit(env.Before);
            var result = WindowsUpdateService.InstallDriver(plan, null, env.Create);
            Check(!result.Success && env.Before.EulaCalls == 0 && env.DownloadCalls == 0 && env.InstallCalls == 0,
                "Changed/unverifiable candidate reached authorization");
        }
    }

    private static void WuaSearchOutcome()
    {
        foreach (var code in new[] { 0, 1, 4, 5, 99 })
        {
            var env = new M5WuaSession { SearchCode = code };
            var result = WindowsUpdateService.InstallDriver(env.Plan(), null, env.Create);
            Check(!result.Success && env.Before.EulaCalls == 0 && env.DownloadCalls == 0, "Failed search authorized download: " + code);
        }
    }

    private static void WuaDownloadOutcome()
    {
        foreach (var code in new[] { 0, 1, 3, 4, 5 })
        {
            var env = new M5WuaSession { DownloadCode = 3, DownloadItemCode = code };
            var result = WindowsUpdateService.InstallDriver(env.Plan(), null, env.Create);
            Check(!result.Success && env.InstallCalls == 0, "Partial/failed item download authorized installation: " + code);
        }
        var digest = new M5WuaSession { DownloadHResult = unchecked((int)0x80096010) };
        var failed = WindowsUpdateService.InstallDriver(digest.Plan(), null, digest.Create);
        Check(!failed.Success && digest.InstallCalls == 0, "Integrity failure HRESULT ignored");
    }

    private static void WuaInstallOutcome()
    {
        var env = new M5WuaSession { InstallCode = 3, InstallItemCode = 4, VerificationCode = 4 };
        var result = WindowsUpdateService.InstallDriver(env.Plan(), null, env.Create);
        Check(!result.Success && !result.InstallerSucceeded && !result.Verified, "Aggregate partial success hid failed item install");
        env = new() { InstallHResult = unchecked((int)0x80096010) };
        result = WindowsUpdateService.InstallDriver(env.Plan(), null, env.Create);
        Check(!result.Success && !result.InstallerSucceeded && !result.Verified, "Negative item installation HRESULT ignored");
    }

    private static void WuaAbsenceIsNotInstallation()
    {
        foreach (var code in new[] { 2, 3, 4 })
        {
            var env = new M5WuaSession { InstallCode = 4, InstallItemCode = 4, VerificationCode = code };
            var result = WindowsUpdateService.InstallDriver(env.Plan(), null, env.Create);
            Check(!result.Success && !result.Verified, "Absence from applicability search manufactured installation: " + code);
        }
    }

    private static void WuaFreshInstalledEvidence()
    {
        var env = new M5WuaSession { ReturnFresh = true };
        env.Fresh.IsInstalled = true;
        var result = WindowsUpdateService.InstallDriver(env.Plan(), null, env.Create);
        Check(result.Success && result.Verified && !env.Before.IsInstalled, "Fresh installed target not verified independently of stale object");
        Check(env.Criteria.Count == 2 && env.Criteria[1].Contains("IsInstalled=1", StringComparison.Ordinal) &&
            env.Criteria[1].Contains("RevisionNumber=4", StringComparison.Ordinal) &&
            env.Criteria[1].Contains(env.Before.Identity.UpdateID, StringComparison.OrdinalIgnoreCase), "Postcheck did not bind installed query to target");
        env = new() { ReturnFresh = true, VerificationCode = 4 };
        env.Fresh.IsInstalled = true;
        result = WindowsUpdateService.InstallDriver(env.Plan(), null, env.Create);
        Check(!result.Verified, "Failed search supplied positive verification");
        env = new() { ReturnFresh = true };
        result = WindowsUpdateService.InstallDriver(env.Plan(), null, env.Create);
        Check(!result.Success && !result.Verified, "Fresh not-installed target ignored");
    }

    private static void WuaRestart()
    {
        foreach (var success in new[] { true, false })
        {
            var env = new M5WuaSession { Reboot = true, InstallCode = success ? 2 : 4, InstallItemCode = success ? 2 : 4 };
            var result = WindowsUpdateService.InstallDriver(env.Plan(), null, env.Create);
            Check(result.Success == success && result.RestartRequired && !result.Verified &&
                result.VerificationStatus == UpdateVerificationStatuses.PendingRestart, "Reboot masked installation failure or invented verification");
        }
    }

    private static async Task DeclinedRecoveryAsync()
    {
        var item = new UpdateItem { Id = "Contoso.Tool", Name = "Contoso Tool", Kind = UpdateKind.Software };
        var initial = new ItemRunResult { Id = item.Id, Kind = "Software", FailureReason = UpdateFailureReasons.FilesInUse };
        var calls = 0;
        var result = await WinGetSingleRetryPolicy.ExecuteAsync(item, initial,
            () => new WinGetRecoveryPreparation(false, "Consent declined"),
            () => { calls++; return Task.FromResult(initial); },
            interactiveFallback: () => { calls++; return Task.FromResult(initial); });
        Check(calls == 0 && !result.Success, "Declined consent launched retry/interactive installer");
        item.CanInstall = false;
        var pause = new UpdatePauseController();
        var blocked = false;
        try { await new UpdateCoordinator().RunAsync([item], new AppSettings(), pause, _ => { }, default); }
        catch (InvalidOperationException) { blocked = true; }
        Check(blocked, "Non-installable item passed coordinator boundary");
    }

    private static void ObservePlanTrustLimit()
    {
        var path = Path.Combine(AppPaths.DataDirectory, "update-plan-trust-limit.json");
        var plan = new UpdatePlan { Items = [Software()] };
        JsonStorage.WriteAtomic(path, plan);
        plan.Items[0].AvailableVersion = "9.0";
        JsonStorage.WriteAtomic(path, plan);
        var validate = typeof(ElevatedUpdateRunner).GetMethod("ValidatePlanPath", BindingFlags.NonPublic | BindingFlags.Static)!;
        validate.Invoke(null, [path]);
        var rejectedOutside = false;
        try { validate.Invoke(null, [Path.Combine(AppPaths.DataDirectory, "..", "update-plan-outside.json")]); }
        catch (TargetInvocationException ex) when (ex.InnerException is UnauthorizedAccessException) { rejectedOutside = true; }
        Check(rejectedOutside, "Plan traversal passed runner path validation");
        Check(JsonStorage.Read<UpdatePlan>(path)!.Items[0].AvailableVersion == "9.0", "Plan trust fixture not exercised");
        Console.WriteLine("OBSERVED deferred limit: allowed-path plan replacement is not bound to prior consent; runner not invoked.");
    }

    private static void RunnerPersistence()
    {
        var path = Path.Combine(AppPaths.DataDirectory, "update-status-m5.json");
        var operations = new FakeAtomicFileOperations(1, new IOException("Simulated sharing violation", unchecked((int)0x80070020)));
        JsonStorage.WriteAtomic(path, new UpdateRunStatus { State = "Completed" }, "status-final", operations, _ => { });
        Check(operations.MoveCalls == 2 && JsonStorage.Read<UpdateRunStatus>(path)?.State == "Completed", "Atomic status retry failed");
        var writes = 0;
        using var publisher = new ElevatedUpdateRunner.RunnerStatusPublisher(path, new UpdateRunStatus(),
            TimeSpan.FromHours(1), (_, _, _) => { if (++writes > 1) throw new UnauthorizedAccessException("Simulated write denial"); });
        publisher.ReportProgress(25, "fixture");
        var failed = false;
        try { publisher.Complete(status => status.State = "Completed"); }
        catch (UpdateStatusChannelException ex) when (ex.Stage == "final") { failed = true; }
        Check(failed, "Runner silently completed after status persistence failure");
    }

    private static string? Value(string[] args, string key) => Array.IndexOf(args, key) is var index && index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

public sealed class M5WuaOffer
{
    public FakeUpdateIdentity Identity { get; } = new();
    public string Title { get; set; } = "Contoso Display 2.0";
    public string DriverClass { get; set; } = "Display";
    public string DriverHardwareID { get; set; } = @"PCI\VEN_1234";
    public string DriverModel { get; set; } = "Contoso Video";
    public string Description { get; set; } = "Display update";
    public bool IsInstalled { get; set; }
    public bool EulaAccepted => false;
    public int EulaCalls { get; private set; }
    public void AcceptEula() => EulaCalls++;
}

public sealed class M5WuaSession
{
    public M5WuaOffer Before { get; } = new();
    public M5WuaOffer Fresh { get; } = new();
    public int SearchCode { get; set; } = 2;
    public int VerificationCode { get; set; } = 2;
    public bool ReturnFresh { get; set; }
    public int DownloadCode { get; set; } = 2;
    public int DownloadItemCode { get; set; } = 2;
    public int DownloadHResult { get; set; }
    public int InstallCode { get; set; } = 2;
    public int InstallItemCode { get; set; } = 2;
    public int InstallHResult { get; set; }
    public bool Reboot { get; set; }
    public int DownloadCalls { get; set; }
    public int InstallCalls { get; set; }
    public List<string> Criteria { get; } = [];
    public string ClientApplicationID { get; set; } = "";
    public int ServerSelection { get; set; }
    public string ServiceID { get; set; } = "";
    public bool Online { get; set; }
    public bool IncludePotentiallySupersededUpdates { get; set; }
    public PlanItem Plan() => new()
    {
        Id = Before.Identity.UpdateID, Name = "Contoso Video", Kind = "Driver", AvailableVersion = "2.0",
        WindowsUpdateId = Before.Identity.UpdateID, WindowsUpdateRevision = 4,
        WindowsUpdateServerSelection = 3, WindowsUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d"
    };
    public object Create(string name) => name switch
    {
        "Microsoft.Update.Session" => this, "Microsoft.Update.UpdateColl" => new M5WuaCollection(),
        _ => throw new InvalidOperationException("Unexpected COM creation")
    };
    public M5WuaSession CreateUpdateSearcher() => this;
    public M5WuaSearch Search(string criteria)
    {
        if (!Online || IncludePotentiallySupersededUpdates || ServerSelection != 3 || ServiceID != "7971f918-a847-4430-9279-4a52d1efe18d")
            throw new InvalidOperationException("Approved WUA source/options changed");
        Criteria.Add(criteria);
        return Criteria.Count == 1 ? new(SearchCode, [Before]) : new(VerificationCode, ReturnFresh ? [Fresh] : []);
    }
    public M5WuaExecutor CreateUpdateDownloader() => new(this);
    public M5WuaExecutor CreateUpdateInstaller() => new(this);
}

public sealed class M5WuaCollection
{
    public void Add(M5WuaOffer offer) { }
}
public sealed class M5WuaSearch(int code, M5WuaOffer[] offers)
{
    public int ResultCode => code;
    public M5WuaSearch Updates => this;
    public int Count => offers.Length;
    public M5WuaOffer Item(int index) => offers[index];
}
public sealed class M5WuaExecutor(M5WuaSession env)
{
    public object? Updates { get; set; }
    public M5WuaOperation Download() { env.DownloadCalls++; return new(env.DownloadCode, env.DownloadItemCode, env.DownloadHResult, false); }
    public M5WuaOperation Install() { env.InstallCalls++; return new(env.InstallCode, env.InstallItemCode, env.InstallHResult, env.Reboot); }
}
public sealed class M5WuaOperation(int code, int itemCode, int hresult, bool reboot)
{
    public int ResultCode => code;
    public bool RebootRequired => reboot;
    public M5WuaItemResult GetUpdateResult(int index) => index == 0 ? new(itemCode, hresult, reboot) : throw new InvalidOperationException();
}
public sealed class M5WuaItemResult(int code, int hresult, bool reboot)
{
    public int ResultCode => code;
    public int HResult => hresult;
    public bool RebootRequired => reboot;
}
