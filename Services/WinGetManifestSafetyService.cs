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
    private static readonly HttpClient Client = CreateClient();
    private static readonly Regex UpgradeBehaviorLine = new(
        "^\\s*UpgradeBehavior\\s*:\\s*([^\\s#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex InstallerUrlLine = new(
        "^\\s*InstallerUrl\\s*:\\s*['\\\"]?([^'\\\"\\r\\n#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex InstallerEntryStart = new(
        "^(?<indent>\\s*)-\\s+Architecture\\s*:\\s*['\\\"]?(?<architecture>[^'\\\"\\s#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public async Task ApplyAsync(IReadOnlyList<UpdateItem> items, CancellationToken cancellationToken)
    {
        using var concurrency = new SemaphoreSlim(6);
        var checks = items
            .Where(x => x.Source.Equals("winget", StringComparison.OrdinalIgnoreCase))
            .Select(async item =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    var inspection = await InspectAsync(item.Id, item.AvailableVersion, cancellationToken);
                    if (inspection.DownloadSizeBytes > 0)
                    {
                        item.DownloadSizeBytes = inspection.DownloadSizeBytes;
                        item.Size = FormatBytes(inspection.DownloadSizeBytes);
                    }

                    ApplySafetyClassification(item, inspection.Safety);
                }
                finally
                {
                    concurrency.Release();
                }
            });
        await Task.WhenAll(checks);
    }

    private static void ApplySafetyClassification(UpdateItem item, WinGetUpgradeSafety safety)
    {
        switch (safety)
        {
            case WinGetUpgradeSafety.Safe:
                return;
            case WinGetUpgradeSafety.RemovesPreviousVersion:
                item.RequiresRiskConfirmation = true;
                item.IsSelected = false;
                item.Status = LocalizationService.Text("Conferma richiesta", "Confirmation required");
                item.ResultDetails = LocalizationService.Text(
                    "L'installer compatibile con questo PC dichiara la rimozione della versione attuale prima di installare quella nuova. È richiesta una conferma aggiuntiva.",
                    "The installer compatible with this PC declares that it removes the current version before installing the new one. Additional confirmation is required.");
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
                item.ResultDetails = LocalizationService.Text(
                    "I metadati sul comportamento dell'installer non sono disponibili o non sono stati verificati. Non risulta dichiarata una rimozione preventiva.",
                    "Installer behavior metadata is unavailable or could not be verified. No prior removal is declared.");
                return;
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
                    return new(ParseUpgradeSafety(manifest), size);
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

        return new(WinGetUpgradeSafety.Unknown, 0);
    }

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

    private static async Task<long> ReadDownloadSizeAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, uri);
            using var headResponse = await Client.SendAsync(
                head, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var length = headResponse.Content.Headers.ContentLength ?? 0;
            if (length <= 0)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, uri);
                using var getResponse = await Client.SendAsync(
                    get, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                length = getResponse.Content.Headers.ContentLength ?? 0;
            }
            return Math.Max(length, 0);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return 0; }
        catch (HttpRequestException) { return 0; }
    }

    internal static WinGetUpgradeSafety ParseUpgradeSafety(string manifest, string? systemArchitecture = null)
    {
        var entries = InstallerEntryStart.Matches(manifest).Cast<Match>().ToList();
        var commonSection = entries.Count == 0 ? manifest : manifest[..entries[0].Index];
        var commonValues = ReadUpgradeBehaviorValues(commonSection);
        if (commonValues.Count > 0)
            return ClassifyUpgradeBehavior(commonValues);

        if (entries.Count == 0)
            return WinGetUpgradeSafety.Unknown;

        var architecture = NormalizeArchitecture(systemArchitecture ?? GetSystemArchitecture());
        var installerSections = entries.Select((entry, index) => new InstallerManifestSection(
            NormalizeArchitecture(entry.Groups["architecture"].Value),
            manifest.Substring(
                entry.Index,
                (index + 1 < entries.Count ? entries[index + 1].Index : manifest.Length) - entry.Index)))
            .ToList();

        var applicable = SelectApplicableSections(installerSections, architecture);
        var values = applicable.SelectMany(x => ReadUpgradeBehaviorValues(x.Content)).ToList();
        return ClassifyUpgradeBehavior(values);
    }

    private static IReadOnlyList<InstallerManifestSection> SelectApplicableSections(
        IReadOnlyList<InstallerManifestSection> sections, string architecture)
    {
        var exact = sections.Where(x => x.Architecture == architecture).ToList();
        if (exact.Count > 0) return exact;

        var neutral = sections.Where(x => x.Architecture == "neutral").ToList();
        if (neutral.Count > 0) return neutral;

        var compatibleArchitectures = architecture switch
        {
            "arm64" => new[] { "x64", "x86", "arm" },
            "x64" => new[] { "x86" },
            _ => Array.Empty<string>()
        };
        foreach (var compatibleArchitecture in compatibleArchitectures)
        {
            var compatible = sections.Where(x => x.Architecture == compatibleArchitecture).ToList();
            if (compatible.Count > 0) return compatible;
        }

        return [];
    }

    private static List<string> ReadUpgradeBehaviorValues(string manifestSection) =>
        UpgradeBehaviorLine.Matches(manifestSection)
            .Select(x => x.Groups[1].Value.Trim())
            .ToList();

    private static WinGetUpgradeSafety ClassifyUpgradeBehavior(IReadOnlyCollection<string> values)
    {
        if (values.Any(x => x.Equals("uninstallPrevious", StringComparison.OrdinalIgnoreCase)))
            return WinGetUpgradeSafety.RemovesPreviousVersion;
        if (values.Any(x => x.Equals("deny", StringComparison.OrdinalIgnoreCase)))
            return WinGetUpgradeSafety.UpgradeUnsupported;
        if (values.Any(x => x.Equals("install", StringComparison.OrdinalIgnoreCase)))
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UpdateCenter/1.0.8");
        return client;
    }

    private sealed record ManifestInspection(WinGetUpgradeSafety Safety, long DownloadSizeBytes);
    private sealed record InstallerManifestSection(string Architecture, string Content);
}
