using UpdateCenter.Models;
using UpdateCenter.Services;
using System.Text.Json;

internal static class DependencyCoverageTests
{
    internal static async Task RunNativeReadOnlyAsync()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production root selected");
        var allowed = new[] { "RockstarGames.Launcher", "Microsoft.VCRedist.2015+.x64", "Microsoft.VCRedist.2015+.x86" };
        async Task<ProcessResult> Read(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
        {
            var idIndex = args.ToList().IndexOf("--id");
            Check(args.SequenceEqual(new[] { "--version" }) || args[0] == "list" && idIndex >= 0 &&
                idIndex + 1 < args.Count && allowed.Contains(args[idIndex + 1]) && args.Contains("--exact") &&
                args.Contains("--source") && args.Contains("winget") && args.Contains("--details"), "Native command outside read-only allowlist");
            var result = await ProcessRunner.RunAsync("winget.exe", args, token, timeout);
            Console.WriteLine(JsonSerializer.Serialize(new { Command = args, result.ExitCode, result.StandardOutput, result.StandardError }));
            return result;
        }
        var details = await Read(["list", "--id", allowed[0], "--exact", "--source", "winget", "--details", "--disable-interactivity", "--nowarn"], default, TimeSpan.FromSeconds(45));
        var identity = details.Success ? WinGetScopeGuard.ParseIdentity(details.StandardOutput, allowed[0]) : null;
        if (identity is null) { Console.WriteLine("REAL SAMPLE: identity unavailable; 0/1 eligibility demonstrated; no installation"); return; }
        var manifest = await WinGetManifestSafetyService.ReadManifestAsync(allowed[0], "1.0.110.3150", default);
        if (manifest is null) { Console.WriteLine("REAL SAMPLE: exact manifest unavailable; eligibility unmeasured; no installation"); return; }
        var item = new UpdateItem { Id = allowed[0], Name = identity.Name, Kind = UpdateKind.Software, Source = "winget",
            InstalledVersion = identity.Version, AvailableVersion = "1.0.110.3150" };
        var safety = new WinGetManifestSafetyService((_, _, _) => Task.FromResult(new WinGetManifestSafetyService.ManifestInspection(
            WinGetManifestSafetyService.ParseUpgradeSafety(manifest), 0, WinGetManifestSafetyService.ParseInstallerScope(manifest), manifest)));
        await safety.ApplyAsync([item], default, Read);
        var preflight = item.CanInstall && item.WinGetBinding is not null &&
            await WinGetScopeGuard.Native(Read).ValidateAsync(ElevatedUpdateRunner.ToPlanItem(item)) is null;
        Console.WriteLine(JsonSerializer.Serialize(new { Measurement = "REAL_READONLY_SINGLE_CANDIDATE", item.Id, item.InstalledVersion,
            item.AvailableVersion, item.CanInstall, item.IsSelected, PreflightPassed = preflight, item.ResultDetails, item.WinGetBinding,
            InstallationsExecuted = 0, TargetVerifiedAfterInstallation = false, ManifestSha256 = WinGetScopeGuard.Digest(manifest) }));
    }

