using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class UpdatesGridTests
{
    internal static void Run(bool preview = false)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(preview); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine(preview ? "Closed isolated UpdatesGrid preview." : "PASS UpdatesGrid: no empty action column; real WPF layout IT/EN, 1538/1240/720 widths, long text, scrollbars, actions, selection, sorting, filtering and 500-row recycling.");
    }

    private static void Verify(bool preview)
    {
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, AutomaticScanInterval = "Off", CheckAppUpdatesAutomatically = false });
        // Load resources only: App's queued OnStartup must never create a real window.
        var app = new Application();
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "App.xaml"))) source = source.Parent;
        var applicationXaml = System.Xml.Linq.XDocument.Load(Path.Combine(source?.FullName ?? throw new InvalidOperationException("App.xaml not found"), "App.xaml")).Root!;
        System.Xml.Linq.XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = new System.Xml.Linq.XElement(presentation + "ResourceDictionary", applicationXaml.Attributes().Where(a => a.IsNamespaceDeclaration), applicationXaml.Element(presentation + "Application.Resources")!.Nodes());
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        LocalizationService.Initialize("it"); ThemeService.Apply("Chiaro"); TypographyService.Apply("Grande");
        var window = new MainWindow();
        VisualQaSafety.Attach(window);
        try
        {
            _ = new WindowInteropHelper(window).EnsureHandle(); window.Visibility = Visibility.Hidden;
            // Application handlers and timers are detached by the test-only safety fixture.
            var vm = (MainViewModel)window.DataContext;
            Invoke(window, "ShowPage", window.FindName("UpdatesPage"), "Aggiornamenti");
            var grid = (DataGrid)window.FindName("UpdatesGrid");
            if (!preview)
            {
                Invoke(window, "ShowPage", window.FindName("HomePage"), "Home");
                window.ShowInTaskbar = false; window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20000; window.Top = -20000; window.Show();
                VerifyScanResultsReachableWithoutResize(window, grid, vm);
                VerifyAvailabilityAndOrdering(window, grid, vm);
            }
            var items = new List<UpdateItem>();
            for (var i = 0; i < 500; i++) items.Add(new UpdateItem
            {
                Id = "QA.Grid." + i, Name = i == 0 ? "MuseHub · dati sintetici" : $"QA {i:0000} · Nome lungo per verificare la leggibilità senza sovrapposizioni",
                Kind = UpdateKind.Software, Source = "QA", InstalledVersion = "2.8.2.2197", AvailableVersion = "3.5.0.2378",
                Status = i == 1 ? "Errore" : "Da aggiornare", Size = "34,5 MB", IsSelected = false,
                ResultDetails = i == 0 ? "" : "Dettaglio sintetico molto lungo: testo completo disponibile nel tooltip, senza espandere in modo incontrollato la riga.",
                OfficialReleasePageUrl = i == 1 ? "https://example.invalid/source" : ""
            });
            typeof(MainViewModel).GetMethod("AddUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [items]);
            Layout(window, 1538, 987);
            if (preview)
            {
                // Reuse the existing test-only isolation: removes real actions/Loaded work.
                // WPF's built-in checkbox, selection, sorting and keyboard behavior stays active.
                window.Title = "UpdateCenter · M7 QA · dati sintetici";
                window.ShowDialog();
                return;
            }
            Capture(window, grid.Columns.Count == 6 ? "grid-after" : "grid-before");
            Check(grid.Columns.Count == 6 && grid.Columns[^1].SortMemberPath == "Status", "A separate empty/focusable action column remains after Status.");
            foreach (var language in new[] { "it", "en" })
            foreach (var width in new[] { 1538d, 1240d, 720d })
            {
                LocalizationService.Initialize(language); Invoke(window, "UpdateOperationsLabels"); LocalizationService.ApplyTo(window); vm.NotifyLanguageChanged();
                Layout(window, width, width == 720 ? 500 : 790);
                grid.ScrollIntoView(vm.Updates[0], grid.Columns[^1]); window.UpdateLayout();
                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(vm.Updates[0]);
                var cells = Visuals<DataGridCell>(row).ToArray();
                Check(cells.All(c => c.Column.DisplayIndex < 6), "Ghost cell remains in the realized row.");
                var status = cells.Single(c => c.Column == grid.Columns[^1]);
                Check(Visuals<TextBlock>(status).Any(t => t.Text == vm.Updates[0].Status), "Last focusable cell has no status content.");
                Check(!Visuals<Button>(status).Any(b => b.Name != "UpdateStatusButton" && b.Visibility == Visibility.Visible), "Inapplicable actions occupy the MuseHub fixture row.");
                Check(grid.Columns[^1].Width.IsStar && grid.SelectionUnit == DataGridSelectionUnit.FullRow, "Sizing/row selection changed.");
                Check(!Visuals<ScrollBar>(grid).Any(s => s.Orientation == Orientation.Vertical && s.IsVisible),
                    "The grid exposes a separate vertical scrollbar.");
                var scroll = Visuals<ScrollViewer>(grid).First();
                if (width == 720) Check(scroll.ScrollableWidth > 0 && ScrollViewer.GetHorizontalScrollBarVisibility(grid) == ScrollBarVisibility.Auto, "Minimum-width columns are clipped without scrolling.");
                else Check(scroll.ScrollableWidth < 2, "Unnecessary horizontal overflow at desktop width.");
                Check(Visuals<DataGridRow>(grid).All(r => r.ActualHeight < 180), "Long text/actions create oversized rows.");
                grid.ScrollIntoView(vm.Updates[499]); window.UpdateLayout();
                Check(grid.EnableRowVirtualization && grid.EnableColumnVirtualization && Visuals<DataGridRow>(grid).Count() < 80, "Virtualization lost.");
                grid.ScrollIntoView(vm.Updates[1], grid.Columns[^1]); window.UpdateLayout();
                var actionCell = Visuals<DataGridCell>((DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(vm.Updates[1]))
                    .Single(c => c.Column == grid.Columns[^1]);
                var buttons = Visuals<Button>(actionCell).Where(b => b.Name != "UpdateStatusButton" && b.Visibility == Visibility.Visible).ToArray();
                Check(buttons.Length == 2 && buttons.All(b => b.Focusable && b.ActualHeight > 0), "Existing source/retry actions were removed or cannot receive keyboard focus.");
                foreach (var button in buttons)
                {
                    button.BringIntoView(); Layout(window, width, width == 720 ? 500 : 790);
                    var viewport = Visuals<ScrollContentPresenter>(grid).First();
                    var bounds = button.TransformToAncestor(viewport).TransformBounds(new Rect(button.RenderSize));
                    Check(bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1 && bounds.Left >= -1 && bounds.Right <= viewport.ActualWidth + 1,
                        $"Action is clipped after BringIntoView at {width}: {button.Content}, {bounds}, viewport {viewport.RenderSize}.");
                }
                if (language == "it" && width == 720) Capture(window, "grid-720-actions");
                OperationsUiFixture.Set(vm, "IsBusy", true);
                Check(!buttons.Single(b => Equals(b.Content, LocalizationService.Translate("Riprova"))).IsEnabled, "Retry lost its busy guard.");
                OperationsUiFixture.Set(vm, "IsBusy", false);
                grid.ScrollIntoView(vm.Updates[0], grid.Columns[0]); window.UpdateLayout();
                var checkbox = Visuals<CheckBox>(grid).First(b => ReferenceEquals(b.DataContext, vm.Updates[0]));
                checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
                Check(vm.Updates[0].IsSelected && vm.SelectedCount == 1, "Checkbox binding/count broken.");
                checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                grid.SelectedItem = vm.Updates[0]; Check(ReferenceEquals(grid.SelectedItem, vm.Updates[0]), "Row selection broken.");
                if (language == "it") Capture(window, "grid-" + (int)width);
            }
            VerifyColumnSortPersistsAcrossSelectionAndRefresh(window, grid, vm);
            vm.SearchText = "MuseHub"; Check(grid.Items.Count == 1, "Search filter broken.");
            vm.SearchText = ""; vm.UpdateTypeFilter = "Errori"; Check(grid.Items.Count == 1, "Status filter broken.");
        }
        finally { window.Close(); app.Shutdown(); }
    }

    private static void VerifyScanResultsReachableWithoutResize(MainWindow window, DataGrid grid, MainViewModel vm)
    {
        var home = (UIElement)window.FindName("HomePage");
        var updates = (UIElement)window.FindName("UpdatesPage");
        var page = (ScrollViewer)window.FindName("UpdatesSectionsViewport");
        var blocked = (Expander)window.FindName("BlockedUpdatesExpander");
        var blockedGrid = (DataGrid)window.FindName("BlockedUpdatesGrid");
        foreach (var width in new[] { 720d, 1240d })
        {
            Invoke(window, "ShowPage", home, "Home");
            Layout(window, width, width == 720 ? 500 : 790);
            var originalSize = window.RenderSize;
            foreach (var count in new[] { 39, 4, 62 })
            {
                Invoke(window, "ShowPage", home, "Home");
                OperationsUiFixture.Set(vm, "IsBusy", true);
                DrainLayout(window);
                typeof(MainViewModel).GetMethod("ClearUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
                var installed = Enumerable.Range(0, count).Select(i => Item($"Scan manageable {i:000}")).ToArray();
                var excluded = Enumerable.Range(0, count).Select(i => Item($"Scan blocked {i:000}", installable: false)).ToArray();
                typeof(MainViewModel).GetMethod("AddUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm, [installed.Concat(excluded).ToArray()]);
                OperationsUiFixture.Set(vm, "IsBusy", false);
                Invoke(window, "ShowPage", updates, "Aggiornamenti");
                blocked.SetCurrentValue(Expander.IsExpandedProperty, false);
                DrainLayout(window);
                ExpectLastRow(grid, installed[^1], "manageable");
                blocked.SetCurrentValue(Expander.IsExpandedProperty, true);
                DrainLayout(window);
                ExpectLastRow(blockedGrid, excluded[^1], "blocked");
                Check(window.RenderSize == originalSize, "The test must keep the scan and results at the same window size.");
            }
        }
        blocked.SetCurrentValue(Expander.IsExpandedProperty, false);
        typeof(MainViewModel).GetMethod("ClearUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        page.ScrollToTop();
        DrainLayout(window);
        Console.WriteLine("PASS VerifyScanResultsReachableWithoutResize");

        void ExpectLastRow(DataGrid table, UpdateItem item, string section)
        {
            page.ScrollToEnd();
            DrainLayout(window);
            var viewport = Visuals<ScrollContentPresenter>(page).First();
            var row = table.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
            var bounds = row?.TransformToAncestor(viewport).TransformBounds(new Rect(row.RenderSize));
            var inner = Visuals<ScrollViewer>(table).First();
            var wrapper = (FrameworkElement)VisualTreeHelper.GetParent(table);
            Check(bounds is { } visible && visible.Top >= -1 && visible.Bottom <= viewport.ActualHeight + 1,
                $"Last {section} row is clipped after scan at {window.ActualWidth}: rows={table.Items.Count}, row={bounds}, page offset={page.VerticalOffset}/{page.ScrollableHeight}, viewport={viewport.ActualHeight}, table height={table.ActualHeight}, reserved height={wrapper.ActualHeight}, inner offset={inner.VerticalOffset}/{inner.ScrollableHeight}.");
            if (window.ActualWidth <= 720)
            {
                Check(inner.ScrollableWidth > 0 && inner.ComputedHorizontalScrollBarVisibility == Visibility.Visible,
                    $"Horizontal scrolling is unavailable in the compact {section} table.");
                inner.ScrollToRightEnd();
                DrainLayout(window);
                Check(Math.Abs(inner.HorizontalOffset - inner.ScrollableWidth) <= 1,
                    $"The compact {section} table cannot scroll to the last column.");
                inner.ScrollToLeftEnd();
                DrainLayout(window);
            }
        }
    }

    private static void VerifyAvailabilityAndOrdering(MainWindow window, DataGrid grid, MainViewModel vm)
    {
        var selected = Item("Zulu", selected: true);
        var unverified = Item("Beta");
        unverified.HasUnverifiedInstallerMetadata = true;
        var needsConfirmation = Item("Delta");
        needsConfirmation.RequiresRiskConfirmation = true;
        var current = Item("Gamma", installable: false);
        current.Status = "Già aggiornato";
        current.ResultDetails = "Target già verificato.";
        var excluded = Item("Alpha", installable: false);
        excluded.Status = "Non applicabile";
        excluded.ResultDetails = "Escluso per incompatibilità.";
        typeof(MainViewModel).GetMethod("AddUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [new[] { current, selected, excluded, needsConfirmation, unverified }]);

        UpdateItem[] expected = [unverified, needsConfirmation, selected];
        Check(grid.Items.Cast<UpdateItem>().SequenceEqual(expected),
            "VerifyAvailabilityAndOrdering: the main grid must contain only selectable rows, in the approved alphabetical order.");
        var blockedGrid = (DataGrid)window.FindName("BlockedUpdatesGrid");
        var blockedSection = (Expander)window.FindName("BlockedUpdatesExpander");
        Check(!blockedSection.IsExpanded && blockedGrid.Items.Cast<UpdateItem>().SequenceEqual(new[] { excluded, current }),
            "Blocked results must retain the original instances and order in a collapsed section.");
        Check(blockedGrid.Columns.Count == grid.Columns.Count &&
            ReferenceEquals(((DataGridTemplateColumn)blockedGrid.Columns[^1]).CellTemplate, ((DataGridTemplateColumn)grid.Columns[^1]).CellTemplate),
            "Blocked status rows must reuse the existing complete status/detail presentation.");
        Check(vm.SelectedCount == 1 && selected.IsSelected && !unverified.IsSelected && !needsConfirmation.IsSelected,
            "The table changed the scan's initial selection.");
        vm.SetAllSelected(true);
        Check(selected.IsSelected && unverified.IsSelected && !needsConfirmation.IsSelected &&
              !current.IsSelected && !excluded.IsSelected && vm.SelectedCount == 2,
            "Select all must respect blocked and separate-confirmation rows.");
        Check(grid.Items.Cast<UpdateItem>().SequenceEqual(expected), "Checkbox changes must preserve the displayed order.");

        foreach (var (language, theme, expectedCounts) in new[]
        {
            ("it", "Chiaro", "5 aggiornamenti rilevati · 3 aggiornabili dall’app · 2 esclusi dall’aggiornamento automatico"),
            ("it", "Scuro", "5 aggiornamenti rilevati · 3 aggiornabili dall’app · 2 esclusi dall’aggiornamento automatico"),
            ("en", "Chiaro", "5 updates detected · 3 updatable in the app · 2 excluded from automatic updates"),
            ("en", "Scuro", "5 updates detected · 3 updatable in the app · 2 excluded from automatic updates")
        })
        {
            ThemeService.Apply(theme);
            LocalizationService.Initialize(language);
            Invoke(window, "UpdateOperationsLabels");
            LocalizationService.ApplyTo(window);
            vm.NotifyLanguageChanged();
            Layout(window, 720, 500);
            var summary = (TextBlock)window.FindName("UpdateAvailabilityText");
            var selectAll = (Button)window.FindName("SelectUpdatesButton");
            var activity = (Border)window.FindName("UpdatesActivity");
            var install = (Button)window.FindName("InstallUpdatesButton");
            Check(summary.IsVisible && summary.ActualHeight > 0 && summary.Text == expectedCounts,
                "All three counts must remain visible at 720x500.");
            Check(activity.Visibility == Visibility.Collapsed &&
                Equals(install.Content, language == "it" ? "Aggiorna selezionati" : "Update selected"),
                "Idle results must show one summary and the localized update action.");
            Check(window.FindName("UpdateSelectionHint") is null && selectAll.ToolTip is string preselection &&
                preselection.Contains(language == "it" ? "A fine scansione" : "After scanning"),
                "The preselection explanation must appear in the bulk-selection tooltip.");
            Check(selectAll.ToolTip is string help && help.Contains(language == "it" ? "conferma separata" : "separate confirmation") &&
                  System.Windows.Automation.AutomationProperties.GetHelpText(selectAll) == help,
                "The bulk-selection help must be localized and accessible.");
            ((FrameworkElement)window.FindName("ManagedUpdatesSection")).BringIntoView();
            Layout(window, 720, 500);
            Capture(window, $"availability-720-{language}-{(theme == "Chiaro" ? "light" : "dark")}");
            Check(Visuals<ScrollContentPresenter>(grid).First().ActualHeight > 0,
                $"The table viewport must remain usable with the new summary: grid={grid.ActualHeight}; section={blockedSection.ActualHeight}; viewport={Visuals<ScrollContentPresenter>(grid).First().ActualHeight}.");
            blockedSection.SetCurrentValue(Expander.IsExpandedProperty, true);
            Layout(window, 720, 500);
            Check(vm.IsBlockedUpdatesExpanded && (blockedSection.Header as TextBlock)?.Text == vm.BlockedUpdatesHeader &&
                blockedGrid.IsVisible && Visuals<ScrollContentPresenter>(blockedGrid).First().ActualHeight > 0 &&
                Visuals<ScrollContentPresenter>(grid).First().ActualHeight > 0,
                "Both sections must retain bounded usable viewports at 720x500.");
            blockedGrid.ScrollIntoView(excluded, blockedGrid.Columns[0]); window.UpdateLayout();
            var blockedBox = Visuals<CheckBox>(blockedGrid).First(x => ReferenceEquals(x.DataContext, excluded));
            Check(!blockedBox.IsEnabled && !excluded.IsSelected, "Blocked results gained an actionable checkbox.");
            blockedGrid.ScrollIntoView(excluded, blockedGrid.Columns[^1]); Layout(window, 720, 500);
            var blockedRow = (DataGridRow)blockedGrid.ItemContainerGenerator.ContainerFromItem(excluded);
            var statusButton = Visuals<Button>(blockedRow).Single(x => x.Name == "UpdateStatusButton");
            statusButton.BringIntoView();
            Layout(window, 720, 500);
            Invoke(window, "UpdateStatus_Click", statusButton, new RoutedEventArgs(Button.ClickEvent, statusButton));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(((Popup)window.FindName("UpdateStatusPopup")).IsOpen &&
                ((TextBox)window.FindName("UpdateStatusReason")).Text == excluded.ResultDetails,
                $"The blocked row lost its complete actual reason in the shared details popup: open={((Popup)window.FindName("UpdateStatusPopup")).IsOpen}, reason='{((TextBox)window.FindName("UpdateStatusReason")).Text}'.");
            var outerScroll = (ScrollViewer)window.FindName("UpdatesSectionsViewport");
            outerScroll.ScrollToVerticalOffset(outerScroll.VerticalOffset - 10);
            Layout(window, 720, 500);
            Check(!((Popup)window.FindName("UpdateStatusPopup")).IsOpen,
                "Scrolling the outer page kept a status popup attached to a displaced row.");
            blockedSection.BringIntoView(); window.UpdateLayout();
            Capture(window, $"blocked-expanded-720-{language}-{(theme == "Chiaro" ? "light" : "dark")}");
            blockedSection.SetCurrentValue(Expander.IsExpandedProperty, false);
            outerScroll.ScrollToEnd();
            Layout(window, 720, 500);
            var closedHeader = (ToggleButton)blockedSection.Template.FindName("BlockedHeaderToggle", blockedSection);
            var closedBounds = closedHeader.TransformToAncestor(outerScroll).TransformBounds(new Rect(closedHeader.RenderSize));
            Check(closedBounds.Top >= -1 && closedBounds.Bottom <= outerScroll.ActualHeight + 1,
                "The collapsed blocked section cannot be reached below the main row list.");
            Capture(window, $"blocked-collapsed-720-{language}-{(theme == "Chiaro" ? "light" : "dark")}");
            outerScroll.ScrollToTop();
        }
        OperationsUiFixture.Set(vm, "StatusText", "Scan warning");
        DrainLayout(window);
        Check(((Border)window.FindName("UpdatesActivity")).IsVisible, "An activity message must remain visible.");
        OperationsUiFixture.Set(vm, "ShowUpdatesActivity", false);
        DrainLayout(window);

        vm.SearchText = "Alpha";
        Layout(window, 720, 500);
        Check(grid.Items.Count == 0 && blockedGrid.Items.Count == 1 && blockedSection.IsExpanded &&
            ReferenceEquals(blockedGrid.Items[0], excluded) && vm.VisibleUpdateCount == 1,
            "A search matching only blocked results must expose the result automatically.");
        var sectionViewport = (ScrollViewer)window.FindName("UpdatesSectionsViewport");
        var sectionHeader = (ToggleButton)blockedSection.Template.FindName("BlockedHeaderToggle", blockedSection);
        var headerBounds = sectionHeader.TransformToAncestor(sectionViewport).TransformBounds(new Rect(sectionHeader.RenderSize));
        var resultRow = (DataGridRow)blockedGrid.ItemContainerGenerator.ContainerFromItem(excluded);
        var rowBounds = resultRow.TransformToAncestor(sectionViewport).TransformBounds(new Rect(resultRow.RenderSize));
        Check(headerBounds.Top >= -1 && headerBounds.Bottom <= sectionViewport.ActualHeight + 1 &&
            rowBounds.Top >= -1 && rowBounds.Bottom <= sectionViewport.ActualHeight + 1,
            $"Blocked-only search must show its header and a complete result row: header={headerBounds}, row={rowBounds}, viewport={sectionViewport.ActualHeight}.");
        Capture(window, "blocked-search-720-en-dark");
        blockedSection.SetCurrentValue(Expander.IsExpandedProperty, false);
        selected.IsSelected = false;
        Layout(window, 720, 500);
        Check(!blockedSection.IsExpanded, "A manual collapse was overwritten by an unrelated selection change.");
        headerBounds = sectionHeader.TransformToAncestor(sectionViewport).TransformBounds(new Rect(sectionHeader.RenderSize));
        Check(headerBounds.Top >= -1 && headerBounds.Bottom <= sectionViewport.ActualHeight + 1,
            "The collapsed blocked-results header is no longer reachable in the section viewport.");
        vm.SearchText = "Beta";
        Check(grid.Items.Count == 1 && blockedGrid.Items.Count == 0 && ReferenceEquals(grid.Items[0], unverified),
            "Search did not apply consistently to both sections.");
        vm.SearchText = "";
        selected.IsSelected = true;
        excluded.CanInstall = true;
        Layout(window, 720, 500);
        Check(ReferenceEquals(grid.Items[0], excluded) && !excluded.IsSelected && selected.IsSelected,
            "A real eligibility change must update the default order without changing selection.");
        Check(((TextBlock)window.FindName("UpdateAvailabilityText")).Text == "5 updates detected · 4 updatable in the app · 1 excluded from automatic updates",
            "A real eligibility change must update all availability counts.");
        Check(!blockedGrid.Items.Contains(excluded) && blockedGrid.Items.Count == 1,
            "A changed candidate appeared in both sections or was copied instead of moved between views.");
        var manyBlocked = Enumerable.Range(0, 500).Select(i => Item($"Blocked {i:000}", installable: false)).ToArray();
        typeof(MainViewModel).GetMethod("AddUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [manyBlocked]);
        var manyManaged = Enumerable.Range(0, 500).Select(i => Item($"Managed {i:000}")).ToArray();
        typeof(MainViewModel).GetMethod("AddUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [manyManaged]);
        Layout(window, 1240, 790);
        sectionViewport.ScrollToEnd();
        Layout(window, 1240, 790);
        var collapsedOffset = sectionViewport.VerticalOffset;
        var collapsedHeight = grid.ActualHeight;
        var collapsedExtent = sectionViewport.ExtentHeight;
        blockedSection.SetCurrentValue(Expander.IsExpandedProperty, true);
        Layout(window, 1240, 790);
        Check(grid.ActualHeight >= collapsedHeight - 1 && sectionViewport.ExtentHeight > collapsedExtent,
            "Expanding blocked results must extend the page without shrinking the managed grid.");
        Check(Math.Abs(sectionViewport.VerticalOffset - collapsedOffset) <= 1,
            "Manual expansion jumped away from the position of the clicked header.");
        var boundary = blockedSection.TranslatePoint(new Point(), (UIElement)sectionViewport.Content).Y;
        sectionViewport.ScrollToVerticalOffset(boundary - 180);
        Layout(window, 1240, 790);
        var managedTail = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(selected);
        var blockedHead = (DataGridRow)blockedGrid.ItemContainerGenerator.ContainerFromItem(manyBlocked[0]);
        Check(managedTail is not null && blockedHead is not null &&
            managedTail.TranslatePoint(new Point(0, managedTail.ActualHeight), sectionViewport).Y > 0 &&
            blockedHead.TranslatePoint(new Point(0, blockedHead.ActualHeight), sectionViewport).Y < sectionViewport.ActualHeight,
            "The page cannot show rows from both sections at their scroll boundary.");
        Capture(window, "continuous-sections-1240");
        Layout(window, 720, 500);
        sectionViewport.ScrollToEnd();
        Layout(window, 720, 500);
        var lastBlocked = (DataGridRow)blockedGrid.ItemContainerGenerator.ContainerFromItem(manyBlocked[^1]);
        Check(lastBlocked is not null && lastBlocked.TranslatePoint(new Point(0, lastBlocked.ActualHeight), sectionViewport).Y
            <= sectionViewport.ActualHeight + 1, "The page scrollbar did not reach the last blocked row.");
        Check(blockedGrid.Items.Count == 501 && blockedGrid.EnableRowVirtualization && blockedGrid.EnableColumnVirtualization &&
            Visuals<DataGridRow>(blockedGrid).Count() < 80 && Visuals<ScrollViewer>(blockedGrid).First().ScrollableHeight > 0,
            "Blocked result expansion lost virtualization or vertical scrolling.");
        var beforeWheel = sectionViewport.VerticalOffset;
        var withinRows = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, 120)
        { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent, Source = blockedGrid };
        blockedGrid.RaiseEvent(withinRows);
        Layout(window, 720, 500);
        Check(withinRows.Handled && sectionViewport.VerticalOffset < beforeWheel,
            "The grid swallowed the wheel while scrolling through the page.");
        Console.WriteLine("PASS SinglePageScroll");
        blockedSection.SetCurrentValue(Expander.IsExpandedProperty, false);
        typeof(MainViewModel).GetMethod("ClearUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        ThemeService.Apply("Chiaro");
        Console.WriteLine("PASS VerifyAvailabilityAndOrdering");
    }

    private static void VerifyColumnSortPersistsAcrossSelectionAndRefresh(MainWindow window, DataGrid grid, MainViewModel vm)
    {
        var nameHeader = Visuals<DataGridColumnHeader>(grid).Single(h => h.Column == grid.Columns[1]);
        var click = typeof(DataGridColumnHeader).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        click.Invoke(nameHeader, null);
        click.Invoke(nameHeader, null);
        var last = vm.Updates.OrderByDescending(x => x.Name).First();
        Check(nameHeader.Column.SortDirection == ListSortDirection.Descending && ReferenceEquals(grid.Items[0], last),
            "Column sorting must override the default order.");
        vm.SetAllSelected(true);
        vm.SearchText = "QA";
        vm.SearchText = "";
        Layout(window, 720, 500);
        Check(nameHeader.Column.SortDirection == ListSortDirection.Descending && ReferenceEquals(grid.Items[0], last) &&
            vm.Updates.All(item => item.IsSelected), "Selection and filtering must preserve manual sorting.");
        Console.WriteLine("PASS VerifyColumnSortPersistsAcrossSelectionAndRefresh");
    }

    private static UpdateItem Item(string name, bool installable = true, bool selected = false) => new()
    {
        Id = "QA.Availability." + name,
        Name = name,
        Kind = UpdateKind.Software,
        Source = "QA",
        CanInstall = installable,
        IsSelected = selected
    };

    private static void Layout(MainWindow w, double width, double height)
    {
        w.Width = width; w.Height = height; w.Measure(new Size(width, height)); w.Arrange(new Rect(0, 0, width, height));
        Invoke(w, "ApplyResponsiveLayout");
        DrainLayout(w);
    }
    private static void DrainLayout(MainWindow w)
    {
        w.UpdateLayout();
        var frame = new DispatcherFrame();
        w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); w.UpdateLayout();
    }
    private static void Capture(MainWindow w, string name)
    {
        var root = Path.GetFullPath("_output/unified-updates-scroll/ui-grid"); Directory.CreateDirectory(root);
        var bitmap = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)w.Content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(root, name + ".png")); png.Save(file);
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var next in Visuals<T>(child)) yield return next;
        }
    }
    private static void Invoke(MainWindow w, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    private static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
