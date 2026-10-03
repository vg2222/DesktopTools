using System.Windows;
using System.Windows.Media;

namespace DesktopTools.UI;

/// <summary>Shared dimensions and layered surfaces for controls, never capture pixels.</summary>
internal static class DesignTokens
{
    public static readonly DependencyProperty ButtonRadiusProperty = DependencyProperty.RegisterAttached("ButtonRadius", typeof(CornerRadius), typeof(DesignTokens), new FrameworkPropertyMetadata(new CornerRadius(10)));
    public static CornerRadius GetButtonRadius(DependencyObject value) => (CornerRadius)value.GetValue(ButtonRadiusProperty);
    public static void SetButtonRadius(DependencyObject value, CornerRadius radius) => value.SetValue(ButtonRadiusProperty, radius);
    internal const double ControlHeight = 36;
    internal const double CaptionButtonWidth = 40;
    internal const double CaptionButtonHeight = 32;

    internal static void ApplySurfaces(ResourceDictionary resources, bool transparency)
    {
        var surface = ((SolidColorBrush)resources["Surface"]).Color;
        if (!transparency || SystemParameters.HighContrast)
        {
            resources["GlassSurface"] = resources["Surface"];
            resources["GlassRim"] = resources["Stroke"];
            return;
        }

        static Color Lift(Color color, double amount, byte alpha) => Color.FromArgb(alpha,
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));

        bool dark = surface.R + surface.G + surface.B < 384;
        byte highlightAlpha = dark ? (byte)205 : (byte)232;
        byte bodyAlpha = dark ? (byte)188 : (byte)218;
        byte depthAlpha = dark ? (byte)218 : (byte)240;

        var glass = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new(Lift(surface, dark ? .13 : .08, highlightAlpha), 0),
                new(Lift(surface, dark ? .05 : .025, bodyAlpha), .42),
                new(Lift(surface, .01, depthAlpha), 1)
            }
        };
        glass.Freeze(); resources["GlassSurface"] = glass;
        var rim = new LinearGradientBrush(Color.FromArgb(dark ? (byte)92 : (byte)116, 255, 255, 255),
            ((SolidColorBrush)resources["Stroke"]).Color, 70);
        rim.Freeze(); resources["GlassRim"] = rim;
    }
}
