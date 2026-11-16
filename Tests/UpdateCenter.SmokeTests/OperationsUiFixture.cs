using System.Reflection;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class OperationsUiFixture
{
    // Synthetic data only. The fixture never calls scan, repair, install or network services.
    public static void Populate(MainViewModel vm, int count)
    {
        Invoke(vm, "ClearUpdates");
        vm.DriverInventory.Clear(); vm.DriverProblems.Clear(); vm.VendorTools.Clear(); vm.History.Clear();
        vm.SearchText = ""; vm.UpdateTypeFilter = "Tutti";
        vm.DriverSearchText = ""; vm.DriverInventoryFilter = "Tutti";
        var items = Enumerable.Range(0, count).Select(i => new UpdateItem
        {
            Id = $"QA.NotARealPackage.{i}",
            Name = i % 5 == 0 ? $"QA {i:0000} · Suite produttività con componenti aggiuntivi e nome molto lungo" : $"QA {i:0000} · Application component",
            Kind = i % 5 == 1 ? UpdateKind.Driver : i % 5 == 4 ? UpdateKind.Runtime : UpdateKind.Software,
            InstalledVersion = "1.2.3456.7890", AvailableVersion = "2.3.4567.8901", Publisher = "QA publisher",
            Source = i % 5 == 1 ? "Windows Update" : "WinGet",
            SourceConfidence = LocalizationService.Text("Dati simulati", "Simulated data"),
            CanInstall = i % 5 != 1, RequiresRiskConfirmation = i % 5 == 2,
            HasUnverifiedInstallerMetadata = i % 5 == 4, IsSelected = i % 5 == 0,
            IsImportant = i % 5 == 0, RequiresRestart = i % 5 == 0, Size = "128 MB",
            Status = i % 5 == 3 ? "Errore" : "Da aggiornare",
            ResultDetails = i % 5 == 3 ? LocalizationService.Text("Errore simulato: dettagli lunghi per verificare leggibilità e accesso al messaggio completo.", "Simulated error: long details to verify readability and access to the complete message.") : "",
            OfficialReleasePageUrl = "https://example.invalid/qa/source",
            CompatibilityDetail = "QA only — no real package or device"
        }).ToList();
        Invoke(vm, "AddUpdates", items);
        for (var i = 0; i < count; i++)
        {
            vm.DriverInventory.Add(new DriverInventoryItem
            {
                DeviceName = $"QA {i:0000} · Device with a long descriptive name — controller and chipset interface",
                Provider = "QA vendor", DeviceClass = i % 2 == 0 ? "System" : "Display",
                DeviceId = $"QA\\DEVICE_{i}", InstalledVersion = "10.20.300.4000",
                DriverDate = new DateTime(2026, 10, 1), IsProcessorOrChipset = i % 2 == 0,
                HasUpdate = i % 3 == 0, AvailableVersion = "11.30.400.5000", AvailableSource = "Windows Update"
            });
            vm.History.Add(new HistoryEntry
            {
                Timestamp = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-i),
                Name = items[i].Name, Kind = items[i].KindLabel, FromVersion = items[i].InstalledVersion,
                ToVersion = items[i].AvailableVersion, Result = i % 3 == 0 ? "Fallito" : i % 3 == 1 ? "Riuscito" : "Annullato",
                Details = i % 3 == 0 ? items[3 % count].ResultDetails : LocalizationService.Text("Nessun aggiornamento è stato eseguito.", "No updates were performed."),
                Diagnostics = "QA fixture: deliberately synthetic diagnostic details. No installer was run."
            });
        }
        if (count > 0)
        {
            vm.DriverProblems.Add(new DriverProblemItem { DeviceName = "QA · Device requiring a verified source", DeviceId = "QA\\SEARCH", ErrorCode = 28, ErrorTitle = LocalizationService.Text("Driver non installato", "Driver not installed") });
            vm.DriverProblems.Add(new DriverProblemItem { DeviceName = "QA · Signed installed package", DeviceId = "QA\\REPAIR", ErrorCode = 10, InstalledInfName = "oem42.inf", InstalledDriverSigned = true });
            vm.DriverProblems.Add(new DriverProblemItem { DeviceName = "QA · Unsupported repair", DeviceId = "QA\\DISABLED", ErrorCode = 0 });
            vm.VendorTools.Add(new VendorSupportItem { Name = "QA · Official support", Description = LocalizationService.Text("Collegamento dimostrativo, senza installazione automatica.", "Demonstration link, without automatic installation."), Url = "https://example.invalid/qa/support" });
        }
        Set(vm, "StatusText", count == 0 ? LocalizationService.Text("Pronto per la scansione", "Ready to scan") : LocalizationService.Text("Scansione completata", "Scan completed"));
        Set(vm, "CpuName", "QA processor");
        Set(vm, "CurrentItemText", LocalizationService.Text("Profilo QA isolato · dati simulati · nessuna installazione", "Isolated QA profile · simulated data · no installation"));
        Set(vm, "HardwareCheckStatus", LocalizationService.Text("Dati simulati per la verifica dell'interfaccia.", "Simulated data for interface verification."));
        Set(vm, "IsBusy", false); Set(vm, "IsScanRunning", false); Set(vm, "Progress", count == 0 ? 0d : 100d);
        vm.NotifyLanguageChanged();
    }

    public static void Set(MainViewModel vm, string name, object value) => typeof(MainViewModel).GetProperty(name)!.SetValue(vm, value);
    private static void Invoke(MainViewModel vm, string name, params object[] args) =>
        typeof(MainViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, args);
}
