using UpdateCenter.Contracts;
using UpdateCenter.RemoteClient;

var dataRoot = Environment.GetEnvironmentVariable("UPDATECENTER_CONTROLLER_DATA");
var store = new ControllerIdentityStore(string.IsNullOrWhiteSpace(dataRoot) ? null : dataRoot);
var client = new RemoteAgentClient(store);

if (args.Length == 0)
{
    ShowUsage();
    return 2;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "discover":
        {
            var seconds = args.Length > 1 && int.TryParse(args[1], out var parsed)
                ? Math.Clamp(parsed, 1, 10)
                : 3;
            var devices = await new LanDiscoveryClient().DiscoverAsync(TimeSpan.FromSeconds(seconds));
            if (devices.Count == 0)
            {
                Console.WriteLine("Nessun Update Center Agent trovato.");
                break;
            }
            foreach (var device in devices)
                Console.WriteLine($"{device.DisplayName} | {device.Address}:{device.ApiPort} | Agent {device.AgentVersion} | {(device.HasController ? "associato" : "da associare")} | {device.AgentId}");
            break;
        }
        case "pair" when args.Length >= 3:
        {
            var port = args.Length >= 4 && int.TryParse(args[3], out var parsedPort) ? parsedPort : 47382;
            var paired = await client.PairAsync(args[1], port, args[2], args[1]);
            Console.WriteLine($"Associato: {paired.AgentId} su {paired.Address}:{paired.ApiPort}.");
            break;
        }
        case "status" when args.Length >= 2:
        {
            var response = await client.GetStatusAsync(args[1]);
            Console.WriteLine($"{response.Status?.MachineName}: Agent {response.Status?.AgentVersion}; operazione attiva: {response.Status?.OperationInProgress}.");
            break;
        }
        case "scan" when args.Length >= 2:
        {
            var started = await client.StartScanAsync(args[1]);
            var operation = started.Operation ?? throw new InvalidDataException("Identificativo scansione mancante.");
            Console.WriteLine($"Scansione remota avviata: {operation.Id}");
            var lastState = operation.State;
            while (!AgentOperationStates.IsTerminal(operation.State))
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                operation = (await client.GetOperationAsync(args[1], operation.Id)).Operation
                            ?? throw new InvalidDataException("Stato scansione mancante.");
                if (!operation.State.Equals(lastState, StringComparison.Ordinal))
                {
                    Console.WriteLine($"{operation.State}: {operation.Message}");
                    lastState = operation.State;
                }
            }
            if (operation.ScanResult is not null)
                Console.WriteLine($"Aggiornamenti: {operation.ScanResult.Updates.Count}; avvisi: {operation.ScanResult.Warnings.Count}; driver: {operation.ScanResult.InstalledDriverCount}; runtime: {operation.ScanResult.RuntimeCheckCount}.");
            return operation.State is AgentOperationStates.Completed or AgentOperationStates.CompletedWithWarnings ? 0 : 1;
        }
        default:
            ShowUsage();
            return 2;
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.GetBaseException().Message);
    return 1;
}

static void ShowUsage()
{
    Console.WriteLine("UpdateCenter.NetworkConsole discover [secondi]");
    Console.WriteLine("UpdateCenter.NetworkConsole pair INDIRIZZO CODICE [PORTA]");
    Console.WriteLine("UpdateCenter.NetworkConsole status INDIRIZZO");
    Console.WriteLine("UpdateCenter.NetworkConsole scan INDIRIZZO");
}
