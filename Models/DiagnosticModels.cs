namespace UpdateCenter.Models;

public sealed class DriverProblemItem
{
    public string DeviceName { get; set; } = "Dispositivo sconosciuto";
    public string Manufacturer { get; set; } = "";
    public string DeviceClass { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public int ErrorCode { get; set; }
    public string ErrorTitle { get; set; } = "Problema rilevato";
    public string SuggestedAction { get; set; } = "Apri Gestione dispositivi per verificare il dispositivo.";
    public string Severity { get; set; } = "Attenzione";
    public string ErrorCodeLabel => $"Codice Gestione dispositivi {ErrorCode}";
    public string DeviceDetail => string.Join(" · ", new[] { DeviceClass, Manufacturer }
        .Where(x => !string.IsNullOrWhiteSpace(x)));
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
    public bool CanAutoInstall => !IsAvailable && !string.IsNullOrWhiteSpace(PackageId);
    public string OfficialActionUrl { get; set; } = "";
    public bool CanOpenOfficialAction => Uri.TryCreate(OfficialActionUrl, UriKind.Absolute, out var uri) &&
                                         uri.Scheme == Uri.UriSchemeHttps;
    public string Status => IsAvailable
        ? "Disponibile"
        : CanAutoInstall
            ? "Installazione disponibile"
            : IsOptional ? "Opzionale non rilevato" : "Non rilevato";
    public string ActionLabel => CanAutoInstall
        ? "Selezionabile negli aggiornamenti"
        : CanOpenOfficialAction ? "Controllo ufficiale" : "Solo diagnosi";
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
    public string HealthLabel => IsHealthy ? "Sano" : IsHealthUnknown ? "Stato non disponibile" : $"Attenzione · {HealthStatus}";
    public string SizeLabel => FormatBytes(SizeBytes);
    public string TemperatureLabel => TemperatureCelsius is >= 1 and <= 125
        ? $"{TemperatureCelsius:0.#} °C"
        : "Non disponibile";
    public string TechnicalDetail => string.Join(" · ", new[]
    {
        string.IsNullOrWhiteSpace(OperationalStatus) ? null : $"Operativo: {OperationalStatus}",
        string.IsNullOrWhiteSpace(FirmwareVersion) ? null : $"Firmware: {FirmwareVersion}",
        string.IsNullOrWhiteSpace(BusType) ? null : $"Bus: {BusType}"
    }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string VolumesLabel => Volumes.Count == 0
        ? "Nessun volume con lettera"
        : string.Join(" · ", Volumes.Select(volume => string.Join(" ", new[]
        {
            volume.DisplayName,
            volume.Label
        }.Where(value => !string.IsNullOrWhiteSpace(value)))));
    public string VolumesDetail => Volumes.Count == 0
        ? "—"
        : string.Join(" | ", Volumes.Select(volume => string.Join(" · ", new[]
        {
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
        return $"{value:0.#} {units[unit]}";
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
    public string SpaceLabel => SizeBytes <= 0 ? "—" : $"{FormatBytes(FreeBytes)} liberi di {FormatBytes(SizeBytes)}";
    public double UsedPercentage => SizeBytes <= 0 ? 0 : Math.Clamp((SizeBytes - FreeBytes) * 100d / SizeBytes, 0, 100);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}

public sealed class StorageHealthScanResult
{
    public List<StorageDeviceItem> Devices { get; set; } = [];
    public List<StorageVolumeItem> Volumes { get; set; } = [];
    public string Status { get; set; } = "Salute dello storage non ancora controllata.";
}
