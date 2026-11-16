using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal enum WinGetUpgradeSafety
{
    Safe,
    RemovesPreviousVersion,
    UpgradeUnsupported,
    Unknown
}

internal sealed class WinGetManifestSafetyService
{
    private readonly Func<string, string, CancellationToken, Task<ManifestInspection>> _inspect;
    private readonly Func<WinGetCommand, WinGetScopeGuard> _createGuard;
    private readonly Func<string, string, string> _readScope;
    private readonly string _architecture;
    private readonly Func<bool> _isAdministrator;

    internal WinGetManifestSafetyService(
        Func<string, string, CancellationToken, Task<ManifestInspection>>? inspect = null,
        Func<WinGetCommand, WinGetScopeGuard>? createGuard = null,
        Func<string, string, string>? readScope = null,
        string? architecture = null, Func<bool>? isAdministrator = null)
    {
        _inspect = inspect ?? InspectAsync;
        _createGuard = createGuard ?? WinGetScopeGuard.Native;
        _readScope = readScope ?? WinGetInstalledScopeService.Detect;
        _architecture = architecture ?? GetSystemArchitecture();
        _isAdministrator = isAdministrator ?? WinGetScopeGuard.CurrentIsAdministrator;
    }
    private static readonly HttpClient Client = CreateClient();
    private static readonly Regex UpgradeBehaviorLine = new(
        "^\\s*UpgradeBehavior\\s*:\\s*([^\\s#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex InstallerUrlLine = new(
        "^\\s*InstallerUrl\\s*:\\s*['\\\"]?([^'\\\"\\r\\n#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex ScopeLine = new(
        "^\\s*Scope\\s*:\\s*['\\\"]?([^'\\\"\\s#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    public async Task ApplyAsync(IReadOnlyList<UpdateItem> items, CancellationToken cancellationToken, WinGetCommand execute)
    {
        using var concurrency = new SemaphoreSlim(6);
        var checks = items
            .Where(x => x.Source.Equals("winget", StringComparison.OrdinalIgnoreCase))
            .Select(async item =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    var inspection = await _inspect(item.Id, item.AvailableVersion, cancellationToken);
                    if (inspection.DownloadSizeBytes > 0)
                    {
                        item.DownloadSizeBytes = inspection.DownloadSizeBytes;
                        item.Size = FormatBytes(inspection.DownloadSizeBytes);
                    }

                    if (item.CanInstall && inspection.Manifest is { } manifest)
                    {
                        var guard = _createGuard(execute);
                        try
                        {
                            var binding = await guard.PrepareAsync(item, manifest, cancellationToken);
                            if (binding is null)
                                item.WinGetVerification = await guard.PrepareVerificationAsync(item, manifest, cancellationToken);
                            if (binding is not null)
                            {
                                item.WinGetBinding = binding;
                                ApplySafetyClassification(item, Enum.Parse<WinGetUpgradeSafety>(binding.Safety));
                                ApplyInstallationRequirements(item, WinGetScopeGuard.AssessRequirements(
                                    WinGetInstallerMetadata.Parse(manifest), item.Id, item.AvailableVersion,
                                    _architecture, binding.Scope, binding,
                                    isAdministrator: _isAdministrator()));
                                if (WinGetScopeGuard.PreparedCopyIsCurrent(binding, manifest, item.AvailableVersion))
                                {
                                    item.CanInstall = false;
                                    item.IsSelected = false;
                                    item.Status = LocalizationService.Text("Già aggiornato", "Already up to date");
                                    item.ResultDetails = LocalizationService.Text(
                                        "Il target è già verificato sulla stessa copia locale. Nessun installer necessario.",
                                        "The target is already verified on the same local installation. No installer is needed.");
                                }
                                return;
                            }
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
                        { LogService.Write("Identità WinGet non verificabile; recupero scope escluso.", ex); }
                    }
                    ApplySafetyClassification(item, inspection.Safety);
                    ApplyScopeCompatibility(
                        item,
                        _readScope(item.Name, item.InstalledVersion),
                        inspection.Scope);
                    ApplyInstallationRequirements(item, WinGetScopeGuard.AssessRequirements(
                        inspection.Manifest is null ? null : WinGetInstallerMetadata.Parse(inspection.Manifest),
                        item.Id, item.AvailableVersion, _architecture,
                        _readScope(item.Name, item.InstalledVersion),
                        isAdministrator: _isAdministrator()));
                }
                finally
                {
                    concurrency.Release();
                }
            });
        await Task.WhenAll(checks);
    }

