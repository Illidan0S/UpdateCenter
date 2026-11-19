using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

// A publisher-specific Burn transition, deliberately separate from B2's generic
// same-LocalId rule. Reviewed CPython sources and manifest bytes are recorded in
// _output/r3-b1-20261110/research. New release pairs require a recipe review.
internal static class PythonBurnTransitionService
{
    internal const string PackageId = "Python.Python.3.13";
    internal const string Provider = "CPython-3.13";
    internal const string UpgradeCode = "{F96F59BD-3E48-5E4E-B1A4-6CAFADEFEAE8}";
    internal const string CustomArguments = "Include_launcher=0";
    private const string Publisher = "Python Software Foundation";
    private const string OldManifest = "7FE6AAB138453720882711688FFB220F9FD81A42C56BFE7D4A10939E6DE052C2";
    private const string TargetManifest = "58EBFF63FDD8E89A0F2CFE722C72E724F6C038FFC1AB2497620E4A5735650FAB";

    internal static bool IsCandidate(string id) => id == PackageId;

    internal static (WinGetInstallerVariant Old, WinGetInstallerVariant Target)? Recipes(
        string id, string installed, string target, string oldManifest, string targetManifest)
    {
        if (id != PackageId || installed != "3.13.15" || target != "3.13.17" ||
            WinGetScopeGuard.Digest(oldManifest) != OldManifest || WinGetScopeGuard.Digest(targetManifest) != TargetManifest)
            return null;
        var old = Recipe(oldManifest, id, installed);
        var next = Recipe(targetManifest, id, target);
        if (old is null || next is null || old.ProductCode == next.ProductCode ||
            old.MappingUpgradeCode != next.MappingUpgradeCode) return null;
        return (old, next);
    }

    private static WinGetInstallerVariant? Recipe(string manifest, string id, string version)
    {
        var parsed = WinGetInstallerMetadata.Parse(manifest);
        if (parsed is null || parsed.Id != id || parsed.Version != version || parsed.Installers.Any(x => x.HasDependencies)) return null;
        var recipes = parsed.Installers.Where(x => x.Scope == "user" && x.Architecture == "x64").ToArray();
        if (recipes.Length != 1) return null;
        var r = recipes[0];
        return r.Type == "burn" && r.Behavior == "install" && r.Elevation == "" && r.NestedType == "" &&
            r.Custom == "InstallAllUsers=0 PrependPath=1" && r.InstallLocation == "DefaultJustForMeTargetDir=<INSTALLPATH>" &&
            r.MappingUpgradeCode == UpgradeCode && r.HasVersionMapping && r.MappingProductCode == "" &&
            Guid.TryParseExact(r.ProductCode, "B", out _) && r.DisplayVersion.Length > 0 &&
            r.Url == $"https://www.python.org/ftp/python/{version}/python-{version}-amd64.exe" && Hash(r.Hash)
                ? r : null;
    }

    internal static WinGetScopeBinding? Prepare(UpdateItem item, string oldManifest, string targetManifest,
        PythonBurnInventory? inventory, WinGetScopeGuard.InstalledIdentity? identity, string sid, string architecture,
        Version windows, bool administrator, string clientVersion)
    {
        var recipes = Recipes(item.Id, item.InstalledVersion, item.AvailableVersion, oldManifest, targetManifest);
        if (recipes is not { } pair || !WinGetScopeGuard.IsReviewedClient(clientVersion) ||
            architecture != "x64" || windows < new Version(10, 0, 17763) || administrator ||
            identity is null || identity.Name != item.Name || identity.Version != item.InstalledVersion ||
            !Matches(inventory, identity, pair.Old, item.InstalledVersion, sid)) return null;
        var state = inventory!;
        return new("user", identity.LocalId, sid, clientVersion,
            WinGetScopeGuard.Digest(targetManifest), "burn", "x64", "Safe", "",
            new(WinGetScopeGuard.Digest(oldManifest), LocalId(pair.Target.ProductCode), UpgradeCode, Provider,
                state.InstallPath, state.FeatureNames, state.ExecutableSha256, state.WindowedExecutableSha256,
                state.UserPathSha256, CustomArguments, state.LauncherStateSha256));
    }

