using UpdateCenter.ViewModels;

namespace UpdateCenter.Services;

public sealed record RemoteUpdateConfirmationSummary(
    string PowerStatus,
    string DiskStatus,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> RiskItems);

public static class RemoteUpdateConfirmationService
{
    public static RemoteUpdateConfirmationSummary Build(
        IReadOnlyList<RemoteUpdateSelectionItem> items,
        IReadOnlyList<NetworkAgentItem> agents)
    {
        var agentById = agents.GroupBy(x => x.AgentId).ToDictionary(x => x.Key, x => x.First());
        var portable = agents.Where(x => x.HasBattery)
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var powerStatus = portable.Count == 0
            ? "Nessun PC portatile rilevato tra quelli selezionati."
            : string.Join("\n", portable.Select(x => x.IsOnBattery
                ? $"{x.DisplayName}: a batteria{BatterySuffix(x)}"
                : $"{x.DisplayName}: alimentatore collegato{BatterySuffix(x)}"));

        var diskStatus = string.Join("\n", items
            .GroupBy(x => x.AgentId)
            .OrderBy(group => agentById.TryGetValue(group.Key, out var agent)
                ? agent.DisplayName
                : group.First().DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => BuildDiskSummary(group, agentById)));
        var warnings = portable.Where(x => x.IsOnBattery)
            .Select(x => $"{x.DisplayName} è un portatile alimentato a batteria. " +
                         "Collega l'alimentatore prima di aggiornare driver o componenti importanti.")
            .ToList();
        var riskItems = items.Where(x => x.RequiresRiskConfirmation)
            .Select(x => $"{x.DeviceName} · {x.Name}").ToList();
        return new RemoteUpdateConfirmationSummary(powerStatus, diskStatus, warnings, riskItems);
    }

    private static string BuildDiskSummary(
        IGrouping<Guid, RemoteUpdateSelectionItem> group,
        IReadOnlyDictionary<Guid, NetworkAgentItem> agents)
    {
        var name = agents.TryGetValue(group.Key, out var agent) ? agent.DisplayName : group.First().DeviceName;
        if (agent is { ProtocolMinor: > 0 and < 3 })
            return $"{name}: aggiorna il componente di rete sul PC per leggere dimensioni e spazio libero";

        var knownBytes = group.Where(x => x.DownloadSizeBytes > 0)
            .Aggregate(0L, (total, item) => total > long.MaxValue - item.DownloadSizeBytes
                ? long.MaxValue
                : total + item.DownloadSizeBytes);
        var unknown = group.Count(x => x.DownloadSizeBytes <= 0);
        var packageText = unknown == 0
            ? $"pacchetti {PreflightService.FormatBytes(knownBytes)}"
            : knownBytes > 0
                ? $"peso noto {PreflightService.FormatBytes(knownBytes)} · {unknown} senza dimensione"
                : $"dimensione non dichiarata per {unknown} elementi";
        var freeText = agent?.SystemDriveFreeBytes > 0
            ? $" · liberi {PreflightService.FormatBytes(agent.SystemDriveFreeBytes)}"
            : " · spazio libero non disponibile";
        return $"{name}: {packageText}{freeText}";
    }

    private static string BatterySuffix(NetworkAgentItem agent) =>
        agent.BatteryPercentage is >= 0 and <= 100 ? $" · {agent.BatteryPercentage}%" : "";
}
