using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class HardwareClipboardService
{
    public static string Build(
        SystemHardwareInfo hardware,
        IEnumerable<DriverInventoryItem> drivers,
        IEnumerable<StorageDeviceItem> storageDevices)
    {
        var driverList = drivers.ToList();
        var lines = new List<string>
        {
            $"CPU: {hardware.CpuName}",
            $"Core e thread: {hardware.CpuCores}",
            "",
            $"GPU: {hardware.GpuName}",
            $"VRAM principale: {hardware.VramTotal}",
            $"VRAM per GPU: {hardware.VramDetails}",
            "",
            $"RAM: {hardware.RamTotal}",
            $"Versione Windows: {hardware.OperatingSystem}",
            ""
        };

        AddDriverSection(lines, "Driver CPU / chipset:",
            driverList.Where(driver => driver.IsProcessorOrChipset));
        lines.Add("");
        AddDriverSection(lines, "Driver GPU:", driverList.Where(IsGpuDriver));
        lines.Add("");
        AddStorageSection(lines, storageDevices);
        return SanitizeClipboardText(string.Join(Environment.NewLine, lines));
    }

    private static void AddDriverSection(
        ICollection<string> lines,
        string title,
        IEnumerable<DriverInventoryItem> drivers)
    {
        lines.Add(title);
        var entries = drivers
            .Where(driver => !string.IsNullOrWhiteSpace(driver.DeviceName))
            .GroupBy(driver => $"{driver.DeviceName}\u001f{driver.InstalledVersion}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(driver => driver.DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (entries.Count == 0)
        {
            lines.Add("- Versione non ancora rilevata: avvia la scansione principale");
            return;
        }

        foreach (var driver in entries)
        {
            var provider = string.IsNullOrWhiteSpace(driver.ProviderLabel) ? "" : $" · {driver.ProviderLabel}";
            lines.Add($"- {driver.DeviceName}: {NormalizeVersion(driver.InstalledVersion)}{provider}");
        }
    }

    private static void AddStorageSection(ICollection<string> lines, IEnumerable<StorageDeviceItem> storageDevices)
    {
        lines.Add("Unità di storage interne:");
        var internalDrives = storageDevices
            .Where(device => !device.IsExternalUsb)
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (internalDrives.Count == 0)
        {
            lines.Add("- Unità non ancora rilevate: avvia la scansione principale");
            return;
        }

        foreach (var drive in internalDrives)
            lines.Add($"- {drive.Name}: {drive.MediaType}, {drive.SizeLabel}, salute {drive.HealthLabel}");
    }

    private static bool IsGpuDriver(DriverInventoryItem driver) =>
        driver.DeviceClass.Equals("Display", StringComparison.OrdinalIgnoreCase) ||
        driver.DeviceClass.Equals("DISPLAY", StringComparison.OrdinalIgnoreCase) ||
        driver.DeviceClass.Contains("video", StringComparison.OrdinalIgnoreCase) ||
        driver.DeviceClass.Contains("scheda video", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeVersion(string version) => string.IsNullOrWhiteSpace(version) || version == "—"
        ? "Versione non disponibile"
        : version;

    private static string SanitizeClipboardText(string value) => new(value
        .Where(character => character is '\r' or '\n' or '\t' || !char.IsControl(character))
        .ToArray());
}
