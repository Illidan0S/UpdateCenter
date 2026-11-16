using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UpdateCenter.Services;

namespace UpdateCenter;

internal static class DialogPresentation
{
    public static void Initialize(Window window, Action dismiss, Func<Control?> initialFocus)
    {
        MainWindow.UpdateSettingsCardTheme(window, "Dialogs");
        window.Resources["DialogCloseText"] = LocalizationService.Translate("Chiudi");
        window.Resources["DialogHideText"] = LocalizationService.Translate("Nascondi avanzamento");
        window.Resources["DialogMinimizeText"] = LocalizationService.Translate("Riduci a icona");
        window.Resources["DialogItemsText"] = LocalizationService.Text("Elementi dell'operazione", "Operation items");
        window.Resources["DialogDetailsText"] = LocalizationService.Text("Dettagli tecnici", "Technical details");
        window.PreviewKeyDown += (_, e) =>
        {
            // Use the existing dismissal path: hiding progress is not cancellation.
            // Enter remains the native activation of the focused control, never a global install shortcut.
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            dismiss();
        };
        window.SourceInitialized += (_, _) => FitToOwner(window);
        window.Loaded += (_, _) =>
        {
            LocalizationService.ApplyTo(window);
            initialFocus()?.Focus();
        };
    }

    internal static void FitToOwner(Window window)
    {
        var workArea = SystemParameters.WorkArea;
        window.MaxWidth = workArea.Width;
        window.MaxHeight = workArea.Height;
        window.MinWidth = Math.Min(window.MinWidth, workArea.Width);
        window.MinHeight = Math.Min(window.MinHeight, workArea.Height);
        var owner = window.Owner;
        window.Width = Math.Clamp(window.Width, window.MinWidth,
            Math.Max(window.MinWidth, Math.Min(workArea.Width, owner?.ActualWidth > 0 ? owner.ActualWidth : workArea.Width)));
        window.Height = Math.Clamp(window.Height, window.MinHeight,
            Math.Max(window.MinHeight, Math.Min(workArea.Height, owner?.ActualHeight > 0 ? owner.ActualHeight : workArea.Height)));
    }
}
