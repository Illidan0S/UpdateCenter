using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class PreflightResult
{
    public List<string> BlockingIssues { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool CanContinue => BlockingIssues.Count == 0;
    public string PowerStatus { get; set; } = "Stato alimentazione non disponibile.";
    public string DiskStatus { get; set; } = "Spazio disponibile non verificato.";
    public bool PowerSafe { get; set; } = true;
    public bool DiskSafe { get; set; } = true;
}

public static class PreflightService
{
    public static bool ShouldCreateRestorePoint(IReadOnlyList<UpdateItem> items, AppSettings settings) =>
        settings.CreateRestorePoint && items.Any(x => x.Kind == UpdateKind.Driver || x.IsImportant);

    public static PreflightResult Check(IReadOnlyList<UpdateItem> selectedItems)
    {
        var result = new PreflightResult();

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            result.BlockingIssues.Add("Update Center richiede Windows 10 versione 1809 (build 17763) o successiva.");

        if (!NetworkInterface.GetIsNetworkAvailable())
            result.BlockingIssues.Add("Nessuna connessione di rete rilevata.");

        try
        {
            var root = Path.GetPathRoot(AppPaths.DataDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                const long oneGb = 1024L * 1024 * 1024;
                var knownDownloadSize = selectedItems.Sum(x => ParseSize(x.Size));
                var estimatedRequired = Math.Max(oneGb, knownDownloadSize * 2 + 512L * 1024 * 1024);
                result.DiskStatus = $"{FormatBytes(drive.AvailableFreeSpace)} liberi · minimo stimato {FormatBytes(estimatedRequired)}";
                if (drive.AvailableFreeSpace < estimatedRequired)
                {
                    result.DiskSafe = false;
                    result.BlockingIssues.Add($"Spazio insufficiente: servono circa {FormatBytes(estimatedRequired)}, " +
                                              $"ma sono disponibili {FormatBytes(drive.AvailableFreeSpace)}.");
                }
                else if (drive.AvailableFreeSpace < estimatedRequired + 3 * oneGb)
                {
                    result.Warnings.Add("Lo spazio sul disco di sistema è sufficiente ma ridotto.");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Write("Controllo spazio disponibile non riuscito.", ex);
            result.Warnings.Add("Non è stato possibile verificare lo spazio disponibile.");
        }

        var hasSensitiveUpdates = selectedItems.Any(x => x.Kind == UpdateKind.Driver || x.IsImportant);
        if (TryGetPowerStatus(out var power))
        {
            if (power.ACLineStatus == 0)
            {
                var percentage = power.BatteryLifePercent <= 100 ? power.BatteryLifePercent : (byte?)null;
                result.PowerStatus = percentage.HasValue
                    ? $"Alimentazione a batteria · {percentage.Value}%"
                    : "Alimentazione a batteria";
                if (hasSensitiveUpdates && percentage.HasValue && percentage.Value <= 25)
                {
                    result.PowerSafe = false;
                    result.BlockingIssues.Add("Batteria troppo bassa per aggiornare driver o componenti importanti. Collega l'alimentatore.");
                }
                else if (hasSensitiveUpdates)
                {
                    result.PowerSafe = false;
                    result.Warnings.Add("Il PC usa la batteria. È consigliato collegare l'alimentatore prima di aggiornare driver o componenti importanti.");
                }
            }
            else if (power.ACLineStatus == 1)
            {
                result.PowerStatus = power.BatteryLifePercent <= 100
                    ? $"Alimentatore collegato · batteria {power.BatteryLifePercent}%"
                    : "Alimentatore collegato";
            }
            else
            {
                result.PowerStatus = "Stato alimentazione non determinato da Windows.";
            }
        }

        return result;
    }

    private static bool TryGetPowerStatus(out SystemPowerStatus status)
    {
        status = default;
        try { return OperatingSystem.IsWindows() && GetSystemPowerStatus(out status); }
        catch { return false; }
    }

    private static long ParseSize(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "—") return 0;
        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !double.TryParse(parts[0].Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var amount)) return 0;
        var multiplier = parts[1].ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            "TB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };
        return (long)Math.Max(0, amount * multiplier);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