    internal static void ApplySafetyClassification(UpdateItem item, WinGetUpgradeSafety safety)
    {
        if (!item.CanInstall) return;
        switch (safety)
        {
            case WinGetUpgradeSafety.Safe:
                return;
            case WinGetUpgradeSafety.RemovesPreviousVersion:
                item.RequiresRiskConfirmation = true;
                item.IsSelected = false;
                item.Status = LocalizationService.Text("Conferma richiesta", "Confirmation required");
                item.ResultDetails = LocalizationService.Translate(LocalizationService.InstallerRemovalRiskMessage);
                return;
            case WinGetUpgradeSafety.UpgradeUnsupported:
                item.IsSelected = false;
                item.CanInstall = false;
                item.Status = LocalizationService.Text("Aggiornamento manuale", "Manual update");
                item.ResultDetails = LocalizationService.Text(
                    "Il manifest ufficiale non consente l'aggiornamento diretto di questo pacchetto. Usa la fonte ufficiale del programma.",
                    "The official manifest does not allow a direct upgrade of this package. Use the program's official source.");
                return;
            default:
                item.HasUnverifiedInstallerMetadata = true;
                item.IsSelected = false;
                item.ResultDetails = LocalizationService.Translate(LocalizationService.InstallerBehaviorUnknownMessage);
                return;
        }
    }

