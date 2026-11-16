using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class UpdateStatusFlyoutTests
{
    internal static void Run(bool preview = false)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(preview); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        if (preview) { Console.WriteLine("Closed isolated status flyout preview."); return; }
        Console.WriteLine("PASS status flyout: WPF activation, full/live content, IT/EN, light/dark, large text, 720x500, Escape/focus, close, single instance, scrolling, recycling and context changes. No scan/install executed. Native DPI 150/200% and physical outside-click remain manual checks.");
    }

    private static void Verify(bool preview)
    {
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, AutomaticScanInterval = "Off", CheckAppUpdatesAutomatically = false });
        var app = new Application();
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "App.xaml"))) source = source.Parent;
        var root = System.Xml.Linq.XDocument.Load(Path.Combine(source!.FullName, "App.xaml")).Root!;
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new System.Xml.Linq.XElement(ns + "ResourceDictionary", root.Attributes().Where(a => a.IsNamespaceDeclaration), root.Element(ns + "Application.Resources")!.Nodes()).ToString());
        LocalizationService.Initialize("it"); ThemeService.Apply("Chiaro"); TypographyService.Apply("Grande");
        var window = new MainWindow();
        VisualQaSafety.Attach(window, allowUpdateStatusFlyout: true);
        try
        {
            var vm = (MainViewModel)window.DataContext;
            var items = Enumerable.Range(0, 500).Select(i => new UpdateItem
            {
                Id = "QA.Status." + i, Name = "QA status " + i, Kind = UpdateKind.Software, Source = "QA",
                IsSelected = false, Status = i == 1 ? "Errore" : "Da aggiornare",
                ResultDetails = string.Join(" ", Enumerable.Repeat("Complete synthetic reason with meaningful instructions, never truncated.", 45)) + " END OF REASON",
                CompatibilityDetail = "Exact package and scope must be verified.",
                OfficialReleasePageUrl = i == 1 ? "https://example.invalid/source" : ""
            }).ToList();
            typeof(MainViewModel).GetMethod("AddUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [items]);
            Invoke(window, "ShowPage", window.FindName("UpdatesPage")!, "Aggiornamenti");
            window.ShowInTaskbar = preview; window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Title = preview ? "UpdateCenter Â· Status QA Â· synthetic data only" : window.Title;
            window.Left = preview ? 100 : -20000; window.Top = preview ? 100 : -20000;
            window.Width = 720; window.Height = 500; window.Show();
            Pump(window);
            var grid = (DataGrid)window.FindName("UpdatesGrid");
            if (preview)
            {
                grid.ScrollIntoView(items[0], grid.Columns[^1]); Pump(window);
                window.Activate();
                // Only status-details handlers survive VisualQaSafety. All executable actions are inert.
                var previewFrame = new DispatcherFrame();
                window.Closed += (_, _) => previewFrame.Continue = false;
                Dispatcher.PushFrame(previewFrame);
                return;
            }
            foreach (var locale in new[] { "it", "en" })
            foreach (var theme in new[] { "Chiaro", "Scuro" })
            {
                items[0].ResultDetails = string.Join(" ", Enumerable.Repeat("Complete synthetic reason with meaningful instructions, never truncated.", 45)) + " END OF REASON";
                LocalizationService.Initialize(locale); ThemeService.Apply(theme); Invoke(window, "UpdateAppearanceTheme"); Invoke(window, "UpdateOperationsLabels"); vm.NotifyLanguageChanged();
                grid.ScrollIntoView(items[0], grid.Columns[^1]); Pump(window);
                var button = Visuals<Button>(grid).FirstOrDefault(b => b.Name == "UpdateStatusButton" && ReferenceEquals(b.DataContext, items[0]));
                Check(button is not null, "Status cell has no keyboard-accessible detail action.");
                Check(AutomationProperties.GetName(button!).Length > 0 && button!.Focusable, "Status action lacks accessible name/focus.");
                button!.Focus();
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); Pump(window);
                var popup = (Popup)window.FindName("UpdateStatusPopup");
                Check(popup.IsOpen && !popup.StaysOpen, "Status activation does not open a dismissible flyout.");
                var content = (FrameworkElement)popup.Child;
                Check(content.ActualWidth <= 720 && content.ActualHeight <= 500 && content.ActualHeight > 0, "Flyout exceeds minimum window bounds.");
                Check(Visuals<TextBlock>(content).Any(t => t.Text == items[0].Status && t.TextWrapping == TextWrapping.Wrap && t.TextTrimming == TextTrimming.None), "Full status title missing.");
                var title = Visuals<TextBlock>(content).Single(t => t.Text == items[0].Status);
                Check(title.TransformToAncestor(Visuals<ScrollViewer>(content).First()).Transform(new Point()).Y >= 0, "Opening long details scrolls the status title out of view.");
                Check(Visuals<TextBlock>(content).Any(t => t.Text == (locale == "it" ? "Dettagli dello stato" : "Status details")), "Flyout header language is stale.");
                Check(Visuals<TextBox>(content).Any(t => t.Text == items[0].ResultDetails && t.IsReadOnly && t.TextWrapping == TextWrapping.Wrap), "Complete selectable reason missing.");
                Check(Visuals<TextBlock>(content).Any(t => t.Text == items[0].PriorityDescription), "Existing instructions missing/localization stale.");
                Check(Visuals<ScrollViewer>(content).Any(s => s.ScrollableHeight > 0), "Long text has no scrolling path.");
                Capture(content, locale + "-" + theme + "-long");
                var outerScroll = Visuals<ScrollViewer>(content).First();
                var reasonBox = Visuals<TextBox>(content).Single(t => t.Text == items[0].ResultDetails);
                reasonBox.Focus(); outerScroll.ScrollToTop(); Pump(window);
                Press(reasonBox, Key.PageDown); Pump(window);
                Check(outerScroll.VerticalOffset > 0, "PageDown in the reason text cannot reveal further status details.");
                var pageOffset = outerScroll.VerticalOffset;
                Press(reasonBox, Key.PageUp); Pump(window);
                Check(outerScroll.VerticalOffset < pageOffset, "PageUp cannot return to preceding details.");
                Press(reasonBox, Key.End); Pump(window);
                Check(Math.Abs(outerScroll.VerticalOffset - outerScroll.ScrollableHeight) < 1, "End cannot reach the final instructions.");
                Capture(content, locale + "-" + theme + "-end");
                Press(reasonBox, Key.Home); Pump(window);
                Check(outerScroll.VerticalOffset == 0, "Home cannot return to the full title.");
                Press(reasonBox, Key.End); Pump(window);
                Press(reasonBox, Key.Escape); Pump(window);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window);
                Check(outerScroll.VerticalOffset == 0, "Reopened details keep the preceding scroll offset.");
                items[0].ResultDetails = "Changed complete reason " + locale + theme;
                Pump(window);
                Check(Visuals<TextBox>(content).Any(t => t.Text == items[0].ResultDetails), "Open flyout keeps stale snapshot text.");
                Capture(content, locale + "-" + theme + "-updated");
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(content), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                content.RaiseEvent(input); Pump(window);
                Check(!popup.IsOpen && input.Handled, "Escape does not dismiss the flyout.");
                Check(ReferenceEquals(Keyboard.FocusedElement, button), "Escape does not restore the invoking control's keyboard focus.");
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window);
                var close = Visuals<Button>(content).Single(b => b.Name == "UpdateStatusCloseButton");
                close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window);
                Check(!popup.IsOpen, "Close button did not dismiss flyout.");
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window);
                grid.ScrollIntoView(items[^1]); Pump(window);
                Check(!popup.IsOpen, "Scrolling/recycling leaves stale status open.");
                Check(Visuals<DataGridRow>(grid).Count() < 80 && grid.EnableRowVirtualization && grid.EnableColumnVirtualization && VirtualizingPanel.GetVirtualizationMode(grid) == VirtualizationMode.Recycling, "Flyout regressed virtualization.");
                grid.ScrollIntoView(items[1], grid.Columns[^1]); Pump(window);
                button = Visuals<Button>(grid).Single(b => b.Name == "UpdateStatusButton" && ReferenceEquals(b.DataContext, items[1]));
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window);
                Check(popup.IsOpen && ReferenceEquals(content.DataContext, items[1]), "Second row displays stale item.");
                var actions = Visuals<Button>(grid).Where(b => ReferenceEquals(b.DataContext, items[1]) && b.Content is string label && (label == LocalizationService.Translate("Apri fonte") || label == LocalizationService.Translate("Riprova")) && b.Visibility == Visibility.Visible).ToArray();
                Check(actions.Length == 2, "Source/retry actions lost.");
                OperationsUiFixture.Set(vm, "IsBusy", true);
                Check(!actions.Single(b => Equals(b.Content, LocalizationService.Translate("Riprova"))).IsEnabled, "Retry busy guard lost.");
                OperationsUiFixture.Set(vm, "IsBusy", false);
                Invoke(window, "ShowPage", window.FindName("HomePage")!, "Home"); Pump(window);
                Check(!popup.IsOpen, "Navigation leaves flyout open.");
                Invoke(window, "ShowPage", window.FindName("UpdatesPage")!, "Aggiornamenti"); Pump(window);
            }
        }
        finally { window.Close(); app.Shutdown(); }
    }

    private static void Press(UIElement target, Key key)
    {
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private static void Pump(MainWindow window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); window.UpdateLayout();
    }
    private static void Capture(FrameworkElement content, string name)
    {
        var folder = Path.GetFullPath("_output/status-flyout-20261109/screenshots"); Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name + ".png")); png.Save(stream);
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static void Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
