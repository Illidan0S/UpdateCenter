using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class ScanAggregationTests
{
    // Exercise the existing private boundary without adding a production-only test API.
    private static readonly Action<MainViewModel, IEnumerable<UpdateItem>> Add = typeof(MainViewModel)
        .GetMethod("AddUpdates", BindingFlags.NonPublic | BindingFlags.Instance)!
        .CreateDelegate<Action<MainViewModel, IEnumerable<UpdateItem>>>();
    private static readonly Action<MainViewModel> Clear = typeof(MainViewModel)
        .GetMethod("ClearUpdates", BindingFlags.NonPublic | BindingFlags.Instance)!
        .CreateDelegate<Action<MainViewModel>>();

    internal static async Task RunAsync()
    {
        AppPathsHardeningTests.Run(AppPaths.DataDirectory);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    JsonStorage.SaveSettings(new AppSettings());
                    JsonStorage.SaveHistory([]);
                    IdentityOrderAndEvents();
                    ExistingAndClearedCollection();
                    NullAndSpecialIds();
                    await ProvidersAndRepeatedScansAsync();
                    await ScanHardeningTests.RunAsync();
                    MeasureRemainingCost();
                    done.SetResult();
                }
                catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(45));
        Console.WriteLine("PASS scan aggregation: identity, first-wins order/state, events, existing/cleared items, null/special IDs, providers, cancellation and repeated scans.");
    }

    private static UpdateItem Item(string? id, UpdateKind kind = UpdateKind.Software, string source = "fixture") =>
        new() { Id = id!, Kind = kind, Name = "Fixture", Source = source, InstalledVersion = "1", AvailableVersion = "2" };

    private static ScanServices Sources() => new(
        (_, _) => Task.FromResult<IReadOnlyList<UpdateItem>>([]),
        _ => Task.FromResult(new QuickHardwareSnapshot(new HardwareScanResult(), new StorageHealthScanResult())),
        _ => Task.FromResult(new HardwareScanResult()),
        (_, _) => Task.FromResult(new DriverScanResult()),
        (_, _) => Task.FromResult(new OfficialDriverCatalogScanResult()),
        _ => Task.FromResult<IReadOnlyList<GameDependencyItem>>([]),
        _ => Task.FromResult(new StorageHealthScanResult()));

    private static void IdentityOrderAndEvents()
    {
        var vm = new MainViewModel(Sources());
        var first = Item("Contoso.Tool");
        first.IsSelected = false;
        first.Progress = 37;
        first.ResultDetails = "first result";
        var duplicate = Item("contoso.TOOL");
        var driver = Item("CONTOSO.TOOL", UpdateKind.Driver);
        var tail = Item("Tail");
        var added = new List<UpdateItem>();
        vm.Updates.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add) added.Add((UpdateItem)e.NewItems![0]!);
        };
        Add(vm, [first, first, duplicate, driver, tail]);
        Check(vm.Updates.SequenceEqual([first, driver, tail]) && added.SequenceEqual([first, driver, tail]),
            "Identity, insertion order or first-candidate priority changed.");
        Check(!first.IsSelected && first.Progress == 37 && first.ResultDetails == "first result" &&
              vm.SelectedCount == 2 && vm.UpdatesView.Cast<UpdateItem>().SequenceEqual([first, driver, tail]),
            "Selected state, details or the bound view changed.");

        var selectionNotifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.SelectedCount)) selectionNotifications++; };
        first.IsSelected = true;
        Check(selectionNotifications == 1 && vm.SelectedCount == 3, "Accepted item handler missing or subscribed twice.");
        duplicate.IsSelected = false;
        Check(selectionNotifications == 1, "Rejected duplicate received a handler.");
        vm.UpdateTypeFilter = "Errori";
        first.Status = "Errore";
        Check(vm.UpdatesView.Cast<UpdateItem>().SequenceEqual([first]), "Status handler no longer refreshes the filtered view.");
        selectionNotifications = 0;
        Add(vm, []);
        Check(selectionNotifications == 1, "Empty aggregation skipped the existing final notifications.");
    }

    private static void ExistingAndClearedCollection()
    {
        var vm = new MainViewModel(Sources());
        var existing = Item("Existing");
        vm.Updates.Add(existing);
        var next = Item("Next");
        Add(vm, [Item("EXISTING"), next]);
        Check(vm.Updates.SequenceEqual([existing, next]), "Existing collection was not used as the identity source.");
        var notifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.SelectedCount)) notifications++; };
        existing.IsSelected = false;
        Check(notifications == 0, "Seeding an index subscribed an externally supplied item.");
        Clear(vm);
        notifications = 0;
        next.IsSelected = false;
        Check(notifications == 0, "Clear failed to detach an accepted item.");
        var replacement = Item("NEXT");
        Add(vm, [replacement]);
        Check(vm.Updates.SequenceEqual([replacement]), "Cleared identity survived into another batch.");
        vm.Updates.Remove(replacement);
        var incremental = Item("next");
        Add(vm, [incremental]);
        Check(vm.Updates.SequenceEqual([incremental]), "An external removal left a stale identity.");
        vm.Updates.Clear();
        Add(vm, [replacement]);
        Check(vm.Updates.SequenceEqual([replacement]), "Direct collection Clear left a stale identity.");
    }

    private static void NullAndSpecialIds()
    {
        var vm = new MainViewModel(Sources());
        var empty = Item("");
        var space = Item(" ");
        var nul = Item("a\0b");
        var composed = Item("é");
        var decomposed = Item("e\u0301");
        var unknownKind = Item("", (UpdateKind)99);
        Add(vm, [empty, Item(""), space, Item(" "), nul, Item("A\0B"), composed, Item("É"), decomposed, unknownKind]);
        Check(vm.Updates.SequenceEqual([empty, space, nul, composed, decomposed, unknownKind]),
            "Empty/whitespace/Unicode/embedded-null IDs or unknown Kind changed equivalence.");

        vm = new MainViewModel(Sources());
        var prefix = Item("Prefix");
        var missing = Item(null);
        Add(vm, [prefix, missing]);
        Add(vm, [Item("PREFIX")]); // Original Any short-circuits before the null ID.
        Check(vm.Updates.SequenceEqual([prefix, missing]), "A matching prefix must precede the null-ID failure.");
        var otherKind = Item(null, UpdateKind.Driver);
        Add(vm, [otherKind]);
        Check(vm.Updates.SequenceEqual([prefix, missing, otherKind]), "A null ID of another Kind was rejected.");
        ExpectNullReference(() => Add(vm, [Item("New")]), "Existing null ID no longer fails at a same-Kind comparison.");
        Check(vm.Updates.SequenceEqual([prefix, missing, otherKind]), "Failure changed the partially aggregated collection.");

        vm = new MainViewModel(Sources());
        var firstNull = Item(null);
        Add(vm, [firstNull]);
        ExpectNullReference(() => Add(vm, [Item(null)]), "A second null ID was silently deduplicated.");
        Check(vm.Updates.Single() == firstNull, "First null-ID candidate was lost.");
    }

    private static async Task ProvidersAndRepeatedScansAsync()
    {
        var previous = new DateTime(2025, 4, 3, 2, 1, 0, DateTimeKind.Utc);
        JsonStorage.SaveSettings(new AppSettings { LastScanUtc = previous });
        var cancel = true;
        MainViewModel? vm = null;
        UpdateItem? software = null, microsoft = null, catalog = null;
        vm = new MainViewModel(Sources() with
        {
            Software = (_, _) =>
            {
                software = Item("App", source: "software");
                return Task.FromResult<IReadOnlyList<UpdateItem>>([software, Item("APP")]);
            },
            Drivers = (token, _) =>
            {
                if (cancel) { vm!.CancelScan(); return Task.FromCanceled<DriverScanResult>(token); }
                microsoft = Item("Driver", UpdateKind.Driver, "microsoft");
                microsoft.IsSelected = false;
                return Task.FromResult(new DriverScanResult { Updates = [microsoft, Item("DRIVER", UpdateKind.Driver)], SourceWarnings = ["fixture partial"] });
            },
            Catalog = (_, _) =>
            {
                catalog = Item("Second", UpdateKind.Driver, "catalog");
                return Task.FromResult(new OfficialDriverCatalogScanResult { Updates = [Item("driver", UpdateKind.Driver, "catalog"), catalog] });
            }
        });
        await vm.ScanAsync();
        Check(vm.Updates.SequenceEqual([software!]) && vm.Settings.LastScanUtc == previous && !vm.IsBusy,
            "Cancelled aggregation finalized or discarded the original partial result.");
        var cancelledItem = software;
        cancel = false;
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var oldItems = vm.Updates.ToArray();
            await vm.ScanAsync();
            Check(vm.Updates.SequenceEqual([software!, microsoft!, catalog!]) && !microsoft!.IsSelected &&
                  vm.ScannedCount == 6 && vm.Settings.LastScanUtc != previous &&
                  JsonStorage.LoadSettings().LastScanUtc == vm.Settings.LastScanUtc &&
                  oldItems.All(old => !vm.Updates.Contains(old)) && !vm.IsBusy,
                  "Cross-provider first-wins order, counts, partial completion or repeated scan changed.");
        }
        var notifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.SelectedCount)) notifications++; };
        cancelledItem!.IsSelected = false;
        Check(notifications == 0, "A previous scan item still has a handler.");
    }

    private static void ExpectNullReference(Action run, string message)
    {
        try { run(); }
        catch (NullReferenceException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void MeasureRemainingCost()
    {
        var previous = Environment.GetEnvironmentVariable("UPDATECENTER_SCAN_DIAGNOSTICS");
        Environment.SetEnvironmentVariable("UPDATECENTER_SCAN_DIAGNOSTICS", "1");
        try
        {
            foreach (var scenario in new[] { "insert-1000", "duplicates-1000", "refresh-1000" })
            {
                for (var repetition = -1; repetition < 20; repetition++)
                {
                    var vm = new MainViewModel(Sources());
                    var items = Enumerable.Range(0, 1000).Select(i => Item("Package-" + i)).ToList();
                    if (scenario != "insert-1000") Add(vm, items);
                    var candidates = scenario == "refresh-1000" ? [] : items.Concat(items.Take(100)).ToList();
                    using var scan = ScanDiagnostics.Start();
                    var allocated = GC.GetAllocatedBytesForCurrentThread();
                    var started = Stopwatch.GetTimestamp();
                    Add(vm, candidates);
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    scan!.Complete("success");
                    Check(vm.Updates.SequenceEqual(items), "Cost probe changed aggregation results.");
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        Kind = repetition < 0 ? "probe-warmup" : "probe", Scenario = scenario,
                        Repetition = repetition, Simulated = true, DurationMs = elapsed,
                        ThreadAllocatedBytes = allocated
                    }));
                }
            }
        }
        finally { Environment.SetEnvironmentVariable("UPDATECENTER_SCAN_DIAGNOSTICS", previous); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
