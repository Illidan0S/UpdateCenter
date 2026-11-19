using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

// Preparation writes coordination metadata only. No runner can cross its
// IRunnerConsent boundary until the single summary has been accepted.
internal sealed class PreparedUpdateSession : IAsyncDisposable
{
    private readonly List<PreparedRunnerBatch> _batches = [];
    private UnifiedConsentGate? _gate;

    internal static async Task<PreparedUpdateSession> PrepareAsync(IReadOnlyList<UpdateItem> items,
        AppSettings settings, UpdatePauseController pause, CancellationToken token,
        Func<ProcessStartInfo, Process?>? launch = null)
    {
        var session = new PreparedUpdateSession();
        try
        {
            // Freeze both partitions before the first process/UAC yield.
            foreach (var administrator in new[] { false, true })
            {
                var batch = items.Where(item => (item.Kind == UpdateKind.Driver) == administrator).ToArray();
                if (batch.Length > 0)
                    session._batches.Add(new PreparedRunnerBatch(batch, settings, pause, administrator));
            }
            foreach (var batch in session._batches) batch.Start(token, launch ?? Process.Start);
            await Task.WhenAll(session._batches.Select(batch => batch.ReceiveAsync(token)));
            session._gate = new UnifiedConsentGate(session._batches.Select(batch => batch.Snapshot).ToArray());
            return session;
        }
        catch (OperationCanceledException) { await session.DisposeAsync(); throw; }
        catch (Exception ex)
        {
            await session.DisposeAsync();
            throw new InvalidOperationException(UnifiedConsentGate.RestartRequired().Message, ex);
        }
    }

    internal bool Confirm(Func<IReadOnlyList<RunnerPlanSnapshot>, bool> summary, Action progress,
        IReadOnlyList<UpdateItem> current, AppSettings settings) =>
        _gate!.Confirm(summary, progress, () => _batches.All(batch => batch.IsCurrent(current, settings)));

    internal PreparedRunnerBatch? Batch(bool administrator) => _batches.SingleOrDefault(batch => batch.Administrator == administrator);

    internal IReadOnlyList<UpdateItem> ConfirmedItems => _batches.SelectMany(batch => batch.Snapshot.Items)
        .Select(item => item.ToUpdateItem()).ToArray();

    internal async Task ReleaseAsync(PreparedRunnerBatch batch, CancellationToken token,
        Func<Stream, string, CancellationToken, Task>? send = null)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            foreach (var pending in _batches.Where(candidate => !candidate.IsReleased))
                if (UnifiedConsentGate.Canonical(pending.ReadCurrent()) != UnifiedConsentGate.Canonical(pending.Snapshot))
                    throw UnifiedConsentGate.RestartRequired();
            _gate!.Consume(_batches.IndexOf(batch), batch.ReadCurrent());
            await batch.ReleaseAsync(token, send ?? RunnerConsentChannel.WriteAsync);
        }
        catch (OperationCanceledException)
        {
            _gate!.Revoke();
            throw;
        }
        catch (Exception ex)
        {
            _gate!.Revoke();
            throw new InvalidOperationException(UnifiedConsentGate.RestartRequired().Message, ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var batch in _batches) await batch.DisposeAsync();
    }
}

