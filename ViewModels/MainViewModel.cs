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
    private double _progress;
    private string _statusText = "Pronto per la scansione";
    private string _currentItemText = "Software e driver verranno verificati tramite fonti ufficiali.";
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
    private string _appUpdateStatus = "Controllo aggiornamenti dell'app non ancora eseguito.";

    public MainViewModel()
    {
        AppPaths.EnsureCreated();
        Settings = JsonStorage.LoadSettings();
        if (Settings.LastAppUpdateCheckUtc is DateTime lastUpdateCheck)
            _appUpdateStatus = $"Ultimo controllo: {lastUpdateCheck.ToLocalTime():dd/MM/yyyy HH:mm}.";
        foreach (var entry in JsonStorage.LoadHistory()) History.Add(entry);
        UpdatesView = CollectionViewSource.GetDefaultView(Updates);
        UpdatesView.Filter = FilterUpdate;
        DriverInventoryView = CollectionViewSource.GetDefaultView(DriverInventory);
        DriverInventoryView.Filter = FilterDriverInventory;
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
    public bool CanUpdate => !IsBusy && Updates.Any(x => x.IsSelected);

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
    public int SelectedCount => Updates.Count(x => x.IsSelected);
    public int VisibleUpdateCount => UpdatesView.Cast<object>().Count();
    public int SoftwareUpdateCount => Updates.Count(x => x.Kind == UpdateKind.Software);
    public int DriverCount => DriverInventory.Count;
    public int VisibleDriverCount => DriverInventoryView.Cast<object>().Count();
    public int ChipsetCount => DriverInventory.Count(x => x.IsProcessorOrChipset);
    public int DriverUpdateCount => Updates.Count(x => x.Kind == UpdateKind.Driver);
    public int VendorCheckCount => VendorTools.Count;
    public string LastScanLabel => Settings.LastScanUtc is DateTime timestamp
        ? timestamp.ToLocalTime().ToString("dd/MM/yyyy 'alle' HH:mm")
        : "Nessuna scansione completata";
    public string HomeScanSummary => !_hasCurrentScan
        ? "Avvia una scansione per ottenere risultati aggiornati."
        : AvailableCount == 0
            ? "Nessun aggiornamento disponibile al momento."
            : $"{SoftwareUpdateCount} software e {DriverUpdateCount} driver da controllare.";
    public IReadOnlyList<UpdateItem> SelectedItems => Updates.Where(x => x.IsSelected).ToList();
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
        ? timestamp.ToLocalTime().ToString("dd/MM/yyyy 'alle' HH:mm")
        : "Mai eseguito";

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
        StatusText = "Scansione in corso";
        CurrentItemText = "Preparazione dei servizi di aggiornamento…";
        ClearUpdates();
        ClearHardware();
        HardwareCheckStatus = "Controllo automatico dei driver in corso…";
        _scanCancellation = new CancellationTokenSource();
        var warnings = new List<string>();
        HardwareScanResult? hardwareScan = null;
        var driverSources = new List<string>();
        var microsoftDriverCount = 0;
        var catalogDriverCount = 0;

        try
        {
            Progress = 12;
            CurrentItemText = "Ricerca dei software aggiornabili con WinGet…";
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
            CurrentItemText = "Inventario di processore, chipset e driver installati…";
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
            CurrentItemText = "Ricerca dei driver proposti da Windows Update…";
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
                HardwareCheckStatus = "Controllo automatico dei driver non completato.";
            }

            if (hardwareScan is not null)
            {
                Progress = 84;
                CurrentItemText = "Confronto con il catalogo verificato dei produttori…";
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
                HardwareCheckStatus = verifiedCount > 0
                    ? $"{verifiedCount} aggiornamenti driver verificati ({microsoftDriverCount} Microsoft, {catalogDriverCount} produttori). Fonti: {sourceLabel}."
                    : $"Nessun driver installabile verificato. Controllate {driverSources.Distinct(StringComparer.CurrentCultureIgnoreCase).Count()} fonti automatiche; disponibili {hardwareScan.VendorTools.Count} controlli manuali ufficiali senza app aggiuntive.";
            }

            Progress = 100;
            StatusText = warnings.Count > 0 && Updates.Count == 0
                ? "Scansione incompleta"
                : Updates.Count == 0
                    ? "Il PC risulta aggiornato"
                    : $"{Updates.Count} aggiornamenti disponibili";
            CurrentItemText = warnings.Count == 0
                ? "Scansione completata usando WinGet, Windows Update e il catalogo verificato dei produttori."
                : $"Scansione completata con avvisi: {string.Join(" · ", warnings)}";
            _hasCurrentScan = true;
            Settings.LastScanUtc = DateTime.UtcNow;
            JsonStorage.SaveSettings(Settings);
            OnPropertyChanged(nameof(LastScanLabel));
            OnPropertyChanged(nameof(HomeScanSummary));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scansione annullata";
            CurrentItemText = "Puoi avviare una nuova scansione.";
            HardwareCheckStatus = "Controllo automatico annullato.";
        }
        finally
        {
            _scanCancellation.Dispose();
            _scanCancellation = null;
            IsBusy = false;
            NotifyCounts();
        }
    }

    public void CancelScan() => _scanCancellation?.Cancel();

    public async Task<AppUpdateInfo?> CheckForAppUpdateAsync(bool manual)
    {
        if (IsAppUpdateCheckBusy) return null;
        if (!manual && !Settings.CheckAppUpdatesAutomatically) return null;
        if (!manual && Settings.LastAppUpdateCheckUtc is DateTime previous &&
            DateTime.UtcNow - previous < TimeSpan.FromHours(24))
            return null;

        IsAppUpdateCheckBusy = true;
        AppUpdateStatus = "Controllo della Release stabile più recente…";
        var checkAttempted = false;
        try
        {
            if (!manual)
                await Task.Delay(1200);
            checkAttempted = true;
            var update = await _appUpdateService.CheckForUpdateAsync(CancellationToken.None);
            if (update is null)
            {
                AppUpdateStatus = "Update Center è aggiornato.";
                return null;
            }

            if (Settings.IgnoredAppVersion.Equals(update.AvailableVersion.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                AppUpdateStatus = $"La versione v{update.AvailableVersion} è stata ignorata.";
                return null;
            }

            AppUpdateStatus = $"Disponibile Update Center v{update.AvailableVersion}.";
            return update;
        }
        catch (Exception ex)
        {
            LogService.Write("Controllo aggiornamenti dell'app non riuscito.", ex);
            AppUpdateStatus = manual
                ? "Controllo non riuscito. Verifica la connessione e riprova."
                : "Controllo automatico non disponibile; l'app continuerà normalmente.";
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
        AppUpdateStatus = $"La versione v{version} non verrà più proposta.";
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
        Progress = 0;
        StatusText = "Aggiornamento in corso";
        CurrentItemText = "Conferma la richiesta di Controllo account utente di Windows.";
        foreach (var item in selected)
        {
            item.Status = "In attesa";
            item.ResultDetails = "In attesa dell'installazione.";
        }

        try
        {
            var result = await _coordinator.RunAsync(selected, Settings, status =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Progress = status.Total == 0 ? 0 : status.CurrentIndex * 100d / status.Total;
                    CurrentItemText = status.Message;
                    StatusText = string.IsNullOrWhiteSpace(status.CurrentName)
                        ? "Aggiornamento in corso"
                        : $"Aggiornamento: {status.CurrentName}";

                    foreach (var runResult in status.Results)
                    {
                        var item = Updates.FirstOrDefault(x => x.Id == runResult.Id);
                        if (item is not null)
                        {
                            item.Status = runResult.Success ? "Aggiornato" : "Errore";
                            item.ResultDetails = runResult.Message;
                        }
                    }
                });
            }, CancellationToken.None);

            SaveRunToHistory(selected, result);
            Progress = 100;
            StatusText = result.Results.All(x => x.Success) ? "Aggiornamenti completati" : "Completato con alcuni errori";
            CurrentItemText = result.Message;
            return result;
        }
        catch (OperationCanceledException ex)
        {
            StatusText = "Aggiornamento annullato";
            CurrentItemText = ex.Message;
            foreach (var item in selected.Where(x => x.Status == "In attesa"))
            {
                item.Status = "Da aggiornare";
                item.ResultDetails = "Operazione annullata prima dell'installazione.";
            }
            return null;
        }
        catch (Exception ex)
        {
            LogService.Write("Aggiornamento selezionato fallito.", ex);
            StatusText = "Aggiornamento non avviato";
            CurrentItemText = ex.Message;
            foreach (var item in selected)
            {
                item.Status = "Errore";
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
            item.IsSelected = selected;
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

    public void SaveSettings() => JsonStorage.SaveSettings(Settings);

    public void ClearHistory()
    {
        History.Clear();
        JsonStorage.SaveHistory(History);
    }

    private void AddUpdates(IEnumerable<UpdateItem> updates)
    {
        foreach (var item in updates)
        {
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
            "Errori" => item.Status.Equals("Errore", StringComparison.OrdinalIgnoreCase),
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
                Result = runResult.Success ? "Riuscito" : "Fallito",
                Details = BuildReadableHistoryDetails(item, runResult)
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
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
