using System.Globalization;
using System.Runtime.InteropServices;

namespace UpdateCenter.Services;

internal static class CpuTemperatureFallbackService
{
    public static double? TryRead()
    {
        var readings = new List<double>();
        Query("root\\wmi", "SELECT InstanceName,CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", row =>
        {
            var name = SafeString(() => Convert.ToString(row.InstanceName));
            if (!LooksLikeCpuSensor(name)) return;
            AddReading(readings, SafeDouble(() => Convert.ToDouble(row.CurrentTemperature, CultureInfo.InvariantCulture)), true);
        });

        Query("root\\cimv2", "SELECT Name,Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation", row =>
        {
            var name = SafeString(() => Convert.ToString(row.Name));
            if (!LooksLikeCpuSensor(name)) return;
            AddReading(readings, SafeDouble(() => Convert.ToDouble(row.Temperature, CultureInfo.InvariantCulture)), false);
        });

        return readings.Count == 0 ? null : readings.Max();
    }

    private static void AddReading(ICollection<double> readings, double? rawValue, bool isTenthsKelvin)
    {
        if (!rawValue.HasValue) return;
        var celsius = isTenthsKelvin
            ? rawValue.Value / 10d - 273.15d
            : rawValue.Value > 200d ? rawValue.Value - 273.15d : rawValue.Value;
        if (celsius is >= 1 and <= 125) readings.Add(celsius);
    }

    private static bool LooksLikeCpuSensor(string name) => new[]
    {
        "cpu", "processor", "package", "core", "tctl", "tdie"
    }.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static void Query(string nameSpace, string query, Action<dynamic> consume)
    {
        object? locatorObject = null;
        object? serviceObject = null;
        object? resultsObject = null;
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType is null) return;
            locatorObject = Activator.CreateInstance(locatorType);
            dynamic locator = locatorObject!;
            serviceObject = locator.ConnectServer(".", nameSpace);
            dynamic service = serviceObject;
            resultsObject = service.ExecQuery(query);
            dynamic results = resultsObject;
            foreach (var row in results)
            {
                try { consume(row); }
                finally { ReleaseCom(row); }
            }
        }
        catch { }
        finally
        {
            ReleaseCom(resultsObject);
            ReleaseCom(serviceObject);
            ReleaseCom(locatorObject);
        }
    }

    private static double? SafeDouble(Func<double> getter) { try { return getter(); } catch { return null; } }
    private static string SafeString(Func<string?> getter) { try { return getter() ?? ""; } catch { return ""; } }
    private static void ReleaseCom(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
        catch { }
    }
}
