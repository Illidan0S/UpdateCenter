using System.Text;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class WinGetService
{
    private readonly WinGetManifestSafetyService _manifestSafety = new();
    private const int ShellExecuteInstallFailed = unchecked((int)0x8A150006);
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    private const int UpdateInstallTechnologyMismatch = unchecked((int)0x8A15008E);
    private const int InstallUpgradeNotSupported = unchecked((int)0x8A150114);
    private static readonly Regex AnsiEscape = new("\\x1B(?:[@-Z\\\\-_]|\\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);
    private static readonly Regex DividerLine = new("^\\s*-{10,}\\s*$", RegexOptions.Compiled);
    private static readonly Regex HeaderToken = new("\\S+", RegexOptions.Compiled);

    public async Task<IReadOnlyList<UpdateItem>> ScanAsync(bool includeUnknown, CancellationToken cancellationToken)
    {
        var arguments = BuildScanArguments(includeUnknown);
        var result = await ProcessRunner.RunAsync(
            "winget.exe", arguments, cancellationToken, TimeSpan.FromMinutes(5));

        var output = Normalize(result.StandardOutput + Environment.NewLine + result.StandardError);
        LogService.Write($"Scansione WinGet terminata con codice {result.ExitCode}.");

        var parsed = ParseUpgradeTable(output);
        if (parsed.Count > 0)
        {
            var installedRows = await ReadInstalledInventoryAsync(cancellationToken);
            var verified = FilterVerifiedInstalledCandidates(parsed, installedRows);
            var rejectedCount = parsed.Count - verified.Count;
            if (rejectedCount > 0)
                LogService.Write($"Esclusi {rejectedCount} candidati WinGet senza un'installazione locale verificata.");
            var applicable = WinGetApplicabilityStore.ExcludeSuppressed(verified).ToList();
            var suppressedCount = verified.Count - applicable.Count;
            if (suppressedCount > 0)
                LogService.Write($"Esclusi {suppressedCount} aggiornamenti WinGet già verificati come non applicabili per le stesse versioni.");
            await PreparePackageMetadataAsync(applicable, cancellationToken);
            return applicable;
        }

        if (!result.Success && !ContainsNoUpdatesMessage(output))
            throw new InvalidOperationException(SummarizeError(output, "WinGet non ha completato la scansione."));

        return [];
    }

    private static async Task<List<WinGetPackageRow>> ReadInstalledInventoryAsync(
        CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            "winget.exe",
            ["list", "--accept-source-agreements", "--disable-interactivity", "--nowarn"],
            cancellationToken,
            TimeSpan.FromMinutes(5));
        var output = Normalize(result.StandardOutput + Environment.NewLine + result.StandardError);
        if (!result.Success)
            throw new InvalidOperationException(SummarizeError(
                output,
                "WinGet non ha permesso di verificare i programmi realmente installati."));

        var rows = ParsePackageRows(output);
        if (rows.Count == 0)
            throw new InvalidOperationException(
                "WinGet non ha restituito un inventario installato verificabile; gli aggiornamenti software dubbi sono stati esclusi.");
        return rows;
    }

    internal static List<UpdateItem> FilterVerifiedInstalledCandidates(
        IEnumerable<UpdateItem> candidates,
        IEnumerable<WinGetPackageRow> installedRows)
    {
        var installedIds = installedRows
            .Where(row => IsSafePackageId(row.Id))
            .Select(row => row.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates
            .Where(candidate => IsSafePackageId(candidate.Id) && installedIds.Contains(candidate.Id))
            .GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public Task PreparePackageMetadataAsync(
        IReadOnlyList<UpdateItem> items, CancellationToken cancellationToken) =>
        _manifestSafety.ApplyAsync(items, cancellationToken);

    public async Task<WinGetPackageAvailability?> ResolveInstallablePackageAsync(
        string packageId, CancellationToken cancellationToken)
    {
        if (!IsSafePackageId(packageId)) return null;
        var result = await ProcessRunner.RunAsync("winget.exe",
            ["show", "--id", packageId, "--exact", "--source", "winget",
                "--accept-source-agreements", "--disable-interactivity", "--nowarn"],
            cancellationToken, TimeSpan.FromMinutes(2));
        if (!result.Success) return null;

        var output = Normalize(result.StandardOutput + Environment.NewLine + result.StandardError);
        var match = Regex.Match(output,
            @"(?im)^\s*(?:Version|Versione)\s*:\s*(?<version>[^\r\n]+?)\s*$");
        var version = match.Success ? match.Groups["version"].Value.Trim() : "Più recente";
        return new WinGetPackageAvailability(packageId, version);
    }

    public static ProcessResult Install(PlanItem item, bool silent)
    {
        if (!IsSafePackageId(item.Id))
            return new ProcessResult(1, "", "Identificativo WinGet non valido.", "winget install");
        var arguments = new List<string>
        {
            "install", "--id", item.Id, "--exact", "--source", "winget",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity", "--nowarn"
        };
        if (silent) arguments.Add("--silent");
        return ProcessRunner.RunAsync(
            "winget.exe", arguments, CancellationToken.None, TimeSpan.FromMinutes(90)).GetAwaiter().GetResult();
    }

    public static ProcessResult Upgrade(PlanItem item, bool silent)
    {
        var attempts = new List<ProcessResult>();
        var first = RunUpgrade(item, silent, useSource: true, useName: false, interactive: false);
        attempts.Add(first);
        if (first.Success)
            return first;

        if (silent && first.ExitCode == ShellExecuteInstallFailed)
        {
            var installed = QueryInstalled("--id", item.Id);
            attempts.Add(installed.Result);
            var installedRow = installed.Rows.FirstOrDefault(x =>
                x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            if (installedRow is not null && IsVersionAtLeast(installedRow.InstalledVersion, item.AvailableVersion))
                return AlreadyCurrent(attempts, item, installedRow.InstalledVersion);
            return CombineAttempts(attempts, first.ExitCode);
        }

        if (!IsInstalledPackageMatchFailure(first))
            return first;

        // La sorgente può impedire a WinGet di correlare un'app installata per utente.
        var withoutSource = RunUpgrade(item, silent, useSource: false, useName: false, interactive: false);
        attempts.Add(withoutSource);
        if (withoutSource.Success)
            return CombineAttempts(attempts, withoutSource.ExitCode);

        if (!IsInstalledPackageMatchFailure(withoutSource))
            return CombineAttempts(attempts, withoutSource.ExitCode);

        var installedById = QueryInstalled("--id", item.Id);
        attempts.Add(installedById.Result);
        var idRow = installedById.Rows.FirstOrDefault(x =>
            x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
        if (idRow is not null && IsVersionAtLeast(idRow.InstalledVersion, item.AvailableVersion))
            return AlreadyCurrent(attempts, item, idRow.InstalledVersion);

        // Alcuni pacchetti WinGet sono elencati correttamente ma non sono più correlabili tramite ID.
        // Il ripiego sul nome è consentito solo con una singola identità esatta; righe duplicate
        // dello stesso ID e della stessa versione vengono considerate una sola corrispondenza.
        var installedByName = QueryInstalled("--name", item.Name);
        attempts.Add(installedByName.Result);
        var exactNameRow = ResolveExactInstalledMatch(installedByName.Rows, item.Name, item.Id);
        if (exactNameRow is not null)
        {
            if (IsVersionAtLeast(exactNameRow.InstalledVersion, item.AvailableVersion))
                return AlreadyCurrent(attempts, item, exactNameRow.InstalledVersion);

            var byName = RunUpgrade(item, silent, useSource: false, useName: true, interactive: false);
            attempts.Add(byName);
            return CombineAttempts(attempts, byName.ExitCode);
        }

        return CombineAttempts(attempts, withoutSource.ExitCode);
    }

    public static ProcessResult RunInteractive(PlanItem item)
    {
        if (!IsSafePackageId(item.Id))
            return new ProcessResult(1, "", "Identificativo WinGet non valido.", "winget interactive");
        var arguments = BuildInteractiveArguments(item);
        return ProcessRunner.RunAsync(
            "winget.exe", arguments, CancellationToken.None, TimeSpan.FromMinutes(90)).GetAwaiter().GetResult();
    }

    internal static IReadOnlyList<string> BuildInteractiveArguments(PlanItem item)
    {
        var operation = item.PackageOperation.Equals(PackageOperations.Install, StringComparison.Ordinal)
            ? "install"
            : "upgrade";
        return
        [
            operation,
            "--id", item.Id,
            "--exact",
            "--source", "winget",
            "--accept-package-agreements",
            "--accept-source-agreements",
            "--interactive",
            "--nowarn"
        ];
    }

    private static WinGetPackageRow? ResolveExactInstalledMatch(
        IEnumerable<WinGetPackageRow> rows, string expectedName, string expectedId)
    {
        var matches = rows
            .Where(x => x.Name.Equals(expectedName, StringComparison.CurrentCultureIgnoreCase))
            .GroupBy(x => new
            {
                Name = x.Name.ToUpperInvariant(),
                Id = x.Id.ToUpperInvariant(),
                Version = x.InstalledVersion.ToUpperInvariant()
            })
            .Select(x => x.First())
            .ToList();

        if (matches.Count != 1 ||
            !matches[0].Id.Equals(expectedId, StringComparison.OrdinalIgnoreCase))
            return null;

        return matches[0];
    }

    public static List<UpdateItem> ParseUpgradeTable(string output) =>
        ParsePackageRows(output)
            .Where(x => !string.IsNullOrWhiteSpace(x.AvailableVersion))
            .Select(x => new UpdateItem
            {
                Id = x.Id,
                Name = string.IsNullOrWhiteSpace(x.Name) ? x.Id : x.Name,
                Kind = RuntimePackageCatalog.IsRuntimePackageId(x.Id)
                    ? UpdateKind.Runtime
                    : UpdateKind.Software,
                InstalledVersion = string.IsNullOrWhiteSpace(x.InstalledVersion) ? "Sconosciuta" : x.InstalledVersion,
                AvailableVersion = string.IsNullOrWhiteSpace(x.AvailableVersion) ? "Più recente" : x.AvailableVersion,
                Source = string.IsNullOrWhiteSpace(x.Source) ? "WinGet" : x.Source,
                IsImportant = false
            })
            .ToList();

    internal static List<WinGetPackageRow> ParsePackageRows(string output)
    {
        var lines = Normalize(output).Replace("\r", "", StringComparison.Ordinal).Split('\n');
        for (var dividerIndex = 1; dividerIndex < lines.Length; dividerIndex++)
        {
            if (!DividerLine.IsMatch(lines[dividerIndex]))
                continue;

            var headerIndex = dividerIndex - 1;
            while (headerIndex >= 0 && string.IsNullOrWhiteSpace(lines[headerIndex])) headerIndex--;
            if (headerIndex < 0) continue;

            var headerMatches = HeaderToken.Matches(lines[headerIndex]).Cast<Match>().Take(5).ToArray();
            if (headerMatches.Length < 3) continue;

            var headers = headerMatches.Select(x => x.Value.Trim()).ToArray();
            var starts = headerMatches.Select(x => x.Index).ToArray();
            var idIndex = FindHeader(headers, "Id");
            var nameIndex = FindHeader(headers, "Nome", "Name");
            var versionIndex = FindHeader(headers, "Versione", "Version");
            var availableIndex = FindHeader(headers, "Disponibile", "Available");
            var sourceIndex = FindHeader(headers, "Origine", "Source");
            if (idIndex < 0 || nameIndex < 0 || versionIndex < 0) continue;

            var rows = new List<WinGetPackageRow>();
            for (var rowIndex = dividerIndex + 1; rowIndex < lines.Length; rowIndex++)
            {
                var line = lines[rowIndex];
                if (string.IsNullOrWhiteSpace(line)) break;
                if (line.TrimStart().StartsWith('-')) continue;
                if (line.Contains("upgrade available", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("upgrades available", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("aggiornamento disponibile", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("aggiornamenti disponibili", StringComparison.OrdinalIgnoreCase))
                    continue;

                var columns = ReadColumns(line, starts);
                var id = ReadColumn(columns, idIndex);
                if (string.IsNullOrWhiteSpace(id) || id.Equals("Id", StringComparison.OrdinalIgnoreCase))
                    continue;

                rows.Add(new WinGetPackageRow(
                    ReadColumn(columns, nameIndex),
                    id,
                    ReadColumn(columns, versionIndex),
                    ReadColumn(columns, availableIndex),
                    ReadColumn(columns, sourceIndex)));
            }

            if (rows.Count > 0) return rows;
        }

        return [];
    }

    private static ProcessResult RunUpgrade(
        PlanItem item, bool silent, bool useSource, bool useName, bool interactive)
    {
        var arguments = new List<string>
        {
            "upgrade", useName ? "--name" : "--id", useName ? item.Name : item.Id, "--exact",
            "--accept-package-agreements", "--accept-source-agreements", "--nowarn"
        };

        if (interactive)
            arguments.Add("--interactive");
        else
            arguments.Add("--disable-interactivity");
        if (silent && !interactive) arguments.Add("--silent");
        if (useSource && IsSafeSource(item.Source))
        {
            arguments.Add("--source");
            arguments.Add(item.Source);
        }

        return ProcessRunner.RunAsync(
            "winget.exe", arguments, CancellationToken.None, TimeSpan.FromMinutes(90)).GetAwaiter().GetResult();
    }

    private static (ProcessResult Result, List<WinGetPackageRow> Rows) QueryInstalled(
        string selector, string value, TimeSpan? timeout = null)
    {
        var arguments = new[]
        {
            "list", selector, value, "--exact", "--accept-source-agreements", "--disable-interactivity", "--nowarn"
        };
        var result = ProcessRunner.RunAsync(
            "winget.exe", arguments, CancellationToken.None, timeout ?? TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
        return (result, ParsePackageRows(result.StandardOutput + Environment.NewLine + result.StandardError));
    }

    internal static UpdateVerificationResult VerifyInstallation(
        PlanItem item,
        Func<string, string, (ProcessResult Result, List<WinGetPackageRow> Rows)>? queryInstalled = null,
        int maxAttempts = 3,
        Action<TimeSpan>? waitBeforeRetry = null)
    {
        if (!IsSafePackageId(item.Id))
        {
            return new UpdateVerificationResult
            {
                IsDefinitive = true,
                Status = UpdateVerificationStatuses.Failed,
                Message = "Identificativo WinGet non valido durante la verifica post-installazione.",
                Diagnostics = $"PackageId rifiutato: {item.Id}"
            };
        }

        queryInstalled ??= (selector, value) =>
            QueryInstalled(selector, value, TimeSpan.FromSeconds(10));
        waitBeforeRetry ??= static delay => Thread.Sleep(delay);
        maxAttempts = Math.Clamp(maxAttempts, 1, 5);
        var diagnostics = new List<string>();
        UpdateVerificationResult? latest = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var installed = queryInstalled("--id", item.Id);
                latest = EvaluateInstalledQuery(item, installed.Result, installed.Rows);
                diagnostics.Add(
                    $"Tentativo {attempt}/{maxAttempts}: code={installed.Result.ExitCode}; " +
                    $"pid={installed.Result.ProcessId?.ToString() ?? "n/d"}; " +
                    $"duration={installed.Result.Duration?.ToString() ?? "n/d"}; " +
                    $"command={installed.Result.CommandLine}; result={latest.Status}.");
            }
            catch (Exception ex)
            {
                latest = new UpdateVerificationResult
                {
                    IsDefinitive = false,
                    Status = UpdateVerificationStatuses.Unavailable,
                    Message = "Verifica post-installazione non disponibile.",
                    Diagnostics = ex.ToString()
                };
                diagnostics.Add($"Tentativo {attempt}/{maxAttempts}: eccezione={ex.Message}");
            }

            if (latest.Verified)
                break;
            if (attempt < maxAttempts)
                waitBeforeRetry(TimeSpan.FromSeconds(attempt * 3));
        }

        latest ??= new UpdateVerificationResult
        {
            IsDefinitive = false,
            Status = UpdateVerificationStatuses.Unavailable,
            Message = "Verifica post-installazione non disponibile."
        };
        latest.Diagnostics = string.Join(Environment.NewLine, diagnostics) +
                             (string.IsNullOrWhiteSpace(latest.Diagnostics)
                                 ? ""
                                 : Environment.NewLine + latest.Diagnostics);
        return latest;
    }

    private static UpdateVerificationResult EvaluateInstalledQuery(
        PlanItem item,
        ProcessResult result,
        IReadOnlyList<WinGetPackageRow> rows)
    {
        if (!result.Success)
        {
            var packageMissing = IsInstalledPackageMatchFailure(result);
            return new UpdateVerificationResult
            {
                IsDefinitive = packageMissing,
                Status = packageMissing
                    ? UpdateVerificationStatuses.Failed
                    : UpdateVerificationStatuses.Unavailable,
                Message = packageMissing
                    ? "Il pacchetto non risulta installato dopo l'operazione."
                    : "WinGet non ha permesso di verificare lo stato installato dopo l'operazione."
            };
        }

        var row = rows.FirstOrDefault(x =>
            x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return new UpdateVerificationResult
            {
                IsDefinitive = true,
                Status = UpdateVerificationStatuses.Failed,
                Message = "Il pacchetto non risulta installato dopo l'operazione."
            };
        }

        var targetHasVersion = !string.IsNullOrWhiteSpace(item.AvailableVersion) &&
                               item.AvailableVersion.Any(char.IsDigit);
        var verified = !targetHasVersion || IsVersionAtLeast(row.InstalledVersion, item.AvailableVersion);
        return new UpdateVerificationResult
        {
            IsDefinitive = true,
            Verified = verified,
            Status = verified ? UpdateVerificationStatuses.Verified : UpdateVerificationStatuses.Failed,
            Message = verified
                ? $"Versione installata verificata: {row.InstalledVersion}."
                : $"La versione installata ({row.InstalledVersion}) non raggiunge quella attesa ({item.AvailableVersion})."
        };
    }

    private static List<string> BuildScanArguments(bool includeUnknown)
    {
        var arguments = new List<string>
        {
            "upgrade", "--accept-source-agreements", "--disable-interactivity", "--nowarn"
        };
        if (includeUnknown) arguments.Add("--include-unknown");
        return arguments;
    }

    private static bool IsSafeSource(string source) =>
        !string.IsNullOrWhiteSpace(source) && source.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    private static bool IsSafePackageId(string packageId) =>
        !string.IsNullOrWhiteSpace(packageId) && packageId.Length <= 160 &&
        packageId.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '+');

    private static bool IsInstalledPackageMatchFailure(ProcessResult result)
    {
        var output = Normalize(result.StandardOutput + Environment.NewLine + result.StandardError);
        return output.Contains("No installed package found matching input criteria", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Non è stato trovato alcun pacchetto installato corrispondente ai criteri di input", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Nessun pacchetto installato trovato corrispondente ai criteri", StringComparison.OrdinalIgnoreCase);
    }

    public static string ClassifyOutcome(ProcessResult result) => result.ExitCode switch
    {
        UpdateNotApplicable => UpdateOutcomes.NotApplicable,
        UpdateInstallTechnologyMismatch or InstallUpgradeNotSupported => UpdateOutcomes.ManualRequired,
        _ => result.Success ? UpdateOutcomes.Completed : UpdateOutcomes.Failed
    };

    internal static bool IsFileInUseFailure(ProcessResult result)
    {
        if (result.Success)
            return false;

        var output = Normalize(result.StandardOutput + Environment.NewLine + result.StandardError);
        if (result.ExitCode == unchecked((int)0x80070020) ||
            output.Contains("0x80070020", StringComparison.OrdinalIgnoreCase))
            return true;

        var installerFilesUsedByOtherApplication =
            output.Contains(
                "i file modificati dal programma di installazione sono attualmente utilizzati da un'applicazione diversa",
                StringComparison.OrdinalIgnoreCase) &&
            output.Contains("chiudere le applicazioni, quindi riprovare", StringComparison.OrdinalIgnoreCase);
        if (installerFilesUsedByOtherApplication)
            return true;

        string[] exactSignals =
        [
            "the process cannot access the file because it is being used by another process",
            "cannot access the file because it is being used by another process",
            "the file is in use by another process",
            "files are in use by another application",
            "close the following applications before continuing",
            "impossibile accedere al file perché è utilizzato da un altro processo",
            "impossibile accedere al file in quanto utilizzato da un altro processo",
            "file in uso da un altro processo",
            "chiudere le applicazioni seguenti prima di continuare"
        ];
        if (exactSignals.Any(signal => output.Contains(signal, StringComparison.OrdinalIgnoreCase)))
            return true;

        var explicitlyRunning = output.Contains("is already running", StringComparison.OrdinalIgnoreCase) ||
                                output.Contains("is currently running", StringComparison.OrdinalIgnoreCase) ||
                                output.Contains("is still running", StringComparison.OrdinalIgnoreCase) ||
                                output.Contains("è già in esecuzione", StringComparison.OrdinalIgnoreCase) ||
                                output.Contains("è ancora in esecuzione", StringComparison.OrdinalIgnoreCase);
        var explicitlyRequestsClose = output.Contains("please close", StringComparison.OrdinalIgnoreCase) ||
                                      output.Contains("close it before", StringComparison.OrdinalIgnoreCase) ||
                                      output.Contains("close the application", StringComparison.OrdinalIgnoreCase) ||
                                      output.Contains("chiudi l'applicazione", StringComparison.OrdinalIgnoreCase) ||
                                      output.Contains("chiudere l'applicazione", StringComparison.OrdinalIgnoreCase) ||
                                      output.Contains("chiudere il programma", StringComparison.OrdinalIgnoreCase);
        return explicitlyRunning && explicitlyRequestsClose;
    }

    internal static string ClassifyFailureReason(ProcessResult result, bool finalSuccess) =>
        !finalSuccess && IsFileInUseFailure(result)
            ? UpdateFailureReasons.FilesInUse
            : UpdateFailureReasons.None;

    internal static bool RequiresRestart(ProcessResult result)
    {
        if (result.ExitCode is 1641 or 3010)
            return true;
        if (!result.Success)
            return false;
        var output = Normalize(result.StandardOutput + Environment.NewLine + result.StandardError);
        return output.Contains("restart required", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("reboot required", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("riavvio richiesto", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("riavvio necessario", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionAtLeast(string installed, string target)
    {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(target) ||
            !installed.Any(char.IsDigit) || !target.Any(char.IsDigit))
            return false;
        return DriverVersionComparer.Compare(installed, target) >= 0;
    }

    private static ProcessResult AlreadyCurrent(List<ProcessResult> attempts, PlanItem item, string installedVersion)
    {
        var message = $"{item.Name} risulta già aggiornato alla versione {installedVersion}. La scansione precedente non era più attuale.";
        attempts.Add(new ProcessResult(0, message, "", "Verifica WinGet dello stato installato"));
        return CombineAttempts(attempts, 0);
    }

    private static ProcessResult CombineAttempts(IReadOnlyList<ProcessResult> attempts, int exitCode)
    {
        var output = new StringBuilder();
        var errors = new StringBuilder();
        var commands = new StringBuilder();
        for (var index = 0; index < attempts.Count; index++)
        {
            var attempt = attempts[index];
            commands.AppendLine($"Tentativo {index + 1}: {attempt.CommandLine}");
            if (!string.IsNullOrWhiteSpace(attempt.StandardOutput))
                output.AppendLine($"--- Tentativo {index + 1} ---\n{attempt.StandardOutput.Trim()}");
            if (!string.IsNullOrWhiteSpace(attempt.StandardError))
                errors.AppendLine($"--- Tentativo {index + 1} ---\n{attempt.StandardError.Trim()}");
        }
        var processId = attempts.LastOrDefault(x => x.ProcessId.HasValue)?.ProcessId;
        var duration = attempts.Where(x => x.Duration.HasValue)
            .Aggregate(TimeSpan.Zero, (total, attempt) => total + attempt.Duration!.Value);
        return new ProcessResult(
            exitCode,
            output.ToString(),
            errors.ToString(),
            commands.ToString().Trim(),
            processId,
            duration == TimeSpan.Zero ? null : duration);
    }

    private static int FindHeader(IReadOnlyList<string> headers, params string[] candidates)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (candidates.Any(x => headers[index].Equals(x, StringComparison.OrdinalIgnoreCase)))
                return index;
        }
        return -1;
    }

    private static string ReadColumn(IReadOnlyList<string> columns, int index) =>
        index >= 0 && index < columns.Count ? columns[index] : "";

    private static List<string> ReadColumns(string line, IReadOnlyList<int> starts)
    {
        var columns = new List<string>(starts.Count);
        for (var index = 0; index < starts.Count; index++)
        {
            var start = starts[index];
            if (start >= line.Length)
            {
                columns.Add("");
                continue;
            }
            var end = index + 1 < starts.Count ? Math.Min(starts[index + 1], line.Length) : line.Length;
            columns.Add(line[start..end].Trim());
        }
        return columns;
    }

    private static string Normalize(string text) => AnsiEscape.Replace(text, "").Replace("\b", "", StringComparison.Ordinal);

    private static bool ContainsNoUpdatesMessage(string text) =>
        text.Contains("No applicable upgrade found", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Nessun aggiornamento applicabile", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("No installed package found", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Non è stato trovato alcun pacchetto installato", StringComparison.OrdinalIgnoreCase);

    private static string SummarizeError(string text, string fallback)
    {
        var useful = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 3 && !x.All(c => c is '-' or ' '))
            .TakeLast(3);
        var message = string.Join(" ", useful);
        return string.IsNullOrWhiteSpace(message) ? fallback : message;
    }
}

internal sealed record WinGetPackageRow(
    string Name,
    string Id,
    string InstalledVersion,
    string AvailableVersion,
    string Source);

public sealed record WinGetPackageAvailability(string Id, string Version);
