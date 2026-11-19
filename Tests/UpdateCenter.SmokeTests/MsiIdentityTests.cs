using UpdateCenter.Models;
using UpdateCenter.Services;
using System.Text.Json;

internal static class MsiIdentityTests
{
    private const string Code = "{11111111-1111-4111-8111-111111111111}";
    private const string Package = "{22222222-2222-4222-8222-222222222222}";
    private const string Other = "{33333333-3333-4333-8333-333333333333}";
    private const string Sid = "S-1-5-21-111-222-333-1001";
    private const string Local = @"ARP\Machine\X86\" + Code;
    private static MsiRegistrationEvidence Proof() => new(Code, 4, "", "1.0.0.0", "1033", Package);

    internal static async Task RunAsync()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production root");
        var tests = new Func<Task>[] { SameProductUpgrade, WrongScopeOrCopy, AmbiguousOrUnavailableRegistration,
            NestedWindowsInstallerRequiresRegistration, ArchiveCannotBorrowBoundMsiRegistration,
            RegisteredMsiCannotBindExe, RegisteredMsiCannotDispatchExe,
            ChangedProductCodeStillNeedsTransition, LowerAndUnverifiableVersions, EvidenceDriftAfterConsent,
            ConsentContainsRegistration, LegacyCannotStripMsiProof, CancelledBeforeDispatch, PendingVerification };
        var failures = 0;
        foreach (var test in tests)
            try { await test(); Console.WriteLine("PASS MSI " + test.Method.Name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL MSI " + test.Method.Name + ": " + ex.Message); }
        Console.WriteLine($"MSI: {tests.Length - failures} PASS, {failures} FAIL; simulated dispatch only");
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    private static Task NestedWindowsInstallerRequiresRegistration()
    {
        // GameInput 4.1.314 is packaged as a ZIP containing a WiX MSI with a new ProductCode.
        foreach (var nestedType in new[] { "wix", "msi" })
        {
            var manifest = $$"""
                PackageIdentifier: Microsoft.GameInput
                PackageVersion: 4.1.314
                InstallerType: zip
                NestedInstallerType: {{nestedType}}
                Scope: machine
                ProductCode: '{AD3B7A9F-628D-4472-A9C2-5D2495CE8441}'
                ElevationRequirement: elevatesSelf
                Installers:
                - Architecture: x64
                  InstallerUrl: https://example.invalid/gameinput.nupkg
                  InstallerSha256: 3C5D5700A210DE289E9F02E2A1CB98848F32B981A5C22F7687A692F762A6CD26
                """;
            var assessment = WinGetScopeGuard.AssessRequirements(WinGetInstallerMetadata.Parse(manifest),
                "Microsoft.GameInput", "4.1.314", "x64", "machine");
            Check(!assessment.CanContinue && assessment.State == WinGetRequirementState.ManualRequired,
                "Packaging Windows Installer in a ZIP bypassed the registration and product transition guard.");
        }
        return Task.CompletedTask;
    }

    private static async Task RegisteredMsiCannotBindExe()
    {
        var f = new Fixture();
        f.Manifest = f.Manifest.Replace("InstallerType: wix", "InstallerType: exe");
        var item = f.Item();
        item.WinGetClientVersion = "v1.29.290";
        Check(await f.Guard().PrepareAsync(item, f.Manifest, default) is null,
            "Matching an ARP key granted an executable permission to update a registered MSI.");
    }

    private static async Task ArchiveCannotBorrowBoundMsiRegistration()
    {
        foreach (var declaration in new[] { "  NestedInstallerType: wix\n", "" })
        {
            var f = new Fixture();
            f.Manifest += "- Architecture: neutral\n  InstallerType: zip\n" + declaration +
                "  InstallerUrl: https://example.invalid/bundle.zip\n  InstallerSha256: " + new string('B', 64) + "\n";
            var item = f.Item();
            item.WinGetClientVersion = "v1.29.290";
            item.WinGetBinding = await f.Guard().PrepareAsync(item, f.Manifest, default);
            var assessment = WinGetScopeGuard.AssessRequirements(WinGetInstallerMetadata.Parse(f.Manifest),
                item.Id, item.AvailableVersion, "x64", "machine", item.WinGetBinding);
            Check(!assessment.CanContinue, "A competing archive inherited a direct installer's MSI registration proof.");
            var result = WinGetService.Upgrade(ElevatedUpdateRunner.ToPlanItem(item), true, f.Dispatch, f.Guard());
            Check(result.PreflightRejected && f.Dispatches == 0, "An archive competed with the bound direct MSI installer.");
        }
    }

    private static Task RegisteredMsiCannotDispatchExe()
    {
        var f = new Fixture();
        f.Manifest = f.Manifest.Replace("InstallerType: wix", "InstallerType: exe");
        var item = f.Item();
        item.WinGetClientVersion = "v1.29.290";
        var result = WinGetService.Upgrade(ElevatedUpdateRunner.ToPlanItem(item), true, f.Dispatch, f.Guard());
        Check(result.PreflightRejected && f.Dispatches == 0,
            "An unbound executable reached dispatch for a registered MSI installation.");
        return Task.CompletedTask;
    }

    private static async Task SameProductUpgrade()
    {
        var f = new Fixture(); var plan = await f.Prepare();
        Check(plan.WinGetBinding?.CopyEvidence?.MsiRegistration == Proof(), "Native proof not frozen");
        var result = WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard());
        Check(result.Success && f.Dispatches == 1 && f.Arguments.Contains("--scope") && f.Arguments.Contains("machine") &&
            f.Arguments.Contains("--installer-type") && f.Arguments.Contains("wix"), "Valid existing same-product path changed");
    }

    private static async Task WrongScopeOrCopy()
    {
        foreach (var evidence in new[] { Proof() with { Context = 2, UserSid = Sid }, Proof() with { ProductCode = Other } })
        {
            var f = new Fixture { Evidence = evidence };
            Check(await f.Guard().PrepareAsync(f.Item(), f.Manifest, default) is null, "Wrong scope/copy bound");
            Check(MsiRegistrationReader.Resolve(Local, "1.0.0.0", Sid, [evidence]) is null, "ARP overrode native scope/copy");
        }
        var user = Proof() with { Context = 2, UserSid = Sid };
        Check(MsiRegistrationReader.Resolve(Local.Replace("Machine", "User"), "1.0.0.0", Sid, [user]) == user, "Valid user context rejected");
        Check(MsiRegistrationReader.Resolve(Local.Replace("Machine", "User"), "1.0.0.0", Sid + "9", [user]) is null, "Other user accepted");
    }

    private static Task AmbiguousOrUnavailableRegistration()
    {
        foreach (var rows in new MsiRegistrationEvidence[]?[] { null, [], [Proof(), Proof()], [Proof() with { Context = 0 }],
            [Proof() with { Language = "" }], [Proof() with { PackageCode = "unknown" }] })
            Check(MsiRegistrationReader.Resolve(Local, "1.0.0.0", Sid, rows) is null, "Unknown/ambiguous native registration accepted");
        return Task.CompletedTask;
    }

    private static async Task ChangedProductCodeStillNeedsTransition()
    {
        var f = new Fixture(); f.Manifest = f.Manifest.Replace("ProductCode: '" + Code, "ProductCode: '" + Other);
        // A valid shared UpgradeCode is a relation, not proof of the authored
        // removal set, context, launch conditions or a frozen execution payload.
        f.Manifest = f.Manifest.Replace("Installers:", "AppsAndFeaturesEntries:\n- UpgradeCode: '{44444444-4444-4444-8444-444444444444}'\nInstallers:");
        Check(await f.Guard().PrepareAsync(f.Item(), f.Manifest, default) is null, "UpgradeCode alone authorized ProductCode transition");
        var result = WinGetService.Upgrade(ElevatedUpdateRunner.ToPlanItem(f.Item()), true, f.Dispatch, f.Guard());
        Check(result.PreflightRejected && f.Dispatches == 0, "Unimplemented transition dispatched via legacy");
    }

    private static async Task LowerAndUnverifiableVersions()
    {
        foreach (var raw in new[] { "0.9", "unknown", "1.0-beta", "1.0.0.1" })
            Check(MsiRegistrationReader.Resolve(Local, raw, Sid, [Proof()]) is null, "Different raw version was treated as the same observation");
        var f = new Fixture(); var plan = await f.Prepare(); plan.AvailableVersion = "0.9";
        Check(WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard()).PreflightRejected && f.Dispatches == 0, "Downgrade dispatched");
    }

