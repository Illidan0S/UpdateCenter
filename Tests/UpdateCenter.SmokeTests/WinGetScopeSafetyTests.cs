using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class WinGetScopeSafetyTests
{
    internal static void Run()
    {
        var failures = new List<string>();
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        // Reduced fixtures from the exact manifests recorded in UPDATE-COVERAGE-NINE-ITEMS.
        // Architecture/type are deliberately not used to claim which installer the runner chooses.
        var fixtures = new (string Id, string Installed, string Manifest)[]
        {
            ("Perplexity.Comet", "user", "InstallerType: exe\nScope: machine\nInstallers:\n- Architecture: x64\n"),
            ("Git.Git", "machine", "InstallerType: inno\nInstallers:\n- Architecture: x64\n  Scope: user\n- Architecture: x64\n  Scope: machine\n"),
            ("Google.Chrome.EXE", "machine", "InstallerType: exe\nInstallers:\n- Architecture: x86\n  Scope: user\n- Architecture: x64\n  Scope: machine\n"),
            ("HypixelStudios.Hytale", "machine", "InstallerType: nullsoft\nScope: user\nInstallers:\n- Architecture: x64\n"),
            ("JRSoftware.InnoSetup.7", "user", "InstallerType: inno\nInstallers:\n- Architecture: x64\n  Scope: machine\n- Architecture: x64\n  Scope: user\n"),
            ("Microsoft.VisualStudioCode", "machine", "InstallerType: inno\nInstallers:\n- Architecture: x64\n  Scope: user\n- Architecture: arm64\n  Scope: machine\n"),
            ("Modrinth.ModrinthApp", "machine", "InstallerType: nullsoft\nScope: user\nInstallers:\n- Architecture: x64\n")
        };
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            foreach (var fixture in fixtures)
            {
                var item = Item(fixture.Id);
                WinGetManifestSafetyService.ApplyScopeCompatibility(item, fixture.Installed,
                    WinGetManifestSafetyService.ParseInstallerScope(fixture.Manifest));
                Check(!item.CanInstall && !item.IsSelected, fixture.Id + ": unresolved scope admitted.");
            }
            foreach (var manifest in new[]
            {
                "Installers:\n- Architecture: x64\n  Scope: machine\n- Architecture: x64\n  Scope: user\n",
                "Installers:\n- Architecture: x64\n  Scope: user\n- Architecture: x64\n  Scope: machine\n",
                "Installers:\n- Architecture: arm64\n  Scope: machine\n  InstallerType: msi\n- Architecture: x64\n  Scope: user\n  InstallerType: inno\n",
                "Scope: invalid\n"
            })
            {
                var scope = WinGetManifestSafetyService.ParseInstallerScope(manifest);
                Check(scope == "ambiguous", "Mixed/invalid scopes were represented as one chosen installer.");
                foreach (var installed in new[] { "machine", "user", "" })
                {
                    var item = Item("Fixture.Unbound");
                    WinGetManifestSafetyService.ApplyScopeCompatibility(item, installed, scope);
                    Check(!item.CanInstall && !item.IsSelected,
                        language + ": ambiguity must stay blocked even when installed scope is unavailable.");
                }
            }
        }
        Check(WinGetManifestSafetyService.ParseInstallerScope(
            "Scope: machine\nInstallers:\n- Architecture: x64\n- Architecture: arm64\n") == "machine",
            "Common inherited scope lost.");
        Check(WinGetManifestSafetyService.ParseInstallerScope(
            "Scope: 'USER' # common\nInstallers:\n- Architecture: x64\n  Scope: user\n") == "user",
            "Repeated equal scope is not ambiguous.");
        Check(WinGetManifestSafetyService.ParseInstallerScope("InstallerType: exe\n") == "ambiguous",
            "An incomplete manifest cannot attest a compatible scope.");
        // Missing/same metadata is not a recovery mechanism: no existing rejection may be undone.
        foreach (var metadata in new[] { "", "user", "machine", "ambiguous" })
        {
            var item = Item("Fixture.AlreadyBlocked");
            item.CanInstall = false;
            WinGetManifestSafetyService.ApplyScopeCompatibility(item, "machine", metadata);
            Check(!item.CanInstall && !item.IsSelected, "Scope metadata cleared an existing rejection.");
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS scope metadata: seven target shapes, inheritance, order independence, unbound architecture/type, IT/EN and no recovery of rejected items.");
    }

    private static UpdateItem Item(string id) => new()
    {
        Id = id, Name = id, Kind = UpdateKind.Software, Source = "winget",
        InstalledVersion = "1.0", AvailableVersion = "2.0"
    };
}
