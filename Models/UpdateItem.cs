using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UpdateCenter.Models;

public enum UpdateKind
{
    Software,
    Driver,
    Runtime
}

public static class PackageOperations
{
    public const string Upgrade = "Upgrade";
    public const string Install = "Install";
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
    public string KindLabel => Kind switch
    {
        UpdateKind.Software => "Software",
        UpdateKind.Runtime => "Runtime",
        _ => UpdateCenter.Services.LocalizationService.Text("Driver", "Driver")
    };
    public string Publisher { get; init; } = "";
    public string InstalledVersion { get; init; } = "—";
    public string AvailableVersion { get; init; } = "—";
    public string Source { get; init; } = "";
    public Services.ReleaseNotesInfo ProviderReleaseNotes { get; init; } = new();
    public string PackageOperation { get; init; } = PackageOperations.Upgrade;
    public string WinGetClientVersion { get; set; } = "";
    public WinGetScopeBinding? WinGetBinding { get; set; }
    public WinGetVerificationBinding? WinGetVerification { get; set; }
    public string Size { get; set; } = "—";
    public long DownloadSizeBytes { get; set; }
    public bool RequiresRestart { get; init; }
    public bool IsImportant { get; init; }
    public bool IsOptional { get; init; }
    private bool _requiresRiskConfirmation;
    public bool RequiresRiskConfirmation
    {
        get => _requiresRiskConfirmation;
        set
        {
            if (_requiresRiskConfirmation == value) return;
            _requiresRiskConfirmation = value;
            if (value) IsSelected = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PriorityLabel));
            OnPropertyChanged(nameof(PriorityDescription));
        }
    }
    public bool HasUnverifiedInstallerMetadata { get; set; }
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
    public string DriverDeviceId { get; init; } = "";
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
        : RequiresRiskConfirmation
        ? UpdateCenter.Services.LocalizationService.Text("Conferma", "Confirm")
        : HasUnverifiedInstallerMetadata
        ? UpdateCenter.Services.LocalizationService.Text("Verifica", "Review")
        : IsImportant
        ? UpdateCenter.Services.LocalizationService.Text("Importante", "Important")
        : IsOptional
            ? UpdateCenter.Services.LocalizationService.Text("Facoltativo", "Optional")
            : "Standard";
    public string PriorityDescription => !CanInstall
        ? UpdateCenter.Services.LocalizationService.Text(
            "Questo elemento non è selezionabile in UpdateCenter. Consulta la colonna Stato per i dettagli.",
            "This item cannot be selected in UpdateCenter. See the Status column for details.")
        : RequiresRiskConfirmation
        ? UpdateCenter.Services.LocalizationService.Text(
            "Questo installer può rimuovere la versione funzionante prima di installare quella nuova. È richiesta una conferma separata.",
            "This installer may remove the working version before installing the new one. Separate confirmation is required.")
        : HasUnverifiedInstallerMetadata
        ? UpdateCenter.Services.LocalizationService.Text(
            "I metadati dell'installer non sono disponibili o non sono stati verificati. Questo avviso non richiede una conferma aggiuntiva; restano necessari i controlli prima dell'avvio.",
            "Installer metadata is unavailable or could not be verified. This notice does not require additional confirmation; pre-launch checks remain necessary.")
        : IsImportant
        ? UpdateCenter.Services.LocalizationService.Text(
            "Aggiornamento obbligatorio o di sicurezza secondo la fonte ufficiale.",
            "Mandatory or security update according to the source.")
        : IsOptional
            ? UpdateCenter.Services.LocalizationService.Text(
                "Driver non selezionato automaticamente da Windows Update.",
                "Driver not automatically selected by Windows Update.")
            : Kind is UpdateKind.Software or UpdateKind.Runtime
                ? UpdateCenter.Services.LocalizationService.Text(
                    Kind == UpdateKind.Runtime
                        ? "Runtime disponibile tramite WinGet; verrà installato solo dopo la conferma."
                        : "Aggiornamento software standard disponibile tramite WinGet; non costituisce una raccomandazione.",
                    Kind == UpdateKind.Runtime
                        ? "Runtime available through WinGet; it will be installed only after confirmation."
                        : "Standard software update available through WinGet; this is not a recommendation.")
                : UpdateCenter.Services.LocalizationService.Text(
                    "Driver selezionato automaticamente da Windows Update, senza priorità di sicurezza dichiarata.",
                    "Driver automatically selected by Windows Update, with no declared security priority.");
    public string RestartLabel => RequiresRestart
        ? UpdateCenter.Services.LocalizationService.Text("Sì", "Yes")
        : "No";
    public bool CanRetry => CanInstall && (Status.Equals("Errore", StringComparison.OrdinalIgnoreCase) ||
                            Status.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
                            Status.Equals("Non riuscito", StringComparison.OrdinalIgnoreCase) ||
                            Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
    public bool CanShowDetails => !string.IsNullOrWhiteSpace(ResultDetails) || !string.IsNullOrWhiteSpace(Diagnostics);

    public string Status
    {
        get => Services.LocalizationService.Translate(_status);
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRetry));
        }
    }

    public string ResultDetails
    {
        get => Services.LocalizationService.Translate(_resultDetails);
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
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ResultDetails));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(PriorityLabel));
        OnPropertyChanged(nameof(PriorityDescription));
        OnPropertyChanged(nameof(RestartLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
