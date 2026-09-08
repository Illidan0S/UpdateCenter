namespace UpdateCenter.Models;

public sealed class StorageTableRow
{
    private string _kindLabel = "";
    public string KindLabel { get => Services.LocalizationService.Translate(_kindLabel); init => _kindLabel = value; }
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    public string VolumesLabel { get; init; } = "—";
    public string VolumesDetail { get; init; } = "";
    public string CapacityLabel { get; init; } = "—";
    public string HealthLabel { get; init; } = "—";
    public string HealthDetail { get; init; } = "";
    public string TemperatureLabel { get; init; } = "—";
    public bool IsHealthy { get; init; }
    public bool IsHealthUnknown { get; init; }
}
