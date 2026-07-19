using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace UpdateCenter.Services;

public static class ThemeService
{
    public static bool IsDark { get; private set; }

    public static void Apply(string? mode)
    {
        var normalized = Normalize(mode);
        IsDark = normalized == "Scuro" || normalized == "Sistema" && IsWindowsDarkTheme();

        if (IsDark)
        {
            SetBrush("BackgroundBrush", "#0D1320");
            SetBrush("SurfaceBrush", "#141C2B");
            SetBrush("SurfaceAltBrush", "#1C2738");
            SetBrush("BorderBrush", "#2D3A4E");
            SetBrush("AccentBrush", "#7B7FF7");
            SetBrush("AccentHoverBrush", "#696DE6");
            SetBrush("TextBrush", "#F5F7FB");
            SetBrush("MutedTextBrush", "#AAB6C8");
            SetBrush("InputBrush", "#1A2434");
            SetBrush("AlternateRowBrush", "#121A28");
            SetBrush("SelectionBrush", "#303954");
            SetBrush("RowHoverBrush", "#202B3E");
            SetBrush("ScrollThumbBrush", "#4B5870");
            SetBrush("ScrollThumbHoverBrush", "#71809A");
            SetBrush("AccentSoftBrush", "#24284B");
            SetBrush("AccentSoftBorderBrush", "#3B4272");
            SetBrush("DangerSoftBrush", "#45232C");
            SetBrush("WarningSoftBrush", "#493722");
        }
        else
        {
            SetBrush("BackgroundBrush", "#F3F5F9");
            SetBrush("SurfaceBrush", "#FFFFFF");
            SetBrush("SurfaceAltBrush", "#EEF1F6");
            SetBrush("BorderBrush", "#DDE3EC");
            SetBrush("AccentBrush", "#5F63F2");
            SetBrush("AccentHoverBrush", "#5155DC");
            SetBrush("TextBrush", "#20242D");
            SetBrush("MutedTextBrush", "#667085");
            SetBrush("InputBrush", "#FFFFFF");
            SetBrush("AlternateRowBrush", "#FAFBFC");
            SetBrush("SelectionBrush", "#E9ECFF");
            SetBrush("RowHoverBrush", "#F4F6FA");
            SetBrush("ScrollThumbBrush", "#C5CDDA");
            SetBrush("ScrollThumbHoverBrush", "#949FB1");
            SetBrush("AccentSoftBrush", "#F0F1FF");
            SetBrush("AccentSoftBorderBrush", "#D8DAFF");
            SetBrush("DangerSoftBrush", "#FDE7EA");
            SetBrush("WarningSoftBrush", "#FFF2DD");
        }
    }

    public static string Normalize(string? mode) => mode is "Chiaro" or "Scuro" ? mode : "Sistema";

    private static bool IsWindowsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetBrush(string key, string colorText)
    {
        if (Application.Current is null) return;
        var color = (Color)ColorConverter.ConvertFromString(colorText)!;
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }
}
