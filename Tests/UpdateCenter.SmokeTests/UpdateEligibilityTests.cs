using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class UpdateEligibilityTests
{
    internal static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Exercise(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static UpdateItem Item(string id) => new()
    {
        Id = id, Name = id, Kind = UpdateKind.Software, Source = "winget",
        InstalledVersion = "1.0", AvailableVersion = "2.0"
    };

    private static void Exercise()
    {
        var failures = new List<string>();
        void Check(bool pass, string message) { if (!pass) failures.Add(message); }
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            foreach (var risk in new[] { false, true })
            {
                var blocked = Item("Fixture.Blocked");
                blocked.HasUnverifiedInstallerMetadata = true;
                blocked.RequiresRiskConfirmation = risk;
                blocked.CanInstall = false;
                blocked.IsSelected = true;
                Check(blocked.PriorityLabel == LocalizationService.Text("Solo verifica", "Review only"),
                    language + ": blocked eligibility must take precedence over metadata/risk priority.");
                Check(!blocked.PriorityDescription.Contains(LocalizationService.Text("può essere selezionato", "can be selected")) &&
                    blocked.PriorityDescription == LocalizationService.Text(
                        "Questo elemento non è selezionabile in UpdateCenter. Consulta la colonna Stato per i dettagli.",
                        "This item cannot be selected in UpdateCenter. See the Status column for details."),
                    language + ": the blocked tooltip must describe eligibility without prescribing a manual update.");
                Check(!blocked.IsSelected && !blocked.CanRetry, "Presentation change bypassed eligibility.");
            }

            var mismatch = Item("Fixture.Scope");
            WinGetManifestSafetyService.ApplyScopeCompatibility(mismatch, "machine", "user");
            Check(!mismatch.CanInstall && !mismatch.IsSelected, "Unresolved scope was automatically admitted.");
            Check(!mismatch.ResultDetails.Contains("WinGet") && mismatch.ResultDetails.Contains(
                LocalizationService.Text("non confermato", "not confirmed")),
                language + ": heuristic scope comparison is presented as an authoritative provider decision.");
            var sameScope = Item("Fixture.SameScope");
            WinGetManifestSafetyService.ApplyScopeCompatibility(sameScope, "machine", "machine");
            Check(sameScope.CanInstall && sameScope.IsSelected, "Same-scope selection changed.");
        }

        // No real provider can run; all persistence stays under Program's GUID root.
        var services = new ScanServices(
            (_, _) => throw new InvalidOperationException("Unexpected software scan"),
            _ => throw new InvalidOperationException("Unexpected hardware snapshot"),
            _ => throw new InvalidOperationException("Unexpected hardware scan"),
            (_, _) => throw new InvalidOperationException("Unexpected driver scan"),
            (_, _) => throw new InvalidOperationException("Unexpected catalog scan"),
            _ => throw new InvalidOperationException("Unexpected dependency scan"),
            _ => throw new InvalidOperationException("Unexpected storage scan"));
        var vm = new MainViewModel(services);
        var batch = new List<UpdateItem>();
        for (var i = 0; i < 31; i++) batch.Add(Item("Fixture.Normal" + i));
        for (var i = 0; i < 11; i++)
        {
            var review = Item("Fixture.Review" + i);
            review.HasUnverifiedInstallerMetadata = true;
            review.IsSelected = false;
            batch.Add(review);
        }
        for (var i = 0; i < 7; i++)
        {
            var blocked = Item("Fixture.Blocked" + i);
            blocked.CanInstall = false;
            batch.Add(blocked);
        }
        foreach (var id in new[] { "ElectronicArts.EADesktop", "Anaconda.Miniconda3" })
        {
            var risk = Item(id);
            risk.RequiresRiskConfirmation = true;
            risk.IsSelected = false;
            batch.Add(risk);
        }
        // Publish through the real aggregation path so count notifications are exercised too.
        var previousDiagnostics = Environment.GetEnvironmentVariable("UPDATECENTER_SCAN_DIAGNOSTICS");
        Environment.SetEnvironmentVariable("UPDATECENTER_SCAN_DIAGNOSTICS", "1");
        try
        {
            using var scan = ScanDiagnostics.Start();
            var addUpdates = typeof(MainViewModel).GetMethod("AddUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!;
            addUpdates.Invoke(vm, [batch.Take(31)]);
            addUpdates.Invoke(vm, [batch]); // Repeated candidates must not double-count selection diagnostics.
            scan!.Complete("success");
        }
        finally { Environment.SetEnvironmentVariable("UPDATECENTER_SCAN_DIAGNOSTICS", previousDiagnostics); }
        var diagnosticLine = Directory.GetFiles(AppPaths.LogsDirectory, "*.log")
            .SelectMany(File.ReadLines).Last(x => x.Contains("operation=scan-baseline"));
        using (var report = JsonDocument.Parse(diagnosticLine[(diagnosticLine.IndexOf("details=", StringComparison.Ordinal) + 8)..]))
        {
            var counts = report.RootElement.GetProperty("Counts");
            foreach (var (name, expected) in new[]
            {
                ("updates.initial-selected", 31), ("updates.bulk-eligible", 42),
                ("updates.individual-risk", 2), ("updates.blocked", 7), ("updates.unknown-metadata", 11)
            })
                Check(counts.TryGetProperty(name, out var value) && value.GetInt32() == expected,
                    "Selection diagnostic count missing or incorrect: " + name);
            Check(!diagnosticLine.Contains("Fixture.") && !diagnosticLine.Contains("Anaconda"),
                "Selection diagnostics must not persist package identities.");
        }
        var countNotifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.SelectedCount)) countNotifications++; };
        Check(vm.AvailableCount == 51 && vm.SelectedCount == 31,
            "Unknown metadata, risk and blocked items must not be preselected in the fixture.");
        var reviewItem = batch[31];
        reviewItem.IsSelected = true;
        Check(vm.SelectedCount == 32 && countNotifications > 0 && reviewItem.HasUnverifiedInstallerMetadata,
            "Individual review selection must notify counts without erasing metadata uncertainty.");
        reviewItem.IsSelected = false;
        vm.SetAllSelected(true);
        Check(vm.AvailableCount == 51 && vm.SelectedCount == 42 && vm.Updates.Count(x => x.CanInstall) == 44,
            "Selection, eligibility and risk consent were conflated.");
        var conditional = vm.Updates.Last();
        conditional.IsSelected = true;
        Check(vm.SelectedCount == 43 && conditional.RequiresRiskConfirmation,
            "Individual selection must retain the requirement for risk confirmation.");
        vm.SetAllSelected(true);
        Check(vm.SelectedCount == 43 && conditional.IsSelected && conditional.RequiresRiskConfirmation &&
            !vm.Updates.Single(x => x.Id == "ElectronicArts.EADesktop").IsSelected,
            "Select All must preserve an explicit risk selection without adding another risk item.");
        countNotifications = 0;
        reviewItem.CanInstall = false;
        Check(!reviewItem.IsSelected && vm.SelectedCount == 42 && countNotifications > 0 &&
            !vm.SelectedItems.Contains(reviewItem),
            "A newly blocked item must leave the selection, plan candidates and count immediately.");
        reviewItem.CanInstall = true;
        Check(!reviewItem.IsSelected && vm.SelectedCount == 42,
            "Restored eligibility must not silently restore a user's previous selection.");
        vm.SetAllSelected(true);
        Check(vm.SelectedCount == 43 && reviewItem.IsSelected,
            "Select All must include a review item once its blocking condition is removed.");
        vm.SetAllSelected(false);
        Check(vm.SelectedCount == 0 && vm.Updates.Count(x => x.CanInstall) == 44,
            "Manual deselection changed eligibility.");
        vm.SetAllSelected(true);
        Check(vm.SelectedCount == 42 && !conditional.IsSelected && conditional.RequiresRiskConfirmation,
            "After Deselect All, bulk selection must require a new individual risk choice.");
        foreach (var item in vm.Updates.Where(x => x.RequiresRiskConfirmation)) item.IsSelected = true;
        vm.SetAllSelected(true);
        Check(vm.SelectedCount == 44 && vm.SelectedItems.Count(x => x.RequiresRiskConfirmation) == 2,
            "Both explicit risk choices must survive bulk selection with their consent requirements intact.");
        vm.SetAllSelected(false);
        Check(vm.SelectedCount == 0 && !vm.CanUpdate, "Deselect All must also disable the update action.");

        var bulk = typeof(MainViewModel).GetMethod("SelectAllWithConfirmation");
        Check(bulk is not null, "BulkRiskConfirmation: explicit confirmation route is missing.");
        if (bulk is not null)
        {
            var ea = Item("ElectronicArts.EADesktop");
            ea.RequiresRiskConfirmation = true;
            ea.IsSelected = false;
            vm.Updates.Clear();
            vm.Updates.Add(ea);
            bulk.Invoke(vm, [new Func<UpdateItem, bool>(_ => false)]);
            Check(!ea.IsSelected && vm.SelectedCount == 0, "Declined EA confirmation changed selection.");
            bulk.Invoke(vm, [new Func<UpdateItem, bool>(_ => true)]);
            Check(ea.IsSelected && ea.RequiresRiskConfirmation && vm.SelectedCount == 1, "Confirmed EA was excluded or lost its execution consent requirement.");
            ea.IsSelected = false;
            var late = Item("Fixture.Late"); late.IsSelected = false;
            bulk.Invoke(vm, [new Func<UpdateItem, bool>(_ => { vm.Updates.Add(late); ea.CanInstall = false; return true; })]);
            Check(!ea.IsSelected && !late.IsSelected && vm.SelectedCount == 0, "Bulk selection admitted a blocked or late-arriving item.");
            ea.CanInstall = true;
            bulk.Invoke(vm, [new Func<UpdateItem, bool>(_ => {
                typeof(UpdateItem).GetProperty(nameof(UpdateItem.AvailableVersion))!.SetValue(ea, "3.0");
                return true;
            })]);
            Check(!ea.IsSelected, "A changed EA target reused the selection confirmation.");
            bulk.Invoke(vm, [new Func<UpdateItem, bool>(_ => { vm.Updates.Remove(ea); return true; })]);
            Check(!ea.IsSelected && vm.SelectedCount == 1, "Removed target retained bulk confirmation.");
            Console.WriteLine("PASS BulkRiskConfirmation: EA accept/reject, eligibility drift, late arrivals and counters.");
        }

        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS blocked priority and tooltip precedence, IT/EN (including risk + unknown metadata).");
        Console.WriteLine("PASS scope explanation is qualified; unresolved scope remains blocked.");
        Console.WriteLine("PASS simulated 31/42/51 selection versus 44 eligible; individual review/risk choices, bulk preservation, deselection and eligibility-change notifications.");
    }
}
