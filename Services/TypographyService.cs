using System.Windows;

namespace UpdateCenter.Services;

public static class TypographyService
{
    private static readonly int[] BaseSizes = [9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 23, 24, 25, 27, 30];

    public static void Apply(string? mode)
    {
        if (Application.Current is null) return;
        var scale = Normalize(mode) switch
        {
            "Media" => 1.10,
            "Grande" => 1.20,
            _ => 1.00
        };

        foreach (var size in BaseSizes)
            Application.Current.Resources[$"FontSize{size}"] = Math.Round(size * scale, 1);

        Application.Current.Resources["DataGridRowHeight"] = Math.Round(52 * scale, 1);
    }

    public static string Normalize(string? mode) => mode is "Media" or "Grande" ? mode : "Piccola";
}
