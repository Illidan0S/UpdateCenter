using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal static class DeferredWinGetVerification
{
    internal static Task<bool> ReconcileAsync(IEnumerable<HistoryEntry> entries, CancellationToken token) =>
        ReconcileAsync(entries, (plan, ct) => Task.Run(() => WinGetService.VerifyInstallation(plan, token: ct), ct), token);

    internal static async Task<bool> ReconcileAsync(IEnumerable<HistoryEntry> entries,
        Func<PlanItem, CancellationToken, Task<UpdateVerificationResult>> verify, CancellationToken token)
    {
        var changed = false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMinutes(2));
        foreach (var entry in entries.Where(x => x.PendingVerificationPlan is not null)
                     .OrderBy(x => x.LastVerificationAttemptUtc ?? DateTime.MinValue).Take(20).ToArray())
        {
            token.ThrowIfCancellationRequested();
            if (budget.IsCancellationRequested) break;
            entry.LastVerificationAttemptUtc = DateTime.UtcNow;
            changed = true;
            var plan = new RunnerPlanItem(entry.PendingVerificationPlan!).ToPlanItem();
            if (WinGetService.ValidateTarget(plan) is not null) continue;
            UpdateVerificationResult evidence;
            try { evidence = await verify(plan, budget.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && budget.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                entry.LastVerificationMessage = LocalizationService.Text("Verifica automatica non disponibile; nuovo tentativo al prossimo avvio.",
                    "Automatic verification unavailable; retry on the next launch.");
                entry.LastVerificationDiagnostics = ex.ToString();
                entry.NotifyLanguageChanged();
                LogService.Write("Verifica differita WinGet non disponibile.", ex);
                continue;
            }
            token.ThrowIfCancellationRequested();
            entry.LastVerificationMessage = evidence.Message;
            entry.LastVerificationDiagnostics = evidence.Diagnostics;
            entry.NotifyLanguageChanged();
            LogService.Write($"Verifica automatica differita {plan.Id}: {evidence.Status}; {evidence.Message}\n{evidence.Diagnostics}");
            if (!evidence.Verified || !evidence.IsDefinitive || evidence.Status != UpdateVerificationStatuses.Verified) continue;
            entry.VerifiedLaterUtc = DateTime.UtcNow;
            entry.Result = LocalizationService.Text("Riuscito", "Succeeded");
            entry.Details += Environment.NewLine + LocalizationService.Text(
                "Verifica differita: target confermato sulla copia approvata; nessun nuovo installer avviato.",
                "Deferred verification: target confirmed on the approved installation; no new installer started.");
            entry.Diagnostics += $"\n\nDeferred verification UTC={entry.VerifiedLaterUtc:O}:\n{evidence.Diagnostics}";
            entry.PendingVerificationPlan = null;
            entry.NotifyLanguageChanged();
            changed = true;
        }
        return changed;
    }
}
