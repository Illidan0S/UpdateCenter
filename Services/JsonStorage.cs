using System.Text.Json;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class JsonStorage
{
    private const int AtomicWriteAttempts = 3;
    private static readonly TimeSpan AtomicWriteRetryDelay = TimeSpan.FromMilliseconds(25);
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
        for (var attempt = 1; attempt <= AtomicWriteAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<T>(stream, Options);
            }
            catch (Exception ex) when (IsTransientReadFailure(ex) && attempt < AtomicWriteAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(5));
            }
            catch
            {
                return default;
            }
        }
        return default;
    }

    public static void WriteAtomic<T>(string path, T value)
        => WriteAtomic(path, value, "json-storage", new AtomicFileOperations(), Thread.Sleep);

    internal static void WriteAtomic<T>(
        string path,
        T value,
        string diagnosticStage,
        IAtomicFileOperations operations,
        Action<TimeSpan> retryDelay)
    {
        AppPaths.EnsureCreated();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(value, Options);
        try
        {
            operations.WriteAndFlush(temporary, json);
            for (var attempt = 1; attempt <= AtomicWriteAttempts; attempt++)
            {
                try
                {
                    operations.MoveReplace(temporary, path);
                    if (attempt > 1)
                        LogAtomicWrite(diagnosticStage, path, attempt, "retry-succeeded", null);
                    return;
                }
                catch (Exception ex) when (IsTransientReplaceFailure(ex) && attempt < AtomicWriteAttempts)
                {
                    LogAtomicWrite(diagnosticStage, path, attempt, "retrying", ex);
                    retryDelay(AtomicWriteRetryDelay);
                }
                catch (Exception ex)
                {
                    LogAtomicWrite(diagnosticStage, path, attempt, "failed", ex);
                    throw new AtomicWriteException(
                        path,
                        attempt,
                        IsTransientReplaceFailure(ex),
                        $"Pubblicazione atomica non riuscita per {Path.GetFileName(path)}.",
                        ex);
                }
            }
        }
        finally
        {
            try { operations.DeleteIfExists(temporary); } catch { }
        }
    }

    private static bool IsTransientReplaceFailure(Exception exception)
    {
        if (exception is not IOException)
            return false;
        var win32 = exception.HResult & 0xFFFF;
        return win32 is 32 or 33;
    }

    private static bool IsTransientReadFailure(Exception exception)
    {
        if (exception is FileNotFoundException or DirectoryNotFoundException or JsonException)
            return true;
        if (exception is not IOException)
            return false;
        var win32 = exception.HResult & 0xFFFF;
        return win32 is 2 or 3 or 32 or 33;
    }

    private static void LogAtomicWrite(
        string stage,
        string path,
        int attempt,
        string outcome,
        Exception? exception)
    {
        var hresult = exception?.HResult ?? 0;
        LogService.WriteEvent(
            "status-channel",
            stage,
            outcome,
            resultCode: exception is null ? null : hresult,
            details: $"path={path}; exception={exception?.GetType().Name ?? "none"}; " +
                     $"hresult=0x{hresult:X8}; win32={hresult & 0xFFFF}; attempt={attempt}; outcome={outcome}",
            exception: exception);
    }
}

internal interface IAtomicFileOperations
{
    void WriteAndFlush(string path, string contents);
    void MoveReplace(string source, string destination);
    void DeleteIfExists(string path);
}

internal sealed class AtomicFileOperations : IAtomicFileOperations
{
    public void WriteAndFlush(string path, string contents)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream);
        writer.Write(contents);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    public void MoveReplace(string source, string destination)
    {
        if (File.Exists(destination))
            File.Replace(source, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(source, destination);
    }

    public void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

internal sealed class AtomicWriteException : IOException
{
    public AtomicWriteException(
        string path,
        int attempts,
        bool transient,
        string message,
        Exception innerException) : base(message, innerException)
    {
        Path = path;
        Attempts = attempts;
        WasTransient = transient;
        HResult = innerException.HResult;
    }

    public string Path { get; }
    public int Attempts { get; }
    public bool WasTransient { get; }
}
