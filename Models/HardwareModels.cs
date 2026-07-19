namespace UpdateCenter.Models;

public sealed class DriverInventoryItem
{
    public string DeviceName { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Provider { get; set; } = "";
    public string InstalledVersion { get; set; } = "—";
    public DateTime? DriverDate { get; set; }
    public string DeviceClass { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public List<string> HardwareIds { get; set; } = [];
    public List<string> CompatibleIds { get; set; } = [];
    public string InfName { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public bool IsProcessorOrChipset { get; set; }
    public bool HasUpdate { get; set; }
    public string AvailableVersion { get; set; } = "—";
    public string AvailableSource { get; set; } = "";
    public string SourceConfidence { get; set; } = "";
    public string CompatibilityDetail { get; set; } = "";
    public string Status => HasUpdate ? "Aggiornamento disponibile" : "Aggiornato";
    public string StatusDetail => HasUpdate
        ? $"{AvailableSource}: {AvailableVersion}"
        : "Nessuna proposta verificata dalle fonti ufficiali";
    public string DriverDateLabel => DriverDate?.ToString("dd/MM/yyyy") ?? "—";
    public string DisplayName => Quantity > 1 ? $"{DeviceName}  ×{Quantity}" : DeviceName;
    public string CategoryLabel => IsProcessorOrChipset ? "CPU / Chipset" :
        string.IsNullOrWhiteSpace(DeviceClass) ? "Dispositivo" : DeviceClass;
    public string ProviderLabel => string.IsNullOrWhiteSpace(Provider) ? Manufacturer : Provider;
}

public sealed class VendorSupportItem
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string ActionLabel { get; set; } = "Apri controllo ufficiale";
    public string SourceLabel { get; set; } = "Produttore ufficiale";
    public string ConfidenceLabel { get; set; } = "Fonte ufficiale";
    public string CompatibilityLabel { get; set; } = "Rilevato dall'hardware del PC";
}

public sealed class HardwareScanResult
{
    public string ComputerManufacturer { get; set; } = "";
    public string ComputerModel { get; set; } = "";
    public string CpuName { get; set; } = "Processore non rilevato";
    public string CpuManufacturer { get; set; } = "";
    public List<DriverInventoryItem> Drivers { get; set; } = [];
    public List<VendorSupportItem> VendorTools { get; set; } = [];
}
