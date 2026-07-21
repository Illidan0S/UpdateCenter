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
    private string _status = Services.LocalizationService.Text("Da aggiornare", "Update available");
    private string _resultDetails = "";
    private string _diagnostics = "";
    private double _progress;
    private bool _canInstall = true;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var allowedValue = CanInstall && value;
            if (_isSelected == allowedValue) return;
            _isSelected = allowedValue;
            OnPropertyChanged();
        }
    }

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required UpdateKind Kind { get; init; }
    public string KindLabel => Kind == UpdateKind.Software
        ? "Software"
        : UpdateCenter.Services.LocalizationService.Text("Driver", "Driver");
    public string Publisher { get; init; } = "";
    public string InstalledVersion { get; init; } = "—";
    public string AvailableVersion { get; init; } = "—";
    public string Source { get; init; } = "";
    public string Size { get; init; } = "—";
    public bool RequiresRestart { get; init; }
    public bool IsImportant { get; init; }
    public bool IsOptional { get; init; }
    public bool CanInstall
    {
        get => _canInstall;
        set
        {
            if (_canInstall == value) return;
            _canInstall = value;
            if (!value) _isSelected = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(PriorityLabel));
            OnPropertyChanged(nameof(PriorityDescription));
            OnPropertyChanged(nameof(CanRetry));
        }
    }
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
    public bool CanOpenOfficialSource => Uri.TryCreate(OfficialReleasePageUrl, UriKind.Absolute, out var uri) &&
                                         uri.Scheme == Uri.UriSchemeHttps;
    public string SourceSummary => string.IsNullOrWhiteSpace(SourceConfidence)
        ? Source
        : $"{Source} · {SourceConfidence}";
    public string PriorityLabel => !CanInstall
        ? UpdateCenter.Services.LocalizationService.Text("Solo verifica", "Review only")
        : IsImportant
        ? UpdateCenter.Services.LocalizationService.Text("Importante", "Important")
        : IsOptional
            ? UpdateCenter.Services.LocalizationService.Text("Facoltativo", "Optional")
            : "Standard";
    public string PriorityDescription => !CanInstall
        ? UpdateCenter.Services.LocalizationService.Text(
            "Questo elemento richiede un aggiornamento manuale dalla fonte ufficiale.",
            "This item requires a manual update from its official source.")
        : IsImportant
        ? UpdateCenter.Services.LocalizationService.Text(
            "Aggiornamento obbligatorio o di sicurezza secondo la fonte ufficiale.",
            "Mandatory or security update according to the source.")
        : IsOptional
            ? UpdateCenter.Services.LocalizationService.Text(
                "Driver non selezionato automaticamente da Windows Update.",
                "Driver not automatically selected by Windows Update.")
            : Kind == UpdateKind.Software
                ? UpdateCenter.Services.LocalizationService.Text(
                    "Aggiornamento software standard disponibile tramite WinGet; non costituisce una raccomandazione.",
                    "Standard software update available through WinGet; this is not a recommendation.")
                : UpdateCenter.Services.LocalizationService.Text(
                    "Driver selezionato automaticamente da Windows Update, senza priorità di sicurezza dichiarata.",
                    "Driver automatically selected by Windows Update, with no declared security priority.");
    public string RestartLabel => RequiresRestart
        ? UpdateCenter.Services.LocalizationService.Text("Sì", "Yes")
        : "No";
    public bool CanRetry => CanInstall && (Status.Equals("Errore", StringComparison.OrdinalIgnoreCase) ||
                            Status.Equals("Error", StringComparison.OrdinalIgnoreCase));
    public bool CanShowDetails => !string.IsNullOrWhiteSpace(ResultDetails) || !string.IsNullOrWhiteSpace(Diagnostics);

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
        set
        {
            _resultDetails = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanShowDetails));
        }
    }

    public string Diagnostics
    {
        get => _diagnostics;
        set
        {
            _diagnostics = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanShowDetails));
        }
    }

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalizedProperties()
    {
        Status = UpdateCenter.Services.LocalizationService.Translate(Status);
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(PriorityLabel));
        OnPropertyChanged(nameof(PriorityDescription));
        OnPropertyChanged(nameof(RestartLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
