using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class SettingsUiTests
{
    // Uses the isolated, hidden STA window from LanguageUiTests; no startup work is run.
    public static void Verify(MainWindow window)
    {
        var vm = (MainViewModel)window.DataContext;
        (string Name, Func<AppSettings, bool> Read)[] choices =
        [
            ("RestorePointChoice", s => s.CreateRestorePoint),
            ("SilentInstallChoice", s => s.SilentSoftwareInstall),
            ("UnknownVersionsChoice", s => s.IncludeUnknownVersions),
            ("AppUpdateChoice", s => s.CheckAppUpdatesAutomatically),
            ("StartupScanChoice", s => s.ScanAtStartup),
            ("NotificationChoice", s => s.NotifyWhenUpdatesAreAvailable)
        ];
        foreach (var (name, read) in choices)
        {
            var control = (CheckBox)window.FindName(name);
            if (control.IsChecked != read(vm.Settings)) throw new Exception(name + ": initial binding lost");
            control.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, !read(vm.Settings));
            control.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (control.IsChecked != read(vm.Settings)) throw new Exception(name + ": two-way binding lost");
            if (read(JsonStorage.LoadSettings()) != read(vm.Settings)) throw new Exception(name + ": automatic persistence lost");
            if (AutomationProperties.GetLabeledBy(control) is not TextBlock { Text.Length: > 0 })
                throw new Exception(name + ": accessible label missing");
        }

        var frequency = (ComboBox)window.FindName("ScanFrequencyChoice");
        foreach (var locale in new[] { "it", "en" })
        {
            LocalizationService.Initialize(locale);
            LocalizationService.ApplyTo(window);
            foreach (var interval in new[] { "Off", "Daily", "Weekly" })
            {
                frequency.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, interval);
                if (vm.Settings.AutomaticScanInterval != interval)
                    throw new Exception($"Frequency binding lost: {locale}/{interval}");
                var selected = (ComboBoxItem)frequency.SelectedItem;
                var italian = interval == "Off" ? "Disattivata" : interval == "Daily" ? "Ogni giorno" : "Ogni settimana";
                if (!Equals(selected.Content, LocalizationService.Translate(italian)))
                    throw new Exception($"Frequency translation lost: {locale}/{interval}");
            }
        }

        // The existing save path persists the entire settings object, not only the last edit.
        vm.SaveSettings();
        var saved = JsonStorage.LoadSettings();
        foreach (var (name, read) in choices)
            if (read(saved) != read(vm.Settings)) throw new Exception(name + ": persistence lost");
        if (saved.AutomaticScanInterval != "Weekly" || saved.ThemeMode != "Chiaro" || saved.FontSizeMode != "Media")
            throw new Exception("Saving settings changed the frequency, theme or text size unexpectedly.");

        var scroll = (ScrollViewer)window.FindName("SettingsScrollViewer");
        window.UpdateLayout();
        scroll.ScrollToVerticalOffset(400);
        window.UpdateLayout();
        foreach (var theme in new[] { "DarkThemeChoice", "LightThemeChoice" })
        {
            ((RadioButton)window.FindName(theme)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            window.UpdateLayout();
            if (Math.Abs(scroll.VerticalOffset - 400) > 1)
                throw new Exception("Theme change lost the settings scroll position.");
        }

        // The preference page now uses the full viewport; no global save action.
        window.Width = 720; window.Height = 500;
        window.Measure(new Size(720, 500));
        window.Arrange(new Rect(0, 0, 720, 500));
        window.UpdateLayout();
        var page = (FrameworkElement)window.FindName("SettingsPage");
        if (window.FindName("SaveSettingsButton") is not null || window.FindName("SettingsSaveBar") is not null || scroll.ViewportHeight < 100)
            throw new Exception("Redundant save action remains or settings viewport is clipped at 720x500.");
        Console.WriteLine("PASS settings UI: six automatically saved options, IT/EN frequency, saved preferences, theme scroll position and full viewport at 720x500.");
    }
}
