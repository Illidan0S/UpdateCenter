using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal sealed class WpfRunnerConsent : IRunnerConsent
{
    public bool Confirm(RunnerPlanSnapshot plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (!HasInteractiveSession() || dispatcher is null || dispatcher.HasShutdownStarted) return false;
        bool Show()
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Read only: runner startup must not migrate settings or initialize the main window.
            var settings = JsonStorage.Read<AppSettings>(AppPaths.SettingsFile);
            if (settings is not null) LocalizationService.Initialize(settings.LanguageMode);
            var window = new RunnerConfirmationWindow(plan);
            using var registration = cancellationToken.Register(() => dispatcher.BeginInvoke(new Action(window.Close)));
            return window.ShowDialog() == true && !cancellationToken.IsCancellationRequested && HasInteractiveSession();
        }
        return dispatcher.CheckAccess() ? Show() : dispatcher.Invoke(Show);
    }

    internal static bool HasInteractiveSession()
    {
        if (!Environment.UserInteractive) return false;
        using var process = Process.GetCurrentProcess();
        if (process.SessionId == 0) return false;
        // A disconnected session must not silently authorize or leave an invisible prompt.
        if (!WTSQuerySessionInformation(IntPtr.Zero, process.SessionId, 8, out var buffer, out var bytes)) return false;
        try { return bytes >= sizeof(int) && Marshal.ReadInt32(buffer) == 0; } // WTSActive
        finally { WTSFreeMemory(buffer); }
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr buffer);
}

