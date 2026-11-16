using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class WindowsUpdateService
{
    private const string DriverSearchCriteria = "IsInstalled=0 and IsHidden=0 and Type='Driver'";
    private const int DefaultServer = 0;
    private const int WindowsUpdateServer = 2;
    private const int OtherServer = 3;
    private const string MicrosoftUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d";

    public Task<DriverScanResult> ScanDriversAsync(
        CancellationToken cancellationToken,
        IReadOnlyList<DriverInventoryItem>? installedDrivers = null) =>
        Task.Run(
            () => ScanDrivers(cancellationToken, installedDrivers ?? [], CreateComObject), cancellationToken);

    private static object? CreateComObject(string progId) =>
        Type.GetTypeFromProgID(progId) is { } type ? Activator.CreateInstance(type) : null;

    internal static DriverScanResult ScanDrivers(
        CancellationToken cancellationToken,
        IReadOnlyList<DriverInventoryItem> installedDrivers,
        Func<string, object?> createComObject)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var sourceWarnings = new List<string>();
        var sources = BuildSearchSources(createComObject, sourceWarnings);
        var updates = new Dictionary<string, UpdateItem>(StringComparer.OrdinalIgnoreCase);
        var checkedSources = new List<string>();
        var defaultSearchCompleted = false;
        var defaultFoundUpdates = false;

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.IsWindowsUpdateFallback && defaultSearchCompleted && defaultFoundUpdates)
            {
                ScanDiagnostics.Count("wua.sources-skipped", 1);
                ScanDiagnostics.Count(DiagnosticSourceName(source), "skipped", 1);
                continue;
            }

            using var phase = ScanDiagnostics.Begin(DiagnosticSourceName(source), cancellationToken);
            using var diagnostics = ScanDiagnostics.IsActive ? new SourceCounters(DiagnosticSourceName(source)) : null;
            try
            {
                var found = SearchSource(source, cancellationToken, installedDrivers, createComObject, sourceWarnings, diagnostics);
                checkedSources.Add(source.Label);
                if (source.ServerSelection == DefaultServer)
                {
                    defaultSearchCompleted = found.IsComplete;
                    defaultFoundUpdates = found.Updates.Count > 0;
                }

                foreach (var item in found.Updates)
                    if (!updates.TryAdd($"{item.WindowsUpdateId}:{item.WindowsUpdateRevision}", item))
                        diagnostics?.Count("duplicates", 1);
                    else
                        diagnostics?.Count("admitted", 1);
                phase?.Complete(found.IsComplete ? "success" : "partial");
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                ScanDiagnostics.Count("wua.source-errors", 1);
                diagnostics?.Count("search-errors", 1);
                var warning = $"{source.Label}: ricerca non completata (0x{ex.HResult:X8}).";
                sourceWarnings.Add(warning);
                // COM messages can contain device identities or endpoints. Only fixed text/codes are logged.
                LogService.Write(warning);
            }
        }

        if (checkedSources.Count == 0)
            throw new InvalidOperationException("Nessuna sorgente Microsoft ha completato la ricerca dei driver.");

        var orderedUpdates = updates.Values
            .OrderByDescending(x => x.IsImportant)
            .ThenBy(x => x.IsOptional)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ScanDiagnostics.Count("wua.returned", orderedUpdates.Count);
        LogService.Write(
            $"Ricerca driver {(sourceWarnings.Count == 0 ? "completata" : "parziale")}: {orderedUpdates.Count} aggiornamenti univoci da {string.Join(", ", checkedSources)}.");

        return new DriverScanResult
        {
            Updates = orderedUpdates,
            SourcesChecked = checkedSources,
            SourceWarnings = sourceWarnings
        };
    }

    private sealed record SourceSearchResult(List<UpdateItem> Updates, bool IsComplete);

    private static SourceSearchResult SearchSource(
        UpdateSource source,
        CancellationToken cancellationToken,
        IReadOnlyList<DriverInventoryItem> installedDrivers,
        Func<string, object?> createComObject,
        List<string> sourceWarnings,
        SourceCounters? diagnostics)
    {
        object? sessionObject = null;
        object? searcherObject = null;
        object? resultObject = null;
        object? updatesObject = null;

        try
        {
            sessionObject = createComObject("Microsoft.Update.Session")
                ?? throw new InvalidOperationException("Impossibile inizializzare Windows Update Agent.");
            dynamic session = sessionObject;
            session.ClientApplicationID = "Update Center";
            searcherObject = session.CreateUpdateSearcher();
            dynamic searcher = searcherObject;
            ConfigureSearcher(searcher, source);
            cancellationToken.ThrowIfCancellationRequested();
            using (var wait = ScanDiagnostics.Begin("wait.wua-search", cancellationToken))
            {
                ScanDiagnostics.Count("wua.sources-queried", 1);
                ScanDiagnostics.Count(DiagnosticSourceName(source), "queried", 1);
                resultObject = searcher.Search(DriverSearchCriteria);
                wait?.Complete();
            }
            dynamic result = resultObject;
            cancellationToken.ThrowIfCancellationRequested();
            var statusReadError = false;
            var resultCode = ReadScanValue(() => (int)result.ResultCode, ref statusReadError, -1);
            diagnostics?.Count(resultCode switch
            {
                2 => "result.succeeded", 3 => "result.succeeded-with-errors", 4 => "result.failed",
                5 => "result.aborted", _ => "result.invalid"
            }, 1);
            if (statusReadError || resultCode is not (2 or 3))
                throw new InvalidOperationException("Windows Update non ha restituito un esito di ricerca utilizzabile.");

            var isComplete = resultCode == 2;
            object? warningsObject = null;
            try
            {
                warningsObject = result.Warnings;
                var warningCount = (int)((dynamic)warningsObject).Count;
                if (warningCount < 0) throw new InvalidOperationException("Conteggio warning WUA non valido.");
                diagnostics?.Count("warnings", warningCount);
                isComplete &= warningCount == 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics?.Count("warning-read-errors", 1);
                isComplete = false;
            }
            finally { ReleaseCom(warningsObject); }

            var updates = new List<UpdateItem>();
            int count;
            try
            {
                updatesObject = result.Updates;
                count = (int)((dynamic)updatesObject).Count;
                if (count < 0) throw new InvalidOperationException("Conteggio candidati WUA non valido.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics?.Count("collection-read-errors", 1);
                throw new InvalidOperationException("Raccolta candidati WUA non leggibile.", ex);
            }
            dynamic candidates = updatesObject;
            diagnostics?.Count("candidates", count);
            if (count == 0) diagnostics?.Count("no-offers", 1);
            var metadataErrors = 0;
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics?.Count("examined", 1);
                object? updateObject = null;
                try
                {
                    try { updateObject = candidates.Item(index); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        RecordExclusion(diagnostics, DriverRejection.MetadataReadError, null);
                        metadataErrors++;
                        continue;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    dynamic update = updateObject;
                    var identityValue = ReadDriverIdentity(updateObject!, out var identityReadError);
                    if (identityValue is not { } identity)
                    {
                        RecordExclusion(diagnostics, identityReadError ? DriverRejection.MetadataReadError : DriverRejection.InvalidIdentity, null);
                        if (identityReadError) metadataErrors++;
                        continue;
                    }
                    var driver = ReadEligibleDriver(updateObject!, out var rejection, out var rejectedClass);
                    if (driver is null)
                    {
                        RecordExclusion(diagnostics, rejection, rejectedClass);
                        if (rejection == DriverRejection.MetadataReadError) metadataErrors++;
                        continue;
                    }
                    var optionalReadError = false;
                    var (updateId, revision) = identity;
                    var (title, description, model, className, hardwareId) = driver;
                    string publisher = ReadScanValue(() => (string?)Convert.ToString(update.DriverManufacturer), ref optionalReadError) ?? "Microsoft Update";
                    DateTime? driverDate = ReadScanValue(() => (DateTime?)Convert.ToDateTime(update.DriverVerDate), ref optionalReadError);
                    long size = ReadScanValue(() => (long)Convert.ToInt64(update.MaxDownloadSize), ref optionalReadError);
                    bool reboot = ReadScanValue(() => (bool)Convert.ToBoolean(update.RebootRequired), ref optionalReadError);
                    string severity = ReadScanValue(() => (string?)Convert.ToString(update.MsrcSeverity), ref optionalReadError) ?? "";
                    bool mandatory = ReadScanValue(() => (bool)Convert.ToBoolean(update.IsMandatory), ref optionalReadError);
                    bool? autoSelected = ReadScanValue(() => (bool?)Convert.ToBoolean(update.AutoSelectOnWebSites), ref optionalReadError);
                    bool browseOnly = ReadScanValue(() => (bool)Convert.ToBoolean(update.BrowseOnly), ref optionalReadError);
                    bool hasSecurityBulletin = ReadScanValue(() => (long)Convert.ToInt64(update.SecurityBulletinIDs.Count), ref optionalReadError) > 0;
                    if (optionalReadError) metadataErrors++;
                    bool isImportant = mandatory || hasSecurityBulletin ||
                                       severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
                                       severity.Equals("Important", StringComparison.OrdinalIgnoreCase) ||
                                       description.Contains("security", StringComparison.OrdinalIgnoreCase) ||
                                       description.Contains("sicurezza", StringComparison.OrdinalIgnoreCase);

                    var availableVersion = ExtractVersion(title) ??
                        (driverDate.HasValue ? driverDate.Value.ToString("dd/MM/yyyy") : "Versione proposta da Windows Update");
                    var installed = FindBestMatch(installedDrivers, model, title, publisher, hardwareId);
                    if (installed is not null)
                    {
                        installed.HasUpdate = true;
                        installed.AvailableVersion = availableVersion;
                    }

                    updates.Add(new UpdateItem
                    {
                        Id = updateId,
                        Name = string.IsNullOrWhiteSpace(model) ? title : model,
                        Publisher = publisher,
                        Kind = UpdateKind.Driver,
                        InstalledVersion = installed?.InstalledVersion ?? "Versione installata rilevata da Windows",
                        AvailableVersion = availableVersion,
                        Source = $"{source.Label} · {className}",
                        Size = FormatBytes(size),
                        DownloadSizeBytes = Math.Max(size, 0),
                        RequiresRestart = reboot,
                        IsImportant = isImportant,
                        IsOptional = !isImportant && (browseOnly || autoSelected == false),
                        IsSelected = isImportant || (!browseOnly && autoSelected != false),
                        WindowsUpdateId = updateId,
                        ProviderReleaseNotes = ProviderReleaseNotes.Read((object)update, description),
                        WindowsUpdateRevision = revision,
                        WindowsUpdateServerSelection = source.ServerSelection,
                        WindowsUpdateServiceId = source.ServiceId,
                        DriverInstallMode = DriverInstallModes.WindowsUpdate,
                        SourceConfidence = "Alta · applicabilità calcolata da Windows Update",
                        CompatibilityDetail = string.IsNullOrWhiteSpace(hardwareId)
                            ? "Compatibilità determinata da Windows Update Agent"
                            : $"ID hardware proposto: {hardwareId}"
                    });
                    diagnostics?.Count("eligible", 1);
                    diagnostics?.Count("eligible-class." + ClassBucket(className), 1);
                }
                finally { ReleaseCom(updateObject); }
            }

            diagnostics?.Count("metadata-read-errors", metadataErrors);
            isComplete &= metadataErrors == 0;
            if (count > 0 && updates.Count == 0) diagnostics?.Count("all-excluded", 1);
            diagnostics?.Count(isComplete ? "complete" : "partial", 1);
            if (!isComplete)
                sourceWarnings.Add($"{source.Label}: risultati parziali o warning WUA; candidati con errori di lettura metadati: {metadataErrors}.");
            ScanDiagnostics.Count(DiagnosticSourceName(source), "returned", updates.Count);
            LogService.Write($"{source.Label}: risultato {(isComplete ? "completo" : "parziale")}, {updates.Count} offerte driver ammesse.");
            return new(updates, isComplete);
        }
        finally
        {
            ReleaseCom(updatesObject);
            ReleaseCom(resultObject);
            ReleaseCom(searcherObject);
            ReleaseCom(sessionObject);
        }
    }

    private static string DiagnosticSourceName(UpdateSource source) => source.ServerSelection switch
    {
        DefaultServer => "wua.configured", WindowsUpdateServer => "wua.online", _ => "wua.microsoft"
    };

    private static List<UpdateSource> BuildSearchSources(Func<string, object?> createComObject, List<string> warnings)
    {
        var sources = new List<UpdateSource>
        {
            new(DefaultServer, "", "Windows Update configurato"),
            new(WindowsUpdateServer, "", "Windows Update online", IsWindowsUpdateFallback: true)
        };

        object? managerObject = null;
        object? servicesObject = null;
        using var phase = ScanDiagnostics.Begin("wua.discovery");
        try
        {
            managerObject = createComObject("Microsoft.Update.ServiceManager");
            if (managerObject is null) throw new InvalidOperationException("Service manager non disponibile.");
            dynamic manager = managerObject;
            servicesObject = manager.Services;
            dynamic services = servicesObject;
            int count = services.Count;
            for (var index = 0; index < count; index++)
            {
                object? serviceObject = null;
                try
                {
                    serviceObject = services.Item(index);
                    dynamic service = serviceObject;
                    var serviceId = Convert.ToString(service.ServiceID) ?? "";
                    if (!serviceId.Equals(MicrosoftUpdateServiceId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    sources.Add(new UpdateSource(OtherServer, serviceId, "Microsoft Update"));
                    ScanDiagnostics.Count("wua.microsoft.registered", 1);
                    break;
                }
                finally
                {
                    ReleaseCom(serviceObject);
                }
            }
            phase?.Complete();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ScanDiagnostics.Count("wua.discovery-errors", 1);
            var warning = $"Enumerazione delle sorgenti Microsoft Update non riuscita (0x{ex.HResult:X8}).";
            warnings.Add(warning);
            LogService.Write(warning);
        }
        finally
        {
            ReleaseCom(servicesObject);
            ReleaseCom(managerObject);
        }

        return sources;
    }

    private static void ConfigureSearcher(dynamic searcher, UpdateSource source)
    {
        searcher.Online = true;
        searcher.IncludePotentiallySupersededUpdates = false;
        if (source.ServerSelection == DefaultServer) return;

        searcher.ServerSelection = source.ServerSelection;
        if (source.ServerSelection == OtherServer)
            searcher.ServiceID = source.ServiceId;
    }

    public static ItemRunResult InstallDriver(
        PlanItem planItem, Action<int, string>? progress = null) =>
        InstallDriver(planItem, progress, CreateComObject);

    internal static ItemRunResult InstallDriver(
        PlanItem planItem, Action<int, string>? progress, Func<string, object?> createComObject)
    {
        EnsureWindows();
        object? sessionObject = null;
        object? searcherObject = null;
        object? searchResultObject = null;
        object? verificationSearchResultObject = null;
        object? collectionObject = null;

        try
        {
            progress?.Invoke(8, "Ricerca del driver nella sorgente Windows Update...");
            sessionObject = createComObject("Microsoft.Update.Session")
                ?? throw new InvalidOperationException("Windows Update Agent non è disponibile.");
            dynamic session = sessionObject;
            session.ClientApplicationID = "Update Center";
            searcherObject = session.CreateUpdateSearcher();
            dynamic searcher = searcherObject;
            ConfigureSearcher(searcher, new UpdateSource(
                planItem.WindowsUpdateServerSelection,
                planItem.WindowsUpdateServiceId,
                "sorgente Microsoft selezionata"));
            searchResultObject = searcher.Search(DriverSearchCriteria);
            dynamic searchResult = searchResultObject;
            int searchCode = Convert.ToInt32(searchResult.ResultCode);
            if (searchCode is not (2 or 3))
                return Failed(planItem, LocalizationService.Text(
                    "La ricerca Windows Update non ha prodotto un esito utilizzabile. Esegui una nuova scansione.",
                    "Windows Update search did not produce a usable result. Run a new scan."),
                    "windows-update-search", searchCode);

            dynamic? selectedUpdate = null;
            int count = searchResult.Updates.Count;
            for (var index = 0; index < count; index++)
            {
                dynamic candidate = searchResult.Updates.Item(index);
                if (ReadDriverIdentity((object)candidate) is { } identity &&
                    identity.Id.Equals(planItem.WindowsUpdateId, StringComparison.OrdinalIgnoreCase) &&
                    identity.Revision == planItem.WindowsUpdateRevision)
                {
                    if (ReadEligibleDriver((object)candidate) is null)
                        return Failed(planItem, LocalizationService.Text(
                            "Aggiornamento bloccato: firmware, BIOS o identità del driver non verificabile.",
                            "Update blocked: firmware, BIOS, or unverifiable driver identity."),
                            "windows-update-safety");
                    selectedUpdate = candidate;
                    break;
                }
            }

            if (selectedUpdate is null)
                return Failed(
                    planItem,
                    LocalizationService.Text(
                        "L'aggiornamento non è più proposto da Windows Update. Esegui una nuova scansione.",
                        "The update is no longer offered by Windows Update. Run a new scan."),
                    "windows-update-search");

            if (!Convert.ToBoolean(selectedUpdate.EulaAccepted))
                selectedUpdate.AcceptEula();

            collectionObject = createComObject("Microsoft.Update.UpdateColl")
                ?? throw new InvalidOperationException("Raccolta Windows Update non disponibile.");
            dynamic collection = collectionObject;
            collection.Add(selectedUpdate);

            progress?.Invoke(35, LocalizationService.Text("Download del driver tramite Windows Update...", "Downloading driver via Windows Update..."));
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = collection;
            dynamic downloadResult = downloader.Download();
            int downloadCode = Convert.ToInt32(downloadResult.ResultCode);
            if (downloadCode is not (2 or 3))
                return Failed(
                    planItem,
                    LocalizationService.Text($"Download del driver non riuscito (codice {downloadCode}).", $"Driver download failed (code {downloadCode})."),
                    "windows-update-download",
                    downloadCode,
                    $"Windows Update DownloadResultCode={downloadCode}.");

            var (downloadItemCode, downloadHResult, _) = ReadItemOperationResult((object)downloadResult, installation: false);
            if (downloadItemCode != 2 || downloadHResult < 0)
                return Failed(planItem, LocalizationService.Text(
                    "Il download del driver selezionato non è completato o non è valido.",
                    "The selected driver download is incomplete or invalid."),
                    "windows-update-download", downloadItemCode,
                    $"DownloadResultCode={downloadCode}; ItemResultCode={downloadItemCode}; HResult=0x{downloadHResult:X8}.");

            progress?.Invoke(72, LocalizationService.Text("Installazione del driver tramite Windows Update...", "Installing driver via Windows Update..."));
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = collection;
            dynamic installResult = installer.Install();
            int resultCode = Convert.ToInt32(installResult.ResultCode);
            var (installItemCode, installHResult, itemReboot) = ReadItemOperationResult((object)installResult, installation: true);
            bool reboot = Convert.ToBoolean(installResult.RebootRequired) || itemReboot;
            bool installerSucceeded = resultCode is 2 or 3 && installItemCode == 2 && installHResult >= 0;
            progress?.Invoke(96, LocalizationService.Text("Verifica del risultato restituito da Windows Update...", "Verifying result returned by Windows Update..."));

            // IUpdate.IsInstalled is a snapshot at search time; the pre-install object is stale.
            var isInstalledAvailable = false;
            var reportedInstalled = false;
            bool? stillApplicable = null;
            string applicabilityDiagnostics;
            try
            {
                progress?.Invoke(98, LocalizationService.Text("Nuova scansione di applicabilità del driver...", "Rescanning driver applicability..."));
                var target = $"Type='Driver' and UpdateID='{Guid.Parse(planItem.WindowsUpdateId!):D}' and RevisionNumber={planItem.WindowsUpdateRevision}";
                verificationSearchResultObject = searcher.Search(
                    $"(IsInstalled=1 and {target}) or (IsInstalled=0 and IsHidden=0 and {target})");
                dynamic verificationSearchResult = verificationSearchResultObject;
                int verificationCode = Convert.ToInt32(verificationSearchResult.ResultCode);
                if (verificationCode is not (2 or 3))
                    throw new InvalidOperationException($"Post-install search result code={verificationCode}.");
                int verificationCount = verificationSearchResult.Updates.Count;
                for (var index = 0; index < verificationCount; index++)
                {
                    dynamic candidate = verificationSearchResult.Updates.Item(index);
                    var candidateId = Convert.ToString(candidate.Identity.UpdateID);
                    var candidateRevision = Convert.ToInt32(candidate.Identity.RevisionNumber);
                    if (string.Equals(candidateId, planItem.WindowsUpdateId, StringComparison.OrdinalIgnoreCase) &&
                        candidateRevision == planItem.WindowsUpdateRevision)
                    {
                        isInstalledAvailable = TryBool(() => Convert.ToBoolean(candidate.IsInstalled), out reportedInstalled);
                        if (isInstalledAvailable) stillApplicable = !reportedInstalled;
                        break;
                    }
                }
                // Absence can mean hidden, no longer offered or incomplete results, not installation.
                applicabilityDiagnostics = $"PostInstallSearchCode={verificationCode}; FreshInstalledStateAvailable={isInstalledAvailable}; PostInstallStillApplicable={stillApplicable?.ToString() ?? "unknown"}.";
            }
            catch (Exception ex)
            {
                applicabilityDiagnostics = $"PostInstallApplicabilityUnavailable={ex.Message}.";
            }

            var targetReached = isInstalledAvailable && reportedInstalled;
            var verification = new UpdateVerificationResult
            {
                IsDefinitive = targetReached || stillApplicable == true && !reboot,
                Verified = targetReached,
                Status = targetReached
                    ? UpdateVerificationStatuses.Verified
                    : reboot
                        ? UpdateVerificationStatuses.PendingRestart
                        : stillApplicable == true
                            ? UpdateVerificationStatuses.Failed
                            : UpdateVerificationStatuses.Unavailable,
                Message = targetReached
                    ? LocalizationService.Text("La nuova ricerca Windows Update conferma il driver installato.", "The fresh Windows Update search confirms the driver is installed.")
                    : reboot
                        ? LocalizationService.Text("La verifica finale richiede il riavvio.", "Final verification requires a restart.")
                        : stillApplicable == true
                            ? LocalizationService.Text("Dopo una nuova scansione il driver risulta ancora applicabile.", "After rescanning, the driver is still applicable.")
                            : LocalizationService.Text("Lo stato post-installazione non è tecnicamente verificabile.", "Post-installation state is not technically verifiable.")
            };
            var decision = UpdateResultPolicy.Resolve(installerSucceeded, reboot, verification);

            return new ItemRunResult
            {
                Id = planItem.Id,
                Name = planItem.Name,
                Kind = planItem.Kind,
                Success = decision.Success,
                InstallerSucceeded = installerSucceeded,
                Verified = decision.Verified,
                VerificationStatus = decision.VerificationStatus,
                ResultCode = resultCode,
                Phase = "windows-update-install",
                RestartRequired = reboot,
                Outcome = decision.Outcome,
                Message = decision.Verified
                    ? LocalizationService.Text("Driver installato e verificato da Windows Update.", "Driver installed and verified by Windows Update.")
                    : decision.Success && decision.VerificationStatus.Equals(UpdateVerificationStatuses.PendingRestart, StringComparison.Ordinal)
                        ? LocalizationService.Text("Windows Update ha completato l'installazione; la verifica finale richiede il riavvio.", "Windows Update completed the installation; final verification requires a restart.")
                        : decision.Success
                            ? LocalizationService.Text("Windows Update segnala installazione completata, ma lo stato installato non è ancora verificabile.", "Windows Update reports installation completed, but the installed state is not yet verifiable.")
                            : LocalizationService.Text($"Installazione non riuscita (codice {resultCode}).", $"Installation failed (code {resultCode})."),
                Diagnostics =
                    $"Windows Update DownloadResultCode={downloadCode}; DownloadItemResultCode={downloadItemCode}; DownloadHResult=0x{downloadHResult:X8}; " +
                    $"InstallResultCode={resultCode}; InstallItemResultCode={installItemCode}; InstallHResult=0x{installHResult:X8}; " +
                    $"IsInstalledAvailable={isInstalledAvailable}; IsInstalled={reportedInstalled}; " +
                    $"RebootRequired={reboot}; {applicabilityDiagnostics}"
            };
        }
        catch (Exception ex)
        {
            LogService.Write($"Errore installazione driver {planItem.Name}.", ex);
            return Failed(
                planItem,
                ex.Message,
                "windows-update-exception",
                ex.HResult,
                ex.ToString());
        }
        finally
        {
            ReleaseCom(collectionObject);
            ReleaseCom(verificationSearchResultObject);
            ReleaseCom(searchResultObject);
            ReleaseCom(searcherObject);
            ReleaseCom(sessionObject);
        }
    }

    private static (int Code, int HResult, bool Reboot) ReadItemOperationResult(object value, bool installation)
    {
        object? itemResultObject = null;
        try
        {
            itemResultObject = ((dynamic)value).GetUpdateResult(0);
            dynamic result = itemResultObject;
            return (Convert.ToInt32(result.ResultCode), Convert.ToInt32(result.HResult),
                installation && Convert.ToBoolean(result.RebootRequired));
        }
        finally { ReleaseCom(itemResultObject); }
    }

    private static (string Id, int Revision)? ReadDriverIdentity(dynamic update) =>
        ReadDriverIdentity((object)update, out _);

    private static (string Id, int Revision)? ReadDriverIdentity(object value, out bool readError)
    {
        dynamic update = value;
        readError = false;
        var id = ReadScanValue(() => (string?)update.Identity.UpdateID, ref readError);
        var revision = ReadScanValue(() => (int)update.Identity.RevisionNumber, ref readError);
        return Guid.TryParse(id, out var guid) && guid != Guid.Empty && revision > 0
            ? (id!, (int)revision)
            : null;
    }

    // WUA exposes class text and a hardware/compatible ID. Unknown or broad classes
    // cannot establish that an offer is an ordinary device driver.
    private static readonly HashSet<string> EligibleDriverClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Display", "Display adapters", "Schede video", "Net", "Network adapters", "Schede di rete",
        "Media", "AudioEndpoint", "AudioProcessingObject", "Bluetooth", "Camera", "Image",
        "HIDClass", "Keyboard", "Mouse", "Monitor", "Printer", "PrintQueue", "Biometric",
        "HDC", "SCSIAdapter", "DiskDrive", "CDROM", "USB", "USBDevice", "Sensor", "Ports"
    };

    private sealed record DriverMetadata(string Title, string Description, string Model, string Class, string HardwareId);

    private enum DriverRejection { None, Firmware, ClassNotAllowed, InvalidIdentity, IncompleteMetadata, MetadataReadError }

    private static DriverMetadata? ReadEligibleDriver(dynamic update) =>
        ReadEligibleDriver((object)update, out _, out _);

    private static DriverMetadata? ReadEligibleDriver(object value, out DriverRejection rejection, out string? className)
    {
        dynamic update = value;
        var readError = false;
        className = ReadScanValue(() => (string?)update.DriverClass, ref readError)?.Trim();
        var hardwareId = ReadScanValue(() => (string?)update.DriverHardwareID, ref readError);
        var title = ReadScanValue(() => (string?)update.Title, ref readError);
        var model = ReadScanValue(() => (string?)update.DriverModel, ref readError);
        var description = ReadScanValue(() => (string?)update.Description, ref readError);
        // Primary diagnostic reason only. The accepted set and the shared installation guard stay unchanged.
        var evidence = $"{className} {hardwareId} {title} {model} {description}";
        var firmware = evidence.Contains("firmware", StringComparison.OrdinalIgnoreCase) ||
               evidence.Contains("bios", StringComparison.OrdinalIgnoreCase) ||
               evidence.Contains("uefi", StringComparison.OrdinalIgnoreCase) ||
               evidence.Contains("f2e7dd72-6468-4e36-b6f1-6488f42c1b52", StringComparison.OrdinalIgnoreCase);
        rejection = readError ? DriverRejection.MetadataReadError
            : firmware ? DriverRejection.Firmware
            : className is null ? DriverRejection.IncompleteMetadata
            : !EligibleDriverClasses.Contains(className) ? DriverRejection.ClassNotAllowed
            : string.IsNullOrWhiteSpace(hardwareId) ? DriverRejection.InvalidIdentity
            : string.IsNullOrWhiteSpace(title) || model is null || description is null ? DriverRejection.IncompleteMetadata
            : DriverRejection.None;
        return rejection == DriverRejection.None ? new DriverMetadata(title!, description!, model!, className!, hardwareId!) : null;
    }

    private static T ReadScanValue<T>(Func<T> getter, ref bool readError, T fallback = default!)
    {
        try { return getter(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { readError = true; return fallback; }
    }

    // One bounded accumulator per active source. Flush even on failure/cancellation; allocate nothing when off.
    private sealed class SourceCounters(string prefix) : IDisposable
    {
        private readonly Dictionary<string, long> _counts = [];
        internal void Count(string name, long value) => _counts[name] = _counts.GetValueOrDefault(name) + value;
        public void Dispose()
        {
            foreach (var (name, value) in _counts)
            {
                ScanDiagnostics.Count("wua", name, value);
                ScanDiagnostics.Count(prefix, name, value);
            }
        }
    }

    private static void RecordExclusion(SourceCounters? diagnostics, DriverRejection reason, string? className)
    {
        if (diagnostics is null) return;
        diagnostics?.Count("excluded", 1);
        diagnostics?.Count(reason switch
        {
            DriverRejection.Firmware => "excluded.firmware",
            DriverRejection.ClassNotAllowed => "excluded.class-not-allowed",
            DriverRejection.InvalidIdentity => "excluded.invalid-identity",
            DriverRejection.MetadataReadError => "excluded.metadata-read-error",
            _ => "excluded.incomplete-metadata"
        }, 1);
        if (className is not null)
            diagnostics?.Count("rejected-class." + ClassBucket(className), 1);
    }

    // Never use arbitrary COM class text as a counter key. Unknown/localized values share a bounded bucket.
    private static string ClassBucket(string className) => className.Trim().ToLowerInvariant() switch
    {
        "display" or "display adapters" or "schede video" => "display",
        "net" or "network adapters" or "schede di rete" => "net",
        "securitydevices" => "securitydevices", "system" => "system", "processor" => "processor",
        "extension" => "extension", "softwarecomponent" => "softwarecomponent",
        "firmware" or "bios" or "uefi" or "{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}" => "firmware",
        "media" => "media", "audioendpoint" => "audioendpoint", "audioprocessingobject" => "audioprocessingobject",
        "bluetooth" => "bluetooth", "camera" => "camera", "image" => "image", "hidclass" => "hidclass",
        "keyboard" => "keyboard", "mouse" => "mouse", "monitor" => "monitor", "printer" => "printer",
        "printqueue" => "printqueue", "biometric" => "biometric", "hdc" => "hdc", "scsiadapter" => "scsiadapter",
        "diskdrive" => "diskdrive", "cdrom" => "cdrom", "usb" => "usb", "usbdevice" => "usbdevice",
        "sensor" => "sensor", "ports" => "ports", _ => "other"
    };

    private static ItemRunResult Failed(
        PlanItem item,
        string message,
        string phase = "windows-update-install",
        int? resultCode = null,
        string diagnostics = "") => new()
    {
        Id = item.Id,
        Name = item.Name,
        Kind = item.Kind,
        Success = false,
        InstallerSucceeded = false,
        Verified = false,
        VerificationStatus = UpdateVerificationStatuses.Failed,
        ResultCode = resultCode,
        Phase = phase,
        Message = message,
        Diagnostics = diagnostics
    };

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            throw new PlatformNotSupportedException("Questa funzione richiede Windows 10 versione 1809 o successiva.");
    }

    private static void ReleaseCom(object? value)
    {
        try { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        catch { }
    }

    private static bool TryBool(Func<bool> getter, out bool value)
    {
        try
        {
            value = getter();
            return true;
        }
        catch
        {
            value = false;
            return false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private static string? ExtractVersion(string title)
    {
        var matches = Regex.Matches(title, @"(?<!\d)(\d+(?:\.\d+){1,4})(?!\d)");
        return matches.Count == 0 ? null : matches[matches.Count - 1].Groups[1].Value;
    }

    private static DriverInventoryItem? FindBestMatch(
        IReadOnlyList<DriverInventoryItem> drivers,
        string model,
        string title,
        string manufacturer,
        string hardwareId)
    {
        DriverInventoryItem? best = null;
        var bestScore = 0;
        var target = Normalize($"{model} {title}");
        var modelNormalized = Normalize(model);
        var manufacturerNormalized = Normalize(manufacturer);

        foreach (var driver in drivers)
        {
            var score = 0;
            var device = Normalize(driver.DeviceName);
            if (device.Length >= 5 && (target.Contains(device, StringComparison.Ordinal) ||
                                      (modelNormalized.Length >= 5 && device.Contains(modelNormalized, StringComparison.Ordinal))))
                score += 100;

            if (!string.IsNullOrWhiteSpace(hardwareId) &&
                driver.DeviceId.StartsWith(hardwareId, StringComparison.OrdinalIgnoreCase))
                score += 120;

            var tokens = device.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length >= 3 && !IgnoredMatchTokens.Contains(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            score += tokens.Count(target.Contains) * 12;

            var driverManufacturer = Normalize($"{driver.Manufacturer} {driver.Provider}");
            if (manufacturerNormalized.Length >= 3 && driverManufacturer.Contains(manufacturerNormalized, StringComparison.Ordinal))
                score += 8;

            if (score > bestScore)
            {
                best = driver;
                bestScore = score;
            }
        }

        return bestScore >= 24 ? best : null;
    }

    private static readonly HashSet<string> IgnoredMatchTokens = new(StringComparer.Ordinal)
    {
        "driver", "device", "system", "controller", "microsoft", "windows", "software", "component"
    };

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();

    private sealed record UpdateSource(
        int ServerSelection,
        string ServiceId,
        string Label,
        bool IsWindowsUpdateFallback = false);
}
