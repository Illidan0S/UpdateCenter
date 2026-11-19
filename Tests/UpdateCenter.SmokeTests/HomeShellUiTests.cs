using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using UpdateCenter;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class HomeShellUiTests
{
    // Uses the hidden, isolated STA fixture. No dispatcher loop or real scan is started.
    public static void Verify(MainWindow window)
    {
        (string Button, string Page, string Title)[] destinations =
        [
            ("HomeNav", "HomePage", "Home"),
            ("UpdatesNav", "UpdatesPage", "Aggiornamenti"),
            ("SystemInfoNav", "SystemInfoPage", "Hardware"),
            ("HardwareNav", "HardwarePage", "Driver e chipset"),
            ("HistoryNav", "HistoryPage", "Cronologia"),
            ("NetworkNav", "NetworkPage", "Gestione rete"),
            ("SettingsNav", "SettingsPage", "Impostazioni")
        ];
        foreach (var locale in new[] { "it", "en" })
        foreach (var theme in new[] { "Chiaro", "Scuro" })
        foreach (var textSize in new[] { "Piccola", "Media", "Grande" })
        {
            LocalizationService.Initialize(locale);
            LocalizationService.ApplyTo(window);
            ThemeService.Apply(theme);
            TypographyService.Apply(textSize);
            Invoke(window, "UpdateAppearanceTheme");
            foreach (var size in new[] { new Size(1240, 790), new Size(960, 700), new Size(720, 500) })
            {
                window.Width = size.Width; window.Height = size.Height;
                window.Measure(size); window.Arrange(new Rect(size));
                Invoke(window, "ApplyResponsiveLayout");
                foreach (var destination in destinations)
                {
                    Invoke(window, "ShowPage", Find<UIElement>(window, destination.Page), destination.Title);
                    window.UpdateLayout();
                    var selected = Find<Button>(window, destination.Button);
                    Check(destinations.Count(d => Equals(Find<Button>(window, d.Button).Tag, true)) == 1,
                        "Navigation must identify exactly one current page.");
                    Check(Equals(selected.Tag, true), "Wrong selected destination.");
                    Check(Find<TextBlock>(window, "PageTitle").Text == LocalizationService.Translate(destination.Title),
                        "Header does not match the current page/language.");
                    Check(AutomationProperties.GetItemStatus(selected).Length > 0, "Current page is not accessible.");
                    foreach (var item in destinations)
                    {
                        var button = Find<Button>(window, item.Button);
                        button.ApplyTemplate();
                        var caption = (TextBlock)button.Template.FindName("NavigationLabel", button);
                        var indicator = (FrameworkElement)button.Template.FindName("SelectionIndicator", button);
                        Check(indicator.Visibility == (item == destination ? Visibility.Visible : Visibility.Collapsed),
                            "Selected indicator disagrees with the current page.");
                        Check(button.Focusable && button.FocusVisualStyle is not null, "Keyboard focus unavailable.");
                        Check(AutomationProperties.GetName(button) == LocalizationService.Translate(item.Title),
                            "Navigation accessible name is missing or untranslated.");
                        if (caption.Visibility == Visibility.Visible)
                        {
                            Check(caption.TextWrapping == TextWrapping.Wrap, "Navigation caption cannot wrap.");
                            var text = new FormattedText(caption.Text, LocalizationService.Culture,
                                FlowDirection.LeftToRight, new Typeface(caption.FontFamily, caption.FontStyle,
                                    caption.FontWeight, caption.FontStretch), caption.FontSize, Brushes.Black, 1)
                            { MaxTextWidth = caption.ActualWidth };
                            Check(text.Height <= caption.ActualHeight + 1, $"Clipped caption: {locale}/{textSize}/{size.Width}/{item.Title}");
                        }
                        else Check(caption.Visibility == Visibility.Collapsed && size.Width == 720, "Unexpected hidden caption.");
                    }
                    var brand = Bounds(window, "BrandPanel");
                    var title = Bounds(window, "PageTitle");
                    var controls = Bounds(window, "MinimizeWindowButton");
                    Check(brand.Right <= title.Left && title.Right <= controls.Left && title.Bottom <= 57,
                        "Header overlaps at the tested size.");
                }
                Invoke(window, "ShowPage", Find<UIElement>(window, "HomePage"), "Home");
                window.UpdateLayout();
                var home = Find<ScrollViewer>(window, "HomePage");
                Check(home.ExtentWidth <= home.ViewportWidth + 1, "Home requires horizontal scrolling.");
                Check(Find<Button>(window, "HomeScanButton").ActualWidth > 0, "Primary action is missing.");
                Check(Find<FrameworkElement>(window, "HomeStatusCard").Visibility == Visibility.Visible,
                    "Status disappeared at the minimum width.");
            }
        }

        var vm = (MainViewModel)window.DataContext;
        Check(Find<Button>(window, "HomeScanButton").IsEnabled, "Idle scan action is disabled.");
        Check(Find<Button>(window, "HomeCancelButton").Visibility == Visibility.Collapsed, "Idle cancellation is visible.");
        Check(Find<TextBlock>(window, "HomeAvailableCount").Text == "0", "Empty update count is incorrect.");
        Set(vm, "IsBusy", true); Set(vm, "IsScanRunning", true);
        Set(vm, "Progress", 38d); Set(vm, "ScannedCount", 7);
        Set(vm, "StatusText", "Scansione in corso");
        Set(vm, "CurrentItemText", "Inventario di processore, chipset e driver installati…");
        window.UpdateLayout();
        Check(!Find<Button>(window, "HomeScanButton").IsEnabled, "Busy scan action is enabled.");
        Check(Find<Button>(window, "HomeCancelButton") is { IsEnabled: true, Visibility: Visibility.Visible },
            "Running scan cannot be cancelled.");
        Check(Find<ProgressBar>(window, "HomeProgress").Value == 38, "Progress binding is lost.");
        Check(Find<TextBlock>(window, "HomeScannedCount").Text == "7", "Scanned count binding is lost.");
        Check(Find<TextBlock>(window, "HomeStatusText").Text == vm.StatusText,
            $"Status binding: expected '{vm.StatusText}', got '{Find<TextBlock>(window, "HomeStatusText").Text}', bound={System.Windows.Data.BindingOperations.IsDataBound(Find<TextBlock>(window, "HomeStatusText"), TextBlock.TextProperty)}.");
        Set(vm, "IsBusy", false); Set(vm, "IsScanRunning", false);
        Set(vm, "StatusText", "Scansione completata con avvisi");
        Set(vm, "CurrentItemText", "Una fonte non è disponibile. Puoi avviare una nuova scansione.");
        window.UpdateLayout();
        Check(Find<TextBlock>(window, "HomeCurrentItemText").Text == vm.CurrentItemText,
            "Diagnostic detail binding is lost.");
        Check(Find<TextBlock>(window, "HomeCurrentItemText").TextWrapping == TextWrapping.Wrap,
            "Long diagnostic details are clipped.");
        Check(Find<Button>(window, "HomeCancelButton").Visibility == Visibility.Collapsed,
            "Completed scan still shows cancellation.");
        Check(Find<TextBlock>(window, "VersionText").Text == "v2.0.1", "Application version changed.");
        Console.WriteLine("PASS Home/shell UI: 36 language/theme/text/viewport combinations, seven destinations, selected indicator, wrapping captions, header bounds, accessible focus, empty/running/diagnostic bindings and version 2.0.1.");
    }

    private static Rect Bounds(MainWindow window, string name)
    {
        var element = Find<FrameworkElement>(window, name);
        return element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
    }
    private static T Find<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static void Set(MainViewModel vm, string name, object value) =>
        typeof(MainViewModel).GetProperty(name)!.SetValue(vm, value);
    private static void Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
