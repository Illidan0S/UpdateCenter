using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal sealed record WinGetProcessCandidate(
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    WinGetBlockerClassification Classification = WinGetBlockerClassification.PackageOwned,
    string StableIdentity = "");

internal sealed record WinGetServiceCandidate(
    string ServiceName,
    string DisplayName,
    string ExecutablePath,
    bool WasRunning,
    bool IsDedicated,
    string StableIdentity);

internal sealed record WinGetRecoveryContext(
    string PackageId,
    IReadOnlyList<string> InstallRoots,
    IReadOnlyList<string> ExecutablePaths,
    IReadOnlyList<string> SharedResourceRoots,
    IReadOnlyList<string> SharedResources,
    IReadOnlyList<string> RegisteredResources,
    IReadOnlyList<WinGetProcessCandidate> FallbackCandidates);

internal enum WinGetBlockerClassification
{
    PackageOwned,
    ExternalConfirmed,
    RecurringProcess,
    ThirdPartyService,
    SystemOrShared,
    Unknown
}

internal enum WinGetStableIdentityKind
{
    None,
    ServiceName,
    ExecutablePath,
    ParentService
}

internal sealed record WinGetStableIdentity(WinGetStableIdentityKind Kind, string Value)
{
    public bool IsStable => Kind != WinGetStableIdentityKind.None && !string.IsNullOrWhiteSpace(Value);
    public string Key => IsStable ? $"{Kind}:{Value}" : "";
}

internal sealed record ClassifiedRestartManagerBlocker(
    RestartManagerBlocker Blocker,
    WinGetBlockerClassification Classification,
    WinGetStableIdentity Identity,
    WindowsServiceProcessSnapshot? LinkedService = null,
    bool Recreated = false);

internal enum WinGetRecoveryAction
{
    Retry,
    CloseConfirmedBlockers,
    StopConfirmedService,
    ManualIntervention,
    RestartRequired,
    RestartManagerUnavailable
}

internal sealed record WinGetRecoveryDecision(
    WinGetRecoveryAction Action,
    IReadOnlyList<ClassifiedRestartManagerBlocker> Blockers,
    string Reason);

internal sealed record WinGetRecoveryPreparation(
    bool ShouldRetry,
    string Diagnostics,
    WinGetRecoveryContext? Context = null,
    bool ShouldRunInteractive = false,
    IWinGetRecoveryLease? Lease = null);

internal sealed record WinGetPostRetryDiagnosis(
    string Diagnostics,
    bool ShouldRunInteractive);

internal interface IWinGetProcessRecoveryPrompt
{
    bool ConfirmGracefulClose(UpdateItem item, IReadOnlyList<WinGetProcessCandidate> candidates);
    bool ConfirmForcedTermination(UpdateItem item, IReadOnlyList<WinGetProcessCandidate> candidates);
    bool ConfirmTemporaryServiceStop(UpdateItem item, WinGetServiceCandidate service);
    bool ConfirmInteractiveInstaller(UpdateItem item);
    void ShowManualCloseRequired(UpdateItem item, string detail);
}

internal interface IWinGetRecoveryLease
{
    string Restore();
}

internal interface IWinGetRecoveryPollingDelay
{
    void Wait(TimeSpan delay);
}

internal sealed class WinGetRecoveryPollingDelay : IWinGetRecoveryPollingDelay
{
    public void Wait(TimeSpan delay) => Thread.Sleep(delay);
}

internal interface IWinGetProcessOperations
{
    WinGetRecoveryContext CreateContext(UpdateItem item);
    IReadOnlyList<WinGetProcessCandidate> CloseGracefully(
        IReadOnlyList<WinGetProcessCandidate> candidates, TimeSpan timeout);
    IReadOnlyList<WinGetProcessCandidate> Terminate(
        IReadOnlyList<WinGetProcessCandidate> candidates, TimeSpan timeout);
}

internal static class WinGetRecoveryDecisionPolicy
{
    public static WinGetRecoveryDecision Evaluate(
        RestartManagerQueryResult query,
        WinGetRecoveryContext context)
    {
        if (!query.Available || !query.Succeeded)
        {
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.RestartManagerUnavailable, [], query.Diagnostics);
        }

