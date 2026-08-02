using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UpdateCenter.Agent;
using UpdateCenter.Contracts;
using UpdateCenter.Core;

if (args.Any(x => x.Equals("--probe-status", StringComparison.OrdinalIgnoreCase)))
{
    var response = await new AgentLocalClient().SendAsync(new AgentRequest
    {
        Command = AgentCommands.GetStatus
    }, TimeSpan.FromSeconds(10));
    Console.WriteLine(response.Success
        ? $"Agent {response.Status?.AgentVersion} su {response.Status?.MachineName}; rete abilitata: {response.Status?.NetworkListenerEnabled}."
        : $"Errore {response.ErrorCode}: {response.Message}");
    return response.Success ? 0 : 1;
}

var localNetworkCommand = args.FirstOrDefault()?.ToLowerInvariant() switch
{
    "--network-status" => AgentCommands.GetNetworkConfiguration,
    "--network-enable" => AgentCommands.EnableNetwork,
    "--network-disable" => AgentCommands.DisableNetwork,
    "--pairing-code" => AgentCommands.CreatePairingCode,
    "--revoke-controller" => AgentCommands.RevokeController,
    _ => ""
};
if (!string.IsNullOrWhiteSpace(localNetworkCommand))
{
    var response = await new AgentLocalClient().SendAsync(new AgentRequest
    {
        Command = localNetworkCommand
    }, TimeSpan.FromSeconds(15));
    if (!response.Success)
    {
        Console.Error.WriteLine($"Errore {response.ErrorCode}: {response.Message}");
        return 1;
    }
    if (response.Network is not null)
        Console.WriteLine($"Rete: {(response.Network.Enabled ? "abilitata" : "disabilitata")}; Agent: {response.Network.AgentId}; Controller: {(response.Network.HasController ? response.Network.ControllerName : "nessuno")}; riavvio richiesto: {response.Network.RestartRequired}.");
    if (response.PairingCode is not null)
        Console.WriteLine($"Codice pairing: {response.PairingCode.Code} (scade alle {response.PairingCode.ExpiresUtc.ToLocalTime():HH:mm:ss}).");
    if (!string.IsNullOrWhiteSpace(response.Message)) Console.WriteLine(response.Message);
    return 0;
}

if (args.Any(x => x.Equals("--probe-scan", StringComparison.OrdinalIgnoreCase)))
{
    var client = new AgentLocalClient();
    var started = await client.SendAsync(new AgentRequest
    {
        Command = AgentCommands.StartScan,
        Scan = new ScanRequest()
    }, TimeSpan.FromSeconds(10));
    if (!started.Success || started.Operation is null)
    {
        Console.Error.WriteLine($"Errore {started.ErrorCode}: {started.Message}");
        return 1;
    }

    Console.WriteLine($"Scansione avviata: {started.Operation.Id}");
    var lastState = "";
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        var response = await client.SendAsync(new AgentRequest
        {
            Command = AgentCommands.GetOperation,
            OperationId = started.Operation.Id
        }, TimeSpan.FromSeconds(10));
        if (!response.Success || response.Operation is null)
        {
            Console.Error.WriteLine($"Errore {response.ErrorCode}: {response.Message}");
            return 1;
        }

        if (!response.Operation.State.Equals(lastState, StringComparison.Ordinal))
        {
            Console.WriteLine($"{response.Operation.State}: {response.Operation.Message}");
            lastState = response.Operation.State;
        }
        if (!AgentOperationStates.IsTerminal(response.Operation.State)) continue;
        var result = response.Operation.ScanResult;
        if (result is not null)
        {
            Console.WriteLine($"Aggiornamenti: {result.Updates.Count}; avvisi: {result.Warnings.Count}; driver inventariati: {result.InstalledDriverCount}; runtime controllati: {result.RuntimeCheckCount}.");
            foreach (var warning in result.Warnings)
                Console.WriteLine($"Avviso: {warning}");
        }
        return response.Operation.State is AgentOperationStates.Completed or AgentOperationStates.CompletedWithWarnings ? 0 : 1;
    }
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Update Center Agent");
if (!Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
}
builder.Services.AddSingleton<AgentOperationRegistry>();
builder.Services.AddSingleton<SingleOperationGate>();
builder.Services.AddSingleton<AgentOperationStore>();
builder.Services.AddSingleton<AgentNetworkSettingsStore>();
builder.Services.AddSingleton<PairingCodeManager>();
builder.Services.AddSingleton<ConnectionRequestManager>();
builder.Services.AddSingleton<SignedRequestVerifier>();
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<ConnectionApprovalWorker>();
builder.Services.AddHostedService<AgentDiscoveryWorker>();
builder.Services.AddHostedService<AgentHttpsWorker>();
await builder.Build().RunAsync();
return 0;
