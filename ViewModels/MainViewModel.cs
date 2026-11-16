using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Threading;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ScanServices _scans;
    private readonly SystemHardwareService _systemHardware = new();
    private readonly Lazy<NetworkManagementViewModel> _network = new(() => new NetworkManagementViewModel());
    private readonly UpdateCoordinator _coordinator = new();
    private readonly AppUpdateService _appUpdateService = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _isBusy;
    private int _updateRunActive;
    private bool _isScanRunning;
    private bool _initialSelectionPending;
    private bool _hasStartedScan;
    private bool _scanSelectionOverridden;
    private bool _applyingInitialSelection;
    private readonly HashSet<UpdateItem> _scanSelectionTouched = [];
    private readonly Dictionary<UpdateItem, bool> _observedSelections = [];
    private bool _isBlockedUpdatesExpanded;
    private string? _revealedBlockedQuery;
    private double _progress;
    private bool _isProgressIndeterminate;
    private string _statusText = LocalizationService.Text("Pronto per la scansione", "Ready to scan");
    private string _currentItemText = LocalizationService.Text("Premi Avvia scansione per iniziare.", "Select Start scan to begin.");
    private string _searchText = "";
    private string _updateTypeFilter = "Tutti";
    private string _driverSearchText = "";
    private string _driverInventoryFilter = "Tutti";
    private int _scannedCount;
    private string _cpuName = "Processore non ancora rilevato";
    private string _computerName = "";
    private string _hardwareCheckStatus = "Esegui una scansione per controllare automaticamente i driver.";
    private string _gameDependencyStatus = "Esegui una scansione per controllare i runtime condivisi.";
    private string _storageHealthStatus = "Esegui una scansione per controllare la salute dello storage.";
    private bool _hasCurrentScan;
    private bool _hardwareOverviewLoaded;
    private bool _hardwareOverviewLoading;
    private bool _hardwareMetricsLoading;
    private readonly object _startupInventoryGate = new();
    private Task<QuickHardwareSnapshot>? _startupInventoryTask;
    private bool _startupInventorySuperseded;
    private bool _isAppUpdateCheckBusy;
    private UpdatePauseController? _activePauseController;
    private bool _isUpdatePaused;
    private bool _isUpdatePauseRequested;
    private string _appUpdateStatus = LocalizationService.Text(
        "Controllo aggiornamenti dell'app non ancora eseguito.",
        "The app update check has not run yet.");

    public MainViewModel() : this(ScanServices.Create())
    {
        _ = Network;
    }

    internal MainViewModel(ScanServices scans)
    {
        _scans = scans;
        AppPaths.EnsureCreated();
        Settings = JsonStorage.LoadSettings();
        if (Settings.LastAppUpdateCheckUtc is DateTime lastUpdateCheck)
            _appUpdateStatus = T(
                $"Ultimo controllo: {lastUpdateCheck.ToLocalTime():dd/MM/yyyy HH:mm}.",
                $"Last check: {lastUpdateCheck.ToLocalTime():MM/dd/yyyy HH:mm}.");
        foreach (var entry in JsonStorage.LoadHistory()) History.Add(entry);
        UpdatesView = CollectionViewSource.GetDefaultView(Updates);
        UpdatesView.Filter = FilterUpdate;
        ManagedUpdatesView = new ListCollectionView(Updates) { Filter = value => value is UpdateItem { CanInstall: true } && FilterUpdate(value) };
        BlockedUpdatesView = new ListCollectionView(Updates) { Filter = value => value is UpdateItem { CanInstall: false } && FilterUpdate(value) };
        foreach (var section in new[] { ManagedUpdatesView, BlockedUpdatesView })
        {
            section.SortDescriptions.Add(new SortDescription(nameof(UpdateItem.Name), ListSortDirection.Ascending));
            section.SortDescriptions.Add(new SortDescription(nameof(UpdateItem.Id), ListSortDirection.Ascending));
            if (section is ICollectionViewLiveShaping { CanChangeLiveFiltering: true } live)
            {
                live.LiveFilteringProperties.Add(nameof(UpdateItem.CanInstall));
                live.IsLiveFiltering = true;
            }
            section.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(ManagedUpdatesHeader));
                OnPropertyChanged(nameof(BlockedUpdatesHeader));
            };
        }
        UpdatesView.SortDescriptions.Add(new SortDescription(nameof(UpdateItem.CanInstall), ListSortDirection.Descending));
        UpdatesView.SortDescriptions.Add(new SortDescription(nameof(UpdateItem.Name), ListSortDirection.Ascending));
        UpdatesView.SortDescriptions.Add(new SortDescription(nameof(UpdateItem.Id), ListSortDirection.Ascending));
        if (UpdatesView is ICollectionViewLiveShaping { CanChangeLiveSorting: true } sortableUpdates)
        {
            sortableUpdates.LiveSortingProperties.Add(nameof(UpdateItem.CanInstall));
            sortableUpdates.IsLiveSorting = true;
        }
        DriverInventoryView = CollectionViewSource.GetDefaultView(DriverInventory);
        DriverInventoryView.Filter = FilterDriverInventory;
        DriverInventoryView.SortDescriptions.Add(
            new SortDescription(nameof(DriverInventoryItem.HasUpdate), ListSortDirection.Descending));
        DriverInventoryView.SortDescriptions.Add(
            new SortDescription(nameof(DriverInventoryItem.DisplayName), ListSortDirection.Ascending));
    }

    public ObservableCollection<UpdateItem> Updates { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public ObservableCollection<DriverInventoryItem> DriverInventory { get; } = [];
    public ObservableCollection<DriverProblemItem> DriverProblems { get; } = [];
    public ObservableCollection<GameDependencyItem> GameDependencies { get; } = [];
    public ObservableCollection<StorageDeviceItem> StorageDevices { get; } = [];
    public ObservableCollection<StorageVolumeItem> StorageVolumes { get; } = [];
    public ObservableCollection<StorageTableRow> StorageRows { get; } = [];
    public ObservableCollection<VendorSupportItem> VendorTools { get; } = [];
    public SystemHardwareInfo HardwareInfo { get; } = new();
    public ICollectionView UpdatesView { get; }
    public ICollectionView ManagedUpdatesView { get; }
    public ICollectionView BlockedUpdatesView { get; }
    public string ManagedUpdatesHeader => T($"Aggiornamenti gestibili ({ManagedUpdatesView.Cast<object>().Count()})",
        $"Manageable updates ({ManagedUpdatesView.Cast<object>().Count()})");
    public string BlockedUpdatesHeader => T($"Non gestibili automaticamente ({BlockedUpdatesView.Cast<object>().Count()})",
        $"Not automatically manageable ({BlockedUpdatesView.Cast<object>().Count()})");
    public bool IsBlockedUpdatesExpanded
    {
        get => _isBlockedUpdatesExpanded;
        set { if (_isBlockedUpdatesExpanded == value) return; _isBlockedUpdatesExpanded = value; OnPropertyChanged(); }
    }
    public ICollectionView DriverInventoryView { get; }
    public AppSettings Settings { get; }
    public AppUpdateService AppUpdateService => _appUpdateService;
    public NetworkManagementViewModel Network => _network.Value;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanScan));
            OnPropertyChanged(nameof(CanUpdate));
            OnPropertyChanged(nameof(CanPauseUpdates));
        }
    }

    public bool CanScan => !IsBusy;
    public bool CanUpdate => !IsBusy && Updates.Any(x => x.CanInstall && x.IsSelected);

    public bool IsScanRunning
    {
        get => _isScanRunning;
        private set { _isScanRunning = value; OnPropertyChanged(); }
    }

    public double Progress
    {
        get => _progress;
        private set { _progress = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    // Scan providers expose no reliable total: phase boundaries are not percentages.
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set { _isProgressIndeterminate = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    public string ProgressText => IsProgressIndeterminate
        ? T("In corso", "In progress")
        : Progress == 0 ? "—" : $"{Progress:0}%";

    public string StatusText
    {
        get => LocalizationService.Translate(_statusText);
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string CurrentItemText
    {
        get => LocalizationService.Translate(_currentItemText);
        private set { _currentItemText = value; OnPropertyChanged(); }
    }

    public int ScannedCount
    {
        get => _scannedCount;
        private set { _scannedCount = value; OnPropertyChanged(); }
    }

    public int AvailableCount => Updates.Count;
    public int SelectedCount => Updates.Count(x => x.CanInstall && x.IsSelected);
    public int VisibleUpdateCount => UpdatesView.Cast<object>().Count();
    public string UpdateAvailabilityLabel
    {
        get
        {
            var selectable = Updates.Count(item => item.CanInstall);
            var unavailable = AvailableCount - selectable;
            return T(
                $"{AvailableCount} rilevati · {selectable} selezionabili · {unavailable} non selezionabili",
                $"{AvailableCount} detected · {selectable} selectable · {unavailable} not selectable");
        }
    }
    public string UpdateSelectionHint => T(
        "Preselezione a fine scansione, salvo conferme specifiche e scelte personali.",
        "Preselection after scanning, respecting separate confirmations and your choices.");
    public string SelectAllHint => T(
        "Seleziona gli aggiornamenti disponibili. Gli elementi con conferma separata vengono inclusi solo dopo il tuo assenso.",
        "Selects available updates. Items requiring separate confirmation are included only after you agree.");
    public string SelectedCountLabel => LocalizationService.Text($"{SelectedCount} selezionati", $"{SelectedCount} selected");
    public string VisibleUpdateCountLabel => LocalizationService.Text($"{VisibleUpdateCount} risultati visibili", $"{VisibleUpdateCount} visible results");
    public int SoftwareUpdateCount => Updates.Count(x => x.Kind == UpdateKind.Software);
    public int RuntimeUpdateCount => Updates.Count(x => x.Kind == UpdateKind.Runtime);
    public int DriverCount => DriverInventory.Count;
    public int DriverProblemCount => DriverProblems.Count;
    public int MissingGameDependencyCount => GameDependencies.Count(x => !x.IsAvailable && !x.IsOptional);
    public int OptionalGameDependencyCount => GameDependencies.Count(x => !x.IsAvailable && x.IsOptional);
    public int StorageDeviceCount => StorageDevices.Count;
    public int StorageVolumeCount => StorageVolumes.Count;
    public int VisibleDriverCount => DriverInventoryView.Cast<object>().Count();
    public int ChipsetCount => DriverInventory.Count(x => x.IsProcessorOrChipset);
    public int DriverUpdateCount => Updates.Count(x => x.Kind == UpdateKind.Driver);
    public int VendorCheckCount => VendorTools.Count;
    public string LastScanLabel => Settings.LastScanUtc is DateTime timestamp
        ? LocalizationService.IsEnglish
            ? timestamp.ToLocalTime().ToString("MM/dd/yyyy 'at' HH:mm", LocalizationService.Culture)
            : timestamp.ToLocalTime().ToString("dd/MM/yyyy 'alle' HH:mm", LocalizationService.Culture)
        : LocalizationService.Text("Nessuna scansione completata", "No completed scans");
    public string HomeScanSummary => !_hasCurrentScan
        ? LocalizationService.Text("Avvia una scansione per ottenere risultati aggiornati.", "Start a scan to get current results.")
        : AvailableCount == 0
            ? LocalizationService.Text("Nessun aggiornamento disponibile al momento.", "No updates are currently available.")
            : LocalizationService.IsEnglish
                ? $"{SoftwareUpdateCount} software, {DriverUpdateCount} driver and {RuntimeUpdateCount} runtime updates to review."
                : $"{SoftwareUpdateCount} software, {DriverUpdateCount} driver e {RuntimeUpdateCount} runtime da controllare.";
    public IReadOnlyList<UpdateItem> SelectedItems => Updates.Where(x => x.CanInstall && x.IsSelected).ToList();
    public bool IsAppUpdateCheckBusy
    {
        get => _isAppUpdateCheckBusy;
        private set
        {
            _isAppUpdateCheckBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCheckAppUpdates));
        }
    }
    public bool CanCheckAppUpdates => !IsAppUpdateCheckBusy;
    public bool CanPauseUpdates => _activePauseController is not null && IsBusy &&
        (!IsUpdatePaused || IsUpdatePauseRequested);
    internal event Action<UpdateRunStatus>? InstallationProgressChanged;
    public bool IsUpdatePaused
    {
        get => _isUpdatePaused;
        private set
        {
            _isUpdatePaused = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PauseButtonText));
            OnPropertyChanged(nameof(UpdatePauseHint));
            OnPropertyChanged(nameof(CanPauseUpdates));
        }
    }
    public bool IsUpdatePauseRequested
    {
        get => _isUpdatePauseRequested;
        private set
        {
            _isUpdatePauseRequested = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PauseButtonText));
            OnPropertyChanged(nameof(UpdatePauseHint));
            OnPropertyChanged(nameof(CanPauseUpdates));
        }
    }
    public string PauseButtonText => IsUpdatePaused
        ? IsUpdatePauseRequested ? T("Riprendi", "Resume") : T("Ripresa richiesta", "Resume requested")
        : IsUpdatePauseRequested
            ? T("Annulla pausa", "Cancel pause")
            : T("Pausa dopo questo aggiornamento", "Pause after this update");
    public string UpdatePauseHint => IsUpdatePaused
        ? T("Batch in pausa tra due aggiornamenti.", "Batch paused between updates.")
        : IsUpdatePauseRequested
            ? T("Pausa richiesta dopo questo aggiornamento.", "Pause requested after this update.")
            : "";
    public string AppUpdateStatus
    {
        get => LocalizationService.Translate(_appUpdateStatus);
        private set { _appUpdateStatus = value; OnPropertyChanged(); }
    }
    public string LastAppUpdateCheckLabel => Settings.LastAppUpdateCheckUtc is DateTime timestamp
        ? LocalizationService.IsEnglish
            ? timestamp.ToLocalTime().ToString("MM/dd/yyyy 'at' HH:mm", LocalizationService.Culture)
            : timestamp.ToLocalTime().ToString("dd/MM/yyyy 'alle' HH:mm", LocalizationService.Culture)
        : LocalizationService.Text("Mai eseguito", "Never");

    public bool IsScheduledScanDue
    {
        get
        {
            var interval = Settings.AutomaticScanInterval switch
            {
                "Daily" => TimeSpan.FromDays(1),
                "Weekly" => TimeSpan.FromDays(7),
                _ => TimeSpan.MaxValue
            };
            return interval != TimeSpan.MaxValue &&
                   (Settings.LastScanUtc is not DateTime last || DateTime.UtcNow - last >= interval);
        }
    }

    public string CpuName
    {
        get => _cpuName;
        private set { _cpuName = value; OnPropertyChanged(); }
    }

    public string ComputerName
    {
        get => _computerName;
        private set { _computerName = value; OnPropertyChanged(); }
    }

    public string HardwareCheckStatus
    {
        get => LocalizationService.Translate(_hardwareCheckStatus);
        private set { _hardwareCheckStatus = value; OnPropertyChanged(); }
    }

    public string GameDependencyStatus
    {
        get => LocalizationService.Translate(_gameDependencyStatus);
        private set { _gameDependencyStatus = value; OnPropertyChanged(); }
    }

    public string StorageHealthStatus
    {
        get => LocalizationService.Translate(_storageHealthStatus);
        private set { _storageHealthStatus = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (_searchText == (value ?? "")) return; _searchText = value ?? ""; _revealedBlockedQuery = null; OnPropertyChanged(); RefreshUpdatesView(); }
    }

    public string UpdateTypeFilter
    {
        get => _updateTypeFilter;
        set { if (_updateTypeFilter == (value ?? "Tutti")) return; _updateTypeFilter = value ?? "Tutti"; _revealedBlockedQuery = null; OnPropertyChanged(); RefreshUpdatesView(); }
    }

    public string DriverSearchText
    {
        get => _driverSearchText;
        set { _driverSearchText = value ?? ""; OnPropertyChanged(); RefreshDriverInventoryView(); }
    }

    public string DriverInventoryFilter
    {
        get => _driverInventoryFilter;
        set { _driverInventoryFilter = value ?? "Tutti"; OnPropertyChanged(); RefreshDriverInventoryView(); }
    }

    private readonly SemaphoreSlim _pendingVerificationGate = new(1, 1);

    internal async Task ResumePendingVerificationAsync(CancellationToken token = default)
    {
        if (!await _pendingVerificationGate.WaitAsync(0, token)) return;
        try
        {
            if (await DeferredWinGetVerification.ReconcileAsync(History.ToArray(), token))
                JsonStorage.SaveHistory(History);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { LogService.Write("Ripresa della verifica automatica non disponibile.", ex); }
        finally { _pendingVerificationGate.Release(); }
    }

    public async Task ScanAsync()
    {
        if (IsBusy) return;
        using var diagnostics = ScanDiagnostics.Start();
        _initialSelectionPending = true;
        _hasStartedScan = true;
        _scanSelectionOverridden = false;
        _scanSelectionTouched.Clear();
        _revealedBlockedQuery = null;
        IsBlockedUpdatesExpanded = false;
        using var scanCancellation = new CancellationTokenSource();
        _scanCancellation = scanCancellation;
        var token = scanCancellation.Token;
        _startupInventorySuperseded = true;
        IsBusy = true;
        Progress = 0;
        IsProgressIndeterminate = true;
        ScannedCount = 0;
        _hasCurrentScan = false;
        OnPropertyChanged(nameof(HomeScanSummary));
        StatusText = T("Scansione in corso", "Scan in progress");
        CurrentItemText = T("Preparazione dei servizi di aggiornamento…", "Preparing update services…");
        ClearUpdates();
        ClearHardware();
        HardwareCheckStatus = T("Controllo automatico dei driver in corso…", "Automatic driver check in progress…");
        IsScanRunning = true;
        var completed = false;
        var warnings = new List<string>();
        QuickHardwareSnapshot? snapshot = null;
        HardwareScanResult? hardwareScan = null;
        var driverSources = new List<string>();
        var microsoftDriverCount = 0;
        var catalogDriverCount = 0;

        try
        {
            var quickHardwareTask = ScanDiagnostics.ObserveAsync("provider.quick-hardware", () => _scans.QuickHardware(token), token);
            token.ThrowIfCancellationRequested();
            await SetScanPhaseAsync("Ricerca degli aggiornamenti software…", "Searching for software updates…", token);
            try
            {
                var software = await ScanDiagnostics.ObserveAsync("provider.software",
                    () => _scans.Software(Settings.IncludeUnknownVersions, token), token);
                ScanDiagnostics.Count("software.returned", software.Count);
                await AddScanUpdatesAsync(software, token);
                ScannedCount += software.Count;
                await ResumePendingVerificationAsync(token);
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                warnings.Add(ex.Message);
                LogService.Write("Scansione software fallita.", ex);
            }

            token.ThrowIfCancellationRequested();
            await SetScanPhaseAsync("Inventario di processore, chipset e driver installati…", "Inventorying processor, chipset and installed drivers…", token);
            try
            {
                using (var wait = ScanDiagnostics.Begin("wait.quick-hardware", token))
                {
                    snapshot = await quickHardwareTask;
                    wait?.Complete();
                }
                token.ThrowIfCancellationRequested();
                ScanDiagnostics.Count("hardware.snapshot-reused", snapshot.Hardware is null ? 0 : 1);
                hardwareScan = snapshot.Hardware ?? await ScanDiagnostics.ObserveAsync("provider.hardware-fallback",
                    () => _scans.Hardware(token), token);
                ScanDiagnostics.Count("hardware.drivers", hardwareScan.Drivers.Count);
                ScannedCount += hardwareScan.Drivers.Count;
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                warnings.Add(ex.Message);
                LogService.Write("Inventario hardware fallito.", ex);
            }

            token.ThrowIfCancellationRequested();
            await SetScanPhaseAsync("Ricerca degli aggiornamenti driver…", "Searching for driver updates…", token);
            try
            {
                var driverScan = await ScanDiagnostics.ObserveAsync("provider.windows-update",
                    () => _scans.Drivers(token, hardwareScan?.Drivers), token);
                ScanDiagnostics.Count("windows-update.returned", driverScan.Updates.Count);
                ScanDiagnostics.Count("windows-update.source-warnings", driverScan.SourceWarnings.Count);
                await AddScanUpdatesAsync(driverScan.Updates, token);
                ScannedCount += driverScan.Updates.Count;
                microsoftDriverCount = driverScan.Updates.Count;
                driverSources.AddRange(driverScan.SourcesChecked);
                if (driverScan.SourceWarnings.Count > 0)
                    warnings.Add("Alcune sorgenti driver Microsoft non erano disponibili.");
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                warnings.Add(ex.Message);
                LogService.Write("Scansione driver fallita.", ex);
                HardwareCheckStatus = T("Controllo automatico dei driver non completato.", "The automatic driver check did not complete.");
            }

            if (hardwareScan is not null)
            {
                token.ThrowIfCancellationRequested();
                await SetScanPhaseAsync("Confronto con il catalogo verificato dei produttori…", "Comparing with the verified manufacturer catalog…", token);
                try
                {
                    var catalogScan = await ScanDiagnostics.ObserveAsync("provider.catalog",
                        () => _scans.Catalog(hardwareScan.Drivers, token), token);
                    ScanDiagnostics.Count("catalog.returned", catalogScan.Updates.Count);
                    ScanDiagnostics.Count("catalog.warnings", catalogScan.Warnings.Count);
                    await AddScanUpdatesAsync(catalogScan.Updates, token);
                    ScannedCount += catalogScan.Updates.Count;
                    catalogDriverCount = catalogScan.Updates.Count;
                    driverSources.AddRange(catalogScan.SourcesChecked);
                    if (catalogScan.Warnings.Count > 0)
                        warnings.Add("Alcune voci del catalogo driver sono state scartate perché non verificabili.");
                }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    warnings.Add(ex.Message);
                    LogService.Write("Catalogo driver ufficiali non disponibile.", ex);
                }

            }

            if (hardwareScan is not null)
            {
                token.ThrowIfCancellationRequested();
                ApplyHardware(hardwareScan);
                var sourceLabel = string.Join(", ", driverSources.Distinct(StringComparer.CurrentCultureIgnoreCase));
                var verifiedCount = microsoftDriverCount + catalogDriverCount;
                HardwareCheckStatus = verifiedCount > 0
                    ? T(
                        $"{verifiedCount} aggiornamenti driver verificati ({microsoftDriverCount} Microsoft, {catalogDriverCount} produttori). Fonti: {sourceLabel}.",
                        $"{verifiedCount} verified driver updates ({microsoftDriverCount} Microsoft, {catalogDriverCount} manufacturers). Sources: {sourceLabel}.")
                    : T(
                        $"Nessun driver installabile verificato. Controllate {driverSources.Distinct(StringComparer.CurrentCultureIgnoreCase).Count()} fonti automatiche; disponibili {hardwareScan.VendorTools.Count} controlli manuali ufficiali senza app aggiuntive.",
                        $"No verified installable drivers. Checked {driverSources.Distinct(StringComparer.CurrentCultureIgnoreCase).Count()} automatic sources; {hardwareScan.VendorTools.Count} manual manufacturer checks are available without extra apps.");
            }

            token.ThrowIfCancellationRequested();
            await SetScanPhaseAsync("Controllo runtime condivisi e salute dello storage…", "Checking shared runtimes and storage health…", token);
            try
            {
                var dependencies = await ScanDiagnostics.ObserveAsync("provider.dependencies",
                    () => _scans.Dependencies(token), token);
                ScanDiagnostics.Count("dependencies.returned", dependencies.Count);
                foreach (var dependency in dependencies)
                {
                    GameDependencies.Add(dependency);
                }
                var requiredMissing = GameDependencies.Count(x => !x.IsAvailable && !x.IsOptional);
                var optionalMissing = GameDependencies.Count(x => !x.IsAvailable && x.IsOptional);
                GameDependencyStatus = requiredMissing == 0
                    ? optionalMissing == 0
                        ? T("Tutti i runtime controllati risultano disponibili.", "All checked runtimes are available.")
                        : T($"Dipendenze principali disponibili; {optionalMissing} componenti opzionali non rilevati.", $"Core dependencies are available; {optionalMissing} optional components were not detected.")
                    : T($"{requiredMissing} dipendenze principali non rilevate.", $"{requiredMissing} core dependencies were not detected.");
                ScannedCount += dependencies.Count;
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                warnings.Add(ex.Message);
                GameDependencyStatus = T("Controllo dei runtime non completato.", "Runtime check did not complete.");
                LogService.Write("Scansione runtime fallita.", ex);
            }

            try
            {
                token.ThrowIfCancellationRequested();
                ScannedCount += await RefreshStorageHealthAsync(token, snapshot?.Storage);
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                warnings.Add(ex.Message);
                StorageHealthStatus = T("Controllo della salute dello storage non completato.", "Storage health check did not complete.");
                LogService.Write("Controllo salute storage fallito.", ex);
            }

            token.ThrowIfCancellationRequested();
            await SetScanPhaseAsync("Finalizzazione e pubblicazione dei risultati…", "Finalizing and publishing results…", token);
            var previousScanUtc = Settings.LastScanUtc;
            Settings.LastScanUtc = DateTime.UtcNow;
            try
            {
                using var phase = ScanDiagnostics.Begin("persist.settings", token);
                JsonStorage.SaveSettings(Settings);
                phase?.Complete();
            }
            catch
            {
                Settings.LastScanUtc = previousScanUtc;
                throw;
            }
            _hasCurrentScan = true;
            OnPropertyChanged(nameof(LastScanLabel));
            OnPropertyChanged(nameof(HomeScanSummary));
            StatusText = warnings.Count > 0 && Updates.Count == 0
                ? T("Scansione incompleta", "Incomplete scan")
                : warnings.Count > 0
                    ? T("Scansione completata con avvisi", "Scan completed with warnings")
                : Updates.Count == 0
                    ? T("Il PC risulta aggiornato", "The PC is up to date")
                    : T($"{Updates.Count} aggiornamenti disponibili", $"{Updates.Count} updates available");
            CurrentItemText = warnings.Count == 0
                ? T(
                    "Scansione completata. I risultati sono pronti per la verifica.",
                    "Scan completed. Results are ready to review.")
                : T(
                    $"Scansione completata con avvisi: {string.Join(" · ", warnings)}",
                    $"Scan completed with warnings: {string.Join(" · ", warnings)}");
            ApplyInitialScanSelection();
            completed = true;
            diagnostics?.Complete(warnings.Count == 0 ? "success" : "partial");
        }
        catch (OperationCanceledException)
        {
            diagnostics?.Complete("cancelled");
            StatusText = T("Scansione annullata", "Scan cancelled");
            CurrentItemText = T("Puoi avviare una nuova scansione.", "You can start a new scan.");
            HardwareCheckStatus = T("Controllo automatico annullato.", "Automatic check cancelled.");
        }
        catch (Exception ex)
        {
            diagnostics?.Complete("error");
            StatusText = T("Scansione non riuscita", "Scan failed");
            CurrentItemText = T("Risultati non finalizzati. Puoi riprovare la scansione.", "Results were not finalized. You can retry the scan.");
            LogService.Write("Finalizzazione della scansione fallita.", ex);
        }
        finally
        {
            _scanCancellation = null;
            _initialSelectionPending = false;
            IsScanRunning = false;
            IsBusy = false;
            NotifyCounts();
            IsProgressIndeterminate = false;
            if (completed) Progress = 100;
            ScanDiagnostics.Count("scan.warnings", warnings.Count);
            ScanDiagnostics.Count("updates.final", Updates.Count);
        }
    }

    public void CancelScan()
    {
        if (!IsScanRunning) return;
        IsScanRunning = false;
        StatusText = T("Annullamento in corso", "Cancelling scan");
        CurrentItemText = T("Annullamento richiesto. Attendo la conclusione dell'operazione in corso…", "Cancellation requested. Waiting for the current operation to return…");
        _scanCancellation?.Cancel();
    }

    public async Task<AppUpdateInfo?> CheckForAppUpdateAsync(bool manual)
    {
        if (IsAppUpdateCheckBusy) return null;
        if (!manual && !Settings.CheckAppUpdatesAutomatically) return null;
        if (!manual && Settings.LastAppUpdateCheckUtc is DateTime previous &&
            DateTime.UtcNow - previous < TimeSpan.FromHours(24))
            return null;

        IsAppUpdateCheckBusy = true;
        AppUpdateStatus = T("Controllo della Release stabile più recente…", "Checking the latest stable release…");
        var checkAttempted = false;
        try
        {
            if (!manual)
                await Task.Delay(1200);
            checkAttempted = true;
            var update = await _appUpdateService.CheckForUpdateAsync(CancellationToken.None);
            if (update is null)
            {
                AppUpdateStatus = T("Update Center è aggiornato.", "Update Center is up to date.");
                return null;
            }

            if (Settings.IgnoredAppVersion.Equals(update.AvailableVersion.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                AppUpdateStatus = T(
                    $"La versione v{update.AvailableVersion} è stata ignorata.",
                    $"Version v{update.AvailableVersion} is ignored.");
                return null;
            }

            AppUpdateStatus = T(
                $"Disponibile Update Center v{update.AvailableVersion}.",
                $"Update Center v{update.AvailableVersion} is available.");
            return update;
        }
        catch (Exception ex)
        {
            LogService.Write("Controllo aggiornamenti dell'app non riuscito.", ex);
            AppUpdateStatus = manual
                ? T("Controllo non riuscito. Verifica la connessione e riprova.", "Check failed. Verify your connection and try again.")
                : T("Controllo automatico non disponibile; l'app continuerà normalmente.", "Automatic check unavailable; the app will continue normally.");
            return null;
        }
        finally
        {
            if (checkAttempted)
            {
                Settings.LastAppUpdateCheckUtc = DateTime.UtcNow;
                JsonStorage.SaveSettings(Settings);
                OnPropertyChanged(nameof(LastAppUpdateCheckLabel));
            }
            IsAppUpdateCheckBusy = false;
        }
    }

    public void IgnoreAppUpdate(SemanticVersion version)
    {
        Settings.IgnoredAppVersion = version.ToString();
        JsonStorage.SaveSettings(Settings);
        AppUpdateStatus = T(
            $"La versione v{version} non verrà più proposta.",
            $"Version v{version} will no longer be offered.");
    }

    public async Task LoadHardwareOverviewAsync(bool force = false)
    {
        if (_hardwareOverviewLoading) return;
        _hardwareOverviewLoading = true;
        try
        {
            if (!_hardwareOverviewLoaded || force)
            {
                HardwareInfo.MonitoringStatus = "Lettura delle caratteristiche hardware…";
                var overview = await _systemHardware.ReadOverviewAsync(CancellationToken.None);
                HardwareInfo.ApplyOverview(overview);
                _hardwareOverviewLoaded = true;
            }
            await RefreshHardwareMetricsAsync();
        }
        catch (Exception ex)
        {
            HardwareInfo.MonitoringStatus = "Alcune caratteristiche non sono state rilevate: " + ex.Message;
            LogService.Write("Lettura panoramica hardware non riuscita.", ex);
        }
        finally
        {
            _hardwareOverviewLoading = false;
        }
    }

    public async Task RefreshHardwareMetricsAsync()
    {
        if (_hardwareMetricsLoading) return;
        _hardwareMetricsLoading = true;
        try
        {
            var metrics = await _systemHardware.ReadMetricsAsync(CancellationToken.None);
            HardwareInfo.ApplyMetrics(metrics);
        }
        catch (Exception ex)
        {
            HardwareInfo.MonitoringStatus = "Monitoraggio temporaneamente non disponibile.";
            LogService.Write("Aggiornamento metriche hardware non riuscito.", ex);
        }
        finally
        {
            _hardwareMetricsLoading = false;
        }
    }

    public Task<UpdateRunStatus?> InstallSelectedAsync() => InstallItemsAsync(SelectedItems);

    internal void ReportPreflightBlocked(PreflightResult preflight)
    {
        StatusText = T("Controlli non superati", "Pre-checks failed");
        CurrentItemText = string.Join(Environment.NewLine, preflight.BlockingIssues);
    }

    public Task<UpdateRunStatus?> InstallItemsAsync(IReadOnlyList<UpdateItem> items) =>
        InstallItemsAsync(items, processRecoveryPrompt: null);

    internal async Task<UpdateRunStatus?> InstallItemsAsync(
        IReadOnlyList<UpdateItem> items,
        IWinGetProcessRecoveryPrompt? processRecoveryPrompt,
        Func<IReadOnlyList<RunnerPlanSnapshot>, bool>? confirmSummary = null,
        Action? showProgress = null)
    {
        var selected = items.Distinct().ToList();
        if (selected.Count == 0 ||
            Interlocked.CompareExchange(ref _updateRunActive, 1, 0) != 0)
            return null;
        if (IsBusy)
        {
            Interlocked.Exchange(ref _updateRunActive, 0);
            return null;
        }

        IsBusy = true;
        try
        {
            _activePauseController = new UpdatePauseController();
            IsUpdatePaused = false;
            IsUpdatePauseRequested = false;
            OnPropertyChanged(nameof(CanPauseUpdates));
            Progress = 0;
            StatusText = T("Aggiornamento in corso", "Update in progress");
            CurrentItemText = T("Conferma la richiesta di Controllo account utente di Windows.", "Confirm the Windows User Account Control prompt if requested.");
            foreach (var item in selected)
            {
                item.Status = T("In attesa", "Waiting");
                item.ResultDetails = T("In attesa dell'installazione.", "Waiting for installation.");
            }

            var result = await _coordinator.RunAsync(selected, Settings, _activePauseController, status =>
                System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyInstallationProgress(selected, status)),
                CancellationToken.None, processRecoveryPrompt, confirmSummary, showProgress);

            ApplyRunResultsSafely(selected, result.Results, isFinal: true);
            SaveRunToHistory(selected, result);
            await RefreshDriversAfterUpdateRunAsync(result.Results);
            Progress = 100;
            StatusText = UpdateCompletionPresentation.From(result.Results).Title;
            CurrentItemText = LocalizationService.Translate(result.Message);
            return result;
        }
        catch (OperationCanceledException ex)
        {
            StatusText = T("Aggiornamento annullato", "Update cancelled");
            CurrentItemText = ex.Message;
            foreach (var item in selected.Where(x =>
                         x.Status.Equals("In attesa", StringComparison.OrdinalIgnoreCase) ||
                         x.Status.Equals("Waiting", StringComparison.OrdinalIgnoreCase)))
            {
                item.Status = T("Da aggiornare", "Update available");
                item.ResultDetails = T("Operazione annullata prima dell'installazione.", "Operation cancelled before installation.");
            }
            return null;
        }
        catch (Exception ex)
        {
            LogService.Write("Aggiornamento selezionato fallito.", ex);
            StatusText = T("Aggiornamento non avviato", "Update not started");
            CurrentItemText = ex.Message;
            foreach (var item in selected.Where(x =>
                         x.Status.Equals("In attesa", StringComparison.OrdinalIgnoreCase) ||
                         x.Status.Equals("Waiting", StringComparison.OrdinalIgnoreCase)))
            {
                item.Status = T("Errore", "Error");
                item.ResultDetails = ex.Message;
            }
            return null;
        }
        finally
        {
            _activePauseController?.Cleanup();
            _activePauseController = null;
            IsUpdatePaused = false;
            IsUpdatePauseRequested = false;
            IsBusy = false;
            Interlocked.Exchange(ref _updateRunActive, 0);
            OnPropertyChanged(nameof(CanPauseUpdates));
            NotifyCounts();
        }
    }

    public async Task EnsureQuickHardwareDataAsync()
    {
        if (_startupInventorySuperseded) return;
        Task<QuickHardwareSnapshot> pending;
        lock (_startupInventoryGate)
        {
            pending = _startupInventoryTask ??= _scans.QuickHardware(CancellationToken.None);
        }
        var snapshot = await pending;
        if (_startupInventorySuperseded || !snapshot.HasAnyData) return;

        if (snapshot.Hardware is not null)
        {
            ClearDriverInventory();
            ApplyHardware(snapshot.Hardware);
            HardwareCheckStatus = T(
                "Inventario locale pronto; avvia la scansione principale per cercare aggiornamenti.",
                "Local inventory ready; start the main scan to search for updates.");
        }

        if (snapshot.Storage is not null)
            ApplyStorage(snapshot.Storage);

        _startupInventorySuperseded = true;
        NotifyCounts();
    }

    public async Task RefreshDriverDiagnosticsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        HardwareCheckStatus = T("Verifica del driver riparato in corso…", "Verifying repaired driver…");
        try
        {
            var scan = await _scans.Hardware(CancellationToken.None);
            _startupInventorySuperseded = true;
            ClearDriverInventory();
            ApplyHardware(scan);
            HardwareCheckStatus = scan.Problems.Count == 0
                ? T("Windows non segnala problemi attivi nei dispositivi.", "Windows reports no active device issues.")
                : T($"Windows segnala ancora {scan.Problems.Count} problemi nei dispositivi.", $"Windows still reports {scan.Problems.Count} device issues.");
        }
        catch (Exception ex)
        {
            HardwareCheckStatus = T($"Verifica dei driver non riuscita: {ex.Message}", $"Driver verification failed: {ex.Message}");
            LogService.Write("Verifica successiva alla riparazione driver non riuscita.", ex);
        }
        finally
        {
            IsBusy = false;
            NotifyCounts();
        }
    }

    private async Task<int> RefreshStorageHealthAsync(
        CancellationToken cancellationToken,
        StorageHealthScanResult? capturedStorage)
    {
        ScanDiagnostics.Count("storage.snapshot-reused", capturedStorage is null ? 0 : 1);
        var storage = capturedStorage ?? await ScanDiagnostics.ObserveAsync("provider.storage-fallback",
            () => _scans.Storage(cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ScanDiagnostics.Count("storage.devices", storage.Devices.Count);
        ScanDiagnostics.Count("storage.volumes", storage.Volumes.Count);
        ApplyStorage(storage);
        return storage.Devices.Count;
    }

    private void ApplyStorage(StorageHealthScanResult storage)
    {
        using var phase = ScanDiagnostics.Begin("apply.storage");
        StorageDevices.Clear();
        StorageVolumes.Clear();
        StorageRows.Clear();
        foreach (var device in storage.Devices) StorageDevices.Add(device);
        foreach (var volume in storage.Volumes) StorageVolumes.Add(volume);
        foreach (var row in StorageTableRowFactory.CreateRows(storage.Devices)) StorageRows.Add(row);
        StorageHealthStatus = storage.Status;
        NotifyCounts();
        phase?.Complete();
    }

    public void ToggleUpdatePause()
    {
        if (_activePauseController is not { } pause || !CanPauseUpdates) return;
        if (pause.IsPauseRequested)
        {
            pause.Resume();
            IsUpdatePauseRequested = false;
        }
        else
        {
            pause.RequestPause();
            IsUpdatePauseRequested = true;
        }
    }

    internal void ApplyInstallationProgress(IReadOnlyList<UpdateItem> selected, UpdateRunStatus status)
    {
        IsUpdatePaused = status.State.Equals("Paused", StringComparison.OrdinalIgnoreCase);
        IsUpdatePauseRequested = _activePauseController?.IsPauseRequested == true;
        var completedPercentage = status.Total > 0 ? status.CurrentIndex * 100d / status.Total : 0;
        Progress = Math.Max(Progress, Math.Min(99, completedPercentage));
        CurrentItemText = LocalizationService.Translate(status.Message);
        StatusText = status.State == "AwaitingConsent"
            ? T("In attesa della conferma finale", "Waiting for final confirmation")
            : IsUpdatePaused
                ? T("Aggiornamenti in pausa", "Updates paused")
                : string.IsNullOrWhiteSpace(status.CurrentName)
                    ? T("Aggiornamento in corso", "Update in progress")
                    : T($"Aggiornamento: {status.CurrentName}", $"Updating: {status.CurrentName}");

        ApplyRunResultsSafely(selected, status.Results);
        InstallationProgressChanged?.Invoke(status);
    }

    public void SetAllSelected(bool selected)
    {
        if (_initialSelectionPending) _scanSelectionOverridden = true;
        foreach (var item in Updates)
            // Bulk selection does not grant risk consent, but must preserve an
            // individual choice. Deselect All still clears every choice.
            item.IsSelected = selected && item.CanInstall &&
                              (!item.RequiresRiskConfirmation || item.IsSelected);
        NotifyCounts();
    }

    public void SelectAllWithConfirmation(Func<UpdateItem, bool> confirm)
    {
        // Capture this click's candidates. Results arriving during a dialog are not part of this choice.
        var candidates = Updates.Where(item => item.CanInstall && item.RequiresRiskConfirmation && !item.IsSelected).ToArray();
        SetAllSelected(true);
        foreach (var item in candidates)
        {
            if (!Updates.Contains(item) || !item.CanInstall || item.IsSelected) continue;
            var target = ElevatedUpdateRunner.ToPlanItem(item);
            var accepted = confirm(new RunnerPlanItem(target).ToUpdateItem());
            if (accepted && Updates.Contains(item) && item.CanInstall && item.RequiresRiskConfirmation &&
                RunnerPlanItem.Matches(item, target))
                item.IsSelected = true;
        }
        NotifyCounts();
    }

    internal void ApplyRunResults(
        IReadOnlyList<UpdateItem> selected, IReadOnlyList<ItemRunResult> results, bool isFinal = false)
    {
        foreach (var runResult in results)
        {
            var item = selected.FirstOrDefault(x =>
                x.Id.Equals(runResult.Id, StringComparison.OrdinalIgnoreCase) &&
                x.Kind.ToString().Equals(runResult.Kind, StringComparison.OrdinalIgnoreCase));
            if (item is null) continue;
            // A newly confirmed target must not change the old proposal's status or eligibility.
            if (runResult.ReportedTarget is { } target && !RunnerPlanItem.Matches(item, target)) continue;

            item.Status = runResult.Outcome switch
            {
                UpdateOutcomes.Cancelled => T("Annullato", "Cancelled"),
                UpdateOutcomes.NotApplicable => T("Non applicabile", "Not applicable"),
                UpdateOutcomes.ManualRequired when runResult.Phase == "winget-preflight" => T("Non eseguibile automaticamente", "Cannot run automatically"),
                UpdateOutcomes.ManualRequired => T("Aggiornamento manuale", "Manual update"),
                _ => !runResult.InstallerSucceeded
                    ? T("Non riuscito", "Failed")
                    : !runResult.Success
                        ? T("Errore di verifica", "Verification failed")
                        : runResult.Verified
                            ? T("Aggiornato", "Updated")
                            : T("Completato · da verificare", "Completed · verify")
            };
            item.ResultDetails = LocalizationService.Translate(runResult.Message);
            item.Diagnostics = runResult.Diagnostics;
            if (runResult.Outcome.Equals(UpdateOutcomes.ManualRequired, StringComparison.Ordinal) ||
                runResult.Outcome.Equals(UpdateOutcomes.NotApplicable, StringComparison.Ordinal) ||
                runResult.FailureReason is UpdateFailureReasons.NotApplicable or
                    UpdateFailureReasons.InstallerTechnologyMismatch or UpdateFailureReasons.UpgradeNotSupported or UpdateFailureReasons.ElevationRequired)
                item.CanInstall = false;

            if (ShouldRemoveCompletedUpdate(runResult) && Updates.Remove(item))
            {
                item.PropertyChanged -= UpdateItemOnPropertyChanged;
            }
        }

        if (isFinal)
        {
            foreach (var item in selected.Where(item => !results.Any(result =>
                item.Id.Equals(result.Id, StringComparison.OrdinalIgnoreCase) &&
                item.Kind.ToString().Equals(result.Kind, StringComparison.OrdinalIgnoreCase) &&
                (result.ReportedTarget is null || RunnerPlanItem.Matches(item, result.ReportedTarget)))))
            {
                item.Status = T("Da aggiornare", "Update available");
                item.Progress = 0;
                item.ResultDetails = T("Nessun risultato per questa selezione preliminare. Esegui una nuova scansione.",
                    "No result for this preliminary selection. Run a new scan.");
            }
        }

        // ObservableCollection aggiorna già la CollectionView. Un Refresh esplicito
        // qui può essere illegale durante una transazione WPF AddNew/EditItem.
        NotifyCounts();
    }

    internal static string GetHistoryResultLabel(ItemRunResult runResult) =>
        runResult.Outcome switch
        {
            UpdateOutcomes.Cancelled => LocalizationService.Text("Annullato", "Cancelled"),
            UpdateOutcomes.NotApplicable => LocalizationService.Text("Non applicabile", "Not applicable"),
            UpdateOutcomes.ManualRequired when runResult.Phase == "winget-preflight" => LocalizationService.Text("Non eseguibile automaticamente", "Cannot run automatically"),
            UpdateOutcomes.ManualRequired => LocalizationService.Text("Manuale", "Manual"),
            _ when runResult.Success && !runResult.Verified => runResult.RestartRequired || runResult.VerificationStatus == UpdateVerificationStatuses.PendingRestart
                ? LocalizationService.Text("Completato · riavvio richiesto", "Completed · restart required")
                : LocalizationService.Text("Completato · da verificare", "Completed · verify"),
            _ => (runResult.TerminalDisposition == ItemTerminalDisposition.Succeeded || (runResult.TerminalDisposition == ItemTerminalDisposition.None && runResult.Success))
                ? (runResult.Verified ? LocalizationService.Text("Riuscito", "Succeeded") : LocalizationService.Text("Completato · da verificare", "Completed · verify"))
                : LocalizationService.Text("Non riuscito", "Failed")
        };

    internal static bool ShouldRemoveCompletedUpdate(ItemRunResult result) =>
        (result.TerminalDisposition == ItemTerminalDisposition.Succeeded || (result.TerminalDisposition == ItemTerminalDisposition.None && result.Success)) &&
        result.Verified &&
        !result.Outcome.Equals(UpdateOutcomes.ManualRequired, StringComparison.Ordinal) &&
        !result.Outcome.Equals(UpdateOutcomes.NotApplicable, StringComparison.Ordinal);

    private void ApplyRunResultsSafely(
        IReadOnlyList<UpdateItem> selected, IReadOnlyList<ItemRunResult> results, bool isFinal = false)
    {
        try
        {
            ApplyRunResults(selected, results, isFinal);
        }
        catch (Exception ex)
        {
            LogService.WriteEvent(
                "ui",
                "apply-run-results",
                "ui-error",
                resultCode: ex.HResult,
                details: "L'esito reale dell'installer è conservato; è fallita solo la proiezione UI.",
                exception: ex);
        }
    }

    private async Task RefreshDriversAfterUpdateRunAsync(IReadOnlyList<ItemRunResult> results)
    {
        if (!results.Any(x =>
                x.Kind.Equals(nameof(UpdateKind.Driver), StringComparison.OrdinalIgnoreCase) &&
                x.InstallerSucceeded))
            return;

        HardwareCheckStatus = T(
            "Ricaricamento dello stato reale dei driver…",
            "Reloading the actual driver state…");

        try
        {
            var hardware = await _scans.Hardware(CancellationToken.None);
            _startupInventorySuperseded = true;
            ClearDriverInventory();
            ApplyHardware(hardware);

            var refreshedUpdates = new List<UpdateItem>();
            var warnings = new List<string>();
            try
            {
                var microsoft = await _scans.Drivers(
                    CancellationToken.None, hardware.Drivers);
                refreshedUpdates.AddRange(microsoft.Updates);
                warnings.AddRange(microsoft.SourceWarnings);
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
                LogService.WriteEvent(
                    "driver-refresh", "windows-update", "warning",
                    resultCode: ex.HResult, details: ex.Message, exception: ex);
            }

            try
            {
                var official = await _scans.Catalog(
                    hardware.Drivers, CancellationToken.None);
                refreshedUpdates.AddRange(official.Updates);
                warnings.AddRange(official.Warnings);
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
                LogService.WriteEvent(
                    "driver-refresh", "official-catalog", "warning",
                    resultCode: ex.HResult, details: ex.Message, exception: ex);
            }

            // Sostituisce completamente le offerte soltanto quando tutte le fonti
            // hanno risposto. In caso contrario conserva le righe fallite/non
            // verificate e aggiunge i risultati nuovi senza duplicati.
            if (warnings.Count == 0)
                ClearDriverUpdates();
            AddUpdates(refreshedUpdates);

            var current = Updates.Count(x => x.Kind == UpdateKind.Driver);
            HardwareCheckStatus = warnings.Count == 0
                ? current == 0
                    ? T("Nessun aggiornamento driver ancora applicabile.",
                        "No driver update is still applicable.")
                    : T(
                        $"{current} aggiornamenti driver risultano ancora applicabili dopo la verifica.",
                        $"{current} driver updates are still applicable after verification.")
                : T(
                    "Elenco driver aggiornato; alcune sorgenti non hanno completato la verifica.",
                    "Driver list refreshed; some sources did not complete verification.");
        }
        catch (Exception ex)
        {
            HardwareCheckStatus = T(
                "Installazione terminata, ma il nuovo inventario driver non è disponibile.",
                "Installation finished, but the refreshed driver inventory is unavailable.");
            LogService.WriteEvent(
                "driver-refresh", "inventory", "failure",
                resultCode: ex.HResult, details: ex.Message, exception: ex);
        }
    }

    private void ClearDriverUpdates()
    {
        foreach (var item in Updates.Where(x => x.Kind == UpdateKind.Driver).ToList())
        {
            item.PropertyChanged -= UpdateItemOnPropertyChanged;
            Updates.Remove(item);
        }
        NotifyCounts();
    }

    public void NotifyCounts()
    {
        OnPropertyChanged(nameof(ManagedUpdatesHeader));
        OnPropertyChanged(nameof(BlockedUpdatesHeader));
        OnPropertyChanged(nameof(AvailableCount));
        OnPropertyChanged(nameof(UpdateAvailabilityLabel));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountLabel));
        OnPropertyChanged(nameof(VisibleUpdateCount));
        OnPropertyChanged(nameof(VisibleUpdateCountLabel));
        OnPropertyChanged(nameof(SoftwareUpdateCount));
        OnPropertyChanged(nameof(RuntimeUpdateCount));
        OnPropertyChanged(nameof(DriverCount));
        OnPropertyChanged(nameof(DriverProblemCount));
        OnPropertyChanged(nameof(MissingGameDependencyCount));
        OnPropertyChanged(nameof(OptionalGameDependencyCount));
        OnPropertyChanged(nameof(StorageDeviceCount));
        OnPropertyChanged(nameof(StorageVolumeCount));
        OnPropertyChanged(nameof(VisibleDriverCount));
        OnPropertyChanged(nameof(ChipsetCount));
        OnPropertyChanged(nameof(DriverUpdateCount));
        OnPropertyChanged(nameof(VendorCheckCount));
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(HomeScanSummary));
        OnPropertyChanged(nameof(HistoryCountLabel));
    }

    public string HistoryCountLabel => LocalizationService.Text(
        $"{History.Count} operazioni registrate",
        $"{History.Count} operations recorded");

    public void SaveSettings()
    {
        Settings.LanguageMode = LocalizationService.Normalize(Settings.LanguageMode);
        Settings.AutomaticScanInterval = AppSettings.NormalizeAutomaticScanInterval(Settings.AutomaticScanInterval);
        JsonStorage.SaveSettings(Settings);
    }

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(ManagedUpdatesHeader));
        OnPropertyChanged(nameof(BlockedUpdatesHeader));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(UpdateSelectionHint));
        OnPropertyChanged(nameof(SelectAllHint));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CurrentItemText));
        OnPropertyChanged(nameof(AppUpdateStatus));
        foreach (var item in Updates) item.RefreshLocalizedProperties();
        foreach (var entry in History) entry.NotifyLanguageChanged();
        HardwareInfo.NotifyLanguageChanged();
        foreach (var driver in DriverInventory) driver.NotifyLanguageChanged();
        foreach (var problem in DriverProblems) problem.NotifyLanguageChanged();
        foreach (var tool in VendorTools) tool.NotifyLanguageChanged();
        if (StorageDevices.Count > 0)
        {
            StorageRows.Clear();
            foreach (var row in StorageTableRowFactory.CreateRows(StorageDevices)) StorageRows.Add(row);
        }
        Network.NotifyLanguageChanged();
        OnPropertyChanged(nameof(LastScanLabel));
        OnPropertyChanged(nameof(HomeScanSummary));
        OnPropertyChanged(nameof(LastAppUpdateCheckLabel));
        OnPropertyChanged(nameof(AppUpdateStatus));
        OnPropertyChanged(nameof(HardwareCheckStatus));
        OnPropertyChanged(nameof(GameDependencyStatus));
        OnPropertyChanged(nameof(StorageHealthStatus));
        OnPropertyChanged(nameof(HistoryCountLabel));
        OnPropertyChanged(nameof(SelectedCountLabel));
        OnPropertyChanged(nameof(VisibleUpdateCountLabel));
        NotifyCounts();
    }

    public void ClearHistory()
    {
        History.Clear();
        JsonStorage.SaveHistory(History);
    }

    private async Task SetScanPhaseAsync(string italian, string english, CancellationToken token)
    {
        CurrentItemText = T(italian, english);
        // Render even cached phases and service queued input before continuing.
        await Dispatcher.Yield(DispatcherPriority.Background);
        token.ThrowIfCancellationRequested();
    }

    private void ApplyInitialScanSelection()
    {
        if (!_initialSelectionPending) return;
        _applyingInitialSelection = true;
        try
        {
            foreach (var item in Updates)
            {
                if (!item.CanInstall) item.IsSelected = false;
                else if (!_scanSelectionOverridden && !_scanSelectionTouched.Contains(item))
                    item.IsSelected = !item.RequiresRiskConfirmation;
            }
        }
        finally { _initialSelectionPending = false; _applyingInitialSelection = false; }
        ScanDiagnostics.Count("updates.final-selected", SelectedCount);
        NotifyCounts();
    }

    private async Task AddScanUpdatesAsync(IEnumerable<UpdateItem> updates, CancellationToken token)
    {
        await SetScanPhaseAsync("Aggregazione e pubblicazione degli aggiornamenti…", "Aggregating and publishing updates…", token);
        AddUpdates(updates);
    }

    private void AddUpdates(IEnumerable<UpdateItem> updates)
    {
        using var phase = ScanDiagnostics.Begin("aggregation.updates");
        // Rebuild from the actual collection for each batch: scans, removals and
        // incremental driver refreshes must not retain an identity cache.
        var idsByKind = new Dictionary<UpdateKind, HashSet<string>>();
        bool Remember(UpdateItem item)
        {
            if (!idsByKind.TryGetValue(item.Kind, out var ids))
                idsByKind.Add(item.Kind, ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return ids.Add(item.Id);
        }
        var useIndex = true;
        foreach (var existing in Updates)
        {
            if (existing is null || existing.Id is null)
            {
                useIndex = false;
                break;
            }
            Remember(existing);
        }
        foreach (var item in updates)
        {
            ScanDiagnostics.Count("aggregation.candidates", 1);
            // Null IDs have asymmetric historical behavior (including Any's
            // short-circuit and null-reference failure). Keep that exact path.
            if (item.Id is null) useIndex = false;
            if (useIndex ? !Remember(item) : Updates.Any(existing =>
                    existing.Kind == item.Kind &&
                    existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)))
            {
                ScanDiagnostics.Count("aggregation.duplicates", 1);
                continue;
            }
            // Capture provider decisions before publishing the item to the UI.
            // Count only new rows: diagnostics sessions accumulate across batches.
            ScanDiagnostics.Count("updates.initial-selected", item.CanInstall && item.IsSelected ? 1 : 0);
            ScanDiagnostics.Count("updates.bulk-eligible", item.CanInstall && !item.RequiresRiskConfirmation ? 1 : 0);
            ScanDiagnostics.Count("updates.individual-risk", item.CanInstall && item.RequiresRiskConfirmation ? 1 : 0);
            ScanDiagnostics.Count("updates.blocked", item.CanInstall ? 0 : 1);
            ScanDiagnostics.Count("updates.unknown-metadata", item.HasUnverifiedInstallerMetadata ? 1 : 0);
            // Publish no automatic choices while results are still arriving.
            if (_hasStartedScan) item.IsSelected = false;
            _observedSelections[item] = item.IsSelected;
            item.PropertyChanged += UpdateItemOnPropertyChanged;
            Updates.Add(item);
        }
        RefreshUpdatesView();
        NotifyCounts();
        phase?.Complete();
    }

    private void ClearUpdates()
    {
        foreach (var item in Updates) item.PropertyChanged -= UpdateItemOnPropertyChanged;
        Updates.Clear();
        _observedSelections.Clear();
        NotifyCounts();
    }

    private void ApplyHardware(HardwareScanResult scan)
    {
        using var phase = ScanDiagnostics.Begin("apply.hardware");
        CpuName = scan.CpuName;
        ComputerName = string.Join(" ", new[] { scan.ComputerManufacturer, scan.ComputerModel }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        foreach (var driver in scan.Drivers) DriverInventory.Add(driver);
        foreach (var problem in scan.Problems)
        {
            var installed = scan.Drivers.FirstOrDefault(x =>
                x.DeviceId.Equals(problem.DeviceId, StringComparison.OrdinalIgnoreCase));
            problem.InstalledInfName = installed?.InfName ?? "";
            problem.InstalledDriverSigned = installed?.IsSigned == true;
            DriverProblems.Add(problem);
        }
        foreach (var tool in scan.VendorTools) VendorTools.Add(tool);
        RefreshDriverInventoryView();
        NotifyCounts();
        phase?.Complete();
    }

    private void ClearDriverInventory()
    {
        DriverInventory.Clear();
        DriverProblems.Clear();
        VendorTools.Clear();
    }

    private void ClearHardware()
    {
        ClearDriverInventory();
        GameDependencies.Clear();
        StorageDevices.Clear();
        StorageVolumes.Clear();
        StorageRows.Clear();
        DriverSearchText = "";
        DriverInventoryFilter = "Tutti";
        CpuName = "Processore non ancora rilevato";
        ComputerName = "";
        HardwareCheckStatus = "Esegui una scansione per controllare automaticamente i driver.";
        GameDependencyStatus = "Esegui una scansione per controllare i runtime condivisi.";
        StorageHealthStatus = "Esegui una scansione per controllare la salute dello storage.";
        NotifyCounts();
    }

    private void UpdateItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateItem.IsSelected))
        {
            if (sender is UpdateItem item)
            {
                if (_initialSelectionPending && !_applyingInitialSelection &&
                    _observedSelections.TryGetValue(item, out var previous) && previous != item.IsSelected)
                    _scanSelectionTouched.Add(item);
                _observedSelections[item] = item.IsSelected;
            }
            NotifyCounts();
        }
        else if (e.PropertyName is nameof(UpdateItem.CanInstall) or nameof(UpdateItem.RequiresRiskConfirmation))
        {
            RefreshUpdatesView();
            NotifyCounts();
        }
        else if (e.PropertyName == nameof(UpdateItem.Status) && (UpdateTypeFilter == "Errori" || !string.IsNullOrWhiteSpace(SearchText)))
        {
            RefreshUpdatesView();
        }
    }

    private bool FilterUpdate(object value)
    {
        if (value is not UpdateItem item) return false;
        var typeMatches = UpdateTypeFilter switch
        {
            "Software" => item.Kind == UpdateKind.Software,
            "Driver" => item.Kind == UpdateKind.Driver,
            "Runtime" => item.Kind == UpdateKind.Runtime,
            "Errori" => item.Status.Equals("Errore", StringComparison.OrdinalIgnoreCase) ||
                        item.Status.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
                        item.Status.Equals("Non riuscito", StringComparison.OrdinalIgnoreCase) ||
                        item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        var searchMatches = string.IsNullOrWhiteSpace(SearchText) ||
                            item.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                            item.Publisher.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.InstalledVersion.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.AvailableVersion.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.Source.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.PriorityLabel.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.Status.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);
        return typeMatches && searchMatches;
    }

    private void RefreshUpdatesView()
    {
        TryRefreshCollectionView(UpdatesView, "updates");
        TryRefreshCollectionView(ManagedUpdatesView, "managed-updates");
        TryRefreshCollectionView(BlockedUpdatesView, "blocked-updates");
        OnPropertyChanged(nameof(ManagedUpdatesHeader));
        OnPropertyChanged(nameof(BlockedUpdatesHeader));
        if (!string.IsNullOrWhiteSpace(SearchText) && ManagedUpdatesView.IsEmpty && !BlockedUpdatesView.IsEmpty &&
            _revealedBlockedQuery != SearchText)
        {
            _revealedBlockedQuery = SearchText;
            IsBlockedUpdatesExpanded = true;
        }
        OnPropertyChanged(nameof(VisibleUpdateCount));
        OnPropertyChanged(nameof(VisibleUpdateCountLabel));
    }

    private bool FilterDriverInventory(object value)
    {
        if (value is not DriverInventoryItem item) return false;
        var category = item.CategoryLabel;
        var typeMatches = DriverInventoryFilter switch
        {
            "Con aggiornamenti" => item.HasUpdate,
            "CPU e chipset" => item.IsProcessorOrChipset,
            "Grafica" => category.Contains("display", StringComparison.OrdinalIgnoreCase) ||
                         item.DeviceName.Contains("graphics", StringComparison.OrdinalIgnoreCase) ||
                         item.DeviceName.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
                         item.DeviceName.Contains("nvidia", StringComparison.OrdinalIgnoreCase),
            "Audio" => category.Contains("media", StringComparison.OrdinalIgnoreCase) ||
                       item.DeviceName.Contains("audio", StringComparison.OrdinalIgnoreCase),
            "Rete" => category.Contains("net", StringComparison.OrdinalIgnoreCase) ||
                      item.DeviceName.Contains("ethernet", StringComparison.OrdinalIgnoreCase) ||
                      item.DeviceName.Contains("wi-fi", StringComparison.OrdinalIgnoreCase) ||
                      item.DeviceName.Contains("wireless", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        var searchMatches = string.IsNullOrWhiteSpace(DriverSearchText) ||
                            item.DisplayName.Contains(DriverSearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.CategoryLabel.Contains(DriverSearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.Manufacturer.Contains(DriverSearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.Provider.Contains(DriverSearchText, StringComparison.CurrentCultureIgnoreCase) ||
                            item.InstalledVersion.Contains(DriverSearchText, StringComparison.OrdinalIgnoreCase) ||
                            item.DeviceId.Contains(DriverSearchText, StringComparison.OrdinalIgnoreCase);
        return typeMatches && searchMatches;
    }

    private void RefreshDriverInventoryView()
    {
        TryRefreshCollectionView(DriverInventoryView, "driver-inventory");
        OnPropertyChanged(nameof(VisibleDriverCount));
    }

    internal static bool TryRefreshCollectionView(ICollectionView view, string viewName)
    {
        if (view is IEditableCollectionView editable &&
            (editable.IsAddingNew || editable.IsEditingItem))
        {
            LogService.WriteEvent(
                "ui", "collection-refresh", "skipped",
                details: $"{viewName}: WPF AddNew/EditItem attivo.");
            return false;
        }

        try
        {
            view.Refresh();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            LogService.WriteEvent(
                "ui", "collection-refresh", "ui-error",
                resultCode: ex.HResult,
                details: $"{viewName}: refresh non eseguito senza alterare l'esito dell'aggiornamento.",
                exception: ex);
            return false;
        }
    }

    private void SaveRunToHistory(IReadOnlyList<UpdateItem> selected, UpdateRunStatus result)
    {
        foreach (var runResult in result.Results)
        {
            var item = runResult.ReportedTarget is { } target
                ? new RunnerPlanItem(target).ToUpdateItem()
                : selected.FirstOrDefault(x => x.Id == runResult.Id);
            var entry = new HistoryEntry
            {
                Timestamp = DateTime.UtcNow,
                Name = runResult.Name,
                Kind = runResult.Kind,
                FromVersion = item?.InstalledVersion ?? "",
                ToVersion = item?.AvailableVersion ?? "",
                Result = GetHistoryResultLabel(runResult),
                Details = BuildReadableHistoryDetails(item, runResult),
                Diagnostics = runResult.Diagnostics,
                PendingVerificationPlan = runResult.ReportedTarget is not null && runResult.InstallerSucceeded && !runResult.Verified &&
                    runResult.VerificationStatus is UpdateVerificationStatuses.Unavailable or UpdateVerificationStatuses.PendingRestart &&
                    item is not null && item.Kind != UpdateKind.Driver && WinGetScopeGuard.IsOfficialSource(item.Source)
                        ? ElevatedUpdateRunner.ToPlanItem(item) : null
            };
            History.Insert(0, entry);
        }
        JsonStorage.SaveHistory(History);
    }

    private static string BuildReadableHistoryDetails(UpdateItem? item, ItemRunResult runResult)
    {
        var fromVersion = string.IsNullOrWhiteSpace(item?.InstalledVersion)
            ? LocalizationService.Text("versione precedente non rilevata", "previous version not detected")
            : item.InstalledVersion;
        var toVersion = string.IsNullOrWhiteSpace(item?.AvailableVersion)
            ? LocalizationService.Text("versione più recente disponibile", "latest available version")
            : item.AvailableVersion;
        var source = string.IsNullOrWhiteSpace(item?.Source)
            ? LocalizationService.Text("fonte di aggiornamento configurata", "configured update source")
            : item.Source;
        var technicalDetail = string.IsNullOrWhiteSpace(runResult.Message)
            ? LocalizationService.Text("Nessun dettaglio tecnico aggiuntivo.", "No additional technical details.")
            : LocalizationService.TranslateHistoryDetails(runResult.Message.Trim());

        if (runResult.Outcome.Equals(UpdateOutcomes.Cancelled, StringComparison.Ordinal))
            return technicalDetail;
        if (runResult.Phase == "winget-preflight" && runResult.Outcome == UpdateOutcomes.ManualRequired)
            return $"{runResult.Name}: {technicalDetail}";
        if (runResult.Outcome == UpdateOutcomes.Failed && runResult.FailureReason is
            UpdateFailureReasons.NotApplicable or UpdateFailureReasons.InstallerTechnologyMismatch or UpdateFailureReasons.UpgradeNotSupported or UpdateFailureReasons.ElevationRequired)
            return $"{runResult.Name} ({fromVersion} → {toVersion}): {technicalDetail}";
        if (runResult.Outcome.Equals(UpdateOutcomes.NotApplicable, StringComparison.Ordinal))
            return LocalizationService.IsEnglish
                ? $"{runResult.Name} is not applicable to this PC according to WinGet. " +
                  $"The report from {fromVersion} to {toVersion} will remain excluded until one of the two versions changes. " +
                  $"Detail: {technicalDetail}"
                : $"{runResult.Name} non è applicabile a questo PC secondo WinGet. " +
                  $"La segnalazione da {fromVersion} a {toVersion} resterà esclusa finché una delle due versioni non cambia. " +
                  $"Dettaglio: {technicalDetail}";

        if (runResult.Outcome.Equals(UpdateOutcomes.ManualRequired, StringComparison.Ordinal))
            return LocalizationService.IsEnglish
                ? $"{runResult.Name} requires a manual update because the installed package and the new one " +
                  $"do not support a compatible automatic upgrade. Detail: {technicalDetail}"
                : $"{runResult.Name} richiede un aggiornamento manuale perché il pacchetto installato e quello nuovo " +
                  $"non supportano un upgrade automatico compatibile. Dettaglio: {technicalDetail}";

        if (runResult.InstallerSucceeded)
        {
            var restart = runResult.RestartRequired
                ? LocalizationService.Text(" Per completare l'operazione è richiesto il riavvio di Windows.", " A Windows restart is required to complete the operation.")
                : LocalizationService.Text(" Non è richiesto alcun riavvio.", " No restart is required.");
            if (runResult.Verified)
                return LocalizationService.IsEnglish
                    ? $"{runResult.Name} was updated and verified from {fromVersion} to {toVersion} using {source}.{restart} Technical detail: {technicalDetail}"
                    : $"{runResult.Name} è stato aggiornato e verificato da {fromVersion} a {toVersion} usando {source}.{restart} Dettaglio tecnico: {technicalDetail}";

            var verification = runResult.VerificationStatus switch
            {
                UpdateVerificationStatuses.PendingRestart => LocalizationService.Text("La verifica finale richiede il riavvio.", "Final verification requires a restart."),
                UpdateVerificationStatuses.Failed => LocalizationService.Text("L'installer è terminato, ma la verifica finale non ha confermato l'aggiornamento.", "The installer finished, but final verification did not confirm the update."),
                _ => LocalizationService.Text("L'installer è terminato, ma la verifica finale non è disponibile.", "The installer finished, but final verification is not available.")
            };
            return LocalizationService.IsEnglish
                ? $"{runResult.Name}: {verification}{restart} Technical detail: {technicalDetail}"
                : $"{runResult.Name}: {verification}{restart} Dettaglio tecnico: {technicalDetail}";
        }

        return LocalizationService.IsEnglish
            ? $"Update of {runResult.Name} from {fromVersion} to {toVersion} failed. " +
              $"The item remains available for a retry. Reason: {technicalDetail}"
            : $"L'aggiornamento di {runResult.Name} da {fromVersion} a {toVersion} non è riuscito. " +
              $"L'elemento resta disponibile per un nuovo tentativo. Motivo: {technicalDetail}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private static string T(string italian, string english) => LocalizationService.Text(italian, english);
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
