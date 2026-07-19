using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UpdateCenter.Models;

public enum UpdateKind
{
    Software,
    Driver
}

public static class DriverInstallModes
{
    public const string WindowsUpdate = "WindowsUpdate";
    public const string OfficialInfPackage = "OfficialInfPackage";
}

public sealed class UpdateItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _status = "Da aggiornare";
    private string _resultDetails = "";
    private double _progress;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required UpdateKind Kind { get; init; }
    public string KindLabel => Kind == UpdateKind.Software ? "Software" : "Driver";
    public string Publisher { get; init; } = "";
    public string InstalledVersion { get; init; } = "—";
    public string AvailableVersion { get; init; } = "—";
    public string Source { get; init; } = "";
    public string Size { get; init; } = "—";
    public bool RequiresRestart { get; init; }
    public bool IsImportant { get; init; }
    public bool IsOptional { get; init; }
    public string? WindowsUpdateId { get; init; }
    public int WindowsUpdateRevision { get; init; }
    public int WindowsUpdateServerSelection { get; init; }
    public string WindowsUpdateServiceId { get; init; } = "";
    public string DriverInstallMode { get; init; } = "";
    public string OfficialReleasePageUrl { get; init; } = "";
    public string OfficialDownloadUrl { get; init; } = "";
    public string ExpectedSha256 { get; init; } = "";
    public List<string> ExpectedSignerSubjects { get; init; } = [];
    public string DriverPackageType { get; init; } = "";
    public List<string> CompatibleHardwareIds { get; init; } = [];
    public string SourceConfidence { get; init; } = "";
    public string CompatibilityDetail { get; init; } = "";
    public string SourceSummary => string.IsNullOrWhiteSpace(SourceConfidence)
        ? Source
        : $"{Source} · {SourceConfidence}";
    public string PriorityLabel => IsImportant
        ? "Importante"
        : IsOptional ? "Facoltativo" : "Standard";
    public string PriorityDescription => IsImportant
        ? "Aggiornamento obbligatorio o di sicurezza secondo la fonte ufficiale."
        : IsOptional
            ? "Driver non selezionato automaticamente da Windows Update."
            : Kind == UpdateKind.Software
                ? "Aggiornamento software standard disponibile tramite WinGet; non costituisce una raccomandazione."
                : "Driver selezionato automaticamente da Windows Update, senza priorità di sicurezza dichiarata.";
    public string RestartLabel => RequiresRestart ? "Sì" : "No";
    public bool CanRetry => Status.Equals("Errore", StringComparison.OrdinalIgnoreCase);

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRetry));
        }
    }

    public string ResultDetails
    {
        get => _resultDetails;
        set { _resultDetails = value ?? ""; OnPropertyChanged(); }
    }

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