    internal static bool Verify(PlanItem item, string oldManifest, string targetManifest,
        PythonBurnInventory? inventory, WinGetScopeGuard.InstalledIdentity? identity, string sid)
    {
        var b = item.WinGetBinding;
        var t = b?.PythonBurnTransition;
        var recipes = Recipes(item.Id, item.InstalledVersion, item.AvailableVersion, oldManifest, targetManifest);
        if (b is null || t is null || recipes is not { } pair || !ValidBinding(b) ||
            t.OldManifestSha256 != WinGetScopeGuard.Digest(oldManifest) ||
            b.ManifestSha256 != WinGetScopeGuard.Digest(targetManifest) || b.LocalId != LocalId(pair.Old.ProductCode) ||
            t.TargetLocalId != LocalId(pair.Target.ProductCode) ||
            !Matches(inventory, identity, pair.Target, item.AvailableVersion, sid)) return false;
        // A single target bundle plus unchanged core path/features proves replacement,
        // rather than a second installation or the version of a different Python line.
        // The old bundle must be absent from the complete relevant ARP inventory.
        return identity!.LocalId == t.TargetLocalId && inventory!.InstallPath == t.InstallPath &&
            inventory.FeatureNames == t.FeatureNames && inventory.UserPathSha256 == t.UserPathSha256 &&
            inventory.LauncherStateSha256 == t.LauncherStateSha256;
    }

    private static bool Matches(PythonBurnInventory? state, WinGetScopeGuard.InstalledIdentity? identity,
        WinGetInstallerVariant recipe, string version, string sid)
    {
        if (state is null || identity is null || state.AccountSid != sid || state.OtherCorePresent ||
            state.Bundles.Count != 1 || identity.Scope != "user" || identity.Architecture != "x64" ||
            identity.LocalId != LocalId(recipe.ProductCode) || identity.Version != version ||
            !SafePath(state.InstallPath) || state.ExecutableVersion != version || state.WindowedExecutableVersion != version ||
            !Hash(state.ExecutableSha256) || !Hash(state.WindowedExecutableSha256) || !Hash(state.UserPathSha256) ||
            !Hash(state.LauncherStateSha256) ||
            !state.PathConfigured || !Features(state.FeatureNames) || state.FeatureVersion != recipe.DisplayVersion ||
            !Version.TryParse(state.CrtVersion, out var crt) || crt.Major != 10 || crt.Minor != 0 ||
            crt < new Version(10, 0, 10137, 0)) return false;
        var bundle = state.Bundles[0];
        return bundle.LocalId == identity.LocalId && bundle.Name == identity.Name &&
            bundle.DisplayVersion == recipe.DisplayVersion && bundle.BundleVersion == recipe.DisplayVersion &&
            bundle.UpgradeCode == UpgradeCode && bundle.ProviderKey == Provider && bundle.Publisher == Publisher;
    }

    internal static bool ValidBinding(WinGetScopeBinding binding)
    {
        var t = binding.PythonBurnTransition;
        return t is not null && WinGetScopeGuard.IsReviewedClient(binding.ClientVersion) &&
            binding.Scope == "user" && binding.Architecture == "x64" && binding.InstallerType == "burn" &&
            binding.Safety == "Safe" && binding.ElevationRequirement == "" && binding.ManifestSha256 == TargetManifest &&
            t.OldManifestSha256 == OldManifest && t.UpgradeCode == UpgradeCode && t.ProviderKey == Provider &&
            Regex.IsMatch(t.TargetLocalId ?? "", @"^ARP\\User\\X64\\\{[a-fA-F0-9-]{36}\}$") &&
            t.TargetLocalId != binding.LocalId && SafePath(t.InstallPath) && Features(t.FeatureNames) &&
            Hash(t.ExecutableSha256) && Hash(t.WindowedExecutableSha256) && Hash(t.UserPathSha256) &&
            t.CustomArguments == CustomArguments && Hash(t.LauncherStateSha256);
    }

    internal static bool SameEvidence(PythonBurnInventory? left, PythonBurnInventory? right) =>
        left is not null && right is not null && left.Bundles.SequenceEqual(right.Bundles) &&
        (left with { Bundles = Array.Empty<PythonBurnRegistration>() }) ==
        (right with { Bundles = Array.Empty<PythonBurnRegistration>() });

