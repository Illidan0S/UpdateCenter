using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UpdateCenter.Contracts;
using UpdateCenter.Core;
using UpdateCenter.RemoteClient;

var request = new AgentRequest
{
    Command = AgentCommands.StartScan,
    Scan = new ScanRequest { IncludeUnknownVersions = true }
};
await using var memory = new MemoryStream();
await PipeJsonProtocol.WriteAsync(memory, request, CancellationToken.None);
memory.Position = 0;
var roundTrip = await PipeJsonProtocol.ReadAsync<AgentRequest>(memory, CancellationToken.None);
if (roundTrip.RequestId != request.RequestId || roundTrip.Command != AgentCommands.StartScan ||
    roundTrip.Scan?.IncludeUnknownVersions != true)
    throw new InvalidOperationException("Round-trip del protocollo locale non riuscito.");

var updateRequest = new AgentRequest
{
    Command = AgentCommands.StartUpdate,
    Update = new RemoteUpdateRequest
    {
        ScanOperationId = Guid.NewGuid(),
        Items = [new RemoteUpdateSelection { Id = "Example.Package", Kind = "Software", RiskConfirmed = true }]
    }
};
await using var updateMemory = new MemoryStream();
await PipeJsonProtocol.WriteAsync(updateMemory, updateRequest, CancellationToken.None);
updateMemory.Position = 0;
var updateRoundTrip = await PipeJsonProtocol.ReadAsync<AgentRequest>(updateMemory, CancellationToken.None);
if (updateRoundTrip.Update?.Items.Single().Id != "Example.Package" ||
    updateRoundTrip.Update.Items.Single().RiskConfirmed != true)
    throw new InvalidOperationException("Round-trip della richiesta di aggiornamento remoto non riuscito.");

var remoteScan = new ScanResult
{
    MachineName = "PC-PORTATILE",
    HasBattery = true,
    IsOnBattery = true,
    BatteryPercentage = 64,
    SystemDriveFreeBytes = 42L * 1024 * 1024 * 1024,
    Updates = [new RemoteUpdateItem
    {
        Id = "Example.Remote",
        Name = "Aggiornamento remoto",
        Kind = "Driver",
        Status = "Aggiornamento manuale",
        ResultDetails = "Cambio di ambito rilevato.",
        DownloadSizeBytes = 125L * 1024 * 1024,
        IsImportant = true,
        RequiresRiskConfirmation = true
    }]
};
var remoteScanJson = JsonSerializer.Serialize(remoteScan);
var remoteScanRoundTrip = JsonSerializer.Deserialize<ScanResult>(remoteScanJson)
                          ?? throw new InvalidOperationException("Riepilogo remoto non deserializzabile.");
if (!remoteScanRoundTrip.HasBattery || !remoteScanRoundTrip.IsOnBattery ||
    remoteScanRoundTrip.BatteryPercentage != 64 ||
    remoteScanRoundTrip.Updates.Single().DownloadSizeBytes != 125L * 1024 * 1024 ||
    remoteScanRoundTrip.Updates.Single().Status != "Aggiornamento manuale" ||
    remoteScanRoundTrip.Updates.Single().ResultDetails != "Cambio di ambito rilevato." ||
    !remoteScanRoundTrip.Updates.Single().IsImportant)
    throw new InvalidOperationException("I dati del riepilogo remoto per PC non sopravvivono alla serializzazione.");

var validatedScanId = Guid.NewGuid();
var validatedScan = new AgentOperation
{
    Id = validatedScanId,
    Kind = "Scan",
    State = AgentOperationStates.Completed,
    CreatedUtc = DateTime.UtcNow,
    UpdatedUtc = DateTime.UtcNow,
    ScanResult = new ScanResult
    {
        CompletedUtc = DateTime.UtcNow,
        Updates = [new RemoteUpdateItem
        {
            Id = "Example.SafeFromScan",
            Name = "Pacchetto dalla scansione",
            Kind = "Software",
            CanInstall = true,
            RequiresRiskConfirmation = true
        }]
    }
};
try
{
    RemoteUpdateSelectionValidator.Validate(validatedScan, new RemoteUpdateRequest
    {
        ScanOperationId = validatedScanId,
        Items = [new RemoteUpdateSelection { Id = "Example.SafeFromScan", Kind = "Software" }]
    }, DateTime.UtcNow);
    throw new InvalidOperationException("Un aggiornamento rischioso è stato accettato senza conferma.");
}
catch (RemoteUpdateValidationException ex) when (ex.ErrorCode == "RiskConfirmationRequired")
{
}
var validatedUpdates = RemoteUpdateSelectionValidator.Validate(validatedScan, new RemoteUpdateRequest
{
    ScanOperationId = validatedScanId,
    Items = [new RemoteUpdateSelection
    {
        Id = "Example.SafeFromScan",
        Kind = "Software",
        RiskConfirmed = true
    }]
}, DateTime.UtcNow);
if (validatedUpdates.Single().Id != "Example.SafeFromScan")
    throw new InvalidOperationException("La selezione valida della scansione non è stata accettata.");

await using var oversized = new MemoryStream();
var invalidHeader = new byte[sizeof(int)];
BinaryPrimitives.WriteInt32LittleEndian(invalidHeader, AgentProtocol.MaximumMessageBytes + 1);
await oversized.WriteAsync(invalidHeader);
oversized.Position = 0;
try
{
    await PipeJsonProtocol.ReadAsync<AgentRequest>(oversized, CancellationToken.None);
    throw new InvalidOperationException("Un messaggio oltre il limite è stato accettato.");
}
catch (InvalidDataException)
{
}

