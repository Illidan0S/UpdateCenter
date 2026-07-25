namespace UpdateCenter.Models;

public enum GpuAdapterKind
{
    Integrated,
    Discrete,
    Virtual,
    Unknown
}

public enum GpuMemoryDisplayMode
{
    Integrated,
    Discrete,
    Hybrid,
    Unknown
}

public sealed record GpuAdapterDescriptor(string Name, long MemoryBytes);

public sealed record GpuPresentation(
    string AdaptersLabel,
    string ConfigurationLabel,
    string PrimaryMemoryLabel,
    string MemoryDetails,
    string UnavailableUsageMessage,
    string MemoryUsageHeading,
    GpuMemoryDisplayMode MemoryDisplayMode);
