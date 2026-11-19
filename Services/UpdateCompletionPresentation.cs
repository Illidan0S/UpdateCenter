using UpdateCenter.Models;

namespace UpdateCenter.Services;

// Presentation only: the terminal ledger and its six dispositions remain unchanged.
internal sealed record UpdateCompletionPresentation(string Title, string Detail)
{
    internal static UpdateCompletionPresentation From(IReadOnlyList<ItemRunResult> results)
    {
        var summary = UpdateSessionSummary.From(results);
        string Count(int value, string oneIt, string manyIt, string oneEn, string manyEn) =>
            LocalizationService.Text($"{value} {(value == 1 ? oneIt : manyIt)}", $"{value} {(value == 1 ? oneEn : manyEn)}");

        var title = summary.ProcessedTerminalCount == 0
            ? LocalizationService.Text("Nessun aggiornamento eseguito", "No updates performed")
            : summary.FailedCount > 0
                ? Count(summary.FailedCount, "aggiornamento non riuscito", "aggiornamenti non riusciti", "update failed", "updates failed")
            : summary.PendingRestartCount > 0
                ? LocalizationService.Text("Installazioni terminate · riavvio necessario", "Installations finished · restart needed")
            : summary.PendingVerificationCount > 0
                ? LocalizationService.Text("Installazioni terminate · controllo automatico da riprendere", "Installations finished · automatic check to resume")
            : summary.CancelledCount > 0
                ? Count(summary.CancelledCount, "aggiornamento annullato", "aggiornamenti annullati", "update cancelled", "updates cancelled")
            : summary.NotAutomaticallyApplicableCount > 0
                ? Count(summary.NotAutomaticallyApplicableCount, "aggiornamento non applicabile automaticamente", "aggiornamenti non applicabili automaticamente", "update not automatically applicable", "updates not automatically applicable")
            : Count(summary.SucceededCount, "aggiornamento completato e verificato", "aggiornamenti completati e verificati", "update completed and verified", "updates completed and verified");

        var details = new List<string>();
        if (summary.PendingVerificationCount > 0)
            details.Add(Count(summary.PendingVerificationCount,
                "verifica sarà ripresa automaticamente al prossimo avvio.", "verifiche saranno riprese automaticamente al prossimo avvio.",
                "check will resume automatically next time the app starts.", "checks will resume automatically next time the app starts."));
        if (summary.PendingRestartCount > 0)
            details.Add(Count(summary.PendingRestartCount,
                "verifica attende il riavvio di Windows.", "verifiche attendono il riavvio di Windows.",
                "check awaits a Windows restart.", "checks await a Windows restart."));
        if (summary.CancelledCount > 0 && summary.CancelledCount != summary.ProcessedTerminalCount)
            details.Add(Count(summary.CancelledCount, "aggiornamento annullato.", "aggiornamenti annullati.", "update cancelled.", "updates cancelled."));
        return new(title, string.Join(Environment.NewLine, details));
    }
}
