using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UpdateCenter;
using UpdateCenter.Controls;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class UpdateExperienceUiTests
{
    internal static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Update experience UI", failure);
        Console.WriteLine("PASS real WPF notes IT/EN/light/dark/720x500, 500-row virtualization, native HWND caption hit tests; isolated synthetic data.");
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
    private static void Verify()
    {
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, AutomaticScanInterval = "Off", CheckAppUpdatesAutomatically = false });
        var app = new Application();
        var rootPath = new DirectoryInfo(AppContext.BaseDirectory);
        while (rootPath is not null && !File.Exists(Path.Combine(rootPath.FullName, "App.xaml"))) rootPath = rootPath.Parent;
        var root = System.Xml.Linq.XDocument.Load(Path.Combine(rootPath!.FullName, "App.xaml")).Root!;
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new System.Xml.Linq.XElement(ns + "ResourceDictionary", root.Attributes().Where(x => x.IsNamespaceDeclaration), root.Element(ns + "Application.Resources")!.Nodes()).ToString());
        LocalizationService.Initialize("it"); ThemeService.Apply("Chiaro"); TypographyService.Apply("Grande");
        var window = new MainWindow(); VisualQaSafety.Attach(window);
        try
        {
            var vm = (MainViewModel)window.DataContext;
            var items = Enumerable.Range(0, 500).Select(i => new UpdateItem { Id = "QA." + i, Name = "Programma dimostrativo " + i, Kind = UpdateKind.Software, Source = "QA", InstalledVersion = "1.0", AvailableVersion = "2.0", IsSelected = false, ProviderReleaseNotes = new("Novità verificate / Verified release notes\n" + string.Join('\n', Enumerable.Repeat("Testo lungo copiabile · selectable plain text <script>not executed</script>", 60)), "https://example.com/releases/2") }).ToList();
            typeof(MainViewModel).GetMethod("AddUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [items]);
            Invoke(window, "ShowPage", window.FindName("UpdatesPage"), "Aggiornamenti");
            window.Title = "UpdateCenter · UX QA · synthetic data only";
            window.Show(); window.UpdateLayout();
            var hwnd = new WindowInteropHelper(window).Handle;
            Check(!window.AllowsTransparency && (GetWindowLong(hwnd, -20) & 0x80000) == 0, "Layered HWND remains");
            var maximize = (Button)window.FindName("MaximizeWindowButton");
            var at = maximize.PointToScreen(new Point(maximize.ActualWidth / 2, maximize.ActualHeight / 2));
            var packed = new IntPtr(((int)at.Y << 16) | ((int)at.X & 65535));
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) Check(SendMessage(hwnd, 0x84, IntPtr.Zero, packed).ToInt32() == 9, "Snap maximize hit test not reached");
            var grid = (DataGrid)window.FindName("UpdatesGrid");
            var panel = (ReleaseNotesPanel)window.FindName("UpdateReleaseNotes");
            var late = new TaskCompletionSource<string?>();
            panel.Reader = new ReleaseNotesService((uri, _) => uri.AbsolutePath.Contains("/First/") ? late.Task : Task.FromResult<string?>("PackageIdentifier: Second.App\nPackageVersion: 2\nManifestType: singleton\nReleaseNotes: Second content"));
            var first = new UpdateItem { Id = "First.App", Name = "First", Kind = UpdateKind.Software, Source = "winget", AvailableVersion = "2" };
            var second = new UpdateItem { Id = "Second.App", Name = "Second", Kind = UpdateKind.Software, Source = "winget", AvailableVersion = "2" };
            var pending = panel.OpenAsync(first, maximize);
            panel.OpenAsync(second, maximize).GetAwaiter().GetResult();
            late.SetResult("PackageIdentifier: First.App\nPackageVersion: 2\nManifestType: singleton\nReleaseNotes: Stale content");
            Pump();
            Check(Visuals<TextBox>(panel).Single().Text.Contains("Second content"), "Late request overwrote current item");
            panel.Visibility = Visibility.Collapsed;
            foreach (var language in new[] { "it", "en" })
            foreach (var theme in new[] { "Chiaro", "Scuro" })
            {
                LocalizationService.Initialize(language); ThemeService.Apply(theme); Invoke(window, "UpdateOperationsLabels");
                window.Width = 720; window.Height = 500; window.UpdateLayout();
                grid.ScrollIntoView(items[499]); window.UpdateLayout();
                Check(Visuals<DataGridRow>(grid).Count() < 80 && grid.EnableRowVirtualization, "500 rows realized");
                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(items[499]);
                var anchor = Visuals<Button>(row).First(x => (string?)x.Content == (language == "it" ? "Novità" : "What’s new"));
                panel.OpenAsync(items[499], anchor).GetAwaiter().GetResult(); window.UpdateLayout();
                Check(panel.ActualWidth <= grid.ActualWidth && panel.ActualHeight > 250, "Notes overflow minimum layout or leave too little reading space");
                var text = Visuals<TextBox>(panel).Single();
                Check(text.Text.Contains("2.0") && text.Text.Contains("<script>") && text.IsReadOnly && text.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "Notes not plain/copyable/scrollable");
                Check(!items[499].IsSelected, "Opening notes changed selection");
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(rootPath.FullName, "_output", "ux-20261109", $"notes-{language}-{theme}.png"))) encoder.Save(file);
                panel.Dismiss(); panel.OpenAsync(items[0], anchor).GetAwaiter().GetResult(); window.UpdateLayout();
                Pump();
                Check(panel.Visibility == Visibility.Visible, "Old close animation hid reopened notes");
                Check(text.Text.Contains(items[0].Name), "Reopening notes kept old item");
                panel.Visibility = Visibility.Collapsed;
            }
            var bar = new ProgressBar { Style = (Style)app.FindResource("UpdateCenterProgressBar"), IsIndeterminate = true, Tag = true, Width = 160, Height = 8 };
            var host = new Window { Content = bar, Width = 200, Height = 80, ShowInTaskbar = false };
            host.Show(); host.UpdateLayout(); Pump();
            var indicator = (System.Windows.Shapes.Rectangle)bar.Template.FindName("IndeterminateIndicator", bar);
            Check(indicator.HasAnimatedProperties, "Enabled indeterminate activity pulse missing");
            bar.Tag = false; Pump();
            Check(!indicator.HasAnimatedProperties, "Reduced-motion pulse still running");
            host.Close();
        }
        finally { window.Close(); app.Shutdown(); }
    }
    static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
