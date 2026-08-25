using System.Windows;
using System.Windows.Input;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter;

public partial class WinGetProcessRecoveryWindow : Window
{
    public WinGetProcessRecoveryWindow(
        string title,
        string message,
        string processText,
        string confirmText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ProcessText.Text = processText;
        ConfirmButton.Content = confirmText;
        Loaded += (_, _) => LocalizationService.ApplyTo(this);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}

internal sealed class WpfWinGetProcessRecoveryPrompt(Window owner) : IWinGetProcessRecoveryPrompt
{
    public bool ConfirmGracefulClose(
        UpdateItem item,
        IReadOnlyList<WinGetProcessCandidate> candidates)
    {
        var externalNames = candidates
            .Where(candidate => candidate.Classification ==
                                WinGetBlockerClassification.ExternalConfirmedBlocker)
            .Select(candidate => candidate.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var message = externalNames.Count > 0
            ? LocalizationService.Text(
                $"{string.Join(", ", externalNames)} sta utilizzando un componente di {item.Name} " +
                "e impedisce l'aggiornamento. Vuoi chiuderlo e riprovare? " +
                "Salva eventuale lavoro prima di continuare.",
                $"{string.Join(", ", externalNames)} is using a {item.Name} component and is blocking " +
                "the update. Close it and retry? Save any work before continuing.")
            : LocalizationService.Text(
                $"{item.Name} è ancora in uso e impedisce l'aggiornamento. " +
                "Salva eventuale lavoro prima di continuare.",
                $"{item.Name} is still in use and is blocking the update. " +
                "Save any work before continuing.");
        return Show(
            LocalizationService.Text("Applicazione ancora aperta", "Application still open"),
            message,
            candidates,
            LocalizationService.Text("Chiudi e riprova", "Close and retry"));
    }

    public bool ConfirmForcedTermination(
        UpdateItem item,
        IReadOnlyList<WinGetProcessCandidate> candidates)
    {
        var processNames = string.Join(", ", candidates
            .Select(candidate => candidate.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return Show(
            LocalizationService.Text("Chiusura forzata", "Force close"),
            LocalizationService.Text(
                $"{processNames} non si è chiuso normalmente. Vuoi terminarlo forzatamente? " +
                "Potresti perdere dati non salvati.",
                $"{processNames} did not close normally. Force it to close? You may lose unsaved data."),
            candidates,
            LocalizationService.Text("Termina e riprova", "Terminate and retry"));
    }

    public bool ConfirmInteractiveInstaller(UpdateItem item) =>
        Show(
            LocalizationService.Text("Installer interattivo", "Interactive installer"),
            LocalizationService.Text(
                "Windows non permette di identificare con sicurezza quale applicazione sta bloccando " +
                $"l'aggiornamento di {item.Name}. Puoi aprire l'installer in modalità interattiva per " +
                "visualizzare direttamente eventuali applicazioni in conflitto.",
                "Windows cannot safely identify which application is blocking the update for " +
                $"{item.Name}. You can open the interactive installer to see any conflicting applications."),
            [],
            LocalizationService.Text("Apri installer", "Open installer"));

    public void ShowManualCloseRequired(UpdateItem item, string detail) =>
        MessageBox.Show(
            owner,
            LocalizationService.Text(
                detail,
                detail),
            LocalizationService.Text("Chiudi manualmente l'app", "Close the app manually"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private bool Show(
        string title,
        string message,
        IReadOnlyList<WinGetProcessCandidate> candidates,
        string confirmText)
    {
        var processText = candidates.Count == 0
            ? ""
            : LocalizationService.Text("Processi rilevati: ", "Detected processes: ") +
              string.Join(", ", candidates.Select(candidate =>
                  $"{candidate.ProcessName} (PID {candidate.ProcessId})"));
        var dialog = new WinGetProcessRecoveryWindow(title, message, processText, confirmText)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true;
    }
}
