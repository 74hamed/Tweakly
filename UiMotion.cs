using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Tweakly;

// A single inherited switch respects Windows' animation preference and read-only captures.
public static class UiMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(UiMotion), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static void Reveal(FrameworkElement element)
    {
        if (!GetEnabled(element)) return;
        var offset = new TranslateTransform();
        element.RenderTransform = offset;
        var duration = TimeSpan.FromMilliseconds(160);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration));
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, duration)
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }
}
