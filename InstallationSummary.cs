using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter;

// Display hints captured before preparation yields. They never authorize effects.
// Exact target matching prevents hints from being attached to a different plan.
internal sealed class InstallationSummaryHint
{
    private readonly string _target;
    internal bool RemovesPreviousVersion { get; }
    internal bool RequiresRestart { get; }
    internal bool IsImportant { get; }
    internal string CompatibilityWarning { get; }

    internal InstallationSummaryHint(UpdateItem item)
    {
        _target = JsonSerializer.Serialize(ElevatedUpdateRunner.ToPlanItem(item));
        RemovesPreviousVersion = item.RequiresRiskConfirmation;
        RequiresRestart = item.RequiresRestart;
        IsImportant = item.IsImportant;
        // Existing compatibility descriptions also contain positive applicability
        // and hardware IDs. Only a reported incompatibility is a decision warning.
        var detail = item.CompatibilityDetail;
        CompatibilityWarning = new[] { "incompatib", "non compatibil", "not compatible", "non supportat", "unsupported" }
            .Any(term => detail.Contains(term, StringComparison.OrdinalIgnoreCase)) ? detail : "";
    }

    internal bool Matches(RunnerPlanItem item) => _target == JsonSerializer.Serialize(item.ToPlanItem());
}

internal sealed record InstallationSummaryRow(string Name, string InstalledVersion, string AvailableVersion,
    string SourceLabel, string Warning, bool IsImportant, string PermissionLabel)
{
    public bool HasWarning => Warning.Length > 0;

    internal static InstallationSummaryRow From(RunnerPlanItem frozen, InstallationSummaryHint? hint)
    {
        if (hint is not null && !hint.Matches(frozen)) throw UnifiedConsentGate.RestartRequired();
        var item = frozen.ToPlanItem();
        var warnings = new List<string>();
        if (item.RemovalManifestSha256.Length > 0 || item.WinGetBinding?.Safety == "RemovesPreviousVersion" || hint?.RemovesPreviousVersion == true)
            warnings.Add(LocalizationService.Text(
                "La versione attuale verrà rimossa prima dell'installazione. Se l'aggiornamento fallisce, potrebbe servire reinstallare il programma.",
                "The current version will be removed first. If the update fails, you may need to reinstall the app."));
        if (hint?.RequiresRestart == true)
            warnings.Add(LocalizationService.Text("Riavvio necessario.", "Restart required."));
        if (hint?.CompatibilityWarning is { Length: > 0 } compatibility)
            warnings.Add(LocalizationService.Translate(compatibility));
        var scope = item.WinGetBinding?.Scope switch
        {
            "user" => LocalizationService.Text("Utente corrente", "Current user"),
            "machine" => LocalizationService.Text("Tutti gli utenti", "All users"),
            _ => ""
        };
        return new(item.Name, item.InstalledVersion, item.AvailableVersion,
            scope.Length == 0 ? item.Source : item.Source + " · " + scope,
            string.Join(" ", warnings), hint?.IsImportant == true,
            item.Kind == nameof(UpdateKind.Driver)
                ? LocalizationService.Text("Richiede autorizzazione amministratore (UAC)", "Administrator approval required (UAC)")
                : item.WinGetBinding?.ElevationRequirement switch
                {
                    "elevationRequired" => LocalizationService.Text("Richiede autorizzazione amministratore (UAC)", "Administrator approval required (UAC)"),
                    "elevationProhibited" => LocalizationService.Text("Nessuna elevazione richiesta", "No elevation required"),
                    "elevatesSelf" => LocalizationService.Text("L'installer può richiedere UAC", "The installer may request UAC"),
                    _ => LocalizationService.Text("Requisiti amministratore non determinati", "Administrator requirements undetermined")
                });
    }
}
