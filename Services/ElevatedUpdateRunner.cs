using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class ElevatedUpdateRunner
{
    public static int Run(string planPath)
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

            if (!IsAdministrator())
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
                status.Message = $"Aggiornamento di {item.Name}…";
                WriteStatus(plan.StatusFile, status);

                ItemRunResult result;
                if (item.Kind.Equals(nameof(UpdateKind.Driver), StringComparison.OrdinalIgnoreCase))
                {
                    result = item.DriverInstallMode.Equals(DriverInstallModes.OfficialInfPackage, StringComparison.Ordinal)
                        ? OfficialDriverPackageService.Install(item)
                        : WindowsUpdateService.InstallDriver(item);
                }
                else
                {
                    result = InstallSoftware(item, plan.SilentSoftwareInstall);
                }

                status.Results.Add(result);
                status.RestartRequired |= result.RestartRequired;
                status.CurrentIndex = index + 1;
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
            var output = string.Join(" ", (result.StandardOutput + "\n" + result.StandardError)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 2)
                .TakeLast(4));

            return new ItemRunResult
            {
                Id = item.Id,
                Name = item.Name,
                Kind = item.Kind,
                Success = result.Success,
                Message = result.Success ? "Software aggiornato con WinGet." :
                    (string.IsNullOrWhiteSpace(output) ? $"WinGet ha restituito il codice {result.ExitCode}." : output)
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
                Message = ex.Message
            };
        }
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
        AppPaths.EnsureCreated();
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
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--elevated-update");
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
                    progress(current);
                }
                await Task.Delay(350, cancellationToken);
            }

            UpdateRunStatus final = JsonStorage.Read<UpdateRunStatus>(statusPath)
                ?? latest
                ?? throw new InvalidOperationException("Il processo di aggiornamento non ha restituito uno stato.");
            progress(final);
            return final;
        }
        finally
        {
            process?.Dispose();
            TryDelete(planPath);
            TryDelete(statusPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
