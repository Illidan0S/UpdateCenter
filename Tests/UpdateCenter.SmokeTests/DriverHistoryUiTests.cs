using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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

internal static class DriverHistoryUiTests
{
    internal static void Run(bool drivers)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(drivers); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine(drivers ? "PASS driver page: one vertical viewport, scrolling header, wheel/page/scrollbar commands, 500 rows with bounded realization, sorting/filtering/selection, empty state, IT/EN light/dark 720x500 and 1240x790."
            : "PASS history details: UIA click, short/long/empty/multiline/diagnostics, selectable text, keyboard scroll, Escape/focus, close, recycling/sort/navigation, IT/EN light/dark 720x500 and 1240x790.");
    }

    private static void Verify(bool drivers)
    {
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, AutomaticScanInterval = "Off", CheckAppUpdatesAutomatically = false, NotifyWhenUpdatesAreAvailable = false });
        var app = new Application();
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "App.xaml"))) source = source.Parent;
        var root = System.Xml.Linq.XDocument.Load(Path.Combine(source!.FullName, "App.xaml")).Root!;
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new System.Xml.Linq.XElement(ns + "ResourceDictionary", root.Attributes().Where(a => a.IsNamespaceDeclaration), root.Element(ns + "Application.Resources")!.Nodes()).ToString());
        LocalizationService.Initialize("it"); ThemeService.Apply("Chiaro"); TypographyService.Apply("Grande");
        var window = new MainWindow();
        VisualQaSafety.Attach(window, allowHistoryDetails: true);
        try
        {
            var vm = (MainViewModel)window.DataContext;
            OperationsUiFixture.Populate(vm, 500);
            window.ShowInTaskbar = false; window.Left = -20000; window.Top = -20000;
            window.Width = 720; window.Height = 500; window.Show(); Pump(window);
            foreach (var locale in new[] { "it", "en" })
            foreach (var theme in new[] { "Chiaro", "Scuro" })
            foreach (var size in new[] { new Size(720, 500), new Size(1240, 790) })
            {
                LocalizationService.Initialize(locale); ThemeService.Apply(theme); vm.NotifyLanguageChanged();
                Invoke(window, "UpdateAppearanceTheme"); Invoke(window, "UpdateOperationsLabels");
                window.Width = size.Width; window.Height = size.Height;
                Invoke(window, "ShowPage", window.FindName(drivers ? "HardwarePage" : "HistoryPage")!, drivers ? "Driver e chipset" : "Cronologia"); Pump(window);
                if (drivers) Drivers(window, vm); else History(window, vm, locale);
                Capture(window, $"{(drivers ? "drivers" : "history")}-{locale}-{theme}-{size.Width}");
            }
        }
        finally { window.Close(); app.Shutdown(); }
    }

    private static void Drivers(MainWindow window, MainViewModel vm)
    {
        var grid = (DataGrid)window.FindName("DriverInventoryGrid");
        var page = (FrameworkElement)window.FindName("HardwarePage");
        var header = (FrameworkElement)window.FindName("DriverHeaderCard");
        var scrollers = Visuals<ScrollViewer>(page).Where(s => s.ScrollableHeight > 0 && s.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled).ToArray();
        Check(scrollers.Length == 1, $"Expected one active vertical scroller, found {scrollers.Length}.");
        var scroll = scrollers.Single(); scroll.ScrollToTop(); Pump(window);
        VerifyDriverSectionWheel(window, vm, scroll);
        scroll.ScrollToTop(); Pump(window);
        var y = header.TransformToAncestor(page).Transform(new Point()).Y;
        scroll.ScrollToVerticalOffset(100); Pump(window);
        Check(header.TransformToAncestor(page).Transform(new Point()).Y < y - 50, "Page heading remains fixed while inventory scrolls.");
        scroll.ScrollToTop(); Pump(window);
        page.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent }); Pump(window);
        // Route from content as actual wheel input does, never from an unrelated parent.
        header.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent }); Pump(window);
        Check(scroll.VerticalOffset > 0, "Wheel over the heading cannot scroll the page.");
        scroll.PageDown(); Pump(window); var offset = scroll.VerticalOffset;
        scroll.PageUp(); Pump(window); Check(scroll.VerticalOffset < offset, "PageUp cannot return toward the header.");
        var scrollbar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
        // The pixel extent is estimated until the last rows are measured. Reissue the
        // native command after layout, as a scrollbar at the end follows its new maximum.
        for (var settle = 0; settle < 3; settle++)
        {
            ScrollBar.ScrollToBottomCommand.Execute(null, scrollbar); Pump(window);
            if (Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1) break;
        }
        Check(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1, "Scrollbar command does not reach the end.");
        Capture(window, $"inventory-end-{LocalizationService.CurrentLanguage}-{(ThemeService.IsDark ? "dark" : "light")}-{window.ActualWidth}");
        Check(Visuals<DataGridRow>(grid).Any(r => ReferenceEquals(r.Item, vm.DriverInventory[^1])), "Scrollbar End does not reach final driver.");
        Console.WriteLine($"driver realized={Visuals<DataGridRow>(grid).Count()}, offset={scroll.VerticalOffset}, viewport={scroll.ViewportHeight}, extent={scroll.ExtentHeight}");
        Check(Visuals<DataGridRow>(grid).Count() < 80, "500 driver rows were realized instead of virtualized.");
        Check(grid.EnableColumnVirtualization && grid.EnableRowVirtualization && VirtualizingPanel.GetVirtualizationMode(grid) == VirtualizationMode.Recycling, "Grid virtualization settings lost.");
        grid.SelectedItem = vm.DriverInventory[250]; grid.ScrollIntoView(grid.SelectedItem); Pump(window);
        Check(Visuals<DataGridRow>(grid).Any(r => r.IsSelected && ReferenceEquals(r.Item, vm.DriverInventory[250])), "Selection/ScrollIntoView lost.");
        grid.CurrentCell = new DataGridCellInfo(grid.SelectedItem, grid.Columns[0]);
        var selectedRow = Visuals<DataGridRow>(grid).Single(r => r.IsSelected);
        var cell = Visuals<DataGridCell>(selectedRow).First(); cell.Focus(); Pump(window);
        var beforeKey = scroll.VerticalOffset;
        for (var keyPress = 0; keyPress < 2; keyPress++)
        {
            ((UIElement)Keyboard.FocusedElement).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid), 0, Key.PageDown) { RoutedEvent = Keyboard.KeyDownEvent }); Pump(window);
        }
        Check(scroll.VerticalOffset > beforeKey, "PageDown keyboard input cannot advance the inventory.");
        grid.Items.SortDescriptions.Clear();
        grid.Items.SortDescriptions.Add(new SortDescription("DeviceName", ListSortDirection.Descending)); Pump(window);
        Check(ReferenceEquals(grid.Items[0], vm.DriverInventory[^1]), "Sorting no longer orders drivers.");
        grid.Items.SortDescriptions.Clear();
        scroll.ScrollToVerticalOffset(((FrameworkElement)window.FindName("DriversContent")).ActualHeight); Pump(window);
        var columnHeader = Visuals<DataGridColumnHeader>(grid).First(h => h.IsVisible && ReferenceEquals(h.Column, grid.Columns[0]));
        var beforeHeaderWheel = scroll.VerticalOffset;
        columnHeader.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent }); Pump(window);
        Check(scroll.VerticalOffset > beforeHeaderWheel, "Wheel over column headers cannot scroll the page.");
        typeof(DataGridColumnHeader).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(columnHeader, null); Pump(window);
        Check(grid.Items.SortDescriptions.Count > 0 && grid.Items.SortDescriptions[0].PropertyName == "DeviceName", "Column header cannot sort the inventory.");
        grid.Items.SortDescriptions.Clear();
        vm.DriverSearchText = "NO_MATCH_FOR_QA"; Pump(window);
        Check(grid.Items.Count == 0 && header.IsVisible, "Filtering all drivers hides the page header.");
        Check(((FrameworkElement)window.FindName("DriverInventoryEmptyState")).IsVisible, "Empty inventory feedback lost.");
        vm.DriverSearchText = ""; Pump(window); scroll.ScrollToTop(); Pump(window);
        Check(grid.Items.Count == 500 && Visuals<DataGridRow>(grid).Count() < 80, "Restoring the filter breaks inventory virtualization.");
        Check(header.DataContext == vm, "Driver header lost its ViewModel after regrouping.");
        var bounds = header.TransformToAncestor(page).TransformBounds(new Rect(header.RenderSize));
        Check(bounds.Width <= page.ActualWidth && bounds.Left >= -1, "Driver header exceeds the page width.");
    }

    private static void VerifyDriverSectionWheel(MainWindow window, MainViewModel vm, ScrollViewer pageScroll)
    {
        var problems = (DataGrid)window.FindName("DriverProblemsGrid");
        var vendors = (ScrollViewer)window.FindName("DriverVendorScrollViewer");
        while (vm.VendorTools.Count < 7)
            vm.VendorTools.Add(new VendorSupportItem { Name = "QA official support " + vm.VendorTools.Count,
                Description = "Synthetic support card, never launched.", Url = "https://example.invalid/qa/support" });
        var savedProblems = vm.DriverProblems.ToArray();
        foreach (var empty in new[] { true, false })
        {
            vm.DriverProblems.Clear();
            if (!empty) foreach (var problem in savedProblems) vm.DriverProblems.Add(problem);
            pageScroll.ScrollToTop(); Pump(window);
            var problemHeader = Visuals<DataGridColumnHeader>(problems).First(h => h.Column == problems.Columns[0]);
            VerifyWheel(problemHeader, "diagnostic header, empty=" + empty);
            if (!empty) VerifyWheel(Visuals<DataGridCell>(problems).First(), "diagnostic cell");
            VerifyWheel(vendors, "official support strip");
            VerifyWheel(Visuals<TextBlock>(vendors).First(t => t.Text == vm.VendorTools[0].Name), "support card text");
            VerifyWheel(Visuals<Button>(vendors).First(b => b.DataContext is VendorSupportItem), "support card button");
        }
        Check(vendors.ScrollableWidth > 0, "Seven support cards have no horizontal scrolling path.");
        var horizontalBar = (ScrollBar)vendors.Template.FindName("PART_HorizontalScrollBar", vendors);
        var verticalBefore = pageScroll.VerticalOffset;
        ScrollBar.ScrollToRightEndCommand.Execute(null, horizontalBar); Pump(window);
        Check(vendors.HorizontalOffset > 0 && Math.Abs(pageScroll.VerticalOffset - verticalBefore) < 1,
            "Horizontal support scrollbar is broken or moves the page vertically.");
        vendors.ScrollToLeftEnd(); Pump(window);

        void VerifyWheel(UIElement target, string label)
        {
            foreach (var delta in new[] { -120, 120 })
            {
                // Place each target inside the viewport, with room to scroll both ways.
                var position = target.TransformToAncestor(pageScroll).Transform(new Point());
                pageScroll.ScrollToVerticalOffset(pageScroll.VerticalOffset + position.Y - 100); Pump(window);
                var before = pageScroll.VerticalOffset;
                Wheel(pageScroll, delta); Pump(window);
                var expected = pageScroll.VerticalOffset - before;
                Check(Math.Abs(expected) > 0, "Fixture has no vertical scroll room for " + label);
                pageScroll.ScrollToVerticalOffset(before); Pump(window);
                var horizontalBefore = vendors.HorizontalOffset;
                Wheel(target, delta); Pump(window);
                Check(Math.Abs(pageScroll.VerticalOffset - before - expected) < 1,
                    $"Wheel over {label} was swallowed or scrolled twice (delta={delta}, before={before}, expected={expected}, actual={pageScroll.VerticalOffset - before}, width={window.Width}, modifiers={Keyboard.Modifiers}).");
                Check(Math.Abs(vendors.HorizontalOffset - horizontalBefore) < 1,
                    "Vertical wheel unexpectedly scrolled official support horizontally.");
            }
        }
    }

    private static void Wheel(UIElement target, int delta)
    {
        var input = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        target.RaiseEvent(input);
        if (input.Handled) return;
        input.RoutedEvent = Mouse.MouseWheelEvent;
        target.RaiseEvent(input);
    }

    private static void History(MainWindow window, MainViewModel vm, string locale)
    {
        var grid = (DataGrid)window.FindName("HistoryGrid");
        var popup = (Popup)window.FindName("HistoryDetailPopup");
        var texts = new[] { "Short complete detail", string.Join("\n", Enumerable.Repeat("Long selectable detail with complete paragraphs.", 120)) + "\nEND", "", "First line\nSecond line\n\nLast line" };
        for (var i = 0; i < texts.Length; i++)
        {
            var entry = vm.History[i]; entry.Details = texts[i]; entry.Diagnostics = i == 3 ? "Existing diagnostics only" : ""; entry.NotifyLanguageChanged();
            grid.ScrollIntoView(entry, grid.Columns[^1]); Pump(window);
            var button = Visuals<Button>(grid).FirstOrDefault(b => b.Name == "HistoryDetailButton" && ReferenceEquals(b.DataContext, entry));
            Check(button is not null, "History detail cell has no clickable/keyboard-accessible button.");
            Check(button!.Focusable && AutomationProperties.GetName(button).Length > 0, "History button lacks accessible focus/name.");
            button.Focus(); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); Pump(window);
            Check(popup.IsOpen && !popup.StaysOpen, "Click does not immediately open dismissible details.");
            var content = (FrameworkElement)popup.Child;
            Check(content.ActualWidth <= window.ActualWidth && content.ActualHeight <= window.ActualHeight, "Details exceed window bounds.");
            Check(Visuals<TextBlock>(content).Any(t => t.Text == (locale == "it" ? "Dettaglio attivit\u00e0" : "Activity details")), "Popup header language is stale.");
            var box = (TextBox)window.FindName("HistoryDetailText");
            Check(box.IsReadOnly && box.TextWrapping == TextWrapping.Wrap && box.Text.Contains(i == 2 ? (locale == "it" ? "Nessun dettaglio disponibile" : "No details available") : texts[i]), "Full/empty detail is missing.");
            Check(i != 3 || box.Text.Contains("Existing diagnostics only"), "Available diagnostics missing.");
            Check(i == 3 || !box.Text.Contains("Technical diagnostics") && !box.Text.Contains("Diagnostica tecnica"), "Absent diagnostics invented.");
            box.SelectAll(); Check(box.SelectedText == box.Text && ApplicationCommands.Copy.CanExecute(null, box), "Full detail is not selectable/copyable.");
            if (i == 1)
            {
                Check(Visuals<ScrollViewer>(content).Any(s => s.ScrollableHeight > 0), "Long detail cannot scroll.");
                box.Focus(); Press(box, Key.PageDown); Pump(window);
                Check(Visuals<ScrollViewer>(content).Any(s => s.VerticalOffset > 0), "PageDown cannot reveal long details.");
                Capture(content, $"popup-{locale}-{(ThemeService.IsDark ? "dark" : "light")}-{window.ActualWidth}");
            }
            if (i == 0)
            {
                ((Button)window.FindName("HistoryDetailCloseButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window);
                Check(!popup.IsOpen && ReferenceEquals(Keyboard.FocusedElement, button), "Close does not restore focus.");
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window);
            }
            Press(content, Key.Escape); Pump(window);
            Check(!popup.IsOpen && ReferenceEquals(Keyboard.FocusedElement, button), "Escape does not close and restore cell focus.");
        }
        grid.Items.SortDescriptions.Clear();
        grid.Items.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Descending)); Pump(window);
        Check(((HistoryEntry)grid.Items[0]).Name.StartsWith("QA 0499"), "History sorting is broken.");
        grid.Items.SortDescriptions.Clear();
        var first = vm.History[0]; grid.ScrollIntoView(first, grid.Columns[^1]); Pump(window);
        var action = Visuals<Button>(grid).Single(b => b.Name == "HistoryDetailButton" && ReferenceEquals(b.DataContext, first));
        action.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window);
        grid.ScrollIntoView(vm.History[^1]); Pump(window);
        Check(!popup.IsOpen && Visuals<DataGridRow>(grid).Count() < 80, "Recycling leaves stale popup or realizes all history.");
        grid.ScrollIntoView(first, grid.Columns[^1]); Pump(window);
        action = Visuals<Button>(grid).Single(b => b.Name == "HistoryDetailButton" && ReferenceEquals(b.DataContext, first));
        action.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window);
        Invoke(window, "ShowPage", window.FindName("HomePage")!, "Home"); Pump(window);
        Check(!popup.IsOpen, "Navigation leaves history popup open.");
    }

    private static void Press(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Pump(MainWindow window)
    {
        window.UpdateLayout(); var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); window.UpdateLayout();
    }
    private static void Capture(FrameworkElement content, string name)
    {
        var folder = Path.GetFullPath("_output/driver-history-ui/screenshots"); Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name + ".png")); png.Save(stream);
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i); if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static void Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
