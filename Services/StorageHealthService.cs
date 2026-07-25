using System.Text.Json;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class StorageHealthService
{
    public async Task<StorageHealthScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        const string script = "$ErrorActionPreference='SilentlyContinue';" +
            "$volumes=@(Get-CimInstance Win32_LogicalDisk -Filter \"DriveType=3\" | ForEach-Object {[pscustomobject]@{" +
            "DriveLetter=([string]$_.DeviceID).TrimEnd(':');Label=[string]$_.VolumeName;FileSystem=[string]$_.FileSystem;" +
            "Size=[int64]$_.Size;Free=[int64]$_.FreeSpace}});" +
            "$diskDrives=@(Get-CimInstance Win32_DiskDrive | ForEach-Object {$disk=$_;" +
            "$logicalDisks=@(Get-CimAssociatedInstance -InputObject $disk -Association Win32_DiskDriveToDiskPartition | ForEach-Object {" +
            "Get-CimAssociatedInstance -InputObject $_ -Association Win32_LogicalDiskToPartition} | Where-Object {$_.DriveType -eq 3});" +
            "$diskVolumes=@($logicalDisks | ForEach-Object {$letter=([string]$_.DeviceID).TrimEnd(':');" +
            "$volumes | Where-Object {$_.DriveLetter -eq $letter} | Select-Object -First 1});" +
            "[pscustomobject]@{Index=[int]$disk.Index;Name=[string]$disk.Model;MediaType=[string]$disk.MediaType;" +
            "Size=[int64]$disk.Size;InterfaceType=[string]$disk.InterfaceType;PNPDeviceID=[string]$disk.PNPDeviceID;" +
            "Status=[string]$disk.Status;FirmwareVersion=[string]$disk.FirmwareRevision;" +
            "SerialNumber=([string]$disk.SerialNumber).Trim();Volumes=$diskVolumes}});" +
            "$usedDiskIndexes=@{};" +
            "$physical=@(Get-PhysicalDisk | ForEach-Object {$physicalDisk=$_;$serial=([string]$physicalDisk.SerialNumber).Trim();" +
            "$diskDrive=@($diskDrives | Where-Object {-not $usedDiskIndexes.ContainsKey($_.Index) -and (" +
            "([string]$_.Index -eq [string]$physicalDisk.DeviceId) -or ($serial -and $_.SerialNumber -eq $serial) -or " +
            "($_.Name -eq [string]$physicalDisk.FriendlyName -and [math]::Abs($_.Size-[int64]$physicalDisk.Size) -lt 10485760))} | Select-Object -First 1);" +
            "if($diskDrive){$usedDiskIndexes[$diskDrive.Index]=$true};" +
            "[pscustomobject]@{Name=[string]$physicalDisk.FriendlyName;MediaType=[string]$physicalDisk.MediaType;" +
            "Size=[int64]$physicalDisk.Size;BusType=[string]$physicalDisk.BusType;HealthStatus=[string]$physicalDisk.HealthStatus;" +
            "OperationalStatus=([string]::Join(', ',@($physicalDisk.OperationalStatus)));FirmwareVersion=[string]$physicalDisk.FirmwareVersion;" +
            "SerialNumber=$serial;Temperature=$physicalDisk.Temperature;Volumes=$(if($diskDrive){@($diskDrive.Volumes)}else{@()})}});" +
            "if($physical.Count -eq 0){$physical=@($diskDrives | ForEach-Object {$disk=$_;[pscustomobject]@{" +
            "Name=$disk.Name;MediaType=$disk.MediaType;Size=$disk.Size;" +
            "BusType=$(if($disk.InterfaceType -eq 'USB' -or $disk.PNPDeviceID.StartsWith('USBSTOR')){'USB'}else{$disk.InterfaceType});" +
            "HealthStatus='Unknown';OperationalStatus=$disk.Status;FirmwareVersion=$disk.FirmwareVersion;" +
            "SerialNumber=$disk.SerialNumber;Temperature=$null;Volumes=@($disk.Volumes)}})};" +
            "[pscustomobject]@{Physical=$physical;Volumes=$volumes}|ConvertTo-Json -Depth 6 -Compress";

        var result = await ProcessRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
            cancellationToken,
            TimeSpan.FromMinutes(2));
        if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new InvalidOperationException("Windows non ha restituito informazioni sulla salute dello storage.");

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput.Trim());
            var scan = new StorageHealthScanResult();
            ReadElements(document.RootElement, "Physical", element =>
            {
                var device = new StorageDeviceItem
                {
                    Name = ReadString(element, "Name", "Disco"),
                    MediaType = NormalizeMediaType(ReadString(element, "MediaType", "Non specificato")),
                    SizeBytes = ReadLong(element, "Size"),
                    HealthStatus = ReadString(element, "HealthStatus", "Unknown"),
                    OperationalStatus = ReadString(element, "OperationalStatus", "Unknown"),
                    FirmwareVersion = ReadString(element, "FirmwareVersion"),
                    SerialNumber = ReadString(element, "SerialNumber"),
                    BusType = ReadString(element, "BusType"),
                    TemperatureCelsius = ReadNullableDouble(element, "Temperature")
                };
                ReadElements(element, "Volumes", volume => device.Volumes.Add(ReadVolume(volume)));
                scan.Devices.Add(device);
            });
            ReadElements(document.RootElement, "Volumes", element => scan.Volumes.Add(ReadVolume(element)));
            if (scan.Volumes.Count == 0)
                AddDriveInfoFallback(scan.Volumes);

            var warningCount = scan.Devices.Count(x => !x.IsHealthy && !x.IsHealthUnknown);
            scan.Status = scan.Devices.Count == 0
                ? "Nessun disco fisico rilevato."
                : warningCount > 0
                    ? $"{warningCount} unità richiedono attenzione secondo Windows."
                    : scan.Devices.All(x => x.IsHealthy)
                        ? "Tutte le unità segnalate da Windows risultano sane."
                        : "Nessun problema segnalato; la salute dettagliata non è disponibile per tutte le unità.";
            LogService.Write($"Controllo storage completato: {scan.Devices.Count} dischi, {scan.Volumes.Count} volumi.");
            return scan;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Windows ha restituito dati storage non leggibili.", ex);
        }
    }

    private static void ReadElements(JsonElement root, string name, Action<JsonElement> consume)
    {
        if (!root.TryGetProperty(name, out var value)) return;
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray()) consume(element);
        else if (value.ValueKind == JsonValueKind.Object)
            consume(value);
    }

    private static string ReadString(JsonElement element, string name, string fallback = "")
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        var text = value.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static long ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        return value.TryGetInt64(out var number) ? number : long.TryParse(value.ToString(), out number) ? number : 0;
    }

    private static StorageVolumeItem ReadVolume(JsonElement element) => new()
    {
        DriveLetter = ReadString(element, "DriveLetter"),
        Label = ReadString(element, "Label"),
        FileSystem = ReadString(element, "FileSystem"),
        SizeBytes = ReadLong(element, "Size"),
        FreeBytes = ReadLong(element, "Free")
    };

    private static double? ReadNullableDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return value.TryGetDouble(out var number) ? number : double.TryParse(value.ToString(), out number) ? number : null;
    }

    private static string NormalizeMediaType(string value) => value switch
    {
        "SSD" => "SSD",
        "HDD" => "HDD",
        "SCM" => "Memoria persistente",
        _ => value.Equals("Unspecified", StringComparison.OrdinalIgnoreCase) ? "Non specificato" : value
    };

    private static void AddDriveInfoFallback(List<StorageVolumeItem> volumes)
    {
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed))
        {
            try
            {
                if (!drive.IsReady) continue;
                volumes.Add(new StorageVolumeItem
                {
                    DriveLetter = drive.Name.TrimEnd(Path.DirectorySeparatorChar).TrimEnd(':'),
                    Label = drive.VolumeLabel,
                    FileSystem = drive.DriveFormat,
                    SizeBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace
                });
            }
            catch { }
        }
    }
}
