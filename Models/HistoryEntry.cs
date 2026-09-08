using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UpdateCenter.Models;

public sealed class HistoryEntry : INotifyPropertyChanged
{
    public DateTime Timestamp { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public string Result { get; set; } = "";
    public string DisplayResult => UpdateCenter.Services.LocalizationService.Translate(Result);
    public string Details { get; set; } = "";
    public string DisplayDetails => UpdateCenter.Services.LocalizationService.TranslateHistoryDetails(Details);
    public string Diagnostics { get; set; } = "";
    public string DisplayDate => Timestamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(DisplayResult));
        OnPropertyChanged(nameof(DisplayDetails));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
