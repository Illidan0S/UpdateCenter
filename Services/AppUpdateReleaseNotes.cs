namespace UpdateCenter.Services;

internal static class AppUpdateReleaseNotes
{
    internal static string ToPlainText(string? body, string language)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        var lines = body.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, line => LanguageHeading(line) == language);
        if (start < 0)
            start = Array.FindIndex(lines, line => LanguageHeading(line) is not null);
        var end = start < 0 ? lines.Length : Array.FindIndex(lines, start + 1, line => LanguageHeading(line) is not null);
        if (end < 0) end = lines.Length;

        var notes = new List<string>();
        var length = 0;
        for (var index = start + 1; index < end; index++)
        {
            var line = lines[index].Trim();
            if (line is "---" or "***") continue;
            line = line.Replace("**", "", StringComparison.Ordinal);
            if (line.StartsWith('#')) line = line.TrimStart('#').TrimStart();
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                line = "•  " + line[2..];
            if (length + line.Length >= 6000)
            {
                notes.Add(line[..Math.Max(0, 6000 - length)] + "…");
                break;
            }
            notes.Add(line);
            length += line.Length + 1;
        }
        return string.Join('\n', notes).Trim();
    }

    private static string? LanguageHeading(string line)
    {
        line = line.Trim();
        if (!line.StartsWith('#')) return null;
        return line.Trim('#').Trim().ToLowerInvariant() switch
        {
            "italiano" or "it" => "it",
            "english" or "en" => "en",
            _ => null
        };
    }
}
