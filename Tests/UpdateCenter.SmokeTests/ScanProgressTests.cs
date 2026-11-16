using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class ScanProgressTests
{
    internal static Task RunAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await ExerciseAsync(); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static ScanServices Sources() => new(
        (_, _) => Task.FromResult<IReadOnlyList<UpdateItem>>([new() { Id = "Fixture.Tool", Name = "Fixture", Kind = UpdateKind.Software }]),
        _ => Task.FromResult(new QuickHardwareSnapshot(new HardwareScanResult(), new StorageHealthScanResult())),
        _ => Task.FromResult(new HardwareScanResult()),
        (_, _) => Task.FromResult(new DriverScanResult()),
        (_, _) => Task.FromResult(new OfficialDriverCatalogScanResult()),
        _ => Task.FromResult<IReadOnlyList<GameDependencyItem>>([]),
        _ => Task.FromResult(new StorageHealthScanResult()));

    private static bool BusyIndicator(MainViewModel vm) =>
        TypeDescriptor.GetProperties(vm)["IsProgressIndeterminate"]?.GetValue(vm) is true;
    private static string ProgressLabel(MainViewModel vm) =>
        TypeDescriptor.GetProperties(vm)["ProgressText"]?.GetValue(vm) as string ?? "missing";
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task ExerciseAsync()
    {
        Console.WriteLine("Isolated scan progress data: " + AppPaths.DataDirectory);
        JsonStorage.SaveHistory([]);
        var failures = new List<string>();
        async Task Case(string name, Func<Task> run)
        {
            JsonStorage.SaveSettings(new AppSettings());
            LocalizationService.Initialize("en");
            try { await run(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }
        await Case("unknown WUA duration, dispatcher responsiveness and cancellation", LongProviderAsync);
        await Case("phase labels and completion after publication/persistence", CompletionAsync);
        await Case("partial source failure retains warnings and results", PartialAsync);
        await Case("finalization failure never reports completion", PersistenceFailureAsync);
        await Case("cancellation at finalization boundary", CancelFinalizationAsync);
        await Case("WinGet scan continuations do not capture the dispatcher", WinGetContinuationAsync);
        await Case("WPF indeterminate animation, hidden/determinate stop", AnimationAsync);
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static async Task LongProviderAsync()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DriverScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new MainViewModel(Sources() with { Drivers = (_, _) => { entered.SetResult(); return release.Task; } });
        var scan = vm.ScanAsync();
        try
        {
            await entered.Task;
            var ea = new UpdateItem { Id = "ElectronicArts.EADesktop", Name = "EA app", Kind = UpdateKind.Software,
                RequiresRiskConfirmation = true, CanInstall = true, IsSelected = false };
            vm.Updates.Add(ea);
            var late = new UpdateItem { Id = "Fixture.Late", Name = "Late result", Kind = UpdateKind.Software, IsSelected = false };
            vm.SelectAllWithConfirmation(_ => { vm.Updates.Add(late); return true; });
            Check(vm.IsScanRunning && ea.IsSelected && ea.RequiresRiskConfirmation && !late.IsSelected && vm.SelectedCount == 2,
                "Bulk selection during a pending scan lost confirmation or selected later results.");
            Check(vm.Progress == 0 && BusyIndicator(vm), "Unknown provider duration is shown as a measured percentage.");
            Check(ProgressLabel(vm) == "In progress" && vm.CurrentItemText == "Searching for driver updates…", "Missing phase/activity label.");
            LocalizationService.Initialize("it");
            vm.NotifyLanguageChanged();
            Check(ProgressLabel(vm) == "In corso", "Activity label does not switch to Italian.");
            Check(vm.CurrentItemText.StartsWith("Ricerca"), "Phase label does not switch to Italian.");
            // Keep a real STA dispatcher alive across a deliberately slow provider;
            // no real WUA/WinGet or approval callbacks are invoked.
            await Task.Delay(1200);
            var inputHandled = false;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => inputHandled = true, DispatcherPriority.Input);
            Check(inputHandled && !scan.IsCompleted && BusyIndicator(vm), "UI stopped servicing input while awaiting provider.");
            vm.CancelScan();
            Check(vm.IsBusy && BusyIndicator(vm) && vm.StatusText == "Annullamento in corso",
                "A non-interruptible provider appears to have stopped before returning.");
        }
        finally { release.TrySetResult(new DriverScanResult()); await scan; }
        Check(!vm.IsBusy && !vm.IsScanRunning && !BusyIndicator(vm) && vm.Progress < 100 && vm.Settings.LastScanUtc is null,
            "Cancellation completed the scan or left activity running.");
    }

    private static async Task CompletionAsync()
    {
        var vm = new MainViewModel(Sources());
        var phases = new List<string>();
        var percentages = new List<double>();
        var publishedAt100 = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CurrentItemText)) phases.Add(vm.CurrentItemText);
            if (e.PropertyName != nameof(vm.Progress)) return;
            percentages.Add(vm.Progress);
            if (vm.Progress == 100)
                publishedAt100 = !vm.IsScanRunning && !vm.IsBusy && !BusyIndicator(vm) &&
                    vm.AvailableCount == 1 && vm.Settings.LastScanUtc is not null &&
                    JsonStorage.LoadSettings().LastScanUtc == vm.Settings.LastScanUtc;
        };
        await vm.ScanAsync();
        Check(publishedAt100 && percentages.All(p => p is 0 or 100), "Premature completion or synthetic phase weights.");
        foreach (var phase in new[] { "software updates", "driver updates", "Aggregating", "Finalizing" })
            Check(phases.Any(p => p.Contains(phase, StringComparison.Ordinal)), "Missing phase: " + phase);
        Check(phases.All(p => !p.Contains("WinGet", StringComparison.OrdinalIgnoreCase) &&
            !p.Contains("Windows Update", StringComparison.OrdinalIgnoreCase)), "Provider names leaked into scan activity/completion text.");
        Check(ProgressLabel(vm) == "100%", "Completed scan lacks terminal percentage.");
        // The next scan must reset the old completion immediately.
        var again = vm.ScanAsync();
        Check(vm.Progress == 0 && BusyIndicator(vm), "A new scan retained the preceding 100%.");
        await again;
    }

    private static async Task PartialAsync()
    {
        var vm = new MainViewModel(Sources() with { Drivers = (_, _) => throw new IOException("fixture WUA unavailable") });
        await vm.ScanAsync();
        Check(vm.Progress == 100 && !BusyIndicator(vm) && vm.AvailableCount == 1 &&
            vm.StatusText.Contains("warnings", StringComparison.OrdinalIgnoreCase) &&
            vm.CurrentItemText.Contains("fixture WUA unavailable"), "Partial results appear to be a fully successful scan.");
    }

    private static async Task PersistenceFailureAsync()
    {
        var previous = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        JsonStorage.SaveSettings(new AppSettings { LastScanUtc = previous });
        var vm = new MainViewModel(Sources());
        // Real Windows sharing violation, confined to the harness GUID data root.
        using var locked = new FileStream(AppPaths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await vm.ScanAsync();
        Check(vm.Progress < 100 && !vm.IsBusy && !BusyIndicator(vm) && vm.Settings.LastScanUtc == previous &&
            vm.StatusText == "Scan failed", "Failed persistence reported success or changed the last completed scan.");
    }

    private static async Task CancelFinalizationAsync()
    {
        var vm = new MainViewModel(Sources());
        var sawFinalization = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CurrentItemText) && vm.CurrentItemText.StartsWith("Finalizing"))
            {
                sawFinalization = true;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(vm.CancelScan));
            }
        };
        await vm.ScanAsync();
        Check(sawFinalization && vm.Progress < 100 && vm.Settings.LastScanUtc is null && vm.StatusText == "Scan cancelled",
            "Finalization swallowed pending cancellation or did not yield to input.");
    }

    private static async Task WinGetContinuationAsync()
    {
        var version = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedUi = true;
        var commands = new List<string>();
        var service = new WinGetService((args, _, _) =>
        {
            commands.Add(args[0]);
            if (args[0] == "--version") return version.Task;
            capturedUi = SynchronizationContext.Current is DispatcherSynchronizationContext;
            return Task.FromResult(new ProcessResult(0, "No available upgrade found.", ""));
        });
        var scan = service.ScanAsync(false, CancellationToken.None);
        version.SetResult(new ProcessResult(0, "v1.12.460", ""));
        var results = await scan;
        Check(!capturedUi && results.Count == 0 && commands.SequenceEqual(["--version", "upgrade"]),
            "Software scan captured the UI context or changed the read-only command path.");
    }

    private static async Task AnimationAsync()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "App.xaml"))) source = source.Parent;
        var root = System.Xml.Linq.XDocument.Load(Path.Combine(source!.FullName, "App.xaml")).Root!;
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
            new System.Xml.Linq.XElement(ns + "ResourceDictionary", root.Attributes().Where(a => a.IsNamespaceDeclaration),
                root.Element(ns + "Application.Resources")!.Nodes()).ToString());
        // Check the actual Home resource scope: Settings imports Fluent locally,
        // which can mask the application-wide implicit ProgressBar style.
        var home = new MainWindow();
        VisualQaSafety.Attach(home);
        try
        {
            var homeBar = (ProgressBar)home.FindName("HomeProgress");
            homeBar.ApplyTemplate();
            Check(homeBar.Template.FindName("IndeterminateIndicator", homeBar) is not null,
                "Home's local resources replace the animated scan template.");
            var activity = (FrameworkElement)homeBar.Template.FindName("IndeterminateIndicator", homeBar);
            Check(double.IsNaN(activity.Width), "Scan bar was constrained to a small fixed-width segment.");
            home.ShowActivated = false;
            home.ShowInTaskbar = false;
            home.WindowStartupLocation = WindowStartupLocation.Manual;
            home.Left = -20000; home.Top = -20000;
            home.Width = 720; home.Height = 500;
            var vm = (MainViewModel)home.DataContext;
            DialogUiFixture.Set(vm, "IsBusy", true);
            DialogUiFixture.Set(vm, "IsScanRunning", true);
            DialogUiFixture.Set(vm, "IsProgressIndeterminate", true);
            home.Show();
            foreach (var language in new[] { "it", "en" })
            foreach (var theme in new[] { "Chiaro", "Scuro" })
            foreach (var page in new[] { "HomePage", "UpdatesPage" })
            {
                LocalizationService.Initialize(language); ThemeService.Apply(theme); vm.NotifyLanguageChanged();
                LocalizationService.ApplyTo(home);
                DialogUiFixture.Set(vm, "StatusText", LocalizationService.Text("Scansione in corso", "Scan in progress"));
                DialogUiFixture.Set(vm, "CurrentItemText", LocalizationService.Text("Ricerca aggiornamenti software…", "Searching for software updates…"));
                DialogUiFixture.Call(home, "ShowPage", home.FindName(page), page == "HomePage" ? "Home" : "Aggiornamenti");
                DialogUiFixture.Call(home, "ApplyResponsiveLayout");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                home.UpdateLayout();
                var scanBar = (ProgressBar)home.FindName(page == "HomePage" ? "HomeProgress" : "UpdatesScanProgress");
                scanBar.ApplyTemplate(); home.UpdateLayout();
                var scanTrack = (FrameworkElement)scanBar.Template.FindName("PART_Track", scanBar);
                var scanActivity = (FrameworkElement)scanBar.Template.FindName("IndeterminateIndicator", scanBar);
                Check(scanBar.IsVisible && scanBar.IsIndeterminate && scanTrack.ActualWidth > 200 &&
                    Math.Abs(scanActivity.ActualWidth - scanTrack.ActualWidth) < 1,
                    "Home/Updates scan activity does not span its full track at 720x500.");
                var folder = Path.Combine(AppContext.BaseDirectory, "scan-qa");
                Directory.CreateDirectory(folder);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(720, 500, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(home);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, $"{page}-{language}-{theme}.png")); png.Save(file);
            }
        }
        finally { home.Close(); }
        var bar = new ProgressBar { Width = 380, Height = 8, IsIndeterminate = true, Style = (Style)app.FindResource("ScanProgressBar") };
        var window = new Window { Title = "UpdateCenter — progress fixture", Width = 440, Height = 100, Content = bar, ShowActivated = false };
        try
        {
            window.Show();
            bar.ApplyTemplate();
            await Task.Delay(150);
            var activity = bar.Template.FindName("IndeterminateIndicator", bar) as System.Windows.Shapes.Rectangle;
            Check(activity is not null && activity.IsVisible && activity.ActualWidth > 0, "No visible indeterminate indicator in the real application template.");
            var opacity = activity!.Opacity;
            await Task.Delay(350);
            Check(activity.HasAnimatedProperties && Math.Abs(opacity - activity.Opacity) > .01, "Indeterminate indicator does not animate.");
            bar.Visibility = Visibility.Collapsed;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!activity.HasAnimatedProperties, "Hidden bar retains its animation clock.");
            bar.Visibility = Visibility.Visible;
            await Task.Delay(100);
            bar.IsIndeterminate = false;
            bar.Value = 37;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!activity.HasAnimatedProperties && !activity.IsVisible, "Determinate state retains an activity overlay/clock.");
            var track = (FrameworkElement)bar.Template.FindName("PART_Track", bar);
            var indicator = (FrameworkElement)bar.Template.FindName("PART_Indicator", bar);
            Check(Math.Abs(indicator.ActualWidth / track.ActualWidth - .37) < .02, "Determinate progress geometry changed.");
        }
        finally { window.Close(); app.Shutdown(); }
    }
}
