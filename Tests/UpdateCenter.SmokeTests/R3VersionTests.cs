using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class R3VersionTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static PlanItem Item(string target) => new()
    {
        Id = "Example.Tool", Name = "Tool", Kind = "Software", Source = "winget", AvailableVersion = target
    };

    private static UpdateVerificationResult Verify(PlanItem item, params WinGetPackageRow[] rows) =>
        WinGetService.VerifyInstallation(item, (_, _) => (new ProcessResult(0, "", ""), rows.ToList()),
            maxAttempts: 1, waitBeforeRetry: _ => { });

    private static WinGetPackageRow Row(string version, string id = "Example.Tool", string source = "winget") =>
        new("Tool", id, version, "", source);

    private static void Inconclusive(UpdateVerificationResult result, string description)
    {
        Check(!result.Verified && !result.IsDefinitive && result.Status == UpdateVerificationStatuses.Unavailable,
            description + " must remain inconclusive.");
    }

    internal static async Task RunAsync()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production data root selected.");
        var failures = new List<string>();
        foreach (var (name, test) in new (string, Func<Task>)[]
        {
            ("Version evidence", () => { VersionEvidence(); return Task.CompletedTask; }),
            ("Query identity and source", () => { QueryEvidence(); return Task.CompletedTask; }),
            ("Installer outcomes", () => { InstallerOutcomes(); return Task.CompletedTask; }),
            ("Legacy and mixed summary", () => { LegacySummary(); return Task.CompletedTask; }),
            ("B2 scoped verification", ScopedVerification),
            ("B2 display mapping", ScopedDisplayVersionMapping),
            ("B2 trailing root mapping", TrailingRootMapping),
            ("B2 unmapped product", UnmappedProduct)
        })
        {
            try { await test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + failures[^1]); }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS R3 offline version evidence and result projection.");
    }

    internal static void RunScope()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production data root selected.");
        const string header = "PackageIdentifier: Example.Tool\nPackageVersion: 2.0\n";
        foreach (var (description, body, expected) in new[]
        {
            ("single override", "Scope: user\nInstallers:\n- Architecture: x64\n  Scope: machine\n", "machine"),
            ("both override", "Scope: machine\nInstallers:\n- Architecture: x64\n  Scope: user\n- Architecture: arm64\n  Scope: user\n", "user"),
            ("mixed inherited and override", "Scope: user\nInstallers:\n- Architecture: x64\n  Scope: machine\n- Architecture: arm64\n", "ambiguous"),
            ("trailing root scope", "Installers:\n- Architecture: x64\n  Scope: user\nScope: machine\n", "ambiguous"),
            ("unknown scope", "Scope: vendor-specific\nInstallers:\n- Architecture: x64\n", "ambiguous")
        })
        {
            var actual = WinGetManifestSafetyService.ParseInstallerScope(header + body);
            Check(actual == expected, $"{description}: expected {expected}, got {actual}.");
            Console.WriteLine("PASS " + description);
        }
        Console.WriteLine("PASS R3 scope inheritance and overrides; no installation or network.");
    }

    private static void VersionEvidence()
    {
        Check(!WinGetService.IsVersionAtLeast("py312_24.5.0-0", "py314_26.9.4-0"), "Miniconda opaque versions falsely ordered.");
        Check(!WinGetService.IsVersionAtLeast("2.0-beta", "2.0-rc"), "Prereleases falsely ordered.");
        foreach (var (installed, target, verified, definitive) in new[]
        {
            ("1.21", "1.21.0", true, true), ("1.20", "1.21", false, true),
            ("1.22", "1.21", true, true),
            ("999999999999999999999999999999.0", "999999999999999999999999999998", true, true),
            ("000999999999999999999999999999999.0", "999999999999999999999999999999", true, true),
            ("py312_24.5.0-0", "py314_26.9.4-0", false, false),
            ("2.0-beta", "2.0-rc", false, false), ("2.0+build1", "2.0+build2", false, false),
            ("2.0-beta", "2.0-beta", true, true), ("2.0-BETA", "2.0-beta", false, false),
            ("Unknown", "Unknown", false, false), ("", "", false, false),
            ("< 2.0", "< 2.0", false, false), ("> 2.0", "> 2.0", false, false)
        })
        {
            var result = Verify(Item(target), Row(installed));
            Check(result.Verified == verified && result.IsDefinitive == definitive,
                $"{installed} vs {target}: verified={result.Verified}, definitive={result.IsDefinitive}.");
        }
    }

    private static void QueryEvidence()
    {
        var item = Item("2.0");
        Inconclusive(Verify(item), "Zero rows");
        Inconclusive(Verify(item, Row("2.0"), Row("2.0")), "Multiple rows");
        Inconclusive(Verify(item, Row("2.0", "Example.Other")), "Other identity");
        Inconclusive(Verify(item, Row("2.0", source: "other")), "Other source");
        Inconclusive(Verify(item, Row("2.0", source: "")), "Missing source");
    }

    private static void InstallerOutcomes()
    {
        var item = Item("2.0-rc");
        UpdateVerificationResult Evidence(PlanItem _) => Verify(item, Row("2.0-beta"));
        var inconclusive = Evidence(item);
        Inconclusive(inconclusive, "Different prereleases");
        foreach (var exit in new[] { 0, 1 })
        {
            var result = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(item, new ProcessResult(exit, "", ""), Evidence);
            Check(!result.Verified && (exit == 0 || !result.Success), $"Installer exit {exit} promoted inconclusive evidence.");
        }
    }

    private static void LegacySummary()
    {
        var legacy = new ItemRunResult { Id = "legacy", Kind = "Software", Success = true, Verified = false,
            Outcome = UpdateOutcomes.Completed, ExecutionState = ItemExecutionState.Terminal,
            TerminalDisposition = ItemTerminalDisposition.Succeeded };
        var verified = new ItemRunResult { Id = "verified", Kind = "Software", Success = true, Verified = true,
            Outcome = UpdateOutcomes.Completed, ExecutionState = ItemExecutionState.Terminal,
            TerminalDisposition = ItemTerminalDisposition.Succeeded };
        var summary = UpdateSessionSummary.From([legacy, verified]);
        Check(summary.ProcessedTerminalCount == 2 && summary.SucceededCount == 1,
            "Legacy unverified completion counted as verified success.");
        Check(legacy.TerminalDisposition == ItemTerminalDisposition.Succeeded && !legacy.Verified,
            "Summary mutated the legacy record.");
        var explicitCancellation = new ItemRunResult { Id = "cancelled", Kind = "Software", Success = false,
            ExecutionState = ItemExecutionState.Terminal, TerminalDisposition = ItemTerminalDisposition.Cancelled };
        var cancellationSummary = UpdateSessionSummary.From([explicitCancellation]);
        Check(cancellationSummary.CancelledCount == 1 && cancellationSummary.FailedCount == 0,
            "Explicit legacy cancellation was reclassified when outcome was missing.");
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            Check(!MainViewModel.ShouldRemoveCompletedUpdate(legacy) &&
                MainViewModel.GetHistoryResultLabel(legacy) != (language == "it" ? "Riuscito" : "Succeeded"),
                "Legacy unverified completion presented as verified.");
        }
    }

    private static async Task ScopedVerification()
    {
        var version = "0.4.23+1";
        var local = @"ARP\User\X64\c6dbe996-22a9-5998-b542-7abe33da3b83";
        var manifest = WinGetScopedExecutionTests.Manifest("ElementLabs.LMStudio");
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __) => Task.FromResult(
            args[0] == "--version" ? new ProcessResult(0, "v1.29.290", "") :
            new ProcessResult(0, $"LM Studio [ElementLabs.LMStudio]\nVersion: {version}\nLocal Identifier: {local}\nInstalled Scope: user\nInstalled Architecture: x64\n", ""));
        var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest),
            () => "S-1-5-21-1000", (_, _) => "user", "x64", readDisplayVersion: _ => version);
        var item = new UpdateItem { Id = "ElementLabs.LMStudio", Name = "LM Studio", Source = "winget",
            Kind = UpdateKind.Software, InstalledVersion = version, AvailableVersion = "0.4.28+2" };
        item.WinGetBinding = await guard.PrepareAsync(item, manifest, default);
        Check(item.WinGetBinding is not null, "Scoped fixture was not admitted.");
        var plan = ElevatedUpdateRunner.ToPlanItem(item);
        var different = await guard.VerifyAsync(plan);
        Inconclusive(different, "B2 distinct opaque versions");
        Check(different.Diagnostics.Contains(version) && different.Diagnostics.Contains(plan.AvailableVersion),
            "B2 inconclusive evidence lost the raw installed or target version.");
        version = plan.AvailableVersion;
        Check((await guard.VerifyAsync(plan)).Verified, "B2 same opaque version did not verify.");
        plan.Source = "other";
        Inconclusive(await guard.VerifyAsync(plan), "B2 changed source");
        plan.Source = "winget";
        local += "-changed";
        Inconclusive(await guard.VerifyAsync(plan), "B2 changed identity");

    }

    private static async Task TrailingRootMapping()
    {
        var version = "0.4.23+1";
        var manifest = WinGetScopedExecutionTests.Manifest("ElementLabs.LMStudio") +
            "\nAppsAndFeaturesEntries:\n- DisplayVersion: 9.99.0\n  ProductCode: c6dbe996-22a9-5998-b542-7abe33da3b83\n";
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __) => Task.FromResult(
            args[0] == "--version" ? new ProcessResult(0, "v1.29.290", "") :
            new ProcessResult(0, $"LM Studio [ElementLabs.LMStudio]\nVersion: {version}\nLocal Identifier: ARP\\User\\X64\\c6dbe996-22a9-5998-b542-7abe33da3b83\nInstalled Scope: user\nInstalled Architecture: x64\n", ""));
        var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest),
            () => "S-1-5-21-1000", (_, _) => "user", "x64", readDisplayVersion: _ => version);
        var item = new UpdateItem { Id = "ElementLabs.LMStudio", Name = "LM Studio", Source = "winget",
            Kind = UpdateKind.Software, InstalledVersion = version, AvailableVersion = "0.4.28+2" };
        item.WinGetBinding = await guard.PrepareAsync(item, manifest, default);
        if (item.WinGetBinding is null) return; // Rejecting unsupported ordering is safe.
        var plan = ElevatedUpdateRunner.ToPlanItem(item);
        version = plan.AvailableVersion;
        Inconclusive(await guard.VerifyAsync(plan), "Root mapping after Installers");
    }

    private static async Task UnmappedProduct()
    {
        var version = "0.4.23+1";
        var manifest = WinGetScopedExecutionTests.Manifest("ElementLabs.LMStudio").Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("ProductCode: c6dbe996-22a9-5998-b542-7abe33da3b83\n", "", StringComparison.Ordinal)
            .Replace("  Scope: user\n  InstallerUrl:", "  Scope: user\n  AppsAndFeaturesEntries:\n  - DisplayVersion: 4.28.2\n  InstallerUrl:", StringComparison.Ordinal);
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __) => Task.FromResult(
            args[0] == "--version" ? new ProcessResult(0, "v1.29.290", "") :
            new ProcessResult(0, $"LM Studio [ElementLabs.LMStudio]\nVersion: {version}\nLocal Identifier: ARP\\User\\X64\\c6dbe996-22a9-5998-b542-7abe33da3b83\nInstalled Scope: user\nInstalled Architecture: x64\n", ""));
        var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest),
            () => "S-1-5-21-1000", (_, _) => "user", "x64", readDisplayVersion: _ => "4.28.2");
        var item = new UpdateItem { Id = "ElementLabs.LMStudio", Name = "LM Studio", Source = "winget",
            Kind = UpdateKind.Software, InstalledVersion = version, AvailableVersion = "0.4.28+2" };
        item.WinGetBinding = await guard.PrepareAsync(item, manifest, default);
        if (item.WinGetBinding is null) return;
        var plan = ElevatedUpdateRunner.ToPlanItem(item);
        version = "4.28.2";
        Inconclusive(await guard.VerifyAsync(plan), "Mapping without recipe or entry product identity");
    }

    private static async Task ScopedDisplayVersionMapping()
    {
        const string product = "c6dbe996-22a9-5998-b542-7abe33da3b83";
        foreach (var (entry, observed, raw, shouldVerify) in new (string, string, string?, bool)[]
        {
            ("", "99.0", null, false),
            ($"  AppsAndFeaturesEntries:\n  - DisplayVersion: 4.28.2\n    ProductCode: {product}\n", "0.4.23+1", "4.28.2.0", true),
            ($"  AppsAndFeaturesEntries:\n  - DisplayVersion: 4.28.2\n    ProductCode: {product}\n", "4.28.2", null, false),
            ($"  AppsAndFeaturesEntries:\n  - DisplayVersion: 4.28.2\n    ProductCode: {product}\n", "0.4.23+1", "4.28.3", false),
            ($"  AppsAndFeaturesEntries:\n  - DisplayVersion: 4.28.2\n    ProductCode: {product}\n", "0.4.28+2", "4.28.2", true),
            ($"  AppsAndFeaturesEntries:\n  - DisplayVersion: 4.28.2\n    ProductCode: {product}\n", "0.4.28+2", "4.28.3", false),
            ($"  AppsAndFeaturesEntries:\n  - DisplayVersion: 4.28.2\n    ProductCode: {product}\n", "0.4.28+2", null, false),
            ("  AppsAndFeaturesEntries:\n  - DisplayVersion: 4.28.2\n    ProductCode: other-product\n", "0.4.23+1", "4.28.2", false),
            ($"  AppsAndFeaturesEntries:\n  - DisplayVersion: 4.28.2\n    ProductCode: {product}\n  - DisplayVersion: 4.28.3\n    ProductCode: {product}\n", "0.4.23+1", "4.28.2", false)
        })
        {
            var version = "0.4.23+1";
            var local = @"ARP\User\X64\" + product;
            var manifest = WinGetScopedExecutionTests.Manifest("ElementLabs.LMStudio").Replace("\r\n", "\n", StringComparison.Ordinal).Replace(
                "  Scope: user\n  InstallerUrl:", "  Scope: user\n" + entry + "  InstallerUrl:", StringComparison.Ordinal);
            Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __) => Task.FromResult(
                args[0] == "--version" ? new ProcessResult(0, "v1.29.290", "") :
                new ProcessResult(0, $"LM Studio [ElementLabs.LMStudio]\nVersion: {version}\nLocal Identifier: {local}\nInstalled Scope: user\nInstalled Architecture: x64\n", ""));
            string? requestedId = null;
            var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest),
                () => "S-1-5-21-1000", (_, _) => "user", "x64", readDisplayVersion: id => { requestedId = id; return raw; });
            var item = new UpdateItem { Id = "ElementLabs.LMStudio", Name = "LM Studio", Source = "winget",
                Kind = UpdateKind.Software, InstalledVersion = version, AvailableVersion = "0.4.28+2" };
            item.WinGetBinding = await guard.PrepareAsync(item, manifest, default);
            if (item.WinGetBinding is null)
            {
                Check(!shouldVerify, "Valid scoped display-version mapping was rejected.");
                continue;
            }
            var plan = ElevatedUpdateRunner.ToPlanItem(item);
            version = observed;
            var result = await guard.VerifyAsync(plan);
            if (shouldVerify) Check(result.Verified && result.IsDefinitive && result.VersionRelation == SoftwareVersionRelation.Equal,
                "Matching mapped DisplayVersion was not verified with an equal relation.");
            else Inconclusive(result, "Missing, different, wrong-product or ambiguous display mapping");
            if (requestedId is not null) Check(requestedId == local, "ARP read used a different local identity.");
        }
    }
}
