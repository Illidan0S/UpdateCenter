using System.Diagnostics;
using System.ComponentModel;
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
    private readonly DispatcherTimer _scheduledScanTimer;
    private readonly DispatcherTimer _networkStatusTimer;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private FrameworkElement? _updateStatusAnchor;
    private bool _appUpdateDialogOpen;
    private bool _scheduledScanRunning;
    private bool _driverRepairInProgress;
    private UpdateProgressWindow? _activeProgressWindow;
    private string _currentPageTitle = "Home";
    private readonly Task? _updateStartup;

    public MainWindow() : this(null) { }

    internal MainWindow(Task? updateStartup)
    {
        _updateStartup = updateStartup;
        InitializeComponent();
        InitializeBlockedUpdatesGrid();
        new NativeCaptionBridge(this, () => MaximizeWindowButton).Attach();
        VersionText.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "2.0.0"}";
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        UpdateOperationsLabels();
        UpdateManagementLabels();
        _hardwareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hardwareTimer.Tick += async (_, _) => await _viewModel.RefreshHardwareMetricsAsync();
        InitializeDriverHistoryUi();
        _scheduledScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _scheduledScanTimer.Tick += ScheduledScanTimer_Tick;
        _networkStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _networkStatusTimer.Tick += async (_, _) => await _viewModel.Network.RefreshNetworkPageStatusAsync();
        HistoryDetailPopup.CustomPopupPlacementCallback = PlaceHistoryDetailPopup;
        UpdateStatusPopup.CustomPopupPlacementCallback = PlaceHistoryDetailPopup;
        UpdatesGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(UpdatesGrid_ScrollChanged));
        BlockedUpdatesGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(UpdatesGrid_ScrollChanged));
        UpdatesSectionsViewport.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(UpdatesGrid_ScrollChanged));
        _viewModel.UpdatesView.CollectionChanged += (_, _) => CloseUpdateStatus();
        _viewModel.ManagedUpdatesView.CollectionChanged += (_, _) => CloseUpdateStatus();
        _viewModel.BlockedUpdatesView.CollectionChanged += (_, _) => CloseUpdateStatus();
        LocationChanged += (_, _) => CloseUpdateStatus();
        SizeChanged += (_, _) => CloseUpdateStatus();
        Deactivated += (_, _) => CloseUpdateStatus();
        StateChanged += (_, _) =>
        {
            WindowBorder.CornerRadius = new CornerRadius(0);
            UpdateWindowControlLabels();
        };
        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;
        Closed += (_, _) =>
        {
            CloseUpdateStatus();
            _hardwareTimer.Stop();
            CloseHistoryDetail();
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
        UpdateNavigationLabels();
        InitializeNotificationIcon();
        if (_updateStartup is not null) await _updateStartup;
        _scheduledScanTimer.Start();
        _ = CheckForAppUpdatesAsync(false);
        _ = _viewModel.EnsureQuickHardwareDataAsync();
        _ = _viewModel.ResumePendingVerificationAsync();
        if (_viewModel.Settings.ScanAtStartup || _viewModel.IsScheduledScanDue)
            await RunScanAsync(navigateToResults: false);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

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
        CloseUpdateStatus();
        CloseHistoryDetail();
        HomePage.Visibility = Visibility.Collapsed;
        UpdatesPage.Visibility = Visibility.Collapsed;
        SystemInfoPage.Visibility = Visibility.Collapsed;
        HardwarePage.Visibility = Visibility.Collapsed;
        HistoryPage.Visibility = Visibility.Collapsed;
        NetworkPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        _currentPageTitle = title;
        UpdateNavigationLabels();
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
            await _viewModel.Network.RequestConnectionsAsync(RequestFingerprint, agent);
            return;
        }
        ShowNetworkPairingDialog(agent);
    }

    private async void NetworkRequestConnections_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Network.RequestConnectionsAsync(RequestFingerprint);

    private void ShowNetworkPairingDialog(NetworkAgentItem agent)
    {
        var dialog = new NetworkPairingWindow(_viewModel.Network, agent) { Owner = this };
        dialog.ShowDialog();
    }

    private async void NetworkPair_Click(object sender, RoutedEventArgs e)
    {
        var network = _viewModel.Network;
        var pin = RequestFingerprint($"{network.Address}:{network.Port}");
        if (pin is not null) await network.PairAsync(pin);
    }

    private string? RequestFingerprint(string endpoint) => AgentFingerprintWindow.Prompt(this, endpoint);

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
                LocalizationService.Text("Dopo aver escluso gli aggiornamenti con rimozione preventiva non rimangono elementi da installare.", "After excluding updates with prior removal, no items remain to install."),
                LocalizationService.Text("Gestione rete", "Network management"),
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
            MessageBox.Show(
                LocalizationService.Text("Impossibile individuare l'eseguibile di Update Center.", "Cannot locate the Update Center executable."),
                LocalizationService.Text("Gestione rete", "Network management"));
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
            MessageBox.Show(
                LocalizationService.Text($"Impossibile aprire la configurazione del componente di rete:\n\n{ex.Message}", $"Cannot open network component configuration:\n\n{ex.Message}"),
                LocalizationService.Text("Gestione rete", "Network management"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SelectAllWithConfirmation(item => MessageBox.Show(this,
            $"{item.Name}\n{item.InstalledVersion} → {item.AvailableVersion}\n{item.Source}\n\n" +
            item.PriorityDescription + "\n\n" +
            LocalizationService.Text(
                "Vuoi includere questo aggiornamento nella selezione? Il consenso all'installazione verrà richiesto prima dell'esecuzione.",
                "Include this update in the selection? Installation consent will be requested before execution."),
            LocalizationService.Text("Conferma selezione", "Confirm selection"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
    private void DeselectAll_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllSelected(false);

    private async void InstallSelected_Click(object sender, RoutedEventArgs e) =>
        await RunUpdatesAsync(_viewModel.SelectedItems);

    private void UpdateStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: UpdateItem item } anchor) return;
        CloseUpdateStatus();
        CloseHistoryDetail();
        _updateStatusAnchor = anchor;
        anchor.Unloaded += UpdateStatusAnchor_Unloaded;
        anchor.DataContextChanged += UpdateStatusAnchor_DataContextChanged;
        UpdateStatusContent.DataContext = item;
        UpdateStatusPopup.PlacementTarget = anchor;
        SizeDetailPopup(UpdateStatusContent, anchor);
        UpdateStatusPopup.IsOpen = true;
        e.Handled = true;
    }

    private void UpdateStatusPopup_Opened(object? sender, EventArgs e)
    {
        // Focusing a long TextBox scrolls its heading out of view. Keep the top visible;
        // Tab reaches the read-only text for keyboard selection/copy.
        UpdateStatusScrollViewer.ScrollToTop();
        UpdateStatusCloseButton.Focus();
    }

    private void UpdateStatusPopup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseUpdateStatus(restoreFocus: true);
            e.Handled = true;
            return;
        }
        // The read-only TextBox is measured by the outer viewer. Its own caret scrolling
        // cannot reveal clipped paragraphs; route document navigation to that viewer.
        // Keep Shift shortcuts available for selecting/copying text.
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
        switch (e.Key)
        {
            case Key.PageDown: UpdateStatusScrollViewer.PageDown(); break;
            case Key.PageUp: UpdateStatusScrollViewer.PageUp(); break;
            case Key.Home: UpdateStatusScrollViewer.ScrollToTop(); break;
            case Key.End: UpdateStatusScrollViewer.ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void CloseUpdateStatus_Click(object sender, RoutedEventArgs e) => CloseUpdateStatus(restoreFocus: true);

    private void CloseUpdateStatus(bool restoreFocus = false)
    {
        var anchor = _updateStatusAnchor;
        var item = UpdateStatusContent.DataContext;
        UpdateStatusPopup.IsOpen = false;
        if (restoreFocus && anchor is { IsVisible: true, IsEnabled: true } && ReferenceEquals(anchor.DataContext, item))
            anchor.Focus();
    }

    private void UpdateStatusPopup_Closed(object? sender, EventArgs e)
    {
        if (_updateStatusAnchor is { } anchor)
        {
            anchor.Unloaded -= UpdateStatusAnchor_Unloaded;
            anchor.DataContextChanged -= UpdateStatusAnchor_DataContextChanged;
        }
        _updateStatusAnchor = null;
        UpdateStatusContent.DataContext = null;
        UpdateStatusPopup.PlacementTarget = null;
    }

    private void UpdateStatusAnchor_Unloaded(object sender, RoutedEventArgs e) => CloseUpdateStatus();
    private void UpdateStatusAnchor_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => CloseUpdateStatus();

    private void UpdatesGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Popup content participates in the logical route, but scrolling its
        // own details does not move the row anchor in the page.
        if (ReferenceEquals(sender, UpdatesSectionsViewport) && !ReferenceEquals(e.OriginalSource, sender)) return;
        // A resized cell or recycled row must never retain a popup for the previous item.
        if (e.HorizontalChange != 0 || e.VerticalChange != 0 || e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
            CloseUpdateStatus();
    }

    private void InitializeBlockedUpdatesGrid()
    {
        // Share the existing cell templates, bindings and commands, not a second
        // set of update objects. WPF columns themselves can have only one owner.
        foreach (var source in UpdatesGrid.Columns)
        {
            DataGridColumn column = source switch
            {
                DataGridTemplateColumn template => new DataGridTemplateColumn { CellTemplate = template.CellTemplate },
                DataGridTextColumn text => new DataGridTextColumn { Binding = text.Binding, ElementStyle = text.ElementStyle },
                _ => throw new InvalidOperationException("Unsupported update column.")
            };
            column.Header = source.Header;
            column.SortMemberPath = source.SortMemberPath;
            column.CanUserSort = source.CanUserSort;
            column.Width = source.Width;
            column.MinWidth = source.MinWidth;
            column.MaxWidth = source.MaxWidth;
            column.IsReadOnly = true;
            BlockedUpdatesGrid.Columns.Add(column);
        }
    }

    private void BlockedUpdatesExpansion_Changed(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, BlockedUpdatesExpander)) return;
        CloseUpdateStatus();
        if (!BlockedUpdatesExpander.IsExpanded) return;

        // Search can expose the only matching section offscreen. A manual
        // expansion leaves the page where the user clicked the header.
        if (!_viewModel.ManagedUpdatesView.IsEmpty || string.IsNullOrWhiteSpace(_viewModel.SearchText))
        {
            // Replace a pending ScrollToEnd with its current pixel position.
            UpdatesSectionsViewport.ScrollToVerticalOffset(UpdatesSectionsViewport.VerticalOffset);
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (BlockedUpdatesExpander.IsExpanded && UpdatesSectionsViewport.Content is UIElement content)
                UpdatesSectionsViewport.ScrollToVerticalOffset(BlockedUpdatesExpander.TranslatePoint(new Point(), content).Y);
        }));
    }

    private async void RetryUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UpdateItem item })
            await RunUpdatesAsync([item]);
    }

    private async Task RunUpdatesAsync(IReadOnlyList<UpdateItem> items)
    {
        if (items.Count == 0 || _viewModel.IsBusy) return;

        var summaryHints = items.Select(item => new InstallationSummaryHint(item)).ToArray();
        var preflight = PreflightService.Check(items);
        if (!preflight.CanContinue)
        {
            _viewModel.ReportPreflightBlocked(preflight);
            return;
        }

        var progressWindow = new UpdateProgressWindow(_viewModel, items) { Owner = this };
        progressWindow.PresentationChanged += (_, _) => UpdateProgressRecallButton();
        progressWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeProgressWindow, progressWindow))
                _activeProgressWindow = null;
            UpdateProgressRecallButton();
        };
        var progressShown = false;

        var result = await _viewModel.InstallItemsAsync(
            items,
            new WpfWinGetProcessRecoveryPrompt(progressWindow),
            plans =>
            {
                if (!WpfRunnerConsent.HasInteractiveSession()) return false;
                var confirmation = new UpdateConfirmationWindow(plans, PreflightService.Check(items), summaryHints) { Owner = this };
                return confirmation.ShowDialog() == true && WpfRunnerConsent.HasInteractiveSession();
            },
            () =>
            {
                _activeProgressWindow = progressWindow;
                progressWindow.Show();
                progressWindow.Activate();
                progressShown = true;
                UpdateProgressRecallButton();
            });
        if (!progressShown)
        {
            progressWindow.Close();
            return;
        }
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

    private void ThemeChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string mode }) return;
        _viewModel.Settings.ThemeMode = ThemeService.Normalize(mode);
        ThemeService.Apply(_viewModel.Settings.ThemeMode);
        SavePreferences();
        UpdateThemeChoices();
    }

    private void UpdateThemeChoices()
    {
        UpdateAppearanceTheme();
        var mode = ThemeService.Normalize(_viewModel.Settings.ThemeMode);
        SystemThemeChoice.IsChecked = mode == "Sistema";
        LightThemeChoice.IsChecked = mode == "Chiaro";
        DarkThemeChoice.IsChecked = mode == "Scuro";
    }

    private void UpdateAppearanceTheme()
    {
        var scrollOffset = SettingsScrollViewer.VerticalOffset;
        UpdateSettingsCardTheme(SettingsPage, "Settings");
        UpdateSettingsCardTheme(HomeContent, "Settings");
        UpdateSettingsCardTheme(UpdatesPage, "Operations");
        UpdateSettingsCardTheme(HardwarePage, "Operations");
        UpdateSettingsCardTheme(HistoryPage, "Operations");
        UpdateSettingsCardTheme(SystemInfoContent, "HardwareNetwork");
        UpdateSettingsCardTheme(NetworkPage, "HardwareNetwork");
        UpdateSettingsCardTheme(AppearanceCard);
        UpdateSettingsCardTheme(LanguageCard);
        UpdateOperationsLabels();
        UpdateManagementLabels();
        // Replacing the Fluent ScrollViewer template resets its offset; retain the section in view.
        SettingsScrollViewer.UpdateLayout();
        SettingsScrollViewer.ScrollToVerticalOffset(scrollOffset);
    }

    internal static void UpdateSettingsCardTheme(FrameworkElement card, string dictionaryName = "Appearance")
    {
        // Keep Fluent local to the modernized content. Other pages retain their templates.
        var theme = ThemeService.IsDark ? "Fluent.Dark.xaml" : "Fluent.Light.xaml";
        var source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/{theme}");
        var currentAppearance = dictionaryName switch
        {
            "Dialogs" => card.Resources.MergedDictionaries[0].MergedDictionaries[0].MergedDictionaries[0].MergedDictionaries[0],
            "HardwareNetwork" => card.Resources.MergedDictionaries[0].MergedDictionaries[0].MergedDictionaries[0],
            "Operations" => card.Resources.MergedDictionaries[0].MergedDictionaries[0],
            "Settings" => card.Resources.MergedDictionaries[0],
            _ => card.Resources
        };
        if (currentAppearance.MergedDictionaries[0].Source == source) return;

        // Recreate the derived styles too: their BasedOn references retain the original
        // Fluent dictionary. DynamicResource on the controls picks up the new styles.
        var resources = new ResourceDictionary
        {
            Source = new Uri($"/UpdateCenter;component/Styles/{dictionaryName}.xaml", UriKind.Relative)
        };
        var appearanceResources = dictionaryName switch
        {
            "Dialogs" => resources.MergedDictionaries[0].MergedDictionaries[0].MergedDictionaries[0].MergedDictionaries[0],
            "HardwareNetwork" => resources.MergedDictionaries[0].MergedDictionaries[0].MergedDictionaries[0],
            "Operations" => resources.MergedDictionaries[0].MergedDictionaries[0],
            "Settings" => resources.MergedDictionaries[0],
            _ => resources
        };
        appearanceResources.MergedDictionaries[0] = new ResourceDictionary { Source = source };
        if (ThemeService.IsDark)
            appearanceResources["AppearanceSecondaryBrush"] = System.Windows.Application.Current.Resources["MutedTextBrush"];
        card.Resources = resources;
    }

    private void FontSizeChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string mode }) return;
        _viewModel.Settings.FontSizeMode = TypographyService.Normalize(mode);
        TypographyService.Apply(_viewModel.Settings.FontSizeMode);
        SavePreferences();
        UpdateFontSizeChoices();
        ApplyResponsiveLayout();
    }

    private void AutomaticScanInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.AddedItems.Count == 0) return;
        ScanFrequencyChoice.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateSource();
        SavePreferences();
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
        var normalized = LocalizationService.Normalize(language);
        if (_viewModel.Settings.LanguageMode == normalized && LocalizationService.CurrentLanguage == normalized)
        {
            UpdateLanguageChoices();
            return;
        }

        _viewModel.Settings.LanguageMode = normalized;
        LocalizationService.Initialize(normalized);
        SavePreferences();
        LocalizationService.ApplyTo(this);
        UpdateNavigationLabels();
        UpdateLanguageChoices();
        _viewModel.NotifyLanguageChanged();
    }

    private void UpdateLanguageChoices()
    {
        RefreshSettingsSaveStatus();
        UpdateOperationsLabels();
        UpdateManagementLabels();
        var language = LocalizationService.Normalize(_viewModel.Settings.LanguageMode);
        ItalianLanguageChoice.IsChecked = language == "it";
        EnglishLanguageChoice.IsChecked = language == "en";
    }

    private void UpdateOperationsLabels()
    {
        Resources["OperationsReleaseNotesText"] = LocalizationService.Text("Novità", "What’s new");
        Resources["OperationsReleaseNotesLabel"] = LocalizationService.Text("Novità della versione", "Release notes");
        // DataTemplate contents are deliberately excluded from the static localization cache.
        // Local dynamic resources update realized and recycled rows without walking the visual tree.
        (string Key, string Italian)[] labels =
        [
            ("OperationsOpenSourceText", "Apri fonte"), ("OperationsRetryText", "Riprova"),
            ("OperationsViewUpdatesText", "Vedi aggiornamenti"), ("OperationsRestartText", "Riavvio richiesto"),
            ("OperationsInstalledText", "Installata"), ("OperationsAvailableText", "Disponibile"),
            ("OperationsUpdatesText", "Aggiornamenti"), ("OperationsInventoryText", "Inventario driver installati"),
            ("OperationsProblemsText", "Diagnosi driver problematici"), ("OperationsHistoryText", "Cronologia"),
            ("OperationsSearchUpdatesText", "Cerca aggiornamenti"), ("OperationsSearchDriversText", "Cerca nei dispositivi"),
            ("OperationsUpdateFilterText", "Filtra gli aggiornamenti"), ("OperationsDriverFilterText", "Filtra i dispositivi"),
            ("OperationsProgressText", "Avanzamento"), ("OperationsVisibleDriversText", "Dispositivi visibili"),
            ("OperationsCloseText", "Chiudi"), ("OperationsDetailsText", "Dettaglio attività")
        ];
        foreach (var scope in new FrameworkElement[] { UpdatesPage, HardwarePage, HistoryPage })
            foreach (var (key, italian) in labels)
                scope.Resources[key] = LocalizationService.Translate(italian);
        HistoryPage.Resources["OperationsHistoryDetailsHintText"] = LocalizationService.Text("Dettagli: fai clic o premi Invio.", "Details: click or press Enter.");
        LocalizationService.ApplyTo(DriversContent);
        RefreshHistoryDetailText();
        UpdatesPage.Resources["OperationsStatusDetailsText"] = LocalizationService.Text("Dettagli dello stato", "Status details");
    }

    private void UpdateManagementLabels()
    {
        NetworkPage.Language = System.Windows.Markup.XmlLanguage.GetLanguage(LocalizationService.CurrentLanguage);
        foreach (var scope in new FrameworkElement[] { SystemInfoContent, NetworkPage })
        {
            scope.Resources["ManagementStorageText"] = LocalizationService.Translate("Salute dello storage");
            scope.Resources["ManagementDevicesText"] = LocalizationService.Translate("Dispositivi rilevati");
            scope.Resources["ManagementResultsText"] = LocalizationService.Translate("Aggiornamenti trovati");
            scope.Resources["ManagementScopeText"] = LocalizationService.Text("Filtra i risultati per dispositivo", "Filter results by device");
            scope.Resources["ManagementRiskText"] = LocalizationService.Text(" · richiede conferma", " · confirmation required");
            scope.Resources["ManagementInstalledText"] = LocalizationService.Text("Installata: ", "Installed: ");
            scope.Resources["ManagementAvailableText"] = LocalizationService.Text("Disponibile: ", "Available: ");
        }
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
        HardwareSummaryGap.Width = new GridLength(narrow ? 0 : 14);
        HardwareSummarySecondColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(HardwareSecondaryDetails, narrow ? 1 : 0);
        Grid.SetColumn(HardwareSecondaryDetails, narrow ? 0 : 2);
        foreach (var (details, action) in new[] { (HardwareHeaderText, HardwareHeaderButton), (HardwareCopyText, HardwareCopyButton) })
        {
            Grid.SetColumnSpan(details, compact ? 2 : 1);
            Grid.SetRow(action, compact ? 1 : 0);
            Grid.SetColumn(action, compact ? 0 : 1);
            Grid.SetColumnSpan(action, compact ? 2 : 1);
            action.HorizontalAlignment = HorizontalAlignment.Left;
            action.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(20, 0, 0, 0);
        }
        Grid.SetRow(NetworkResultsFooterActions, compact ? 1 : 0);
        Grid.SetColumn(NetworkResultsFooterActions, compact ? 0 : 2);
        Grid.SetColumnSpan(NetworkResultsFooterActions, compact ? 3 : 1);
        NetworkResultsFooterActions.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        NetworkVisibleResultsText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarColumn.Width = new GridLength(sidebarWidth);
        ContentHost.Margin = iconOnly ? new Thickness(8, 0, 8, 8) :
            narrow ? new Thickness(10, 0, 10, 10) : new Thickness(18, 0, 18, 18);
        var stackHomeAction = ActualWidth - sidebarWidth - ContentHost.Margin.Left - ContentHost.Margin.Right < 700;
        HomeLastScanActionsRow.Height = stackHomeAction ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(HomeViewUpdatesButton, stackHomeAction ? 1 : 0);
        Grid.SetColumn(HomeViewUpdatesButton, stackHomeAction ? 0 : 1);
        Grid.SetColumnSpan(HomeLastScanDetails, stackHomeAction ? 2 : 1);
        HomeViewUpdatesButton.HorizontalAlignment = HorizontalAlignment.Left;
        HomeViewUpdatesButton.Margin = stackHomeAction ? new Thickness(0, 12, 0, 0) : new Thickness(20, 0, 0, 0);
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
        NetworkStatusBar.Visibility = Visibility.Visible;

        NetworkResultNameColumn.MinWidth = 230;
        NetworkVersionsColumn.MinWidth = 210;
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
        HistoryHintBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NetworkPreviewBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DriverSummaryPanel.Visibility = compact || shortDriverLayout ? Visibility.Collapsed : Visibility.Visible;
        DriverMachineName.Visibility = shortDriverLayout ? Visibility.Collapsed : Visibility.Visible;
        DriverSourceDescription.Visibility = shortDriverLayout ? Visibility.Collapsed : Visibility.Visible;
        DriverHeaderCard.Padding = shortDriverLayout ? new Thickness(14) : new Thickness(18);

        UpdateNavigationLabels();
    }

    private void UpdateNavigationLabels()
    {
        // These values depend on the current page/width, not the first localization snapshot.
        PageTitle.Text = LocalizationService.Translate(_currentPageTitle);
        var iconOnly = ActualWidth < 900;
        SetNavigationAppearance(HomeNav, "Home", iconOnly);
        SetNavigationAppearance(UpdatesNav, "Aggiornamenti", iconOnly);
        SetNavigationAppearance(SystemInfoNav, "Hardware", iconOnly);
        SetNavigationAppearance(HardwareNav, "Driver e chipset", iconOnly);
        SetNavigationAppearance(HistoryNav, "Cronologia", iconOnly);
        SetNavigationAppearance(NetworkNav, "Gestione rete", iconOnly);
        SetNavigationAppearance(SettingsNav, "Impostazioni", iconOnly);
        UpdateWindowControlLabels();
    }

    private void SetNavigationAppearance(System.Windows.Controls.Button button, string title, bool centered)
    {
        var label = LocalizationService.Translate(title);
        button.Content = centered ? string.Empty : label;
        var selected = _currentPageTitle == title;
        button.Tag = selected;
        button.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        System.Windows.Automation.AutomationProperties.SetItemStatus(button,
            selected ? LocalizationService.Text("Pagina corrente", "Current page") : string.Empty);
        button.Padding = centered ? new Thickness(8, 10, 8, 10) : new Thickness(10, 10, 10, 10);
    }

    private void UpdateWindowControlLabels()
    {
        SetWindowControlLabel(MinimizeWindowButton, LocalizationService.Text("Riduci a icona", "Minimize"));
        SetWindowControlLabel(MaximizeWindowButton, WindowState == WindowState.Maximized
            ? LocalizationService.Text("Ripristina finestra", "Restore window")
            : LocalizationService.Text("Ingrandisci finestra", "Maximize window"));
        SetWindowControlLabel(CloseWindowButton, LocalizationService.Text("Chiudi", "Close"));
    }

    private static void SetWindowControlLabel(System.Windows.Controls.Button button, string label)
    {
        button.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmSettingChange = 0x001A;
        const int wmThemeChanged = 0x031A;
        if ((msg == wmSettingChange || msg == wmThemeChanged) &&
            ThemeService.Normalize(_viewModel.Settings.ThemeMode) == "Sistema")
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ThemeService.Apply("Sistema");
                UpdateAppearanceTheme();
            }));
        }
        return IntPtr.Zero;
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.LogsDirectory) { UseShellExecute = true });
    }

    private void StorageHealthCard_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => ScrollPage(SystemInfoPage, e);

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
            LocalizationService.Text(
                $"Update Center riapplicherà il pacchetto driver già registrato e scelto da Windows per:\n\n" +
                $"{problem.DeviceName}\n{problem.InstalledInfName}\n\n" +
                "Il dispositivo verrà riavviato e controllato nuovamente. Il pacchetto non verrà eliminato dal sistema. Continuare?",
                $"Update Center will reapply the driver package already registered and selected by Windows for:\n\n" +
                $"{problem.DeviceName}\n{problem.InstalledInfName}\n\n" +
                "The device will be restarted and checked again. The package will not be removed from the system. Continue?"),
            LocalizationService.Text("Riparazione driver con Windows", "Windows driver repair"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        _driverRepairInProgress = true;
        repairButton.Content = LocalizationService.Text("Riparazione in corso…", "Repairing…");
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
                LocalizationService.Translate(result.Message),
                result.Success
                    ? LocalizationService.Text("Driver riparato", "Driver repaired")
                    : LocalizationService.Text("Driver ancora da controllare", "Driver still needs checking"),
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
                LocalizationService.Text($"Riparazione non riuscita:\n\n{ex.Message}", $"Repair failed:\n\n{ex.Message}"),
                LocalizationService.Text("Riparazione driver", "Driver repair"),
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
            LocalizationService.Text(
                $"Update Center cercherà un driver compatibile e verificato per {problem.DeviceName} tramite Windows Update e il catalogo ufficiale dei produttori.",
                $"Update Center will search for a compatible and verified driver for {problem.DeviceName} via Windows Update and the official vendor catalog."),
            LocalizationService.Text("Ricerca driver", "Driver search"),
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
            MessageBox.Show(ex.Message, LocalizationService.Text("Protezione sistema non aperta", "System Protection not opened"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(ex.Message, LocalizationService.Text("Collegamento non aperto", "Link not opened"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(ex.Message, LocalizationService.Text("Gestione attività non aperta", "Task Manager not opened"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                button.Content = LocalizationService.Text("Raccolta informazioni…", "Gathering info…");
            }
            _viewModel.HardwareInfo.MonitoringStatus = LocalizationService.Text("Raccolta delle informazioni hardware locali…", "Gathering local hardware information…");
            await _viewModel.EnsureQuickHardwareDataAsync();
            Clipboard.SetText(HardwareClipboardService.Build(
                _viewModel.HardwareInfo,
                _viewModel.DriverInventory,
                _viewModel.StorageDevices));
            _viewModel.HardwareInfo.MonitoringStatus = LocalizationService.Text("Riepilogo hardware copiato negli appunti.", "Hardware summary copied to clipboard.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, LocalizationService.Text("Copia non riuscita", "Copy failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (MessageBox.Show(
                LocalizationService.Text("Cancellare la cronologia visibile? I log tecnici resteranno disponibili.", "Clear visible history? Technical logs will remain available."),
                LocalizationService.Text("Cancella cronologia", "Clear history"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _viewModel.ClearHistory();
    }
}
