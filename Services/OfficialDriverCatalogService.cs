using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class OfficialDriverCatalogService
{
    private const string ResourceName = "UpdateCenter.Assets.driver-catalog.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, string[]> OfficialDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AMD"] = ["amd.com"],
        ["Intel"] = ["intel.com"],
        ["NVIDIA"] = ["nvidia.com"],
        ["MSI"] = ["msi.com"],
        ["ASUS"] = ["asus.com"],
        ["GIGABYTE"] = ["gigabyte.com"],
        ["Dell"] = ["dell.com"],
        ["HP"] = ["hp.com"],
        ["Lenovo"] = ["lenovo.com"],
        ["Acer"] = ["acer.com"],
        ["Epson"] = ["epson.com", "epson.it"],
        ["Logitech"] = ["logitech.com", "logi.com"],
        ["Realtek"] = ["realtek.com"],
        ["MediaTek"] = ["mediatek.com"],
        ["VB-Audio"] = ["vb-audio.com"]
    };

    public Task<OfficialDriverCatalogScanResult> ScanAsync(
        IReadOnlyList<DriverInventoryItem> installedDrivers,
        CancellationToken cancellationToken) =>
        Task.Run(() => Scan(installedDrivers, cancellationToken), cancellationToken);

    private static OfficialDriverCatalogScanResult Scan(
        IReadOnlyList<DriverInventoryItem> installedDrivers,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var catalog = LoadCatalog();
        if (catalog.SchemaVersion != 1)
            throw new InvalidOperationException("Versione del catalogo driver non supportata.");

        var updates = new List<UpdateItem>();
        var build = Environment.OSVersion.Version.Build;
        var architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";

        foreach (var entry in catalog.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryValidateEntry(entry, build, architecture, out var validationError))
            {
                warnings.Add($"{entry.Id}: {validationError}");
                continue;
            }

            var installed = FindExactHardwareMatch(installedDrivers, entry);
            if (installed is null || DriverVersionComparer.Compare(entry.Version, installed.InstalledVersion) <= 0)
                continue;

            var matchedId = FindMatchedId(installed, entry);
            var compatibility = $"ID hardware esatto: {matchedId}; Windows build {build} {architecture}";
            installed.HasUpdate = true;
            installed.AvailableVersion = entry.Version;
            installed.AvailableSource = entry.SourceName;
            installed.SourceConfidence = "Alta · ID hardware esatto";
            installed.CompatibilityDetail = compatibility;

            updates.Add(new UpdateItem
            {
                Id = entry.Id,
                Name = entry.DeviceName,
                Kind = UpdateKind.Driver,
                Publisher = entry.Vendor,
                InstalledVersion = installed.InstalledVersion,
                AvailableVersion = entry.Version,
                Source = $"{entry.SourceName} · catalogo Update Center {catalog.CatalogVersion}",
                Size = "—",
                RequiresRestart = entry.RequiresRestart,
                IsImportant = false,
                IsOptional = true,
                DriverInstallMode = DriverInstallModes.OfficialInfPackage,
                OfficialReleasePageUrl = entry.ReleasePageUrl,
                OfficialDownloadUrl = entry.DownloadUrl,
                ExpectedSha256 = entry.Sha256,
                ExpectedSignerSubjects = entry.SignerSubjects,
                DriverPackageType = entry.PackageType,
                CompatibleHardwareIds = entry.HardwareIds.Concat(entry.CompatibleIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SourceConfidence = "Alta · fonte ufficiale, hash e firma richiesti",
                CompatibilityDetail = compatibility,
                ResultDetails = $"Compatibilità verificata dal catalogo: {compatibility}."
            });
        }

        LogService.Write($"Catalogo driver ufficiali {catalog.CatalogVersion}: {updates.Count} aggiornamenti applicabili, {warnings.Count} voci ignorate.");
        return new OfficialDriverCatalogScanResult
        {
            Updates = updates,
            SourcesChecked = [$"Catalogo Update Center {catalog.CatalogVersion} (URL produttore)"],
            Warnings = warnings
        };
    }

    private static OfficialDriverCatalog LoadCatalog()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Catalogo driver incorporato non disponibile.");
        return JsonSerializer.Deserialize<OfficialDriverCatalog>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Catalogo driver incorporato non leggibile.");
    }

    private static bool TryValidateEntry(
        OfficialDriverCatalogEntry entry,
        int windowsBuild,
        string architecture,
        out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.DeviceName) ||
            string.IsNullOrWhiteSpace(entry.Version) || DriverVersionComparer.Compare(entry.Version, "0") <= 0)
            return Fail("identità o versione non valida", out error);
        if (ContainsFirmwareTerm(entry.DeviceName) || ContainsFirmwareTerm(entry.Notes))
            return Fail("BIOS e firmware sono esclusi", out error);
        if (entry.HardwareIds.Count == 0 && entry.CompatibleIds.Count == 0)
            return Fail("manca un ID hardware o compatibile esatto", out error);
        if (entry.Architectures.Count == 0 || !entry.Architectures.Contains(architecture, StringComparer.OrdinalIgnoreCase))
            return Fail("architettura non applicabile", out error);
        if (windowsBuild < entry.MinimumWindowsBuild ||
            entry.MaximumWindowsBuild is int maximum && windowsBuild > maximum)
            return Fail("versione di Windows non applicabile", out error);
        if (!entry.PackageType.Equals("zip-inf", StringComparison.OrdinalIgnoreCase) &&
            !entry.PackageType.Equals("cab-inf", StringComparison.OrdinalIgnoreCase))
            return Fail("sono ammessi soltanto pacchetti ZIP/CAB contenenti driver INF", out error);
        if (!Regex.IsMatch(entry.Sha256, "^[A-Fa-f0-9]{64}$") || entry.SignerSubjects.Count == 0)
            return Fail("hash SHA-256 o firmatario atteso mancante", out error);
        if (!IsOfficialUri(entry.Vendor, entry.DownloadUrl) || !IsOfficialUri(entry.Vendor, entry.ReleasePageUrl))
            return Fail("URL non appartenente a un dominio ufficiale consentito", out error);
        return true;
    }

    public static bool IsOfficialUri(string vendor, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !OfficialDomains.TryGetValue(vendor, out var domains)) return false;
        return domains.Any(domain => uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                                     uri.Host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase));
    }

    public static void ValidateAuthorizedPackagePlan(PlanItem item)
    {
        var catalog = LoadCatalog();
        var build = Environment.OSVersion.Version.Build;
        var architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        var authorized = catalog.Entries.Any(entry =>
        {
            if (!TryValidateEntry(entry, build, architecture, out _)) return false;
            var entryIds = entry.HardwareIds.Concat(entry.CompatibleIds)
                .Select(NormalizeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var planIds = item.CompatibleHardwareIds.Select(NormalizeId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return entry.Id.Equals(item.Id, StringComparison.Ordinal) &&
                   entry.Vendor.Equals(item.Vendor, StringComparison.OrdinalIgnoreCase) &&
                   entry.DeviceName.Equals(item.Name, StringComparison.Ordinal) &&
                   entry.Version.Equals(item.AvailableVersion, StringComparison.OrdinalIgnoreCase) &&
                   entry.DownloadUrl.Equals(item.OfficialDownloadUrl, StringComparison.Ordinal) &&
                   entry.ReleasePageUrl.Equals(item.OfficialReleasePageUrl, StringComparison.Ordinal) &&
                   entry.Sha256.Equals(item.ExpectedSha256, StringComparison.OrdinalIgnoreCase) &&
                   entry.PackageType.Equals(item.DriverPackageType, StringComparison.OrdinalIgnoreCase) &&
                   entry.SignerSubjects.ToHashSet(StringComparer.OrdinalIgnoreCase)
                       .SetEquals(item.ExpectedSignerSubjects) &&
                   entryIds.SetEquals(planIds);
        });
        if (!authorized)
            throw new InvalidOperationException("Il pacchetto non corrisponde al catalogo driver incorporato e non può essere installato.");
    }

    private static DriverInventoryItem? FindExactHardwareMatch(
        IReadOnlyList<DriverInventoryItem> installedDrivers,
        OfficialDriverCatalogEntry entry) =>
        installedDrivers.FirstOrDefault(driver => !string.IsNullOrWhiteSpace(FindMatchedId(driver, entry)));

    private static string FindMatchedId(DriverInventoryItem driver, OfficialDriverCatalogEntry entry)
    {
        var detected = driver.HardwareIds.Concat(driver.CompatibleIds).Append(driver.DeviceId)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entry.HardwareIds.Concat(entry.CompatibleIds)
            .FirstOrDefault(id => detected.Contains(NormalizeId(id))) ?? "";
    }

    private static string NormalizeId(string value) => value.Trim().TrimEnd('\0');
    private static bool ContainsFirmwareTerm(string value) =>
        Regex.IsMatch(value ?? "", @"\b(bios|uefi|firmware)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static bool Fail(string message, out string error) { error = message; return false; }
}
