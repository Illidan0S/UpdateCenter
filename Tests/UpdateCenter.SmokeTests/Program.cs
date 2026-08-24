using UpdateCenter.Models;
using UpdateCenter.Services;
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Windows.Data;
using UpdateCenter.Core;
using UpdateCenter.Contracts;
using UpdateCenter.RemoteClient;
using UpdateCenter.ViewModels;

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

sealed class EditableSmokeItem : IEditableObject
{
    public void BeginEdit() { }
    public void CancelEdit() { }
    public void EndEdit() { }
}
