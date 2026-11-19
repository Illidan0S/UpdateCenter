using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed partial class AppUpdateService
{
    // A maintainer explicitly authorizes these bytes by publishing the per-tag marker.
    // Both it and the executables are bound to the official release by the GitHub API.
    internal async Task<bool> CheckUnsignedAuthorizationAsync(string version, bool setup, Stream candidate,
        CancellationToken token)
    {
        if (!SemanticVersion.TryParse(version, out var number) || number.ToString() != version ||
            number <= new SemanticVersion(2, 0, 1))
            throw new InvalidDataException("Versione non ammessa dal protocollo di aggiornamento.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        var tag = "v" + version;
        using var metadata = await _httpClient.GetAsync(ReleaseApi + tag,
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        metadata.EnsureSuccessStatusCode();
        await metadata.Content.LoadIntoBufferAsync(1024 * 1024, ct).ConfigureAwait(false);
        using var json = await metadata.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(json, JsonOptions, ct).ConfigureAwait(false);
        if (release is null || release.TagName != tag || release.Draft != false || release.Prerelease != false ||
            release.HtmlUrl != ReleasePage + "tag/" + tag || release.Assets is null)
            throw new InvalidDataException("Release ufficiale stabile non verificata.");

        var name = $"UpdateCenter-{tag}-update.json";
        if (!release.Assets.Any(asset => asset.Name == name)) return false;

        var marker = ExactUploadedAsset(release, tag, name);
        var markerHash = NormalizeApiDigest(marker.Digest);
        if (marker.Size is < 1 or > 4096 || markerHash is null)
            throw new InvalidDataException("Dimensione o digest del manifesto non validi.");

        var portable = ExactUploadedAsset(release, tag, AssetName(version, false));
        var installer = ExactUploadedAsset(release, tag, AssetName(version, true));
        var portableHash = NormalizeApiDigest(portable.Digest);
        var setupHash = NormalizeApiDigest(installer.Digest);
        if (portableHash is null || setupHash is null || portable.Size is < 1 or > 536870912 ||
            installer.Size is < 1 or > 536870912 || candidate.Length != (setup ? installer.Size : portable.Size))
            throw new InvalidDataException("Gli asset autorizzati non corrispondono alla release.");

        using var response = await GetAssetResponseAsync(ExactAssetUri(marker, tag, name), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(4096, ct).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length != marker.Size || Convert.ToHexString(SHA256.HashData(bytes)) != markerHash)
            throw new InvalidDataException("Il manifesto non coincide con il digest GitHub.");
        try
        {
            using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 3 });
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 5 ||
                !root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var revision) || revision != 1 ||
                !Matches(root, "repository", "Illidan0S/UpdateCenter") || !Matches(root, "tag", tag) ||
                !Matches(root, "portableSha256", portableHash) || !Matches(root, "setupSha256", setupHash))
                throw new InvalidDataException("Il manifesto non autorizza questi pacchetti.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Manifesto di aggiornamento non valido.", ex); }

        candidate.Position = 0;
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(candidate, ct).ConfigureAwait(false));
        if (actual != (setup ? setupHash : portableHash))
            throw new InvalidDataException("Il pacchetto locale non coincide con il manifesto.");
        return true;
    }

    private static bool Matches(JsonElement item, string property, string expected) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        value.ValueEquals(expected);
}
