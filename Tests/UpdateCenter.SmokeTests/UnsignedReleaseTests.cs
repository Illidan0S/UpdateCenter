using System.Net;
using System.Net.Http;
using System.Reflection;
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

    internal static async Task Unsigned201Async(string officialPortable)
    {
        const string version = "2.0.1";
        const string name = "UpdateCenter-v2.0.1-Portable.exe";
        // The PE has the real build metadata. GitHub responses are simulated and
        // the fixture stops at the verifier without replacing or launching a file.
        var candidatePath = typeof(ReleasePublisherVerifier).Assembly.Location;
        using var installed = File.OpenRead(officialPortable);
        using var candidate = File.OpenRead(candidatePath);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(candidate));
        using var handler = new ReleaseFixture(name, digest, candidate.Length, version);
        using var http = new HttpClient(handler);
        var releases = new AppUpdateService(http);
        Task Verify(string targetVersion = version) => ReleasePublisherVerifier.VerifyReplacementAsync(
            officialPortable, installed, candidatePath, candidate, targetVersion, releases);

        await Verify();
        if (handler.RequestCount != 2)
            throw new InvalidOperationException("Unsigned 2.0.1 must consult the GitHub release and exact checksum.");
        Console.WriteLine("PASS Official115AcceptsExactPublishedUnsigned201");

        handler.Digest = new string('0', 64);
        await Reject(() => Verify(), "Unsigned201RejectsChangedPayload");
        handler.Digest = digest;
        handler.OmitDigest = true;
        await Reject(() => Verify(), "Unsigned201RequiresApiDigest");
        handler.OmitDigest = false;
        handler.SidecarName = "other.exe";
        await Reject(() => Verify(), "Unsigned201RejectsWrongChecksumName");
        handler.SidecarName = name;
        handler.PathOwner = "another/repository";
        await Reject(() => Verify(), "Unsigned201RejectsForeignAsset");
        handler.PathOwner = "Illidan0S/UpdateCenter";
        handler.Prerelease = true;
        await Reject(() => Verify(), "Unsigned201RejectsPrerelease");
        handler.Prerelease = false;
        handler.Draft = null;
        await Reject(() => Verify(), "Unsigned201RequiresExplicitStableFlags");
        handler.Draft = false;
        handler.Tag = "v2.0.2";
        await Reject(() => Verify(), "Unsigned201RejectsWrongReleaseTag");
        handler.Tag = "v2.0.1";
        handler.Redirect = new("https://example.invalid/checksum");
        await Reject(() => Verify(), "Unsigned201RejectsExternalChecksumRedirect");
        handler.Redirect = null;

        using var tampered = new MemoryStream();
        installed.Position = 0;
        await installed.CopyToAsync(tampered);
        tampered.Position = 0;
        var firstByte = tampered.ReadByte();
        tampered.Position = 0;
        tampered.WriteByte((byte)(firstByte ^ 1));
        var requests = handler.RequestCount;
        await Reject(() => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, tampered, candidatePath,
            candidate, version, releases), "Altered115CannotUseUnsigned201Policy");
        await Reject(() => Verify("2.0.2"), "UnsignedFutureReleaseIsNotAdmitted");
        if (handler.RequestCount != requests)
            throw new InvalidOperationException("An unrelated migration reached the exact release exception.");

        var future = new UpdateCenter.Models.AppUpdateInfo
        {
            InstalledVersion = new(2, 0, 1), AvailableVersion = new(2, 0, 2),
            ReleaseNotes = "", DownloadSize = candidate.Length, IsInstallerPackage = false,
            AssetName = "UpdateCenter-v2.0.2-Portable.exe",
            DownloadUri = new("https://github.com/Illidan0S/UpdateCenter/releases/download/v2.0.2/UpdateCenter-v2.0.2-Portable.exe"),
            Sha256Uri = new("https://github.com/Illidan0S/UpdateCenter/releases/download/v2.0.2/UpdateCenter-v2.0.2-Portable.exe.sha256"),
            ReleasePageUri = new("https://github.com/Illidan0S/UpdateCenter/releases/tag/v2.0.2")
        };
        await Reject(() => ReleasePublisherVerifier.VerifyDownloadAsync(candidatePath, candidatePath, candidate,
            future, releases, default), "Unsigned201RequiresSignedSuccessorBeforeLaunch");
        if (handler.RequestCount != requests)
            throw new InvalidOperationException("Future unsigned update reached official asset verification before authenticity.");
    }

    internal static async Task SignedSuccessorAsync(string officialPortable)
    {
        const string version = "2.0.1";
        const string asset = "UpdateCenter-v2.0.1-Portable.exe";
        var candidatePath = typeof(ReleasePublisherVerifier).Assembly.Location;
        using var installed = File.OpenRead(officialPortable);
        using var candidate = File.OpenRead(candidatePath);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(candidate));
        using var handler = new ReleaseFixture(asset, digest, candidate.Length, version);
        using var http = new HttpClient(handler);
        var releases = new AppUpdateService(http);
        var expectedPin = new string('A', 64);
        var signatureChecks = 0;
        var trusted = true;

        Task CheckSigner(string[] paths, string pin, CancellationToken token)
        {
            if (paths.Length != 1 || paths[0] != candidatePath || pin != expectedPin)
                throw new InvalidOperationException("The signer check must bind the staged PE to the configured publisher.");
            signatureChecks++;
            return trusted ? Task.CompletedTask : Task.FromException(new UnauthorizedAccessException());
        }
        Task Verify(string? pin) => ReleasePublisherVerifier.VerifyReplacementAsync(officialPortable, installed,
            candidatePath, candidate, version, releases, pin, CheckSigner);

        if (!typeof(ReleasePublisherVerifier).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(metadata => metadata.Key == "UnsignedReleaseVersion" && metadata.Value == version))
            await Reject(() => Verify(null), "Signed201RequiresConfiguredPin");
        await Reject(() => Verify("invalid"), "Signed201RejectsMalformedPin");
        if (signatureChecks != 0 || handler.RequestCount != 0)
            throw new InvalidOperationException("A missing or malformed pin reached the verification boundary.");

        trusted = false;
        await Reject(() => Verify(expectedPin), "Signed201RequiresAuthenticodeTrust");
        if (signatureChecks != 1 || handler.RequestCount != 0)
            throw new InvalidOperationException("A failed signer check reached the release boundary.");

        trusted = true;
        await Verify(expectedPin);
        if (signatureChecks != 2 || handler.RequestCount != 2)
            throw new InvalidOperationException("Both the pinned signer and exact published bytes are required.");
        Console.WriteLine("PASS Official115RequiresPinnedSignatureAndPublished201Bytes");

        handler.Digest = new string('0', 64);
        await Reject(() => Verify(expectedPin), "Signed201RejectsDifferentPublishedBytes");
        handler.Digest = digest;
        handler.SidecarName = "other.exe";
        await Reject(() => Verify(expectedPin), "Signed201RejectsForeignChecksum");
        handler.SidecarName = asset;
        handler.Prerelease = true;
        await Reject(() => Verify(expectedPin), "Signed201RejectsPrerelease");
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
