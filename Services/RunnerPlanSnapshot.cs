using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

// A private, defensive copy is the authority for both presentation and execution.
// No mutable DTO or collection belonging to this snapshot escapes this type.
internal sealed class RunnerPlanSnapshot
{
    public bool CreateRestorePoint { get; }
    public bool SilentSoftwareInstall { get; }
    public bool Interactive { get; }
    public bool Recovery { get; }
    public string StatusFile { get; }
    public string PauseFile { get; }
    public int PauseOwnerProcessId { get; }
    public ReadOnlyCollection<RunnerPlanItem> Items { get; }
    // The received file cannot attest installer risk. Every batch requires acknowledgement.
    public bool RequiresRiskAcknowledgement => true;

    private RunnerPlanSnapshot(UpdatePlan plan, bool requireAdministrator, bool interactive, bool recovery)
    {
        CreateRestorePoint = plan.CreateRestorePoint;
        SilentSoftwareInstall = plan.SilentSoftwareInstall;
        Interactive = interactive;
        Recovery = recovery;
        StatusFile = plan.StatusFile;
        PauseFile = plan.PauseFile;
        PauseOwnerProcessId = plan.PauseOwnerProcessId;
        if (plan.Items is null || plan.Items.Count is < 1 or > 1000 || PauseOwnerProcessId < 0 ||
            (interactive && (requireAdministrator || SilentSoftwareInstall || CreateRestorePoint || recovery)) ||
            (recovery && (requireAdministrator || CreateRestorePoint)))
            throw Invalid();
        Items = Array.AsReadOnly(plan.Items.Select(item => new RunnerPlanItem(item)).ToArray());
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var frozen in Items)
        {
            var item = frozen.ToPlanItem();
            ValidateItem(item, requireAdministrator);
            if (interactive && item.WinGetBinding?.PythonBurnTransition is not null) throw Invalid();
            if (!identities.Add(item.Kind + "\u001f" + item.Id)) throw Invalid();
        }
    }

    public static RunnerPlanSnapshot Create(UpdatePlan plan, bool requireAdministrator, bool interactive = false, bool recovery = false) =>
        new(plan, requireAdministrator, interactive, recovery);

    public static RunnerPlanSnapshot Read(string path, bool requireAdministrator)
    {
        // Exactly one file read. Required fields are checked before DTO defaults can hide omissions.
        var root = JsonStorage.Read<JsonElement>(path);
        var fields = Fields(root, "CreateRestorePoint", "SilentSoftwareInstall", "StatusFile", "PauseFile", "PauseOwnerProcessId", "Items");
        if (fields["Items"].ValueKind != JsonValueKind.Array) throw Invalid();
        foreach (var element in fields["Items"].EnumerateArray())
        {
            var item = Fields(element, "Id", "Name", "Kind", "Source", "InstalledVersion", "AvailableVersion", "PackageOperation", "DriverInstallMode");
            if (item.TryGetValue("WinGetBinding", out var binding) && binding.ValueKind != JsonValueKind.Null)
            {
                var constraints = Fields(binding, "Scope", "LocalId", "AccountSid", "ClientVersion", "ManifestSha256", "InstallerType", "Architecture", "Safety");
                if (constraints.TryGetValue("PythonBurnTransition", out var transition) && transition.ValueKind != JsonValueKind.Null)
                    Fields(transition, "OldManifestSha256", "TargetLocalId", "UpgradeCode", "ProviderKey", "InstallPath", "FeatureNames",
                        "ExecutableSha256", "WindowedExecutableSha256", "UserPathSha256", "CustomArguments", "LauncherStateSha256");
                if (constraints.TryGetValue("CopyEvidence", out var copy) && copy.ValueKind != JsonValueKind.Null)
                {
                    var proof = Fields(copy, "DisplayVersion", "UnspecifiedScope", "ExecutablePath", "ProductName", "CompanyName", "ProductVersion", "ExecutableSha256");
                    if (proof.TryGetValue("MsiRegistration", out var msi) && msi.ValueKind != JsonValueKind.Null)
                        Fields(msi, "ProductCode", "Context", "UserSid", "ProductVersion", "Language", "PackageCode");
                }
            }
            if (item["DriverInstallMode"].GetString() == DriverInstallModes.WindowsUpdate)
                Fields(element, "WindowsUpdateId", "WindowsUpdateRevision", "WindowsUpdateServerSelection", "WindowsUpdateServiceId");
            if (item["DriverInstallMode"].GetString() == DriverInstallModes.OfficialInfPackage)
                Fields(element, "Vendor", "OfficialReleasePageUrl", "OfficialDownloadUrl", "ExpectedSha256",
                    "ExpectedSignerSubjects", "DriverPackageType", "CompatibleHardwareIds", "DriverDeviceId");
        }
        var plan = root.Deserialize<UpdatePlan>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw Invalid();
        return Create(plan, requireAdministrator);
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            if (!fields.TryAdd(property.Name, property.Value)) throw Invalid();
        if (required.Any(name => !fields.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)) throw Invalid();
        return fields;
    }

    private static void ValidateItem(PlanItem item, bool requireAdministrator)
    {
        foreach (var text in new[] { item.Id, item.Name, item.Source, item.InstalledVersion, item.AvailableVersion,
            item.PackageOperation, item.WinGetClientVersion, item.Kind, item.DriverInstallMode, item.DriverDeviceId, item.Vendor, item.OfficialReleasePageUrl,
            item.OfficialDownloadUrl, item.ExpectedSha256, item.DriverPackageType, item.WindowsUpdateServiceId })
            ValidateText(text);
        foreach (var text in item.ExpectedSignerSubjects.Concat(item.CompatibleHardwareIds))
        { ValidateText(text); if (string.IsNullOrWhiteSpace(text)) throw Invalid(); }
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name) ||
            string.IsNullOrWhiteSpace(item.Source) || string.IsNullOrWhiteSpace(item.AvailableVersion) ||
            item.PackageOperation is not (PackageOperations.Upgrade or PackageOperations.Install)) throw Invalid();
        var driver = item.Kind == nameof(UpdateKind.Driver);
        if (driver && item.WinGetClientVersion.Length != 0) throw Invalid();
        if (item.WinGetVerification is { } verification &&
            (driver || item.WinGetBinding is not null || !WinGetScopeGuard.ValidVerificationBinding(verification))) throw Invalid();
        if (item.WinGetBinding is { } binding && (driver || !WinGetScopeGuard.ValidBinding(binding) ||
            (item.WinGetClientVersion.Length != 0 && item.WinGetClientVersion != binding.ClientVersion))) throw Invalid();
        if (item.WinGetBinding is { } scoped)
            foreach (var text in new[] { scoped.Scope, scoped.LocalId, scoped.AccountSid, scoped.ClientVersion,
                scoped.ManifestSha256, scoped.InstallerType, scoped.Architecture, scoped.Safety, scoped.ElevationRequirement }) ValidateText(text);
        if (item.WinGetBinding?.CopyEvidence is { } copyEvidence)
            foreach (var text in new[] { copyEvidence.DisplayVersion, copyEvidence.ExecutablePath, copyEvidence.ProductName,
                copyEvidence.CompanyName, copyEvidence.ProductVersion, copyEvidence.ExecutableSha256 }) ValidateText(text);
        if (driver != requireAdministrator || item.Kind is not (nameof(UpdateKind.Driver) or nameof(UpdateKind.Software) or nameof(UpdateKind.Runtime)))
            throw Invalid();
        if (!driver)
        {
            if (item.DriverInstallMode != "" || HasWuaMetadata(item) || HasPackageMetadata(item) || WinGetService.ValidateTarget(item) is not null)
                throw Invalid();
            return;
        }
        // The current driver providers perform an update, not a WinGet fresh-install operation.
        if (item.PackageOperation != PackageOperations.Upgrade) throw Invalid();
        if (item.DriverInstallMode == DriverInstallModes.WindowsUpdate)
        {
            if (!Guid.TryParse(item.WindowsUpdateId, out var updateId) || updateId == Guid.Empty ||
                !Guid.TryParse(item.Id, out var id) || id != updateId || item.WindowsUpdateRevision < 0 ||
                item.WindowsUpdateServerSelection is < 0 or > 3 || HasPackageMetadata(item)) throw Invalid();
            if (item.WindowsUpdateServerSelection == 3)
            {
                // The scanner only offers the registered Microsoft Update service in this mode.
                if (!item.WindowsUpdateServiceId.Equals("7971f918-a847-4430-9279-4a52d1efe18d", StringComparison.OrdinalIgnoreCase)) throw Invalid();
            }
            else if (item.WindowsUpdateServiceId != "") throw Invalid();
        }
        else if (item.DriverInstallMode == DriverInstallModes.OfficialInfPackage)
        {
            if (HasWuaMetadata(item)) throw Invalid();
            OfficialDriverPackageService.ValidatePlan(item);
        }
        else throw Invalid();
    }

    private static bool HasWuaMetadata(PlanItem item) => !string.IsNullOrEmpty(item.WindowsUpdateId) ||
        item.WindowsUpdateRevision != 0 || item.WindowsUpdateServerSelection != 0 || item.WindowsUpdateServiceId != "";
    private static bool HasPackageMetadata(PlanItem item) => item.OfficialReleasePageUrl != "" || item.OfficialDownloadUrl != "" ||
        item.DriverDeviceId != "" || item.ExpectedSha256 != "" || item.DriverPackageType != "" || item.ExpectedSignerSubjects.Count != 0 || item.CompatibleHardwareIds.Count != 0;

    private static void ValidateText(string? text)
    {
        if (text is null || text.Length > 4096 || text.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format))
            throw Invalid();
    }

    private static InvalidOperationException Invalid() => new(LocalizationService.Text(
        "Piano incompleto, incoerente o non supportato. Nessuna operazione autorizzata.",
        "Incomplete, inconsistent or unsupported plan. No operation authorized."));
}

