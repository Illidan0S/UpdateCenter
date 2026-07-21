using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly WinGetService _winGet = new();
    private readonly WindowsUpdateService _windowsUpdate = new();
    private readonly OfficialDriverCatalogService _officialDriverCatalog = new();
    private readonly HardwareInventoryService _hardwareInventory = new();
    private readonly SystemHardwareService _systemHardware = new();
    private readonly UpdateCoordinator _coordinator = new();
    private readonly AppUpdateService _appUpdateService = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _isBusy;
    private bool _isScanRunning;
    private double _progress;
    private string _statusText = LocalizationService.Text("Pronto per la scansione", "Ready to scan");
    private string _currentItemText = LocalizationService.Text("Premi Avvia scansione per iniziare.", "Select Start scan to begin.");
    private string _searchText = "";
    private string _filter = "Tutti";
    private string _driverSearchText = "";
    private string _driverInventoryFilter = "Tutti";
    private int _scannedCount;
    private string _cpuName = "Processore non ancora rilevato";
    private string _computerName = "";
    private string _hardwareCheckStatus = "Esegui una scansione per controllare automaticamente i driver.";
    private bool _hasCurrentScan;
    private bool _hardwareOverviewLoaded;
    private bool _hardwareOverviewLoading;
    private bool _hardwareMetricsLoading;
    private bool _isAppUpdateCheckBusy;
    private string _appUpdateStatus = LocalizationService.Text(
        "Controllo aggiornamenti dell'app non ancora eseguito.",
        "The app update check has not run yet.");

    public MainViewModel()
    {
        AppPaths.EnsureCreated();
        Settings = JsonStorage.LoadSettings();
        if (Settings.LastAppUpdateCheckUtc is DateTime lastUpdateCheck)
            _appUpdateStatus = LocalizationService.IsEnglish
                ? $"Last check: {lastUpdateCheck.ToLocalTime():MM/dd/yyyy HH:mm}."
                : $"Ultimo controllo: {lastUpdateCheck.ToLocalTime():dd/MM/yyyy HH:mm}.";
        foreach (var entry in JsonStorage.LoadHistory()) History.Add(entry);
        UpdatesView = CollectionViewSource.GetDefaultView(Updates);
        UpdatesView.Filter = FilterUpdate;
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
    public ObservableCollection<VendorSupportItem> VendorTools { get; } = [];
    public SystemHardwareInfo HardwareInfo { get; } = new();
    public ICollectionView UpdatesView { get; }
    public ICollectionView DriverInventoryView { get; }
    public AppSettings Settings { get; }
    public AppUpdateService AppUpdateService => _appUpdateService;

    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanScan)); OnPropertyChanged(nameof(CanUpdate)); }
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
        private set { _progress = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string CurrentItemText
    {
        get => _currentItemText;
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
    public int SoftwareUpdateCount => Updates.Count(x => x.Kind == UpdateKind.Software);
    public int DriverCount => DriverInventory.Count;
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
                ? $"{SoftwareUpdateCount} software and {DriverUpdateCount} driver updates to review."
                : $"{SoftwareUpdateCount} software e {DriverUpdateCount} driver da controllare.";
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
    public string AppUpdateStatus
    {
        get => _appUpdateStatus;
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
        get => _hardwareCheckStatus;
        private set { _hardwareCheckStatus = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value ?? ""; OnPropertyChanged(); RefreshUpdatesView(); }
    }

    public string Filter
    {
        get => _filter;
        set { _filter = value ?? "Tutti"; OnPropertyChanged(); RefreshUpdatesView(); }
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

    public async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Progress = 2;
        ScannedCount = 0;
        _hasCurrentScan = false;
        OnPropertyChanged(nameof(HomeScanSummary));
        StatusText = T("Scansione in corso", "Scan in progress");
        CurrentItemText = T("Preparazione dei servizi di aggiornamento…", "Preparing update services…");
        ClearUpdates();
        ClearHardware();
        HardwareCheckStatus = T("Controllo automatico dei driver in corso…", "Automatic driver check in progress…");
        _scanCancellation = new CancellationTokenSource();
        IsScanRunning = true;
        var warnings = new List<string>();
        HardwareScanResult? hardwareScan = null;
        var driverSources = new List<string>();
        var microsoftDriverCount = 0;
        var catalogDriverCount = 0;

        try
        {
            Progress = 12;
            CurrentItemText = T("Ricerca dei software aggiornabili con WinGet…", "Searching for software updates with WinGet…");
            try
            {
                var software = await _winGet.ScanAsync(Settings.IncludeUnknownVersions, _scanCancellation.Token);
                AddUpdates(software);
                ScannedCount += software.Count;
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
                LogService.Write("Scansione software fallita.", ex);
            }

            Progress = 38;
            CurrentItemText = T("Inventario di processore, chipset e driver installati…", "Inventorying processor, chipset and installed drivers…");
            try
            {
                hardwareScan = await _hardwareInventory.ScanAsync(_scanCancellation.Token);
                ScannedCount += hardwareScan.Drivers.Count;
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
                LogService.Write("Inventario hardware fallito.", ex);
            }

            Progress = 68;
            CurrentItemText = T("Ricerca dei driver proposti da Windows Update…", "Searching for drivers offered by Windows Update…");
            try
            {
                var driverScan = await _windowsUpdate.ScanDriversAsync(
                    _scanCancellation.Token, hardwareScan?.Drivers);
                AddUpdates(driverScan.Updates);
                ScannedCount += driverScan.Updates.Count;
                microsoftDriverCount = driverScan.Updates.Count;
                driverSources.AddRange(driverScan.SourcesChecked);
                if (driverScan.SourceWarnings.Count > 0)
                    warnings.Add("Alcune sorgenti driver Microsoft non erano disponibili.");
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
                LogService.Write("Scansione driver fallita.", ex);
                HardwareCheckStatus = T("Controllo automatico dei driver non completato.", "The automatic driver check did not complete.");
            }

            if (hardwareScan is not null)
            {
                Progress = 84;
                CurrentItemText = T("Confronto con il catalogo verificato dei produttori…", "Comparing with the verified manufacturer catalog…");
                try
                {
                    var catalogScan = await _officialDriverCatalog.ScanAsync(
                        hardwareScan.Drivers, _scanCancellation.Token);
                    AddUpdates(catalogScan.Updates);
                    ScannedCount += catalogScan.Updates.Count;
                    catalogDriverCount = catalogScan.Updates.Count;
                    driverSources.AddRange(catalogScan.SourcesChecked);
                    if (catalogScan.Warnings.Count > 0)
                        warnings.Add("Alcune voci del catalogo driver sono state scartate perché non verificabili.");
                }
                catch (Exception ex)
                {
                    warnings.Add(ex.Message);
                    LogService.Write("Catalogo driver ufficiali non disponibile.", ex);
                }

            }

            if (hardwareScan is not null)
            {
                ApplyHardware(hardwareScan);
                var sourceLabel = string.Join(", ", driverSources.Distinct(StringComparer.CurrentCultureIgnoreCase));
                var verifiedCount = microsoftDriverCount + catalogDriverCount;
                HardwareCheckStatus = LocalizationService.IsEnglish
                    ? verifiedCount > 0
                        ? $"{verifiedCount} verified driver updates ({microsoftDriverCount} Microsoft, {catalogDriverCount} manufacturers). Sources: {sourceLabel}."
                        : $"No verified installable drivers. Checked {driverSources.Distinct(StringComparer.CurrentCultureIgnoreCase).Count()} automatic sources; {hardwareScan.VendorTools.Count} manual manufacturer checks are available without extra apps."
                    : verifiedCount > 0
                        ? $"{verifiedCount} aggiornamenti driver verificati ({microsoftDriverCount} Microsoft, {catalogDriverCount} produttori). Fonti: {sourceLabel}."
                        : $"Nessun driver installabile verificato. Controllate {driverSources.Distinct(StringComparer.CurrentCultureIgnoreCase).Count()} fonti automatiche; disponibili {hardwareScan.VendorTools.Count} controlli manuali ufficiali senza app aggiuntive.";
            }

            Progress = 100;
            StatusText = warnings.Count > 0 && Updates.Count == 0
                ? T("Scansione incompleta", "Incomplete scan")
                : Updates.Count == 0
                    ? T("Il PC risulta aggiornato", "The PC is up to date")
                    : LocalizationService.IsEnglish
                        ? $"{Updates.Count} updates available"
                        : $"{Updates.Count} aggiornamenti disponibili";
            CurrentItemText = warnings.Count == 0
                ? T(
                    "Scansione completata usando WinGet, Windows Update e il catalogo verificato dei produttori.",
                    "Scan completed using WinGet, Windows Update and the verified manufacturer catalog.")
                : LocalizationService.IsEnglish
                    ? $"Scan completed with warnings: {string.Join(" · ", warnings)}"
                    : $"Scansione completata con avvisi: {string.Join(" · ", warnings)}";
            _hasCurrentScan = true;
            Settings.LastScanUtc = DateTime.UtcNow;
            JsonStorage.SaveSettings(Settings);
            OnPropertyChanged(nameof(LastScanLabel));
            OnPropertyChanged(nameof(HomeScanSummary));
        }
        catch (OperationCanceledException)
        {
            StatusText = T("Scansione annullata", "Scan cancelled");
            CurrentItemText = T("Puoi avviare una nuova scansione.", "You can start a new scan.");
            HardwareCheckStatus = T("Controllo automatico annullato.", "Automatic check cancelled.");
        }
        finally
        {
            _scanCancellation.Dispose();
            _scanCancellation = null;
            IsScanRunning = false;
            IsBusy = false;
            NotifyCounts();
        }
    }

    public void CancelScan()
    {
        if (!IsScanRunning) return;
        IsScanRunning = false;
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
                AppUpdateStatus = LocalizationService.IsEnglish
                    ? $"Version v{update.AvailableVersion} is ignored."
                    : $"La versione v{update.AvailableVersion} è stata ignorata.";
                return null;
            }

            AppUpdateStatus = LocalizationService.IsEnglish
                ? $"Update Center v{update.AvailableVersion} is available."
                : $"Disponibile Update Center v{update.AvailableVersion}.";
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
        AppUpdateStatus = LocalizationService.IsEnglish
            ? $"Version v{version} will no longer be offered."
            : $"La versione v{version} non verrà più proposta.";
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

    public async Task<UpdateRunStatus?> InstallItemsAsync(IReadOnlyList<UpdateItem> items)
    {
        var selected = items.Distinct().ToList();
        if (IsBusy || selected.Count == 0) return null;

        IsBusy = true;
        Progress = 2;
        StatusText = T("Aggiornamento in corso", "Update in progress");
        CurrentItemText = T("Conferma la richiesta di Controllo account utente di Windows.", "Confirm the Windows User Account Control prompt if requested.");
        foreach (var item in selected)
        {
            item.Status = T("In attesa", "Waiting");
            item.ResultDetails = T("In attesa dell'installazione.", "Waiting for installation.");
        }

        try
        {
            var result = await _coordinator.RunAsync(selected, Settings, status =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var itemFraction = status.CurrentIndex < status.Total
                        ? Math.Clamp(status.CurrentItemProgress, 0, 100) / 100d
                        : 0;
                    var completedPercentage = status.Total == 0
                        ? 0
                        : (status.CurrentIndex + itemFraction) * 100d / status.Total;
                    Progress = Math.Max(Progress, completedPercentage);
                    CurrentItemText = status.Message;
                    StatusText = string.IsNullOrWhiteSpace(status.CurrentName)
                        ? T("Aggiornamento in corso", "Update in progress")
                        : LocalizationService.IsEnglish ? $"Updating: {status.CurrentName}" : $"Aggiornamento: {status.CurrentName}";

                    ApplyRunResults(selected, status.Results);
                });
            }, CancellationToken.None);

            ApplyRunResults(selected, result.Results);
            SaveRunToHistory(selected, result);
            Progress = 100;
            StatusText = result.Results.All(x => x.Success)
                ? T("Aggiornamenti completati", "Updates completed")
                : T("Completato con alcuni errori", "Completed with some errors");
            CurrentItemText = result.Message;
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
            foreach (var item in selected)
            {
                item.Status = T("Errore", "Error");
                item.ResultDetails = ex.Message;
            }
            return null;
        }
        finally
        {
            IsBusy = false;
            NotifyCounts();
        }
    }

    public void SetAllSelected(bool selected)
    {
        foreach (var item in Updates)
            item.IsSelected = selected && item.CanInstall && !item.RequiresRiskConfirmation;
        NotifyCounts();
    }

    private void ApplyRunResults(
        IReadOnlyList<UpdateItem> selected, IReadOnlyList<ItemRunResult> results)
    {
        var removedAny = false;
        foreach (var runResult in results)
        {
            var item = selected.FirstOrDefault(x =>
                x.Id.Equals(runResult.Id, StringComparison.OrdinalIgnoreCase) &&
                x.Kind.ToString().Equals(runResult.Kind, StringComparison.OrdinalIgnoreCase));
            if (item is null) continue;

            item.Status = runResult.Outcome switch
            {
                UpdateOutcomes.NotApplicable => T("Non applicabile", "Not applicable"),
                UpdateOutcomes.ManualRequired => T("Aggiornamento manuale", "Manual update"),
                _ => runResult.Success ? T("Aggiornato", "Updated") : T("Errore", "Error")
            };
            item.ResultDetails = runResult.Message;
            item.Diagnostics = runResult.Diagnostics;
            if (runResult.Outcome.Equals(UpdateOutcomes.ManualRequired, StringComparison.Ordinal))
                item.CanInstall = false;

            var shouldRemove = runResult.Success &&
                               !runResult.Outcome.Equals(UpdateOutcomes.ManualRequired, StringComparison.Ordinal);
            if (shouldRemove && Updates.Remove(item))
            {
                item.PropertyChanged -= UpdateItemOnPropertyChanged;
                removedAny = true;
            }
        }

        if (!removedAny) return;
        RefreshUpdatesView();
        NotifyCounts();
    }

    public void NotifyCounts()
    {
        OnPropertyChanged(nameof(AvailableCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(VisibleUpdateCount));
        OnPropertyChanged(nameof(SoftwareUpdateCount));
        OnPropertyChanged(nameof(DriverCount));
        OnPropertyChanged(nameof(VisibleDriverCount));
        OnPropertyChanged(nameof(ChipsetCount));
        OnPropertyChanged(nameof(DriverUpdateCount));
        OnPropertyChanged(nameof(VendorCheckCount));
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(HomeScanSummary));
    }

    public void SaveSettings()
    {
        Settings.LanguageMode = LocalizationService.Normalize(Settings.LanguageMode);
        Settings.AutomaticScanInterval = Settings.AutomaticScanInterval is "Daily" or "Weekly"
            ? Settings.AutomaticScanInterval
            : "Off";
        JsonStorage.SaveSettings(Settings);
    }

    public void NotifyLanguageChanged()
    {
        StatusText = LocalizationService.Translate(StatusText);
        CurrentItemText = LocalizationService.Translate(CurrentItemText);
        AppUpdateStatus = LocalizationService.Translate(AppUpdateStatus);
        foreach (var item in Updates) item.RefreshLocalizedProperties();
        OnPropertyChanged(nameof(LastScanLabel));
        OnPropertyChanged(nameof(HomeScanSummary));
        OnPropertyChanged(nameof(LastAppUpdateCheckLabel));
        OnPropertyChanged(nameof(AppUpdateStatus));
        NotifyCounts();
    }

    public void ClearHistory()
    {
        History.Clear();
        JsonStorage.SaveHistory(History);
    }

    private void AddUpdates(IEnumerable<UpdateItem> updates)
    {
        foreach (var item in updates)
        {
            if (Updates.Any(existing =>
                    existing.Kind == item.Kind &&
                    existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            item.PropertyChanged += UpdateItemOnPropertyChanged;
            Updates.Add(item);
        }
        RefreshUpdatesView();
        NotifyCounts();
    }

    private void ClearUpdates()
    {
        foreach (var item in Updates) item.PropertyChanged -= UpdateItemOnPropertyChanged;
        Updates.Clear();
        NotifyCounts();
    }

    private void ApplyHardware(HardwareScanResult scan)
    {
        CpuName = scan.CpuName;
        ComputerName = string.Join(" ", new[] { scan.ComputerManufacturer, scan.ComputerModel }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        foreach (var driver in scan.Drivers) DriverInventory.Add(driver);
        foreach (var tool in scan.VendorTools) VendorTools.Add(tool);
        RefreshDriverInventoryView();
        NotifyCounts();
    }

    private void ClearHardware()
    {
        DriverInventory.Clear();
        VendorTools.Clear();
        DriverSearchText = "";
        DriverInventoryFilter = "Tutti";
        CpuName = "Processore non ancora rilevato";
        ComputerName = "";
        HardwareCheckStatus = "Esegui una scansione per controllare automaticamente i driver.";
        NotifyCounts();
    }

    private void UpdateItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateItem.IsSelected))
        {
            NotifyCounts();
            if (Filter == "Selezionati") RefreshUpdatesView();
        }
        else if (e.PropertyName == nameof(UpdateItem.Status) && Filter == "Errori")
        {
            RefreshUpdatesView();
        }
    }

    private bool FilterUpdate(object value)
    {
        if (value is not UpdateItem item) return false;
        var typeMatches = Filter switch
        {
            "Software" => item.Kind == UpdateKind.Software,
            "Driver" => item.Kind == UpdateKind.Driver,
            "Importanti" => item.IsImportant,
            "Standard" => !item.IsImportant && !item.IsOptional,
            "Facoltativi" => item.IsOptional,
            "Selezionati" => item.IsSelected,
            "Riavvio richiesto" => item.RequiresRestart,
            "Errori" => item.Status.Equals("Errore", StringComparison.OrdinalIgnoreCase) ||
                        item.Status.Equals("Error", StringComparison.OrdinalIgnoreCase),
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
        UpdatesView.Refresh();
        OnPropertyChanged(nameof(VisibleUpdateCount));
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
        DriverInventoryView.Refresh();
        OnPropertyChanged(nameof(VisibleDriverCount));
    }

    private void SaveRunToHistory(IReadOnlyList<UpdateItem> selected, UpdateRunStatus result)
    {
        foreach (var runResult in result.Results)
        {
            var item = selected.FirstOrDefault(x => x.Id == runResult.Id);
            var entry = new HistoryEntry
            {
                Timestamp = DateTime.UtcNow,
                Name = runResult.Name,
                Kind = runResult.Kind,
                FromVersion = item?.InstalledVersion ?? "",
                ToVersion = item?.AvailableVersion ?? "",
                Result = runResult.Outcome switch
                {
                    UpdateOutcomes.NotApplicable => "Non applicabile",
                    UpdateOutcomes.ManualRequired => "Manuale",
                    _ => runResult.Success ? "Riuscito" : "Fallito"
                },
                Details = BuildReadableHistoryDetails(item, runResult),
                Diagnostics = runResult.Diagnostics
            };
            History.Insert(0, entry);
        }
        JsonStorage.SaveHistory(History);
    }

    private static string BuildReadableHistoryDetails(UpdateItem? item, ItemRunResult runResult)
    {
        var fromVersion = string.IsNullOrWhiteSpace(item?.InstalledVersion) ? "versione precedente non rilevata" : item.InstalledVersion;
        var toVersion = string.IsNullOrWhiteSpace(item?.AvailableVersion) ? "versione più recente disponibile" : item.AvailableVersion;
        var source = string.IsNullOrWhiteSpace(item?.Source) ? "fonte di aggiornamento configurata" : item.Source;
        var technicalDetail = string.IsNullOrWhiteSpace(runResult.Message) ? "Nessun dettaglio tecnico aggiuntivo." : runResult.Message.Trim();

        if (runResult.Outcome.Equals(UpdateOutcomes.NotApplicable, StringComparison.Ordinal))
            return $"{runResult.Name} non Ã¨ applicabile a questo PC secondo WinGet. " +
                   $"La segnalazione da {fromVersion} a {toVersion} Ã¨ stata rimossa. Dettaglio: {technicalDetail}";

        if (runResult.Outcome.Equals(UpdateOutcomes.ManualRequired, StringComparison.Ordinal))
            return $"{runResult.Name} richiede un aggiornamento manuale perchÃ© il pacchetto installato e quello nuovo " +
                   $"non supportano un upgrade automatico compatibile. Dettaglio: {technicalDetail}";

        if (runResult.Success)
        {
            var restart = runResult.RestartRequired
                ? " Per completare l'operazione è richiesto il riavvio di Windows."
                : " Non è richiesto alcun riavvio.";
            return $"{runResult.Name} è stato aggiornato correttamente da {fromVersion} a {toVersion} usando {source}.{restart} Dettaglio tecnico: {technicalDetail}";
        }

        return $"L'aggiornamento di {runResult.Name} da {fromVersion} a {toVersion} non è riuscito. " +
               $"L'elemento resta disponibile per un nuovo tentativo. Motivo: {technicalDetail}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private static string T(string italian, string english) => LocalizationService.Text(italian, english);
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
