using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UpdateCenter;

// Test assemblies only (excluded from UpdateCenter.csproj). Keep visual fixtures inert
// even when a tester clicks an enabled action, uses Enter, or invokes it through UIA.
internal static class VisualQaSafety
{
    private static readonly RoutedEvent[] Events = [ButtonBase.ClickEvent, MenuItem.ClickEvent, Control.MouseDoubleClickEvent,
        UIElement.PreviewKeyDownEvent, UIElement.KeyDownEvent, FrameworkElement.LoadedEvent,
        Selector.SelectionChangedEvent, Hyperlink.RequestNavigateEvent];

    public static void Attach(Window window, bool allowDialogDecisions = false, bool allowUpdateStatusFlyout = false, bool allowHistoryDetails = false)
    {
        bool Allow(Delegate handler)
        {
            if (allowHistoryDetails && handler.Method.DeclaringType == typeof(MainWindow) && handler.Method.Name is "HistoryDetail_Click" or "HistoryPopup_PreviewKeyDown" or "CloseHistoryDetail_Click") return true;
            if (allowUpdateStatusFlyout && handler.Method.DeclaringType == typeof(MainWindow) && handler.Method.Name is "UpdateStatus_Click" or "UpdateStatusPopup_PreviewKeyDown" or "CloseUpdateStatus_Click") return true;
            // These handlers only close/hide windows or return a modal decision to the QA host.
            // The host never passes that decision to an installation or remote service.
            if (handler.Method.DeclaringType == typeof(UpdateProgressWindow))
                return handler.Method.Name is "Close_Click" or "Minimize_Click";
            if (handler.Method.DeclaringType == typeof(AppUpdateWindow))
                return handler.Method.Name == "Later_Click";
            return allowDialogDecisions &&
                (handler.Method.DeclaringType == typeof(UpdateConfirmationWindow) || handler.Method.DeclaringType == typeof(WinGetProcessRecoveryWindow)) &&
                handler.Method.Name is "Cancel_Click" or "Confirm_Click" or "ExcludeRiskItems_Click";
        }
        void Clean() => Sanitize(window, Allow);
        Clean();
        window.LayoutUpdated += (_, _) => Clean();
        window.PreviewMouseDown += (_, _) => Clean();
        window.PreviewKeyDown += (_, e) =>
        {
            Clean();
            if (e.Key != Key.Escape || (allowUpdateStatusFlyout && window.FindName("UpdateStatusPopup") is Popup { IsOpen: true }) || (allowHistoryDetails && window.FindName("HistoryDetailPopup") is Popup { IsOpen: true })) return;
            e.Handled = true;
            // Preserve the progress window's hide-on-close semantics; never invoke a cancel/installer command.
            window.Close();
        };
        window.Loaded += (_, _) => UpdateCenter.Services.LocalizationService.ApplyTo(window);
        StopTimers(window);
    }

    public static int Sanitize(DependencyObject root, Func<Delegate, bool>? allow = null)
    {
        var removed = 0;
        foreach (var node in Nodes(root))
        {
            if (node is ButtonBase button && button.Command is not null) button.Command = null;
            if (node is MenuItem menu && menu.Command is not null) menu.Command = null;
            if (node is Hyperlink link && link.Command is not null) link.Command = null;
            if (node is UIElement input && input.InputBindings.Count > 0) input.InputBindings.Clear();
            foreach (var routedEvent in Events)
            foreach (var handler in ApplicationHandlers(node, routedEvent))
            {
                if (allow?.Invoke(handler) == true) continue;
                if (node is UIElement ui) ui.RemoveHandler(routedEvent, handler);
                else if (node is ContentElement content) content.RemoveHandler(routedEvent, handler);
                removed++;
            }
        }
        return removed;
    }

    public static IReadOnlyList<Delegate> ApplicationHandlers(DependencyObject node, RoutedEvent routedEvent)
    {
        var baseType = node is UIElement ? typeof(UIElement) : node is ContentElement ? typeof(ContentElement) : null;
        if (baseType is null) return [];
        var storeProperty = baseType.GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("QA isolation unavailable: routed event store not found.");
        var store = storeProperty.GetValue(node);
        if (store is null) return [];
        var method = store.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("QA isolation unavailable: routed handlers cannot be inspected.");
        var handlers = (Array?)method.Invoke(store, [routedEvent]);
        if (handlers is null) return [];
        return handlers.Cast<object>().Select(info => (Delegate)info.GetType().GetProperty("Handler")!.GetValue(info)!)
            .Where(handler => handler.Method.Module.Assembly == typeof(MainWindow).Assembly).ToList();
    }

    public static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>(); pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            yield return node;
            if (node is FrameworkElement { ContextMenu: not null } owner) pending.Push(owner.ContextMenu);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) pending.Push(child);
            if (node is not Visual && node is not System.Windows.Media.Media3D.Visual3D) continue;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) pending.Push(VisualTreeHelper.GetChild(node, i));
        }
    }

    public static void StopTimers(Window window)
    {
        foreach (var field in window.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            if (field.GetValue(window) is DispatcherTimer timer) timer.Stop();
    }
}
