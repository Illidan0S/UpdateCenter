namespace UpdateCenter.Models;

public static class UpdateOutcomes
{
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string NotApplicable = "NotApplicable";
    public const string ManualRequired = "ManualRequired";
}

public static class UpdateVerificationStatuses
{
    public const string NotRun = "NotRun";
    public const string Verified = "Verified";
    public const string Failed = "Failed";
    public const string Unavailable = "Unavailable";
    public const string PendingRestart = "PendingRestart";
}

public sealed class UpdateVerificationResult
{
    public bool IsDefinitive { get; set; }
    public bool Verified { get; set; }
    public string Status { get; set; } = UpdateVerificationStatuses.NotRun;
    public string Message { get; set; } = "";
    public string Diagnostics { get; set; } = "";
}

internal readonly record struct UpdateResultDecision(
    bool Success,
    bool Verified,
    string VerificationStatus,
    string Outcome);

internal static class UpdateResultPolicy
{
    public static UpdateResultDecision Resolve(
        bool installerSucceeded,
        bool restartRequired,
        UpdateVerificationResult verification)
    {
        if (verification.Verified)
        {
            return new UpdateResultDecision(
                true,
                true,
                UpdateVerificationStatuses.Verified,
                UpdateOutcomes.Completed);
        }

        if (restartRequired ||
            verification.Status.Equals(UpdateVerificationStatuses.PendingRestart, StringComparison.Ordinal))
        {
            return new UpdateResultDecision(
                installerSucceeded,
                false,
                UpdateVerificationStatuses.PendingRestart,
                installerSucceeded ? UpdateOutcomes.Completed : UpdateOutcomes.Failed);
        }

        var success = installerSucceeded && !verification.IsDefinitive;
        return new UpdateResultDecision(
            success,
            false,
            verification.Status,
            success ? UpdateOutcomes.Completed : UpdateOutcomes.Failed);
    }
}

public sealed class UpdatePlan
{
    public bool CreateRestorePoint { get; set; }
    public bool SilentSoftwareInstall { get; set; }
    public string StatusFile { get; set; } = "";
    public string PauseFile { get; set; } = "";
    public int PauseOwnerProcessId { get; set; }
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
    public string PackageOperation { get; set; } = PackageOperations.Upgrade;
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
    public string Phase { get; set; } = "";
    public double CurrentItemProgress { get; set; }
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastProgressUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CurrentItemStartedUtc { get; set; }
    public string CurrentItemId { get; set; } = "";
    public string InstallerTool { get; set; } = "";
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
    public bool InstallerSucceeded { get; set; }
    public bool Verified { get; set; }
    public string VerificationStatus { get; set; } = UpdateVerificationStatuses.NotRun;
    public int? ResultCode { get; set; }
    public string Phase { get; set; } = "";
    public bool RestartRequired { get; set; }
    public string Outcome { get; set; } = UpdateOutcomes.Completed;
    public string Message { get; set; } = "";
    public string Diagnostics { get; set; } = "";
}