        var classified = query.Blockers
            .Select(blocker => ClassifyBlocker(blocker, context))
            .ToList();
        if (query.RebootReason != RestartManagerRebootReason.None)
        {
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.RestartRequired,
                classified,
                $"Restart Manager richiede un riavvio: {query.RebootReason}.");
        }
        if (classified.Any(x => x.Blocker.Liveness == RestartManagerProcessLiveness.Unknown))
        {
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.ManualIntervention,
                classified,
                "La liveness di almeno un record Restart Manager non è determinabile in sicurezza.");
        }

        var live = classified
            .Where(x => x.Blocker.Liveness == RestartManagerProcessLiveness.Live)
            .ToList();
        if (live.Any(x => x.Classification == WinGetBlockerClassification.SystemOrShared))
        {
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.ManualIntervention,
                classified,
                "Sono presenti processi di sistema o risorse condivise che UpdateCenter non può gestire.");
        }
        if (live.Any(x => x.Classification == WinGetBlockerClassification.Unknown))
        {
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.ManualIntervention,
                classified,
                "Sono presenti blocker non attribuibili in sicurezza.");
        }
        if (live.Count == 0)
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.Retry,
                classified,
                classified.Count == 0
                    ? "Restart Manager non rileva più blocker sulle risorse registrate."
                    : "I soli record Restart Manager residui sono verificati dead/stale.");
        if (live.All(x => x.Classification == WinGetBlockerClassification.ThirdPartyService))
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.StopConfirmedService,
                classified,
                "Un servizio di terze parti dedicato è concretamente collegato ai blocker.");
        if (live.All(x => x.Classification is WinGetBlockerClassification.PackageOwned or
                WinGetBlockerClassification.ExternalConfirmed or
                WinGetBlockerClassification.RecurringProcess))
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.CloseConfirmedBlockers,
                classified,
                "Tutti i blocker sono processi con identità eseguibile verificata.");
        return new WinGetRecoveryDecision(
            WinGetRecoveryAction.ManualIntervention,
            classified,
            "La combinazione di blocker richiede remediation diverse e non viene automatizzata.");
    }

    internal static ClassifiedRestartManagerBlocker ClassifyBlocker(
        RestartManagerBlocker blocker,
        WinGetRecoveryContext context)
    {
        var processName = !string.IsNullOrWhiteSpace(blocker.ExecutablePath)
            ? Path.GetFileNameWithoutExtension(blocker.ExecutablePath)
            : blocker.ApplicationName;
        if (blocker.ApplicationType is RestartManagerApplicationType.Critical or
                RestartManagerApplicationType.Explorer ||
            WinGetProcessOperations.IsNeverCloseProcess(processName) ||
            WinGetProcessOperations.IsProtectedOrSharedPath(blocker.ExecutablePath))
            return Classified(blocker, WinGetBlockerClassification.SystemOrShared);

        var linkedService = FindConcreteService(blocker);
        var hasServiceEvidence = (blocker.ServiceMappings?.Count ?? 0) > 0 ||
            (blocker.ParentChain ?? []).Any(parent => parent.Services.Count > 0);
        if (!string.IsNullOrWhiteSpace(blocker.ServiceShortName) ||
            blocker.ApplicationType == RestartManagerApplicationType.Service ||
            linkedService is not null || hasServiceEvidence)
        {
            if (linkedService is not null && IsSafeThirdPartyDedicatedService(linkedService))
            {
                var kind = !string.IsNullOrWhiteSpace(blocker.ServiceShortName) ||
                           linkedService.ProcessId == blocker.ProcessId
                    ? WinGetStableIdentityKind.ServiceName
                    : WinGetStableIdentityKind.ParentService;
                return Classified(
                    blocker,
                    WinGetBlockerClassification.ThirdPartyService,
                    new WinGetStableIdentity(kind, linkedService.ServiceName.ToUpperInvariant()),
                    linkedService);
            }
            return Classified(blocker, WinGetBlockerClassification.SystemOrShared);
        }

        if (WinGetProcessOperations.IsAttributedProcess(
            processName,
            blocker.ExecutablePath,
            context.InstallRoots,
            context.ExecutablePaths)
            )
            return Classified(
                blocker,
                WinGetBlockerClassification.PackageOwned,
                ExecutableIdentity(blocker.ExecutablePath));

        if (string.IsNullOrWhiteSpace(blocker.ExecutablePath) || blocker.EvidenceResources.Count == 0)
            return Classified(blocker, WinGetBlockerClassification.Unknown);
        return Classified(
            blocker,
            WinGetBlockerClassification.ExternalConfirmed,
            ExecutableIdentity(blocker.ExecutablePath));
    }

    internal static ClassifiedRestartManagerBlocker MarkRecurring(
        ClassifiedRestartManagerBlocker current,
        IEnumerable<ClassifiedRestartManagerBlocker> previous)
    {
        if (!current.Identity.IsStable)
            return current;
        var recreated = previous.Any(prior =>
            prior.Blocker.ProcessId != current.Blocker.ProcessId &&
            prior.Identity.IsStable &&
            StableIdentityEquals(prior.Identity, current.Identity));
        return recreated
            ? current with
            {
                Classification = current.Classification == WinGetBlockerClassification.ThirdPartyService
                    ? current.Classification
                    : WinGetBlockerClassification.RecurringProcess,
                Recreated = true
            }
            : current;
    }

    internal static bool StableIdentityEquals(WinGetStableIdentity left, WinGetStableIdentity right)
    {
        if (!left.IsStable || !right.IsStable)
            return false;
        if (left.Kind is WinGetStableIdentityKind.ServiceName or WinGetStableIdentityKind.ParentService &&
            right.Kind is WinGetStableIdentityKind.ServiceName or WinGetStableIdentityKind.ParentService)
            return left.Value.Equals(right.Value, StringComparison.OrdinalIgnoreCase);
        return left.Kind == right.Kind && left.Value.Equals(right.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static ClassifiedRestartManagerBlocker Classified(
        RestartManagerBlocker blocker,
        WinGetBlockerClassification classification,
        WinGetStableIdentity? identity = null,
        WindowsServiceProcessSnapshot? service = null) =>
        new(blocker, classification, identity ?? new WinGetStableIdentity(WinGetStableIdentityKind.None, ""), service);

    private static WinGetStableIdentity ExecutableIdentity(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new WinGetStableIdentity(WinGetStableIdentityKind.None, "");
        try
        {
            return new WinGetStableIdentity(
                WinGetStableIdentityKind.ExecutablePath,
                Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToUpperInvariant());
        }
        catch
        {
            return new WinGetStableIdentity(WinGetStableIdentityKind.None, "");
        }
    }

    private static WindowsServiceProcessSnapshot? FindConcreteService(RestartManagerBlocker blocker)
    {
        var direct = blocker.ServiceMappings ?? [];
        if (!string.IsNullOrWhiteSpace(blocker.ServiceShortName))
        {
            var exact = direct.Where(service => service.ServiceName.Equals(
                blocker.ServiceShortName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1)
                return exact[0];
            return null;
        }
        var directForPid = direct.Where(service => service.ProcessId == blocker.ProcessId).ToList();
        if (directForPid.Count > 0)
            return directForPid.Count == 1 ? directForPid[0] : null;

        var parentServices = (blocker.ParentChain ?? [])
            .SelectMany(parent => parent.Services)
            .Where(service => service.ProcessId > 0)
            .DistinctBy(service => service.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parentServices.Count == 1 ? parentServices[0] : null;
    }

    internal static bool IsSafeThirdPartyDedicatedService(WindowsServiceProcessSnapshot service) =>
        service.IsDedicated && !service.IsShared && !service.RunsInSystemProcess && service.ProcessId > 0 &&
        !string.IsNullOrWhiteSpace(service.ExecutablePath) &&
        !WinGetProcessOperations.IsProtectedOrSharedPath(service.ExecutablePath) &&
        !WinGetProcessOperations.IsNeverCloseProcess(Path.GetFileNameWithoutExtension(service.ExecutablePath));
}

internal sealed class WinGetProcessRecoveryService
{
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ForcedCloseTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ServiceStateTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan StabilizationPollInterval = TimeSpan.FromMilliseconds(250);
    private const int MaximumStabilizationQueries = 8;
    private readonly IWinGetProcessOperations _operations;
    private readonly IWindowsRestartManagerService _restartManager;
    private readonly IWindowsServiceControl _serviceControl;
    private readonly IWinGetRecoveryPollingDelay _pollingDelay;

    public WinGetProcessRecoveryService() : this(
        new WinGetProcessOperations(),
        new WindowsRestartManagerService(),
        new WindowsServiceControl(),
        new WinGetRecoveryPollingDelay())
    {
    }

    internal WinGetProcessRecoveryService(
        IWinGetProcessOperations operations,
        IWindowsRestartManagerService restartManager,
        IWindowsServiceControl? serviceControl = null,
        IWinGetRecoveryPollingDelay? pollingDelay = null)
    {
        _operations = operations;
        _restartManager = restartManager;
        _serviceControl = serviceControl ?? new WindowsServiceControl();
        _pollingDelay = pollingDelay ?? new WinGetRecoveryPollingDelay();
    }

    public WinGetRecoveryPreparation PrepareRetry(
        UpdateItem item,
        ItemRunResult failedResult,
        IWinGetProcessRecoveryPrompt prompt)
    {
        if (!failedResult.FailureReason.Equals(UpdateFailureReasons.FilesInUse, StringComparison.Ordinal))
            return new WinGetRecoveryPreparation(false, LocalizationService.Text("L'esito non è classificato come file in uso.", "The outcome is not classified as files in use."));

        var context = _operations.CreateContext(item);
        var diagnostics = new List<string>
        {
            $"PackageId esatto: {context.PackageId}.",
            $"InstallLocation: {string.Join("; ", context.InstallRoots)}.",
            $"DisplayIcon/eseguibili: {string.Join("; ", context.ExecutablePaths)}.",
            $"Shared resource roots: {string.Join("; ", context.SharedResourceRoots)}.",
            $"Shared resources registrate: {string.Join("; ", context.SharedResources)}."
        };
        var initial = Stabilize(
            item, failedResult, context, "snapshot-iniziale", diagnostics, previous: []);
        if (initial.Action == WinGetRecoveryAction.Retry)
            return ReadyToRetry(context, diagnostics,
                "Due query consecutive confermano l'assenza di blocker; retry autorizzato.");
        if (initial.Action == WinGetRecoveryAction.RestartManagerUnavailable)
            return UseConservativeFallback(item, failedResult, context, prompt, diagnostics);
        if (initial.Action is WinGetRecoveryAction.ManualIntervention or WinGetRecoveryAction.RestartRequired)
            return RequireSafeFallback(item, failedResult, prompt, initial, diagnostics, context);
        if (initial.Action == WinGetRecoveryAction.StopConfirmedService)
            return PrepareServiceRetry(item, failedResult, prompt, context, initial, diagnostics);

        var confirmedCandidates = ToConfirmedCandidates(initial.Blockers);
        if (confirmedCandidates.Count == 0)
            return RequireSafeFallback(item, failedResult, prompt, initial, diagnostics, context);
        if (!prompt.ConfirmGracefulClose(item, confirmedCandidates))
        {
            diagnostics.Add("L'utente ha annullato la richiesta di chiusura pulita.");
            LogChoice(item, failedResult, "graceful-close", "cancelled", confirmedCandidates);
            return new WinGetRecoveryPreparation(false, string.Join(Environment.NewLine, diagnostics), context);
        }

        diagnostics.Add("Chiusura pulita confermata dall'utente: " + Describe(confirmedCandidates));
        LogChoice(item, failedResult, "graceful-close", "confirmed", confirmedCandidates);
        var authorizedIdentities = confirmedCandidates
            .Select(candidate => candidate.StableIdentity)
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processLevelRemaining = _operations.CloseGracefully(confirmedCandidates, GracefulCloseTimeout);
        diagnostics.Add(processLevelRemaining.Count == 0
            ? "CloseMainWindow: i PID proposti non risultano più attivi."
            : "CloseMainWindow: PID ancora attivi: " + Describe(processLevelRemaining));

        var afterClose = Stabilize(
            item, failedResult, context, "dopo-chiusura-pulita", diagnostics, initial.Blockers);
        if (afterClose.Action == WinGetRecoveryAction.Retry)
            return ReadyToRetry(context, diagnostics, "Restart Manager conferma che le risorse non sono più bloccate.");
        if (afterClose.Action != WinGetRecoveryAction.CloseConfirmedBlockers)
            return RequireSafeFallback(item, failedResult, prompt, afterClose, diagnostics, context);

        var remainingConfirmedCandidates = ToConfirmedCandidates(afterClose.Blockers);
        if (remainingConfirmedCandidates.Any(candidate =>
                string.IsNullOrWhiteSpace(candidate.StableIdentity) ||
                !authorizedIdentities.Contains(candidate.StableIdentity)))
        {
            diagnostics.Add("Sono comparsi blocker con identità diversa o non verificabile; nessuna terminazione proposta.");
            return RequireSafeFallback(item, failedResult, prompt, afterClose, diagnostics, context);
        }
        if (remainingConfirmedCandidates.Count == 0)
            return RequireSafeFallback(item, failedResult, prompt, afterClose, diagnostics, context);
        if (!prompt.ConfirmForcedTermination(item, remainingConfirmedCandidates))
        {
            diagnostics.Add("Terminazione forzata non autorizzata; nessun retry.");
            LogChoice(item, failedResult, "forced-close", "cancelled", remainingConfirmedCandidates);
            return RequireSafeFallback(item, failedResult, prompt, afterClose, diagnostics, context);
        }

        diagnostics.Add("Terminazione forzata confermata esplicitamente: " + Describe(remainingConfirmedCandidates));
        LogChoice(item, failedResult, "forced-close", "confirmed", remainingConfirmedCandidates);
        var survivedKill = _operations.Terminate(remainingConfirmedCandidates, ForcedCloseTimeout);
        diagnostics.Add(survivedKill.Count == 0
            ? "Kill(entireProcessTree=true): i PID proposti non risultano più attivi."
            : "Kill(entireProcessTree=true): PID ancora attivi: " + Describe(survivedKill));

        var afterKill = Stabilize(
            item, failedResult, context, "dopo-terminazione-forzata", diagnostics, afterClose.Blockers);
        if (afterKill.Action == WinGetRecoveryAction.Retry)
            return ReadyToRetry(context, diagnostics, "Restart Manager conferma la rimozione dei blocker; retry autorizzato.");
        return RequireSafeFallback(item, failedResult, prompt, afterKill, diagnostics, context);
    }

    public WinGetPostRetryDiagnosis DiagnoseFailedRetry(
        UpdateItem item,
        ItemRunResult retryResult,
        IWinGetProcessRecoveryPrompt prompt,
        WinGetRecoveryContext? preparedContext)
    {
        var context = preparedContext ?? _operations.CreateContext(item);
        var diagnostics = new List<string>();
        var decision = Stabilize(
            item, retryResult, context, "dopo-retry-files-in-use", diagnostics, previous: []);
        var message = BuildRetryFailureMessage(item, decision);
        retryResult.Message = message;
        prompt.ShowManualCloseRequired(item, message);
        LogService.WriteEvent(
            "winget-recovery", "interactive-fallback",
            "not-offered-after-retry",
            item.Id, retryResult.ResultCode,
            message + " Nessun ulteriore tentativo WinGet dopo il retry unico.");
        diagnostics.Add("Messaggio finale: " + message);
        diagnostics.Add("Fallback interattivo: non offerto dopo il retry unico.");
        return new WinGetPostRetryDiagnosis(
            string.Join(Environment.NewLine, diagnostics), ShouldRunInteractive: false);
    }

    private WinGetRecoveryDecision Stabilize(
        UpdateItem item,
        ItemRunResult failedResult,
        WinGetRecoveryContext context,
        string phase,
        ICollection<string> diagnostics,
        IReadOnlyList<ClassifiedRestartManagerBlocker> previous)
    {
        var history = previous.ToList();
        IReadOnlyList<ClassifiedRestartManagerBlocker> lastNonEmpty = previous;
        IReadOnlyList<ClassifiedRestartManagerBlocker> priorScan = [];
        var consecutiveClean = 0;
        WinGetRecoveryDecision? latest = null;
        for (var scan = 1; scan <= MaximumStabilizationQueries; scan++)
        {
            var query = _restartManager.Query(context.RegisteredResources);
            var evaluated = WinGetRecoveryDecisionPolicy.Evaluate(query, context);
            var classified = evaluated.Blockers
                .Select(blocker => blocker.Blocker.Liveness == RestartManagerProcessLiveness.Live
                    ? WinGetRecoveryDecisionPolicy.MarkRecurring(blocker, history)
                    : blocker)
                .ToList();
            latest = evaluated with { Blockers = classified };
            if (classified.Any(blocker => blocker.Recreated))
            {
                latest = latest with
                {
                    Action = classified.All(IsNormalProcess)
                        ? WinGetRecoveryAction.CloseConfirmedBlockers
                        : latest.Action,
                    Reason = "Rilevato blocker ricreato con PID diverso e identità stabile verificata."
                };
            }
            diagnostics.Add($"Restart Manager [{phase} #{scan}]: {query.Diagnostics}");
            diagnostics.Add("Classificazione blocker: " + Describe(classified));
            diagnostics.Add($"Decisione [{phase} #{scan}]: {latest.Action}; {latest.Reason}");
            LogService.WriteEvent(
                "winget-recovery", $"restart-manager-{phase}-{scan}",
                query.Succeeded ? latest.Action.ToString() : "failure",
                item.Id, failedResult.ResultCode,
                query.Diagnostics + Environment.NewLine + "Classificazione: " + Describe(classified));

            if (latest.Action is WinGetRecoveryAction.RestartManagerUnavailable or
                WinGetRecoveryAction.RestartRequired)
                return latest;
            var relevant = RelevantBlockers(classified);
            if (relevant.Count == 0 &&
                query.RebootReason == RestartManagerRebootReason.None)
            {
                consecutiveClean++;
                if (consecutiveClean >= 2)
                    return latest with
                    {
                        Action = WinGetRecoveryAction.Retry,
                        Reason = "Due query consecutive non rilevano blocker rilevanti."
                    };
            }
            else
            {
                consecutiveClean = 0;
                if (priorScan.Count > 0 && SameObservedBlockers(priorScan, relevant))
                    return latest;
                history.AddRange(classified.Where(blocker =>
                    blocker.Blocker.Liveness == RestartManagerProcessLiveness.Live));
                lastNonEmpty = relevant;
            }
            priorScan = relevant;
            if (scan < MaximumStabilizationQueries)
                _pollingDelay.Wait(StabilizationPollInterval);
        }
        return new WinGetRecoveryDecision(
            WinGetRecoveryAction.ManualIntervention, lastNonEmpty,
            "Stabilizzazione Restart Manager non conclusiva.");
    }

    private WinGetRecoveryPreparation PrepareServiceRetry(
        UpdateItem item,
        ItemRunResult failedResult,
        IWinGetProcessRecoveryPrompt prompt,
        WinGetRecoveryContext context,
        WinGetRecoveryDecision decision,
        ICollection<string> diagnostics)
    {
        var liveBlockers = decision.Blockers
            .Where(blocker => blocker.Blocker.Liveness == RestartManagerProcessLiveness.Live)
            .ToList();
        var linked = liveBlockers
            .Select(blocker => blocker.LinkedService)
            .Where(service => service is not null)
            .Cast<WindowsServiceProcessSnapshot>()
            .DistinctBy(service => service.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (linked.Count != 1 || liveBlockers.Any(blocker => blocker.LinkedService is null ||
                !blocker.LinkedService.ServiceName.Equals(linked[0].ServiceName, StringComparison.OrdinalIgnoreCase)))
            return RequireSafeFallback(item, failedResult, prompt, decision, diagnostics, context);

        var current = _serviceControl.Query(linked[0].ServiceName);
        if (current is null || !current.IsRunning ||
            !WinGetRecoveryDecisionPolicy.IsSafeThirdPartyDedicatedService(current))
        {
            diagnostics.Add("Lo stato corrente del servizio non conferma un servizio third-party dedicato e arrestabile.");
            return RequireSafeFallback(item, failedResult, prompt, decision, diagnostics, context);
        }
        var candidate = new WinGetServiceCandidate(
            current.ServiceName,
            string.IsNullOrWhiteSpace(current.DisplayName) ? current.ServiceName : current.DisplayName,
            current.ExecutablePath,
            current.IsRunning,
            current.IsDedicated,
            $"ServiceName:{current.ServiceName.ToUpperInvariant()}");
        if (!prompt.ConfirmTemporaryServiceStop(item, candidate))
        {
            diagnostics.Add($"Stop temporaneo del servizio {candidate.ServiceName} annullato dall'utente.");
            LogService.WriteEvent("winget-recovery", "service-stop", "cancelled",
                item.Id, failedResult.ResultCode, candidate.StableIdentity);
            return new WinGetRecoveryPreparation(false, string.Join(Environment.NewLine, diagnostics), context);
        }

        diagnostics.Add($"Stop temporaneo del servizio {candidate.ServiceName} confermato dall'utente.");
        LogService.WriteEvent("winget-recovery", "service-stop-confirmation", "confirmed",
            item.Id, failedResult.ResultCode, candidate.StableIdentity);

        var stop = _serviceControl.StopAndWait(candidate.ServiceName, ServiceStateTimeout);
        diagnostics.Add("Stop servizio: " + stop.Diagnostics);
        LogService.WriteEvent("winget-recovery", "service-stop",
            stop.Succeeded ? "success" : "failure", item.Id, failedResult.ResultCode, stop.Diagnostics);
        if (!stop.Succeeded)
            return RequireSafeFallback(item, failedResult, prompt, decision, diagnostics, context);

        var lease = new WinGetServiceRecoveryLease(
            _serviceControl, candidate.ServiceName, candidate.WasRunning, ServiceStateTimeout,
            item.Id, failedResult.ResultCode);
        try
        {
            var afterStop = Stabilize(
                item, failedResult, context, "dopo-stop-servizio", diagnostics, decision.Blockers);
            if (afterStop.Action == WinGetRecoveryAction.Retry)
                return ReadyToRetry(context, diagnostics,
                    "Il servizio è Stopped e due query confermano risorse libere; retry autorizzato.", lease);

            var fallback = RequireSafeFallback(item, failedResult, prompt, afterStop, diagnostics, context);
            return fallback with { Lease = lease };
        }
        catch
        {
            _ = lease.Restore();
            throw;
        }
    }

    private static bool IsNormalProcess(ClassifiedRestartManagerBlocker blocker) =>
        blocker.Blocker.Liveness == RestartManagerProcessLiveness.Live &&
        blocker.Classification is WinGetBlockerClassification.PackageOwned or
            WinGetBlockerClassification.ExternalConfirmed or
            WinGetBlockerClassification.RecurringProcess;

    private static IReadOnlyList<ClassifiedRestartManagerBlocker> RelevantBlockers(
        IEnumerable<ClassifiedRestartManagerBlocker> blockers) =>
        blockers.Where(blocker => blocker.Blocker.Liveness != RestartManagerProcessLiveness.DeadOrStale).ToList();

    private static bool SameObservedBlockers(
        IReadOnlyList<ClassifiedRestartManagerBlocker> left,
        IReadOnlyList<ClassifiedRestartManagerBlocker> right)
    {
        if (left.Count != right.Count)
            return false;
        var leftKeys = left.Select(ObservationKey).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var rightKeys = right.Select(ObservationKey).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return leftKeys.SequenceEqual(rightKeys, StringComparer.OrdinalIgnoreCase);
    }

    private static string ObservationKey(ClassifiedRestartManagerBlocker blocker) =>
        blocker.Identity.IsStable
            ? $"{blocker.Blocker.ProcessId}:{blocker.Identity.Key}"
            : $"{blocker.Blocker.ProcessId}:unknown";

    private static WinGetRecoveryPreparation UseConservativeFallback(
        UpdateItem item,
        ItemRunResult failedResult,
        WinGetRecoveryContext context,
        IWinGetProcessRecoveryPrompt prompt,
        ICollection<string> diagnostics)
    {
        var fallback = context.FallbackCandidates;
        if (fallback.Count == 0)
        {
            var interactiveDetail = LocalizationService.Text(
                "Restart Manager non è disponibile e nessun processo è attribuibile con certezza al pacchetto.",
                "Restart Manager is unavailable and no process can be safely attributed to the package.");
            diagnostics.Add("Fallback conservativo: " + interactiveDetail);
            var openInteractive = prompt.ConfirmInteractiveInstaller(item);
            LogService.WriteEvent(
                "winget-recovery", "interactive-fallback",
                openInteractive ? "confirmed" : "cancelled",
                item.Id, failedResult.ResultCode, interactiveDetail);
            return new WinGetRecoveryPreparation(
                false,
                string.Join(Environment.NewLine, diagnostics),
                context,
                openInteractive);
        }
        var names = string.Join(", ", fallback.Select(candidate => candidate.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var detail = LocalizationService.Text(
            "Windows non può verificare automaticamente quali file sono ancora in uso. " +
            $"Chiudi manualmente {names} e riprova.",
            "Windows cannot automatically verify which files are still in use. " +
            $"Manually close {names} and retry.");
        diagnostics.Add("Fallback conservativo InstallLocation/DisplayIcon: " + Describe(fallback));
        var openFallback = prompt.ConfirmInteractiveInstaller(item);
        LogService.WriteEvent(
            "winget-recovery", "fallback-process-discovery",
            openFallback ? "interactive-confirmed" : "manual-required",
            item.Id, failedResult.ResultCode, detail);
        if (!openFallback)
            prompt.ShowManualCloseRequired(item, detail);
        return new WinGetRecoveryPreparation(
            false, string.Join(Environment.NewLine, diagnostics), context, openFallback);
    }

    private static WinGetRecoveryPreparation RequireSafeFallback(
        UpdateItem item,
        ItemRunResult failedResult,
        IWinGetProcessRecoveryPrompt prompt,
        WinGetRecoveryDecision decision,
        ICollection<string> diagnostics,
        WinGetRecoveryContext context)
    {
        var detail = BuildManualMessage(item, decision);
        failedResult.Message = detail;
        diagnostics.Add("Remediation automatica rifiutata: " + detail);
        var openInteractive = prompt.ConfirmInteractiveInstaller(item);
        LogService.WriteEvent(
            "winget-recovery", "interactive-fallback",
            openInteractive ? "confirmed" : "cancelled",
            item.Id, failedResult.ResultCode, detail);
        if (!openInteractive)
            prompt.ShowManualCloseRequired(item, detail);
        return new WinGetRecoveryPreparation(
            false, string.Join(Environment.NewLine, diagnostics), context, openInteractive);
    }

    private static WinGetRecoveryPreparation ReadyToRetry(
        WinGetRecoveryContext context,
        ICollection<string> diagnostics,
        string detail,
        IWinGetRecoveryLease? lease = null)
    {
        diagnostics.Add(detail);
        return new WinGetRecoveryPreparation(true, string.Join(Environment.NewLine, diagnostics), context, Lease: lease);
    }

    private static string BuildManualMessage(UpdateItem item, WinGetRecoveryDecision decision)
    {
        var recurring = decision.Blockers.Where(x => x.Recreated).ToList();
        if (recurring.Count > 0)
        {
            var names = string.Join("; ", recurring.Select(x =>
                $"{x.Blocker.ApplicationName} ({x.Blocker.ExecutablePath}, PID {x.Blocker.ProcessId}" +
                (x.LinkedService is null ? ")" : $", service: {x.LinkedService.ServiceName})")));
            return LocalizationService.Text(
                $"Un blocker ricompare e continua a utilizzare le risorse di {item.Name}: {names}. Chiudi il componente o riavvia il PC prima di riprovare.",
                $"A blocker keeps restarting and using resources required by {item.Name}: {names}. Close the component or restart the PC before retrying.");
        }
        if (decision.Action == WinGetRecoveryAction.RestartRequired)
            return LocalizationService.Text(
                $"Windows segnala che le risorse di {item.Name} richiedono un riavvio prima di riprovare.",
                $"Windows reports that resources for {item.Name} require a restart before retrying.");
        var unknown = decision.Blockers
            .Where(x => x.Classification == WinGetBlockerClassification.Unknown)
            .Select(Describe)
            .ToList();
        if (unknown.Count > 0)
            return LocalizationService.Text(
                $"Un componente non identificabile con sufficiente certezza sta utilizzando file di {item.Name}. " +
                "UpdateCenter non lo chiuderà automaticamente.",
                $"A component that cannot be identified with certainty is using files required by {item.Name}. " +
                "UpdateCenter will not close it automatically.");
        var system = decision.Blockers
            .Where(x => x.Classification == WinGetBlockerClassification.SystemOrShared)
            .Select(Describe)
            .ToList();
        if (system.Count > 0)
            return LocalizationService.Text(
                $"Un componente di sistema o condiviso sta utilizzando file di {item.Name}. " +
                "UpdateCenter non può interromperlo in sicurezza.",
                $"A system or shared component is using files required by {item.Name}. " +
                "UpdateCenter cannot safely terminate it.");
        var live = decision.Blockers.Where(x => x.Blocker.Liveness == RestartManagerProcessLiveness.Live).ToList();
        if (live.Count > 0)
        {
            var names = string.Join("; ", live.Select(x =>
                $"{x.Blocker.ApplicationName} ({x.Blocker.ExecutablePath}, PID {x.Blocker.ProcessId})"));
            return LocalizationService.Text(
                $"Le risorse di {item.Name} sono ancora bloccate da: {names}. Chiudi il componente o riavvia il PC prima di riprovare.",
                $"Resources required by {item.Name} are still blocked by: {names}. Close the component or restart the PC before retrying.");
        }
        return LocalizationService.Text(
            $"Windows non permette di dimostrare che le risorse di {item.Name} siano libere. " +
            "Chiudi manualmente le applicazioni interessate o riavvia il PC e riprova.",
            $"Windows cannot prove that resources for {item.Name} are free. " +
            "Manually close the affected applications or restart the PC and retry.");
    }

    private static string BuildRetryFailureMessage(UpdateItem item, WinGetRecoveryDecision decision)
    {
        if (decision.Action == WinGetRecoveryAction.RestartManagerUnavailable || decision.Blockers.Count == 0)
        {
            return LocalizationService.Text(
                "L'installer segnala ancora file in uso, ma Windows non permette di identificare in sicurezza " +
                "il processo responsabile. Riavvia il PC o chiudi manualmente le applicazioni interessate e riprova.",
                "The installer still reports files in use, but Windows cannot safely identify " +
                "the responsible process. Restart the PC or manually close the affected applications and retry.");
        }
        return BuildManualMessage(item, decision);
    }

    private static IReadOnlyList<WinGetProcessCandidate> ToConfirmedCandidates(
        IEnumerable<ClassifiedRestartManagerBlocker> blockers) =>
        blockers
            .Where(x =>
                x.Blocker.Liveness == RestartManagerProcessLiveness.Live &&
                (x.Classification is WinGetBlockerClassification.PackageOwned or
                    WinGetBlockerClassification.ExternalConfirmed or
                    WinGetBlockerClassification.RecurringProcess) &&
                x.Blocker.ProcessId > 0 &&
                !string.IsNullOrWhiteSpace(x.Blocker.ExecutablePath))
            .Select(x => new WinGetProcessCandidate(
                x.Blocker.ProcessId,
                Path.GetFileNameWithoutExtension(x.Blocker.ExecutablePath),
                x.Blocker.ExecutablePath,
                x.Classification,
                x.Identity.Key))
            .DistinctBy(x => x.ProcessId)
            .ToList();

    private static string Describe(IEnumerable<WinGetProcessCandidate> candidates) =>
        string.Join(", ", candidates.Select(x => $"{x.ProcessName} (PID {x.ProcessId}, {x.ExecutablePath})"));

    private static string Describe(IEnumerable<ClassifiedRestartManagerBlocker> blockers) =>
        string.Join("; ", blockers.Select(Describe));

    private static string Describe(ClassifiedRestartManagerBlocker blocker) =>
        $"{blocker.Blocker.ApplicationName} (PID {blocker.Blocker.ProcessId}, " +
        $"tipo={blocker.Blocker.ApplicationType}, classe={blocker.Classification}, " +
        $"liveness={blocker.Blocker.Liveness}, " +
        $"stableIdentity={blocker.Identity.Key}, recreated={blocker.Recreated}, " +
        $"servizio={blocker.Blocker.ServiceShortName}, restartable={blocker.Blocker.Restartable}, " +
        $"reboot={blocker.Blocker.RebootReason}, path={blocker.Blocker.ExecutablePath}, " +
        $"parentPid={blocker.Blocker.ParentProcessId}, " +
        $"parentChain={string.Join(" -> ", (blocker.Blocker.ParentChain ?? []).Select(p => p.ProcessId))}, " +
        $"serviceMapping={string.Join(", ", (blocker.Blocker.ServiceMappings ?? []).Select(s => s.ServiceName))}, " +
        $"evidence={string.Join("; ", blocker.Blocker.EvidenceResources)})";

    private static void LogChoice(
        UpdateItem item,
        ItemRunResult failedResult,
        string phase,
        string outcome,
        IReadOnlyList<WinGetProcessCandidate> candidates) =>
        LogService.WriteEvent(
            "winget-recovery", phase, outcome,
            item.Id, failedResult.ResultCode, Describe(candidates));
}

internal sealed class WinGetServiceRecoveryLease : IWinGetRecoveryLease
{
    private readonly IWindowsServiceControl _serviceControl;
    private readonly string _serviceName;
    private readonly bool _restoreRunning;
    private readonly TimeSpan _timeout;
    private readonly string _itemId;
    private readonly int? _resultCode;
    private int _restored;

    public WinGetServiceRecoveryLease(
        IWindowsServiceControl serviceControl,
        string serviceName,
        bool restoreRunning,
        TimeSpan timeout,
        string itemId = "",
        int? resultCode = null)
    {
        _serviceControl = serviceControl;
        _serviceName = serviceName;
        _restoreRunning = restoreRunning;
        _timeout = timeout;
        _itemId = itemId;
        _resultCode = resultCode;
    }

    public string Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
            return $"Ripristino servizio {_serviceName}: già eseguito.";
        if (!_restoreRunning)
            return $"Ripristino servizio {_serviceName}: non necessario (non era Running).";
        var result = _serviceControl.StartAndWait(_serviceName, _timeout);
        LogService.WriteEvent(
            "winget-recovery", "service-start",
            result.Succeeded ? "success" : "failure", _itemId, _resultCode, result.Diagnostics);
        return "Ripristino servizio: " + result.Diagnostics;
    }
}

internal static class WinGetSingleRetryPolicy
{
    public static async Task<ItemRunResult> ExecuteAsync(
        UpdateItem item,
        ItemRunResult initialResult,
        Func<WinGetRecoveryPreparation> prepareRetry,
        Func<Task<ItemRunResult>> retry,
        Func<ItemRunResult, WinGetRecoveryContext?, WinGetPostRetryDiagnosis>? diagnoseFailedRetry = null,
        Func<Task<ItemRunResult>>? interactiveFallback = null)
    {
        if (!initialResult.FailureReason.Equals(UpdateFailureReasons.FilesInUse, StringComparison.Ordinal))
            return initialResult;
        var preparation = prepareRetry();
        ItemRunResult? outcome = null;
        try
        {
            if (!preparation.ShouldRetry)
            {
                if (preparation.ShouldRunInteractive && interactiveFallback is not null)
                {
                    outcome = await RunInteractiveFallback(
                        item, initialResult, preparation, retryResult: null,
                        postRetryDiagnostics: "", interactiveFallback);
                    return outcome;
                }
                initialResult.Diagnostics = CombineDiagnostics(
                    initialResult, preparation.Diagnostics, retryResult: null,
                    postRetryDiagnostics: "", interactiveResult: null);
                outcome = initialResult;
                return outcome;
            }

            LogService.WriteEvent(
                "winget-recovery", "retry", "started",
                item.Id, initialResult.ResultCode,
                "Unico retry dopo la verifica Restart Manager dei blocker.");
            var retryResult = await retry();
            var diagnosis = new WinGetPostRetryDiagnosis("", false);
            if (retryResult.FailureReason.Equals(UpdateFailureReasons.FilesInUse, StringComparison.Ordinal) &&
                diagnoseFailedRetry is not null)
                diagnosis = diagnoseFailedRetry(retryResult, preparation.Context);
            retryResult.Diagnostics = CombineDiagnostics(
                initialResult, preparation.Diagnostics, retryResult, diagnosis.Diagnostics, interactiveResult: null);
            LogService.WriteEvent(
                "winget-recovery", "retry", retryResult.Success ? "success" : "failure",
                item.Id, retryResult.ResultCode,
                $"failureReason={retryResult.FailureReason}; verified={retryResult.Verified}; " +
                $"verification={retryResult.VerificationStatus}; nessun ulteriore retry automatico.");
            outcome = retryResult;
            return outcome;
        }
        finally
        {
            if (preparation.Lease is not null)
            {
                var restoration = preparation.Lease.Restore();
                if (outcome is not null)
                    outcome.Diagnostics = string.IsNullOrWhiteSpace(outcome.Diagnostics)
                        ? restoration
                        : outcome.Diagnostics + Environment.NewLine + Environment.NewLine + restoration;
            }
        }
    }

    private static async Task<ItemRunResult> RunInteractiveFallback(
        UpdateItem item,
        ItemRunResult initialResult,
        WinGetRecoveryPreparation preparation,
        ItemRunResult? retryResult,
        string postRetryDiagnostics,
        Func<Task<ItemRunResult>> interactiveFallback)
    {
        LogService.WriteEvent(
            "winget-recovery", "interactive-installer", "started",
            item.Id, retryResult?.ResultCode ?? initialResult.ResultCode,
            "Singolo tentativo WinGet interattivo autorizzato dall'utente.");
        var interactiveResult = await interactiveFallback();
        interactiveResult.Diagnostics = CombineDiagnostics(
            initialResult,
            preparation.Diagnostics,
            retryResult,
            postRetryDiagnostics,
            interactiveResult);
        LogService.WriteEvent(
            "winget-recovery", "interactive-installer",
            interactiveResult.Success ? "success" : "failure",
            item.Id, interactiveResult.ResultCode,
            $"verified={interactiveResult.Verified}; verification={interactiveResult.VerificationStatus}; " +
            "nessun ulteriore tentativo automatico.");
        return interactiveResult;
    }

    private static string CombineDiagnostics(
        ItemRunResult initialResult,
        string recoveryDiagnostics,
        ItemRunResult? retryResult,
        string postRetryDiagnostics,
        ItemRunResult? interactiveResult)
    {
        var sections = new List<string>
        {
            $"Tentativo iniziale: ResultCode={initialResult.ResultCode?.ToString() ?? "n/d"}; " +
            $"FailureReason={initialResult.FailureReason}.\n{initialResult.Diagnostics}",
            "Gestione processi e Restart Manager:\n" + recoveryDiagnostics
        };
        if (retryResult is not null)
        {
            sections.Add(
                $"Retry unico: ResultCode={retryResult.ResultCode?.ToString() ?? "n/d"}; " +
                $"FailureReason={retryResult.FailureReason}; Success={retryResult.Success}; " +
                $"Verified={retryResult.Verified}.\n{retryResult.Diagnostics}");
        }
        if (!string.IsNullOrWhiteSpace(postRetryDiagnostics))
            sections.Add("Diagnosi dopo retry FilesInUse:\n" + postRetryDiagnostics);
        if (interactiveResult is not null)
        {
            sections.Add(
                $"Installer interattivo: ResultCode={interactiveResult.ResultCode?.ToString() ?? "n/d"}; " +
                $"Success={interactiveResult.Success}; Verified={interactiveResult.Verified}; " +
                $"Verification={interactiveResult.VerificationStatus}.\n{interactiveResult.Diagnostics}");
        }
        return string.Join("\n\n", sections);
    }
}

internal sealed class WinGetProcessOperations : IWinGetProcessOperations
{
    private const int MaximumRegisteredResources = 512;
    private const int MaximumBinaryFilesPerRoot = 384;
    private const int MaximumFileEntriesPerRoot = 2048;
    private const int MaximumDirectoriesPerRoot = 96;
    private const int MaximumTraversalDepth = 4;
    private const int MaximumSharedResources = 128;
    private const int MaximumSharedEntries = 512;
    private const int MaximumSharedDepth = 2;
    private static readonly HashSet<string> BinaryExtensions = new(
        [".exe", ".dll", ".sys", ".ocx", ".cpl", ".msi"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> NeverCloseProcessNames = new(
        [
            "system", "registry", "idle", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "svchost", "fontdrvhost", "dwm", "explorer",
            "sihost", "ctfmon", "runtimebroker", "shellexperiencehost",
            "startmenuexperiencehost", "searchhost", "searchindexer", "taskhostw",
            "msedgewebview2", "officeclicktorun"
        ], StringComparer.OrdinalIgnoreCase);

    public WinGetRecoveryContext CreateContext(UpdateItem item)
    {
        var metadata = ReadInstalledMetadata(item);
        var candidates = FindCandidates(metadata);
        var sharedRoots = PackageRecoveryHints.Get(item.Id).SharedResourceRoots;
        var sharedResources = EnumerateSharedResources(sharedRoots);
        var normalResources = BuildRegisteredResources(metadata, candidates);
        return new WinGetRecoveryContext(
            item.Id,
            metadata.InstallRoots,
            metadata.ExecutablePaths,
            sharedRoots,
            sharedResources,
            normalResources.Concat(sharedResources)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            candidates);
    }

    internal static IReadOnlyList<string> EnumerateSharedResources(
        IReadOnlyCollection<string> roots)
    {
        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entriesExamined = 0;
        foreach (var configuredRoot in roots)
        {
            if (resources.Count >= MaximumSharedResources || entriesExamined >= MaximumSharedEntries)
                break;

            string root;
            try
            {
                if (!Path.IsPathFullyQualified(configuredRoot)) continue;
                root = Path.GetFullPath(configuredRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch { continue; }
            if (!Directory.Exists(root)) continue;

            var queue = new Queue<(string Directory, int Depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0 && resources.Count < MaximumSharedResources &&
                   entriesExamined < MaximumSharedEntries)
            {
                var (directory, depth) = queue.Dequeue();
                var files = EnumerateAtMost(
                    () => Directory.EnumerateFiles(directory),
                    MaximumSharedEntries - entriesExamined);
                foreach (var file in files)
                {
                    entriesExamined++;
                    AddExistingResource(resources, file);
                    if (resources.Count >= MaximumSharedResources) break;
                }

                if (depth >= MaximumSharedDepth) continue;
                var directories = EnumerateAtMost(
                    () => Directory.EnumerateDirectories(directory),
                    MaximumSharedEntries - entriesExamined);
                foreach (var child in directories)
                {
                    entriesExamined++;
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            queue.Enqueue((child, depth + 1));
                    }
                    catch { }
                }
            }
        }
        return resources.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<WinGetProcessCandidate> CloseGracefully(
        IReadOnlyList<WinGetProcessCandidate> candidates, TimeSpan timeout)
    {
        foreach (var candidate in candidates)
        {
            using var process = OpenValidatedProcess(candidate);
            if (process is null) continue;
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                    process.CloseMainWindow();
            }
            catch { }
        }
        return WaitForExit(candidates, timeout);
    }

    public IReadOnlyList<WinGetProcessCandidate> Terminate(
        IReadOnlyList<WinGetProcessCandidate> candidates, TimeSpan timeout)
    {
        foreach (var candidate in candidates)
        {
            using var process = OpenValidatedProcess(candidate);
            if (process is null) continue;
            try { process.Kill(entireProcessTree: true); } catch { }
        }
        return WaitForExit(candidates, timeout);
    }

    internal static bool IsAttributedProcess(
        string processName,
        string executablePath,
        IReadOnlyCollection<string> installRoots,
        IReadOnlyCollection<string> executablePaths)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(executablePath) ||
            IsNeverCloseProcess(processName))
            return false;
        string fullPath;
        try { fullPath = Path.GetFullPath(executablePath); }
        catch { return false; }
        if (IsProtectedOrSharedPath(fullPath)) return false;
        if (executablePaths.Any(path => PathsEqual(path, fullPath))) return true;
        return installRoots.Any(root => IsSafeInstallRoot(root) && IsUnderRoot(fullPath, root));
    }

    internal static bool IsNeverCloseProcess(string processName) =>
        NeverCloseProcessNames.Contains(Path.GetFileNameWithoutExtension(processName));

    internal static bool IsProtectedOrSharedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows) &&
            (PathsEqual(path, windows) || IsUnderRoot(path, windows)))
            return true;
        var commonRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Common Files")
        };
        return commonRoots.Where(root => !string.IsNullOrWhiteSpace(root))
            .Any(root => PathsEqual(path, root) || IsUnderRoot(path, root));
    }

    private static IReadOnlyList<WinGetProcessCandidate> FindCandidates(InstalledAppMetadata metadata)
    {
        if (!OperatingSystem.IsWindows() ||
            metadata.InstallRoots.Count == 0 && metadata.ExecutablePaths.Count == 0)
            return [];
        var candidates = new List<WinGetProcessCandidate>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.HasExited) continue;
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path) ||
                        !IsAttributedProcess(process.ProcessName, path, metadata.InstallRoots, metadata.ExecutablePaths))
                        continue;
                    candidates.Add(new WinGetProcessCandidate(process.Id, process.ProcessName, Path.GetFullPath(path)));
                }
                catch { }
            }
        }
        return candidates.DistinctBy(x => x.ProcessId)
            .OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> BuildRegisteredResources(
        InstalledAppMetadata metadata,
        IReadOnlyList<WinGetProcessCandidate> candidates)
    {
        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in metadata.ExecutablePaths) AddExistingResource(resources, path);
        foreach (var candidate in candidates) AddExistingResource(resources, candidate.ExecutablePath);
        foreach (var module in FindOwnedProcessModules(metadata, candidates))
            AddExistingResource(resources, module);
        foreach (var root in metadata.InstallRoots)
        {
            foreach (var binary in EnumerateBinaries(root))
            {
                if (resources.Count >= MaximumRegisteredResources) break;
                AddExistingResource(resources, binary);
            }
            if (resources.Count >= MaximumRegisteredResources) break;
        }
        return resources.Take(MaximumRegisteredResources).ToList();
    }

    private static IEnumerable<string> FindOwnedProcessModules(
        InstalledAppMetadata metadata,
        IReadOnlyList<WinGetProcessCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            using var process = OpenValidatedProcess(candidate);
            if (process is null) continue;
            var ownedModules = new List<string>();
            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    var path = module.FileName;
                    if (IsOwnedResourcePath(path, metadata))
                        ownedModules.Add(path);
                }
            }
            catch
            {
                // I moduli non ispezionabili non ampliano l'insieme delle risorse fidate.
            }
            foreach (var path in ownedModules)
                yield return path;
        }
    }

    private static bool IsOwnedResourcePath(string path, InstalledAppMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(path) || IsProtectedOrSharedPath(path)) return false;
        return metadata.ExecutablePaths.Any(executable => PathsEqual(executable, path)) ||
               metadata.InstallRoots.Any(root => IsSafeInstallRoot(root) && IsUnderRoot(path, root));
    }

    private static IEnumerable<string> EnumerateBinaries(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((root, 0));
        var directoriesVisited = 0;
        var filesFound = 0;
        var entriesExamined = 0;
        while (queue.Count > 0 && directoriesVisited < MaximumDirectoriesPerRoot &&
               filesFound < MaximumBinaryFilesPerRoot && entriesExamined < MaximumFileEntriesPerRoot)
        {
            var (directory, depth) = queue.Dequeue();
            directoriesVisited++;
            var files = EnumerateAtMost(
                () => Directory.EnumerateFiles(directory),
                MaximumFileEntriesPerRoot - entriesExamined);
            foreach (var file in files)
            {
                entriesExamined++;
                if (!BinaryExtensions.Contains(Path.GetExtension(file))) continue;
                filesFound++;
                yield return file;
                if (filesFound >= MaximumBinaryFilesPerRoot) yield break;
            }
            if (depth >= MaximumTraversalDepth) continue;
            var directories = EnumerateAtMost(
                () => Directory.EnumerateDirectories(directory),
                MaximumDirectoriesPerRoot - directoriesVisited - queue.Count);
            foreach (var child in directories)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        queue.Enqueue((child, depth + 1));
                }
                catch { }
            }
        }
    }

    private static IReadOnlyList<string> EnumerateAtMost(
        Func<IEnumerable<string>> enumerate,
        int maximum)
    {
        if (maximum <= 0) return [];
        try { return enumerate().Take(maximum).ToList(); }
        catch { return []; }
    }

    private static void AddExistingResource(ISet<string> resources, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath)) resources.Add(fullPath);
        }
        catch { }
    }

    private static IReadOnlyList<WinGetProcessCandidate> WaitForExit(
        IReadOnlyList<WinGetProcessCandidate> candidates, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var remaining = candidates.Where(IsCandidateStillRunning).ToList();
            if (remaining.Count == 0) return [];
            Thread.Sleep(100);
        }
        return candidates.Where(IsCandidateStillRunning).ToList();
    }

    private static bool IsCandidateStillRunning(WinGetProcessCandidate candidate)
    {
        using var process = OpenValidatedProcess(candidate);
        return process is not null;
    }

    private static Process? OpenValidatedProcess(WinGetProcessCandidate candidate)
    {
        try
        {
            var process = Process.GetProcessById(candidate.ProcessId);
            if (process.HasExited ||
                !process.ProcessName.Equals(candidate.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                !PathsEqual(process.MainModule?.FileName ?? "", candidate.ExecutablePath))
            {
                process.Dispose();
                return null;
            }
            return process;
        }
        catch { return null; }
    }

    private static InstalledAppMetadata ReadInstalledMetadata(UpdateItem item)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in RegistryLocations())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(subKeyName);
                    var displayName = Convert.ToString(entry?.GetValue("DisplayName"))?.Trim() ?? "";
                    var winGetId = Convert.ToString(entry?.GetValue("WinGetId"))?.Trim() ?? "";
                    var packageIdentifier =
                        Convert.ToString(entry?.GetValue("PackageIdentifier"))?.Trim() ?? "";
                    var registeredPackageId = !string.IsNullOrWhiteSpace(winGetId)
                        ? winGetId
                        : packageIdentifier;
                    if (!string.IsNullOrWhiteSpace(registeredPackageId)
                            ? !registeredPackageId.Equals(item.Id, StringComparison.OrdinalIgnoreCase)
                            : !RegistrationNameMatches(displayName, item.Name))
                        continue;
                    AddSafeRoot(roots, Convert.ToString(entry?.GetValue("InstallLocation")));
                    var displayIcon = ParseDisplayIcon(Convert.ToString(entry?.GetValue("DisplayIcon")));
                    if (!string.IsNullOrWhiteSpace(displayIcon)) executables.Add(displayIcon);
                }
            }
            catch { }
        }
        return new InstalledAppMetadata(roots.ToList(), executables.ToList());
    }

    internal static bool RegistrationNameMatches(string registeredName, string packageName) =>
        NormalizeRegistrationName(registeredName)
            .Equals(NormalizeRegistrationName(packageName), StringComparison.CurrentCultureIgnoreCase);

    private static string NormalizeRegistrationName(string value)
    {
        var normalized = value.Trim();
        string[] suffixes = [" (64-bit)", " (32-bit)", " (x64)", " (x86)"];
        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(suffix, true, CultureInfo.CurrentCulture))
                return normalized[..^suffix.Length].TrimEnd();
        }
        return normalized;
    }

    private static string ParseDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (expanded.StartsWith('"'))
        {
            var quote = expanded.IndexOf('"', 1);
            if (quote > 1) expanded = expanded[1..quote];
        }
        else
        {
            var iconIndex = expanded.LastIndexOf(',');
            if (iconIndex > 0 && int.TryParse(expanded[(iconIndex + 1)..], out _))
                expanded = expanded[..iconIndex];
        }
        expanded = expanded.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(expanded) ||
            !Path.GetExtension(expanded).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return "";
        try { return Path.GetFullPath(expanded); } catch { return ""; }
    }

    private static void AddSafeRoot(ISet<string> roots, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(expanded)) return;
        try
        {
            var root = Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (IsSafeInstallRoot(root)) roots.Add(root);
        }
        catch { }
    }

    private static bool IsSafeInstallRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) return false;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetPathRoot(fullRoot)?.TrimEnd(Path.DirectorySeparatorChar) == fullRoot ||
            IsProtectedOrSharedPath(fullRoot))
            return false;
        var broadRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        return broadRoots.Where(path => !string.IsNullOrWhiteSpace(path)).All(path => !PathsEqual(path, fullRoot));
    }

    private static bool IsUnderRoot(string path, string root)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static IEnumerable<(RegistryHive Hive, RegistryView View)> RegistryLocations()
    {
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry64);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry32);
    }

    private sealed record InstalledAppMetadata(
        IReadOnlyList<string> InstallRoots,
        IReadOnlyList<string> ExecutablePaths);
}
