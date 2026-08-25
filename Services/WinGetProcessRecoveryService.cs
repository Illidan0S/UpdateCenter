using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal sealed record WinGetProcessCandidate(
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    WinGetBlockerClassification Classification = WinGetBlockerClassification.PackageOwned);

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
    ExternalConfirmedBlocker,
    SystemOrService,
    Unknown
}

internal sealed record ClassifiedRestartManagerBlocker(
    RestartManagerBlocker Blocker,
    WinGetBlockerClassification Classification);

internal enum WinGetRecoveryAction
{
    Retry,
    CloseConfirmedBlockers,
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
    bool ShouldRunInteractive = false);

internal sealed record WinGetPostRetryDiagnosis(
    string Diagnostics,
    bool ShouldRunInteractive);

internal interface IWinGetProcessRecoveryPrompt
{
    bool ConfirmGracefulClose(UpdateItem item, IReadOnlyList<WinGetProcessCandidate> candidates);
    bool ConfirmForcedTermination(UpdateItem item, IReadOnlyList<WinGetProcessCandidate> candidates);
    bool ConfirmInteractiveInstaller(UpdateItem item);
    void ShowManualCloseRequired(UpdateItem item, string detail);
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
            .Select(blocker => new ClassifiedRestartManagerBlocker(blocker, Classify(blocker, context)))
            .ToList();
        if (query.RebootReason != RestartManagerRebootReason.None)
        {
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.RestartRequired,
                classified,
                $"Restart Manager richiede un riavvio: {query.RebootReason}.");
        }
        if (classified.Any(x => x.Classification == WinGetBlockerClassification.SystemOrService))
        {
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.ManualIntervention,
                classified,
                "Sono presenti processi di sistema o servizi che UpdateCenter non può chiudere.");
        }
        if (classified.Any(x => x.Classification == WinGetBlockerClassification.Unknown))
        {
            return new WinGetRecoveryDecision(
                WinGetRecoveryAction.ManualIntervention,
                classified,
                "Sono presenti blocker non attribuibili in sicurezza.");
        }
        return classified.Count == 0
            ? new WinGetRecoveryDecision(
                WinGetRecoveryAction.Retry,
                classified,
                "Restart Manager non rileva più blocker sulle risorse registrate.")
            : new WinGetRecoveryDecision(
                WinGetRecoveryAction.CloseConfirmedBlockers,
                classified,
                "Tutti i blocker rilevati sono package-owned o esterni confermati da Restart Manager.");
    }

    internal static WinGetBlockerClassification Classify(
        RestartManagerBlocker blocker,
        WinGetRecoveryContext context)
    {
        var processName = !string.IsNullOrWhiteSpace(blocker.ExecutablePath)
            ? Path.GetFileNameWithoutExtension(blocker.ExecutablePath)
            : blocker.ApplicationName;
        if (!string.IsNullOrWhiteSpace(blocker.ServiceShortName) ||
            blocker.ApplicationType is RestartManagerApplicationType.Service or
                RestartManagerApplicationType.Critical or RestartManagerApplicationType.Explorer ||
            WinGetProcessOperations.IsNeverCloseProcess(processName) ||
            WinGetProcessOperations.IsProtectedOrSharedPath(blocker.ExecutablePath))
            return WinGetBlockerClassification.SystemOrService;

        if (WinGetProcessOperations.IsAttributedProcess(
            processName,
            blocker.ExecutablePath,
            context.InstallRoots,
            context.ExecutablePaths)
            )
            return WinGetBlockerClassification.PackageOwned;

        if (string.IsNullOrWhiteSpace(blocker.ExecutablePath) || blocker.EvidenceResources.Count == 0)
            return WinGetBlockerClassification.Unknown;
        return WinGetBlockerClassification.ExternalConfirmedBlocker;
    }
}

internal sealed class WinGetProcessRecoveryService
{
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ForcedCloseTimeout = TimeSpan.FromSeconds(5);
    private readonly IWinGetProcessOperations _operations;
    private readonly IWindowsRestartManagerService _restartManager;

