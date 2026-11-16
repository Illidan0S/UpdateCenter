using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class LanguageUiTests
{
    public static void Run(bool verifySettings = false, bool verifyHomeShell = false)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyLanguageSwitches(verifySettings, verifyHomeShell); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("PASS language UI: active title, responsive navigation, IT/EN and saved preferences.");
    }

    private static void VerifyLanguageSwitches(bool verifySettings, bool verifyHomeShell)
    {
        // Program initializes an isolated AppPaths root before this dispatch.
        JsonStorage.SaveSettings(new AppSettings
        {
            ScanAtStartup = false, AutomaticScanInterval = "Off",
            CheckAppUpdatesAutomatically = false, NotifyWhenUpdatesAreAvailable = false
        });
        var app = new App();
        app.InitializeComponent();
        LocalizationService.Initialize("it");
        ThemeService.Apply("Chiaro");
        TypographyService.Apply("Media");
        var window = new MainWindow();
        try
        {
            // A hidden HWND supplies real window dimensions; Loaded is never raised.
            _ = new WindowInteropHelper(window).EnsureHandle();
            window.Visibility = Visibility.Hidden; // Window defaults to Collapsed (zero layout size).
            // Do not Show/Run/pump the dispatcher: no startup scans, timers or hardware reads.
            Layout(window, 1240);
            Invoke(window, "ShowPage", Find<UIElement>(window, "HomePage"), "Home");
            LocalizationService.ApplyTo(window); // Capture the real initial static-text cache.
            Invoke(window, "ShowPage", Find<UIElement>(window, "SettingsPage"), "Impostazioni");
            Choose(window, "EnglishLanguageChoice");
            Equal("Settings", Find<TextBlock>(window, "PageTitle").Text, "active page title after IT -> EN");
            VerifyNavigation(window, "en", compact: false);
            VerifySaved("en");

            // The cache was created at desktop width; switches must preserve icon-only mode.
            Layout(window, 720);
            Choose(window, "ItalianLanguageChoice");
            Equal("Impostazioni", Find<TextBlock>(window, "PageTitle").Text, "active page title after EN -> IT");
            VerifyNavigation(window, "it", compact: true);
            VerifySaved("it");
            Choose(window, "EnglishLanguageChoice");
            VerifyNavigation(window, "en", compact: true);

            Layout(window, 1240);
            Choose(window, "ItalianLanguageChoice");
            VerifyNavigation(window, "it", compact: false);
            Choose(window, "EnglishLanguageChoice");
            VerifyNavigation(window, "en", compact: false);
            VerifySaved("en");
            if (verifySettings) SettingsUiTests.Verify(window);
            if (verifyHomeShell) HomeShellUiTests.Verify(window);
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
    }

    private static void VerifyNavigation(MainWindow window, string language, bool compact)
    {
        (string Name, string Icon, string Italian, string English)[] items =
        [
            ("HomeNav", "⌂", "Home", "Home"),
            ("UpdatesNav", "↓", "Aggiornamenti", "Updates"),
            ("SystemInfoNav", "▤", "Hardware", "Hardware"),
            ("HardwareNav", "▣", "Driver e chipset", "Drivers and chipset"),
            ("HistoryNav", "◷", "Cronologia", "History"),
            ("NetworkNav", "⌘", "Gestione rete", "Network management"),
            ("SettingsNav", "⚙", "Impostazioni", "Settings")
        ];
        foreach (var (name, icon, italian, english) in items)
        {
            var label = language == "en" ? english : italian;
            var button = Find<Button>(window, name);
            // The vector icon is now separate from the localized, wrapping caption.
            Equal(compact ? string.Empty : label, button.Content, name + " visible label");
            Equal(label, button.ToolTip, name + " tooltip");
            Equal(label, AutomationProperties.GetName(button), name + " accessible name");
        }
        if (Find<UIElement>(window, "SettingsPage").Visibility != Visibility.Visible)
            throw new InvalidOperationException("Changing language navigated away from Settings.");
    }

    private static void VerifySaved(string language)
    {
        var saved = JsonStorage.LoadSettings();
        Equal(language, saved.LanguageMode, "persisted language");
        Equal("Chiaro", saved.ThemeMode, "preserved theme");
        Equal("Media", saved.FontSizeMode, "preserved text size");
    }

    private static void Layout(MainWindow window, double width)
    {
        window.Width = width;
        window.Measure(new Size(width, 790));
        window.Arrange(new Rect(0, 0, width, 790));
        Equal(width, window.ActualWidth, "fixture window width");
        Invoke(window, "ApplyResponsiveLayout");
    }

    private static void Choose(MainWindow window, string name) =>
        Find<RadioButton>(window, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static T Find<T>(MainWindow window, string name) => (T)window.FindName(name);

    private static void Invoke(MainWindow window, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static void Equal(object expected, object? actual, string context)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{context}: expected '{expected}', got '{actual}'.");
    }
}
