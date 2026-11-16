// This alias also binds every unqualified Process use in the linked production sources.
global using Process = UpdateCenter.ConsentTestHost.ReadOnlyProcess;
using System.Diagnostics;
using UpdateCenter.Models;

namespace UpdateCenter.ConsentTestHost
{
    internal sealed class NativeOperationBlockedException(string operation) : InvalidOperationException(
        "TEST HOST: native operation blocked: " + operation);

    internal static class NativeBlock
    {
        internal static int Attempts;
        internal static T Deny<T>(string operation)
        { Interlocked.Increment(ref Attempts); throw new NativeOperationBlockedException(operation); }
    }

    // Only session/liveness queries required by the real consent and runner are supported.
    public sealed class ReadOnlyProcess : IDisposable
    {
        private readonly System.Diagnostics.Process _process;
        private ReadOnlyProcess(System.Diagnostics.Process process) => _process = process;
        public static ReadOnlyProcess GetCurrentProcess() => new(System.Diagnostics.Process.GetCurrentProcess());
        public static ReadOnlyProcess GetProcessById(int id) => new(System.Diagnostics.Process.GetProcessById(id));
        public int SessionId => _process.SessionId;
        public int Id => _process.Id;
        public bool HasExited => _process.HasExited;
        public int ExitCode => NativeBlock.Deny<int>("process exit code");
        public static ReadOnlyProcess? Start(ProcessStartInfo _) => NativeBlock.Deny<ReadOnlyProcess>("process launch / UAC");
        public void Kill(bool entireProcessTree) => NativeBlock.Deny<int>("process termination");
        public Task WaitForExitAsync(CancellationToken _) => NativeBlock.Deny<Task>("external process wait");
        public void Dispose() => _process.Dispose();
    }
}

namespace UpdateCenter.Services
{
    using UpdateCenter.ConsentTestHost;

    public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError,
        string CommandLine = "", int? ProcessId = null, TimeSpan? Duration = null)
    { public bool Success => ExitCode == 0; public bool PreflightRejected { get; init; } }

    public static class ProcessRunner
    {
        public static Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments,
            CancellationToken cancellationToken = default, TimeSpan? timeout = null, bool createWindow = false) =>
            NativeBlock.Deny<Task<ProcessResult>>("external process / restore point");
    }

    // Metadata validation intentionally admits only synthetic test targets. This is NOT
    // a replacement test of WinGet's real validation, already covered by M5/M6-B offline.
    public static class WinGetService
    {
        internal static ProcessResult? ValidateTarget(PlanItem item) =>
            item.Id is "Test.Software" or "Test.Runtime" && item.Source == "test-only" &&
            item.AvailableVersion is "2.0" or "9.0" ? null : new(1, "", "Only synthetic targets are accepted");
        public static ProcessResult RunInteractive(PlanItem item, CancellationToken token = default, Action<string>? phase = null) => NativeBlock.Deny<ProcessResult>("interactive WinGet");
        public static ProcessResult Install(PlanItem item, bool silent, CancellationToken token = default, Action<string>? phase = null) => NativeBlock.Deny<ProcessResult>("WinGet install");
        public static ProcessResult Upgrade(PlanItem item, bool silent, CancellationToken token = default, Action<string>? phase = null) => NativeBlock.Deny<ProcessResult>("WinGet upgrade");
        public static string ClassifyOutcome(ProcessResult result) => NativeBlock.Deny<string>("native result classification");
        public static bool RequiresRestart(ProcessResult result) => NativeBlock.Deny<bool>("native restart result");
        public static UpdateVerificationResult VerifyInstallation(PlanItem item, CancellationToken token = default) => NativeBlock.Deny<UpdateVerificationResult>("native verification");
        public static string ClassifyFailureReason(ProcessResult result, bool success) => NativeBlock.Deny<string>("native failure classification");
    }

    public static class WindowsUpdateService
    {
        public static ItemRunResult InstallDriver(PlanItem item, Action<int, string> progress) =>
            NativeBlock.Deny<ItemRunResult>("WUA EULA / download / installation");
    }
    public static class OfficialDriverPackageService
    {
        public static void ValidatePlan(PlanItem item) => NativeBlock.Deny<int>("external INF packages are unsupported");
        public static ItemRunResult Install(PlanItem item, Action<int, string> progress) =>
            NativeBlock.Deny<ItemRunResult>("driver download / installation");
    }
    public static class WinGetApplicabilityStore
    { public static void RecordNotApplicable(PlanItem item) => NativeBlock.Deny<int>("native applicability persistence"); }
    public static class PreflightService
    {
        public static bool ShouldCreateRestorePoint(IEnumerable<UpdateItem> items, AppSettings settings) =>
            NativeBlock.Deny<bool>("native coordinator preflight");
    }
    internal sealed record WinGetRecoveryContext;
    internal interface IWinGetRecoveryLease { string Restore(); }
    internal sealed record WinGetRecoveryPreparation(bool ShouldRetry, string Diagnostics,
        WinGetRecoveryContext? Context = null, bool ShouldRunInteractive = false,
        IWinGetRecoveryLease? Lease = null, bool ConsentCancelled = false);
    internal sealed record WinGetPostRetryDiagnosis(string Diagnostics, bool ShouldRunInteractive);
    internal interface IWinGetProcessRecoveryPrompt { }
    internal sealed class WinGetProcessRecoveryService
    {
        public WinGetRecoveryPreparation PrepareRetry(UpdateItem item, ItemRunResult result, IWinGetProcessRecoveryPrompt? prompt) =>
            NativeBlock.Deny<WinGetRecoveryPreparation>("native process/service recovery");
        public WinGetPostRetryDiagnosis DiagnoseFailedRetry(UpdateItem item, ItemRunResult result,
            IWinGetProcessRecoveryPrompt? prompt, WinGetRecoveryContext? context) =>
            NativeBlock.Deny<WinGetPostRetryDiagnosis>("native recovery diagnosis");
    }
    internal static class WinGetSingleRetryPolicy
    {
        public static Task<ItemRunResult> ExecuteAsync(UpdateItem item, ItemRunResult initialResult,
            Func<WinGetRecoveryPreparation> prepareRetry, Func<Task<ItemRunResult>> retry,
            Func<ItemRunResult, WinGetRecoveryContext?, WinGetPostRetryDiagnosis>? diagnoseFailedRetry = null,
            Func<Task<ItemRunResult>>? interactiveFallback = null) =>
            NativeBlock.Deny<Task<ItemRunResult>>("native coordinator retry policy; use explicit test scenarios");
    }
}
