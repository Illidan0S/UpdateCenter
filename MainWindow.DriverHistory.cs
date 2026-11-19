using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter;

public partial class MainWindow
{
    private FrameworkElement? _historyDetailAnchor;

    private void InitializeDriverHistoryUi()
    {
        // Tag is not a logical child: give the reused header an explicit, stable context.
        DriversContent.DataContext = _viewModel;
        // An explicit group survives an empty inventory/filter. WPF owns scrolling,
        // BringIntoView, selection, sorting and hierarchical row recycling.
        _viewModel.DriverInventoryView.GroupDescriptions.Add(new DriverPageGroup());
        DriverInventoryGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DriverInventoryGrid_ScrollChanged));
        DriverProblemsGrid.PreviewMouseWheel += DriverSection_PreviewMouseWheel;
        DriverVendorScrollViewer.PreviewMouseWheel += DriverSection_PreviewMouseWheel;
        DriversContent.SizeChanged += (_, _) => PositionDriverColumnHeaders();
        HistoryGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(HistoryGrid_ScrollChanged));
        _viewModel.History.CollectionChanged += (_, _) => CloseHistoryDetail();
        LocationChanged += (_, _) => CloseHistoryDetail();
        SizeChanged += (_, _) => CloseHistoryDetail();
        Deactivated += (_, _) => CloseHistoryDetail();
    }

    private void DriverInventoryGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DriverInventoryGrid.Template.FindName("DG_ScrollViewer", DriverInventoryGrid))) PositionDriverColumnHeaders();
    }

    private void DriverSection_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Nested ScrollViewers consume wheel input even with vertical scrolling disabled.
        // Route ordinary vertical wheel input once through the page's native viewer,
        // retaining WPF/system wheel settings and the virtualized pixel scroll path.
        if (e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;
        if (DriverInventoryGrid.Template.FindName("DG_ScrollViewer", DriverInventoryGrid) is not ScrollViewer { IsVisible: true } viewer) return;
        viewer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = Mouse.MouseWheelEvent });
        e.Handled = true;
    }

    private void PositionDriverColumnHeaders()
    {
        if (DriverInventoryGrid.Template.FindName("DG_ScrollViewer", DriverInventoryGrid) is ScrollViewer viewer &&
            viewer.Template.FindName("DriverHeadersOffset", viewer) is TranslateTransform offset)
            offset.Y = DriversContent.ActualHeight - viewer.VerticalOffset;
    }

    private sealed class DriverPageGroup : GroupDescription
    {
        internal DriverPageGroup() => GroupNames.Add("Inventory");
        public override object GroupNameFromItem(object item, int level, CultureInfo culture) => "Inventory";
    }

    private void HistoryDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryEntry entry } anchor) return;
        CloseHistoryDetail();
        CloseUpdateStatus();
        _historyDetailAnchor = anchor;
        anchor.Unloaded += HistoryDetailAnchor_Unloaded;
        anchor.DataContextChanged += HistoryDetailAnchor_DataContextChanged;
        entry.PropertyChanged += HistoryDetailEntry_PropertyChanged;
        HistoryDetailContent.DataContext = entry;
        RefreshHistoryDetailText();
        HistoryDetailPopup.PlacementTarget = anchor;
        SizeDetailPopup(HistoryDetailContent, anchor);
        HistoryDetailPopup.IsOpen = true;
        e.Handled = true;
    }

    private void RefreshHistoryDetailText()
    {
        if (HistoryDetailContent.DataContext is not HistoryEntry entry) return;
        var details = string.IsNullOrWhiteSpace(entry.DisplayDetails)
            ? LocalizationService.Text("Nessun dettaglio disponibile per questa operazione.", "No details available for this operation.")
            : entry.DisplayDetails;
        var diagnostics = entry.Diagnostics + (entry.PendingVerificationPlan is not null && entry.LastVerificationDiagnostics.Length > 0
            ? $"\n\nAutomatic verification UTC={entry.LastVerificationAttemptUtc:O}:\n{entry.LastVerificationDiagnostics}" : "");
        HistoryDetailText.Text = string.IsNullOrWhiteSpace(diagnostics) ? details
            : $"{details}\n\n--- {LocalizationService.Text("Diagnostica tecnica", "Technical diagnostics")} ---\n{diagnostics}";
    }

    private void SizeDetailPopup(FrameworkElement content, FrameworkElement anchor)
    {
        var dpi = VisualTreeHelper.GetDpi(anchor);
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        content.Width = Math.Max(120, Math.Min(440, Math.Min(ActualWidth - 48, area.Width / dpi.DpiScaleX - 32)));
        content.MaxHeight = Math.Max(120, Math.Min(360, Math.Min(ActualHeight - 64, area.Height / dpi.DpiScaleY - 32)));
    }

    private void HistoryDetailPopup_Opened(object? sender, EventArgs e)
    {
        HistoryDetailScrollViewer.ScrollToTop();
        HistoryDetailCloseButton.Focus();
    }

    private void HistoryPopup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseHistoryDetail(restoreFocus: true);
            e.Handled = true;
            return;
        }
        // Preserve selection shortcuts, including Shift+Home/End and Ctrl+C.
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
        switch (e.Key)
        {
            case Key.PageDown: HistoryDetailScrollViewer.PageDown(); break;
            case Key.PageUp: HistoryDetailScrollViewer.PageUp(); break;
            case Key.Home: HistoryDetailScrollViewer.ScrollToTop(); break;
            case Key.End: HistoryDetailScrollViewer.ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void CloseHistoryDetail_Click(object sender, RoutedEventArgs e) => CloseHistoryDetail(restoreFocus: true);

    private void CloseHistoryDetail(bool restoreFocus = false)
    {
        var anchor = _historyDetailAnchor;
        var entry = HistoryDetailContent.DataContext;
        HistoryDetailPopup.IsOpen = false;
        if (restoreFocus && anchor is { IsVisible: true, IsEnabled: true } && ReferenceEquals(anchor.DataContext, entry)) anchor.Focus();
    }

    private void HistoryDetailPopup_Closed(object? sender, EventArgs e)
    {
        if (_historyDetailAnchor is { } anchor)
        {
            anchor.Unloaded -= HistoryDetailAnchor_Unloaded;
            anchor.DataContextChanged -= HistoryDetailAnchor_DataContextChanged;
        }
        if (HistoryDetailContent.DataContext is HistoryEntry entry) entry.PropertyChanged -= HistoryDetailEntry_PropertyChanged;
        _historyDetailAnchor = null;
        HistoryDetailContent.DataContext = null;
        HistoryDetailText.Clear();
        HistoryDetailPopup.PlacementTarget = null;
    }

    private void HistoryDetailEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshHistoryDetailText();
    private void HistoryDetailAnchor_Unloaded(object sender, RoutedEventArgs e) => CloseHistoryDetail();
    private void HistoryDetailAnchor_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => CloseHistoryDetail();
    private void HistoryGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.HorizontalChange != 0 || e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) CloseHistoryDetail();
    }
}
