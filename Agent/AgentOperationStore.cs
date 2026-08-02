using System.Text.Json;
using Microsoft.Extensions.Hosting.WindowsServices;
using UpdateCenter.Contracts;

namespace UpdateCenter.Agent;

public sealed class AgentOperationStore(ILogger<AgentOperationStore> logger)
{
    private const int MaximumRetainedFiles = 256;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _operationsDirectory = AgentDataPaths.OperationsDirectory;

    public IReadOnlyList<AgentOperation> Load()
    {
        Directory.CreateDirectory(_operationsDirectory);
        var loaded = new List<AgentOperation>();
        foreach (var path in Directory.EnumerateFiles(_operationsDirectory, "operation-*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(MaximumRetainedFiles))
        {
            try
            {
                var operation = JsonSerializer.Deserialize<AgentOperation>(File.ReadAllBytes(path), JsonOptions);
                if (operation is not null && operation.Id != Guid.Empty)
                    loaded.Add(operation);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Stato operazione locale non leggibile: {Path}", path);
            }
        }
        return loaded;
    }

    public async Task SaveAsync(AgentOperation operation, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_operationsDirectory);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = Path.Combine(_operationsDirectory, $"operation-{operation.Id:N}.json");
            var temporary = path + ".tmp";
            var payload = JsonSerializer.SerializeToUtf8Bytes(operation, JsonOptions);
            if (payload.Length > AgentProtocol.MaximumMessageBytes)
            {
                operation = new AgentOperation
                {
                    Id = operation.Id,
                    Kind = operation.Kind,
                    State = operation.State,
                    Message = operation.Message + " Il risultato dettagliato supera il limite di persistenza.",
                    CreatedUtc = operation.CreatedUtc,
                    UpdatedUtc = operation.UpdatedUtc,
                    CurrentIndex = operation.CurrentIndex,
                    Total = operation.Total,
                    CurrentItemName = operation.CurrentItemName,
                    Phase = operation.Phase,
                    CurrentItemProgress = operation.CurrentItemProgress,
                    RestartRequired = operation.RestartRequired
                };
                payload = JsonSerializer.SerializeToUtf8Bytes(operation, JsonOptions);
            }
            await File.WriteAllBytesAsync(temporary, payload, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
            TrimFiles();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void TrimFiles()
    {
        foreach (var path in Directory.EnumerateFiles(_operationsDirectory, "operation-*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(MaximumRetainedFiles))
        {
            try { File.Delete(path); }
            catch (Exception ex) { logger.LogDebug(ex, "Pulizia stato operazione non riuscita: {Path}", path); }
        }

        foreach (var path in Directory.EnumerateFiles(_operationsDirectory, "*.tmp"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-1)) File.Delete(path);
            }
            catch (Exception ex) { logger.LogDebug(ex, "Pulizia file temporaneo non riuscita: {Path}", path); }
        }
    }

}
