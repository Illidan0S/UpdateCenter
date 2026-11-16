using System.Reflection;
using System.Runtime.ExceptionServices;
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

internal static class R1DialogScrollTests
{
    internal static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("PASS R1 dialog scrolling: wheel over rows/headers/background in both directions, boundary chaining, 500-row virtualization, native keyboard/scrollbars, fixed footer, resize, IT/EN light/dark; no effects.");
    }

    private static void Verify()
    {
        Check(!AppPaths.DataDirectory.Equals(AppPaths.ResolveDefaultRoot(), StringComparison.OrdinalIgnoreCase), "Production data root selected");
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, AutomaticScanInterval = "Off", CheckAppUpdatesAutomatically = false });
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        try
        {
            foreach (var locale in new[] { "it", "en" })
            foreach (var theme in new[] { "Chiaro", "Scuro" })
            foreach (var count in new[] { 2, 500 })
            foreach (var confirmation in new[] { true, false })
            {
                LocalizationService.Initialize(locale); ThemeService.Apply(theme); TypographyService.Apply("Media");
                var items = Enumerable.Range(0, count).Select(i => new UpdateItem { Id = "Fixture.App" + i, Name = "Fixture app " + i,
                    Kind = UpdateKind.Software, Source = "winget", InstalledVersion = "1.0", AvailableVersion = "2.0", Status = "Da aggiornare" }).ToArray();
                var plan = RunnerPlanSnapshot.Create(new UpdatePlan { Items = items.Select(ElevatedUpdateRunner.ToPlanItem).ToList() }, false);
                var preflight = new PreflightResult { PowerStatus = "Fixture", DiskStatus = "Fixture" };
                preflight.Warnings.AddRange(Enumerable.Repeat("Fixture warning with available technical information.", 12));
                Window window = confirmation
                    ? new UpdateConfirmationWindow([plan], preflight)
                    : new UpdateProgressWindow(new MainViewModel(), items);
                VisualQaSafety.Attach(window);
                try
                {
                    if (window is UpdateProgressWindow)
                    {
                        // Exercise the same bounded operation grid with a visible, inert details section.
                        items[0].ResultDetails = string.Join('\n', Enumerable.Repeat("Fixture detail", 40));
                        items[0].Diagnostics = string.Join('\n', Enumerable.Repeat("Fixture diagnostic", 40));
                    }
                    window.Width = 720; window.Height = 500; window.Show(); Pump(window);
                    if (window is UpdateProgressWindow) { Visuals<Expander>(window).Single().IsExpanded = true; Pump(window); }
                    var grid = (DataGrid)window.FindName(confirmation ? "ItemsGrid" : "OperationItemsGrid");
                    var page = Visuals<ScrollViewer>(window).Single(s => s.IsAncestorOf(grid));
                    var inner = Visuals<ScrollViewer>(grid).First();
                    foreach (var size in new[] { new Size(720, 500), new Size(1240, 790), new Size(720, 500) })
                    {
                        window.Width = size.Width; window.Height = size.Height; Pump(window);
                        Check(page.ScrollableHeight > 5, "Fixture requires outer overflow");
                        Check(double.IsFinite(grid.ActualHeight) && grid.ActualHeight <= grid.MaxHeight + 1, "Grid is measured without a bounded viewport");
                        if (count == 2) Check(inner.ScrollableHeight == 0, "Small fixture unexpectedly overflows table");
                        if (count == 2) Capture(window, $"{(confirmation ? "summary" : "progress")}-{locale}-{theme}-{size.Width}x{size.Height}");
                        VerifyBoundaryWheel(window, grid, page, inner, count > 2);
                        if (count == 500) VerifyLargeGrid(window, grid, page, inner);
                        VerifyPageControls(window, page);
                        var footer = (FrameworkElement)window.FindName("DialogFooter");
                        var bounds = footer.TransformToAncestor(window).TransformBounds(new Rect(footer.RenderSize));
                        Check(bounds.Bottom <= window.ActualHeight + 1 && bounds.Right <= window.ActualWidth + 1, "Footer clipped after scrolling/resizing");
                        Check(grid.Items.Count == count, "Scroll changed operation items");
                    }
                    if (count == 2) Capture(window, $"{(confirmation ? "summary" : "progress")}-{locale}-{theme}");
                    Console.WriteLine($"PASS {locale}/{theme}/{(confirmation ? "summary" : "progress")}/{count}: both sizes and resize");
                }
                finally
                {
                    if (window is UpdateProgressWindow) typeof(UpdateProgressWindow).GetField("_canClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                    window.Close();
                }
            }
        }
        finally { app.Shutdown(); }
    }

    private static void VerifyBoundaryWheel(Window window, DataGrid grid, ScrollViewer page, ScrollViewer inner, bool large)
    {
        foreach (var delta in new[] { -120, 120 })
        {
            if (delta < 0) inner.ScrollToBottom(); else inner.ScrollToTop(); Pump(window);
            var targets = new UIElement[] { Visuals<DataGridCell>(grid).First(), Visuals<DataGridColumnHeader>(grid).First(h => h.Column is not null), grid };
            foreach (var target in targets)
            {
                page.ScrollToVerticalOffset(Math.Max(1, page.ScrollableHeight - 50)); Pump(window);
                var before = page.VerticalOffset; var beforeInner = inner.VerticalOffset;
                Wheel(page, delta); Pump(window); var expected = page.VerticalOffset - before;
                Check(Math.Abs(expected) > 0, "Fixture lacks page scroll room");
                page.ScrollToVerticalOffset(before); Pump(window);
                Wheel(target, delta); Pump(window);
                Check(Math.Abs(page.VerticalOffset - before - expected) < 1,
                    $"Wheel trapped/doubled over {target.GetType().Name}, large={large}, delta={delta}, expected={expected}, actual={page.VerticalOffset - before}");
                Check(Math.Abs(inner.VerticalOffset - beforeInner) < .001, "Boundary wheel moved grid unexpectedly");
            }
        }
    }

    private static void VerifyLargeGrid(Window window, DataGrid grid, ScrollViewer page, ScrollViewer inner)
    {
        Check(grid.EnableRowVirtualization && ScrollViewer.GetCanContentScroll(grid) && VirtualizingPanel.GetVirtualizationMode(grid) == VirtualizationMode.Recycling, "Grid virtualization changed");
        grid.ScrollIntoView(grid.Items[250]); Pump(window);
        Check(Visuals<DataGridRow>(grid).Count() < 50, "500 rows materialized");
        foreach (var delta in new[] { -120, 120 })
        {
            var before = inner.VerticalOffset; var beforePage = page.VerticalOffset;
            Wheel(Visuals<DataGridCell>(grid).First(), delta); Pump(window);
            Check(delta < 0 ? inner.VerticalOffset > before : inner.VerticalOffset < before, "Native grid wheel cannot reach remaining rows");
            Check(Math.Abs(page.VerticalOffset - beforePage) < 1, "Grid and page both scrolled away from boundary");
        }
        grid.SelectedIndex = 250; grid.CurrentCell = new DataGridCellInfo(grid.Items[250], grid.Columns[0]);
        grid.ScrollIntoView(grid.Items[250], grid.Columns[0]); Pump(window);
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(grid.Items[250]);
        var cell = Visuals<DataGridCell>(row).Single(c => c.Column == grid.Columns[0]);
        cell.BringIntoView(); Pump(window); window.Activate();
        Check(cell.Focus(), "Native grid cell could not receive keyboard focus"); Pump(window);
        Check(cell.IsKeyboardFocusWithin, $"Keyboard fixture did not focus a real cell: {Keyboard.FocusedElement?.GetType().Name}");
        Key(cell, System.Windows.Input.Key.Down); Pump(window);
        Check(grid.SelectedIndex == 251 && grid.IsKeyboardFocusWithin,
            $"Native row keyboard navigation/focus changed: selected={grid.SelectedIndex}, current={grid.Items.IndexOf(grid.CurrentCell.Item)}, focusWithin={grid.IsKeyboardFocusWithin}, focused={Keyboard.FocusedElement?.GetType().Name}");
    }

    private static void VerifyPageControls(Window window, ScrollViewer page)
    {
        page.ScrollToTop(); Pump(window); page.Focus();
        Key(page, System.Windows.Input.Key.PageDown); Pump(window);
        var afterDown = page.VerticalOffset; Check(afterDown > 0, "PageDown cannot scroll dialog");
        Key(page, System.Windows.Input.Key.PageUp); Pump(window); Check(page.VerticalOffset < afterDown, "PageUp cannot scroll dialog");
        var bar = (ScrollBar)page.Template.FindName("PART_VerticalScrollBar", page);
        ScrollBar.ScrollToBottomCommand.Execute(null, bar); Pump(window);
        Check(Math.Abs(page.VerticalOffset - page.ScrollableHeight) < 1, "Page scrollbar cannot reach bottom");
        ScrollBar.ScrollToTopCommand.Execute(null, bar); Pump(window); Check(page.VerticalOffset == 0, "Page scrollbar cannot reach top");
    }

    private static void Wheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        target.RaiseEvent(args); if (args.Handled) return;
        args.RoutedEvent = Mouse.MouseWheelEvent; target.RaiseEvent(args);
    }
    private static void Key(UIElement target, System.Windows.Input.Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
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
        var output = Path.Combine(source!.FullName, "_output", "r1-20261110", "dialog-screenshots"); Directory.CreateDirectory(output);
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
