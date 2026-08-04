using Microsoft.Win32;

namespace UpdateCenter.Services;

internal static class WinGetInstalledScopeService
{
    public static string Detect(string displayName, string installedVersion)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(displayName)) return "";
        var machine = Matches(RegistryHive.LocalMachine, RegistryView.Registry64, displayName, installedVersion) ||
                      Matches(RegistryHive.LocalMachine, RegistryView.Registry32, displayName, installedVersion);
        var user = Matches(RegistryHive.CurrentUser, RegistryView.Default, displayName, installedVersion);
        return machine == user ? "" : machine ? "machine" : "user";
    }

    private static bool Matches(
        RegistryHive hive,
        RegistryView view,
        string displayName,
        string installedVersion)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) return false;
            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(subKeyName);
                var name = entry?.GetValue("DisplayName") as string;
                if (!string.Equals(name?.Trim(), displayName.Trim(), StringComparison.CurrentCultureIgnoreCase)) continue;
                var version = entry?.GetValue("DisplayVersion") as string;
                if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(version) ||
                    version.Trim().Equals(installedVersion.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex)
        {
            LogService.Write($"Lettura ambito WinGet non riuscita per {hive}/{view}.", ex);
        }
        return false;
    }
}
