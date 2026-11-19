using System.Security.Cryptography;
using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class R3BurnTests
{
    private const string Id = "Python.Python.3.13";
    private const string Name = "Python 3.13.15 (64-bit)";
    private const string Installed = "3.13.15";
    private const string Target = "3.13.17";
    private const string LocalId = @"ARP\User\X64\{bdda61aa-57d0-45c8-ae92-fa67d560e32f}";

    internal static async Task RunScanAsync(bool expectRecovered)
    {
        if (AppPaths.DataDirectory == AppPaths.ResolveDefaultRoot()) throw new InvalidOperationException("Production data root selected.");
        var root = Directory.GetCurrentDirectory();
        var manifestPath = Path.Combine(root, "_output", "r3-implementation-20261110", "research", "manifests", Id + "-" + Target + ".installer.yaml");
        var manifest = File.ReadAllText(manifestPath);
        var oldManifest = File.ReadAllText(Path.Combine(root, "_output", "r3-implementation-20261110", "research", "manifests", Id + "-" + Installed + ".installer.yaml"));
        var details = File.ReadAllText(Path.Combine(root, "_output", "r3-b1-20261110", "research", "python-list-user-details.txt"));
        using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "_output", "r3-b1-20261110", "research", "identity-runtime.json")));
        var sid = identity.RootElement.GetProperty("Sid").GetString() ?? throw new InvalidOperationException("Frozen SID missing.");
        // This is a reduced one-candidate table reconstructed from the frozen
        // observation; it is not a replay of the full historical inventory.
        var header = $"{"Name",-32}{"Id",-30}{"Version",-16}{"Available",-16}Source";
        var row = $"{Name,-32}{Id,-30}{Installed,-16}{Target,-16}winget";
        var table = string.Join('\n', header, new string('-', 100), row);
        var calls = new List<string>();
        Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __)
        {
            calls.Add(string.Join(' ', args));
            if (args.SequenceEqual(new[] { "--version" })) return Task.FromResult(new ProcessResult(0, "v1.29.290", ""));
            if (args.Count > 0 && args[0] == "upgrade" && !args.Contains("--id")) return Task.FromResult(new ProcessResult(0, table, ""));
            if (args.Count > 0 && args[0] == "list" && args.Contains("--details")) return Task.FromResult(new ProcessResult(0, details, ""));
            if (args.Count > 0 && args[0] == "list" && !args.Contains("--id")) return Task.FromResult(new ProcessResult(0, table, ""));
            throw new InvalidOperationException("Unexpected external command: " + string.Join(' ', args));
        }
        var metadata = new WinGetManifestSafetyService(
            inspect: (id, version, _) =>
            {
                if (id != Id || version != Target) throw new InvalidOperationException("Unexpected metadata target.");
                return Task.FromResult(new WinGetManifestSafetyService.ManifestInspection(
                    WinGetManifestSafetyService.ParseUpgradeSafety(manifest, "x64"), 0,
                    WinGetManifestSafetyService.ParseInstallerScope(manifest), manifest));
            },
            createGuard: execute => new WinGetScopeGuard(execute, (_, version, _) => Task.FromResult<string?>(version == Installed ? oldManifest : version == Target ? manifest : null),
                () => sid, (_, _) => "", "x64", isAdministrator: () => false, windowsVersion: new Version(10, 0, 26320),
                readDisplayVersion: _ => "3.13.15150.0", readPythonInventory: () => FrozenInventory(root, sid)),
            readScope: (_, _) => "", architecture: "x64", isAdministrator: () => false);
        var items = await new WinGetService(Execute, metadata).ScanAsync(false, CancellationToken.None);
        var item = items.Single();
        if (item.Id != Id || item.InstalledVersion != Installed || item.AvailableVersion != Target ||
            item.CanInstall != expectRecovered || (item.WinGetBinding is not null) != expectRecovered)
            throw new InvalidOperationException($"Unexpected baseline candidate: {item.Id} {item.InstalledVersion} -> {item.AvailableVersion}; CanInstall={item.CanInstall}; bound={item.WinGetBinding is not null}.");
        Console.WriteLine($"{(expectRecovered ? "RECOVERED" : "BASELINE")} reduced frozen candidate: {Id} {Installed} -> {Target}; CanInstall={item.CanInstall}; bound={item.WinGetBinding is not null}; status={item.Status}");
        Console.WriteLine("Local identity: " + LocalId);
        Console.WriteLine("Target manifest SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))));
        Console.WriteLine("Commands simulated: " + string.Join(" | ", calls));
    }

    private static PythonBurnInventory FrozenInventory(string root, string sid)
    {
        var research = Path.Combine(root, "_output", "r3-b1-20261110", "research");
        using var executables = JsonDocument.Parse(File.ReadAllText(Path.Combine(research, "python-exe-file-evidence.json")));
        using var userPath = JsonDocument.Parse(File.ReadAllText(Path.Combine(research, "python-user-path-raw-sha256.json")));
        using var crt = JsonDocument.Parse(File.ReadAllText(Path.Combine(research, "ucrtbase-file-version.json")));
        using var launcher = JsonDocument.Parse(File.ReadAllText(Path.Combine(research, "python-launcher-state-sha256.json")));
        var exe = executables.RootElement[0]; var windowed = executables.RootElement[1];
        var installPath = Path.GetDirectoryName(exe.GetProperty("Path").GetString())!;
        var registration = new PythonBurnRegistration(LocalId, Name, "3.13.15150.0", "3.13.15150.0",
            "{F96F59BD-3E48-5E4E-B1A4-6CAFADEFEAE8}", "CPython-3.13", "Python Software Foundation");
        return new(sid, [registration], installPath, "Shortcuts|dev|doc|doc_shortcut|exe|lib|path|pip|tcltk|test",
            "3.13.15150.0", exe.GetProperty("FileVersion").GetString()!, exe.GetProperty("SHA256").GetString()!,
            windowed.GetProperty("FileVersion").GetString()!, windowed.GetProperty("SHA256").GetString()!,
            userPath.RootElement.GetProperty("SHA256").GetString()!, true, false,
            crt.RootElement.GetProperty("ProductVersion").GetString()!, launcher.RootElement.GetProperty("SHA256").GetString()!);
    }

    internal static async Task RunAsync()
    {
        var failures = new List<string>();
        foreach (var (name, test) in new (string, Func<Task>)[]
        {
            ("successful transition through runner", async () => {
                var f = new BurnFixture(); var item = await f.Scan();
                var result = f.Run(item, approve: true, transition: true);
                Check(result.Verified && result.Success && f.InstallCalls == 1, "Correct old-to-target transition was not verified.");
                Check(f.Command.Contains("--custom") && !f.Command.Contains("--override") &&
                    f.Command.Contains("Include_launcher=0") && f.Command.Contains("--scope") && f.Command.Contains("user") &&
                    f.Command.Contains("--architecture") && f.Command.Contains("x64") && f.Command.Contains("--installer-type") &&
                    f.Command.Contains("burn") && f.Command.Contains("--version") && f.Command.Contains(Target),
                    "Approved scope, target, installer recipe or launcher boundary was not preserved.");
            }),
            ("denied consent", async () => {
                var f = new BurnFixture(); var item = await f.Scan(); f.Run(item, approve: false, transition: true);
                Check(f.InstallCalls == 0, "Installer boundary reached without consent.");
            }),
            ("exit zero without target evidence", async () => {
                var f = new BurnFixture(); var item = await f.Scan(); var result = f.Run(item, true, false);
                Check(!result.Verified && result.InstallerSucceeded, "Exit zero verified an unchanged old installation.");
            }),
            ("side by side", async () => {
                var f = new BurnFixture { SideBySide = true }; var item = await f.Scan();
                var result = f.Run(item, true, true);
                Check(!result.Verified, "Old bundle alongside target was accepted as an upgrade.");
            }),
            ("target at another path", async () => {
                var f = new BurnFixture { ChangePathAfter = true }; var item = await f.Scan();
                Check(!f.Run(item, true, true).Verified, "Target at another location verified the approved copy.");
            }),
            ("metadata drift after consent", async () => {
                var f = new BurnFixture(); var item = await f.Scan();
                f.AfterConsent = () => f.TargetManifest += "\n# drift\n";
                var result = f.Run(item, true, true);
                Check(f.InstallCalls == 0 && !result.Verified, "Changed target metadata reached the installer.");
            }),
            ("account drift after consent", async () => {
                var f = new BurnFixture(); var item = await f.Scan(); f.AfterConsent = () => f.Sid += "-1";
                var result = f.Run(item, true, true);
                Check(f.InstallCalls == 0 && !result.Verified, "Changed account reached the installer.");
            }),
            ("old metadata drift after consent", async () => {
                var f = new BurnFixture(); var item = await f.Scan(); f.AfterConsent = () => f.OldManifest += "\n# drift\n";
                Check(!f.Run(item, true, true).Verified && f.InstallCalls == 0, "Changed old manifest reached installation.");
            }),
            ("plan file mutation after consent", async () => {
                var f = new BurnFixture { MutatePlanFile = true }; var item = await f.Scan();
                var result = f.Run(item, true, true);
                Check(result.Verified && f.Command.Contains(Target) && !f.Command.Contains("9.0"), "Plan file mutation changed the frozen approved target.");
            }),
            ("launcher state drift", async () => {
                var f = new BurnFixture { AfterTransition = state => state with { LauncherStateSha256 = new string('A', 64) } };
                var item = await f.Scan(); Check(!f.Run(item, true, true).Verified, "Launcher mutation accepted as verified.");
            }),
            ("feature state drift", async () => {
                var f = new BurnFixture { AfterTransition = state => state with { FeatureNames = "exe|lib|path" } };
                var item = await f.Scan(); Check(!f.Run(item, true, true).Verified, "Changed feature set accepted as verified.");
            }),
            ("old runtime CRT rejected", async () => {
                var f = new BurnFixture(); f.Inventory = f.Inventory with { CrtVersion = "10.0.10000.0" };
                var item = await f.Scan(false); Check(!item.CanInstall, "Incompatible CRT admitted automatic install.");
            }),
            ("wrong client rejected", async () => {
                var f = new BurnFixture { Client = "v1.30.0" }; var item = await f.Scan(false);
                Check(!item.CanInstall, "Unreviewed WinGet client admitted automatic install.");
            }),
            ("machine scope rejected", async () => {
                var f = new BurnFixture { DetailsTransform = s => ReplaceRequired(s, "Installed Scope: user", "Installed Scope: machine") };
                var item = await f.Scan(false); Check(!item.CanInstall, "Different scope admitted automatic install.");
            }),
            ("architecture mismatch rejected", async () => {
                var f = new BurnFixture { DetailsTransform = s => ReplaceRequired(s, "Installed Architecture: x64", "Installed Architecture: x86") };
                var item = await f.Scan(false); Check(!item.CanInstall, "Different architecture admitted automatic install.");
            }),
            ("raw ARP mismatch rejected", async () => {
                var f = new BurnFixture(); var old = f.Inventory.Bundles.Single();
                f.Inventory = f.Inventory with { Bundles = [old with { DisplayVersion = "3.13.99999.0" }] };
                var item = await f.Scan(false); Check(!item.CanInstall, "Wrong raw ARP version admitted automatic install.");
            }),
            ("multiple ARP registrations rejected", async () => {
                var f = new BurnFixture(); var old = f.Inventory.Bundles.Single();
                f.Inventory = f.Inventory with { Bundles = [old, old with { LocalId = old.LocalId + "-stale" }] };
                var item = await f.Scan(false); Check(!item.CanInstall, "Multiple ARP registrations admitted automatic install.");
            }),
            ("interactive mode blocked", async () => {
                var f = new BurnFixture(); var item = await f.Scan(); var result = f.RunInteractive(item);
                Check(!result.Verified && f.InstallCalls == 0, "Interactive path dispatched the bound upgrade.");
            }),
            ("incomplete binding JSON blocked", async () => {
                var f = new BurnFixture(); var item = await f.Scan(); f.RunMissingNestedField(item);
                Check(f.InstallCalls == 0, "Incomplete binding reached the installer.");
            }),
            ("duplicate copy rejected at scan", async () => {
                var f = new BurnFixture { DuplicateDetails = true };
                var item = await f.Scan(requireAdmitted: false);
                Check(!item.CanInstall && item.WinGetBinding is null, "Ambiguous installations were admitted.");
            }),
            ("restart pending", async () => {
                var f = new BurnFixture { ExitCode = 3010 }; var item = await f.Scan(); var result = f.Run(item, true, false);
                Check(!result.Verified && result.RestartRequired && result.VerificationStatus == UpdateVerificationStatuses.PendingRestart,
                    "Restart without target evidence did not remain pending.");
            }),
            ("installer error remains failure", async () => {
                var f = new BurnFixture { ExitCode = 1 }; var item = await f.Scan(); var result = f.Run(item, true, false);
                Check(!result.Verified && !result.Success, "Installer error without evidence became success.");
            })
        })
        {
            try { await test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS B1 production scan, consent, runner and verification with simulated external boundaries.");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static string ReplaceRequired(string input, string oldValue, string newValue)
    {
        Check(input.Contains(oldValue, StringComparison.Ordinal), "Fixture does not contain the expected mutation field: " + oldValue);
        return input.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    internal static async Task RunNativePreflightAsync()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production data root selected.");
        var unscoped = new[] { "list", "--id", Id, "--exact", "--source", "winget", "--details", "--disable-interactivity", "--nowarn" };
        var scoped = new[] { "list", "--id", Id, "--exact", "--source", "winget", "--scope", "user", "--details", "--disable-interactivity", "--nowarn" };
        async Task<ProcessResult> ReadOnlyWinGet(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
        {
            if (!args.SequenceEqual(new[] { "--version" }) && !args.SequenceEqual(unscoped) && !args.SequenceEqual(scoped))
                throw new InvalidOperationException("Command not in the read-only preflight allowlist.");
            Console.WriteLine("READ-ONLY winget " + string.Join(' ', args));
            return await ProcessRunner.RunAsync("winget.exe", args, token, timeout);
        }
        var result = await ReadOnlyWinGet(unscoped, CancellationToken.None, TimeSpan.FromSeconds(45));
        var observed = result.Success ? WinGetScopeGuard.ParseIdentity(result.StandardOutput, Id) : null;
        Check(observed is not null, "Unique live WinGet identity is unavailable.");
        var inventory = PythonBurnTransitionService.ReadInventory();
        Console.WriteLine("READ-ONLY inventory: " + JsonSerializer.Serialize(inventory));
        var manifest = await WinGetManifestSafetyService.ReadManifestAsync(Id, Target, CancellationToken.None);
        Check(manifest is not null, "Target manifest metadata is unavailable.");
        var item = new UpdateItem { Id = Id, Name = observed!.Name, Kind = UpdateKind.Runtime, Source = "winget",
            InstalledVersion = observed.Version, AvailableVersion = Target };
        var guard = WinGetScopeGuard.Native(ReadOnlyWinGet);
        item.WinGetBinding = await guard.PrepareAsync(item, manifest!, CancellationToken.None);
        Check(item.WinGetBinding?.PythonBurnTransition is not null, "Live candidate did not pass identity and applicability preparation.");
        var snapshot = RunnerPlanSnapshot.Create(new UpdatePlan { Items = [ElevatedUpdateRunner.ToPlanItem(item)] }, false);
        var plan = snapshot.Items.Single().ToPlanItem();
        var rejected = await guard.ValidateAsync(plan);
        Check(rejected is null, "Live candidate did not pass bound preflight: " + rejected?.StandardError);
        Console.WriteLine("PASS native read-only preparation, frozen plan and preflight; no installer, runner or verification executed.");
        Console.WriteLine("READ-ONLY binding: " + JsonSerializer.Serialize(plan.WinGetBinding));
    }

    private sealed class BurnFixture
    {
        private readonly string _root = Directory.GetCurrentDirectory();
        private readonly string _details;
        internal string OldManifest;
        private readonly PythonBurnInventory _initial;
        internal string TargetManifest;
        internal string Sid;
        internal PythonBurnInventory Inventory;
        internal bool DuplicateDetails, SideBySide, ChangePathAfter, MutatePlanFile;
        internal int ExitCode, InstallCalls;
        internal string[] Command = [];
        internal Action? AfterConsent;
        internal Func<PythonBurnInventory, PythonBurnInventory>? AfterTransition;
        internal Func<string, string>? DetailsTransform;
        internal string Client = "v1.29.290";
        private bool _target;

        internal BurnFixture()
        {
            var manifests = Path.Combine(_root, "Tests", "UpdateCenter.SmokeTests", "Fixtures", "R3B1");
            Sid = "S-1-5-21-1000";
            _details = $"{Name} [{Id}]\nVersion: {Installed}\nLocal Identifier: {LocalId}\nInstalled Scope: user\nInstalled Architecture: x64\n";
            OldManifest = File.ReadAllText(Path.Combine(manifests, Id + "-" + Installed + ".installer.yaml"));
            TargetManifest = File.ReadAllText(Path.Combine(manifests, Id + "-" + Target + ".installer.yaml"));
            var registration = new PythonBurnRegistration(LocalId, Name, "3.13.15150.0", "3.13.15150.0",
                "{F96F59BD-3E48-5E4E-B1A4-6CAFADEFEAE8}", "CPython-3.13", "Python Software Foundation");
            Inventory = _initial = new PythonBurnInventory(Sid, [registration], @"C:\Test\Python313",
                "Shortcuts|dev|doc|doc_shortcut|exe|lib|path|pip|tcltk|test", "3.13.15150.0",
                Installed, new string('A', 64), Installed, new string('B', 64), new string('C', 64), true, false,
                "10.0.26100.9739", new string('D', 64));
        }

        private string Details()
        {
            if (!_target) { var text = DetailsTransform?.Invoke(_details) ?? _details; return text + (DuplicateDetails ? "\n" + text : ""); }
            var targetId = PythonBurnTransitionService.Recipes(Id, Installed, Target, OldManifest, TargetManifest)!.Value.Target.ProductCode;
            return _details.Replace(Installed, Target, StringComparison.Ordinal)
                .Replace("{bdda61aa-57d0-45c8-ae92-fa67d560e32f}", targetId, StringComparison.OrdinalIgnoreCase);
        }

        private Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken _, TimeSpan __)
        {
            if (args.SequenceEqual(new[] { "--version" })) return Task.FromResult(new ProcessResult(0, Client, ""));
            var header = $"{"Name",-32}{"Id",-30}{"Version",-16}{"Available",-16}Source";
            var row = $"{Name,-32}{Id,-30}{Installed,-16}{Target,-16}winget";
            var table = string.Join('\n', header, new string('-', 100), row);
            if (args.Count > 0 && args[0] == "upgrade" && !args.Contains("--id")) return Task.FromResult(new ProcessResult(0, table, ""));
            if (args.Count > 0 && args[0] == "list" && args.Contains("--details")) return Task.FromResult(new ProcessResult(0, Details(), ""));
            if (args.Count > 0 && args[0] == "list" && !args.Contains("--id")) return Task.FromResult(new ProcessResult(0, table, ""));
            if (args.Count > 0 && args[0] == "upgrade" && args.Contains("--id"))
            {
                InstallCalls++; Command = args.ToArray();
                if (_transition) Transition();
                return Task.FromResult(new ProcessResult(ExitCode, "simulated process", ""));
            }
            throw new InvalidOperationException("Unexpected command " + string.Join(' ', args));
        }

        private bool _transition;
        private void Transition()
        {
            var recipe = PythonBurnTransitionService.Recipes(Id, Installed, Target, OldManifest, TargetManifest)!.Value.Target;
            var targetId = @"ARP\User\X64\" + recipe.ProductCode;
            var reg = new PythonBurnRegistration(targetId, Name.Replace(Installed, Target), recipe.DisplayVersion,
                recipe.DisplayVersion, "{F96F59BD-3E48-5E4E-B1A4-6CAFADEFEAE8}", "CPython-3.13", "Python Software Foundation");
            Inventory = Inventory with { Bundles = SideBySide ? [.. _initial.Bundles, reg] : [reg],
                InstallPath = ChangePathAfter ? Inventory.InstallPath + "-other" : Inventory.InstallPath,
                FeatureVersion = recipe.DisplayVersion, ExecutableVersion = Target, WindowedExecutableVersion = Target };
            if (AfterTransition is not null) Inventory = AfterTransition(Inventory);
            _target = true;
        }

        private WinGetScopeGuard Guard() => new(Execute,
            (_, version, _) => Task.FromResult<string?>(version == Installed ? OldManifest : version == Target ? TargetManifest : null),
            () => Sid, (_, _) => "", "x64", isAdministrator: () => false, windowsVersion: new Version(10, 0, 26320),
            readDisplayVersion: _ => Inventory.FeatureVersion, readPythonInventory: () => Inventory);

        internal async Task<UpdateItem> Scan(bool requireAdmitted = true)
        {
            var metadata = new WinGetManifestSafetyService(
                inspect: (_, _, _) => Task.FromResult(new WinGetManifestSafetyService.ManifestInspection(
                    WinGetManifestSafetyService.ParseUpgradeSafety(TargetManifest, "x64"), 0,
                    WinGetManifestSafetyService.ParseInstallerScope(TargetManifest), TargetManifest)),
                createGuard: _ => Guard(), readScope: (_, _) => "", architecture: "x64", isAdministrator: () => false);
            var item = (await new WinGetService(Execute, metadata).ScanAsync(false, CancellationToken.None)).Single();
            if (requireAdmitted) Check(item.CanInstall && item.WinGetBinding?.PythonBurnTransition is not null,
                "Frozen candidate did not pass the actual scan and binding path.");
            return item;
        }

        internal ItemRunResult Run(UpdateItem item, bool approve, bool transition)
        {
            _transition = transition;
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(AppPaths.DataDirectory, "update-plan-" + id + ".json");
            var status = Path.Combine(AppPaths.DataDirectory, "update-status-" + id + ".json");
            var plan = new UpdatePlan { Items = [ElevatedUpdateRunner.ToPlanItem(item)], SilentSoftwareInstall = true,
                StatusFile = status, PauseFile = Path.Combine(AppPaths.DataDirectory, "update-pause-" + id + ".signal"),
                PauseOwnerProcessId = Environment.ProcessId };
            JsonStorage.WriteAtomic(path, plan);
            ElevatedUpdateRunner.Run(path, false, new TestConsent(snapshot => {
                Check(InstallCalls == 0 && snapshot.Items.Single().ToPlanItem().WinGetBinding == item.WinGetBinding,
                    "Consent did not receive frozen target before effects.");
                if (MutatePlanFile) { plan.Items[0].AvailableVersion = "9.0"; JsonStorage.WriteAtomic(path, plan); }
                AfterConsent?.Invoke(); return approve;
            }), new TestEffects(this), CancellationToken.None);
            return JsonStorage.Read<UpdateRunStatus>(status)!.Results.Single();
        }

        internal ItemRunResult RunInteractive(UpdateItem item) => ElevatedUpdateRunner.InstallSoftwareInteractive(
            item, new TestConsent(_ => true), new TestEffects(this), CancellationToken.None);

        internal void RunMissingNestedField(UpdateItem item)
        {
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(AppPaths.DataDirectory, "update-plan-" + id + ".json");
            var plan = new UpdatePlan { Items = [ElevatedUpdateRunner.ToPlanItem(item)], SilentSoftwareInstall = true,
                StatusFile = Path.Combine(AppPaths.DataDirectory, "update-status-" + id + ".json"),
                PauseFile = Path.Combine(AppPaths.DataDirectory, "update-pause-" + id + ".signal"), PauseOwnerProcessId = Environment.ProcessId };
            var node = JsonSerializer.SerializeToNode(plan)!.AsObject();
            node["Items"]![0]!["WinGetBinding"]!["PythonBurnTransition"]!.AsObject().Remove("LauncherStateSha256");
            File.WriteAllText(path, node.ToJsonString());
            ElevatedUpdateRunner.Run(path, false, new TestConsent(_ => true), new TestEffects(this), CancellationToken.None);
        }

        private sealed class TestConsent(Func<RunnerPlanSnapshot, bool> confirm) : IRunnerConsent
        { public bool Confirm(RunnerPlanSnapshot plan, CancellationToken _) => confirm(plan); }

        private sealed class TestEffects(BurnFixture fixture) : IRunnerEffects
        {
            public bool IsAdministrator => false;
            public (bool Created, string Message) CreateRestorePoint() => throw new InvalidOperationException("Unexpected restore point.");
            public ItemRunResult InstallDriver(PlanItem item, Action<int, string> progress) => throw new InvalidOperationException("Unexpected driver.");
            public ItemRunResult InstallSoftware(PlanItem item, bool silent, bool interactive) =>
                ElevatedUpdateRunner.InstallSoftwareWithBoundaries(item, silent, interactive, fixture.Execute, fixture.Guard());
            public ItemRunResult InstallSoftware(PlanItem item, bool silent, bool interactive, CancellationToken token, Action<string> phase) =>
                ElevatedUpdateRunner.InstallSoftwareWithBoundaries(item, silent, interactive, fixture.Execute, fixture.Guard(), token, phase);
        }
    }
}
