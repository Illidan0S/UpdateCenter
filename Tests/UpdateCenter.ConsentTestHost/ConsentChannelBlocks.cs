using System.Diagnostics;
using UpdateCenter.Models;
using UpdateCenter.ConsentTestHost;

namespace UpdateCenter.Services;

// The manual M8 host never creates a pipe, launches a runner or transfers a grant.
// Real IPC is tested separately by the smoke harness with simulated effects.
internal sealed class PipeRunnerConsent : IRunnerConsent
{
    internal PipeRunnerConsent(string pipe, int owner, long created, string path, bool administrator) =>
        NativeBlock.Deny<int>("native consent channel");
    public bool Confirm(RunnerPlanSnapshot plan, CancellationToken token) => NativeBlock.Deny<bool>("native consent channel");
}

internal sealed class PreparedUpdateSession : IAsyncDisposable
{
    internal static Task<PreparedUpdateSession> PrepareAsync(IReadOnlyList<UpdateItem> items, AppSettings settings,
        UpdatePauseController pause, CancellationToken token, Func<ProcessStartInfo, ReadOnlyProcess?>? launch = null) =>
        NativeBlock.Deny<Task<PreparedUpdateSession>>("native prepared runners");
    internal bool Confirm(Func<IReadOnlyList<RunnerPlanSnapshot>, bool> summary, Action progress,
        IReadOnlyList<UpdateItem> items, AppSettings settings) => NativeBlock.Deny<bool>("native session consent");
    internal PreparedRunnerBatch? Batch(bool administrator) => NativeBlock.Deny<PreparedRunnerBatch>("native prepared batch");
    internal Task ReleaseAsync(PreparedRunnerBatch batch, CancellationToken token) => NativeBlock.Deny<Task>("native consent release");
    internal IReadOnlyList<UpdateItem> ConfirmedItems => NativeBlock.Deny<IReadOnlyList<UpdateItem>>("native confirmed targets");
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class PreparedRunnerBatch
{
    internal string PlanPath => NativeBlock.Deny<string>("native plan path");
    internal string StatusPath => NativeBlock.Deny<string>("native status path");
    internal ReadOnlyProcess? Process => NativeBlock.Deny<ReadOnlyProcess>("native prepared process");
}

internal static class UnifiedConsentGate
{
    internal static InvalidOperationException RestartRequired() => new NativeOperationBlockedException("native consent session");
}

// The older manual host has no scope-bound or release-notes scenarios. Reject
// bindings rather than pretending to reproduce the B2 attestation boundary.
internal static class WinGetScopeGuard
{
    internal static bool ValidBinding(WinGetScopeBinding binding) => false;
}
public sealed record ReleaseNotesInfo;
