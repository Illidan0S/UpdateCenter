namespace UpdateCenter.Models;

public sealed class HistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public string Result { get; set; } = "";
    public string Details { get; set; } = "";
    public string Diagnostics { get; set; } = "";
    public string DisplayDate => Timestamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}
