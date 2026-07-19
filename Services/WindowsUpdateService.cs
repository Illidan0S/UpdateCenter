using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class WindowsUpdateService
{
    public Task<IReadOnlyList<UpdateItem>> ScanDriversAsync(
        CancellationToken cancellationToken,
        IReadOnlyList<DriverInventoryItem>? installedDrivers = null) =>
        Task.Run<IReadOnlyList<UpdateItem>>(
            () => ScanDrivers(cancellationToken, installedDrivers ?? []), cancellationToken);

    private static List<UpdateItem> ScanDrivers(
        CancellationToken cancellationToken,
        IReadOnlyList<DriverInventoryItem> installedDrivers)
    {
        EnsureWindows();
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
            resultObject = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'");
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
                    Source = $"Windows Update · {className}",
                    Size = FormatBytes(size),
                    RequiresRestart = reboot,
                    IsImportant = isImportant,
                    IsOptional = !isImportant && autoSelected == false,
                    WindowsUpdateId = updateId,
                    WindowsUpdateRevision = revision
                });
            }

            LogService.Write($"Windows Update ha trovato {updates.Count} aggiornamenti driver.");
            return updates;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Windows Update non ha completato la ricerca (0x{ex.HResult:X8}).", ex);
        }
        finally
        {
            ReleaseCom(resultObject);
            ReleaseCom(searcherObject);
            ReleaseCom(sessionObject);
        }
    }

    public static ItemRunResult InstallDriver(PlanItem planItem)
    {
        EnsureWindows();
        object? sessionObject = null;
        object? searcherObject = null;
        object? searchResultObject = null;
        object? collectionObject = null;

        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                ?? throw new InvalidOperationException("Windows Update Agent non è disponibile.");
            sessionObject = Activator.CreateInstance(sessionType)!;
            dynamic session = sessionObject;
            session.ClientApplicationID = "Update Center";
            searcherObject = session.CreateUpdateSearcher();
            dynamic searcher = searcherObject;
            searchResultObject = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'");
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

            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = collection;
            dynamic downloadResult = downloader.Download();
            int downloadCode = Convert.ToInt32(downloadResult.ResultCode);
            if (downloadCode is not (2 or 3))
                return Failed(planItem, $"Download del driver non riuscito (codice {downloadCode}).");

            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = collection;
            dynamic installResult = installer.Install();
            int resultCode = Convert.ToInt32(installResult.ResultCode);
            bool reboot = Convert.ToBoolean(installResult.RebootRequired);
            bool success = resultCode is 2 or 3;

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
}
