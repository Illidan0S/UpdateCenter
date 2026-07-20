using System.Text;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class WinGetService
{
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
            return parsed;

        if (!result.Success && !ContainsNoUpdatesMessage(output))
            throw new InvalidOperationException(SummarizeError(output, "WinGet non ha completato la scansione."));

        return [];
    }

    public static ProcessResult Upgrade(PlanItem item, bool silent)
    {
        var attempts = new List<ProcessResult>();
        var first = RunUpgrade(item, silent, useSource: true, useName: false);
        attempts.Add(first);
        if (first.Success || !IsInstalledPackageMatchFailure(first))
            return first;

        // La sorgente può impedire a WinGet di correlare un'app installata per utente.
        var withoutSource = RunUpgrade(item, silent, useSource: false, useName: false);
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
        // Il ripiego sul nome è consentito solo con una singola corrispondenza esatta.
        var installedByName = QueryInstalled("--name", item.Name);
        attempts.Add(installedByName.Result);
        var exactNameRows = installedByName.Rows
            .Where(x => x.Name.Equals(item.Name, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        if (exactNameRows.Count == 1)
        {
            if (IsVersionAtLeast(exactNameRows[0].InstalledVersion, item.AvailableVersion))
                return AlreadyCurrent(attempts, item, exactNameRows[0].InstalledVersion);

            var byName = RunUpgrade(item, silent, useSource: false, useName: true);
            attempts.Add(byName);
            return CombineAttempts(attempts, byName.ExitCode);
        }

        return CombineAttempts(attempts, withoutSource.ExitCode);
    }

    public static List<UpdateItem> ParseUpgradeTable(string output) =>
        ParsePackageRows(output)
            .Where(x => !string.IsNullOrWhiteSpace(x.AvailableVersion))
            .Select(x => new UpdateItem
            {
                Id = x.Id,
                Name = string.IsNullOrWhiteSpace(x.Name) ? x.Id : x.Name,
                Kind = UpdateKind.Software,
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

    private static ProcessResult RunUpgrade(PlanItem item, bool silent, bool useSource, bool useName)
    {
        var arguments = new List<string>
        {
            "upgrade", useName ? "--name" : "--id", useName ? item.Name : item.Id, "--exact",
            "--accept-package-agreements", "--accept-source-agreements",
            "--disable-interactivity", "--nowarn"
        };

        if (silent) arguments.Add("--silent");
        if (useSource && IsSafeSource(item.Source))
        {
            arguments.Add("--source");
            arguments.Add(item.Source);
        }

        return ProcessRunner.RunAsync(
            "winget.exe", arguments, CancellationToken.None, TimeSpan.FromMinutes(45)).GetAwaiter().GetResult();
    }

    private static (ProcessResult Result, List<WinGetPackageRow> Rows) QueryInstalled(string selector, string value)
    {
        var arguments = new[]
        {
            "list", selector, value, "--exact", "--accept-source-agreements", "--disable-interactivity", "--nowarn"
        };
        var result = ProcessRunner.RunAsync(
            "winget.exe", arguments, CancellationToken.None, TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
        return (result, ParsePackageRows(result.StandardOutput + Environment.NewLine + result.StandardError));
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

    private static bool IsInstalledPackageMatchFailure(ProcessResult result)
    {
        var output = Normalize(result.StandardOutput + Environment.NewLine + result.StandardError);
        return output.Contains("No installed package found matching input criteria", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Non è stato trovato alcun pacchetto installato corrispondente ai criteri di input", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Nessun pacchetto installato trovato corrispondente ai criteri", StringComparison.OrdinalIgnoreCase);
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
        return new ProcessResult(exitCode, output.ToString(), errors.ToString(), commands.ToString().Trim());
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
