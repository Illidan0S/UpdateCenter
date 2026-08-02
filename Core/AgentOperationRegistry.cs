using System.Collections.Concurrent;
using UpdateCenter.Contracts;

namespace UpdateCenter.Core;

public sealed class AgentOperationRegistry
{
    private const int MaximumRetainedOperations = 256;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<Guid, AgentOperation> _operations = new();

    public AgentOperation Create(string kind, string message)
    {
        Trim();
        var now = DateTime.UtcNow;
        var operation = new AgentOperation
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            State = AgentOperationStates.Queued,
            Message = message,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        if (!_operations.TryAdd(operation.Id, operation))
            throw new InvalidOperationException("Impossibile creare l'operazione locale.");
        return operation;
    }

    public AgentOperation? Get(Guid id) => _operations.TryGetValue(id, out var operation) ? operation : null;

    public AgentOperation Update(
        Guid id,
        string state,
        string message,
        ScanResult? scanResult = null,
        RemoteUpdateResult? updateResult = null,
        int? currentIndex = null,
        int? total = null,
        string? currentItemName = null,
        string? phase = null,
        double? currentItemProgress = null,
        bool? restartRequired = null)
    {
        while (_operations.TryGetValue(id, out var current))
        {
            var updated = new AgentOperation
            {
                Id = current.Id,
                Kind = current.Kind,
                State = state,
                Message = message,
                CreatedUtc = current.CreatedUtc,
                UpdatedUtc = DateTime.UtcNow,
                ScanResult = scanResult ?? current.ScanResult,
                UpdateResult = updateResult ?? current.UpdateResult,
                CurrentIndex = currentIndex ?? current.CurrentIndex,
                Total = total ?? current.Total,
                CurrentItemName = currentItemName ?? current.CurrentItemName,
                Phase = phase ?? current.Phase,
                CurrentItemProgress = currentItemProgress ?? current.CurrentItemProgress,
                RestartRequired = restartRequired ?? current.RestartRequired
            };
            if (_operations.TryUpdate(id, updated, current))
                return updated;
        }
        throw new KeyNotFoundException("Operazione locale non trovata.");
    }

    public IReadOnlyList<AgentOperation> Snapshot() => _operations.Values
        .OrderByDescending(x => x.CreatedUtc)
        .ToList();

    public void Restore(IEnumerable<AgentOperation> persistedOperations)
    {
        foreach (var persisted in persistedOperations
                     .OrderByDescending(x => x.CreatedUtc)
                     .Take(MaximumRetainedOperations))
        {
            var operation = AgentOperationStates.IsTerminal(persisted.State)
                ? persisted
                : new AgentOperation
                {
                    Id = persisted.Id,
                    Kind = persisted.Kind,
                    State = AgentOperationStates.Failed,
                    Message = "Operazione interrotta dal riavvio dell'Agent.",
                    CreatedUtc = persisted.CreatedUtc,
                    UpdatedUtc = DateTime.UtcNow,
                    ScanResult = persisted.ScanResult,
                    UpdateResult = persisted.UpdateResult,
                    CurrentIndex = persisted.CurrentIndex,
                    Total = persisted.Total,
                    CurrentItemName = persisted.CurrentItemName,
                    Phase = persisted.Phase,
                    CurrentItemProgress = persisted.CurrentItemProgress,
                    RestartRequired = persisted.RestartRequired
                };
            _operations.TryAdd(operation.Id, operation);
        }
        Trim(reserveSlot: false);
    }

    private void Trim(bool reserveSlot = true)
    {
        var expiry = DateTime.UtcNow - Retention;
        foreach (var item in _operations.Values.Where(x =>
                     AgentOperationStates.IsTerminal(x.State) && x.UpdatedUtc < expiry))
            _operations.TryRemove(item.Id, out _);

        var overflow = _operations.Count - MaximumRetainedOperations + (reserveSlot ? 1 : 0);
        if (overflow <= 0) return;
        foreach (var item in _operations.Values
                     .Where(x => AgentOperationStates.IsTerminal(x.State))
                     .OrderBy(x => x.UpdatedUtc)
                     .Take(overflow))
            _operations.TryRemove(item.Id, out _);
    }
}
