using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal sealed partial class WinGetScopeGuard
{
    // Capture a verifiable target without changing the legacy execution recipe.
    // A new ARP key is accepted only as an expected postcondition, never as
    // permission to migrate, uninstall or replace an otherwise blocked product.
    internal async Task<WinGetVerificationBinding?> PrepareVerificationAsync(UpdateItem item, string manifest, CancellationToken token)
    {
        if (item.WinGetBinding is not null || item.PackageOperation != PackageOperations.Upgrade ||
            !IsOfficialSource(item.Source) || !IsReviewedClient(item.WinGetClientVersion) ||
            readDisplayVersion is null || registrationExists is null) return null;
        var sid = readSid();
        var identity = await ReadVerificationIdentity(item.Id, item.Source, "", token).ConfigureAwait(false);
        if (!ValidSid(sid) || identity is null || identity.Name != item.Name || identity.Version != item.InstalledVersion ||
            identity.InstallerType != "exe" || identity.Architecture is not ("x64" or "x86" or "arm64") ||
            await ReadVerificationIdentity(item.Id, item.Source, identity.Scope, token).ConfigureAwait(false) != identity) return null;
        var metadata = WinGetInstallerMetadata.Parse(manifest);
        if (metadata is null || metadata.Id != item.Id || metadata.Version != item.AvailableVersion ||
            SoftwareVersionEvidence.Compare(item.AvailableVersion, item.InstalledVersion) != SoftwareVersionRelation.Greater) return null;
        var recipes = metadata.Installers.Where(x => x.Architecture == identity.Architecture &&
            (x.Scope.Length == 0 || x.Scope == identity.Scope)).ToArray();
        if (recipes is not [var recipe] || recipe.Type is not ("exe" or "burn") ||
            recipe.Behavior is not ("" or "install") || !Uri.TryCreate(recipe.Url, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            !Regex.IsMatch(recipe.Hash, "^[A-Fa-f0-9]{64}$")) return null;
        var code = recipe.MappingProductCode.Length > 0 ? recipe.MappingProductCode : recipe.ProductCode;
        if (!Guid.TryParse(code, out _)) return null;
        var targetLocal = identity.LocalId[..(identity.LocalId.LastIndexOf('\\') + 1)] + code;
        var raw = readDisplayVersion(identity.LocalId);
        var targetRaw = recipe.DisplayVersion.Length > 0 ? recipe.DisplayVersion : item.AvailableVersion;
        var evidence = new WinGetVerificationBinding(identity.LocalId, targetLocal, identity.Scope, identity.Architecture,
            sid, Digest(manifest), raw ?? "", targetRaw);
        if (!ValidVerificationBinding(evidence) ||
            registrationExists(identity.LocalId) != true ||
            (!SameLocalId(targetLocal, identity.LocalId) && registrationExists(targetLocal) != false) ||
            readDisplayVersion(identity.LocalId) != raw || readSid() != sid ||
            await ReadVerificationIdentity(item.Id, item.Source, "", token).ConfigureAwait(false) != identity ||
            !await ClientMatches(item.WinGetClientVersion, token).ConfigureAwait(false)) return null;
        return evidence;
    }

    private async Task<InstalledIdentity?> ReadVerificationIdentity(string id, string source, string scope, CancellationToken token)
    {
        var args = new List<string> { "list", "--id", id, "--exact", "--source", source, "--details", "--disable-interactivity", "--nowarn" };
        if (scope.Length > 0) args.AddRange(["--scope", scope]);
        var result = await execute(args, token, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var origins = Regex.Matches(result.StandardOutput, @"(?im)^(?:Source|Origine):[ \t]*([^\r\n]+)");
        return result.Success && origins.Count == 1 && origins[0].Groups[1].Value.Trim().Equals(source, StringComparison.OrdinalIgnoreCase)
            ? ParseIdentity(result.StandardOutput, id) : null;
    }

    private async Task<UpdateVerificationResult> VerifyUnboundAsync(PlanItem item, CancellationToken token)
    {
        UpdateVerificationResult Pending(string reason) => new() { Status = UpdateVerificationStatuses.Unavailable, Message = reason };
        var proof = item.WinGetVerification!;
        if (!ValidVerificationBinding(proof) || !IsOfficialSource(item.Source) || readSid() != proof.AccountSid ||
            await CheckClientAsync(item.WinGetClientVersion, token).ConfigureAwait(false) is not null)
            return Pending(LocalizationService.Text("Identità del client o dell'account non più coerente con il piano approvato.", "Client or account identity no longer matches the approved plan."));
        var identity = await ReadVerificationIdentity(item.Id, item.Source, "", token).ConfigureAwait(false);
        if (identity is null || !SameLocalId(identity.LocalId, proof.TargetLocalId) || identity.Scope != proof.Scope ||
            identity.Architecture != proof.Architecture || identity.InstallerType != "exe" ||
            await ReadVerificationIdentity(item.Id, item.Source, proof.Scope, token).ConfigureAwait(false) != identity)
            return Pending(LocalizationService.Text("La copia target non è ancora identificata univocamente per origine, architettura e ambito.", "The target installation is not yet uniquely identified by source, architecture and scope."));
        if (!SameLocalId(proof.OldLocalId, proof.TargetLocalId) && registrationExists?.Invoke(proof.OldLocalId) != false)
            return Pending(LocalizationService.Text("La scomparsa della precedente registrazione non è dimostrata; possibile copia affiancata.", "Removal of the previous registration is not proven; a parallel installation may exist."));
        var raw = readDisplayVersion?.Invoke(proof.TargetLocalId);
        if (SoftwareVersionEvidence.Compare(identity.Version, item.AvailableVersion) != SoftwareVersionRelation.Equal ||
            SoftwareVersionEvidence.Compare(raw, proof.TargetDisplayVersion) != SoftwareVersionRelation.Equal)
            return Pending(LocalizationService.Text("La registrazione del target non conferma ancora le versioni approvate, nei rispettivi domini.", "The target registration does not yet confirm the approved versions in their respective domains."));
        if (readSid() != proof.AccountSid || readDisplayVersion?.Invoke(proof.TargetLocalId) != raw ||
            (!SameLocalId(proof.OldLocalId, proof.TargetLocalId) && registrationExists?.Invoke(proof.OldLocalId) != false) ||
            await ReadVerificationIdentity(item.Id, item.Source, "", token).ConfigureAwait(false) != identity ||
            await CheckClientAsync(item.WinGetClientVersion, token).ConfigureAwait(false) is not null)
            return Pending(LocalizationService.Text("Le evidenze sono cambiate durante la verifica automatica.", "Evidence changed during automatic verification."));
        return new() { Verified = true, IsDefinitive = true, Status = UpdateVerificationStatuses.Verified,
            VersionRelation = SoftwareVersionRelation.Equal,
            Message = LocalizationService.Text("Target verificato tramite identità dettagliata WinGet e registrazione locale approvata.", "Target verified using detailed WinGet identity and the approved local registration."),
            Diagnostics = $"source={item.Source}; scope={proof.Scope}; architecture={proof.Architecture}; old={proof.OldLocalId}; target={proof.TargetLocalId}; provider={identity.Version}; raw={raw}; expectedRaw={proof.TargetDisplayVersion}; manifest={proof.ManifestSha256}" };
    }

    private static bool SameLocalId(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    internal static bool ValidVerificationBinding(WinGetVerificationBinding proof) =>
        proof.Scope is "user" or "machine" && proof.Architecture is "x64" or "x86" or "arm64" &&
        ValidSid(proof.AccountSid) && ValidLocalId(proof.OldLocalId, proof.Scope) && ValidLocalId(proof.TargetLocalId, proof.Scope) &&
        proof.OldLocalId[..proof.OldLocalId.LastIndexOf('\\')] == proof.TargetLocalId[..proof.TargetLocalId.LastIndexOf('\\')] &&
        Guid.TryParse(proof.TargetLocalId.Split('\\').Last(), out _) &&
        Regex.IsMatch(proof.ManifestSha256 ?? "", "^[A-F0-9]{64}$") &&
        SoftwareVersionEvidence.IsValidVersion(proof.OldDisplayVersion) && SoftwareVersionEvidence.IsValidVersion(proof.TargetDisplayVersion) &&
        SoftwareVersionEvidence.Compare(proof.TargetDisplayVersion, proof.OldDisplayVersion) == SoftwareVersionRelation.Greater;
}
