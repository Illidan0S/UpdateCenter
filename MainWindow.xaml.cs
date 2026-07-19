using System.Diagnostics;
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
    private HistoryEntry? _pendingHistoryEntry;
    private FrameworkElement? _pendingHistoryElement;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _hardwareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hardwareTimer.Tick += async (_, _) => await _viewModel.RefreshHardwareMetricsAsync();
        _historyHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _historyHoverTimer.Tick += HistoryHoverTimer_Tick;
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
        };
        SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessageHook);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ShowPage(HomePage, "Home");
        UpdateThemeChoices();
        UpdateFontSizeChoices();
        ApplyResponsiveLayout();
        if (_viewModel.Settings.ScanAtStartup)
            await _viewModel.ScanAsync();
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
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, "Impostazioni");

    private void ShowPage(UIElement page, string title)
    {
        HomePage.Visibility = Visibility.Collapsed;
        UpdatesPage.Visibility = Visibility.Collapsed;
        SystemInfoPage.Visibility = Visibility.Collapsed;
        HardwarePage.Visibility = Visibility.Collapsed;
        HistoryPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        PageTitle.Text = title;
        if (ReferenceEquals(page, SystemInfoPage))
            _hardwareTimer.Start();
        else
            _hardwareTimer.Stop();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(HomePage, "Home");
        await _viewModel.ScanAsync();
        if (_viewModel.AvailableCount > 0)
            ShowPage(UpdatesPage, "Aggiornamenti");
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e) => _viewModel.CancelScan();
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

        var result = await _viewModel.InstallItemsAsync(items);
        if (result is null) return;

        var succeeded = result.Results.Count(x => x.Success);
        var failed = result.Results.Count - succeeded;
        var message = $"Aggiornamenti riusciti: {succeeded}\nAggiornamenti falliti: {failed}";
        if (result.RestorePointRequested && !result.RestorePointCreated)
            message += "\n\nIl punto di ripristino non è stato creato. Controlla Protezione sistema.";

        if (result.RestartRequired)
        {
            message += "\n\nWindows richiede un riavvio. Riavviare adesso?";
            var restart = MessageBox.Show(message, "Aggiornamento completato", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (restart == MessageBoxResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false }); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Riavvio non avviato", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        }
        else
        {
            MessageBox.Show(message, "Aggiornamento completato", MessageBoxButton.OK,
                failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSettings();
        ThemeService.Apply(_viewModel.Settings.ThemeMode);
        TypographyService.Apply(_viewModel.Settings.FontSizeMode);
        ApplyResponsiveLayout();
        MessageBox.Show("Impostazioni salvate.", "Update Center", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void UpdateFontSizeChoices()
    {
        var mode = TypographyService.Normalize(_viewModel.Settings.FontSizeMode);
        SmallFontChoice.IsChecked = mode == "Piccola";
        MediumFontChoice.IsChecked = mode == "Media";
        LargeFontChoice.IsChecked = mode == "Grande";
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (!IsInitialized) return;
        var iconOnly = ActualWidth < 820;
        var compact = ActualWidth < 1080;
        var narrow = ActualWidth < 920;
        var sidebarWidth = iconOnly ? 76d : narrow ? 180d : compact ? 200d : 230d;
        SidebarColumn.Width = new GridLength(sidebarWidth);
        TitleSidebarColumn.Width = new GridLength(sidebarWidth);
        ContentHost.Margin = iconOnly ? new Thickness(8, 0, 8, 8) :
            narrow ? new Thickness(10, 0, 10, 10) : new Thickness(18, 0, 18, 18);
        HomeStatusColumn.Width = new GridLength(iconOnly ? 210d : narrow ? 220d : compact ? 255d : 300d);
        UpdateFilterColumn.Width = new GridLength(iconOnly ? 135d : narrow ? 145d : 170d);

        SidebarHeading.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        SidebarSourceCard.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        AppNameText.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        VersionBadge.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        HistoryHintBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BrandPanel.HorizontalAlignment = iconOnly ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        BrandPanel.Margin = iconOnly ? new Thickness(0) : new Thickness(20, 0, 0, 0);

        SetNavigationAppearance(HomeNav, iconOnly ? "⌂" : "⌂   Home", iconOnly);
        SetNavigationAppearance(UpdatesNav, iconOnly ? "↓" : "↓   Aggiornamenti", iconOnly);
        SetNavigationAppearance(SystemInfoNav, iconOnly ? "▤" : "▤   Hardware", iconOnly);
        SetNavigationAppearance(HardwareNav, iconOnly ? "▣" : "▣   Driver e chipset", iconOnly);
        SetNavigationAppearance(HistoryNav, iconOnly ? "◷" : "◷   Cronologia", iconOnly);
        SetNavigationAppearance(SettingsNav, iconOnly ? "⚙" : "⚙   Impostazioni", iconOnly);
    }

    private static void SetNavigationAppearance(Button button, string content, bool centered)
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
        if (sender is not FrameworkElement { Tag: string target } ||
            !(target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)))
            return;

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

    private void CopyHardwareInfo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_viewModel.HardwareInfo.BuildClipboardText());
            _viewModel.HardwareInfo.MonitoringStatus = "Riepilogo hardware copiato negli appunti.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copia non riuscita", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HistoryDetail_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryEntry entry } element) return;
        ArmHistoryDetail(element, entry);
    }

    private void HistoryDetail_MouseLeave(object sender, MouseEventArgs e)
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
        HistoryDetailText.Text = string.IsNullOrWhiteSpace(_pendingHistoryEntry.Details)
            ? "Nessun dettaglio disponibile per questa operazione."
            : _pendingHistoryEntry.Details;
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

    private static CustomPopupPlacement[] PlaceHistoryDetailPopup(Size popupSize, Size targetSize, Point offset)
    {
        var horizontal = Math.Min(0, targetSize.Width - popupSize.Width);
        return
        [
            new CustomPopupPlacement(new Point(horizontal, targetSize.Height + 8), PopupPrimaryAxis.Vertical),
            new CustomPopupPlacement(new Point(horizontal, -popupSize.Height - 8), PopupPrimaryAxis.Vertical)
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
