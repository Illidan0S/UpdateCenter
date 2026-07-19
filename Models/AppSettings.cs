namespace UpdateCenter.Models;

public sealed class AppSettings
{
    public bool CreateRestorePoint { get; set; } = true;
    public bool IncludeUnknownVersions { get; set; }
    public bool ScanAtStartup { get; set; }
    public bool SilentSoftwareInstall { get; set; } = true;
    public bool CheckAppUpdatesAutomatically { get; set; } = true;
    public DateTime? LastScanUtc { get; set; }
    public DateTime? LastAppUpdateCheckUtc { get; set; }
    public string IgnoredAppVersion { get; set; } = "";
    public string ThemeMode { get; set; } = "Chiaro";
    public string FontSizeMode { get; set; } = "Media";
    public int DefaultsRevision { get; set; } = 1;
}
