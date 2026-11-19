using System.Reflection;
using System.Text.RegularExpressions;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class WinGetB2CoverageTests
{
    internal static void Run()
    {
        Action[] tests = [ExactManifestDifferential, MinicondaDenyCannotBecomeConsent,
            InheritedBehaviorHonorsOverrides, UnknownVariantIsNotSafe, CompatibleArchitecturesAreNotPreferences,
            LocalCopiesStayAmbiguous, InstalledInventoryKeepsAmbiguity, DuplicateCopiesAreNotVerified,
            DenyReasonSurvivesScopeSummary, UnsupportedShapeCannotAttestSafety];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            try { test(); Console.WriteLine("PASS B2 " + test.Method.Name); }
            catch (Exception ex) { failures.Add(test.Method.Name); Console.WriteLine("FAIL B2 " + test.Method.Name + ": " + ex.Message); }
        }
        Console.WriteLine($"B2 metadata boundaries: {tests.Length - failures.Count} PASS, {failures.Count} FAIL; no installer execution.");
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(", ", failures));
    }

    private static void ExactManifestDifferential()
    {
        // Literal expectations, same bytes for both algorithms. Local scopes are the
        // later read-only inventory, not invented reconstructions of the old scan.
        (string Id, string Local, string First, bool PreviouslyAllowed, string Current)[] cases =
        [
            ("Anaconda.Miniconda3", "user", "user", true, "ambiguous"),
            ("Anysphere.Cursor", "user", "user", true, "ambiguous"),
            ("AnywhereLabs.DSHDesktop", "user", "user", true, "ambiguous"),
            ("ElementLabs.LMStudio", "user", "user", true, "ambiguous"),
            ("Opera.OperaGX", "user", "user", true, "ambiguous"),
            ("Python.Python.3.13", "", "user", true, "ambiguous"),
            ("Git.Git", "machine", "user", false, "ambiguous"),
            ("Google.Chrome.EXE", "machine", "user", false, "ambiguous"),
            ("JRSoftware.InnoSetup.7", "user", "machine", false, "ambiguous"),
            ("Microsoft.VisualStudioCode", "machine", "user", false, "ambiguous"),
            ("Perplexity.Comet", "user", "machine", false, "machine"),
            ("HypixelStudios.Hytale", "machine", "user", false, "user"),
            ("Modrinth.ModrinthApp", "machine", "user", false, "user")
        ];
        foreach (var c in cases)
        {
            var manifest = Manifest(c.Id);
            // The pre-B algorithm at ef96f649, retained here ONLY as a differential oracle.
            var first = Regex.Match(manifest, "^\\s*Scope\\s*:\\s*['\\\"]?([^'\\\"\\s#]+)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline).Groups[1].Value.Trim().ToLowerInvariant();
            Check(first == c.First, c.Id + " historical first scope");
            var oldAllows = string.IsNullOrWhiteSpace(c.Local) || string.IsNullOrWhiteSpace(first) || c.Local == first;
            Check(oldAllows == c.PreviouslyAllowed, c.Id + " differential decision");
            var scope = WinGetManifestSafetyService.ParseInstallerScope(manifest);
            Check(scope == c.Current, c.Id + " current scope");
            var item = Item(c.Id);
            WinGetManifestSafetyService.ApplyScopeCompatibility(item, c.Local, scope);
            Check(!item.CanInstall && !item.IsSelected, c.Id + " unbound execution must remain blocked");
        }
    }

    private static void MinicondaDenyCannotBecomeConsent()
    {
        Check(WinGetManifestSafetyService.ParseUpgradeSafety(Manifest("Anaconda.Miniconda3"), "x64") ==
            WinGetUpgradeSafety.UpgradeUnsupported,
            "The user deny variant must not be authorized by the machine uninstallPrevious variant.");
        foreach (var order in new[] { new[] { "deny", "uninstallPrevious" }, new[] { "uninstallPrevious", "deny" } })
            Check(WinGetManifestSafetyService.ParseUpgradeSafety(
                "Installers:\n- Architecture: x64\n  UpgradeBehavior: " + order[0] +
                "\n- Architecture: x64\n  UpgradeBehavior: " + order[1], "x64") == WinGetUpgradeSafety.UpgradeUnsupported,
                "Reordering candidates changed deny into consent.");
    }

    private static void InheritedBehaviorHonorsOverrides()
    {
        Check(WinGetManifestSafetyService.ParseUpgradeSafety(
            "UpgradeBehavior: install\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: deny\n", "x64") ==
            WinGetUpgradeSafety.UpgradeUnsupported, "Common install hid an installer deny override.");
        Check(WinGetManifestSafetyService.ParseUpgradeSafety(
            "UpgradeBehavior: install\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: uninstallPrevious\n", "x64") ==
            WinGetUpgradeSafety.RemovesPreviousVersion, "Common install hid removal.");
        Check(WinGetManifestSafetyService.ParseUpgradeSafety(
            "UpgradeBehavior: deny\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: install\n", "x64") ==
            WinGetUpgradeSafety.Safe, "An overridden root value was treated as another installer.");
        Check(WinGetManifestSafetyService.ParseUpgradeSafety(
            "UpgradeBehavior: install\nInstallers:\n- Architecture: x64\n- Architecture: x64\n", "x64") ==
            WinGetUpgradeSafety.Safe, "Inherited install was lost.");
    }

    private static void UnknownVariantIsNotSafe()
    {
        foreach (var unknown in new[] { "", "  UpgradeBehavior: futureBehavior\n" })
            Check(WinGetManifestSafetyService.ParseUpgradeSafety(
                "Installers:\n- Architecture: x64\n  UpgradeBehavior: install\n- Architecture: x64\n" + unknown, "x64") ==
                WinGetUpgradeSafety.Unknown, "One known install concealed an unknown candidate.");
    }

    private static void UnsupportedShapeCannotAttestSafety()
    {
        Check(WinGetManifestSafetyService.ParseUpgradeSafety(
            "Scope: user\nInstallers:\n- InstallerType: exe\n  Architecture: x64\n  UpgradeBehavior: install\n" +
            "- InstallerType: exe\n  Architecture: x64\n", "x64") == WinGetUpgradeSafety.Unknown,
            "Unrecognized installer boundaries concealed an unknown sibling.");
    }

    private static void DuplicateCopiesAreNotVerified()
    {
        var item = new PlanItem { Id = "Example.Copies", Source = "winget", AvailableVersion = "2.0" };
        foreach (var secondVersion in new[] { "2.0", "1.0" })
        {
            var result = WinGetService.VerifyInstallation(item, (_, _) => (new ProcessResult(0, "", ""),
                [new("Copy", item.Id, "2.0", "", "winget"), new("Copy", item.Id, secondVersion, "", "winget")]), maxAttempts: 1);
            Check(!result.Verified && !result.IsDefinitive && result.Status == UpdateVerificationStatuses.Unavailable,
                "Multiple installed copies were collapsed into a verified version.");
        }
        var single = WinGetService.VerifyInstallation(item, (_, _) => (new ProcessResult(0, "", ""),
            [new("Copy", item.Id, "2.0", "", "winget")]), maxAttempts: 1);
        Check(single.Verified, "Unique exact installed result stopped verifying.");
    }

    private static void CompatibleArchitecturesAreNotPreferences()
    {
        foreach (var (behavior, expected) in new[] { ("deny", WinGetUpgradeSafety.UpgradeUnsupported),
            ("uninstallPrevious", WinGetUpgradeSafety.RemovesPreviousVersion) })
            Check(WinGetManifestSafetyService.ParseUpgradeSafety(
                "Installers:\n- Architecture: x64\n  InstallerType: inno\n  UpgradeBehavior: install\n" +
                "- Architecture: x86\n  InstallerType: exe\n  UpgradeBehavior: " + behavior, "x64") == expected,
                "OS-preferred architecture/type hid another runner-selectable installer.");
        Check(WinGetManifestSafetyService.ParseUpgradeSafety(
            "Installers:\n- Architecture: x64\n  UpgradeBehavior: install\n" +
            "- Architecture: arm64\n  UpgradeBehavior: deny\n", "x64") == WinGetUpgradeSafety.Safe,
            "An inapplicable ARM64 installer blocked x64.");
    }

    private static void LocalCopiesStayAmbiguous()
    {
        Check(WinGetInstalledScopeService.ResolveScope([new("user", "1.0")], "1.0") == "user", "Unique HKCU copy");
        Check(WinGetInstalledScopeService.ResolveScope([new("machine", "1.0")], "1.0") == "machine", "Unique HKLM copy");
        foreach (var scope in new[] { "user", "machine" })
        foreach (var version in new[] { "1.0", "2.0" })
        {
            var detected = WinGetInstalledScopeService.ResolveScope([new("user", "1.0"), new(scope, version)], "1.0");
            Check(detected == "ambiguous", "Multiple same-name copies disappeared during version/scope matching.");
            var item = Item("Example.Duplicate");
            WinGetManifestSafetyService.ApplyScopeCompatibility(item, detected, "user");
            Check(!item.CanInstall && !item.IsSelected, "Ambiguous local identity admitted by uniform manifest.");
        }
        Check(WinGetInstalledScopeService.ResolveScope([new("user", "3.13.15150.0")], "3.13.15") == "",
            "ARP mapping was invented from unequal versions.");
        Check(WinGetInstalledScopeService.ResolveScope([new("user", "")], "1.0") == "", "Missing ARP version attested scope.");
        Check(WinGetInstalledScopeService.ResolveScope([new("user", "1.0")], "") == "", "Missing offered version attested scope.");
        Check(WinGetInstalledScopeService.ResolveScope([], "1.0") == "", "Absent entry attested scope.");
        Check(WinGetInstalledScopeService.ResolveScope([new("machine", "1.0"), new("machine", "1.0", true)], "1.0") == "machine",
            "A hidden SystemComponent MSI was confused with a second visible copy.");
    }

    private static void InstalledInventoryKeepsAmbiguity()
    {
        foreach (var secondVersion in new[] { "1.0", "0.9" })
        {
            var item = Item("Example.Copies");
            var candidates = WinGetService.FilterVerifiedInstalledCandidates([item],
                [new("Copy", item.Id, "1.0", "", "winget"), new("Other copy", item.Id, secondVersion, "", "winget")]);
            Check(candidates.Count == 1 && !candidates[0].CanInstall && !candidates[0].IsSelected,
                "Duplicate installed IDs became a unique upgradable target.");
            var reason = item.ResultDetails;
            WinGetManifestSafetyService.ApplySafetyClassification(item, WinGetUpgradeSafety.RemovesPreviousVersion);
            Check(!item.CanInstall && item.ResultDetails == reason, "Risk summary concealed the identity rejection.");
        }
        var single = Item("Example.Single");
        Check(WinGetService.FilterVerifiedInstalledCandidates([single],
            [new(single.Name, single.Id, "1.0", "", "winget")]).Single().CanInstall, "Unique installed ID blocked.");
    }

    private static void DenyReasonSurvivesScopeSummary()
    {
        foreach (var language in new[] { "it", "en" })
        {
            LocalizationService.Initialize(language);
            var item = Item("Anaconda.Miniconda3");
            WinGetManifestSafetyService.ApplySafetyClassification(item,
                WinGetManifestSafetyService.ParseUpgradeSafety(Manifest(item.Id), "x64"));
            var denyReason = item.ResultDetails;
            WinGetManifestSafetyService.ApplyScopeCompatibility(item, "user", "ambiguous");
            item.IsSelected = true;
            Check(!item.CanInstall && !item.IsSelected && !item.RequiresRiskConfirmation &&
                denyReason.Length > 0 && item.ResultDetails == denyReason,
                "A scope summary or individual selection turned deny into specific consent.");
        }
        LocalizationService.Initialize("it");
    }

    private static UpdateItem Item(string id) => new()
    {
        Id = id, Name = id, Source = "winget", Kind = UpdateKind.Software,
        InstalledVersion = "1.0", AvailableVersion = "2.0"
    };

    private static string Manifest(string id)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith("." + id + ".installer.yaml", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
