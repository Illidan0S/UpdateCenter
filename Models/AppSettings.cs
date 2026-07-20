namespace UpdateCenter.Models;

public sealed class AppSettings
{
    public const int CurrentDefaultsRevision = 3;

    public bool CreateRestorePoint { get; set; } = true;
    public bool IncludeUnknownVersions { get; set; }
    public bool ScanAtStartup { get; set; }
    public bool SilentSoftwareInstall { get; set; } = true;
    public bool CheckAppUpdatesAutomatically { get; set; } = true;
    public bool NotifyWhenUpdatesAreAvailable { get; set; } = true;
    public DateTime? LastScanUtc { get; set; }
    public DateTime? LastAppUpdateCheckUtc { get; set; }
    public string IgnoredAppVersion { get; set; } = "";
    public string ThemeMode { get; set; } = "Chiaro";
    public string FontSizeMode { get; set; } = "Media";
    public string LanguageMode { get; set; } = "it";
    public string AutomaticScanInterval { get; set; } = "Off";
    public int DefaultsRevision { get; set; } = CurrentDefaultsRevision;

    public bool ApplyMigrations()
    {
        var changed = false;
        if (DefaultsRevision >= CurrentDefaultsRevision) return changed;

        if (DefaultsRevision < 2 && FontSizeMode.Equals("Media", StringComparison.OrdinalIgnoreCase))
        {
            FontSizeMode = "Piccola";
            changed = true;
        }

        if (DefaultsRevision < 3)
        {
            LanguageMode = string.IsNullOrWhiteSpace(LanguageMode) ? "it" : LanguageMode;
            AutomaticScanInterval = string.IsNullOrWhiteSpace(AutomaticScanInterval) ? "Off" : AutomaticScanInterval;
            changed = true;
        }

        DefaultsRevision = CurrentDefaultsRevision;
        return true;
    }
}