    private static bool Features(string? names)
    {
        if (names is null) return false;
        var parts = names.Split('|');
        string[] supported = ["Shortcuts", "dev", "doc", "doc_shortcut", "exe", "lib", "path", "pip", "tcltk", "test"];
        return parts.Length >= 3 && parts.Distinct(StringComparer.Ordinal).Count() == parts.Length &&
            parts.All(x => supported.Contains(x, StringComparer.Ordinal)) &&
            parts.SequenceEqual(parts.Order(StringComparer.Ordinal)) &&
            new[] { "exe", "lib", "path" }.All(x => parts.Contains(x, StringComparer.Ordinal));
    }
    private static bool Hash(string? value) => Regex.IsMatch(value ?? "", "^[A-F0-9]{64}$");
    private static bool SafePath(string? path) => path is not null && path.Length is > 3 and < 1024 &&
        Regex.IsMatch(path, @"^[A-Za-z]:\\") && !path.Any(char.IsControl) &&
        !path.Contains("..", StringComparison.Ordinal) && !path.Contains('/') && !path.EndsWith('\\');
    private static string LocalId(string product) => @"ARP\User\X64\" + product;

    // Read-only registry/PE/version/hash inspection. No MSI API which could repair,
    // no executable launch, no installer payload/cache read and no persistent I/O.
    internal static PythonBurnInventory? ReadInventory()
    {
        try
        {
            using var account = WindowsIdentity.GetCurrent();
            var sid = account.User?.Value;
            if (sid is null) return null;
            var bundles = new List<PythonBurnRegistration>();
            var launchers = new List<string>();
            ReadBundles(RegistryHive.CurrentUser, RegistryView.Default, "User", bundles, launchers);
            ReadBundles(RegistryHive.LocalMachine, RegistryView.Registry64, "Machine", bundles, launchers);
            ReadBundles(RegistryHive.LocalMachine, RegistryView.Registry32, "Machine", bundles, launchers);
            using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var core = user.OpenSubKey(@"Software\Python\PythonCore\3.13", writable: false);
            using var location = core?.OpenSubKey("InstallPath", writable: false);
            using var features = core?.OpenSubKey("InstalledFeatures", writable: false);
            var path = (location?.GetValue("") as string)?.TrimEnd('\\');
            if (!SafePath(path) || features is null ||
                location!.GetValue("ExecutablePath") as string != path + @"\python.exe" ||
                location.GetValue("WindowedExecutablePath") as string != path + @"\pythonw.exe") return null;
            var names = features.GetValueNames().Order(StringComparer.Ordinal).ToArray();
            if (names.Length == 0 || names.Any(x => features.GetValueKind(x) != RegistryValueKind.String)) return null;
            var versions = names.Select(x => features.GetValue(x) as string).Distinct(StringComparer.Ordinal).ToArray();
            if (versions.Length != 1 || versions[0] is null) return null;
            var exe = ReadExecutable(path + @"\python.exe", "python.exe");
            var windowed = ReadExecutable(path + @"\pythonw.exe", "pythonw.exe");
            using var environment = user.OpenSubKey("Environment", writable: false);
            if (exe is null || windowed is null || environment is null ||
                environment.GetValueKind("Path") != RegistryValueKind.ExpandString) return null;
            var userPath = environment.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (userPath is null) return null;
            var paths = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimEnd('\\')).ToArray();
            var configured = paths.Count(x => x.Equals(path, StringComparison.OrdinalIgnoreCase)) == 1 &&
                paths.Count(x => x.Equals(path + @"\Scripts", StringComparison.OrdinalIgnoreCase)) == 1;
            var otherCore = OtherCore(RegistryHive.CurrentUser, RegistryView.Default, allowUserCore: true) ||
                OtherCore(RegistryHive.LocalMachine, RegistryView.Registry64, allowUserCore: false) ||
                OtherCore(RegistryHive.LocalMachine, RegistryView.Registry32, allowUserCore: false);
            var crt = FileVersionInfo.GetVersionInfo(Path.Combine(Environment.SystemDirectory, "ucrtbase.dll"));
            var crtVersion = new Version(crt.FileMajorPart, crt.FileMinorPart, crt.FileBuildPart, crt.FilePrivatePart).ToString();
            return new(sid, bundles, path!, string.Join('|', names), versions[0]!, exe.Value.Version, exe.Value.Hash,
                windowed.Value.Version, windowed.Value.Hash, WinGetScopeGuard.Digest(userPath), configured, otherCore, crtVersion,
                WinGetScopeGuard.Digest(string.Join('\n', launchers.Order(StringComparer.Ordinal))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or
            ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return null; }
    }

    private static void ReadBundles(RegistryHive hive, RegistryView view, string scope, List<PythonBurnRegistration> result,
        List<string> launchers)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var arp = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", writable: false);
        if (arp is null) return;
        foreach (var name in arp.GetSubKeyNames())
        {
            using var key = arp.OpenSubKey(name, writable: false);
            if (key is null) throw new IOException("ARP changed during inspection.");
            var display = key.GetValue("DisplayName") as string ?? "";
            var provider = key.GetValue("BundleProviderKey") as string ?? "";
            var codes = key.GetValue("BundleUpgradeCode") as string[];
            if (display == "Python Launcher")
                launchers.Add($"{hive}|{view}|{name}|{display}|{key.GetValue("DisplayVersion")}|{key.GetValue("Publisher")}|{key.GetValue("WindowsInstaller")}");
            var visiblePython = Regex.IsMatch(display, @"^Python 3\.13(?:\.| |$)") &&
                !(key.GetValue("SystemComponent") is int hidden && hidden != 0 && key.GetValue("WindowsInstaller") is int msi && msi == 1);
            if (provider != Provider && codes?.Contains(UpgradeCode, StringComparer.OrdinalIgnoreCase) != true && !visiblePython) continue;
            var arch = view == RegistryView.Registry32 ? "X86" : "X64";
            // HKCU's uninstall key is shared between views. Read it once; do not
            // manufacture two installed copies from that registry alias.
            result.Add(new($@"ARP\{scope}\{arch}\{name}", display, key.GetValue("DisplayVersion") as string ?? "",
                key.GetValue("BundleVersion") as string ?? "", codes is { Length: 1 } ? codes[0].ToUpperInvariant() : "",
                provider, key.GetValue("Publisher") as string ?? ""));
        }
    }