using (var gate = new SingleOperationGate())
{
    using var first = await gate.TryEnterAsync(CancellationToken.None)
                      ?? throw new InvalidOperationException("Il lock iniziale non è stato acquisito.");
    if (await gate.TryEnterAsync(CancellationToken.None) is not null)
        throw new InvalidOperationException("Due operazioni concorrenti sono state accettate.");
    first.Dispose();
    using var second = await gate.TryEnterAsync(CancellationToken.None)
                       ?? throw new InvalidOperationException("Il lock non è stato rilasciato.");
}

var registry = new AgentOperationRegistry();
for (var index = 0; index < 300; index++)
{
    var operation = registry.Create("Test", "Creato");
    registry.Update(operation.Id, AgentOperationStates.Completed, "Completato");
}
if (registry.Snapshot().Count > 256)
    throw new InvalidOperationException("La retention delle operazioni non rispetta il limite.");

var progressOperation = registry.Create("Update", "Accodato");
var progressSnapshot = registry.Update(
    progressOperation.Id, AgentOperationStates.Running, "Download", currentIndex: 1, total: 3,
    currentItemName: "Pacchetto test", phase: "Download", currentItemProgress: 42, restartRequired: true);
if (progressSnapshot.Total != 3 || progressSnapshot.CurrentItemProgress != 42 ||
    progressSnapshot.CurrentItemName != "Pacchetto test" || !progressSnapshot.RestartRequired)
    throw new InvalidOperationException("L'avanzamento dell'aggiornamento remoto non viene conservato.");

var interruptedId = Guid.NewGuid();
var restoredRegistry = new AgentOperationRegistry();
restoredRegistry.Restore([
    new AgentOperation
    {
        Id = interruptedId,
        Kind = "Scan",
        State = AgentOperationStates.Running,
        Message = "In corso",
        CreatedUtc = DateTime.UtcNow.AddMinutes(-1),
        UpdatedUtc = DateTime.UtcNow.AddMinutes(-1)
    }
]);
var restored = restoredRegistry.Get(interruptedId);
if (restored?.State != AgentOperationStates.Failed ||
    !restored.Message.Contains("riavvio", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Il recupero dopo il riavvio non chiude correttamente le operazioni interrotte.");

if (new AgentStatus().NetworkListenerEnabled)
    throw new InvalidOperationException("Il listener di rete deve essere disabilitato per impostazione predefinita.");

using (var signingKey = RSA.Create(2048))
{
    var body = Encoding.UTF8.GetBytes("{\"includeSoftware\":true}");
    var canonical = SignedRequestProtocol.BuildCanonical(
        "post", "/api/v1/scans", "1700000000", "00000000-0000-0000-0000-000000000001", body);
    var signature = signingKey.SignData(
        Encoding.UTF8.GetBytes(canonical),
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    if (!signingKey.VerifyData(
            Encoding.UTF8.GetBytes(canonical),
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1))
        throw new InvalidOperationException("Firma della richiesta Controller non verificabile.");
    var modified = SignedRequestProtocol.BuildCanonical(
        "post", "/api/v1/scans", "1700000000", "00000000-0000-0000-0000-000000000001",
        Encoding.UTF8.GetBytes("{\"includeSoftware\":false}"));
    if (signingKey.VerifyData(
            Encoding.UTF8.GetBytes(modified),
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1))
        throw new InvalidOperationException("Una richiesta modificata ha mantenuto una firma valida.");
}

var controllerTestDirectory = Path.Combine(Path.GetTempPath(), "UpdateCenter-ControllerStore-" + Guid.NewGuid().ToString("N"));
try
{
    var oldAgentId = Guid.NewGuid();
    var currentAgentId = Guid.NewGuid();
    var oldRecord = new PairedAgentRecord
    {
        AgentId = oldAgentId,
        Address = "192.168.1.14",
        ApiPort = 47382,
        DisplayName = "Agent precedente",
        CertificateSha256 = new string('A', 64),
        PairedUtc = DateTime.UtcNow.AddMinutes(-1)
    };
    var currentRecord = new PairedAgentRecord
    {
        AgentId = currentAgentId,
        Address = "192.168.1.14",
        ApiPort = 47382,
        DisplayName = "Agent corrente",
        CertificateSha256 = new string('B', 64),
        PairedUtc = DateTime.UtcNow
    };
    Directory.CreateDirectory(controllerTestDirectory);
    File.WriteAllText(
        Path.Combine(controllerTestDirectory, "agents.json"),
        JsonSerializer.Serialize(new[] { oldRecord, currentRecord }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    var controllerStore = new ControllerIdentityStore(controllerTestDirectory);
    var records = controllerStore.LoadAgents();
    var current = controllerStore.FindByAddress("192.168.1.14");
    if (records.Count != 1 || current?.AgentId != currentAgentId)
        throw new InvalidOperationException("Il Controller non ha normalizzato l'associazione legacy dello stesso indirizzo.");
    controllerStore.SaveAgent(currentRecord);
    if (controllerStore.LoadAgents().Count != 1)
        throw new InvalidOperationException("Il salvataggio non ha eliminato le associazioni duplicate dello stesso indirizzo.");
    if (!controllerStore.RemoveAgent(currentAgentId, "192.168.1.14") || controllerStore.LoadAgents().Count != 0)
        throw new InvalidOperationException("La revoca locale non ha rimosso l'associazione del dispositivo.");
}
finally
{
    if (Directory.Exists(controllerTestDirectory)) Directory.Delete(controllerTestDirectory, recursive: true);
}

Console.WriteLine("Test Network Core superati: protocollo, limiti, lock, retention, firme e associazioni Controller.");
