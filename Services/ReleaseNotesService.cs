using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed record ReleaseNotesInfo(string Text = "", string Url = "", string Description = "", string Locale = "");

// Informational metadata only: never projected into an installation plan.
internal sealed class ReleaseNotesService
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Func<Uri, CancellationToken, Task<string?>> _read;
    internal ReleaseNotesService(Func<Uri, CancellationToken, Task<string?>>? read = null) => _read = read ?? ReadAsync;

    internal async Task<ReleaseNotesInfo> GetAsync(UpdateItem item, string language, CancellationToken token)
    {
        if (!item.Source.Equals("winget", StringComparison.OrdinalIgnoreCase))
            return item.ProviderReleaseNotes with { Url = SafeLink(item.ProviderReleaseNotes.Url)?.AbsoluteUri ?? SafeLink(item.OfficialReleasePageUrl)?.AbsoluteUri ?? "" };
        if (!Regex.IsMatch(item.Id, @"^[A-Za-z0-9][A-Za-z0-9.\-_]+$") || string.IsNullOrWhiteSpace(item.AvailableVersion) || item.AvailableVersion.Length > 128)
            return new();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var root = WinGetManifestSafetyService.BuildManifestUris(item.Id, item.AvailableVersion)[1];
        try
        {
            var yaml = await _read(root, timeout.Token);
            var fields = Fields(yaml ?? "");
            if (fields is null || !Identity(fields, item.Id, item.AvailableVersion)) return new();
            if (fields.GetValueOrDefault("ManifestType") == "singleton") return Parse(yaml!, item.Id, item.AvailableVersion) ?? new();
            var preferred = language == "it" ? "it-IT" : "en-US";
            var fallback = fields.GetValueOrDefault("DefaultLocale", "en-US");
            foreach (var locale in new[] { preferred, fallback }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Regex.IsMatch(locale, @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")) continue;
                var uri = new Uri(root, Uri.EscapeDataString(item.Id) + ".locale." + locale + ".yaml");
                var data = await _read(uri, timeout.Token);
                var notes = data is null ? null : Parse(data, item.Id, item.AvailableVersion);
                if (notes is not null && notes.Locale.Equals(locale, StringComparison.OrdinalIgnoreCase) && (notes.Text.Length > 0 || notes.Url.Length > 0)) return notes;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        catch (HttpRequestException) { }
        catch (IOException) { }
        token.ThrowIfCancellationRequested();
        return new();
    }

    internal static ReleaseNotesInfo? Parse(string yaml, string id, string version)
    {
        var values = Fields(yaml);
        if (values is null || !Identity(values, id, version)) return null;
        return new(values.GetValueOrDefault("ReleaseNotes", ""), SafeLink(values.GetValueOrDefault("ReleaseNotesUrl"))?.AbsoluteUri ?? "", "", values.GetValueOrDefault("PackageLocale", ""));
    }
    private static bool Identity(Dictionary<string, string> fields, string id, string version) =>
        fields.GetValueOrDefault("PackageIdentifier") == id && fields.GetValueOrDefault("PackageVersion") == version;

    // Explicit subset of canonical root YAML scalars; unsupported syntax yields no notes.
    private static Dictionary<string, string>? Fields(string yaml)
    {
        if (yaml.Length > 262144 || yaml.Contains('\t')) return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = yaml.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith('#') || line.StartsWith('-')) continue;
            var match = Regex.Match(line, @"^([A-Za-z]+):\s*(.*)$");
            if (!match.Success) return null;
            var key = match.Groups[1].Value;
            if (key is not ("PackageIdentifier" or "PackageVersion" or "PackageLocale" or "DefaultLocale" or "ManifestType" or "ReleaseNotes" or "ReleaseNotesUrl")) continue;
            var value = match.Groups[2].Value.Trim();
            if (value is "|" or "|-" or "|+" or ">" or ">-" or ">+")
            {
                var block = new List<string>();
                while (i + 1 < lines.Length && (lines[i + 1].Length == 0 || lines[i + 1].StartsWith(' '))) block.Add(lines[++i]);
                var indent = block.Where(x => x.Length > 0).Select(x => x.Length - x.TrimStart().Length).DefaultIfEmpty(0).Min();
                value = string.Join(value.StartsWith('>') ? " " : "\n", block.Select(x => x.Length >= indent ? x[indent..] : "")).TrimEnd();
            }
            else if (value.StartsWith('"'))
            {
                try { value = JsonSerializer.Deserialize<string>(value) ?? ""; } catch (JsonException) { return null; }
            }
            else if (value.StartsWith('\''))
            {
                if (!value.EndsWith('\'') || value.Length < 2) return null;
                value = value[1..^1].Replace("''", "'");
            }
            else
            {
                if (value.StartsWith('&') || value.StartsWith('*') || value.StartsWith('!') || value.StartsWith('[') || value.StartsWith('{')) return null;
                var comment = value.IndexOf(" #", StringComparison.Ordinal); if (comment >= 0) value = value[..comment].TrimEnd();
            }
            if (!result.TryAdd(key, value)) return null;
        }
        return result;
    }
    internal static Uri? SafeLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.UserInfo.Length == 0 && !uri.IsLoopback && uri.Host.Contains('.') && !IPAddress.TryParse(uri.Host, out _) && uri.HostNameType == UriHostNameType.Dns ? uri : null;
    private static async Task<string?> ReadAsync(Uri uri, CancellationToken token)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 262144) return null;
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (bytes.Length + count > 262144) return null;
            bytes.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\uFEFF');
    }
}