    private static bool OtherCore(RegistryHive hive, RegistryView view, bool allowUserCore)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var core = root.OpenSubKey(@"Software\Python\PythonCore", writable: false);
        return core?.GetSubKeyNames().Any(x => (x == "3.13" || x.StartsWith("3.13-", StringComparison.Ordinal)) &&
            (!allowUserCore || x != "3.13")) == true;
    }

    private static (string Version, string Hash)? ReadExecutable(string path, string filename)
    {
        // Do not accept a redirected installation as proof of a stable copy.
        for (var entry = path; entry is not null && entry.Length > 3; entry = Path.GetDirectoryName(entry))
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return null;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 64 or > 16 * 1024 * 1024) return null;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt16() != 0x5A4D) return null;
        stream.Position = 0x3c;
        var offset = reader.ReadInt32();
        if (offset < 64 || offset > stream.Length - 6) return null;
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) return null;
        var version = FileVersionInfo.GetVersionInfo(path);
        if (version.CompanyName != Publisher || version.OriginalFilename != filename ||
            version.FileVersion is null || version.ProductVersion != version.FileVersion) return null;
        stream.Position = 0;
        return (version.FileVersion, Convert.ToHexString(SHA256.HashData(stream)));
    }
}

internal sealed record PythonBurnRegistration(string LocalId, string Name, string DisplayVersion,
    string BundleVersion, string UpgradeCode, string ProviderKey, string Publisher);
internal sealed record PythonBurnInventory(string AccountSid, IReadOnlyList<PythonBurnRegistration> Bundles,
    string InstallPath, string FeatureNames, string FeatureVersion, string ExecutableVersion, string ExecutableSha256,
    string WindowedExecutableVersion, string WindowedExecutableSha256, string UserPathSha256, bool PathConfigured,
    bool OtherCorePresent, string CrtVersion, string LauncherStateSha256);
