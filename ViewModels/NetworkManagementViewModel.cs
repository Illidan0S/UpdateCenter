using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using UpdateCenter.Contracts;
using UpdateCenter.Core;
using UpdateCenter.RemoteClient;
using UpdateCenter.Services;

namespace UpdateCenter.ViewModels;

public sealed class NetworkManagementViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaximumConcurrentComputers = 4;
    private readonly ControllerIdentityStore _identityStore = new();
    private readonly RemoteAgentClient _remoteClient;
    private readonly LanDiscoveryClient _discoveryClient = new();
    private readonly AgentLocalClient _localAgentClient = new(AgentProtocol.ApprovalPipeName);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<Guid, NetworkAgentItem> _agentsById = [];
    private readonly Dictionary<string, NetworkAgentItem> _agentsByAddress = new(StringComparer.OrdinalIgnoreCase);
    private NetworkAgentItem? _selectedAgent;
    private bool _isBusy;
    private string _statusText = "Pronto. Cerca i PC con il componente di rete Update Center.";
    private string _address = "";
    private string _port = "47382";
    private string _pairingCode = "";
    private string _remoteState = "Nessun PC selezionato";
    private string _scanSummary = "Nessuna scansione remota eseguita.";
    private int _resultCount;
    private int _warningCount;
    private ResultScopeOption? _selectedResultScope;
    private bool _localStatusRefreshInProgress;
    private bool _knownAuthorizationRefreshInProgress;
    private DateTime _lastKnownAuthorizationRefreshUtc;
    private bool _hasLocalController;
    private string _localConnectionStateText = "Controllo dello stato di questo PC...";
    private string _localConnectionDetailText = "";

    public NetworkManagementViewModel()
    {
        _remoteClient = new RemoteAgentClient(_identityStore);
        ResultsView = CollectionViewSource.GetDefaultView(ScanResults);
        ResultsView.Filter = FilterResult;
        ResultsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RemoteUpdateSelectionItem.DeviceGroupLabel)));
        ResultsView.SortDescriptions.Add(new SortDescription(nameof(RemoteUpdateSelectionItem.DeviceGroupLabel), ListSortDirection.Ascending));
        ResultsView.SortDescriptions.Add(new SortDescription(nameof(RemoteUpdateSelectionItem.TypeOrder), ListSortDirection.Ascending));
        ResultsView.SortDescriptions.Add(new SortDescription(nameof(RemoteUpdateSelectionItem.Name), ListSortDirection.Ascending));
        ResultScopes.Add(ResultScopeOption.All);
        _selectedResultScope = ResultScopes[0];
        LoadSavedAgents();
        SelectedAgent = Agents.FirstOrDefault(x => x.IsPaired) ?? Agents.FirstOrDefault();
    }

    public ObservableCollection<NetworkAgentItem> Agents { get; } = [];
    public ObservableCollection<RemoteUpdateSelectionItem> ScanResults { get; } = [];
    public ObservableCollection<ResultScopeOption> ResultScopes { get; } = [];
    public ICollectionView ResultsView { get; }
    public bool HasLocalController
    {
        get => _hasLocalController;
        private set { if (_hasLocalController == value) return; _hasLocalController = value; OnPropertyChanged(); }
    }
    public string LocalConnectionStateText
    {
        get => LocalizationService.Translate(_localConnectionStateText);
        private set { if (_localConnectionStateText == value) return; _localConnectionStateText = value; OnPropertyChanged(); }
    }
    public string LocalConnectionDetailText
    {
        get => LocalizationService.Translate(_localConnectionDetailText);
        private set { if (_localConnectionDetailText == value) return; _localConnectionDetailText = value; OnPropertyChanged(); }
    }

    public NetworkAgentItem? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            if (ReferenceEquals(_selectedAgent, value)) return;
            _selectedAgent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AssociationPanelTitle));
            OnPropertyChanged(nameof(AssociationPanelDescription));
            if (value is not null)
            {
                Address = value.Address;
                Port = value.ApiPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
                RemoteState = value.IsPaired
                    ? "PC associato: puoi controllarne lo stato o avviare una scansione."
                    : value.HasController
                        ? "Questo dispositivo è collegato a un altro PC principale."
                        : "PC rilevato e pronto per l'associazione.";
            }
            LoadSelectedResults();
            OnActionStateChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnActionStateChanged();
        }
    }

    public string StatusText
    {
        get => LocalizationService.Translate(_statusText);
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string Address
    {
        get => _address;
        set
        {
            if (_address == value) return;
            _address = value.Trim();
            OnPropertyChanged();
            OnActionStateChanged();
        }
    }

    public string Port
    {
        get => _port;
        set
        {
            if (_port == value) return;
            _port = value.Trim();
            OnPropertyChanged();
            OnActionStateChanged();
        }
    }

    public string PairingCode
    {
        get => _pairingCode;
        set
        {
            if (_pairingCode == value) return;
            _pairingCode = new string(value.Where(char.IsDigit).Take(8).ToArray());
            OnPropertyChanged();
            OnActionStateChanged();
        }
    }

    public string RemoteState
    {
        get => LocalizationService.Translate(_remoteState);
        private set { _remoteState = value; OnPropertyChanged(); }
    }

    public string ScanSummary
    {
        get => LocalizationService.Translate(_scanSummary);
        private set { _scanSummary = value; OnPropertyChanged(); }
    }

    public int ResultCount
    {
        get => _resultCount;
        private set { _resultCount = value; OnPropertyChanged(); }
    }

    public int WarningCount
    {
        get => _warningCount;
        private set { _warningCount = value; OnPropertyChanged(); }
    }

    public ResultScopeOption? SelectedResultScope
    {
        get => _selectedResultScope;
        set
        {
            if (ReferenceEquals(_selectedResultScope, value) || value is null) return;
            _selectedResultScope = value;
            OnPropertyChanged();
            MainViewModel.TryRefreshCollectionView(ResultsView, "network-results");
            UpdateResultSummary();
            OnActionStateChanged();
        }
    }

    public int SelectedComputerCount => GetScanTargets().Count;
    public string ScanActionText => SelectedComputerCount switch
    {
        0 => LocalizationService.Text("Scansiona", "Scan"),
        1 => LocalizationService.Text("Scansiona 1 PC", "Scan 1 PC"),
        var count => LocalizationService.IsEnglish ? $"Scan {count} PCs" : $"Scansiona {count} PC"
    };
    public int SelectedUpdateCount => GetVisibleSelectedUpdates().Count;
    public int UpdateComputerCount => GetVisibleSelectedUpdates().Select(x => x.AgentId).Distinct().Count();
    public string UpdateActionText => LocalizationService.Translate("Aggiorna elementi selezionati");
    public int ConnectionRequestTargetCount => GetConnectionRequestTargets().Count;
    public string ConnectionRequestActionText => ConnectionRequestTargetCount switch
    {
        0 => LocalizationService.Translate("Richiedi collegamento"),
        1 => LocalizationService.Translate("Richiedi collegamento"),
        var count => LocalizationService.IsEnglish ? $"Request from {count} PCs" : $"Richiedi a {count} PC"
    };
    public string AssociationPanelTitle => SelectedAgent?.IsPaired == true
        ? LocalizationService.Translate("Connessione sicura")
        : LocalizationService.Translate("Associazione sicura");
    public string AssociationPanelDescription => SelectedAgent?.IsPaired == true
        ? LocalizationService.Text("Il dispositivo evidenziato ha autorizzato questo PC principale.", "The highlighted device authorized this controller PC.")
        : LocalizationService.Text("Inserisci il codice temporaneo mostrato sul PC che vuoi associare.", "Enter the temporary code shown on the PC you want to pair.");

    public bool CanDiscover => !IsBusy;
    public bool CanRequestConnections => !IsBusy && ConnectionRequestTargetCount > 0;
    public bool CanPair => !IsBusy && IsEndpointValid() && PairingCode.Length == 8 &&
                           SelectedAgent?.IsPaired != true && SelectedAgent?.HasController != true;
    public bool CanContact => !IsBusy && SelectedAgent?.IsPaired == true;
    public bool CanScanSelected => !IsBusy && Agents.Any(x => x.IsSelected && x.IsPaired);
    public bool CanScanTargets => !IsBusy && GetScanTargets().Count > 0;
    public bool CanUpdateCurrent => !IsBusy && SelectedAgent?.IsPaired == true &&
                                    SelectedAgent.ScanOperationId != Guid.Empty &&
                                    SelectedAgent.Updates.Any(x => x.IsSelected && x.CanInstall);
    public bool CanUpdateSelected => !IsBusy && SelectedUpdateCount > 0;
    public bool CanCancelCurrent => SelectedAgent?.IsPaired == true && SelectedAgent.ActiveOperationId != Guid.Empty;

    public async Task RefreshLocalConnectionStatusAsync()
    {
        if (_localStatusRefreshInProgress) return;
        _localStatusRefreshInProgress = true;
        try
        {
            var response = await _localAgentClient.SendAsync(
                new AgentRequest { Command = AgentCommands.GetNetworkConfiguration },
                TimeSpan.FromSeconds(2),
                _lifetime.Token);
            var configuration = response.Success ? response.Network : null;
            if (configuration is null) throw new InvalidDataException("Stato locale non disponibile.");

            HasLocalController = configuration.HasController;
            LocalConnectionStateText = configuration.HasController
                ? $"Connesso a {configuration.ControllerName}"
                : configuration.Enabled ? "Questo PC non è collegato" : "Gestione remota disabilitata";
            LocalConnectionDetailText = configuration.HasController
                ? "Il PC principale può controllare scansioni e aggiornamenti."
                : configuration.ConnectionRequestsEnabled
                    ? "Rilevabile: le richieste di collegamento sono abilitate."
                    : configuration.Enabled
                        ? "Le richieste sono disabilitate da Configura questo PC."
                        : "Configura questo PC per renderlo disponibile nella rete locale.";
        }
        catch (Exception)
        {
            HasLocalController = false;
            LocalConnectionStateText = "Componente di rete non disponibile";
            LocalConnectionDetailText = "Apri Configura questo PC per installarlo o controllarne lo stato.";
        }
        finally
        {
            _localStatusRefreshInProgress = false;
        }
    }

    public async Task RefreshNetworkPageStatusAsync()
    {
        await RefreshLocalConnectionStatusAsync();
        if (_knownAuthorizationRefreshInProgress ||
            DateTime.UtcNow - _lastKnownAuthorizationRefreshUtc < TimeSpan.FromSeconds(2))
            return;

        _knownAuthorizationRefreshInProgress = true;
        _lastKnownAuthorizationRefreshUtc = DateTime.UtcNow;
        try
        {
            var paired = Agents.Where(x => x.IsPaired).ToList();
            using var concurrency = new SemaphoreSlim(16, 16);
            var checks = paired.Select(async agent =>
            {
                await concurrency.WaitAsync(_lifetime.Token);
                try
                {
                    var current = await _discoveryClient.ProbeAddressAsync(
                        agent.Address,
                        agent.ApiPort,
                        _lifetime.Token);
                    var authorizationRejected = false;
                    if (current is not null && current.AgentId == agent.AgentId && current.HasController)
                    {
                        try
                        {
                            await _remoteClient.GetStatusAsync(agent.Address, _lifetime.Token);
                        }
                        catch (RemoteAgentException ex) when (ex.ErrorCode == "Unauthorized")
                        {
                            authorizationRejected = true;
                        }
                        catch
                        {
                            // Un errore di rete non equivale a una revoca.
                        }
                    }
                    return (Agent: agent, Current: current, AuthorizationRejected: authorizationRejected);
                }
                finally
                {
                    concurrency.Release();
                }
            });

            var results = await Task.WhenAll(checks);
            var revoked = 0;
            foreach (var result in results.Where(x => x.Current is not null &&
                                                       x.Current.AgentId == x.Agent.AgentId &&
                                                       (!x.Current.HasController || x.AuthorizationRejected)))
            {
                result.Agent.Apply(result.Current!, isPaired: false);
                ClearLocalAuthorization(
                    result.Agent,
                    hasAnotherController: result.AuthorizationRejected && result.Current!.HasController);
                revoked++;
            }
            if (revoked > 0)
            {
                LoadSelectedResults();
                SortAgents();
                StatusText = revoked == 1
                    ? "Un dispositivo ha revocato il collegamento ed è ora non autorizzato."
                    : $"{revoked} dispositivi hanno revocato il collegamento e sono ora non autorizzati.";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _knownAuthorizationRefreshInProgress = false;
        }
    }

    public async Task DiscoverAsync()
    {
        if (!CanDiscover) return;
        IsBusy = true;
        StatusText = "Ricerca automatica dei PC nella rete locale...";
        try
        {
            var discovered = await _discoveryClient.DiscoverAsync(TimeSpan.FromSeconds(3), _lifetime.Token);
            MergeDiscoveredAgents(discovered);
            StatusText = discovered.Count == 0
                ? "Nessun PC gestibile trovato. Verifica che il componente di rete sia abilitato e che i dispositivi non usino una rete ospiti isolata."
                : $"Rilevati {discovered.Count} PC con il componente di rete Update Center.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            StatusText = "Ricerca interrotta.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ricerca non riuscita: {FriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PairAsync()
    {
        if (!CanPair || !TryGetPort(out var port)) return;
        NetworkAgentItem? pairedItem = null;
        IsBusy = true;
        StatusText = $"Associazione con {Address} in corso...";
        try
        {
            var record = await _remoteClient.PairAsync(
                Address, port, PairingCode, SelectedAgent?.DisplayName ?? Address, _lifetime.Token);
            var item = FindAgent(record.AgentId, record.Address);
            if (item is null)
            {
                item = new NetworkAgentItem();
                AddAgent(item);
            }
            var previousId = item.AgentId;
            var previousAddress = item.Address;
            item.Apply(record);
            pairedItem = item;
            ReindexAgent(item, previousId, previousAddress);
            SelectedAgent = item;
            var verifiedStatus = (await _remoteClient.GetStatusAsync(item.Address, _lifetime.Token)).Status
                                 ?? throw new InvalidDataException("Stato remoto mancante dopo l'associazione.");
            item.Apply(verifiedStatus);
            PairingCode = "";
            RemoteState = $"{verifiedStatus.MachineName} · {verifiedStatus.OperatingSystem} · componente {verifiedStatus.AgentVersion}";
            StatusText = $"{item.DisplayName} è associato, verificato e raggiungibile.";
        }
        catch (RemoteAgentException ex) when (ex.ErrorCode == "Unauthorized")
        {
            if (SelectedAgent is not null) await HandleAuthorizationRevokedAsync(SelectedAgent);
            StatusText = "Il dispositivo non autorizza questo PC principale. Richiedi nuovamente il collegamento.";
        }
        catch (Exception ex)
        {
            if (pairedItem is not null)
            {
                pairedItem.ConnectionState = "Da verificare";
                StatusText = $"Associazione salvata, ma il controllo immediato non è riuscito: {FriendlyMessage(ex)}. Premi Controlla stato.";
            }
            else
            {
                StatusText = $"Associazione non riuscita: {FriendlyMessage(ex)}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RequestConnectionsAsync(NetworkAgentItem? explicitAgent = null)
    {
        var targets = explicitAgent is null
            ? GetConnectionRequestTargets()
            : explicitAgent.CanRequestConnection ? [explicitAgent] : [];
        if (targets.Count == 0) return;

        StatusText = targets.Count == 1
            ? $"Invio richiesta di collegamento a {targets[0].DisplayName}..."
            : $"Invio richieste di collegamento a {targets.Count} PC...";
        using var concurrency = new SemaphoreSlim(MaximumConcurrentComputers, MaximumConcurrentComputers);
        var startTasks = targets.Select(async agent =>
        {
            await concurrency.WaitAsync(_lifetime.Token);
            try { return (Agent: agent, Pending: await BeginConnectionRequestAsync(agent)); }
            finally
            {
                concurrency.Release();
            }
        });
        var started = await Task.WhenAll(startTasks);
        await Task.WhenAll(started
            .Where(x => x.Pending is not null)
            .Select(x => PollConnectionRequestAsync(x.Agent, x.Pending!)));

        var accepted = targets.Count(x => x.IsPaired);
        var pending = targets.Count(x => x.ConnectionRequestInProgress);
        StatusText = pending > 0
            ? $"{pending} richieste ancora in attesa; {accepted} collegamenti accettati."
            : accepted == targets.Count
                ? $"Collegati {accepted} PC."
                : $"Richieste concluse: {accepted} accettate, {targets.Count - accepted} non accettate.";
        SortAgents();
        OnActionStateChanged();
    }

    private async Task<PendingRemoteConnectionRequest?> BeginConnectionRequestAsync(NetworkAgentItem agent)
    {
        agent.ConnectionRequestInProgress = true;
        agent.ConnectionRequestStatus = "Invio richiesta";
        try
        {
            var pending = await _remoteClient.RequestConnectionAsync(
                agent.Address, agent.ApiPort, agent.DisplayName, _lifetime.Token);
            agent.ConnectionRequestStatus = "In attesa di approvazione";
            return pending;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            agent.ConnectionRequestStatus = "Richiesta interrotta";
        }
        catch (Exception ex)
        {
            agent.ConnectionRequestStatus = $"Non riuscita: {FriendlyMessage(ex)}";
        }
        agent.ConnectionRequestInProgress = false;
        return null;
    }

    private async Task PollConnectionRequestAsync(
        NetworkAgentItem agent,
        PendingRemoteConnectionRequest pending)
    {
        try
        {
            while (DateTime.UtcNow < pending.ExpiresUtc)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token);
                var response = await _remoteClient.GetConnectionRequestStatusAsync(pending, _lifetime.Token);
                if (response.Status == ConnectionRequestStates.Pending) continue;
                if (response.Status == ConnectionRequestStates.Accepted)
                {
                    var record = _identityStore.FindByAddress(agent.Address)
                                 ?? throw new InvalidDataException("Autorizzazione accettata ma non salvata.");
                    var previousId = agent.AgentId;
                    var previousAddress = agent.Address;
                    agent.Apply(record);
                    ReindexAgent(agent, previousId, previousAddress);
                    try
                    {
                        var status = (await _remoteClient.GetStatusAsync(agent.Address, _lifetime.Token)).Status;
                        if (status is not null) agent.Apply(status);
                    }
                    catch
                    {
                        agent.ConnectionState = "Da verificare";
                    }
                    return;
                }
                agent.ConnectionRequestStatus = response.Status == ConnectionRequestStates.Rejected
                    ? "Richiesta rifiutata"
                    : "Richiesta scaduta";
                return;
            }
            agent.ConnectionRequestStatus = "Richiesta scaduta";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            agent.ConnectionRequestStatus = "Richiesta interrotta";
        }
        catch (Exception ex)
        {
            agent.ConnectionRequestStatus = $"Non riuscita: {FriendlyMessage(ex)}";
        }
        finally
        {
            agent.ConnectionRequestInProgress = false;
        }
    }

    public async Task RefreshStatusAsync()
    {
        var agent = SelectedAgent;
        if (agent is null || !CanContact) return;
        IsBusy = true;
        StatusText = $"Controllo di {agent.DisplayName}...";
        try
        {
            var status = (await _remoteClient.GetStatusAsync(agent.Address, _lifetime.Token)).Status
                         ?? throw new InvalidDataException("Stato remoto mancante.");
            agent.Apply(status);
            RemoteState = $"{status.MachineName} · {status.OperatingSystem} · componente {status.AgentVersion}";
            if (status.OperationInProgress && status.ActiveOperationId != Guid.Empty)
            {
                StatusText = $"PC raggiungibile; recupero avanzamento {NetworkAgentItem.OperationKindText(status.ActiveOperationKind)}...";
                await MonitorExistingOperationAsync(agent, status.ActiveOperationId);
            }
            else
            {
                StatusText = "PC raggiungibile e pronto per una scansione remota.";
            }
        }
        catch (RemoteAgentException ex) when (ex.ErrorCode == "Unauthorized")
        {
            await HandleAuthorizationRevokedAsync(agent);
            StatusText = "Il dispositivo ha revocato il collegamento. Richiedi una nuova autorizzazione.";
        }
        catch (Exception ex)
        {
            agent.ConnectionState = "Non raggiungibile";
            StatusText = $"Controllo non riuscito: {FriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StartScanAsync()
    {
        var agent = SelectedAgent;
        if (agent is null || !CanContact) return;
        await RunForAgentsAsync([agent], ScanAgentAsync, "scansione");
    }

    public async Task StartSelectedScansAsync()
    {
        if (!CanScanTargets) return;
        await RunForAgentsAsync(GetScanTargets(), ScanAgentAsync, "scansioni");
    }

    public async Task StartUpdateCurrentAsync()
    {
        var agent = SelectedAgent;
        if (agent is null || !CanUpdateCurrent) return;
        if (agent.Updates.Any(x => x.IsSelected && x.CanInstall && x.RequiresRiskConfirmation && !x.RiskConfirmed))
        {
            StatusText = "Conferma o escludi gli aggiornamenti con rimozione preventiva.";
            return;
        }
        await RunForAgentsAsync([agent], UpdateAgentAsync, "aggiornamento");
    }

    public async Task StartUpdatesSelectedAsync()
    {
        if (!CanUpdateSelected) return;
        if (GetVisibleSelectedUpdates().Any(x => x.RequiresRiskConfirmation && !x.RiskConfirmed))
        {
            StatusText = "Conferma o escludi gli aggiornamenti con rimozione preventiva.";
            return;
        }
        var selectedByAgent = GetVisibleSelectedUpdates()
            .GroupBy(x => x.AgentId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<RemoteUpdateSelectionItem>)x.ToList());
        var agents = Agents.Where(agent => agent.IsPaired && agent.ScanOperationId != Guid.Empty &&
                                           selectedByAgent.ContainsKey(agent.AgentId)).ToList();
        await RunForAgentsAsync(
            agents,
            agent => UpdateAgentAsync(agent, selectedByAgent[agent.AgentId]),
            "aggiornamenti");
    }

    public void SelectVisibleUpdates()
    {
        var visible = ResultsView.Cast<RemoteUpdateSelectionItem>().ToList();
        foreach (var item in visible)
            item.IsSelected = item.CanInstall && !item.RequiresRiskConfirmation;
        StatusText = "Selezionati gli aggiornamenti installabili mostrati.";
        OnActionStateChanged();
    }

    public void DeselectVisibleUpdates()
    {
        foreach (var item in ResultsView.Cast<RemoteUpdateSelectionItem>()) item.IsSelected = false;
        StatusText = "Deselezionati gli aggiornamenti mostrati.";
        OnActionStateChanged();
    }

    public IReadOnlyList<RemoteUpdateSelectionItem> GetSelectedUpdatesForConfirmation() =>
        GetVisibleSelectedUpdates();

    public IReadOnlyList<RemoteUpdateSelectionItem> GetSelectedUpdatesForAgent(NetworkAgentItem agent) =>
        agent.Updates.Where(x => x.IsSelected && x.CanInstall).ToList();

    public IReadOnlyList<NetworkAgentItem> GetAgentsForUpdates(
        IReadOnlyList<RemoteUpdateSelectionItem> updates)
    {
        var agentIds = updates.Select(x => x.AgentId).ToHashSet();
        return Agents.Where(x => agentIds.Contains(x.AgentId)).ToList();
    }

    public void ApplyRiskDecision(
        IReadOnlyList<RemoteUpdateSelectionItem> updates,
        bool includeRiskyUpdates)
    {
        foreach (var item in updates.Where(x => x.RequiresRiskConfirmation))
        {
            item.RiskConfirmed = includeRiskyUpdates;
            if (!includeRiskyUpdates) item.IsSelected = false;
        }
        OnActionStateChanged();
    }

    public async Task CancelCurrentAsync()
    {
        var agent = SelectedAgent;
        if (agent is null || !CanCancelCurrent) return;
        try
        {
            await _remoteClient.CancelOperationAsync(agent.Address, agent.ActiveOperationId, _lifetime.Token);
            agent.OperationStatus = "Annullamento richiesto";
            StatusText = $"Annullamento richiesto su {agent.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Annullamento non riuscito: {FriendlyMessage(ex)}";
        }
    }

    private async Task RunForAgentsAsync(
        IReadOnlyList<NetworkAgentItem> agents,
        Func<NetworkAgentItem, Task> action,
        string operationLabel)
    {
        if (IsBusy || agents.Count == 0) return;
        IsBusy = true;
        StatusText = $"Avvio {operationLabel} su {agents.Count} PC...";
        using var concurrency = new SemaphoreSlim(MaximumConcurrentComputers, MaximumConcurrentComputers);
        try
        {
            var tasks = agents.Select(async agent =>
            {
                await concurrency.WaitAsync(_lifetime.Token);
                try { await action(agent); }
                finally { concurrency.Release(); }
            });
            await Task.WhenAll(tasks);
            var failed = agents.Count(x => x.ConnectionState is "Errore" or "Non raggiungibile");
            StatusText = failed == 0
                ? $"{operationLabel} completati su {agents.Count} PC."
                : $"{operationLabel} terminati: {agents.Count - failed} PC completati, {failed} con errori.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            StatusText = "Controllo locale interrotto; le operazioni già avviate possono proseguire sui PC remoti.";
        }
        catch (Exception ex)
        {
            StatusText = $"Operazione multipla non completata: {FriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
            LoadSelectedResults();
        }
    }

    private async Task ScanAgentAsync(NetworkAgentItem agent)
    {
        agent.OperationStatus = "Avvio scansione";
        agent.Progress = 2;
        agent.ConnectionState = "Scansione";
        try
        {
            var operation = (await _remoteClient.StartScanAsync(agent.Address, cancellationToken: _lifetime.Token)).Operation
                            ?? throw new InvalidDataException("Identificativo della scansione mancante.");
            agent.ActiveOperationId = operation.Id;
            while (!AgentOperationStates.IsTerminal(operation.State))
            {
                agent.OperationStatus = string.IsNullOrWhiteSpace(operation.Message)
                    ? NetworkAgentItem.OperationStateText(operation.State)
                    : operation.Message;
                await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token);
                operation = (await _remoteClient.GetOperationAsync(agent.Address, operation.Id, _lifetime.Token)).Operation
                            ?? throw new InvalidDataException("Stato della scansione mancante.");
            }
            if (operation.ScanResult is null)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(operation.Message)
                    ? $"Scansione terminata: {NetworkAgentItem.OperationStateText(operation.State)}." : operation.Message);
            agent.SetScanResults(operation.Id, operation.ScanResult);
            agent.ActiveOperationId = Guid.Empty;
            agent.Progress = 100;
            agent.OperationStatus = operation.State == AgentOperationStates.CompletedWithWarnings
                ? "Scansione completata con avvisi" : "Scansione completata";
            agent.ConnectionState = "Raggiungibile";
            if (ReferenceEquals(agent, SelectedAgent)) LoadSelectedResults();
        }
        catch (RemoteAgentException ex) when (ex.ErrorCode == "Unauthorized")
        {
            await HandleAuthorizationRevokedAsync(agent);
            agent.OperationStatus = "Autorizzazione revocata";
        }
        catch (Exception ex)
        {
            agent.ConnectionState = "Errore";
            agent.OperationStatus = FriendlyMessage(ex);
        }
    }

    private Task UpdateAgentAsync(NetworkAgentItem agent) =>
        UpdateAgentAsync(agent, agent.Updates.Where(x => x.IsSelected && x.CanInstall).ToList());

    private async Task UpdateAgentAsync(
        NetworkAgentItem agent,
        IReadOnlyList<RemoteUpdateSelectionItem> selected)
    {
        if (selected.Count == 0 || agent.ScanOperationId == Guid.Empty) return;
        agent.OperationStatus = "Invio aggiornamenti";
        agent.Progress = 1;
        agent.ConnectionState = "Aggiornamento";
        try
        {
            var request = new RemoteUpdateRequest
            {
                ScanOperationId = agent.ScanOperationId,
                Items = selected.Select(x => new RemoteUpdateSelection
                {
                    Id = x.Id,
                    Kind = x.Kind,
                    RiskConfirmed = !x.RequiresRiskConfirmation || x.RiskConfirmed
                }).ToList()
            };
            var operation = (await _remoteClient.StartUpdateAsync(agent.Address, request, _lifetime.Token)).Operation
                            ?? throw new InvalidDataException("Identificativo aggiornamento mancante.");
            agent.ActiveOperationId = operation.Id;
            while (!AgentOperationStates.IsTerminal(operation.State))
            {
                agent.ApplyProgress(operation);
                if (ReferenceEquals(agent, SelectedAgent)) LoadSelectedResults();
                await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.Token);
                operation = (await _remoteClient.GetOperationAsync(agent.Address, operation.Id, _lifetime.Token)).Operation
                            ?? throw new InvalidDataException("Stato aggiornamento mancante.");
            }
            agent.ApplyProgress(operation);
            agent.ApplyUpdateResult(operation.UpdateResult);
            agent.ConnectionState = operation.State == AgentOperationStates.Completed ? "Raggiungibile" : "Attenzione";
            agent.OperationStatus = operation.Message;
            if (ReferenceEquals(agent, SelectedAgent)) LoadSelectedResults();
        }
        catch (RemoteAgentException ex) when (ex.ErrorCode == "Unauthorized")
        {
            await HandleAuthorizationRevokedAsync(agent);
            agent.OperationStatus = "Autorizzazione revocata";
        }
        catch (Exception ex)
        {
            agent.ConnectionState = "Errore";
            agent.OperationStatus = FriendlyMessage(ex);
        }
    }

    private async Task MonitorExistingOperationAsync(NetworkAgentItem agent, Guid operationId)
    {
        AgentOperation operation;
        do
        {
            operation = (await _remoteClient.GetOperationAsync(agent.Address, operationId, _lifetime.Token)).Operation
                        ?? throw new InvalidDataException("Operazione remota non trovata.");
            agent.ApplyProgress(operation);
            if (ReferenceEquals(agent, SelectedAgent)) LoadSelectedResults();
            if (!AgentOperationStates.IsTerminal(operation.State))
                await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.Token);
        } while (!AgentOperationStates.IsTerminal(operation.State));

        if (operation.ScanResult is not null)
            agent.SetScanResults(operation.Id, operation.ScanResult);
        if (operation.UpdateResult is not null)
            agent.ApplyUpdateResult(operation.UpdateResult);
        agent.ConnectionState = operation.State == AgentOperationStates.Completed ? "Raggiungibile" : "Attenzione";
        agent.OperationStatus = operation.Message;
        StatusText = operation.Message;
        if (ReferenceEquals(agent, SelectedAgent)) LoadSelectedResults();
    }

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(LocalConnectionStateText));
        OnPropertyChanged(nameof(LocalConnectionDetailText));
        OnPropertyChanged(nameof(RemoteState));
        OnPropertyChanged(nameof(ScanSummary));
        OnPropertyChanged(nameof(ScanActionText));
        OnPropertyChanged(nameof(UpdateActionText));
        OnPropertyChanged(nameof(ConnectionRequestActionText));
        OnPropertyChanged(nameof(AssociationPanelTitle));
        OnPropertyChanged(nameof(AssociationPanelDescription));
        OnPropertyChanged(nameof(SelectedResultScope));
        foreach (var agent in Agents)
            agent.NotifyLanguageChanged();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void AddAgent(NetworkAgentItem item)
    {
        item.PropertyChanged += AgentOnPropertyChanged;
        Agents.Add(item);
    }

    private void AgentOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkAgentItem.IsSelected) or nameof(NetworkAgentItem.Updates) or
            nameof(NetworkAgentItem.HasSelectedUpdates) or
            nameof(NetworkAgentItem.ConnectionRequestsEnabled) or nameof(NetworkAgentItem.ConnectionRequestInProgress) or
            nameof(NetworkAgentItem.ActiveOperationId) or
            nameof(NetworkAgentItem.IsPaired) or nameof(NetworkAgentItem.ScanOperationId))
            OnActionStateChanged();
        if (ReferenceEquals(sender, SelectedAgent) && e.PropertyName == nameof(NetworkAgentItem.IsPaired))
        {
            OnPropertyChanged(nameof(AssociationPanelTitle));
            OnPropertyChanged(nameof(AssociationPanelDescription));
        }
        if (e.PropertyName == nameof(NetworkAgentItem.Updates))
            LoadSelectedResults();
    }

    private void LoadSelectedResults()
    {
        var previousScopeId = SelectedResultScope?.AgentId ?? Guid.Empty;
        ScanResults.Clear();
        foreach (var agent in Agents.Where(x => x.ScanOperationId != Guid.Empty))
            foreach (var update in agent.Updates)
                ScanResults.Add(update);

        var scopedAgents = Agents.Where(x => x.ScanOperationId != Guid.Empty)
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var desiredIds = scopedAgents.Select(x => x.AgentId).ToHashSet();
        foreach (var obsolete in ResultScopes.Where(x => !x.IsAll && !desiredIds.Contains(x.AgentId)).ToList())
            ResultScopes.Remove(obsolete);
        foreach (var agent in scopedAgents.Where(agent => ResultScopes.All(x => x.AgentId != agent.AgentId)))
            ResultScopes.Add(new ResultScopeOption(agent.AgentId, agent.DisplayName));
        _selectedResultScope = ResultScopes.FirstOrDefault(x => x.AgentId == previousScopeId) ?? ResultScopes[0];
        OnPropertyChanged(nameof(SelectedResultScope));
        MainViewModel.TryRefreshCollectionView(ResultsView, "network-results");
        UpdateResultSummary();
        OnActionStateChanged();
    }

    private bool FilterResult(object value)
    {
        if (value is not RemoteUpdateSelectionItem item) return false;
        if (SelectedResultScope is { IsAll: false } scope && item.AgentId != scope.AgentId) return false;
        return true;
    }

    private void UpdateResultSummary()
    {
        var visible = ResultsView.Cast<RemoteUpdateSelectionItem>().ToList();
        ResultCount = visible.Count;
        var scopedAgents = SelectedResultScope is { IsAll: false } scope
            ? Agents.Where(x => x.AgentId == scope.AgentId).ToList()
            : Agents.Where(x => x.ScanOperationId != Guid.Empty).ToList();
        WarningCount = scopedAgents.Sum(x => x.WarningCount);
        if (scopedAgents.Count == 0)
        {
            ScanSummary = "Nessuna scansione remota disponibile.";
            return;
        }
        ScanSummary = $"{ResultCount} aggiornamenti · {scopedAgents.Count} PC · " +
                      $"{scopedAgents.Sum(x => x.InstalledDriverCount)} driver · " +
                      $"{scopedAgents.Sum(x => x.RuntimeCheckCount)} runtime · {WarningCount} avvisi";
    }

    private IReadOnlyList<NetworkAgentItem> GetScanTargets()
    {
        return Agents.Where(x => x.IsSelected && x.IsPaired).ToList();
    }

    private IReadOnlyList<RemoteUpdateSelectionItem> GetVisibleSelectedUpdates() =>
        ResultsView.Cast<RemoteUpdateSelectionItem>()
            .Where(x => x.IsSelected && x.CanInstall)
            .ToList();

    private IReadOnlyList<NetworkAgentItem> GetConnectionRequestTargets()
    {
        return Agents.Where(x => x.IsSelected && x.CanRequestConnection).ToList();
    }

    private void LoadSavedAgents()
    {
        var localAddresses = LanDiscoveryClient.GetLocalIPv4Addresses();
        foreach (var record in _identityStore.LoadAgents())
        {
            if (System.Net.IPAddress.TryParse(record.Address, out var address) &&
                (System.Net.IPAddress.IsLoopback(address) || localAddresses.Contains(address)))
                continue;
            var item = new NetworkAgentItem();
            item.Apply(record);
            AddAgent(item);
            ReindexAgent(item, Guid.Empty, "");
        }
    }

    private void MergeDiscoveredAgents(IReadOnlyList<DiscoveredAgent> discovered)
    {
        var saved = _identityStore.LoadAgents().ToDictionary(x => x.AgentId);
        foreach (var device in discovered)
        {
            saved.TryGetValue(device.AgentId, out var record);
            if (record is not null &&
                !record.CertificateSha256.Equals(device.CertificateSha256, StringComparison.OrdinalIgnoreCase))
            {
                _identityStore.RemoveAgent(device.AgentId, device.Address);
                record = null;
            }
            if (record is not null && !device.HasController)
            {
                _identityStore.RemoveAgent(device.AgentId, device.Address);
                record = null;
            }
            if (record is not null && (!record.Address.Equals(device.Address, StringComparison.OrdinalIgnoreCase) ||
                                       record.ApiPort != device.ApiPort))
            {
                record = new PairedAgentRecord
                {
                    AgentId = record.AgentId,
                    DisplayName = device.DisplayName,
                    Address = device.Address,
                    ApiPort = device.ApiPort,
                    CertificateSha256 = record.CertificateSha256,
                    PairedUtc = record.PairedUtc
                };
                _identityStore.SaveAgent(record);
            }

            var item = FindAgent(device.AgentId, device.Address);
            var isNewDevice = item is null;
            if (item is null)
            {
                item = new NetworkAgentItem();
                AddAgent(item);
            }
            var previousId = item.AgentId;
            var previousAddress = item.Address;
            item.Apply(device, record is not null);
            if (isNewDevice && record is null && !device.HasController)
                item.IsSelected = true;
            if (record is null && item.Updates.Count > 0)
                item.MarkUnpaired(device.HasController);
            ReindexAgent(item, previousId, previousAddress);
        }
        SortAgents();
    }

    private async Task HandleAuthorizationRevokedAsync(NetworkAgentItem agent)
    {
        DiscoveredAgent? current = null;
        try
        {
            current = await _discoveryClient.ProbeAddressAsync(
                agent.Address,
                agent.ApiPort,
                _lifetime.Token);
        }
        catch
        {
            // La risposta Unauthorized è già sufficiente per invalidare l'autorizzazione locale.
        }

        var sameDevice = current is not null && current.AgentId == agent.AgentId;
        if (sameDevice) agent.Apply(current!, isPaired: false);
        ClearLocalAuthorization(agent, sameDevice && current!.HasController);
        LoadSelectedResults();
    }

    private void ClearLocalAuthorization(NetworkAgentItem? agent, bool hasAnotherController)
    {
        if (agent is null) return;
        _identityStore.RemoveAgent(agent.AgentId, agent.Address);
        agent.MarkUnpaired(hasAnotherController);
        RemoteState = hasAnotherController
            ? "Il dispositivo è collegato a un altro PC principale."
            : "Autorizzazione rimossa; il dispositivo può essere associato nuovamente.";
        OnActionStateChanged();
    }

    private NetworkAgentItem? FindAgent(Guid agentId, string address)
    {
        if (agentId != Guid.Empty && _agentsById.TryGetValue(agentId, out var byId)) return byId;
        return _agentsByAddress.TryGetValue(address, out var byAddress) ? byAddress : null;
    }

    private void ReindexAgent(NetworkAgentItem item, Guid previousId, string previousAddress)
    {
        if (previousId != Guid.Empty && previousId != item.AgentId &&
            _agentsById.TryGetValue(previousId, out var previousById) && ReferenceEquals(previousById, item))
            _agentsById.Remove(previousId);
        if (!string.IsNullOrWhiteSpace(previousAddress) &&
            !previousAddress.Equals(item.Address, StringComparison.OrdinalIgnoreCase) &&
            _agentsByAddress.TryGetValue(previousAddress, out var previousByAddress) &&
            ReferenceEquals(previousByAddress, item))
            _agentsByAddress.Remove(previousAddress);
        if (item.AgentId != Guid.Empty) _agentsById[item.AgentId] = item;
        if (!string.IsNullOrWhiteSpace(item.Address)) _agentsByAddress[item.Address] = item;
    }

    private void SortAgents()
    {
        var selectedId = SelectedAgent?.AgentId;
        var ordered = Agents.OrderByDescending(x => x.IsPaired)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        Agents.Clear();
        foreach (var item in ordered) Agents.Add(item);
        if (selectedId is Guid id) SelectedAgent = Agents.FirstOrDefault(x => x.AgentId == id);
    }

    private bool IsEndpointValid() => !string.IsNullOrWhiteSpace(Address) && TryGetPort(out _);
    private bool TryGetPort(out int port) => int.TryParse(Port, out port) && port is > 0 and <= 65535;

    private static string FriendlyMessage(Exception exception)
        => UserMessageFormatter.FromException(exception);

    private void OnActionStateChanged()
    {
        OnPropertyChanged(nameof(CanDiscover));
        OnPropertyChanged(nameof(CanRequestConnections));
        OnPropertyChanged(nameof(ConnectionRequestTargetCount));
        OnPropertyChanged(nameof(ConnectionRequestActionText));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanContact));
        OnPropertyChanged(nameof(CanScanSelected));
        OnPropertyChanged(nameof(CanUpdateCurrent));
        OnPropertyChanged(nameof(CanUpdateSelected));
        OnPropertyChanged(nameof(CanCancelCurrent));
        OnPropertyChanged(nameof(CanScanTargets));
        OnPropertyChanged(nameof(SelectedComputerCount));
        OnPropertyChanged(nameof(ScanActionText));
        OnPropertyChanged(nameof(SelectedUpdateCount));
        OnPropertyChanged(nameof(UpdateComputerCount));
        OnPropertyChanged(nameof(UpdateActionText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class NetworkAgentItem : INotifyPropertyChanged
{
    private Guid _agentId;
    private string _displayName = "PC sconosciuto";
    private string _machineName = "";
    private string _address = "";
    private int _apiPort = 47382;
    private int _protocolMinor;
    private string _agentVersion = "";
    private bool _hasController;
    private bool _isPaired;
    private bool _isSelected;
    private string _connectionState = "Non verificato";
    private string _lastScanText = "Mai";
    private string _operationStatus = "Nessuna operazione";
    private double _progress;
    private Guid _activeOperationId;
    private Guid _scanOperationId;
    private int _warningCount;
    private int _installedDriverCount;
    private int _runtimeCheckCount;
    private bool _hasBattery;
    private bool _isOnBattery;
    private int _batteryPercentage = -1;
    private long _systemDriveFreeBytes;
    private bool _connectionRequestsEnabled;
    private DateTime _connectionRequestsExpiresUtc;
    private bool _connectionRequestInProgress;
    private string _connectionRequestStatus = "";

    public Guid AgentId { get => _agentId; private set => Set(ref _agentId, value); }
    public string DisplayName { get => _displayName; private set => Set(ref _displayName, value); }
    public string MachineName { get => _machineName; private set => Set(ref _machineName, value); }
    public string Address { get => _address; private set { Set(ref _address, value); OnPropertyChanged(nameof(Endpoint)); } }
    public int ApiPort { get => _apiPort; private set { Set(ref _apiPort, value); OnPropertyChanged(nameof(Endpoint)); } }
    public int ProtocolMinor { get => _protocolMinor; private set => Set(ref _protocolMinor, value); }
    public string AgentVersion { get => _agentVersion; private set => Set(ref _agentVersion, value); }
    public bool HasController { get => _hasController; private set { Set(ref _hasController, value); OnPropertyChanged(nameof(AssociationText)); } }
    public bool IsPaired { get => _isPaired; private set { Set(ref _isPaired, value); OnPropertyChanged(nameof(AssociationText)); } }
    public bool ConnectionRequestsEnabled
    {
        get => _connectionRequestsEnabled;
        private set
        {
            Set(ref _connectionRequestsEnabled, value);
            OnPropertyChanged(nameof(CanRequestConnection));
            OnPropertyChanged(nameof(CanStartConnectionAction));
            OnPropertyChanged(nameof(ConnectionActionText));
            OnPropertyChanged(nameof(AssociationText));
        }
    }
    public DateTime ConnectionRequestsExpiresUtc { get => _connectionRequestsExpiresUtc; private set => Set(ref _connectionRequestsExpiresUtc, value); }
    public bool ConnectionRequestInProgress
    {
        get => _connectionRequestInProgress;
        set
        {
            Set(ref _connectionRequestInProgress, value);
            OnPropertyChanged(nameof(CanRequestConnection));
            OnPropertyChanged(nameof(ConnectionActionText));
        }
    }
    public string ConnectionRequestStatus
    {
        get => _connectionRequestStatus;
        set
        {
            Set(ref _connectionRequestStatus, value);
            OnPropertyChanged(nameof(AssociationText));
        }
    }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public string ConnectionState { get => LocalizationService.Translate(_connectionState); set => Set(ref _connectionState, value); }
    public string LastScanText { get => _lastScanText; set => Set(ref _lastScanText, value); }
    public string OperationStatus { get => LocalizationService.Translate(_operationStatus); set => Set(ref _operationStatus, value); }
    public double Progress { get => _progress; set => Set(ref _progress, Math.Clamp(value, 0, 100)); }
    public Guid ActiveOperationId
    {
        get => _activeOperationId;
        set
        {
            if (_activeOperationId == value) return;
            _activeOperationId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOperationActive));
        }
    }
    public Guid ScanOperationId { get => _scanOperationId; private set => Set(ref _scanOperationId, value); }
    public int WarningCount { get => _warningCount; private set => Set(ref _warningCount, value); }
    public int InstalledDriverCount { get => _installedDriverCount; private set => Set(ref _installedDriverCount, value); }
    public int RuntimeCheckCount { get => _runtimeCheckCount; private set => Set(ref _runtimeCheckCount, value); }
    public bool HasBattery { get => _hasBattery; private set => Set(ref _hasBattery, value); }
    public bool IsOnBattery { get => _isOnBattery; private set => Set(ref _isOnBattery, value); }
    public int BatteryPercentage { get => _batteryPercentage; private set => Set(ref _batteryPercentage, value); }
    public long SystemDriveFreeBytes { get => _systemDriveFreeBytes; private set => Set(ref _systemDriveFreeBytes, value); }
    public ObservableCollection<RemoteUpdateSelectionItem> Updates { get; } = [];
    public bool HasSelectedUpdates => Updates.Any(x => x.IsSelected && x.CanInstall);
    public bool IsOperationActive => ActiveOperationId != Guid.Empty;
    public string Endpoint => $"{Address}:{ApiPort}";
    public bool CanRequestConnection => !IsPaired && !HasController && ConnectionRequestsEnabled && !ConnectionRequestInProgress;
    public bool CanStartConnectionAction => !IsPaired && !HasController && !ConnectionRequestInProgress;
    public string ConnectionActionText => LocalizationService.Translate(ConnectionRequestInProgress ? "In attesa" : ConnectionRequestsEnabled ? "Richiedi" : "Codice");
    public string AssociationText => LocalizationService.Translate(IsPaired
        ? "Autorizzato"
        : HasController
            ? "Collegato a un altro PC"
            : !string.IsNullOrWhiteSpace(ConnectionRequestStatus)
                ? ConnectionRequestStatus
                : ConnectionRequestsEnabled ? "Pronto a collegarsi" : "Non autorizzato");

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(ConnectionState));
        OnPropertyChanged(nameof(OperationStatus));
        OnPropertyChanged(nameof(ConnectionActionText));
        OnPropertyChanged(nameof(AssociationText));
        foreach (var update in Updates)
            update.NotifyLanguageChanged();
    }

    public void Apply(DiscoveredAgent agent, bool isPaired)
    {
        AgentId = agent.AgentId;
        DisplayName = string.IsNullOrWhiteSpace(agent.DisplayName) ? agent.MachineName : agent.DisplayName;
        MachineName = agent.MachineName;
        Address = agent.Address;
        ApiPort = agent.ApiPort;
        ProtocolMinor = agent.ProtocolMinor;
        AgentVersion = agent.AgentVersion;
        HasController = agent.HasController;
        ConnectionRequestsEnabled = agent.ConnectionRequestsEnabled;
        ConnectionRequestsExpiresUtc = agent.ConnectionRequestsExpiresUtc;
        IsPaired = isPaired;
        if (isPaired)
            ConnectionRequestStatus = "";
        ConnectionState = "Rilevato";
    }

    public void Apply(PairedAgentRecord record)
    {
        AgentId = record.AgentId;
        DisplayName = string.IsNullOrWhiteSpace(record.DisplayName) ? record.Address : record.DisplayName;
        Address = record.Address;
        ApiPort = record.ApiPort;
        HasController = true;
        ConnectionRequestsEnabled = false;
        IsPaired = true;
        ConnectionRequestStatus = "";
        ConnectionState = "Salvato";
    }

    public void Apply(AgentStatus status)
    {
        ConnectionRequestStatus = "";
        MachineName = status.MachineName;
        if (!string.IsNullOrWhiteSpace(status.MachineName)) DisplayName = status.MachineName;
        AgentVersion = status.AgentVersion;
        ProtocolMinor = status.ProtocolMinor;
        ConnectionState = status.OperationInProgress ? "Operazione attiva" : "Raggiungibile";
        OperationStatus = status.OperationInProgress
            ? string.IsNullOrWhiteSpace(status.ActiveOperationKind)
                ? "Operazione in corso"
                : OperationKindText(status.ActiveOperationKind)
            : "Pronto";
        ActiveOperationId = status.ActiveOperationId;
    }

    public void MarkUnpaired(bool hasController)
    {
        IsPaired = false;
        HasController = hasController;
        ConnectionRequestStatus = "";
        if (hasController) ConnectionRequestsEnabled = false;
        ConnectionState = hasController ? "Collegato a un altro PC" : "Non autorizzato";
        OperationStatus = "Collegamento revocato";
        ActiveOperationId = Guid.Empty;
        foreach (var update in Updates) update.MarkDeviceDisconnected();
        OnPropertyChanged(nameof(Updates));
    }

    public void SetScanResults(Guid operationId, ScanResult result)
    {
        foreach (var item in Updates) item.PropertyChanged -= UpdateOnPropertyChanged;
        Updates.Clear();
        var softwareCount = result.Updates.Count(x => x.Kind.Equals("Software", StringComparison.OrdinalIgnoreCase));
        var driverCount = result.Updates.Count(x => x.Kind.Equals("Driver", StringComparison.OrdinalIgnoreCase));
        var runtimeCount = result.Updates.Count(x => x.Kind.Equals("Runtime", StringComparison.OrdinalIgnoreCase));
        var deviceGroupLabel = $"{DisplayName} · {result.Updates.Count} aggiornamenti · " +
                               $"{softwareCount} software · {driverCount} driver · {runtimeCount} runtime";
        foreach (var update in result.Updates)
        {
            var item = new RemoteUpdateSelectionItem(update, AgentId, DisplayName, deviceGroupLabel);
            item.PropertyChanged += UpdateOnPropertyChanged;
            Updates.Add(item);
        }
        ScanOperationId = operationId;
        WarningCount = result.Warnings.Count;
        InstalledDriverCount = result.InstalledDriverCount;
        RuntimeCheckCount = result.RuntimeCheckCount;
        HasBattery = result.HasBattery;
        IsOnBattery = result.IsOnBattery;
        BatteryPercentage = result.BatteryPercentage;
        SystemDriveFreeBytes = result.SystemDriveFreeBytes;
        LastScanText = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        OnPropertyChanged(nameof(Updates));
    }

    public void ApplyProgress(AgentOperation operation)
    {
        ActiveOperationId = AgentOperationStates.IsTerminal(operation.State) ? Guid.Empty : operation.Id;
        var completed = operation.Total <= 0 ? 0 : operation.CurrentIndex * 100d / operation.Total;
        var current = operation.Total <= 0 ? 0 : operation.CurrentItemProgress / operation.Total;
        Progress = AgentOperationStates.IsTerminal(operation.State) ? 100 : Math.Min(99, completed + current);
        OperationStatus = string.IsNullOrWhiteSpace(operation.Message)
            ? OperationStateText(operation.State)
            : operation.Message;
    }

    public static string OperationKindText(string value) => value switch
    {
        "Scan" => "scansione",
        "Update" or "Install" => "aggiornamento",
        _ => string.IsNullOrWhiteSpace(value) ? "operazione" : value
    };

    public static string OperationStateText(string value) => value switch
    {
        AgentOperationStates.Queued => "In coda",
        AgentOperationStates.Running => "In corso",
        AgentOperationStates.Completed => "Completata",
        AgentOperationStates.CompletedWithWarnings => "Completata con avvisi",
        AgentOperationStates.Cancelled => "Annullata",
        AgentOperationStates.Failed => "Non riuscita",
        _ => string.IsNullOrWhiteSpace(value) ? "Stato non disponibile" : value
    };

    public void ApplyUpdateResult(RemoteUpdateResult? result)
    {
        if (result is null) return;
        foreach (var outcome in result.Results)
        {
            var item = Updates.FirstOrDefault(x => x.Id.Equals(outcome.Id, StringComparison.OrdinalIgnoreCase) &&
                                                   x.Kind.Equals(outcome.Kind, StringComparison.OrdinalIgnoreCase));
            if (item is null) continue;
            item.Status = outcome.Success ? "Aggiornato" : "Errore";
            item.ResultMessage = outcome.Message;
            item.IsSelected = false;
        }
        OnPropertyChanged(nameof(Updates));
    }

    private void UpdateOnPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(HasSelectedUpdates));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record ResultScopeOption(Guid AgentId, string Label)
{
    public static ResultScopeOption All { get; } = new(Guid.Empty, "Tutti i dispositivi");
    public bool IsAll => AgentId == Guid.Empty;
    public override string ToString() => Label;
}

