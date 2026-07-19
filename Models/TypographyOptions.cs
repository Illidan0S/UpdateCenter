namespace UpdateCenter.Models;

public static class TypographyOptions
{
    public static double ScaleFor(string? mode) => Normalize(mode) switch
    {
        "Media" => 1.22,
        "Grande" => 1.34,
        _ => 1.10
    };

    public static string Normalize(string? mode) => mode is "Media" or "Grande" ? mode : "Piccola";
}
