using System.Diagnostics;
using System.Text;

namespace UpdateCenter.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        bool createWindow = false)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = !createWindow,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Impossibile avviare {fileName}.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts?.Token ?? CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                if (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
                    throw new TimeoutException($"{fileName} non ha risposto entro il tempo previsto.");
                throw;
            }

            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"{fileName} non è disponibile sul computer.", ex);
        }
    }
}
