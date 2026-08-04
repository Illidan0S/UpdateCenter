using System.IO.Pipes;
using UpdateCenter.Contracts;

namespace UpdateCenter.Core;

public sealed class AgentLocalClient(string pipeName = AgentProtocol.ControlPipeName)
{
    public async Task<AgentResponse> SendAsync(
        AgentRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
        await PipeJsonProtocol.WriteAsync(pipe, request, timeoutSource.Token).ConfigureAwait(false);
        return await PipeJsonProtocol.ReadAsync<AgentResponse>(pipe, timeoutSource.Token).ConfigureAwait(false);
    }
}
