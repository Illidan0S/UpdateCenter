using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class ReleaseAuthenticityTests
{
    private const string CandidateAssetName = "UpdateCenter-v2.0.1-Portable.exe";

    public static void Run()
    {
        // Never enter the download method (which cleans Updates/) on a real profile.
        if (AppPaths.DataDirectory.Equals(AppPaths.ResolveDefaultRoot(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An isolated AppPaths root is required.");

        var payload = "unsigned inert fixture, never executable"u8.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(payload));
        var info = new AppUpdateInfo
        {
            InstalledVersion = new(2, 0, 0), AvailableVersion = new(2, 0, 1), ReleaseNotes = "",
            AssetName = CandidateAssetName, DownloadSize = payload.Length,
            DownloadUri = new($"https://github.com/Illidan0S/UpdateCenter/releases/download/v2.0.1/{CandidateAssetName}"),
            Sha256Uri = new($"https://github.com/Illidan0S/UpdateCenter/releases/download/v2.0.1/{CandidateAssetName}.sha256"),
            ApiSha256 = digest, IsInstallerPackage = false, ReleasePageUri = new("https://github.com/")
        };
        using var client = new HttpClient(new FixtureHttp(payload, digest));
        var service = new AppUpdateService(client);
        try { service.DownloadAndStartUpdateAsync(info, null, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (UnauthorizedAccessException ex) when (ex.Message.Contains("GitHub", StringComparison.Ordinal))
        {
            if (File.Exists(Path.Combine(AppPaths.UpdatesDirectory, info.AssetName)))
                throw new InvalidOperationException("Unsigned candidate retained after rejection.");
            Console.WriteLine("PASS matching GitHub digest cannot authorize an unsigned update");
            return;
        }
        throw new InvalidOperationException("Unsigned update reached the launch boundary.");
    }

    private sealed class FixtureHttp(byte[] payload, string digest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? new StringContent(digest + "  " + CandidateAssetName)
                : new ByteArrayContent(payload);
            return Task.FromResult(response);
        }
    }
}
