using System.Text.RegularExpressions;

namespace UpdateCenter.Services;

// Bounded reader for the block mappings used by the official installer manifests.
// This is not a YAML loader: flow mappings, aliases and unsupported shapes cannot
// authorize recovery. Nested lists are never mistaken for installer entries.
internal sealed record WinGetInstallerMetadata(string Id, string Version, IReadOnlyList<WinGetInstallerVariant> Installers)
{
    internal static WinGetInstallerMetadata? Parse(string text)
    {
        if (text.Length > 1024 * 1024 || text.Contains('\t') || Regex.IsMatch(text, @"(?m)^\s*(?:-\s*)?<<:")) return null;
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var header = Array.FindIndex(lines, x => Regex.IsMatch(x, @"^Installers:\s*(?:#.*)?$"));
        if (header < 0) return null;
        var root = Read(lines[..header], 0);
        if (root is null) return null;
        var starts = new List<int>();
        var indent = -1;
        var end = lines.Length;
        for (var i = header + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var spaces = line.Length - line.TrimStart(' ').Length;
            if (spaces == 0 && !line.StartsWith('-')) { end = i; break; }
            if (line.TrimStart().StartsWith('-'))
            {
                if (indent < 0) indent = spaces;
                if (spaces < indent) return null;
                if (spaces == indent)
                {
                    if (!Regex.IsMatch(line[spaces..], @"^- [A-Za-z][A-Za-z0-9]*:")) return null;
                    starts.Add(i);
                }
            }
        }
        if (starts.Count == 0) return null;
        // Trailing root fields do not pass through Read. Reject the same YAML
        // key/alias forms there too; an escaped Dependencies key must never be
        // interpreted as absence of dependency effects.
        if (lines[end..].Any(x => x.Length > 0 && "\"'?:&*!".Contains(x[0]))) return null;
        if (lines[end..].Any(x => Regex.IsMatch(x,
            @"^(PackageIdentifier|PackageVersion|Installers|Scope|Architecture|InstallerType|NestedInstallerType|UpgradeBehavior|InstallerSwitches|ProductCode|ElevationRequirement|MinimumOSVersion|RestrictedCapabilities|UnsupportedOSArchitectures|AppsAndFeaturesEntries)\s*:"))) return null;
        var defaults = WinGetDependencyPolicy.ParseDeclarations([.. lines[..header], .. lines[end..]], 0);
        var variants = new List<WinGetInstallerVariant>();
        for (var i = 0; i < starts.Count; i++)
        {
            var block = lines[starts[i]..(i + 1 < starts.Count ? starts[i + 1] : end)];
            block[0] = new string(' ', indent + 2) + block[0][(indent + 2)..];
            var own = Read(block, indent + 2);
            if (own is null) return null;
            var dependencies = WinGetDependencyPolicy.ParseDeclarations(block, indent + 2);
            // WinGet restores the common list when the installer has no entries.
            // An unreadable local declaration may have entries and stays unknown.
            if (dependencies is { Count: 0 }) dependencies = defaults;
            string Value(string key) => own.GetValueOrDefault(key, root.GetValueOrDefault(key, ""));
            variants.Add(new(Value("Architecture").ToLowerInvariant(), Value("Scope").ToLowerInvariant(),
                Value("InstallerType").ToLowerInvariant(), Value("UpgradeBehavior"), Value("InstallerUrl"),
                Value("InstallerSha256"), Value("ProductCode"), Value("Custom"), Value("ElevationRequirement"),
                Value("MinimumOSVersion"), Value("RestrictedCapabilities").Split('\u001f', StringSplitOptions.RemoveEmptyEntries),
                Value("UnsupportedOSArchitectures").Split('\u001f', StringSplitOptions.RemoveEmptyEntries),
                Value("NestedInstallerType").ToLowerInvariant(),
                Value("DisplayVersion"), Value("MappingProductCode"), Value("HasVersionMapping") == "true",
                Value("MappingUpgradeCode"), Value("InstallLocation"), dependencies));
        }
        return new(root.GetValueOrDefault("PackageIdentifier", ""), root.GetValueOrDefault("PackageVersion", ""), variants);
    }

    // WinGet can match a ZIP's effective installer type and can accept an
    // unspecified scope. Include those competitors when binding a single recipe.
    internal WinGetInstallerVariant[] DispatchRecipes(string scope, string type, string architecture) =>
        Installers.Where(x => (x.Scope == scope || x.Scope.Length == 0) &&
            (x.Type == type || (x.Type == "zip" && (x.NestedType == type || x.NestedType.Length == 0))) &&
            (architecture.Length == 0 || architecture == "neutral" || x.Architecture == architecture ||
                x.Architecture is "" or "neutral")).ToArray();

