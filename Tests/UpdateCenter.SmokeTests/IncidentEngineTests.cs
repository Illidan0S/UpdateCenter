using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class IncidentEngineTests
{
    internal static async Task UnreviewedClientBlocksScanAndDispatch()
    {
        var offers = new[]
        {
            (Id: "Microsoft.VCRedist.2015+.x64", Name: "VC++ x64", Installed: "14.51.36247.0", Target: "14.52.37029.0"),
            (Id: "Microsoft.VCRedist.2015+.x86", Name: "VC++ x86", Installed: "14.51.36247.0", Target: "14.52.37029.0"),
            (Id: "Microsoft.DotNet.DesktopRuntime.8", Name: "Desktop Runtime", Installed: "8.0.30", Target: "8.0.34")
        };
        string Manifest(string id, string target) => $"PackageIdentifier: {id}\nPackageVersion: {target}\n" +
            "InstallerType: exe\nScope: machine\nUpgradeBehavior: install\nInstallers:\n- Architecture: x64\n";
        var client = "v1.30.301";
        var preflightReads = new List<string>();
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __)
        {
            if (args[0] == "--version") return Task.FromResult(new ProcessResult(0, client, ""));
            if (!args.Contains("--id") && args[0] is "upgrade" or "list")
            {
                var rows = $"{"Name",-24}{"Id",-40}{"Version",-18}{"Available",-18}Source\n" + new string('-', 105) + "\n" +
                    string.Join('\n', offers.Select(x => $"{x.Name,-24}{x.Id,-40}{x.Installed,-18}{x.Target,-18}winget"));
                return Task.FromResult(new ProcessResult(0, rows, ""));
            }
            throw new InvalidOperationException("Unexpected scan command: " + string.Join(' ', args));
        }
        WinGetScopeGuard Guard(WinGetCommand read) => new(read, (id, target, _) => Task.FromResult<string?>(Manifest(id, target)),
            () => "S-1-5-21-1000", (_, _) => "machine", "x64", isAdministrator: () => false);
        var metadata = new WinGetManifestSafetyService((id, target, _) => Task.FromResult(
            new WinGetManifestSafetyService.ManifestInspection(WinGetUpgradeSafety.Safe, 0, "machine", Manifest(id, target))),
            createGuard: read => Guard((args, token, timeout) => args[0] == "list"
                ? Task.FromResult(new ProcessResult(0, "", "")) : read(args, token, timeout)),
            readScope: (_, _) => "machine", architecture: "x64", isAdministrator: () => false);
        var scanner = new WinGetService(Execute, metadata);
        var items = await scanner.ScanAsync(false, default);
        Check(items.Count == 3 && items.All(x => !x.CanInstall && !x.IsSelected && x.ResultDetails.Contains(client)),
            "An unreviewed client left false selectable runtime candidates or hid the reason.");
        client = "v1.29.290";
        var previouslySelectable = await scanner.ScanAsync(false, default);
        Check(previouslySelectable.Count == 3 && previouslySelectable.All(x => x.CanInstall),
            "The reviewed client was blocked by the new scan rule.");
        client = "v1.30.300";
        Task<ProcessResult> ReadPreflight(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
        {
            preflightReads.Add(string.Join(' ', args));
            return Execute(args, token, timeout);
        }
        foreach (var item in previouslySelectable)
        {
            var result = ElevatedUpdateRunner.InstallSoftwareWithBoundaries(ElevatedUpdateRunner.ToPlanItem(item), true, false,
                ReadPreflight, Guard(ReadPreflight));
            var execution = new ItemExecutionStateMachine();
            execution.BeginInitialAttempt();
            Check(execution.Complete(result).TerminalDisposition == ItemTerminalDisposition.NotAutomaticallyApplicable &&
                result.Phase == "winget-preflight" && !result.InstallerSucceeded && result.VerificationStatus == UpdateVerificationStatuses.NotRun &&
                result.Message.Contains(client) && result.Message.Contains("v1.29.290"), item.Id + ": client rejection lost its provenance.");
        }
        Check(preflightReads.SequenceEqual(new[] { "--version", "--version", "--version" }), "Client drift reached an identity query or installer.");
        Console.WriteLine("PASS UnreviewedClientBlocksScanAndDispatch: three Microsoft candidates retained, reviewed client selectable, drift blocks before dispatch.");
    }

    internal static async Task RunAsync()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production data root selected.");
        var failures = new List<string>();
        foreach (var (name, test) in new (string, Func<Task>)[]
        {
            ("Certainly lower target never dispatches", () => { LowerTarget(); return Task.CompletedTask; }),
            ("Absent source remains distinct from conflicting source", () => { MissingSource(); return Task.CompletedTask; }),
            ("Mapped ARP version can bind despite provider range", MappedCopy),
            ("Scoped verification retries delayed evidence", DelayedCopy),
            ("Opaque copy can use consistent PE product evidence", OpaqueCopy),
            ("Deferred verification preserves original outcome evidence", DeferredVerification),
            ("Legacy preflight refuses a changed installed copy", () => { LegacyDrift(); return Task.CompletedTask; }),
            ("Deferred verification rotates unresolved entries", DeferredFairness),
            ("History keeps only the runner reported bound target", () => { HistoryTarget(); return Task.CompletedTask; }),
            ("Auxiliary executables cannot supply product evidence", () => { PrimaryExecutable(); return Task.CompletedTask; }),
            ("Consent exposes the executable used for copy verification", () => { CopyConsent(); return Task.CompletedTask; }),
            ("Mapped lower target cannot escape through legacy fallback", MappedDowngrade)
        })
        {
            try { await test(); Console.WriteLine("PASS incident " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL incident " + failures[^1]); }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static void LowerTarget()
    {
        foreach (var interactive in new[] { false, true })
        {
            var effects = 0;
            Task<ProcessResult> Execute(IReadOnlyList<string> _, CancellationToken __, TimeSpan ___)
            { effects++; return Task.FromResult(new ProcessResult(0, "simulated", "")); }
            var item = new PlanItem { Id = "Example.Tool", Name = "Tool", Kind = "Software", Source = "winget",
                InstalledVersion = "10.0", AvailableVersion = "9.0" };
            var result = interactive ? WinGetService.RunInteractive(item, Execute) : WinGetService.Upgrade(item, true, Execute);
            Check(result.PreflightRejected && effects == 0, "A certainly lower approved target reached the installer boundary.");
        }
    }

    private static void MissingSource()
    {
        var item = new PlanItem { Id = "Example.Tool", Name = "Tool", Source = "winget", AvailableVersion = "2.0" };
        UpdateVerificationResult Verify(string source) => WinGetService.VerifyInstallation(item,
            (_, _) => (new ProcessResult(0, "", ""), [new("Tool", item.Id, "2.0", "", source)]), maxAttempts: 1);
        var absent = Verify(""); var conflicting = Verify("other");
        Check(!absent.Verified && !conflicting.Verified, "An unbound row supplied positive source/copy proof.");
        Check(absent.Message != conflicting.Message, "Absent source was reported as a conflicting source.");
        var mixed = WinGetService.VerifyInstallation(item, (_, _) => (new ProcessResult(0, "", ""),
            [new("Tool", item.Id, "2.0", "", ""), new("Tool", item.Id, "2.0", "", "other")]), maxAttempts: 1);
        Check(!mixed.Verified && mixed.Message == conflicting.Message, "An omitted source hid an explicit conflicting source.");
    }

    private static async Task MappedCopy()
    {
        var manifest = WinGetScopedExecutionTests.Manifest("Ubisoft.Connect");
        const string details = "Ubisoft Connect [Ubisoft.Connect]\nVersion: < 175.1.0.13416\nLocal Identifier: ARP\\Machine\\X86\\Uplay\nInstalled Scope: machine\nInstalled Architecture: x86\n";
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __) => Task.FromResult(
            new ProcessResult(0, args[0] == "--version" ? "v1.29.290" : details, ""));
        var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest),
            () => "S-1-5-21-1000", (_, _) => "", "x64", readDisplayVersion: _ => "174.0.13280");
        var item = new UpdateItem { Id = "Ubisoft.Connect", Name = "Ubisoft Connect", Source = "winget", Kind = UpdateKind.Software,
            InstalledVersion = "< 175.1.0.13416", AvailableVersion = "175.1.0.13416" };
        Check(await guard.PrepareAsync(item, manifest, default) is not null,
            "Exact local identity and mapped raw ARP version were lost because the provider reports a version range.");
    }

    private static async Task DelayedCopy()
    {
        var manifest = WinGetScopedExecutionTests.Manifest("ElementLabs.LMStudio");
        var version = "0.4.23+1";
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __) => Task.FromResult(
            new ProcessResult(0, args[0] == "--version" ? "v1.29.290" :
                $"LM Studio [ElementLabs.LMStudio]\nVersion: {version}\nLocal Identifier: ARP\\User\\X64\\c6dbe996-22a9-5998-b542-7abe33da3b83\nInstalled Scope: user\nInstalled Architecture: x64\n", ""));
        var reads = 0;
        var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest),
            () => "S-1-5-21-1000", (_, _) => "user", "x64", readDisplayVersion: _ => ++reads < 2 ? version : "0.4.28+2");
        var item = new UpdateItem { Id = "ElementLabs.LMStudio", Name = "LM Studio", Kind = UpdateKind.Software,
            Source = "winget", InstalledVersion = version, AvailableVersion = "0.4.28+2" };
        item.WinGetBinding = await guard.PrepareAsync(item, manifest, default);
        Check(item.WinGetBinding is not null, "Delayed fixture could not bind.");
        reads = 0;
        Check((await guard.VerifyAsync(ElevatedUpdateRunner.ToPlanItem(item))).Verified,
            "Scoped verification stopped before delayed exact-copy evidence became available.");
    }

    private static async Task OpaqueCopy()
    {
        var manifest = WinGetScopedExecutionTests.Manifest("Moonsworth.LunarClient");
        var version = "3.7.21-ow";
        var key = "ARP\\User\\X64\\1fcec38f-e773-5444-8669-32b8eb41524b";
        var evidence = new WinGetCopyEvidence(version, ExecutablePath: @"C:\fixture\launcher\Lunar Client.exe",
            ProductName: "Lunar Client", CompanyName: "Moonsworth, LLC", ProductVersion: "2026.5", ExecutableSha256: new string('A', 64));
        var effects = new List<string[]>();
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __)
        {
            if (args[0] == "upgrade" && !args.Contains("--id") || args[0] == "list" && !args.Contains("--id"))
            {
                var table = $"{"Name",-30}{"Id",-30}{"Version",-16}{"Available",-16}Source\n" + new string('-', 105) + "\n" +
                    $"{"Uninstall Lunar Client",-30}{"Moonsworth.LunarClient",-30}{version,-16}{"3.7.28",-16}winget\n";
                return Task.FromResult(new ProcessResult(0, table, ""));
            }
            if (args[0] is not ("--version" or "list")) { effects.Add(args.ToArray()); return Task.FromResult(new ProcessResult(0, "simulated", "")); }
            return Task.FromResult(new ProcessResult(0, args[0] == "--version" ? "v1.29.290" :
                $"Uninstall Lunar Client [Moonsworth.LunarClient]\nVersion: {version}\nLocal Identifier: {key}\nInstalled Scope: user\nInstalled Architecture: neutral\n", ""));
        }
        var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest), () => "S-1-5-21-1000",
            (_, _) => "user", "x64", readDisplayVersion: _ => version, readCopyEvidence: _ => evidence);
        var metadata = new WinGetManifestSafetyService((_, _, _) => Task.FromResult(new WinGetManifestSafetyService.ManifestInspection(
            WinGetUpgradeSafety.Safe, 0, "", manifest)), _ => guard, (_, _) => "user", "x64", () => false);
        var scanner = new WinGetService(Execute, metadata);
        var item = (await scanner.ScanAsync(false, default)).Single();
        Check(item.WinGetBinding?.CopyEvidence?.ExecutablePath == evidence.ExecutablePath,
            "The opaque exact local copy lost its independent PE identity before consent.");
        var plan = ElevatedUpdateRunner.ToPlanItem(item);
        var frozen = RunnerPlanSnapshot.Create(new UpdatePlan { Items = [plan], SilentSoftwareInstall = true }, false);
        plan.WinGetBinding = plan.WinGetBinding! with { CopyEvidence = evidence with { ExecutablePath = @"C:\other\app.exe" } };
        plan = frozen.Items.Single().ToPlanItem();
        Check(plan.WinGetBinding?.CopyEvidence?.ExecutablePath == @"C:\fixture\launcher\Lunar Client.exe", "Consent snapshot followed mutable copy evidence.");
        var initial = ElevatedUpdateRunner.InstallSoftwareWithBoundaries(plan, true, false, Execute, guard);
        Check(initial.InstallerSucceeded && !initial.Verified && effects.Count == 1,
            "A proven opaque local copy was unnecessarily blocked.");
        Check(!effects[0].Contains("--scope"), "A missing manifest scope was invented as a command constraint.");
        version = "3.7.28-ow";
        evidence = evidence with { DisplayVersion = version, ProductVersion = "3.7.28.0", ExecutableSha256 = new string('B', 64) };
        Check((await guard.VerifyAsync(plan)).Verified, "The same executable and exact native copy at the target PE version remained pending.");
        var completed = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan, new ProcessResult(0, "simulated", ""),
            target => guard.VerifyAsync(target).GetAwaiter().GetResult());
        Check(completed.Verified && completed.Success, "Verified copy evidence failed the production result policy.");
        var current = (await scanner.ScanAsync(false, default)).Single();
        Check(!current.CanInstall && !current.IsSelected, "An independently verified current target was offered for installation again.");
        evidence = evidence with { ExecutablePath = @"C:\other\Lunar Client.exe" };
        Check(!(await guard.VerifyAsync(plan)).Verified, "Another copy supplied PE proof.");
    }

    private static async Task DeferredVerification()
    {
        var binding = new WinGetScopeBinding("user", @"ARP\User\X64\product", "S-1-5-21-1000", "v1.29.290",
            new string('A', 64), "nullsoft", "x64", "Safe");
        var plan = new PlanItem { Id = "Example.Tool", Name = "Tool", Source = "winget", Kind = "Software",
            InstalledVersion = "1.0", AvailableVersion = "2.0", WinGetBinding = binding };
        var entry = new HistoryEntry { Name = "Tool", Result = "Da verificare", Details = "Original pending result",
            Diagnostics = "Original installer evidence", PendingVerificationPlan = plan };
        var legacy = new HistoryEntry { Name = "Legacy", Result = "Da verificare", Details = "Original legacy result" };
        var calls = 0;
        Task<UpdateVerificationResult> Verify(PlanItem target, CancellationToken _)
        {
            calls++;
            Check(target.WinGetBinding == binding && target.AvailableVersion == "2.0", "Deferred target lost the approved copy.");
            return Task.FromResult(new UpdateVerificationResult { Verified = true, IsDefinitive = true,
                Status = UpdateVerificationStatuses.Verified, Diagnostics = "Independent current evidence" });
        }
        Check(await DeferredWinGetVerification.ReconcileAsync([entry, legacy], Verify, default) && calls == 1,
            "A reliably verified pending result was never reconciled.");
        Check(entry.VerifiedLaterUtc is not null && entry.PendingVerificationPlan is null &&
            entry.Diagnostics.Contains("Original installer evidence") && entry.Diagnostics.Contains("Independent current evidence"),
            "Deferred verification lost the original evidence or stayed pending.");
        Check(legacy.Result == "Da verificare" && legacy.Details == "Original legacy result", "Legacy history without a bound target was invented.");
        Check(!await DeferredWinGetVerification.ReconcileAsync([entry, legacy], Verify, default) && calls == 1,
            "A completed deferred result was repeated.");
    }

    private static void LegacyDrift()
    {
        const string manifest = "PackageIdentifier: Example.Tool\nPackageVersion: 2.0\nInstallerType: nullsoft\nScope: user\nUpgradeBehavior: install\nInstallers:\n- Architecture: x64\n";
        var effects = 0;
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __)
        {
            if (args[0] == "--version") return Task.FromResult(new ProcessResult(0, "v1.29.290", ""));
            if (args[0] == "list") return Task.FromResult(new ProcessResult(0,
                "Tool [Example.Tool]\nVersion: 3.0\nLocal Identifier: ARP\\User\\X64\\other-copy\nInstalled Scope: user\nInstalled Architecture: x64\n", ""));
            effects++; return Task.FromResult(new ProcessResult(0, "simulated", ""));
        }
        var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest),
            () => "S-1-5-21-1000", (_, _) => "user", "x64");
        var item = new PlanItem { Id = "Example.Tool", Name = "Tool", Source = "winget", Kind = "Software",
            InstalledVersion = "1.0", AvailableVersion = "2.0" };
        Check(WinGetService.Upgrade(item, true, Execute, guard).PreflightRejected && effects == 0,
            "The unbound preflight ignored changed native identity/version and reached execution.");
    }

    private static async Task DeferredFairness()
    {
        var binding = new WinGetScopeBinding("user", @"ARP\User\X64\product", "S-1-5-21-1000", "v1.29.290",
            new string('A', 64), "nullsoft", "x64", "Safe");
        var entries = Enumerable.Range(0, 21).Select(i => new HistoryEntry { Name = "Tool" + i,
            PendingVerificationPlan = new PlanItem { Id = "Example.Tool" + i, Name = "Tool", Kind = "Software", Source = "winget",
                InstalledVersion = "1.0", AvailableVersion = "2.0", WinGetBinding = binding } }).ToArray();
        Task<UpdateVerificationResult> Verify(PlanItem plan, CancellationToken _) => Task.FromResult(plan.Id == "Example.Tool20"
            ? new UpdateVerificationResult { Verified = true, IsDefinitive = true, Status = UpdateVerificationStatuses.Verified }
            : new UpdateVerificationResult { Status = UpdateVerificationStatuses.Unavailable });
        await DeferredWinGetVerification.ReconcileAsync(entries, Verify, default);
        await DeferredWinGetVerification.ReconcileAsync(entries, Verify, default);
        Check(entries[20].VerifiedLaterUtc is not null, "Older verifiable entries starved behind unresolved entries.");
    }

    private static void HistoryTarget()
    {
        var binding = new WinGetScopeBinding("user", @"ARP\User\X64\product", "S-1-5-21-1000", "v1.29.290",
            new string('A', 64), "nullsoft", "x64", "Safe");
        var selected = new UpdateItem { Id = "Example.Tool", Name = "Tool", Kind = UpdateKind.Software, Source = "winget",
            InstalledVersion = "1.0", AvailableVersion = "9.0", WinGetBinding = binding };
        var vm = new UpdateCenter.ViewModels.MainViewModel(null!);
        var save = typeof(UpdateCenter.ViewModels.MainViewModel).GetMethod("SaveRunToHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = new ItemRunResult { Id = selected.Id, Name = "Tool", Kind = "Software", InstallerSucceeded = true,
            VerificationStatus = UpdateVerificationStatuses.Unavailable };
        save.Invoke(vm, [new[] { selected }, new UpdateRunStatus { Results = [result] }]);
        Check(vm.History[0].PendingVerificationPlan is null, "Mutable selected UI data became an approved deferred target.");
        result.ReportedTarget = ElevatedUpdateRunner.ToPlanItem(selected);
        result.ReportedTarget.AvailableVersion = "2.0";
        save.Invoke(vm, [new[] { selected }, new UpdateRunStatus { Results = [result] }]);
        result.ReportedTarget.AvailableVersion = "8.0";
        Check(vm.History[0].PendingVerificationPlan?.AvailableVersion == "2.0", "Deferred history followed the mutable selected target.");
    }

    private static void PrimaryExecutable()
    {
        var helper = new WinGetCopyEvidence("3.7.21-ow", ExecutablePath: @"C:\fixture\launcher\Updater.exe",
            ProductName: "Lunar Client", CompanyName: "Moonsworth LLC", ProductVersion: "3.7.28.0", ExecutableSha256: new string('A', 64));
        Check(!WinGetCopyEvidenceReader.Valid(helper), "An auxiliary executable masqueraded as the primary product.");
    }

    private static void CopyConsent()
    {
        var copy = new WinGetCopyEvidence("3.7.21-ow", true, @"C:\fixture\launcher\Lunar Client.exe",
            "Lunar Client", "Moonsworth LLC", "3.7.21.0", new string('A', 64));
        var plan = new PlanItem { Id = "Example.Tool", Name = "Tool", Kind = "Software", Source = "winget", InstalledVersion = "1.0",
            AvailableVersion = "2.0", WinGetBinding = new("user", @"ARP\User\X64\product", "S-1-5-21-1000", "v1.29.290",
                new string('B', 64), "nullsoft", "neutral", "Safe", CopyEvidence: copy) };
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            var description = RunnerConsentPresentation.Describe(RunnerPlanSnapshot.Create(new UpdatePlan { Items = [plan] }, false));
            Check(description.Contains(copy.ExecutablePath) && description.Contains(copy.ProductName),
                "Consent omitted the exact executable identity used to verify this copy.");
            Check(!description.Contains(language == "it" ? "Ambito vincolato:" : "Required scope:"),
                "Consent promised a scope command constraint that this manifest cannot express.");
        }
        LocalizationService.Initialize("it");
    }

    private static async Task MappedDowngrade()
    {
        var manifest = WinGetScopedExecutionTests.Manifest("Ubisoft.Connect");
        const string id = "Ubisoft.Connect", version = "< 175.1.0.13416", target = "175.1.0.13416";
        const string details = "Ubisoft Connect [Ubisoft.Connect]\nVersion: < 175.1.0.13416\nLocal Identifier: ARP\\Machine\\X86\\Uplay\nInstalled Scope: machine\nInstalled Architecture: x86\n";
        var effects = 0;
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __)
        {
            if (args[0] == "--version") return Task.FromResult(new ProcessResult(0, "v1.29.290", ""));
            if (args[0] == "list" && args.Contains("--id")) return Task.FromResult(new ProcessResult(0, details, ""));
            if (!args.Contains("--id"))
            {
                var table = $"{"Name",-24}{"Id",-25}{"Version",-24}{"Available",-24}Source\n" + new string('-', 105) + "\n" +
                    $"{"Ubisoft Connect",-24}{id,-25}{version,-24}{target,-24}winget\n";
                return Task.FromResult(new ProcessResult(0, table, ""));
            }
            effects++; return Task.FromResult(new ProcessResult(0, "simulated installer", ""));
        }
        var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest),
            () => "S-1-5-21-1000", (_, _) => "", "x64", readDisplayVersion: _ => "176.1.14000",
            readCopyEvidence: _ => new WinGetCopyEvidence("176.1.14000"));
        var metadata = new WinGetManifestSafetyService((_, _, _) => Task.FromResult(new WinGetManifestSafetyService.ManifestInspection(
            WinGetUpgradeSafety.Safe, 0, "machine", manifest)), _ => guard, (_, _) => "", "x64", () => false);
        var item = (await new WinGetService(Execute, metadata).ScanAsync(false, default)).Single();
        var snapshot = RunnerPlanSnapshot.Create(new UpdatePlan { Items = [ElevatedUpdateRunner.ToPlanItem(item)] }, false);
        var result = ElevatedUpdateRunner.InstallSoftwareWithBoundaries(snapshot.Items[0].ToPlanItem(), true, false, Execute, guard);
        Check(effects == 0 && !result.InstallerSucceeded && !result.Verified && result.Phase == "winget-preflight",
            "A certainly lower mapped DisplayVersion reached installation after binding admission failed.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
