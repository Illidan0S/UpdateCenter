using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using UpdateCenter.Contracts;
using UpdateCenter.Core;
using UpdateCenter.Services;

namespace UpdateCenter.ViewModels;

public sealed class LocalAgentSetupViewModel : INotifyPropertyChanged
{
    private readonly AgentLocalClient _client = new();
    private bool _isBusy;
    private bool _agentAvailable;
    private bool _networkEnabled;
    private bool _networkScopeActive;
    private bool _hasController;
    private string _controllerName = "Nessuno";
    private string _statusText = "Controllo della configurazione locale...";
    private string _pairingCode = "—";
    private string _pairingExpiry = "Nessun codice attivo";
    private string _agentId = "—";
    private string _networkScopeName = "Nessuna rete configurata";
    private string _allowedSubnets = "—";
    private bool _connectionRequestsEnabled;
    private DateTime _connectionRequestsExpiresUtc;
    private int _pendingConnectionRequestCount;

    public bool IsAdministrator { get; } = IsCurrentProcessAdministrator();
    public bool SetupFilesAvailable => File.Exists(SetupScriptPath);
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            NotifyActions();
        }
    }
    public bool AgentAvailable
    {
        get => _agentAvailable;
        private set { _agentAvailable = value; OnPropertyChanged(); NotifyActions(); }
    }
    public bool NetworkEnabled
    {
        get => _networkEnabled;
        private set { _networkEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(NetworkStateText)); OnPropertyChanged(nameof(ConnectionStatusText)); NotifyActions(); }
    }
    public bool HasController
    {
        get => _hasController;
        private set { _hasController = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionStatusText)); NotifyActions(); }
    }
    public bool NetworkScopeActive
    {
        get => _networkScopeActive;
        private set { _networkScopeActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(NetworkStateText)); NotifyActions(); }
    }
    public string ControllerName
    {
        get => LocalizationService.Translate(_controllerName);
        private set { _controllerName = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionStatusText)); }
    }
    public string StatusText
    {
        get => LocalizationService.Translate(_statusText);
        private set { _statusText = value; OnPropertyChanged(); }
    }
    public string PairingCode
    {
        get => _pairingCode;
        private set { _pairingCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCopyCode)); }
    }
    public string PairingExpiry
    {
        get => LocalizationService.Translate(_pairingExpiry);
        private set { _pairingExpiry = value; OnPropertyChanged(); }
    }
    public string AgentId
    {
        get => _agentId;
        private set { _agentId = value; OnPropertyChanged(); }
    }
    public string NetworkScopeName
    {
        get => LocalizationService.Translate(_networkScopeName);
        private set { _networkScopeName = value; OnPropertyChanged(); }
    }
    public string AllowedSubnets
    {
        get => _allowedSubnets;
        private set { _allowedSubnets = value; OnPropertyChanged(); }
    }
    public bool ConnectionRequestsEnabled
    {
        get => _connectionRequestsEnabled;
        private set
        {
            _connectionRequestsEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionRequestStatusText));
            NotifyActions();
        }
    }
    public DateTime ConnectionRequestsExpiresUtc
    {
        get => _connectionRequestsExpiresUtc;
        private set
        {
            _connectionRequestsExpiresUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionRequestStatusText));
        }
    }
    public int PendingConnectionRequestCount
    {
        get => _pendingConnectionRequestCount;
        private set
        {
            _pendingConnectionRequestCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionRequestStatusText));
        }
    }
    public string NetworkStateText => LocalizationService.Translate(!NetworkEnabled
        ? "Disabilitata"
        : NetworkScopeActive
            ? "Attiva sulla rete corrente"
            : "In pausa: il PC non è sulla rete autorizzata");
    public string ConnectionStatusText => HasController
        ? LocalizationService.IsEnglish ? $"Connected to {ControllerName}" : $"Connesso a {ControllerName}"
        : LocalizationService.Translate(NetworkEnabled ? "Non collegato a un PC principale" : "Gestione remota disabilitata");
    public string ConnectionRequestStatusText => HasController
        ? LocalizationService.IsEnglish ? $"This PC is already connected to {ControllerName}." : $"Questo PC è già collegato a {ControllerName}."
        : ConnectionRequestsEnabled
            ? LocalizationService.Text("Richieste di collegamento abilitate", "Connection requests enabled") +
              (PendingConnectionRequestCount > 0 ? LocalizationService.IsEnglish ? $" · {PendingConnectionRequestCount} pending" : $" · {PendingConnectionRequestCount} in attesa" : "")
            : LocalizationService.Translate("Richieste automatiche disabilitate");
    public bool CanSetup => !IsBusy && IsAdministrator && SetupFilesAvailable;
    public bool CanRefresh => !IsBusy;
    public bool CanGenerateCode => !IsBusy && IsAdministrator && AgentAvailable && NetworkEnabled && NetworkScopeActive && !HasController;
    public bool CanEnableConnectionRequests => !IsBusy && IsAdministrator && AgentAvailable && NetworkEnabled &&
                                               NetworkScopeActive && !HasController && !ConnectionRequestsEnabled;
    public bool CanDisableConnectionRequests => !IsBusy && IsAdministrator && AgentAvailable && ConnectionRequestsEnabled;
    public bool CanCopyCode => PairingCode.Length == 8 && PairingCode.All(char.IsDigit);
    public bool CanDisable => !IsBusy && IsAdministrator && AgentAvailable && NetworkEnabled && File.Exists(DisableScriptPath);
    public bool CanRevoke => !IsBusy && IsAdministrator && AgentAvailable && HasController;
    public bool CanUninstall => !IsBusy && IsAdministrator && File.Exists(UninstallScriptPath) &&
                                (AgentAvailable || HasInstalledAgentFiles());

    public async Task RefreshAsync()
    {
        if (!CanRefresh) return;
        IsBusy = true;
        StatusText = "Lettura della configurazione del componente di rete...";
        try
        {
            var response = await SendAsync(AgentCommands.GetNetworkConfiguration, TimeSpan.FromSeconds(2));
            Apply(response.Network ?? throw new InvalidDataException("Configurazione del componente mancante."));
            StatusText = NetworkEnabled
                ? NetworkScopeActive
                    ? "Il PC è gestibile esclusivamente dalla rete locale autorizzata."
                    : "Gestione sospesa automaticamente perché la rete corrente è diversa da quella autorizzata."
                : "Il componente è installato, ma la gestione remota è disabilitata.";
        }
        catch (UnauthorizedAccessException)
        {
            AgentAvailable = true;
            StatusText = "Il componente è presente, ma questa finestra non dispone dei privilegi amministrativi.";
        }
        catch (Exception ex)
        {
            AgentAvailable = false;
            NetworkEnabled = false;
            NetworkScopeActive = false;
            HasController = false;
            ControllerName = "Nessuno";
            ConnectionRequestsEnabled = false;
            ConnectionRequestsExpiresUtc = default;
            PendingConnectionRequestCount = 0;
            AgentId = "—";
            NetworkScopeName = "Nessuna rete configurata";
            AllowedSubnets = "—";
            StatusText = $"Componente di rete non raggiungibile: {FriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetupAsync()
    {
        if (!CanSetup) return;
        IsBusy = true;
        StatusText = "Installazione e abilitazione del componente di rete...";
        try
        {
            await RunScriptAsync(SetupScriptPath);
            await Task.Delay(1000);
            StatusText = "Componente configurato. Lettura dello stato...";
        }
        catch (Exception ex)
        {
            StatusText = $"Configurazione non riuscita: {FriendlyMessage(ex)}";
            IsBusy = false;
            return;
        }
        IsBusy = false;
        await RefreshAsync();
    }

    public async Task GeneratePairingCodeAsync()
    {
        if (!CanGenerateCode) return;
        IsBusy = true;
        StatusText = "Generazione del codice temporaneo...";
        try
        {
            var info = (await SendAsync(AgentCommands.CreatePairingCode)).PairingCode
                       ?? throw new InvalidDataException("Codice di associazione mancante.");
            PairingCode = info.Code;
            PairingExpiry = $"Valido fino alle {info.ExpiresUtc.ToLocalTime():HH:mm:ss}; monouso";
            StatusText = "Inserisci questo codice sul PC principale entro 5 minuti.";
        }
        catch (Exception ex)
        {
            StatusText = $"Codice non generato: {FriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task EnableConnectionRequestsAsync()
    {
        if (!CanEnableConnectionRequests) return;
        IsBusy = true;
        StatusText = "Abilitazione delle richieste di collegamento...";
        try
        {
            var response = await SendAsync(AgentCommands.EnableConnectionRequests);
            Apply(response.Network ?? throw new InvalidDataException("Configurazione del componente mancante."));
            StatusText = "Questo PC è rilevabile e può ricevere richieste di collegamento. Ogni richiesta deve essere approvata qui.";
        }
        catch (Exception ex)
        {
            StatusText = $"Richieste non abilitate: {FriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisableConnectionRequestsAsync()
    {
        if (!CanDisableConnectionRequests) return;
        IsBusy = true;
        StatusText = "Disabilitazione delle richieste di collegamento...";
        try
        {
            var response = await SendAsync(AgentCommands.DisableConnectionRequests);
            Apply(response.Network ?? throw new InvalidDataException("Configurazione del componente mancante."));
            StatusText = "Questo PC non accetta più nuove richieste di collegamento.";
        }
        catch (Exception ex)
        {
            StatusText = $"Richieste non disabilitate: {FriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisableAsync()
    {
        if (!CanDisable) return;
        IsBusy = true;
        StatusText = "Disabilitazione della gestione di rete...";
        try
        {
            await RunScriptAsync(DisableScriptPath);
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            StatusText = $"Disabilitazione non riuscita: {FriendlyMessage(ex)}";
            IsBusy = false;
            return;
        }
        IsBusy = false;
        await RefreshAsync();
    }

    public async Task RevokeControllerAsync()
    {
        if (!CanRevoke) return;
        IsBusy = true;
        StatusText = "Revoca del PC principale...";
        try
        {
            var response = await SendAsync(AgentCommands.RevokeController);
            Apply(response.Network ?? throw new InvalidDataException("Configurazione del componente mancante."));
            PairingCode = "—";
            PairingExpiry = "Nessun codice attivo";
            StatusText = "PC principale revocato. Questo dispositivo non accetterà più i suoi comandi.";
        }
        catch (Exception ex)
        {
            StatusText = $"Revoca non riuscita: {FriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UninstallAsync()
    {
        if (!CanUninstall) return;
        IsBusy = true;
        StatusText = "Rimozione completa del componente di rete...";
        try
        {
            await RunScriptAsync(UninstallScriptPath);
            AgentAvailable = false;
            NetworkEnabled = false;
            NetworkScopeActive = false;
            HasController = false;
            ControllerName = "Nessuno";
            ConnectionRequestsEnabled = false;
            ConnectionRequestsExpiresUtc = default;
            PendingConnectionRequestCount = 0;
            AgentId = "—";
            NetworkScopeName = "Nessuna rete configurata";
            AllowedSubnets = "—";
            PairingCode = "—";
            PairingExpiry = "Nessun codice attivo";
            StatusText = "Componente di rete disinstallato. Update Center locale non è stato rimosso.";
        }
        catch (Exception ex)
        {
            StatusText = $"Disinstallazione non riuscita: {FriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<AgentResponse> SendAsync(string command, TimeSpan? timeout = null)
    {
        var response = await _client.SendAsync(
            new AgentRequest { Command = command }, timeout ?? TimeSpan.FromSeconds(15));
        if (!response.Success)
            throw new InvalidOperationException($"{response.ErrorCode}: {response.Message}");
        return response;
    }

    private void Apply(AgentNetworkConfiguration configuration)
    {
        AgentAvailable = true;
        NetworkEnabled = configuration.Enabled;
        NetworkScopeActive = configuration.NetworkScopeActive;
        HasController = configuration.HasController;
        ControllerName = configuration.HasController && !string.IsNullOrWhiteSpace(configuration.ControllerName)
            ? configuration.ControllerName
            : "Nessuno";
        AgentId = configuration.AgentId == Guid.Empty ? "—" : configuration.AgentId.ToString("D");
        NetworkScopeName = string.IsNullOrWhiteSpace(configuration.NetworkScopeName)
            ? "Nessuna rete configurata"
            : configuration.NetworkScopeName;
        AllowedSubnets = configuration.AllowedSubnets.Count == 0
            ? "—"
            : string.Join(", ", configuration.AllowedSubnets);
        ConnectionRequestsEnabled = configuration.ConnectionRequestsEnabled;
        ConnectionRequestsExpiresUtc = configuration.ConnectionRequestsExpiresUtc;
        PendingConnectionRequestCount = configuration.PendingConnectionRequestCount;
    }

    private static async Task RunScriptAsync(string scriptPath)
    {
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Script della preview non trovato.", scriptPath);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        if (!process.Start()) throw new InvalidOperationException("Impossibile avviare PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
    }

    private static bool IsCurrentProcessAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string FriendlyMessage(Exception exception)
        => UserMessageFormatter.FromException(exception);

    private static string SetupScriptPath => Path.Combine(AppContext.BaseDirectory, "setup-agent-preview.ps1");
    private static string DisableScriptPath => Path.Combine(AppContext.BaseDirectory, "disable-network-preview.ps1");
    private static string UninstallScriptPath => Path.Combine(AppContext.BaseDirectory, "uninstall-agent-preview.ps1");

    private static bool HasInstalledAgentFiles() => File.Exists(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Update Center Network",
        "UpdateCenter.Agent.exe"));

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanSetup));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanGenerateCode));
        OnPropertyChanged(nameof(CanEnableConnectionRequests));
        OnPropertyChanged(nameof(CanDisableConnectionRequests));
        OnPropertyChanged(nameof(CanDisable));
        OnPropertyChanged(nameof(CanRevoke));
        OnPropertyChanged(nameof(CanUninstall));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
