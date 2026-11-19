using UpdateCenter.Services;
using System.Security.Principal;
using System.Windows;

namespace UpdateCenter.ConsentTestHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // First initialization, before any application I/O. Never use the production profile.
        AppPaths.InitializeRoot(Path.GetFullPath(Path.Combine(Path.GetTempPath(),
            "UpdateCenter.ConsentTestHost", Guid.NewGuid().ToString("N"))));
        try
        {
            if (args is ["--self-test"]) return HostSafetyTests.Run();
            if (args.Length != 0) throw new ArgumentException("Only no arguments or --self-test are supported. No external plans.");
            SafetyGuard.Verify();
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Avvia normalmente, senza amministratore. Run without elevation.");
            return new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(new ManualHostWindow());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            if (args.Length == 0) MessageBox.Show(ex.Message, "Consent TEST host — avvio bloccato / startup blocked");
            return 1;
        }
    }
}
