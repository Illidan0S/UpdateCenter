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
        var arguments = new List<string>
        {
            "upgrade",
            "--accept-source-agreements",
            "--disable-interactivity",
            "--nowarn"
        };

        if (includeUnknown)
            arguments.Add("--include-unknown");

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
        var arguments = new List<string>
        {
            "upgrade", "--id", item.Id, "--exact",
            "--accept-package-agreements", "--accept-source-agreements",
            "--disable-interactivity", "--nowarn"
        };

        if (silent)
            arguments.Add("--silent");

        if (!string.IsNullOrWhiteSpace(item.Source) &&
            item.Source.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            arguments.Add("--source");
            arguments.Add(item.Source);
        }

        return ProcessRunner.RunAsync(
            "winget.exe", arguments, CancellationToken.None, TimeSpan.FromMinutes(45)).GetAwaiter().GetResult();
    }

    internal static List<UpdateItem> ParseUpgradeTable(string output)
    {
        var lines = output.Replace("\r", "").Split('\n');
        for (var dividerIndex = 1; dividerIndex < lines.Length; dividerIndex++)
        {
            if (!DividerLine.IsMatch(lines[dividerIndex]))
                continue;

            var headerIndex = dividerIndex - 1;
            while (headerIndex >= 0 && string.IsNullOrWhiteSpace(lines[headerIndex])) headerIndex--;
            if (headerIndex < 0) continue;

            var headerMatches = HeaderToken.Matches(lines[headerIndex]);
            if (headerMatches.Count < 4) continue;
            var starts = headerMatches.Cast<Match>().Take(5).Select(x => x.Index).ToArray();
            var items = new List<UpdateItem>();

            for (var rowIndex = dividerIndex + 1; rowIndex < lines.Length; rowIndex++)
            {
                var line = lines[rowIndex];
                if (string.IsNullOrWhiteSpace(line))
                    break;

                if (line.TrimStart().StartsWith('-') || line.Contains("upgrade disponibili", StringComparison.OrdinalIgnoreCase))
                    continue;

                var columns = ReadColumns(line, starts);
                if (columns.Count < 4 || string.IsNullOrWhiteSpace(columns[1]))
                    continue;

                var name = columns[0];
                var id = columns[1];
                var installed = columns[2];
                var available = columns[3];
                var source = columns.Count >= 5 ? columns[4] : "WinGet";

                if (id.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                    available.Equals("Disponibile", StringComparison.OrdinalIgnoreCase) ||
                    available.Equals("Available", StringComparison.OrdinalIgnoreCase))
                    continue;

                items.Add(new UpdateItem
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? id : name,
                    Kind = UpdateKind.Software,
                    InstalledVersion = string.IsNullOrWhiteSpace(installed) ? "Sconosciuta" : installed,
                    AvailableVersion = string.IsNullOrWhiteSpace(available) ? "Più recente" : available,
                    Source = string.IsNullOrWhiteSpace(source) ? "WinGet" : source,
                    IsImportant = false
                });
            }

            if (items.Count > 0)
                return items;
        }

        return [];
    }

    private static List<string> ReadColumns(string line, IReadOnlyList<int> starts)
    {
        var columns = new List<string>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            if (start >= line.Length)
            {
                columns.Add("");
                continue;
            }

            var end = i + 1 < starts.Count ? Math.Min(starts[i + 1], line.Length) : line.Length;
            columns.Add(line[start..end].Trim());
        }
        return columns;
    }

    private static string Normalize(string text) => AnsiEscape.Replace(text, "").Replace("\b", "");

    private static bool ContainsNoUpdatesMessage(string text) =>
        text.Contains("No applicable upgrade found", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Nessun aggiornamento applicabile", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("No installed package found", StringComparison.OrdinalIgnoreCase);

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
