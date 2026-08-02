namespace UpdateCenter.Contracts;

public static class AgentProtocol
{
    public const int MajorVersion = 1;
    public const int MinorVersion = 4;
    public const string ControlPipeName = "UpdateCenter.Agent.Control.v1";
    public const string ApprovalPipeName = "UpdateCenter.Agent.Approval.v1";
    public const int MaximumMessageBytes = 8 * 1024 * 1024;
    public const int MaximumScanItems = 2_000;
    public const int MaximumWarnings = 100;
    public const int MaximumCollectionItemsPerUpdate = 32;
    public const int MaximumUpdateItems = 256;
}

public static class AgentCommands
{
    public const string GetStatus = "GetStatus";
    public const string StartScan = "StartScan";
    public const string StartUpdate = "StartUpdate";
    public const string GetOperation = "GetOperation";
    public const string CancelOperation = "CancelOperation";
    public const string GetNetworkConfiguration = "GetNetworkConfiguration";
    public const string EnableNetwork = "EnableNetwork";
    public const string DisableNetwork = "DisableNetwork";
    public const string CreatePairingCode = "CreatePairingCode";
    public const string RevokeController = "RevokeController";
    public const string EnableConnectionRequests = "EnableConnectionRequests";
    public const string DisableConnectionRequests = "DisableConnectionRequests";
    public const string GetPendingConnectionRequests = "GetPendingConnectionRequests";
    public const string RespondConnectionRequest = "RespondConnectionRequest";
}

public static class AgentOperationStates
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string CompletedWithWarnings = "CompletedWithWarnings";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    public static bool IsTerminal(string state) => state is
        Completed or CompletedWithWarnings or Cancelled or Failed;
}

public sealed class AgentRequest
{
    public Guid RequestId { get; init; } = Guid.NewGuid();
    public int ProtocolMajor { get; init; } = AgentProtocol.MajorVersion;
    public int ProtocolMinor { get; init; } = AgentProtocol.MinorVersion;
    public string Command { get; init; } = "";
    public Guid? OperationId { get; init; }
    public ScanRequest? Scan { get; init; }
    public RemoteUpdateRequest? Update { get; init; }
    public ConnectionRequestDecision? ConnectionDecision { get; init; }
}

public sealed class AgentResponse
{
    public Guid RequestId { get; init; }
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = "";
    public string Message { get; init; } = "";
    public AgentStatus? Status { get; init; }
    public AgentOperation? Operation { get; init; }
    public AgentNetworkConfiguration? Network { get; init; }
    public PairingCodeInfo? PairingCode { get; init; }
    public IReadOnlyList<PendingConnectionRequest> ConnectionRequests { get; init; } = [];

    public static AgentResponse Ok(Guid requestId, string message = "") => new()
    {
        RequestId = requestId,
        Success = true,
        Message = message
    };

    public static AgentResponse Error(Guid requestId, string code, string message) => new()
    {
        RequestId = requestId,
        Success = false,
        ErrorCode = code,
        Message = message
    };
}

public sealed class AgentNetworkConfiguration
{
    public bool Enabled { get; init; }
    public Guid AgentId { get; init; }
    public string DisplayName { get; init; } = "";
    public int DiscoveryPort { get; init; } = 47381;
    public int ApiPort { get; init; } = 47382;
    public bool HasController { get; init; }
    public string ControllerName { get; init; } = "";
    public string CertificateSha256 { get; init; } = "";
    public bool RestartRequired { get; init; }
    public string NetworkScopeName { get; init; } = "";
    public bool NetworkScopeActive { get; init; }
    public IReadOnlyList<string> AllowedSubnets { get; init; } = [];
    public bool ConnectionRequestsEnabled { get; init; }
    public DateTime ConnectionRequestsExpiresUtc { get; init; }
    public int PendingConnectionRequestCount { get; init; }
}

public sealed class PairingCodeInfo
{
    public string Code { get; init; } = "";
    public DateTime ExpiresUtc { get; init; }
}

public static class ConnectionRequestStates
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";
}

public sealed class ConnectionRequestCreate
{
    public Guid ControllerId { get; init; }
    public string ControllerName { get; init; } = "";
    public string ControllerCertificateBase64 { get; init; } = "";
}

