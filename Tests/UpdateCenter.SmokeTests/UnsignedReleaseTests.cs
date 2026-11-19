using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using UpdateCenter.Services;

internal static class UnsignedReleaseTests
{
    internal static async Task RunAsync(string officialPortable, string candidate)
    {
        using var installed = File.OpenRead(officialPortable);
        using var incoming = File.OpenRead(candidate);
        var name = "UpdateCenter-v2.0.0-Portable.exe";
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(incoming));
        using var handler = new ReleaseFixture(name, digest, incoming.Length);
        using var client = new HttpClient(handler);
        var releases = new AppUpdateService(client);
        await ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, installed, candidate, incoming, "2.0.0", releases);
        Console.WriteLine("PASS Official115AcceptsExactPublishedUnsigned200");

        handler.Digest = new string('0', 64);
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, installed, candidate, incoming, "2.0.0", releases),
            "DifferentAssetBytesRejected");
        handler.Digest = digest;

        handler.Tag = "v2.0.1";
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, installed, candidate, incoming, "2.0.0", releases),
            "WrongTagRejected");
        handler.Tag = "v2.0.0";
        handler.Prerelease = true;
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, installed, candidate, incoming, "2.0.0", releases),
            "PrereleaseRejected");
        handler.Prerelease = false;

        handler.PathOwner = "another/repository";
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, installed, candidate, incoming, "2.0.0", releases),
            "DifferentRepositoryRejected");
        handler.PathOwner = "Illidan0S/UpdateCenter";
        handler.Redirect = new("https://example.invalid/checksum");
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, installed, candidate, incoming, "2.0.0", releases),
            "OffsiteChecksumRedirectRejected");
        handler.Redirect = null;

        handler.SidecarName = "another.exe";
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, installed, candidate, incoming, "2.0.0", releases),
            "ChecksumMustNameTheExactAsset");
        handler.SidecarName = name;
        handler.OmitDigest = true;
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, installed, candidate, incoming, "2.0.0", releases),
            "MissingGitHubDigestRejected");
        handler.OmitDigest = false;

        var firstByte = installed.ReadByte();
        using var altered = new MemoryStream();
        installed.Position = 0;
        await installed.CopyToAsync(altered);
        altered.Position = 0;
        altered.WriteByte((byte)(firstByte ^ 1));
        var requests = handler.RequestCount;
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, altered, candidate, incoming, "2.0.0", releases),
            "Altered115CannotUseUnsignedPolicy");
        if (handler.RequestCount != requests)
            throw new InvalidOperationException("Altered original reached the unsigned policy.");

        // An unsigned installed 2.0.0 may transition only to a signed successor.
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(candidate, incoming, candidate, incoming, "2.0.0", releases),
            "UnsignedSameVersionDoesNotUseLegacyException");
    }

    private static async Task Reject(Func<Task> verify, string name)
    {
        try { await verify(); }
        catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException)
        {
            Console.WriteLine("PASS " + name);
            return;
        }
        throw new InvalidOperationException(name);
    }

    private sealed class ReleaseFixture(string name, string digest, long size, string version = "2.0.0") : HttpMessageHandler
    {
        internal string Tag { get; set; } = "v" + version;
        internal string PathOwner { get; set; } = "Illidan0S/UpdateCenter";
        internal string Digest { get; set; } = digest;
        internal string? SidecarName { get; set; }
        internal bool Prerelease { get; set; }
        internal bool? Draft { get; set; } = false;
        internal bool OmitDigest { get; set; }
        internal Uri? Redirect { get; set; }
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestCount++;
            var api = $"https://api.github.com/repos/Illidan0S/UpdateCenter/releases/tags/v{version}";
            var url = $"https://github.com/{PathOwner}/releases/download/v{version}/{name}";
            if (request.RequestUri?.AbsoluteUri == api)
            {
                var release = JsonSerializer.Serialize(new
                {
                    tag_name = Tag, draft = Draft, prerelease = Prerelease,
                    html_url = $"https://github.com/Illidan0S/UpdateCenter/releases/tag/v{version}",
                    assets = new[]
                    {
                        new { name, size, digest = OmitDigest ? null : "sha256:" + Digest, state = "uploaded", browser_download_url = url },
                        new { name = name + ".sha256", size = 100L, digest = (string?)null, state = "uploaded", browser_download_url = url + ".sha256" }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(release) });
            }
            if (request.RequestUri?.AbsoluteUri == url + ".sha256")
            {
                if (Redirect is not null)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                    response.Headers.Location = Redirect;
                    return Task.FromResult(response);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent($"{Digest}  {SidecarName ?? name}\r\n") });
            }
            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        }
    }
}
