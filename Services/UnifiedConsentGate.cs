using System.Text.Json;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

// Volatile, one invocation only. Never serialized as evidence of authorization.
internal sealed class UnifiedConsentGate(IReadOnlyList<RunnerPlanSnapshot> plans)
{
    private readonly RunnerPlanSnapshot[] _plans = plans.ToArray();
    private readonly HashSet<int> _consumed = [];
    private bool _decided;
    private bool _approved;

    internal void Revoke() => _approved = false;

    internal bool Confirm(Func<IReadOnlyList<RunnerPlanSnapshot>, bool> showSummary,
        Action showProgress, Func<bool> stillValid)
    {
        if (_decided) throw RestartRequired();
        _decided = true;
        if (_plans.Length == 0 || !stillValid()) throw RestartRequired();
        if (!showSummary(Array.AsReadOnly(_plans))) return false;
        if (!stillValid()) throw RestartRequired();
        showProgress(); // A failed UI transition cannot authorize installation.
        _approved = true;
        return true;
    }

    internal void Consume(int index, RunnerPlanSnapshot actual)
    {
        if (!_approved || index < 0 || index >= _plans.Length || _consumed.Contains(index) ||
            Canonical(_plans[index]) != Canonical(actual))
        {
            _approved = false;
            throw RestartRequired();
        }
        _consumed.Add(index);
    }

    internal static string Canonical(RunnerPlanSnapshot plan) => JsonSerializer.Serialize(new
    {
        plan.Interactive, plan.Recovery,
        Plan = ToPlan(plan)
    });

    internal static UpdatePlan ToPlan(RunnerPlanSnapshot plan) => new()
    {
        CreateRestorePoint = plan.CreateRestorePoint, SilentSoftwareInstall = plan.SilentSoftwareInstall,
        StatusFile = plan.StatusFile, PauseFile = plan.PauseFile, PauseOwnerProcessId = plan.PauseOwnerProcessId,
        Items = plan.Items.Select(item => item.ToPlanItem()).ToList()
    };

    internal static InvalidOperationException RestartRequired() => new(LocalizationService.Text(
        "Il piano o la sessione di autorizzazione è cambiato. Nessun nuovo avvio autorizzato: seleziona di nuovo gli aggiornamenti e ripeti il riepilogo.",
        "The plan or authorization session changed. No new execution authorized: select the updates again and review a new summary."));
}
