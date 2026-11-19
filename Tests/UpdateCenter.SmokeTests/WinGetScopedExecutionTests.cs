using System.Reflection;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class WinGetScopedExecutionTests
{
    internal static void Run()
    {
        ScopedRecipeRecovery().GetAwaiter().GetResult();
        ScopedExecutionAndDrift().GetAwaiter().GetResult();
        UnsupportedManifestDoesNotDowngrade().GetAwaiter().GetResult();
        UnsupportedYamlCannotHideRisk();
        PreviouslyUnknownArchitectureCanBeReportedAfterUpgrade().GetAwaiter().GetResult();
        Console.WriteLine("PASS B2 scoped recipe recovery: exact metadata, native identity and bound scope.");
    }

    private static void UnsupportedYamlCannotHideRisk()
    {
        foreach (var value in new[] { "  \"UpgradeBehavior\": uninstallPrevious", "  UpgradeBehavior: \"uninstall\\u0050revious\"" })
        {
            var fixture = "PackageIdentifier: Example.Tool\nPackageVersion: 2.0\nUpgradeBehavior: install\nInstallers:\n- Architecture: x64\n" + value;
            Check(WinGetInstallerMetadata.Parse(fixture) is null, "Unsupported YAML concealed a removal override.");
        }
        var spaced = WinGetInstallerMetadata.Parse("UpgradeBehavior: install\nInstallers:\n- Architecture: x64\n  UpgradeBehavior : uninstallPrevious\n");
        Check(spaced?.Installers.Single().Behavior == "uninstallPrevious", "Whitespace before colon hid an override.");
        foreach (var unsupported in new[] { "  ? UpgradeBehavior\n  : uninstallPrevious", "  <<: *defaults" })
            Check(WinGetInstallerMetadata.Parse("Installers:\n- Architecture: x64\n" + unsupported) is null, "Complex/merged key accepted.");
    }

    private static async Task PreviouslyUnknownArchitectureCanBeReportedAfterUpgrade()
    {
        var f = new GuardFixture { Arch = "" };
        var plan = await f.Plan();
        f.Version = plan.AvailableVersion; f.Arch = "x64";
        Check((await f.Guard.VerifyAsync(plan)).Verified,
            "New WinGet tracking metadata invalidated an architecture that was never promised as a constraint.");
    }

    private static async Task ScopedExecutionAndDrift()
    {
        foreach (var interactive in new[] { false, true })
        {
            var f = new GuardFixture();
            var plan = await f.Plan();
            var result = interactive ? WinGetService.RunInteractive(plan, f.Execute, f.Guard) :
                WinGetService.Upgrade(plan, true, f.Execute, f.Guard);
            Check(result.Success && f.Effects.Count == 1, "Validated scoped execution never reached the command boundary.");
            var args = f.Effects.Single();
            foreach (var (flag, value) in new[] { ("--id", plan.Id), ("--version", plan.AvailableVersion),
                ("--source", "winget"), ("--scope", "user"), ("--installer-type", "nullsoft"), ("--architecture", "x64") })
                Check(Value(args, flag) == value && args.Count(x => x == flag) == 1, "Scope/target changed at dispatch: " + flag);
            Check(args[0] == "upgrade" && !args.Contains("--force") && !args.Contains("--override"), "Scoped upgrade changed operation.");
            f.Effects.Clear();
            Check(!WinGetService.Install(plan, true, f.Execute).Success && f.Effects.Count == 0, "Install fallback escaped scoped upgrade.");
            f.Version = plan.AvailableVersion;
            Check((await f.Guard.VerifyAsync(plan)).Verified, "Same local identity at target version did not verify.");
            f.LocalId += "-changed";
            Check(!(await f.Guard.VerifyAsync(plan)).Verified, "A different ARP identity verified as the approved copy.");
        }
        Action<GuardFixture, PlanItem>[] mutations =
        [
            (f,p) => f.Version = "9.0", (f,p) => f.LocalId += "-other", (f,p) => f.Sid += "-1",
            (f,p) => f.Client = "v1.30.0", (f,p) => f.ManifestText += "\n# changed offer\n",
            (f,p) => f.ManifestText = null, (f,p) => f.Duplicate = true,
            (f,p) => f.NativeScope = "ambiguous", (f,p) => f.Arch = "arm64",
            (f,p) => p.Id = "Example.Other", (f,p) => p.AvailableVersion = "9.0", (f,p) => p.Source = "other",
            (f,p) => p.WinGetBinding = null,
            (f,p) => { p.WinGetBinding = null; p.Source = "WinGet"; },
            (f,p) => { p.WinGetBinding = null; p.Source = "WINGET"; },
            (f,p) => p.WinGetBinding = p.WinGetBinding! with { Scope = "machine" },
            (f,p) => p.WinGetBinding = p.WinGetBinding! with { InstallerType = "inno" }
        ];
        foreach (var mutate in mutations)
        {
            var f = new GuardFixture(); var plan = await f.Plan(); mutate(f, plan);
            Check(!WinGetService.Upgrade(plan, true, f.Execute, f.Guard).Success && f.Effects.Count == 0,
                "Drift or stripped constraints reached installer dispatch.");
        }
        var valid = new GuardFixture().Details();
        foreach (var bad in new[] { valid + valid, valid + "Broken [Example.Other]\n", valid + "Local Identifier: duplicate\n",
            valid.Replace("Local Identifier:", "Unknown field:", StringComparison.Ordinal), valid.Replace("Version:", "Version: 1.0\nVersion:", StringComparison.Ordinal) })
            Check(WinGetScopeGuard.ParseIdentity(bad, "ElementLabs.LMStudio") is null, "Malformed or duplicate detail record became unique.");
        Console.WriteLine("PASS B2 ScopedExecutionAndDrift: silent/interactive, identity/account/client/manifest/tuple drift, stripped binding, verification.");
    }

    private static async Task UnsupportedManifestDoesNotDowngrade()
    {
        foreach (var bad in new[] { "UpgradeBehavior: install\n", "garbage", "Installers: []\nUpgradeBehavior: install\n" })
        {
            var f = new GuardFixture(); var plan = await f.Plan(); plan.WinGetBinding = null; f.ManifestText = bad;
            Check(!WinGetService.Upgrade(plan, true, f.Execute, f.Guard).Success && f.Effects.Count == 0,
                "Missing binding plus unsupported metadata downgraded execution.");
        }
    }

    private sealed class GuardFixture
    {
        internal string? ManifestText = Manifest("ElementLabs.LMStudio");
        internal string Version = "0.4.23+1";
        internal string LocalId = "ARP\\User\\X64\\c6dbe996-22a9-5998-b542-7abe33da3b83";
        internal string Sid = "S-1-5-21-1000";
        internal string Client = "v1.29.290";
        internal string NativeScope = "user";
        internal string Arch = "x64";
        internal bool Duplicate;
        internal List<string[]> Effects { get; } = [];
        internal WinGetScopeGuard Guard => new(Execute, (_, _, _) => Task.FromResult(ManifestText), () => Sid,
            (_, _) => NativeScope, "x64", readDisplayVersion: _ => Version);
        internal string Details() => $"LM Studio [ElementLabs.LMStudio]\nVersion: {Version}\nLocal Identifier: {LocalId}\nInstalled Scope: user\nInstalled Architecture: {Arch}\n";
        internal Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __)
        {
            if (args[0] == "--version") return Task.FromResult(new ProcessResult(0, Client, ""));
            if (args[0] == "list") return Task.FromResult(new ProcessResult(0, Details() + (Duplicate ? Details() : ""), ""));
            Effects.Add(args.ToArray());
            return Task.FromResult(new ProcessResult(0, "simulated", ""));
        }
        internal async Task<PlanItem> Plan()
        {
            var item = new UpdateItem { Id = "ElementLabs.LMStudio", Name = "LM Studio", Source = "winget", Kind = UpdateKind.Software,
                InstalledVersion = Version, AvailableVersion = "0.4.28+2" };
            item.WinGetBinding = await Guard.PrepareAsync(item, ManifestText!, default);
            Check(item.WinGetBinding is not null, "Fixture failed real scope/recipe validation.");
            return ElevatedUpdateRunner.ToPlanItem(item);
        }
    }

    private static async Task ScopedRecipeRecovery()
    {
        var recovered = new List<UpdateItem>();
        (string Id, string Scope, string Installed, string Available, string Key, string Arch, bool Allowed)[] cases =
        [
            ("Anysphere.Cursor", "user", "3.25.6", "3.27.12", "{DADADADA-ADAD-ADAD-ADAD-ADADADADADAD}}_is1", "", true),
            ("ElementLabs.LMStudio", "user", "0.4.23+1", "0.4.28+2", "c6dbe996-22a9-5998-b542-7abe33da3b83", "x64", true),
            ("Google.Chrome.EXE", "machine", "156.0.8078.80", "157.0.8103.39", "Google Chrome", "x64", true),
            ("JRSoftware.InnoSetup.7", "user", "7.0.2", "7.1.1", "Inno Setup 7_is1", "", true),
            ("Microsoft.VisualStudioCode", "machine", "1.141.0", "1.143.1", "{EA457B21-F73E-494C-ACAB-524FDE069978}_is1", "x64", true),
            ("Anaconda.Miniconda3", "user", "py312_24.5.0-0", "py314_26.9.4-0", "Miniconda3 py312_24.5.0-0 (Python 3.12.4 64-bit)", "", false),
            ("AnywhereLabs.DSHDesktop", "user", "2.0.9", "2.0.22-next", "85820364-4c28-594e-b046-8896b3669248", "", false),
            ("Git.Git", "machine", "2.55.0.3", "2.56.0.4", "Git_is1", "x64", false)
        ];
        foreach (var c in cases)
        {
            var manifest = Manifest(c.Id);
            var item = new UpdateItem { Id = c.Id, Name = c.Id, Kind = UpdateKind.Software,
                Source = "winget", InstalledVersion = c.Installed, AvailableVersion = c.Available };
            var local = $"ARP\\{(c.Scope == "user" ? "User" : "Machine")}\\X64\\{c.Key}";
            Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __)
            {
                if (args.SequenceEqual(new[] { "--version" })) return Task.FromResult(new ProcessResult(0, "v1.29.290", ""));
                Check(args[0] == "list" && args.Contains("--details") && args.Contains("--exact") &&
                    Value(args, "--scope") == c.Scope && Value(args, "--source") == "winget" && Value(args, "--id") == c.Id,
                    "Identity query changed the target or scope.");
                return Task.FromResult(new ProcessResult(0,
                    $"{item.Name} [{item.Id}]\nVersion: {item.InstalledVersion}\nLocal Identifier: {local}\nInstalled Scope: {c.Scope}\n" +
                    (c.Arch.Length == 0 ? "" : $"Installed Architecture: {c.Arch}\n"), ""));
            }
            var guard = new WinGetScopeGuard(Execute, (_, _, _) => Task.FromResult<string?>(manifest),
                () => "S-1-5-21-1000", (_, _) => c.Scope, "x64");
            var binding = await guard.PrepareAsync(item, manifest, default);
            Check((binding is not null) == c.Allowed, c.Id + ": scoped admission differs from source-reviewed recipe.");
            if (binding is not null)
            {
                Check(binding.Scope == c.Scope && binding.LocalId == local && binding.AccountSid == "S-1-5-21-1000" &&
                    binding.ClientVersion == "v1.29.290" && binding.ManifestSha256.Length == 64,
                    c.Id + ": incomplete execution constraints.");
                item.WinGetBinding = binding;
                WinGetManifestSafetyService.ApplySafetyClassification(item, Enum.Parse<WinGetUpgradeSafety>(binding.Safety));
                recovered.Add(item);
            }
        }
        var vm = new MainViewModel(null!);
        for (var i = 0; i < 26; i++) vm.Updates.Add(new() { Id = "Baseline.Safe" + i, Name = "safe", Kind = UpdateKind.Software });
        for (var i = 0; i < 12; i++) vm.Updates.Add(new() { Id = "Baseline.Unknown" + i, Name = "review", Kind = UpdateKind.Software, IsSelected = false, HasUnverifiedInstallerMetadata = true });
        for (var i = 0; i < 8; i++) vm.Updates.Add(new() { Id = "Baseline.Blocked" + i, Name = "blocked", Kind = UpdateKind.Software, CanInstall = false });
        var risky = new UpdateItem { Id = "Baseline.Conditional", Name = "risk", Kind = UpdateKind.Software, IsSelected = false, RequiresRiskConfirmation = true };
        vm.Updates.Add(risky);
        foreach (var item in recovered) vm.Updates.Add(item);
        Check(vm.Updates.Count == 52 && vm.SelectedCount == 29 && vm.Updates.Count(x => x.CanInstall) == 44,
            "Controlled B2 fixture has incorrect initial/individual coverage.");
        vm.SetAllSelected(true);
        Check(vm.SelectedCount == 43 && !risky.IsSelected, "Collective selection bypassed specific consent.");
        risky.IsSelected = true; vm.SetAllSelected(true);
        Check(vm.SelectedCount == 44 && risky.RequiresRiskConfirmation, "Individual risk choice lost its separate confirmation requirement.");
        Console.WriteLine("PASS B2 controlled baseline: 52 detected, 29 initial, 43 bulk, 44 individual, 8 blocked, 1 specific consent; fixture only.");
    }

    internal static string Manifest(string id)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith("." + id + ".installer.yaml", StringComparison.Ordinal)))!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Value(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
