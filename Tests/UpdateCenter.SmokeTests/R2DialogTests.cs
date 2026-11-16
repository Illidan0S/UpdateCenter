using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class R2DialogTests
{
    internal static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("PASS R2 operation rows: 3 verified results visible, 500 virtualized, ledger/table agreement, scroll and resize, IT/EN light/dark; no installs or UAC.");
    }

    private static void Verify()
    {
        Check(!AppPaths.DataDirectory.Equals(AppPaths.ResolveDefaultRoot(), StringComparison.OrdinalIgnoreCase), "Production data root selected");
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, AutomaticScanInterval = "Off", CheckAppUpdatesAutomatically = false });
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        try
        {
            VerifyPermissionSummary();
            foreach (var locale in new[] { "it", "en" })
            foreach (var theme in new[] { "Chiaro", "Scuro" })
            foreach (var count in new[] { 3, 500 })
            {
                LocalizationService.Initialize(locale); ThemeService.Apply(theme); TypographyService.Apply("Media");
                var names = new[] { "Comfy Desktop", "Core Temp", "Antigravity" };
                var results = Enumerable.Range(0, count).Select(i => new ItemRunResult
                {
                    Id = "Fixture.App" + i, Name = i < 3 ? names[i] : "Fixture app " + i,
                    Kind = "Software", Success = true, Verified = true, ExecutionState = ItemExecutionState.Terminal,
                    Outcome = "Completed", VerificationStatus = UpdateVerificationStatuses.Verified,
                    TerminalDisposition = ItemTerminalDisposition.Succeeded,
                    Message = "Fixture verified result", Diagnostics = "Simulated version evidence"
                }).ToList();
                var window = new UpdateProgressWindow(new MainViewModel());
                VisualQaSafety.Attach(window);
                try
                {
                    window.Width = 720; window.Height = 500;
                    window.ShowCompleted(new UpdateRunStatus { State = "Completed", Results = results });
                    Pump(window);
                    var grid = (DataGrid)window.FindName("OperationItemsGrid");
                    var page = (ScrollViewer)window.FindName("ContentScrollViewer");
                    var inner = Visuals<ScrollViewer>(grid).First();
                    foreach (var size in new[] { new Size(720, 500), new Size(1240, 790), new Size(720, 500) })
                    {
                        window.Width = size.Width; window.Height = size.Height; Pump(window);
                        Check(grid.Items.Count == count, "Completed table excludes ledger results");
                        Check(((TextBlock)window.FindName("SucceededCountText")).Text == count.ToString(), "Table and success count disagree");
                        Check(((TextBlock)window.FindName("FailedCountText")).Text == "0", "Verified fixture counted as failed");
                        Check((string)grid.Items[2].GetType().GetProperty("Name")!.GetValue(grid.Items[2])! == "Antigravity", "Third result identity missing");
                        Check(double.IsFinite(grid.MaxHeight) && grid.ActualHeight <= grid.MaxHeight + 1, "Grid lost bounded viewport");
                        if (count == 3)
                        {
                            grid.ScrollIntoView(grid.Items[0]); Pump(window);
                            page.ScrollToBottom(); Pump(window);
                            Capture(window, $"three-{locale}-{theme}-{size.Width}x{size.Height}");
                            Check(inner.ScrollableHeight == 0,
                                $"Third result hidden: items={grid.Items.Count}, gridHeight={grid.ActualHeight}, scrollable={inner.ScrollableHeight}");
                            var third = (DataGridRow?)grid.ItemContainerGenerator.ContainerFromIndex(2);
                            Check(third is not null, "Third row was not materialized");
                            var bounds = third!.TransformToAncestor(grid).TransformBounds(new Rect(third.RenderSize));
                            Check(bounds.Bottom <= grid.ActualHeight + 1, "Third result clipped inside table");
                        }
                        else
                        {
                            Check(grid.EnableRowVirtualization && ScrollViewer.GetCanContentScroll(grid) &&
                                VirtualizingPanel.GetVirtualizationMode(grid) == VirtualizationMode.Recycling, "Virtualization disabled");
                            grid.ScrollIntoView(grid.Items[250]); Pump(window);
                            Check(Visuals<DataGridRow>(grid).Count() < 50, "500-row fixture materialized too many rows");
                            foreach (var delta in new[] { -120, 120 })
                            {
                                var before = inner.VerticalOffset;
                                Wheel(Visuals<DataGridCell>(grid).First(), delta); Pump(window);
                                Check(delta < 0 ? inner.VerticalOffset > before : inner.VerticalOffset < before, "Native wheel cannot scroll table");
                            }
                            var bar = (ScrollBar)inner.Template.FindName("PART_VerticalScrollBar", inner);
                            ScrollBar.ScrollToBottomCommand.Execute(null, bar); Pump(window);
                            Check(Math.Abs(inner.VerticalOffset - inner.ScrollableHeight) < 1, "Grid scrollbar cannot reach last result");
                        }
                        if (page.ScrollableHeight > 0)
                        {
                            inner.ScrollToBottom(); page.ScrollToTop(); Pump(window);
                            Wheel(Visuals<DataGridCell>(grid).First(), -120); Pump(window);
                            Check(page.VerticalOffset > 0, "Wheel trapped at bottom of completed table");
                            inner.ScrollToTop(); page.ScrollToBottom(); Pump(window);
                            var before = page.VerticalOffset;
                            Wheel(Visuals<DataGridCell>(grid).First(), 120); Pump(window);
                            Check(page.VerticalOffset < before, "Wheel trapped at top of completed table");
                        }
                        var footer = (FrameworkElement)window.FindName("DialogFooter");
                        var footerBounds = footer.TransformToAncestor(window).TransformBounds(new Rect(footer.RenderSize));
                        Check(footerBounds.Bottom <= window.ActualHeight + 1, "Footer is clipped");
                    }
                    Console.WriteLine($"PASS R2 {locale}/{theme}/{count}: ledger, rows, viewport, scrolling, resize");
                }
                finally { window.Close(); }
            }
        }
        finally { app.Shutdown(); }
    }

    private static void VerifyPermissionSummary()
    {
        foreach (var locale in new[] { "it", "en" })
        foreach (var theme in new[] { "Chiaro", "Scuro" })
        {
            LocalizationService.Initialize(locale); ThemeService.Apply(theme); TypographyService.Apply("Media");
            var requirements = new[] { "elevationRequired", "elevationProhibited", "elevatesSelf", "" };
            var items = requirements.Select((requirement, index) =>
            {
                var binding = new WinGetScopeBinding("machine", @"ARP\Machine\X64\Fixture" + index,
                    "S-1-5-21-1-2-3-1001", "v1.29.290", new string('A', 64), "exe", "x64", "Safe");
                var json = JsonSerializer.SerializeToNode(binding)!;
                json["ElevationRequirement"] = requirement;
                return new PlanItem { Id = "Fixture.App" + index, Name = "Fixture app " + index, Kind = "Software",
                    InstalledVersion = "1.0", AvailableVersion = "2.0", Source = "winget",
                    WinGetBinding = json.Deserialize<WinGetScopeBinding>() };
            }).ToList();
            var snapshot = RunnerPlanSnapshot.Create(new UpdatePlan { Items = items }, false);
            var window = new UpdateConfirmationWindow([snapshot], new PreflightResult()) { Width = 720, Height = 500 };
            VisualQaSafety.Attach(window);
            try
            {
                window.Show(); Pump(window);
                var grid = (DataGrid)window.FindName("ItemsGrid");
                var expected = locale == "it"
                    ? new[] { "Richiede autorizzazione amministratore (UAC)", "Nessuna elevazione richiesta", "L'installer può richiedere UAC", "Requisiti amministratore non determinati" }
                    : new[] { "Administrator approval required (UAC)", "No elevation required", "The installer may request UAC", "Administrator requirements undetermined" };
                Check(grid.Items.Count == 4, "Summary changed approved targets");
                for (var index = 0; index < 4; index++)
                {
                    grid.ScrollIntoView(grid.Items[index]); Pump(window);
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(index);
                    var text = string.Join(" ", Visuals<TextBlock>(row).Select(x => x.Text));
                    Check(text.Contains(expected[index], StringComparison.Ordinal), $"Missing approved-plan privilege label: {expected[index]}; rendered={text}");
                    Check(!text.Contains("S-1-5-") && !text.Contains("ARP\\"), "Raw binding shown as permission explanation");
                }
                Check(!Visuals<CheckBox>(window).Any(), "New generic checkbox in authoritative summary");
                Check(((Button)window.FindName("ConfirmButton")).IsEnabled, "Supported native UAC path cannot be approved");
                Capture(window, $"permissions-{locale}-{theme}");
                Console.WriteLine($"PASS R2 permissions {locale}/{theme}: frozen UAC/no-elevation/self-elevation/unknown labels");
            }
            finally { window.Close(); }
        }
    }

    private static void Wheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        target.RaiseEvent(args); if (args.Handled) return;
        args.RoutedEvent = Mouse.MouseWheelEvent; target.RaiseEvent(args);
    }
    private static void Pump(Window window) { window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T typed) yield return typed;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static void Capture(Window window, string name)
    {
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "UpdateCenter.sln"))) source = source.Parent;
        var output = Path.Combine(source!.FullName, "_output", "r2-20261110", "dialog-screenshots"); Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
