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
        try
        {
            ValidatePlanPath(planPath);
            plan = JsonStorage.Read<UpdatePlan>(planPath)
                ?? throw new InvalidOperationException("Piano di aggiornamento non valido.");
            ValidateStatusPath(plan.StatusFile);

            status = new UpdateRunStatus
            {
                State = "Running",
                Total = plan.Items.Count,
                Message = "Preparazione aggiornamenti…",
                RestorePointRequested = plan.CreateRestorePoint
            };
            WriteStatus(plan.StatusFile, status);

            if (requireAdministrator && !IsAdministrator())
                throw new UnauthorizedAccessException("I privilegi di amministratore non sono stati concessi.");

            if (plan.CreateRestorePoint)
            {
                status.Message = "Creazione del punto di ripristino…";
                WriteStatus(plan.StatusFile, status);
                status.RestorePointCreated = TryCreateRestorePoint(out var restoreMessage);
                status.Message = restoreMessage;
                WriteStatus(plan.StatusFile, status);
            }

            for (var index = 0; index < plan.Items.Count; index++)
            {
                var item = plan.Items[index];
                status.CurrentIndex = index;
                status.CurrentName = item.Name;
                status.Phase = "Preparazione";
                status.CurrentItemProgress = 1;
                status.LastHeartbeatUtc = DateTime.UtcNow;
                status.Message = $"Aggiornamento di {item.Name}…";
                WriteStatus(plan.StatusFile, status);

                void ReportItemProgress(int percent, string message)
                {
                    status.CurrentItemProgress = Math.Clamp(percent, 1, 99);
                    status.Phase = message;
                    status.Message = message;
                    status.LastHeartbeatUtc = DateTime.UtcNow;
                    WriteStatus(plan.StatusFile, status);
                }

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

                status.Results.Add(result);
                status.RestartRequired |= result.RestartRequired;
                status.CurrentIndex = index + 1;
                status.CurrentItemProgress = 100;
                status.Phase = "Completato";
                status.LastHeartbeatUtc = DateTime.UtcNow;
                status.Message = result.Message;
                WriteStatus(plan.StatusFile, status);
            }

            status.State = "Completed";
            status.CurrentName = "";
            status.Message = status.Results.All(x => x.Success)
                ? "Tutti gli aggiornamenti selezionati sono terminati."
                : "Operazione terminata: alcuni aggiornamenti richiedono attenzione.";
            WriteStatus(plan.StatusFile, status);
            return status.Results.All(x => x.Success) ? 0 : 1;
        }
        catch (Exception ex)
        {
            LogService.Write("Esecuzione elevata interrotta.", ex);
            if (plan is not null && status is not null)
            {
                status.State = "Failed";
                status.Message = ex.Message;
                try { WriteStatus(plan.StatusFile, status); } catch { }
            }
            return 1;
        }
    }

    private static ItemRunResult InstallSoftware(PlanItem item, bool silent)
    {
        try
        {
            var result = WinGetService.Upgrade(item, silent);
            var outcome = WinGetService.ClassifyOutcome(result);
            var output = string.Join(" ", (result.StandardOutput + "\n" + result.StandardError)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 2)
                .TakeLast(4));
            var alreadyCurrentMessage = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.Contains("risulta già aggiornato", StringComparison.OrdinalIgnoreCase));

            return new ItemRunResult
            {
                Id = item.Id,
                Name = item.Name,
                Kind = item.Kind,
                Success = !outcome.Equals(UpdateOutcomes.Failed, StringComparison.Ordinal),
                Outcome = outcome,
                Message = outcome switch
                {
                    UpdateOutcomes.Completed => alreadyCurrentMessage ?? "Software aggiornato con WinGet.",
                    UpdateOutcomes.NotApplicable => "La versione segnalata da WinGet non Ã¨ applicabile a questo PC. La voce verrÃ  rimossa fino alla prossima scansione.",
                    UpdateOutcomes.ManualRequired => "Questo pacchetto non supporta l'aggiornamento automatico con la tecnologia di installazione corrente. Usa l'installer ufficiale del produttore.",
                    _ => string.IsNullOrWhiteSpace(output)
                        ? $"WinGet ha restituito il codice {result.ExitCode}."
                        : output
                },
                Diagnostics = BuildProcessDiagnostics(result)
            };
        }
        catch (Exception ex)
        {
            LogService.Write($"Errore aggiornamento software {item.Name}.", ex);
            return new ItemRunResult
            {
                Id = item.Id,
                Name = item.Name,
                Kind = item.Kind,
                Success = false,
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

    private static void WriteStatus(string path, UpdateRunStatus status) => JsonStorage.WriteAtomic(path, status);
}

public sealed class UpdateCoordinator
{
    public async Task<UpdateRunStatus> RunAsync(
        IReadOnlyList<UpdateItem> selectedItems,
        AppSettings settings,
        Action<UpdateRunStatus> progress,
        CancellationToken cancellationToken)
    {
        if (selectedItems.Any(x => !x.CanInstall))
            throw new InvalidOperationException(
                "Gli elementi non installabili automaticamente non possono essere avviati.");

        AppPaths.EnsureCreated();
        var software = selectedItems.Where(x => x.Kind == UpdateKind.Software).ToList();
        var drivers = selectedItems.Where(x => x.Kind == UpdateKind.Driver).ToList();
        var aggregate = new UpdateRunStatus
        {
            State = "Running",
            Total = selectedItems.Count,
            Message = "Preparazione aggiornamenti…"
        };

        if (software.Count > 0)
        {
            var softwareResult = await RunBatchAsync(
                software, settings, requireAdministrator: false, aggregate.Results.Count,
                aggregate, progress, cancellationToken);
            MergeBatch(aggregate, softwareResult);
        }

        if (drivers.Count > 0)
        {
            var driverResult = await RunBatchAsync(
                drivers, settings, requireAdministrator: true, aggregate.Results.Count,
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

    private static async Task<UpdateRunStatus> RunBatchAsync(
        IReadOnlyList<UpdateItem> selectedItems,
        AppSettings settings,
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
            Items = selectedItems.Select(x => new PlanItem
            {
                Id = x.Id,
                Name = x.Name,
                Kind = x.Kind.ToString(),
                Source = x.Source,
                InstalledVersion = x.InstalledVersion,
                AvailableVersion = x.AvailableVersion,
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
            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = JsonStorage.Read<UpdateRunStatus>(statusPath);
                if (current is not null)
                {
                    latest = current;
                    progress(BuildAggregateProgress(aggregate, current, completedBeforeBatch));
                    if (current.State.Equals("Running", StringComparison.OrdinalIgnoreCase) &&
                        DateTime.UtcNow - current.LastHeartbeatUtc > TimeSpan.FromMinutes(12))
                    {
                        try { process.Kill(true); } catch { }
                        throw new TimeoutException(
                            $"L'aggiornamento di {current.CurrentName} non ha comunicato progressi per 12 minuti ed Ã¨ stato interrotto.");
                    }
                }
                await Task.Delay(350, cancellationToken);
            }

            UpdateRunStatus final = JsonStorage.Read<UpdateRunStatus>(statusPath)
                ?? latest
                ?? throw new InvalidOperationException("Il processo di aggiornamento non ha restituito uno stato.");
            if (final.State.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
                final.State.Equals("Starting", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Il processo di aggiornamento di {final.CurrentName} si Ã¨ chiuso prima di restituire un risultato.");
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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
