using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class OfficialDriverPackageService
{
    private const long MaximumDownloadBytes = 1024L * 1024 * 1024;
    private static readonly string[] ForbiddenPackageExtensions =
        [".exe", ".msi", ".msp", ".appx", ".msix", ".bat", ".cmd", ".ps1", ".vbs", ".js"];

    public static ItemRunResult Install(PlanItem item, Action<int, string>? progress = null)
    {
        var workRoot = Path.Combine(Path.GetTempPath(), "UpdateCenter", "driver-" + Guid.NewGuid().ToString("N"));
        var currentPhase = "official-inf-prepare";
        try
        {
            progress?.Invoke(10, "Verifica del piano di installazione driver...");
            ValidatePlan(item);
            Directory.CreateDirectory(workRoot);
            var packagePath = Path.Combine(workRoot,
                item.DriverPackageType.Equals("cab-inf", StringComparison.OrdinalIgnoreCase) ? "driver.cab" : "driver.zip");
            currentPhase = "official-inf-download";
            progress?.Invoke(15, "Download del pacchetto driver ufficiale...");
            DownloadVerified(item, packagePath, progress);

            var extractPath = Path.Combine(workRoot, "extracted");
            Directory.CreateDirectory(extractPath);
            currentPhase = "official-inf-extract";
            progress?.Invoke(62, "Estrazione sicura del pacchetto driver...");
            if (item.DriverPackageType.Equals("zip-inf", StringComparison.OrdinalIgnoreCase))
                ExtractZipSafely(packagePath, extractPath);
            else
                ExtractCab(packagePath, extractPath);

            RejectCompanionApplications(extractPath);
            currentPhase = "official-inf-hardware-validation";
            progress?.Invoke(72, "Verifica della compatibilita con il dispositivo...");
            var matchingInfs = FindMatchingInfs(extractPath, item.CompatibleHardwareIds);
            if (matchingInfs.Count == 0)
                return Failed(
                    item,
                    "Pacchetto rifiutato: nessun INF contiene uno degli ID hardware verificati.",
                    currentPhase);

            currentPhase = "official-inf-signature-validation";
            for (var infIndex = 0; infIndex < matchingInfs.Count; infIndex++)
            {
                var infPath = matchingInfs[infIndex];
                progress?.Invoke(80 + (int)(7d * infIndex / Math.Max(1, matchingInfs.Count)),
                    $"Verifica firma: {Path.GetFileName(infPath)}...");
                VerifyCatalogSignature(infPath, item.ExpectedSignerSubjects, extractPath);
            }

            var messages = new List<string>();
            var installerDiagnostics = new List<string>();
            var restartRequired = false;
            int? installerResultCode = null;
            currentPhase = "official-inf-install";
            for (var infIndex = 0; infIndex < matchingInfs.Count; infIndex++)
            {
                var infPath = matchingInfs[infIndex];
                progress?.Invoke(88 + (int)(8d * infIndex / Math.Max(1, matchingInfs.Count)),
                    $"Installazione del driver {Path.GetFileName(infPath)}...");
                var result = ProcessRunner.RunAsync(
                    "pnputil.exe",
                    ["/add-driver", infPath, "/install"],
                    CancellationToken.None,
                    TimeSpan.FromMinutes(90)).GetAwaiter().GetResult();
                installerResultCode = result.ExitCode;
                var output = string.Join(" ", (result.StandardOutput + "\n" + result.StandardError)
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).TakeLast(5));
                installerDiagnostics.Add(
                    $"{Path.GetFileName(infPath)}: code={result.ExitCode}; pid={result.ProcessId?.ToString() ?? "n/d"}; " +
                    $"duration={result.Duration?.ToString() ?? "n/d"}; command={result.CommandLine}; output={output}");
                var pnputilRestartRequired = result.ExitCode is 1641 or 3010 ||
                                             output.Contains("restart required", StringComparison.OrdinalIgnoreCase) ||
                                             output.Contains("reboot required", StringComparison.OrdinalIgnoreCase) ||
                                             output.Contains("riavvio richiesto", StringComparison.OrdinalIgnoreCase) ||
                                             output.Contains("riavvio necessario", StringComparison.OrdinalIgnoreCase) ||
                                             output.Contains("necessario riavviare", StringComparison.OrdinalIgnoreCase);
                restartRequired |= pnputilRestartRequired;
                if (!result.Success && !pnputilRestartRequired)
                {
                    var verificationAfterError = VerifyInstalledDriver(item, restartRequired);
                    if (verificationAfterError.Verified)
                    {
                        return new ItemRunResult
                        {
                            Id = item.Id,
                            Name = item.Name,
                            Kind = item.Kind,
                            Success = true,
                            InstallerSucceeded = false,
                            Verified = true,
                            VerificationStatus = UpdateVerificationStatuses.Verified,
                            ResultCode = result.ExitCode,
                            Phase = currentPhase,
                            Outcome = UpdateOutcomes.Completed,
                            RestartRequired = restartRequired,
                            Message = "PnPUtil ha restituito un errore, ma la versione target del driver risulta installata.",
                            Diagnostics = string.Join(Environment.NewLine, installerDiagnostics) +
                                          Environment.NewLine + "Verifica post-installazione: " +
                                          verificationAfterError.Diagnostics
                        };
                    }
                    return Failed(
                        item,
                        string.IsNullOrWhiteSpace(output)
                            ? $"PnPUtil ha restituito il codice {result.ExitCode}."
                            : output,
                        currentPhase,
                        result.ExitCode,
                        string.Join(Environment.NewLine, installerDiagnostics) +
                        Environment.NewLine + "Verifica post-installazione: " +
                        verificationAfterError.Diagnostics);
                }
                messages.Add(Path.GetFileName(infPath));
            }

            currentPhase = "official-inf-verification";
            progress?.Invoke(99, "Verifica finale della versione installata...");
            var verification = VerifyInstalledDriver(item, restartRequired);
            var decision = UpdateResultPolicy.Resolve(true, restartRequired, verification);
            return new ItemRunResult
            {
                Id = item.Id,
                Name = item.Name,
                Kind = item.Kind,
                Success = decision.Success,
                InstallerSucceeded = true,
                Verified = decision.Verified,
                VerificationStatus = decision.VerificationStatus,
                ResultCode = installerResultCode,
                Phase = currentPhase,
                Outcome = decision.Outcome,
                RestartRequired = restartRequired,
                Message = verification.Verified
                    ? $"Driver INF ufficiale installato e verificato ({string.Join(", ", messages)}). Nessuna app del produttore è stata eseguita."
                    : $"Driver INF ufficiale installato ({string.Join(", ", messages)}). {verification.Message}",
                Diagnostics = string.Join(Environment.NewLine, installerDiagnostics) +
                              (string.IsNullOrWhiteSpace(verification.Diagnostics)
                                  ? ""
                                  : Environment.NewLine + "Verifica post-installazione: " + verification.Diagnostics)
            };
        }
        catch (Exception ex)
        {
            LogService.Write($"Installazione del pacchetto driver ufficiale {item.Name} rifiutata o fallita.", ex);
            return Failed(item, ex.Message, currentPhase, ex.HResult, ex.ToString());
        }
        finally
        {
            TryDeleteVerifiedWorkDirectory(workRoot);
        }
    }

    private static void ValidatePlan(PlanItem item)
    {
        if (!item.DriverInstallMode.Equals(DriverInstallModes.OfficialInfPackage, StringComparison.Ordinal))
            throw new InvalidOperationException("Modalità di installazione driver non valida.");
        if (Regex.IsMatch(item.Name, @"\b(bios|uefi|firmware)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidOperationException("BIOS e firmware non possono essere installati automaticamente.");
        if (!item.DriverPackageType.Equals("zip-inf", StringComparison.OrdinalIgnoreCase) &&
            !item.DriverPackageType.Equals("cab-inf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sono ammessi soltanto pacchetti ZIP/CAB con driver INF.");
        if (!OfficialDriverCatalogService.IsOfficialUri(item.Vendor, item.OfficialDownloadUrl) ||
            !OfficialDriverCatalogService.IsOfficialUri(item.Vendor, item.OfficialReleasePageUrl))
            throw new InvalidOperationException("Il download non appartiene a un dominio ufficiale consentito.");
        if (!Regex.IsMatch(item.ExpectedSha256, "^[A-Fa-f0-9]{64}$"))
            throw new InvalidOperationException("Hash SHA-256 atteso non valido.");
        if (item.ExpectedSignerSubjects.Count == 0 || item.CompatibleHardwareIds.Count == 0)
            throw new InvalidOperationException("Firmatario o ID hardware verificato mancante.");
        OfficialDriverCatalogService.ValidateAuthorizedPackagePlan(item);
    }

    private static void DownloadVerified(
        PlanItem item,
        string destinationPath,
        Action<int, string>? progress)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UpdateCenter/1.1.4");
        var current = new Uri(item.OfficialDownloadUrl, UriKind.Absolute);
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            if (!OfficialDriverCatalogService.IsOfficialUri(item.Vendor, current.AbsoluteUri))
                throw new InvalidOperationException("Reindirizzamento verso un dominio non ufficiale rifiutato.");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location
                    ?? throw new InvalidOperationException("Reindirizzamento del download privo di destinazione.");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length > MaximumDownloadBytes)
                throw new InvalidOperationException("Pacchetto driver troppo grande.");
            using var source = response.Content.ReadAsStream();
            using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[128 * 1024];
            long total = 0;
            var expectedLength = response.Content.Headers.ContentLength;
            while (true)
            {
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > MaximumDownloadBytes) throw new InvalidOperationException("Pacchetto driver troppo grande.");
                destination.Write(buffer, 0, read);
                if (expectedLength is > 0)
                {
                    var percent = 15 + (int)Math.Min(42, total * 42d / expectedLength.Value);
                    progress?.Invoke(percent,
                        $"Download driver: {FormatBytes(total)} di {FormatBytes(expectedLength.Value)}...");
                }
            }
            destination.Flush(true);
            progress?.Invoke(58, "Verifica dell'hash SHA-256 del pacchetto...");
            VerifyHash(destinationPath, item.ExpectedSha256);
            return;
        }
        throw new InvalidOperationException("Troppi reindirizzamenti durante il download del driver.");
    }

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pacchetto driver rifiutato: hash SHA-256 non corrispondente.");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private static void ExtractZipSafely(string packagePath, string destinationRoot)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            total += entry.Length;
            if (total > MaximumDownloadBytes * 2)
                throw new InvalidOperationException("Archivio driver espanso oltre il limite consentito.");
            var target = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            EnsureInside(target, destinationRoot);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open();
            using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(destination);
        }
    }

    private static void ExtractCab(string packagePath, string destinationRoot)
    {
        var result = ProcessRunner.RunAsync(
            "expand.exe", ["-F:*", packagePath, destinationRoot], CancellationToken.None, TimeSpan.FromMinutes(5))
            .GetAwaiter().GetResult();
        if (!result.Success)
            throw new InvalidOperationException("Estrazione del pacchetto CAB non riuscita.");
    }

    private static void RejectCompanionApplications(string root)
    {
        var forbidden = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => ForbiddenPackageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        if (forbidden is not null)
            throw new InvalidOperationException($"Pacchetto rifiutato: contiene un'applicazione o script ({Path.GetFileName(forbidden)}). Sono ammessi solo driver INF.");
    }

    private static List<string> FindMatchingInfs(string root, IReadOnlyList<string> hardwareIds)
    {
        var normalizedIds = hardwareIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Directory.EnumerateFiles(root, "*.inf", SearchOption.AllDirectories)
            .Where(path =>
            {
                var text = NormalizeId(File.ReadAllText(path, Encoding.Default));
                return normalizedIds.Any(text.Contains);
            })
            .ToList();
    }

    private static void VerifyCatalogSignature(string infPath, IReadOnlyList<string> expectedSigners, string root)
    {
        var infText = File.ReadAllText(infPath, Encoding.Default);
        var catalogNames = Regex.Matches(infText, @"(?im)^\s*CatalogFile(?:\.[^=\r\n]+)?\s*=\s*(?<name>[^;\r\n]+)")
            .Select(match => match.Groups["name"].Value.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (catalogNames.Count == 0)
            throw new InvalidOperationException($"Pacchetto rifiutato: {Path.GetFileName(infPath)} non dichiara un catalogo firmato.");

        foreach (var catalogName in catalogNames)
        {
            var catalogPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(infPath)!, catalogName));
            EnsureInside(catalogPath, root);
            if (!File.Exists(catalogPath)) continue;
            var signature = ReadAuthenticodeSignature(catalogPath);
            if (signature.Status.Equals("Valid", StringComparison.OrdinalIgnoreCase) &&
                expectedSigners.Any(expected => signature.Subject.Contains(expected, StringComparison.OrdinalIgnoreCase)))
                return;
        }
        throw new InvalidOperationException($"Pacchetto rifiutato: firma Authenticode non valida o firmatario inatteso per {Path.GetFileName(infPath)}.");
    }

    private static SignatureInfo ReadAuthenticodeSignature(string path)
    {
        var escaped = path.Replace("'", "''", StringComparison.Ordinal);
        var command = "$s=Get-AuthenticodeSignature -LiteralPath '" + escaped + "';" +
                      "[pscustomobject]@{Status=[string]$s.Status;Subject=[string]$s.SignerCertificate.Subject}|ConvertTo-Json -Compress";
        var result = ProcessRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command],
            CancellationToken.None,
            TimeSpan.FromMinutes(1)).GetAwaiter().GetResult();
        if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new InvalidOperationException("Impossibile verificare la firma Authenticode del catalogo driver.");
        return JsonSerializer.Deserialize<SignatureInfo>(result.StandardOutput.Trim(),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("Risposta della verifica firma non valida.");
    }

    private static UpdateVerificationResult VerifyInstalledDriver(PlanItem item, bool restartRequired)
    {
        UpdateVerificationResult? latest = null;
        var attemptDiagnostics = new List<string>();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            latest = VerifyInstalledDriverOnce(item, restartRequired);
            attemptDiagnostics.Add(
                $"Tentativo inventario {attempt}/3: {latest.Status}. {latest.Diagnostics}");
            if (latest.Verified)
                break;
            if (attempt < 3)
                Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        latest ??= new UpdateVerificationResult
        {
            IsDefinitive = false,
            Status = UpdateVerificationStatuses.Unavailable,
            Message = "Verifica finale dell'inventario non disponibile."
        };
        latest.Diagnostics = string.Join(Environment.NewLine, attemptDiagnostics);
        return latest;
    }

    private static UpdateVerificationResult VerifyInstalledDriverOnce(PlanItem item, bool restartRequired)
    {
        try
        {
            var scan = new HardwareInventoryService()
                .ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
            var expectedIds = item.CompatibleHardwareIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var candidates = scan.Drivers.Where(driver =>
                driver.HardwareIds.Concat(driver.CompatibleIds).Append(driver.DeviceId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeId)
                    .Any(expectedIds.Contains))
                .ToList();

            if (candidates.Count == 0)
            {
                return new UpdateVerificationResult
                {
                    IsDefinitive = !restartRequired,
                    Status = restartRequired
                        ? UpdateVerificationStatuses.PendingRestart
                        : UpdateVerificationStatuses.Failed,
                    Message = restartRequired
                        ? "La verifica dell'inventario verrà completata dopo il riavvio."
                        : "Il dispositivo aggiornato non è stato ritrovato nell'inventario hardware.",
                    Diagnostics = "Nessun dispositivo compatibile trovato nella verifica post-installazione."
                };
            }

            var targetHasVersion = !string.IsNullOrWhiteSpace(item.AvailableVersion) &&
                                   item.AvailableVersion.Any(char.IsDigit);
            var verified = !targetHasVersion || candidates.Any(driver =>
                DriverVersionComparer.Compare(driver.InstalledVersion, item.AvailableVersion) >= 0);
            return new UpdateVerificationResult
            {
                IsDefinitive = verified || !restartRequired,
                Verified = verified,
                Status = verified
                    ? UpdateVerificationStatuses.Verified
                    : restartRequired
                        ? UpdateVerificationStatuses.PendingRestart
                        : UpdateVerificationStatuses.Failed,
                Message = verified
                    ? "Versione driver verificata nell'inventario hardware."
                    : restartRequired
                        ? "La nuova versione non è ancora visibile; verifica da completare dopo il riavvio."
                        : "La versione attesa non risulta installata nell'inventario hardware.",
                Diagnostics = "Versioni rilevate: " + string.Join(", ",
                    candidates.Select(x => $"{x.DeviceName}={x.InstalledVersion}"))
            };
        }
        catch (Exception ex)
        {
            return new UpdateVerificationResult
            {
                IsDefinitive = false,
                Status = restartRequired
                    ? UpdateVerificationStatuses.PendingRestart
                    : UpdateVerificationStatuses.Unavailable,
                Message = restartRequired
                    ? "Verifica finale rinviata al riavvio."
                    : "Verifica finale dell'inventario non disponibile.",
                Diagnostics = ex.ToString()
            };
        }
    }

    private static string NormalizeId(string value) =>
        value.Trim().TrimEnd('\0').Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private static void EnsureInside(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Percorso del pacchetto driver non consentito.");
    }

    private static void TryDeleteVerifiedWorkDirectory(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "UpdateCenter"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(fullPath).StartsWith("driver-", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);
        }
        catch { }
    }

    private static ItemRunResult Failed(
        PlanItem item,
        string message,
        string phase = "official-inf-install",
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
        Outcome = UpdateOutcomes.Failed,
        Message = message,
        Diagnostics = diagnostics
    };

    private sealed class SignatureInfo
    {
        public string Status { get; set; } = "";
        public string Subject { get; set; } = "";
    }
}
