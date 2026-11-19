using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UpdateCenter.Services;

internal static class UnsignedUpdateAuthorizationTests
{
    internal static async Task RunAsync()
    {
        var expected = new Version(2, 4, 0, 0);
        var old = new Version(2, 0, 0, 0);
        if (!AppUpdateService.MatchesAssetVersion("2.4.0", "2.4.0", "2.4.0.0", expected, expected) ||
            AppUpdateService.MatchesAssetVersion("2.4.0", "2.4.0", "2.4.0.0", old, expected) ||
            AppUpdateService.MatchesAssetVersion("2.4.0", "2.4.0", "2.4.0.0", expected, old) ||
            AppUpdateService.MatchesAssetVersion("2.4.0", "2.0.0", "2.4.0.0", expected, expected))
            throw new InvalidOperationException("Both fixed and display versions must identify the same release.");
        Console.WriteLine("PASS FixedAndDisplayVersionsMustAgree");
        foreach (var scenario in new[] { "accepted", "setup", "absent", "wrong-origin", "duplicate", "wrong-tag",
            "wrong-hash", "wrong-size", "oversize", "foreign-url", "bytes-changed", "repeated-field", "wrong-other-package" })
        {
            using var http = new HttpClient(new Fixture(scenario));
            var service = new AppUpdateService(http);
            using var input = new MemoryStream("package"u8.ToArray());
            bool allowed;
            try { allowed = await service.CheckUnsignedAuthorizationAsync("2.4.0", scenario == "setup", input, default); }
            catch (InvalidDataException) when (scenario is not ("accepted" or "setup" or "absent"))
            {
                Console.WriteLine("PASS reject " + scenario);
                continue;
            }
            if (scenario == "absent" ? allowed : !allowed || scenario is not ("accepted" or "setup"))
                throw new InvalidOperationException("Unexpected authorization result for " + scenario);
            Console.WriteLine("PASS " + scenario);
        }
    }

    internal static async Task RunGeneratedAsync(string markerPath)
    {
        var name = Path.GetFileName(markerPath);
        const string prefix = "UpdateCenter-v", suffix = "-update.json";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
            throw new InvalidDataException("Expected a workflow update manifest.");
        var version = name[prefix.Length..^suffix.Length];
        if (!UpdateCenter.Models.SemanticVersion.TryParse(version, out var parsed) || parsed.ToString() != version)
            throw new InvalidDataException("Expected a canonical fixture version.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(markerPath))!;
        var portable = File.ReadAllBytes(Path.Combine(directory, $"UpdateCenter-v{version}-Portable.exe"));
        var setup = File.ReadAllBytes(Path.Combine(directory, $"UpdateCenter-Setup-v{version}.exe"));
        if (portable.AsSpan().SequenceEqual(setup)) throw new InvalidDataException("The two package fixtures must differ.");
        var marker = File.ReadAllBytes(markerPath);
        foreach (var (testSetup, tamper) in new[] { (false, false), (true, false), (false, true) })
        {
            using var http = new HttpClient(new Fixture(tamper ? "bytes-changed" : "accepted", version, marker, portable, setup));
            using var input = new MemoryStream(testSetup ? setup : portable);
            try
            {
                if (!await new AppUpdateService(http).CheckUnsignedAuthorizationAsync(version, testSetup, input, default))
                    throw new InvalidOperationException("The generated approval marker was ignored.");
            }
            catch (InvalidDataException) when (tamper)
            {
                Console.WriteLine("PASS TamperedMarkerRejectedByProductionReader");
                continue;
            }
            if (tamper) throw new InvalidOperationException("Modified marker accepted.");
            Console.WriteLine("PASS GeneratedManifestAcceptedByProductionReader " + (testSetup ? "Setup" : "Portable"));
        }
    }

    private sealed class Fixture(string scenario, string version = "2.4.0", byte[]? markerBytes = null,
        byte[]? portableBytes = null, byte[]? setupBytes = null) : HttpMessageHandler
    {
        private const string Root = "https://github.com/Illidan0S/UpdateCenter/releases/";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var tag = "v" + version;
            var marker = $"UpdateCenter-{tag}-update.json";
            var portable = portableBytes ?? "package"u8.ToArray();
            var setup = setupBytes ?? portable;
            var package = Convert.ToHexString(SHA256.HashData(portable));
            var setupHash = Convert.ToHexString(SHA256.HashData(setup));
            var metadata = JsonSerializer.Serialize(new {
                schema = 1, repository = scenario == "wrong-origin" ? "other/repo" : "Illidan0S/UpdateCenter",
                tag = scenario == "wrong-tag" ? "v2.4.1" : tag, portableSha256 = package,
                setupSha256 = scenario == "wrong-other-package" ? new string('B', 64) : setupHash
            });
            if (scenario == "repeated-field") metadata = metadata.Insert(1, "\"schema\":1,");
            var bytes = markerBytes ?? Encoding.UTF8.GetBytes(metadata);
            var digest = Convert.ToHexString(SHA256.HashData(bytes));
            object Asset(string name, string hash, int size) => new { name, digest = "sha256:" + hash, state = "uploaded", size,
                browser_download_url = (scenario == "foreign-url" && name == marker ? "https://example.invalid/" : Root) + "download/" + tag + "/" + name };
            var assets = new List<object> {
                Asset($"UpdateCenter-{tag}-Portable.exe", package, portable.Length), Asset($"UpdateCenter-Setup-{tag}.exe", setupHash, setup.Length),
                Asset($"UpdateCenter-{tag}-Portable.exe.sha256", package, 100), Asset($"UpdateCenter-Setup-{tag}.exe.sha256", setupHash, 100)
            };
            if (scenario != "absent") assets.Add(Asset(marker, scenario == "wrong-hash" ? new string('A',64) : digest,
                scenario == "oversize" ? 1000000 : bytes.Length + (scenario == "wrong-size" ? 1 : 0)));
            if (scenario == "duplicate") assets.Add(assets[^1]);
            var uri = request.RequestUri!.AbsoluteUri;
            HttpContent result;
            if (uri == "https://api.github.com/repos/Illidan0S/UpdateCenter/releases/tags/" + tag)
                result = new StringContent(JsonSerializer.Serialize(new { tag_name = tag, html_url = Root+"tag/"+tag, draft=false, prerelease=false, assets }));
            else if (uri == Root+"download/"+tag+"/"+marker)
                result = new ByteArrayContent(scenario == "bytes-changed" ? new byte[bytes.Length] : bytes);
            else throw new InvalidOperationException("Unexpected request " + uri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=result});
        }
    }
}
