using UpdateCenter.Contracts;

namespace UpdateCenter.Core;

public static class RemoteUpdateSelectionValidator
{
    public static IReadOnlyList<RemoteUpdateItem> Validate(
        AgentOperation? scanOperation,
        RemoteUpdateRequest? request,
        DateTime utcNow)
    {
        if (request is null || request.ScanOperationId == Guid.Empty)
            throw new RemoteUpdateValidationException("MissingScan", "La scansione di origine non è specificata.");
        if (request.Items.Count is 0 or > AgentProtocol.MaximumUpdateItems)
            throw new RemoteUpdateValidationException("InvalidSelection", "Selezione aggiornamenti vuota o troppo grande.");
        if (scanOperation?.ScanResult is null || !AgentOperationStates.IsTerminal(scanOperation.State))
            throw new RemoteUpdateValidationException("ScanNotAvailable", "La scansione di origine non è disponibile o non è terminata.");
        if (scanOperation.Id != request.ScanOperationId)
            throw new RemoteUpdateValidationException("ScanNotAvailable", "L'identificativo della scansione non corrisponde.");
        if (scanOperation.ScanResult.CompletedUtc < utcNow.AddHours(-2))
            throw new RemoteUpdateValidationException("ScanExpired", "La scansione ha più di due ore. Esegui una nuova scansione prima di aggiornare.");

        var available = new Dictionary<string, RemoteUpdateItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in scanOperation.ScanResult.Updates)
            available.TryAdd(Key(item.Kind, item.Id), item);
        var selected = new List<RemoteUpdateItem>(request.Items.Count);
        var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in request.Items)
        {
            var key = Key(selection.Kind, selection.Id);
            if (!selectedKeys.Add(key)) continue;
            if (!available.TryGetValue(key, out var item))
                throw new RemoteUpdateValidationException("UpdateNotInScan", $"{selection.Id} non appartiene alla scansione indicata.");
            if (!item.CanInstall)
                throw new RemoteUpdateValidationException("UpdateNotInstallable", $"{item.Name} non è installabile automaticamente.");
            if (item.RequiresRiskConfirmation && !selection.RiskConfirmed)
                throw new RemoteUpdateValidationException("RiskConfirmationRequired", $"{item.Name} richiede una conferma esplicita.");
            selected.Add(item);
        }
        if (selected.Count == 0)
            throw new RemoteUpdateValidationException("InvalidSelection", "Nessun aggiornamento valido selezionato.");
        return selected;
    }

    private static string Key(string kind, string id) => $"{kind}\n{id}";
}

public sealed class RemoteUpdateValidationException(string errorCode, string message)
    : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}
