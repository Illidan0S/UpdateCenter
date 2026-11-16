using System.Windows;
using System.Windows.Controls;
using UpdateCenter.Models;
using UpdateCenter.Services;
namespace UpdateCenter;
public partial class MainWindow
{
    private async void ReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UpdateItem item } anchor) await UpdateReleaseNotes.OpenAsync(item, anchor);
    }
    private void UpdateSelection_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true } box) UiMotion.Reveal(box);
    }
}
