namespace UpdateCenter.Models;

public sealed class UpdatePlan
{
    public bool CreateRestorePoint { get; set; }
    public bool SilentSoftwareInstall { get; set; }
    public string StatusFile { get; set; } = "";
    public List<PlanItem> Items { get; set; } = [];
}

public sealed class PlanItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Source { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
    public string? WindowsUpdateId { get; set; }
    public int WindowsUpdateRevision { get; set; }
    public int WindowsUpdateServerSelection { get; set; }
    public string WindowsUpdateServiceId { get; set; } = "";
    public string DriverInstallMode { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string OfficialReleasePageUrl { get; set; } = "";
    public string OfficialDownloadUrl { get; set; } = "";
    public string ExpectedSha256 { get; set; } = "";
    public List<string> ExpectedSignerSubjects { get; set; } = [];
    public string DriverPackageType { get; set; } = "";
    public List<string> CompatibleHardwareIds { get; set; } = [];
}

public sealed class UpdateRunStatus
{
    public string State { get; set; } = "Starting";
    public int CurrentIndex { get; set; }
    public int Total { get; set; }
    public string CurrentName { get; set; } = "";
    public string Message { get; set; } = "";
    public bool RestorePointRequested { get; set; }
    public bool RestorePointCreated { get; set; }
    public bool RestartRequired { get; set; }
    public List<ItemRunResult> Results { get; set; } = [];
}

public sealed class ItemRunResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Success { get; set; }
    public bool RestartRequired { get; set; }
    public string Message { get; set; } = "";
}
