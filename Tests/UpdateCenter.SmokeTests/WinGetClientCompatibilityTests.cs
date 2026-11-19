using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class WinGetClientCompatibilityTests
{
    private const string OlderClient = "v1.29.290";
    private const string NewerClient = "v1.30.300";
    private const string ServicedClient = "v1.30.310";
    private const string UnknownClient = "v1.30.301";

    internal static async Task RunAsync()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production data root selected.");
        foreach (var test in new Func<Task>[]
        {
            EnglishDetailsRetainInstalledTechnology,
            ReviewedClientsBindObservedVersion,
            InstalledCategoryDriftRefusesDispatch,
            IncompatibleCategoryCannotBind,
            InstalledCategoryDriftCannotVerifyTarget,
            MissingCategoryRespectsHistoricalClient,
            UnsupportedClientNeverDispatches,
            ReviewedClientDriftStopsDispatch,
            SnapshotFreezesClientVersion,
            FinalNativeBoundaryRejectsChangedClient,
            VerificationClientLossRemainsPending,
            IncidentEngineTests.UnreviewedClientBlocksScanAndDispatch
        })
        {
            await test();
            Console.WriteLine("PASS " + test.Method.Name);
        }
    }

    private static async Task InstalledCategoryDriftRefusesDispatch()
    {
        var fixture = new Fixture(ServicedClient);
        var item = fixture.Proposal();
        item.WinGetBinding = await fixture.Bind(item);
        Check(item.WinGetBinding is not null, "No initial binding for a traditional EXE copy.");
        var plan = Freeze(ElevatedUpdateRunner.ToPlanItem(item)).ToPlanItem();
        fixture.Category = "nullsoft";
        Check(fixture.Upgrade(plan).PreflightRejected && fixture.Dispatches == 0,
            "An installed-category change after consent reached dispatch.");
    }

    private static async Task IncompatibleCategoryCannotBind()
    {
        foreach (var category in new[] { "msi", "msix", "portable", "inno", "unknown" })
        {
            var fixture = new Fixture(ServicedClient) { Category = category };
            var proposal = fixture.Proposal();
            Check(await fixture.Bind(proposal) is null, "A nullsoft recipe bound an incompatible installed category: " + category);
            var result = fixture.Upgrade(Freeze(ElevatedUpdateRunner.ToPlanItem(proposal)).ToPlanItem());
            Check(result.PreflightRejected && fixture.Dispatches == 0, "An unbound nullsoft recipe dispatched for " + category);
        }
    }

    private static async Task MissingCategoryRespectsHistoricalClient()
    {
        foreach (var client in new[] { NewerClient, ServicedClient })
        {
            var f = new Fixture(client);
            var item = f.Proposal();
            var original = await f.Bind(item);
            Check(original is not null, "Test requires a bound installation.");
            var json = System.Text.Json.JsonSerializer.SerializeToNode(original)!.AsObject();
            json.Remove("RegisteredInstallerCategory");
            item.WinGetBinding = System.Text.Json.JsonSerializer.Deserialize<WinGetScopeBinding>(json.ToJsonString());
            var result = f.Upgrade(Freeze(ElevatedUpdateRunner.ToPlanItem(item)).ToPlanItem());
            Check(client == NewerClient ? result.Success && f.Dispatches == 1 : result.PreflightRejected && f.Dispatches == 0,
                "Missing installed category violated plan compatibility or removed an observable 1.30.310 constraint.");
        }
    }

    private static async Task InstalledCategoryDriftCannotVerifyTarget()
    {
        var fixture = new Fixture(ServicedClient);
        var item = fixture.Proposal();
        item.WinGetBinding = await fixture.Bind(item);
        Check(item.WinGetBinding is not null, "No initial binding for target verification.");
        var plan = Freeze(ElevatedUpdateRunner.ToPlanItem(item)).ToPlanItem();
        fixture.LocalVersion = "2.0";
        Check((await fixture.Verify(plan)).Verified, "The same category, local identity and target version failed verification.");
        fixture.Category = "nullsoft";
        Check(!(await fixture.Verify(plan)).Verified, "A changed installed category verified the frozen installation.");
    }

    private static Task EnglishDetailsRetainInstalledTechnology()
    {
        const string details = """
            Microsoft Visual Studio Code [Microsoft.VisualStudioCode]
            Version: 1.141.0
            Local Identifier: ARP\Machine\X64\{EA457B21-F73E-494C-ACAB-524FDE069978}_is1
            Installer Category: exe
            Installed Scope: Machine
            Installed Architecture: X64
            Origin Source: winget
            """;
        var identity = WinGetScopeGuard.ParseIdentity(details, "Microsoft.VisualStudioCode");
        Check(identity is { Scope: "machine", Architecture: "x64", InstallerType: "exe" },
            "Native English installed details lost the installation technology.");
        Check(WinGetScopeGuard.ParseIdentity(details + "\nInstaller Type: msi", "Microsoft.VisualStudioCode") is null,
            "Conflicting installed technology fields were accepted.");
        return Task.CompletedTask;
    }

    private static Task VerificationClientLossRemainsPending()
    {
        foreach (var failAt in new[] { 1, 2 })
        foreach (var mode in new[] { "missing", "drift", "cancel", "timeout" })
        {
            var checks = 0;
            var queries = 0;
            Task<ProcessResult> Client(IReadOnlyList<string> args, CancellationToken ct, TimeSpan timeout)
            {
                if (++checks == failAt)
                {
                    if (mode == "missing") throw new InvalidOperationException("Registered App Installer unavailable");
                    if (mode == "cancel") throw new OperationCanceledException();
                    if (mode == "timeout") throw new TimeoutException();
                    return Task.FromResult(new ProcessResult(0, OlderClient, ""));
                }
                return Task.FromResult(new ProcessResult(0, NewerClient, ""));
            }
            try
            {
                var result = WinGetService.VerifyInstallation(new PlanItem
                {
                    Id = "Microsoft.Test", Source = "winget", AvailableVersion = "2.0", WinGetClientVersion = NewerClient
                }, (_, _) =>
                {
                    queries++;
                    return (new ProcessResult(0, "", ""), new List<WinGetPackageRow>
                    { new("Fixture", "Microsoft.Test", "2.0", "", "winget") });
                }, maxAttempts: 1, verifyClient: Client);
                Check(mode is "missing" or "drift", "Cancellation or timeout was swallowed.");
                Check(!result.Verified && !result.IsDefinitive && result.Status == UpdateVerificationStatuses.Unavailable,
                    "Client loss/drift declared success or failure instead of pending verification.");
            }
            catch (OperationCanceledException) when (mode == "cancel") { }
            catch (TimeoutException) when (mode == "timeout") { }
            Check(queries == failAt - 1, "Client was not checked before and after installed-version evidence.");
        }
        return Task.CompletedTask;
    }

    private static async Task FinalNativeBoundaryRejectsChangedClient()
    {
        foreach (var verb in new[] { "upgrade", "install" })
        foreach (var observed in new[] { OlderClient, NewerClient, ServicedClient, UnknownClient, "v1.30.311" })
        {
            var effects = 0;
            Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken ct, TimeSpan timeout)
            {
                if (args[0] == "--version") return Task.FromResult(new ProcessResult(0, observed, ""));
                effects++;
                return Task.FromResult(new ProcessResult(0, "fixture", ""));
            }
            var result = await WinGetService.ExecutePinnedAsync(new PlanItem { WinGetClientVersion = NewerClient },
                [verb], default, TimeSpan.FromSeconds(1), Execute);
            Check(observed == NewerClient ? result.Success && effects == 1 : result.PreflightRejected && effects == 0,
                "Final native boundary did not enforce the frozen client for " + verb);
        }
    }

    private static async Task ReviewedClientsBindObservedVersion()
    {
        foreach (var client in new[] { OlderClient, NewerClient, ServicedClient })
        {
            var fixture = new Fixture(client);
            var item = fixture.Proposal();
            item.WinGetBinding = await fixture.Bind(item);
            Check(item.WinGetBinding?.ClientVersion == client, $"Binding did not record the observed reviewed client {client}.");
            var plan = Freeze(ElevatedUpdateRunner.ToPlanItem(item)).ToPlanItem();
            Check(plan.WinGetClientVersion == client && fixture.Upgrade(plan).Success && fixture.Dispatches == 1,
                "Unchanged reviewed client did not reach the simulated installer boundary.");
        }
    }

    private static async Task UnsupportedClientNeverDispatches()
    {
        foreach (var client in new[] { UnknownClient, "v1.30.311", "v1.30.310-preview" })
        {
            var fixture = new Fixture(client);
            Check(await fixture.Bind(fixture.Proposal()) is null,
                "An adjacent but unreviewed client could create a binding.");
            var rejected = fixture.Upgrade(Freeze(ElevatedUpdateRunner.ToPlanItem(fixture.Proposal())).ToPlanItem());
            Check(rejected.PreflightRejected && !rejected.Success && fixture.Dispatches == 0,
                "An unreviewed client reached dispatch through an unbound plan.");
        }
    }

    private static async Task ReviewedClientDriftStopsDispatch()
    {
        foreach (var (initial, changed) in new[] { (OlderClient, NewerClient), (NewerClient, OlderClient),
            (NewerClient, ServicedClient), (ServicedClient, NewerClient) })
        foreach (var bound in new[] { false, true })
        {
            var fixture = new Fixture(initial);
            var item = fixture.Proposal();
            if (bound)
            {
                item.WinGetBinding = await fixture.Bind(item);
                Check(item.WinGetBinding is not null, "Fixture did not create a bound plan.");
            }
            var snapshot = Freeze(ElevatedUpdateRunner.ToPlanItem(item));
            Check(fixture.Upgrade(snapshot.ToPlanItem()).Success && fixture.Dispatches == 1,
                "Unchanged plan was not selectable at the simulated boundary.");
            fixture.Client = changed;
            var refused = fixture.Upgrade(snapshot.ToPlanItem());
            Check(refused.PreflightRejected && !refused.Success && fixture.Dispatches == 1,
                "A reviewed client different from the consented version reached dispatch.");
        }
    }

    private static async Task SnapshotFreezesClientVersion()
    {
        var fixture = new Fixture(OlderClient);
        var proposal = fixture.Proposal();
        proposal.WinGetBinding = await fixture.Bind(proposal);
        Check(proposal.WinGetBinding is not null, "Fixture did not create a bound plan.");
        var dto = ElevatedUpdateRunner.ToPlanItem(proposal);
        var snapshot = Freeze(dto);
        dto.WinGetClientVersion = NewerClient;
        var copy = snapshot.ToPlanItem();
        Check(copy.WinGetClientVersion == OlderClient && RunnerPlanItem.Matches(proposal, copy),
            "The frozen plan did not preserve the proposal's client version.");
        copy.WinGetClientVersion = NewerClient;
        Check(!RunnerPlanItem.Matches(proposal, copy) && snapshot.ToPlanItem().WinGetClientVersion == OlderClient &&
            snapshot.ToUpdateItem().WinGetClientVersion == OlderClient,
            "Client version is missing from plan matching or escaped the snapshot copy.");
        RejectSnapshot(dto, "Conflicting proposal and binding client versions were accepted.");
        dto.WinGetClientVersion = "v1.29.290\n";
        RejectSnapshot(dto, "A client version containing a control character was accepted.");
    }

    private static RunnerPlanItem Freeze(PlanItem item) =>
        RunnerPlanSnapshot.Create(new UpdatePlan { Items = [item] }, requireAdministrator: false).Items.Single();

    private static void RejectSnapshot(PlanItem item, string message)
    {
        try { _ = Freeze(item); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }

    private sealed class Fixture(string client)
    {
        private const string Id = "Fixture.Client";
        private const string Name = "Fixture Client";
        private const string LocalId = @"ARP\User\X64\Fixture Client";
        private const string Manifest = """
            PackageIdentifier: Fixture.Client
            PackageVersion: 2.0
            Scope: user
            InstallerType: nullsoft
            UpgradeBehavior: install
            ElevationRequirement: elevationProhibited
            Installers:
            - Architecture: x64
              InstallerUrl: https://example.invalid/installer.exe
              InstallerSha256: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
            """;

        public string Client { get; set; } = client;
        public string Category { get; set; } = "exe";
        public string LocalVersion { get; set; } = "1.0";
        public int Dispatches { get; private set; }

        public UpdateItem Proposal() => new()
        {
            Id = Id, Name = Name, Source = "winget", Kind = UpdateKind.Software,
            InstalledVersion = "1.0", AvailableVersion = "2.0", WinGetClientVersion = Client
        };

        public Task<WinGetScopeBinding?> Bind(UpdateItem item) => Guard().PrepareAsync(item, Manifest, default);
        public ProcessResult Upgrade(PlanItem item) => WinGetService.Upgrade(item, true, Dispatch, Guard());
        public Task<UpdateVerificationResult> Verify(PlanItem item) => Guard().VerifyAsync(item);

        private WinGetScopeGuard Guard() => new(Read, (_, _, _) => Task.FromResult<string?>(Manifest),
            () => "S-1-5-21-1000", (_, _) => "user", "x64", isAdministrator: () => false,
            readDisplayVersion: _ => LocalVersion);

        private Task<ProcessResult> Read(IReadOnlyList<string> args, CancellationToken token, TimeSpan _)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(args[0] switch
            {
                "--version" => new ProcessResult(0, Client, ""),
                "list" => new ProcessResult(0, $"{Name} [{Id}]\nVersion: {LocalVersion}\nLocal Identifier: {LocalId}\nInstalled Scope: user\nInstalled Architecture: x64\nInstaller Category: {Category}\n", ""),
                _ => throw new InvalidOperationException("Unexpected command at the read-only boundary.")
            });
        }

        private Task<ProcessResult> Dispatch(IReadOnlyList<string> args, CancellationToken token, TimeSpan _)
        {
            token.ThrowIfCancellationRequested();
            Check(args[0] == "upgrade", "Unexpected simulated installation command.");
            Dispatches++;
            return Task.FromResult(new ProcessResult(0, "simulated", ""));
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