    private static async Task EvidenceDriftAfterConsent()
    {
        foreach (var changed in new[] { Proof() with { Context = 2, UserSid = Sid }, Proof() with { PackageCode = Other },
            Proof() with { Language = "1040" }, Proof() with { ProductVersion = "1.0.0.1" } })
        {
            var f = new Fixture(); var plan = await f.Prepare(); f.Evidence = changed;
            Check(WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard()).PreflightRejected && f.Dispatches == 0, "Changed MSI evidence dispatched");
        }
    }

    private static async Task ConsentContainsRegistration()
    {
        var f = new Fixture(); var plan = await f.Prepare();
        RunnerPlanSnapshot Snapshot() => RunnerPlanSnapshot.Create(new UpdatePlan { Items = [plan] }, false);
        var original = Snapshot(); var gate = new UnifiedConsentGate([original]);
        Check(gate.Confirm(_ => true, () => { }, () => true), "Consent fixture");
        plan.WinGetBinding = plan.WinGetBinding! with { CopyEvidence = plan.WinGetBinding.CopyEvidence! with { MsiRegistration = Proof() with { PackageCode = Other } } };
        var rejected = false; try { gate.Consume(0, Snapshot()); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && original.Items[0].ToPlanItem().WinGetBinding!.CopyEvidence!.MsiRegistration == Proof(), "MSI proof escaped frozen consent");
        foreach (var language in new[] { "it", "en" }) { LocalizationService.Initialize(language); Check(RunnerConsentPresentation.Describe(original).Contains(Code), "MSI identity omitted from consent"); }
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var path = Path.Combine(AppPaths.DataDirectory, "msi-schema-" + Guid.NewGuid().ToString("N") + ".json");
        var serialized = JsonSerializer.Serialize(new UpdatePlan { Items = [original.Items[0].ToPlanItem()] });
        File.WriteAllText(path, serialized.Replace("\"Context\":4", "\"Context\":4,\"context\":2", StringComparison.Ordinal));
        rejected = false; try { RunnerPlanSnapshot.Read(path, false); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Duplicate MSI context in JSON accepted");
    }

    private static async Task LegacyCannotStripMsiProof()
    {
        var f = new Fixture(); var plan = await f.Prepare();
        plan.WinGetBinding = plan.WinGetBinding! with { CopyEvidence = plan.WinGetBinding.CopyEvidence! with { MsiRegistration = null } };
        Check(WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard()).PreflightRejected && f.Dispatches == 0, "Stripped MSI proof accepted");
        plan.WinGetBinding = null;
        Check(WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard()).PreflightRejected && f.Dispatches == 0, "Legacy MSI bypass");
    }

    private static async Task CancelledBeforeDispatch()
    {
        var f = new Fixture(); var plan = await f.Prepare(); using var token = new CancellationTokenSource(); token.Cancel();
        var cancelled = false; try { WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard(), token.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && f.Dispatches == 0, "Cancellation dispatched");
    }

    private static async Task PendingVerification()
    {
        var f = new Fixture(); var plan = await f.Prepare();
        Check(!(await f.Guard().VerifyAsync(plan)).Verified, "Old target declared verified");
        f.Version = "2.0"; f.Evidence = Proof() with { ProductVersion = "2.0" };
        Check((await f.Guard().VerifyAsync(plan)).Verified, "Same-copy exact target not verified");
        f.CopyUnavailable = true;
        Check(!(await f.Guard().VerifyAsync(plan)).Verified, "Missing native MSI proof was promoted to verified");
        f.CopyUnavailable = false; f.Evidence = f.Evidence with { Context = 2, UserSid = Sid };
        Check(!(await f.Guard().VerifyAsync(plan)).Verified, "Changed native MSI context was promoted to verified");
        f.Evidence = Proof() with { ProductVersion = "2.0", Language = "1040" };
        Check(!(await f.Guard().VerifyAsync(plan)).Verified, "Changed native MSI language was promoted to verified");
    }

    private sealed class Fixture
    {
        internal MsiRegistrationEvidence Evidence = Proof();
        internal string Version = "1.0.0.0";
        internal int Dispatches;
        internal bool CopyUnavailable;
        internal IReadOnlyList<string> Arguments = [];
        internal string Manifest = $"PackageIdentifier: Fixture.Msi\nPackageVersion: 2.0\nInstallerType: wix\nScope: machine\nProductCode: '{Code}'\nUpgradeBehavior: install\nInstallers:\n- Architecture: x86\n  InstallerUrl: https://example.org/target.msi\n  InstallerSha256: {new string('A',64)}\n";
        internal UpdateItem Item() => new() { Id = "Fixture.Msi", Name = "Fixture", Source = "winget", Kind = UpdateKind.Software, InstalledVersion = "1.0.0.0", AvailableVersion = "2.0" };
        internal WinGetScopeGuard Guard() => new((args, token, _) => { token.ThrowIfCancellationRequested(); return Task.FromResult(new ProcessResult(0,
            args[0] == "--version" ? "v1.29.290" : $"Fixture [Fixture.Msi]\nVersion: {Version}\nLocal Identifier: {Local}\nInstalled Scope: machine\nInstalled Architecture: x86\n", "")); },
            (_, _, _) => Task.FromResult<string?>(Manifest), () => Sid, (_, _) => "machine", "x64", isAdministrator: () => false,
            readDisplayVersion: _ => Version, readCopyEvidence: _ => CopyUnavailable ? null : new WinGetCopyEvidence(Version, MsiRegistration: Evidence));
        internal async Task<PlanItem> Prepare() { var item = Item(); item.WinGetBinding = await Guard().PrepareAsync(item, Manifest, default); Check(item.WinGetBinding is not null, "Fixture did not bind"); return ElevatedUpdateRunner.ToPlanItem(item); }
        internal Task<ProcessResult> Dispatch(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout) { Dispatches++; Arguments = args; return Task.FromResult(new ProcessResult(0, "simulated", "")); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    internal static void NativeReadOnly()
    {
        if (AppPaths.DataDirectory == AppPaths.ResolveDefaultRoot()) throw new InvalidOperationException("Production root");
        var copy = WinGetCopyEvidenceReader.Read(@"ARP\Machine\X86\{69012533-AE90-48A1-B7E5-7124E81C4B40}");
        Console.WriteLine($"Native ARP machine / MSI user contradiction rejected: {copy is null}; installations=0");
        var machine = WinGetCopyEvidenceReader.Read(@"ARP\Machine\X86\{1F3BC6BD-2010-40E7-85B2-F1A5E2EB5FF2}");
        var machineValid = machine?.MsiRegistration is { Context: 4, ProductVersion: "3.13.150.0", Language: "1033" };
        Console.WriteLine($"Native coherent machine registration retained: {machineValid}; installations=0");
        var blocked = 0;
        foreach (var candidate in new[] {
            new PlanItem { Id = "OpenMedia.4KVideoDownloader", Name = "4K Video Downloader", Source = "winget", Kind = "Software", InstalledVersion = "4.29.0.5640", AvailableVersion = "4.33.5.0172" },
            new PlanItem { Id = "Python.Launcher", Name = "Python Launcher", Source = "winget", Kind = "Software", InstalledVersion = "3.13.5", AvailableVersion = "3.15.2" } })
        {
            Task<ProcessResult> Read(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
            {
                Check(args[0] == "--version" || args[0] == "list" && args.Contains(candidate.Id) && args.Contains("--exact") && args.Contains("--source"), "Non-read-only native command");
                return ProcessRunner.RunAsync("winget.exe", args, token, timeout);
            }
            var refusal = WinGetScopeGuard.Native(Read).ValidateAsync(candidate).GetAwaiter().GetResult();
            if (refusal is not null) blocked++;
            Console.WriteLine($"REAL READONLY PREFLIGHT {candidate.Id}: admitted={refusal is null}; reason={refusal?.StandardError}; installations=0");
        }
        Environment.ExitCode = copy is null && machineValid && blocked == 2 ? 0 : 1;
    }
}