    internal static async Task RunAsync()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production root selected");
        var tests = new Func<Task>[] { SatisfiedDependenciesRecoverReplay, GenericRecipeRecovery, UnmetDependenciesStayBlocked,
            UnsupportedDeclarationsStayClosed, PreflightDriftNeverDispatches, ConsentBindsDependencies,
            CancellationNeverDispatches, UnstableDependenciesStayBlocked, ExactTargetVerification, TrailingDependencyKeysStayClosed,
            LateRawDependencyDriftRejected, LateScopedDependencyDriftRejected, MainCopyDriftDuringDependenciesRejected,
            DependencyFreeRecipePreserved, SelectedDependencyOverride, StableInstallerCategoryOnDependencies };
        var failures = 0;
        foreach (var test in tests)
            try { await test(); Console.WriteLine("PASS dependencies " + test.Method.Name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL dependencies " + test.Method.Name + ": " + ex.Message); }
        Console.WriteLine($"Dependencies: {tests.Length-failures} PASS, {failures} FAIL; simulated processes only");
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    private static async Task SatisfiedDependenciesRecoverReplay()
    {
        foreach (var interactive in new[] { false, true })
        foreach (var version in new[] { "14.51.36247.0", "14.51.36248.0" })
        {
        var fixture = new Fixture { DependencyVersion = version };
        var item = await fixture.Scan();
        Check(item.CanInstall && item.WinGetBinding is not null, "Replay remains blocked with both machine dependencies satisfied");
        var plan = ElevatedUpdateRunner.ToPlanItem(item);
        var result = interactive ? WinGetService.RunInteractive(plan, fixture.Dispatch, fixture.Guard()) :
            WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard());
        Check(result.Success && fixture.Dispatches == 1, "Bound recipe did not dispatch once");
        Check(fixture.Arguments.Contains("--skip-dependencies"), "WinGet could install dependencies outside the plan");
        foreach (var argument in new[] { "--exact", "--id", "RockstarGames.Launcher", "--source", "winget", "--version", "1.0.110.3150", "--installer-type", "exe", "--architecture", "x64" })
            Check(fixture.Arguments.Contains(argument), "Missing approved constraint: " + argument);
        Check(!fixture.Arguments.Contains("--force") && !fixture.Arguments.Contains("--override") && !fixture.Arguments.Contains("--scope"), "Unexpected override or invented scope");
        }
    }

    private static async Task GenericRecipeRecovery()
    {
        var fixture = new Fixture { TargetId = "Fixture.Generic", TargetName = "Generic Tool" };
        fixture.Manifest = fixture.Manifest.Replace("RockstarGames.Launcher", fixture.TargetId).Replace("Rockstar Games Launcher", fixture.TargetName)
            .Replace("Microsoft.VCRedist.2015+", "Fixture.Runtime");
        var item = await fixture.Scan();
        Check(item.CanInstall && item.WinGetBinding is { DependencySnapshot.Length: > 0 }, "Recovery depends on a specific application ID");
    }

    private static async Task UnmetDependenciesStayBlocked()
    {
        foreach (var change in new Action<Fixture>[] {
            f => f.DependencyVersion = "14.51.36246.0", f => f.DependencyVersion = "unknown",
            f => f.DependencyVersion = "14.51.36247-beta", f => f.DependencyRaw = "14.51.36248.0",
            f => f.DependencyMode = "missing", f => f.DependencyMode = "duplicate",
            f => f.DependencyMode = "user", f => f.DependencyMode = "unknown-architecture" })
        {
            var fixture = new Fixture(); change(fixture);
            var item = await fixture.Scan();
            Check(!item.CanInstall && !item.IsSelected && fixture.Dispatches == 0, "Unsatisfied/ambiguous dependency became selectable");
        }
    }

    private static async Task UnsupportedDeclarationsStayClosed()
    {
        var original = new Fixture().Manifest;
        foreach (var manifest in new[] {
            original.Replace("Dependencies:", "Dependencies: {}"),
            original.Replace("  PackageDependencies:", "  WindowsFeatures:\n  - NetFx3\n  PackageDependencies:"),
            original.Replace("  PackageDependencies:", "  ExternalDependencies:\n  - Other\n  PackageDependencies:"),
            original.Replace("    MinimumVersion: 14.51.36247.0", ""),
            original.Replace("Microsoft.VCRedist.2015+.x86", "Microsoft.VCRedist.2015+.x64"),
            original.Replace("- Architecture: x64", "- Architecture: x64\n  Dependencies: {}"),
            original.Replace("  PackageDependencies:", "  PackageDependencies: []"),
            original.Replace("  PackageDependencies:", "  PackageDependencies:\n  - PackageIdentifier: RockstarGames.Launcher\n    MinimumVersion: 1.0"),
            original.Replace("InstallerType: exe", "InstallerType: wix"),
            original.Replace("ProductCode: Rockstar Games Launcher", "ProductCode: DifferentProduct") })
        {
            var fixture = new Fixture { Manifest = manifest };
            Check(!(await fixture.Scan()).CanInstall, "Unsupported dependency/recipe shape admitted");
        }
        // The parser's count boundary protects both the last allowed and later denied set.
        foreach (var count in new[] { 0, 1, 8, 9, 10 })
        {
            var text = "Dependencies:\n  PackageDependencies:\n" + string.Concat(Enumerable.Range(0, count)
                .Select(i => $"  - PackageIdentifier: Fixture.Runtime{i}\n    MinimumVersion: 1.0\n")) + "Installers:\n- Architecture: x64\n";
            Check((WinGetDependencyPolicy.ParseDeclarations(text.Split('\n'), 0) is not null) == (count is >= 1 and <= 8), "Dependency count boundary wrong");
        }
    }

    private static async Task PreflightDriftNeverDispatches()
    {
        foreach (var change in new Action<Fixture>[] {
            f => f.DependencyVersion = "14.51.36248.0", f => f.DependencyRaw = "14.51.36246.0",
            f => f.DependencyMode = "missing", f => f.DependencyMode = "duplicate", f => f.DependencyMode = "user",
            f => f.DependencyMode = "changed-key", f => f.Sid += "9", f => f.Client = "v1.30.0",
            f => f.Manifest = f.Manifest.Replace("14.51.36247.0", "14.52.0.0") })
        foreach (var interactive in new[] { false, true })
        {
            var fixture = new Fixture(); var plan = ElevatedUpdateRunner.ToPlanItem(await fixture.Scan()); change(fixture);
            var result = interactive ? WinGetService.RunInteractive(plan, fixture.Dispatch, fixture.Guard()) :
                WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard());
            Check(result.PreflightRejected && fixture.Dispatches == 0, "Changed dependency/account/client/manifest dispatched");
        }
        var stripped = new Fixture(); var approved = ElevatedUpdateRunner.ToPlanItem(await stripped.Scan());
        approved.WinGetBinding = approved.WinGetBinding! with { DependencySnapshot = "" };
        Check(WinGetService.Upgrade(approved, true, stripped.Dispatch, stripped.Guard()).PreflightRejected && stripped.Dispatches == 0,
            "Stripped proof authorized dependency processing");
        approved.WinGetBinding = null;
        Check(WinGetService.Upgrade(approved, true, stripped.Dispatch, stripped.Guard()).PreflightRejected && stripped.Dispatches == 0,
            "Legacy fallback authorized dependency processing");
    }

