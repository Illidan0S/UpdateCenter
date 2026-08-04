using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

namespace UpdateCenter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _hardwareTimer;
    private readonly DispatcherTimer _historyHoverTimer;
    private readonly DispatcherTimer _scheduledScanTimer;
    private readonly DispatcherTimer _networkStatusTimer;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private HistoryEntry? _pendingHistoryEntry;
    private FrameworkElement? _pendingHistoryElement;
    private bool _appUpdateDialogOpen;
    private bool _scheduledScanRunning;
    private bool _driverRepairInProgress;
    private UpdateProgressWindow? _activeProgressWindow;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.1.0"}";
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _hardwareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hardwareTimer.Tick += async (_, _) => await _viewModel.RefreshHardwareMetricsAsync();
        _historyHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _historyHoverTimer.Tick += HistoryHoverTimer_Tick;
        _scheduledScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _scheduledScanTimer.Tick += ScheduledScanTimer_Tick;
        _networkStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _networkStatusTimer.Tick += async (_, _) => await _viewModel.Network.RefreshNetworkPageStatusAsync();
        HistoryDetailPopup.CustomPopupPlacementCallback = PlaceHistoryDetailPopup;
        StateChanged += (_, _) => WindowBorder.CornerRadius = WindowState == WindowState.Maximized
            ? new CornerRadius(0)
            : new CornerRadius(22);
        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;
        Closed += (_, _) =>
        {
            _hardwareTimer.Stop();
            _historyHoverTimer.Stop();
            _scheduledScanTimer.Stop();
            _networkStatusTimer.Stop();
            _viewModel.Network.Dispose();
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        };
        SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessageHook);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ShowPage(HomePage, "Home");
        AboutVersionText.Text = VersionText.Text;
        UpdateThemeChoices();
        UpdateFontSizeChoices();
        UpdateLanguageChoices();
        ApplyResponsiveLayout();
        LocalizationService.ApplyTo(this);
        InitializeNotificationIcon();
        _scheduledScanTimer.Start();
        _ = CheckForAppUpdatesAsync(false);
        _ = _viewModel.EnsureQuickHardwareDataAsync();
        if (_viewModel.Settings.ScanAtStartup || _viewModel.IsScheduledScanDue)
            await RunScanAsync(navigateToResults: false);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage, "Home");
    private void UpdatesNav_Click(object sender, RoutedEventArgs e) => ShowPage(UpdatesPage, "Aggiornamenti");
    private void OpenUpdates_Click(object sender, RoutedEventArgs e) => ShowPage(UpdatesPage, "Aggiornamenti");
    private async void SystemInfoNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(SystemInfoPage, "Hardware");
        await _viewModel.LoadHardwareOverviewAsync();
    }

    private void HardwareNav_Click(object sender, RoutedEventArgs e) => ShowPage(HardwarePage, "Driver e chipset");
    private void HistoryNav_Click(object sender, RoutedEventArgs e) => ShowPage(HistoryPage, "Cronologia");
    private async void NetworkNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NetworkPage, "Gestione rete");
        await _viewModel.Network.RefreshNetworkPageStatusAsync();
    }
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, "Impostazioni");

    private async void CheckForAppUpdates_Click(object sender, RoutedEventArgs e) =>
        await CheckForAppUpdatesAsync(true);

    private async Task CheckForAppUpdatesAsync(bool manual)
    {
        if (_appUpdateDialogOpen) return;
        var update = await _viewModel.CheckForAppUpdateAsync(manual);
        if (update is null || !IsVisible) return;

        _appUpdateDialogOpen = true;
        try
        {
            var window = new AppUpdateWindow(update, _viewModel.AppUpdateService) { Owner = this };
            window.ShowDialog();
            if (window.IgnoreRequested)
                _viewModel.IgnoreAppUpdate(update.AvailableVersion);
        }
        finally
        {
            _appUpdateDialogOpen = false;
        }
    }

    private void ShowPage(UIElement page, string title)
    {
        HomePage.Visibility = Visibility.Collapsed;
        UpdatesPage.Visibility = Visibility.Collapsed;
        SystemInfoPage.Visibility = Visibility.Collapsed;
        HardwarePage.Visibility = Visibility.Collapsed;
        HistoryPage.Visibility = Visibility.Collapsed;
        NetworkPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        PageTitle.Text = LocalizationService.Translate(title);
        if (ReferenceEquals(page, SystemInfoPage))
            _hardwareTimer.Start();
        else
            _hardwareTimer.Stop();
        if (ReferenceEquals(page, NetworkPage))
            _networkStatusTimer.Start();
        else
            _networkStatusTimer.Stop();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync(navigateToResults: true);
    }

    private async Task RunScanAsync(bool navigateToResults)
    {
        if (_viewModel.IsBusy) return;
        ShowPage(HomePage, "Home");
        await _viewModel.ScanAsync();
        ShowUpdatesNotification();
        if (navigateToResults && _viewModel.AvailableCount > 0)
            ShowPage(UpdatesPage, "Aggiornamenti");
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e) => _viewModel.CancelScan();

    private async void NetworkDiscover_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.Network.DiscoverAsync();
    }

    private async void NetworkSelectForPair_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NetworkAgentItem agent }) return;
        if (agent.ConnectionRequestsEnabled)
        {
            await _viewModel.Network.RequestConnectionsAsync(agent);
            return;
        }
        ShowNetworkPairingDialog(agent);
    }

    private async void NetworkRequestConnections_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Network.RequestConnectionsAsync();

    private void ShowNetworkPairingDialog(NetworkAgentItem agent)
    {
        var dialog = new NetworkPairingWindow(_viewModel.Network, agent) { Owner = this };
        dialog.ShowDialog();
    }

    private async void NetworkPair_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Network.PairAsync();

    private async void NetworkStatus_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Network.RefreshStatusAsync();

    private async void NetworkScan_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Network.StartScanAsync();

    private async void NetworkScanSelected_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Network.StartSelectedScansAsync();

    private async void NetworkUpdateCurrent_Click(object sender, RoutedEventArgs e)
    {
        var agent = _viewModel.Network.SelectedAgent;
        if (agent is null) return;
        var selected = _viewModel.Network.GetSelectedUpdatesForAgent(agent);
        await ConfirmAndStartRemoteUpdatesAsync(selected, [agent], _viewModel.Network.StartUpdateCurrentAsync);
    }

    private async void NetworkUpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.Network.GetSelectedUpdatesForConfirmation();
        if (selected.Count == 0) return;
        await ConfirmAndStartRemoteUpdatesAsync(
            selected,
            _viewModel.Network.GetAgentsForUpdates(selected),
            _viewModel.Network.StartUpdatesSelectedAsync);
    }

    private async Task ConfirmAndStartRemoteUpdatesAsync(
        IReadOnlyList<RemoteUpdateSelectionItem> selected,
        IReadOnlyList<NetworkAgentItem> agents,
        Func<Task> startUpdates)
    {
        if (selected.Count == 0) return;
        var confirmation = new UpdateConfirmationWindow(selected, agents)
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true) return;

        _viewModel.Network.ApplyRiskDecision(selected, includeRiskyUpdates: !confirmation.ExcludeRiskyItems);
        if (!selected.Any(x => x.IsSelected && x.CanInstall))
        {
            MessageBox.Show(
                "Dopo aver escluso gli aggiornamenti con rimozione preventiva non rimangono elementi da installare.",
                "Gestione rete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        await startUpdates();
    }

    private void NetworkSelectVisibleUpdates_Click(object sender, RoutedEventArgs e) =>
        _viewModel.Network.SelectVisibleUpdates();

    private void NetworkDeselectVisibleUpdates_Click(object sender, RoutedEventArgs e) =>
        _viewModel.Network.DeselectVisibleUpdates();

    private async void NetworkCancelCurrent_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Network.CancelCurrentAsync();

    private void ConfigureThisPc_Click(object sender, RoutedEventArgs e)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            MessageBox.Show("Impossibile individuare l'eseguibile di Update Center.", "Gestione rete");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--agent-setup");
            Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // L'utente ha annullato la richiesta UAC.
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile aprire la configurazione del componente di rete:\n\n{ex.Message}",
                "Gestione rete", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllSelected(true);
    private void DeselectAll_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllSelected(false);

    private async void InstallSelected_Click(object sender, RoutedEventArgs e) =>
        await RunUpdatesAsync(_viewModel.SelectedItems);

    private async void RetryUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UpdateItem item })
            await RunUpdatesAsync([item]);
    }

    private async Task RunUpdatesAsync(IReadOnlyList<UpdateItem> items)
    {
        if (items.Count == 0 || _viewModel.IsBusy) return;

        var preflight = PreflightService.Check(items);
        var restorePointWillBeCreated = PreflightService.ShouldCreateRestorePoint(items, _viewModel.Settings);
        var confirmation = new UpdateConfirmationWindow(
            items,
            preflight,
            _viewModel.Settings.CreateRestorePoint,
            restorePointWillBeCreated)
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true) return;

        var progressWindow = new UpdateProgressWindow(_viewModel) { Owner = this };
        _activeProgressWindow = progressWindow;
        progressWindow.PresentationChanged += (_, _) => UpdateProgressRecallButton();
        progressWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeProgressWindow, progressWindow))
                _activeProgressWindow = null;
            UpdateProgressRecallButton();
        };
        progressWindow.Show();
        progressWindow.Activate();
        UpdateProgressRecallButton();

        var result = await _viewModel.InstallItemsAsync(items);
        if (result is null)
        {
            progressWindow.ShowFailure(_viewModel.StatusText, _viewModel.CurrentItemText);
            UpdateProgressRecallButton();
            return;
        }

        progressWindow.ShowCompleted(result);
        UpdateProgressRecallButton();
    }

    private void ShowProgress_Click(object sender, RoutedEventArgs e) => _activeProgressWindow?.BringToFront();

    private void UpdateProgressRecallButton()
    {
        ShowProgressButton.Visibility = _activeProgressWindow is { IsOperationInProgress: true, IsHiddenOrMinimized: true }
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSettings();
        ThemeService.Apply(_viewModel.Settings.ThemeMode);
        TypographyService.Apply(_viewModel.Settings.FontSizeMode);
        if (_notifyIcon is not null)
            _notifyIcon.Visible = _viewModel.Settings.NotifyWhenUpdatesAreAvailable;
        ApplyResponsiveLayout();
        MessageBox.Show(LocalizationService.Text("Impostazioni salvate.", "Settings saved."),
            "Update Center", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ThemeChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string mode }) return;
        _viewModel.Settings.ThemeMode = ThemeService.Normalize(mode);
        ThemeService.Apply(_viewModel.Settings.ThemeMode);
        _viewModel.SaveSettings();
        UpdateThemeChoices();
    }

    private void UpdateThemeChoices()
    {
        var mode = ThemeService.Normalize(_viewModel.Settings.ThemeMode);
        SystemThemeChoice.IsChecked = mode == "Sistema";
        LightThemeChoice.IsChecked = mode == "Chiaro";
        DarkThemeChoice.IsChecked = mode == "Scuro";
    }

    private void FontSizeChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string mode }) return;
        _viewModel.Settings.FontSizeMode = TypographyService.Normalize(mode);
        TypographyService.Apply(_viewModel.Settings.FontSizeMode);
        _viewModel.SaveSettings();
        UpdateFontSizeChoices();
        ApplyResponsiveLayout();
    }

    private void AutomaticScanInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.AddedItems.Count == 0) return;
        _viewModel.SaveSettings();
    }

    private void UpdateFontSizeChoices()
    {
        var mode = TypographyService.Normalize(_viewModel.Settings.FontSizeMode);
        SmallFontChoice.IsChecked = mode == "Piccola";
        MediumFontChoice.IsChecked = mode == "Media";
        LargeFontChoice.IsChecked = mode == "Grande";
    }

    private void LanguageChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string language }) return;
        _viewModel.Settings.LanguageMode = LocalizationService.Normalize(language);
        LocalizationService.Initialize(_viewModel.Settings.LanguageMode);
        _viewModel.SaveSettings();
        LocalizationService.ApplyTo(this);
        ApplyResponsiveLayout();
        UpdateLanguageChoices();
        _viewModel.NotifyLanguageChanged();
    }

    private void UpdateLanguageChoices()
    {
        var language = LocalizationService.Normalize(_viewModel.Settings.LanguageMode);
        ItalianLanguageChoice.IsChecked = language == "it";
        EnglishLanguageChoice.IsChecked = language == "en";
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (!IsInitialized) return;
        var iconOnly = ActualWidth < 900;
        var compact = ActualWidth < 1120;
        var narrow = ActualWidth < 1000;
        var shortDriverLayout = ActualHeight < 680;
        var stackedUpdatesFooter = ActualWidth < 1120;
        var stackedNetworkActions = ActualWidth < 860;
        var stackedNetworkPageHeader = ActualWidth < 1320;
        var stackedNetworkDeviceHeader = ActualWidth < 1280;
        var ultraCompactNetwork = ActualWidth < 900;
        var stackedNetworkResultTools = !ultraCompactNetwork && ActualWidth < 1500;
        var sidebarWidth = iconOnly ? 76d : narrow ? 205d : compact ? 230d : 260d;
        SidebarColumn.Width = new GridLength(sidebarWidth);
        TitleSidebarColumn.Width = new GridLength(sidebarWidth);
        ContentHost.Margin = iconOnly ? new Thickness(8, 0, 8, 8) :
            narrow ? new Thickness(10, 0, 10, 10) : new Thickness(18, 0, 18, 18);
        HomeStatusColumn.Width = new GridLength(iconOnly ? 210d : narrow ? 220d : compact ? 255d : 300d);
        NetworkDevicesColumn.Width = new GridLength(1, GridUnitType.Star);
        NetworkTopCardsColumnGap.Width = new GridLength(0);
        NetworkPairingColumn.Width = new GridLength(0);
        NetworkTopCardsRowGap.Height = new GridLength(0);
        NetworkTopCardsSecondRow.Height = new GridLength(0);
        Grid.SetRow(NetworkDevicesCard, 0);
        Grid.SetColumn(NetworkDevicesCard, 0);
        Grid.SetColumnSpan(NetworkDevicesCard, 3);
        NetworkDevicesCard.MaxHeight = double.PositiveInfinity;

        NetworkDeviceHeaderActionsRow.Height = stackedNetworkDeviceHeader ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(NetworkDeviceHeaderActions, stackedNetworkDeviceHeader ? 1 : 0);
        Grid.SetColumn(NetworkDeviceHeaderActions, stackedNetworkDeviceHeader ? 0 : 1);
        Grid.SetColumnSpan(NetworkDeviceHeaderActions, stackedNetworkDeviceHeader ? 2 : 1);
        NetworkDeviceHeaderActions.Margin = stackedNetworkDeviceHeader
            ? new Thickness(0, 10, 0, 0)
            : new Thickness(0);

        NetworkPageHeaderActionsRow.Height = stackedNetworkPageHeader ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(NetworkPageHeaderActions, stackedNetworkPageHeader ? 1 : 0);
        Grid.SetColumn(NetworkPageHeaderActions, stackedNetworkPageHeader ? 0 : 1);
        Grid.SetColumnSpan(NetworkPageHeaderActions, stackedNetworkPageHeader ? 2 : 1);
        Grid.SetColumnSpan(NetworkPageHeaderTitle, stackedNetworkPageHeader ? 2 : 1);
        NetworkPageHeaderActions.Margin = stackedNetworkPageHeader
            ? new Thickness(0, 10, 0, 0)
            : new Thickness(0);
        NetworkPageHeaderActions.HorizontalAlignment = stackedNetworkPageHeader
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;

        NetworkActionButtonsRow.Height = stackedNetworkActions ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(NetworkActionButtons, stackedNetworkActions ? 1 : 0);
        Grid.SetColumn(NetworkActionButtons, stackedNetworkActions ? 0 : 1);
        Grid.SetColumnSpan(NetworkActionButtons, stackedNetworkActions ? 2 : 1);
        NetworkActionButtons.Margin = stackedNetworkActions ? new Thickness(0, 9, 0, 0) : new Thickness(0);

        NetworkResultsToolsRow.Height = stackedNetworkResultTools ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(NetworkResultsTools, stackedNetworkResultTools ? 1 : 0);
        Grid.SetColumn(NetworkResultsTools, stackedNetworkResultTools ? 0 : 1);
        Grid.SetColumnSpan(NetworkResultsTools, stackedNetworkResultTools ? 2 : 1);
        NetworkResultsTools.Margin = stackedNetworkResultTools ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        NetworkActionHint.Visibility = ActualWidth < 860 ? Visibility.Collapsed : Visibility.Visible;
        NetworkStatusBar.Visibility = ActualHeight < 740 ? Visibility.Collapsed : Visibility.Visible;

        NetworkResultNameColumn.MinWidth = ActualWidth < 800 ? 150 : 170;
        NetworkVersionsColumn.MinWidth = ActualWidth < 800 ? 180 : 200;
        UpdateFilterColumn.Width = new GridLength(iconOnly ? 130d : narrow ? 145d : 165d);
        DriverFilterColumn.Width = new GridLength(iconOnly ? 145d : narrow ? 160d : 190d);
        UpdatesFooterSecondRow.Height = stackedUpdatesFooter ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(UpdatesFooterRight, stackedUpdatesFooter ? 1 : 0);
        Grid.SetColumn(UpdatesFooterRight, stackedUpdatesFooter ? 0 : 2);
        Grid.SetColumnSpan(UpdatesFooterRight, stackedUpdatesFooter ? 3 : 1);
        UpdatesFooterRight.Margin = stackedUpdatesFooter ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        VisibleUpdatesText.Visibility = stackedUpdatesFooter ? Visibility.Collapsed : Visibility.Visible;

        SidebarHeading.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        SidebarSourceCard.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        AppNameText.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        VersionBadge.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        HistoryHintBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NetworkPreviewBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DriverSummaryPanel.Visibility = compact || shortDriverLayout ? Visibility.Collapsed : Visibility.Visible;
        DriverMachineName.Visibility = shortDriverLayout ? Visibility.Collapsed : Visibility.Visible;
        DriverSourceDescription.Visibility = shortDriverLayout ? Visibility.Collapsed : Visibility.Visible;
        DriverHeaderCard.Padding = shortDriverLayout ? new Thickness(14) : new Thickness(18);
        HomeStatusCard.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        HomeStatusColumn.Width = new GridLength(iconOnly ? 0d : narrow ? 220d : compact ? 255d : 300d);
        Grid.SetColumnSpan(HomeHeroContent, iconOnly ? 2 : 1);
        BrandPanel.HorizontalAlignment = iconOnly ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        BrandPanel.Margin = iconOnly ? new Thickness(0) : new Thickness(20, 0, 0, 0);

        SetNavigationAppearance(HomeNav, iconOnly ? "⌂" : $"⌂   {LocalizationService.Translate("Home")}", iconOnly);
        SetNavigationAppearance(UpdatesNav, iconOnly ? "↓" : $"↓   {LocalizationService.Translate("Aggiornamenti")}", iconOnly);
        SetNavigationAppearance(SystemInfoNav, iconOnly ? "▤" : $"▤   {LocalizationService.Translate("Hardware")}", iconOnly);
        SetNavigationAppearance(HardwareNav, iconOnly ? "▣" : $"▣   {LocalizationService.Translate("Driver e chipset")}", iconOnly);
        SetNavigationAppearance(HistoryNav, iconOnly ? "◷" : $"◷   {LocalizationService.Translate("Cronologia")}", iconOnly);
        SetNavigationAppearance(NetworkNav, iconOnly ? "⌘" : $"⌘   {LocalizationService.Translate("Gestione rete")}", iconOnly);
        SetNavigationAppearance(SettingsNav, iconOnly ? "⚙" : $"⚙   {LocalizationService.Translate("Impostazioni")}", iconOnly);
    }

    private static void SetNavigationAppearance(System.Windows.Controls.Button button, string content, bool centered)
    {
        button.Content = content;
        button.HorizontalContentAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        button.Padding = centered ? new Thickness(10, 12, 10, 12) : new Thickness(16, 12, 16, 12);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmNcHitTest = 0x0084;
        const int wmSettingChange = 0x001A;
        const int wmThemeChanged = 0x031A;
        if (msg == wmNcHitTest && WindowState == WindowState.Normal && ResizeMode == ResizeMode.CanResize)
        {
            var hit = HitTestResizeBorder(hwnd);
            if (hit != 0)
            {
                handled = true;
                return new IntPtr(hit);
            }
        }
        if ((msg == wmSettingChange || msg == wmThemeChanged) &&
            ThemeService.Normalize(_viewModel.Settings.ThemeMode) == "Sistema")
        {
            Dispatcher.BeginInvoke(new Action(() => ThemeService.Apply("Sistema")));
        }
        return IntPtr.Zero;
    }

    private static int HitTestResizeBorder(IntPtr hwnd)
    {
        if (!GetCursorPos(out var cursor) || !GetWindowRect(hwnd, out var window)) return 0;
        var dpi = Math.Max(GetDpiForWindow(hwnd), 96u);
        var border = Math.Max(7, (int)Math.Ceiling(8 * dpi / 96d));
        var left = cursor.X >= window.Left && cursor.X < window.Left + border;
        var right = cursor.X <= window.Right && cursor.X > window.Right - border;
        var top = cursor.Y >= window.Top && cursor.Y < window.Top + border;
        var bottom = cursor.Y <= window.Bottom && cursor.Y > window.Bottom - border;

        if (top && left) return 13;     // HTTOPLEFT
        if (top && right) return 14;    // HTTOPRIGHT
        if (bottom && left) return 16;  // HTBOTTOMLEFT
        if (bottom && right) return 17; // HTBOTTOMRIGHT
        if (left) return 10;            // HTLEFT
        if (right) return 11;           // HTRIGHT
        if (top) return 12;             // HTTOP
        if (bottom) return 15;          // HTBOTTOM
        return 0;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.LogsDirectory) { UseShellExecute = true });
    }

    private void DriverInventoryGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => ScrollHardwarePage(e);

    private void DriverVendorScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => ScrollHardwarePage(e);

    private void StorageHealthCard_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => ScrollPage(SystemInfoPage, e);

    private void ScrollHardwarePage(MouseWheelEventArgs e) => ScrollPage(HardwarePage, e);

    private static void ScrollPage(ScrollViewer page, MouseWheelEventArgs e)
    {
        if (page.Visibility != Visibility.Visible || page.ScrollableHeight <= 0) return;
        page.ScrollToVerticalOffset(page.VerticalOffset - (e.Delta / 3d));
        e.Handled = true;
    }

    private async void RepairDriver_Click(object sender, RoutedEventArgs e)
    {
        if (_driverRepairInProgress || sender is not Button { Tag: DriverProblemItem problem } repairButton ||
            !problem.CanManageDriverProblem)
            return;

        if (!problem.CanRepairWithInstalledDriver)
        {
            await RunScanFromProblemAsync(problem);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Update Center riapplicherà il pacchetto driver già registrato e scelto da Windows per:\n\n" +
            $"{problem.DeviceName}\n{problem.InstalledInfName}\n\n" +
            "Il dispositivo verrà riavviato e controllato nuovamente. Il pacchetto non verrà eliminato dal sistema. Continuare?",
            "Riparazione driver con Windows",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        _driverRepairInProgress = true;
        repairButton.Content = "Riparazione in corso…";
        repairButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var result = await DriverRepairService.RunElevatedAsync(
                problem.DeviceId,
                problem.DeviceName,
                problem.InstalledInfName);
            MessageBox.Show(
                this,
                result.Message,
                result.Success ? "Driver riparato" : "Driver ancora da controllare",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await _viewModel.RefreshDriverDiagnosticsAsync();
        }
        catch (OperationCanceledException)
        {
            // L'utente ha annullato la richiesta amministratore.
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Riparazione non riuscita:\n\n{ex.Message}",
                "Riparazione driver",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            repairButton.ClearValue(ContentControl.ContentProperty);
            repairButton.ClearValue(UIElement.IsEnabledProperty);
            _driverRepairInProgress = false;
        }
    }

    private async Task RunScanFromProblemAsync(DriverProblemItem problem)
    {
        if (_viewModel.IsBusy) return;
        MessageBox.Show(
            $"Update Center cercherà un driver compatibile e verificato per {problem.DeviceName} tramite Windows Update e il catalogo ufficiale dei produttori.",
            "Ricerca driver",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        ShowPage(HomePage, "Home");
        await _viewModel.ScanAsync();
        ShowUpdatesNotification();
        ShowPage(UpdatesPage, "Aggiornamenti");
    }

    private async void ScheduledScanTimer_Tick(object? sender, EventArgs e)
    {
        if (_scheduledScanRunning || _viewModel.IsBusy || !_viewModel.IsScheduledScanDue) return;
        _scheduledScanRunning = true;
        try
        {
            await RunScanAsync(navigateToResults: false);
        }
        finally
        {
            _scheduledScanRunning = false;
        }
    }

    private void InitializeNotificationIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            var icon = string.IsNullOrWhiteSpace(executable)
                ? null
                : System.Drawing.Icon.ExtractAssociatedIcon(executable);
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon ?? System.Drawing.SystemIcons.Information,
                Text = "Update Center",
                Visible = _viewModel.Settings.NotifyWhenUpdatesAreAvailable
            };
            _notifyIcon.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();
                Activate();
                ShowPage(UpdatesPage, "Aggiornamenti");
            }));
        }
        catch (Exception ex)
        {
            LogService.Write("Icona notifiche non disponibile.", ex);
        }
    }

    private void ShowUpdatesNotification()
    {
        if (!_viewModel.Settings.NotifyWhenUpdatesAreAvailable ||
            _viewModel.AvailableCount == 0 || _notifyIcon is null)
            return;

        _notifyIcon.BalloonTipTitle = "Update Center";
        _notifyIcon.BalloonTipText = LocalizationService.IsEnglish
            ? $"{_viewModel.AvailableCount} updates are ready to review."
            : $"{_viewModel.AvailableCount} aggiornamenti disponibili da controllare.";
        _notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(8000);
    }

    private void OpenSystemProtection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("systempropertiesprotection.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Protezione sistema non aperta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenVendorSupport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string target } element)
            return;

        if (element.DataContext is VendorSupportItem { IsInstalledApplication: true } support)
        {
            if (!target.Equals(support.ApplicationPath, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(target) ||
                !Path.GetFileName(target).Equals("NVIDIA App.exe", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(target).Contains(
                    $"{Path.DirectorySeparatorChar}NVIDIA Corporation{Path.DirectorySeparatorChar}NVIDIA app{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                return;
        }
        else if (!(target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Collegamento non aperto", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenTaskManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Gestione attività non aperta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CopyHardwareInfo_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var originalContent = button?.Content;
        try
        {
            if (button is not null)
            {
                button.IsEnabled = false;
                button.Content = "Raccolta informazioni…";
            }
            _viewModel.HardwareInfo.MonitoringStatus = "Raccolta delle informazioni hardware locali…";
            await _viewModel.EnsureQuickHardwareDataAsync();
            Clipboard.SetText(HardwareClipboardService.Build(
                _viewModel.HardwareInfo,
                _viewModel.DriverInventory,
                _viewModel.StorageDevices));
            _viewModel.HardwareInfo.MonitoringStatus = "Riepilogo hardware copiato negli appunti.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copia non riuscita", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (button is not null)
            {
                button.Content = originalContent;
                button.IsEnabled = true;
            }
        }
    }

    private void HistoryDetail_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryEntry entry } element) return;
        ArmHistoryDetail(element, entry);
    }

    private void HistoryDetail_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (HistoryDetailPopup.IsOpen) return;
        _historyHoverTimer.Stop();
        _pendingHistoryEntry = null;
        _pendingHistoryElement = null;
    }

    private void HistoryDetail_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryEntry entry } element) return;
        _historyHoverTimer.Stop();
        _pendingHistoryEntry = entry;
        _pendingHistoryElement = element;
        if (HistoryDetailPopup.IsOpen)
            HistoryDetailPopup.IsOpen = false;
    }

    private void HistoryDetail_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryEntry entry } element || !element.IsMouseOver) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (element.IsMouseOver)
                ArmHistoryDetail(element, entry);
        }));
    }

    private void ArmHistoryDetail(FrameworkElement element, HistoryEntry entry)
    {
        _pendingHistoryEntry = entry;
        _pendingHistoryElement = element;
        _historyHoverTimer.Stop();
        _historyHoverTimer.Interval = TimeSpan.FromSeconds(1);
        if (!HistoryDetailPopup.IsOpen)
            _historyHoverTimer.Start();
    }

    private void HistoryHoverTimer_Tick(object? sender, EventArgs e)
    {
        _historyHoverTimer.Stop();
        if (_pendingHistoryEntry is null || _pendingHistoryElement is null) return;
        if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed)
        {
            _historyHoverTimer.Interval = TimeSpan.FromMilliseconds(150);
            _historyHoverTimer.Start();
            return;
        }

        HistoryDetailTitle.Text = _pendingHistoryEntry.Name;
        var readableDetails = string.IsNullOrWhiteSpace(_pendingHistoryEntry.Details)
            ? "Nessun dettaglio disponibile per questa operazione."
            : _pendingHistoryEntry.Details;
        HistoryDetailText.Text = string.IsNullOrWhiteSpace(_pendingHistoryEntry.Diagnostics)
            ? readableDetails
            : $"{readableDetails}\n\n--- Diagnostica tecnica ---\n{_pendingHistoryEntry.Diagnostics}";
        HistoryDetailStatus.Text = "";
        HistoryDetailPopup.PlacementTarget = _pendingHistoryElement;
        HistoryDetailPopup.IsOpen = true;
    }

    private void CopyHistoryDetail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(HistoryDetailText.Text);
            HistoryDetailStatus.Text = "Dettaglio copiato negli appunti.";
        }
        catch (Exception ex)
        {
            HistoryDetailStatus.Text = "Copia non riuscita.";
            LogService.Write("Impossibile copiare un dettaglio della cronologia.", ex);
        }
    }

    private void CloseHistoryDetail_Click(object sender, RoutedEventArgs e) => HistoryDetailPopup.IsOpen = false;

    private void HistoryDetailPopup_Closed(object? sender, EventArgs e)
    {
        _historyHoverTimer.Stop();
        var entry = _pendingHistoryEntry;
        var element = _pendingHistoryElement;
        if (entry is not null && element is not null && element.IsMouseOver)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (element.IsMouseOver)
                    ArmHistoryDetail(element, entry);
            }));
            return;
        }
        _pendingHistoryEntry = null;
        _pendingHistoryElement = null;
    }

    private static CustomPopupPlacement[] PlaceHistoryDetailPopup(System.Windows.Size popupSize, System.Windows.Size targetSize, System.Windows.Point offset)
    {
        var horizontal = Math.Min(0, targetSize.Width - popupSize.Width);
        return
        [
            new CustomPopupPlacement(new System.Windows.Point(horizontal, targetSize.Height + 8), PopupPrimaryAxis.Vertical),
            new CustomPopupPlacement(new System.Windows.Point(horizontal, -popupSize.Height - 8), PopupPrimaryAxis.Vertical)
        ];
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Cancellare la cronologia visibile? I log tecnici resteranno disponibili.",
                "Cancella cronologia", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _viewModel.ClearHistory();
    }
}