public sealed class RemoteUpdateSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _riskConfirmed;
    private string _status = "Da aggiornare";
    private string _resultMessage = "";
    private bool _deviceAuthorized = true;

    public RemoteUpdateSelectionItem(
        RemoteUpdateItem source,
        Guid agentId,
        string deviceName,
        string? deviceGroupLabel = null)
    {
        SourceItem = source;
        AgentId = agentId;
        DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "PC sconosciuto" : deviceName;
        DeviceGroupLabel = string.IsNullOrWhiteSpace(deviceGroupLabel) ? DeviceName : deviceGroupLabel;
        _isSelected = source.CanInstall && !source.RequiresRiskConfirmation;
        _status = string.IsNullOrWhiteSpace(source.Status) ? "Da aggiornare" : source.Status;
        _resultMessage = source.ResultDetails;
    }

    public RemoteUpdateItem SourceItem { get; }
    public Guid AgentId { get; }
    public string DeviceName { get; }
    public string DeviceGroupLabel { get; }
    public string Id => SourceItem.Id;
    public string Name => SourceItem.Name;
    public string Kind => SourceItem.Kind;
    public int TypeOrder => Kind.Equals("Driver", StringComparison.OrdinalIgnoreCase) ? 0
        : Kind.Equals("Software", StringComparison.OrdinalIgnoreCase) ? 1
        : Kind.Equals("Runtime", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    public string InstalledVersion => SourceItem.InstalledVersion;
    public string AvailableVersion => SourceItem.AvailableVersion;
    public string Source => SourceItem.Source;
    public string KindLabel => Kind;
    public bool IsImportant => SourceItem.IsImportant;
    public bool IsOptional => SourceItem.IsOptional;
    public bool RequiresRestart => SourceItem.RequiresRestart;
    public long DownloadSizeBytes => SourceItem.DownloadSizeBytes;
    public string DownloadSizeLabel => DownloadSizeBytes > 0
        ? PreflightService.FormatBytes(DownloadSizeBytes)
        : "Non dichiarata";
    public string PriorityLabel => LocalizationService.Translate(RequiresRiskConfirmation
        ? "Conferma"
        : SourceItem.HasUnverifiedInstallerMetadata
            ? "Verifica"
            : !CanInstall
                ? "Solo verifica"
                : IsImportant
                    ? "Importante"
                    : IsOptional ? "Facoltativo" : "Standard");
    public string PriorityDescription => RequiresRiskConfirmation
        ? "L'installer può rimuovere la versione funzionante prima di installare quella nuova."
        : IsImportant
            ? "Aggiornamento obbligatorio o di sicurezza secondo la fonte ufficiale."
            : IsOptional
                ? "Aggiornamento facoltativo secondo la fonte ufficiale."
                : "Aggiornamento standard.";
    public string RestartLabel => RequiresRestart ? "Sì" : "No";
    public bool CanInstall => SourceItem.CanInstall && _deviceAuthorized;
    public bool RequiresRiskConfirmation => SourceItem.RequiresRiskConfirmation;
    public string RiskLabel => RequiresRiskConfirmation ? LocalizationService.Translate("Richiesta") : "—";
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var allowed = value && CanInstall;
            if (_isSelected == allowed) return;
            _isSelected = allowed;
            OnPropertyChanged();
        }
    }
    public bool RiskConfirmed
    {
        get => _riskConfirmed;
        set { if (_riskConfirmed == value) return; _riskConfirmed = value; OnPropertyChanged(); }
    }
    public string Status { get => LocalizationService.Translate(_status); set { if (_status == value) return; _status = value; OnPropertyChanged(); } }
    public string ResultMessage { get => _resultMessage; set { if (_resultMessage == value) return; _resultMessage = value; OnPropertyChanged(); } }

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(PriorityLabel));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(RiskLabel));
    }

    public void MarkDeviceDisconnected()
    {
        if (!_deviceAuthorized) return;
        _deviceAuthorized = false;
        IsSelected = false;
        Status = "Dispositivo scollegato";
        ResultMessage = "Collegalo nuovamente ed esegui una nuova scansione.";
        OnPropertyChanged(nameof(CanInstall));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    public event PropertyChangedEventHandler? PropertyChanged;
}
