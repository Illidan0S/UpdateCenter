namespace UpdateCenter.Models;

// Evidence for read-only verification only. Never authorizes an installer transition.
public sealed record WinGetVerificationBinding(string OldLocalId, string TargetLocalId,
    string Scope, string Architecture, string AccountSid, string ManifestSha256,
    string OldDisplayVersion, string TargetDisplayVersion,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? RemovalUpgradeCode = null);

// A reviewed execution constraint, not an authorization token or a payload signature.
// InstalledType identifies the exact-copy recipe. RegisteredInstallerCategory also
// records the provider's observed category for ordinary packages and Burn transitions.
public sealed record WinGetScopeBinding(string Scope, string LocalId, string AccountSid,
    string ClientVersion, string ManifestSha256, string InstallerType, string Architecture, string Safety,
    string ElevationRequirement = "", PythonBurnTransitionBinding? PythonBurnTransition = null,
    WinGetCopyEvidence? CopyEvidence = null, string DependencySnapshot = "", string InstalledType = "",
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? RegisteredInstallerCategory = null);

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
