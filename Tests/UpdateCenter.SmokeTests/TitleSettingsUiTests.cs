using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shapes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class TitleSettingsUiTests
{
    internal static void Reopen(string directory)
    {
        var full = System.IO.Path.GetFullPath(directory);
        var expectedParent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UpdateCenter.SmokeTests"));
        var info = new DirectoryInfo(full);
        if (!string.Equals(info.Parent?.FullName, expectedParent, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(info.Name, "N", out _))
            throw new InvalidOperationException("Reopen QA requires an isolated GUID root.");
        AppPaths.InitializeRoot(full);
        var settings = new MainViewModel().Settings;
        Check(settings.IncludeUnknownVersions && settings.LanguageMode == "en" && settings.ThemeMode == "Scuro" && settings.FontSizeMode == "Grande" && !settings.ScanAtStartup && settings.AutomaticScanInterval == "Off", "New process lost preferences");
        Console.WriteLine("PASS preferences reloaded in a separate process; no application startup work");
    }
    internal static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Title/settings QA", failure);
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static T Find<T>(MainWindow w, string name) => (T)w.FindName(name);
    static void Verify()
    {
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, CheckAppUpdatesAutomatically = false, NotifyWhenUpdatesAreAvailable = false });
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(System.IO.Path.Combine(source.FullName, "App.xaml"))) source = source.Parent;
        var root = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(source!.FullName, "App.xaml")).Root!;
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new System.Xml.Linq.XElement(ns + "ResourceDictionary", root.Attributes().Where(x => x.IsNamespaceDeclaration), root.Element(ns + "Application.Resources")!.Nodes()).ToString());
        LocalizationService.Initialize("it"); ThemeService.Apply("Chiaro"); TypographyService.Apply("Media");
        var window = new MainWindow();
        VisualQaSafety.Sanitize(window, d => d.Method.Name is "OrdinaryPreference_Click" or "RetrySettingsSave_Click" or "ThemeChoice_Click" or "FontSizeChoice_Click" or "LanguageChoice_Click" or "AutomaticScanInterval_SelectionChanged" or "Minimize_Click" or "Maximize_Click" or "Close_Click");
        VisualQaSafety.StopTimers(window);
        var errors = new List<string>();
        try
        {
            window.Show(); window.UpdateLayout();
            var vm = (MainViewModel)window.DataContext;
            var box = Find<CheckBox>(window, "UnknownVersionsChoice");
            box.SetCurrentValue(ToggleButton.IsCheckedProperty, true); box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (!JsonStorage.LoadSettings().IncludeUnknownVersions) errors.Add("RED checkbox change is not automatically persisted");
            var maximize = Find<Button>(window, "MaximizeWindowButton");
            var bridge = Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("UpdateCenter.Services.NativeCaptionBridge")!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [window, (Func<FrameworkElement>)(() => maximize)], null)!;
            var at = maximize.PointToScreen(new Point(maximize.ActualWidth / 2, maximize.ActualHeight / 2));
            var packed = new IntPtr(((int)at.Y << 16) | ((int)at.X & 65535));
            object[] args = [new WindowInteropHelper(window).Handle, 0xA1, new IntPtr(9), packed, false];
            bridge.GetType().GetMethod("Hook", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, args);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !(bool)args[4]) errors.Add("RED native maximize down falls through to default caption tracking/painting");
            // Undo the direct hook probe's capture before exercising the registered HWND hook.
            SendMessage(new WindowInteropHelper(window).Handle, 0x1F, IntPtr.Zero, IntPtr.Zero);
            foreach (var error in errors) Console.WriteLine(error);
            Check(errors.Count == 0, string.Join("; ", errors));
            VerifyCaption(window, source.FullName);
            VerifyPreferences(window, source.FullName);
            var closed = false; window.Closed += (_, _) => closed = true;
            Find<Button>(window, "CloseWindowButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            Check(closed, "Close command lost");
            Console.WriteLine("PASS title/settings regression boundaries");
        }
        finally { if (window.IsLoaded) window.Close(); app.Shutdown(); }
    }
    static void Invoke(MainWindow w, string method, params object[] args) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    static void Pump(int milliseconds = 180)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    static IntPtr Pack(Point p) => new(((int)p.Y << 16) | ((int)p.X & 65535));
    static void VerifyCaption(MainWindow window, string root)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var max = Find<Button>(window, "MaximizeWindowButton");
        foreach (var theme in new[] { "Chiaro", "Scuro" })
        {
            ThemeService.Apply(theme); window.Width = 720; window.Height = 500; window.UpdateLayout();
            var at = max.PointToScreen(new Point(max.ActualWidth / 2, max.ActualHeight / 2));
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                Check(SendMessage(hwnd, 0x84, IntPtr.Zero, Pack(at)).ToInt32() == 9, "Maximize hit area differs from visible button");
                Check(SendMessage(hwnd, 0x84, IntPtr.Zero, Pack(window.PointToScreen(new Point(280, 28)))).ToInt32() == 2, "Native drag caption lost");
                Check(SendMessage(hwnd, 0x84, IntPtr.Zero, Pack(window.PointToScreen(new Point(1, 200)))).ToInt32() == 10, "Native left resize border lost");
                SendMessage(hwnd, 0xA0, new IntPtr(9), Pack(at));
                SendMessage(hwnd, 0xA1, new IntPtr(9), Pack(at));
                var client = new NativePoint { X = (int)at.X, Y = (int)at.Y }; ScreenToClient(hwnd, ref client);
                SendMessage(hwnd, 0x202, IntPtr.Zero, Pack(new Point(client.X, client.Y))); Pump();
                Check(window.WindowState == WindowState.Maximized, "NC mouse click did not maximize exactly once");
                Check(ReferenceEquals(max.Content, window.FindResource("WindowRestoreIcon")), "Restore icon out of sync");
                var visible = window.PointToScreen(new Point(8, 8));
                var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() }; GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor);
                Check(visible.Y >= monitor.Work.Top - 2, "Maximized client covered work-area top");
                var bottom = window.PointToScreen(new Point(window.ActualWidth - 8, window.ActualHeight - 8));
                Check(bottom.Y <= monitor.Work.Bottom + 2 && bottom.X <= monitor.Work.Right + 2, "Maximized content covered taskbar/work-area edge");
                at = max.PointToScreen(new Point(max.ActualWidth / 2, max.ActualHeight / 2));
                SendMessage(hwnd, 0xA1, new IntPtr(9), Pack(at)); client = new NativePoint { X = (int)at.X, Y = (int)at.Y }; ScreenToClient(hwnd, ref client);
                SendMessage(hwnd, 0x202, IntPtr.Zero, Pack(new Point(client.X, client.Y))); Pump();
                Check(window.WindowState == WindowState.Normal, "NC mouse click did not restore");
                at = max.PointToScreen(new Point(max.ActualWidth / 2, max.ActualHeight / 2));
                SendMessage(hwnd, 0xA1, new IntPtr(9), Pack(at));
                SendMessage(hwnd, 0x202, IntPtr.Zero, Pack(new Point(100, 140))); Pump();
                Check(window.WindowState == WindowState.Normal, "Release outside maximize invoked command");
                SendMessage(hwnd, 0xA1, new IntPtr(9), Pack(at)); SendMessage(hwnd, 0x1F, IntPtr.Zero, IntPtr.Zero);
                SendMessage(hwnd, 0xA2, new IntPtr(9), Pack(at)); Pump();
                Check(window.WindowState == WindowState.Normal, "Cancelled press left stale state");
            }
            max.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            Check(window.WindowState == WindowState.Maximized, "Keyboard/UIA click maximize lost");
            max.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            Check(window.WindowState == WindowState.Normal && ReferenceEquals(max.Content, window.FindResource("WindowMaximizeIcon")), "Keyboard/UIA restore lost");
            Find<Button>(window, "MinimizeWindowButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            Check(window.WindowState == WindowState.Minimized, "Minimize command lost");
            SystemCommands.RestoreWindow(window); Pump();
            Check(window.WindowState == WindowState.Normal && !window.AllowsTransparency, "Native restore/layering regressed");
            Check(Visuals<System.Windows.Shapes.Path>(max).Count() == 1, "More than one custom caption glyph");
            Capture(window, root, "title-" + theme);
            window.Activate();
            var hover = max.PointToScreen(new Point(max.ActualWidth / 2, max.ActualHeight / 2));
            SendMessage(hwnd, 0xA0, new IntPtr(9), Pack(hover)); Pump();
            GetWindowRect(hwnd, out var rectangle);
            using var image = new System.Drawing.Bitmap(rectangle.Right - rectangle.Left, 64);
            using (var graphics = System.Drawing.Graphics.FromImage(image)) graphics.CopyFromScreen(rectangle.Left, rectangle.Top, 0, 0, image.Size);
            image.Save(System.IO.Path.Combine(root, "_output", "title-settings-20261109", "native-title-" + theme + ".png"));
            if (theme == "Scuro" && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && GetCursorPos(out var cursor))
            {
                try
                {
                    var moved = SetCursorPos((int)hover.X, (int)hover.Y); Pump(1200);
                    GetCursorPos(out var actual);
                    Console.WriteLine($"Snap hover probe: cursorMoved={moved}, target={hover.X:0},{hover.Y:0}, actual={actual.X},{actual.Y}");
                    GetWindowRect(hwnd, out rectangle);
                    using var snap = new System.Drawing.Bitmap(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
                    using (var graphics = System.Drawing.Graphics.FromImage(snap)) graphics.CopyFromScreen(rectangle.Left, rectangle.Top, 0, 0, snap.Size);
                    snap.Save(System.IO.Path.Combine(root, "_output", "title-settings-20261109", "native-snap-hover.png"));
                }
                finally { SetCursorPos(cursor.X, cursor.Y); Pump(400); }
            }
        }
        Console.WriteLine("PASS native NC press/release, drag-out, cancel, maximize/restore icon, keyboard command, minimize/restore, caption drag region, light/dark 720x500");
    }
    static void VerifyPreferences(MainWindow window, string root)
    {
        var vm = (MainViewModel)window.DataContext;
        Invoke(window, "ShowPage", window.FindName("SettingsPage"), "Impostazioni");
        Invoke(window, "InitializeNotificationIcon");
        var tray = (System.Windows.Forms.NotifyIcon)typeof(MainWindow).GetField("_notifyIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        (string Name, Func<AppSettings, bool> Read)[] checks = [("RestorePointChoice", s => s.CreateRestorePoint), ("SilentInstallChoice", s => s.SilentSoftwareInstall), ("UnknownVersionsChoice", s => s.IncludeUnknownVersions), ("AppUpdateChoice", s => s.CheckAppUpdatesAutomatically), ("StartupScanChoice", s => s.ScanAtStartup), ("NotificationChoice", s => s.NotifyWhenUpdatesAreAvailable)];
        foreach (var (name, read) in checks)
        {
            var control = Find<CheckBox>(window, name);
            foreach (var state in new[] { false, true, false })
            {
                control.SetCurrentValue(ToggleButton.IsCheckedProperty, state); control.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(read(JsonStorage.LoadSettings()) == state, "Preference not persisted: " + name);
                if (name == "NotificationChoice") Check(tray.Visible == state, "Tray preference not applied immediately");
            }
        }
        foreach (var interval in new[] { "Daily", "Weekly", "Off" })
        {
            Find<ComboBox>(window, "ScanFrequencyChoice").SetCurrentValue(Selector.SelectedValueProperty, interval);
            Check(JsonStorage.LoadSettings().AutomaticScanInterval == interval, "Frequency not persisted");
        }
        // Failure with a real exclusive lock must keep the last on-disk snapshot.
        var original = File.ReadAllBytes(AppPaths.SettingsFile);
        using (var locked = new FileStream(AppPaths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var control = Find<CheckBox>(window, "UnknownVersionsChoice");
            control.SetCurrentValue(ToggleButton.IsCheckedProperty, true); control.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(Find<StackPanel>(window, "SettingsSaveErrorPanel").Visibility == Visibility.Visible, "Save failure not presented");
            Find<RadioButton>(window, "EnglishLanguageChoice").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(Find<TextBlock>(window, "SettingsSaveErrorText").Text.StartsWith("Preferences could not be saved", StringComparison.Ordinal), "Save failure is not localized");
        }
        Check(original.SequenceEqual(File.ReadAllBytes(AppPaths.SettingsFile)), "Failed save damaged existing preferences");
        Invoke(window, "RetrySettingsSave_Click", window, new RoutedEventArgs());
        Check(JsonStorage.LoadSettings().IncludeUnknownVersions && Find<StackPanel>(window, "SettingsSaveErrorPanel").Visibility == Visibility.Collapsed, "Retry did not recover");
        foreach (var language in new[] { "it", "en" })
        foreach (var theme in new[] { "Chiaro", "Scuro" })
        {
            Find<RadioButton>(window, language == "it" ? "ItalianLanguageChoice" : "EnglishLanguageChoice").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Find<RadioButton>(window, theme == "Chiaro" ? "LightThemeChoice" : "DarkThemeChoice").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Find<RadioButton>(window, "LargeFontChoice").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(JsonStorage.LoadSettings().LanguageMode == language && JsonStorage.LoadSettings().ThemeMode == theme && JsonStorage.LoadSettings().FontSizeMode == "Grande", "Appearance/language not persisted");
            window.Width = 720; window.Height = 500; window.UpdateLayout();
            Check(window.FindName("SaveSettingsButton") is null && window.FindName("SettingsSaveBar") is null, "Global save bar remains");
            Check(Find<ScrollViewer>(window, "SettingsScrollViewer").ViewportHeight > 100, "Settings viewport clipped");
            Capture(window, root, "settings-" + language + "-" + theme);
        }
        var reopened = new MainViewModel().Settings;
        Check(reopened.IncludeUnknownVersions && reopened.LanguageMode == "en" && reopened.ThemeMode == "Scuro" && reopened.FontSizeMode == "Grande", "Reloaded application lost preferences");
        using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, ArgumentList = { "--title-settings-reopen", System.IO.Path.GetDirectoryName(AppPaths.SettingsFile)! } })!;
        Check(child.WaitForExit(15000) && child.ExitCode == 0, "Preferences did not survive process restart");
        Console.WriteLine(child.StandardOutput.ReadToEnd());
        Console.WriteLine("PASS six ordinary autosaves, appearance/language/frequency, tray immediate update, actual write failure/retry, IT/EN and settings reload; no explicit operations invoked");
    }
    static void Capture(MainWindow w, string root, string name)
    {
        w.UpdateLayout(); var bmp = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(w);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(System.IO.Path.Combine(root, "_output", "title-settings-20261109", name + ".png")); encoder.Save(file);
    }
    static IEnumerable<T> Visuals<T>(DependencyObject d) where T : DependencyObject
    {
        if (d is T result) yield return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(d, i))) yield return child;
    }
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

}
