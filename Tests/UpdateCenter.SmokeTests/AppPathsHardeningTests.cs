using UpdateCenter.Services;

internal static class AppPathsHardeningTests
{
    internal static void Run(string isolatedRoot)
    {
        var root = Path.GetFullPath(isolatedRoot);
        var expectedDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateCenter");
        if (AppPaths.ResolveDefaultRoot() != expectedDefault || AppPaths.DataDirectory != root ||
            string.Equals(root, expectedDefault, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Smoke paths must be isolated before the first I/O.");

        AppPaths.EnsureCreated();
        foreach (var file in new[] { AppPaths.HistoryFile, AppPaths.SettingsFile, AppPaths.WinGetApplicabilityFile })
            if (Path.GetDirectoryName(file) != root)
                throw new InvalidOperationException("Storage escaped the isolated root: " + file);
        foreach (var directory in new[] { AppPaths.LogsDirectory, AppPaths.UpdatesDirectory })
            if (Path.GetDirectoryName(directory) != root || !Directory.Exists(directory))
                throw new InvalidOperationException("Expected directory in isolated root: " + directory);

        try { AppPaths.InitializeRoot(Path.Combine(root, "late")); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An initialized root was replaced.");
    }
}
