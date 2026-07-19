namespace UpdateCenter.Models;

public static class DriverVersionComparer
{
    public static int Compare(string? left, string? right)
    {
        var leftParts = Parse(left);
        var rightParts = Parse(right);
        var length = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < length; index++)
        {
            var leftPart = index < leftParts.Length ? leftParts[index] : 0;
            var rightPart = index < rightParts.Length ? rightParts[index] : 0;
            var comparison = leftPart.CompareTo(rightPart);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static long[] Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var cleaned = value.Trim().TrimStart('v', 'V');
        var parts = cleaned.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<long>(parts.Length);
        foreach (var part in parts)
        {
            var digits = new string(part.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0 || !long.TryParse(digits, out var number)) return [];
            result.Add(number);
        }
        return result.ToArray();
    }
}
