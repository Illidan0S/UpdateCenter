using UpdateCenter.Models;
using UpdateCenter.Services;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Data;
using UpdateCenter.Core;
using UpdateCenter.Contracts;
using UpdateCenter.RemoteClient;
using UpdateCenter.ViewModels;

if (args.Length == 2 && args[0].Equals("--hold-restart-manager-file", StringComparison.Ordinal))
{
    using var heldFile = new FileStream(args[1], FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    Console.WriteLine("READY");
    await Console.Out.FlushAsync();
    await Console.In.ReadLineAsync();
    return;
}

var parsingCases = new Dictionary<string, SemanticVersion>
{
    ["1.0.0"] = new(1, 0, 0),
    ["v1.0.1"] = new(1, 0, 1),
    ["1.1.0"] = new(1, 1, 0)
};

foreach (var (text, expected) in parsingCases)
{
    if (!SemanticVersion.TryParse(text, out var parsed) || parsed != expected)
        throw new InvalidOperationException($"Parsing semantico non riuscito per {text}.");
}

foreach (var invalid in new[] { "", "1.0", "1.0.0.0", "v1.1.0-beta", "release-1.0.0" })
{
    if (SemanticVersion.TryParse(invalid, out _))
        throw new InvalidOperationException($"Versione non valida accettata: {invalid}.");
}

var v100 = new SemanticVersion(1, 0, 0);
var v101 = new SemanticVersion(1, 0, 1);
var v110 = new SemanticVersion(1, 1, 0);
if (!(v100 < v101 && v101 < v110 && v110 > v100))
    throw new InvalidOperationException("Ordinamento semantico non valido.");
if (typeof(AppSettings).Assembly.GetName().Version?.ToString(3) != "1.1.4")
    throw new InvalidOperationException("La versione dell'assembly non corrisponde alla release 1.1.4.");

var defaultVerification = new UpdateVerificationResult();
if (defaultVerification.Verified || defaultVerification.IsDefinitive ||
    defaultVerification.Status != UpdateVerificationStatuses.NotRun)
    throw new InvalidOperationException("Lo stato iniziale della verifica post-installazione non è sicuro.");

var watchdogNow = new DateTime(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);
var silentInstallerStatus = new UpdateRunStatus
{
    State = "Running",
    CurrentItemStartedUtc = watchdogNow - TimeSpan.FromMinutes(20),
    LastProgressUtc = watchdogNow - TimeSpan.FromMinutes(13),
    LastHeartbeatUtc = watchdogNow - TimeSpan.FromSeconds(5)
};
var silentDecision = UpdateWatchdogPolicy.Evaluate(
    silentInstallerStatus, watchdogNow, UpdateWatchdogThresholds.Default);
if (silentDecision.ShouldTerminate || !silentDecision.ShouldWarnProgress)
    throw new InvalidOperationException("Un installer silenzioso con heartbeat vivo viene terminato erroneamente.");

silentInstallerStatus.LastHeartbeatUtc = watchdogNow - TimeSpan.FromSeconds(80);
var staleHeartbeatDecision = UpdateWatchdogPolicy.Evaluate(
    silentInstallerStatus, watchdogNow, UpdateWatchdogThresholds.Default);
if (!staleHeartbeatDecision.ShouldTerminate ||
    staleHeartbeatDecision.TerminationReason != "runner-heartbeat-timeout")
    throw new InvalidOperationException("Il watchdog non rileva un heartbeat runner fermo.");

silentInstallerStatus.LastHeartbeatUtc = watchdogNow;
silentInstallerStatus.LastProgressUtc = watchdogNow;
silentInstallerStatus.CurrentItemStartedUtc = watchdogNow - TimeSpan.FromMinutes(91);
var absoluteTimeoutDecision = UpdateWatchdogPolicy.Evaluate(
    silentInstallerStatus, watchdogNow, UpdateWatchdogThresholds.Default);
if (!absoluteTimeoutDecision.ShouldTerminate ||
    absoluteTimeoutDecision.TerminationReason != "absolute-item-timeout")
    throw new InvalidOperationException("Il timeout massimo assoluto non viene applicato.");

var heartbeatStatusPath = Path.Combine(
    Path.GetTempPath(), $"updatecenter-heartbeat-smoke-{Guid.NewGuid():N}.json");
var fixedProgressUtc = watchdogNow - TimeSpan.FromMinutes(13);
try
{
    var heartbeatStatus = new UpdateRunStatus
    {
        State = "Running",
        LastHeartbeatUtc = watchdogNow,
        LastProgressUtc = fixedProgressUtc
    };
    using (var publisher = new ElevatedUpdateRunner.RunnerStatusPublisher(
               heartbeatStatusPath, heartbeatStatus, TimeSpan.FromMilliseconds(15)))
    {
        var initialHeartbeat = JsonStorage.Read<UpdateRunStatus>(heartbeatStatusPath)?.LastHeartbeatUtc
            ?? throw new InvalidOperationException("Heartbeat iniziale non pubblicato.");
        Thread.Sleep(80);
        var refreshedHeartbeat = JsonStorage.Read<UpdateRunStatus>(heartbeatStatusPath)
            ?? throw new InvalidOperationException("Heartbeat periodico non pubblicato.");
        if (refreshedHeartbeat.LastHeartbeatUtc <= initialHeartbeat ||
            refreshedHeartbeat.LastProgressUtc != fixedProgressUtc)
            throw new InvalidOperationException("Heartbeat e progresso non sono mantenuti separati.");
    }
}
finally
{
    try { File.Delete(heartbeatStatusPath); } catch { }
}

var unavailableDecision = UpdateResultPolicy.Resolve(
    installerSucceeded: true,
    restartRequired: false,
    new UpdateVerificationResult
    {
        IsDefinitive = false,
        Status = UpdateVerificationStatuses.Unavailable
    });
var oldVersionDecision = UpdateResultPolicy.Resolve(
    installerSucceeded: true,
    restartRequired: false,
    new UpdateVerificationResult
    {
        IsDefinitive = true,
        Status = UpdateVerificationStatuses.Failed
    });
var rebootDecision = UpdateResultPolicy.Resolve(
    installerSucceeded: true,
    restartRequired: true,
    new UpdateVerificationResult
    {
        IsDefinitive = false,
        Status = UpdateVerificationStatuses.PendingRestart
    });
var verifiedAfterInstallerError = UpdateResultPolicy.Resolve(
    installerSucceeded: false,
    restartRequired: false,
    new UpdateVerificationResult
    {
        IsDefinitive = true,
        Verified = true,
        Status = UpdateVerificationStatuses.Verified
    });
if (!unavailableDecision.Success || unavailableDecision.Verified ||
    oldVersionDecision.Success ||
    !rebootDecision.Success || rebootDecision.Verified ||
    rebootDecision.VerificationStatus != UpdateVerificationStatuses.PendingRestart ||
    !verifiedAfterInstallerError.Success || !verifiedAfterInstallerError.Verified)
    throw new InvalidOperationException("La semantica finale installer/verifica/riavvio non è coerente.");

var verifiedRun = new ItemRunResult
{
    Success = true,
    InstallerSucceeded = true,
    Verified = true,
    Outcome = UpdateOutcomes.Completed
};
var unverifiedRun = new ItemRunResult
{
    Success = true,
    InstallerSucceeded = true,
    Verified = false,
    VerificationStatus = UpdateVerificationStatuses.Unavailable,
    Outcome = UpdateOutcomes.Completed
};
if (!MainViewModel.ShouldRemoveCompletedUpdate(verifiedRun) ||
    MainViewModel.ShouldRemoveCompletedUpdate(unverifiedRun))
    throw new InvalidOperationException("Gli aggiornamenti vengono rimossi senza una verifica positiva.");

var editableItems = new ArrayList { new EditableSmokeItem() };
var editableView = new ListCollectionView(editableItems);
((IEditableCollectionViewAddNewItem)editableView).AddNewItem(new object());
if (MainViewModel.TryRefreshCollectionView(editableView, "smoke-add-new"))
    throw new InvalidOperationException("Refresh WPF eseguito durante AddNew.");
editableView.CancelNew();
if (!MainViewModel.TryRefreshCollectionView(editableView, "smoke-after-add-new"))
    throw new InvalidOperationException("Refresh WPF non ripristinato dopo AddNew.");
editableView.EditItem(editableItems[0]!);
if (MainViewModel.TryRefreshCollectionView(editableView, "smoke-edit-item"))
    throw new InvalidOperationException("Refresh WPF eseguito durante EditItem.");
editableView.CancelEdit();
if (!MainViewModel.TryRefreshCollectionView(editableView, "smoke-after-edit-item"))
    throw new InvalidOperationException("Refresh WPF non ripristinato dopo EditItem.");

var supportedUpdateTargetMethod = typeof(AppUpdateService).GetMethod(
    "IsSupportedUpdateTargetName", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Validazione del nome dell'eseguibile da aggiornare non trovata.");
bool IsSupportedUpdateTarget(string name) =>
    Convert.ToBoolean(supportedUpdateTargetMethod.Invoke(null, [name]));
if (!IsSupportedUpdateTarget("UpdateCenter.exe") ||
    !IsSupportedUpdateTarget("UpdateCenter-v1.0.7.exe") ||
    !IsSupportedUpdateTarget("UpdateCenter-v1.0.7-Portable.exe") ||
    !IsSupportedUpdateTarget("UpdateCenter-Portable.exe") ||
    IsSupportedUpdateTarget("AltroProgramma.exe"))
    throw new InvalidOperationException("La selezione degli eseguibili aggiornabili non è sicura o non supporta standard e portable.");

var setupSelfUpdateStartInfoMethod = typeof(AppUpdateService).GetMethod(
    "CreateSetupSelfUpdateStartInfo", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Preparazione del self-update Setup non trovata.");
var setupSelfUpdateStartInfo = (ProcessStartInfo?)setupSelfUpdateStartInfoMethod.Invoke(null, [@"C:\Temp\UpdateCenter-Setup-v1.1.4.exe"])
    ?? throw new InvalidOperationException("Preparazione del self-update Setup non disponibile.");
var expectedSetupArguments = new[] { "/VERYSILENT", "/NORESTART", "/CLOSEAPPLICATIONS", "/SUPPRESSMSGBOXES", "/SELFUPDATE" };
if (!setupSelfUpdateStartInfo.UseShellExecute ||
    !setupSelfUpdateStartInfo.Verb.Equals("runas", StringComparison.OrdinalIgnoreCase) ||
    expectedSetupArguments.Except(setupSelfUpdateStartInfo.ArgumentList, StringComparer.OrdinalIgnoreCase).Any() ||
    setupSelfUpdateStartInfo.ArgumentList.Count(argument => argument.Equals("/SELFUPDATE", StringComparison.OrdinalIgnoreCase)) != 1)
    throw new InvalidOperationException("Il self-update Setup non passa una sola volta gli argomenti richiesti.");

var installerScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "installer.iss");
if (!File.Exists(installerScriptPath))
    throw new InvalidOperationException("Script Inno Setup non trovato per il controllo self-update.");
var installerScript = File.ReadAllText(installerScriptPath);
const string selfUpdateRunEntry = "Filename: \"{app}\\{#MyAppExeName}\"; WorkingDir: \"{app}\"; Flags: nowait runasoriginaluser; Check: IsSelfUpdate";
if (!installerScript.Contains("function IsSelfUpdate: Boolean;", StringComparison.Ordinal) ||
    !installerScript.Contains("ParamCount", StringComparison.Ordinal) ||
    !installerScript.Contains("ParamStr(Index)", StringComparison.Ordinal) ||
    !installerScript.Contains("'/SELFUPDATE'", StringComparison.Ordinal) ||
    installerScript.Split('\n').Count(line => line.Trim().Equals(selfUpdateRunEntry, StringComparison.Ordinal)) != 1)
    throw new InvalidOperationException("Il rilancio Inno Setup non è limitato a un solo self-update riuscito.");
const string manualRunEntry = "Filename: \"{app}\\{#MyAppExeName}\"; Description: \"{cm:LaunchUpdateCenter}\"; WorkingDir: \"{app}\"; Flags: nowait postinstall skipifsilent";
if (!installerScript.Contains(manualRunEntry, StringComparison.Ordinal) ||
    !IsSupportedUpdateTarget("UpdateCenter-v1.1.4-Portable.exe"))
    throw new InvalidOperationException("Il comportamento del Setup manuale o del percorso Portable è regredito.");

var settings = new AppSettings();
if (!settings.CheckAppUpdatesAutomatically)
    throw new InvalidOperationException("Il controllo automatico deve essere attivo per impostazione predefinita.");
if (!settings.NotifyWhenUpdatesAreAvailable || settings.LanguageMode != "it" || settings.AutomaticScanInterval != "Off")
    throw new InvalidOperationException("Le nuove preferenze predefinite non sono valide.");
var legacyIntervalSettings = new AppSettings { AutomaticScanInterval = "Ogni giorno" };
if (!legacyIntervalSettings.ApplyMigrations() || legacyIntervalSettings.AutomaticScanInterval != "Daily" ||
    AppSettings.NormalizeAutomaticScanInterval("Weekly") != "Weekly")
    throw new InvalidOperationException("La frequenza delle scansioni automatiche non viene normalizzata correttamente.");

var smallScale = TypographyOptions.ScaleFor("Piccola");
var mediumScale = TypographyOptions.ScaleFor("Media");
var largeScale = TypographyOptions.ScaleFor("Grande");
if (smallScale != 1.10 || !(smallScale < mediumScale && mediumScale < largeScale))
    throw new InvalidOperationException("La progressione delle dimensioni del testo non è valida.");

var legacyMediumSettings = new AppSettings { DefaultsRevision = 1, FontSizeMode = "Media" };
if (!legacyMediumSettings.ApplyMigrations() || legacyMediumSettings.FontSizeMode != "Piccola")
    throw new InvalidOperationException("La vecchia dimensione Media non è stata migrata a Piccola.");
var legacyLargeSettings = new AppSettings { DefaultsRevision = 1, FontSizeMode = "Grande" };
legacyLargeSettings.ApplyMigrations();
if (legacyLargeSettings.FontSizeMode != "Grande")
    throw new InvalidOperationException("La preferenza Grande non deve essere ridotta durante la migrazione.");

if (DriverVersionComparer.Compare("32.0.21043.19003", "32.0.21043.1000") <= 0 ||
    DriverVersionComparer.Compare("6.0.9954.1", "6.0.9954.1") != 0 ||
    DriverVersionComparer.Compare("25.040.2.218", "25.40.2.217") <= 0)
    throw new InvalidOperationException("Confronto delle versioni driver non valido.");

var wingetItalian = string.Join('\n',
    $"{"Nome",-24}{"Id",-25}{"Versione",-16}{"Disponibile",-16}Origine",
    new string('-', 90),
    $"{"Opera GX Stable",-24}{"Opera.OperaGX",-25}{"133.0.5932.39",-16}{"133.0.5932.56",-16}winget");
var wingetEnglish = string.Join('\n',
    $"{"Name",-24}{"Id",-25}{"Version",-16}{"Available",-16}Source",
    new string('-', 90),
    $"{"PowerToys (Preview)",-24}{"Microsoft.PowerToys",-25}{"0.90.0",-16}{"0.91.0",-16}winget");
var parsedItalian = WinGetService.ParseUpgradeTable(wingetItalian);
var parsedEnglish = WinGetService.ParseUpgradeTable(wingetEnglish);
var wingetRuntime = string.Join('\n',
    $"{"Name",-28}{"Id",-38}{"Version",-16}{"Available",-16}Source",
    new string('-', 104),
    $"{"Microsoft Visual C++",-28}{"Microsoft.VCRedist.2015+.x64",-38}{"14.40.1",-16}{"14.42.2",-16}winget");
var parsedRuntime = WinGetService.ParseUpgradeTable(wingetRuntime);
if (parsedItalian.Count != 1 || parsedItalian[0].Id != "Opera.OperaGX" ||
    parsedItalian[0].AvailableVersion != "133.0.5932.56")
    throw new InvalidOperationException("Parsing della tabella WinGet italiana non riuscito.");
if (parsedEnglish.Count != 1 || parsedEnglish[0].Id != "Microsoft.PowerToys")
    throw new InvalidOperationException("Parsing della tabella WinGet inglese non riuscito.");
if (parsedRuntime.Count != 1 || parsedRuntime[0].Kind != UpdateKind.Runtime)
    throw new InvalidOperationException("Un pacchetto runtime WinGet non è stato classificato correttamente.");

var installedInventory = string.Join('\n',
    $"{"Nome",-24}{"Id",-25}{"Versione",-16}{"Disponibile",-16}Origine",
    new string('-', 90),
    $"{"Opera GX Stable",-24}{"Opera.OperaGX",-25}{"133.0.5932.39",-16}{"",-16}winget");
var installedRows = WinGetService.ParsePackageRows(installedInventory);
var verificationAttempt = 0;
var delayedWinGetVerification = WinGetService.VerifyInstallation(
    new PlanItem
    {
        Id = "Example.DelayedInventory",
        Name = "Delayed Inventory",
        InstalledVersion = "1.0.0",
        AvailableVersion = "2.0.0"
    },
    (selector, value) =>
    {
        if (selector != "--id" || value != "Example.DelayedInventory")
            throw new InvalidOperationException("La verifica WinGet non usa l'ID esatto.");
        verificationAttempt++;
        var version = verificationAttempt == 1 ? "1.0.0" : "2.0.0";
        return (
            new ProcessResult(0, "", "", "winget list --id Example.DelayedInventory --exact"),
            new List<WinGetPackageRow>
            {
                new("Delayed Inventory", "Example.DelayedInventory", version, "", "winget")
            });
    },
    maxAttempts: 3,
    waitBeforeRetry: _ => { });
if (!delayedWinGetVerification.Verified || verificationAttempt != 2)
    throw new InvalidOperationException("La verifica WinGet non accetta l'inventario aggiornato a un retry successivo.");

var uninstalledCandidate = new UpdateItem
{
    Id = "Example.NotInstalled",
    Name = "Programma non installato",
    Kind = UpdateKind.Software,
    InstalledVersion = "1.0",
    AvailableVersion = "2.0"
};
var filterInstalledMethod = typeof(WinGetService).GetMethod(
    "FilterVerifiedInstalledCandidates", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Filtro delle installazioni WinGet verificate non trovato.");
var verifiedCandidates = ((IEnumerable<UpdateItem>?)filterInstalledMethod.Invoke(
    null, [parsedItalian.Concat([uninstalledCandidate]).ToList(), installedRows]))?.ToList()
    ?? throw new InvalidOperationException("Filtro delle installazioni WinGet non eseguibile.");
if (verifiedCandidates.Count != 1 || verifiedCandidates[0].Id != "Opera.OperaGX")
    throw new InvalidOperationException("Un software non installato è stato incluso negli aggiornamenti WinGet.");

if (WinGetService.ClassifyOutcome(new ProcessResult(unchecked((int)0x8A15002B), "", "")) != UpdateOutcomes.NotApplicable ||
    WinGetService.ClassifyOutcome(new ProcessResult(unchecked((int)0x8A15008E), "", "")) != UpdateOutcomes.ManualRequired ||
    WinGetService.ClassifyOutcome(new ProcessResult(unchecked((int)0x8A150114), "", "")) != UpdateOutcomes.ManualRequired ||
    WinGetService.ClassifyOutcome(new ProcessResult(0, "", "")) != UpdateOutcomes.Completed)
    throw new InvalidOperationException("Classificazione degli esiti WinGet non valida.");

var fileInUseResult = new ProcessResult(
    1,
    "",
    "OBS Studio is already running. Please close the application before continuing.");
var realItalianFileInUseResult = new ProcessResult(
    6,
    "I file modificati dal programma di installazione sono attualmente utilizzati da un'applicazione diversa.\n" +
    "Chiudere le applicazioni, quindi riprovare.\n" +
    "Programma di installazione non riuscito con codice di uscita: '6'",
    "");
var definitiveOldTargetDecision = UpdateResultPolicy.Resolve(
    installerSucceeded: false,
    restartRequired: false,
    new UpdateVerificationResult
    {
        IsDefinitive = true,
        Verified = false,
        Status = UpdateVerificationStatuses.Failed,
        Message = "La versione installata è ancora quella precedente."
    });
var realItalianFailureReason = WinGetService.ClassifyFailureReason(
    realItalianFileInUseResult,
    definitiveOldTargetDecision.Success);
if (!WinGetService.IsFileInUseFailure(fileInUseResult) ||
    realItalianFailureReason != UpdateFailureReasons.FilesInUse ||
    WinGetService.ClassifyFailureReason(
        new ProcessResult(6, "Errore generico dell'installer.", ""),
        finalSuccess: false) != UpdateFailureReasons.None)
    throw new InvalidOperationException("La classificazione conservativa dei file in uso non è valida.");

var obsRoot = @"C:\Program Files\obs-studio";
var obsPath = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe";
if (!WinGetProcessOperations.IsAttributedProcess("obs64", obsPath, [obsRoot], []) ||
    WinGetProcessOperations.IsAttributedProcess("obs64", @"C:\Tools\obs64.exe", [obsRoot], []) ||
    WinGetProcessOperations.IsAttributedProcess("explorer", obsPath, [obsRoot], [obsPath]) ||
    WinGetProcessOperations.RegistrationNameMatches("OBS Studio Beta", "OBS Studio"))
    throw new InvalidOperationException("L'attribuzione conservativa dei processi WinGet non è valida.");

var blockedItem = new UpdateItem
{
    Id = "OBSProject.OBSStudio",
    Name = "OBS Studio",
    Kind = UpdateKind.Software,
    InstalledVersion = "31.0",
    AvailableVersion = "31.1"
};
var blockedResult = new ItemRunResult
{
    Id = blockedItem.Id,
    Name = blockedItem.Name,
    Success = false,
    ResultCode = 1,
    FailureReason = UpdateFailureReasons.FilesInUse,
    Diagnostics = "Codice di uscita: 1\nOBS Studio is already running."
};
var expectedObsSharedRoot = Path.GetFullPath(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "obs-studio-hook"));
var obsHint = PackageRecoveryHints.Get("OBSProject.OBSStudio");
if (obsHint.SharedResourceRoots.Count != 1 ||
    !obsHint.SharedResourceRoots[0].Equals(expectedObsSharedRoot, StringComparison.OrdinalIgnoreCase) ||
    PackageRecoveryHints.Get("OBSProject.Other").SharedResourceRoots.Count != 0 ||
    PackageRecoveryHints.Get("obsproject.obsstudio").SharedResourceRoots.Count != 0)
    throw new InvalidOperationException("Gli hint shared-resource OBS non usano il package ID esatto.");

var interactivePlanItem = ElevatedUpdateRunner.ToPlanItem(blockedItem);
var interactiveArguments = WinGetService.BuildInteractiveArguments(interactivePlanItem);
var interactiveArgumentList = interactiveArguments.ToList();
var interactiveIdIndex = interactiveArgumentList.IndexOf("--id");
var interactiveSourceIndex = interactiveArgumentList.IndexOf("--source");
if (interactiveArguments.Contains("--silent", StringComparer.OrdinalIgnoreCase) ||
    interactiveArguments.Contains("--disable-interactivity", StringComparer.OrdinalIgnoreCase) ||
    !interactiveArguments.Contains("--interactive", StringComparer.OrdinalIgnoreCase) ||
    !interactiveArguments.Contains("--exact", StringComparer.OrdinalIgnoreCase) ||
    !interactiveArguments.Contains("--id", StringComparer.OrdinalIgnoreCase) ||
    !interactiveArguments.Contains("--source", StringComparer.OrdinalIgnoreCase) ||
    !interactiveArguments.Contains("winget", StringComparer.OrdinalIgnoreCase) ||
    interactiveIdIndex < 0 || interactiveIdIndex + 1 >= interactiveArgumentList.Count ||
    interactiveArgumentList[interactiveIdIndex + 1] != blockedItem.Id ||
    interactiveSourceIndex < 0 || interactiveSourceIndex + 1 >= interactiveArgumentList.Count ||
    interactiveArgumentList[interactiveSourceIndex + 1] != "winget")
    throw new InvalidOperationException("Il comando WinGet interattivo non mantiene i vincoli richiesti.");

var interactiveVerificationCalls = 0;
var evaluatedInteractiveResult = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(
    interactivePlanItem,
    new ProcessResult(6, "Installer interattivo terminato.", "", "winget interactive"),
    _ =>
    {
        interactiveVerificationCalls++;
        return new UpdateVerificationResult
        {
            IsDefinitive = true,
            Verified = true,
            Status = UpdateVerificationStatuses.Verified,
            Message = "Versione target installata.",
            Diagnostics = "QueryInstalled exact ID: target confermato."
        };
    });
if (interactiveVerificationCalls != 1 || !evaluatedInteractiveResult.Success ||
    !evaluatedInteractiveResult.Verified || evaluatedInteractiveResult.ResultCode != 6 ||
    evaluatedInteractiveResult.Phase != "winget-interactive")
    throw new InvalidOperationException("L'installer interattivo non applica sempre la verifica post-installazione.");

var obsHookPath = Path.Combine(expectedObsSharedRoot, "obs-hook.dll");
var obsCandidate = new WinGetProcessCandidate(1234, "obs64", obsPath);
var recoveryContext = new WinGetRecoveryContext(
    blockedItem.Id,
    [obsRoot],
    [obsPath],
    [expectedObsSharedRoot],
    [obsHookPath],
    [obsPath, obsHookPath],
    [obsCandidate]);
var packageBlocker = new RestartManagerBlocker(
    obsCandidate.ProcessId,
    "OBS Studio",
    "",
    RestartManagerApplicationType.MainWindow,
    0,
    true,
    RestartManagerRebootReason.None,
    obsPath,
    [obsPath]);
var externalBlocker = new RestartManagerBlocker(
    4321,
    "Plugin host esterno",
    "",
    RestartManagerApplicationType.OtherWindow,
    0,
    false,
    RestartManagerRebootReason.None,
    @"C:\Tools\plugin-host.exe",
    [obsHookPath]);
var unknownBlocker = new RestartManagerBlocker(
    5432,
    "Processo senza evidenza",
    "",
    RestartManagerApplicationType.OtherWindow,
    0,
    false,
    RestartManagerRebootReason.None,
    @"C:\Tools\unknown.exe",
    []);
var serviceBlocker = new RestartManagerBlocker(
    888,
    "Servizio condiviso",
    "SharedService",
    RestartManagerApplicationType.Service,
    0,
    false,
    RestartManagerRebootReason.None,
    @"C:\Windows\System32\svchost.exe",
    [obsHookPath]);

var packageDecision = WinGetRecoveryDecisionPolicy.Evaluate(
    SuccessfulRestartManagerQuery(obsPath, packageBlocker), recoveryContext);
var externalDecision = WinGetRecoveryDecisionPolicy.Evaluate(
    SuccessfulRestartManagerQuery(obsPath, externalBlocker), recoveryContext);
var unknownDecision = WinGetRecoveryDecisionPolicy.Evaluate(
    SuccessfulRestartManagerQuery(obsPath, unknownBlocker), recoveryContext);
var serviceDecision = WinGetRecoveryDecisionPolicy.Evaluate(
    SuccessfulRestartManagerQuery(obsPath, serviceBlocker), recoveryContext);
var noBlockerDecision = WinGetRecoveryDecisionPolicy.Evaluate(
    SuccessfulRestartManagerQuery(obsPath), recoveryContext);
if (packageDecision.Action != WinGetRecoveryAction.CloseConfirmedBlockers ||
    packageDecision.Blockers.Single().Classification != WinGetBlockerClassification.PackageOwned ||
    externalDecision.Action != WinGetRecoveryAction.CloseConfirmedBlockers ||
    externalDecision.Blockers.Single().Classification != WinGetBlockerClassification.ExternalConfirmedBlocker ||
    unknownDecision.Action != WinGetRecoveryAction.ManualIntervention ||
    unknownDecision.Blockers.Single().Classification != WinGetBlockerClassification.Unknown ||
    serviceDecision.Action != WinGetRecoveryAction.ManualIntervention ||
    serviceDecision.Blockers.Single().Classification != WinGetBlockerClassification.SystemOrService ||
    noBlockerDecision.Action != WinGetRecoveryAction.Retry)
    throw new InvalidOperationException("La policy Restart Manager dei blocker non è conservativa.");

var noBlockerOperations = new FakeWinGetProcessOperations(recoveryContext, [], []);
var noBlockerPrompt = new FakeWinGetRecoveryPrompt(confirmClose: true, confirmKill: false);
var noBlockerService = new WinGetProcessRecoveryService(
    noBlockerOperations,
    new FakeRestartManagerService(SuccessfulRestartManagerQuery(obsPath)));
var noBlockerPreparation = noBlockerService.PrepareRetry(blockedItem, blockedResult, noBlockerPrompt);
if (!noBlockerPreparation.ShouldRetry || noBlockerOperations.CloseCalls != 0)
    throw new InvalidOperationException("L'assenza di blocker Restart Manager non autorizza il retry diretto.");

var gracefulOperations = new FakeWinGetProcessOperations(recoveryContext, [], []);
var gracefulPrompt = new FakeWinGetRecoveryPrompt(confirmClose: true, confirmKill: false);
var gracefulService = new WinGetProcessRecoveryService(
    gracefulOperations,
    new FakeRestartManagerService(
        SuccessfulRestartManagerQuery(obsPath, packageBlocker),
        SuccessfulRestartManagerQuery(obsPath)));
var gracefulPreparation = gracefulService.PrepareRetry(blockedItem, blockedResult, gracefulPrompt);
if (!gracefulPreparation.ShouldRetry || gracefulOperations.CloseCalls != 1 ||
    gracefulOperations.KillCalls != 0)
    throw new InvalidOperationException("La chiusura pulita non viene verificata nuovamente con Restart Manager.");

var residualOperations = new FakeWinGetProcessOperations(recoveryContext, [], []);
var residualPrompt = new FakeWinGetRecoveryPrompt(confirmClose: true, confirmKill: false);
var residualService = new WinGetProcessRecoveryService(
    residualOperations,
    new FakeRestartManagerService(
        SuccessfulRestartManagerQuery(obsPath, packageBlocker),
        SuccessfulRestartManagerQuery(obsPath, packageBlocker)));
var residualPreparation = residualService.PrepareRetry(blockedItem, blockedResult, residualPrompt);
if (residualPreparation.ShouldRetry || residualOperations.CloseCalls != 1 ||
    residualOperations.KillCalls != 0 || residualPrompt.KillPrompts != 1)
    throw new InvalidOperationException("Un blocker residuo deve impedire il retry senza kill esplicito.");

var forcedOperations = new FakeWinGetProcessOperations(recoveryContext, [obsCandidate], []);
var forcedPrompt = new FakeWinGetRecoveryPrompt(confirmClose: true, confirmKill: true);
var forcedService = new WinGetProcessRecoveryService(
    forcedOperations,
    new FakeRestartManagerService(
        SuccessfulRestartManagerQuery(obsPath, packageBlocker),
        SuccessfulRestartManagerQuery(obsPath, packageBlocker),
        SuccessfulRestartManagerQuery(obsPath)));
var forcedPreparation = forcedService.PrepareRetry(blockedItem, blockedResult, forcedPrompt);
if (!forcedPreparation.ShouldRetry || forcedOperations.CloseCalls != 1 ||
    forcedOperations.KillCalls != 1 || forcedPrompt.KillPrompts != 1)
    throw new InvalidOperationException("La terminazione forzata confermata non viene riverificata con Restart Manager.");

var externalOperations = new FakeWinGetProcessOperations(recoveryContext, [], []);
var externalPrompt = new FakeWinGetRecoveryPrompt(confirmClose: true, confirmKill: true);
var externalService = new WinGetProcessRecoveryService(
    externalOperations,
    new FakeRestartManagerService(
        SuccessfulRestartManagerQuery(obsHookPath, externalBlocker),
        SuccessfulRestartManagerQuery(obsHookPath)));
var externalPreparation = externalService.PrepareRetry(blockedItem, blockedResult, externalPrompt);
if (!externalPreparation.ShouldRetry || externalOperations.CloseCalls != 1 ||
    externalOperations.KillCalls != 0 || externalPrompt.ManualPrompts != 0)
    throw new InvalidOperationException("Un blocker esterno confermato non segue la chiusura controllata.");

var unknownOperations = new FakeWinGetProcessOperations(recoveryContext, [], []);
var unknownPrompt = new FakeWinGetRecoveryPrompt(confirmClose: true, confirmKill: true);
var unknownService = new WinGetProcessRecoveryService(
    unknownOperations,
    new FakeRestartManagerService(SuccessfulRestartManagerQuery(obsHookPath, unknownBlocker)));
var unknownPreparation = unknownService.PrepareRetry(blockedItem, blockedResult, unknownPrompt);
if (unknownPreparation.ShouldRetry || unknownOperations.CloseCalls != 0 ||
    unknownOperations.KillCalls != 0 || unknownPrompt.ManualPrompts != 1)
    throw new InvalidOperationException("Un blocker senza evidenza RM non deve essere terminato.");

var unavailableContext = recoveryContext with { FallbackCandidates = [] };
var unavailableOperations = new FakeWinGetProcessOperations(unavailableContext, [], []);
var unavailablePrompt = new FakeWinGetRecoveryPrompt(
    confirmClose: true, confirmKill: true, confirmInteractive: true);
var unavailableService = new WinGetProcessRecoveryService(
    unavailableOperations,
    new FakeRestartManagerService(new RestartManagerQueryResult(
        Available: false,
        Succeeded: false,
        Resources: [obsPath],
        Blockers: [],
        RebootReason: RestartManagerRebootReason.None,
        ErrorCode: 1,
        Diagnostics: "Restart Manager non disponibile.")));
var unavailablePreparation = unavailableService.PrepareRetry(blockedItem, blockedResult, unavailablePrompt);
if (unavailablePreparation.ShouldRetry || !unavailablePreparation.ShouldRunInteractive ||
    unavailableOperations.CloseCalls != 0 || unavailableOperations.KillCalls != 0 ||
    unavailablePrompt.InteractivePrompts != 1)
    throw new InvalidOperationException("Il fallback interattivo non è disponibile senza blocker identificabili.");

var realItalianBlockedResult = new ItemRunResult
{
    Id = blockedItem.Id,
    Name = blockedItem.Name,
    Success = definitiveOldTargetDecision.Success,
    InstallerSucceeded = false,
    Verified = definitiveOldTargetDecision.Verified,
    VerificationStatus = definitiveOldTargetDecision.VerificationStatus,
    ResultCode = realItalianFileInUseResult.ExitCode,
    FailureReason = realItalianFailureReason,
    Diagnostics = realItalianFileInUseResult.StandardOutput
};
var recoveryRequestCount = 0;
var recoveryRetryCount = 0;
var recoveryRequestedResult = await WinGetSingleRetryPolicy.ExecuteAsync(
    blockedItem,
    realItalianBlockedResult,
    () =>
    {
        recoveryRequestCount++;
        return new WinGetRecoveryPreparation(false, "Chiusura manuale richiesta.");
    },
    () =>
    {
        recoveryRetryCount++;
        return Task.FromResult(new ItemRunResult { Id = blockedItem.Id, Success = true });
    });
if (recoveryRequestCount != 1 || recoveryRetryCount != 0 ||
    recoveryRequestedResult.FailureReason != UpdateFailureReasons.FilesInUse)
    throw new InvalidOperationException("Il fallimento WinGet italiano per file in uso non richiede la recovery.");

var genericRecoveryRequestCount = 0;
var genericFailedResult = CloneBlockedResult(realItalianBlockedResult);
genericFailedResult.FailureReason = UpdateFailureReasons.None;
await WinGetSingleRetryPolicy.ExecuteAsync(
    blockedItem,
    genericFailedResult,
    () =>
    {
        genericRecoveryRequestCount++;
        return new WinGetRecoveryPreparation(true, "Non deve essere raggiunto.");
    },
    () => Task.FromResult(new ItemRunResult { Id = blockedItem.Id, Success = true }));
if (genericRecoveryRequestCount != 0)
    throw new InvalidOperationException("Un fallimento WinGet generico ha attivato la recovery processi.");

var failedRetryCount = 0;
var failedRetryDiagnosisCount = 0;
var failedRetryResult = await WinGetSingleRetryPolicy.ExecuteAsync(
    blockedItem,
    CloneBlockedResult(blockedResult),
    () => new WinGetRecoveryPreparation(true, "Processo chiuso."),
    () =>
    {
        failedRetryCount++;
        return Task.FromResult(new ItemRunResult
        {
            Id = blockedItem.Id,
            Success = false,
            ResultCode = 2,
            FailureReason = UpdateFailureReasons.FilesInUse,
            Diagnostics = "Retry fallito."
        });
    },
    (_, _) =>
    {
        failedRetryDiagnosisCount++;
        return new WinGetPostRetryDiagnosis(
            "Blocker residuo diagnosticato; nessun secondo retry.",
            ShouldRunInteractive: false);
    });
if (failedRetryResult.Success || failedRetryCount != 1 ||
    failedRetryDiagnosisCount != 1 ||
    !failedRetryResult.Diagnostics.Contains("nessun", StringComparison.OrdinalIgnoreCase) &&
    !failedRetryResult.Diagnostics.Contains("Retry unico", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Un retry fallito non deve generare ulteriori tentativi.");

var interactiveRetryCount = 0;
var interactiveFallbackCount = 0;
var interactiveDiagnosisCount = 0;
var postRetryInteractiveResult = await WinGetSingleRetryPolicy.ExecuteAsync(
    blockedItem,
    CloneBlockedResult(blockedResult),
    () => new WinGetRecoveryPreparation(true, "Risorse verificate prima del retry."),
    () =>
    {
        interactiveRetryCount++;
        return Task.FromResult(new ItemRunResult
        {
            Id = blockedItem.Id,
            Success = false,
            ResultCode = 6,
            FailureReason = UpdateFailureReasons.FilesInUse,
            Diagnostics = "Retry silent ancora FilesInUse."
        });
    },
    (_, _) =>
    {
        interactiveDiagnosisCount++;
        return new WinGetPostRetryDiagnosis(
            "Restart Manager non identifica blocker.",
            ShouldRunInteractive: true);
    },
    () =>
    {
        interactiveFallbackCount++;
        return Task.FromResult(evaluatedInteractiveResult);
    });
if (!postRetryInteractiveResult.Success || !postRetryInteractiveResult.Verified ||
    interactiveRetryCount != 1 || interactiveDiagnosisCount != 1 || interactiveFallbackCount != 1 ||
    !postRetryInteractiveResult.Diagnostics.Contains("Installer interattivo", StringComparison.Ordinal))
    throw new InvalidOperationException("Il fallback interattivo post-retry non è singolo o non conserva la verifica.");

var verifiedRetryCount = 0;
var verifiedRetryResult = await WinGetSingleRetryPolicy.ExecuteAsync(
    blockedItem,
    CloneBlockedResult(blockedResult),
    () => new WinGetRecoveryPreparation(true, "Processo chiuso."),
    () =>
    {
        verifiedRetryCount++;
        return Task.FromResult(new ItemRunResult
        {
            Id = blockedItem.Id,
            Success = true,
            Verified = true,
            VerificationStatus = UpdateVerificationStatuses.Verified,
            ResultCode = 1,
            Diagnostics = "Target verificato nonostante l'exit code anomalo."
        });
    });
if (!verifiedRetryResult.Success || !verifiedRetryResult.Verified || verifiedRetryCount != 1 ||
    !verifiedRetryResult.Diagnostics.Contains("ResultCode=1", StringComparison.Ordinal))
    throw new InvalidOperationException("Il target verificato dopo retry non prevale sull'exit code anomalo.");

await VerifyRestartManagerIntegrationAsync();

var suppressedHytale = new WinGetApplicabilitySuppression
{
    PackageId = "HypixelStudios.Hytale",
    InstalledVersion = "2026.01.13-e6eb932",
    AvailableVersion = "2026.07.29-8228f98",
    RecordedUtc = DateTime.UtcNow
};
var sameHytaleUpdate = new UpdateItem
{
    Id = "HypixelStudios.Hytale",
    Name = "Hytale Launcher",
    Kind = UpdateKind.Software,
    InstalledVersion = "2026.01.13-e6eb932",
    AvailableVersion = "2026.07.29-8228f98"
};
var futureHytaleUpdate = new UpdateItem
{
    Id = "HypixelStudios.Hytale",
    Name = "Hytale Launcher",
    Kind = UpdateKind.Software,
    InstalledVersion = "2026.01.13-e6eb932",
    AvailableVersion = "2026.08.15-future"
};
if (!WinGetApplicabilityStore.Matches(suppressedHytale, sameHytaleUpdate) ||
    WinGetApplicabilityStore.Matches(suppressedHytale, futureHytaleUpdate))
    throw new InvalidOperationException("La quarantena WinGet non distingue correttamente una nuova versione futura.");
if (WinGetManifestSafetyService.ParseInstallerScope("InstallerType: nullsoft\nScope: user\n") != "user")
    throw new InvalidOperationException("L'ambito dell'installer WinGet non viene letto correttamente.");
WinGetManifestSafetyService.ApplyScopeCompatibility(sameHytaleUpdate, "machine", "user");
if (sameHytaleUpdate.CanInstall || sameHytaleUpdate.IsSelected ||
    !sameHytaleUpdate.Status.Contains("manuale", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Il cambio di ambito WinGet non viene bloccato in modo sicuro.");

var safeManifest = "PackageIdentifier: Example.Safe\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: install";
var destructiveManifest = "PackageIdentifier: Example.Risky\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: uninstallPrevious";
var deniedManifest = "PackageIdentifier: Example.Denied\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: deny";
var unknownManifest = "PackageIdentifier: Example.Unknown\nInstallerType: exe";
var mixedArchitectureManifest = "PackageIdentifier: Example.Mixed\nInstallers:\n" +
    "- Architecture: x64\n  UpgradeBehavior: install\n" +
    "- Architecture: x86\n  UpgradeBehavior: uninstallPrevious";
if (WinGetManifestSafetyService.ParseUpgradeSafety(safeManifest, "x64") != WinGetUpgradeSafety.Safe ||
    WinGetManifestSafetyService.ParseUpgradeSafety(destructiveManifest, "x64") != WinGetUpgradeSafety.RemovesPreviousVersion ||
    WinGetManifestSafetyService.ParseUpgradeSafety(deniedManifest, "x64") != WinGetUpgradeSafety.UpgradeUnsupported ||
    WinGetManifestSafetyService.ParseUpgradeSafety(mixedArchitectureManifest, "x64") != WinGetUpgradeSafety.Safe ||
    WinGetManifestSafetyService.ParseUpgradeSafety(unknownManifest) != WinGetUpgradeSafety.Unknown)
    throw new InvalidOperationException("Classificazione di sicurezza dei manifest WinGet non valida.");
var installerUris = WinGetManifestSafetyService.ParseInstallerUrls(
    "Installers:\n- Architecture: x64\n  InstallerUrl: https://example.com/package-x64.exe\n" +
    "- Architecture: x86\n  InstallerUrl: 'https://example.com/package-x86.exe'");
if (installerUris.Count != 2 || installerUris.Any(x => x.Scheme != Uri.UriSchemeHttps))
    throw new InvalidOperationException("Parsing delle dimensioni installer WinGet non valido.");
var manifestUris = WinGetManifestSafetyService.BuildManifestUris("JetBrains.CLion", "2026.2");
if (!manifestUris[0].AbsoluteUri.EndsWith(
        "/manifests/j/JetBrains/CLion/2026.2/JetBrains.CLion.installer.yaml",
        StringComparison.Ordinal))
    throw new InvalidOperationException("Percorso del manifest WinGet non valido.");

var riskySelection = new UpdateItem
{
    Id = "Example.Risky",
    Name = "Risky package",
    Kind = UpdateKind.Software,
    RequiresRiskConfirmation = true
};
riskySelection.IsSelected = false;
if (!riskySelection.CanInstall || riskySelection.IsSelected || riskySelection.PriorityLabel != "Conferma")
    throw new InvalidOperationException("Gli aggiornamenti rischiosi devono restare installabili ma non preselezionati.");

var repairableDriver = new DriverProblemItem
{
    DeviceId = "PCI\\VEN_14C3&DEV_0616",
    ErrorCode = 31,
    InstalledInfName = "oem42.inf",
    InstalledDriverSigned = true
};
var unsafeDriver = new DriverProblemItem
{
    DeviceId = "PCI\\VEN_14C3&DEV_0616",
    ErrorCode = 31,
    InstalledInfName = "..\\driver.inf",
    InstalledDriverSigned = true
};
if (!repairableDriver.CanRepairWithInstalledDriver || unsafeDriver.CanRepairWithInstalledDriver)
    throw new InvalidOperationException("La riparazione driver non limita correttamente i pacchetti OEM registrati da Windows.");
if (PreflightService.FormatBytes(125L * 1024 * 1024) != "125 MB")
    throw new InvalidOperationException("La dimensione dei pacchetti remoti non viene formattata correttamente.");

var networkAgent = new NetworkAgentItem();
networkAgent.Apply(new DiscoveredAgent
{
    AgentId = Guid.NewGuid(),
    DisplayName = "PC test",
    MachineName = "PC-TEST",
    Address = "192.168.1.25",
    ApiPort = 47382,
    ConnectionRequestsEnabled = true
}, isPaired: false);
networkAgent.ConnectionRequestStatus = "Collegamento accettato";
networkAgent.Apply(new PairedAgentRecord
{
    AgentId = networkAgent.AgentId,
    DisplayName = networkAgent.DisplayName,
    Address = networkAgent.Address,
    ApiPort = networkAgent.ApiPort,
    CertificateSha256 = new string('A', 64),
    PairedUtc = DateTime.UtcNow
});
if (networkAgent.AssociationText != "Autorizzato" || networkAgent.ConnectionRequestStatus.Length != 0)
    throw new InvalidOperationException("Lo stato terminale della richiesta contraddice l'autorizzazione del dispositivo.");
networkAgent.ConnectionRequestStatus = "Collegamento accettato";
networkAgent.MarkUnpaired(hasController: false);
if (networkAgent.AssociationText != "Non autorizzato" || networkAgent.ConnectionRequestStatus.Length != 0)
    throw new InvalidOperationException("La revoca non ripulisce correttamente lo stato del collegamento.");

var laptopAgent = new NetworkAgentItem();
var laptopId = Guid.NewGuid();
laptopAgent.Apply(new DiscoveredAgent
{
    AgentId = laptopId,
    DisplayName = "PORTATILE-IT",
    MachineName = "PORTATILE-IT",
    Address = "192.168.1.30",
    ApiPort = 47382
}, isPaired: false);
laptopAgent.SetScanResults(Guid.NewGuid(), new ScanResult
{
    HasBattery = true,
    IsOnBattery = true,
    BatteryPercentage = 55,
    SystemDriveFreeBytes = 20L * 1024 * 1024 * 1024,
    Updates = [new RemoteUpdateItem
    {
        Id = "Example.RemoteRisk",
        Name = "Aggiornamento rischioso",
        Kind = "Software",
        CanInstall = true,
        RequiresRiskConfirmation = true,
        DownloadSizeBytes = 125L * 1024 * 1024
    }]
});
var remoteRisk = laptopAgent.Updates.Single();
remoteRisk.IsSelected = true;
var remoteSummary = RemoteUpdateConfirmationService.Build([remoteRisk], [laptopAgent]);
if (!remoteSummary.PowerStatus.Contains("PORTATILE-IT", StringComparison.Ordinal) ||
    !remoteSummary.PowerStatus.Contains("55%", StringComparison.Ordinal) ||
    !remoteSummary.DiskStatus.Contains("125 MB", StringComparison.Ordinal) ||
    !remoteSummary.DiskStatus.Contains("20 GB", StringComparison.Ordinal) ||
    !remoteSummary.RiskItems.Single().Contains("PORTATILE-IT", StringComparison.Ordinal) ||
    remoteSummary.Warnings.Count != 1)
    throw new InvalidOperationException("Il riepilogo remoto non raggruppa correttamente dimensioni, portatili e rischi per PC.");

var duplicateOperaRows = string.Join('\n',
    $"{"Nome",-36}{"Id",-20}{"Versione",-16}{"Disponibile",-16}Origine",
    new string('-', 96),
    $"{"Opera GX Stable 133.0.5932.39",-36}{"Opera.OperaGX",-20}{"133.0.5932.39",-16}{"133.0.5932.56",-16}winget",
    $"{"Opera GX Stable 133.0.5932.39",-36}{"Opera.OperaGX",-20}{"133.0.5932.39",-16}{"",-16}winget");
var parseRowsMethod = typeof(WinGetService).GetMethod("ParsePackageRows", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Parser interno WinGet non trovato.");
var duplicateParsedRows = parseRowsMethod.Invoke(null, [duplicateOperaRows])
    ?? throw new InvalidOperationException("Parsing delle righe duplicate WinGet non riuscito.");
var resolveMatchMethod = typeof(WinGetService).GetMethod("ResolveExactInstalledMatch", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Risoluzione della corrispondenza WinGet non trovata.");
var resolvedOpera = resolveMatchMethod.Invoke(null,
    [duplicateParsedRows, "Opera GX Stable 133.0.5932.39", "Opera.OperaGX"]);
if (resolvedOpera is null)
    throw new InvalidOperationException("Le righe WinGet duplicate dello stesso pacchetto non sono state unificate.");

LocalizationService.Initialize("en");
if (LocalizationService.Translate("Aggiornamenti") != "Updates")
    throw new InvalidOperationException("Traduzione inglese non disponibile.");
LocalizationService.Initialize("it");

var catalogAssembly = typeof(AppSettings).Assembly;
var catalogResource = catalogAssembly.GetManifestResourceNames()
    .SingleOrDefault(x => x.EndsWith("driver-catalog.json", StringComparison.OrdinalIgnoreCase))
    ?? throw new InvalidOperationException("Catalogo driver incorporato nei test non trovato.");
using (var catalogStream = catalogAssembly.GetManifestResourceStream(catalogResource)!)
using (var catalogJson = JsonDocument.Parse(catalogStream))
{
    if (catalogJson.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
        catalogJson.RootElement.GetProperty("entries").ValueKind != JsonValueKind.Array)
        throw new InvalidOperationException("Schema del catalogo driver non valido.");
}

var missingDriver = HardwareInventoryService.DescribeDeviceError(28);
var disabledDevice = HardwareInventoryService.DescribeDeviceError(22);
if (!missingDriver.Title.Contains("mancante", StringComparison.OrdinalIgnoreCase) ||
    !missingDriver.Severity.Equals("Critico", StringComparison.Ordinal) ||
    !disabledDevice.Title.Contains("disabilitato", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Classificazione dei problemi PnP non valida.");

var runtimeDependencies = await new GameDependencyService().ScanAsync(CancellationToken.None);
if (!runtimeDependencies.Any(x => x.Name.Contains("DirectX", StringComparison.OrdinalIgnoreCase)) ||
    !runtimeDependencies.Any(x => x.Name.Contains("Visual C++", StringComparison.OrdinalIgnoreCase)) ||
    !runtimeDependencies.Any(x => x.Name.Contains("Vulkan", StringComparison.OrdinalIgnoreCase)) ||
    !runtimeDependencies.Any(x => x.Name.Contains(".NET", StringComparison.OrdinalIgnoreCase)) ||
    !runtimeDependencies.Any(x => x.Name.Contains("PhysX", StringComparison.OrdinalIgnoreCase)) ||
    !runtimeDependencies.Any(x => x.Name.Contains("WebView2", StringComparison.OrdinalIgnoreCase)) ||
    !runtimeDependencies.Any(x => x.Name.Contains("Java", StringComparison.OrdinalIgnoreCase)) ||
    !runtimeDependencies.Any(x => x.Name.Contains("XNA", StringComparison.OrdinalIgnoreCase)) ||
    !runtimeDependencies.Any(x => x.Name.Contains("Mono", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Catalogo dei runtime incompleto.");
var missingRuntime = new GameDependencyItem
{
    Name = "Runtime non installato",
    PackageId = "Example.Runtime",
    IsAvailable = false
};
var installedRuntimeUpdate = new UpdateItem
{
    Id = "Microsoft.VCRedist.2015+.x64",
    Name = "Microsoft Visual C++ 2015–2022",
    Kind = UpdateKind.Runtime,
    PackageOperation = PackageOperations.Upgrade
};
if (missingRuntime.CanAutoInstall ||
    runtimeDependencies.Where(x => !x.IsAvailable).Any(x => x.CanAutoInstall) ||
    installedRuntimeUpdate.KindLabel != "Runtime" ||
    installedRuntimeUpdate.PackageOperation != PackageOperations.Upgrade ||
    !RuntimePackageCatalog.IsRuntimePackageId("Microsoft.VCRedist.2015+.x64") ||
    !RuntimePackageCatalog.IsRuntimePackageId("Microsoft.DotNet.DesktopRuntime.9"))
    throw new InvalidOperationException("I runtime mancanti non devono essere proposti come aggiornamenti installabili.");

var packageSize = PreflightService.CalculatePackageSize(
[
    new UpdateItem { Id = "Known", Name = "Known", Kind = UpdateKind.Software, DownloadSizeBytes = 150 * 1024 * 1024 },
    new UpdateItem { Id = "Unknown", Name = "Unknown", Kind = UpdateKind.Software }
]);
if (packageSize.TotalBytes != 150L * 1024 * 1024 || packageSize.KnownCount != 1 || packageSize.UnknownCount != 1)
    throw new InvalidOperationException("Calcolo delle dimensioni selezionate non valido.");

var clipboardHardware = new SystemHardwareInfo();
clipboardHardware.ApplyOverview(new HardwareOverviewSnapshot(
    "CPU test", "8 / 16", "GPU test · A\0M\0D GPU", "GPU dedicata rilevata", "12 GB",
    "GPU test: 12 GB", "Non esposta dal driver o da Windows", "VRAM DEDICATA IN USO",
    GpuMemoryDisplayMode.Discrete, "32 GB", "1920x1080", "60 Hz", "Windows 11", "PC test"));
clipboardHardware.ApplyMetrics(new HardwareMetricsSnapshot(
    10, 20, 30, "6 GB", 2L * 1024 * 1024 * 1024, 0, 16L * 1024 * 1024 * 1024,
    "GPU test", 55, null, "Test"));
if (!clipboardHardware.HasCpuTemperature || clipboardHardware.HasGpuTemperature)
    throw new InvalidOperationException("La visibilità condizionale delle temperature hardware non è valida.");
var clipboardText = HardwareClipboardService.Build(
    clipboardHardware,
    [
        new DriverInventoryItem { DeviceName = "Chipset test", IsProcessorOrChipset = true, InstalledVersion = "1.2.3", Provider = "CPU vendor" },
        new DriverInventoryItem { DeviceName = "GPU test", DeviceClass = "Display", InstalledVersion = "4.5.6", Provider = "GPU vendor" }
    ],
    [
        new StorageDeviceItem { Name = "SSD interno", MediaType = "SSD", SizeBytes = 1_000_000_000_000, BusType = "NVMe", HealthStatus = "Healthy" },
        new StorageDeviceItem { Name = "Chiavetta", MediaType = "SSD", SizeBytes = 64_000_000_000, BusType = "USB", HealthStatus = "Healthy" }
    ]);
if (!clipboardText.Contains("SSD interno", StringComparison.Ordinal) ||
    clipboardText.Contains("Chiavetta", StringComparison.Ordinal) ||
    !clipboardText.Contains("Windows 11", StringComparison.Ordinal) ||
    !clipboardText.Contains("Chipset test: 1.2.3", StringComparison.Ordinal) ||
    !clipboardText.Contains("GPU test: 4.5.6", StringComparison.Ordinal) ||
    !clipboardText.Contains("VRAM principale: 12 GB", StringComparison.Ordinal) ||
    !clipboardText.Contains("AMD GPU", StringComparison.Ordinal) ||
    clipboardText.Contains('\0'))
    throw new InvalidOperationException("Il riepilogo hardware non contiene tutti i dati richiesti.");

if (GpuPresentationService.Classify("Intel(R) Iris(R) Xe Graphics") != GpuAdapterKind.Integrated ||
    GpuPresentationService.Classify("Intel Arc A770 Graphics") != GpuAdapterKind.Discrete ||
    GpuPresentationService.Classify("NVIDIA GeForce RTX 4070 Ti") != GpuAdapterKind.Discrete ||
    GpuPresentationService.Classify("AMD Radeon(TM) Graphics") != GpuAdapterKind.Integrated ||
    GpuPresentationService.Classify("Microsoft Basic Display Adapter") != GpuAdapterKind.Virtual ||
    GpuPresentationService.Classify("Intel Arc Graphics") != GpuAdapterKind.Unknown)
    throw new InvalidOperationException("La classificazione adattiva delle GPU non è valida.");

var hybridGpuPresentation = GpuPresentationService.Build(
[
    new GpuAdapterDescriptor("Intel(R) Iris(R) Xe Graphics", 1024L * 1024 * 1024),
    new GpuAdapterDescriptor("Intel Arc A770 Graphics", 16L * 1024 * 1024 * 1024)
]);
if (!hybridGpuPresentation.ConfigurationLabel.Contains("ibrida", StringComparison.OrdinalIgnoreCase) ||
    !hybridGpuPresentation.AdaptersLabel.Contains("GPU integrata", StringComparison.Ordinal) ||
    !hybridGpuPresentation.AdaptersLabel.Contains("GPU dedicata", StringComparison.Ordinal) ||
    !hybridGpuPresentation.MemoryDetails.Contains("RAM condivisa", StringComparison.Ordinal) ||
    !hybridGpuPresentation.PrimaryMemoryLabel.Contains("16 GB dedicati", StringComparison.Ordinal) ||
    hybridGpuPresentation.MemoryDisplayMode != GpuMemoryDisplayMode.Hybrid)
    throw new InvalidOperationException("La presentazione delle configurazioni GPU ibride non è valida.");

var integratedGpuInfo = new SystemHardwareInfo();
integratedGpuInfo.ApplyOverview(new HardwareOverviewSnapshot(
    "CPU test", "4 / 8", "Intel Iris Xe — GPU integrata", "GPU integrata rilevata", "1 GB riservati",
    "Intel Iris Xe — 1 GB riservati; usa anche RAM condivisa dinamicamente",
    "Non esposta dal driver per questa GPU integrata", "MEMORIA GPU CONDIVISA IN USO",
    GpuMemoryDisplayMode.Integrated, "8 GB", "1920x1080", "60 Hz", "Windows 11", "Notebook test"));
integratedGpuInfo.ApplyMetrics(new HardwareMetricsSnapshot(
    10, 20, 1, "2 GB", 64L * 1024 * 1024, 1L * 1024 * 1024 * 1024,
    4L * 1024 * 1024 * 1024, "Windows", null, null, "Test"));
if (integratedGpuInfo.GpuMemoryUsageHeading != "MEMORIA GPU CONDIVISA IN USO" ||
    !integratedGpuInfo.VramUsed.Contains("1 GB di 4 GB condivisi", StringComparison.Ordinal))
    throw new InvalidOperationException("La memoria condivisa delle GPU integrate non viene presentata correttamente.");

var nvidiaAppKnownPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    "NVIDIA Corporation", "NVIDIA app", "CEF", "NVIDIA App.exe");
if (File.Exists(nvidiaAppKnownPath))
{
    var findNvidiaApp = typeof(HardwareInventoryService).GetMethod(
        "FindNvidiaAppExecutable", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Rilevamento NVIDIA App non trovato.");
    var detectedNvidiaApp = Convert.ToString(findNvidiaApp.Invoke(null, null));
    if (!string.Equals(detectedNvidiaApp, nvidiaAppKnownPath, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("NVIDIA App installata non è stata rilevata.");
}

var quickHardware = await new QuickHardwareDataService(
    new HardwareInventoryService(),
    new StorageHealthService()).LoadAsync(CancellationToken.None);
if (quickHardware.Hardware is null || quickHardware.Hardware.Drivers.Count == 0)
    throw new InvalidOperationException("L'inventario hardware rapido non ha rilevato i driver installati.");
var storageHealth = quickHardware.Storage
    ?? throw new InvalidOperationException("L'inventario hardware rapido non ha rilevato lo storage.");
if (storageHealth.Volumes.Count == 0)
    throw new InvalidOperationException("Il controllo storage non ha rilevato alcun volume locale.");
var storageRows = StorageTableRowFactory.CreateRows(storageHealth.Devices);
if (storageRows.Count != storageHealth.Devices.Count ||
    storageRows.Any(x => x.KindLabel != "Unità fisica"))
    throw new InvalidOperationException("La tabella storage deve contenere una sola riga per unità fisica.");
var mappedRow = StorageTableRowFactory.CreateRows(
[
    new StorageDeviceItem
    {
        Name = "SSD test",
        MediaType = "SSD",
        BusType = "NVMe",
        SizeBytes = 1_000_000_000_000,
        HealthStatus = "Healthy",
        Volumes =
        [
            new StorageVolumeItem
            {
                DriveLetter = "E",
                Label = "FAST Disk",
                FileSystem = "NTFS",
                SizeBytes = 1_000_000_000_000,
                FreeBytes = 100_000_000_000
            }
        ]
    }
]).Single();
if (!mappedRow.VolumesLabel.Contains("E:", StringComparison.Ordinal) ||
    !mappedRow.VolumesLabel.Contains("FAST Disk", StringComparison.Ordinal) ||
    !mappedRow.VolumesDetail.StartsWith("E:", StringComparison.Ordinal) ||
    mappedRow.CapacityLabel == "—")
    throw new InvalidOperationException("L'associazione tra unità fisica e volume non è valida.");

var multiVolumeRow = StorageTableRowFactory.CreateRows(
[
    new StorageDeviceItem
    {
        Name = "SSD multivolume",
        SizeBytes = 2_000_000_000_000,
        Volumes =
        [
            new StorageVolumeItem { DriveLetter = "Z", FileSystem = "FAT32", SizeBytes = 100_000_000, FreeBytes = 50_000_000 },
            new StorageVolumeItem { DriveLetter = "C", FileSystem = "NTFS", SizeBytes = 1_999_000_000_000, FreeBytes = 500_000_000_000 }
        ]
    }
]).Single();
if (!multiVolumeRow.VolumesLabel.StartsWith("C:", StringComparison.Ordinal) ||
    !multiVolumeRow.VolumesDetail.StartsWith("C: · NTFS", StringComparison.Ordinal) ||
    !multiVolumeRow.VolumesDetail.Contains("Z: · FAT32", StringComparison.Ordinal))
    throw new InvalidOperationException("Il volume principale deve precedere le piccole partizioni nella tabella storage.");

var windowsSizedRow = StorageTableRowFactory.CreateRows(
[
    new StorageDeviceItem
    {
        SizeBytes = 2_000_398_934_016,
        Volumes =
        [
            new StorageVolumeItem
            {
                DriveLetter = "C",
                FileSystem = "NTFS",
                SizeBytes = 1_825_364_418_560,
                FreeBytes = 79_135_057_920
            },
            new StorageVolumeItem
            {
                DriveLetter = "Z",
                FileSystem = "FAT32",
                SizeBytes = 100_663_296,
                FreeBytes = 61_236_900
            }
        ]
    }
]).Single();
var normalizedCapacity = windowsSizedRow.CapacityLabel.Replace('.', ',');
var normalizedVolumeDetail = windowsSizedRow.VolumesDetail.Replace('.', ',');
if (normalizedCapacity != "1,81 TB" ||
    !normalizedVolumeDetail.Contains("73,7 GB liberi di 1,66 TB", StringComparison.Ordinal) ||
    !normalizedVolumeDetail.Contains("58,4 MB liberi di 96,0 MB", StringComparison.Ordinal))
    throw new InvalidOperationException("Le capacità storage devono usare la precisione a tre cifre di Esplora file.");

var systemFat32Volume = new StorageVolumeItem
{
    DriveLetter = "Z",
    FileSystem = "FAT32",
    SizeBytes = 100_663_296,
    FreeBytes = 61_271_040
};
var labeledFat32Volume = new StorageVolumeItem
{
    DriveLetter = "F",
    Label = "DATI",
    FileSystem = "FAT32",
    SizeBytes = 536_870_912
};
if (StorageHealthService.IsUserVisibleVolume(systemFat32Volume) ||
    !StorageHealthService.IsUserVisibleVolume(labeledFat32Volume))
    throw new InvalidOperationException("Le partizioni FAT32 di sistema non devono essere mostrate come volumi dati.");

var repairedHistoryText = JsonStorage.RepairLegacyEncoding("Hytale Launcher non ÃƒÂ¨ applicabile: versione piÃ¹ recente");
if (repairedHistoryText != "Hytale Launcher non è applicabile: versione più recente")
    throw new InvalidOperationException("La riparazione della codifica nella cronologia non è valida.");

if (UserMessageFormatter.FromException(new TaskCanceledException("The operation was canceled.")) !=
        "tempo di attesa scaduto" ||
    UserMessageFormatter.FromException(new InvalidOperationException("RateLimited: Attendi alcuni secondi.")) !=
        "Attendi alcuni secondi.")
    throw new InvalidOperationException("La traduzione degli errori tecnici non è valida.");

var pauseController = new UpdatePauseController(Path.GetTempPath());
pauseController.RequestPause();
if (!pauseController.IsPauseRequested)
    throw new InvalidOperationException("Il segnale di pausa non è stato creato.");
pauseController.Resume();
if (pauseController.IsPauseRequested)
    throw new InvalidOperationException("Il segnale di pausa non è stato rimosso.");
pauseController.Cleanup();

Console.WriteLine("Smoke test superati: aggiornamenti, diagnostica driver, runtime, storage e pausa.");

static ItemRunResult CloneBlockedResult(ItemRunResult source) => new()
{
    Id = source.Id,
    Name = source.Name,
    Success = source.Success,
    ResultCode = source.ResultCode,
    FailureReason = source.FailureReason,
    Diagnostics = source.Diagnostics
};

static RestartManagerQueryResult SuccessfulRestartManagerQuery(
    string resource,
    params RestartManagerBlocker[] blockers) =>
    new(
        Available: true,
        Succeeded: true,
        Resources: [resource],
        Blockers: blockers,
        RebootReason: RestartManagerRebootReason.None,
        ErrorCode: 0,
        Diagnostics: blockers.Length == 0 ? "Nessun blocker." : "Blocker rilevati.");

static async Task VerifyRestartManagerIntegrationAsync()
{
    if (!OperatingSystem.IsWindows())
        return;

    var testDirectory = Path.Combine(
        Path.GetTempPath(), $"updatecenter-restart-manager-{Guid.NewGuid():N}");
    Directory.CreateDirectory(testDirectory);
    var lockedFile = Path.Combine(testDirectory, "locked-module.dll");
    await File.WriteAllBytesAsync(lockedFile, [0x4D, 0x5A, 0x00, 0x00]);
    Process? helper = null;
    try
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Percorso del processo SmokeTests non disponibile.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        startInfo.ArgumentList.Add("--hold-restart-manager-file");
        startInfo.ArgumentList.Add(lockedFile);
        helper = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Helper Restart Manager non avviato.");
        var ready = await helper.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (!string.Equals(ready, "READY", StringComparison.Ordinal))
        {
            var error = await helper.StandardError.ReadToEndAsync();
            throw new InvalidOperationException("Helper Restart Manager non sincronizzato. " + error);
        }

        var service = new WindowsRestartManagerService();
        var sharedResources = WinGetProcessOperations.EnumerateSharedResources([testDirectory]);
        if (sharedResources.Count != 1 ||
            !sharedResources[0].Equals(lockedFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La risorsa shared temporanea non è stata enumerata in sicurezza.");
        var whileLocked = service.Query(sharedResources);
        if (!whileLocked.Succeeded || whileLocked.Blockers.All(x => x.ProcessId != helper.Id))
            throw new InvalidOperationException(
                $"Restart Manager non ha restituito il PID helper {helper.Id}. {whileLocked.Diagnostics}");
        var sharedContext = new WinGetRecoveryContext(
            "Test.SharedPackage",
            [Path.Combine(testDirectory, "primary-install-root")],
            [],
            [testDirectory],
            sharedResources,
            sharedResources,
            []);
        var sharedDecision = WinGetRecoveryDecisionPolicy.Evaluate(whileLocked, sharedContext);
        var helperClassification = sharedDecision.Blockers
            .Single(x => x.Blocker.ProcessId == helper.Id)
            .Classification;
        if (helperClassification != WinGetBlockerClassification.ExternalConfirmedBlocker)
            throw new InvalidOperationException(
                $"Il locker della shared resource è stato classificato come {helperClassification}.");

        await helper.StandardInput.WriteLineAsync();
        if (!helper.WaitForExit(5000))
            throw new InvalidOperationException("Helper Restart Manager non terminato entro il limite.");

        var afterRelease = service.Query(sharedResources);
        if (!afterRelease.Succeeded || afterRelease.Blockers.Any(x => x.ProcessId == helper.Id))
            throw new InvalidOperationException(
                $"Restart Manager segnala ancora il PID helper {helper.Id}. {afterRelease.Diagnostics}");
        Console.WriteLine("Restart Manager integration test: OK");
    }
    finally
    {
        if (helper is not null)
        {
            if (!helper.HasExited)
            {
                try { helper.Kill(entireProcessTree: true); } catch { }
                helper.WaitForExit(2000);
            }
            helper.Dispose();
        }
        try { Directory.Delete(testDirectory, recursive: true); } catch { }
    }
}

sealed class EditableSmokeItem : IEditableObject
{
    public void BeginEdit() { }
    public void CancelEdit() { }
    public void EndEdit() { }
}

sealed class FakeWinGetProcessOperations(
    WinGetRecoveryContext context,
    IReadOnlyList<WinGetProcessCandidate> remainingAfterClose,
    IReadOnlyList<WinGetProcessCandidate> remainingAfterKill) : IWinGetProcessOperations
{
    public int CloseCalls { get; private set; }
    public int KillCalls { get; private set; }

    public WinGetRecoveryContext CreateContext(UpdateItem item) => context;

    public IReadOnlyList<WinGetProcessCandidate> CloseGracefully(
        IReadOnlyList<WinGetProcessCandidate> processCandidates,
        TimeSpan timeout)
    {
        CloseCalls++;
        return remainingAfterClose;
    }

    public IReadOnlyList<WinGetProcessCandidate> Terminate(
        IReadOnlyList<WinGetProcessCandidate> processCandidates,
        TimeSpan timeout)
    {
        KillCalls++;
        return remainingAfterKill;
    }
}

sealed class FakeRestartManagerService(params RestartManagerQueryResult[] results)
    : IWindowsRestartManagerService
{
    private int _index;

    public RestartManagerQueryResult Query(IReadOnlyCollection<string> resources)
    {
        if (results.Length == 0)
            throw new InvalidOperationException("Nessun risultato Restart Manager configurato.");
        var result = results[Math.Min(_index, results.Length - 1)];
        _index++;
        return result;
    }
}

sealed class FakeWinGetRecoveryPrompt(
    bool confirmClose,
    bool confirmKill,
    bool confirmInteractive = false) : IWinGetProcessRecoveryPrompt
{
    public int KillPrompts { get; private set; }

    public bool ConfirmGracefulClose(
        UpdateItem item,
        IReadOnlyList<WinGetProcessCandidate> candidates) => confirmClose;

    public bool ConfirmForcedTermination(
        UpdateItem item,
        IReadOnlyList<WinGetProcessCandidate> candidates)
    {
        KillPrompts++;
        return confirmKill;
    }

    public int ManualPrompts { get; private set; }
    public int InteractivePrompts { get; private set; }

    public bool ConfirmInteractiveInstaller(UpdateItem item)
    {
        InteractivePrompts++;
        return confirmInteractive;
    }

    public void ShowManualCloseRequired(UpdateItem item, string detail) => ManualPrompts++;
}
