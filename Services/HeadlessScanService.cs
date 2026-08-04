using System.Runtime.InteropServices;
using UpdateCenter.Contracts;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class HeadlessScanService
{
    private readonly WinGetService _winGet = new();
    private readonly HardwareInventoryService _hardwareInventory = new();
    private readonly WindowsUpdateService _windowsUpdate = new();
    private readonly OfficialDriverCatalogService _officialDriverCatalog = new();
    private readonly GameDependencyService _gameDependencies = new();

    public async Task<ScanResult> ScanAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        var startedUtc = DateTime.UtcNow;
        var warnings = new List<string>();
        var updates = new Dictionary<string, UpdateItem>(StringComparer.OrdinalIgnoreCase);
        HardwareScanResult? hardware = null;
        var runtimeCheckCount = 0;

        void AddUpdates(IEnumerable<UpdateItem> items)
        {
            foreach (var item in items)
                updates.TryAdd($"{item.Kind}:{item.Id}", item);
        }

        if (request.IncludeSoftware || request.IncludeRuntimes)
        {
            try
            {
                var wingetUpdates = await _winGet.ScanAsync(request.IncludeUnknownVersions, cancellationToken);
                AddUpdates(wingetUpdates.Where(item =>
                    request.IncludeSoftware && item.Kind == UpdateKind.Software ||
                    request.IncludeRuntimes && item.Kind == UpdateKind.Runtime));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Software/WinGet: {ex.Message}");
                LogService.Write("Scansione headless WinGet fallita.", ex);
            }
        }

        if (request.IncludeDrivers)
        {
            try
            {
                hardware = await _hardwareInventory.ScanAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Inventario driver: {ex.Message}");
                LogService.Write("Inventario driver headless fallito.", ex);
            }

            try
            {
                var microsoft = await _windowsUpdate.ScanDriversAsync(cancellationToken, hardware?.Drivers);
                AddUpdates(microsoft.Updates);
                warnings.AddRange(microsoft.SourceWarnings.Select(x => $"Driver Microsoft: {x}"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Driver Microsoft: {ex.Message}");
                LogService.Write("Scansione headless dei driver Microsoft fallita.", ex);
            }

            if (hardware is not null)
            {
                try
                {
                    var official = await _officialDriverCatalog.ScanAsync(hardware.Drivers, cancellationToken);
                    AddUpdates(official.Updates);
                    warnings.AddRange(official.Warnings.Select(x => $"Catalogo driver: {x}"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warnings.Add($"Catalogo driver: {ex.Message}");
                    LogService.Write("Scansione headless del catalogo driver fallita.", ex);
                }
            }
        }

        if (request.IncludeRuntimes)
        {
            try
            {
                runtimeCheckCount = (await _gameDependencies.ScanAsync(cancellationToken)).Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Runtime: {ex.Message}");
                LogService.Write("Scansione headless dei runtime fallita.", ex);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var machinePreflight = PreflightService.CaptureMachineSnapshot();
        var orderedUpdates = updates.Values
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (orderedUpdates.Count > AgentProtocol.MaximumScanItems)
            warnings.Add($"Risultati limitati ai primi {AgentProtocol.MaximumScanItems} elementi.");
        return new ScanResult
        {
            StartedUtc = startedUtc,
            CompletedUtc = DateTime.UtcNow,
            MachineName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            UserName = Environment.UserName,
            Updates = orderedUpdates
                .Take(AgentProtocol.MaximumScanItems)
                .Select(Map)
                .ToList(),
            Warnings = warnings
                .Distinct(StringComparer.Ordinal)
                .Take(AgentProtocol.MaximumWarnings)
                .Select(x => Limit(x, 1_024))
                .ToList(),
            InstalledDriverCount = hardware?.Drivers.Count ?? 0,
            RuntimeCheckCount = runtimeCheckCount,
            HasBattery = machinePreflight.HasBattery,
            IsOnBattery = machinePreflight.IsOnBattery,
            BatteryPercentage = machinePreflight.BatteryPercentage,
            SystemDriveFreeBytes = machinePreflight.SystemDriveFreeBytes
        };
    }

    private static RemoteUpdateItem Map(UpdateItem item) => new()
    {
        Id = Limit(item.Id, 512),
        Name = Limit(item.Name, 512),
        Kind = item.Kind.ToString(),
        Publisher = Limit(item.Publisher, 512),
        InstalledVersion = Limit(item.InstalledVersion, 128),
        AvailableVersion = Limit(item.AvailableVersion, 128),
        Source = Limit(item.Source, 1_024),
        Status = Limit(item.Status, 256),
        ResultDetails = Limit(item.ResultDetails, 2_048),
        PackageOperation = Limit(item.PackageOperation, 64),
        CanInstall = item.CanInstall,
        RequiresRestart = item.RequiresRestart,
        IsImportant = item.IsImportant,
        IsOptional = item.IsOptional,
        RequiresRiskConfirmation = item.RequiresRiskConfirmation,
        DownloadSizeBytes = Math.Clamp(item.DownloadSizeBytes, 0, 1024L * 1024 * 1024 * 1024),
        HasUnverifiedInstallerMetadata = item.HasUnverifiedInstallerMetadata,
        WindowsUpdateId = item.WindowsUpdateId,
        WindowsUpdateRevision = item.WindowsUpdateRevision,
        WindowsUpdateServerSelection = item.WindowsUpdateServerSelection,
        WindowsUpdateServiceId = Limit(item.WindowsUpdateServiceId, 128),
        DriverInstallMode = Limit(item.DriverInstallMode, 64),
        OfficialReleasePageUrl = Limit(item.OfficialReleasePageUrl, 2_048),
        OfficialDownloadUrl = Limit(item.OfficialDownloadUrl, 2_048),
        ExpectedSha256 = Limit(item.ExpectedSha256, 128),
        ExpectedSignerSubjects = item.ExpectedSignerSubjects
            .Take(AgentProtocol.MaximumCollectionItemsPerUpdate)
            .Select(x => Limit(x, 512))
            .ToList(),
        DriverPackageType = Limit(item.DriverPackageType, 64),
        CompatibleHardwareIds = item.CompatibleHardwareIds
            .Take(AgentProtocol.MaximumCollectionItemsPerUpdate)
            .Select(x => Limit(x, 512))
            .ToList()
    };

    private static string Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