internal static class RunnerConsentPresentation
{
    internal static string Describe(RunnerPlanSnapshot plan)
    {
        static string T(string it, string en) => LocalizationService.Text(it, en);
        static string YesNo(bool value) => value ? T("Sì", "Yes") : T("No", "No");
        var text = new StringBuilder();
        text.AppendLine(T("Conferma finale del piano ricevuto", "Final confirmation of the received plan"));
        text.AppendLine(T("Verifica l'intero elenco: la conferma autorizza soltanto questo piano per questa esecuzione.",
            "Review the entire list: confirmation authorizes only this plan for this execution."));
        text.AppendLine($"{T("Aggiornamenti", "Updates")}: {plan.Items.Count}");
        if (plan.Recovery)
            text.AppendLine(T("RECUPERO: autorizzi la preparazione per questo target. Chiusure di processi o arresti temporanei di servizi richiedono anche le conferme specifiche. Il nuovo runner o fallback richiederà una nuova conferma prima di installare.",
                "RECOVERY: authorize preparation for this target. Process closures or temporary service stops also require their specific confirmations. A new runner or fallback requires new confirmation before installing."));
        text.AppendLine($"{T("Installazione software silenziosa", "Silent software installation")}: {YesNo(plan.SilentSoftwareInstall)}");
        text.AppendLine($"{T("Fallback WinGet interattivo", "Interactive WinGet fallback")}: {YesNo(plan.Interactive)}");
        text.AppendLine($"{T("Richiesta punto di ripristino per questo batch", "Restore point request for this batch")}: {YesNo(plan.CreateRestorePoint)}");
        text.AppendLine();
        text.AppendLine(T("I nomi e le versioni installate sono descrizioni ricevute, non prove di sicurezza. L'origine WinGet è un nome configurato, non una garanzia sul contenuto.",
            "Names and installed versions are received descriptions, not safety evidence. A WinGet source is a configured name, not a content guarantee."));
        text.AppendLine(T("I metadati di rischio non sono attestati: gli installer possono rimuovere la versione precedente, interrompere applicazioni o richiedere un riavvio.",
            "Risk metadata is not attested: installers may remove the previous version, interrupt applications or require a restart."));
        if (plan.Items.Any(item => item.Kind == nameof(UpdateKind.Driver)))
            text.AppendLine(T("ATTENZIONE DRIVER: operazioni amministrative sul dispositivo, con possibile interruzione del servizio o riavvio. BIOS e firmware restano esclusi dai controlli del provider.",
                "DRIVER WARNING: administrative device operations may interrupt service or require a restart. Provider checks continue to exclude BIOS and firmware."));
        foreach (var (frozen, index) in plan.Items.Select((item, index) => (item, index)))
        {
            var item = frozen.ToPlanItem();
            text.AppendLine();
            text.AppendLine($"{index + 1}. {item.Name}");
            text.AppendLine($"ID: {item.Id}");
            text.AppendLine($"{T("Tipo / operazione", "Kind / operation")}: {item.Kind} / {item.PackageOperation}");
            text.AppendLine($"{T("Versione installata dichiarata", "Reported installed version")}: {item.InstalledVersion}");
            text.AppendLine($"{T("Versione richiesta", "Requested version")}: {item.AvailableVersion}");
            text.AppendLine($"{T("Origine dichiarata", "Reported source")}: {item.Source}");
            text.AppendLine($"Provider: {(item.Kind == nameof(UpdateKind.Driver) ? item.DriverInstallMode : "WinGet")}");
            if (item.WinGetBinding is { } binding)
            {
                text.AppendLine($"{(binding.CopyEvidence?.UnspecifiedScope == true ? T("Ambito osservato della copia", "Observed installation scope") : T("Ambito vincolato", "Required scope"))}: {binding.Scope}; {T("account", "account")}: {binding.AccountSid}");
                if (binding.CopyEvidence?.UnspecifiedScope == true)
                    text.AppendLine(T("Il manifest non dichiara l'ambito: la stessa copia viene ricontrollata, senza forzare un ambito all'installer.",
                        "The manifest does not declare a scope: the same installation is rechecked without forcing an installer scope."));
                if (binding.CopyEvidence is { ExecutablePath.Length: > 0 } executable)
                    text.AppendLine($"{T("Eseguibile usato per verificare la copia", "Executable used to verify this installation")}: {executable.ProductName} — {executable.ExecutablePath}");
                text.AppendLine($"{T("Copia locale WinGet", "Local WinGet installation")}: {binding.LocalId}");
                if (binding.InstalledType.Length > 0)
                {
                    text.AppendLine($"{T("Categoria installata verificata", "Verified installed category")}: {binding.InstalledType}");
                    if (binding.ElevationRequirement.Length == 0)
                        text.AppendLine(T("Il manifest non dichiara i privilegi richiesti dall'installer.",
                            "The manifest does not declare the privileges required by the installer."));
                }
                if (binding.CopyEvidence?.MsiRegistration is { } msi)
                    text.AppendLine($"{T("Registrazione Windows Installer verificata", "Verified Windows Installer registration")}: " +
                        $"{msi.ProductCode}; {T("contesto", "context")}: {msi.Context}; {T("versione", "version")}: {msi.ProductVersion}; " +
                        $"{T("lingua", "language")}: {msi.Language}");
                if (WinGetDependencyPolicy.Read(binding.DependencySnapshot) is { } dependencies)
                {
                    text.AppendLine(T("Dipendenze già presenti: saranno ricontrollate; non sono autorizzate installazioni aggiuntive di dipendenze da WinGet.",
                        "Dependencies already present: they will be rechecked; additional dependency installations by WinGet are not authorized."));
                    foreach (var dependency in dependencies)
                    {
                        var version = dependency.MinimumVersion.Length == 0 ?
                            $"WinGet {dependency.Version}, ARP {dependency.DisplayVersion} ({dependency.Publisher})" :
                            $"{dependency.DisplayVersion} >= {dependency.MinimumVersion}";
                        text.AppendLine($"  {dependency.Id}: {version}; machine; {dependency.LocalId}");
                    }
                }
                text.AppendLine($"{T("Tipo installer", "Installer type")}: {binding.InstallerType}; " +
                    $"{T("Architettura vincolata", "Required architecture")}: {(binding.Architecture.Length == 0 ? T("selezione compatibile WinGet", "WinGet compatibility selection") : binding.Architecture)}");
                text.AppendLine(T("Identità e metadati saranno ricontrollati prima dell'avvio; un cambiamento richiede un nuovo piano.",
                    "Identity and metadata will be rechecked before launch; a change requires a new plan."));
                if (binding.PythonBurnTransition is { } transition)
                {
                    text.AppendLine($"{T("Percorso Python conservato", "Preserved Python path")}: {transition.InstallPath}");
                    text.AppendLine($"{T("Funzionalità conservate", "Preserved features")}: {transition.FeatureNames}");
                    text.AppendLine(T("Upgrade della stessa linea Python per utente; Python Launcher è escluso. Nessun cambio di ambito previsto.",
                        "Upgrade of the same per-user Python release line; Python Launcher is excluded. No scope change is planned."));
                    text.AppendLine($"{T("Bundle target atteso", "Expected target bundle")}: {transition.TargetLocalId}");
                }
            }
            if (item.WinGetBinding?.Safety == "RemovesPreviousVersion" || item.RemovalManifestSha256.Length > 0)
                text.AppendLine(T("RIMOZIONE DICHIARATA: la variante richiede la rimozione della versione precedente.",
                    "DECLARED REMOVAL: the variant requires removing the previous version."));
            if (item.DriverInstallMode == DriverInstallModes.WindowsUpdate)
            {
                text.AppendLine($"UpdateID: {item.WindowsUpdateId}; {T("revisione", "revision")}: {item.WindowsUpdateRevision}");
                text.AppendLine($"ServerSelection: {item.WindowsUpdateServerSelection}; ServiceID: {item.WindowsUpdateServiceId}");
                text.AppendLine(T("L'identità WUA è UpdateID/revisione; la versione visualizzata è descrittiva.",
                    "WUA identity is UpdateID/revision; the displayed version is descriptive."));
            }
            if (item.DriverInstallMode == DriverInstallModes.OfficialInfPackage)
            {
                text.AppendLine($"Vendor: {item.Vendor}; {item.DriverPackageType}");
                text.AppendLine($"{T("Pagina ufficiale", "Official page")}: {item.OfficialReleasePageUrl}");
                text.AppendLine($"Download: {item.OfficialDownloadUrl}");
                text.AppendLine($"SHA-256: {item.ExpectedSha256}");
                text.AppendLine($"{T("Firmatari attesi", "Expected signers")}: {string.Join("; ", item.ExpectedSignerSubjects)}");
                text.AppendLine($"Hardware IDs: {string.Join("; ", item.CompatibleHardwareIds)}");
            }
        }
        return text.ToString();
    }
}

