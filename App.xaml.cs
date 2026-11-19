using System.Windows;
using UpdateCenter.Services;

namespace UpdateCenter;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == PortableSourceUpdate.Mode)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                if (e.Args.Length != 3 || !Guid.TryParseExact(e.Args[1], "N", out var session) ||
                    !int.TryParse(e.Args[2], out var owner))
                    throw new InvalidDataException("Richiesta di recupero non valida.");
                PortableSourceUpdate.ApplyAsync(session, owner).GetAwaiter().GetResult();
                Shutdown(0);
            }
            catch (Exception ex) { LogService.Write("Aggiornamento Portable non completato.", ex); Shutdown(1); }
            return;
        }

        if (e.Args.Length == 4 &&
            e.Args[0].Equals("--apply-update", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(e.Args[2], out var parentProcessId))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = AppUpdateService.ApplyUpdate(e.Args[1], parentProcessId, e.Args[3]);
            Shutdown(exitCode);
            return;
        }

        if (e.Args.Length > 0 &&
            (e.Args[0].Equals("--update-runner-admin", StringComparison.OrdinalIgnoreCase) ||
             e.Args[0].Equals("--update-runner-user", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var requireAdministrator = e.Args[0].Equals("--update-runner-admin", StringComparison.OrdinalIgnoreCase);
            var exitCode = e.Args.Length == 2
                ? ElevatedUpdateRunner.Run(e.Args[1], requireAdministrator)
                : e.Args.Length == 6 && e.Args[2] == "--consent-channel" && int.TryParse(e.Args[4], out var ownerId) &&
                  long.TryParse(e.Args[5], out var ownerCreated)
                    ? ElevatedUpdateRunner.RunWithConsentChannel(e.Args[1], requireAdministrator, e.Args[3], ownerId, ownerCreated)
                    : 2;
            Shutdown(exitCode);
            return;
        }

        if (e.Args.Length == 2 &&
            e.Args[0].Equals("--repair-driver-admin", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = DriverRepairService.RunAdministrator(e.Args[1]);
            Shutdown(exitCode);
            return;
        }

        TaskCompletionSource? updateStartup = null;
        var supervisorId = 0;
        if (e.Args.Length > 0 && e.Args[0].Equals("--update-complete", StringComparison.OrdinalIgnoreCase))
        {
            // The legacy four-argument notification can still start the UI,
            // but it does not authorize removing a backup.
            if (e.Args.Length != 4)
            {
                if (e.Args.Length != 6 || !int.TryParse(e.Args[5], out supervisorId) || supervisorId <= 0 ||
                    !SelfUpdateCompatibility.IsSupported() || !AppUpdateService.IsValidStartupConfirmation(e.Args))
                {
                    Shutdown(1);
                    return;
                }
                AppPaths.UpdateStartupPending = true;
                updateStartup = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Write("Errore non gestito nell'interfaccia.", args.Exception);
            if (!AppPaths.UpdateStartupPending) ShowStartupError(args.Exception);
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                LogService.Write("Errore non gestito nel processo.", exception);
        };

        try
        {
            var settings = JsonStorage.LoadSettings();
            LocalizationService.Initialize(settings.LanguageMode);
            ThemeService.Apply(settings.ThemeMode);
            TypographyService.Apply(settings.FontSizeMode);
            if (e.Args.Length == 2 &&
                e.Args[0].Equals("--connection-request", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(e.Args[1], out var connectionRequestId))
            {
                var requestWindow = new ConnectionRequestWindow(connectionRequestId);
                MainWindow = requestWindow;
                requestWindow.Show();
                return;
            }
            if (e.Args.Length == 1 && e.Args[0].Equals("--agent-setup", StringComparison.OrdinalIgnoreCase))
            {
                var setupWindow = new AgentSetupWindow();
                MainWindow = setupWindow;
                setupWindow.Show();
                return;
            }
            var mainWindow = new MainWindow(updateStartup?.Task);
            if (updateStartup is not null)
            {
                mainWindow.IsEnabled = false;
                mainWindow.ContentRendered += ConfirmUpdateStartup;
            }
            MainWindow = mainWindow;
            mainWindow.Show();

            async void ConfirmUpdateStartup(object? sender, EventArgs args)
            {
                mainWindow.ContentRendered -= ConfirmUpdateStartup;
                try
                {
                    await PortableUpdateStartup.SignalWindowRenderedAsync(e.Args[4], supervisorId);
                    AppPaths.UpdateStartupPending = false;
                    mainWindow.IsEnabled = true;
                    updateStartup!.SetResult();
                }
                catch { Shutdown(1); }
            }
        }
        catch (Exception ex)
        {
            LogService.Write("Avvio dell'applicazione non riuscito.", ex);
            if (!AppPaths.UpdateStartupPending) ShowStartupError(ex);
            Shutdown(1);
        }
    }

    private static void ShowStartupError(Exception exception)
    {
        MessageBox.Show(
            "Update Center ha riscontrato un errore:\n\n" + exception.Message +
            "\n\nI dettagli sono stati salvati in %LOCALAPPDATA%\\UpdateCenter\\Logs.",
            "Errore Update Center",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
