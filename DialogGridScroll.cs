using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UpdateCenter;

// Only the two installation dialogs opt in. Keep their grids bounded and
// virtualized; hand the wheel to the surrounding page once a table reaches its edge.
internal static class DialogGridScroll
{
    internal static void ForwardAtBoundary(DataGrid grid, ScrollViewer page, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;
        var table = FindViewer(grid);
        if (table is null) return;
        if (CanScroll(table, e.Delta) || !CanScroll(page, e.Delta)) return;

        // Raise only the bubbling event on the parent viewer: no recursion through
        // the table, no double step, and WPF retains the user's wheel-line settings.
        page.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent
        });
        e.Handled = true;
    }

    private static bool CanScroll(ScrollViewer viewer, int delta) => delta > 0
        ? viewer.VerticalOffset > .001
        : viewer.VerticalOffset < viewer.ScrollableHeight - .001;

    private static ScrollViewer? FindViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindViewer(child) is { } nested) return nested;
        }
        return null;
    }
}
