namespace UpdateCenter.Services;

public static class AppPaths
{
    private static int _cleanupCompleted;
    internal static bool UpdateStartupPending { get; set; }

    private static readonly object RootGate = new();
    private static string? _dataDirectory;

    internal static string ResolveDefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateCenter");

    internal static string InitializeRoot(string? root = null)
    {
        lock (RootGate)
        {
            if (root is null) return _dataDirectory ??= ResolveDefaultRoot();
            if (_dataDirectory is not null)
                throw new InvalidOperationException("The application data root has already been initialized.");
            if (!Path.IsPathFullyQualified(root))
                throw new ArgumentException("The application data root must be absolute.", nameof(root));
            return _dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
    }

    public static string DataDirectory => InitializeRoot();
    public static string LogsDirectory => Path.Combine(DataDirectory, "Logs");
    public static string UpdatesDirectory => Path.Combine(DataDirectory, "Updates");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string HistoryFile => Path.Combine(DataDirectory, "history.json");
    public static string WinGetApplicabilityFile => Path.Combine(DataDirectory, "winget-not-applicable.json");

    public static void EnsureCreated()
    {
        if (UpdateStartupPending) return;
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
            foreach (var pattern in new[]
                     { "update-plan-*.json", "update-status-*.json", "update-pause-*.signal", "update-pause-*.signal.tmp",
                       "driver-repair-*.json", "driver-repair-status-*.json" })
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
