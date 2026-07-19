namespace UpdateCenter.Services;

public static class LogService
{
    private static readonly object Gate = new();
    private const long MaximumDailyLogSize = 2L * 1024 * 1024;

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            AppPaths.EnsureCreated();
            var path = Path.Combine(AppPaths.LogsDirectory, $"UpdateCenter-{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"{DateTime.Now:O}  {message}";
            if (exception is not null)
                line += $"{Environment.NewLine}{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}";

            lock (Gate)
            {
                if (File.Exists(path) && new FileInfo(path).Length >= MaximumDailyLogSize)
                    return;
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Il logging non deve mai interrompere scansione o aggiornamenti.
        }
    }
}
