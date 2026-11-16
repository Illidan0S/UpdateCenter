using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using UpdateCenter.Services;

namespace UpdateCenter;

// Presentation only: preserve the original group keys, scope objects and remote result data.
public sealed class ManagementTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var text = values.FirstOrDefault() as string ?? "";
        if (parameter as string == "Group" && LocalizationService.IsEnglish)
            return Regex.Replace(text, @" · (\d+) aggiornamenti · (\d+) software · (\d+) driver · (\d+) runtime$",
                " · $1 updates · $2 software · $3 drivers · $4 runtime");
        return LocalizationService.Translate(text);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
