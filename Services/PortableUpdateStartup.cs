using System.Diagnostics;
using System.IO.Pipes;

namespace UpdateCenter.Services;

internal static class PortableUpdateStartup
{
    private const string Prefix = "UpdateCenter.Startup.";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    internal static string CreateName() => Prefix + Guid.NewGuid().ToString("N");

    internal static bool IsValidName(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(name.AsSpan(Prefix.Length), "N", out _);

    internal static async Task ConfirmAsync(NamedPipeServerStream pipe, Process child, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        var token = deadline.Token;
        var ready = ReadReadyAsync();
        var exit = Task.CompletedTask;
        try
        {
            exit = child.WaitForExitAsync(token);
            if (await Task.WhenAny(ready, exit).ConfigureAwait(false) == exit)
            {
                await exit.ConfigureAwait(false);
                throw new InvalidOperationException("La nuova versione è terminata prima di inizializzare l'interfaccia.");
            }

            await ready.ConfigureAwait(false);
            if (child.HasExited)
                throw new InvalidOperationException("La nuova versione è terminata durante il riavvio.");
            await RunnerConsentChannel.WriteAsync(pipe, "commit", token).ConfigureAwait(false);
        }
        catch (Exception startupError)
        {
            // Only stop the process this updater launched. If it cannot be stopped,
            // leave the backup and avoid replacing an executable still in use.
            try
            {
                if (!child.HasExited)
                {
                    try { child.Kill(); }
                    catch (InvalidOperationException) when (child.HasExited) { }
                    if (!child.WaitForExit(5000)) throw new TimeoutException();
                }
            }
            catch (Exception stopError)
            {
                throw new UpdateProcessStillRunningException(new AggregateException(startupError, stopError));
            }
            throw;
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            try { await ready.ConfigureAwait(false); } catch { /* The failure has already been reported. */ }
            try { await exit.ConfigureAwait(false); } catch { /* Do not change the startup outcome during cleanup. */ }
        }

        async Task ReadReadyAsync()
        {
            await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
            if (RunnerConsentChannel.PeerId(pipe, server: true) != child.Id ||
                await RunnerConsentChannel.ReadAsync(pipe, token).ConfigureAwait(false) != "ready")
                throw new UnauthorizedAccessException("Conferma di avvio non valida.");
        }
    }

    internal static async Task SignalWindowRenderedAsync(string name, int supervisorId)
    {
        using var deadline = new CancellationTokenSource(StartupTimeout);
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        if (RunnerConsentChannel.PeerId(pipe, server: false) != supervisorId)
            throw new UnauthorizedAccessException("Identità del processo di aggiornamento non valida.");
        await RunnerConsentChannel.WriteAsync(pipe, "ready", deadline.Token).ConfigureAwait(false);
        if (await RunnerConsentChannel.ReadAsync(pipe, deadline.Token).ConfigureAwait(false) != "commit")
            throw new InvalidDataException("Conferma dell'aggiornamento non valida.");
    }

    internal static void WaitForWindow(string targetPath, string backupPath, string updaterPath, string version)
    {
        var name = CreateName();
        using var pipe = RunnerConsentChannel.Create(name);
        var start = new ProcessStartInfo(targetPath)
        {
            WorkingDirectory = Path.GetDirectoryName(targetPath)!,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "--update-complete", backupPath, updaterPath, version, name,
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);

        using var child = Process.Start(start) ?? throw new InvalidOperationException("La nuova versione non è stata riavviata.");
        ConfirmAsync(pipe, child, StartupTimeout).GetAwaiter().GetResult();
    }
}