public sealed class ConnectionRequestStatusQuery
{
    public string PollToken { get; init; } = "";
}

public sealed class ConnectionRequestResponse
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = "";
    public string Message { get; init; } = "";
    public Guid RequestId { get; init; }
    public string Status { get; init; } = "";
    public string PollToken { get; init; } = "";
    public DateTime ExpiresUtc { get; init; }
    public Guid AgentId { get; init; }
    public string AgentCertificateSha256 { get; init; } = "";
}

public sealed class PendingConnectionRequest
{
    public Guid RequestId { get; init; }
    public Guid ControllerId { get; init; }
    public string ControllerName { get; init; } = "";
    public string ControllerCertificateSha256 { get; init; } = "";
    public string RemoteAddress { get; init; } = "";
    public DateTime RequestedUtc { get; init; }
    public DateTime ExpiresUtc { get; init; }
    public string Status { get; init; } = ConnectionRequestStates.Pending;
}

public sealed class ConnectionRequestDecision
{
    public Guid RequestId { get; init; }
    public bool Accept { get; init; }
}

public static class DiscoveryProtocol
{
    public const string Magic = "UpdateCenter.Discovery.v1";
    public const int DefaultPort = 47381;
    public const int MaximumDatagramBytes = 4 * 1024;
}

public static class SignedRequestProtocol
{
    public const string ControllerHeader = "X-UpdateCenter-Controller";
    public const string TimestampHeader = "X-UpdateCenter-Timestamp";
    public const string NonceHeader = "X-UpdateCenter-Nonce";
    public const string SignatureHeader = "X-UpdateCenter-Signature";

    public static string BuildCanonical(
        string method,
        string pathAndQuery,
        string timestamp,
        string nonce,
        ReadOnlySpan<byte> body)
    {
        var bodyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body));
        return $"{method.ToUpperInvariant()}\n{pathAndQuery}\n{timestamp}\n{nonce}\n{bodyHash}";
    }
}

public sealed class DiscoveryRequest
{
    public string Magic { get; init; } = DiscoveryProtocol.Magic;
    public Guid RequestId { get; init; } = Guid.NewGuid();
    public int ProtocolMajor { get; init; } = AgentProtocol.MajorVersion;
}

public sealed class DiscoveredAgent
{
    public string Magic { get; init; } = DiscoveryProtocol.Magic;
    public Guid RequestId { get; init; }
    public Guid AgentId { get; init; }
    public string DisplayName { get; init; } = "";
    public string MachineName { get; init; } = "";
    public string Address { get; init; } = "";
    public int ApiPort { get; init; }
    public int ProtocolMajor { get; init; }
    public int ProtocolMinor { get; init; }
    public string AgentVersion { get; init; } = "";
    public string CertificateSha256 { get; init; } = "";
    public bool HasController { get; init; }
    public bool ConnectionRequestsEnabled { get; init; }
    public DateTime ConnectionRequestsExpiresUtc { get; init; }
}

public sealed class PairingRequest
{
    public string Code { get; init; } = "";
    public Guid ControllerId { get; init; }
    public string ControllerName { get; init; } = "";
    public string ControllerCertificateBase64 { get; init; } = "";
}

public sealed class PairingResponse
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = "";
    public string Message { get; init; } = "";
    public Guid AgentId { get; init; }
    public string AgentCertificateSha256 { get; init; } = "";
}