internal sealed class RunnerPlanItem
{
    private readonly PlanItem _item;
    public string Id => _item.Id;
    public string Name => _item.Name;
    public string Kind => _item.Kind;
    public string AvailableVersion => _item.AvailableVersion;
    public RunnerPlanItem(PlanItem item) => _item = Copy(item);
    public PlanItem ToPlanItem() => Copy(_item);
    public UpdateItem ToUpdateItem() => new()
    {
        Id = _item.Id, Name = _item.Name, Kind = Enum.Parse<UpdateKind>(_item.Kind), Source = _item.Source,
        InstalledVersion = _item.InstalledVersion, AvailableVersion = _item.AvailableVersion, PackageOperation = _item.PackageOperation,
        WinGetClientVersion = _item.WinGetClientVersion,
        WinGetBinding = _item.WinGetBinding,
        WinGetVerification = _item.WinGetVerification,
        WindowsUpdateId = _item.WindowsUpdateId, WindowsUpdateRevision = _item.WindowsUpdateRevision,
        WindowsUpdateServerSelection = _item.WindowsUpdateServerSelection, WindowsUpdateServiceId = _item.WindowsUpdateServiceId,
        DriverInstallMode = _item.DriverInstallMode, DriverDeviceId = _item.DriverDeviceId, Publisher = _item.Vendor, OfficialReleasePageUrl = _item.OfficialReleasePageUrl,
        OfficialDownloadUrl = _item.OfficialDownloadUrl, ExpectedSha256 = _item.ExpectedSha256,
        ExpectedSignerSubjects = [.. _item.ExpectedSignerSubjects], DriverPackageType = _item.DriverPackageType,
        CompatibleHardwareIds = [.. _item.CompatibleHardwareIds], RequiresRiskConfirmation = true, HasUnverifiedInstallerMetadata = true
    };

