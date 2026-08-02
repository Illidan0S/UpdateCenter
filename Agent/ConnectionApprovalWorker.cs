using System.IO.Pipes;
using UpdateCenter.Contracts;
using UpdateCenter.Core;

namespace UpdateCenter.Agent;

public sealed class ConnectionApprovalWorker(
    ILogger<ConnectionApprovalWorker> logger,
    ConnectionRequestManager connectionRequests,
    AgentNetworkSettingsStore networkSettings) : BackgroundService
{
    private const int MaximumClients = 5;
    private readonly SemaphoreSlim _clientLimit = new(MaximumClients, MaximumClients);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = LocalPipeFactory.CreateApprovalPipe(MaximumClients);
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await _clientLimit.WaitAsync(stoppingToken).ConfigureAwait(false);
                _ = HandleAsync(pipe, stoppingToken).ContinueWith(
                    task =>
                    {
                        _clientLimit.Release();
                        if (task.Exception is not null)
                            logger.LogWarning(task.Exception, "Richiesta locale di approvazione non gestita.");
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

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            AgentResponse response;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var request = await PipeJsonProtocol.ReadAsync<AgentRequest>(pipe, timeout.Token).ConfigureAwait(false);
                response = request.Command switch
                {
                    AgentCommands.GetNetworkConfiguration => new AgentResponse
                    {
                        RequestId = request.RequestId,
                        Success = true,
                        Network = networkSettings.GetConfiguration(
                            pendingConnectionRequestCount: connectionRequests.PendingCount)
                    },
                    AgentCommands.GetPendingConnectionRequests => new AgentResponse
                    {
                        RequestId = request.RequestId,
                        Success = true,
                        ConnectionRequests = connectionRequests.GetPending()
                    },
                    AgentCommands.RespondConnectionRequest => Decide(request),
                    _ => AgentResponse.Error(request.RequestId, "CommandNotAllowed", "Comando non consentito sul canale di approvazione.")
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Messaggio di approvazione locale non valido.");
                response = AgentResponse.Error(Guid.Empty, "InvalidRequest", "Richiesta locale non valida.");
            }
            await PipeJsonProtocol.WriteAsync(pipe, response, stoppingToken).ConfigureAwait(false);
        }
    }

    private AgentResponse Decide(AgentRequest request)
    {
        if (request.ConnectionDecision is not { RequestId: var id } decision || id == Guid.Empty)
            return AgentResponse.Error(request.RequestId, "InvalidDecision", "Decisione mancante.");
        return connectionRequests.Decide(id, decision.Accept, out var message)
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

    public override void Dispose()
    {
        _clientLimit.Dispose();
        base.Dispose();
    }
}
