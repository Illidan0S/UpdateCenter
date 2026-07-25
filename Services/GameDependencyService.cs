using System.Diagnostics;
using Microsoft.Win32;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class RuntimePackageCatalog
{
    private static readonly HashSet<string> ExactPackageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.DirectX",
        "Microsoft.VCRedist.2015+.x64",
        "Microsoft.VCRedist.2015+.x86",
        "OpenAL.OpenAL",
        "Nvidia.PhysX",
        "Microsoft.EdgeWebView2Runtime",
        "Microsoft.XNARedist",
        "Mono.Mono",
        "Oracle.JavaRuntimeEnvironment"
    };

    public static bool IsRuntimePackageId(string packageId) =>
        ExactPackageIds.Contains(packageId) ||
        packageId.StartsWith("Microsoft.DotNet.DesktopRuntime.", StringComparison.OrdinalIgnoreCase) ||
        packageId.StartsWith("Microsoft.DotNet.Runtime.", StringComparison.OrdinalIgnoreCase) ||
        packageId.StartsWith("EclipseAdoptium.Temurin.", StringComparison.OrdinalIgnoreCase) &&
        packageId.EndsWith(".JRE", StringComparison.OrdinalIgnoreCase);
}

public sealed class GameDependencyService
{
    public Task<IReadOnlyList<GameDependencyItem>> ScanAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<GameDependencyItem>>(() => Scan(cancellationToken), cancellationToken);

    private static IReadOnlyList<GameDependencyItem> Scan(CancellationToken cancellationToken)
    {
        var items = new List<GameDependencyItem>();
        cancellationToken.ThrowIfCancellationRequested();
        AddDirectX(items);
        cancellationToken.ThrowIfCancellationRequested();
        AddVisualCpp(items, RegistryView.Registry64, "x64");
        AddVisualCpp(items, RegistryView.Registry32, "x86");
        cancellationToken.ThrowIfCancellationRequested();
        AddOpenAl(items);
        AddVulkan(items);
        AddDotNetDesktop(items);
        AddInstalledApplication(items, "NVIDIA PhysX", "x86/x64", ["NVIDIA PhysX"],
            "Nvidia.PhysX", true, "Runtime fisico usato da numerosi giochi, soprattutto meno recenti.");
        AddWebView2(items);
        AddInstalledApplication(items, "Java Runtime", "x64", ["Temurin", "Java(TM)", "OpenJDK", "Java SE"],
            "", true, "Alcuni giochi, server e launcher Java richiedono una versione specifica: Update Center evita installazioni generiche non richieste.");
        AddInstalledApplication(items, "Microsoft XNA Framework", "x86", ["Microsoft XNA Framework"],
            "Microsoft.XNARedist", true, "Runtime legacy richiesto da alcuni giochi XNA.");
        AddInstalledApplication(items, "Mono Runtime", "x86/x64", ["Mono for Windows", "Mono Runtime"],
            "Mono.Mono", true, "Runtime usato da alcuni giochi e strumenti basati su Mono.");
        LogService.Write($"Scansione runtime completata: {items.Count} componenti controllati.");
        return items;
    }

