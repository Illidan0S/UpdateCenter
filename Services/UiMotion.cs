using System.Windows;
using System.Windows.Media.Animation;
namespace UpdateCenter.Services;

internal static class UiMotion

{
    private static readonly DependencyProperty GenerationProperty = DependencyProperty.RegisterAttached("Generation", typeof(int), typeof(UiMotion), new PropertyMetadata(0));
    private static int Next(UIElement element) { var value = (int)element.GetValue(GenerationProperty) + 1; element.SetValue(GenerationProperty, value); return value; }
    internal static void Reveal(UIElement element)
    {
        Next(element);
        element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1;
        if (SystemParameters.ClientAreaAnimation) element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.75, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
    }
    internal static void Hide(UIElement element)
    {
        var generation = Next(element);
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (!SystemParameters.ClientAreaAnimation) { element.Visibility = Visibility.Collapsed; return; }
        var fade = new DoubleAnimation(1, .75, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => { if ((int)element.GetValue(GenerationProperty) == generation) element.Visibility = Visibility.Collapsed; };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