    private static Dictionary<string, string>? Read(string[] lines, int indent)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var switches = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var spaces = line.Length - line.TrimStart(' ').Length;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            if (spaces == indent) switches = line.Trim() == "InstallerSwitches:";
            if (spaces != indent && !(switches && spaces == indent + 2)) continue;
            var trimmed = line.Trim();
            if ("\"'?:&*!".Contains(trimmed[0])) return null;
            if (trimmed.StartsWith("AppsAndFeaturesEntries", StringComparison.Ordinal))
            {
                if (trimmed != "AppsAndFeaturesEntries:" || result.ContainsKey("HasVersionMapping")) return null;
                var end = index + 1;
                while (end < lines.Length)
                {
                    var next = lines[end];
                    if (string.IsNullOrWhiteSpace(next) || next.TrimStart().StartsWith('#')) { end++; continue; }
                    var depth = next.Length - next.TrimStart(' ').Length;
                    if (depth < indent || (depth == indent && !next.TrimStart().StartsWith("- ", StringComparison.Ordinal))) break;
                    end++;
                }
                var entries = lines[(index + 1)..end].Where(x => !string.IsNullOrWhiteSpace(x) && !x.TrimStart().StartsWith('#')).ToArray();
                // A single explicit ARP mapping only. Multiple components/ranges
                // require evidence beyond this bounded reader.
                if (entries.Length == 0 || entries.Count(x => x.TrimStart().StartsWith("- ", StringComparison.Ordinal)) != 1) return null;
                var firstIndent = entries[0].Length - entries[0].TrimStart(' ').Length;
                if (firstIndent != indent && firstIndent != indent + 2) return null;
                var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                {
                    var entryLine = entries[entryIndex];
                    var entryDepth = entryLine.Length - entryLine.TrimStart(' ').Length;
                    if (entryDepth != firstIndent + (entryIndex == 0 ? 0 : 2)) return null;
                    var entryText = entryIndex == 0 ? entryLine.TrimStart()[2..] : entryLine.Trim();
                    var matchEntry = Regex.Match(entryText, @"^(DisplayName|Publisher|DisplayVersion|ProductCode|UpgradeCode|InstallerType)\s*:\s*(.*)$");
                    if (!matchEntry.Success) return null;
                    var scalar = ReadScalar(matchEntry.Groups[2].Value);
                    if (scalar is null || !mapping.TryAdd(matchEntry.Groups[1].Value, scalar)) return null;
                }
                result.Add("HasVersionMapping", "true");
                result.Add("DisplayVersion", mapping.GetValueOrDefault("DisplayVersion", ""));
                result.Add("MappingProductCode", mapping.GetValueOrDefault("ProductCode", ""));
                result.Add("MappingUpgradeCode", mapping.GetValueOrDefault("UpgradeCode", ""));
                index = end - 1;
                continue;
            }
            var list = Regex.Match(trimmed, @"^(RestrictedCapabilities|UnsupportedOSArchitectures)\s*:\s*(.*)$");
            if (list.Success)
            {
                var value = list.Groups[2].Value.Trim();
                var comment = value.IndexOf('#');
                if (comment >= 0) value = value[..comment].Trim();
                if (value is not ("" or "[]")) return null;
                var values = new List<string>();
                if (value.Length == 0)
                {
                    for (; index + 1 < lines.Length; index++)
                    {
                        var next = lines[index + 1];
                        if (string.IsNullOrWhiteSpace(next) || next.TrimStart().StartsWith('#')) continue;
                        var nextIndent = next.Length - next.TrimStart(' ').Length;
                        if (nextIndent > indent && nextIndent != indent + 2) return null;
                        if (nextIndent < indent) break;
                        if (!next.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                        {
                            if (nextIndent > indent || next.Trim() == "-") return null;
                            break;
                        }
                        var scalar = ReadScalar(next.TrimStart()[2..]);
                        if (scalar is null || !Regex.IsMatch(scalar, "^[A-Za-z][A-Za-z0-9]*$")) return null;
                        values.Add(scalar);
                    }
                    // Null/unsupported shapes cannot erase inherited restrictions.
                    // Only an explicit [] represents a reviewed empty override.
                    if (values.Count == 0) return null;
                }
                if (!result.TryAdd(list.Groups[1].Value, string.Join('\u001f', values))) return null;
                continue;
            }
            var match = Regex.Match(trimmed, @"^(PackageIdentifier|PackageVersion|Architecture|Scope|InstallerType|NestedInstallerType|UpgradeBehavior|InstallerUrl|InstallerSha256|ProductCode|Custom|InstallLocation|ElevationRequirement|MinimumOSVersion)\s*:\s*(.*)$");
            if (!match.Success) continue;
            var scalarValue = ReadScalar(match.Groups[2].Value);
            if (scalarValue is null || !result.TryAdd(match.Groups[1].Value, scalarValue)) return null;
            if (match.Groups[1].Value == "ElevationRequirement" &&
                scalarValue is not ("elevationRequired" or "elevationProhibited" or "elevatesSelf")) return null;
            if (match.Groups[1].Value == "MinimumOSVersion" &&
                (!Regex.IsMatch(scalarValue, @"^\d{1,5}(?:\.\d{1,5}){0,3}$") ||
                 scalarValue.Split('.').Any(x => !ushort.TryParse(x, out _)))) return null;
        }
        return result;
    }

    private static string? ReadScalar(string value)
    {
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0) value = value[..comment];
        value = value.Trim();
        if (value.StartsWith('"') || value.StartsWith('\''))
        {
            if (value.Length < 2 || value[^1] != value[0]) return null;
            if (value[0] == '"' && value.Contains('\\')) return null;
            return value[1..^1];
        }
        return value.Length > 0 && "&*!|>[{".Contains(value[0]) ? null : value;
    }

    internal static bool Compatible(string candidate, string architecture) => candidate == "neutral" || candidate == architecture ||
        (architecture == "x64" && candidate == "x86") || (architecture == "arm64" && candidate is "x64" or "x86" or "arm");
}

internal sealed record WinGetInstallerVariant(string Architecture, string Scope, string Type, string Behavior,
    string Url, string Hash, string ProductCode, string Custom, string Elevation, string MinimumOSVersion,
    IReadOnlyList<string> RestrictedCapabilities, IReadOnlyList<string> UnsupportedOSArchitectures, string NestedType,
    string DisplayVersion = "", string MappingProductCode = "", bool HasVersionMapping = false,
    string MappingUpgradeCode = "", string InstallLocation = "", IReadOnlyList<WinGetPackageDependency>? Dependencies = null)
{
    // Null represents a declaration outside the supported subset, never absence.
    internal bool HasDependencies => Dependencies is not { Count: 0 };
}
