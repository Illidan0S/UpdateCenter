namespace UpdateCenter.Models;

public readonly record struct SemanticVersion(int Major, int Minor, int Patch) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        // Le Release accettate sono esclusivamente stabili e usano MAJOR.MINOR.PATCH.
        if (normalized.Contains('-') || normalized.Contains('+')) return false;
        var parts = normalized.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch) ||
            major < 0 || minor < 0 || patch < 0)
            return false;

        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
}

public sealed class AppUpdateInfo
{
    public required SemanticVersion InstalledVersion { get; init; }
    public required SemanticVersion AvailableVersion { get; init; }
    public required string ReleaseNotes { get; init; }
    public required long DownloadSize { get; init; }
    public required Uri DownloadUri { get; init; }
    public required Uri Sha256Uri { get; init; }
    public required string AssetName { get; init; }
    public required Uri ReleasePageUri { get; init; }
    public string? ApiSha256 { get; init; }

    public string InstalledVersionLabel => $"v{InstalledVersion}";
    public string AvailableVersionLabel => $"v{AvailableVersion}";
    public string DownloadSizeLabel => FormatBytes(DownloadSize);

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "Dimensione non disponibile";
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}

public sealed record AppUpdateDownloadProgress(double Percentage, string Message);
