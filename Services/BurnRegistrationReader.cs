using Microsoft.Win32;

namespace UpdateCenter.Services;

internal static class BurnRegistrationReader
{
    // The approved machine bundle must be the only member of this family visible
    // to the current account. HKCU's uninstall key is shared by both registry views.
    internal static bool HasUniqueRegistration(string family, string localId)
    {
        if (!Guid.TryParseExact(family, "B", out var expected)) return false;
        var found = false;
        try
        {
            foreach (var (hive, view, prefix) in new[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"ARP\Machine\X64\"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"ARP\Machine\X86\"),
                (RegistryHive.CurrentUser, RegistryView.Default, @"ARP\User\X64\")
            })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key is null) return false;
                    var value = key.GetValue("BundleUpgradeCode");
                    bool ExpectedFamily(string code) => Guid.TryParse(code, out var parsed) && parsed == expected;
                    if (value is string single && ExpectedFamily(single)) return false;
                    if (value is not string[] codes || !codes.Any(ExpectedFamily)) continue;
                    if (found || codes.Length != 1 || !string.Equals(prefix + name, localId, StringComparison.OrdinalIgnoreCase) ||
                        !Guid.TryParseExact(name, "B", out var product) ||
                        !Guid.TryParseExact(key.GetValue("BundleProviderKey") as string, "B", out var provider) || provider != product ||
                        key.GetValue("WindowsInstaller") is int msi && msi != 0) return false;
                    found = true;
                }
            }
            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return false; }
    }
}
