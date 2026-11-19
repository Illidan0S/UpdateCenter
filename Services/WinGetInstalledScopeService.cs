using Microsoft.Win32;

namespace UpdateCenter.Services;

internal static class WinGetInstalledScopeService
{
    internal static bool? RegistrationExists(string localId)
    {
        var parts = localId.Split('\\');
        if (parts.Length != 4 || parts[0] != "ARP" || parts[1] is not ("User" or "Machine") ||
            parts[2] is not ("X64" or "X86" or "Arm64" or "Arm") ||
            string.IsNullOrWhiteSpace(parts[3]) || parts[3].Any(char.IsControl)) return null;
        try
        {
            using var root = RegistryKey.OpenBaseKey(parts[1] == "User" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                parts[2] is "X86" or "Arm" ? RegistryView.Registry32 : RegistryView.Registry64);
            using var entry = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + parts[3], false);
            return entry is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return null; }
    }

    public static string Detect(string displayName, string installedVersion)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(displayName)) return "";
        var entries = ReadEntries(RegistryHive.LocalMachine, RegistryView.Registry64, "machine", displayName)
            .Concat(ReadEntries(RegistryHive.LocalMachine, RegistryView.Registry32, "machine", displayName))
            .Concat(ReadEntries(RegistryHive.CurrentUser, RegistryView.Default, "user", displayName));
        return ResolveScope(entries, installedVersion);
    }

    // WinGet's ARP LocalId identifies the hive, registry view and exact key.
    // This is read-only; it never invokes Windows Installer or repairs a product.
    internal static string? ReadDisplayVersion(string localId)
    {
        var parts = localId.Split('\\');
        if (parts.Length != 4 || parts[0] != "ARP" || parts[1] is not ("User" or "Machine") ||
            parts[2] is not ("X64" or "X86" or "Arm64" or "Arm") ||
            string.IsNullOrWhiteSpace(parts[3]) || parts[3].Any(char.IsControl)) return null;
        try
        {
            using var root = RegistryKey.OpenBaseKey(parts[1] == "User" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                parts[2] is "X86" or "Arm" ? RegistryView.Registry32 : RegistryView.Registry64);
            using var entry = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + parts[3], writable: false);
            return entry?.GetValue("DisplayVersion") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return null; }
    }

    // Registry entries are only name/version evidence, never a WinGet package identity.
    internal static string ResolveScope(IEnumerable<InstalledScopeEntry> entries, string installedVersion)
    {
        // Match WinGet's ARP visibility rule: hidden bundle components are not
        // separate installed copies. The reader accepts only nonzero DWORDs.
        var candidates = entries.Where(x => !x.IsSystemComponent).ToList();
        // Count before version filtering: another installed version is still another
        // copy that a name/version hint cannot bind to the WinGet execution target.
        if (candidates.Count > 1) return "ambiguous";
        if (candidates.Count == 0 || string.IsNullOrWhiteSpace(installedVersion)) return "";
        var candidate = candidates[0];
        return candidate.Scope is "user" or "machine" && !string.IsNullOrWhiteSpace(candidate.Version) &&
            candidate.Version.Trim().Equals(installedVersion.Trim(), StringComparison.OrdinalIgnoreCase)
            ? candidate.Scope : "";
    }

    private static IReadOnlyList<InstalledScopeEntry> ReadEntries(
        RegistryHive hive,
        RegistryView view,
        string scope,
        string displayName)
    {
        var entries = new List<InstalledScopeEntry>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) return entries;
            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(subKeyName);
                var name = entry?.GetValue("DisplayName") as string;
                if (!string.Equals(name?.Trim(), displayName.Trim(), StringComparison.CurrentCultureIgnoreCase)) continue;
                var version = entry?.GetValue("DisplayVersion") as string;
                entries.Add(new(scope, version ?? "", entry?.GetValue("SystemComponent") is int component && component != 0));
            }
        }
        catch (Exception ex)
        {
            LogService.Write($"Lettura ambito WinGet non riuscita per {hive}/{view}.", ex);
        }
        return entries;
    }
}

internal sealed record InstalledScopeEntry(string Scope, string Version, bool IsSystemComponent = false);
