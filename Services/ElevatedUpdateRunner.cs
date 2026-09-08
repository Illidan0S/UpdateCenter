using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class ElevatedUpdateRunner
{
    internal const int StatusChannelFailureExitCode = 3;

    public static int Run(string planPath, bool requireAdministrator)
    {
        if (!OperatingSystem.IsWindows()) return 2;

        UpdatePlan? plan = null;
        UpdateRunStatus? status = null;
        RunnerStatusPublisher? publisher = null;
        try
        {
            ValidatePlanPath(planPath);
            plan = JsonStorage.Read<UpdatePlan>(planPath)
                ?? throw new InvalidOperationException(LocalizationService.Text("Piano di aggiornamento non valido.", "Invalid update plan."));
            ValidateStatusPath(plan.StatusFile);
            ValidatePausePath(plan.PauseFile);

            status = new UpdateRunStatus
            {
                State = "Running",
                Total = plan.Items.Count,
                Message = LocalizationService.Text("Preparazione aggiornamenti…", "Preparing updates…"),
                RestorePointRequested = plan.CreateRestorePoint
            };
            publisher = new RunnerStatusPublisher(plan.StatusFile, status);

            if (requireAdministrator && !IsAdministrator())
                throw new UnauthorizedAccessException(LocalizationService.Text("I privilegi di amministratore non sono stati concessi.", "Administrator privileges were not granted."));

            if (plan.CreateRestorePoint)
            {
                publisher.Update(current =>
                {
                    current.Phase = "restore-point";
                    current.Message = LocalizationService.Text("Creazione del punto di ripristino…", "Creating restore point…");
                }, markProgress: true);
                var restorePointCreated = TryCreateRestorePoint(out var restoreMessage);
                publisher.Update(current =>
                {
                    current.RestorePointCreated = restorePointCreated;
                    current.Message = restoreMessage;
                }, markProgress: true);
            }

            for (var index = 0; index < plan.Items.Count; index++)
            {
                WaitWhilePaused(plan, publisher);
                var item = plan.Items[index];
                publisher.Update(current =>
                {
                    current.CurrentIndex = index;
                    current.CurrentItemId = item.Id;
                    current.CurrentName = item.Name;
                    current.InstallerTool = GetInstallerTool(item);
                    current.Phase = "Preparazione";
                    current.CurrentItemProgress = 1;
                    current.CurrentItemStartedUtc = DateTime.UtcNow;
                    current.Message = LocalizationService.IsEnglish ? $"Updating {item.Name}…" : $"Aggiornamento di {item.Name}…";
                }, markProgress: true);

                void ReportItemProgress(int percent, string message) =>
                    publisher.ReportProgress(percent, message);

                ItemRunResult result;
                if (item.Kind.Equals(nameof(UpdateKind.Driver), StringComparison.OrdinalIgnoreCase))
                {
                    result = item.DriverInstallMode.Equals(
                        DriverInstallModes.OfficialInfPackage, StringComparison.Ordinal)
                        ? OfficialDriverPackageService.Install(item, ReportItemProgress)
                        : WindowsUpdateService.InstallDriver(item, ReportItemProgress);
                }
                else
                {
                    ReportItemProgress(12, LocalizationService.Text("Avvio dell'aggiornamento software con WinGet...", "Starting software update with WinGet..."));
                    result = InstallSoftware(item, plan.SilentSoftwareInstall);
                }

                LogService.WriteEvent(
                    "update-attempt",
                    string.IsNullOrWhiteSpace(result.Phase) ? "result" : result.Phase,
                    result.Success
                        ? "success"
                        : result.InstallerSucceeded
                            ? "verification-failure"
                            : "failure",
                    result.Id,
                    result.ResultCode,
                    $"installerSucceeded={result.InstallerSucceeded}; " +
                    $"verification={result.VerificationStatus}; verified={result.Verified}; " +
                    $"failureReason={result.FailureReason}; {result.Message}");
                publisher.Update(current =>
                {
                    current.Results.Add(result);
                    current.RestartRequired |= result.RestartRequired;
                    current.CurrentIndex = index + 1;
                    current.CurrentItemProgress = 100;
                    current.Phase = "Completato";
                    current.Message = result.Message;
                    current.CurrentItemStartedUtc = null;
                    current.CurrentItemId = "";
                    current.InstallerTool = "";
                }, markProgress: true);
            }

            publisher.Complete(current =>
            {
                current.State = "Completed";
                current.CurrentName = "";
                current.Message = current.Results.All(x => x.Success)
                    ? "Tutti gli aggiornamenti selezionati sono terminati."
                    : "Operazione terminata: alcuni aggiornamenti richiedono attenzione.";
            });
            return status.Results.All(x => x.Success) ? 0 : 1;
        }
        catch (Exception ex)
        {
            LogService.Write("Esecuzione elevata interrotta.", ex);
            if (publisher is not null)
            {
                publisher.TryPublishFailure(current =>
                {
                    current.State = "Failed";
                    current.Message = "Il canale di stato dell'aggiornamento non è disponibile.";
                });
            }
            return ex is UpdateStatusChannelException or AtomicWriteException or UnauthorizedAccessException
                ? StatusChannelFailureExitCode
                : 1;
        }
        finally
        {
            publisher?.Dispose();
        }
    }

    internal static ItemRunResult InstallSoftwareInteractive(UpdateItem item) =>
        InstallSoftware(ToPlanItem(item), silent: false, interactive: true);

    internal static ItemRunResult EvaluateInteractiveResultForTest(
        PlanItem item,
        ProcessResult installerResult,
        Func<PlanItem, UpdateVerificationResult> verification) =>
        InstallSoftware(
            item,
            silent: false,
            interactive: true,
            suppliedResult: installerResult,
            verificationOverride: verification);

    private static ItemRunResult InstallSoftware(
        PlanItem item,
        bool silent,
        bool interactive = false,
        ProcessResult? suppliedResult = null,
        Func<PlanItem, UpdateVerificationResult>? verificationOverride = null)
    {
        try
        {
            var isFreshInstall = item.PackageOperation.Equals(PackageOperations.Install, StringComparison.Ordinal);
            var result = suppliedResult ?? (interactive
                ? WinGetService.RunInteractive(item)
                : isFreshInstall
                ? WinGetService.Install(item, silent)
                : WinGetService.Upgrade(item, silent));
            var installerOutcome = WinGetService.ClassifyOutcome(result);
            var restartRequired = WinGetService.RequiresRestart(result);
            var installerSucceeded = installerOutcome.Equals(UpdateOutcomes.Completed, StringComparison.Ordinal) ||
                                     restartRequired;
            var shouldVerify = installerOutcome.Equals(UpdateOutcomes.Completed, StringComparison.Ordinal) ||
                               installerOutcome.Equals(UpdateOutcomes.Failed, StringComparison.Ordinal);
            var verification = shouldVerify
                ? verificationOverride?.Invoke(item) ?? WinGetService.VerifyInstallation(item)
                : new UpdateVerificationResult
                {
                    Status = UpdateVerificationStatuses.NotRun,
                    Message = "Verifica post-installazione non richiesta per questo esito."
                };
            var decision = UpdateResultPolicy.Resolve(installerSucceeded, restartRequired, verification);
            var failureReason = WinGetService.ClassifyFailureReason(result, decision.Success);
            var finalOutcome = installerOutcome is UpdateOutcomes.NotApplicable or UpdateOutcomes.ManualRequired
                ? installerOutcome
                : decision.Outcome;

            if (installerOutcome.Equals(UpdateOutcomes.NotApplicable, StringComparison.Ordinal))
                WinGetApplicabilityStore.RecordNotApplicable(item);
            var output = string.Join(" ", (result.StandardOutput + "\n" + result.StandardError)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 2)
                .TakeLast(4));
            var alreadyCurrentMessage = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.Contains("risulta già aggiornato", StringComparison.OrdinalIgnoreCase));
            var completedMessage = alreadyCurrentMessage ?? (isFreshInstall
                ? "Runtime installato con WinGet."
                : "Software aggiornato con WinGet.");
            if (installerSucceeded && !string.IsNullOrWhiteSpace(verification.Message))
                completedMessage = verification.IsDefinitive && !verification.Verified
                    ? $"WinGet ha completato l'installer, ma la verifica post-installazione non è riuscita. {verification.Message}"
                    : $"{completedMessage} {verification.Message}";
            if (!installerSucceeded && verification.Verified)
                completedMessage =
                    $"WinGet ha restituito il codice {result.ExitCode}, ma la versione target risulta installata. {verification.Message}";

            var message = decision.Success
                ? completedMessage
                : installerOutcome switch
                {
                    UpdateOutcomes.NotApplicable => "La versione segnalata da WinGet non è applicabile a questo PC. " +
                                                    "L'elemento resta visibile come aggiornamento manuale in questa scansione e verrà escluso dalle successive " +
                                                    "finché non cambia la versione installata o quella proposta. Usa l'aggiornamento interno del programma " +
                                                    "o il sito ufficiale del produttore.",
                    UpdateOutcomes.ManualRequired => "Questo pacchetto non supporta l'aggiornamento automatico con la tecnologia di installazione corrente. Usa l'installer ufficiale del produttore.",
                    _ => installerSucceeded && !string.IsNullOrWhiteSpace(verification.Message)
                        ? $"WinGet ha completato l'installer, ma la verifica post-installazione non è riuscita. {verification.Message}"
                        : string.IsNullOrWhiteSpace(output)
                            ? $"WinGet ha restituito il codice {result.ExitCode}."
                            : output
                };

            return new ItemRunResult
            {
                Id = item.Id,
                Name = item.Name,
                Kind = item.Kind.Equals(nameof(UpdateKind.Runtime), StringComparison.Ordinal)
                    ? "Runtime"
                    : item.Kind,
                Success = decision.Success,
                InstallerSucceeded = installerSucceeded,
                Verified = decision.Verified,
                VerificationStatus = decision.VerificationStatus,
                ResultCode = result.ExitCode,
                Phase = interactive
                    ? "winget-interactive"
                    : isFreshInstall ? "winget-install" : "winget-upgrade",
                FailureReason = failureReason,
                Outcome = finalOutcome,
                RestartRequired = restartRequired,
                Message = message,
                Diagnostics = BuildProcessDiagnostics(result) +
                              (string.IsNullOrWhiteSpace(verification.Diagnostics)
                                  ? ""
                                  : "\n\nVerifica post-installazione:\n" + verification.Diagnostics)
            };
        }
        catch (Exception ex)
        {
            LogService.Write($"Errore aggiornamento software {item.Name}.", ex);
            return new ItemRunResult
            {
                Id = item.Id,
                Name = item.Name,
                Kind = item.Kind.Equals(nameof(UpdateKind.Runtime), StringComparison.Ordinal)
                    ? "Runtime"
                    : item.Kind,
                Success = false,
                InstallerSucceeded = false,
                Verified = false,
                VerificationStatus = UpdateVerificationStatuses.NotRun,
                ResultCode = ex.HResult,
                Phase = "winget-exception",
                Outcome = UpdateOutcomes.Failed,
                Message = ex.Message,
                Diagnostics = ex.ToString()
            };
        }
    }

    private static string BuildProcessDiagnostics(ProcessResult result)
    {
        var lines = new List<string>
        {
            $"Codice di uscita: {result.ExitCode}",
            $"PID processo esterno: {result.ProcessId?.ToString() ?? "non disponibile"}",
            $"Durata: {result.Duration?.ToString() ?? "non disponibile"}",
            $"Comando/i eseguiti:\n{result.CommandLine}"
        };
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            lines.Add("Output:\n" + result.StandardOutput.Trim());
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            lines.Add("Errori:\n" + result.StandardError.Trim());
        return string.Join("\n\n", lines);
    }

    private static bool TryCreateRestorePoint(out string message)
    {
        try
        {
            var description = $"Update Center {DateTime.Now:yyyy-MM-dd HH-mm}";
            var escaped = description.Replace("'", "''");
            var command = $"Checkpoint-Computer -Description '{escaped}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop";
            var result = ProcessRunner.RunAsync(
                "powershell.exe",
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command],
                CancellationToken.None,
                TimeSpan.FromMinutes(3)).GetAwaiter().GetResult();

            if (result.Success)
            {
                message = "Punto di ripristino creato.";
                return true;
            }

            message = "Punto di ripristino non creato; gli aggiornamenti continueranno. Verifica che Protezione sistema sia attiva.";
            LogService.Write($"Creazione punto di ripristino fallita: {result.StandardError}");
            return false;
        }
        catch (Exception ex)
        {
            message = "Punto di ripristino non disponibile; gli aggiornamenti continueranno.";
            LogService.Write("Creazione punto di ripristino fallita.", ex);
            return false;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void ValidatePlanPath(string path)
    {
        AppPaths.EnsureCreated();
        var fullPath = Path.GetFullPath(path);
        var allowedRoot = Path.GetFullPath(AppPaths.DataDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith("update-plan-", StringComparison.OrdinalIgnoreCase) ||
            !fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Percorso del piano non consentito.");
    }

    private static void ValidateStatusPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var allowedRoot = Path.GetFullPath(AppPaths.DataDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith("update-status-", StringComparison.OrdinalIgnoreCase) ||
            !fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Percorso dello stato non consentito.");
    }

    private static void ValidatePausePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var allowedRoot = Path.GetFullPath(AppPaths.DataDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith("update-pause-", StringComparison.OrdinalIgnoreCase) ||
            !fullPath.EndsWith(".signal", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Percorso del segnale di pausa non consentito.");
    }

    private static string GetInstallerTool(PlanItem item)
    {
        if (!item.Kind.Equals(nameof(UpdateKind.Driver), StringComparison.OrdinalIgnoreCase))
            return "WinGet";
        return item.DriverInstallMode.Equals(DriverInstallModes.OfficialInfPackage, StringComparison.Ordinal)
            ? "PnPUtil/INF"
            : "Windows Update";
    }

    private static void WaitWhilePaused(UpdatePlan plan, RunnerStatusPublisher publisher)
    {
        var wasPaused = false;
        while (File.Exists(plan.PauseFile))
        {
            if (plan.PauseOwnerProcessId > 0 && !IsProcessRunning(plan.PauseOwnerProcessId))
            {
                try { File.Delete(plan.PauseFile); } catch { }
                break;
            }

            wasPaused = true;
            publisher.Update(status =>
            {
                status.State = "Paused";
                status.CurrentName = "";
                status.Phase = "Pausa";
                status.CurrentItemProgress = 0;
                status.Message = "Aggiornamenti in pausa. Premi Riprendi in Update Center per continuare.";
            });
            Thread.Sleep(350);
        }

        if (!wasPaused) return;
        publisher.Update(status =>
        {
            status.State = "Running";
            status.Phase = "Ripresa";
            status.Message = "Ripresa degli aggiornamenti…";
        }, markProgress: true);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch { return false; }
    }

    internal sealed class RunnerStatusPublisher : IDisposable
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
        private readonly object _sync = new();
        private readonly string _statusPath;
        private readonly UpdateRunStatus _status;
        private readonly TimeSpan _heartbeatInterval;
        private readonly Action<string, UpdateRunStatus, string> _statusWriter;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _heartbeatTask;
        private Exception? _lastFailure;
        private int _consecutiveFailures;

        public RunnerStatusPublisher(
            string statusPath,
            UpdateRunStatus status,
            TimeSpan? heartbeatInterval = null,
            Action<string, UpdateRunStatus, string>? statusWriter = null)
        {
            _statusPath = statusPath;
            _status = status;
            _heartbeatInterval = heartbeatInterval ?? HeartbeatInterval;
            _statusWriter = statusWriter ?? ((path, current, stage) => JsonStorage.WriteAtomic(
                path, current, $"status-{stage}", new AtomicFileOperations(), Thread.Sleep));
            lock (_sync)
            {
                Stamp(markProgress: false);
                PublishRequired("initial");
            }
            _heartbeatTask = Task.Run(PublishHeartbeatAsync);
        }

        public void Update(
            Action<UpdateRunStatus> update,
            bool markProgress = false,
            string stage = "progress")
        {
            lock (_sync)
            {
                update(_status);
                Stamp(markProgress);
                _ = TryPublish(stage);
            }
        }

        public void Complete(Action<UpdateRunStatus> update)
        {
            lock (_sync)
            {
                update(_status);
                Stamp(markProgress: true);
                PublishRequired("final");
            }
        }

        public bool TryPublishFailure(Action<UpdateRunStatus> update)
        {
            lock (_sync)
            {
                update(_status);
                Stamp(markProgress: false);
                return TryPublish("final-failure");
            }
        }

        public void ReportProgress(int percent, string message)
        {
            lock (_sync)
            {
                var normalizedPercent = Math.Clamp(percent, 1, 99);
                var changed = Math.Abs(_status.CurrentItemProgress - normalizedPercent) > 0.001 ||
                              !_status.Phase.Equals(message, StringComparison.Ordinal);
                _status.CurrentItemProgress = normalizedPercent;
                _status.Phase = message;
                _status.Message = message;
                var now = DateTime.UtcNow;
                _status.LastHeartbeatUtc = now;
                if (changed)
                    _status.LastProgressUtc = now;
                _ = TryPublish("progress");
            }
        }

        private async Task PublishHeartbeatAsync()
        {
            using var timer = new PeriodicTimer(_heartbeatInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                {
                    Update(_ => { }, stage: "heartbeat");
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private void Stamp(bool markProgress)
        {
            var now = DateTime.UtcNow;
            _status.LastHeartbeatUtc = now;
            if (markProgress)
                _status.LastProgressUtc = now;
        }

        private bool TryPublish(string stage)
        {
            try
            {
                _statusWriter(_statusPath, _status, stage);
                if (_consecutiveFailures > 0)
                    LogPublication(stage, "recovered", _lastFailure, _consecutiveFailures);
                _consecutiveFailures = 0;
                _lastFailure = null;
                return true;
            }
            catch (Exception ex)
            {
                _lastFailure = ex;
                _consecutiveFailures++;
                LogPublication(stage, "failed", ex, _consecutiveFailures);
                return false;
            }
        }

        private void PublishRequired(string stage)
        {
            if (TryPublish(stage)) return;
            throw new UpdateStatusChannelException(
                stage,
                _statusPath,
                _consecutiveFailures,
                "Il canale di stato dell'aggiornamento non è disponibile.",
                _lastFailure!);
        }

        private void LogPublication(string stage, string outcome, Exception? exception, int failures)
        {
            var attempts = exception is AtomicWriteException atomic ? atomic.Attempts : 1;
            var hresult = exception?.HResult ?? 0;
            LogService.WriteEvent(
                "status-channel",
                stage,
                outcome,
                resultCode: exception is null ? null : hresult,
                details: $"path={_statusPath}; exception={exception?.GetType().Name ?? "none"}; " +
                         $"hresult=0x{hresult:X8}; win32={hresult & 0xFFFF}; retryCount={Math.Max(0, attempts - 1)}; " +
                         $"consecutiveFailures={failures}; outcome={outcome}",
                exception: exception);
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _heartbeatTask.GetAwaiter().GetResult(); } catch { }
            _stop.Dispose();
        }
    }

    internal static PlanItem ToPlanItem(UpdateItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Kind = item.Kind.ToString(),
        Source = item.Source,
        InstalledVersion = item.InstalledVersion,
        AvailableVersion = item.AvailableVersion,
        PackageOperation = item.PackageOperation,
        WindowsUpdateId = item.WindowsUpdateId,
        WindowsUpdateRevision = item.WindowsUpdateRevision,
        WindowsUpdateServerSelection = item.WindowsUpdateServerSelection,
        WindowsUpdateServiceId = item.WindowsUpdateServiceId,
        DriverInstallMode = item.DriverInstallMode,
        Vendor = item.Publisher,
        OfficialReleasePageUrl = item.OfficialReleasePageUrl,
        OfficialDownloadUrl = item.OfficialDownloadUrl,
        ExpectedSha256 = item.ExpectedSha256,
        ExpectedSignerSubjects = item.ExpectedSignerSubjects,
        DriverPackageType = item.DriverPackageType,
        CompatibleHardwareIds = item.CompatibleHardwareIds
    };
}

internal sealed class UpdateStatusChannelException : IOException
{
    public UpdateStatusChannelException(
        string stage,
        string path,
        int failureCount,
        string message,
        Exception innerException) : base(message, innerException)
    {
        Stage = stage;
        Path = path;
        FailureCount = failureCount;
        HResult = innerException.HResult;
    }

    public string Stage { get; }
    public string Path { get; }
    public int FailureCount { get; }
}

internal readonly record struct UpdateWatchdogThresholds(
    TimeSpan HeartbeatTimeout,
    TimeSpan ProgressWarning,
    TimeSpan ItemTimeout)
{
    public static UpdateWatchdogThresholds Default { get; } = new(
        TimeSpan.FromSeconds(75),
        TimeSpan.FromMinutes(12),
        TimeSpan.FromMinutes(90));
}

internal readonly record struct UpdateWatchdogDecision(
    bool ShouldTerminate,
    string TerminationReason,
    bool ShouldWarnProgress,
    TimeSpan HeartbeatAge,
    TimeSpan ProgressAge,
    TimeSpan ItemDuration);

internal static class UpdateWatchdogPolicy
{
    public static UpdateWatchdogDecision Evaluate(
        UpdateRunStatus status,
        DateTime nowUtc,
        UpdateWatchdogThresholds thresholds)
    {
        var heartbeatAge = Elapsed(nowUtc, status.LastHeartbeatUtc);
        var progressAge = Elapsed(nowUtc, status.LastProgressUtc);
        var itemDuration = status.CurrentItemStartedUtc is DateTime startedUtc
            ? Elapsed(nowUtc, startedUtc)
            : TimeSpan.Zero;
        var supervised = status.State.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
                         status.State.Equals("Paused", StringComparison.OrdinalIgnoreCase) ||
                         status.State.Equals("Starting", StringComparison.OrdinalIgnoreCase);
        if (supervised && heartbeatAge > thresholds.HeartbeatTimeout)
        {
            return new UpdateWatchdogDecision(
                true, "runner-heartbeat-timeout", false,
                heartbeatAge, progressAge, itemDuration);
        }

        var installing = status.State.Equals("Running", StringComparison.OrdinalIgnoreCase) &&
                         status.CurrentItemStartedUtc.HasValue;
        if (installing && itemDuration > thresholds.ItemTimeout)
        {
            return new UpdateWatchdogDecision(
                true, "absolute-item-timeout", false,
                heartbeatAge, progressAge, itemDuration);
        }

        return new UpdateWatchdogDecision(
            false, "", installing && progressAge > thresholds.ProgressWarning,
            heartbeatAge, progressAge, itemDuration);
    }

    private static TimeSpan Elapsed(DateTime nowUtc, DateTime thenUtc) =>
        nowUtc > thenUtc ? nowUtc - thenUtc : TimeSpan.Zero;
}

public sealed class UpdateCoordinator
{
    private readonly WinGetProcessRecoveryService _processRecovery;

    public UpdateCoordinator() : this(new WinGetProcessRecoveryService())
    {
    }

    internal UpdateCoordinator(WinGetProcessRecoveryService processRecovery) =>
        _processRecovery = processRecovery;

    public Task<UpdateRunStatus> RunAsync(
        IReadOnlyList<UpdateItem> selectedItems,
        AppSettings settings,
        UpdatePauseController pauseController,
        Action<UpdateRunStatus> progress,
        CancellationToken cancellationToken) =>
        RunAsync(selectedItems, settings, pauseController, progress, cancellationToken, processRecoveryPrompt: null);

    internal async Task<UpdateRunStatus> RunAsync(
        IReadOnlyList<UpdateItem> selectedItems,
        AppSettings settings,
        UpdatePauseController pauseController,
        Action<UpdateRunStatus> progress,
        CancellationToken cancellationToken,
        IWinGetProcessRecoveryPrompt? processRecoveryPrompt)
    {
        var observer = progress;
        progress = status => ReportProgressSafely(observer, status);
        if (selectedItems.Any(x => !x.CanInstall))
            throw new InvalidOperationException(
                "Gli elementi non installabili automaticamente non possono essere avviati.");

        AppPaths.EnsureCreated();
        var software = selectedItems.Where(x => x.Kind is UpdateKind.Software or UpdateKind.Runtime).ToList();
        var drivers = selectedItems.Where(x => x.Kind == UpdateKind.Driver).ToList();
        var aggregate = new UpdateRunStatus
        {
            State = "Running",
            Total = selectedItems.Count,
            Message = "Preparazione aggiornamenti…"
        };
        var terminalLedger = new UpdateSessionResultLedger();

        try
        {
            if (software.Count > 0)
            {
                var softwareResult = await RunBatchAsync(
                    software, settings, pauseController, requireAdministrator: false, aggregate.Results.Count,
                    aggregate, progress, cancellationToken);
                var softwareTerminalResults = new List<ItemRunResult>();
                for (var index = 0; index < softwareResult.Results.Count; index++)
                {
                    var initialResult = softwareResult.Results[index];
                    var item = FindItem(software, initialResult)
                        ?? throw new InvalidOperationException(
                            $"Il runner ha restituito un risultato per un item non pianificato: {initialResult.Id}.");
                    var stateMachine = new ItemExecutionStateMachine();
                    stateMachine.BeginInitialAttempt();
                    var finalResult = initialResult;
                    if (processRecoveryPrompt is not null &&
                        initialResult.FailureReason.Equals(UpdateFailureReasons.FilesInUse, StringComparison.Ordinal))
                    {
                        stateMachine.BeginRecovery();
                        finalResult = await WinGetSingleRetryPolicy.ExecuteAsync(
                            item,
                            initialResult,
                            () => _processRecovery.PrepareRetry(item, initialResult, processRecoveryPrompt),
                            async () =>
                            {
                                stateMachine.BeginRetry();
                                var retryBatch = await RunBatchAsync(
                                    [item], settings, pauseController, requireAdministrator: false,
                                    completedBeforeBatch: Math.Min(index, Math.Max(0, aggregate.Total - 1)),
                                    aggregate, progress, cancellationToken,
                                    allowRestorePoint: false);
                                softwareResult.RestartRequired |= retryBatch.RestartRequired;
                                return retryBatch.Results.Single();
                            },
                            (retryResult, preparedContext) => _processRecovery.DiagnoseFailedRetry(
                                item, retryResult, processRecoveryPrompt, preparedContext),
                            () => Task.Run(() =>
                                ElevatedUpdateRunner.InstallSoftwareInteractive(item)));
                    }
                    finalResult = stateMachine.Complete(finalResult);
                    terminalLedger.Record(finalResult);
                    softwareTerminalResults.Add(finalResult);
                }
                softwareResult.Results = softwareTerminalResults;
                MergeBatchMetadata(aggregate, softwareResult);
                aggregate.Results = terminalLedger.Results.ToList();
                progress(aggregate);
            }

            if (drivers.Count > 0)
            {
                var driverResult = await RunBatchAsync(
                    drivers, settings, pauseController, requireAdministrator: true, aggregate.Results.Count,
                    aggregate, progress, cancellationToken);
                foreach (var directResult in driverResult.Results)
                {
                    if (FindItem(drivers, directResult) is null)
                        throw new InvalidOperationException(
                            $"Il runner ha restituito un risultato driver non pianificato: {directResult.Id}.");
                    var stateMachine = new ItemExecutionStateMachine();
                    stateMachine.BeginInitialAttempt();
                    terminalLedger.Record(stateMachine.Complete(directResult));
                }
                MergeBatchMetadata(aggregate, driverResult);
                aggregate.Results = terminalLedger.Results.ToList();
                progress(aggregate);
            }

            var summary = UpdateSessionSummary.From(aggregate.Results);
            aggregate.State = "Completed";
            aggregate.CurrentIndex = summary.ProcessedTerminalCount;
            aggregate.CurrentName = "";
            aggregate.Message = summary.ProcessedTerminalCount == 0
                ? LocalizationService.Text("Nessun aggiornamento è stato eseguito.", "No updates were performed.")
                : summary.HasProblems
                    ? LocalizationService.Text("Operazione terminata: alcuni aggiornamenti richiedono attenzione.", "Operation finished: some updates require attention.")
                    : LocalizationService.Text("Tutti gli aggiornamenti selezionati sono terminati.", "All selected updates finished.");
            progress(aggregate);
            return aggregate;
        }
        finally
        {
            pauseController.Cleanup();
        }
    }

    internal static void ReportProgressSafely(Action<UpdateRunStatus> observer, UpdateRunStatus status)
    {
        try { observer(status); }
        catch (Exception ex)
        {
            LogService.WriteEvent("ui", "progress", "ui-error", exception: ex,
                details: "Il risultato tecnico è conservato; aggiornamento della vista non riuscito.");
        }
    }

    private static async Task<UpdateRunStatus> RunBatchAsync(
        IReadOnlyList<UpdateItem> selectedItems,
        AppSettings settings,
        UpdatePauseController pauseController,
        bool requireAdministrator,
        int completedBeforeBatch,
        UpdateRunStatus aggregate,
        Action<UpdateRunStatus> progress,
        CancellationToken cancellationToken,
        bool allowRestorePoint = true)
    {
        var token = Guid.NewGuid().ToString("N");
        var planPath = Path.Combine(AppPaths.DataDirectory, $"update-plan-{token}.json");
        var statusPath = Path.Combine(AppPaths.DataDirectory, $"update-status-{token}.json");
        var plan = new UpdatePlan
        {
            CreateRestorePoint = allowRestorePoint &&
                                 PreflightService.ShouldCreateRestorePoint(selectedItems, settings),
            SilentSoftwareInstall = settings.SilentSoftwareInstall,
            StatusFile = statusPath,
            PauseFile = pauseController.SignalPath,
            PauseOwnerProcessId = Environment.ProcessId,
            Items = selectedItems.Select(ElevatedUpdateRunner.ToPlanItem).ToList()
        };

        JsonStorage.WriteAtomic(planPath, plan);
        Process? process = null;
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Percorso dell'applicazione non disponibile.");
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = requireAdministrator,
                WorkingDirectory = AppContext.BaseDirectory
            };
            if (requireAdministrator)
                startInfo.Verb = "runas";
            startInfo.ArgumentList.Add(requireAdministrator ? "--update-runner-admin" : "--update-runner-user");
            startInfo.ArgumentList.Add(planPath);

            try
            {
                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Impossibile avviare il processo di aggiornamento.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Autorizzazione amministratore annullata.", ex);
            }

            UpdateRunStatus? latest = null;
            var runnerStartedUtc = DateTime.UtcNow;
            DateTime? warnedProgressTimestamp = null;
            string? forcedFailureMessage = null;
            var thresholds = UpdateWatchdogThresholds.Default;
            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = JsonStorage.Read<UpdateRunStatus>(statusPath);
                if (current is not null)
                {
                    latest = current;
                    progress(BuildAggregateProgress(aggregate, current, completedBeforeBatch));
                    var decision = UpdateWatchdogPolicy.Evaluate(current, DateTime.UtcNow, thresholds);
                    if (decision.ShouldWarnProgress &&
                        warnedProgressTimestamp != current.LastProgressUtc)
                    {
                        warnedProgressTimestamp = current.LastProgressUtc;
                        LogService.WriteEvent(
                            "watchdog", current.Phase, "progress-stalled-warning",
                            current.CurrentItemId,
                            details: BuildWatchdogDiagnostics(
                                process.Id, current, decision,
                                "Nessun progresso per 12 minuti; heartbeat runner ancora valido, installazione lasciata in esecuzione."));
                    }

                    if (decision.ShouldTerminate)
                    {
                        var timeoutMessage = decision.TerminationReason == "absolute-item-timeout"
                            ? $"L'aggiornamento di {current.CurrentName} ha superato il limite massimo di 90 minuti."
                            : $"Il runner dell'aggiornamento di {current.CurrentName} non comunica da oltre 75 secondi.";
                        LogService.WriteEvent(
                            "watchdog", current.Phase, decision.TerminationReason,
                            current.CurrentItemId,
                            details: BuildWatchdogDiagnostics(process.Id, current, decision, timeoutMessage));
                        try { process.Kill(true); } catch { }
                        forcedFailureMessage = timeoutMessage;
                        break;
                    }
                }
                else if (DateTime.UtcNow - runnerStartedUtc > thresholds.HeartbeatTimeout)
                {
                    var message = "Il runner non ha pubblicato lo stato iniziale entro 75 secondi.";
                    LogService.WriteEvent(
                        "watchdog", "runner-start", "runner-heartbeat-timeout",
                        details: $"runnerPid={process.Id}; reason={message}");
                    try { process.Kill(true); } catch { }
                    throw new TimeoutException(message);
                }
                await Task.Delay(350, cancellationToken);
            }

            if (forcedFailureMessage is not null && !process.HasExited)
                await process.WaitForExitAsync(cancellationToken);

            var final = JsonStorage.Read<UpdateRunStatus>(statusPath) ?? latest;
            if (final is null)
            {
                throw new InvalidOperationException(
                    process.ExitCode == ElevatedUpdateRunner.StatusChannelFailureExitCode
                        ? LocalizationService.Text("Il runner non ha potuto inizializzare il canale di stato protetto.", "The runner could not initialize the protected status channel.")
                        : LocalizationService.Text("Il processo di aggiornamento non ha restituito uno stato.", "The update process did not return a status."));
            }
            var incompleteState = final.State.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
                                  final.State.Equals("Starting", StringComparison.OrdinalIgnoreCase) ||
                                  final.State.Equals("Paused", StringComparison.OrdinalIgnoreCase);
            if (forcedFailureMessage is not null ||
                process.ExitCode == ElevatedUpdateRunner.StatusChannelFailureExitCode ||
                final.State.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                incompleteState)
            {
                var reason = forcedFailureMessage ??
                             LocalizationService.Text("Il runner si è interrotto per un errore infrastrutturale del canale di stato.", "The runner stopped due to an infrastructure error in the status channel.");
                final = BuildControlledBatchFailure(selectedItems, final, reason);
            }
            else
            {
                final = EnsureCompletedBatchCardinality(selectedItems, final);
            }
            progress(BuildAggregateProgress(aggregate, final, completedBeforeBatch));
            return final;
        }
        finally
        {
            process?.Dispose();
            TryDelete(planPath);
            TryDelete(statusPath);
        }
    }

    private static UpdateRunStatus BuildAggregateProgress(
        UpdateRunStatus aggregate,
        UpdateRunStatus batch,
        int completedBeforeBatch) => new()
    {
        State = batch.State,
        CurrentIndex = completedBeforeBatch + batch.CurrentIndex,
        Total = aggregate.Total,
        CurrentName = batch.CurrentName,
        Message = batch.Message,
        Phase = batch.Phase,
        CurrentItemProgress = batch.CurrentItemProgress,
        LastHeartbeatUtc = batch.LastHeartbeatUtc,
        LastProgressUtc = batch.LastProgressUtc,
        CurrentItemStartedUtc = batch.CurrentItemStartedUtc,
        CurrentItemId = batch.CurrentItemId,
        InstallerTool = batch.InstallerTool,
        RestorePointRequested = aggregate.RestorePointRequested || batch.RestorePointRequested,
        RestorePointCreated = aggregate.RestorePointCreated || batch.RestorePointCreated,
        RestartRequired = aggregate.RestartRequired || batch.RestartRequired,
        Results = aggregate.Results.ToList()
    };

    private static void MergeBatchMetadata(UpdateRunStatus aggregate, UpdateRunStatus batch)
    {
        aggregate.RestorePointRequested |= batch.RestorePointRequested;
        aggregate.RestorePointCreated |= batch.RestorePointCreated;
        aggregate.RestartRequired |= batch.RestartRequired;
    }

    private static UpdateItem? FindItem(
        IEnumerable<UpdateItem> items,
        ItemRunResult result) =>
        items.FirstOrDefault(item =>
            item.Id.Equals(result.Id, StringComparison.OrdinalIgnoreCase) &&
            item.Kind.ToString().Equals(result.Kind, StringComparison.OrdinalIgnoreCase));

    internal static UpdateRunStatus EnsureCompletedBatchCardinality(
        IReadOnlyList<UpdateItem> selectedItems,
        UpdateRunStatus status)
    {
        var duplicate = status.Results
            .GroupBy(ResultKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        var unexpected = status.Results.FirstOrDefault(result =>
            selectedItems.All(item => !ResultMatches(item, result)));
        var missing = selectedItems.Where(item =>
            status.Results.All(result => !ResultMatches(item, result))).ToList();
        if (duplicate is null && unexpected is null && missing.Count == 0 &&
            status.Results.Count == selectedItems.Count)
            return status;

        var reason = duplicate is not null
            ? $"Il runner ha pubblicato risultati duplicati per {duplicate.Key}."
            : unexpected is not null
                ? $"Il runner ha pubblicato un risultato non pianificato per {unexpected.Id}."
                : $"Il runner non ha pubblicato il risultato terminale di {missing.FirstOrDefault()?.Name ?? "un item pianificato"}.";
        status = BuildControlledBatchFailure(
            selectedItems,
            status,
            reason,
            missing.FirstOrDefault() ?? selectedItems.FirstOrDefault());
        foreach (var item in missing.Skip(1))
            status.Results.Add(CreateInfrastructureFailure(item, reason));
        return status;
    }

    internal static UpdateRunStatus BuildControlledBatchFailure(
        IReadOnlyList<UpdateItem> selectedItems,
        UpdateRunStatus status,
        string reason,
        UpdateItem? preferredItem = null)
    {
        var started = preferredItem ?? selectedItems.FirstOrDefault(item =>
            item.Id.Equals(status.CurrentItemId, StringComparison.OrdinalIgnoreCase));
        if (started is null && status.Results.Count > 0)
        {
            var last = status.Results[^1];
            started = selectedItems.FirstOrDefault(item => ResultMatches(item, last));
        }
        if (started is null)
            throw new InvalidOperationException(
                "Il runner si è interrotto prima di iniziare un item. " + reason);

        status.Results = status.Results
            .Where(result => selectedItems.Any(item => ResultMatches(item, result)))
            .GroupBy(ResultKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        var existing = status.Results.FirstOrDefault(result => ResultMatches(started, result));
        if (existing is null)
            status.Results.Add(CreateInfrastructureFailure(started, reason));
        else
        {
            existing.Diagnostics = string.IsNullOrWhiteSpace(existing.Diagnostics)
                ? reason
                : existing.Diagnostics + Environment.NewLine + Environment.NewLine + reason;
        }

        status.State = "Failed";
        status.Message = LocalizationService.Text("Operazione interrotta da un errore infrastrutturale controllato.", "Operation stopped by a controlled infrastructure error.");
        status.CurrentItemStartedUtc = null;
        return status;
    }

    private static ItemRunResult CreateInfrastructureFailure(UpdateItem item, string reason) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Kind = item.Kind.ToString(),
        Success = false,
        InstallerSucceeded = false,
        Verified = false,
        VerificationStatus = UpdateVerificationStatuses.NotRun,
        Phase = "status-channel",
        FailureReason = UpdateFailureReasons.Infrastructure,
        Outcome = UpdateOutcomes.Failed,
        Message = LocalizationService.Text("Aggiornamento interrotto da un errore infrastrutturale controllato.", "Update stopped by a controlled infrastructure error."),
        Diagnostics = reason
    };

    private static bool ResultMatches(UpdateItem item, ItemRunResult result) =>
        item.Id.Equals(result.Id, StringComparison.OrdinalIgnoreCase) &&
        item.Kind.ToString().Equals(result.Kind, StringComparison.OrdinalIgnoreCase);

    private static string ResultKey(ItemRunResult result) =>
        $"{result.Kind.Trim()}\u001f{result.Id.Trim()}";

    private static string BuildWatchdogDiagnostics(
        int runnerProcessId,
        UpdateRunStatus status,
        UpdateWatchdogDecision decision,
        string reason) =>
        $"runnerPid={runnerProcessId}; tool={status.InstallerTool}; phase={status.Phase}; " +
        $"heartbeatAge={decision.HeartbeatAge}; progressAge={decision.ProgressAge}; " +
        $"itemDuration={decision.ItemDuration}; reason={reason}";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public sealed class UpdatePauseController
{
    public UpdatePauseController() : this(AppPaths.DataDirectory)
    {
        AppPaths.EnsureCreated();
    }

    internal UpdatePauseController(string signalDirectory) =>
        SignalPath = Path.Combine(signalDirectory, $"update-pause-{Guid.NewGuid():N}.signal");

    public string SignalPath { get; }
    public bool IsPauseRequested => File.Exists(SignalPath);

    public void RequestPause()
    {
        var temporary = SignalPath + ".tmp";
        File.WriteAllText(temporary, $"{Environment.ProcessId}|{DateTime.UtcNow:O}");
        File.Move(temporary, SignalPath, true);
    }

    public void Resume()
    {
        try { if (File.Exists(SignalPath)) File.Delete(SignalPath); } catch { }
    }

    public void Cleanup()
    {
        Resume();
        try { if (File.Exists(SignalPath + ".tmp")) File.Delete(SignalPath + ".tmp"); } catch { }
    }
}
