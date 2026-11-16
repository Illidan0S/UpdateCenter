using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal static class WinGetCopyEvidenceReader
{
    internal static WinGetCopyEvidence? Read(string localId)
    {
        var parts = localId.Split('\\');
        if (parts.Length != 4 || parts[0] != "ARP" || parts[1] is not ("User" or "Machine") ||
            parts[2] is not ("X64" or "X86" or "Arm64" or "Arm")) return null;
        try
        {
            using var root = RegistryKey.OpenBaseKey(parts[1] == "User" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                parts[2] is "X86" or "Arm" ? RegistryView.Registry32 : RegistryView.Registry64);
            using var entry = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + parts[3], false);
            var raw = entry?.GetValue("DisplayVersion") as string;
            if (!SoftwareVersionEvidence.IsValidVersion(raw)) return null;
            var basic = new WinGetCopyEvidence(raw!);
            if (entry?.GetValue("WindowsInstaller") is int msi && msi == 1)
            {
                var registration = MsiRegistrationReader.Read(localId, raw!);
                if (registration is null) return null;
                // Freeze the native registration alongside the ARP observation.
                // An HKLM uninstall key alone does not prove machine context.
                basic = basic with { MsiRegistration = registration };
            }
            var publisher = entry?.GetValue("Publisher") as string ?? "";
            var uninstall = entry?.GetValue("UninstallString") as string ?? "";
            // Only a quoted exact uninstall executable supplies a directory.
            // No shell parsing, PATH lookup, recursive search or installer API.
            if (!uninstall.StartsWith('"')) return basic;
            var end = uninstall.IndexOf('"', 1);
            if (end < 2) return basic;
            var uninstaller = uninstall[1..end];
            var directory = Path.GetDirectoryName(uninstaller);
            if (directory is null || !SafePath(uninstaller) || !Directory.Exists(directory)) return basic;
            var uninstallVersion = FileVersionInfo.GetVersionInfo(uninstaller);
            if (string.IsNullOrWhiteSpace(uninstallVersion.ProductName) || uninstallVersion.FileVersion != raw ||
                NormalizePublisher(uninstallVersion.CompanyName) != NormalizePublisher(publisher)) return basic;
            var paths = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly).Take(33).ToArray();
            if (paths.Length > 32) return basic;
            var candidates = new List<WinGetCopyEvidence>();
            foreach (var path in paths)
            {
                if (path.Equals(uninstaller, StringComparison.OrdinalIgnoreCase) || !SafePath(path)) continue;
                var version = FileVersionInfo.GetVersionInfo(path);
                if (version.FileVersion != raw || string.IsNullOrWhiteSpace(version.ProductName) ||
                    !Path.GetFileNameWithoutExtension(path).Equals(version.ProductName, StringComparison.OrdinalIgnoreCase) ||
                    version.ProductName != uninstallVersion.ProductName || version.CompanyName != uninstallVersion.CompanyName ||
                    !SoftwareVersionEvidence.IsValidVersion(version.ProductVersion) ||
                    NormalizePublisher(version.CompanyName) != NormalizePublisher(publisher) || NormalizePublisher(publisher).Length == 0) continue;
                using var file = File.OpenRead(path);
                candidates.Add(basic with { ExecutablePath = Path.GetFullPath(path), ProductName = version.ProductName,
                    CompanyName = version.CompanyName!, ProductVersion = version.ProductVersion!,
                    ExecutableSha256 = Convert.ToHexString(SHA256.HashData(file)) });
            }
            return candidates.Count == 1 ? candidates[0] : basic;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { return null; }
    }

    private static string NormalizePublisher(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool SafePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    internal static bool Valid(WinGetCopyEvidence evidence) => SoftwareVersionEvidence.IsValidVersion(evidence.DisplayVersion) &&
        (evidence.MsiRegistration is null || MsiRegistrationReader.Valid(evidence.MsiRegistration, evidence.DisplayVersion)) &&
        (evidence.ExecutablePath.Length == 0
            ? evidence.ProductName.Length == 0 && evidence.CompanyName.Length == 0 && evidence.ProductVersion.Length == 0 && evidence.ExecutableSha256.Length == 0
            : Path.IsPathFullyQualified(evidence.ExecutablePath) && evidence.ExecutablePath.Length <= 1024 &&
              !evidence.ExecutablePath.Any(char.IsControl) && Path.GetExtension(evidence.ExecutablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
              !string.IsNullOrWhiteSpace(evidence.ProductName) && !string.IsNullOrWhiteSpace(evidence.CompanyName) &&
              Path.GetFileNameWithoutExtension(evidence.ExecutablePath).Equals(evidence.ProductName, StringComparison.OrdinalIgnoreCase) &&
              SoftwareVersionEvidence.IsValidVersion(evidence.ProductVersion) &&
              System.Text.RegularExpressions.Regex.IsMatch(evidence.ExecutableSha256, "^[A-F0-9]{64}$"));

    internal static bool SameExecutable(WinGetCopyEvidence before, WinGetCopyEvidence after) =>
        before.ExecutablePath.Length > 0 && before.ExecutablePath.Equals(after.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
        before.CompanyName == after.CompanyName && before.ProductName == after.ProductName;
}
