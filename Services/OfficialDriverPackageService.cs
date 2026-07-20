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

    public static ItemRunResult Install(PlanItem item)
    {
        var workRoot = Path.Combine(Path.GetTempPath(), "UpdateCenter", "driver-" + Guid.NewGuid().ToString("N"));
        try
        {
            ValidatePlan(item);
            Directory.CreateDirectory(workRoot);
            var packagePath = Path.Combine(workRoot,
                item.DriverPackageType.Equals("cab-inf", StringComparison.OrdinalIgnoreCase) ? "driver.cab" : "driver.zip");
            DownloadVerified(item, packagePath);

            var extractPath = Path.Combine(workRoot, "extracted");
            Directory.CreateDirectory(extractPath);
            if (item.DriverPackageType.Equals("zip-inf", StringComparison.OrdinalIgnoreCase))
                ExtractZipSafely(packagePath, extractPath);
            else
                ExtractCab(packagePath, extractPath);

            RejectCompanionApplications(extractPath);
            var matchingInfs = FindMatchingInfs(extractPath, item.CompatibleHardwareIds);
            if (matchingInfs.Count == 0)
                return Failed(item, "Pacchetto rifiutato: nessun INF contiene uno degli ID hardware verificati.");

            foreach (var infPath in matchingInfs)
                VerifyCatalogSignature(infPath, item.ExpectedSignerSubjects, extractPath);

            var messages = new List<string>();
            var restartRequired = false;
            foreach (var infPath in matchingInfs)
            {
                var result = ProcessRunner.RunAsync(
                    "pnputil.exe",
                    ["/add-driver", infPath, "/install"],
                    CancellationToken.None,
                    TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
                var output = string.Join(" ", (result.StandardOutput + "\n" + result.StandardError)
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).TakeLast(5));
                if (!result.Success)
                    return Failed(item, string.IsNullOrWhiteSpace(output)
                        ? $"PnPUtil ha restituito il codice {result.ExitCode}."
                        : output);
                if (output.Contains("restart", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("riavvio", StringComparison.OrdinalIgnoreCase))
                    restartRequired = true;
                messages.Add(Path.GetFileName(infPath));
            }

            return new ItemRunResult
            {
                Id = item.Id,
                Name = item.Name,
                Kind = item.Kind,
                Success = true,
                RestartRequired = restartRequired,
                Message = $"Driver INF ufficiale installato ({string.Join(", ", messages)}). Nessuna app del produttore è stata eseguita."
            };
        }
        catch (Exception ex)
        {
            LogService.Write($"Installazione del pacchetto driver ufficiale {item.Name} rifiutata o fallita.", ex);
            return Failed(item, ex.Message);
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

    private static void DownloadVerified(PlanItem item, string destinationPath)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UpdateCenter/1.0.3");
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
            while (true)
            {
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > MaximumDownloadBytes) throw new InvalidOperationException("Pacchetto driver troppo grande.");
                destination.Write(buffer, 0, read);
            }
            destination.Flush(true);
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

    private static string NormalizeId(string value) => value.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

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

    private static ItemRunResult Failed(PlanItem item, string message) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Kind = item.Kind,
        Success = false,
        Message = message
    };

    private sealed class SignatureInfo
    {
        public string Status { get; set; } = "";
        public string Subject { get; set; } = "";
    }
}
