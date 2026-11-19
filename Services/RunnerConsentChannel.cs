using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace UpdateCenter.Services;

// The pipe name is an address, not a credential. Both endpoints authenticate the
// live process at the other end. Binaries, process memory and UI remain trusted,
// as in M6-B; files/argv and status JSON do not carry authority.
internal static class RunnerConsentChannel
{
    internal const string Prefix = "UpdateCenter.Consent.";
    private const int MaximumMessageBytes = 8 * 1024 * 1024;

    internal static NamedPipeServerStream Create(string name)
    {
        using var user = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(user.User ?? throw new UnauthorizedAccessException(),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        // Explicit account ACL allows the same user's split-token elevated runner.
        // CurrentUserOnly would also require equal elevation and break this case.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, security);
    }

    internal static async Task WriteAsync(Stream stream, string message, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var bytes = Encoding.UTF8.GetBytes(message);
        if (bytes.Length > MaximumMessageBytes) throw UnifiedConsentGate.RestartRequired();
        try
        {
            await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
            await stream.WriteAsync(bytes, deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
            await stream.FlushAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException(LocalizationService.Text("Tempo scaduto nell'invio del consenso IPC.", "Consent IPC write timed out.")); }
    }

    internal static Task<string> ReadAsync(Stream stream, CancellationToken token) =>
        ReadAsync(stream, token, TimeSpan.FromSeconds(30));

    internal static async Task<string> ReadAsync(Stream stream, CancellationToken token, TimeSpan timeout)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try
        {
            var size = new byte[4];
            await stream.ReadExactlyAsync(size, deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
            var length = BitConverter.ToInt32(size);
            if (length < 1 || length > MaximumMessageBytes) throw UnifiedConsentGate.RestartRequired();
            var bytes = new byte[length];
            await stream.ReadExactlyAsync(bytes, deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException(LocalizationService.Text("Tempo scaduto nella lettura del consenso IPC; ripeti il riepilogo.",
            "Consent IPC read timed out; repeat the summary.")); }
    }

    internal static int PeerId(PipeStream pipe, bool server)
    {
        uint id;
        var ok = server ? GetNamedPipeClientProcessId(pipe.SafePipeHandle, out id)
            : GetNamedPipeServerProcessId(pipe.SafePipeHandle, out id);
        if (!ok || id == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return checked((int)id);
    }

    // A retained kernel handle prevents PID recycling from changing the authority.
    internal sealed class Peer : IDisposable
    {
        private readonly SafeProcessHandle _handle;
        internal int Id { get; }
        internal long Created { get; }
        internal Peer(int id)
        {
            Id = id;
            _handle = OpenProcess(0x00101000, false, id); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
            try
            {
                if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                var image = new StringBuilder(32768); var length = image.Capacity;
                if (!QueryFullProcessImageName(_handle, 0, image, ref length) ||
                    !string.Equals(Path.GetFullPath(image.ToString()), Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase) ||
                    !ProcessIdToSessionId((uint)id, out var session) || session == 0 || session != Process.GetCurrentProcess().SessionId ||
                    !GetProcessTimes(_handle, out var created, out _, out _, out _))
                    throw new UnauthorizedAccessException("Untrusted consent endpoint.");
                Created = created;
                if (!OpenProcessToken(_handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (token)
                using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                using (var ownIdentity = WindowsIdentity.GetCurrent())
                    if (identity.User != ownIdentity.User) throw new UnauthorizedAccessException("Consent account changed.");
                EnsureAlive();
            }
            catch { _handle.Dispose(); throw; }
        }
        internal void EnsureAlive()
        {
            if (WaitForSingleObject(_handle, 0) != 258) throw UnifiedConsentGate.RestartRequired();
        }
        public void Dispose() => _handle.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint timeout);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}

internal sealed class PipeRunnerConsent(string pipeName, int ownerId, long ownerCreated,
    string planPath, bool requireAdministrator) : IRunnerConsent
{
    private int _used;
    public bool Confirm(RunnerPlanSnapshot plan, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _used, 1) != 0) throw UnifiedConsentGate.RestartRequired();
        try { return ConfirmAsync(plan, cancellationToken).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException(UnifiedConsentGate.RestartRequired().Message, ex); }
    }

    private async Task<bool> ConfirmAsync(RunnerPlanSnapshot plan, CancellationToken token)
    {
        if (!pipeName.StartsWith(RunnerConsentChannel.Prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(pipeName[RunnerConsentChannel.Prefix.Length..], "N", out _) ||
            plan.PauseOwnerProcessId != ownerId || plan.Interactive || plan.Recovery)
            throw UnifiedConsentGate.RestartRequired();
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(30000, token).ConfigureAwait(false);
        if (RunnerConsentChannel.PeerId(pipe, false) != ownerId) throw UnifiedConsentGate.RestartRequired();
        using var authority = new RunnerConsentChannel.Peer(ownerId);
        if (authority.Created != ownerCreated) throw UnifiedConsentGate.RestartRequired();
        var exact = UnifiedConsentGate.Canonical(plan);
        await RunnerConsentChannel.WriteAsync(pipe, exact, token).ConfigureAwait(false);
        // The human may review the summary; an abandoned session still expires.
        var answer = await RunnerConsentChannel.ReadAsync(pipe, token, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
        authority.EnsureAlive();
        token.ThrowIfCancellationRequested();
        if (answer != "authorize:" + exact) return false;
        // This re-read is a consistency check only. Execution keeps the original
        // frozen snapshot; a replacement file never becomes a new authority.
        if (UnifiedConsentGate.Canonical(RunnerPlanSnapshot.Read(planPath, requireAdministrator)) != exact)
            throw UnifiedConsentGate.RestartRequired();
        return WpfRunnerConsent.HasInteractiveSession();
    }
}
