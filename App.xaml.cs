using System.Windows;
using UpdateCenter.Services;

namespace UpdateCenter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 4 &&
            e.Args[0].Equals("--apply-update", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(e.Args[2], out var parentProcessId))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = AppUpdateService.ApplyUpdate(e.Args[1], parentProcessId, e.Args[3]);
            Shutdown(exitCode);
            return;
        }

        if (e.Args.Length == 2 && e.Args[0].Equals("--elevated-update", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = ElevatedUpdateRunner.Run(e.Args[1]);
            Shutdown(exitCode);
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Write("Errore non gestito nell'interfaccia.", args.Exception);
            ShowStartupError(args.Exception);
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
            ThemeService.Apply(settings.ThemeMode);
            TypographyService.Apply(settings.FontSizeMode);
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            if (e.Args.Length == 4 && e.Args[0].Equals("--update-complete", StringComparison.OrdinalIgnoreCase))
                AppUpdateService.ScheduleSuccessfulUpdateCleanup(e.Args[1], e.Args[2], e.Args[3]);
        }
        catch (Exception ex)
        {
            LogService.Write("Avvio dell'applicazione non riuscito.", ex);
            ShowStartupError(ex);
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
