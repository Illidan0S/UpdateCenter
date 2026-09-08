using UpdateCenter.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UpdateCenter.Models;

public sealed class DriverInventoryItem : INotifyPropertyChanged
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
    public bool IsSigned { get; set; }
    public int Quantity { get; set; } = 1;
    public bool IsProcessorOrChipset { get; set; }
    public bool HasUpdate { get; set; }
    public string AvailableVersion { get; set; } = "—";
    public string AvailableSource { get; set; } = "";
    public string SourceConfidence { get; set; } = "";
    public string CompatibilityDetail { get; set; } = "";
    public string Status => LocalizationService.Translate(HasUpdate ? "Aggiornamento disponibile" : "Aggiornato");
    public string StatusDetail => HasUpdate
        ? $"{AvailableSource}: {AvailableVersion}"
        : LocalizationService.Translate("Nessuna proposta verificata dalle fonti ufficiali");
    public string DriverDateLabel => DriverDate?.ToString("dd/MM/yyyy") ?? "—";
    public string DisplayName => Quantity > 1 ? $"{DeviceName}  ×{Quantity}" : DeviceName;
    public string CategoryLabel => IsProcessorOrChipset ? "CPU / Chipset" :
        string.IsNullOrWhiteSpace(DeviceClass) ? LocalizationService.Translate("Dispositivo") : DeviceClass;
    public string ProviderLabel => string.IsNullOrWhiteSpace(Provider) ? Manufacturer : Provider;

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(CategoryLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class VendorSupportItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _description = "";
    private string _actionLabel = "Apri controllo ufficiale";
    private string _sourceLabel = "Produttore ufficiale";
    private string _confidenceLabel = "Fonte ufficiale";
    private string _compatibilityLabel = "Rilevato dall'hardware del PC";

    public string Name { get => LocalizationService.Translate(_name); set => _name = value; }
    public string Description { get => LocalizationService.Translate(_description); set => _description = value; }
    public string Url { get; set; } = "";
    public string ApplicationPath { get; set; } = "";
    public bool IsInstalledApplication => !string.IsNullOrWhiteSpace(ApplicationPath);
    public string LaunchTarget => IsInstalledApplication ? ApplicationPath : Url;
    public string ActionLabel { get => LocalizationService.Translate(_actionLabel); set => _actionLabel = value; }
    public string SourceLabel { get => LocalizationService.Translate(_sourceLabel); set => _sourceLabel = value; }
    public string ConfidenceLabel { get => LocalizationService.Translate(_confidenceLabel); set => _confidenceLabel = value; }
    public string CompatibilityLabel { get => LocalizationService.Translate(_compatibilityLabel); set => _compatibilityLabel = value; }

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(ConfidenceLabel));
        OnPropertyChanged(nameof(CompatibilityLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class HardwareScanResult
{
    public string ComputerManufacturer { get; set; } = "";
    public string ComputerModel { get; set; } = "";
    public string CpuName { get; set; } = "Processore non rilevato";
    public string CpuManufacturer { get; set; } = "";
    public List<DriverInventoryItem> Drivers { get; set; } = [];
    public List<DriverProblemItem> Problems { get; set; } = [];
    public List<VendorSupportItem> VendorTools { get; set; } = [];
}

public sealed record QuickHardwareSnapshot(
    HardwareScanResult? Hardware,
    StorageHealthScanResult? Storage)
{
    public bool HasAnyData => Hardware is not null || Storage is not null;
}
