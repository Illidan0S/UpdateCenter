using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class ReleaseAuthenticityTests
{
    public static void Run()
    {
        // Never enter the download method (which cleans Updates/) on a real profile.
        if (AppPaths.DataDirectory.Equals(AppPaths.ResolveDefaultRoot(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An isolated AppPaths root is required.");

        SelfUpdateMigrationTests.CheckWindowsAdmission();
        var installed = AppUpdateService.GetInstalledVersion();
        var available = installed with { Patch = installed.Patch + 1 };
        var candidateAssetName = $"UpdateCenter-v{available}-Portable.exe";
        var url = $"https://github.com/Illidan0S/UpdateCenter/releases/download/v{available}/{candidateAssetName}";
        var payload = "unsigned inert fixture, never executable"u8.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(payload));
        var info = new AppUpdateInfo
        {
            InstalledVersion = installed, AvailableVersion = available, ReleaseNotes = "",
            AssetName = candidateAssetName, DownloadSize = payload.Length,
            DownloadUri = new(url), Sha256Uri = new(url + ".sha256"),
            ApiSha256 = digest, IsInstallerPackage = false, ReleasePageUri = new("https://github.com/")
        };
        using var client = new HttpClient(new FixtureHttp(payload, digest, candidateAssetName));
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

    private sealed class FixtureHttp(byte[] payload, string digest, string candidateAssetName) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? new StringContent(digest + "  " + candidateAssetName)
                : new ByteArrayContent(payload);
            return Task.FromResult(response);
        }
    }
}