// System controls and colours keep this security prompt independent of main-window initialization.
internal sealed class RunnerConfirmationWindow : Window
{
    internal RunnerConfirmationWindow(RunnerPlanSnapshot plan)
    {
        Title = LocalizationService.Text("Update Center — autorizza il piano effettivo", "Update Center — authorize the effective plan");
        var area = SystemParameters.WorkArea;
        Width = Math.Min(840, area.Width); Height = Math.Min(660, area.Height);
        MinWidth = Math.Min(480, area.Width); MinHeight = Math.Min(360, area.Height);
        MaxWidth = area.Width; MaxHeight = area.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SystemColors.WindowBrush; Foreground = SystemColors.WindowTextBrush;
        FontSize = 14;
        foreach (var type in new[] { typeof(Button), typeof(CheckBox), typeof(TextBox) }) Resources[type] = new Style(type);

        var layout = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom);
        var acknowledgement = new CheckBox
        {
            Content = new TextBlock { Text = LocalizationService.Text(
                "Ho verificato l'intero piano e accetto i rischi indicati.", "I reviewed the entire plan and accept the stated risks."), TextWrapping = TextWrapping.Wrap },
            Margin = new Thickness(0, 12, 0, 12)
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = LocalizationService.Text("Annulla", "Cancel"), IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) };
        var confirm = new Button { Content = LocalizationService.Text("Autorizza questo batch", "Authorize this batch"), IsEnabled = false, Padding = new Thickness(16, 8, 16, 8) };
        acknowledgement.Checked += (_, _) => confirm.IsEnabled = true;
        acknowledgement.Unchecked += (_, _) => confirm.IsEnabled = false;
        confirm.Click += (_, _) => { if (acknowledgement.IsChecked == true) DialogResult = true; };
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(confirm); actions.Children.Add(cancel);
        footer.Children.Add(acknowledgement); footer.Children.Add(actions); layout.Children.Add(footer);
        var details = new TextBox { Text = RunnerConsentPresentation.Describe(plan), IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = SystemColors.WindowBrush, Foreground = SystemColors.WindowTextBrush };
        AutomationProperties.SetName(details, LocalizationService.Text("Piano effettivo e rischi", "Effective plan and risks"));
        AutomationProperties.SetName(acknowledgement, LocalizationService.Text("Accetta i rischi del piano", "Accept plan risks"));
        layout.Children.Add(details); Content = layout;
        Loaded += (_, _) => cancel.Focus();
    }
}
