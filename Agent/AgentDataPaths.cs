using Microsoft.Extensions.Hosting.WindowsServices;

namespace UpdateCenter.Agent;

internal static class AgentDataPaths
{
    public static string RootDirectory { get; } = BuildRootDirectory();
    public static string OperationsDirectory { get; } = Path.Combine(RootDirectory, "Operations");
    public static string NetworkSettingsFile { get; } = Path.Combine(RootDirectory, "network-settings.json");

    private static string BuildRootDirectory()
    {
        if (!WindowsServiceHelpers.IsWindowsService())
            return Path.Combine(AppContext.BaseDirectory, "AgentData");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "UpdateCenter",
            "Agent");
    }
}