    public WinGetProcessRecoveryService() : this(new WinGetProcessOperations(), new WindowsRestartManagerService())
    {
    }

    internal WinGetProcessRecoveryService(
        IWinGetProcessOperations operations,
        IWindowsRestartManagerService restartManager)
    {
        _operations = operations;
        _restartManager = restartManager;
    }

    public WinGetRecoveryPreparation PrepareRetry(
        UpdateItem item,
        ItemRunResult failedResult,
        IWinGetProcessRecoveryPrompt prompt)
    {
        if (!failedResult.FailureReason.Equals(UpdateFailureReasons.FilesInUse, StringComparison.Ordinal))
            return new WinGetRecoveryPreparation(false, "L'esito non è classificato come file in uso.");

        var context = _operations.CreateContext(item);
        var diagnostics = new List<string>
        {
            $"PackageId esatto: {context.PackageId}.",
            $"InstallLocation: {string.Join("; ", context.InstallRoots)}.",
            $"DisplayIcon/eseguibili: {string.Join("; ", context.ExecutablePaths)}.",
            $"Shared resource roots: {string.Join("; ", context.SharedResourceRoots)}.",
            $"Shared resources registrate: {string.Join("; ", context.SharedResources)}."
        };
        var initial = QueryAndAssess(item, failedResult, context, "prima-della-chiusura", diagnostics);
        if (initial.Action == WinGetRecoveryAction.Retry)
            return ReadyToRetry(context, diagnostics, "Nessun blocker rilevato; retry autorizzato.");
        if (initial.Action == WinGetRecoveryAction.RestartManagerUnavailable)
            return UseConservativeFallback(item, failedResult, context, prompt, diagnostics);
        if (initial.Action is WinGetRecoveryAction.ManualIntervention or WinGetRecoveryAction.RestartRequired)
            return RequireManualIntervention(item, prompt, initial, diagnostics);

        var confirmedCandidates = ToConfirmedCandidates(initial.Blockers);
        if (confirmedCandidates.Count == 0)
        {
            diagnostics.Add("Restart Manager ha rilevato blocker del pacchetto, ma nessun PID è chiudibile in sicurezza.");
            prompt.ShowManualCloseRequired(item, BuildManualMessage(item, initial));
            return new WinGetRecoveryPreparation(false, string.Join(Environment.NewLine, diagnostics), context);
        }
        if (!prompt.ConfirmGracefulClose(item, confirmedCandidates))
        {
            diagnostics.Add("L'utente ha annullato la richiesta di chiusura pulita.");
            LogChoice(item, failedResult, "graceful-close", "cancelled", confirmedCandidates);
            return new WinGetRecoveryPreparation(false, string.Join(Environment.NewLine, diagnostics), context);
        }

        diagnostics.Add("Chiusura pulita confermata dall'utente: " + Describe(confirmedCandidates));
        LogChoice(item, failedResult, "graceful-close", "confirmed", confirmedCandidates);
        var gracefullyAuthorizedPids = confirmedCandidates.Select(x => x.ProcessId).ToHashSet();
        var processLevelRemaining = _operations.CloseGracefully(confirmedCandidates, GracefulCloseTimeout);
        diagnostics.Add(processLevelRemaining.Count == 0
            ? "CloseMainWindow: i PID proposti non risultano più attivi."
            : "CloseMainWindow: PID ancora attivi: " + Describe(processLevelRemaining));

        var afterClose = QueryAndAssess(item, failedResult, context, "dopo-chiusura-pulita", diagnostics);
        if (afterClose.Action == WinGetRecoveryAction.Retry)
            return ReadyToRetry(context, diagnostics, "Restart Manager conferma che le risorse non sono più bloccate.");
        if (afterClose.Action != WinGetRecoveryAction.CloseConfirmedBlockers)
            return RequireManualIntervention(item, prompt, afterClose, diagnostics);

        var remainingConfirmedCandidates = ToConfirmedCandidates(afterClose.Blockers);
        var newlyDetectedCandidates = remainingConfirmedCandidates
            .Where(candidate => !gracefullyAuthorizedPids.Contains(candidate.ProcessId))
            .ToList();
        if (newlyDetectedCandidates.Count > 0)
        {
            if (!prompt.ConfirmGracefulClose(item, newlyDetectedCandidates))
            {
                diagnostics.Add("Chiusura pulita dei nuovi blocker confermati non autorizzata; nessun retry.");
                LogChoice(item, failedResult, "graceful-close-new-blockers", "cancelled", newlyDetectedCandidates);
                return new WinGetRecoveryPreparation(false, string.Join(Environment.NewLine, diagnostics), context);
            }
            gracefullyAuthorizedPids.UnionWith(newlyDetectedCandidates.Select(x => x.ProcessId));
            diagnostics.Add("Chiusura pulita dei nuovi blocker confermati: " + Describe(newlyDetectedCandidates));
            LogChoice(item, failedResult, "graceful-close-new-blockers", "confirmed", newlyDetectedCandidates);
            _operations.CloseGracefully(newlyDetectedCandidates, GracefulCloseTimeout);
            afterClose = QueryAndAssess(
                item, failedResult, context, "dopo-chiusura-nuovi-blocker", diagnostics);
            if (afterClose.Action == WinGetRecoveryAction.Retry)
                return ReadyToRetry(context, diagnostics, "Restart Manager conferma che le risorse non sono più bloccate.");
            if (afterClose.Action != WinGetRecoveryAction.CloseConfirmedBlockers)
                return RequireManualIntervention(item, prompt, afterClose, diagnostics);
            remainingConfirmedCandidates = ToConfirmedCandidates(afterClose.Blockers);
        }

        if (remainingConfirmedCandidates.Any(candidate =>
                !gracefullyAuthorizedPids.Contains(candidate.ProcessId)))
        {
            diagnostics.Add("Sono comparsi ulteriori blocker non presentati all'utente; terminazione forzata non proposta.");
            return RequireManualIntervention(item, prompt, afterClose, diagnostics);
        }
        if (remainingConfirmedCandidates.Count == 0 ||
            !prompt.ConfirmForcedTermination(item, remainingConfirmedCandidates))
        {
            diagnostics.Add("Terminazione forzata non autorizzata; nessun retry.");
            LogChoice(item, failedResult, "forced-close", "cancelled", remainingConfirmedCandidates);
            return new WinGetRecoveryPreparation(false, string.Join(Environment.NewLine, diagnostics), context);
        }

        diagnostics.Add("Terminazione forzata confermata esplicitamente: " + Describe(remainingConfirmedCandidates));
        LogChoice(item, failedResult, "forced-close", "confirmed", remainingConfirmedCandidates);
        var survivedKill = _operations.Terminate(remainingConfirmedCandidates, ForcedCloseTimeout);
        diagnostics.Add(survivedKill.Count == 0
            ? "Kill(entireProcessTree=true): i PID proposti non risultano più attivi."
            : "Kill(entireProcessTree=true): PID ancora attivi: " + Describe(survivedKill));

        var afterKill = QueryAndAssess(item, failedResult, context, "dopo-terminazione-forzata", diagnostics);
        if (afterKill.Action == WinGetRecoveryAction.Retry)
            return ReadyToRetry(context, diagnostics, "Restart Manager conferma la rimozione dei blocker; retry autorizzato.");
        return RequireManualIntervention(item, prompt, afterKill, diagnostics);
    }

