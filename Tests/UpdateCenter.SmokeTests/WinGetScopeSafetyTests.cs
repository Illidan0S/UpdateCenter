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
        UnboundScanEligibilityAsync(Check).GetAwaiter().GetResult();
        RemovalPresentation(Check);
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS scope metadata: seven target shapes, inheritance, order independence, unbound architecture/type, IT/EN and no recovery of rejected items.");
    }

    private static async Task UnboundScanEligibilityAsync(Action<bool, string> check)
    {
        foreach (var (id, type, installed, target, behavior) in new[]
        {
            ("Microsoft.Outlook", "msix", "1.2026.617.600", "1.2026.812.100", ""),
            ("Fixture.Lower", "exe", "3.0", "2.0", "install"),
            ("Fixture.UnknownTarget", "exe", "1.0", "Unknown", "install"),
            ("ElectronicArts.EADesktop", "burn", "13.778.0.6291", "13.832.0.6345", "uninstallPrevious"),
            ("Fixture.Standard", "exe", "1.0", "2.0", "install")
        })
        {
            var manifest = $"PackageIdentifier: {id}\nPackageVersion: {target}\nInstallerType: {type}\n" +
                (id == "Microsoft.Outlook" ? "PackageFamilyName: Microsoft.OutlookForWindows_8wekyb3d8bbwe\n" : "Scope: machine\n") +
                (behavior.Length == 0 ? "" : $"UpgradeBehavior: {behavior}\n") +
                "Installers:\n- Architecture: x64\n  InstallerUrl: https://example.invalid/fixture\n" +
                "  InstallerSha256: " + new string('A', 64) + "\n";
            var item = new UpdateItem { Id = id, Name = id, Source = "winget", Kind = UpdateKind.Software,
                InstalledVersion = installed, AvailableVersion = target, WinGetClientVersion = WinGetScopeGuard.ReviewedClient };
            var safety = new WinGetManifestSafetyService((_, _, _) => Task.FromResult(new WinGetManifestSafetyService.ManifestInspection(
                WinGetManifestSafetyService.ParseUpgradeSafety(manifest, "x64"), 0,
                WinGetManifestSafetyService.ParseInstallerScope(manifest), manifest)),
                command => new WinGetScopeGuard(command, (_, _, _) => Task.FromResult<string?>(manifest),
                    () => "", (_, _) => "machine", "x64"), (_, _) => "machine", "x64", () => false);
            await safety.ApplyAsync([item], default, (_, _, _) => throw new InvalidOperationException("Unexpected WinGet command."));
            var targetError = WinGetService.ValidateTarget(ElevatedUpdateRunner.ToPlanItem(item));
            if (id == "Microsoft.Outlook")
                check(!item.CanInstall && !item.IsSelected && item.ResultDetails.Contains("MSIX", StringComparison.OrdinalIgnoreCase),
                    "MSIX-only target admitted without a supported package-family binding.");
            else if (id is "Fixture.Lower" or "Fixture.UnknownTarget")
                check(targetError is not null && !item.CanInstall && !item.IsSelected && item.ResultDetails == targetError.StandardError,
                    "Scan did not expose the preflight target rejection for " + id);
            else if (id == "ElectronicArts.EADesktop")
                check(!item.CanInstall && !item.IsSelected && item.WinGetVerification is null && item.RemovalManifestSha256 == "" &&
                    item.ResultDetails.Contains("Burn", StringComparison.OrdinalIgnoreCase),
                    "Unbound removal was admitted without proof of the installed and target Burn family.");
            else
                check(item.CanInstall && !item.RequiresRiskConfirmation && item.RemovalManifestSha256.Length == 0,
                    "Ordinary unbound proposal acquired a removal digest or was blocked.");
        }
    }

    private static UpdateItem Item(string id) => new()
    {
        Id = id, Name = id, Kind = UpdateKind.Software, Source = "winget",
        InstalledVersion = "1.0", AvailableVersion = "2.0"
    };

    private static void RemovalPresentation(Action<bool, string> check)
    {
        var ordinary = Item("Fixture.Ordinary");
        ordinary.HasUnverifiedInstallerMetadata = true;
        var digest = Item("Fixture.Burn");
        digest.RemovalManifestSha256 = new string('A', 64);
        var bound = Item("Fixture.Bound");
        bound.WinGetBinding = new("user", @"ARP\User\X64\Tool", "S-1-5-21-1000", "v1.29.290",
            new string('B', 64), "inno", "x64", "RemovesPreviousVersion");
        var blocked = Item("Fixture.Blocked");
        blocked.RemovalManifestSha256 = digest.RemovalManifestSha256;
        blocked.CanInstall = false;
        blocked.ResultDetails = "Prior rejection";
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            var marker = LocalizationService.Text("sul PC interessato", "on the affected PC");
            foreach (var item in new[] { digest, bound })
            {
                var remote = HeadlessScanService.Map(item);
                check(!remote.CanInstall && item.CanInstall && remote.ResultDetails.Contains(marker),
                    $"Remote removal proposal for {item.Id} did not stay local-only ({language}).");
            }
            check(HeadlessScanService.Map(ordinary).CanInstall, "Ordinary warning blocked a remote proposal.");
            check(HeadlessScanService.Map(blocked).ResultDetails == blocked.ResultDetails, "Earlier remote refusal was overwritten.");
            var risk = Item("Fixture.Removal");
            WinGetManifestSafetyService.ApplySafetyClassification(risk, WinGetUpgradeSafety.RemovesPreviousVersion);
            var warning = risk.ResultDetails;
            WinGetManifestSafetyService.ApplyInstallationRequirements(risk, new(WinGetRequirementState.Unknown, true,
                LocalizationService.Translate(LocalizationService.WinGetRequirementsUnknownMessage)));
            check(risk.RequiresRiskConfirmation && !risk.IsSelected && risk.ResultDetails.Contains(warning) &&
                risk.ResultDetails.Contains(LocalizationService.Text("privilegi", "privileges")) &&
                !risk.ResultDetails.Contains(LocalizationService.Text("non richiede una conferma", "does not require separate confirmation")),
                $"Uncertain privileges contradicted the required removal consent ({language}).");
        }
    }
}
