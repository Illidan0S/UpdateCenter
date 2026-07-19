using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class HardwareInventoryService
{
    private static readonly string[] ChipsetTerms =
    [
        "processor", "processore", "chipset", "gpio", "smbus", "psp", "v-cache",
        "provisioning", "serial io", "management engine", "host bridge", "i2c controller",
        "pci express root", "pci bus", "root complex", "iommu", "crash defender",
        "link controller", "system management", "platform security"
    ];

    public async Task<HardwareScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        const string script = "$ErrorActionPreference='Stop';" +
            "$cpu=Get-CimInstance Win32_Processor | Select-Object -First 1 Name,Manufacturer;" +
            "$pc=Get-CimInstance Win32_ComputerSystem | Select-Object -First 1 Manufacturer,Model;" +
            "$drivers=@(Get-CimInstance Win32_PnPSignedDriver | Where-Object {$_.DeviceName} | " +
            "Select-Object DeviceName,Manufacturer,DriverProviderName,DriverVersion,DriverDate,DeviceClass,DeviceID,InfName);" +
            "[pscustomobject]@{Computer=$pc;Cpu=$cpu;Drivers=$drivers}|ConvertTo-Json -Depth 4 -Compress";

        var result = await ProcessRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
            cancellationToken,
            TimeSpan.FromMinutes(3));

        if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new InvalidOperationException("Impossibile leggere l'inventario dei driver installati.");

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput.Trim());
            var root = document.RootElement;
            var scan = new HardwareScanResult
            {
                ComputerManufacturer = ReadString(root, "Computer", "Manufacturer"),
                ComputerModel = ReadString(root, "Computer", "Model"),
                CpuName = ReadString(root, "Cpu", "Name", "Processore non rilevato"),
                CpuManufacturer = ReadString(root, "Cpu", "Manufacturer")
            };

            if (root.TryGetProperty("Drivers", out var driversElement))
            {
                if (driversElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var driver in driversElement.EnumerateArray())
                        AddDriver(scan.Drivers, driver);
                }
                else if (driversElement.ValueKind == JsonValueKind.Object)
                {
                    AddDriver(scan.Drivers, driversElement);
                }
            }

            scan.Drivers = scan.Drivers
                .GroupBy(x => $"{x.DeviceName}\u001f{x.InstalledVersion}\u001f{x.Provider}", StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    first.Quantity = group.Count();
                    return first;
                })
                .OrderByDescending(x => x.IsProcessorOrChipset)
                .ThenBy(x => x.DeviceName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            scan.VendorTools = BuildVendorTools(scan);
            LogService.Write($"Inventario hardware completato: {scan.Drivers.Count} driver installati.");
            return scan;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Windows ha restituito un inventario hardware non leggibile.", ex);
        }
    }

    private static void AddDriver(List<DriverInventoryItem> list, JsonElement element)
    {
        var name = ReadProperty(element, "DeviceName");
        if (string.IsNullOrWhiteSpace(name)) return;
        var manufacturer = ReadProperty(element, "Manufacturer");
        var provider = ReadProperty(element, "DriverProviderName");
        var className = ReadProperty(element, "DeviceClass");
        var combined = $"{name} {manufacturer} {provider} {className}";

        list.Add(new DriverInventoryItem
        {
            DeviceName = name,
            Manufacturer = manufacturer,
            Provider = provider,
            InstalledVersion = ReadProperty(element, "DriverVersion", "—"),
            DriverDate = ParsePowerShellDate(ReadProperty(element, "DriverDate")),
            DeviceClass = className,
            DeviceId = ReadProperty(element, "DeviceID"),
            InfName = ReadProperty(element, "InfName"),
            IsProcessorOrChipset = ChipsetTerms.Any(term => combined.Contains(term, StringComparison.OrdinalIgnoreCase))
        });
    }

    private static List<VendorSupportItem> BuildVendorTools(HardwareScanResult scan)
    {
        var tools = new List<VendorSupportItem>();
        var allHardware = string.Join(" ", scan.CpuName, scan.CpuManufacturer,
            string.Join(" ", scan.Drivers.Select(x => $"{x.Manufacturer} {x.Provider}")));

        if (allHardware.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            allHardware.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(new VendorSupportItem
            {
                Name = "AMD Driver & Chipset Support",
                Description = "Controllo ufficiale per processori Ryzen, chipset AMD e grafica Radeon.",
                Url = "https://www.amd.com/en/support/download/drivers.html"
            });
        }

        if (allHardware.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(new VendorSupportItem
            {
                Name = "Intel Driver & Support Assistant",
                Description = "Rilevamento ufficiale degli aggiornamenti per componenti Intel.",
                Url = "https://www.intel.com/content/www/us/en/support/detect.html"
            });
        }

        if (allHardware.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(new VendorSupportItem
            {
                Name = "NVIDIA App",
                Description = "Driver ufficiali Game Ready e Studio per schede NVIDIA.",
                Url = "https://www.nvidia.com/en-us/software/nvidia-app/"
            });
        }

        tools.Add(new VendorSupportItem
        {
            Name = "Driver facoltativi Windows",
            Description = "Apre la sezione ufficiale di Windows Update dedicata agli aggiornamenti facoltativi.",
            Url = "ms-settings:windowsupdate-optionalupdates",
            ActionLabel = "Apri Windows Update"
        });

        return tools;
    }

    private static string ReadString(JsonElement root, string objectName, string propertyName, string fallback = "")
    {
        if (root.TryGetProperty(objectName, out var obj) && obj.ValueKind == JsonValueKind.Object)
            return ReadProperty(obj, propertyName, fallback);
        return fallback;
    }

    private static string ReadProperty(JsonElement element, string name, string fallback = "")
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        return value.ToString().Trim();
    }

    private static DateTime? ParsePowerShellDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return parsed;

        var match = Regex.Match(value, @"/Date\((?<ms>-?\d+)(?:[+-]\d+)?\)/");
        if (match.Success && long.TryParse(match.Groups["ms"].Value, out var milliseconds))
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime; }
            catch { }
        }
        return null;
    }
}
