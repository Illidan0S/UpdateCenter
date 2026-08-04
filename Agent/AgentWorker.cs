using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting.WindowsServices;
using UpdateCenter.Contracts;
using UpdateCenter.Core;

namespace UpdateCenter.Agent;

public sealed class AgentWorker(
    ILogger<AgentWorker> logger,
    AgentOperationRegistry operations,
    SingleOperationGate operationGate,
    AgentOperationStore operationStore,
    AgentNetworkSettingsStore networkSettings,
    PairingCodeManager pairingCodes,
    ConnectionRequestManager connectionRequests) : BackgroundService
{
    private const int MaximumConcurrentLocalClients = 10;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly SemaphoreSlim _clientLimit = new(MaximumConcurrentLocalClients, MaximumConcurrentLocalClients);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _operationCancellation = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        operations.Restore(operationStore.Load());
        foreach (var restored in operations.Snapshot())
            await PersistAsync(restored, stoppingToken).ConfigureAwait(false);
        logger.LogInformation("Update Center Agent locale avviato. Listener di rete disabilitato.");
        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = LocalPipeFactory.CreateControlPipe(
                AgentProtocol.ControlPipeName,
                MaximumConcurrentLocalClients,
                WindowsServiceHelpers.IsWindowsService());
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await _clientLimit.WaitAsync(stoppingToken).ConfigureAwait(false);
                _ = HandleClientAsync(pipe, stoppingToken).ContinueWith(
                    task =>
                    {
                        _clientLimit.Release();
                        if (task.Exception is not null)
                            logger.LogError(task.Exception, "Richiesta locale non gestita.");
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            AgentResponse response;
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                var request = await PipeJsonProtocol.ReadAsync<AgentRequest>(pipe, requestTimeout.Token)
                    .ConfigureAwait(false);
                response = await DispatchAsync(request, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                response = AgentResponse.Error(Guid.Empty, "RequestTimeout", "La richiesta locale è scaduta.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Richiesta locale non valida.");
                response = AgentResponse.Error(Guid.Empty, "InvalidRequest", "La richiesta locale non è valida.");
            }

            try
            {
                await PipeJsonProtocol.WriteAsync(pipe, response, stoppingToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Il client può essersi disconnesso dopo aver inviato la richiesta.
            }
        }
    }

    private async Task<AgentResponse> DispatchAsync(AgentRequest request, CancellationToken stoppingToken)
    {
        if (request.ProtocolMajor != AgentProtocol.MajorVersion)
            return AgentResponse.Error(request.RequestId, "ProtocolMismatch", "Versione principale del protocollo non compatibile.");

        return request.Command switch
        {
            AgentCommands.GetStatus => BuildStatusResponse(request.RequestId),
            AgentCommands.StartScan => await StartScanAsync(request, stoppingToken).ConfigureAwait(false),
            AgentCommands.StartUpdate => await StartUpdateAsync(request, stoppingToken).ConfigureAwait(false),
            AgentCommands.GetOperation => GetOperation(request),
            AgentCommands.CancelOperation => CancelOperation(request),
            AgentCommands.GetNetworkConfiguration => NetworkConfiguration(request.RequestId),
            AgentCommands.EnableNetwork => EnableNetwork(request.RequestId),
            AgentCommands.DisableNetwork => DisableNetwork(request.RequestId),
            AgentCommands.CreatePairingCode => CreatePairingCode(request.RequestId),
            AgentCommands.RevokeController => RevokeController(request.RequestId),
            AgentCommands.EnableConnectionRequests => EnableConnectionRequests(request.RequestId),
            AgentCommands.DisableConnectionRequests => DisableConnectionRequests(request.RequestId),
            AgentCommands.GetPendingConnectionRequests => GetPendingConnectionRequests(request.RequestId),
            AgentCommands.RespondConnectionRequest => RespondConnectionRequest(request),
            _ => AgentResponse.Error(request.RequestId, "UnknownCommand", "Comando locale non riconosciuto.")
        };
    }

    private AgentResponse BuildStatusResponse(Guid requestId)
    {
        var configuration = networkSettings.GetConfiguration();
        var active = operations.Snapshot().FirstOrDefault(x => !AgentOperationStates.IsTerminal(x.State));
        return new AgentResponse
        {
            RequestId = requestId,
            Success = true,
            Status = new AgentStatus
            {
                AgentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
                MachineName = Environment.MachineName,
                OperatingSystem = RuntimeInformation.OSDescription,
                StartedUtc = _startedUtc,
                NetworkListenerEnabled = configuration.Enabled,
                OperationInProgress = operationGate.IsBusy,
                ActiveOperationId = active?.Id ?? Guid.Empty,
                ActiveOperationKind = active?.Kind ?? "",
                ControllerName = configuration.ControllerName,
                Capabilities = ["LocalStatus", "LocalScan", "CancelOperation", "LanDiscoveryV1", "RemoteScanV1", "RemoteUpdateV1", "ConnectionRequestV1"]
            }
        };
    }

    private AgentResponse NetworkConfiguration(Guid requestId) => new()
    {
        RequestId = requestId,
        Success = true,
        Network = networkSettings.GetConfiguration(pendingConnectionRequestCount: connectionRequests.PendingCount)
    };

    private AgentResponse EnableNetwork(Guid requestId)
    {
        try
        {
            return new AgentResponse
            {
                RequestId = requestId,
                Success = true,
                Message = "Gestione abilitata esclusivamente sulla rete locale corrente. Riavvia l'Agent per applicare la modifica.",
                Network = networkSettings.Enable()
            };
        }
        catch (InvalidOperationException ex)
        {
            return AgentResponse.Error(requestId, "NoActiveLocalNetwork", ex.Message);
        }
    }

    private AgentResponse DisableNetwork(Guid requestId)
    {
        pairingCodes.Clear();
        connectionRequests.ClearPending();
        return new AgentResponse
        {
            RequestId = requestId,
            Success = true,
            Message = "Gestione rete disabilitata. Riavvia l'Agent per chiudere i listener.",
            Network = networkSettings.Disable()
        };
    }

    private AgentResponse CreatePairingCode(Guid requestId)
    {
        var configuration = networkSettings.GetConfiguration();
        if (!configuration.Enabled)
            return AgentResponse.Error(requestId, "NetworkDisabled", "Abilita prima la gestione rete.");
        if (configuration.HasController)
            return AgentResponse.Error(requestId, "ControllerAlreadyPaired", "Revoca il Controller esistente prima di crearne uno nuovo.");
        return new AgentResponse
        {
            RequestId = requestId,
            Success = true,
            PairingCode = pairingCodes.Create(),
            Message = "Codice monouso creato; scade tra 5 minuti."
        };
    }

    private AgentResponse RevokeController(Guid requestId) => new()
    {
        RequestId = requestId,
        Success = true,
        Message = "Controller revocato.",
        Network = networkSettings.RevokeController()
    };

    private AgentResponse EnableConnectionRequests(Guid requestId)
    {
        try
        {
            return new AgentResponse
            {
                RequestId = requestId,
                Success = true,
                Message = "Richieste di collegamento abilitate.",
                Network = networkSettings.EnableConnectionRequests()
            };
        }
        catch (InvalidOperationException ex)
        {
            return AgentResponse.Error(requestId, "ConnectionRequestsUnavailable", ex.Message);
        }
    }

    private AgentResponse DisableConnectionRequests(Guid requestId)
    {
        connectionRequests.ClearPending();
        return new AgentResponse
        {
            RequestId = requestId,
            Success = true,
            Message = "Richieste di collegamento disabilitate.",
            Network = networkSettings.DisableConnectionRequests()
        };
    }

    private AgentResponse GetPendingConnectionRequests(Guid requestId) => new()
    {
        RequestId = requestId,
        Success = true,
        ConnectionRequests = connectionRequests.GetPending()
    };

    private AgentResponse RespondConnectionRequest(AgentRequest request)
    {
        if (request.ConnectionDecision is not { RequestId: var requestId } decision || requestId == Guid.Empty)
            return AgentResponse.Error(request.RequestId, "InvalidDecision", "Decisione di collegamento mancante.");
        return connectionRequests.Decide(requestId, decision.Accept, out var message)
            ? new AgentResponse
            {
                RequestId = request.RequestId,
                Success = true,
                Message = message,
                Network = networkSettings.GetConfiguration(
                    pendingConnectionRequestCount: connectionRequests.PendingCount)
            }
            : AgentResponse.Error(request.RequestId, "DecisionFailed", message);
    }

    private async Task<AgentResponse> StartScanAsync(AgentRequest request, CancellationToken stoppingToken)
    {
        var lease = await operationGate.TryEnterAsync(stoppingToken).ConfigureAwait(false);
        if (lease is null)
            return AgentResponse.Error(request.RequestId, "AgentBusy", "È già in corso un'operazione locale.");

        var operation = operations.Create("Scan", "Scansione accodata.");
        await PersistAsync(operation, stoppingToken).ConfigureAwait(false);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!_operationCancellation.TryAdd(operation.Id, cancellation))
        {
            cancellation.Dispose();
            lease.Dispose();
            return AgentResponse.Error(request.RequestId, "InternalError", "Impossibile registrare la scansione.");
        }

        _ = RunScanAsync(operation.Id, request.Scan ?? new ScanRequest(), lease, cancellation);
        return new AgentResponse
        {
            RequestId = request.RequestId,
            Success = true,
            Message = "Scansione locale avviata.",
            Operation = operation
        };
    }

    private async Task<AgentResponse> StartUpdateAsync(AgentRequest request, CancellationToken stoppingToken)
    {
        IReadOnlyList<RemoteUpdateItem> selected;
        try
        {
            selected = RemoteUpdateSelectionValidator.Validate(
                request.Update is null ? null : operations.Get(request.Update.ScanOperationId),
                request.Update,
                DateTime.UtcNow);
        }
        catch (RemoteUpdateValidationException ex)
        {
            return AgentResponse.Error(request.RequestId, ex.ErrorCode, ex.Message);
        }

        var lease = await operationGate.TryEnterAsync(stoppingToken).ConfigureAwait(false);
        if (lease is null)
            return AgentResponse.Error(request.RequestId, "AgentBusy", "È già in corso un'operazione locale.");
        var operation = operations.Create("Update", "Aggiornamenti accodati.");
        operation = operations.Update(operation.Id, operation.State, operation.Message, total: selected.Count);
        await PersistAsync(operation, stoppingToken).ConfigureAwait(false);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!_operationCancellation.TryAdd(operation.Id, cancellation))
        {
            cancellation.Dispose();
            lease.Dispose();
            return AgentResponse.Error(request.RequestId, "InternalError", "Impossibile registrare l'aggiornamento.");
        }
        _ = RunUpdateAsync(operation.Id, selected, lease, cancellation);
        return new AgentResponse
        {
            RequestId = request.RequestId,
            Success = true,
            Message = "Aggiornamenti remoti avviati.",
            Operation = operation
        };
    }

    private AgentResponse GetOperation(AgentRequest request)
    {
        if (request.OperationId is not Guid operationId)
            return AgentResponse.Error(request.RequestId, "MissingOperationId", "Identificativo operazione mancante.");
        var operation = operations.Get(operationId);
        return operation is null
            ? AgentResponse.Error(request.RequestId, "OperationNotFound", "Operazione non trovata.")
            : new AgentResponse { RequestId = request.RequestId, Success = true, Operation = operation };
    }

    private AgentResponse CancelOperation(AgentRequest request)
    {
        if (request.OperationId is not Guid operationId)
            return AgentResponse.Error(request.RequestId, "MissingOperationId", "Identificativo operazione mancante.");
        if (!_operationCancellation.TryGetValue(operationId, out var cancellation))
            return AgentResponse.Error(request.RequestId, "OperationNotCancellable", "L'operazione non è attiva o non è annullabile.");
        cancellation.Cancel();
        return AgentResponse.Ok(request.RequestId, "Annullamento richiesto.");
    }

    private async Task RunScanAsync(
        Guid operationId,
        ScanRequest request,
        IDisposable lease,
        CancellationTokenSource cancellation)
    {
        try
        {
            await UpdateAndPersistAsync(
                operationId,
                AgentOperationStates.Running,
                "Scansione locale in corso.").ConfigureAwait(false);
            var result = await RunSessionHelperAsync(operationId, request, cancellation.Token).ConfigureAwait(false);
            var state = result.Warnings.Count == 0
                ? AgentOperationStates.Completed
                : AgentOperationStates.CompletedWithWarnings;
            await UpdateAndPersistAsync(
                operationId,
                state,
                "Scansione locale completata.",
                result).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await UpdateAndPersistAsync(
                operationId,
                AgentOperationStates.Cancelled,
                "Scansione locale annullata.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scansione locale {OperationId} fallita.", operationId);
            await UpdateAndPersistAsync(
                operationId,
                AgentOperationStates.Failed,
                ex.Message).ConfigureAwait(false);
        }
        finally
        {
            _operationCancellation.TryRemove(operationId, out _);
            cancellation.Dispose();
            lease.Dispose();
        }
    }

    private async Task RunUpdateAsync(
        Guid operationId,
        IReadOnlyList<RemoteUpdateItem> updates,
        IDisposable lease,
        CancellationTokenSource cancellation)
    {
        try
        {
            await UpdateAndPersistAsync(
                operationId,
                AgentOperationStates.Running,
                "Preparazione aggiornamenti remoti; per i driver Windows può mostrare una conferma UAC sul PC gestito.",
                total: updates.Count).ConfigureAwait(false);
            var result = await RunUpdateHelperAsync(operationId, updates, cancellation.Token).ConfigureAwait(false);
            var state = result.Results.All(x => x.Success)
                ? AgentOperationStates.Completed
                : AgentOperationStates.CompletedWithWarnings;
            await UpdateAndPersistAsync(
                operationId,
                state,
                result.Results.All(x => x.Success)
                    ? "Aggiornamenti remoti completati."
                    : "Aggiornamenti terminati: alcuni elementi richiedono attenzione.",
                updateResult: result,
                currentIndex: result.Results.Count,
                total: updates.Count,
                currentItemProgress: 100,
                restartRequired: result.RestartRequired).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await UpdateAndPersistAsync(operationId, AgentOperationStates.Cancelled, "Aggiornamenti remoti annullati.")
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Aggiornamento remoto {OperationId} fallito.", operationId);
            await UpdateAndPersistAsync(operationId, AgentOperationStates.Failed, ex.Message).ConfigureAwait(false);
        }
        finally
        {
            _operationCancellation.TryRemove(operationId, out _);
            cancellation.Dispose();
            lease.Dispose();
        }
    }

    private async Task<AgentOperation> UpdateAndPersistAsync(
        Guid operationId,
        string state,
        string message,
        ScanResult? result = null,
        RemoteUpdateResult? updateResult = null,
        int? currentIndex = null,
        int? total = null,
        string? currentItemName = null,
        string? phase = null,
        double? currentItemProgress = null,
        bool? restartRequired = null)
    {
        var operation = operations.Update(
            operationId, state, message, result, updateResult, currentIndex, total,
            currentItemName, phase, currentItemProgress, restartRequired);
        await PersistAsync(operation, CancellationToken.None).ConfigureAwait(false);
        return operation;
    }

    private async Task PersistAsync(AgentOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            await operationStore.SaveAsync(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Persistenza operazione locale {OperationId} non riuscita.", operation.Id);
        }
    }

    private static async Task<ScanResult> RunSessionHelperAsync(
        Guid operationId,
        ScanRequest request,
        CancellationToken cancellationToken)
    {
        var pipeName = $"UpdateCenter.Agent.Helper.v1.{operationId:N}";
        var helperExecutable = Path.Combine(AppContext.BaseDirectory, "UpdateCenter.SessionHelper.exe");
        if (!File.Exists(helperExecutable))
            throw new FileNotFoundException("Session Helper non trovato accanto all'Agent.", helperExecutable);
        using var launchTicket = InteractiveSessionLauncher.Prepare(
            WindowsServiceHelpers.IsWindowsService());
        await using var pipe = LocalPipeFactory.CreateHelperPipe(pipeName, launchTicket.UserSid);
        using var helper = launchTicket.Start(
            helperExecutable,
            ["--pipe", pipeName],
            AppContext.BaseDirectory);
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!helper.HasExited) helper.Kill(entireProcessTree: true);
            }
            catch { }
        });

        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        await PipeJsonProtocol.WriteAsync(pipe, new SessionHelperRequest
        {
            Command = SessionHelperCommands.Scan,
            Scan = request
        }, cancellationToken).ConfigureAwait(false);
        var response = await PipeJsonProtocol.ReadAsync<SessionHelperResponse>(pipe, cancellationToken).ConfigureAwait(false);
        await helper.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.ScanResult is null)
            throw new InvalidDataException(string.IsNullOrWhiteSpace(response.Error)
                ? "Il Session Helper non ha restituito la scansione."
                : response.Error);
        return response.ScanResult;
    }

    private async Task<RemoteUpdateResult> RunUpdateHelperAsync(
        Guid operationId,
        IReadOnlyList<RemoteUpdateItem> updates,
        CancellationToken cancellationToken)
    {
        var pipeName = $"UpdateCenter.Agent.Helper.v1.{operationId:N}";
        var helperExecutable = Path.Combine(AppContext.BaseDirectory, "UpdateCenter.SessionHelper.exe");
        if (!File.Exists(helperExecutable))
            throw new FileNotFoundException("Session Helper non trovato accanto all'Agent.", helperExecutable);
        using var launchTicket = InteractiveSessionLauncher.Prepare(WindowsServiceHelpers.IsWindowsService());
        await using var pipe = LocalPipeFactory.CreateHelperPipe(pipeName, launchTicket.UserSid);
        using var helper = launchTicket.Start(helperExecutable, ["--pipe", pipeName], AppContext.BaseDirectory);
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try { if (!helper.HasExited) helper.Kill(entireProcessTree: true); } catch { }
        });
        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        await PipeJsonProtocol.WriteAsync(pipe, new SessionHelperRequest
        {
            Command = SessionHelperCommands.Install,
            Updates = updates
        }, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var response = await PipeJsonProtocol.ReadAsync<SessionHelperResponse>(pipe, cancellationToken)
                .ConfigureAwait(false);
            if (!response.Success)
                throw new InvalidDataException(string.IsNullOrWhiteSpace(response.Error)
                    ? "Aggiornamento remoto non riuscito."
                    : response.Error);
            if (response.IsFinal)
            {
                await helper.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return response.UpdateResult
                       ?? throw new InvalidDataException("Risultato aggiornamento remoto mancante.");
            }
            await UpdateAndPersistAsync(
                operationId,
                AgentOperationStates.Running,
                response.Message,
                currentIndex: response.CurrentIndex,
                total: response.Total,
                currentItemName: response.CurrentItemName,
                phase: response.Phase,
                currentItemProgress: response.CurrentItemProgress,
                restartRequired: response.RestartRequired).ConfigureAwait(false);
        }
    }

    public override void Dispose()
    {
        foreach (var cancellation in _operationCancellation.Values)
            cancellation.Cancel();
        _clientLimit.Dispose();
        base.Dispose();
    }
}
