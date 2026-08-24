namespace UpdateCenter.Services;

public static class LogService
{
    private static readonly object Gate = new();
    private const long MaximumDailyLogSize = 2L * 1024 * 1024;

    public static void WriteEvent(
        string operation,
        string phase,
        string outcome,
        string? itemId = null,
        int? resultCode = null,
        string? details = null,
        Exception? exception = null)
    {
        var fields = new List<string>
        {
            $"operation={operation}",
            $"phase={phase}",
            $"outcome={outcome}"
        };
        if (!string.IsNullOrWhiteSpace(itemId)) fields.Add($"item={itemId}");
        if (resultCode.HasValue) fields.Add($"code={resultCode.Value} (0x{resultCode.Value:X8})");
        if (!string.IsNullOrWhiteSpace(details))
            fields.Add($"details={details.Replace(Environment.NewLine, " ")}");
        Write(string.Join(" | ", fields), exception);
    }

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
