namespace UpdateCenter.Models;

// Evidence for read-only verification only. Never authorizes an installer transition.
public sealed record WinGetVerificationBinding(string OldLocalId, string TargetLocalId,
    string Scope, string Architecture, string AccountSid, string ManifestSha256,
    string OldDisplayVersion, string TargetDisplayVersion);

// A reviewed execution constraint, not an authorization token or a payload signature.
public sealed record WinGetScopeBinding(string Scope, string LocalId, string AccountSid,
    string ClientVersion, string ManifestSha256, string InstallerType, string Architecture, string Safety,
    string ElevationRequirement = "", PythonBurnTransitionBinding? PythonBurnTransition = null,
    WinGetCopyEvidence? CopyEvidence = null, string DependencySnapshot = "", string InstalledType = "");

// Read-only continuity evidence. It never authorizes executing this path.
public sealed record WinGetCopyEvidence(string DisplayVersion, bool UnspecifiedScope = false,
    string ExecutablePath = "", string ProductName = "", string CompanyName = "",
    string ProductVersion = "", string ExecutableSha256 = "", MsiRegistrationEvidence? MsiRegistration = null);

// Registration identity, not permission to execute an MSI or infer a major upgrade.
public sealed record MsiRegistrationEvidence(string ProductCode, int Context, string UserSid,
    string ProductVersion, string Language, string PackageCode);

// Explicit old -> target bundle continuity. This is not an MSI ProductCode rule.
public sealed record PythonBurnTransitionBinding(string OldManifestSha256, string TargetLocalId,
    string UpgradeCode, string ProviderKey, string InstallPath, string FeatureNames,
    string ExecutableSha256, string WindowedExecutableSha256, string UserPathSha256,
    string CustomArguments, string LauncherStateSha256);
