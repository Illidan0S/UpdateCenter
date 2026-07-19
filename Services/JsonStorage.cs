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
        return Read<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
    }

    public static void SaveSettings(AppSettings settings) => WriteAtomic(AppPaths.SettingsFile, settings);

    public static List<HistoryEntry> LoadHistory()
    {
        AppPaths.EnsureCreated();
        return Read<List<HistoryEntry>>(AppPaths.HistoryFile) ?? [];
    }

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
