using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class WinGetB2Inventory
{
    internal static async Task RunAsync()
    {
        if (Path.GetFullPath(AppPaths.DataDirectory).Equals(Path.GetFullPath(AppPaths.ResolveDefaultRoot()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production data root is forbidden.");
        Console.WriteLine("Isolated data root: " + AppPaths.DataDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        // Production detection only. No installer/runner/UI paths are called.
        var items = await new WinGetService().ScanAsync(false, timeout.Token);
        var initial = items.Count(x => x.CanInstall && x.IsSelected);
        var vm = new MainViewModel(null!);
        foreach (var item in items) vm.Updates.Add(item);
        vm.SetAllSelected(true);
        var report = new
        {
            ObservedUtc = DateTime.UtcNow, Scope = "WinGet detection only; WUA not queried",
            Detected = items.Count, InitialSelected = initial, BulkSelected = vm.SelectedCount,
            IndividuallyEligible = items.Count(x => x.CanInstall), Blocked = items.Count(x => !x.CanInstall),
            SpecificConsent = items.Count(x => x.CanInstall && x.RequiresRiskConfirmation),
            Scoped = items.Count(x => x.WinGetBinding is not null),
            Items = items.Select(x => new { x.Id, x.Name, x.InstalledVersion, x.AvailableVersion, x.Source,
                x.CanInstall, x.RequiresRiskConfirmation, x.HasUnverifiedInstallerMetadata,
                Scope = x.WinGetBinding?.Scope, Architecture = x.WinGetBinding?.Architecture,
                InstallerType = x.WinGetBinding?.InstallerType, x.ResultDetails }).ToArray()
        };
        var directory = Path.GetFullPath("_output/b2-20261109");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "inventory-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Observed WinGet: {report.Detected} detected; {report.InitialSelected} initial; {report.BulkSelected} bulk; " +
            $"{report.IndividuallyEligible} individual; {report.Blocked} blocked; {report.SpecificConsent} specific consent; {report.Scoped} scoped.");
        Console.WriteLine(path);
    }
}
