using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace UpdateCenter.Services;

public sealed class DriverRepairRequest
{
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public string InfName { get; init; } = "";
    public string StatusFile { get; init; } = "";
}

public sealed class DriverRepairResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string Diagnostics { get; init; } = "";
}

public static class DriverRepairService
{
    public static async Task<DriverRepairResult> RunElevatedAsync(
        string deviceId,
        string deviceName,
        string infName,
        CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();
        var token = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(AppPaths.DataDirectory, $"driver-repair-{token}.json");
        var statusPath = Path.Combine(AppPaths.DataDirectory, $"driver-repair-status-{token}.json");
        JsonStorage.WriteAtomic(requestPath, new DriverRepairRequest
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            InfName = infName,
            StatusFile = statusPath
        });
        LogService.Write($"Riparazione driver richiesta per {deviceName} ({infName}).");

        Process? process = null;
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Percorso di Update Center non disponibile.");
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--repair-driver-admin");
            startInfo.ArgumentList.Add(requestPath);
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Impossibile avviare la riparazione del driver.");
                LogService.Write($"Processo amministratore avviato (PID {process.Id}).");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Autorizzazione amministratore annullata.", ex);
            }

            await process.WaitForExitAsync(cancellationToken);
            LogService.Write($"Processo amministratore terminato con codice {process.ExitCode}. " +
                             $"File esito presente: {File.Exists(statusPath)}.");
            var result = JsonStorage.Read<DriverRepairResult>(statusPath);
            if (result is not null)
                return result;

            return new DriverRepairResult
            {
                Message = "La procedura amministratore è terminata senza restituire un esito. " +
                          "Riprova e controlla il registro di Update Center."
            };
        }
        finally
        {
            process?.Dispose();
            TryDelete(requestPath);
            TryDelete(statusPath);
        }
    }

    public static int RunAdministrator(string requestPath)
    {
        DriverRepairRequest? request = null;
        try
        {
            LogService.Write("Processo amministratore per la riparazione driver avviato.");
            ValidateDataPath(requestPath, "driver-repair-", ".json");
            request = JsonStorage.Read<DriverRepairRequest>(requestPath)
                      ?? throw new InvalidOperationException("Richiesta di riparazione non valida.");
            ValidateDataPath(request.StatusFile, "driver-repair-status-", ".json");
            ValidateRequest(request);
            if (!IsAdministrator())
                throw new UnauthorizedAccessException("I privilegi di amministratore non sono stati concessi.");

            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var infPath = Path.GetFullPath(Path.Combine(windows, "INF", request.InfName));
            var infRoot = Path.GetFullPath(Path.Combine(windows, "INF")) + Path.DirectorySeparatorChar;
            if (!infPath.StartsWith(infRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(infPath))
                throw new FileNotFoundException("Il pacchetto driver registrato da Windows non è più disponibile.");

            var reinstall = ProcessRunner.RunAsync(
                "pnputil.exe",
                ["/add-driver", infPath, "/install"],
                CancellationToken.None,
                TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
            LogService.Write($"Riapplicazione INF terminata con codice {reinstall.ExitCode}.");
            // PnPUtil può restituire un codice diverso da zero quando l'INF è già
            // presente nel Driver Store ("pacchetti aggiunti: 0"). Non è un errore
            // conclusivo: verifichiamo lo stato PnP dopo riavvio e nuova scansione.
            var diagnostics = new List<string>
            {
                $"Registrazione INF (codice {reinstall.ExitCode}): {CompactOutput(reinstall)}"
            };
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                var restart = ProcessRunner.RunAsync(
                    "pnputil.exe",
                    ["/restart-device", request.DeviceId],
                    CancellationToken.None,
                    TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
                LogService.Write($"Riavvio dispositivo terminato con codice {restart.ExitCode}.");
                diagnostics.Add($"Riavvio dispositivo (codice {restart.ExitCode}): {CompactOutput(restart)}");
                var scan = ProcessRunner.RunAsync(
                    "pnputil.exe",
                    ["/scan-devices"],
                    CancellationToken.None,
                    TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
                LogService.Write($"Nuova scansione PnP terminata con codice {scan.ExitCode}.");
                diagnostics.Add($"Scansione dispositivi (codice {scan.ExitCode}): {CompactOutput(scan)}");
            }

            Thread.Sleep(1500);
            var inventory = new HardwareInventoryService().ScanAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            LogService.Write("Verifica hardware successiva alla riparazione completata.");
            var remaining = inventory.Problems.FirstOrDefault(x =>
                x.DeviceId.Equals(request.DeviceId, StringComparison.OrdinalIgnoreCase));
            var result = remaining is null
                ? new DriverRepairResult
                {
                    Success = true,
                    Message = $"Il driver di {request.DeviceName} è stato riapplicato e Windows non segnala più il problema.",
                    Diagnostics = string.Join("\n", diagnostics)
                }
                : new DriverRepairResult
                {
                    Message = $"Il driver è stato riapplicato, ma Windows segnala ancora il Codice {remaining.ErrorCode}. " +
                              "Esegui la ricerca aggiornamenti driver o usa il supporto ufficiale del produttore.",
                    Diagnostics = string.Join("\n", diagnostics)
                };
            JsonStorage.WriteAtomic(request.StatusFile, result);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            LogService.Write("Riparazione driver non riuscita.", ex);
            if (request is not null)
            {
                try
                {
                    JsonStorage.WriteAtomic(request.StatusFile, new DriverRepairResult
                    {
                        Message = ex.Message,
                        Diagnostics = ex.ToString()
                    });
                }
                catch { }
            }
            return 1;
        }
    }

    private static void ValidateRequest(DriverRepairRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 1_024 ||
            request.DeviceId.Any(char.IsControl))
            throw new InvalidDataException("Identificativo del dispositivo non valido.");
        if (!Regex.IsMatch(request.InfName, "^oem[0-9]{1,6}\\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("Update Center può reinstallare soltanto un pacchetto OEM già registrato e scelto da Windows.");
    }

    private static void ValidateDataPath(string path, string prefix, string extension)
    {
        AppPaths.EnsureCreated();
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetFullPath(AppPaths.DataDirectory) + Path.DirectorySeparatorChar;
        var fileName = Path.GetFileName(fullPath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Percorso della richiesta di riparazione non consentito.");
    }

    private static string CompactOutput(ProcessResult result)
    {
        var text = string.Join(" ", (result.StandardOutput + "\n" + result.StandardError)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).TakeLast(8));
        return string.IsNullOrWhiteSpace(text) ? $"PnPUtil: codice {result.ExitCode}." : text;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
