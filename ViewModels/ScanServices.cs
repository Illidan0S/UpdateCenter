using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter.ViewModels;

internal sealed record ScanServices(
    Func<bool, CancellationToken, Task<IReadOnlyList<UpdateItem>>> Software,
    Func<CancellationToken, Task<QuickHardwareSnapshot>> QuickHardware,
    Func<CancellationToken, Task<HardwareScanResult>> Hardware,
    Func<CancellationToken, IReadOnlyList<DriverInventoryItem>?, Task<DriverScanResult>> Drivers,
    Func<IReadOnlyList<DriverInventoryItem>, CancellationToken, Task<OfficialDriverCatalogScanResult>> Catalog,
    Func<CancellationToken, Task<IReadOnlyList<GameDependencyItem>>> Dependencies,
    Func<CancellationToken, Task<StorageHealthScanResult>> Storage)
{
    internal static ScanServices Create()
    {
        var hardware = new HardwareInventoryService();
        var storage = new StorageHealthService();
        return new(new WinGetService().ScanAsync, new QuickHardwareDataService(hardware, storage).LoadAsync,
            hardware.ScanAsync, new WindowsUpdateService().ScanDriversAsync, new OfficialDriverCatalogService().ScanAsync,
            new GameDependencyService().ScanAsync, storage.ScanAsync);
    }
}
