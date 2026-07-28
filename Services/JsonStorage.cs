using System.Text.Json;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class JsonStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings LoadSettings()
    {
        AppPaths.EnsureCreated();
        var settings = Read<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
        if (settings.ApplyMigrations())
            SaveSettings(settings);
        return settings;
    }

    public static void SaveSettings(AppSettings settings) => WriteAtomic(AppPaths.SettingsFile, settings);

    public static List<HistoryEntry> LoadHistory()
    {
        AppPaths.EnsureCreated();
        var history = Read<List<HistoryEntry>>(AppPaths.HistoryFile) ?? [];
        var repaired = false;
        foreach (var entry in history)
        {
            repaired |= RepairLegacyEncoding(entry);
        }
        if (repaired) SaveHistory(history);
        return history;
    }

    private static bool RepairLegacyEncoding(HistoryEntry entry)
    {
        var original = string.Join('\u001f', entry.Name, entry.Kind, entry.Result, entry.Details, entry.Diagnostics);
        entry.Name = RepairLegacyEncoding(entry.Name);
        entry.Kind = RepairLegacyEncoding(entry.Kind);
        entry.Result = RepairLegacyEncoding(entry.Result);
        entry.Details = RepairLegacyEncoding(entry.Details);
        entry.Diagnostics = RepairLegacyEncoding(entry.Diagnostics);
        return original != string.Join('\u001f', entry.Name, entry.Kind, entry.Result, entry.Details, entry.Diagnostics);
    }

    internal static string RepairLegacyEncoding(string value) => value
        .Replace("ÃƒÂ¨", "è", StringComparison.Ordinal)
        .Replace("ÃƒÂ©", "é", StringComparison.Ordinal)
        .Replace("ÃƒÂ¹", "ù", StringComparison.Ordinal)
        .Replace("ÃƒÂ ", "à", StringComparison.Ordinal)
        .Replace("ÃƒÂ²", "ò", StringComparison.Ordinal)
        .Replace("ÃƒÂ¬", "ì", StringComparison.Ordinal)
        .Replace("Ã¨", "è", StringComparison.Ordinal)
        .Replace("Ã©", "é", StringComparison.Ordinal)
        .Replace("Ã¹", "ù", StringComparison.Ordinal)
        .Replace("Ã ", "à", StringComparison.Ordinal)
        .Replace("Ã²", "ò", StringComparison.Ordinal)
        .Replace("Ã¬", "ì", StringComparison.Ordinal);

    public static void SaveHistory(IEnumerable<HistoryEntry> entries) =>
        WriteAtomic(AppPaths.HistoryFile, entries.OrderByDescending(x => x.Timestamp).Take(500).ToList());

    public static T? Read<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch
        {
            return default;
        }
    }

    public static void WriteAtomic<T>(string path, T value)
    {
        AppPaths.EnsureCreated();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        File.Move(temporary, path, true);
    }
}
