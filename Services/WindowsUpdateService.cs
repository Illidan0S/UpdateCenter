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
            () => ScanDrivers(cancellationToken, installedDrivers ?? []), cancellationToken);

    private static DriverScanResult ScanDrivers(
        CancellationToken cancellationToken,
        IReadOnlyList<DriverInventoryItem> installedDrivers)
    {
        EnsureWindows();
        var sources = BuildSearchSources();
        var updates = new Dictionary<string, UpdateItem>(StringComparer.OrdinalIgnoreCase);
        var checkedSources = new List<string>();
        var sourceWarnings = new List<string>();
        var defaultSearchCompleted = false;
        var defaultFoundUpdates = false;

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.IsWindowsUpdateFallback && defaultSearchCompleted && defaultFoundUpdates)
                continue;

            try
            {
                var found = SearchSource(source, cancellationToken, installedDrivers);
                checkedSources.Add(source.Label);
                if (source.ServerSelection == DefaultServer)
                {
                    defaultSearchCompleted = true;
                    defaultFoundUpdates = found.Count > 0;
                }

                foreach (var item in found)
                    updates.TryAdd($"{item.WindowsUpdateId}:{item.WindowsUpdateRevision}", item);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                sourceWarnings.Add($"{source.Label}: {ex.Message}");
                LogService.Write($"Ricerca driver non riuscita tramite {source.Label}.", ex);
            }
        }

        if (checkedSources.Count == 0)
            throw new InvalidOperationException("Nessuna sorgente Microsoft ha completato la ricerca dei driver.");

        var orderedUpdates = updates.Values
            .OrderByDescending(x => x.IsImportant)
            .ThenBy(x => x.IsOptional)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        LogService.Write(
            $"Ricerca driver completata: {orderedUpdates.Count} aggiornamenti univoci da {string.Join(", ", checkedSources)}.");

        return new DriverScanResult
        {
            Updates = orderedUpdates,
            SourcesChecked = checkedSources,
            SourceWarnings = sourceWarnings
        };
    }

    private static List<UpdateItem> SearchSource(
        UpdateSource source,
        CancellationToken cancellationToken,
        IReadOnlyList<DriverInventoryItem> installedDrivers)
    {
        object? sessionObject = null;
        object? searcherObject = null;
        object? resultObject = null;

        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                ?? throw new InvalidOperationException("Windows Update Agent non è disponibile.");
            sessionObject = Activator.CreateInstance(sessionType)
                ?? throw new InvalidOperationException("Impossibile inizializzare Windows Update Agent.");
            dynamic session = sessionObject;
            session.ClientApplicationID = "Update Center";
            searcherObject = session.CreateUpdateSearcher();
            dynamic searcher = searcherObject;
            ConfigureSearcher(searcher, source);
            cancellationToken.ThrowIfCancellationRequested();
            resultObject = searcher.Search(DriverSearchCriteria);
            dynamic result = resultObject;

            var updates = new List<UpdateItem>();
            int count = result.Updates.Count;
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dynamic update = result.Updates.Item(index);
                string updateId = Convert.ToString(update.Identity.UpdateID) ?? "";
                int revision = Convert.ToInt32(update.Identity.RevisionNumber);
                string title = Convert.ToString(update.Title) ?? "Driver Windows";
                string description = SafeString(() => Convert.ToString(update.Description)) ?? "";
                string publisher = SafeString(() => Convert.ToString(update.DriverManufacturer)) ?? "Microsoft Update";
                string model = SafeString(() => Convert.ToString(update.DriverModel)) ?? "";
                string className = SafeString(() => Convert.ToString(update.DriverClass)) ?? "Driver";
                string hardwareId = SafeString(() => Convert.ToString(update.DriverHardwareID)) ?? "";
                DateTime? driverDate = SafeDate(() => Convert.ToDateTime(update.DriverVerDate));
                long size = SafeLong(() => Convert.ToInt64(update.MaxDownloadSize));
                bool reboot = SafeBool(() => Convert.ToBoolean(update.RebootRequired));
                string severity = SafeString(() => Convert.ToString(update.MsrcSeverity)) ?? "";
                bool mandatory = SafeBool(() => Convert.ToBoolean(update.IsMandatory));
                bool? autoSelected = SafeNullableBool(() => Convert.ToBoolean(update.AutoSelectOnWebSites));
                bool browseOnly = SafeBool(() => Convert.ToBoolean(update.BrowseOnly));
                bool hasSecurityBulletin = SafeLong(() => Convert.ToInt64(update.SecurityBulletinIDs.Count)) > 0;
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
                    RequiresRestart = reboot,
                    IsImportant = isImportant,
                    IsOptional = !isImportant && (browseOnly || autoSelected == false),
                    IsSelected = isImportant || (!browseOnly && autoSelected != false),
                    WindowsUpdateId = updateId,
                    WindowsUpdateRevision = revision,
                    WindowsUpdateServerSelection = source.ServerSelection,
                    WindowsUpdateServiceId = source.ServiceId,
                    DriverInstallMode = DriverInstallModes.WindowsUpdate,
                    SourceConfidence = "Alta · applicabilità calcolata da Windows Update",
                    CompatibilityDetail = string.IsNullOrWhiteSpace(hardwareId)
                        ? "Compatibilità determinata da Windows Update Agent"
                        : $"ID hardware proposto: {hardwareId}"
                });
            }

            LogService.Write($"{source.Label} ha trovato {updates.Count} aggiornamenti driver applicabili.");
            return updates;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                $"ricerca non completata (0x{ex.HResult:X8})", ex);
        }
        finally
        {
            ReleaseCom(resultObject);
            ReleaseCom(searcherObject);
            ReleaseCom(sessionObject);
        }
    }

    private static List<UpdateSource> BuildSearchSources()
    {
        var sources = new List<UpdateSource>
        {
            new(DefaultServer, "", "Windows Update configurato"),
            new(WindowsUpdateServer, "", "Windows Update online", IsWindowsUpdateFallback: true)
        };

        object? managerObject = null;
        object? servicesObject = null;
        try
        {
            var managerType = Type.GetTypeFromProgID("Microsoft.Update.ServiceManager");
            if (managerType is null) return sources;
            managerObject = Activator.CreateInstance(managerType);
            if (managerObject is null) return sources;
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
                    break;
                }
                finally
                {
                    ReleaseCom(serviceObject);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Write("Enumerazione delle sorgenti Microsoft Update non riuscita.", ex);
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
        PlanItem planItem, Action<int, string>? progress = null)
    {
        EnsureWindows();
        object? sessionObject = null;
        object? searcherObject = null;
        object? searchResultObject = null;
        object? collectionObject = null;

        try
        {
            progress?.Invoke(8, "Ricerca del driver nella sorgente Windows Update...");
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                ?? throw new InvalidOperationException("Windows Update Agent non è disponibile.");
            sessionObject = Activator.CreateInstance(sessionType)!;
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

            dynamic? selectedUpdate = null;
            int count = searchResult.Updates.Count;
            for (var index = 0; index < count; index++)
            {
                dynamic candidate = searchResult.Updates.Item(index);
                var candidateId = Convert.ToString(candidate.Identity.UpdateID);
                var candidateRevision = Convert.ToInt32(candidate.Identity.RevisionNumber);
                if (candidateId == planItem.WindowsUpdateId && candidateRevision == planItem.WindowsUpdateRevision)
                {
                    selectedUpdate = candidate;
                    break;
                }
            }

            if (selectedUpdate is null)
                return Failed(planItem, "L'aggiornamento non è più proposto da Windows Update. Esegui una nuova scansione.");

            if (!Convert.ToBoolean(selectedUpdate.EulaAccepted))
                selectedUpdate.AcceptEula();

            var collectionType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")
                ?? throw new InvalidOperationException("Raccolta Windows Update non disponibile.");
            collectionObject = Activator.CreateInstance(collectionType)!;
            dynamic collection = collectionObject;
            collection.Add(selectedUpdate);

            progress?.Invoke(35, "Download del driver tramite Windows Update...");
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = collection;
            dynamic downloadResult = downloader.Download();
            int downloadCode = Convert.ToInt32(downloadResult.ResultCode);
            if (downloadCode is not (2 or 3))
                return Failed(planItem, $"Download del driver non riuscito (codice {downloadCode}).");

            progress?.Invoke(72, "Installazione del driver tramite Windows Update...");
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = collection;
            dynamic installResult = installer.Install();
            int resultCode = Convert.ToInt32(installResult.ResultCode);
            bool reboot = Convert.ToBoolean(installResult.RebootRequired);
            bool success = resultCode is 2 or 3;
            progress?.Invoke(98, "Verifica del risultato restituito da Windows Update...");

            return new ItemRunResult
            {
                Id = planItem.Id,
                Name = planItem.Name,
                Kind = planItem.Kind,
                Success = success,
                RestartRequired = reboot,
                Message = success ? "Driver installato da Windows Update." : $"Installazione non riuscita (codice {resultCode})."
            };
        }
        catch (Exception ex)
        {
            LogService.Write($"Errore installazione driver {planItem.Name}.", ex);
            return Failed(planItem, ex.Message);
        }
        finally
        {
            ReleaseCom(collectionObject);
            ReleaseCom(searchResultObject);
            ReleaseCom(searcherObject);
            ReleaseCom(sessionObject);
        }
    }

    private static ItemRunResult Failed(PlanItem item, string message) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Kind = item.Kind,
        Success = false,
        Message = message
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

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private static string? SafeString(Func<string?> getter) { try { return getter(); } catch { return null; } }
    private static DateTime? SafeDate(Func<DateTime> getter) { try { return getter(); } catch { return null; } }
    private static long SafeLong(Func<long> getter) { try { return getter(); } catch { return 0; } }
    private static bool SafeBool(Func<bool> getter) { try { return getter(); } catch { return false; } }
    private static bool? SafeNullableBool(Func<bool> getter) { try { return getter(); } catch { return null; } }

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
