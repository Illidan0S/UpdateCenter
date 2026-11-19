using System.Text.Json;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class ScopeRecipeTests
{
    private const string Sid = "S-1-5-21-111-222-333-1001";
    internal static async Task RunAsync()
    {
        var failed = 0;
        var tests = new Func<Task>[] { ExactCopyAndContext, WrongCopyOrScope, AmbiguousRecipe,
            TechnologyCannotChange, FrozenConsent, DriftBeforeDispatch, DowngradeAndOpaqueVersion,
            BlockedWithoutDispatch, CancelledBeforeDispatch, PostInstallContinuity, PortableDependenciesStayWithPortable,
            UnboundRemovalRequiresUnchangedConsent };
        foreach (var test in tests)
        {
            try { await test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Method.Name + ": " + ex.Message); }
        }
        Console.WriteLine($"Scope recipes: {tests.Length - failed} PASS, {failed} FAIL; external boundaries simulated; installations=0");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    private static async Task ExactCopyAndContext()
    {
        foreach (var scope in new[] { "machine", "user" })
        {
            var f = new Fixture(scope); var plan = await f.Prepare();
            Check(plan.WinGetBinding!.Scope == scope && plan.WinGetBinding.LocalId == f.Local &&
                plan.WinGetBinding.InstallerType == "nullsoft" && plan.WinGetBinding.Architecture == "x64", "Wrong recipe bound");
            var result = WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard());
            Check(result.Success && f.Commands.Count == 1, "Exact copy not dispatched in fixture");
            var args = f.Commands.Single();
            foreach (var pair in new[] { ("--id", "Fixture.Tool"), ("--source", "winget"), ("--version", "2.0"),
                ("--scope", scope), ("--installer-type", "nullsoft"), ("--architecture", "x64") })
                Check(args[Array.IndexOf(args, pair.Item1) + 1] == pair.Item2, "Dispatch constraint missing: " + pair.Item1);
            Check(!args.Contains("--override") && !args.Contains("--custom"), "Installer scope forced by custom switches");
        }
    }

    private static async Task UnboundRemovalRequiresUnchangedConsent()
    {
        var f = new Fixture();
        f.Manifest = "PackageIdentifier: Fixture.Tool\nPackageVersion: 2.0\nInstallers:\n" +
            f.Recipe.Replace("ProductCode: Fixture", "ProductCode: NewBundle").Replace("nullsoft", "burn");
        var ordinary = await f.Scan();
        ordinary.WinGetClientVersion = WinGetScopeGuard.ReviewedClient;
        var oldPlan = ElevatedUpdateRunner.ToPlanItem(ordinary);
        Check(ordinary.CanInstall && ordinary.WinGetBinding is null && !ordinary.RequiresRiskConfirmation &&
            await f.Guard().ValidateAsync(oldPlan) is null, "Fixture must begin with an eligible ordinary upgrade.");

        f.Manifest = f.Manifest.Replace("UpgradeBehavior: install", "UpgradeBehavior: uninstallPrevious");
        Check(WinGetService.Upgrade(oldPlan, true, f.Dispatch, f.Guard()).PreflightRejected && f.Commands.Count == 0,
            "An ordinary plan dispatched an installer that now removes the previous version.");
        var risk = await f.Scan();
        risk.WinGetClientVersion = WinGetScopeGuard.ReviewedClient;
        Check(!risk.CanInstall && !risk.IsSelected && risk.RemovalManifestSha256.Length == 0,
            "A removal with no demonstrated bundle transition was offered.");
        var handEdited = ElevatedUpdateRunner.ToPlanItem(risk);
        handEdited.RemovalManifestSha256 = WinGetScopeGuard.Digest(f.Manifest);
        Check(WinGetService.ValidateTarget(handEdited) is not null &&
            WinGetService.Install(handEdited, true, f.Dispatch).PreflightRejected &&
            WinGetService.Upgrade(handEdited, true, f.Dispatch, f.Guard()).PreflightRejected &&
            WinGetService.RunInteractive(handEdited, f.Dispatch).PreflightRejected && f.Commands.Count == 0,
            "A manifest digest alone authorized a removal or a fresh installation.");
    }

    private static async Task PortableDependenciesStayWithPortable()
    {
        var f = new Fixture();
        var first = f.Manifest.IndexOf("- Architecture: x64\n  Scope: machine\n  InstallerType: wix", StringComparison.Ordinal);
        var zip = f.Manifest.IndexOf("- Architecture: x64\n  InstallerType: zip", StringComparison.Ordinal);
        f.Manifest = f.Manifest.Remove(first, zip - first)
            .Replace("  ElevationRequirement: elevatesSelf\n", "")
            .Replace("  NestedInstallerType: portable", "  NestedInstallerType: portable\n  Dependencies:\n    PackageDependencies:\n    - PackageIdentifier: Fixture.Runtime");
        var item = await f.Scan();
        Check(item.CanInstall && !item.IsSelected && item.HasUnverifiedInstallerMetadata &&
            item.WinGetBinding is { InstallerType: "nullsoft", DependencySnapshot: "", ElevationRequirement: "" },
            "ZIP dependencies blocked the EXE, or the unknown elevation warning was lost");
        var plan = ElevatedUpdateRunner.ToPlanItem(item);
        var snapshot = RunnerPlanSnapshot.Create(new UpdatePlan { Items = [plan] }, false);
        LocalizationService.Initialize("en");
        Check(RunnerConsentPresentation.Describe(snapshot).Contains("does not declare the privileges"),
            "Consent omitted the unknown elevation requirement");
        Check(WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard()).Success &&
            f.Commands.Single().Contains("nullsoft") && !f.Commands[0].Contains("--skip-dependencies"),
            "The dependency-free EXE did not use the approved arguments");
        f.Commands.Clear();
        plan.WinGetBinding = null;
        Check(WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard()).PreflightRejected &&
            f.Commands.Count == 0, "Stripped EXE binding enabled legacy dispatch");
        f.Manifest = f.Manifest.Replace("Installers:", "Dependencies:\n  WindowsFeatures:\n  - FixtureFeature\nInstallers:");
        Check(!(await f.Scan()).CanInstall && f.Commands.Count == 0, "Common dependency effects were ignored");
    }

    private static async Task WrongCopyOrScope()
    {
        foreach (var change in new Action<Fixture>[] { f => f.Key = "Other", f => f.ObservedScope = "user",
            f => f.Arch = "", f => f.Arch = "arm64", f => f.Copy = null })
        { var f = new Fixture(); change(f); Check(await f.Binding() is null, "Wrong or unproven copy admitted"); }
    }

    private static async Task AmbiguousRecipe()
    {
        var f = new Fixture(); f.Manifest += "\n" + f.Recipe;
        Check(await f.Binding() is null, "Duplicate dispatch recipe accepted");
        f = new(); f.Manifest += "\n" + f.Recipe.Replace("Scope: machine", "Scope: user");
        Check(await f.Binding() is null, "Identical payload relabelled by scope accepted");
        f = new(); f.Manifest = f.Manifest.Replace("InstallerType: zip", "InstallerType: nullsoft").Replace("NestedInstallerType: portable", "Scope: machine");
        Check(await f.Binding() is null, "Unbound same-type installer could be selected");
        f = new(); f.Manifest = f.Manifest.Replace("NestedInstallerType: portable", "NestedInstallerType: nullsoft\n  Scope: machine");
        Check(await f.Binding() is null, "ZIP with matching effective installer type could be selected");
        f = new(); f.Manifest += "\n" + f.Recipe.Replace("Architecture: x64", "Architecture: neutral");
        Check(await f.Binding() is null, "Neutral recipe could be selected instead of the bound architecture");
    }

    private static async Task TechnologyCannotChange()
    {
        foreach (var type in new[] { "msi", "msix", "", "unknown" })
        { var f = new Fixture { Category = type }; Check(await f.Binding() is null, "Unproven technology accepted"); }
        var g = new Fixture(); g.Manifest = g.Manifest.Replace("InstallerType: nullsoft", "InstallerType: wix");
        Check(await g.Binding() is null, "EXE to MSI transition accepted");
    }

    private static async Task FrozenConsent()
    {
        var f = new Fixture(); var plan = await f.Prepare();
        RunnerPlanSnapshot Freeze() => RunnerPlanSnapshot.Create(new UpdatePlan { Items = [plan] }, false);
        var original = Freeze(); var gate = new UnifiedConsentGate([original]);
        Check(gate.Confirm(_ => true, () => { }, () => true), "Fixture consent failed");
        plan.WinGetBinding = plan.WinGetBinding! with { Scope = "user", LocalId = @"ARP\User\X64\Fixture" };
        var rejected = false; try { gate.Consume(0, Freeze()); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && original.Items[0].ToPlanItem().WinGetBinding!.Scope == "machine", "Consent target was mutable");
        plan = original.Items[0].ToPlanItem();
        var categoryGate = new UnifiedConsentGate([original]);
        Check(categoryGate.Confirm(_ => true, () => { }, () => true), "Category consent fixture failed");
        plan.WinGetBinding = plan.WinGetBinding! with { InstalledType = "" };
        rejected = false; try { categoryGate.Consume(0, Freeze()); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && original.Items[0].ToPlanItem().WinGetBinding!.InstalledType == "exe", "Category escaped consent");
        Check(WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard()).PreflightRejected && f.Commands.Count == 0, "Stripped category dispatched");
        plan = original.Items[0].ToPlanItem();
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var path = Path.Combine(AppPaths.DataDirectory, "scope-plan.json");
        var json = JsonSerializer.Serialize(new UpdatePlan { Items = [plan] });
        File.WriteAllText(path, json);
        Check(RunnerPlanSnapshot.Read(path, false).Items[0].ToPlanItem().WinGetBinding == plan.WinGetBinding, "Category lost on JSON roundtrip");
        File.WriteAllText(path, json.Replace("\"InstalledType\":\"exe\"", "\"InstalledType\":\"exe\",\"installedType\":\"msi\""));
        rejected = false; try { RunnerPlanSnapshot.Read(path, false); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Duplicate category accepted");
        foreach (var language in new[] { "it", "en" }) { LocalizationService.Initialize(language);
            Check(RunnerConsentPresentation.Describe(original).Contains("exe"), "Category missing from consent presentation"); }
    }

    private static async Task DriftBeforeDispatch()
    {
        foreach (var change in new Action<Fixture>[] { f => f.ObservedScope = "user", f => f.Version = "1.1",
            f => f.Key = "Other", f => f.Category = "msi", f => f.Arch = "x86", f => f.Account = Sid + "0",
            f => f.Manifest = f.Manifest.Replace(new string('A', 64), new string('D', 64)), f => f.Copy = null })
        {
            var f = new Fixture(); var plan = await f.Prepare(); change(f);
            Check(WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard()).PreflightRejected && f.Commands.Count == 0,
                "Drift dispatched");
        }
    }

    private static async Task DowngradeAndOpaqueVersion()
    {
        foreach (var version in new[] { "0.9", "1.0", "unknown", "2.0-beta" })
        { var f = new Fixture { Target = version }; f.Manifest = f.Manifest.Replace("PackageVersion: 2.0", "PackageVersion: " + version);
            Check(await f.Binding() is null, "Non-increasing/noncomparable target admitted"); }
    }

    private static async Task BlockedWithoutDispatch()
    {
        var f = new Fixture(); var plan = await f.Prepare(); plan.WinGetBinding = null;
        Check(WinGetService.Upgrade(plan, true, f.Dispatch, f.Guard()).PreflightRejected && f.Commands.Count == 0, "Stripped binding dispatched");
        foreach (var type in new[] { "inno", "nullsoft", "exe" })
        {
            f = new(); f.Manifest = f.Manifest.Replace("Scope: machine", "Scope: user").Replace("InstallerType: nullsoft", "InstallerType: " + type);
            Check(await f.Binding() is null, "Opposite-scope recipe accepted");
            Check(WinGetService.Upgrade(ElevatedUpdateRunner.ToPlanItem(f.Item()), true, f.Dispatch, f.Guard()).PreflightRejected && f.Commands.Count == 0,
                "Opposite-scope recipe dispatched");
        }
        f = new(); f.Manifest = f.Manifest.Replace("Installers:", "Dependencies:\n  PackageDependencies:\n  - PackageIdentifier: Fixture.Dependency\nInstallers:");
        Check(await f.Binding() is null, "Recovery admitted dependency effects");
        Check(WinGetService.Upgrade(ElevatedUpdateRunner.ToPlanItem(f.Item()), true, f.Dispatch, f.Guard()).PreflightRejected && f.Commands.Count == 0,
            "Dependency-bearing recovery dispatched");
    }

    private static async Task CancelledBeforeDispatch()
    {
        var f = new Fixture(); var plan = await f.Prepare(); using var c = new CancellationTokenSource(); c.Cancel();
        try { await f.Guard().ValidateAsync(plan, c.Token); throw new InvalidOperationException("Cancellation lost"); }
        catch (OperationCanceledException) when (c.IsCancellationRequested) { }
        Check(f.Commands.Count == 0, "Cancelled dispatch");
    }

    private static async Task PostInstallContinuity()
    {
        var f = new Fixture(); var plan = await f.Prepare();
        Check(!(await f.Guard().VerifyAsync(plan)).Verified, "Old copy reported updated");
        f.Version = "2.0"; f.Copy = new("2.0");
        Check((await f.Guard().VerifyAsync(plan)).Verified, "Exact target not verified");
        foreach (var change in new Action<Fixture>[] { x => x.Category = "msi", x => x.ObservedScope = "user", x => x.Key = "Other", x => x.Copy = null })
        {
            f = new(); plan = await f.Prepare(); f.Version = "2.0"; f.Copy = new("2.0"); change(f);
            Check(!(await f.Guard().VerifyAsync(plan)).Verified, "Changed installation promoted to verified");
        }
    }

    private sealed class Fixture
    {
        internal string ObservedScope, Key = "Fixture", Arch = "x64", Category = "exe", Version = "1.0", Target = "2.0", Account = Sid;
        internal WinGetCopyEvidence? Copy = new("1.0");
        internal string Local => $"ARP\\{(ObservedScope == "user" ? "User" : "Machine")}\\X64\\{Key}";
        internal string Recipe, Manifest;
        internal List<string[]> Commands = [];
        internal Fixture(string scope = "machine")
        {
            ObservedScope = scope;
            Recipe = $"- Architecture: x64\n  Scope: {scope}\n  InstallerType: nullsoft\n  ProductCode: Fixture\n  ElevationRequirement: elevatesSelf\n  UpgradeBehavior: install\n  InstallerUrl: https://example.invalid/tool.exe\n  InstallerSha256: {new string('A', 64)}";
            Manifest = $"PackageIdentifier: Fixture.Tool\nPackageVersion: 2.0\nInstallers:\n{Recipe}\n- Architecture: x64\n  Scope: {scope}\n  InstallerType: wix\n  ProductCode: OtherMSI\n  InstallerUrl: https://example.invalid/tool.msi\n  InstallerSha256: {new string('B', 64)}\n- Architecture: x64\n  InstallerType: zip\n  NestedInstallerType: portable\n  InstallerUrl: https://example.invalid/tool.zip\n  InstallerSha256: {new string('C', 64)}";
        }
        internal UpdateItem Item() => new() { Id = "Fixture.Tool", Name = "Fixture", Kind = UpdateKind.Software, Source = "winget", InstalledVersion = "1.0", AvailableVersion = Target };
        internal WinGetScopeGuard Guard() => new(Read, (_, _, _) => Task.FromResult<string?>(Manifest), () => Account,
            (_, _) => ObservedScope, "x64", isAdministrator: () => false, readDisplayVersion: _ => Copy?.DisplayVersion, readCopyEvidence: _ => Copy);
        internal Task<WinGetScopeBinding?> Binding() => Guard().PrepareAsync(Item(), Manifest, default);
        internal async Task<UpdateItem> Scan()
        {
            var item = Item();
            var safety = new WinGetManifestSafetyService((_, _, _) => Task.FromResult(new WinGetManifestSafetyService.ManifestInspection(
                WinGetManifestSafetyService.ParseUpgradeSafety(Manifest), 0, WinGetManifestSafetyService.ParseInstallerScope(Manifest), Manifest)),
                _ => Guard(), (_, _) => ObservedScope, "x64", () => false);
            await safety.ApplyAsync([item], default, Read);
            return item;
        }
        internal async Task<PlanItem> Prepare() { var item = Item(); item.WinGetBinding = await Binding(); Check(item.WinGetBinding is not null, "Exact recipe recovery unavailable"); return ElevatedUpdateRunner.ToPlanItem(item); }
        private Task<ProcessResult> Read(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new ProcessResult(0, args[0] == "--version" ? WinGetScopeGuard.ReviewedClient :
            $"Fixture [Fixture.Tool]\nVersion: {Version}\nLocal Identifier: {Local}\nInstalled Scope: {ObservedScope}\nInstalled Architecture: {Arch}\nInstaller Type: {Category}\n", "")); }
        internal Task<ProcessResult> Dispatch(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout) { token.ThrowIfCancellationRequested(); Commands.Add(args.ToArray()); return Task.FromResult(new ProcessResult(0, "fixture only", "")); }
    }

    internal static async Task NativeAsync(bool obsOnly = false, bool microsoftOnly = false)
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production root selected");
        var rows = new List<object>();
        var recovered = 0;
        var cases = microsoftOnly ? new[] { ("Microsoft.VCRedist.2015+.x64", "14.52.37029.0"),
            ("Microsoft.VCRedist.2015+.x86", "14.52.37029.0"), ("Microsoft.DotNet.DesktopRuntime.8", "8.0.34") } :
            obsOnly ? new[] { ("OBSProject.OBSStudio", "33.0.0") } :
            new[] { ("Notepad++.Notepad++", "8.9.9"), ("VideoLAN.VLC", "3.0.24") };
        foreach (var (id, target) in cases)
        {
            async Task<ProcessResult> Read(IReadOnlyList<string> args, CancellationToken ct, TimeSpan timeout)
            {
                Check(args.SequenceEqual(new[] { "--version" }) || args[0] == "list" && args.Contains(id) && args.Contains("--exact") && args.Contains("--source") && args.Contains("--details"), "Non-read-only command");
                return await WinGetService.ExecuteAsync(args, ct, timeout);
            }
            var details = await Read(["list", "--id", id, "--exact", "--source", "winget", "--details", "--disable-interactivity", "--nowarn"], default, TimeSpan.FromSeconds(45));
            var identity = WinGetScopeGuard.ParseIdentity(details.StandardOutput, id); Check(identity is not null, "Identity unavailable");
            var item = new UpdateItem { Id = id, Name = identity!.Name, Source = "winget", Kind = UpdateKind.Software, InstalledVersion = identity.Version, AvailableVersion = target };
            item.WinGetClientVersion = (await Read(["--version"], default, TimeSpan.FromSeconds(15))).StandardOutput.Trim();
            var manifest = await WinGetManifestSafetyService.ReadManifestAsync(id, target, default); Check(manifest is not null, "Manifest unavailable");
            var guard = WinGetScopeGuard.Native(Read);
            var safety = new WinGetManifestSafetyService((_, _, _) => Task.FromResult(new WinGetManifestSafetyService.ManifestInspection(
                WinGetManifestSafetyService.ParseUpgradeSafety(manifest!, "x64"), 0, WinGetManifestSafetyService.ParseInstallerScope(manifest!), manifest!)));
            await safety.ApplyAsync([item], default, Read);
            var refusal = await guard.ValidateAsync(ElevatedUpdateRunner.ToPlanItem(item));
            var admitted = item.CanInstall && refusal is null;
            if (admitted) recovered++;
            rows.Add(new { id, target, item.InstalledVersion, item.CanInstall, item.IsSelected, item.HasUnverifiedInstallerMetadata,
                Admitted = admitted, item.ResultDetails, item.WinGetBinding, Installations = 0 });
            Console.WriteLine($"REAL READONLY {id}: selectable={item.CanInstall}; preflight={admitted}; installations=0; {refusal?.StandardError}");
        }
        var output = microsoftOnly ? "_output/winget130-20261111" : obsOnly ? "_output/obs-recipe-20261111" : "_output/scope-coverage-20261110";
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "targeted-preflight.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        Check(recovered == cases.Length, "All targeted native preflights must be positive");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
