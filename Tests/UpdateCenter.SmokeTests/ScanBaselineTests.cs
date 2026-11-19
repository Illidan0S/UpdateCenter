using System.Text.Json;
using System.Windows.Threading;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class ScanBaselineTests
{
    private const string Switch = "UPDATECENTER_SCAN_DIAGNOSTICS";
    private const string Marker = "operation=scan-baseline | phase=complete | outcome=";

    internal static async Task RunAsync()
    {
        AppPathsHardeningTests.Run(AppPaths.DataDirectory);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Kind = "environment", Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            Repetitions = 20, WarmupsPerScenario = 1, CacheState = "uncontrolled", Simulated = true
        }));
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await ExerciseAsync(); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(90));
        Console.WriteLine("PASS scan baseline: opt-in, outcomes, counts, overlap, privacy and isolated regressions.");
    }

    private static async Task ExerciseAsync()
    {
        var previous = Environment.GetEnvironmentVariable(Switch);
        try
        {
            Environment.SetEnvironmentVariable(Switch, null);
            await ScanAsync("normal", 250);
            Check(Reports().Count == 0, "Diagnostics must be disabled by default.");
            Environment.SetEnvironmentVariable(Switch, "1");
            await ProviderTelemetryAsync();
            foreach (var scenario in new[] { "normal", "large", "slow", "unavailable", "partial", "cancelled", "error" })
            {
                // One explicit warm-up per scenario, then all 20 retained samples.
                for (var repetition = -1; repetition < 20; repetition++)
                {
                    var before = Reports().Count;
                    await ScanAsync(scenario, scenario == "large" ? 1000 : 250);
                    var reports = Reports();
                    Check(reports.Count == before + 1, "Expected one diagnostic report per accepted scan.");
                    var report = reports[^1];
                    Validate(report, scenario);
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        Kind = repetition < 0 ? "warmup" : "sample", Scenario = scenario,
                        Repetition = repetition, Simulated = true, Report = report
                    }));
                }
            }
            // Existing state and safety regressions under enabled instrumentation.
            await ScanHardeningTests.RunAsync();
            await QuickHardwareHardeningTests.RunAsync();
            WindowsUpdateSafetyTests.Run();
            WinGetExecutionTests.Run();
        }
        finally { Environment.SetEnvironmentVariable(Switch, previous); }
    }

    private static async Task ProviderTelemetryAsync()
    {
        foreach (var firmware in new[] { false, true })
        {
            using (var scan = ScanDiagnostics.Start())
            {
                var offer = new FakeDriverOffer { DriverClass = firmware ? "Firmware" : "Display" };
                var session = new FakeUpdateSession(offer);
                var result = WindowsUpdateService.ScanDrivers(default, [], session.Create);
                Check(result.Updates.Count == (firmware ? 0 : 1), "WUA safety changed with diagnostics.");
                Check(session.DownloaderCalls == 0 && session.InstallerCalls == 0, "Scan attempted a download/install.");
                scan!.Complete("success");
            }
            var counts = Reports()[^1].GetProperty("Counts");
            Check(counts.GetProperty("wua.configured.candidates").GetInt64() == 1, "Missing WUA per-source candidates.");
            Check(counts.GetProperty("wua.configured.returned").GetInt64() == (firmware ? 0 : 1), "Wrong WUA per-source results.");
            Check(counts.GetProperty("wua.returned").GetInt64() == (firmware ? 0 : 1), "Wrong WUA results.");
            if (firmware) Check(counts.GetProperty("wua.excluded").GetInt64() == 2, "Missing conservative exclusions.");
        }
        foreach (var exitCode in new[] { 0, unchecked((int)0x8A15002B) })
        {
            using (var scan = ScanDiagnostics.Start())
            {
                var scanner = new WinGetService((args, _, _) => Task.FromResult(new ProcessResult(
                    args[0] == "--version" ? 0 : exitCode,
                    args[0] == "--version" ? "v1.0.123" : "No applicable upgrade found", "")));
                Check((await scanner.ScanAsync(false, default)).Count == 0, "Empty WinGet scan changed.");
                scan!.Complete("success");
            }
            Check(Reports()[^1].GetProperty("Outcome").GetString() == "success",
                "An accepted no-updates result must not be diagnosed as a partial scan.");
        }
        var winget = Reports()[^1];
        Check(winget.GetProperty("Counts").GetProperty("winget.returned").GetInt64() == 0 &&
              winget.GetProperty("Phases").EnumerateArray().Any(p => p.GetProperty("Name").GetString() == "winget.search"),
              "Missing WinGet phase/count.");
        using (var scan = ScanDiagnostics.Start())
        {
            await QuickHardwareHardeningTests.RunAsync();
            scan!.Complete("success");
        }
        var quick = Reports()[^1];
        Check(quick.GetProperty("Outcome").GetString() == "partial" &&
              quick.GetProperty("Counts").GetProperty("quick.timeouts").GetInt64() == 1 &&
              quick.GetProperty("Counts").GetProperty("quick.errors").GetInt64() == 1,
              "Recovered quick-provider errors disappeared from diagnostics.");

        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> observed;
        using (var scan = ScanDiagnostics.Start())
        {
            observed = ScanDiagnostics.ObserveAsync("fixture.pending", () => pending.Task, default);
            scan!.Complete("cancelled");
        }
        var frozen = Reports()[^1].GetRawText();
        Check(Reports()[^1].GetProperty("PendingPhases").GetInt32() == 1, "Unfinished work counted as complete.");
        pending.SetResult(1);
        await observed;
        Check(Reports()[^1].GetRawText() == frozen, "Late provider rewrote a completed scan report.");
        Check(ScanDiagnostics.Begin("fixture.leaked") is null, "Ambient diagnostic context leaked.");
    }

    private static async Task ScanAsync(string scenario, int size)
    {
        var previous = new DateTime(2025, 4, 3, 2, 1, 0, DateTimeKind.Utc);
        JsonStorage.SaveSettings(new AppSettings { LastScanUtc = previous });
        JsonStorage.SaveHistory([]);
        var history = File.ReadAllBytes(AppPaths.HistoryFile);
        MainViewModel? vm = null;
        var quickReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<QuickHardwareSnapshot> Quick(CancellationToken token)
        {
            if (scenario == "slow")
            {
                await quickReady.Task.WaitAsync(token);
                await Task.Delay(25, token);
            }
            return new(new HardwareScanResult { CpuName = "PRIVATE-HARDWARE" }, new StorageHealthScanResult());
        }
        Task<IReadOnlyList<UpdateItem>> Software(bool _, CancellationToken token)
        {
            quickReady.TrySetResult();
            if (scenario == "unavailable") throw new IOException("PRIVATE-PROVIDER-DETAIL");
            if (scenario == "cancelled")
            {
                vm!.CancelScan();
                return Task.FromCanceled<IReadOnlyList<UpdateItem>>(token);
            }
            var rows = Enumerable.Range(0, size).Select(i => new UpdateItem
            {
                Id = "PRIVATE-PACKAGE-" + i, Name = "PRIVATE-NAME", Kind = UpdateKind.Software,
                InstalledVersion = "1.0", AvailableVersion = "2.0"
            }).ToList();
            rows.AddRange(rows.Take(size / 10).ToArray());
            return Task.FromResult<IReadOnlyList<UpdateItem>>(rows);
        }
        var services = new ScanServices(Software,
            token => scenario == "error" ? throw new IOException("PRIVATE-FATAL-DETAIL") : Quick(token),
            _ => Task.FromResult(new HardwareScanResult()),
            (_, _) => Task.FromResult(new DriverScanResult
            {
                SourceWarnings = scenario == "partial" ? ["PRIVATE-SOURCE-DETAIL"] : []
            }),
            (_, _) => Task.FromResult(new OfficialDriverCatalogScanResult()),
            _ => Task.FromResult<IReadOnlyList<GameDependencyItem>>([]),
            _ => Task.FromResult(new StorageHealthScanResult()));
        vm = new MainViewModel(services);
        try
        {
            await vm.ScanAsync();
            Check(scenario != "error", "Unexpected swallowed fatal error.");
        }
        catch (IOException) when (scenario == "error") { }
        Check(!vm.IsBusy && !vm.IsScanRunning, "Busy state leaked.");
        Check(File.ReadAllBytes(AppPaths.HistoryFile).SequenceEqual(history), "Scan changed history.");
        Check(JsonStorage.LoadSettings().LastScanUtc == vm.Settings.LastScanUtc, "Persisted state differs.");
        if (scenario is "cancelled" or "error")
            Check(vm.Settings.LastScanUtc == previous, "Cancelled/failed scan persisted success.");
        else
        {
            Check(vm.Settings.LastScanUtc != previous, "Completed scan did not persist.");
            Check(vm.Updates.Count == (scenario == "unavailable" ? 0 : size), "Aggregation changed results.");
        }
    }

    private static void Validate(JsonElement report, string scenario)
    {
        var expected = scenario switch
        {
            "unavailable" or "partial" => "partial", "cancelled" => "cancelled", "error" => "error", _ => "success"
        };
        Check(report.GetProperty("Outcome").GetString() == expected, "Wrong terminal outcome: " + scenario);
        Check(!report.GetRawText().Contains("PRIVATE-", StringComparison.Ordinal), "Sensitive data in diagnostic payload.");
        var total = report.GetProperty("TotalMs").GetDouble();
        Check(total >= 0 && double.IsFinite(total), "Invalid monotonic total.");
        foreach (var phase in report.GetProperty("Phases").EnumerateArray())
        {
            var start = phase.GetProperty("StartMs").GetDouble();
            var duration = phase.GetProperty("DurationMs").GetDouble();
            Check(start >= 0 && duration >= 0 && start + duration <= total + 0.1, "Phase outside scan interval.");
        }
        if (scenario is "normal" or "large" or "slow" or "partial")
        {
            Check(report.GetProperty("Phases").EnumerateArray().Any(p =>
                p.GetProperty("Name").GetString() == "persist.settings" &&
                p.GetProperty("Outcome").GetString() == "success"), "Missing persistence interval.");
            var counts = report.GetProperty("Counts");
            var size = scenario == "large" ? 1000 : 250;
            Check(counts.GetProperty("software.returned").GetInt64() == size + size / 10, "Missing source count.");
            Check(counts.GetProperty("aggregation.duplicates").GetInt64() == size / 10, "Wrong duplicate count.");
            Check(counts.GetProperty("updates.final").GetInt64() == size, "Wrong final count.");
        }
        if (scenario == "slow")
        {
            var phases = report.GetProperty("Phases").EnumerateArray().ToArray();
            var quick = phases.Single(p => p.GetProperty("Name").GetString() == "provider.quick-hardware");
            var software = phases.Single(p => p.GetProperty("Name").GetString() == "provider.software");
            var wait = phases.Single(p => p.GetProperty("Name").GetString() == "wait.quick-hardware");
            Check(quick.GetProperty("StartMs").GetDouble() <= software.GetProperty("StartMs").GetDouble() &&
                  wait.GetProperty("DurationMs").GetDouble() <= quick.GetProperty("DurationMs").GetDouble(),
                  "Lost provider overlap/residual wait distinction.");
        }
    }

    private static List<JsonElement> Reports() => Directory.Exists(AppPaths.LogsDirectory)
        ? Directory.EnumerateFiles(AppPaths.LogsDirectory, "*.log").Order()
            .SelectMany(File.ReadLines).Where(line => line.Contains(Marker, StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[(line.IndexOf("details=", StringComparison.Ordinal) + 8)..]))
            .ToList() : [];

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