    internal static bool Matches(UpdateItem expected, PlanItem actual) =>
        JsonSerializer.Serialize(ElevatedUpdateRunner.ToPlanItem(expected)) == JsonSerializer.Serialize(actual);


    private static PlanItem Copy(PlanItem item) => new()
    {
        Id = item.Id, Name = item.Name, Kind = item.Kind, Source = item.Source,
        InstalledVersion = item.InstalledVersion, AvailableVersion = item.AvailableVersion, PackageOperation = item.PackageOperation,
        WinGetClientVersion = item.WinGetClientVersion,
        WinGetBinding = item.WinGetBinding,
        WinGetVerification = item.WinGetVerification,
        WindowsUpdateId = item.WindowsUpdateId, WindowsUpdateRevision = item.WindowsUpdateRevision,
        WindowsUpdateServerSelection = item.WindowsUpdateServerSelection, WindowsUpdateServiceId = item.WindowsUpdateServiceId,
        DriverInstallMode = item.DriverInstallMode, DriverDeviceId = item.DriverDeviceId, Vendor = item.Vendor, OfficialReleasePageUrl = item.OfficialReleasePageUrl,
        OfficialDownloadUrl = item.OfficialDownloadUrl, ExpectedSha256 = item.ExpectedSha256,
        ExpectedSignerSubjects = [.. item.ExpectedSignerSubjects], DriverPackageType = item.DriverPackageType,
        CompatibleHardwareIds = [.. item.CompatibleHardwareIds]
    };
}
