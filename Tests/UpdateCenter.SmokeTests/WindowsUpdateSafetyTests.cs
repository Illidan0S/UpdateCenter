using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class WindowsUpdateSafetyTests
{
    public static void Run()
    {
        var failures = new List<string>();
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        foreach (var (name, edit) in new (string, Action<FakeDriverOffer>)[]
        {
            ("firmware class", d => d.DriverClass = "Firmware"),
            ("BIOS class", d => d.DriverClass = "BIOS"),
            ("UEFI class", d => d.DriverClass = "UEFI"),
            ("firmware class GUID", d => d.DriverClass = "{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}"),
            ("UEFI resource", d => d.DriverHardwareID = @"UEFI\RES_{1234}"),
            ("compatible UEFI resource", d => d.DriverHardwareID = @"ACPI\UEFI_RES_1234"),
            ("firmware title", d => d.Title = "Contoso Firmware Update 2.0"),
            ("BIOS model", d => d.DriverModel = "System BIOS"),
            ("UEFI description", d => d.Description = "Aggiornamento UEFI"),
            ("generic", d => d.DriverClass = "Driver"),
            ("unknown class", d => d.DriverClass = "Classe locale sconosciuta"),
            ("security device", d => d.DriverClass = "SecurityDevices"),
            ("system", d => d.DriverClass = "System"),
            ("processor", d => d.DriverClass = "Processor"),
            ("extension", d => d.DriverClass = "Extension"),
            ("software component", d => d.DriverClass = "SoftwareComponent"),
            ("missing hardware identity", d => d.DriverHardwareID = ""),
            ("unreadable description", d => d.DescriptionReadable = false)
        })
        {
            var driver = new FakeDriverOffer();
            edit(driver);
            var session = new FakeUpdateSession(driver);
            var installed = new DriverInventoryItem { DeviceName = "Contoso Video", DeviceId = @"PCI\VEN_1234", DeviceClass = "Display" };
            var scan = WindowsUpdateService.ScanDrivers(default, [installed], session.Create);
            Check(scan.Updates.Count == 0 && !installed.HasUpdate, $"{name}: scan offered the update or flagged inventory");

            // Even a saved, hand-crafted ordinary video-driver plan must use the fresh WUA metadata.
            var plan = new PlanItem { Name = "Contoso Video", WindowsUpdateId = driver.Identity.UpdateID, WindowsUpdateRevision = 4 };
            var result = WindowsUpdateService.InstallDriver(plan, null, session.Create);
            Check(!result.Success && result.Phase == "windows-update-safety" && driver.EulaCalls == 0 &&
                session.CollectionCalls == 0 && session.DownloaderCalls == 0 && session.InstallerCalls == 0,
                $"{name}: install phase={result.Phase}, EULA={driver.EulaCalls}, collections={session.CollectionCalls}, downloaders={session.DownloaderCalls}, installers={session.InstallerCalls}");
        }

        var allowed = new FakeDriverOffer();
        var safeSession = new FakeUpdateSession(allowed);
        var inventory = new DriverInventoryItem { DeviceName = "Contoso Video", DeviceId = allowed.DriverHardwareID };
        Check(WindowsUpdateService.ScanDrivers(default, [inventory], safeSession.Create).Updates.Single().WindowsUpdateId ==
            allowed.Identity.UpdateID && inventory.HasUpdate, "A specific display update should remain available");
        var safeResult = WindowsUpdateService.InstallDriver(new PlanItem
        {
            WindowsUpdateId = allowed.Identity.UpdateID, WindowsUpdateRevision = 4
        }, null, safeSession.Create);
        Check(safeResult.Phase == "windows-update-download" && allowed.EulaCalls == 1 && safeSession.DownloaderCalls == 1,
            "Allowed display update never reached the simulated downloader");

        allowed.Identity.UpdateID = "";
        Check(WindowsUpdateService.ScanDrivers(default, [], safeSession.Create).Updates.Count == 0,
            "A missing Windows Update identity was offered");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }
}

// Public members mirror IDispatch, so the production dynamic binder sees the same properties.
public sealed class FakeDriverOffer
{
    public FakeUpdateIdentity Identity { get; } = new();
    public string Title { get; set; } = "Contoso Display 2.0";
    private string _description = "Display device update";
    public bool DescriptionReadable { get; set; } = true;
    public string Description { get => DescriptionReadable ? _description : throw new InvalidOperationException(); set => _description = value; }
    public string DriverManufacturer => "Contoso";
    public string DriverModel { get; set; } = "Contoso Video";
    public string DriverClass { get; set; } = "Display";
    public string DriverHardwareID { get; set; } = @"PCI\VEN_1234";
    public bool VersionDateReadable { get; set; } = true;
    public DateTime DriverVerDate => VersionDateReadable ? new(2026, 1, 1) : throw new InvalidOperationException();
    public long MaxDownloadSize => 1024;
    public bool RebootRequired => false;
    public string MsrcSeverity => "";
    public bool IsMandatory => false;
    public bool AutoSelectOnWebSites { get; set; } = true;
    public bool BrowseOnly { get; set; }
    public FakeWarningCollection SecurityBulletinIDs => new();
    public bool EulaAccepted => false;
    public int EulaCalls { get; private set; }
    public void AcceptEula() => EulaCalls++;
}

public sealed class FakeUpdateIdentity
{
    public string UpdateID { get; set; } = "bdab5028-0844-41bb-966a-b95d10aaa202";
    public int RevisionNumber { get; set; } = 4;
}

public sealed class FakeUpdateSession(FakeDriverOffer driver)
{
    public int CollectionCalls { get; private set; }
    public int DownloaderCalls { get; private set; }
    public int InstallerCalls { get; private set; }
    public object? Create(string progId)
    {
        switch (progId)
        {
            case "Microsoft.Update.ServiceManager": return new CoverageWuaManager(false);
            case "Microsoft.Update.Session": return this;
            case "Microsoft.Update.UpdateColl": CollectionCalls++; return new FakeUpdateCollection();
            default: throw new InvalidOperationException(progId);
        }
    }

    public string ClientApplicationID { get; set; } = "";
    public FakeUpdateSession CreateUpdateSearcher() => this;
    public bool Online { get; set; }
    public bool IncludePotentiallySupersededUpdates { get; set; }
    public int ServerSelection { get; set; }
    public FakeSearchResult Search(string criteria) => new(driver);
    public FakeDownloader CreateUpdateDownloader() { DownloaderCalls++; return new FakeDownloader(); }
    public object CreateUpdateInstaller() { InstallerCalls++; throw new InvalidOperationException("Unexpected installer"); }
}

public sealed class FakeSearchResult(FakeDriverOffer driver)
{
    public int ResultCode => 2;
    public FakeWarningCollection Warnings => new();
    public FakeSearchResult Updates => this;
    public int Count => 1;
    public FakeDriverOffer Item(int index) => index == 0 ? driver : throw new ArgumentOutOfRangeException(nameof(index));
}

public sealed class FakeWarningCollection
{
    public int Count { get; set; }
}

public sealed class FakeUpdateCollection
{
    public void Add(FakeDriverOffer driver) { }
}

public sealed class FakeDownloader
{
    public object? Updates { get; set; }
    public FakeDownloader Download() => this;
    public int ResultCode => 4;
}
