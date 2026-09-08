using UpdateCenter.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UpdateCenter.Models;

public sealed class DriverProblemItem : INotifyPropertyChanged
{
    private string _deviceName = "Dispositivo sconosciuto";
    private string _errorTitle = "Problema rilevato";
    private string _suggestedAction = "Apri Gestione dispositivi per verificare il dispositivo.";
    private string _severity = "Attenzione";

    public string DeviceName { get => LocalizationService.Translate(_deviceName); set => _deviceName = value; }
    public string Manufacturer { get; set; } = "";
    public string DeviceClass { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public int ErrorCode { get; set; }
    public string ErrorTitle { get => LocalizationService.Translate(_errorTitle); set => _errorTitle = value; }
    public string SuggestedAction { get => LocalizationService.Translate(_suggestedAction); set => _suggestedAction = value; }
    public string Severity { get => LocalizationService.Translate(_severity); set => _severity = value; }
    public string InstalledInfName { get; set; } = "";
    public bool InstalledDriverSigned { get; set; }
    public bool CanManageDriverProblem => ErrorCode is 10 or 18 or 28 or 31 or 37 or 39 or 40 or 43;
    public bool CanRepairWithInstalledDriver
    {
        get
        {
            if (!InstalledDriverSigned || ErrorCode is not (10 or 18 or 28 or 31 or 37 or 39 or 40 or 43)) return false;
            var name = Path.GetFileName(InstalledInfName);
            if (!name.Equals(InstalledInfName, StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith("oem", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) return false;
            return name[3..^4].Length is > 0 and <= 6 && name[3..^4].All(char.IsDigit);
        }
    }
    public string RepairAvailabilityText => CanRepairWithInstalledDriver
        ? LocalizationService.IsEnglish ? $"Windows package: {InstalledInfName}" : $"Pacchetto Windows: {InstalledInfName}"
        : LocalizationService.Text("Ricerca tramite fonti verificate", "Search via verified sources");
    public string RepairActionText => CanRepairWithInstalledDriver
        ? LocalizationService.Text("Reinstalla driver", "Reinstall driver")
        : LocalizationService.Text("Cerca driver", "Search driver");
    public string ErrorCodeLabel => LocalizationService.IsEnglish
        ? $"Device Manager code {ErrorCode}"
        : $"Codice Gestione dispositivi {ErrorCode}";
    public string DeviceDetail => string.Join(" · ", new[] { DeviceClass, Manufacturer }
        .Where(x => !string.IsNullOrWhiteSpace(x)));

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(ErrorTitle));
        OnPropertyChanged(nameof(SuggestedAction));
        OnPropertyChanged(nameof(Severity));
        OnPropertyChanged(nameof(RepairAvailabilityText));
        OnPropertyChanged(nameof(RepairActionText));
        OnPropertyChanged(nameof(ErrorCodeLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GameDependencyItem
{
    public string Name { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string InstalledVersion { get; set; } = "—";
    public bool IsAvailable { get; set; }
    public bool IsOptional { get; set; }
    public string PackageId { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
    public bool AllowMissingInstallation { get; set; }
    public bool CanAutoInstall => AllowMissingInstallation && !IsAvailable && !string.IsNullOrWhiteSpace(PackageId);
    public string OfficialActionUrl { get; set; } = "";
    public bool CanOpenOfficialAction => Uri.TryCreate(OfficialActionUrl, UriKind.Absolute, out var uri) &&
                                         uri.Scheme == Uri.UriSchemeHttps;
    public string Status => IsAvailable
        ? LocalizationService.Text("Disponibile", "Available")
        : IsOptional ? LocalizationService.Text("Opzionale non rilevato", "Optional not detected") : LocalizationService.Text("Non rilevato", "Not detected");
    public string ActionLabel => CanAutoInstall
        ? LocalizationService.Text("Selezionabile negli aggiornamenti", "Selectable in updates")
        : CanOpenOfficialAction ? LocalizationService.Text("Controllo ufficiale", "Official check") : LocalizationService.Text("Solo diagnosi", "Diagnosis only");
    public string Detail { get; set; } = "";
}

public sealed class StorageDeviceItem
{
    public string Name { get; set; } = "Disco";
    public string MediaType { get; set; } = "Non specificato";
    public long SizeBytes { get; set; }
    public string HealthStatus { get; set; } = "Unknown";
    public string OperationalStatus { get; set; } = "Unknown";
    public string FirmwareVersion { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string BusType { get; set; } = "";
    public bool IsExternalUsb => BusType.Equals("USB", StringComparison.OrdinalIgnoreCase);
    public double? TemperatureCelsius { get; set; }
    public List<StorageVolumeItem> Volumes { get; set; } = [];
    public bool IsHealthy => HealthStatus.Equals("Healthy", StringComparison.OrdinalIgnoreCase) ||
                             HealthStatus.Equals("Sano", StringComparison.OrdinalIgnoreCase);
    public bool IsHealthUnknown => string.IsNullOrWhiteSpace(HealthStatus) ||
                                   HealthStatus.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                                   HealthStatus.Equals("Non disponibile", StringComparison.OrdinalIgnoreCase);
    public string HealthLabel => IsHealthy
        ? LocalizationService.Text("Sano", "Healthy")
        : IsHealthUnknown
            ? LocalizationService.Text("Stato non disponibile", "Status not available")
            : LocalizationService.Text($"Attenzione · {HealthStatus}", $"Warning · {HealthStatus}");
    public string SizeLabel => FormatBytes(SizeBytes);
    public string TemperatureLabel => TemperatureCelsius is >= 1 and <= 125
        ? $"{TemperatureCelsius:0.#} °C"
        : LocalizationService.Text("Non disponibile", "Not available");
    public string TechnicalDetail => string.Join(" · ", new[]
    {
        string.IsNullOrWhiteSpace(OperationalStatus) ? null : LocalizationService.IsEnglish ? $"Operational: {OperationalStatus}" : $"Operativo: {OperationalStatus}",
        string.IsNullOrWhiteSpace(FirmwareVersion) ? null : $"Firmware: {FirmwareVersion}",
        string.IsNullOrWhiteSpace(BusType) ? null : $"Bus: {BusType}"
    }.Where(x => !string.IsNullOrWhiteSpace(x)));
    private IEnumerable<StorageVolumeItem> DisplayVolumes => Volumes
        .OrderByDescending(volume => volume.SizeBytes)
        .ThenBy(volume => volume.DriveLetter, StringComparer.OrdinalIgnoreCase);
    public string VolumesLabel => Volumes.Count == 0
        ? LocalizationService.Text("Nessun volume con lettera", "No lettered volumes")
        : string.Join(" · ", DisplayVolumes.Select(volume => string.Join(" ", new[]
        {
            volume.DisplayName,
            volume.Label
        }.Where(value => !string.IsNullOrWhiteSpace(value)))));
    public string VolumesDetail => Volumes.Count == 0
        ? "—"
        : string.Join(" | ", DisplayVolumes.Select(volume => string.Join(" · ", new[]
        {
            volume.DisplayName,
            volume.FileSystem,
            volume.SpaceLabel
        }.Where(value => !string.IsNullOrWhiteSpace(value)))));

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{FormatWindowsValue(value)} {units[unit]}";
    }

    private static string FormatWindowsValue(double value)
    {
        var decimals = value >= 100 ? 0 : value >= 10 ? 1 : 2;
        var factor = Math.Pow(10, decimals);
        var truncated = Math.Floor(value * factor) / factor;
        return truncated.ToString($"F{decimals}");
    }
}

public sealed class StorageVolumeItem
{
    public string DriveLetter { get; set; } = "";
    public string Label { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public long SizeBytes { get; set; }
    public long FreeBytes { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(DriveLetter) ? Label : $"{DriveLetter}:";
    public string SpaceLabel => SizeBytes <= 0 ? "—" : LocalizationService.IsEnglish ? $"{FormatBytes(FreeBytes)} free of {FormatBytes(SizeBytes)}" : $"{FormatBytes(FreeBytes)} liberi di {FormatBytes(SizeBytes)}";
    public double UsedPercentage => SizeBytes <= 0 ? 0 : Math.Clamp((SizeBytes - FreeBytes) * 100d / SizeBytes, 0, 100);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        var decimals = value >= 100 ? 0 : value >= 10 ? 1 : 2;
        var factor = Math.Pow(10, decimals);
        var truncated = Math.Floor(value * factor) / factor;
        return $"{truncated.ToString($"F{decimals}")} {units[unit]}";
    }
}

public sealed class StorageHealthScanResult
{
    public List<StorageDeviceItem> Devices { get; set; } = [];
    public List<StorageVolumeItem> Volumes { get; set; } = [];
    public string Status { get; set; } = LocalizationService.Text("Salute dello storage non ancora controllata.", "Storage health not checked yet.");
}
