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
        var hardwareTask = CaptureAsync(
            token => _hardwareInventory.ScanAsync(token),
            "inventario locale dei driver",
            cancellationToken);
        var storageTask = CaptureAsync(
            token => _storageHealth.ScanAsync(token),
            "inventario locale dello storage",
            cancellationToken);

        await Task.WhenAll(hardwareTask, storageTask).ConfigureAwait(false);
        return new QuickHardwareSnapshot(
            await hardwareTask.ConfigureAwait(false),
            await storageTask.ConfigureAwait(false));
    }

    private static async Task<T?> CaptureAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LocalInventoryTimeout);
        try
        {
            return await operation(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogService.Write($"Timeout durante {operationName}; Update Center continuerà con i dati disponibili.");
            return null;
        }
        catch (Exception ex)
        {
            LogService.Write($"{operationName} non completato; Update Center continuerà con i dati disponibili.", ex);
            return null;
        }
    }
}
