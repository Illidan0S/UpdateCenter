using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using UpdateCenter.Services;

namespace UpdateCenter;
public partial class MainWindow
{
    private bool _settingsSaveFailed;

    private void OrdinaryPreference_Click(object sender, RoutedEventArgs e)
    {
        // Click is raised after the user's toggle, not during initialization or
        // rebinding. Commit the binding explicitly before writing the snapshot.
        if (sender is CheckBox box) box.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
        SavePreferences();
    }
    private void RetrySettingsSave_Click(object sender, RoutedEventArgs e) => SavePreferences();

    private void SavePreferences()
    {
        try
        {
            _viewModel.SaveSettings();
            _settingsSaveFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _settingsSaveFailed = true;
            LogService.Write("Salvataggio automatico delle preferenze non riuscito.", ex);
        }
        if (_notifyIcon is not null) _notifyIcon.Visible = _viewModel.Settings.NotifyWhenUpdatesAreAvailable;
        RefreshSettingsSaveStatus();
    }
    private void RefreshSettingsSaveStatus()
    {
        SettingsSaveErrorPanel.Visibility = _settingsSaveFailed ? Visibility.Visible : Visibility.Collapsed;
        SettingsSaveErrorText.Text = LocalizationService.Text(
            "Impossibile salvare le preferenze. Le modifiche sono attive in questa sessione: riprova prima di chiudere.",
            "Preferences could not be saved. Changes are active in this session: retry before closing.");
    }
}
