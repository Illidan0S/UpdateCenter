using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class ElevatedUpdateRunner
{
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
                ?? throw new InvalidOperationException("Piano di aggiornamento non valido.");
            ValidateStatusPath(plan.StatusFile);
            ValidatePausePath(plan.PauseFile);

            status = new UpdateRunStatus
            {
                State = "Running",
                Total = plan.Items.Count,
                Message = "Preparazione aggiornamenti…",
                RestorePointRequested = plan.CreateRestorePoint
            };
            publisher = new RunnerStatusPublisher(plan.StatusFile, status);

            if (requireAdministrator && !IsAdministrator())
                throw new UnauthorizedAccessException("I privilegi di amministratore non sono stati concessi.");

            if (plan.CreateRestorePoint)
            {
                publisher.Update(current =>
                {
                    current.Phase = "restore-point";
                    current.Message = "Creazione del punto di ripristino…";
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
                    current.Message = $"Aggiornamento di {item.Name}…";
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
                    ReportItemProgress(12, "Avvio dell'aggiornamento software con WinGet...");
                    result = InstallSoftware(item, plan.SilentSoftwareInstall);
                }

                LogService.WriteEvent(
                    "update",
                    string.IsNullOrWhiteSpace(result.Phase) ? "result" : result.Phase,
                    result.Success
                        ? "success"
                        : result.InstallerSucceeded
                            ? "verification-failure"
                            : "failure",
                    result.Id,
                    result.ResultCode,
                    $"installerSucceeded={result.InstallerSucceeded}; " +
                    $"verification={result.VerificationStatus}; verified={result.Verified}; {result.Message}");
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

            publisher.Update(current =>
            {
                current.State = "Completed";
                current.CurrentName = "";
                current.Message = current.Results.All(x => x.Success)
                    ? "Tutti gli aggiornamenti selezionati sono terminati."
                    : "Operazione terminata: alcuni aggiornamenti richiedono attenzione.";
            }, markProgress: true);
            return status.Results.All(x => x.Success) ? 0 : 1;
        }
        catch (Exception ex)
        {
            LogService.Write("Esecuzione elevata interrotta.", ex);
            if (publisher is not null)
            {
                try
                {
                    publisher.Update(current =>
                    {
                        current.State = "Failed";
                        current.Message = ex.Message;
                    });
                }
                catch { }
            }
            return 1;
        }
        finally
        {
            publisher?.Dispose();
        }
    }

    private static ItemRunResult InstallSoftware(PlanItem item, bool silent)
    {
        try
        {
            var isFreshInstall = item.PackageOperation.Equals(PackageOperations.Install, StringComparison.Ordinal);
            var result = isFreshInstall
                ? WinGetService.Install(item, silent)
                : WinGetService.Upgrade(item, silent);
            var installerOutcome = WinGetService.ClassifyOutcome(result);
            var restartRequired = WinGetService.RequiresRestart(result);
            var installerSucceeded = installerOutcome.Equals(UpdateOutcomes.Completed, StringComparison.Ordinal) ||
                                     restartRequired;
            var shouldVerify = installerOutcome.Equals(UpdateOutcomes.Completed, StringComparison.Ordinal) ||
                               installerOutcome.Equals(UpdateOutcomes.Failed, StringComparison.Ordinal);
            var verification = shouldVerify
                ? WinGetService.VerifyInstallation(item)
                : new UpdateVerificationResult
                {
                    Status = UpdateVerificationStatuses.NotRun,
                    Message = "Verifica post-installazione non richiesta per questo esito."
                };
            var decision = UpdateResultPolicy.Resolve(installerSucceeded, restartRequired, verification);
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
                Phase = isFreshInstall ? "winget-install" : "winget-upgrade",
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
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _heartbeatTask;

        public RunnerStatusPublisher(
            string statusPath,
            UpdateRunStatus status,
            TimeSpan? heartbeatInterval = null)
        {
            _statusPath = statusPath;
            _status = status;
            _heartbeatInterval = heartbeatInterval ?? HeartbeatInterval;
            Update(_ => { });
            _heartbeatTask = Task.Run(PublishHeartbeatAsync);
        }

        public void Update(Action<UpdateRunStatus> update, bool markProgress = false)
        {
            lock (_sync)
            {
                update(_status);
                var now = DateTime.UtcNow;
                _status.LastHeartbeatUtc = now;
                if (markProgress)
                    _status.LastProgressUtc = now;
                JsonStorage.WriteAtomic(_statusPath, _status);
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
                JsonStorage.WriteAtomic(_statusPath, _status);
            }
        }

        private async Task PublishHeartbeatAsync()
        {
            using var timer = new PeriodicTimer(_heartbeatInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                {
                    try { Update(_ => { }); }
                    catch (Exception ex)
                    {
                        LogService.WriteEvent(
                            "watchdog", "runner-heartbeat", "write-failure",
                            resultCode: ex.HResult,
                            details: ex.Message,
                            exception: ex);
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _heartbeatTask.GetAwaiter().GetResult(); } catch { }
            _stop.Dispose();
        }
    }
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
    public async Task<UpdateRunStatus> RunAsync(
        IReadOnlyList<UpdateItem> selectedItems,
        AppSettings settings,
        UpdatePauseController pauseController,
        Action<UpdateRunStatus> progress,
        CancellationToken cancellationToken)
    {
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

        try
        {
            if (software.Count > 0)
            {
                var softwareResult = await RunBatchAsync(
                    software, settings, pauseController, requireAdministrator: false, aggregate.Results.Count,
                    aggregate, progress, cancellationToken);
                MergeBatch(aggregate, softwareResult);
            }

            if (drivers.Count > 0)
            {
                var driverResult = await RunBatchAsync(
                    drivers, settings, pauseController, requireAdministrator: true, aggregate.Results.Count,
                    aggregate, progress, cancellationToken);
                MergeBatch(aggregate, driverResult);
            }

            aggregate.State = "Completed";
            aggregate.CurrentIndex = aggregate.Results.Count;
            aggregate.CurrentName = "";
            aggregate.Message = aggregate.Results.All(x => x.Success)
                ? "Tutti gli aggiornamenti selezionati sono terminati."
                : "Operazione terminata: alcuni aggiornamenti richiedono attenzione.";
            progress(aggregate);
            return aggregate;
        }
        finally
        {
            pauseController.Cleanup();
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
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var planPath = Path.Combine(AppPaths.DataDirectory, $"update-plan-{token}.json");
        var statusPath = Path.Combine(AppPaths.DataDirectory, $"update-status-{token}.json");
        var plan = new UpdatePlan
        {
            CreateRestorePoint = PreflightService.ShouldCreateRestorePoint(selectedItems, settings),
            SilentSoftwareInstall = settings.SilentSoftwareInstall,
            StatusFile = statusPath,
            PauseFile = pauseController.SignalPath,
            PauseOwnerProcessId = Environment.ProcessId,
            Items = selectedItems.Select(x => new PlanItem
            {
                Id = x.Id,
                Name = x.Name,
                Kind = x.Kind.ToString(),
                Source = x.Source,
                InstalledVersion = x.InstalledVersion,
                AvailableVersion = x.AvailableVersion,
                PackageOperation = x.PackageOperation,
                WindowsUpdateId = x.WindowsUpdateId,
                WindowsUpdateRevision = x.WindowsUpdateRevision,
                WindowsUpdateServerSelection = x.WindowsUpdateServerSelection,
                WindowsUpdateServiceId = x.WindowsUpdateServiceId,
                DriverInstallMode = x.DriverInstallMode,
                Vendor = x.Publisher,
                OfficialReleasePageUrl = x.OfficialReleasePageUrl,
                OfficialDownloadUrl = x.OfficialDownloadUrl,
                ExpectedSha256 = x.ExpectedSha256,
                ExpectedSignerSubjects = x.ExpectedSignerSubjects,
                DriverPackageType = x.DriverPackageType,
                CompatibleHardwareIds = x.CompatibleHardwareIds
            }).ToList()
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
                        throw new TimeoutException(timeoutMessage);
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

            UpdateRunStatus final = JsonStorage.Read<UpdateRunStatus>(statusPath)
                ?? latest
                ?? throw new InvalidOperationException("Il processo di aggiornamento non ha restituito uno stato.");
            if (final.State.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
                final.State.Equals("Starting", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Il processo di aggiornamento di {final.CurrentName} si è chiuso prima di restituire un risultato.");
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
        Results = aggregate.Results.Concat(batch.Results).ToList()
    };

    private static void MergeBatch(UpdateRunStatus aggregate, UpdateRunStatus batch)
    {
        aggregate.Results.AddRange(batch.Results);
        aggregate.RestorePointRequested |= batch.RestorePointRequested;
        aggregate.RestorePointCreated |= batch.RestorePointCreated;
        aggregate.RestartRequired |= batch.RestartRequired;
    }

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
