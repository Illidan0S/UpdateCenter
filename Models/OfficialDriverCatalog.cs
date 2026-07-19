namespace UpdateCenter.Models;

public sealed class OfficialDriverCatalog
{
    public int SchemaVersion { get; set; }
    public string CatalogVersion { get; set; } = "";
    public List<OfficialDriverCatalogEntry> Entries { get; set; } = [];
}

public sealed class OfficialDriverCatalogEntry
{
    public string Id { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime? DriverDateUtc { get; set; }
    public List<string> HardwareIds { get; set; } = [];
    public List<string> CompatibleIds { get; set; } = [];
    public List<string> Architectures { get; set; } = [];
    public int MinimumWindowsBuild { get; set; } = 17763;
    public int? MaximumWindowsBuild { get; set; }
    public string SourceName { get; set; } = "Produttore ufficiale";
    public string ReleasePageUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public List<string> SignerSubjects { get; set; } = [];
    public string PackageType { get; set; } = "";
    public bool RequiresRestart { get; set; }
    public bool OemPreferred { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class OfficialDriverCatalogScanResult
{
    public IReadOnlyList<UpdateItem> Updates { get; init; } = [];
    public IReadOnlyList<string> SourcesChecked { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
