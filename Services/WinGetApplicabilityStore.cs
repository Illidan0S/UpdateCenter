using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class WinGetApplicabilitySuppression
{
    public string PackageId { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
}

public static class WinGetApplicabilityStore
{
    private static readonly object Gate = new();
    private static readonly TimeSpan Retention = TimeSpan.FromDays(180);

    public static IReadOnlyList<UpdateItem> ExcludeSuppressed(IReadOnlyList<UpdateItem> items)
    {
        lock (Gate)
        {
            var entries = LoadCurrent();
            return items.Where(item => !entries.Any(entry => Matches(entry, item))).ToList();
        }
    }

    public static void RecordNotApplicable(PlanItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.AvailableVersion)) return;
        lock (Gate)
        {
            var entries = LoadCurrent()
                .Where(entry => !entry.PackageId.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            entries.Add(new WinGetApplicabilitySuppression
            {
                PackageId = item.Id.Trim(),
                InstalledVersion = item.InstalledVersion.Trim(),
                AvailableVersion = item.AvailableVersion.Trim(),
                RecordedUtc = DateTime.UtcNow
            });
            JsonStorage.WriteAtomic(AppPaths.WinGetApplicabilityFile,
                entries.OrderByDescending(x => x.RecordedUtc).Take(512).ToList());
        }
    }

    internal static bool Matches(WinGetApplicabilitySuppression entry, UpdateItem item) =>
        entry.PackageId.Equals(item.Id, StringComparison.OrdinalIgnoreCase) &&
        entry.InstalledVersion.Equals(item.InstalledVersion, StringComparison.OrdinalIgnoreCase) &&
        entry.AvailableVersion.Equals(item.AvailableVersion, StringComparison.OrdinalIgnoreCase);

    private static List<WinGetApplicabilitySuppression> LoadCurrent()
    {
        AppPaths.EnsureCreated();
        var limit = DateTime.UtcNow - Retention;
        return (JsonStorage.Read<List<WinGetApplicabilitySuppression>>(AppPaths.WinGetApplicabilityFile) ?? [])
            .Where(x => x.RecordedUtc >= limit &&
                        !string.IsNullOrWhiteSpace(x.PackageId) &&
                        !string.IsNullOrWhiteSpace(x.AvailableVersion))
            .ToList();
    }
}
