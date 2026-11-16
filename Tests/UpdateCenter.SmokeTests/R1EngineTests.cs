using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class R1EngineTests
{
    internal static void Run()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production data root selected");
        var failures = new List<string>();
        Action[] tests = [PackagedServicesCannotReachInstaller, RequiredElevationCannotReachInstaller,
            IncompatibleArchitectureCannotReachInstaller, IncompatibleWindowsCannotReachInstaller,
            IrrelevantArchitectureDoesNotBlockSupportedRecipe, ClaudeFailureKeepsCauseAndManualAction,
            QuerySuccessIsNotVersionProof, OtherSourceDoesNotVerifyTarget, OutcomeEvidenceMatrix,
            AudacityFailureAndSuppressionRemainSeparate, RequirementStatesAndScanSelection,
            ElevationProhibitionAndListBoundaries, CriticalRequirementsCannotBeErased,
            ColdCacheRequirementsAndHistoryTranslation];
        foreach (var test in tests)
        {
            try { test(); Console.WriteLine("PASS R1 " + test.Method.Name); }
            catch (Exception ex) { failures.Add(test.Method.Name + ": " + ex.Message); Console.WriteLine("FAIL R1 " + failures[^1]); }
        }
        Console.WriteLine($"R1 engine offline: {tests.Length - failures.Count} PASS, {failures.Count} FAIL; no native installers or production writes.");
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    // Reduced metadata fixture, not a reconstruction of the historical scan bytes.
    // Official 2.42304.2 manifest consulted 2026-11-10: both MSIX architectures
    // declare these services; user EXE alternatives do not identify the current copy.
    private const string ClaudeManifest = """
        PackageIdentifier: Anthropic.Claude
        PackageVersion: 2.42304.2
        Installers:
        - Architecture: x64
          InstallerType: msix
          MinimumOSVersion: 10.0.18362.0
          PackageFamilyName: Claude_pzs8sxrjxfjjc
          RestrictedCapabilities:
          - localSystemServices
          - packagedServices
          - runFullTrust
          - unvirtualizedResources
        - Architecture: arm64
          InstallerType: msix
          MinimumOSVersion: 10.0.18362.0
          PackageFamilyName: Claude_pzs8sxrjxfjjc
          RestrictedCapabilities:
          - localSystemServices
          - packagedServices
        - Architecture: x64
          InstallerType: exe
          Scope: user
          UpgradeBehavior: install
          ProductCode: AnthropicClaude
        - Architecture: arm64
          InstallerType: exe
          Scope: user
          UpgradeBehavior: install
          ProductCode: AnthropicClaude
        """;

    private static PlanItem Claude() => new() { Id = "Anthropic.Claude", Name = "Claude", Kind = "Software",
        Source = "winget", InstalledVersion = "1.52386.3.0", AvailableVersion = "2.42304.2" };

    private static void PackagedServicesCannotReachInstaller()
    {
        foreach (var interactive in new[] { false, true })
        {
            var (result, effects) = Dispatch(ClaudeManifest, Claude(), interactive);
            Check(result.PreflightRejected && effects == 0,
                "Unbound MSIX service candidate reached installer instead of manual preflight");
            Check(!string.IsNullOrWhiteSpace(result.StandardError), "Missing actionable preflight explanation");
        }
        foreach (var capability in new[] { "packagedServices", "localSystemServices" })
        {
            var inherited = Manifest("RestrictedCapabilities:\n- " + capability + "\n", "msix");
            var (result, effects) = Dispatch(inherited);
            Check(result.PreflightRejected && effects == 0, "Root service capability disappeared during inheritance");
        }
    }

    private static void RequiredElevationCannotReachInstaller()
    {
        foreach (var manifest in new[] { Manifest("ElevationRequirement: elevationRequired\n"),
            Manifest("ElevationRequirement: elevatesSelf\n") + "  ElevationRequirement: elevationRequired\n" })
        {
            var (result, effects) = Dispatch(manifest);
            Check(result.PreflightRejected && effects == 0, "Explicit required elevation reached unsupported software-admin path");
        }
    }

    private static void IncompatibleArchitectureCannotReachInstaller()
    {
        var (result, effects) = Dispatch(Manifest("", arch: "arm64"));
        Check(result.PreflightRejected && effects == 0, "No compatible architecture still launched installer");
    }

    private static void IncompatibleWindowsCannotReachInstaller()
    {
        var (result, effects) = Dispatch(Manifest("MinimumOSVersion: 99.0.0.0\n"));
        Check(result.PreflightRejected && effects == 0, "Known unsupported Windows version still launched installer");
    }

    private static void IrrelevantArchitectureDoesNotBlockSupportedRecipe()
    {
        var manifest = Manifest("ElevationRequirement: elevatesSelf\n") +
            "- Architecture: arm64\n  InstallerType: msix\n  RestrictedCapabilities:\n  - packagedServices\n";
        var (result, effects) = Dispatch(manifest);
        Check(result.Success && effects == 1, "An ARM-only adverse variant blocked the x64 execution candidate");
    }

    private static void ClaudeFailureKeepsCauseAndManualAction()
    {
        const int code = unchecked((int)0x80073D28);
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            var plan = Claude();
            var result = Complete(ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan,
                new(code, "Installer failed with exit code: 0x80073d28. RAW_SENTINEL", "", "winget upgrade --id Anthropic.Claude"),
                _ => OldVersion(plan)));
            Check(result.Outcome == "Failed" && !result.Success && result.FailureReason == "ElevationRequired" &&
                result.TerminalDisposition == ItemTerminalDisposition.Failed, "Claude execution failure lost its specific privilege cause");
            Check(result.ResultCode == code && result.Diagnostics.Contains("RAW_SENTINEL") &&
                !result.Message.Contains("RAW_SENTINEL"), "Raw diagnostics leaked into short explanation or were lost");
            Check(result.Message.Contains(language == "it" ? "amministr" : "administr", StringComparison.OrdinalIgnoreCase),
                "Missing localized manual administrator action");
            var item = new UpdateItem { Id = plan.Id, Name = plan.Name, Kind = UpdateKind.Software,
                Source = "winget", InstalledVersion = plan.InstalledVersion, AvailableVersion = plan.AvailableVersion };
            var vm = new MainViewModel(); vm.Updates.Add(item); vm.ApplyRunResults([item], [result], isFinal: true);
            Check(!item.CanRetry && !item.CanInstall, "Privilege failure offered a retry through the same unsupported path");
        }
        Check(WinGetService.ClassifyFailureReason(new(code, "", ""), true) == "", "Verified target retained a failure cause");
    }

    private static void QuerySuccessIsNotVersionProof()
    {
        var plan = Claude();
        var calls = 0;
        var result = WinGetService.VerifyInstallation(plan, (_, _) =>
        {
            calls++;
            return (new ProcessResult(0, "query completed", ""),
                new List<WinGetPackageRow> { new(plan.Name, plan.Id, plan.InstalledVersion, "", "winget") });
        }, waitBeforeRetry: _ => { });
        Check(calls == 3 && result.IsDefinitive && !result.Verified && result.Status == "Failed",
            "Query success was promoted into installed-version success");
        Check(result.Diagnostics.Contains("queryExit=0") && result.Diagnostics.Contains("parsedRows=1") &&
            result.Diagnostics.Contains("exactMatches=1") && result.Diagnostics.Contains("installed=[1.52386.3.0]") &&
            result.Diagnostics.Contains("target=2.42304.2"), "Verification diagnostic lacks parsed evidence distinguishing query from application result");
        var empty = WinGetService.VerifyInstallation(plan, (_, _) => (new ProcessResult(0, "not a table", ""), []), maxAttempts: 1);
        Check(!empty.Verified && !empty.IsDefinitive && empty.Status == "Unavailable", "Unparseable exit-zero response became definitive");
    }

    private static void OtherSourceDoesNotVerifyTarget()
    {
        var plan = Claude();
        var verification = WinGetService.VerifyInstallation(plan, (_, _) => (new ProcessResult(0, "", ""),
            [new(plan.Name, plan.Id, plan.AvailableVersion, "", "other-source")]), maxAttempts: 1);
        Check(!verification.Verified && !verification.IsDefinitive, "Conflicting explicit source proved target version");
    }

    private static void OutcomeEvidenceMatrix()
    {
        var plan = Claude();
        var failed = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan, new(1, "error", ""), _ => OldVersion(plan));
        var exitZeroOld = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan, new(0, "done", ""), _ => OldVersion(plan));
        var unavailable = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan, new(0, "done", ""),
            _ => new() { Status = "Unavailable" });
        var restart = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan, new(3010, "", ""),
            _ => new() { Status = "Unavailable" });
        var verified = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan, new(0, "", ""),
            _ => new() { IsDefinitive = true, Verified = true, Status = "Verified" });
        Check(!failed.Success && !exitZeroOld.Success, "Failed/old-version result promoted into success");
        Check(!unavailable.Verified && !MainViewModel.ShouldRemoveCompletedUpdate(unavailable) &&
            !restart.Verified && restart.RestartRequired && restart.VerificationStatus == "PendingRestart",
            "Pending/unavailable verification was treated as proven installed version");
        Check(verified.Verified && MainViewModel.ShouldRemoveCompletedUpdate(verified), "Positive version evidence was discarded");
        var interrupted = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan, new(0, "", ""), _ => throw new TimeoutException("query interrupted"));
        Check(!interrupted.Verified && interrupted.FailureReason == "Infrastructure" && interrupted.VerificationStatus == "Unavailable",
            "Interrupted verification became verified success");
        var cancelled = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan, new(0, "", ""), _ => throw new OperationCanceledException());
        Check(cancelled.Outcome == "Cancelled" && !cancelled.Verified, "Cancellation lost its distinct disposition");
        Check(WinGetService.ClassifyFailureReason(new(unchecked((int)0x80070020), "", ""), false) == "FilesInUse", "Existing file-in-use evidence regressed");
    }

    private static void AudacityFailureAndSuppressionRemainSeparate()
    {
        var plan = new PlanItem { Id = "Audacity.Audacity", Name = "Audacity", Kind = "Software", Source = "winget",
            InstalledVersion = "3.7.8", AvailableVersion = "4.0.2.263060742" };
        var result = Complete(ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan,
            new(unchecked((int)0x8A15002B), "The install technology of the newer version specified is different from the current version installed.", ""),
            _ => throw new InvalidOperationException("Applicability rejection must not verify")));
        Check(result.Outcome == "Failed" && result.FailureReason == "InstallerTechnologyMismatch" &&
            result.TerminalDisposition == ItemTerminalDisposition.Failed, "Audacity attempted failure reclassified as prevented operation");
        var item = new UpdateItem { Id = plan.Id, Name = plan.Name, Kind = UpdateKind.Software, Source = plan.Source,
            InstalledVersion = plan.InstalledVersion, AvailableVersion = plan.AvailableVersion };
        Check(WinGetApplicabilityStore.ExcludeSuppressed([item]).Count == 0, "Audacity same version pair no longer suppressed");
    }

    private static void RequirementStatesAndScanSelection()
    {
        foreach (var (root, expectedState, canInstall, selected) in new[] {
            ("ElevationRequirement: elevationProhibited\n", "Supported", true, true),
            ("ElevationRequirement: elevatesSelf\n", "InstallerSelfElevation", true, true),
            ("ElevationRequirement: elevationRequired\n", "ManualRequired", false, false),
            ("", "Unknown", true, false) })
        {
            var assessment = WinGetScopeGuard.AssessRequirements(WinGetInstallerMetadata.Parse(Manifest(root)),
                "Fixture.Tool", "2.0", "x64", "user", windowsVersion: new Version(10, 0, 17763, 0));
            Check(assessment.State.ToString() == expectedState, "Wrong requirements state: " + expectedState);
            var item = new UpdateItem { Id = "Fixture.Tool", Name = "Fixture", Kind = UpdateKind.Software, Source = "winget" };
            WinGetManifestSafetyService.ApplyInstallationRequirements(item, assessment);
            Check(item.CanInstall == canInstall && item.IsSelected == selected && item.CanShowDetails,
                "Scan selection did not reflect " + expectedState);
            if (expectedState == "Unknown") Check(item.HasUnverifiedInstallerMetadata, "Unknown requirements masqueraded as verified metadata");
        }
        var unsupported = new UpdateItem { Id = "Fixture.Tool", Name = "Fixture", Kind = UpdateKind.Software, CanInstall = false, ResultDetails = "Existing deny reason" };
        var unknown = WinGetScopeGuard.AssessRequirements(null, "Fixture.Tool", "2.0", "x64", "user");
        WinGetManifestSafetyService.ApplyInstallationRequirements(unsupported, unknown);
        Check(unsupported.ResultDetails == "Existing deny reason", "Requirements explanation replaced a stronger existing rejection");
    }

    private static void ElevationProhibitionAndListBoundaries()
    {
        var parsed = WinGetInstallerMetadata.Parse(Manifest("ElevationRequirement: elevationProhibited\n"));
        var admin = WinGetScopeGuard.AssessRequirements(parsed, "Fixture.Tool", "2.0", "x64", "user", isAdministrator: true);
        Check(!admin.CanContinue, "Inherited elevated UI token bypassed an elevation prohibition");
        var overridden = Manifest("ElevationRequirement: elevationRequired\nRestrictedCapabilities:\n- packagedServices\n") +
            "  ElevationRequirement: elevatesSelf\n  RestrictedCapabilities: []\n";
        var good = WinGetScopeGuard.AssessRequirements(WinGetInstallerMetadata.Parse(overridden), "Fixture.Tool", "2.0", "x64", "user");
        Check(good.CanContinue && good.State == WinGetRequirementState.InstallerSelfElevation, "Variant override did not replace inherited requirements");
        foreach (var declaration in new[] { "RestrictedCapabilities: [packagedServices]", "RestrictedCapabilities: *services", "ElevationRequirement: &elevation elevationRequired" })
        {
            var (result, effects) = Dispatch(Manifest(declaration + "\n", "msix"));
            Check(result.PreflightRejected && effects == 0, "Unsupported YAML shape hid a requirement");
        }
        var excluded = Manifest("UnsupportedOSArchitectures:\n- x64\n", arch: "x86");
        Check(Dispatch(excluded).Result.PreflightRejected, "Explicitly unsupported host architecture reached installer");
        var servicesWindows10 = WinGetScopeGuard.AssessRequirements(WinGetInstallerMetadata.Parse(ClaudeManifest),
            "Anthropic.Claude", "2.42304.2", "x64", "user", windowsVersion: new Version(10, 0, 17763, 0));
        Check(servicesWindows10.CanContinue && servicesWindows10.State == WinGetRequirementState.Unknown,
            "Inapplicable MSIX candidate on build 17763 was treated as selected or guaranteed EXE support");
    }

    private static void CriticalRequirementsCannotBeErased()
    {
        foreach (var invalid in new[] { "", "\"\"", "null", "futureRequirement" })
        {
            var manifest = Manifest("ElevationRequirement: elevationRequired\n") + "  ElevationRequirement: " + invalid + "\n";
            var (result, effects) = Dispatch(manifest);
            Check(result.PreflightRejected && effects == 0, "Invalid elevation override erased required administrator privileges: " + invalid);
        }
        foreach (var invalid in new[] { "", "\"\"", "null", "Windows11" })
        {
            var manifest = Manifest("MinimumOSVersion: 99.0.0.0\nElevationRequirement: elevatesSelf\n") + "  MinimumOSVersion: " + invalid + "\n";
            Check(Dispatch(manifest).Result.PreflightRejected, "Invalid Windows version override erased an incompatible requirement: " + invalid);
        }
        foreach (var list in new[] { "  RestrictedCapabilities:\n      - packagedServices\n",
            "  RestrictedCapabilities:\n    map: packagedServices\n", "  RestrictedCapabilities:\n  -\n    packagedServices\n" })
            Check(Dispatch(Manifest("", "msix") + list).Result.PreflightRejected,
                "Unsupported capability list shape silently dropped packagedServices");
        Check(Dispatch(Manifest("RestrictedCapabilities:\n- packagedServices\n", "msix") + "  RestrictedCapabilities:\n").Result.PreflightRejected,
            "Empty capability override silently erased root services");
        foreach (var type in new[] { "\"\"", "futureType", "exe" })
        {
            var manifest = Manifest("InstallerType: msix\nRestrictedCapabilities:\n- packagedServices\n", type);
            var (result, effects) = Dispatch(manifest);
            Check(result.PreflightRejected && effects == 0,
                "Explicit service capability disappeared behind missing/contradictory installer type: " + type);
        }
    }

    private static void ColdCacheRequirementsAndHistoryTranslation()
    {
        static string[] Messages(string language)
        {
            LocalizationService.Initialize(language);
            var inputs = new (string? Manifest, bool Admin)[] {
                (null, false), (Manifest("MinimumOSVersion: 99.0.0.0\n"), false),
                (Manifest("ElevationRequirement: elevationRequired\n"), false),
                (Manifest("ElevationRequirement: elevationProhibited\n"), true), (Manifest(""), false),
                (Manifest("ElevationRequirement: elevatesSelf\n"), false),
                (Manifest("ElevationRequirement: elevationProhibited\n"), false) };
            var values = inputs.Select(input => WinGetScopeGuard.AssessRequirements(
                input.Manifest is null ? null : WinGetInstallerMetadata.Parse(input.Manifest), "Fixture.Tool", "2.0", "x64", "user",
                windowsVersion: new Version(10, 0, 17763, 0), isAdministrator: input.Admin).Message).ToList();
            var plan = Claude();
            values.Add(WinGetService.VerifyInstallation(plan, (_, _) => (new ProcessResult(0, "", ""),
                [new(plan.Name, plan.Id, plan.AvailableVersion, "", "other")]), maxAttempts: 1).Message);
            return values.ToArray();
        }
        var italian = Messages("it"); var english = Messages("en");
        static void Cold()
        {
            // Mirrors a fresh UI process without introducing production cache-reset APIs.
            foreach (var name in new[] { "DynamicItalianToEnglish", "DynamicEnglishToItalian" })
                ((System.Collections.Concurrent.ConcurrentDictionary<string, string>)typeof(LocalizationService)
                    .GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!).Clear();
        }
        foreach (var language in new[] { "it", "en" })
        {
            var original = language == "en" ? italian : english;
            var expected = language == "en" ? english : italian;
            Cold(); LocalizationService.Initialize(language);
            for (var i = 0; i < original.Length; i++)
            {
                var entry = new HistoryEntry { Name = "Fixture", Details = "Fixture: " + original[i] };
                var translated = LocalizationService.Translate(original[i]);
                var history = entry.DisplayDetails;
                Check(translated == expected[i] && history == "Fixture: " + expected[i],
                    $"Requirements/source message failed cold-cache translation {language}/{i}: translated=[{translated}]; history=[{history}]");
                entry.Details = "Fixture: " + expected[i];
                Check(entry.DisplayDetails == "Fixture: " + expected[i], "An already localized history message was translated a second time");
                entry.Details = "Fixture: " + original[i];
                Check(entry.Details == "Fixture: " + original[i], "Displaying translated history rewrote stored data");
            }
        }
        foreach (var safety in new[] { WinGetUpgradeSafety.Unknown, WinGetUpgradeSafety.RemovesPreviousVersion })
        {
            LocalizationService.Initialize("it");
            var item = new UpdateItem { Id = "Fixture.Tool", Name = "Fixture", Kind = UpdateKind.Software };
            WinGetManifestSafetyService.ApplySafetyClassification(item, safety);
            var original = item.ResultDetails;
            WinGetManifestSafetyService.ApplyInstallationRequirements(item,
                WinGetScopeGuard.AssessRequirements(WinGetInstallerMetadata.Parse(Manifest("")), "Fixture.Tool", "2.0", "x64", "user"));
            var completeItalian = item.ResultDetails;
            Cold(); LocalizationService.Initialize("en");
            Check(item.ResultDetails != completeItalian && !item.ResultDetails.Contains(original) && item.ResultDetails.Contains(english[4]),
                "Composed scan explanation did not translate all safety/requirements parts");
            Cold(); LocalizationService.Initialize("it");
            Check(item.ResultDetails == completeItalian, "Round-trip locale changed the scan explanation");
        }
    }

    private static string Manifest(string root, string type = "exe", string arch = "x64") =>
        "PackageIdentifier: Fixture.Tool\nPackageVersion: 2.0\nScope: user\nUpgradeBehavior: install\n" + root +
        "Installers:\n- Architecture: " + arch + "\n  InstallerType: " + type + "\n";

    private static (ProcessResult Result, int Effects) Dispatch(string manifest, PlanItem? plan = null, bool interactive = false)
    {
        plan ??= new() { Id = "Fixture.Tool", Name = "Fixture", Source = "winget", Kind = "Software",
            InstalledVersion = "1.0", AvailableVersion = "2.0" };
        var effects = 0;
        WinGetCommand execute = (args, _, _) =>
        {
            if (args[0] == "--version") return Task.FromResult(new ProcessResult(0, "v1.29.290", ""));
            if (args[0] == "list") return Task.FromResult(new ProcessResult(0,
                $"{plan.Name} [{plan.Id}]\nVersion: {plan.InstalledVersion}\nLocal Identifier: ARP\\User\\X64\\fixture\nInstalled Scope: user\nInstalled Architecture: x64\n", ""));
            effects++; return Task.FromResult(new ProcessResult(0, "offline effect", ""));
        };
        var guard = new WinGetScopeGuard(execute, (_, _, _) => Task.FromResult<string?>(manifest),
            () => "S-1-5-21-1000", (_, _) => "user", "x64");
        var result = interactive ? WinGetService.RunInteractive(plan, execute, guard) : WinGetService.Upgrade(plan, true, execute, guard);
        return (result, effects);
    }

    private static UpdateVerificationResult OldVersion(PlanItem plan) => WinGetService.VerifyInstallation(plan,
        (_, _) => (new ProcessResult(0, "", ""), [new(plan.Name, plan.Id, plan.InstalledVersion, "", plan.Source)]), maxAttempts: 1);
    private static ItemRunResult Complete(ItemRunResult result)
    { var state = new ItemExecutionStateMachine(); state.BeginInitialAttempt(); return state.Complete(result); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
