using System.Text;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal delegate Task<ProcessResult> WinGetCommand(
    IReadOnlyList<string> arguments, CancellationToken cancellationToken, TimeSpan timeout);

public sealed class WinGetService
{
    private readonly WinGetCommand _execute;

    public WinGetService() : this(ExecuteAsync) { }
    internal WinGetService(WinGetCommand execute, WinGetManifestSafetyService? manifestSafety = null)
    {
        _execute = execute;
        _manifestSafety = manifestSafety ?? new();
    }

    internal static Task<ProcessResult> ExecuteAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken, TimeSpan timeout) =>
        ProcessRunner.RunAsync(ResolveClientExecutable(), arguments, cancellationToken, timeout);

    internal static string ResolveClientExecutable()
    {
        var packages = new Windows.Management.Deployment.PackageManager().FindPackagesForUserWithPackageTypes(
            "", "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", Windows.Management.Deployment.PackageTypes.Main).ToArray();
        if (packages.Length != 1 || packages[0].SignatureKind != Windows.ApplicationModel.PackageSignatureKind.Store ||
            packages[0].Id.Publisher != "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US")
            throw new InvalidOperationException("Microsoft App Installer identity could not be verified.");
        return Path.Combine(packages[0].InstalledLocation.Path, "winget.exe");
    }

    private static WinGetCommand NativeCommand(PlanItem item) => async (arguments, token, timeout) =>
    {
        var executable = ResolveClientExecutable();
        return await ExecutePinnedAsync(item, arguments, token, timeout,
            (args, ct, limit) => ProcessRunner.RunAsync(executable, args, ct, limit)).ConfigureAwait(false);
    };

    internal static async Task<ProcessResult> ExecutePinnedAsync(PlanItem item, IReadOnlyList<string> arguments,
        CancellationToken token, TimeSpan timeout, WinGetCommand execute)
    {
        if (arguments[0] is "upgrade" or "install")
        {
            var expected = item.WinGetClientVersion.Length > 0 ? item.WinGetClientVersion : item.WinGetBinding?.ClientVersion;
            var current = await execute(["--version"], token, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            if (!current.Success || string.IsNullOrEmpty(expected) || current.StandardOutput.Trim() != expected ||
                WinGetScopeGuard.ClientCompatibilityMessage(expected) is not null)
                return new ProcessResult(1, "", "WinGet changed after consent. A new scan and consent are required.") { PreflightRejected = true };
        }
        token.ThrowIfCancellationRequested();
        return await execute(arguments, token, timeout).ConfigureAwait(false);
    }

    private readonly WinGetManifestSafetyService _manifestSafety;
    private const int ShellExecuteInstallFailed = unchecked((int)0x8A150006);
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    private const int UpdateInstallTechnologyMismatch = unchecked((int)0x8A15008E);
    private const int InstallUpgradeNotSupported = unchecked((int)0x8A150114);
    private const int PackagedServiceRequiresAdministrator = unchecked((int)0x80073D28);
    private static readonly Regex AnsiEscape = new("\\x1B(?:[@-Z\\\\-_]|\\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);
    private static readonly Regex DividerLine = new("^\\s*-{10,}\\s*$", RegexOptions.Compiled);
    private static readonly Regex HeaderToken = new("\\S+", RegexOptions.Compiled);

    public async Task<IReadOnlyList<UpdateItem>> ScanAsync(bool includeUnknown, CancellationToken cancellationToken)
    {
        using var scan = ScanDiagnostics.Begin("winget.scan", cancellationToken);
        string clientVersion;
        using (var phase = ScanDiagnostics.Begin("winget.identify", cancellationToken))
        {
            clientVersion = await IdentifyClientAsync(cancellationToken).ConfigureAwait(false);
            phase?.Complete();
        }
        var arguments = BuildScanArguments(includeUnknown);
        ProcessResult result;
        using (var phase = ScanDiagnostics.Begin("winget.search", cancellationToken))
        {
            result = await _execute(arguments, cancellationToken, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            // This interval measures command completion. The scan below distinguishes
            // valid no-updates exit codes from incomplete or failed searches.
            ScanDiagnostics.Count("winget.search-nonzero-exit", result.Success ? 0 : 1);
            phase?.Complete();
        }

        var output = Normalize(result.StandardOutput + Environment.NewLine + result.StandardError);
        LogService.Write($"Scansione WinGet terminata con codice {result.ExitCode}.");

        var parsed = ParseUpgradeTable(output);
        ScanDiagnostics.Count("winget.parsed-candidates", parsed.Count);
        if (parsed.Count > 0)
        {
            List<WinGetPackageRow> installedRows;
            using (var phase = ScanDiagnostics.Begin("winget.installed-inventory", cancellationToken))
            {
                installedRows = await ReadInstalledInventoryAsync(cancellationToken).ConfigureAwait(false);
                ScanDiagnostics.Count("winget.installed-rows", installedRows.Count);
                phase?.Complete();
            }
            var verified = FilterVerifiedInstalledCandidates(parsed, installedRows);
            var rejectedCount = parsed.Count - verified.Count;
            ScanDiagnostics.Count("winget.unverified-or-duplicate", rejectedCount);
            if (rejectedCount > 0)
                LogService.Write($"Esclusi {rejectedCount} candidati WinGet senza un'installazione locale verificata.");
            var applicable = WinGetApplicabilityStore.ExcludeSuppressed(verified).ToList();
            foreach (var item in applicable) item.WinGetClientVersion = clientVersion;
            var suppressedCount = verified.Count - applicable.Count;
            ScanDiagnostics.Count("winget.suppressed", suppressedCount);
            if (suppressedCount > 0)
                LogService.Write($"Esclusi {suppressedCount} aggiornamenti WinGet già verificati come non applicabili per le stesse versioni.");
            using (var phase = ScanDiagnostics.Begin("winget.metadata", cancellationToken))
            {
                await PreparePackageMetadataAsync(applicable, cancellationToken).ConfigureAwait(false);
                if (WinGetScopeGuard.ClientCompatibilityMessage(clientVersion) is { } rejection)
                {
                    foreach (var item in applicable.Where(x => x.CanInstall && x.PackageOperation == PackageOperations.Upgrade &&
                                 WinGetScopeGuard.IsOfficialSource(x.Source)))
                    {
                        item.CanInstall = false;
                        item.IsSelected = false;
                        item.Status = LocalizationService.Text("Aggiornamento manuale", "Manual update");
                        item.ResultDetails = rejection;
                    }
                }
                phase?.Complete();
            }
            ScanDiagnostics.Count("winget.returned", applicable.Count);
            scan?.Complete(result.Success ? "success" : "partial");
            return applicable;
        }

        if (!result.Success && !ContainsNoUpdatesMessage(output))
            throw new InvalidOperationException(SummarizeError(output, "WinGet non ha completato la scansione."));

        ScanDiagnostics.Count("winget.returned", 0);
        scan?.Complete();
        return [];
    }

    private async Task<string> IdentifyClientAsync(CancellationToken cancellationToken)
    {
        var unavailable = LocalizationService.Text(
            "Impossibile identificare la versione di WinGet. Verifica Installazione app di Microsoft.",
            "Could not identify the WinGet version. Check Microsoft App Installer.");
        ProcessResult result;
        try
        {
            result = await _execute(["--version"], cancellationToken, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            throw new InvalidOperationException(unavailable, ex);
        }
        var version = Normalize(result.StandardOutput).Trim();
        if (!result.Success || !IsClientVersion(version))
            throw new InvalidOperationException(unavailable);
        LogService.Write($"WinGet {version}");
        return result.StandardOutput.Trim();
    }

    internal static bool IsClientVersion(string version) =>
        Regex.IsMatch(version, @"\Av?[0-9]+(?:\.[0-9]+){1,3}(?:[-+][a-zA-Z0-9.+-]+)?\z");

    private async Task<List<WinGetPackageRow>> ReadInstalledInventoryAsync(
        CancellationToken cancellationToken)
    {
        var result = await _execute(
            ["list", "--accept-source-agreements", "--disable-interactivity", "--nowarn"],
            cancellationToken,
            TimeSpan.FromMinutes(5)).ConfigureAwait(false);
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
        var installedCounts = installedRows
            .Where(row => IsSafePackageId(row.Id))
            .GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return candidates
            .Where(candidate => IsSafePackageId(candidate.Id) && installedCounts.ContainsKey(candidate.Id))
            .GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var candidate = group.First();
                if (installedCounts[candidate.Id] > 1)
                {
                    candidate.CanInstall = false;
                    candidate.IsSelected = false;
                    candidate.Status = LocalizationService.Text("Aggiornamento manuale", "Manual update");
                    candidate.ResultDetails = LocalizationService.Text(
                        "WinGet rileva più installazioni per lo stesso ID. Il percorso attuale non identifica una copia univoca da aggiornare.",
                        "WinGet reports multiple installations for the same ID. The current execution path does not identify a unique copy to upgrade.");
                }
                return candidate;
            })
            .ToList();
    }

    public Task PreparePackageMetadataAsync(
        IReadOnlyList<UpdateItem> items, CancellationToken cancellationToken) =>
        _manifestSafety.ApplyAsync(items, cancellationToken, _execute);

    public async Task<WinGetPackageAvailability?> ResolveInstallablePackageAsync(
        string packageId, CancellationToken cancellationToken)
    {
        if (!IsSafePackageId(packageId)) return null;
        var result = await _execute(
            ["show", "--id", packageId, "--exact", "--source", "winget",
                "--accept-source-agreements", "--disable-interactivity", "--nowarn"],
            cancellationToken, TimeSpan.FromMinutes(2));
        if (!result.Success) return null;

        return ParsePackageAvailability(packageId, result.StandardOutput);
    }

    internal static WinGetPackageAvailability? ParsePackageAvailability(string packageId, string output)
    {
        var match = Regex.Match(Normalize(output),
            @"(?im)^(?:Version|Versione)[ \t]*:[ \t]*(?<version>[^\r\n]*)");
        var version = match.Groups["version"].Value.Trim(' ');
        return IsExactPackageVersion(version) ? new WinGetPackageAvailability(packageId, version) : null;
    }

    public static ProcessResult Install(PlanItem item, bool silent) => Install(item, silent, NativeCommand(item));

    internal static ProcessResult Install(PlanItem item, bool silent, WinGetCommand execute,
        CancellationToken token = default, Action<string>? phase = null)
    {
        if (RequiresUpgradeGuard(item)) return new(1, "", "This upgrade cannot fall back to install.") { PreflightRejected = true };
        if (ValidateTarget(item) is { } failure) return failure with { PreflightRejected = true };
        var arguments = new List<string>
        {
            "install", "--id", item.Id, "--exact", "--version", item.AvailableVersion, "--source", item.Source,
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity", "--nowarn"
        };
        if (silent) arguments.Add("--silent");
        token.ThrowIfCancellationRequested();
        phase?.Invoke("winget-installer");
        token.ThrowIfCancellationRequested();
        return execute(arguments, token, TimeSpan.FromMinutes(90)).GetAwaiter().GetResult();
    }

    public static ProcessResult Upgrade(PlanItem item, bool silent) => Upgrade(item, silent, default, null);

    internal static ProcessResult Upgrade(PlanItem item, bool silent, CancellationToken token, Action<string>? phase)
    {
        var execute = NativeCommand(item);
        return Upgrade(item, silent, execute, WinGetScopeGuard.Native(execute), token, phase);
    }

    internal static ProcessResult Upgrade(PlanItem item, bool silent, WinGetCommand execute, WinGetScopeGuard? guard = null,
        CancellationToken token = default, Action<string>? phase = null)
    {
        token.ThrowIfCancellationRequested();
        if (ValidateTarget(item) is { } failure) return failure with { PreflightRejected = true };
        phase?.Invoke("winget-preflight");
        if (guard is not null && guard.ValidateAsync(item, token).GetAwaiter().GetResult() is { } rejected)
            return rejected with { PreflightRejected = true };
        if (RequiresUpgradeGuard(item) && guard is null) return new(1, "", "This upgrade requires revalidation.") { PreflightRejected = true };
        token.ThrowIfCancellationRequested();
        phase?.Invoke("winget-installer");
        token.ThrowIfCancellationRequested();
        var first = RunUpgrade(item, silent, execute, token);
        if (first.Success) return first;

        if ((silent && first.ExitCode == ShellExecuteInstallFailed) || IsInstalledPackageMatchFailure(first))
        {
            // Read-only reconciliation can recognize a target installed concurrently. Never retry
            // execution without the approved source/ID or substitute a name-based catalog match.
            // Native/protected paths retain the real installer exit code. Their
            // independent verifier can confirm a target without rewriting execution.
            if (guard is not null || item.WinGetBinding is not null || item.WinGetVerification is not null) return first;
            phase?.Invoke("winget-verification");
            var installed = RunnerReadOnlyWait.RunAsync(ct => Task.FromResult(
                QueryInstalled("--id", item.Id, execute, TimeSpan.FromSeconds(45), source: item.Source, token: ct)),
                token, TimeSpan.FromSeconds(60), "winget-reconciliation").GetAwaiter().GetResult();
            var attempts = new List<ProcessResult> { first, installed.Result };
            if (EvaluateInstalledQuery(item, installed.Result, installed.Rows).Verified)
                return AlreadyCurrent(attempts, item, installed.Rows.First(x =>
                    x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)).InstalledVersion);
            return CombineAttempts(attempts, first.ExitCode);
        }
        return first;
    }

    public static ProcessResult RunInteractive(PlanItem item) => RunInteractive(item, default, null);

    internal static ProcessResult RunInteractive(PlanItem item, CancellationToken token, Action<string>? phase)
    {
        var execute = NativeCommand(item);
        return RunInteractive(item, execute, WinGetScopeGuard.Native(execute), token, phase);
    }

    internal static ProcessResult Install(PlanItem item, bool silent, CancellationToken token, Action<string>? phase) =>
        Install(item, silent, NativeCommand(item), token, phase);

    internal static ProcessResult RunInteractive(PlanItem item, WinGetCommand execute, WinGetScopeGuard? guard = null,
        CancellationToken token = default, Action<string>? phase = null)
    {
        token.ThrowIfCancellationRequested();
        if (ValidateTarget(item) is { } failure) return failure with { PreflightRejected = true };
        if (item.WinGetBinding?.PythonBurnTransition is not null)
            return new(1, "", LocalizationService.Text(
                "Questo upgrade Python richiede le opzioni vincolate del piano; l'installer interattivo non è disponibile.",
                "This Python upgrade requires the plan's fixed options; the interactive installer is unavailable.")) { PreflightRejected = true };
        phase?.Invoke("winget-preflight");
        if (guard is not null && guard.ValidateAsync(item, token).GetAwaiter().GetResult() is { } rejected)
            return rejected with { PreflightRejected = true };
        if (RequiresUpgradeGuard(item) && guard is null) return new(1, "", "This upgrade requires revalidation.") { PreflightRejected = true };
        var arguments = BuildInteractiveArguments(item);
        token.ThrowIfCancellationRequested();
        phase?.Invoke("winget-installer");
        token.ThrowIfCancellationRequested();
        return execute(arguments, token, TimeSpan.FromMinutes(90)).GetAwaiter().GetResult();
    }

    internal static IReadOnlyList<string> BuildInteractiveArguments(PlanItem item)
    {
        var operation = item.PackageOperation.Equals(PackageOperations.Install, StringComparison.Ordinal)
            ? "install"
            : "upgrade";
        var arguments = new List<string>
        {
            operation,
            "--id", item.Id,
            "--exact",
            "--version", item.AvailableVersion,
            "--source", item.Source,
            "--accept-package-agreements",
            "--accept-source-agreements",
            "--interactive",
            "--nowarn"
        };
        AddScopeArguments(arguments, item);
        return arguments;
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
            .Where(x => IsExactPackageVersion(x.AvailableVersion))
            .Select(x => new UpdateItem
            {
                Id = x.Id,
                Name = string.IsNullOrWhiteSpace(x.Name) ? x.Id : x.Name,
                Kind = RuntimePackageCatalog.IsRuntimePackageId(x.Id)
                    ? UpdateKind.Runtime
                    : UpdateKind.Software,
                InstalledVersion = string.IsNullOrWhiteSpace(x.InstalledVersion) ? "Sconosciuta" : x.InstalledVersion,
                AvailableVersion = x.AvailableVersion,
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
        PlanItem item, bool silent, WinGetCommand execute, CancellationToken token)
    {
        var arguments = new List<string>
        {
            "upgrade", "--id", item.Id, "--exact",
            "--version", item.AvailableVersion, "--source", item.Source,
            "--accept-package-agreements", "--accept-source-agreements", "--nowarn"
        };

        arguments.Add("--disable-interactivity");
        if (silent) arguments.Add("--silent");
        AddScopeArguments(arguments, item);
        token.ThrowIfCancellationRequested();
        return execute(arguments, token, TimeSpan.FromMinutes(90)).GetAwaiter().GetResult();
    }

    private static void AddScopeArguments(List<string> arguments, PlanItem item)
    {
        if (item.WinGetVerification is { RemovalUpgradeCode: not null } removal)
        {
            arguments.AddRange(["--scope", removal.Scope, "--installer-type", "burn", "--architecture", removal.Architecture]);
            return;
        }
        if (item.WinGetBinding is not { } binding) return;
        if (binding.CopyEvidence?.UnspecifiedScope != true) arguments.AddRange(["--scope", binding.Scope]);
        if (binding.DependencySnapshot.Length > 0) arguments.Add("--skip-dependencies");
        arguments.AddRange(["--installer-type", binding.InstallerType]);
        if (binding.Architecture.Length > 0 && binding.Architecture != "neutral") arguments.AddRange(["--architecture", binding.Architecture]);
        if (binding.PythonBurnTransition is { } transition)
            arguments.AddRange(["--custom", transition.CustomArguments]);
    }

    private static (ProcessResult Result, List<WinGetPackageRow> Rows) QueryInstalled(
        string selector, string value, WinGetCommand execute, TimeSpan? timeout = null, string? source = null,
        CancellationToken token = default)
    {
        var arguments = new List<string>
        {
            "list", selector, value, "--exact", "--accept-source-agreements", "--disable-interactivity", "--nowarn"
        };
        if (source is not null) arguments.AddRange(["--source", source]);
        token.ThrowIfCancellationRequested();
        var result = execute(arguments, token, timeout ?? TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
        return (result, ParsePackageRows(result.StandardOutput + Environment.NewLine + result.StandardError));
    }

    internal static UpdateVerificationResult VerifyInstallation(
        PlanItem item,
        Func<string, string, (ProcessResult Result, List<WinGetPackageRow> Rows)>? queryInstalled = null,
        int maxAttempts = 3,
        Action<TimeSpan>? waitBeforeRetry = null, CancellationToken token = default,
        WinGetCommand? verifyClient = null)
    {
        var nativeVerification = queryInstalled is null || verifyClient is not null;
        if ((item.WinGetBinding is not null || item.WinGetVerification is not null) && queryInstalled is null)
            return WinGetScopeGuard.Native(ExecuteAsync).VerifyAsync(item, token).GetAwaiter().GetResult();
        if (queryInstalled is null)
            return new UpdateVerificationResult
            {
                Status = UpdateVerificationStatuses.Unavailable,
                Message = LocalizationService.Text(
                    "Il piano storico non contiene le evidenze della copia approvata necessarie alla verifica automatica.",
                    "The historical plan lacks the approved installation evidence required for automatic verification.")
            };
        if (nativeVerification && CheckVerificationClient(item, verifyClient ?? ExecuteAsync, token) is { } clientFailure)
            return clientFailure;
        if (!IsSafePackageId(item.Id))
        {
            return new UpdateVerificationResult
            {
                IsDefinitive = true,
                Status = UpdateVerificationStatuses.Failed,
                Message = LocalizationService.Text("Identificativo WinGet non valido durante la verifica post-installazione.", "Invalid WinGet identifier during post-installation verification."),
                Diagnostics = $"PackageId rifiutato: {item.Id}"
            };
        }

        queryInstalled ??= (selector, value) =>
            QueryInstalled(selector, value, ExecuteAsync, TimeSpan.FromSeconds(10), source: item.Source, token: token);
        waitBeforeRetry ??= delay => Task.Delay(delay, token).GetAwaiter().GetResult();
        maxAttempts = Math.Clamp(maxAttempts, 1, 5);
        var diagnostics = new List<string>();
        UpdateVerificationResult? latest = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var installed = queryInstalled("--id", item.Id);
                token.ThrowIfCancellationRequested();
                latest = EvaluateInstalledQuery(item, installed.Result, installed.Rows);
                var matches = installed.Rows.Where(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                diagnostics.Add(
                    $"Tentativo {attempt}/{maxAttempts}: queryExit={installed.Result.ExitCode}; querySucceeded={installed.Result.Success}; " +
                    $"parsedRows={installed.Rows.Count}; exactMatches={matches.Length}; " +
                    $"installed=[{string.Join("; ", matches.Select(x => x.InstalledVersion))}]; " +
                    $"sources=[{string.Join("; ", matches.Select(x => x.Source))}]; target={item.AvailableVersion}; source={item.Source}; " +
                    $"pid={installed.Result.ProcessId?.ToString() ?? "n/d"}; " +
                    $"duration={installed.Result.Duration?.ToString() ?? "n/d"}; " +
                    $"command={installed.Result.CommandLine}; versionEvidence={latest.Status}; explanation={latest.Message}");
            }
            catch (OperationCanceledException) { throw; }
            catch (TimeoutException) { throw; }
            catch (Exception ex)
            {
                latest = new UpdateVerificationResult
                {
                    IsDefinitive = false,
                    Status = UpdateVerificationStatuses.Unavailable,
                    Message = LocalizationService.Text("Verifica post-installazione non disponibile.", "Post-installation verification not available."),
                    Diagnostics = ex.ToString()
                };
                diagnostics.Add($"Tentativo {attempt}/{maxAttempts}: eccezione={ex.Message}");
            }

            if (latest.Verified)
                break;
            if (attempt < maxAttempts)
                waitBeforeRetry(TimeSpan.FromSeconds(attempt * 3));
        }

        if (nativeVerification && CheckVerificationClient(item, verifyClient ?? ExecuteAsync, token) is { } changedClient)
            return changedClient;
        latest ??= new UpdateVerificationResult
        {
            IsDefinitive = false,
            Status = UpdateVerificationStatuses.Unavailable,
            Message = LocalizationService.Text("Verifica post-installazione non disponibile.", "Post-installation verification not available.")
        };
        latest.Diagnostics = string.Join(Environment.NewLine, diagnostics) +
                             (string.IsNullOrWhiteSpace(latest.Diagnostics)
                                 ? ""
                                 : Environment.NewLine + latest.Diagnostics);
        token.ThrowIfCancellationRequested();
        return latest;
    }

    private static UpdateVerificationResult? CheckVerificationClient(PlanItem item, WinGetCommand execute, CancellationToken token)
    {
        try
        {
            var failure = WinGetScopeGuard.Native(execute).CheckClientAsync(item.WinGetClientVersion, token).GetAwaiter().GetResult();
            return failure is null ? null : new UpdateVerificationResult
            {
                Status = UpdateVerificationStatuses.Unavailable, Message = failure.StandardError
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException) { throw; }
        catch (Exception ex)
        {
            return new UpdateVerificationResult
            {
                Status = UpdateVerificationStatuses.Unavailable,
                Message = LocalizationService.Text("Verifica post-installazione non disponibile.", "Post-installation verification not available."),
                Diagnostics = ex.ToString()
            };
        }
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
                    ? LocalizationService.Text("Il pacchetto non risulta installato dopo l'operazione.", "The package does not appear to be installed after the operation.")
                    : LocalizationService.Text("WinGet non ha permesso di verificare lo stato installato dopo l'operazione.", "WinGet could not verify the installed state after the operation.")
            };
        }

        var matches = rows.Where(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Any(x => string.IsNullOrWhiteSpace(x.Source)) &&
            !matches.Any(x => x.Source.Length > 0 && !x.Source.Equals(item.Source, StringComparison.OrdinalIgnoreCase)))
            return new UpdateVerificationResult
            {
                Status = UpdateVerificationStatuses.Unavailable,
                Message = LocalizationService.Text(
                    "La tabella WinGet omette l'origine; occorre verificare la stessa copia locale con evidenze indipendenti.",
                    "The WinGet table omits the source; the same local installation requires independent verification.")
            };
        if (string.IsNullOrWhiteSpace(item.Source) ||
            matches.Any(x => !x.Source.Equals(item.Source, StringComparison.OrdinalIgnoreCase)))
            return new UpdateVerificationResult
            {
                Status = UpdateVerificationStatuses.Unavailable,
                Message = LocalizationService.Translate(LocalizationService.WinGetVerificationSourceMismatchMessage)
            };
        var versions = matches.Select(x => x.InstalledVersion.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var row = matches.FirstOrDefault();
        if (row is null)
        {
            return new UpdateVerificationResult
            {
                IsDefinitive = false,
                Status = UpdateVerificationStatuses.Unavailable,
                Message = LocalizationService.Text("La risposta WinGet non contiene una versione installata verificabile.", "The WinGet response does not contain a verifiable installed version.")
            };
        }

        if (matches.Count != 1)
            return new UpdateVerificationResult
            {
                Status = UpdateVerificationStatuses.Unavailable,
                Message = LocalizationService.Text("Verifica ambigua: sono presenti più copie installate.", "Verification is ambiguous: multiple installed copies are present."),
                Diagnostics = $"PackageId={item.Id}; installed=[{string.Join("; ", versions)}]; target={item.AvailableVersion}"
            };
        return EvaluateVersionEvidence(row.InstalledVersion, item.AvailableVersion);
    }

    internal static UpdateVerificationResult EvaluateVersionEvidence(string installed, string target)
    {
        var relation = SoftwareVersionEvidence.Compare(installed, target);
        var verified = relation is SoftwareVersionRelation.Equal or SoftwareVersionRelation.Greater;
        return new UpdateVerificationResult
        {
            VersionRelation = relation,
            IsDefinitive = relation != SoftwareVersionRelation.NotComparable,
            Verified = verified,
            Status = verified ? UpdateVerificationStatuses.Verified : relation == SoftwareVersionRelation.Less
                ? UpdateVerificationStatuses.Failed : UpdateVerificationStatuses.Unavailable,
            Message = relation == SoftwareVersionRelation.NotComparable
                ? LocalizationService.Text("Verifica inconcludente: le versioni non sono confrontabili con una regola attendibile.", "Verification is inconclusive: the versions cannot be compared using a reliable rule.")
                : verified
                    ? LocalizationService.Text($"Versione installata verificata: {installed}.", $"Verified installed version: {installed}.")
                    : LocalizationService.Text($"La versione installata ({installed}) non raggiunge quella attesa ({target}).", $"The installed version ({installed}) is below the expected version ({target})."),
            Diagnostics = $"installed={installed}; target={target}; relation={relation}"
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
        !string.IsNullOrWhiteSpace(source) && source.Length <= 160 && char.IsLetterOrDigit(source[0]) &&
        source.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    private static bool IsSafePackageId(string packageId) =>
        !string.IsNullOrWhiteSpace(packageId) && packageId.Length <= 160 &&
        packageId.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '+');

    private static bool RequiresUpgradeGuard(PlanItem item) =>
        item.WinGetBinding is not null || item.WinGetVerification is not null || item.RemovalManifestSha256 != "";

    internal static ProcessResult? ValidateTarget(PlanItem item)
    {
        var removalProof = item.WinGetVerification is { RemovalUpgradeCode: not null };
        if (removalProof != (item.RemovalManifestSha256 != "") ||
            removalProof && item.RemovalManifestSha256 != item.WinGetVerification!.ManifestSha256)
            return new ProcessResult(1, "", LocalizationService.Text(
                "La prova della transizione e il manifest della rimozione devono corrispondere. Esegui una nuova scansione.",
                "The transition evidence must match the removal manifest. Run a new scan."));
        if (item.WinGetVerification is { } proof && (item.WinGetBinding is not null ||
            item.PackageOperation != PackageOperations.Upgrade || !WinGetScopeGuard.IsOfficialSource(item.Source) ||
            !WinGetScopeGuard.ValidVerificationBinding(proof)))
            return new ProcessResult(1, "", "Invalid WinGet verification target.");
        if (item.WinGetBinding is { } binding && (item.PackageOperation != PackageOperations.Upgrade ||
            !WinGetScopeGuard.IsOfficialSource(item.Source) || !WinGetScopeGuard.ValidBinding(binding)))
            return new ProcessResult(1, "", "Invalid scoped WinGet target.");
        if (!IsSafePackageId(item.Id))
            return new ProcessResult(1, "", LocalizationService.Text(
                "Identificativo WinGet non valido.", "Invalid WinGet package ID."));
        if (!IsSafeSource(item.Source))
            return new ProcessResult(1, "", LocalizationService.Text(
                "Origine WinGet non verificabile. Esegui una nuova scansione.",
                "The WinGet source cannot be verified. Run a new scan."));
        if (!IsExactPackageVersion(item.AvailableVersion))
            return new ProcessResult(1, "", LocalizationService.Text(
                "Versione WinGet non verificabile. Esegui una nuova scansione.",
                "The WinGet target version cannot be verified. Run a new scan."));
        if (item.PackageOperation == PackageOperations.Upgrade &&
            SoftwareVersionEvidence.Compare(item.InstalledVersion, item.AvailableVersion) == SoftwareVersionRelation.Greater)
            return new ProcessResult(1, "", LocalizationService.Text(
                "Il target è inferiore alla versione installata. Aggiornamento bloccato; esegui una nuova scansione.",
                "The target is below the installed version. Upgrade blocked; run a new scan."));
        return null;
    }

    private static readonly HashSet<string> UnresolvedVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Latest", "Più recente", "Unknown", "Sconosciuto", "Sconosciuta", "Unavailable",
        "Non disponibile", "Not available", "None", "Nessuna", "NA"
    };

    // PackageVersion is a manifest string, not necessarily a System.Version or SemVer.
    private static bool IsExactPackageVersion(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value == value.Trim() && value[0] != '-' && value.Any(char.IsLetterOrDigit) &&
        !value.Any(char.IsControl) && value.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|', '…']) < 0 &&
        !value.EndsWith("...", StringComparison.Ordinal) && !UnresolvedVersions.Contains(value);

    private static bool IsInstalledPackageMatchFailure(ProcessResult result)
    {
        var output = Normalize(result.StandardOutput + Environment.NewLine + result.StandardError);
        return output.Contains("No installed package found matching input criteria", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Non è stato trovato alcun pacchetto installato corrispondente ai criteri di input", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Nessun pacchetto installato trovato corrispondente ai criteri", StringComparison.OrdinalIgnoreCase);
    }

    // Provider cause, not the terminal disposition of an attempted operation.
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

    internal static string ClassifyFailureReason(ProcessResult result, bool finalSuccess)
    {
        if (finalSuccess || result.Success) return UpdateFailureReasons.None;
        if (result.ExitCode == PackagedServiceRequiresAdministrator) return UpdateFailureReasons.ElevationRequired;
        // The specific HRESULT works in every locale. With --version, WinGet can
        // instead emit UPDATE_NOT_APPLICABLE plus an explicit technology message.
        // Never infer that cause from the generic HRESULT alone or from other errors.
        if (result.ExitCode == UpdateInstallTechnologyMismatch ||
            (result.ExitCode == UpdateNotApplicable && HasTechnologyMismatchMessage(result)))
            return UpdateFailureReasons.InstallerTechnologyMismatch;
        if (result.ExitCode == UpdateNotApplicable) return UpdateFailureReasons.NotApplicable;
        if (result.ExitCode == InstallUpgradeNotSupported) return UpdateFailureReasons.UpgradeNotSupported;
        return IsFileInUseFailure(result) ? UpdateFailureReasons.FilesInUse : UpdateFailureReasons.None;
    }

    private static bool HasTechnologyMismatchMessage(ProcessResult result)
    {
        var output = Regex.Replace(Normalize(result.StandardOutput + "\n" + result.StandardError), @"\s+", " ");
        return output.Contains("install technology is different from the current version installed", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("install technology of the newer version specified is different from the current version installed", StringComparison.OrdinalIgnoreCase) ||
               (output.Contains("tecnologia di installazione", StringComparison.OrdinalIgnoreCase) &&
                output.Contains("diversa", StringComparison.OrdinalIgnoreCase) &&
                (output.Contains("versione corrente installata", StringComparison.OrdinalIgnoreCase) ||
                 output.Contains("versione attualmente installata", StringComparison.OrdinalIgnoreCase)));
    }

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

    internal static bool IsVersionAtLeast(string installed, string target) =>
        SoftwareVersionEvidence.Compare(installed, target) is SoftwareVersionRelation.Equal or SoftwareVersionRelation.Greater;

    private static ProcessResult AlreadyCurrent(List<ProcessResult> attempts, PlanItem item, string installedVersion)
    {
        var message = LocalizationService.Text($"{item.Name} risulta già aggiornato alla versione {installedVersion}. La scansione precedente non era più attuale.", $"{item.Name} is already updated to version {installedVersion}. The previous scan was no longer current.");
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