public sealed class AgentStatus
{
    public string AgentVersion { get; init; } = "";
    public string MachineName { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public DateTime StartedUtc { get; init; }
    public bool NetworkListenerEnabled { get; init; }
    public bool OperationInProgress { get; init; }
    public Guid ActiveOperationId { get; init; }
    public string ActiveOperationKind { get; init; } = "";
    public string ControllerName { get; init; } = "";
    public int ProtocolMajor { get; init; } = AgentProtocol.MajorVersion;
    public int ProtocolMinor { get; init; } = AgentProtocol.MinorVersion;
    public IReadOnlyList<string> Capabilities { get; init; } = [];
}

public sealed class ScanRequest
{
    public bool IncludeUnknownVersions { get; init; }
    public bool IncludeSoftware { get; init; } = true;
    public bool IncludeDrivers { get; init; } = true;
    public bool IncludeRuntimes { get; init; } = true;
}

public sealed class AgentOperation
{
    public Guid Id { get; init; }
    public string Kind { get; init; } = "";
    public string State { get; init; } = AgentOperationStates.Queued;
    public string Message { get; init; } = "";
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
    public ScanResult? ScanResult { get; init; }
    public RemoteUpdateResult? UpdateResult { get; init; }
    public int CurrentIndex { get; init; }
    public int Total { get; init; }
    public string CurrentItemName { get; init; } = "";
    public string Phase { get; init; } = "";
    public double CurrentItemProgress { get; init; }
    public bool RestartRequired { get; init; }
}

public sealed class RemoteUpdateRequest
{
    public Guid ScanOperationId { get; init; }
    public IReadOnlyList<RemoteUpdateSelection> Items { get; init; } = [];
}

public sealed class RemoteUpdateSelection
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public bool RiskConfirmed { get; init; }
}

public sealed class RemoteUpdateResult
{
    public DateTime StartedUtc { get; init; }
    public DateTime CompletedUtc { get; init; }
    public bool RestartRequired { get; init; }
    public IReadOnlyList<RemoteUpdateItemResult> Results { get; init; } = [];
}

public sealed class RemoteUpdateItemResult
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public bool Success { get; init; }
    public bool RestartRequired { get; init; }
    public string Outcome { get; init; } = "";
    public string Message { get; init; } = "";
}

public static class SessionHelperCommands
{
    public const string Scan = "Scan";
    public const string Install = "Install";
}

public sealed class SessionHelperRequest
{
    public string Command { get; init; } = "";
    public ScanRequest? Scan { get; init; }
    public IReadOnlyList<RemoteUpdateItem> Updates { get; init; } = [];
}

public sealed class SessionHelperResponse
{
    public bool IsFinal { get; init; }
    public bool Success { get; init; }
    public string Error { get; init; } = "";
    public ScanResult? ScanResult { get; init; }
    public RemoteUpdateResult? UpdateResult { get; init; }
    public int CurrentIndex { get; init; }
    public int Total { get; init; }
    public string CurrentItemName { get; init; } = "";
    public string Message { get; init; } = "";
    public string Phase { get; init; } = "";
    public double CurrentItemProgress { get; init; }
    public bool RestartRequired { get; init; }
}

public sealed class ScanResult
{
    public DateTime StartedUtc { get; init; }
    public DateTime CompletedUtc { get; init; }
    public string MachineName { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string UserName { get; init; } = "";
    public IReadOnlyList<RemoteUpdateItem> Updates { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public int InstalledDriverCount { get; init; }
    public int RuntimeCheckCount { get; init; }
    public bool HasBattery { get; init; }
    public bool IsOnBattery { get; init; }
    public int BatteryPercentage { get; init; } = -1;
    public long SystemDriveFreeBytes { get; init; }
}

public sealed class RemoteUpdateItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string InstalledVersion { get; init; } = "";
    public string AvailableVersion { get; init; } = "";
    public string Source { get; init; } = "";
    public string Status { get; init; } = "";
    public string ResultDetails { get; init; } = "";
    public string PackageOperation { get; init; } = "";
    public bool CanInstall { get; init; }
    public bool RequiresRestart { get; init; }
    public bool IsImportant { get; init; }
    public bool IsOptional { get; init; }
    public bool RequiresRiskConfirmation { get; init; }
    public long DownloadSizeBytes { get; init; }
    public bool HasUnverifiedInstallerMetadata { get; init; }
    public string? WindowsUpdateId { get; init; }
    public int WindowsUpdateRevision { get; init; }
    public int WindowsUpdateServerSelection { get; init; }
    public string WindowsUpdateServiceId { get; init; } = "";
    public string DriverInstallMode { get; init; } = "";
    public string OfficialReleasePageUrl { get; init; } = "";
    public string OfficialDownloadUrl { get; init; } = "";
    public string ExpectedSha256 { get; init; } = "";
    public IReadOnlyList<string> ExpectedSignerSubjects { get; init; } = [];
    public string DriverPackageType { get; init; } = "";
    public IReadOnlyList<string> CompatibleHardwareIds { get; init; } = [];
}
