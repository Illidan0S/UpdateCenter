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
                    var safety = await InspectAsync(item.Id, item.AvailableVersion, cancellationToken);
                    if (safety == WinGetUpgradeSafety.Safe) return;

                    item.CanInstall = false;
                    item.Status = LocalizationService.Text("Aggiornamento manuale", "Manual update");
                    item.ResultDetails = safety == WinGetUpgradeSafety.RemovesPreviousVersion
                        ? LocalizationService.Text(
                            "Il manifest prevede la rimozione della versione funzionante prima dell'installazione. Per evitare perdite in caso di errore, Update Center non esegue automaticamente questo aggiornamento.",
                            "The manifest removes the working version before installation. To prevent loss if setup fails, Update Center will not run this update automatically.")
                        : LocalizationService.Text(
                            "Update Center non è riuscito a verificare in modo certo il comportamento dell'installer. L'aggiornamento automatico è stato disattivato per sicurezza.",
                            "Update Center could not reliably verify the installer behavior. Automatic updating was disabled for safety.");
                }
                finally
                {
                    concurrency.Release();
                }
            });
        await Task.WhenAll(checks);
    }

    private static async Task<WinGetUpgradeSafety> InspectAsync(
        string packageId, string version, CancellationToken cancellationToken)
    {
        foreach (var uri in BuildManifestUris(packageId, version))
        {
            try
            {
                using var response = await Client.GetAsync(uri, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound) continue;
                if (!response.IsSuccessStatusCode) return WinGetUpgradeSafety.Unknown;
                return ParseUpgradeSafety(await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return WinGetUpgradeSafety.Unknown;
            }
            catch (HttpRequestException)
            {
                return WinGetUpgradeSafety.Unknown;
            }
        }

        return WinGetUpgradeSafety.Unknown;
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UpdateCenter/1.0.4");
        return client;
    }
}
