using UpdateCenter;
internal static class UpdateExperienceTests
{
    internal static void Run()
    {
        if (typeof(MainWindow).Assembly.GetType("UpdateCenter.Services.ReleaseNotesService") is null)
            throw new InvalidOperationException("Missing version-bound release notes reader.");
        var type = typeof(MainWindow).Assembly.GetType("UpdateCenter.Services.ReleaseNotesService")!;
        var parse = type.GetMethod("Parse", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        object? Read(string yaml, string version = "2") => parse.Invoke(null, [yaml, "Test.App", version]);
        const string document = "PackageIdentifier: Test.App\nPackageVersion: '2'\nPackageLocale: it-IT\nReleaseNotes: |-\n  Novità\n  Seconda riga\nReleaseNotesUrl: https://example.com/releases/2\n";
        var result = Read(document) ?? throw new Exception("Exact release lost");
        if (!result.ToString()!.Contains("Novità") || Read(document, "3") is not null || Read(document + "PackageVersion: 3\n") is not null)
            throw new Exception("Release identity/multiline validation failed");
        var link = type.GetMethod("SafeLink", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var value in new[] { "file:///c:/test", "javascript:alert(1)", "https://localhost/x", "https://127.0.0.1/x", "https://user@example.com/x" })
            if (link.Invoke(null, [value]) is not null) throw new Exception("Unsafe external URL admitted");
        Console.WriteLine("PASS exact-version notes, multiline text, duplicate identity, unsafe links");
        CheckRetrieval().GetAwaiter().GetResult();
    }
    private static async Task CheckRetrieval()
    {
        var item = new UpdateCenter.Models.UpdateItem { Id = "Test.App", Name = "Test", Kind = UpdateCenter.Models.UpdateKind.Software, Source = "winget", AvailableVersion = "2" };
        var calls = new List<string>();
        var reader = new UpdateCenter.Services.ReleaseNotesService((uri, token) =>
        {
            calls.Add(uri.AbsoluteUri);
            return Task.FromResult<string?>(uri.AbsolutePath.EndsWith(".locale.fr-FR.yaml") ? "PackageIdentifier: Test.App\nPackageVersion: 2\nPackageLocale: fr-FR\nReleaseNotes: Notes françaises" : uri.AbsolutePath.EndsWith(".locale.it-IT.yaml") ? null : "PackageIdentifier: Test.App\nPackageVersion: 2\nManifestType: version\nDefaultLocale: fr-FR");
        });
        var result = await reader.GetAsync(item, "it", default);
        if (result.Text != "Notes françaises" || calls.Count != 3 || calls.Any(x => !x.Contains("/2/"))) throw new Exception("Locale fallback or exact-version retrieval failed");
        var offline = new UpdateCenter.Services.ReleaseNotesService((_, _) => throw new System.Net.Http.HttpRequestException());
        if ((await offline.GetAsync(item, "it", default)).Text != "") throw new Exception("Offline fallback failed");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var blocked = new UpdateCenter.Services.ReleaseNotesService(async (_, token) => { await Task.Delay(1000, token); return null; });
        try { await blocked.GetAsync(item, "it", cancel.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        if (!item.CanInstall || !item.IsSelected || item.WinGetBinding is not null) throw new Exception("Informational retrieval changed eligibility");
        Console.WriteLine("PASS on-demand exact version, locale fallback, offline, cancellation, eligibility unchanged");
        var wua = UpdateCenter.Services.ProviderReleaseNotes.Read(new FakeWua(), "Generic provider description");
        if (wua.Text != "Version-specific notes" || wua.Description != "Generic provider description" || wua.Url != "https://support.microsoft.com/help/1234567") throw new Exception("WUA notes/description/KB confused");
        Console.WriteLine("PASS WUA release notes separate from description, numeric KB fallback");
    }
}
public sealed class FakeWua
{
    public string ReleaseNotes => "Version-specific notes";
    public FakeStrings MoreInfoUrls => new([]);
    public FakeStrings KBArticleIDs => new(["1234567"]);
}
public sealed class FakeStrings(string[] values)
{
    public int Count => values.Length;
    public string Item(int i) => values[i];
}
