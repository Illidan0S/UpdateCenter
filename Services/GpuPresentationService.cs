using UpdateCenter.Models;

namespace UpdateCenter.Services;

public static class GpuPresentationService
{
    private static readonly string[] VirtualAdapterMarkers =
    [
        "Microsoft Basic Display", "Microsoft Remote Display", "Basic Render Driver",
        "VMware", "VirtualBox", "Hyper-V", "Parallels", "Citrix"
    ];

    private static readonly string[] IntegratedAdapterMarkers =
    [
        "Integrated", "Iris", "UHD Graphics", "HD Graphics", "Intel(R) Graphics",
        "Intel Graphics", "Radeon(TM) Graphics", "Radeon Graphics", "Vega Graphics"
    ];

    private static readonly string[] DiscreteAdapterMarkers =
    [
        "GeForce", "Quadro", "NVIDIA RTX", "NVIDIA T", "NVIDIA A", "Tesla",
        "Radeon RX", "Radeon Pro", "FirePro", "FireGL", "Mobility Radeon",
        "Intel Arc A", "Intel Arc B", "Intel Arc Pro"
    ];

    public static GpuAdapterKind Classify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return GpuAdapterKind.Unknown;
        if (ContainsAny(name, VirtualAdapterMarkers)) return GpuAdapterKind.Virtual;
        if (ContainsAny(name, DiscreteAdapterMarkers)) return GpuAdapterKind.Discrete;
        if (ContainsAny(name, IntegratedAdapterMarkers)) return GpuAdapterKind.Integrated;
        return GpuAdapterKind.Unknown;
    }

    public static GpuPresentation Build(IEnumerable<GpuAdapterDescriptor> adapters)
    {
        var detected = adapters
            .Where(adapter => !string.IsNullOrWhiteSpace(adapter.Name))
            .GroupBy(adapter => adapter.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.OrderByDescending(adapter => adapter.MemoryBytes).First() with
            {
                Name = group.Key,
                MemoryBytes = Math.Max(group.Max(adapter => adapter.MemoryBytes), 0)
            })
            .ToList();

        if (detected.Count == 0)
        {
            return new GpuPresentation(
                "Scheda video non rilevata",
                "Identificazione non disponibile",
                "Non esposta dal sistema",
                "Windows e il driver video non hanno fornito informazioni sulla memoria.",
                "Non esposta dal driver o da Windows");
        }

        var classified = detected
            .Select(adapter => (Adapter: adapter, Kind: Classify(adapter.Name)))
            .ToList();
        var hasIntegrated = classified.Any(item => item.Kind == GpuAdapterKind.Integrated);
        var hasDiscrete = classified.Any(item => item.Kind == GpuAdapterKind.Discrete);
        var hasUnknown = classified.Any(item => item.Kind == GpuAdapterKind.Unknown);

        var adaptersLabel = string.Join(Environment.NewLine,
            classified.Select(item => $"{item.Adapter.Name} — {KindLabel(item.Kind)}"));
        var configurationLabel = hasIntegrated && hasDiscrete
            ? $"Configurazione ibrida · {detected.Count} GPU rilevate"
            : detected.Count > 1
                ? $"{detected.Count} GPU rilevate · {ConfigurationDetail(classified)}"
                : hasUnknown
                    ? "Tipo non determinabile con certezza · dati parziali"
                    : $"{KindLabel(classified[0].Kind)} rilevata";

        var primary = classified
            .OrderBy(item => PrimaryPriority(item.Kind))
            .ThenByDescending(item => item.Adapter.MemoryBytes)
            .First();
        var primaryMemoryLabel = MemoryLabel(primary.Adapter.MemoryBytes, primary.Kind, compact: true);
        var memoryDetails = string.Join(Environment.NewLine,
            classified.Select(item => $"{item.Adapter.Name} — {MemoryLabel(item.Adapter.MemoryBytes, item.Kind, compact: false)}"));
        var unavailableUsageMessage = hasIntegrated && !hasDiscrete
            ? "Non esposta dal driver per questa GPU integrata"
            : hasIntegrated && hasDiscrete
                ? "Non esposta per la GPU attualmente attiva"
                : "Non esposta dal driver o da Windows";

        return new GpuPresentation(
            adaptersLabel,
            configurationLabel,
            primaryMemoryLabel,
            memoryDetails,
            unavailableUsageMessage);
    }

    private static string ConfigurationDetail(IEnumerable<(GpuAdapterDescriptor Adapter, GpuAdapterKind Kind)> adapters)
    {
        var kinds = adapters.Select(item => item.Kind).Distinct().ToList();
        return kinds.Count == 1 ? KindLabel(kinds[0]) : "configurazione mista o parzialmente identificata";
    }

    private static string MemoryLabel(long bytes, GpuAdapterKind kind, bool compact)
    {
        if (bytes <= 0)
        {
            return kind switch
            {
                GpuAdapterKind.Integrated => "RAM condivisa dinamicamente",
                GpuAdapterKind.Virtual => "Memoria gestita dall'ambiente virtuale",
                _ => "Quantità non esposta dal driver"
            };
        }

        var size = FormatBytes(bytes);
        return kind switch
        {
            GpuAdapterKind.Integrated => compact
                ? $"{size} riservati + RAM condivisa"
                : $"{size} riservati; usa anche RAM condivisa dinamicamente",
            GpuAdapterKind.Discrete => $"{size} dedicati",
            GpuAdapterKind.Virtual => $"{size} assegnati dall'ambiente virtuale",
            _ => $"{size} segnalati dal driver · tipo di memoria non determinato"
        };
    }

    private static string KindLabel(GpuAdapterKind kind) => kind switch
    {
        GpuAdapterKind.Integrated => "GPU integrata",
        GpuAdapterKind.Discrete => "GPU dedicata",
        GpuAdapterKind.Virtual => "GPU virtuale",
        _ => "tipo non determinato"
    };

    private static int PrimaryPriority(GpuAdapterKind kind) => kind switch
    {
        GpuAdapterKind.Discrete => 0,
        GpuAdapterKind.Integrated => 1,
        GpuAdapterKind.Unknown => 2,
        _ => 3
    };

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024d * 1024 * 1024):0.#} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024d * 1024):0.#} MB";
        return $"{Math.Max(bytes, 0) / 1024d:0.#} KB";
    }
}