    internal static void ApplyInstallationRequirements(UpdateItem item, WinGetRequirementAssessment requirements)
    {
        // A requirements summary must never overwrite a stronger B2/risk rejection.
        if (!item.CanInstall) return;
        item.Diagnostics = string.Join(Environment.NewLine, new[] { item.Diagnostics,
            $"WinGet requirements: state={requirements.State}; canContinue={requirements.CanContinue}; " +
            $"id={item.Id}; version={item.AvailableVersion}; source={item.Source}; " +
            $"installerConstraint={item.WinGetBinding?.InstallerType ?? "unbound"}; " +
            $"scopeConstraint={item.WinGetBinding?.Scope ?? "unbound"}." }.Where(x => !string.IsNullOrWhiteSpace(x)));
        item.ResultDetails = string.Join(" ", new[] { item.ResultDetails, requirements.Message }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (requirements.State == WinGetRequirementState.Unknown)
        {
            item.HasUnverifiedInstallerMetadata = true;
            item.IsSelected = false;
        }
        if (!requirements.CanContinue)
        {
            item.CanInstall = false;
            item.IsSelected = false;
            item.Status = LocalizationService.Text("Aggiornamento manuale", "Manual update");
        }
    }

    private static async Task<ManifestInspection> InspectAsync(
        string packageId, string version, CancellationToken cancellationToken)
    {
        foreach (var uri in BuildManifestUris(packageId, version))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var response = await Client.GetAsync(uri, cancellationToken);
                    if (response.StatusCode == HttpStatusCode.NotFound) break;
                    if (!response.IsSuccessStatusCode)
                    {
                        if (attempt == 0) continue;
                        break;
                    }

                    var manifest = await response.Content.ReadAsStringAsync(cancellationToken);
                    var size = await ResolveDownloadSizeAsync(ParseInstallerUrls(manifest), cancellationToken);
                    return new(ParseUpgradeSafety(manifest), size, ParseInstallerScope(manifest), manifest);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt == 0) continue;
                }
                catch (HttpRequestException)
                {
                    if (attempt == 0) continue;
                }
            }
        }

        return new(WinGetUpgradeSafety.Unknown, 0, "", null);
    }

    internal static async Task<string?> ReadManifestAsync(string id, string version, CancellationToken token)
    {
        foreach (var uri in BuildManifestUris(id, version))
        {
            try
            {
                using var response = await Client.GetAsync(uri, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound) continue;
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }
            catch (HttpRequestException) { return null; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        }
        return null;
    }

    internal static string ParseInstallerScope(string manifest)
    {
        // Resolve common values with installer overrides before summarizing.
        // This still does not select an installer or authorize another scope.
        var parsed = WinGetInstallerMetadata.Parse(manifest);
        if (parsed is null) return "ambiguous";
        var scopes = parsed.Installers.Select(x => x.Scope).Distinct(StringComparer.Ordinal).ToArray();
        return scopes.Length == 1 && scopes[0] is "" or "user" or "machine" ? scopes[0] : "ambiguous";
    }

    internal static void ApplyScopeCompatibility(UpdateItem item, string installedScope, string availableScope)
    {
        // Keep the substantive rejection (for example UpgradeBehavior: deny).
        // An additional scope summary must never replace it with a promise of recovery.
        if (!item.CanInstall) return;
        if (installedScope == "ambiguous")
        {
            item.CanInstall = false;
            item.IsSelected = false;
            item.Status = LocalizationService.Text("Aggiornamento manuale", "Manual update");
            item.ResultDetails = LocalizationService.Text(
                "Installazione non univoca: sono presenti più copie con lo stesso nome nel registro. " +
                "Nome e versione non identificano quale copia verrà aggiornata. È necessaria una verifica manuale.",
                "Installation is ambiguous: multiple copies with the same name are present in the registry. " +
                "Name and version do not identify which copy will be upgraded. Manual verification is required.");
            return;
        }
        if (availableScope == "ambiguous")
        {
            item.CanInstall = false;
            item.IsSelected = false;
            item.Status = LocalizationService.Text("Aggiornamento manuale", "Manual update");
            item.ResultDetails = LocalizationService.Text(
                "Ambito non confermato: il manifest dichiara ambiti diversi o non riconosciuti. " +
                "La presenza di una variante compatibile non garantisce che venga utilizzata. " +
                "L'aggiornamento automatico resta bloccato finché non è possibile vincolare e verificare lo stesso ambito nell'esecuzione.",
                "Scope not confirmed: the manifest declares different or unrecognized scopes. " +
                "The presence of a compatible variant does not guarantee it will be used. " +
                "Automatic updating remains blocked until the same scope can be bound and verified during execution.");
            return;
        }
        if (string.IsNullOrWhiteSpace(installedScope) || string.IsNullOrWhiteSpace(availableScope) ||
            installedScope.Equals(availableScope, StringComparison.OrdinalIgnoreCase)) return;

        item.CanInstall = false;
        item.IsSelected = false;
        item.Status = LocalizationService.Text("Aggiornamento manuale", "Manual update");
        item.ResultDetails = LocalizationService.Text(
            $"Ambito non confermato: la copia rilevata è {ScopeLabel(installedScope)}, " +
            $"mentre una voce dei metadati riporta {ScopeLabel(availableScope)}. Il manifest può contenere più installer. " +
            "L'aggiornamento automatico resta bloccato: occorre verificare un installer della versione esatta per lo stesso ambito.",
            $"Scope not confirmed: the detected installation is {installedScope}, while one metadata entry declares " +
            $"{availableScope}. The manifest may contain multiple installers. Automatic updating remains blocked: " +
            "an installer for the exact version and the same scope must be verified.");
    }

    private static string ScopeLabel(string scope) => scope.Equals("machine", StringComparison.OrdinalIgnoreCase)
        ? "per l'intero computer"
        : scope.Equals("user", StringComparison.OrdinalIgnoreCase) ? "per il singolo utente" : scope;

    internal static IReadOnlyList<Uri> ParseInstallerUrls(string manifest) =>
        InstallerUrlLine.Matches(manifest)
            .Select(x => x.Groups[1].Value.Trim())
            .Select(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) ? uri : null)
            .Where(x => x is { Scheme: "https" })
            .Cast<Uri>()
            .DistinctBy(x => x.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

    private static async Task<long> ResolveDownloadSizeAsync(
        IReadOnlyList<Uri> installerUris, CancellationToken cancellationToken)
    {
        var lengths = await Task.WhenAll(installerUris.Select(uri =>
            ReadDownloadSizeAsync(uri, cancellationToken)));
        return lengths.DefaultIfEmpty(0).Max();
    }

    private static Task<long> ReadDownloadSizeAsync(Uri uri, CancellationToken cancellationToken) =>
        ReadDownloadSizeAsync(Client, uri, cancellationToken);

    internal static async Task<long> ReadDownloadSizeAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, uri);
            using var headResponse = await client.SendAsync(
                head, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            // Optional scan metadata must never request an installer payload.
            // A failed HEAD may describe an error page, not the installer.
            return headResponse.IsSuccessStatusCode
                ? Math.Max(headResponse.Content.Headers.ContentLength ?? 0, 0)
                : 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return 0; }
        catch (HttpRequestException) { return 0; }
    }

    internal static WinGetUpgradeSafety ParseUpgradeSafety(string manifest, string? systemArchitecture = null)
    {
        var parsed = WinGetInstallerMetadata.Parse(manifest);
        if (parsed is not null)
        {
            var arch = NormalizeArchitecture(systemArchitecture ?? GetSystemArchitecture());
            return ClassifyUpgradeBehavior(parsed.Installers.Where(x => WinGetInstallerMetadata.Compatible(x.Architecture, arch))
                .Select(x => x.Behavior).ToArray());
        }
        // Unsupported YAML cannot certify safety. Explicit adverse declarations
        // still remain conservative evidence; never turn them into a safe fallback.
        var adverse = ClassifyUpgradeBehavior(ReadUpgradeBehaviorValues(manifest));
        return adverse is WinGetUpgradeSafety.UpgradeUnsupported or WinGetUpgradeSafety.RemovesPreviousVersion
            ? adverse : WinGetUpgradeSafety.Unknown;
    }

    private static List<string> ReadUpgradeBehaviorValues(string manifestSection) =>
        UpgradeBehaviorLine.Matches(manifestSection)
            .Select(x => x.Groups[1].Value.Trim())
            .ToList();

    internal static WinGetUpgradeSafety ClassifyUpgradeBehavior(IReadOnlyCollection<string> values)
    {
        if (values.Any(x => x.Equals("deny", StringComparison.OrdinalIgnoreCase)))
            return WinGetUpgradeSafety.UpgradeUnsupported;
        if (values.Any(x => x.Equals("uninstallPrevious", StringComparison.OrdinalIgnoreCase)))
            return WinGetUpgradeSafety.RemovesPreviousVersion;
        if (values.Count > 0 && values.All(x => x.Equals("install", StringComparison.OrdinalIgnoreCase)))
            return WinGetUpgradeSafety.Safe;
        return WinGetUpgradeSafety.Unknown;
    }

    private static string GetSystemArchitecture() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        Architecture.X86 => "x86",
        _ => "x64"
    };

    private static string NormalizeArchitecture(string value) => value.Trim().ToLowerInvariant();

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    internal static IReadOnlyList<Uri> BuildManifestUris(string packageId, string version)
    {
        var idSegments = packageId.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);
        var packagePath = string.Join('/', idSegments);
        var first = char.ToLowerInvariant(packageId[0]);
        var escapedVersion = Uri.EscapeDataString(version);
        var root = $"https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/{first}/{packagePath}/{escapedVersion}";
        var escapedId = Uri.EscapeDataString(packageId);
        return
        [
            new Uri($"{root}/{escapedId}.installer.yaml"),
            new Uri($"{root}/{escapedId}.yaml")
        ];
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UpdateCenter/2.0.0");
        return client;
    }

    internal sealed record ManifestInspection(WinGetUpgradeSafety Safety, long DownloadSizeBytes, string Scope, string? Manifest);

}