    private static void AddDirectX(List<GameDependencyItem> items)
    {
        var system32 = Environment.SystemDirectory;
        var directXFile = Path.Combine(system32, "d3d12.dll");
        var version = ReadRegistryValue(RegistryView.Registry64,
            @"SOFTWARE\Microsoft\DirectX", "Version");
        items.Add(new GameDependencyItem
        {
            Name = "DirectX di Windows",
            Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            IsAvailable = File.Exists(directXFile),
            InstalledVersion = string.IsNullOrWhiteSpace(version) ? ReadFileVersion(directXFile) : version,
            Detail = "Componente grafico integrato in Windows e aggiornato tramite Windows Update."
        });

        var legacyFiles = new[]
        {
            Path.Combine(system32, "d3dx9_43.dll"),
            Path.Combine(system32, "XAudio2_7.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "d3dx9_43.dll")
        };
        var legacyFound = legacyFiles.Count(File.Exists);
        items.Add(new GameDependencyItem
        {
            Name = "DirectX End-User Runtimes (legacy)",
            Architecture = "x86/x64",
            IsAvailable = legacyFound >= 2,
            IsOptional = true,
            PackageId = "Microsoft.DirectX",
            InstalledVersion = legacyFound >= 2 ? "Giugno 2010 / componenti presenti" : "—",
            Detail = legacyFound >= 2
                ? "Librerie legacy rilevate per giochi meno recenti."
                : "Opzionale: alcuni giochi meno recenti richiedono D3DX9 o XAudio 2.7."
        });
    }

    private static void AddVisualCpp(List<GameDependencyItem> items, RegistryView view, string architecture)
    {
        const string path = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes";
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var key = baseKey.OpenSubKey($@"{path}\{architecture}");
        var installed = ConvertToInt(key?.GetValue("Installed")) == 1;
        var version = Convert.ToString(key?.GetValue("Version"))?.Trim() ?? "";
        items.Add(new GameDependencyItem
        {
            Name = "Microsoft Visual C++ 2015–2022",
            Architecture = architecture,
            IsAvailable = installed,
            PackageId = architecture == "x64"
                ? "Microsoft.VCRedist.2015+.x64"
                : "Microsoft.VCRedist.2015+.x86",
            InstalledVersion = installed && !string.IsNullOrWhiteSpace(version) ? version : "—",
            Detail = installed
                ? "Runtime unificato usato dalla maggior parte dei giochi moderni."
                : $"Runtime {architecture} non rilevato nel registro di Windows."
        });
    }

    private static void AddOpenAl(List<GameDependencyItem> items)
    {
        var candidates = SystemLibraryCandidates("OpenAL32.dll");
        var path = candidates.FirstOrDefault(File.Exists) ?? "";
        items.Add(new GameDependencyItem
        {
            Name = "OpenAL",
            Architecture = "x86/x64",
            IsAvailable = !string.IsNullOrWhiteSpace(path),
            IsOptional = true,
            PackageId = "OpenAL.OpenAL",
            InstalledVersion = ReadFileVersion(path),
            Detail = string.IsNullOrWhiteSpace(path)
                ? "Opzionale: richiesto principalmente da alcuni giochi meno recenti."
                : "Libreria audio OpenAL rilevata nel sistema."
        });
    }

    private static void AddVulkan(List<GameDependencyItem> items)
    {
        var path = SystemLibraryCandidates("vulkan-1.dll").FirstOrDefault(File.Exists) ?? "";
        var hasRegisteredDriver = HasRegistryValues(RegistryView.Registry64,
                                      @"SOFTWARE\Khronos\Vulkan\Drivers") ||
                                  HasRegistryValues(RegistryView.Registry32,
                                      @"SOFTWARE\Khronos\Vulkan\Drivers");
        items.Add(new GameDependencyItem
        {
            Name = "Vulkan Runtime",
            Architecture = "x86/x64",
            IsAvailable = !string.IsNullOrWhiteSpace(path) && hasRegisteredDriver,
            IsOptional = true,
            InstalledVersion = ReadFileVersion(path),
            Detail = !string.IsNullOrWhiteSpace(path) && hasRegisteredDriver
                ? "Loader Vulkan e driver grafico registrato correttamente."
                : "Opzionale: normalmente viene installato insieme al driver della GPU."
        });
    }

    private static void AddDotNetDesktop(List<GameDependencyItem> items)
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "shared", "Microsoft.WindowsDesktop.App"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "dotnet", "shared", "Microsoft.WindowsDesktop.App")
        };
        var versions = roots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root).Select(Path.GetFileName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ParseVersion)
            .ToList();
        items.Add(new GameDependencyItem
        {
            Name = ".NET Desktop Runtime",
            Architecture = "x86/x64",
            IsAvailable = versions.Count > 0,
            IsOptional = true,
            InstalledVersion = versions.FirstOrDefault() ?? "—",
            Detail = versions.Count > 0
                ? $"Versioni rilevate: {string.Join(", ", versions.Take(5))}."
                : "Nessun .NET Desktop Runtime separato rilevato; alcuni giochi o launcher possono richiederlo."
        });
    }

    private static void AddWebView2(List<GameDependencyItem> items)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "EdgeWebView", "Application");
        var versions = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Select(Path.GetFileName)
                .Where(x => Version.TryParse(x, out _)).OrderByDescending(ParseVersion).ToList()
            : [];
        items.Add(new GameDependencyItem
        {
            Name = "Microsoft Edge WebView2 Runtime",
            Architecture = "x86/x64",
            IsAvailable = versions.Count > 0,
            IsOptional = true,
            InstalledVersion = versions.FirstOrDefault() ?? "—",
            PackageId = "Microsoft.EdgeWebView2Runtime",
            Detail = versions.Count > 0
                ? "Runtime web integrato usato da launcher e interfacce di alcuni giochi."
                : "Runtime non rilevato; alcuni launcher possono richiederlo."
        });
    }

    private static void AddInstalledApplication(
        List<GameDependencyItem> items,
        string name,
        string architecture,
        IReadOnlyList<string> displayNameFragments,
        string packageId,
        bool optional,
        string detail)
    {
        var match = FindInstalledApplication(displayNameFragments);
        items.Add(new GameDependencyItem
        {
            Name = name,
            Architecture = architecture,
            IsAvailable = match is not null,
            IsOptional = optional,
            InstalledVersion = match?.Version ?? "—",
            PackageId = packageId,
            Detail = detail
        });
    }

    private static InstalledApplication? FindInstalledApplication(IReadOnlyList<string> fragments)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(subKeyName);
                    var displayName = Convert.ToString(key?.GetValue("DisplayName"))?.Trim() ?? "";
                    if (!fragments.Any(fragment => displayName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var version = Convert.ToString(key?.GetValue("DisplayVersion"))?.Trim();
                    return new InstalledApplication(displayName, string.IsNullOrWhiteSpace(version) ? "Rilevato" : version);
                }
            }
            catch { }
        }
        return null;
    }

    private static IEnumerable<string> SystemLibraryCandidates(string fileName)
    {
        yield return Path.Combine(Environment.SystemDirectory, fileName);
        if (Environment.Is64BitOperatingSystem)
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", fileName);
    }

    private static string ReadRegistryValue(RegistryView view, string path, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(path);
            return Convert.ToString(key?.GetValue(name))?.Trim() ?? "";
        }
        catch { return ""; }
    }

    private static bool HasRegistryValues(RegistryView view, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetValueNames().Length > 0;
        }
        catch { return false; }
    }

    private static int ConvertToInt(object? value)
    {
        try { return Convert.ToInt32(value); }
        catch { return 0; }
    }

    private static string ReadFileVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "—";
        try { return FileVersionInfo.GetVersionInfo(path).ProductVersion?.Trim() ?? "Rilevato"; }
        catch { return "Rilevato"; }
    }

    private static Version ParseVersion(string? value) =>
        Version.TryParse(value?.Split('-', 2)[0], out var parsed) ? parsed : new Version(0, 0);

    private sealed record InstalledApplication(string Name, string Version);
}
