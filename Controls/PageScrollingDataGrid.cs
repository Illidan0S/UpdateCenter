using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UpdateCenter.Controls;

/// <summary>
/// Reserves the full height of a table in the page while WPF realizes only
/// the rows in a finite DataGrid viewport.
/// </summary>
public sealed class PageScrollingDataGrid : Decorator
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(ScrollViewer), typeof(PageScrollingDataGrid),
        new FrameworkPropertyMetadata(null, PageChanged));

    private ScrollViewer? _gridScroll;
    private double _gridHeight;

    public PageScrollingDataGrid()
    {
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(GridScrolled));
        IsVisibleChanged += (_, e) =>
        {
            // The items can change while the results page or its expander is collapsed.
            // The cached reservation then belongs to the previous set of rows.
            if ((bool)e.NewValue) InvalidateMeasure();
        };
    }

    public ScrollViewer? Page
    {
        get => (ScrollViewer?)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not DataGrid grid) return new Size();

        // An outer ScrollViewer measures its content with infinite height.
        // Use its actual viewport for the table, including on compact windows.
        var height = Math.Max(180, Page?.ViewportHeight ?? 0);
        grid.Measure(new Size(availableSize.Width, height));
        _gridScroll = FindScrollViewer(grid);
        _gridHeight = grid.DesiredSize.Height;
        return new Size(grid.DesiredSize.Width, _gridHeight + (_gridScroll?.ScrollableHeight ?? 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null) return finalSize;

        var offset = 0d;
        if (_gridScroll is { } scroll && Page is { } page && TryGetPageTop(out var top))
        {
            offset = Math.Clamp(page.VerticalOffset - top, 0, scroll.ScrollableHeight);
            if (Math.Abs(scroll.VerticalOffset - offset) > 0.5)
                scroll.ScrollToVerticalOffset(offset);
        }

        Child.Arrange(new Rect(0, offset, finalSize.Width, _gridHeight));
        return finalSize;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || Page is not { ScrollableHeight: > 0 } page)
            return;

        page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta / 3d);
        e.Handled = true;
    }

    private static void PageChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var table = (PageScrollingDataGrid)sender;
        if (e.OldValue is ScrollViewer oldPage) oldPage.ScrollChanged -= table.PageScrolled;
        if (e.NewValue is ScrollViewer newPage) newPage.ScrollChanged += table.PageScrolled;
        table.InvalidateMeasure();
    }

    private void PageScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender) || !IsVisible) return;

        if (e.ViewportHeightChange != 0) InvalidateMeasure();
        else if (e.VerticalChange != 0 || e.ExtentHeightChange != 0) InvalidateArrange();
    }

    private void GridScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_gridScroll is not { } scroll || !ReferenceEquals(e.OriginalSource, scroll) || !IsVisible) return;

        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
        {
            InvalidateMeasure();
            return;
        }

        // Keyboard navigation and ScrollIntoView can move the native grid.
        // Publish that movement through the page's scrollbar as well.
        if (e.VerticalChange == 0 || Page is not { } page || !TryGetPageTop(out var top)) return;
        var expected = Math.Clamp(page.VerticalOffset - top, 0, scroll.ScrollableHeight);
        if (Math.Abs(e.VerticalOffset - expected) > 0.5)
            page.ScrollToVerticalOffset(top + e.VerticalOffset);
    }

    private bool TryGetPageTop(out double top)
    {
        if (Page?.Content is UIElement content && content.IsAncestorOf(this))
        {
            top = TranslatePoint(new Point(), content).Y;
            return true;
        }

        top = 0;
        return false;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer scroll) return scroll;
            if (FindScrollViewer(child) is { } nested) return nested;
        }

        return null;
    }
}
