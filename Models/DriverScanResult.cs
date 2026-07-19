namespace UpdateCenter.Models;

public sealed class DriverScanResult
{
    public IReadOnlyList<UpdateItem> Updates { get; init; } = [];
    public IReadOnlyList<string> SourcesChecked { get; init; } = [];
    public IReadOnlyList<string> SourceWarnings { get; init; } = [];
}
