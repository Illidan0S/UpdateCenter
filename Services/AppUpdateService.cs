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

public sealed partial class AppUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Illidan0S/UpdateCenter/releases/latest";
    private const string ReleaseApi = "https://api.github.com/repos/Illidan0S/UpdateCenter/releases/tags/";
    private const string ReleasePage = "https://github.com/Illidan0S/UpdateCenter/releases/";
    private const string ApiVersion = "2026-03-10";
    private static readonly Regex Sha256Pattern = new("(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])", RegexOptions.Compiled);
    private static readonly Regex SupportedExecutableNamePattern = new(
        @"^UpdateCenter(?:-v\d+\.\d+\.\d+)?(?:-Portable)?\.exe$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public AppUpdateService() : this(new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = false
    }) { Timeout = Timeout.InfiniteTimeSpan }) { }

    internal AppUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"UpdateCenter/{GetInstalledVersion()}");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
    }

    public Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken) =>
        CheckForUpdateAsync(GetInstalledVersion(), IsInstalledSetup(), cancellationToken);

    internal async Task<AppUpdateInfo?> CheckForUpdateAsync(SemanticVersion installedVersion, bool isInstalledSetup,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, timeout.Token)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("GitHub ha restituito una Release non leggibile.");

        if (release.Draft != false || release.Prerelease != false)
            throw new InvalidDataException("GitHub non ha restituito una release stabile pubblicata.");
        if (!SemanticVersion.TryParse(release.TagName, out var availableVersion) || release.TagName != $"v{availableVersion}")
            throw new InvalidOperationException("La Release stabile usa un numero di versione non valido.");
        var expectedPage = GetReleasePage(availableVersion);
        if (release.HtmlUrl != expectedPage.AbsoluteUri)
            throw new InvalidDataException("La Release contiene un collegamento non valido.");

        if (availableVersion <= installedVersion || availableVersion == ReleasePublisherVerifier.WithdrawnVersion)
            return null;

        var executableName = AssetName(availableVersion.ToString(), isInstalledSetup);
        var checksumName = executableName + ".sha256";
        var executable = ExactUploadedAsset(release, release.TagName, executableName);
        var checksum = ExactUploadedAsset(release, release.TagName, checksumName);
        var downloadUri = ExactAssetUri(executable, release.TagName, executableName);
        var checksumUri = ExactAssetUri(checksum, release.TagName, checksumName);
        var apiSha256 = NormalizeApiDigest(executable.Digest);
        if (executable.Digest is not null && apiSha256 is null)
            throw new InvalidDataException("Il digest dell'asset GitHub non è leggibile.");

        return new AppUpdateInfo
        {
            InstalledVersion = installedVersion,
            AvailableVersion = availableVersion,
            ReleaseNotes = release.Body?.Trim() ?? "",
            DownloadSize = Math.Max(executable.Size, 0),
            DownloadUri = downloadUri,
            Sha256Uri = checksumUri,
            AssetName = executableName,
            IsInstallerPackage = isInstalledSetup,
            ReleasePageUri = expectedPage,
            ApiSha256 = apiSha256
        };
    }

    public async Task DownloadAndStartUpdateAsync(
        AppUpdateInfo update,
        IProgress<AppUpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tag = $"v{update.AvailableVersion}";
        var name = AssetName(update.AvailableVersion.ToString(), update.IsInstallerPackage);
        if (update.AvailableVersion <= GetInstalledVersion() || update.AvailableVersion == ReleasePublisherVerifier.WithdrawnVersion ||
            update.AssetName != name ||
            update.DownloadUri.AbsoluteUri != AssetUrl(tag, name) || update.Sha256Uri.AbsoluteUri != AssetUrl(tag, name + ".sha256"))
            throw new InvalidDataException("Asset di aggiornamento inatteso.");

        AppPaths.EnsureCreated();
        Directory.CreateDirectory(AppPaths.UpdatesDirectory);
        CleanupUpdateDownloads();

        progress?.Report(new AppUpdateDownloadProgress(2, "Download del checksum SHA-256…"));
        var expectedSha256 = await DownloadExpectedSha256Async(update.Sha256Uri, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(update.ApiSha256) &&
            !expectedSha256.Equals(update.ApiSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il checksum SHA-256 pubblicato non coincide con il digest registrato da GitHub.");

        var finalPath = Path.Combine(AppPaths.UpdatesDirectory, update.AssetName);
        var partialPath = finalPath + ".part";
        TryDelete(partialPath);
        TryDelete(finalPath);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var response = await GetAssetResponseAsync(update.DownloadUri, timeout.Token).ConfigureAwait(false);
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
            File.Move(partialPath, finalPath);
            // Keep the same bytes read-locked through digest, trust evaluation and launch.
            using var candidate = File.OpenRead(finalPath);
            var actualSha256 = Convert.ToHexString(await SHA256.HashDataAsync(candidate, cancellationToken).ConfigureAwait(false));
            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Il file scaricato non ha superato la verifica SHA-256.");

            var targetPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Il percorso dell'eseguibile in uso non è disponibile.");
            var explicitlyUnsigned = await ReleasePublisherVerifier.VerifyDownloadAsync(targetPath, finalPath, candidate,
                update, this, cancellationToken).ConfigureAwait(false);
            progress?.Report(new AppUpdateDownloadProgress(100, "Download verificato · preparazione del riavvio…"));
            if (update.IsInstallerPackage)
            {
                var installerStart = CreateSetupSelfUpdateStartInfo(finalPath);
                _ = Process.Start(installerStart)
                    ?? throw new InvalidOperationException("Impossibile avviare l'installer dell'aggiornamento.");
                LogService.Write($"Installer dell'app v{update.AvailableVersion} scaricato e avviato.");
                return;
            }

            if (!IsSupportedUpdateTargetName(Path.GetFileName(targetPath)))
                throw new InvalidOperationException("Il nome dell'eseguibile non consente un aggiornamento automatico sicuro.");

            if (explicitlyUnsigned)
            {
                await PortableSourceUpdate.StartAsync(targetPath, update.AvailableVersion, this, cancellationToken).ConfigureAwait(false);
                return;
            }

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
        // The legacy process delegates replacement to this executable. If its
        // apphost cannot start, it never reaches the replacement code.
        if (!SelfUpdateCompatibility.IsSupported()) return 1;

        string? targetPath = null;
        byte[]? originalHash = null;
        try
        {
            var updaterPath = Path.GetFullPath(Environment.ProcessPath
                ?? throw new InvalidOperationException("Percorso del programma di aggiornamento non disponibile."));
            var updatesRoot = Path.GetFullPath(AppPaths.UpdatesDirectory) + Path.DirectorySeparatorChar;
            if (parentProcessId <= 0 || version != GetInstalledVersion().ToString() ||
                !Path.GetDirectoryName(updaterPath)!.Equals(Path.TrimEndingDirectorySeparator(updatesRoot), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(updaterPath).Equals($"UpdateCenter-v{version}-Portable.exe", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Il programma di aggiornamento non proviene dalla cartella autorizzata.");

            targetPath = Path.GetFullPath(targetPathArgument);
            if (!IsSupportedUpdateTargetName(Path.GetFileName(targetPath)) ||
                targetPath.StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase) ||
                updaterPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Percorso di destinazione dell'aggiornamento non valido.");

            WaitForParentExit(parentProcessId, targetPath);
            if (!File.Exists(targetPath))
                throw new FileNotFoundException("L'eseguibile installato non è stato trovato.", targetPath);

            using var updater = File.OpenRead(updaterPath);
            using (var installed = File.OpenRead(targetPath))
            {
                ReleasePublisherVerifier.VerifyReplacementAsync(targetPath, installed, updaterPath, updater, version,
                    new AppUpdateService()).GetAwaiter().GetResult();
                installed.Position = 0;
                originalHash = SHA256.HashData(installed);
            }

            ReplacePortable(targetPath, updaterPath, updater, originalHash, version, PortableUpdateStartup.WaitForWindow);
            LogService.Write($"Aggiornamento dell'app v{version} applicato correttamente.");
            return 0;
        }
        catch (Exception ex)
        {
            LogService.Write($"Applicazione dell'aggiornamento app v{version} non riuscita.", ex);
            if (originalHash is not null && targetPath is not null)
                RestartVerifiedOriginal(targetPath, originalHash);
            return 1;
        }
    }

    internal static void RestartVerifiedOriginal(string targetPath, byte[] hash, Action<string>? launch = null)
    {
        if (File.Exists(targetPath + ".update-backup")) return;
        try
        {
            using var restored = File.OpenRead(targetPath);
            if ((File.GetAttributes(targetPath) & FileAttributes.ReparsePoint) != 0 ||
                !CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(restored))) return;
            if (launch is not null) launch(targetPath);
            else Process.Start(new ProcessStartInfo(targetPath)
            {
                WorkingDirectory = Path.GetDirectoryName(targetPath)!, UseShellExecute = false
            })?.Dispose();
        }
        catch { }
    }

    internal static void ReplacePortable(string targetPath, string updaterPath, Stream updater, byte[] originalHash,
        string version, Action<string, string, string, string> confirm)
    {
        var backupPath = targetPath + ".update-backup";
        var stagedPath = targetPath + ".update-new-" + Guid.NewGuid().ToString("N");
        var created = false;
        try
        {
            using (var staged = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                updater.Position = 0;
                updater.CopyTo(staged);
            }
            updater.Position = 0;
            var expected = SHA256.HashData(updater);
            PortableUpdateTransaction.Apply(targetPath, stagedPath, backupPath, originalHash, expected, () =>
            {
                using var installed = File.OpenRead(targetPath);
                if (!expected.AsSpan().SequenceEqual(SHA256.HashData(installed)))
                    throw new InvalidDataException("Verifica dell'eseguibile sostituito non riuscita.");
                confirm(targetPath, backupPath, updaterPath, version);
            });
        }
        catch
        {
            if (created) TryDelete(stagedPath);
            throw;
        }
    }

    internal static bool IsValidStartupConfirmation(string[] args)
    {
        var currentPath = Environment.ProcessPath;
        return currentPath is not null && args[3] == GetInstalledVersion().ToString() &&
            args[1].Equals(currentPath + ".update-backup", StringComparison.OrdinalIgnoreCase) &&
            args[2].Equals(Path.Combine(AppPaths.UpdatesDirectory, $"UpdateCenter-v{args[3]}-Portable.exe"),
                StringComparison.OrdinalIgnoreCase) &&
            PortableUpdateStartup.IsValidName(args[4]);
    }

    internal static bool MatchesAssetVersion(string version, string? productText, string? fileText,
        Version fixedProduct, Version fixedFile) =>
        productText?.Trim() == version && fileText?.Trim() == version + ".0" &&
        fixedFile.ToString() == version + ".0" && fixedFile == fixedProduct;

    internal async Task VerifyOfficialAssetAsync(string version, bool setup, string path, Stream file, CancellationToken cancellationToken)
    {
        if (!SemanticVersion.TryParse(version, out var parsed) || parsed.ToString() != version)
            throw new InvalidDataException("Versione dell'asset non valida.");
        var info = FileVersionInfo.GetVersionInfo(path);
        var fixedProduct = new Version(info.ProductMajorPart, info.ProductMinorPart, info.ProductBuildPart, info.ProductPrivatePart);
        var fixedFile = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        if (!MatchesAssetVersion(version, info.ProductVersion, info.FileVersion, fixedProduct, fixedFile))
            throw new InvalidDataException("Versione del file di aggiornamento inattesa.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var tag = $"v{version}";
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi + tag);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(body, JsonOptions, token).ConfigureAwait(false);
        if (release is null || release.TagName != tag || release.Draft != false || release.Prerelease != false ||
            release.HtmlUrl != ReleasePage + "tag/" + tag)
            throw new InvalidDataException("Release ufficiale stabile non verificata.");

        var name = AssetName(version, setup);
        var asset = ExactUploadedAsset(release, tag, name);
        var checksum = ExactUploadedAsset(release, tag, name + ".sha256");
        var apiDigest = NormalizeApiDigest(asset.Digest);
        if (apiDigest is null || asset.Size <= 0 || asset.Size > 512L * 1024 * 1024 || asset.Size != file.Length)
            throw new InvalidDataException("Digest o dimensione GitHub non verificabile.");

        var sidecar = await DownloadChecksumTextAsync(new Uri(checksum.BrowserDownloadUrl), token).ConfigureAwait(false);
        if (!sidecar.TrimEnd('\r', '\n').Equals($"{apiDigest}  {name}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checksum dell'asset GitHub non coerente.");
        file.Position = 0;
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
        if (actual != apiDigest) throw new InvalidDataException("Il file non coincide con l'asset GitHub ufficiale.");
    }

    private async Task<string> DownloadExpectedSha256Async(Uri uri, CancellationToken cancellationToken)
    {
        var text = await DownloadChecksumTextAsync(uri, cancellationToken).ConfigureAwait(false);
        var match = Sha256Pattern.Match(text);
        if (!match.Success)
            throw new InvalidDataException("Il file SHA-256 pubblicato non è valido.");
        return match.Value.ToUpperInvariant();
    }

    private async Task<string> DownloadChecksumTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await GetAssetResponseAsync(uri, timeout.Token).ConfigureAwait(false);
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
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private async Task<HttpResponseMessage> GetAssetResponseAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
        var location = response.Headers.Location;
        response.Dispose();
        if (location is null || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps ||
            !location.IsDefaultPort || location.UserInfo.Length != 0 || location.Fragment.Length != 0 ||
            location.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
            throw new InvalidDataException("Redirect dell'asset GitHub non consentito.");
        using var redirected = new HttpRequestMessage(HttpMethod.Get, location);
        return await _httpClient.SendAsync(redirected, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
    }

    internal static SemanticVersion GetInstalledVersion()
    {
        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        return new SemanticVersion(
            Math.Max(assemblyVersion?.Major ?? 0, 0),
            Math.Max(assemblyVersion?.Minor ?? 0, 0),
            Math.Max(assemblyVersion?.Build ?? 0, 0));
    }

    private static ProcessStartInfo CreateSetupSelfUpdateStartInfo(string installerPath)
    {
        var installerStart = new ProcessStartInfo
        {
            FileName = installerPath,
            WorkingDirectory = AppPaths.UpdatesDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };
        installerStart.ArgumentList.Add("/VERYSILENT");
        installerStart.ArgumentList.Add("/NORESTART");
        installerStart.ArgumentList.Add("/CLOSEAPPLICATIONS");
        installerStart.ArgumentList.Add("/SUPPRESSMSGBOXES");
        installerStart.ArgumentList.Add("/SELFUPDATE");
        return installerStart;
    }

    private static string AssetName(string version, bool setup) => setup
        ? $"UpdateCenter-Setup-v{version}.exe"
        : $"UpdateCenter-v{version}-Portable.exe";

    private static string AssetUrl(string tag, string name) => $"{ReleasePage}download/{tag}/{name}";

    internal static Uri GetReleasePage(SemanticVersion version) => new($"{ReleasePage}tag/v{version}");

    private static Uri ExactAssetUri(GitHubAsset asset, string tag, string name)
    {
        if (asset.BrowserDownloadUrl != AssetUrl(tag, name))
            throw new InvalidDataException("La Release contiene un collegamento di download non consentito.");
        return new Uri(asset.BrowserDownloadUrl);
    }

    private static GitHubAsset ExactUploadedAsset(GitHubRelease release, string tag, string name)
    {
        var assets = release.Assets.Where(asset => asset.Name == name).ToArray();
        if (assets.Length != 1 || assets[0].State != "uploaded")
            throw new InvalidDataException("Asset GitHub ufficiale mancante o ambiguo.");
        _ = ExactAssetUri(assets[0], tag, name);
        return assets[0];
    }

    private static string? NormalizeApiDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return null;
        var value = digest[7..].Trim();
        return Sha256Pattern.IsMatch(value) && value.Length == 64 ? value.ToUpperInvariant() : null;
    }

    internal static bool IsSupportedUpdateTargetName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && SupportedExecutableNamePattern.IsMatch(fileName);

    private static bool IsInstalledSetup()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) return false;

        var directory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(directory)) return false;
        if (Directory.EnumerateFiles(directory, "unins*.exe", SearchOption.TopDirectoryOnly).Any())
            return true;

        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "UpdateCenter");
        return directory.TrimEnd(Path.DirectorySeparatorChar)
            .Equals(localPrograms.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static void WaitForParentExit(int processId, string targetPath)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return;
            string? processPath;
            try { processPath = process.MainModule?.FileName; }
            catch (Exception ex) when ((ex is InvalidOperationException or System.ComponentModel.Win32Exception) && process.HasExited)
            {
                // The 1.1.5 window can close while its image is being inspected.
                return;
            }
            if (processPath is null && process.HasExited) return;
            if (processPath is null || !Path.GetFullPath(processPath).Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Il processo precedente non corrisponde alla destinazione.");
            if (!process.WaitForExit(60_000))
                throw new TimeoutException("Update Center non si è chiuso entro il tempo previsto.");
        }
        catch (ArgumentException)
        {
            // Il processo è già terminato.
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
        [JsonPropertyName("draft")] public bool? Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool? Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("state")] public string State { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
