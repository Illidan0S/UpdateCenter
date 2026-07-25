using UpdateCenter.Models;
using UpdateCenter.Services;
using System.Reflection;
using System.Text.Json;

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
if (typeof(AppSettings).Assembly.GetName().Version?.ToString(3) != "1.0.7")
    throw new InvalidOperationException("La versione dell'assembly non corrisponde alla build 1.0.7.");

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

if (WinGetService.ClassifyOutcome(new ProcessResult(unchecked((int)0x8A15002B), "", "")) != UpdateOutcomes.NotApplicable ||
    WinGetService.ClassifyOutcome(new ProcessResult(unchecked((int)0x8A15008E), "", "")) != UpdateOutcomes.ManualRequired ||
    WinGetService.ClassifyOutcome(new ProcessResult(unchecked((int)0x8A150114), "", "")) != UpdateOutcomes.ManualRequired ||
    WinGetService.ClassifyOutcome(new ProcessResult(0, "", "")) != UpdateOutcomes.Completed)
    throw new InvalidOperationException("Classificazione degli esiti WinGet non valida.");

var safeManifest = "PackageIdentifier: Example.Safe\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: install";
var destructiveManifest = "PackageIdentifier: Example.Risky\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: uninstallPrevious";
var unknownManifest = "PackageIdentifier: Example.Unknown\nInstallerType: exe";
if (WinGetManifestSafetyService.ParseUpgradeSafety(safeManifest) != WinGetUpgradeSafety.Safe ||
    WinGetManifestSafetyService.ParseUpgradeSafety(destructiveManifest) != WinGetUpgradeSafety.RemovesPreviousVersion ||
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
var runtimeInstall = new UpdateItem
{
    Id = "Microsoft.VCRedist.2015+.x64",
    Name = "Microsoft Visual C++ 2015–2022",
    Kind = UpdateKind.Runtime,
    PackageOperation = PackageOperations.Install
};
if (runtimeInstall.KindLabel != "Runtime" || runtimeInstall.PackageOperation != PackageOperations.Install ||
    !RuntimePackageCatalog.IsRuntimePackageId("Microsoft.VCRedist.2015+.x64") ||
    !RuntimePackageCatalog.IsRuntimePackageId("Microsoft.DotNet.DesktopRuntime.9"))
    throw new InvalidOperationException("Il flusso di installazione dei runtime non è configurato.");

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
    "GPU test: 12 GB", "Non esposta dal driver o da Windows", "32 GB", "1920x1080", "60 Hz", "Windows 11", "PC test"));
clipboardHardware.ApplyMetrics(new HardwareMetricsSnapshot(
    10, 20, 30, "6 GB", "2 GB", "GPU test", 55, null, "Test"));
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
    !hybridGpuPresentation.PrimaryMemoryLabel.Contains("16 GB dedicati", StringComparison.Ordinal))
    throw new InvalidOperationException("La presentazione delle configurazioni GPU ibride non è valida.");

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
    mappedRow.CapacityLabel == "—")
    throw new InvalidOperationException("L'associazione tra unità fisica e volume non è valida.");

var pauseController = new UpdatePauseController(Path.GetTempPath());
pauseController.RequestPause();
if (!pauseController.IsPauseRequested)
    throw new InvalidOperationException("Il segnale di pausa non è stato creato.");
pauseController.Resume();
if (pauseController.IsPauseRequested)
    throw new InvalidOperationException("Il segnale di pausa non è stato rimosso.");
pauseController.Cleanup();

Console.WriteLine("Smoke test superati: aggiornamenti, diagnostica driver, runtime, storage e pausa.");
