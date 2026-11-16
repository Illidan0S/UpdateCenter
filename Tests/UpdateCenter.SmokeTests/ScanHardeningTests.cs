using System.Windows.Threading;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class ScanHardeningTests
{
    internal static Task RunAsync()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await ExerciseScansAsync(); completed.SetResult(); }
                catch (Exception ex) { completed.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static async Task ExerciseScansAsync()
    {
        var failures = new List<string>();
        async Task Case(string name, Func<Task> run)
        {
            try { await run(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }

        var previousSettings = File.Exists(AppPaths.SettingsFile) ? File.ReadAllBytes(AppPaths.SettingsFile) : null;
        var previousHistory = File.Exists(AppPaths.HistoryFile) ? File.ReadAllBytes(AppPaths.HistoryFile) : null;
        try
        {
            JsonStorage.SaveHistory([]);
            JsonStorage.SaveSettings(new AppSettings());
            await Case("fresh hardware and storage", TestFreshAsync);
            await Case("late startup after repair", TestRepairAsync);
            foreach (var phase in new[] { "software", "hardware", "drivers", "catalog", "dependencies", "storage", "storage-return" })
                await Case("cancel " + phase, () => TestCancelAsync(phase));
            await Case("ordinary source failure", TestPartialAsync);
        }
        finally
        {
            Restore(AppPaths.SettingsFile, previousSettings);
            Restore(AppPaths.HistoryFile, previousHistory);
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static void Restore(string path, byte[]? bytes)
    {
        if (bytes is null) File.Delete(path);
        else File.WriteAllBytes(path, bytes);
    }

    private static QuickHardwareSnapshot Snapshot(string id) => new(
        new HardwareScanResult { CpuName = "CPU-" + id },
        new StorageHealthScanResult { Status = "Disk-" + id });

    private static ScanServices EmptySources() => new(
        (_, _) => Task.FromResult<IReadOnlyList<UpdateItem>>([]),
        _ => Task.FromResult(Snapshot("current")),
        _ => Task.FromResult(new HardwareScanResult { CpuName = "CPU-repaired" }),
        (_, _) => Task.FromResult(new DriverScanResult()),
        (_, _) => Task.FromResult(new OfficialDriverCatalogScanResult()),
        _ => Task.FromResult<IReadOnlyList<GameDependencyItem>>([]),
        _ => Task.FromResult(new StorageHealthScanResult { Status = "Disk-fallback" }));

    private static async Task TestFreshAsync()
    {
        var startup = new TaskCompletionSource<QuickHardwareSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captures = 0;
        var tokens = new List<CancellationToken>();
        var services = EmptySources() with
        {
            QuickHardware = token =>
            {
                tokens.Add(token);
                return ++captures == 1 ? startup.Task : Task.FromResult(Snapshot(captures.ToString()));
            }
        };
        var vm = new MainViewModel(services);
        var initial = vm.EnsureQuickHardwareDataAsync();
        var secondConsumer = vm.EnsureQuickHardwareDataAsync();
        var firstScan = vm.ScanAsync();
        startup.SetResult(Snapshot("startup"));
        await Task.WhenAll(initial, secondConsumer, firstScan);
        if (vm.CpuName != "CPU-2" || vm.StorageHealthStatus != "Disk-2")
            throw new InvalidOperationException($"A late startup result replaced the explicit inventory ({vm.CpuName}, {vm.StorageHealthStatus}).");
        await vm.ScanAsync();
        if (vm.CpuName != "CPU-3" || vm.StorageHealthStatus != "Disk-3" ||
            tokens.Count != 3 || tokens[0].CanBeCanceled || !tokens[1].CanBeCanceled ||
            !tokens[2].CanBeCanceled || tokens[1] == tokens[2])
            throw new InvalidOperationException("The next scan reused inventory or a prior scan token.");
    }

    private static async Task TestRepairAsync()
    {
        var startup = new TaskCompletionSource<QuickHardwareSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new MainViewModel(EmptySources() with { QuickHardware = _ => startup.Task });
        var initial = vm.EnsureQuickHardwareDataAsync();
        await vm.RefreshDriverDiagnosticsAsync();
        startup.SetResult(Snapshot("startup"));
        await initial;
        if (vm.CpuName != "CPU-repaired")
            throw new InvalidOperationException("Late startup inventory replaced verified repair data.");
    }

    private static async Task TestCancelAsync(string phase)
    {
        var previous = new DateTime(2025, 4, 3, 2, 1, 0, DateTimeKind.Utc);
        JsonStorage.SaveSettings(new AppSettings { LastScanUtc = previous });
        MainViewModel? vm = null;
        var later = new List<string>();
        var canceled = false;
        Task<T> Step<T>(string name, CancellationToken token, T result)
        {
            if (canceled) later.Add(name);
            if (phase == name || (phase == "storage-return" && name == "storage"))
            {
                canceled = true;
                vm!.CancelScan();
                if (phase != "storage-return") return Task.FromCanceled<T>(token);
            }
            return Task.FromResult(result);
        }
        vm = new MainViewModel(new ScanServices(
            (_, ct) => Step<IReadOnlyList<UpdateItem>>("software", ct, []),
            _ => Task.FromResult(new QuickHardwareSnapshot(null, null)),
            ct => Step("hardware", ct, new HardwareScanResult()),
            (ct, _) => Step("drivers", ct, new DriverScanResult()),
            (_, ct) => Step("catalog", ct, new OfficialDriverCatalogScanResult()),
            ct => Step<IReadOnlyList<GameDependencyItem>>("dependencies", ct, []),
            ct => Step("storage", ct, new StorageHealthScanResult())));
        var oldSummary = vm.HomeScanSummary;
        await vm.ScanAsync();
        if (!canceled || later.Count != 0 || vm.IsBusy || vm.IsScanRunning ||
            vm.HomeScanSummary != oldSummary || vm.Settings.LastScanUtc != previous ||
            JsonStorage.LoadSettings().LastScanUtc != previous ||
            vm.StatusText != LocalizationService.Text("Scansione annullata", "Scan cancelled"))
            throw new InvalidOperationException("Cancelled scan finalized or ran subsequent phases: " + string.Join(", ", later));
    }

    private static async Task TestPartialAsync()
    {
        var vm = new MainViewModel(EmptySources() with
        {
            Software = (_, _) => throw new IOException("fixture source unavailable")
        });
        await vm.ScanAsync();
        if (vm.CpuName != "CPU-current" || vm.StorageHealthStatus != "Disk-current" ||
            !vm.CurrentItemText.Contains("fixture source unavailable", StringComparison.Ordinal))
            throw new InvalidOperationException("A source error prevented remaining scan phases or lost its warning.");
    }
}
