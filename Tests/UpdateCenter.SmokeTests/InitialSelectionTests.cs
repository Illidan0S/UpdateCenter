using System.Windows.Threading;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class InitialSelectionTests
{
    internal static Task RunAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await Exercise(); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }

    private static UpdateItem Item(string id, bool risk = false, bool blocked = false) => new()
    {
        Id = id, Name = id, Kind = UpdateKind.Software, Source = "winget", InstalledVersion = "1.0", AvailableVersion = "2.0",
        IsSelected = false, RequiresRiskConfirmation = risk, CanInstall = !blocked
    };

    private static ScanServices Sources(IReadOnlyList<UpdateItem> items) => new(
        (_, _) => Task.FromResult(items),
        _ => Task.FromResult(new QuickHardwareSnapshot(new HardwareScanResult(), new StorageHealthScanResult())),
        _ => Task.FromResult(new HardwareScanResult()),
        (_, _) => Task.FromResult(new DriverScanResult()),
        (_, _) => Task.FromResult(new OfficialDriverCatalogScanResult()),
        _ => Task.FromResult<IReadOnlyList<GameDependencyItem>>([]),
        _ => Task.FromResult(new StorageHealthScanResult()));

    private static async Task Exercise()
    {
        JsonStorage.SaveHistory([]);
        var ordinary = Item("Example.Standard");
        var warning = Item("Example.Information"); warning.HasUnverifiedInstallerMetadata = true;
        var risk = Item("ElectronicArts.EADesktop", risk: true);
        var blocked = Item("Example.Blocked", blocked: true);
        var vm = new MainViewModel(Sources([ordinary, warning, risk, blocked]));
        await vm.ScanAsync();
        Check(vm.Progress == 100 && ordinary.IsSelected && warning.IsSelected && !risk.IsSelected && !blocked.IsSelected &&
            vm.SelectedCount == 2, "Completed scan did not preselect all ordinary admissible results, independently of priority/informational warnings.");
        Console.WriteLine("PASS OrdinaryCompletionSelection");
        await ManualChoicesAndChangingEligibility();
        await BulkChoicesDuringScan();
        await CancellationAndNewScan();
        SectionsSearchAndStability();
        RunnerConsentTests.CheckSelectionConsentBoundary();
    }
    private static async Task ManualChoicesAndChangingEligibility()
    {
        var manual = Item("Example.Manual"); var ordinary = Item("Example.Ordinary");
        var blocked = Item("Example.Blocked"); var restored = Item("Example.Restored", blocked: true);
        var risk = Item("Example.NewRisk");
        var release = new TaskCompletionSource<DriverScanResult>(); var entered = new TaskCompletionSource();
        var late = Item("Example.Late");
        var vm = new MainViewModel(Sources([manual, ordinary, blocked, restored, risk]) with
        {
            Drivers = (_, _) => { entered.SetResult(); return release.Task; },
            Catalog = (_, _) => Task.FromResult(new OfficialDriverCatalogScanResult { Updates = [late] })
        });
        var scan = vm.ScanAsync(); await entered.Task;
        Check(vm.SelectedCount == 0 && !vm.CanUpdate, "Provider defaults were published as automatic selections before completion.");
        manual.IsSelected = true; manual.IsSelected = false;
        blocked.CanInstall = false; restored.CanInstall = true;
        risk.IsSelected = true; risk.RequiresRiskConfirmation = true;
        release.SetResult(new DriverScanResult()); await scan;
        Check(!manual.IsSelected && ordinary.IsSelected && !blocked.IsSelected && restored.IsSelected &&
            !risk.IsSelected && late.IsSelected && vm.SelectedCount == 3, "Manual choice or latest eligibility was overwritten.");
        var order = vm.ManagedUpdatesView.Cast<UpdateItem>().ToArray();
        vm.SetAllSelected(false);
        typeof(MainViewModel).GetMethod("ApplyInitialScanSelection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, null);
        Check(vm.SelectedCount == 0 && !vm.CanUpdate && order.SequenceEqual(vm.ManagedUpdatesView.Cast<UpdateItem>()),
            "Initial selection repeated or changing checkboxes reordered rows.");
        ordinary.CanInstall = false; ordinary.CanInstall = true;
        Check(!ordinary.IsSelected, "Restored eligibility caused a late automatic selection.");
        var after = Item("Example.After"); after.IsSelected = true;
        Add(vm, [after]);
        Check(!after.IsSelected && vm.SelectedCount == 0, "A late provider result undid Deselect All.");
        Console.WriteLine("PASS ManualChoicesAndChangingEligibility");
    }

    private static async Task BulkChoicesDuringScan()
    {
        foreach (var action in new[] { "accept", "reject", "clear" })
        {
            var ordinary = Item("Example.Standard"); var risk = Item("ElectronicArts.EADesktop", risk: true);
            var late = Item("Example.Late");
            var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<DriverScanResult>();
            var vm = new MainViewModel(Sources([ordinary, risk]) with
            {
                Drivers = (_, _) => { entered.SetResult(); return release.Task; },
                Catalog = (_, _) => Task.FromResult(new OfficialDriverCatalogScanResult { Updates = [late] })
            });
            var scan = vm.ScanAsync(); await entered.Task;
            if (action == "clear") vm.SetAllSelected(false);
            else vm.SelectAllWithConfirmation(_ => action == "accept");
            release.SetResult(new DriverScanResult()); await scan;
            Check(!late.IsSelected && ordinary.IsSelected == (action != "clear") && risk.IsSelected == (action == "accept") &&
                risk.RequiresRiskConfirmation, "Completion overrode explicit bulk consent/deselection or selected a later arrival.");
        }
        Console.WriteLine("PASS BulkChoicesDuringScan");
    }

    private static async Task CancellationAndNewScan()
    {
        var ordinary = Item("Example.Standard");
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<DriverScanResult>();
        var first = true;
        var vm = new MainViewModel(Sources([ordinary]) with { Drivers = (_, _) =>
        {
            if (!first) return Task.FromResult(new DriverScanResult());
            first = false; entered.SetResult(); return release.Task;
        } });
        var scan = vm.ScanAsync(); await entered.Task; vm.CancelScan(); release.SetResult(new DriverScanResult()); await scan;
        Check(!ordinary.IsSelected && vm.Progress < 100, "Cancelled scan applied final preselection.");
        var late = Item("Example.AfterCancellation"); late.IsSelected = true; Add(vm, [late]);
        Check(!late.IsSelected, "A refresh after cancellation restored provider-selected defaults.");
        await vm.ScanAsync();
        Check(ordinary.IsSelected && vm.SelectedCount == 1 && vm.Progress == 100, "New scan did not reset initial-selection lifecycle.");
        Console.WriteLine("PASS CancellationAndNewScan");
    }

    private static void SectionsSearchAndStability()
    {
        var vm = new MainViewModel(null!); var regular = Item("Example.Regular"); var risk = Item("ElectronicArts.EADesktop", risk: true);
        var blocked = Item("Example.Blocked", blocked: true); blocked.Status = "Exact scope mismatch";
        Add(vm, [regular, risk, blocked]);
        Check(vm.ManagedUpdatesView.Cast<UpdateItem>().Count() == 2 && vm.BlockedUpdatesView.Cast<UpdateItem>().Single() == blocked &&
            !vm.IsBlockedUpdatesExpanded && vm.VisibleUpdateCount == 3, "Views copied/dropped/misclassified targets.");
        vm.SearchText = "Blocked";
        Check(vm.ManagedUpdatesView.IsEmpty && vm.IsBlockedUpdatesExpanded && vm.VisibleUpdateCount == 1, "Blocked-only search hid its matches.");
        vm.IsBlockedUpdatesExpanded = false; vm.NotifyCounts(); blocked.Status = "Exact architecture mismatch";
        Check(!vm.IsBlockedUpdatesExpanded, "Manual collapse was undone without a new query.");
        vm.SearchText = "Regular"; Check(vm.BlockedUpdatesView.IsEmpty && vm.ManagedUpdatesView.Cast<UpdateItem>().Single() == regular,
            "Search did not filter both views.");
        vm.SearchText = ""; regular.IsSelected = true; regular.CanInstall = false;
        Check(!regular.IsSelected && !vm.SelectedItems.Contains(regular) && vm.BlockedUpdatesView.Cast<UpdateItem>().Contains(regular) &&
            vm.ManagedUpdatesView.Cast<UpdateItem>().Single() == risk, "Eligibility drift did not move the same row out of the installable view.");
        vm.UpdateTypeFilter = "Runtime";
        Check(vm.ManagedUpdatesView.IsEmpty && vm.BlockedUpdatesView.IsEmpty && vm.VisibleUpdateCount == 0, "Type filter disagrees across sections.");
        vm.UpdateTypeFilter = "Tutti"; vm.SearchText = "New reason";
        Check(vm.BlockedUpdatesView.IsEmpty, "Unmatched reason unexpectedly visible.");
        blocked.Status = "New reason";
        Check(vm.IsBlockedUpdatesExpanded && vm.BlockedUpdatesView.Cast<UpdateItem>().Single() == blocked && vm.VisibleUpdateCount == 1,
            "Changing status did not update search matches and expansion.");
        vm.SearchText = "";
        var edit = (System.ComponentModel.IEditableCollectionView)vm.ManagedUpdatesView;
        edit.EditItem(risk); risk.CanInstall = false; edit.CommitEdit();
        awaitDispatcherRefresh();
        Check(vm.ManagedUpdatesView.IsEmpty && vm.BlockedUpdatesView.Cast<UpdateItem>().Count() == 3,
            "An eligibility change during WPF editing left a stale section after commit.");
        Console.WriteLine("PASS SectionsSearchAndStability");
        static void awaitDispatcherRefresh() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Add(MainViewModel vm, IEnumerable<UpdateItem> items) => typeof(MainViewModel)
        .GetMethod("AddUpdates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, [items]);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
