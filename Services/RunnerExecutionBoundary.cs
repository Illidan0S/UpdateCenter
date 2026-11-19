using UpdateCenter.Models;

namespace UpdateCenter.Services;

// Internal dependency boundaries. Production consent is real WPF or an authenticated
// live UI channel; CLI/files/environment cannot select simulated consent or effects.
internal interface IRunnerConsent
{
    bool Confirm(RunnerPlanSnapshot plan, CancellationToken cancellationToken);
}

internal interface IRunnerEffects
{
    bool IsAdministrator { get; }
    (bool Created, string Message) CreateRestorePoint();
    ItemRunResult InstallSoftware(PlanItem item, bool silent, bool interactive);
    ItemRunResult InstallSoftware(PlanItem item, bool silent, bool interactive,
        CancellationToken token, Action<string> phase)
    {
        token.ThrowIfCancellationRequested();
        return InstallSoftware(item, silent, interactive);
    }
    ItemRunResult InstallDriver(PlanItem item, Action<int, string> progress);
}
