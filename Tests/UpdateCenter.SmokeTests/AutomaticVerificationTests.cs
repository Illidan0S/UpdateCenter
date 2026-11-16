using System.Reflection;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class AutomaticVerificationTests
{
    internal static async Task RunAsync()
    {
        if (AppPaths.DataDirectory == AppPaths.ResolveDefaultRoot()) throw new Exception("Isolated test data required");
        UnboundPendingHistory();
        Console.WriteLine("PASS UnboundPendingHistory");
        foreach (var test in new Func<Task>[] { DetailedIdentityAndMappedVersion, RejectWrongEvidence,
            ConsentAndPreflightDrift, BoundedRegistrationRetries, DeferredStartupAndRestart, InstallerFailureIsNotRewritten,
            MicrosoftManifestReplay })
        {
            await test(); Console.WriteLine("PASS " + test.Method.Name);
        }
    }

    private static void UnboundPendingHistory()
    {
        var vm = new MainViewModel(null!);
        var plan = new PlanItem { Id = "Example.Bundle", Name = "Bundle", Kind = "Software", Source = "winget",
            InstalledVersion = "1.0", AvailableVersion = "2.0", WinGetClientVersion = "v1.30.300" };
        var result = new ItemRunResult { Id = plan.Id, Name = plan.Name, Kind = plan.Kind,
            InstallerSucceeded = true, VerificationStatus = UpdateVerificationStatuses.Unavailable, ReportedTarget = plan };
        typeof(MainViewModel).GetMethod("SaveRunToHistory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [Array.Empty<UpdateItem>(), new UpdateRunStatus { Results = [result] }]);
        Check(vm.History[0].PendingVerificationPlan?.AvailableVersion == "2.0",
            "Unbound approved pending target was discarded instead of persisted for automatic verification.");
        plan.AvailableVersion = "9.0";
        Check(vm.History[0].PendingVerificationPlan?.AvailableVersion == "2.0", "Pending target was mutable.");
    }

    private static async Task DetailedIdentityAndMappedVersion()
    {
        var f = new Fixture();
        var plan = await f.Prepare();
        Check(plan.WinGetVerification?.TargetDisplayVersion == "8.4.2826.52802", "Raw version mapping lost.");
        f.InstallCompleted = true;
        var result = await f.Guard.VerifyAsync(plan);
        Check(result.Verified && result.IsDefinitive && result.Status == UpdateVerificationStatuses.Verified &&
            result.Diagnostics.Contains("raw=8.4.2826.52802") && f.Dispatches == 0,
            "Detailed source/unique copy/mapped registry evidence failed or dispatched an installer.");
        foreach (var previous in new[] { "8.4.2826.52802", "9.0.0.0" })
            Check(!WinGetScopeGuard.ValidVerificationBinding(plan.WinGetVerification! with { OldDisplayVersion = previous }),
                "Equal/lower raw target was accepted despite catalog version growth.");
    }

    private static async Task InstallerFailureIsNotRewritten()
    {
        var f = new Fixture(); var plan = await f.Prepare(); f.FailInstaller = true;
        var result = WinGetService.Upgrade(plan, true, f.Execute, f.Guard);
        Check(result.ExitCode == unchecked((int)0x8A150006) && !result.Success && f.Dispatches == 1,
            "Installer error was rewritten as success using a source/id/version-only table.");
    }

    private static async Task MicrosoftManifestReplay()
    {
        // Exact observed target manifests, with simulated pre-installation registry/command boundaries.
        foreach (var (id, old, target, arch) in new[]
        {
            ("Microsoft.DotNet.SDK.8", "8.0.424", "8.0.428", "x64"),
            ("Microsoft.VCRedist.2015+.x64", "14.51.36247.0", "14.52.37029.0", "x64"),
            ("Microsoft.VCRedist.2015+.x86", "14.51.36247.0", "14.52.37029.0", "x86")
        })
        {
            var assembly = typeof(AutomaticVerificationTests).Assembly;
            var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith(id + ".yaml", StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            var manifest = reader.ReadToEnd();
            var recipe = WinGetInstallerMetadata.Parse(manifest)!.Installers.Single(x => x.Architecture == arch);
            var f = new Fixture { Id = id, OldVersion = old, Version = target, Arch = arch, Manifest = manifest,
                OldRaw = id == "Microsoft.DotNet.SDK.8" ? "8.4.2424.0" : old,
                Raw = recipe.DisplayVersion.Length > 0 ? recipe.DisplayVersion : target,
                CurrentLocal = @"ARP\Machine\X86\" + recipe.ProductCode };
            var plan = await f.Prepare();
            Check(await f.Guard.ValidateAsync(plan) is null, "New verification proof changed valid preflight: " + id);
            f.InstallCompleted = true;
            Check((await f.Guard.VerifyAsync(plan)).Verified && f.Dispatches == 0, "Exact Microsoft manifest replay failed: " + id);
        }
    }

    private static async Task RejectWrongEvidence()
    {
        foreach (var mutate in new Action<Fixture>[]
        {
            f => f.Source = "", f => f.Source = "other", f => f.Arch = "x86", f => f.Scope = "user",
            f => f.CurrentLocal = @"ARP\Machine\X86\{33333333-3333-3333-3333-333333333333}",
            f => f.Raw = "8.4.2825.0", f => f.Version = "8.0.427", f => f.Version = "unknown",
            f => f.OldExists = true, f => f.OldExists = null, f => f.Ambiguous = true,
            f => f.Sid = "S-1-5-21-9999", f => f.Client = "v1.29.290", f => f.Client = "v1.30.301",
            f => f.ReadFailure = new InvalidOperationException("Provider unavailable"),
            f => f.ReadFailure = new TimeoutException("Read timeout")
        })
        {
            var f = new Fixture(); var plan = await f.Prepare(); f.InstallCompleted = true; mutate(f);
            var result = await f.Guard.VerifyAsync(plan);
            Check(!result.Verified && !result.IsDefinitive && result.Status == UpdateVerificationStatuses.Unavailable &&
                result.Message.Length > 0 && f.Dispatches == 0, "Wrong/ambiguous evidence produced a definitive outcome.");
        }
        var missing = WinGetService.VerifyInstallation(new PlanItem { Id = "Example.Bundle", Source = "winget", AvailableVersion = "2.0" });
        Check(!missing.Verified && missing.Message.Length > 0, "Legacy history without proof manufactured verification.");
    }

    private static async Task ConsentAndPreflightDrift()
    {
        foreach (var mutation in new Action<Fixture>[]
        {
            f => f.OldRaw = "8.4.9999.0", f => f.Source = "other", f => f.Arch = "x86",
            f => f.Manifest += "\n# changed", f => f.Client = "v1.29.290",
            f => f.Sid = "S-1-5-21-9999"
        })
        {
            var f = new Fixture(); var plan = await f.Prepare();
            var frozen = RunnerPlanSnapshot.Create(new UpdatePlan { Items = [plan] }, false).Items.Single();
            var approved = frozen.ToPlanItem();
            Check(approved.WinGetVerification == plan.WinGetVerification, "Snapshot lost verification evidence.");
            plan.WinGetVerification = plan.WinGetVerification! with { Architecture = "x86" };
            Check(frozen.ToPlanItem().WinGetVerification!.Architecture == "x64", "Snapshot proof mutated.");
            mutation(f);
            var result = WinGetService.Upgrade(approved, true, f.Execute, f.Guard);
            Check(result.PreflightRejected && f.Dispatches == 0, "Evidence drift reached dispatch.");
        }
    }

    private static async Task BoundedRegistrationRetries()
    {
        var f = new Fixture(); var plan = await f.Prepare(); f.InstallCompleted = true;
        f.Raw = "8.4.2825.0";
        f.OnWait = () => f.Raw = "8.4.2826.52802";
        Check((await f.Guard.VerifyAsync(plan)).Verified && f.Waits == 1, "Delayed registration was not retried once then stopped.");
        f.Raw = "8.4.2825.0"; f.OnWait = null; f.Waits = 0;
        Check(!(await f.Guard.VerifyAsync(plan)).Verified && f.Waits == 2, "Retry budget was not bounded to three observations.");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await f.Guard.VerifyAsync(plan, cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        Check(f.Dispatches == 0, "Verification executed an installer.");
    }

    private static async Task DeferredStartupAndRestart()
    {
        var f = new Fixture(); var plan = await f.Prepare();
        var entry = new HistoryEntry { Name = "Bundle", Result = "Completato · da verificare", Details = "Original details",
            Diagnostics = "Original installer log", PendingVerificationPlan = plan };
        var legacy = new HistoryEntry { Name = "Legacy", Result = "Completato · da verificare" };
        f.InstallCompleted = true; f.Raw = "8.4.2825.0";
        Check(await DeferredWinGetVerification.ReconcileAsync([entry, legacy], f.Guard.VerifyAsync, default) &&
            entry.PendingVerificationPlan is not null && entry.LastVerificationMessage.Length > 0 &&
            entry.Diagnostics == "Original installer log", "Pending evidence lost or history overwritten.");
        // JSON round-trip models app closure/reboot: frozen target survives, no installer replay.
        var restored = System.Text.Json.JsonSerializer.Deserialize<HistoryEntry>(System.Text.Json.JsonSerializer.Serialize(entry))!;
        f.Raw = "8.4.2826.52802";
        Check(await DeferredWinGetVerification.ReconcileAsync([restored, legacy], f.Guard.VerifyAsync, default) &&
            restored.PendingVerificationPlan is null && restored.VerifiedLaterUtc is not null &&
            restored.Diagnostics.Contains("Original installer log") && restored.Diagnostics.Contains("Deferred verification") &&
            legacy.VerifiedLaterUtc is null && f.Dispatches == 0, "Restart reconciliation lost target/evidence or fabricated a legacy success.");
        Check(!await DeferredWinGetVerification.ReconcileAsync([restored], f.Guard.VerifyAsync, default), "Verified history was reprocessed.");
        var withoutProof = new HistoryEntry { Name = "No frozen identity", PendingVerificationPlan = new PlanItem
        {
            Id = "Example.Old", Name = "Old", Kind = "Software", Source = "winget", InstalledVersion = "1.0",
            AvailableVersion = "2.0", WinGetClientVersion = "v1.30.300"
        } };
        JsonStorage.SaveHistory([restored, legacy, withoutProof]);
        var vm = new MainViewModel(null!);
        await vm.ResumePendingVerificationAsync();
        Check(vm.History[0].VerifiedLaterUtc == restored.VerifiedLaterUtc, "Startup overwrote confirmed history.");
        var attempted = JsonStorage.LoadHistory().Single(x => x.Name == withoutProof.Name);
        Check(attempted.LastVerificationAttemptUtc is not null && attempted.PendingVerificationPlan is not null &&
            attempted.LastVerificationMessage.Length > 0 && attempted.VerifiedLaterUtc is null,
            "Startup did not attempt and persist an explicit unresolved result without scanning or installing.");
    }

    private sealed class Fixture
    {
        private const string OldLocal = @"ARP\Machine\X86\{11111111-1111-1111-1111-111111111111}";
        public string CurrentLocal = @"ARP\Machine\X86\{22222222-2222-2222-2222-222222222222}";
        public string Scope = "machine", Arch = "x64", Source = "winget", Sid = "S-1-5-21-1000", Client = "v1.30.300";
        public string Id = "Example.Bundle", OldVersion = "8.0.424";
        public string Raw = "8.4.2826.52802", OldRaw = "8.4.2424.0", Version = "8.0.428";
        public bool InstallCompleted, Ambiguous, FailInstaller;
        public bool? OldExists = false;
        public int Dispatches, Waits;
        public Action? OnWait;
        public Exception? ReadFailure;
        public string Manifest = "PackageIdentifier: Example.Bundle\nPackageVersion: 8.0.428\nInstallerType: burn\nScope: machine\nUpgradeBehavior: install\nInstallers:\n- Architecture: x64\n  InstallerUrl: https://example.com/bundle.exe\n  InstallerSha256: " + new string('A', 64) + "\n  ProductCode: '{22222222-2222-2222-2222-222222222222}'\n  AppsAndFeaturesEntries:\n  - DisplayVersion: 8.4.2826.52802\n    ProductCode: '{22222222-2222-2222-2222-222222222222}'\n";
        public WinGetScopeGuard Guard => new(Execute, (_, _, _) => Task.FromResult<string?>(Manifest), () => Sid,
            (_, _) => "machine", "x64", isAdministrator: () => false,
            readDisplayVersion: local => local == OldLocal ? OldRaw : Raw,
            registrationExists: local => !InstallCompleted ? local == OldLocal : local == OldLocal ? OldExists : true,
            waitBeforeRetry: (_, ct) => { ct.ThrowIfCancellationRequested(); Waits++; OnWait?.Invoke(); return Task.CompletedTask; });
        public Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken ct, TimeSpan timeout)
        {
            ct.ThrowIfCancellationRequested();
            if (ReadFailure is not null) throw ReadFailure;
            if (args[0] == "--version") return Task.FromResult(new ProcessResult(0, Client, ""));
            if (args[0] != "list")
            {
                Dispatches++;
                if (FailInstaller) return Task.FromResult(new ProcessResult(unchecked((int)0x8A150006), "", "Simulated installer failure"));
                throw new Exception("Unexpected dispatch in read-only fixture");
            }
            if (!args.Contains("--details"))
                return Task.FromResult(new ProcessResult(0, "Name            Id                    Version     Source\n----------------------------------------------------------\nBundle          Example.Bundle        8.0.428     winget\n", ""));
            var text = $"Bundle [{Id}]\nVersion: {(InstallCompleted ? Version : OldVersion)}\nLocal Identifier: {(InstallCompleted ? CurrentLocal : OldLocal)}\nInstalled Scope: {Scope}\nInstalled Architecture: {Arch}\nInstaller Type: exe\nSource: {Source}\n";
            if (Ambiguous) text += text;
            return Task.FromResult(new ProcessResult(0, text, ""));
        }
        public async Task<PlanItem> Prepare()
        {
            var item = new UpdateItem { Id = Id, Name = "Bundle", Kind = UpdateKind.Software,
                Source = "winget", InstalledVersion = OldVersion, AvailableVersion = Version, WinGetClientVersion = Client };
            item.WinGetVerification = await Guard.PrepareVerificationAsync(item, Manifest, default);
            Check(item.WinGetVerification is not null, "Fixture could not freeze verification target.");
            return ElevatedUpdateRunner.ToPlanItem(item);
        }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