    public WinGetPostRetryDiagnosis DiagnoseFailedRetry(
        UpdateItem item,
        ItemRunResult retryResult,
        IWinGetProcessRecoveryPrompt prompt,
        WinGetRecoveryContext? preparedContext)
    {
        var context = preparedContext ?? _operations.CreateContext(item);
        var diagnostics = new List<string>();
        var decision = QueryAndAssess(item, retryResult, context, "dopo-retry-files-in-use", diagnostics);
        var message = BuildRetryFailureMessage(item, decision);
        retryResult.Message = message;
        var canOfferInteractive = decision.Action is WinGetRecoveryAction.Retry or
            WinGetRecoveryAction.RestartManagerUnavailable;
        var openInteractive = canOfferInteractive && prompt.ConfirmInteractiveInstaller(item);
        if (!canOfferInteractive)
            prompt.ShowManualCloseRequired(item, message);
        LogService.WriteEvent(
            "winget-recovery", "interactive-fallback",
            openInteractive ? "confirmed" : canOfferInteractive ? "cancelled" : "not-safe",
            item.Id, retryResult.ResultCode, message);
        diagnostics.Add("Messaggio finale: " + message);
        diagnostics.Add("Fallback interattivo: " + (openInteractive ? "confermato" : "non avviato"));
        return new WinGetPostRetryDiagnosis(
            string.Join(Environment.NewLine, diagnostics), openInteractive);
    }

