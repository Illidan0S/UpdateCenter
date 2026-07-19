using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class AppUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Illidan0S/UpdateCenter/releases/latest";
    private const string ApiVersion = "2026-03-10";
    private static readonly Regex Sha256Pattern = new("(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public AppUpdateService()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"UpdateCenter/{GetInstalledVersion()}");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, timeout.Token)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("GitHub ha restituito una Release non leggibile.");

        if (release.Draft || release.Prerelease)
            return null;
        if (!SemanticVersion.TryParse(release.TagName, out var availableVersion))
            throw new InvalidOperationException("La Release stabile usa un numero di versione non valido.");

        var installedVersion = GetInstalledVersion();
        if (availableVersion <= installedVersion)
            return null;

        var executableName = $"UpdateCenter-v{availableVersion}.exe";
        var checksumName = executableName + ".sha256";
        var executable = release.Assets.FirstOrDefault(x => x.Name.Equals(executableName, StringComparison.OrdinalIgnoreCase));
        var checksum = release.Assets.FirstOrDefault(x => x.Name.Equals(checksumName, StringComparison.OrdinalIgnoreCase));
        if (executable is null || checksum is null)
            throw new InvalidOperationException("La Release non contiene tutti gli artefatti necessari per l'aggiornamento sicuro.");

        var downloadUri = ValidateGitHubDownloadUri(executable.BrowserDownloadUrl);
        var checksumUri = ValidateGitHubDownloadUri(checksum.BrowserDownloadUrl);
        var releaseUri = ValidateGitHubPageUri(release.HtmlUrl);
        var apiSha256 = NormalizeApiDigest(executable.Digest);

        return new AppUpdateInfo
        {
            InstalledVersion = installedVersion,
            AvailableVersion = availableVersion,
            ReleaseNotes = string.IsNullOrWhiteSpace(release.Body)
                ? "Questa Release non contiene note aggiuntive."
                : release.Body.Trim(),
            DownloadSize = Math.Max(executable.Size, 0),
            DownloadUri = downloadUri,
            Sha256Uri = checksumUri,
            AssetName = executableName,
            ReleasePageUri = releaseUri,
            ApiSha256 = apiSha256
        };
    }

    public async Task DownloadAndStartUpdateAsync(
        AppUpdateInfo update,
        IProgress<AppUpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        Directory.CreateDirectory(AppPaths.UpdatesDirectory);
        CleanupUpdateDownloads();

        progress?.Report(new AppUpdateDownloadProgress(2, "Download della firma SHA-256…"));
        var expectedSha256 = await DownloadExpectedSha256Async(update.Sha256Uri, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(update.ApiSha256) &&
            !expectedSha256.Equals(update.ApiSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La firma SHA-256 pubblicata non coincide con quella registrata da GitHub.");

        var finalPath = Path.Combine(AppPaths.UpdatesDirectory, update.AssetName);
        var partialPath = finalPath + ".part";
        TryDelete(partialPath);
        TryDelete(finalPath);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? update.DownloadSize;
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var destination = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true))
            {
                var buffer = new byte[1024 * 128];
                long received = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    received += read;
                    var percentage = total > 0 ? Math.Clamp(received * 92d / total + 3, 3, 95) : 45;
                    progress?.Report(new AppUpdateDownloadProgress(percentage, $"Download in corso · {FormatBytes(received)}"));
                }
                await destination.FlushAsync(timeout.Token).ConfigureAwait(false);
            }

            progress?.Report(new AppUpdateDownloadProgress(96, "Verifica SHA-256…"));
            var actualSha256 = await ComputeSha256Async(partialPath, cancellationToken).ConfigureAwait(false);
            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Il file scaricato non ha superato la verifica SHA-256.");

            File.Move(partialPath, finalPath, true);
            var targetPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Il percorso dell'eseguibile in uso non è disponibile.");
            if (!Path.GetFileName(targetPath).Equals("UpdateCenter.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("L'aggiornamento automatico richiede UpdateCenter.exe avviato dalla propria installazione.");

            progress?.Report(new AppUpdateDownloadProgress(100, "Download verificato · preparazione del riavvio…"));
            var startInfo = new ProcessStartInfo
            {
                FileName = finalPath,
                WorkingDirectory = AppPaths.UpdatesDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--apply-update");
            startInfo.ArgumentList.Add(targetPath);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(update.AvailableVersion.ToString());
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Impossibile avviare l'applicazione dell'aggiornamento.");
            LogService.Write($"Aggiornamento dell'app v{update.AvailableVersion} scaricato e verificato; applicazione avviata.");
        }
        catch
        {
            TryDelete(partialPath);
            TryDelete(finalPath);
            throw;
        }
    }

    public static int ApplyUpdate(string targetPathArgument, int parentProcessId, string version)
    {
        string? targetPath = null;
        string? backupPath = null;
        string? stagedPath = null;
        try
        {
            var updaterPath = Path.GetFullPath(Environment.ProcessPath
                ?? throw new InvalidOperationException("Percorso del programma di aggiornamento non disponibile."));
            var updatesRoot = Path.GetFullPath(AppPaths.UpdatesDirectory) + Path.DirectorySeparatorChar;
            if (!updaterPath.StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Il programma di aggiornamento non proviene dalla cartella autorizzata.");

            targetPath = Path.GetFullPath(targetPathArgument);
            if (!Path.GetFileName(targetPath).Equals("UpdateCenter.exe", StringComparison.OrdinalIgnoreCase) ||
                updaterPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Percorso di destinazione dell'aggiornamento non valido.");

            WaitForParentExit(parentProcessId);
            if (!File.Exists(targetPath))
                throw new FileNotFoundException("L'eseguibile installato non è stato trovato.", targetPath);

            var targetDirectory = Path.GetDirectoryName(targetPath)
                ?? throw new InvalidOperationException("Cartella di installazione non valida.");
            backupPath = targetPath + ".update-backup";
            stagedPath = targetPath + ".update-new";
            TryDelete(backupPath);
            TryDelete(stagedPath);

            File.Copy(updaterPath, stagedPath, true);
            MoveWithRetries(targetPath, backupPath, false);
            try
            {
                MoveWithRetries(stagedPath, targetPath, false);
                var sourceHash = ComputeSha256(updaterPath);
                var installedHash = ComputeSha256(targetPath);
                if (!sourceHash.Equals(installedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Verifica dell'eseguibile sostituito non riuscita.");

                StartUpdatedApplication(targetPath, backupPath, updaterPath, version);
                LogService.Write($"Aggiornamento dell'app v{version} applicato correttamente.");
                return 0;
            }
            catch
            {
                RestoreBackup(targetPath, backupPath);
                throw;
            }
        }
        catch (Exception ex)
        {
            LogService.Write($"Applicazione dell'aggiornamento app v{version} non riuscita; ripristino della versione precedente.", ex);
            if (targetPath is not null && backupPath is not null)
                RestoreBackup(targetPath, backupPath);
            if (stagedPath is not null) TryDelete(stagedPath);
            if (targetPath is not null && File.Exists(targetPath))
            {
                try { Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = false }); } catch { }
            }
            return 1;
        }
    }

    public static void ScheduleSuccessfulUpdateCleanup(string backupPathArgument, string updaterPathArgument, string version)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var currentPath = Path.GetFullPath(Environment.ProcessPath ?? "");
                var backupPath = Path.GetFullPath(backupPathArgument);
                var updaterPath = Path.GetFullPath(updaterPathArgument);
                var expectedBackup = currentPath + ".update-backup";
                var updatesRoot = Path.GetFullPath(AppPaths.UpdatesDirectory) + Path.DirectorySeparatorChar;
                if (!backupPath.Equals(expectedBackup, StringComparison.OrdinalIgnoreCase) ||
                    !updaterPath.StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase))
                    return;

                await Task.Delay(1500).ConfigureAwait(false);
                await DeleteWithRetriesAsync(backupPath).ConfigureAwait(false);
                await DeleteWithRetriesAsync(updaterPath).ConfigureAwait(false);
                TryDelete(currentPath + ".update-new");
                LogService.Write($"Pulizia dell'aggiornamento app v{version} completata.");
            }
            catch (Exception ex)
            {
                LogService.Write("Pulizia dei file dell'aggiornamento app non completata.", ex);
            }
        });
    }

    private async Task<string> DownloadExpectedSha256Async(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 32 * 1024)
            throw new InvalidDataException("Il file SHA-256 pubblicato è troppo grande.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > 32 * 1024)
                throw new InvalidDataException("Il file SHA-256 pubblicato è troppo grande.");
            memory.Write(buffer, 0, read);
        }
        var text = Encoding.UTF8.GetString(memory.ToArray());
        var match = Sha256Pattern.Match(text);
        if (!match.Success)
            throw new InvalidDataException("Il file SHA-256 pubblicato non è valido.");
        return match.Value.ToUpperInvariant();
    }

    private static SemanticVersion GetInstalledVersion()
    {
        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        return new SemanticVersion(
            Math.Max(assemblyVersion?.Major ?? 0, 0),
            Math.Max(assemblyVersion?.Minor ?? 0, 0),
            Math.Max(assemblyVersion?.Build ?? 0, 0));
    }

    private static Uri ValidateGitHubDownloadUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La Release contiene un collegamento di download non consentito.");
        return uri;
    }

    private static Uri ValidateGitHubPageUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La Release contiene un collegamento non valido.");
        return uri;
    }

    private static string? NormalizeApiDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return null;
        var value = digest[7..].Trim();
        return Sha256Pattern.IsMatch(value) && value.Length == 64 ? value.ToUpperInvariant() : null;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static void WaitForParentExit(int processId)
    {
        if (processId <= 0) return;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(60_000))
                throw new TimeoutException("Update Center non si è chiuso entro il tempo previsto.");
        }
        catch (ArgumentException)
        {
            // Il processo è già terminato.
        }
    }

    private static void StartUpdatedApplication(string targetPath, string backupPath, string updaterPath, string version)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = targetPath,
            WorkingDirectory = Path.GetDirectoryName(targetPath)!,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--update-complete");
        startInfo.ArgumentList.Add(backupPath);
        startInfo.ArgumentList.Add(updaterPath);
        startInfo.ArgumentList.Add(version);
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("La nuova versione non è stata riavviata.");
    }

    private static void RestoreBackup(string targetPath, string backupPath)
    {
        try
        {
            if (!File.Exists(backupPath)) return;
            DeleteWithRetries(targetPath);
            MoveWithRetries(backupPath, targetPath, true);
        }
        catch (Exception ex)
        {
            LogService.Write("Ripristino del vecchio eseguibile non riuscito.", ex);
        }
    }

    private static void CleanupUpdateDownloads()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(AppPaths.UpdatesDirectory, "*", SearchOption.TopDirectoryOnly))
                TryDelete(path);
        }
        catch { }
    }

    private static async Task DeleteWithRetriesAsync(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.Delete(path);
                return;
            }
            catch when (attempt < 7)
            {
                await Task.Delay(500).ConfigureAwait(false);
            }
        }
    }

    private static void DeleteWithRetries(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.Delete(path);
                return;
            }
            catch (Exception) when (attempt < 7)
            {
                Thread.Sleep(400);
            }
        }
    }

    private static void MoveWithRetries(string source, string destination, bool overwrite)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite);
                return;
            }
            catch (Exception) when (attempt < 7)
            {
                Thread.Sleep(400);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("body")] public string Body { get; set; } = "";
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
