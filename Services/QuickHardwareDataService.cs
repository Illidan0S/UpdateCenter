using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class QuickHardwareDataService
{
    private static readonly TimeSpan LocalInventoryTimeout = TimeSpan.FromSeconds(30);
    private readonly HardwareInventoryService _hardwareInventory;
    private readonly StorageHealthService _storageHealth;

    public QuickHardwareDataService(
        HardwareInventoryService hardwareInventory,
        StorageHealthService storageHealth)
    {
        _hardwareInventory = hardwareInventory;
        _storageHealth = storageHealth;
    }

    public async Task<QuickHardwareSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hardwareTask = CaptureAsync(
            token => _hardwareInventory.ScanAsync(token),
            "inventario locale dei driver",
            cancellationToken, "quick.hardware");
        var storageTask = CaptureAsync(
            token => _storageHealth.ScanAsync(token),
            "inventario locale dello storage",
            cancellationToken, "quick.storage");

        await Task.WhenAll(hardwareTask, storageTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new QuickHardwareSnapshot(
            await hardwareTask.ConfigureAwait(false),
            await storageTask.ConfigureAwait(false));
    }

    internal static async Task<T?> CaptureAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken,
        string diagnosticPhase = "quick.capture")
        where T : class
    {
        using var phase = ScanDiagnostics.Begin(diagnosticPhase, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LocalInventoryTimeout);
        try
        {
            var result = await operation(timeout.Token).ConfigureAwait(false);
            phase?.Complete();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            ScanDiagnostics.Count("quick.timeouts", 1);
            phase?.Complete("partial");
            LogService.Write($"Timeout durante {operationName}; Update Center continuerà con i dati disponibili.");
            return null;
        }
        catch (Exception ex)
        {
            ScanDiagnostics.Count("quick.errors", 1);
            phase?.Complete("partial");
            LogService.Write($"{operationName} non completato; Update Center continuerà con i dati disponibili.", ex);
            return null;
        }
    }
}
