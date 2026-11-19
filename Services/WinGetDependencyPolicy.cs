using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal sealed record WinGetPackageDependency(string Id, string MinimumVersion);
internal sealed record WinGetSatisfiedDependency(string Id, string MinimumVersion, string LocalId,
    string Name, string Version, string Architecture, string DisplayVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InstallerType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Publisher = null);

// Only already satisfied, unique machine packages. Never resolves or installs a
// dependency tree. The canonical string keeps the consent snapshot deeply immutable.
internal static class WinGetDependencyPolicy
{
    internal static IReadOnlyList<WinGetPackageDependency>? ParseDeclarations(string[] mapping, int indent)
    {
        var lines = mapping
            .Select(x => x.Split(" #", 2, StringSplitOptions.None)[0].TrimEnd())
            .Where(x => x.Length > 0 && !x.TrimStart().StartsWith('#')).ToArray();
        var declarations = lines.Select((line, index) => (line, index))
            .Where(x => Depth(x.line) == indent &&
                Regex.IsMatch(x.line.TrimStart(), @"^(?:Package)?Dependencies\s*:", RegexOptions.IgnoreCase)).ToArray();
        if (declarations.Length == 0) return [];
        if (declarations.Length != 1 || declarations[0].line[indent..] != "Dependencies:") return null;
        var start = declarations[0].index + 1;
        var end = start;
        while (end < lines.Length && Depth(lines[end]) > indent) end++;
        var block = lines[start..end].Select(x => x[indent..]).ToArray();
        if (block is ["  PackageDependencies: []"]) return [];
        if (block.Length < 2 || block[0] != "  PackageDependencies:") return null;
        var result = new List<WinGetPackageDependency>();
        for (var index = 1; index < block.Length;)
        {
            var id = Regex.Match(block[index++], @"^(  |    )- PackageIdentifier: (\S+)$");
            if (!id.Success) return null;
            var minimum = index < block.Length
                ? Regex.Match(block[index], "^" + id.Groups[1].Value + @"  MinimumVersion: (\S+)$")
                : Match.Empty;
            if (minimum.Success) index++;
            var packageId = Scalar(id.Groups[2].Value);
            var version = minimum.Success ? Scalar(minimum.Groups[1].Value) : "";
            if (!ValidId(packageId) || (minimum.Success && !Numeric(version)) ||
                result.Any(x => x.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))) return null;
            result.Add(new(packageId, version));
            if (result.Count > 8) return null;
        }
        return result.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }

    private static int Depth(string line) => line.Length - line.TrimStart(' ').Length;

    internal static async Task<string?> CaptureAsync(WinGetInstallerMetadata metadata, WinGetScopeBinding binding,
        Func<string, string, CancellationToken, Task<WinGetScopeGuard.InstalledIdentity?>> readIdentity,
        Func<string, string?>? readRaw, CancellationToken token)
    {
        var dependencies = EligibleDependencies(metadata, binding);
        if (dependencies is null || readRaw is null) return null;
        var evidence = new List<(WinGetSatisfiedDependency Dependency, WinGetScopeGuard.InstalledIdentity Identity)>();
        foreach (var dependency in dependencies)
        {
            token.ThrowIfCancellationRequested();
            var identity = await readIdentity(dependency.Id, "", token).ConfigureAwait(false);
            if (identity is null || identity.Scope != "machine" ||
                (identity.InstallerType.Length > 0 && !ValidInstallerType(identity.InstallerType)) ||
                identity.Architecture is not ("x86" or "x64" or "arm" or "arm64") ||
                await readIdentity(dependency.Id, "machine", token).ConfigureAwait(false) != identity) return null;
            var raw = readRaw(identity.LocalId);
            if (!ValidObservedVersions(identity.Version, raw, dependency.MinimumVersion) ||
                (dependency.MinimumVersion == "" &&
                    (identity.Architecture != binding.Architecture || identity.InstallerType.Length == 0 ||
                     string.IsNullOrWhiteSpace(identity.Publisher)))) return null;
            evidence.Add((new(dependency.Id, dependency.MinimumVersion, identity.LocalId, identity.Name,
                identity.Version, identity.Architecture, raw!,
                identity.InstallerType.Length == 0 ? null : identity.InstallerType, identity.Publisher), identity));
        }
        // Repeat all observations after collecting the whole set, not just each
        // package in isolation: a change during the other queries invalidates it.
        foreach (var (dependency, firstIdentity) in evidence)
        {
            token.ThrowIfCancellationRequested();
            var identity = await readIdentity(dependency.Id, "", token).ConfigureAwait(false);
            if (identity != firstIdentity ||
                await readIdentity(dependency.Id, "machine", token).ConfigureAwait(false) != identity ||
                readRaw(dependency.LocalId) != dependency.DisplayVersion) return null;
        }
        token.ThrowIfCancellationRequested();
        return JsonSerializer.Serialize(evidence.Select(x => x.Dependency).ToArray());
    }

    internal static bool Matches(WinGetInstallerMetadata metadata, WinGetScopeBinding binding)
    {
        var dependencies = EligibleDependencies(metadata, binding);
        return dependencies is not null && Read(binding.DependencySnapshot) is { } evidence &&
            evidence.Select(x => new WinGetPackageDependency(x.Id, x.MinimumVersion)).SequenceEqual(dependencies);
    }

    internal static WinGetSatisfiedDependency[]? Read(string? snapshot)
    {
        if (snapshot is null || snapshot.Length is < 2 or > 16000) return null;
        try
        {
            var result = JsonSerializer.Deserialize<WinGetSatisfiedDependency[]>(snapshot);
            if (result is null || result.Length is < 1 or > 8 || result.Any(x => x is null || !ValidId(x.Id) ||
                string.IsNullOrWhiteSpace(x.Name) || x.Name.Any(char.IsControl) ||
                x.LocalId is null || !Regex.IsMatch(x.LocalId, @"^ARP\\Machine\\(?:X86|X64|Arm|Arm64)\\[^\\\p{C}]+$") ||
                x.Architecture is not ("x86" or "x64" or "arm" or "arm64") ||
                (x.InstallerType is not null && !ValidInstallerType(x.InstallerType)) ||
                (x.MinimumVersion == "" && (x.InstallerType is null || string.IsNullOrWhiteSpace(x.Publisher) ||
                    x.Publisher.Length > 256 || x.Publisher.Any(char.IsControl))) ||
                !ValidObservedVersions(x.Version, x.DisplayVersion, x.MinimumVersion)) ||
                result.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length ||
                JsonSerializer.Serialize(result) != snapshot) return null;
            return result;
        }
        catch (JsonException) { return null; }
    }

    private static IReadOnlyList<WinGetPackageDependency>? EligibleDependencies(WinGetInstallerMetadata metadata, WinGetScopeBinding binding)
    {
        if (binding.Scope != "machine" || binding.CopyEvidence is null || binding.PythonBurnTransition is not null ||
            binding.Architecture.Length == 0 ||
            binding.InstallerType is not ("exe" or "inno" or "nullsoft" or "burn")) return null;
        var recipes = metadata.DispatchRecipes(binding.Scope, binding.InstallerType, binding.Architecture);
        if (recipes is not [var recipe] || recipe.Type != binding.InstallerType ||
            recipe.Dependencies is not { Count: > 0 } dependencies ||
            dependencies.Any(x => x.Id.Equals(metadata.Id, StringComparison.OrdinalIgnoreCase))) return null;
        if (binding.ElevationRequirement == "elevationRequired")
            return dependencies.All(x => Numeric(x.MinimumVersion)) ? dependencies : null;

        var key = binding.LocalId.Split('\\').Last();
        // Both catalog version and ARP mapping refer to this exact traditional copy.
        // A version-free dependency never authorizes an MSI transition or an unbound recipe.
        if (binding is not { ElevationRequirement: "", Safety: "Safe", InstallerType: "nullsoft",
                CopyEvidence: { MsiRegistration: null, UnspecifiedScope: false } } ||
            recipe is not { Scope: "machine", Behavior: "install", Elevation: "", NestedType: "", Custom: "",
                RestrictedCapabilities.Count: 0 } || recipe.Architecture != binding.Architecture ||
            !recipe.ProductCode.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            !recipe.MappingProductCode.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            !Numeric(metadata.Version) || !Numeric(recipe.DisplayVersion) || !Numeric(binding.CopyEvidence.DisplayVersion) ||
            SoftwareVersionEvidence.Compare(recipe.DisplayVersion, metadata.Version) != SoftwareVersionRelation.Equal ||
            SoftwareVersionEvidence.Compare(recipe.DisplayVersion, binding.CopyEvidence.DisplayVersion) != SoftwareVersionRelation.Greater)
            return null;
        return dependencies.All(x => x.MinimumVersion == "") ? dependencies : null;
    }

    internal static bool ValidSnapshot(WinGetScopeBinding binding) => Read(binding.DependencySnapshot) is { } items &&
        (binding.ElevationRequirement == "elevationRequired" ? items.All(x => Numeric(x.MinimumVersion)) :
            binding is { ElevationRequirement: "", InstallerType: "nullsoft", Safety: "Safe",
                CopyEvidence: { MsiRegistration: null, UnspecifiedScope: false } } && items.All(x => x.MinimumVersion == ""));

    // For presence alone, observe and freeze provider and ARP values independently.
    // A minimum needs both versions in the same comparable domain.
    private static bool ValidObservedVersions(string provider, string? raw, string? minimum) =>
        Numeric(provider) && Numeric(raw) && (minimum == "" || Numeric(minimum) &&
            SoftwareVersionEvidence.Compare(provider, raw) == SoftwareVersionRelation.Equal &&
            SoftwareVersionEvidence.Compare(raw, minimum) is SoftwareVersionRelation.Equal or SoftwareVersionRelation.Greater);

    internal static bool SameNumericVersion(string? first, string? second) => Numeric(first) && Numeric(second) &&
        SoftwareVersionEvidence.Compare(first, second) == SoftwareVersionRelation.Equal;
    private static string Scalar(string value) => value.Length > 1 && value[0] is '\'' or '"' && value[^1] == value[0] ? value[1..^1] : value;
    private static bool ValidInstallerType(string value) => Regex.IsMatch(value, @"^[a-z][a-z0-9]{0,31}$");
    private static bool ValidId(string? id) => id is not null && Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9.+_-]{0,255}$");
    private static bool Numeric(string? version) => version is not null && version.Length <= 128 && Regex.IsMatch(version, @"^\d+(?:\.\d+)*$");
}