internal sealed class PreparedRunnerBatch : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private RunnerConsentChannel.Peer? _peer;
    private readonly string _pipeName = RunnerConsentChannel.Prefix + Guid.NewGuid().ToString("N");
    private bool _ready;
    private bool _released;
    private bool _disposed;
    internal bool IsReleased => _released;
    internal bool Administrator { get; }
    internal string PlanPath { get; }
    internal string StatusPath => Snapshot.StatusFile;
    internal RunnerPlanSnapshot Snapshot { get; }
    internal Process? Process { get; private set; }

    internal PreparedRunnerBatch(IReadOnlyList<UpdateItem> items, AppSettings settings,
        UpdatePauseController pause, bool administrator)
    {
        Administrator = administrator;
        var id = Guid.NewGuid().ToString("N");
        PlanPath = Path.Combine(AppPaths.DataDirectory, $"update-plan-{id}.json");
        Snapshot = RunnerPlanSnapshot.Create(new UpdatePlan
        {
            CreateRestorePoint = PreflightService.ShouldCreateRestorePoint(items, settings),
            SilentSoftwareInstall = settings.SilentSoftwareInstall,
            StatusFile = Path.Combine(AppPaths.DataDirectory, $"update-status-{id}.json"),
            PauseFile = pause.SignalPath, PauseOwnerProcessId = Environment.ProcessId,
            Items = items.Select(ElevatedUpdateRunner.ToPlanItem).ToList()
        }, administrator);
        _pipe = RunnerConsentChannel.Create(_pipeName);
    }

    internal void Start(CancellationToken token, Func<ProcessStartInfo, Process?> launch)
    {
        JsonStorage.WriteAtomic(PlanPath, UnifiedConsentGate.ToPlan(Snapshot));
        using var owner = new RunnerConsentChannel.Peer(Environment.ProcessId);
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = Administrator
        };
        if (Administrator) info.Verb = "runas";
        foreach (var argument in new[] { Administrator ? "--update-runner-admin" : "--update-runner-user",
                     PlanPath, "--consent-channel", _pipeName,
                     Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     owner.Created.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            info.ArgumentList.Add(argument);
        try
        {
            Process = UpdateCoordinator.LaunchRunner(info, token, launch)
                ?? throw UnifiedConsentGate.RestartRequired();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new OperationCanceledException("Autorizzazione amministratore annullata / Administrator authorization cancelled.", ex); }
    }

    internal async Task ReceiveAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var connected = _pipe.WaitForConnectionAsync(timeout.Token);
        var exited = Process!.WaitForExitAsync(timeout.Token);
        if (await Task.WhenAny(connected, exited) != connected)
        {
            timeout.Cancel();
            try { await connected; } catch (OperationCanceledException) { }
            throw UnifiedConsentGate.RestartRequired();
        }
        await connected;
        if (Process is null || Process.HasExited || RunnerConsentChannel.PeerId(_pipe, true) != Process.Id)
            throw UnifiedConsentGate.RestartRequired();
        _peer = new RunnerConsentChannel.Peer(Process.Id);
        if (_peer.Created != Process.StartTime.ToUniversalTime().ToFileTimeUtc()) throw UnifiedConsentGate.RestartRequired();
        var received = await RunnerConsentChannel.ReadAsync(_pipe, timeout.Token);
        if (received != UnifiedConsentGate.Canonical(Snapshot)) throw UnifiedConsentGate.RestartRequired();
        _ready = true;
        timeout.Cancel();
    }

    internal RunnerPlanSnapshot ReadCurrent()
    {
        if (!_ready || _released || !_pipe.IsConnected) throw UnifiedConsentGate.RestartRequired();
        _peer!.EnsureAlive();
        return RunnerPlanSnapshot.Read(PlanPath, Administrator);
    }

    internal bool IsCurrent(IReadOnlyList<UpdateItem> items, AppSettings settings)
    {
        var selected = items.Where(item => (item.Kind == UpdateKind.Driver) == Administrator).ToArray();
        if (selected.Any(item => !item.CanInstall)) return false;
        var current = UnifiedConsentGate.ToPlan(Snapshot);
        current.Items = selected.Select(ElevatedUpdateRunner.ToPlanItem).ToList();
        current.CreateRestorePoint = PreflightService.ShouldCreateRestorePoint(selected, settings);
        current.SilentSoftwareInstall = settings.SilentSoftwareInstall;
        var exact = UnifiedConsentGate.Canonical(Snapshot);
        return UnifiedConsentGate.Canonical(RunnerPlanSnapshot.Create(current, Administrator)) == exact &&
            UnifiedConsentGate.Canonical(ReadCurrent()) == exact;
    }

    internal async Task ReleaseAsync(CancellationToken token, Func<Stream, string, CancellationToken, Task> send)
    {
        if (!_ready || _released) throw UnifiedConsentGate.RestartRequired();
        _peer!.EnsureAlive();
        _released = true;
        try { await send(_pipe, "authorize:" + UnifiedConsentGate.Canonical(Snapshot), token); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException)
        {
            // A complete frame may already have arrived. EOF denies an incomplete
            // frame; either way the coordinator must monitor the actual outcome.
            _pipe.Dispose();
            LogService.WriteEvent("consent", "delivery", "channel-interrupted", exception: ex,
                details: "Grant delivery uncertain; retaining runner/status supervision.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _pipe.Dispose(); // Revokes all grants not yet sent; no reconnect is accepted.
        _peer?.Dispose();
        // Prepared runners exit on EOF without effects. Do not kill a released installer.
        if (Process is not null && !_released)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await Process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                // Retain diagnostic files if a prepared process fails to exit promptly.
                Process.Dispose();
                return;
            }
        }
        Process?.Dispose();
        try { File.Delete(PlanPath); } catch { }
        try { File.Delete(StatusPath); } catch { }
    }
}