    private static async Task ConsentBindsDependencies()
    {
        var fixture = new Fixture(); var plan = ElevatedUpdateRunner.ToPlanItem(await fixture.Scan());
        RunnerPlanSnapshot Snapshot(PlanItem item) => RunnerPlanSnapshot.Create(new UpdatePlan { Items = [item] }, false);
        var snapshot = Snapshot(plan); var gate = new UnifiedConsentGate([snapshot]);
        Check(gate.Confirm(_ => true, () => { }, () => true), "Fixture consent failed");
        var original = plan.WinGetBinding!.DependencySnapshot;
        plan.WinGetBinding = plan.WinGetBinding with { DependencySnapshot = original.Replace("14.51.36247.0", "14.51.36248.0") };
        Check(snapshot.Items[0].ToPlanItem().WinGetBinding!.DependencySnapshot == original, "Consent shares mutable dependency data");
        var rejected = false; try { gate.Consume(0, Snapshot(plan)); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Changed dependencies consumed consent");
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language); var text = RunnerConsentPresentation.Describe(snapshot);
            Check(text.Contains("Microsoft.VCRedist.2015+.x64") && text.Contains("Microsoft.VCRedist.2015+.x86") &&
                text.Contains("14.51.36247.0") && text.Contains(language == "it" ? "non sono autorizzate" : "not authorized"), "Consent omitted dependency limits");
        }
        foreach (var invalid in new[] { "null", "[]", "[{}]", "{}", original + " ", original.Replace("ARP", "OTHER") })
            Check(WinGetDependencyPolicy.Read(invalid) is null, "Invalid or noncanonical evidence accepted");
        var restored = JsonSerializer.Deserialize<PlanItem>(JsonSerializer.Serialize(snapshot.Items[0].ToPlanItem()))!;
        Check(await fixture.Guard().ValidateAsync(restored) is null, "Valid serialized snapshot lost eligibility");
    }

    private static async Task CancellationNeverDispatches()
    {
        var fixture = new Fixture(); var plan = ElevatedUpdateRunner.ToPlanItem(await fixture.Scan());
        using var cts = new CancellationTokenSource();
        fixture.BeforeDependencyRead = () => cts.Cancel();
        var cancelled = false;
        try { WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard(), cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && fixture.Dispatches == 0, "Cancellation during dependency reads dispatched");
    }

    private static async Task UnstableDependenciesStayBlocked()
    {
        var fixture = new Fixture(); var reads = 0;
        fixture.BeforeDependencyRead = () => { if (++reads == 5) fixture.DependencyVersion = "14.51.36248.0"; };
        Check(!(await fixture.Scan()).CanInstall, "Dependency drift between full-set reads admitted");
    }

    private static async Task ExactTargetVerification()
    {
        var fixture = new Fixture(); var plan = ElevatedUpdateRunner.ToPlanItem(await fixture.Scan());
        Check(!(await fixture.Guard().VerifyAsync(plan)).Verified, "Unchanged version was verified");
        fixture.TargetVersion = "1.0.110.3150";
        Check((await fixture.Guard().VerifyAsync(plan)).Verified, "Exact same-copy target was not verified");
        fixture.TargetVersion = "1.0.111.0";
        Check(!(await fixture.Guard().VerifyAsync(plan)).Verified, "Different target was declared verified");
    }

    private static async Task TrailingDependencyKeysStayClosed()
    {
        var original = new Fixture().Manifest;
        var start = original.IndexOf("Dependencies:", StringComparison.Ordinal);
        var end = original.IndexOf("ProductCode:", start, StringComparison.Ordinal);
        var without = original.Remove(start, end - start);
        foreach (var key in new[] { "\"Dependen\\u0063ies\"", "'Dependencies'", "? Dependencies" })
        {
            var fixture = new Fixture { Manifest = without + "\n" + key + ": { PackageDependencies: [{ PackageIdentifier: Fixture.Unapproved, MinimumVersion: 1.0 }] }\n" };
            Check(!(await fixture.Scan()).CanInstall, "A trailing non-plain dependency key escaped the metadata guard");
            Check(WinGetService.Upgrade(ElevatedUpdateRunner.ToPlanItem(fixture.Item()), true, fixture.Dispatch, fixture.Guard()).PreflightRejected &&
                fixture.Dispatches == 0, "Trailing unsupported dependency reached dispatch via legacy preflight");
        }
    }

    private static async Task LateRawDependencyDriftRejected()
    {
        var fixture = new Fixture(); var plan = ElevatedUpdateRunner.ToPlanItem(await fixture.Scan()); var reads = 0;
        fixture.BeforeDependencyRaw = () => { if (++reads == 3) fixture.DependencyRaw = "14.51.36246.0"; };
        Check(WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard()).PreflightRejected && fixture.Dispatches == 0,
            "Raw-only dependency drift during final reread reached installer");
    }

    private static async Task LateScopedDependencyDriftRejected()
    {
        var fixture = new Fixture(); var plan = ElevatedUpdateRunner.ToPlanItem(await fixture.Scan()); var reads = 0;
        fixture.RewriteDependency = (args, details) => ++reads == 6 && args.Contains("--scope") ? details.Replace("ARP\\Machine", "ARP\\User") : details;
        Check(WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard()).PreflightRejected && fixture.Dispatches == 0,
            "Scoped-only dependency drift during final reread reached installer");
    }

    private static async Task MainCopyDriftDuringDependenciesRejected()
    {
        var fixture = new Fixture(); var plan = ElevatedUpdateRunner.ToPlanItem(await fixture.Scan());
        fixture.BeforeDependencyRead = () => fixture.TargetRaw = "1.0.111.0";
        Check(WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard()).PreflightRejected && fixture.Dispatches == 0,
            "Main copy advanced during dependency reads but lower approved target dispatched");
    }

    private static async Task DependencyFreeRecipePreserved()
    {
        var fixture = new Fixture();
        var start = fixture.Manifest.IndexOf("Dependencies:", StringComparison.Ordinal);
        var end = fixture.Manifest.IndexOf("ProductCode:", start, StringComparison.Ordinal);
        fixture.Manifest = fixture.Manifest.Remove(start, end - start);
        var plan = ElevatedUpdateRunner.ToPlanItem(await fixture.Scan());
        Check(WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard()).Success && fixture.Dispatches == 1 &&
            !fixture.Arguments.Contains("--skip-dependencies") && plan.WinGetBinding?.DependencySnapshot == "", "Dependency-free recipe changed");
    }

    private static async Task SelectedDependencyOverride()
    {
        const string local = "\n  Dependencies:\n    PackageDependencies:\n    - PackageIdentifier: Fixture.SelectedRuntime\n      MinimumVersion: 14.51.36247.0";
        var fixture = new Fixture();
        fixture.Manifest = fixture.Manifest.Replace("- Architecture: x64", "- Architecture: x64" + local);
        var item = await fixture.Scan();
        var approved = WinGetDependencyPolicy.Read(item.WinGetBinding?.DependencySnapshot);
        Check(item.CanInstall && approved is [{ Id: "Fixture.SelectedRuntime" }], "Installer dependencies did not replace the root list");
        var plan = ElevatedUpdateRunner.ToPlanItem(item);
        fixture.Manifest = fixture.Manifest.Replace("Fixture.SelectedRuntime", "Fixture.OtherRuntime");
        Check(WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard()).PreflightRejected &&
            fixture.Dispatches == 0, "A changed installer dependency reached dispatch");

        fixture = new Fixture();
        fixture.Manifest = fixture.Manifest.Replace("- Architecture: x64",
            "- Architecture: x64\n  Dependencies:\n    PackageDependencies: []");
        item = await fixture.Scan();
        approved = WinGetDependencyPolicy.Read(item.WinGetBinding?.DependencySnapshot);
        Check(item.CanInstall && approved?.Length == 2, "An empty installer dependency list erased root dependencies");
    }

    private static async Task StableInstallerCategoryOnDependencies()
    {
        var fixture = new Fixture { RewriteDependency = (_, details) => details + "Categoria programma di installazione: exe\n" };
        var item = await fixture.Scan();
        Check(item.CanInstall && item.WinGetBinding is { DependencySnapshot.Length: > 0 },
            "Stable native dependency category invalidated the evidence reread");
        var plan = ElevatedUpdateRunner.ToPlanItem(item);
        var frozen = WinGetDependencyPolicy.Read(plan.WinGetBinding?.DependencySnapshot);
        Check(frozen is [{ InstallerType: "exe" }, { InstallerType: "exe" }], "Consent omitted the observed dependency category");
        fixture.RewriteDependency = (_, details) => details + "Installer Type: msi\n";
        Check(WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard()).PreflightRejected &&
            fixture.Dispatches == 0, "A stable category change after consent reached dispatch");
        var reads = 0;
        fixture.RewriteDependency = (_, details) => details + (++reads <= 4 ? "Installer Type: exe\n" : "Installer Type: msi\n");
        Check(WinGetService.Upgrade(plan, true, fixture.Dispatch, fixture.Guard()).PreflightRejected &&
            fixture.Dispatches == 0, "Dependency category changed during the preflight reread");
    }

    private sealed class Fixture
    {
        internal string Manifest = WinGetScopedExecutionTests.Manifest("RockstarGames.Launcher");
        internal string DependencyVersion = "14.51.36247.0";
        internal string? DependencyRaw;
        internal string? TargetRaw;
        internal string DependencyMode = "";
        internal string TargetId = "RockstarGames.Launcher", TargetName = "Rockstar Games Launcher", TargetVersion = "1.0.109.3031";
        internal string Sid = "S-1-5-21-111-222-333-1001", Client = "v1.29.290";
        internal Action? BeforeDependencyRead;
        internal Action? BeforeDependencyRaw;
        internal Func<IReadOnlyList<string>, string, string>? RewriteDependency;
        internal int Dispatches;
        internal IReadOnlyList<string> Arguments = [];
        internal UpdateItem Item() => new() { Id = TargetId, Name = TargetName,
            Kind = UpdateKind.Software, Source = "winget", InstalledVersion = "1.0.109.3031", AvailableVersion = "1.0.110.3150" };
        internal Task<ProcessResult> Read(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
        {
            token.ThrowIfCancellationRequested();
            if (args[0] == "--version") return Task.FromResult(new ProcessResult(0, Client, ""));
            Check(args[0] == "list", "Unexpected external command");
            var id = args[args.ToList().IndexOf("--id") + 1];
            var target = id == TargetId;
            if (!target) { BeforeDependencyRead?.Invoke(); token.ThrowIfCancellationRequested(); }
            var arch = id.EndsWith("x86", StringComparison.Ordinal) ? "x86" : "x64";
            var details = target
                ? $"{TargetName} [{TargetId}]\nVersion: {TargetVersion}\nLocal Identifier: ARP\\Machine\\X86\\{TargetName}\nInstalled Scope: machine\nInstalled Architecture: x64\n"
                : $"Runtime [{id}]\nVersion: {DependencyVersion}\nLocal Identifier: ARP\\Machine\\{arch.ToUpperInvariant()}\\{id}\nInstalled Scope: machine\nInstalled Architecture: {arch}\n";
            if (!target) details = DependencyMode switch {
                "missing" => "", "duplicate" => details + details,
                "user" => details.Replace("Machine", "User").Replace("machine", "user"),
                "unknown-architecture" => details.Replace($"Installed Architecture: {arch}", "Installed Architecture: neutral"),
                "changed-key" => details.Replace($"\\{id}\n", $"\\ChangedKey\n"), _ => details };
            if (!target && RewriteDependency is not null) details = RewriteDependency(args, details);
            return Task.FromResult(new ProcessResult(0, details, ""));
        }
        internal string Raw(string local)
        {
            if (local.EndsWith(TargetName, StringComparison.Ordinal)) return TargetRaw ?? TargetVersion;
            BeforeDependencyRaw?.Invoke(); return DependencyRaw ?? DependencyVersion;
        }
        internal WinGetScopeGuard Guard() => new(Read, (_, _, _) => Task.FromResult<string?>(Manifest),
            () => Sid, (_, _) => "machine", "x64", isAdministrator: () => false,
            readDisplayVersion: Raw, readCopyEvidence: id => new WinGetCopyEvidence(Raw(id)));
        internal async Task<UpdateItem> Scan()
        {
            var item = Item();
            var safety = new WinGetManifestSafetyService((_, _, _) => Task.FromResult(new WinGetManifestSafetyService.ManifestInspection(
                WinGetManifestSafetyService.ParseUpgradeSafety(Manifest), 0, "", Manifest)), _ => Guard(), (_, _) => "machine", "x64", () => false);
            await safety.ApplyAsync([item], default, Read);
            return item;
        }
        internal Task<ProcessResult> Dispatch(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
        { Dispatches++; Arguments = args.ToArray(); return Task.FromResult(new ProcessResult(0, "simulated", "")); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
