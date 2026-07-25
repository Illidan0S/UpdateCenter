using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class StorageTableRowFactory
{
    public static IReadOnlyList<StorageTableRow> CreateRows(IEnumerable<StorageDeviceItem> devices) =>
        devices.Select(CreateDeviceRow).ToList();

    private static StorageTableRow CreateDeviceRow(StorageDeviceItem device) => new()
    {
        KindLabel = "Unità fisica",
        Name = device.Name,
        Detail = string.Join(" · ", new[] { device.MediaType, device.BusType }
            .Where(value => !string.IsNullOrWhiteSpace(value))),
        VolumesLabel = device.VolumesLabel,
        VolumesDetail = device.VolumesDetail,
        CapacityLabel = device.SizeLabel,
        HealthLabel = device.HealthLabel,
        HealthDetail = device.TechnicalDetail,
        TemperatureLabel = device.TemperatureCelsius is >= 1 and <= 125
            ? device.TemperatureLabel
            : "—",
        IsHealthy = device.IsHealthy,
        IsHealthUnknown = device.IsHealthUnknown
    };
}
