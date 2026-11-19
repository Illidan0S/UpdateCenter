using System.Net;
using System.Net.Http;
using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class AppUpdateDetectionTests
{
    private const string Tag = "v90.0.1";
    private const string ReleaseRoot = "https://github.com/Illidan0S/UpdateCenter/releases/";
    private const string PortableName = "UpdateCenter-" + Tag + "-Portable.exe";
    private const string SetupName = "UpdateCenter-Setup-" + Tag + ".exe";

    internal static async Task RunAsync()
    {
        await AppReleaseChoosesOnlyCompleteMatchingAssets();
        await PendingOfferSurvivesCooldownAndManualCheck();
    }

    private static async Task AppReleaseChoosesOnlyCompleteMatchingAssets()
    {
        using var handler = new ReleaseFixture();
        using var http = new HttpClient(handler);
        var service = new AppUpdateService(http);
        var offer = await service.CheckForUpdateAsync(default);
        Check(offer?.AssetName == PortableName && offer.ReleasePageUri.AbsoluteUri == ReleaseRoot + "tag/" + Tag,
            "A complete official Portable release must be offered.");
        var setup = await service.CheckForUpdateAsync(new(2, 0, 0), true, default);
        Check(setup is { IsInstallerPackage: true, AssetName: SetupName }, "Setup must receive the matching installer.");
        var current = await service.CheckForUpdateAsync(new(90, 0, 1), false, default);
        Check(current is null, "The current release must not be offered again.");
        handler.VersionTag = "v2.0.1";
        handler.ReleasePage = ReleaseRoot + "tag/v2.0.1";
        Check(await service.CheckForUpdateAsync(new(2, 0, 0), false, default) is null,
            "The withdrawn release must not be offered when GitHub still reports its tag as latest.");
        handler.VersionTag = Tag;
        handler.ReleasePage = ReleaseRoot + "tag/" + Tag;

        handler.Assets = DefaultAssets().Append(Asset(PortableName)).ToArray();
        await RejectReleaseAsync(service, "Duplicate executable assets");
        handler.Assets = DefaultAssets().Select(x => x.name == PortableName ? Asset(PortableName, "new") : x).ToArray();
        await RejectReleaseAsync(service, "Incomplete asset upload");
        handler.Assets = DefaultAssets().Where(x => x.name != PortableName + ".sha256").ToArray();
        await RejectReleaseAsync(service, "Missing checksum");
        handler.Assets = DefaultAssets();
        handler.ReleasePage = "https://github.com/another-owner/another-project/releases/tag/" + Tag;
        await RejectReleaseAsync(service, "Different GitHub release page");
        handler.ReleasePage = ReleaseRoot + "tag/" + Tag;
        handler.Status = HttpStatusCode.NotFound;
        try
        {
            await service.CheckForUpdateAsync(default);
            throw new InvalidOperationException("A missing stable release was reported as an up-to-date app.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
        Console.WriteLine("PASS AppReleaseChoosesOnlyCompleteMatchingAssets");
    }

    private static async Task PendingOfferSurvivesCooldownAndManualCheck()
    {
        JsonStorage.SaveSettings(new AppSettings());
        using var handler = new ReleaseFixture();
        using var http = new HttpClient(handler);
        var service = new AppUpdateService(http);
        var vm = new MainViewModel(ScanServices.Create(), service);
        Check(!vm.HasAppUpdateNotice, "No release has been confirmed on a fresh profile.");
        JsonStorage.SaveSettings(new AppSettings { PendingAppUpdateVersion = "2.0.1" });
        Check(!new MainViewModel(ScanServices.Create(), service).HasAppUpdateNotice,
            "A withdrawn release persisted by an older client must stay hidden even while offline.");
        var offered = await vm.CheckForAppUpdateAsync(manual: false);
        Check(offered is not null && vm.HasAppUpdateNotice && vm.AppUpdateNotice.Contains(Tag),
            "The background check must make the new version available in Home.");
        var reopened = new MainViewModel(ScanServices.Create(), service);
        Check(reopened.HasAppUpdateNotice && reopened.AppUpdateOffer is null,
            "Only the validated version label should survive a restart.");
        var requestCount = handler.RequestCount;
        Check(await reopened.CheckForAppUpdateAsync(manual: false) is null && handler.RequestCount == requestCount,
            "A recent successful check should reuse the stored notice without reaching GitHub.");

        handler.Status = HttpStatusCode.ServiceUnavailable;
        var previousSuccess = reopened.Settings.LastAppUpdateCheckUtc;
        Check(await reopened.CheckForAppUpdateAsync(manual: true) is null && reopened.HasAppUpdateNotice &&
            reopened.Settings.LastAppUpdateCheckUtc == previousSuccess && reopened.CanCheckAppUpdates,
            "A failed refresh must preserve the notice and permit retry.");
        reopened.IgnoreAppUpdate(new(90, 0, 1));
        var ignored = new MainViewModel(ScanServices.Create(), service);
        Check(!ignored.HasAppUpdateNotice, "The exact ignored release must remain hidden after restart.");

        handler.Status = HttpStatusCode.OK;
        Check(await ignored.CheckForAppUpdateAsync(manual: true) is not null && !ignored.HasAppUpdateNotice,
            "An explicit manual check may review an ignored version without enabling its automatic notice.");
        handler.VersionTag = "v0.0.0";
        handler.ReleasePage = ReleaseRoot + "tag/" + handler.VersionTag;
        Check(await ignored.CheckForAppUpdateAsync(manual: true) is null && ignored.Settings.PendingAppUpdateVersion == "" &&
            ignored.AppUpdateOffer is null, "A confirmed current result must clear a cached offer.");
        Console.WriteLine("PASS PendingOfferSurvivesCooldownAndManualCheck");
    }

    private static async Task RejectReleaseAsync(AppUpdateService service, string scenario)
    {
        try { await service.CheckForUpdateAsync(default); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(scenario + " was accepted as an update.");
    }

    private static ReleaseAsset[] DefaultAssets() =>
        [Asset(PortableName), Asset(PortableName + ".sha256"), Asset(SetupName), Asset(SetupName + ".sha256")];

    private static ReleaseAsset Asset(string name, string state = "uploaded") =>
        new(name, state, ReleaseRoot + "download/" + Tag + "/" + name, 128, null);

    private sealed record ReleaseAsset(string name, string state, string browser_download_url, long size, string? digest);

    private sealed class ReleaseFixture : HttpMessageHandler
    {
        public ReleaseAsset[] Assets { get; set; } = DefaultAssets();
        public string ReleasePage { get; set; } = ReleaseRoot + "tag/" + Tag;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string VersionTag { get; set; } = Tag;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri ==
                "https://api.github.com/repos/Illidan0S/UpdateCenter/releases/latest", "Unexpected network request in update detection.");
            RequestCount++;
            var data = JsonSerializer.Serialize(new
            {
                tag_name = VersionTag, draft = false, prerelease = false, html_url = ReleasePage,
                body = "## Italiano\n- Prima novità.\n\n## English\n- First change.",
                assets = Assets
            });
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(data) });
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
