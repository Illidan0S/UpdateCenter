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

public static class UpdateFailureReasons
{
    public const string None = "";
    public const string FilesInUse = "FilesInUse";
    public const string Infrastructure = "Infrastructure";
}

public enum ItemExecutionState
{
    Pending,
    InitialAttempt,
    Recovery,
    Retry,
    Terminal
}

public enum ItemTerminalDisposition
{
    None,
    Succeeded,
    Failed,
    NotAutomaticallyApplicable,
    Cancelled
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
    public string FailureReason { get; set; } = UpdateFailureReasons.None;
    public bool RestartRequired { get; set; }
    public string Outcome { get; set; } = UpdateOutcomes.Completed;
    public ItemExecutionState ExecutionState { get; set; } = ItemExecutionState.InitialAttempt;
    public ItemTerminalDisposition TerminalDisposition { get; set; }
    public string Message { get; set; } = "";
    public string Diagnostics { get; set; } = "";
}

internal sealed class ItemExecutionStateMachine
{
    public ItemExecutionState State { get; private set; } = ItemExecutionState.Pending;

    public void BeginInitialAttempt() => Transition(ItemExecutionState.Pending, ItemExecutionState.InitialAttempt);

    public void BeginRecovery() => Transition(ItemExecutionState.InitialAttempt, ItemExecutionState.Recovery);

    public void BeginRetry() => Transition(ItemExecutionState.Recovery, ItemExecutionState.Retry);

    public ItemRunResult Complete(ItemRunResult result)
    {
        if (State == ItemExecutionState.Pending || State == ItemExecutionState.Terminal)
            throw new InvalidOperationException($"Transizione terminale non valida dallo stato {State}.");

        result.ExecutionState = ItemExecutionState.Terminal;
        result.TerminalDisposition = ResolveDisposition(result);
        State = ItemExecutionState.Terminal;
        return result;
    }

    internal static ItemTerminalDisposition ResolveDisposition(ItemRunResult result)
    {
        if (result.Outcome.Equals(UpdateOutcomes.NotApplicable, StringComparison.Ordinal) ||
            result.Outcome.Equals(UpdateOutcomes.ManualRequired, StringComparison.Ordinal))
            return ItemTerminalDisposition.NotAutomaticallyApplicable;
        return result.Success
            ? ItemTerminalDisposition.Succeeded
            : ItemTerminalDisposition.Failed;
    }

    private void Transition(ItemExecutionState expected, ItemExecutionState next)
    {
        if (State != expected)
            throw new InvalidOperationException($"Transizione item non valida: {State} -> {next}.");
        State = next;
    }
}

internal sealed class UpdateSessionResultLedger
{
    private readonly List<ItemRunResult> _results = [];
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ItemRunResult> Results => _results;

    public void Record(ItemRunResult result)
    {
        if (result.ExecutionState != ItemExecutionState.Terminal ||
            result.TerminalDisposition == ItemTerminalDisposition.None)
            throw new InvalidOperationException($"L'item {result.Id} non possiede un risultato terminale.");
        if (!_keys.Add(Key(result.Id, result.Kind)))
            throw new InvalidOperationException($"Risultato terminale duplicato per {result.Kind}:{result.Id}.");
        _results.Add(result);
        UpdateCenter.Services.LogService.WriteEvent("update", "terminal-result",
            result.TerminalDisposition == ItemTerminalDisposition.Succeeded && !result.Verified
                ? "completed-unverified" : result.TerminalDisposition.ToString(),
            result.Id, result.ResultCode,
            $"success={result.Success}; verified={result.Verified}; verification={result.VerificationStatus}; " +
            $"failureReason={result.FailureReason}; {result.Message}");
    }

    private static string Key(string id, string kind) => $"{kind.Trim()}\u001f{id.Trim()}";
}

internal readonly record struct UpdateSessionSummary(
    int ProcessedTerminalCount,
    int SucceededCount,
    int FailedCount,
    int NotAutomaticallyApplicableCount,
    int CancelledCount)
{
    public bool HasProblems => FailedCount > 0 || NotAutomaticallyApplicableCount > 0 || CancelledCount > 0;

    public static UpdateSessionSummary From(IReadOnlyList<ItemRunResult> results)
    {
        var ledger = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (result.ExecutionState != ItemExecutionState.Terminal ||
                result.TerminalDisposition == ItemTerminalDisposition.None)
                throw new InvalidOperationException($"Il riepilogo ha ricevuto un risultato non terminale per {result.Id}.");
            if (!ledger.Add($"{result.Kind.Trim()}\u001f{result.Id.Trim()}"))
                throw new InvalidOperationException($"Il riepilogo ha ricevuto un risultato duplicato per {result.Kind}:{result.Id}.");
        }

        var summary = new UpdateSessionSummary(
            results.Count,
            results.Count(x => x.TerminalDisposition == ItemTerminalDisposition.Succeeded),
            results.Count(x => x.TerminalDisposition == ItemTerminalDisposition.Failed),
            results.Count(x => x.TerminalDisposition == ItemTerminalDisposition.NotAutomaticallyApplicable),
            results.Count(x => x.TerminalDisposition == ItemTerminalDisposition.Cancelled));
        if (summary.ProcessedTerminalCount != summary.SucceededCount + summary.FailedCount +
            summary.NotAutomaticallyApplicableCount + summary.CancelledCount)
            throw new InvalidOperationException("I contatori terminali della sessione non sono coerenti.");
        return summary;
    }
}
