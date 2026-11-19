namespace UpdateCenter.Models;

public enum SoftwareVersionRelation { NotComparable, Less, Equal, Greater }

// Only compare values in the same version domain. In particular an ARP
// DisplayVersion is not implicitly a catalog PackageVersion.
internal static class SoftwareVersionEvidence
{
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown", "latest", "unavailable", "not available", "none", "null", "na", "n/a",
        "sconosciuto", "sconosciuta", "più recente", "non disponibile", "nessuna"
    };

    internal static bool IsValidVersion(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value == value.Trim() &&
        value.Any(char.IsLetterOrDigit) && !value.Any(char.IsControl) &&
        value.IndexOfAny(['<', '>', '…', '*', '?']) < 0 &&
        !value.EndsWith("...", StringComparison.Ordinal) && !Placeholders.Contains(value);

    internal static SoftwareVersionRelation Compare(string? installed, string? target)
    {
        if (!IsValidVersion(installed) || !IsValidVersion(target)) return SoftwareVersionRelation.NotComparable;
        if (string.Equals(installed, target, StringComparison.Ordinal)) return SoftwareVersionRelation.Equal;
        var left = installed!.Split('.');
        var right = target!.Split('.');
        static bool Numeric(string[] parts) => parts.All(p => p.Length > 0 && p.All(c => c is >= '0' and <= '9'));
        if (!Numeric(left) || !Numeric(right)) return SoftwareVersionRelation.NotComparable;
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var a = i < left.Length ? left[i].TrimStart('0') : "";
            var b = i < right.Length ? right[i].TrimStart('0') : "";
            var relation = a.Length.CompareTo(b.Length);
            if (relation == 0) relation = string.CompareOrdinal(a, b);
            if (relation < 0) return SoftwareVersionRelation.Less;
            if (relation > 0) return SoftwareVersionRelation.Greater;
        }
        return SoftwareVersionRelation.Equal;
    }
}
