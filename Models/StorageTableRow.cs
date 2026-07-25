namespace UpdateCenter.Models;

public sealed class StorageTableRow
{
    public string KindLabel { get; init; } = "";
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
