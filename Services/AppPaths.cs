namespace UpdateCenter.Services;

public static class AppPaths
{
    private static int _cleanupCompleted;

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateCenter");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "Logs");
    public static string UpdatesDirectory { get; } = Path.Combine(DataDirectory, "Updates");
    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");
    public static string HistoryFile { get; } = Path.Combine(DataDirectory, "history.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(UpdatesDirectory);
        if (Interlocked.Exchange(ref _cleanupCompleted, 1) == 0)
            CleanupOldFiles();
    }

    private static void CleanupOldFiles()
    {
        try
        {
            var staleTemporaryLimit = DateTime.UtcNow.AddDays(-1);
            foreach (var pattern in new[] { "update-plan-*.json", "update-status-*.json" })
            {
                foreach (var path in Directory.EnumerateFiles(DataDirectory, pattern, SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) < staleTemporaryLimit)
                            File.Delete(path);
                    }
                    catch { }
                }
            }

            var logLimit = DateTime.UtcNow.AddDays(-30);
            foreach (var path in Directory.EnumerateFiles(LogsDirectory, "UpdateCenter-*.log", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < logLimit)
                        File.Delete(path);
                }
                catch { }
            }

            foreach (var path in Directory.EnumerateFiles(UpdatesDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < staleTemporaryLimit)
                        File.Delete(path);
                }
                catch { }
            }
        }
        catch
        {
            // La pulizia non deve impedire l'avvio dell'applicazione.
        }
    }
}
