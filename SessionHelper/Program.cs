using System.IO.Pipes;
using UpdateCenter.Contracts;
using UpdateCenter.Core;
using UpdateCenter.Models;
using UpdateCenter.Services;

if (args.Length == 2 &&
    (args[0].Equals("--update-runner-admin", StringComparison.OrdinalIgnoreCase) ||
     args[0].Equals("--update-runner-user", StringComparison.OrdinalIgnoreCase)))
{
    var requireAdministrator = args[0].Equals("--update-runner-admin", StringComparison.OrdinalIgnoreCase);
    return ElevatedUpdateRunner.Run(args[1], requireAdministrator);
}

var pipeIndex = Array.FindIndex(args, x => x.Equals("--pipe", StringComparison.OrdinalIgnoreCase));
if (pipeIndex < 0 || pipeIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[pipeIndex + 1]))
{
    Console.Error.WriteLine("Uso: UpdateCenter.SessionHelper --pipe NOME_PIPE");
    return 2;
}

try
{
    var settings = JsonStorage.LoadSettings();
    LocalizationService.Initialize(settings.LanguageMode);
    await using var pipe = new NamedPipeClientStream(
        ".",
        args[pipeIndex + 1],
        PipeDirection.InOut,
        PipeOptions.Asynchronous);
    using var timeout = new CancellationTokenSource(TimeSpan.FromHours(2));
    await pipe.ConnectAsync(timeout.Token);
    var request = await PipeJsonProtocol.ReadAsync<SessionHelperRequest>(pipe, timeout.Token);

    if (request.Command.Equals(SessionHelperCommands.Scan, StringComparison.Ordinal))
    {
        var result = await new HeadlessScanService().ScanAsync(request.Scan ?? new ScanRequest(), timeout.Token);
        await PipeJsonProtocol.WriteAsync(pipe, new SessionHelperResponse
        {
            IsFinal = true,
            Success = true,
            ScanResult = result,
            Message = "Scansione locale completata."
        }, timeout.Token);
        return 0;
    }

    if (!request.Command.Equals(SessionHelperCommands.Install, StringComparison.Ordinal))
        throw new InvalidDataException("Comando Session Helper non riconosciuto.");

    var updates = request.Updates.Select(MapUpdate).ToList();
    if (updates.Count == 0) throw new InvalidDataException("Nessun aggiornamento valido ricevuto.");
    var startedUtc = DateTime.UtcNow;
    var writeGate = new object();
    void Send(SessionHelperResponse response)
    {
        lock (writeGate)
            PipeJsonProtocol.WriteAsync(pipe, response, timeout.Token).GetAwaiter().GetResult();
    }

    var resultStatus = await new UpdateCoordinator().RunAsync(
        updates,
        settings,
        new UpdatePauseController(),
        progress => Send(new SessionHelperResponse
        {
            IsFinal = false,
            Success = true,
            CurrentIndex = progress.CurrentIndex,
            Total = progress.Total,
            CurrentItemName = progress.CurrentName,
            Message = progress.Message,
            Phase = progress.Phase,
            CurrentItemProgress = progress.CurrentItemProgress,
            RestartRequired = progress.RestartRequired
        }),
        timeout.Token);

    var remoteResult = new RemoteUpdateResult
    {
        StartedUtc = startedUtc,
        CompletedUtc = DateTime.UtcNow,
        RestartRequired = resultStatus.RestartRequired,
        Results = resultStatus.Results.Select(item => new RemoteUpdateItemResult
        {
            Id = item.Id,
            Name = item.Name,
            Kind = item.Kind,
            Success = item.Success,
            RestartRequired = item.RestartRequired,
            Outcome = item.Outcome,
            Message = item.Message
        }).ToList()
    };
    Send(new SessionHelperResponse
    {
        IsFinal = true,
        Success = true,
        UpdateResult = remoteResult,
        CurrentIndex = resultStatus.Total,
        Total = resultStatus.Total,
        Message = resultStatus.Message,
        CurrentItemProgress = 100,
        RestartRequired = resultStatus.RestartRequired
    });
    return resultStatus.Results.All(x => x.Success) ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operazione locale annullata o scaduta.");
    return 3;
}
catch (Exception ex)
{
    LogService.Write("Session Helper interrotto.", ex);
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static UpdateItem MapUpdate(RemoteUpdateItem item)
{
    if (!Enum.TryParse<UpdateKind>(item.Kind, ignoreCase: true, out var kind))
        throw new InvalidDataException($"Tipo aggiornamento non valido: {item.Kind}.");
    return new UpdateItem
    {
        Id = item.Id,
        Name = item.Name,
        Kind = kind,
        Publisher = item.Publisher,
        InstalledVersion = item.InstalledVersion,
        AvailableVersion = item.AvailableVersion,
        Source = item.Source,
        PackageOperation = item.PackageOperation,
        CanInstall = item.CanInstall,
        RequiresRestart = item.RequiresRestart,
        RequiresRiskConfirmation = item.RequiresRiskConfirmation,
        HasUnverifiedInstallerMetadata = item.HasUnverifiedInstallerMetadata,
        WindowsUpdateId = item.WindowsUpdateId,
        WindowsUpdateRevision = item.WindowsUpdateRevision,
        WindowsUpdateServerSelection = item.WindowsUpdateServerSelection,
        WindowsUpdateServiceId = item.WindowsUpdateServiceId,
        DriverInstallMode = item.DriverInstallMode,
        OfficialReleasePageUrl = item.OfficialReleasePageUrl,
        OfficialDownloadUrl = item.OfficialDownloadUrl,
        ExpectedSha256 = item.ExpectedSha256,
        ExpectedSignerSubjects = item.ExpectedSignerSubjects.ToList(),
        DriverPackageType = item.DriverPackageType,
        CompatibleHardwareIds = item.CompatibleHardwareIds.ToList()
    };
}
