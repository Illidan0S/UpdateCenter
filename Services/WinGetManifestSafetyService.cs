using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal enum WinGetUpgradeSafety
{
    Safe,
    RemovesPreviousVersion,
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
                    var safety = inspection.Safety;
                    if (safety == WinGetUpgradeSafety.Safe) return;

                    item.RequiresRiskConfirmation = true;
                    item.IsSelected = false;
                    item.Status = LocalizationService.Text("Conferma richiesta", "Confirmation required");
                    item.ResultDetails = safety == WinGetUpgradeSafety.RemovesPreviousVersion
                        ? LocalizationService.Text(
                            "Il pacchetto è configurato per rimuovere la versione attuale prima di installare quella nuova. È richiesta una conferma aggiuntiva prima di procedere.",
                            "The package is configured to remove the current version before installing the new one. Additional confirmation is required before proceeding.")
                        : LocalizationService.Text(
                            "Il comportamento dell'installer non è dichiarato in modo verificabile dalla fonte. È richiesta una conferma aggiuntiva prima di procedere.",
                            "The installer behavior is not declared by the source in a verifiable way. Additional confirmation is required before proceeding.");
                }
                finally
                {
                    concurrency.Release();
                }
            });
        await Task.WhenAll(checks);
    }

    private static async Task<ManifestInspection> InspectAsync(
        string packageId, string version, CancellationToken cancellationToken)
    {
        foreach (var uri in BuildManifestUris(packageId, version))
        {
            try
            {
                using var response = await Client.GetAsync(uri, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound) continue;
                if (!response.IsSuccessStatusCode) return new(WinGetUpgradeSafety.Unknown, 0);
                var manifest = await response.Content.ReadAsStringAsync(cancellationToken);
                var size = await ResolveDownloadSizeAsync(ParseInstallerUrls(manifest), cancellationToken);
                return new(ParseUpgradeSafety(manifest), size);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(WinGetUpgradeSafety.Unknown, 0);
            }
            catch (HttpRequestException)
            {
                return new(WinGetUpgradeSafety.Unknown, 0);
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

    internal static WinGetUpgradeSafety ParseUpgradeSafety(string manifest)
    {
        var values = UpgradeBehaviorLine.Matches(manifest)
            .Select(x => x.Groups[1].Value.Trim())
            .ToList();
        if (values.Any(x => x.Equals("uninstallPrevious", StringComparison.OrdinalIgnoreCase) ||
                            x.Equals("deny", StringComparison.OrdinalIgnoreCase)))
            return WinGetUpgradeSafety.RemovesPreviousVersion;
        return values.Any(x => x.Equals("install", StringComparison.OrdinalIgnoreCase))
            ? WinGetUpgradeSafety.Safe
            : WinGetUpgradeSafety.Unknown;
    }

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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UpdateCenter/1.0.6");
        return client;
    }

    private sealed record ManifestInspection(WinGetUpgradeSafety Safety, long DownloadSizeBytes);
}
