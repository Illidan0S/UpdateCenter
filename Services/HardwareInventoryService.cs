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
            "$pnp=@{};Get-CimInstance Win32_PnPEntity | Where-Object {$_.PNPDeviceID} | ForEach-Object {" +
            "$pnp[$_.PNPDeviceID]=[pscustomobject]@{HardwareIds=@($_.HardwareID);CompatibleIds=@($_.CompatibleID)}};" +
            "$drivers=@(Get-CimInstance Win32_PnPSignedDriver | Where-Object {$_.DeviceName} | ForEach-Object {" +
            "$ids=$pnp[$_.DeviceID];[pscustomobject]@{DeviceName=$_.DeviceName;Manufacturer=$_.Manufacturer;" +
            "DriverProviderName=$_.DriverProviderName;DriverVersion=$_.DriverVersion;DriverDate=$_.DriverDate;" +
            "DeviceClass=$_.DeviceClass;DeviceID=$_.DeviceID;InfName=$_.InfName;" +
            "HardwareIds=@($ids.HardwareIds);CompatibleIds=@($ids.CompatibleIds)}});" +
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
            HardwareIds = ReadStringArray(element, "HardwareIds"),
            CompatibleIds = ReadStringArray(element, "CompatibleIds"),
            InfName = ReadProperty(element, "InfName"),
            IsProcessorOrChipset = ChipsetTerms.Any(term => combined.Contains(term, StringComparison.OrdinalIgnoreCase))
        });
    }

    private static List<VendorSupportItem> BuildVendorTools(HardwareScanResult scan)
    {
        var tools = new List<VendorSupportItem>();
        var oemSupport = BuildOemSupport(scan.ComputerManufacturer, scan.ComputerModel);
        if (oemSupport is not null)
            tools.Add(oemSupport);

        if (IsCpuVendor(scan, "AMD", "Advanced Micro Devices") ||
            HasHardware(scan, ["AMD", "Advanced Micro Devices"], ["PCI\\VEN_1022", "PCI\\VEN_1002"]))
        {
            tools.Add(new VendorSupportItem
            {
                Name = "Supporto driver AMD",
                Description = "Controllo ufficiale per chipset AMD e grafica Radeon, senza installare strumenti di rilevamento.",
                Url = "https://www.amd.com/en/support/download/drivers.html",
                CompatibilityLabel = "Hardware AMD rilevato tramite produttore o ID PCI"
            });
        }

        if (IsCpuVendor(scan, "Intel") || HasHardware(scan, ["Intel"], ["PCI\\VEN_8086"]))
        {
            tools.Add(new VendorSupportItem
            {
                Name = "Supporto driver Intel",
                Description = "Download Center ufficiale per i componenti Intel rilevati; Update Center non installa Intel DSA.",
                Url = "https://www.intel.com/content/www/us/en/download-center/home.html",
                CompatibilityLabel = "Hardware Intel rilevato tramite produttore o ID PCI"
            });
        }

        if (HasHardware(scan, ["NVIDIA"], ["PCI\\VEN_10DE"]))
        {
            tools.Add(new VendorSupportItem
            {
                Name = "Supporto driver NVIDIA",
                Description = "Ricerca manuale dei driver ufficiali Game Ready o Studio; NVIDIA App non viene installata.",
                Url = "https://www.nvidia.com/it-it/drivers/",
                CompatibilityLabel = "GPU NVIDIA rilevata tramite ID PCI"
            });
        }

        var logitechDevices = MatchingDevices(scan, ["Logitech"]);
        var hasGHubComponent = logitechDevices.Any(x =>
            ContainsAny($"{x.DeviceName} {x.Provider}", ["G HUB", "GHUB", "Virtual Bus", "Virtual Keyboard"]));
        if (hasGHubComponent)
        {
            tools.Add(new VendorSupportItem
            {
                Name = "Supporto Logitech G HUB",
                Description = "Pagina ufficiale per i componenti virtuali G HUB già presenti; nessuna app viene installata da Update Center.",
                Url = "https://support.logi.com/hc/it/articles/360025298133-Logitech-G-HUB",
                ActionLabel = "Apri supporto Logitech",
                CompatibilityLabel = "Componente G HUB rilevato per nome e produttore"
            });
        }
        else if (logitechDevices.Count > 0)
        {
            var device = logitechDevices.First();
            tools.Add(new VendorSupportItem
            {
                Name = $"Supporto Logitech · {device.DeviceName}",
                Description = "Supporto ufficiale generico: il modello rilevato non richiede automaticamente G HUB.",
                Url = "https://support.logi.com/hc/it",
                ActionLabel = "Apri supporto Logitech",
                CompatibilityLabel = "Periferica Logitech rilevata; pagina modello non determinabile con certezza"
            });
        }

        var epsonDevices = MatchingDevices(scan, ["EPSON", "Seiko Epson"]);
        var wf2760 = epsonDevices.FirstOrDefault(x =>
            ContainsAny(x.DeviceName, ["WF-2760", "WF 2760", "WF2760"]));
        if (wf2760 is not null)
        {
            tools.Add(new VendorSupportItem
            {
                Name = "Supporto Epson WF-2760",
                Description = "Driver ufficiali per il modello WF-2760 rilevato. Firmware e utility restano esclusi dall'installazione automatica.",
                Url = "https://www.epson.it/it_IT/support/sc/epson-workforce-wf-2760dwf/s/s1476",
                ActionLabel = "Apri supporto Epson",
                CompatibilityLabel = "Modello WF-2760 rilevato esattamente nell'inventario"
            });
        }
        else if (epsonDevices.Count > 0)
        {
            var device = epsonDevices.First();
            tools.Add(new VendorSupportItem
            {
                Name = $"Supporto Epson · {device.DeviceName}",
                Description = "Pagina ufficiale generica Epson: nessun modello diverso viene scambiato per WF-2760.",
                Url = "https://www.epson.it/it_IT/support",
                ActionLabel = "Apri supporto Epson",
                CompatibilityLabel = "Dispositivo Epson rilevato; collegamento generico per evitare falsi abbinamenti"
            });
        }

        if (HasHardware(scan, ["VB-Audio", "VB-Audio Software"], []))
        {
            tools.Add(new VendorSupportItem
            {
                Name = "VB-Audio Virtual Cable",
                Description = "Controllo manuale del driver audio virtuale sul sito ufficiale VB-Audio.",
                Url = "https://vb-audio.com/Cable/",
                ActionLabel = "Apri VB-Audio",
                CompatibilityLabel = "Driver VB-Audio già presente nell'inventario"
            });
        }

        tools.Add(new VendorSupportItem
        {
            Name = "Driver facoltativi Windows",
            Description = "Apre la sezione ufficiale di Windows Update dedicata agli aggiornamenti facoltativi.",
            Url = "ms-settings:windowsupdate-optionalupdates",
            ActionLabel = "Apri Windows Update",
            SourceLabel = "Microsoft Windows",
            CompatibilityLabel = "Controllo applicabilità eseguito da Windows"
        });

        return tools;
    }

    private static VendorSupportItem? BuildOemSupport(string manufacturer, string model)
    {
        var identity = $"{manufacturer} {model}";
        if (identity.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("MSI", StringComparison.OrdinalIgnoreCase))
        {
            var isX670eGamingPlusWifi = model.Contains("MS-7E16", StringComparison.OrdinalIgnoreCase);
            return new VendorSupportItem
            {
                Name = isX670eGamingPlusWifi ? "MSI X670E Gaming Plus WiFi" : $"Supporto MSI · {model}",
                Description = "Portale ufficiale per driver chipset, rete, Wi-Fi, Bluetooth e audio della scheda madre.",
                Url = isX670eGamingPlusWifi
                    ? "https://www.msi.com/Motherboard/X670E-GAMING-PLUS-WIFI/support"
                    : "https://www.msi.com/support/download/",
                ActionLabel = "Apri supporto MSI",
                CompatibilityLabel = isX670eGamingPlusWifi
                    ? "Scheda madre MS-7E16 abbinata al modello ufficiale X670E Gaming Plus WiFi"
                    : $"Produttore MSI rilevato; modello {model} non abbinato a una pagina esatta"
            };
        }

        if (identity.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
            return CreateOemSupport("ASUS", model, "https://www.asus.com/support/download-center/");
        if (identity.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase))
            return CreateOemSupport("GIGABYTE", model, "https://www.gigabyte.com/Support/Consumer/Download");
        if (identity.Contains("Dell", StringComparison.OrdinalIgnoreCase))
            return CreateOemSupport("Dell", model, "https://www.dell.com/support/home/drivers");
        if (identity.Contains("Hewlett-Packard", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("HP", StringComparison.OrdinalIgnoreCase))
            return CreateOemSupport("HP", model, "https://support.hp.com/drivers");
        if (identity.Contains("Lenovo", StringComparison.OrdinalIgnoreCase))
            return CreateOemSupport("Lenovo", model, "https://pcsupport.lenovo.com/");
        if (identity.Contains("Acer", StringComparison.OrdinalIgnoreCase))
            return CreateOemSupport("Acer", model, "https://www.acer.com/support/drivers-and-manuals");

        return null;
    }

    private static VendorSupportItem CreateOemSupport(string manufacturer, string model, string url) => new()
    {
        Name = $"Supporto {manufacturer} · {model}",
        Description = "Driver e firmware specifici per il modello, forniti direttamente dal produttore del PC.",
        Url = url,
        ActionLabel = $"Apri supporto {manufacturer}",
        CompatibilityLabel = $"PC {manufacturer} modello {model} rilevato da Windows"
    };

    private static bool IsCpuVendor(HardwareScanResult scan, params string[] terms) =>
        ContainsAny($"{scan.CpuManufacturer} {scan.CpuName}", terms);

    private static bool HasHardware(HardwareScanResult scan, string[] vendorTerms, string[] idPrefixes) =>
        scan.Drivers.Any(driver =>
            ContainsAny($"{driver.DeviceName} {driver.Manufacturer} {driver.Provider}", vendorTerms) ||
            driver.HardwareIds.Concat(driver.CompatibleIds).Append(driver.DeviceId)
                .Any(id => idPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))));

    private static List<DriverInventoryItem> MatchingDevices(HardwareScanResult scan, string[] terms) =>
        scan.Drivers.Where(driver =>
            ContainsAny($"{driver.DeviceName} {driver.Manufacturer} {driver.Provider}", terms)).ToList();

    private static bool ContainsAny(string value, IEnumerable<string> terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

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

    private static List<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(x => x.ToString().Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var single = value.ToString().Trim();
        return string.IsNullOrWhiteSpace(single) ? [] : [single];
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
