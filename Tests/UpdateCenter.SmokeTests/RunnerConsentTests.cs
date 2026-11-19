using UpdateCenter.Models;
using UpdateCenter.Services;
using System.Diagnostics;
using System.Text.Json.Nodes;
using UpdateCenter.ViewModels;

internal static class RunnerConsentTests
{
    private static Exception? _assertionFailure;
    internal static void CheckSelectionConsentBoundary()
    {
        _assertionFailure = null;
        DeclinedOrMissing();
        RiskAndPresentation();
        if (_assertionFailure is not null) throw _assertionFailure;
        Console.WriteLine("PASS DeclinedOrMissing / RiskAndPresentation: selection never authorizes dispatch.");
    }
    internal static void Run()
    {
        Check(!Path.GetFullPath(AppPaths.DataDirectory).Equals(Path.GetFullPath(AppPaths.ResolveDefaultRoot()),
            StringComparison.OrdinalIgnoreCase), "Production root selected");
        Console.WriteLine("Isolated data root: " + AppPaths.DataDirectory);
        var failures = new List<string>();
        Action[] tests = [ ApprovedBatch, DeclinedOrMissing, ConfirmationFailure,
            Cancellation, InvalidPlan, RecreatedPlan, FrozenAfterRead, InteractiveFallback,
            RecoveryGate, RetryRequiresNewConsent, DriverBatch, RawSchema, RiskAndPresentation,
            DefensiveCopies, WaitingStatus, CancelBeforeLaunch, CancelledResults, EffectiveResults,
            BrokenStatusChannel, InvalidDriverMetadata, InfrastructureResultTarget,
            FallbackCancellation, ProjectionKeepsPreliminaryTarget, ProjectionFinalizesUnreportedItems, ScopedBindingConsent ];
        foreach (var test in tests)
        {
            _assertionFailure = null;
            try { test(); if (_assertionFailure is not null) throw _assertionFailure; Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception ex) { failures.Add(test.Method.Name); Console.WriteLine("FAIL " + test.Method.Name + ": " + ex.Message); }
        }
        Console.WriteLine($"M6-B runner boundary: {tests.Length - failures.Count} PASS, {failures.Count} FAIL; simulated effects only.");
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(", ", failures));
    }

    private static void ApprovedBatch()
    {
        var (path, plan) = Fixture();
        plan.CreateRestorePoint = true;
        plan.Items.Add(Software("Contoso.Second"));
        JsonStorage.WriteAtomic(path, plan);
        var effects = new Effects();
        var consent = new Consent((_, _) => { Check(effects.Calls.Count == 0, "Effects preceded consent"); return true; });
        var exit = ElevatedUpdateRunner.Run(path, false, consent, effects, default);
        Check(exit == 0 && consent.Calls == 1, "Whole batch was not confirmed exactly once");
        Check(effects.Calls.SequenceEqual(new[] { "restore", "Contoso.Tool|2.0|winget|Upgrade|True|False",
            "Contoso.Second|2.0|winget|Upgrade|True|False" }), "Approved batch changed");
    }

    private static void DeclinedOrMissing()
    {
        foreach (var consent in new IRunnerConsent?[] { new Consent((_, _) => false), null })
        {
            var (path, plan) = Fixture(); plan.CreateRestorePoint = true;
            JsonStorage.WriteAtomic(path, plan);
            var effects = new Effects();
            Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) != 0, "Rejected consent reported success");
            Check(effects.Calls.Count == 0, "Denied/missing confirmation reached an effect");
        }
    }

    private static void ConfirmationFailure()
    {
        foreach (var error in new Exception[] { new InvalidOperationException("No interactive session"), new OperationCanceledException() })
        {
            var (path, _) = Fixture(); var effects = new Effects();
            var consent = new Consent((_, _) => throw error);
            Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) != 0 && effects.Calls.Count == 0,
                "Unavailable/closed confirmation reached effects");
        }
    }

    private static void Cancellation()
    {
        foreach (var before in new[] { true, false })
        {
            using var cancellation = new CancellationTokenSource();
            var (path, _) = Fixture(); var effects = new Effects();
            if (before) cancellation.Cancel();
            var consent = new Consent((_, _) => { cancellation.Cancel(); return true; });
            Check(ElevatedUpdateRunner.Run(path, false, consent, effects, cancellation.Token) != 0 && effects.Calls.Count == 0,
                "Cancellation before/at confirmation reached effects");
        }
    }

    private static void InvalidPlan()
    {
        foreach (var mutate in new Action<UpdatePlan>[] { p => p.Items[0].Id = "", p => p.Items[0].AvailableVersion = "",
            p => p.Items[0].Source = "", p => p.Items[0].PackageOperation = "Remove", p => p.Items[0].Kind = "Unknown",
            p => p.Items.Add(Software()), p => p.Items[0].DriverInstallMode = DriverInstallModes.WindowsUpdate })
        {
            var (path, plan) = Fixture(); mutate(plan); JsonStorage.WriteAtomic(path, plan);
            var effects = new Effects(); var consent = new Consent((_, _) => true);
            Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) != 0 && effects.Calls.Count == 0 && consent.Calls == 0,
                "Invalid/ambiguous plan was offered or executed");
        }
    }

    private static void RecreatedPlan()
    {
        var (path, plan) = Fixture(); var effects = new Effects();
        var consent = new Consent((_, _) => true);
        Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) == 0, "First run failed");
        effects.Calls.Clear(); plan.Items[0].AvailableVersion = "9.0"; JsonStorage.WriteAtomic(path, plan);
        var denied = new Consent((_, _) => false);
        Check(ElevatedUpdateRunner.Run(path, false, denied, effects, default) != 0 && denied.Calls == 1 && effects.Calls.Count == 0,
            "Recreated/altered plan reused old approval");
        plan.Items[0].AvailableVersion = "2.0"; JsonStorage.WriteAtomic(path, plan);
        Check(ElevatedUpdateRunner.Run(path, false, denied, effects, default) != 0 && denied.Calls == 2 && effects.Calls.Count == 0,
            "Exact replay reused old approval");
    }

    private static void FrozenAfterRead()
    {
        var (path, plan) = Fixture(); var effects = new Effects();
        var consent = new Consent((received, _) =>
        {
            Check(received.Items[0].AvailableVersion == "2.0", "Wrong displayed target");
            var presentationCopy = received.Items[0].ToPlanItem(); presentationCopy.AvailableVersion = "8.0";
            plan.Items[0].AvailableVersion = "9.0"; JsonStorage.WriteAtomic(path, plan);
            return true;
        });
        Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) == 0 && consent.Calls == 1 &&
            effects.Calls.Single() == "Contoso.Tool|2.0|winget|Upgrade|True|False", "Execution reread the changed JSON");
    }

    private static void InteractiveFallback()
    {
        var item = new UpdateItem { Id = "Contoso.Tool", Name = "Tool", Kind = UpdateKind.Software,
            Source = "winget", AvailableVersion = "2.0" };
        foreach (var approved in new[] { false, true })
        {
            var effects = new Effects(); var consent = new Consent((_, interactive) =>
            { Check(interactive, "Fallback mode was not disclosed"); return approved; });
            var result = ElevatedUpdateRunner.InstallSoftwareInteractive(item, consent, effects, default);
            Check(consent.Calls == 1 && result.Success == approved && effects.Calls.Count == (approved ? 1 : 0),
                "Fallback bypassed its own effective-plan confirmation");
            if (approved) Check(effects.Calls.Single() == "Contoso.Tool|2.0|winget|Upgrade|False|True", "Fallback parameters changed");
        }
    }

    private static UpdateItem SoftwareItem() => new() { Id = "Contoso.Tool", Name = "Tool", Kind = UpdateKind.Software,
        Source = "winget", InstalledVersion = "1.0", AvailableVersion = "2.0" };

    private static void RecoveryGate()
    {
        foreach (var approved in new[] { false, true })
        {
            var operations = 0;
            var consent = new Consent((plan, _) => { Check(plan.Recovery && plan.Items.Single().Id == "Contoso.Tool", "Wrong recovery target/purpose"); return approved; });
            var preparation = ElevatedUpdateRunner.PrepareRecovery(SoftwareItem(), true, consent, default, item =>
            { operations++; Check(item.Source == "winget" && item.AvailableVersion == "2.0", "Recovery target changed"); return new(true, "simulated"); });
            Check(consent.Calls == 1 && operations == (approved ? 1 : 0) && preparation.ShouldRetry == approved,
                "Recovery effects bypassed effective-target consent");
        }
    }

    private static void RetryRequiresNewConsent()
    {
        var (path, plan) = Fixture(); var effects = new Effects();
        var firstConsent = new Consent((_, _) => true);
        Check(ElevatedUpdateRunner.Run(path, false, firstConsent, effects, default) == 0, "Initial run failed");
        effects.Calls.Clear();
        var retryConsent = new Consent((_, _) => false);
        var initial = new ItemRunResult { Id = "Contoso.Tool", Kind = "Software", FailureReason = UpdateFailureReasons.FilesInUse };
        var result = WinGetSingleRetryPolicy.ExecuteAsync(SoftwareItem(), initial, () => new(true, "fake blockers cleared"), () =>
        {
            plan.Items.Add(Software("Contoso.Added")); JsonStorage.WriteAtomic(path, plan);
            ElevatedUpdateRunner.Run(path, false, retryConsent, effects, default);
            return Task.FromResult(JsonStorage.Read<UpdateRunStatus>(plan.StatusFile)!.Results[0]);
        }).GetAwaiter().GetResult();
        Check(retryConsent.Calls == 1 && effects.Calls.Count == 0 && !result.Success,
            "New runner/retry broadened prior authorization");
    }

    private static void DriverBatch()
    {
        const string updateId = "c42aaae1-cb2a-469d-8be6-ec88f26f485d";
        foreach (var approved in new[] { false, true })
        {
            var (path, plan) = Fixture(); plan.CreateRestorePoint = true;
            plan.Items = [new PlanItem { Id = updateId, WindowsUpdateId = updateId, WindowsUpdateRevision = 3,
                Name = "Driver", Kind = "Driver", Source = "Windows Update", AvailableVersion = "1.2",
                DriverInstallMode = DriverInstallModes.WindowsUpdate }];
            JsonStorage.WriteAtomic(path, plan); var effects = new Effects();
            var consent = new Consent((received, _) =>
            {
                var shown = received.Items[0].ToPlanItem();
                Check(shown.WindowsUpdateRevision == 3 && effects.Calls.Count == 0, "Driver metadata/effect order changed"); return approved;
            });
            Check((ElevatedUpdateRunner.Run(path, true, consent, effects, default) == 0) == approved &&
                effects.Calls.Count == (approved ? 2 : 0), "Driver consent failed to control restore/provider");
            if (approved) Check(effects.Calls.SequenceEqual(new[] { "restore", "driver|" + updateId }), "Wrong driver routing");
            effects.Calls.Clear();
            Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) != 0 && effects.Calls.Count == 0,
                "User runner accepted driver batch");
            effects.Administrator = false;
            Check(ElevatedUpdateRunner.Run(path, true, consent, effects, default) != 0 && effects.Calls.Count == 0,
                "Missing administrator privileges accepted");
        }
    }

    private static void RawSchema()
    {
        foreach (var mutate in new Action<JsonObject>[] {
            p => p.Remove("SilentSoftwareInstall"), p => p.Remove("CreateRestorePoint"),
            p => p["Items"]![0]!.AsObject().Remove("PackageOperation"),
            p => p["Items"]![0]! ["ExpectedSignerSubjects"] = null,
            p => p["Items"]![0]! ["Id"] = "Contoso.Tool\u202eOther",
            p => p["Items"] = null, p => p["Items"]![0] = null,
            p => p["silentsoftwareinstall"] = true })
        {
            var (path, _) = Fixture(); var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            mutate(json); File.WriteAllText(path, json.ToJsonString());
            var effects = new Effects(); var consent = new Consent((_, _) => true);
            Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) != 0 && effects.Calls.Count == 0 && consent.Calls == 0,
                "Raw missing/null/duplicate/spoofed metadata was accepted");
        }
    }

    private static void RiskAndPresentation()
    {
        foreach (var risk in new bool?[] { null, false, true })
        {
            var (path, _) = Fixture(); var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (risk is not null) json["Items"]![0]!["RequiresRiskConfirmation"] = risk.Value;
            json["Approved"] = true; // A caller-supplied grant is deliberately meaningless.
            File.WriteAllText(path, json.ToJsonString()); var effects = new Effects();
            var consent = new Consent((received, _) =>
            {
                Check(received.RequiresRiskAcknowledgement, "JSON metadata suppressed risk acknowledgement");
                foreach (var language in new[] { "it", "en" })
                {
                    LocalizationService.Initialize(language);
                    var text = RunnerConsentPresentation.Describe(received);
                    Check(new[] { "Contoso.Tool", "2.0", "winget", "Upgrade", "WinGet" }.All(text.Contains), "Presentation omitted execution identity");
                }
                return false;
            });
            Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) != 0 && effects.Calls.Count == 0 && consent.Calls == 1,
                "Untrusted flags authorized execution");
        }
    }

    private static void DefensiveCopies()
    {
        var item = new UpdateItem { Id = "Contoso.Tool", Name = "Tool", Kind = UpdateKind.Software,
            Source = "winget", AvailableVersion = "2.0", ExpectedSignerSubjects = ["Original"], CompatibleHardwareIds = ["PCI\\VEN_1234"] };
        var copy = ElevatedUpdateRunner.ToPlanItem(item);
        item.ExpectedSignerSubjects[0] = "Changed"; item.CompatibleHardwareIds.Clear();
        Check(copy.ExpectedSignerSubjects.SequenceEqual(new[] { "Original" }) && copy.CompatibleHardwareIds.Count == 1,
            "ToPlanItem retained mutable collections");
        var frozen = new RunnerPlanItem(copy); copy.ExpectedSignerSubjects.Clear();
        var providerCopy = frozen.ToPlanItem(); providerCopy.ExpectedSignerSubjects.Clear(); providerCopy.AvailableVersion = "9.0";
        Check(frozen.ToPlanItem().ExpectedSignerSubjects.SequenceEqual(new[] { "Original" }) && frozen.AvailableVersion == "2.0",
            "Snapshot exposed its mutable values");
    }

    private static void WaitingStatus()
    {
        var (path, plan) = Fixture(); var effects = new Effects();
        var consent = new Consent((_, _) =>
        {
            var status = JsonStorage.Read<UpdateRunStatus>(plan.StatusFile)!;
            Check(status.State == "AwaitingConsent" && status.CurrentItemStartedUtc is null && !status.RestorePointRequested && effects.Calls.Count == 0,
                "Waiting status implies installation started");
            var now = DateTime.UtcNow; status.LastProgressUtc = now.AddHours(-5); status.LastHeartbeatUtc = now;
            var decision = UpdateWatchdogPolicy.Evaluate(status, now, UpdateWatchdogThresholds.Default);
            Check(!decision.ShouldTerminate && !decision.ShouldWarnProgress, "Human consent wait timed out as an installer");
            status.LastHeartbeatUtc = now.AddMinutes(-2);
            Check(UpdateWatchdogPolicy.Evaluate(status, now, UpdateWatchdogThresholds.Default).ShouldTerminate,
                "Consent wait disabled heartbeat supervision"); return false;
        });
        Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) != 0 && consent.Calls == 1, "Waiting status test did not reach gate");
    }

    private static void CancelBeforeLaunch()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var launches = 0;
        try { UpdateCoordinator.LaunchRunner(new ProcessStartInfo("never-start.exe"), cancelled.Token, _ => { launches++; return null; }); }
        catch (OperationCanceledException) { }
        Check(launches == 0, "Already cancelled request launched a process");
        UpdateCoordinator.LaunchRunner(new ProcessStartInfo("never-start.exe"), default, _ => { launches++; return null; });
        Check(launches == 1, "Live request failed to reach simulated launcher");
    }

    private static void CancelledResults()
    {
        var (path, plan) = Fixture(); var effects = new Effects();
        ElevatedUpdateRunner.Run(path, false, new Consent((_, _) => false), effects, default);
        var status = JsonStorage.Read<UpdateRunStatus>(plan.StatusFile)!;
        Check(status.State == "Cancelled" && status.Results.Count == 1 && !status.RestorePointCreated, "Cancellation lost batch state/results");
        var state = new ItemExecutionStateMachine(); state.BeginInitialAttempt(); var result = state.Complete(status.Results.Single());
        Check(result.TerminalDisposition == ItemTerminalDisposition.Cancelled && !result.Success && !result.InstallerSucceeded && !result.Verified,
            "Cancellation reported installation/failure instead of cancelled");
    }

    private static void EffectiveResults()
    {
        var (path, plan) = Fixture(); plan.Items[0].AvailableVersion = "9.0"; plan.Items[0].Source = "other-source";
        plan.Items.Add(Software("Contoso.Added")); JsonStorage.WriteAtomic(path, plan);
        var effects = new Effects(); var consent = new Consent((_, _) => true);
        Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) == 0, "Changed plan approval failed");
        var status = JsonStorage.Read<UpdateRunStatus>(plan.StatusFile)!;
        Check(status.ReceivedItems?.Count == 2 && status.Results[0].ReportedTarget?.AvailableVersion == "9.0" &&
            status.Results[0].ReportedTarget?.Source == "other-source" && status.Results[1].ReportedTarget?.Id == "Contoso.Added",
            "Results lost the effective targets, risking wrong recovery/history");
        var actual = UpdateCoordinator.ResolveReceivedItems([SoftwareItem()], status, false);
        Check(actual.Count == 2 && actual[0].Source == "other-source" && actual[0].AvailableVersion == "9.0",
            "Coordinator reconstructed the preliminary target for recovery");
        Check(UpdateCoordinator.EnsureCompletedBatchCardinality(actual, status).State == "Completed",
            "Independently approved batch results were discarded");
    }

    private static void BrokenStatusChannel()
    {
        var (path, plan) = Fixture(); var effects = new Effects();
        var consent = new Consent((_, _) =>
        {
            File.Delete(plan.StatusFile); Directory.CreateDirectory(plan.StatusFile); return true;
        });
        Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) == ElevatedUpdateRunner.StatusChannelFailureExitCode &&
            effects.Calls.Count == 0, "Unpublishable authorization transition reached installation effects");
    }

    private static void InvalidDriverMetadata()
    {
        foreach (var mutate in new Action<PlanItem>[] { p => p.WindowsUpdateId = null, p => p.WindowsUpdateRevision = -1,
            p => p.Id = "different", p => p.WindowsUpdateServerSelection = 4, p => p.WindowsUpdateServiceId = "other-service",
            p => p.DriverInstallMode = "Other", p => p.PackageOperation = PackageOperations.Install })
        {
            var (path, plan) = Fixture();
            var item = new PlanItem { Id = "c42aaae1-cb2a-469d-8be6-ec88f26f485d", WindowsUpdateId = "c42aaae1-cb2a-469d-8be6-ec88f26f485d",
                WindowsUpdateRevision = 1, Kind = "Driver", Name = "Driver", Source = "Windows Update", AvailableVersion = "1.2",
                DriverInstallMode = DriverInstallModes.WindowsUpdate };
            mutate(item); plan.Items = [item]; plan.CreateRestorePoint = true; JsonStorage.WriteAtomic(path, plan);
            var effects = new Effects(); var consent = new Consent((_, _) => true);
            Check(ElevatedUpdateRunner.Run(path, true, consent, effects, default) != 0 && consent.Calls == 0 && effects.Calls.Count == 0,
                "Invalid WUA identity/source/operation reached consent or effects");
        }
    }

    private static void InfrastructureResultTarget()
    {
        foreach (var differentId in new[] { false, true })
        {
            var target = Software(differentId ? "Contoso.Other" : "Contoso.Tool");
            target.AvailableVersion = "9.0"; target.Source = "other-source";
            var status = new UpdateRunStatus { Total = 1, State = "Failed", CurrentItemId = target.Id, ReceivedItems = [target] };
            var effective = UpdateCoordinator.ResolveReceivedItems([SoftwareItem()], status, false);
            var failed = UpdateCoordinator.BuildControlledBatchFailure(effective, status, "simulated process failure");
            var result = failed.Results.Single();
            Check(result.ReportedTarget?.Id == target.Id && result.ReportedTarget?.AvailableVersion == "9.0" &&
                result.ReportedTarget?.Source == "other-source", "Infrastructure result lost independently approved target");
        }
    }

    private static void FallbackCancellation()
    {
        foreach (var before in new[] { true, false })
        {
            using var cancellation = new CancellationTokenSource();
            if (before) cancellation.Cancel();
            var effects = new Effects();
            var consent = new Consent((_, _) => { cancellation.Cancel(); return true; });
            var result = ElevatedUpdateRunner.InstallSoftwareInteractive(SoftwareItem(), consent, effects, cancellation.Token);
            Check(result.Outcome == UpdateOutcomes.Cancelled && effects.Calls.Count == 0 && consent.Calls == (before ? 0 : 1),
                "Fallback ignored cancellation before/during consent");
        }
    }

    private static void ProjectionKeepsPreliminaryTarget()
    {
        var vm = new MainViewModel(null!); // No scan/network services are used by result projection.
        var original = SoftwareItem(); original.Status = "Waiting"; vm.Updates.Add(original);
        var changed = Software(); changed.AvailableVersion = "9.0"; changed.Source = "other-source";
        vm.ApplyRunResults([original], [new ItemRunResult { Id = original.Id, Kind = "Software", Outcome = UpdateOutcomes.ManualRequired,
            ReportedTarget = changed }], isFinal: true);
        Check(original.CanInstall && vm.Updates.Contains(original) && original.Status is not ("Waiting" or "Manual update" or "Aggiornamento manuale"),
            "Different target disabled/updated the preliminary proposal or left it waiting");
        vm.ApplyRunResults([original], [new ItemRunResult { Id = original.Id, Kind = "Software", Outcome = UpdateOutcomes.ManualRequired }], isFinal: true);
        Check(!original.CanInstall, "Legacy result projection stopped applying matching outcomes");
    }

    private static void ProjectionFinalizesUnreportedItems()
    {
        var vm = new MainViewModel(null!);
        var original = SoftwareItem(); original.Status = "Waiting";
        var notStarted = new UpdateItem { Id = "driver-not-started", Name = "Driver", Kind = UpdateKind.Driver, Status = "Waiting" };
        vm.ApplyRunResults([original, notStarted], [new ItemRunResult { Id = "Contoso.Other", Kind = "Software", Success = true,
            ReportedTarget = Software("Contoso.Other") }], isFinal: true);
        Check(original.Status != "Waiting" && notStarted.Status != "Waiting" && original.CanInstall && notStarted.CanInstall,
            "Unreported preliminary items stayed waiting after completion");
    }

    private static PlanItem Software(string id = "Contoso.Tool") => new()
    { Id = id, Name = "Tool", Kind = "Software", Source = "winget", InstalledVersion = "1.0", AvailableVersion = "2.0" };

    private static void ScopedBindingConsent()
    {
        var original = new WinGetScopeBinding("user", "ARP\\User\\X64\\Example_is1", "S-1-5-21-1000",
            "v1.29.290", new string('A', 64), "inno", "x64", "RemovesPreviousVersion");
        foreach (var approve in new[] { false, true })
        {
            var (path, plan) = Fixture(); plan.Items[0].WinGetBinding = original; JsonStorage.WriteAtomic(path, plan);
            var effects = new ScopedEffects(original);
            var consent = new Consent((snapshot, _) =>
            {
                Check(effects.Calls == 0, "Scoped effect before authoritative consent");
                var presented = RunnerConsentPresentation.Describe(snapshot);
                Check(presented.Contains(original.LocalId) && presented.Contains(original.AccountSid) &&
                    presented.Contains("user") && presented.Contains("inno") && presented.Contains("x64"), "Scoped consent omitted constraints");
                plan.Items[0].WinGetBinding = original with { Scope = "machine", LocalId = "ARP\\Machine\\X64\\Other" };
                JsonStorage.WriteAtomic(path, plan); // File mutation after display cannot alter the frozen tuple.
                var copy = snapshot.Items[0].ToPlanItem(); copy.WinGetBinding = null;
                Check(snapshot.Items[0].ToUpdateItem().WinGetBinding == original, "Snapshot lost immutable constraints");
                return approve;
            });
            var exit = ElevatedUpdateRunner.Run(path, false, consent, effects, default);
            Check((exit == 0) == approve && effects.Calls == (approve ? 1 : 0), "Scoped consent decline/mutation changed execution");
        }
        foreach (var field in new[] { "Scope", "LocalId", "AccountSid", "ClientVersion", "ManifestSha256", "InstallerType", "Architecture", "Safety" })
        {
            var (path, plan) = Fixture(); plan.Items[0].WinGetBinding = original; JsonStorage.WriteAtomic(path, plan);
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            json["Items"]![0]!["WinGetBinding"]!.AsObject().Remove(field);
            File.WriteAllText(path, json.ToJsonString());
            var effects = new ScopedEffects(original); var consent = new Consent((_, _) => true);
            Check(ElevatedUpdateRunner.Run(path, false, consent, effects, default) != 0 && effects.Calls == 0 && consent.Calls == 0,
                "Missing scoped schema field reached consent/effects: " + field);
        }
        var update = SoftwareItem(); update.WinGetBinding = original;
        var prepared = false;
        var recoveryConsent = new Consent((_, _) => true);
        var recovery = ElevatedUpdateRunner.PrepareRecovery(update, true, recoveryConsent, default, _ =>
        {
            prepared = true;
            // The legacy recovery provider can also match another scope's normalized
            // name (e.g. Tool (64-bit)). It must never receive a scoped identity.
            return new WinGetRecoveryPreparation(true, "unsafe broad provider");
        });
        Check(!prepared && recoveryConsent.Calls == 0, "Scoped target entered broad process-remediation recovery");
        var changed = ElevatedUpdateRunner.ToPlanItem(update); changed.WinGetBinding = original with { Architecture = "x86" };
        Check(!RunnerPlanItem.Matches(update, changed), "Result projection ignored changed scope constraints");
    }

    private sealed class ScopedEffects(WinGetScopeBinding expected) : IRunnerEffects
    {
        internal int Calls;
        public bool IsAdministrator => false;
        public (bool Created, string Message) CreateRestorePoint() => throw new InvalidOperationException("Unexpected restore point");
        public ItemRunResult InstallDriver(PlanItem item, Action<int, string> progress) => throw new InvalidOperationException("Unexpected driver");
        public ItemRunResult InstallSoftware(PlanItem item, bool silent, bool interactive)
        {
            Check(item.WinGetBinding == expected, "Execution differs from scoped consent"); Calls++;
            return new() { Id = item.Id, Name = item.Name, Kind = item.Kind, Success = true, InstallerSucceeded = true,
                Verified = true, VerificationStatus = UpdateVerificationStatuses.Verified };
        }
    }

    private static (string Path, UpdatePlan Plan) Fixture()
    {
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(AppPaths.DataDirectory, $"update-plan-{id}.json");
        var plan = new UpdatePlan { Items = [Software()], SilentSoftwareInstall = true,
            StatusFile = Path.Combine(AppPaths.DataDirectory, $"update-status-{id}.json"),
            PauseFile = Path.Combine(AppPaths.DataDirectory, $"update-pause-{id}.signal"), PauseOwnerProcessId = Environment.ProcessId };
        JsonStorage.WriteAtomic(path, plan); return (path, plan);
    }

    private sealed class Consent(Func<RunnerPlanSnapshot, bool, bool> confirm) : IRunnerConsent
    {
        public int Calls { get; private set; }
        public bool Confirm(RunnerPlanSnapshot plan, CancellationToken cancellationToken)
        { Calls++; return confirm(plan, plan.Interactive); }
    }

    private sealed class Effects : IRunnerEffects
    {
        public List<string> Calls { get; } = [];
        public bool Administrator { get; set; } = true;
        public bool IsAdministrator => Administrator;
        public (bool Created, string Message) CreateRestorePoint() { Calls.Add("restore"); return (true, "simulated"); }
        public ItemRunResult InstallSoftware(PlanItem item, bool silent, bool interactive)
        { Calls.Add($"{item.Id}|{item.AvailableVersion}|{item.Source}|{item.PackageOperation}|{silent}|{interactive}"); return Result(item); }
        public ItemRunResult InstallDriver(PlanItem item, Action<int, string> progress)
        { Calls.Add("driver|" + item.WindowsUpdateId); return Result(item); }
        private static ItemRunResult Result(PlanItem item) => new()
        { Id = item.Id, Name = item.Name, Kind = item.Kind, Success = true, InstallerSucceeded = true,
            Verified = true, VerificationStatus = UpdateVerificationStatuses.Verified };
    }

    private static void Check(bool condition, string message)
    { if (!condition) { _assertionFailure = new InvalidOperationException(message); throw _assertionFailure; } }
}
