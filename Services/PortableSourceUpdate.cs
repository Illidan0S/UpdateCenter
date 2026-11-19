using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

// The running release owns the transaction. Its verified successor acknowledges the first rendered window.
internal static class PortableSourceUpdate
{
    internal const string Mode = "--apply-authorized-portable";
    internal static string HelperPath(Guid session) =>
        Path.Combine(AppPaths.UpdatesDirectory, $"UpdateCenter-recovery-{session:N}.exe");
    private static string PipeName(Guid session) => $"UpdateCenter.Recovery.{session:N}";
    private static string GetCandidatePath(SemanticVersion version)
    {
        if (ReleasePublisherVerifier.UnsignedSourceVersion is not { } current || version <= current ||
            version == ReleasePublisherVerifier.WithdrawnVersion)
            throw new InvalidDataException("Versione Portable non ammessa.");
        return Path.Combine(AppPaths.UpdatesDirectory, $"UpdateCenter-v{version}-Portable.exe");
    }

    internal static async Task StartAsync(string target, SemanticVersion incoming, AppUpdateService releases, CancellationToken token)
    {
        EnsureExecutionContext();
        _ = GetCandidatePath(incoming);
        var session = Guid.NewGuid();
        var helperPath = HelperPath(session);
        using var paths = GuardPaths(target);
        using var original = File.OpenRead(target);
        await releases.VerifyOfficialAssetAsync(ReleasePublisherVerifier.UnsignedSourceVersion!.Value.ToString(), false,
            target, original, token).ConfigureAwait(false);
        original.Position = 0;
        using (var copy = new FileStream(helperPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await original.CopyToAsync(copy, token).ConfigureAwait(false);
        // Reopen after the writer closes and keep this handle until the verified helper is live.
        using var locked = File.OpenRead(helperPath);
        VerifyIdentical(original, locked);
        using var server = RunnerConsentChannel.Create(PipeName(session));
        var start = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false, WorkingDirectory = AppPaths.UpdatesDirectory, CreateNoWindow = true
        };
        start.ArgumentList.Add(Mode);
        start.ArgumentList.Add(session.ToString("N"));
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        using var helper = Process.Start(start) ?? throw new IOException("Impossibile avviare il recupero.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        // The UI remains open if the helper cannot authenticate its inputs or exits before it is ready.
        var ready = AwaitHelperAsync();
        var exited = helper.WaitForExitAsync(deadline.Token);
        try
        {
            if (await Task.WhenAny(ready, exited).ConfigureAwait(false) == exited)
                throw new IOException("Il programma di recupero è terminato prima della verifica.");
            await ready.ConfigureAwait(false);
            await RunnerConsentChannel.WriteAsync(server, "commit", deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            try { await ready.ConfigureAwait(false); } catch { }
            try { await exited.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        async Task AwaitHelperAsync()
        {
            await server.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
            if (RunnerConsentChannel.PeerId(server, true) != helper.Id)
                throw new UnauthorizedAccessException("Identità del processo di recupero non valida.");
            await RunnerConsentChannel.WriteAsync(server, incoming.ToString(), deadline.Token).ConfigureAwait(false);
            if (await RunnerConsentChannel.ReadAsync(server, deadline.Token, TimeSpan.FromMinutes(3)).ConfigureAwait(false) != "ready")
                throw new UnauthorizedAccessException("Il recupero non è stato confermato dal processo previsto.");
        }
    }

    internal static async Task ApplyAsync(Guid session, int ownerId)
    {
        EnsureExecutionContext();
        if (session == Guid.Empty || ownerId <= 0 || ownerId == Environment.ProcessId ||
            !string.Equals(Environment.ProcessPath, HelperPath(session), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Percorso del programma di recupero non valido.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = deadline.Token;
        using var pipe = new NamedPipeClientStream(".", PipeName(session), PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(10000, token).ConfigureAwait(false);
        if (RunnerConsentChannel.PeerId(pipe, false) != ownerId)
            throw new UnauthorizedAccessException("Identità del processo di origine non valida.");
        using var owner = Process.GetProcessById(ownerId);
        _ = owner.SafeHandle; // Retain its identity across the handshake and shutdown.
        if (owner.SessionId != Process.GetCurrentProcess().SessionId || owner.HasExited)
            throw new UnauthorizedAccessException("Sessione del processo di origine non valida.");
        var target = owner.MainModule?.FileName ?? throw new IOException("Percorso del processo di origine non disponibile.");
        var selected = await RunnerConsentChannel.ReadAsync(pipe, token).ConfigureAwait(false);
        if (!SemanticVersion.TryParse(selected, out var incoming) || selected != incoming.ToString())
            throw new InvalidDataException("Versione Portable non valida.");
        await VerifyAndApplyAsync(target, incoming, session, new AppUpdateService(), async () =>
        {
            await RunnerConsentChannel.WriteAsync(pipe, "ready", token).ConfigureAwait(false);
            if (await RunnerConsentChannel.ReadAsync(pipe, token).ConfigureAwait(false) != "commit")
                throw new UnauthorizedAccessException("Conferma del processo di origine non valida.");
            await owner.WaitForExitAsync(token).ConfigureAwait(false);
        }, PortableUpdateStartup.WaitForWindow, token).ConfigureAwait(false);
    }

    internal static async Task VerifyAndApplyAsync(string target, SemanticVersion incoming, Guid session, AppUpdateService releases,
        Func<Task> awaitOwnerExit, Action<string, string, string, string> confirm, CancellationToken token,
        Action<string>? launchRestored = null)
    {
        var candidatePath = GetCandidatePath(incoming);
        using var paths = GuardPaths(target);
        using var helper = File.OpenRead(HelperPath(session));
        using var candidate = File.OpenRead(candidatePath);
        RejectReparse(HelperPath(session));
        RejectReparse(candidatePath);
        byte[] originalHash;
        using (var original = File.OpenRead(target))
        {
            RejectReparse(target);
            originalHash = VerifyIdentical(original, helper);
            if (!await ReleasePublisherVerifier.VerifyFromUnsignedSourceAsync(target, original, candidatePath, candidate,
                incoming, false, releases, token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("L'autorizzazione all'aggiornamento senza firma non è più disponibile.");
            await awaitOwnerExit().ConfigureAwait(false);
        }
        var startupAttempted = false;
        try
        {
            AppUpdateService.ReplacePortable(target, candidatePath, candidate, originalHash, incoming.ToString(),
                (path, backup, updater, version) =>
                {
                    startupAttempted = true;
                    confirm(path, backup, updater, version);
                });
        }
        catch (Exception ex) when (startupAttempted && ex is not UpdateProcessStillRunningException)
        {
            AppUpdateService.RestartVerifiedOriginal(target, originalHash, launchRestored);
            throw;
        }
    }

    private static byte[] VerifyIdentical(Stream original, Stream helper)
    {
        original.Position = 0;
        helper.Position = 0;
        var hash = SHA256.HashData(original);
        if (!CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(helper)))
            throw new InvalidDataException("La copia di recupero non corrisponde all'eseguibile in uso.");
        return hash;
    }

    private static void EnsureExecutionContext()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (ReleasePublisherVerifier.UnsignedSourceVersion is null || !SelfUpdateCompatibility.IsSupported() ||
            new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Per questo recupero avvia il Portable dal tuo account senza elevazione.");
    }

    private static DirectoryLocks GuardPaths(string target)
    {
        var staging = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppPaths.UpdatesDirectory));
        if (!Path.IsPathFullyQualified(target) || !string.Equals(Path.GetFullPath(target), target, StringComparison.OrdinalIgnoreCase) ||
            !AppUpdateService.IsSupportedUpdateTargetName(Path.GetFileName(target)) ||
            target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Percorso Portable non valido.");
        var locks = new DirectoryLocks();
        try
        {
            locks.Pin(staging);
            locks.Pin(Path.GetDirectoryName(target)!);
            RejectReparse(target);
            return locks;
        }
        catch { locks.Dispose(); throw; }
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Il recupero richiede un percorso locale senza collegamenti.");
    }

    private sealed class DirectoryLocks : IDisposable
    {
        private readonly Dictionary<string, SafeFileHandle> _handles = new(StringComparer.OrdinalIgnoreCase);
        internal void Pin(string directory)
        {
            var root = Path.GetPathRoot(directory);
            if (root is null || root.Length != 3 || new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable))
                throw new UnauthorizedAccessException("Percorso di recupero non locale.");
            var folders = new Stack<string>();
            for (var path = directory; path != null && path.Length > root.Length; path = Path.GetDirectoryName(path))
                folders.Push(path);
            foreach (var path in folders)
            {
                if (_handles.ContainsKey(path)) continue;
                // Open each ancestor without share-delete so it cannot be redirected between validation and replacement.
                var handle = OpenDirectory(path, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open,
                    0x02000000 | 0x00200000, IntPtr.Zero); // BACKUP_SEMANTICS | OPEN_REPARSE_POINT
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    throw new IOException("Impossibile proteggere la directory di recupero.", Marshal.GetLastPInvokeError());
                }
                _handles.Add(path, handle);
                RejectReparse(path);
            }
        }
        public void Dispose() { foreach (var handle in _handles.Values) handle.Dispose(); }
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle OpenDirectory(string name, uint access, FileShare share, IntPtr security,
            FileMode creation, uint flags, IntPtr template);
    }
}