    private WinGetRecoveryDecision QueryAndAssess(
        UpdateItem item,
        ItemRunResult failedResult,
        WinGetRecoveryContext context,
        string phase,
        ICollection<string> diagnostics)
    {
        var query = _restartManager.Query(context.RegisteredResources);
        var decision = WinGetRecoveryDecisionPolicy.Evaluate(query, context);
        diagnostics.Add($"Restart Manager [{phase}]: {query.Diagnostics}");
        diagnostics.Add("Classificazione blocker: " + Describe(decision.Blockers));
        diagnostics.Add($"Decisione [{phase}]: {decision.Action}; {decision.Reason}");
        LogService.WriteEvent(
            "winget-recovery", "restart-manager-" + phase,
            query.Succeeded ? decision.Action.ToString() : "failure",
            item.Id, failedResult.ResultCode,
            query.Diagnostics + Environment.NewLine + "Classificazione: " + Describe(decision.Blockers));
        return decision;
    }

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
            const string interactiveDetail =
                "Restart Manager non è disponibile e nessun processo è attribuibile con certezza al pacchetto.";
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
        var detail = fallback.Count == 0
            ? "Restart Manager non è disponibile e nessun processo è attribuibile con certezza al pacchetto. " +
              "Il retry automatico non è sicuro. Chiudi manualmente le applicazioni interessate o riavvia il PC."
            : "Restart Manager non è disponibile. I processi attribuibili al pacchetto sono: " +
              Describe(fallback) + ". Chiudili manualmente; il retry automatico non è sicuro.";
        diagnostics.Add("Fallback conservativo InstallLocation/DisplayIcon: " + detail);
        LogService.WriteEvent(
            "winget-recovery", "fallback-process-discovery", "manual-required",
            item.Id, failedResult.ResultCode, detail);
        prompt.ShowManualCloseRequired(item, detail);
        return new WinGetRecoveryPreparation(false, string.Join(Environment.NewLine, diagnostics), context);
    }

    private static WinGetRecoveryPreparation RequireManualIntervention(
        UpdateItem item,
        IWinGetProcessRecoveryPrompt prompt,
        WinGetRecoveryDecision decision,
        ICollection<string> diagnostics)
    {
        var detail = BuildManualMessage(item, decision);
        diagnostics.Add("Intervento manuale richiesto: " + detail);
        prompt.ShowManualCloseRequired(item, detail);
        return new WinGetRecoveryPreparation(false, string.Join(Environment.NewLine, diagnostics), Context: null);
    }

    private static WinGetRecoveryPreparation ReadyToRetry(
        WinGetRecoveryContext context,
        ICollection<string> diagnostics,
        string detail)
    {
        diagnostics.Add(detail);
        return new WinGetRecoveryPreparation(true, string.Join(Environment.NewLine, diagnostics), context);
    }

    private static string BuildManualMessage(UpdateItem item, WinGetRecoveryDecision decision)
    {
        if (decision.Action == WinGetRecoveryAction.RestartRequired)
            return $"Windows segnala che le risorse di {item.Name} richiedono un riavvio prima di riprovare.";
        var unknown = decision.Blockers
            .Where(x => x.Classification == WinGetBlockerClassification.Unknown)
            .Select(Describe)
            .ToList();
        if (unknown.Count > 0)
            return $"Windows segnala blocker non identificabili in sicurezza per {item.Name}: " +
                   $"{string.Join(", ", unknown)}. Non verranno terminati da UpdateCenter.";
        var system = decision.Blockers
            .Where(x => x.Classification == WinGetBlockerClassification.SystemOrService)
            .Select(Describe)
            .ToList();
        if (system.Count > 0)
            return $"Windows segnala componenti di sistema o servizi che bloccano {item.Name}: " +
                   $"{string.Join(", ", system)}. Non verranno terminati da UpdateCenter.";
        return $"Windows non permette di dimostrare che le risorse di {item.Name} siano libere. " +
               "Chiudi manualmente le applicazioni interessate o riavvia il PC e riprova.";
    }

    private static string BuildRetryFailureMessage(UpdateItem item, WinGetRecoveryDecision decision)
    {
        if (decision.Action == WinGetRecoveryAction.RestartManagerUnavailable || decision.Blockers.Count == 0)
        {
            return "L'installer segnala ancora file in uso, ma Windows non permette di identificare in sicurezza " +
                   "il processo responsabile. Riavvia il PC o chiudi manualmente le applicazioni interessate e riprova.";
        }
        return BuildManualMessage(item, decision);
    }

    private static IReadOnlyList<WinGetProcessCandidate> ToConfirmedCandidates(
        IEnumerable<ClassifiedRestartManagerBlocker> blockers) =>
        blockers
            .Where(x =>
                (x.Classification is WinGetBlockerClassification.PackageOwned or
                    WinGetBlockerClassification.ExternalConfirmedBlocker) &&
                x.Blocker.ProcessId > 0 &&
                !string.IsNullOrWhiteSpace(x.Blocker.ExecutablePath))
            .Select(x => new WinGetProcessCandidate(
                x.Blocker.ProcessId,
                Path.GetFileNameWithoutExtension(x.Blocker.ExecutablePath),
                x.Blocker.ExecutablePath,
                x.Classification))
            .DistinctBy(x => x.ProcessId)
            .ToList();

    private static string Describe(IEnumerable<WinGetProcessCandidate> candidates) =>
        string.Join(", ", candidates.Select(x => $"{x.ProcessName} (PID {x.ProcessId}, {x.ExecutablePath})"));

    private static string Describe(IEnumerable<ClassifiedRestartManagerBlocker> blockers) =>
        string.Join("; ", blockers.Select(Describe));

    private static string Describe(ClassifiedRestartManagerBlocker blocker) =>
        $"{blocker.Blocker.ApplicationName} (PID {blocker.Blocker.ProcessId}, " +
        $"tipo={blocker.Blocker.ApplicationType}, classe={blocker.Classification}, " +
        $"servizio={blocker.Blocker.ServiceShortName}, restartable={blocker.Blocker.Restartable}, " +
        $"reboot={blocker.Blocker.RebootReason}, path={blocker.Blocker.ExecutablePath}, " +
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
        if (!preparation.ShouldRetry)
        {
            if (preparation.ShouldRunInteractive && interactiveFallback is not null)
            {
                var interactiveResult = await RunInteractiveFallback(
                    item, initialResult, preparation, retryResult: null,
                    postRetryDiagnostics: "", interactiveFallback);
                return interactiveResult;
            }
            initialResult.Diagnostics = CombineDiagnostics(
                initialResult, preparation.Diagnostics, retryResult: null,
                postRetryDiagnostics: "", interactiveResult: null);
            return initialResult;
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
        if (diagnosis.ShouldRunInteractive && interactiveFallback is not null)
        {
            return await RunInteractiveFallback(
                item, initialResult, preparation, retryResult,
                diagnosis.Diagnostics, interactiveFallback);
        }
        retryResult.Diagnostics = CombineDiagnostics(
            initialResult, preparation.Diagnostics, retryResult, diagnosis.Diagnostics, interactiveResult: null);
        LogService.WriteEvent(
            "winget-recovery", "retry", retryResult.Success ? "success" : "failure",
            item.Id, retryResult.ResultCode,
            $"failureReason={retryResult.FailureReason}; verified={retryResult.Verified}; " +
            $"verification={retryResult.VerificationStatus}; nessun ulteriore retry automatico.");
        return retryResult;
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
