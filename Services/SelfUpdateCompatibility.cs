using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UpdateCenter.Services;

internal static class SelfUpdateCompatibility
{
    internal static bool IsSupported()
    {
        if (RuntimeInformation.OSArchitecture != Architecture.X64 || !OperatingSystem.IsWindowsVersionAtLeast(10))
            return false;

        var version = Environment.OSVersion.Version;
        if (version.Major != 10 || version.Minor != 0) return false;
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var windows = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return GetProductInfo(version.Major, version.Minor, 0, 0, out var sku) &&
                Supports(version.Build, sku, RuntimeInformation.OSArchitecture,
                    string.Equals(windows?.GetValue("InstallationType") as string, "Client", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    // Admission policy, not a claim of validation on every Windows release/flight.
    // Allow newer Windows 11 desktop builds (including Insider); retain the exact
    // LTSC/IoT exceptions and reject unknown editions and non-client architectures.
    // Keep in sync with IsSupportedWindows in installer.iss.
    internal static bool Supports(int build, int sku, Architecture architecture, bool client)
    {
        if (!client || architecture != Architecture.X64) return false;
        return build switch
        {
            17763 => sku is 125 or 126,
            19044 => sku is 125 or 126 or 191,
            26100 => sku is 4 or 27 or 121 or 122 or 125 or 126 or 188 or 191,
            >= 26200 => sku is 4 or 27 or 48 or 49 or 99 or 100 or 101 or 121 or 122 or 161 or 162 or 164 or 165 ||
                build == 26200 && sku == 188,
            _ => false
        };
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProductInfo(int major, int minor, int servicePackMajor, int servicePackMinor, out int sku);
}
