namespace UpdateCenter.Services;

internal sealed record PackageRecoveryHint(IReadOnlyList<string> SharedResourceRoots);

internal static class PackageRecoveryHints
{
    private const string ObsPackageId = "OBSProject.OBSStudio";

    public static PackageRecoveryHint Get(string packageId)
    {
        if (!packageId.Equals(ObsPackageId, StringComparison.Ordinal))
            return new PackageRecoveryHint([]);

        var commonApplicationData =
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonApplicationData))
            return new PackageRecoveryHint([]);

        var root = Path.GetFullPath(Path.Combine(commonApplicationData, "obs-studio-hook"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return new PackageRecoveryHint([root]);
    }
}
