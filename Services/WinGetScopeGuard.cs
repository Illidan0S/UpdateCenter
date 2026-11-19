using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal sealed partial class WinGetScopeGuard(WinGetCommand execute,
    Func<string, string, CancellationToken, Task<string?>> readManifest,
    Func<string> readSid, Func<string, string, string> readScope, string architecture,
    TimeSpan? validationTimeout = null, Func<bool>? isAdministrator = null, Version? windowsVersion = null,
    Func<string, string?>? readDisplayVersion = null, Func<PythonBurnInventory?>? readPythonInventory = null,
    Func<TimeSpan, CancellationToken, Task>? waitBeforeRetry = null,
    Func<string, WinGetCopyEvidence?>? readCopyEvidence = null,
    Func<string, bool?>? registrationExists = null, Func<string, string, bool>? hasUniqueBurnRegistration = null)
{
    internal const string ReviewedClient = "v1.29.290";
    internal const string ReviewedClient130 = "v1.30.300";
    internal const string ReviewedClient130310 = "v1.30.310";
    internal static bool IsReviewedClient(string? version) =>
        version is ReviewedClient or ReviewedClient130 or ReviewedClient130310;
    internal static WinGetScopeGuard Native(WinGetCommand execute) =>
        Native(execute, WinGetManifestSafetyService.ReadManifestAsync);

    internal static WinGetScopeGuard Native(WinGetCommand execute,
        Func<string, string, CancellationToken, Task<string?>> readManifest) => new(execute,
        readManifest, CurrentSid, WinGetInstalledScopeService.Detect,
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        readDisplayVersion: WinGetInstalledScopeService.ReadDisplayVersion,
        readPythonInventory: PythonBurnTransitionService.ReadInventory,
        waitBeforeRetry: Task.Delay, readCopyEvidence: WinGetCopyEvidenceReader.Read,
        registrationExists: WinGetInstalledScopeService.RegistrationExists,
        hasUniqueBurnRegistration: BurnRegistrationReader.HasUniqueRegistration);

    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? "";
    }

    internal static bool CurrentIsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal async Task<WinGetScopeBinding?> PrepareAsync(UpdateItem item, string manifest, CancellationToken token)
    {
        if (!item.CanInstall || item.PackageOperation != PackageOperations.Upgrade || !IsOfficialSource(item.Source)) return null;
        if (PythonBurnTransitionService.IsCandidate(item.Id))
            return await PreparePythonAsync(item, manifest, token).ConfigureAwait(false);
        var scope = readScope(item.Name, item.InstalledVersion);
        if (scope == "ambiguous") return null;
        var sid = readSid();
        if (!ValidSid(sid)) return null;
        var clientVersion = await ReadClientVersionAsync(token).ConfigureAwait(false);
        if (clientVersion is null || !string.IsNullOrEmpty(item.WinGetClientVersion) && item.WinGetClientVersion != clientVersion) return null;
        var identity = await ReadIdentity(item.Id, item.Source, scope, token).ConfigureAwait(false);
        if (scope.Length == 0)
        {
            // A catalog range/mapping is not an ARP DisplayVersion. Resolve the
            // exact native identity instead of guessing scope from name/version.
            if (identity is null || !SoftwareVersionEvidence.IsValidVersion(readDisplayVersion?.Invoke(identity.LocalId))) return null;
            scope = identity.Scope;
            if (await ReadIdentity(item.Id, item.Source, scope, token).ConfigureAwait(false) != identity) return null;
        }
        if (identity is null || identity.Name != item.Name || identity.Version != item.InstalledVersion ||
            identity.Scope != scope || readSid() != sid) return null;
        var copy = readCopyEvidence?.Invoke(identity.LocalId);
        if (readCopyEvidence is not null && (copy is null || !WinGetCopyEvidenceReader.Valid(copy))) return null;
        var metadata = WinGetInstallerMetadata.Parse(manifest);
        var unspecified = copy is not null && metadata?.Installers.All(x => x.Scope.Length == 0) == true;
        if (copy is not null) copy = copy with { UnspecifiedScope = unspecified };
        var selection = Select(manifest, item.Id, item.AvailableVersion, scope, identity.Architecture, identity.LocalId, unspecified);
        var installedType = "";
        if (selection is null && ExactCopyRecipe(metadata, item.Id, item.InstalledVersion, item.AvailableVersion, identity, copy) is { } recipe)
        {
            selection = (recipe.Type, WinGetUpgradeSafety.Safe);
            installedType = identity.InstallerType;
        }
        if (selection is null || !InstalledTechnologyMatches(identity.InstallerType, selection.Value.Type)) return null;
        var binding = new WinGetScopeBinding(scope, identity.LocalId, sid, clientVersion, Digest(manifest), selection.Value.Type,
            identity.Architecture, selection.Value.Safety.ToString(),
            BoundElevation(metadata!, unspecified ? "" : scope, selection.Value.Type, identity.Architecture), CopyEvidence: copy,
            InstalledType: installedType, RegisteredInstallerCategory: identity.InstallerType.Length == 0 ? null : identity.InstallerType);
        if (!ValidBinding(binding)) return null;
        if (copy is not null && (CertainlyLowerTarget(item.AvailableVersion, binding, metadata!, copy) ||
            await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false) != identity ||
            await ReadIdentity(item.Id, item.Source, scope, token).ConfigureAwait(false) != identity ||
            readCopyEvidence?.Invoke(identity.LocalId) != copy with { UnspecifiedScope = false } || readSid() != sid)) return null;
        if (binding.Scope == "machine" &&
            metadata!.DispatchRecipes(binding.Scope, binding.InstallerType, binding.Architecture).Any(x => x.HasDependencies))
        {
            var dependencies = await CaptureDependencies(metadata, manifest, binding, identity, token).ConfigureAwait(false);
            if (dependencies is null || readSid() != sid ||
                readCopyEvidence?.Invoke(binding.LocalId) != copy! with { UnspecifiedScope = false } ||
                await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false) != identity) return null;
            binding = binding with { DependencySnapshot = dependencies };
        }
        return await ClientMatches(clientVersion, token).ConfigureAwait(false) ? binding : null;
    }

    internal Task<ProcessResult?> ValidateAsync(PlanItem item, CancellationToken token = default) =>
        RunnerReadOnlyWait.RunAsync(ct => ValidateCoreAsync(item, ct), token,
            validationTimeout ?? TimeSpan.FromSeconds(90), "winget-preflight");

    private async Task<ProcessResult?> ValidateCoreAsync(PlanItem item, CancellationToken token)
    {
        if (WinGetService.ValidateTarget(item) is { } invalid) return invalid;
        var approvedClient = item.WinGetBinding?.ClientVersion ?? item.WinGetClientVersion;
        if (item.WinGetBinding is not null && !string.IsNullOrEmpty(item.WinGetClientVersion) &&
            item.WinGetClientVersion != approvedClient) return Failure();
        // Even legacy plans must carry the exact client observation approved by
        // the user. Being another supported client does not preserve that plan.
        if (await CheckClientAsync(approvedClient, token).ConfigureAwait(false) is { } clientRejection) return clientRejection;
        if (item.PackageOperation != PackageOperations.Upgrade)
            return item.WinGetBinding is null ? null : Failure();
        if (!IsOfficialSource(item.Source)) return item.WinGetBinding is null ? null : Failure();
        var manifest = await readManifest(item.Id, item.AvailableVersion, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (manifest is null) return Failure();
        var metadata = WinGetInstallerMetadata.Parse(manifest);
        if (metadata is null || metadata.Id != item.Id || metadata.Version != item.AvailableVersion) return Failure();
        if (PythonBurnTransitionService.IsCandidate(item.Id) || item.WinGetBinding?.PythonBurnTransition is not null)
        {
            if (item.WinGetBinding is not { PythonBurnTransition: not null } python || !ValidBinding(python)) return Failure();
            var current = await PreparePythonAsync(new UpdateItem
            {
                Id = item.Id, Name = item.Name, Kind = UpdateKind.Software, Source = item.Source, InstalledVersion = item.InstalledVersion,
                AvailableVersion = item.AvailableVersion, PackageOperation = item.PackageOperation, CanInstall = true,
                WinGetClientVersion = approvedClient
            }, manifest, token).ConfigureAwait(false);
            if (current is null || !MatchesRegisteredCategory(python, current.RegisteredInstallerCategory ?? "")) return Failure();
            return current with { RegisteredInstallerCategory = python.RegisteredInstallerCategory } == python ? null : Failure();
        }
        if (item.WinGetBinding is not { } binding)
        {
            if (item.WinGetVerification is { } expected &&
                await PrepareVerificationAsync(new RunnerPlanItem(item).ToUpdateItem(), manifest, token).ConfigureAwait(false) != expected)
                return Failure();
            // A stripped binding cannot downgrade a multi-scope upgrade to legacy execution.
            var local = readScope(item.Name, item.InstalledVersion);
            var offered = WinGetManifestSafetyService.ParseInstallerScope(manifest);
            var safety = WinGetManifestSafetyService.ParseUpgradeSafety(manifest, architecture);
            if (safety == WinGetUpgradeSafety.RemovesPreviousVersion
                    ? item.RemovalManifestSha256 != Digest(manifest)
                    : !string.IsNullOrEmpty(item.RemovalManifestSha256))
                return new(1, "", LocalizationService.Text(
                    "Il consenso alla rimozione deve corrispondere al manifest corrente. Ripeti la scansione e la conferma.",
                    "Removal consent must match the current manifest. Scan and confirm again."));
            if (local == "ambiguous" || offered == "ambiguous" ||
                (local.Length > 0 && offered.Length > 0 && local != offered) ||
                safety == WinGetUpgradeSafety.UpgradeUnsupported)
                return Failure();
            var requirements = AssessRequirements(metadata, item.Id, item.AvailableVersion, architecture, local,
                windowsVersion: windowsVersion, isAdministrator: (isAdministrator ?? CurrentIsAdministrator)());
            if (!requirements.CanContinue) return new(1, "", requirements.Message);
            // Legacy plans still need a unique, unchanged native identity just
            // before dispatch. A catalog ID alone cannot authorize another copy.
            var installed = await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false);
            if (installed is null || installed.Name != item.Name || installed.Version != item.InstalledVersion ||
                local.Length > 0 && installed.Scope != local ||
                await ReadIdentity(item.Id, item.Source, installed.Scope, token).ConfigureAwait(false) != installed) return Failure();
            var currentCopy = readCopyEvidence?.Invoke(installed.LocalId);
            if (readCopyEvidence is not null && (currentCopy is null || !WinGetCopyEvidenceReader.Valid(currentCopy))) return Failure();
            if (installed.InstallerType is "msi" or "wix" || currentCopy?.MsiRegistration is not null)
                return new(1, "", LocalizationService.Text(
                    "La copia Windows Installer richiede una ricetta MSI vincolata alla sua registrazione.",
                    "The Windows Installer copy requires an MSI recipe bound to its registration."));
            if (installed.InstallerType.Length > 0 && PlatformCandidates(metadata, architecture, windowsVersion)
                    .Where(x => x.Scope.Length == 0 || x.Scope == installed.Scope)
                    .Any(x => !InstalledTechnologyMatches(installed.InstallerType, x.Type)))
                return Failure();
            if (currentCopy is not null)
            {
                var mapped = metadata.Installers.Where(x => x.Scope.Length == 0 || x.Scope == installed.Scope)
                    .Where(x => (x.MappingProductCode.Length > 0 ? x.MappingProductCode : x.ProductCode)
                        .Equals(installed.LocalId.Split('\\', 4).Last(), StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.DisplayVersion).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
                if (mapped.Length == 1 && SoftwareVersionEvidence.Compare(currentCopy.DisplayVersion, mapped[0]) == SoftwareVersionRelation.Greater)
                    return Failure();
                if (currentCopy != readCopyEvidence?.Invoke(installed.LocalId)) return Failure();
            }
            if (await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false) != installed) return Failure();
            return await CheckClientAsync(approvedClient, token).ConfigureAwait(false);
        }
        var observedScope = readScope(item.Name, item.InstalledVersion);
        if (!ValidBinding(binding) || readSid() != binding.AccountSid) return Failure();
        if (Digest(manifest) != binding.ManifestSha256 ||
            (binding.CopyEvidence is null ? observedScope != binding.Scope : observedScope.Length > 0 && observedScope != binding.Scope))
            return Failure();
        var identity = await ReadIdentity(item.Id, item.Source, binding.Scope, token).ConfigureAwait(false);
        if (identity is null || identity.Name != item.Name || identity.Version != item.InstalledVersion ||
            identity.LocalId != binding.LocalId || identity.Scope != binding.Scope || identity.Architecture != binding.Architecture ||
            !MatchesRegisteredCategory(binding, identity.InstallerType) ||
            (binding.InstalledType.Length > 0 && identity.InstallerType != binding.InstalledType) ||
            readSid() != binding.AccountSid) return Failure();
        if (binding.CopyEvidence is { } expectedCopy &&
            (readCopyEvidence?.Invoke(binding.LocalId) != expectedCopy with { UnspecifiedScope = false } ||
             await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false) != identity ||
             CertainlyLowerTarget(item.AvailableVersion, binding, metadata, expectedCopy))) return Failure();
        var unspecified = binding.CopyEvidence?.UnspecifiedScope == true;
        var selection = binding.InstalledType.Length == 0
            ? Select(manifest, item.Id, item.AvailableVersion, binding.Scope, binding.Architecture, binding.LocalId, unspecified)
            : ExactCopyRecipe(metadata, item.Id, item.InstalledVersion, item.AvailableVersion, identity, binding.CopyEvidence) is { } recipe
                ? (recipe.Type, WinGetUpgradeSafety.Safe) : ((string Type, WinGetUpgradeSafety Safety)?)null;
        if (selection is null || selection.Value.Type != binding.InstallerType || selection.Value.Safety.ToString() != binding.Safety ||
            BoundElevation(metadata, unspecified ? "" : binding.Scope, binding.InstallerType, binding.Architecture) != binding.ElevationRequirement)
            return Failure();
        var boundRequirements = AssessRequirements(metadata, item.Id, item.AvailableVersion, architecture, binding.Scope, binding,
            windowsVersion, (isAdministrator ?? CurrentIsAdministrator)());
        if (binding.DependencySnapshot.Length > 0 && (!WinGetDependencyPolicy.Matches(metadata, binding) ||
            await CaptureDependencies(metadata, manifest, binding, identity, token).ConfigureAwait(false) != binding.DependencySnapshot ||
            readSid() != binding.AccountSid || readCopyEvidence?.Invoke(binding.LocalId) != binding.CopyEvidence! with { UnspecifiedScope = false } ||
            await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false) != identity)) return Failure();
        if (!boundRequirements.CanContinue) return new(1, "", boundRequirements.Message);
        return await CheckClientAsync(approvedClient, token).ConfigureAwait(false);
    }

    private async Task<string?> CaptureDependencies(WinGetInstallerMetadata metadata, string manifest,
        WinGetScopeBinding binding, InstalledIdentity identity, CancellationToken token)
    {
        if (binding.ElevationRequirement == "elevationRequired")
            return await WinGetDependencyPolicy.CaptureAsync(metadata, binding,
                (id, scope, ct) => ReadIdentity(id, "winget", scope, ct), readDisplayVersion, token).ConfigureAwait(false);
        // The bounded metadata reader normally retains only Custom. This recipe
        // relies on the original installer's defaults, including its upgrade switches.
        if (Regex.IsMatch(manifest, @"(?im)^[ \t]*(?:-[ \t]+)?InstallerSwitches[ \t]*:") ||
            identity.InstallerType != "exe" || identity.Architecture != binding.Architecture ||
            !WinGetDependencyPolicy.SameNumericVersion(identity.Version, binding.CopyEvidence?.DisplayVersion))
            return null;
        var snapshot = await WinGetDependencyPolicy.CaptureAsync(metadata, binding,
            (id, scope, ct) => ReadIdentity(id, "winget", scope, ct, requireDependencyRegistration: true),
            readDisplayVersion, token).ConfigureAwait(false);
        return snapshot is not null &&
            await ReadVerificationIdentity(metadata.Id, "winget", "", token).ConfigureAwait(false) == identity &&
            await ReadVerificationIdentity(metadata.Id, "winget", binding.Scope, token).ConfigureAwait(false) == identity
            ? snapshot : null;
    }

    // The same bounded metadata is assessed at scan time and immediately before
    // dispatch. This is not WinGet selection: without a B2 binding every plausible
    // candidate remains relevant. Scope labels and preferred architecture alone
    // cannot authorize switching an installed MSIX to an EXE recipe.
    internal static WinGetRequirementAssessment AssessRequirements(WinGetInstallerMetadata? metadata,
        string id, string version, string architecture, string installedScope, WinGetScopeBinding? binding = null,
        Version? windowsVersion = null, bool isAdministrator = false)
    {
        if (metadata is null || metadata.Id != id || metadata.Version != version)
            return new(WinGetRequirementState.Unknown, false, LocalizationService.Translate(LocalizationService.WinGetRequirementsUnavailableMessage));

        var candidates = PlatformCandidates(metadata, architecture, windowsVersion).ToArray();
        if (binding is not null)
            candidates = candidates.Where(x => x.Scope == (binding.CopyEvidence?.UnspecifiedScope == true ? "" : binding.Scope) && x.Type == binding.InstallerType &&
                (binding.Architecture.Length == 0 || x.Architecture == binding.Architecture)).ToArray();
        if (candidates.Length == 0)
            return new(WinGetRequirementState.ManualRequired, false, LocalizationService.Translate(LocalizationService.WinGetPlatformUnsupportedMessage));

        var dispatchCandidates = binding is null ? candidates :
            metadata.DispatchRecipes(binding.Scope, binding.InstallerType, binding.Architecture);
        if (dispatchCandidates.Any(x => x.Type == "zip" && (x.NestedType is "msi" or "wix" ||
            x.NestedType.Length == 0 && binding?.InstallerType is "msi" or "wix")))
            return new(WinGetRequirementState.ManualRequired, false, LocalizationService.Text(
                "Un archivio può partecipare alla selezione MSI. Il runner non può vincolare il prodotto Windows Installer interno alla registrazione esistente.",
                "An archive can participate in MSI selection. The runner cannot bind the nested Windows Installer product to the existing registration."));
        if (candidates.Any(x => x.Type is "msi" or "wix") &&
            (binding?.CopyEvidence?.MsiRegistration is null || !ValidBinding(binding)))
            return new(WinGetRequirementState.ManualRequired, false, LocalizationService.Text(
                "Identità MSI non dimostrata: occorre una registrazione Windows Installer coerente con copia, account, versione e ambito. Il solo registro ARP non autorizza l'aggiornamento o un cambio di tecnologia.",
                "MSI identity is not proven: a Windows Installer registration must agree with the installation, account, version and scope. An ARP entry alone does not authorize an upgrade or a technology change."));

        // Only an exact, singular traditional machine recipe uses WinGet's reviewed
        // ShellExecute runas path. The user runner and its original account stay unchanged.
        if (binding is not null && SupportsNativeElevation(metadata, binding, candidates))
            return new(WinGetRequirementState.NativeInstallerElevation, true,
                LocalizationService.Translate(LocalizationService.WinGetNativeElevationMessage));

        // Without an exact binding, every viable candidate can contribute effects.
        // An exact binding excludes only recipes WinGet's filters cannot select.
        if (dispatchCandidates.Any(x => x.HasDependencies) &&
            (binding is not { ElevationRequirement: "" } || !WinGetDependencyPolicy.Matches(metadata, binding)))
            return new(WinGetRequirementState.ManualRequired, false, LocalizationService.Text(
                "Le dipendenze della ricetta WinGet non sono verificabili come già presenti.",
                "The WinGet recipe's dependencies could not be verified as already installed."));

        // Mixed unbound recipes do not prove which elevation requirement applies.
        if (binding is null && candidates.Any(RequiresUnsupportedElevation) && candidates.Any(x => !RequiresUnsupportedElevation(x)))
            return new(WinGetRequirementState.Unknown, false,
                LocalizationService.Translate(LocalizationService.WinGetSelectionUnknownMessage));

        // Explicit service capabilities remain adverse evidence even if another
        // field is missing or contradictory. A blank/type override cannot erase it.
        if (candidates.Any(x => x.Elevation.Equals("elevationRequired", StringComparison.OrdinalIgnoreCase) ||
            x.RestrictedCapabilities.Any(c => c is "packagedServices" or "localSystemServices")))
            return new(WinGetRequirementState.ManualRequired, false, LocalizationService.Translate(LocalizationService.WinGetElevationUnsupportedMessage));

        if (isAdministrator && candidates.Any(x => x.Elevation.Equals("elevationProhibited", StringComparison.OrdinalIgnoreCase)))
            return new(WinGetRequirementState.ManualRequired, false, LocalizationService.Translate(LocalizationService.WinGetElevationProhibitedMessage));

        if (binding is null && candidates.All(x => x.Type is "msix" or "appx"))
            return new(WinGetRequirementState.ManualRequired, false, LocalizationService.Text(
                "Pacchetto MSIX/AppX: il runner non supporta ancora la verifica della famiglia del pacchetto installato.",
                "MSIX/AppX package: the runner cannot yet verify the installed package family."));

        // Absence of a declaration is not evidence that no privilege, package
        // service, dependency or installer-specific condition will be required.
        var incomplete = candidates.Any(x => !KnownArchitecture(x.Architecture) ||
            x.Type is not ("exe" or "inno" or "nullsoft" or "burn" or "msi" or "wix") ||
            x.Scope is not ("user" or "machine") || x.Scope != installedScope ||
            x.Elevation is not ("elevationProhibited" or "elevatesSelf") ||
            (x.MinimumOSVersion.Length > 0 && !TryWindowsVersion(x.MinimumOSVersion, out _))) ||
            candidates.Select(x => x.Type).Distinct(StringComparer.Ordinal).Count() != 1;
        if (incomplete)
            return new(WinGetRequirementState.Unknown, true, LocalizationService.Translate(LocalizationService.WinGetRequirementsUnknownMessage));

        if (candidates.Any(x => x.Elevation == "elevatesSelf"))
            return new(WinGetRequirementState.InstallerSelfElevation, true, LocalizationService.Translate(LocalizationService.WinGetSelfElevationMessage));
        return new(WinGetRequirementState.Supported, true, LocalizationService.Translate(LocalizationService.WinGetRequirementsSupportedMessage));
    }

    private static bool RequiresUnsupportedElevation(WinGetInstallerVariant variant) =>
        variant.Elevation == "elevationRequired" || variant.RestrictedCapabilities.Any(c => c is "packagedServices" or "localSystemServices");

    private static string BoundElevation(WinGetInstallerMetadata metadata, string scope, string type, string architecture)
    {
        var recipes = metadata.Installers.Where(x => x.Scope == scope && x.Type == type &&
            (architecture.Length == 0 || x.Architecture == architecture)).ToArray();
        return recipes.Length == 1 ? recipes[0].Elevation : "";
    }

    private static bool SupportsNativeElevation(WinGetInstallerMetadata metadata, WinGetScopeBinding binding,
        IReadOnlyList<WinGetInstallerVariant> candidates)
    {
        if (!ValidBinding(binding) || binding.Scope != "machine" || binding.Architecture.Length == 0 ||
            binding.ElevationRequirement != "elevationRequired" ||
            binding.InstallerType is not ("exe" or "inno" or "nullsoft" or "burn")) return false;
        // Count before OS filtering too: omitted selection criteria cannot choose another recipe.
        var recipes = metadata.DispatchRecipes(binding.Scope, binding.InstallerType, binding.Architecture);
        if (recipes.Length != 1 || candidates.Count != 1 || recipes[0] != candidates[0]) return false;
        var recipe = recipes[0];
        return (!recipe.HasDependencies || WinGetDependencyPolicy.Matches(metadata, binding)) &&
            recipe.Elevation == "elevationRequired" && recipe.RestrictedCapabilities.Count == 0 &&
            recipe.NestedType.Length == 0 && recipe.ProductCode.Length > 0 &&
            recipe.ProductCode.Equals(binding.LocalId.Split('\\', 4).Last(), StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(recipe.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            Regex.IsMatch(recipe.Hash, "^[A-Fa-f0-9]{64}$");
    }

    private static bool KnownArchitecture(string value) => value is "x64" or "x86" or "arm64" or "arm" or "neutral";
    private static IEnumerable<WinGetInstallerVariant> PlatformCandidates(WinGetInstallerMetadata metadata,
        string architecture, Version? windowsVersion)
    {
        var os = windowsVersion ?? Environment.OSVersion.Version;
        return metadata.Installers.Where(x =>
            (!KnownArchitecture(x.Architecture) || WinGetInstallerMetadata.Compatible(x.Architecture, architecture)) &&
            !x.UnsupportedOSArchitectures.Contains(architecture, StringComparer.OrdinalIgnoreCase) &&
            (!TryWindowsVersion(x.MinimumOSVersion, out var minimum) || os >= minimum));
    }

    private static bool InstalledTechnologyMatches(string installed, string candidate) => installed switch
    {
        "" => true,
        "msi" or "wix" => candidate is "msi" or "wix",
        "exe" => candidate is "exe" or "inno" or "nullsoft" or "burn",
        "inno" or "nullsoft" or "burn" => installed == candidate,
        _ => false
    };

    private static bool MatchesRegisteredCategory(WinGetScopeBinding binding, string observed) =>
        InstalledTechnologyMatches(observed, binding.InstallerType) &&
        (binding.RegisteredInstallerCategory == observed ||
         binding.RegisteredInstallerCategory is null && (observed.Length == 0 || binding.ClientVersion != ReviewedClient130310));
    private static bool TryWindowsVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var parts = value.Split('.');
        if (parts.Length is < 1 or > 4 || parts.Any(x => !ushort.TryParse(x, out _))) return false;
        var numbers = parts.Select(int.Parse).Concat(Enumerable.Repeat(0, 4 - parts.Length)).ToArray();
        version = new(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    internal Task<UpdateVerificationResult> VerifyAsync(PlanItem item, CancellationToken token = default) =>
        RunnerReadOnlyWait.RunAsync(ct => VerifyWithRetriesAsync(item, ct), token,
            TimeSpan.FromSeconds(75), "winget-verification");

    private async Task<UpdateVerificationResult> VerifyWithRetriesAsync(PlanItem item, CancellationToken token)
    {
        UpdateVerificationResult latest = Unavailable();
        var diagnostics = new List<string>();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            latest = await VerifyCoreAsync(item, token).ConfigureAwait(false);
            diagnostics.Add($"Scoped attempt {attempt}/3: {latest.Status}; {latest.Message}; {latest.Diagnostics}");
            if (latest.Verified) break;
            if (attempt < 3 && waitBeforeRetry is not null)
                await waitBeforeRetry(TimeSpan.FromSeconds(attempt * 3), token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        latest.Diagnostics = string.Join(Environment.NewLine, diagnostics);
        return latest;
    }

    private async Task<UpdateVerificationResult> VerifyCoreAsync(PlanItem item, CancellationToken token)
    {
        if (item.WinGetBinding is null && item.WinGetVerification is not null)
        {
            try { return await VerifyUnboundAsync(item, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new() { Status = UpdateVerificationStatuses.Unavailable,
                    Message = LocalizationService.Text("Verifica automatica temporaneamente non disponibile.", "Automatic verification temporarily unavailable."),
                    Diagnostics = ex.ToString() };
            }
        }
        var binding = item.WinGetBinding;
        if (binding is null || !IsOfficialSource(item.Source) || !ValidBinding(binding) || readSid() != binding.AccountSid ||
            !string.IsNullOrEmpty(item.WinGetClientVersion) && item.WinGetClientVersion != binding.ClientVersion ||
            !await ClientMatches(binding.ClientVersion, token).ConfigureAwait(false))
            return Unavailable();
        if (binding.PythonBurnTransition is not null)
            return await VerifyPythonAsync(item, token).ConfigureAwait(false);
        var identity = await ReadIdentity(item.Id, item.Source, binding.Scope, token).ConfigureAwait(false);
        if (identity is null || identity.LocalId != binding.LocalId || identity.Scope != binding.Scope ||
            !MatchesRegisteredCategory(binding, identity.InstallerType) ||
            (binding.InstalledType.Length > 0 && identity.InstallerType != binding.InstalledType) ||
            (binding.Architecture.Length > 0 && identity.Architecture != binding.Architecture) || readSid() != binding.AccountSid) return Unavailable();
        // Some applications include the version in their DisplayName; LocalId is the
        // continuity evidence. A changed ARP key cannot be reported as this copy.
        var manifest = await readManifest(item.Id, item.AvailableVersion, token).ConfigureAwait(false);
        if (manifest is null || Digest(manifest) != binding.ManifestSha256) return Unavailable();
        var metadata = WinGetInstallerMetadata.Parse(manifest);
        if (metadata is null || metadata.Id != item.Id || metadata.Version != item.AvailableVersion) return Unavailable();
        var recipes = metadata.Installers.Where(x => x.Scope == (binding.CopyEvidence?.UnspecifiedScope == true ? "" : binding.Scope) && x.Type == binding.InstallerType &&
            (binding.Architecture.Length == 0 || x.Architecture == binding.Architecture)).ToArray();
        if (recipes.Length == 0) return Unavailable();
        var localKey = binding.LocalId.Split('\\', 4).Last();
        if (recipes.Any(x => x.MappingProductCode.Length > 0
            ? !x.MappingProductCode.Equals(localKey, StringComparison.OrdinalIgnoreCase)
            : x.HasVersionMapping && !x.ProductCode.Equals(localKey, StringComparison.OrdinalIgnoreCase))) return Unavailable();
        var displayVersions = recipes.Select(x => x.HasVersionMapping && x.DisplayVersion.Length > 0
            ? x.DisplayVersion : item.AvailableVersion).Distinct(StringComparer.Ordinal).ToArray();
        if (displayVersions.Length != 1 || readSid() != binding.AccountSid) return Unavailable();
        // WinGet can normalize its Version to PackageVersion. Never interpret
        // that field as raw DisplayVersion (Python is a concrete counterexample).
        // ARP mappings require a separate read of the exact bound registry key.
        var evidence = WinGetService.EvaluateVersionEvidence(identity.Version, item.AvailableVersion);
        var rawDisplayVersion = readDisplayVersion?.Invoke(binding.LocalId);
        var providerMatches = evidence.VersionRelation == SoftwareVersionRelation.Equal;
        var rawRelation = SoftwareVersionEvidence.Compare(rawDisplayVersion, displayVersions[0]);
        var mappedMatches = rawRelation == SoftwareVersionRelation.Equal;
        var copy = binding.CopyEvidence is null ? null : readCopyEvidence?.Invoke(binding.LocalId);
        if (binding.InstalledType.Length > 0 && (copy is null || !WinGetCopyEvidenceReader.Valid(copy) ||
            copy.MsiRegistration is not null || copy.DisplayVersion != rawDisplayVersion)) return Unavailable();
        if (binding.CopyEvidence?.MsiRegistration is { } approvedMsi &&
            (copy?.MsiRegistration is not { } currentMsi || !WinGetCopyEvidenceReader.Valid(copy) ||
             !MsiRegistrationReader.Matches(currentMsi, binding.LocalId, rawDisplayVersion ?? "", binding.AccountSid) ||
             currentMsi.Context != approvedMsi.Context || currentMsi.UserSid != approvedMsi.UserSid ||
             currentMsi.Language != approvedMsi.Language)) return Unavailable();
        var executableMatches = binding.CopyEvidence is { } before && copy is not null && WinGetCopyEvidenceReader.Valid(copy) &&
            WinGetCopyEvidenceReader.SameExecutable(before, copy) && copy.DisplayVersion == rawDisplayVersion &&
            recipes.All(x => x.DisplayVersion.Length == 0 && x.ProductCode.Equals(localKey, StringComparison.OrdinalIgnoreCase)) &&
            SoftwareVersionEvidence.Compare(copy.ProductVersion, item.AvailableVersion) == SoftwareVersionRelation.Equal;
        evidence.VersionRelation = rawRelation;
        evidence.Verified = mappedMatches || executableMatches;
        if (executableMatches) evidence.VersionRelation = SoftwareVersionRelation.Equal;
        evidence.IsDefinitive = evidence.Verified;
        evidence.Status = evidence.Verified ? UpdateVerificationStatuses.Verified : UpdateVerificationStatuses.Unavailable;
        evidence.Message = evidence.Verified
            ? LocalizationService.Text("Target verificato sulla stessa copia WinGet.", "Target verified on the same WinGet installation.")
            : LocalizationService.Text(
                "Verifica inconcludente: le versioni della stessa copia non confermano il target nel rispettivo dominio.",
                "Verification is inconclusive: the same installation's versions do not confirm the target in their respective domains.");
        evidence.Diagnostics += $"; rawDisplayVersion={rawDisplayVersion ?? "unavailable"}; mappedTarget={displayVersions[0]}; " +
            $"providerMatches={providerMatches}; mappedMatches={mappedMatches}; executableMatches={executableMatches}; " +
            $"PE={copy?.ExecutablePath}; PEProductVersion={copy?.ProductVersion}; LocalId={identity.LocalId}";
        if (binding.CopyEvidence is not null && (copy != readCopyEvidence?.Invoke(binding.LocalId) ||
            await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false) != identity)) return Unavailable();
        if (await ReadIdentity(item.Id, item.Source, binding.Scope, token).ConfigureAwait(false) != identity ||
            readSid() != binding.AccountSid || !await ClientMatches(binding.ClientVersion, token).ConfigureAwait(false)) return Unavailable();

        return evidence;
    }

    internal (string Type, WinGetUpgradeSafety Safety)? Select(string manifest, string id, string version,
        string scope, string installedArchitecture, string localId, bool unspecifiedScope = false)
    {
        var parsed = WinGetInstallerMetadata.Parse(manifest);
        if (parsed is null || parsed.Id != id || parsed.Version != version ||
            (unspecifiedScope ? parsed.Installers.Any(x => x.Scope.Length != 0) : parsed.Installers.Any(x => x.Scope is not ("user" or "machine")))) return null;
        var applicable = parsed.Installers.Where(x => x.Scope == (unspecifiedScope ? "" : scope) &&
            (installedArchitecture.Length > 0 ? x.Architecture == installedArchitecture :
                WinGetInstallerMetadata.Compatible(x.Architecture, architecture))).ToArray();
        if (applicable.Length == 0 || applicable.Select(x => x.Type).Distinct(StringComparer.Ordinal).Count() != 1 ||
            applicable.Any(x => x.Type is not ("exe" or "inno" or "nullsoft" or "burn" or "msi" or "wix") ||
                !Uri.TryCreate(x.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !Regex.IsMatch(x.Hash, "^[A-Fa-f0-9]{64}$"))) return null;
        var key = localId.Split('\\', 4).Last();
        foreach (var candidate in applicable)
        {
            if (candidate.ProductCode.Length > 0 && !candidate.ProductCode.Equals(key, StringComparison.OrdinalIgnoreCase)) return null;
            if (unspecifiedScope && !candidate.ProductCode.Equals(key, StringComparison.OrdinalIgnoreCase)) return null;
            // Merely relabelling an identical recipe does not demonstrate a scope distinction.
            if (parsed.Installers.Any(other => other.Scope != candidate.Scope && other.Url == candidate.Url &&
                other.Hash == candidate.Hash && other.Custom == candidate.Custom)) return null;
        }
        var safety = WinGetManifestSafetyService.ClassifyUpgradeBehavior(applicable.Select(x => x.Behavior).ToArray());
        return safety == WinGetUpgradeSafety.UpgradeUnsupported ? null : (applicable[0].Type, safety);
    }

    internal static string? ClientCompatibilityMessage(string version)
    {
        if (IsReviewedClient(version)) return null;
        return WinGetService.IsClientVersion(version)
            ? LocalizationService.Text(
                $"WinGet {version} non è validato per gli aggiornamenti automatici. Versioni validate: {ReviewedClient}, {ReviewedClient130}, {ReviewedClient130310}.",
                $"WinGet {version} is not validated for automatic upgrades. Validated versions: {ReviewedClient}, {ReviewedClient130}, {ReviewedClient130310}.")
            : LocalizationService.Text(
                "La versione di WinGet non è verificabile. Aggiornamento automatico bloccato.",
                "The WinGet version could not be verified. Automatic upgrade blocked.");
    }

    private async Task<string?> ReadClientVersionAsync(CancellationToken token)
    {
        var result = await execute(["--version"], token, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var observed = result.StandardOutput.Trim();
        return result.Success && IsReviewedClient(observed) ? observed : null;
    }

    internal async Task<ProcessResult?> CheckClientAsync(string? approved, CancellationToken token = default)
    {
        if (!IsReviewedClient(approved)) return new(1, "", LocalizationService.Text(
            "Il piano non contiene una versione WinGet validata e congelata. Ripeti la scansione e il consenso.",
            "The plan does not contain a validated, frozen WinGet version. Scan again and renew consent."));
        var result = await execute(["--version"], token, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!result.Success) return Failure();
        var observed = result.StandardOutput.Trim();
        if (ClientCompatibilityMessage(observed) is { } rejection) return new(1, "", rejection);
        return observed == approved ? null : new(1, "", LocalizationService.Text(
            $"WinGet è cambiato da {approved} a {observed} dopo la preparazione del piano. Ripeti la scansione e il consenso.",
            $"WinGet changed from {approved} to {observed} after the plan was prepared. Scan again and renew consent."));
    }

    private async Task<bool> ClientMatches(string approved, CancellationToken token) =>
        await CheckClientAsync(approved, token).ConfigureAwait(false) is null;

    private async Task<InstalledIdentity?> ReadIdentity(string id, string source, string scope, CancellationToken token,
        bool requireDependencyRegistration = false)
    {
        var arguments = new List<string> { "list", "--id", id, "--exact", "--source", source };
        if (scope.Length > 0) arguments.AddRange(["--scope", scope]);
        arguments.AddRange(["--details", "--disable-interactivity", "--nowarn"]);
        var result = await execute(arguments, token, TimeSpan.FromSeconds(45)).ConfigureAwait(false);
        return result.Success ? ParseIdentity(result.StandardOutput, id, requireDependencyRegistration ? source : null) : null;
    }

    internal static InstalledIdentity? ParseIdentity(string output, string expectedId, string? dependencySource = null)
    {
        output = Regex.Replace(output, "\\x1B(?:[@-Z\\\\-_]|\\[[0-?]*[ -/]*[@-~])", "").Replace("\r", "", StringComparison.Ordinal);
        var headers = Regex.Matches(output, @"(?m)^(\S[^\n]*) \[([^\]\n]+)\]$");
        if (headers.Count != 1 || headers[0].Groups[2].Value != expectedId) return null;
        string? Field(string names, bool optional = false)
        {
            var found = Regex.Matches(output, "(?im)^(?:" + names + @"):[ \t]*([^\n]*)$");
            return found.Count == 1 ? found[0].Groups[1].Value.Trim() : found.Count == 0 && optional ? "" : null;
        }
        var version = Field("Version|Versione");
        var local = Field("Local Identifier|Identificatore locale");
        var scope = Field("Installed Scope|Ambito installazione")?.ToLowerInvariant();
        var arch = Field("Installed Architecture|Architettura installata", true)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(version) || local is null || scope is not ("user" or "machine") ||
            arch is null || arch is not ("" or "x64" or "x86" or "arm64" or "arm" or "neutral") || !ValidLocalId(local, scope)) return null;
        var type = Field("Installer Type|Installer Category|Categoria programma di installazione", true)?.ToLowerInvariant();
        if (type is null) return null;
        string? publisher = null;
        if (dependencySource is not null)
        {
            publisher = Field("Publisher|Editore");
            if (string.IsNullOrWhiteSpace(publisher) || publisher.Length > 256 || publisher.Any(char.IsControl) ||
                !string.Equals(Field("Origin Source|Source|Origine"), dependencySource, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Field("Product Code|Codice prodotto"), local.Split('\\').Last(), StringComparison.OrdinalIgnoreCase))
                return null;
        }
        return new(headers[0].Groups[1].Value, version, local, scope, arch, type, publisher);
    }

    internal static bool ValidBinding(WinGetScopeBinding b) => b.Scope is "user" or "machine" &&
        ValidLocalId(b.LocalId, b.Scope) && ValidSid(b.AccountSid) && IsReviewedClient(b.ClientVersion) &&
        Regex.IsMatch(b.ManifestSha256 ?? "", "^[A-F0-9]{64}$") &&
        b.InstallerType is "exe" or "inno" or "nullsoft" or "burn" or "msi" or "wix" &&
        (b.RegisteredInstallerCategory is null || b.RegisteredInstallerCategory.Length > 0 &&
            InstalledTechnologyMatches(b.RegisteredInstallerCategory, b.InstallerType) &&
            (b.InstalledType == "" || b.InstalledType == b.RegisteredInstallerCategory)) &&
        (b.Architecture is "" or "x64" or "x86" or "arm64" or "arm" || b.Architecture == "neutral" && b.CopyEvidence is not null) &&
        b.Safety is "Safe" or "Unknown" or "RemovesPreviousVersion" &&
        b.ElevationRequirement is "" or "elevationRequired" or "elevatesSelf" or "elevationProhibited" &&
        (b.CopyEvidence is null || WinGetCopyEvidenceReader.Valid(b.CopyEvidence)) &&
        (b.CopyEvidence?.MsiRegistration is not { } msi || MsiRegistrationReader.Matches(msi, b.LocalId, b.CopyEvidence.DisplayVersion, b.AccountSid)) &&
        ((b.InstallerType is "msi" or "wix") == (b.CopyEvidence?.MsiRegistration is not null)) &&
        (b.InstalledType == "" || b.InstalledType == "exe" && b.InstallerType is "nullsoft" or "inno" &&
            b.Architecture is "x64" or "x86" or "arm64" && b.Safety == "Safe" && b.PythonBurnTransition is null &&
            b.CopyEvidence is { UnspecifiedScope: false, MsiRegistration: null } && b.DependencySnapshot == "") &&
        b.DependencySnapshot is not null && (b.DependencySnapshot.Length == 0 ||
            b.Scope == "machine" && b.CopyEvidence is not null && b.PythonBurnTransition is null &&
            WinGetDependencyPolicy.ValidSnapshot(b)) &&
        (b.PythonBurnTransition is null || b.CopyEvidence is null && PythonBurnTransitionService.ValidBinding(b));

    private static bool CertainlyLowerTarget(string target, WinGetScopeBinding binding, WinGetInstallerMetadata metadata, WinGetCopyEvidence copy)
    {
        var recipes = metadata.Installers.Where(x => x.Scope == (copy.UnspecifiedScope ? "" : binding.Scope) &&
            x.Type == binding.InstallerType && (binding.Architecture.Length == 0 || x.Architecture == binding.Architecture)).ToArray();
        var key = binding.LocalId.Split('\\', 4).Last();
        var mapped = recipes.Where(x => x.DisplayVersion.Length > 0 &&
            (x.MappingProductCode.Length > 0 ? x.MappingProductCode : x.ProductCode).Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.DisplayVersion).Distinct(StringComparer.Ordinal).ToArray();
        if (mapped.Length == 1 && SoftwareVersionEvidence.Compare(copy.DisplayVersion, mapped[0]) == SoftwareVersionRelation.Greater) return true;
        // ProductVersion can belong to another publisher version domain. Its
        // ordering against PackageVersion must never reject an upgrade.
        return false;
    }

    internal static bool PreparedCopyIsCurrent(WinGetScopeBinding binding, string manifest, string target)
    {
        if (binding.CopyEvidence is not { } copy || !WinGetCopyEvidenceReader.Valid(copy) || Digest(manifest) != binding.ManifestSha256)
            return false;
        var metadata = WinGetInstallerMetadata.Parse(manifest);
        var recipes = metadata?.Installers.Where(x => x.Scope == (copy.UnspecifiedScope ? "" : binding.Scope) &&
            x.Type == binding.InstallerType && (binding.Architecture.Length == 0 || x.Architecture == binding.Architecture)).ToArray();
        if (recipes is null || recipes.Length == 0) return false;
        var key = binding.LocalId.Split('\\', 4).Last();
        if (recipes.Any(x => !(x.MappingProductCode.Length > 0 ? x.MappingProductCode : x.ProductCode).Equals(key, StringComparison.OrdinalIgnoreCase))) return false;
        var targets = recipes.Select(x => x.DisplayVersion.Length > 0 ? x.DisplayVersion : target).Distinct(StringComparer.Ordinal).ToArray();
        return targets.Length == 1 && SoftwareVersionEvidence.Compare(copy.DisplayVersion, targets[0]) == SoftwareVersionRelation.Equal ||
            copy.ExecutablePath.Length > 0 && recipes.All(x => x.DisplayVersion.Length == 0) &&
            SoftwareVersionEvidence.Compare(copy.ProductVersion, target) == SoftwareVersionRelation.Equal;
    }

    private async Task<WinGetScopeBinding?> PreparePythonAsync(UpdateItem item, string manifest, CancellationToken token)
    {
        var sid = readSid();
        if (!ValidSid(sid) || readPythonInventory is null) return null;
        var clientVersion = await ReadClientVersionAsync(token).ConfigureAwait(false);
        if (clientVersion is null || !string.IsNullOrEmpty(item.WinGetClientVersion) && item.WinGetClientVersion != clientVersion) return null;
        var state = readPythonInventory();
        var identity = await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false);
        if (identity is null || !InstalledTechnologyMatches(identity.InstallerType, "burn") ||
            await ReadIdentity(item.Id, item.Source, "user", token).ConfigureAwait(false) != identity) return null;
        var oldManifest = await readManifest(item.Id, item.InstalledVersion, token).ConfigureAwait(false);
        if (oldManifest is null) return null;
        var binding = PythonBurnTransitionService.Prepare(item, oldManifest, manifest, state, identity, sid,
            architecture, windowsVersion ?? Environment.OSVersion.Version, (isAdministrator ?? CurrentIsAdministrator)(), clientVersion);
        if (binding is null || readSid() != sid) return null;
        // Two independent reads must agree before a plan can bind the copy. This
        // catches registry/file/account drift while WinGet queries are in flight.
        var second = PythonBurnTransitionService.Prepare(item, oldManifest, manifest, readPythonInventory(), identity, sid,
            architecture, windowsVersion ?? Environment.OSVersion.Version, (isAdministrator ?? CurrentIsAdministrator)(), clientVersion);
        if (binding != second || readSid() != sid || !await ClientMatches(clientVersion, token).ConfigureAwait(false)) return null;
        return binding with { RegisteredInstallerCategory = identity.InstallerType.Length == 0 ? null : identity.InstallerType };
    }

    private async Task<UpdateVerificationResult> VerifyPythonAsync(PlanItem item, CancellationToken token)
    {
        if (readPythonInventory is null || item.WinGetBinding is not { } binding) return Unavailable();
        var oldManifest = await readManifest(item.Id, item.InstalledVersion, token).ConfigureAwait(false);
        var targetManifest = await readManifest(item.Id, item.AvailableVersion, token).ConfigureAwait(false);
        if (oldManifest is null || targetManifest is null) return Unavailable();
        var firstEvidence = readPythonInventory();
        var identity = await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false);
        if (identity is null || !MatchesRegisteredCategory(binding, identity.InstallerType) ||
            await ReadIdentity(item.Id, item.Source, "user", token).ConfigureAwait(false) != identity ||
            readSid() != binding.AccountSid || !PythonBurnTransitionService.Verify(item, oldManifest, targetManifest,
                firstEvidence, identity, binding.AccountSid)) return Unavailable();
        if (await ReadIdentity(item.Id, item.Source, "", token).ConfigureAwait(false) != identity ||
            readSid() != binding.AccountSid || !PythonBurnTransitionService.SameEvidence(firstEvidence, readPythonInventory()) ||
            !await ClientMatches(binding.ClientVersion, token).ConfigureAwait(false))
            return Unavailable();
        return new() { Verified = true, IsDefinitive = true, Status = UpdateVerificationStatuses.Verified,
            VersionRelation = SoftwareVersionRelation.Equal,
            Message = LocalizationService.Text("Upgrade del bundle Python verificato sulla copia e sul percorso approvati.",
                "Python bundle upgrade verified on the approved installation and path."),
            Diagnostics = $"Python Burn old={binding.LocalId}; target={identity.LocalId}; " +
                $"path={binding.PythonBurnTransition!.InstallPath}; raw/PE versions and features match; old bundle absent." };
    }

    private static bool ValidSid(string? sid) => sid is not null && Regex.IsMatch(sid, @"^S-1-\d+(?:-\d+)+$");
    private static bool ValidLocalId(string? local, string scope) => local is not null && local.Length <= 1024 &&
        !local.Any(char.IsControl) && Regex.IsMatch(local, @"^ARP\\" + (scope == "user" ? "User" : "Machine") + @"\\(?:X64|X86|Arm64|Arm)\\[^\\]+$");
    internal static string Digest(string manifest) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
    internal static bool IsOfficialSource(string? source) => string.Equals(source, "winget", StringComparison.OrdinalIgnoreCase);
    private static ProcessResult Failure() => new(1, "", LocalizationService.Text(
        "Il target WinGet, l'ambito o i metadati sono cambiati/non verificabili. È necessaria una nuova scansione e un nuovo consenso.",
        "The WinGet target, scope or metadata changed or could not be verified. A new scan and consent are required."));
    private static UpdateVerificationResult Unavailable() => new() { Status = UpdateVerificationStatuses.Unavailable,
        Message = LocalizationService.Text("La stessa copia WinGet non è verificabile.", "The same WinGet installation cannot be verified.") };
    internal sealed record InstalledIdentity(string Name, string Version, string LocalId, string Scope, string Architecture,
        string InstallerType = "", string? Publisher = null);

    // A mixed catalog is not itself ambiguous when one exact existing EXE copy
    // maps to one traditional recipe. Never infer scope, architecture, a major MSI
    // transition or an installer switch. WinGet receives its existing type/scope
    // filters; all recipes that those filters could select must be the same one.
    private static WinGetInstallerVariant? ExactCopyRecipe(WinGetInstallerMetadata? metadata, string id,
        string installed, string target, InstalledIdentity identity, WinGetCopyEvidence? copy)
    {
        if (metadata is null || metadata.Id != id || metadata.Version != target ||
            identity.InstallerType != "exe" || identity.Architecture is not ("x64" or "x86" or "arm64") ||
            copy is null || !WinGetCopyEvidenceReader.Valid(copy) || copy.MsiRegistration is not null || copy.UnspecifiedScope ||
            SoftwareVersionEvidence.Compare(copy.DisplayVersion, installed) != SoftwareVersionRelation.Equal ||
            SoftwareVersionEvidence.Compare(target, copy.DisplayVersion) != SoftwareVersionRelation.Greater ||
            metadata.Installers.Any(x => x.Scope is not ("user" or "machine") &&
                !(x.Scope == "" && x.Type == "zip" && x.NestedType == "portable"))) return null;
        var key = identity.LocalId.Split('\\', 4).Last();
        var exact = metadata.Installers.Where(x => x.Scope == identity.Scope && x.Architecture == identity.Architecture &&
            x.Type is "nullsoft" or "inno" && x.ProductCode.Equals(key, StringComparison.OrdinalIgnoreCase) &&
            (x.MappingProductCode.Length == 0 || x.MappingProductCode.Equals(key, StringComparison.OrdinalIgnoreCase)) &&
            (x.DisplayVersion.Length == 0 || SoftwareVersionEvidence.Compare(x.DisplayVersion, target) == SoftwareVersionRelation.Equal)).ToArray();
        if (exact.Length != 1) return null;
        var recipe = exact[0];
        if (recipe.HasDependencies || recipe.Behavior != "install" ||
            recipe.Elevation is not ("" or "elevatesSelf" or "elevationProhibited") ||
            !Uri.TryCreate(recipe.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !Regex.IsMatch(recipe.Hash, "^[A-Fa-f0-9]{64}$") ||
            metadata.DispatchRecipes(identity.Scope, recipe.Type, identity.Architecture) is not [var only] || only != recipe ||
            metadata.Installers.Any(x => x.Scope != recipe.Scope && x.Url == recipe.Url && x.Hash == recipe.Hash && x.Custom == recipe.Custom)) return null;
        return recipe;
    }
}

internal enum WinGetRequirementState { Supported, InstallerSelfElevation, NativeInstallerElevation, ManualRequired, Unknown }
internal sealed record WinGetRequirementAssessment(WinGetRequirementState State, bool CanContinue, string Message);
