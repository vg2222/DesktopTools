using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopTools.UI;

/// <summary>
/// The "checking" visual: a rainbow rim that runs around the picture and a soft light bar that sweeps over it while text is being analysed.
/// Place it over the picture (it never takes input). <see cref="Start"/> sizes it in the picture's own coordinates.
/// </summary>
public sealed class ScanOverlay : Grid
{
    private readonly Border rim, glow;
    private readonly System.Windows.Shapes.Rectangle beam;
    private readonly RotateTransform rimRotation = new(0, .5, .5);
    private readonly TranslateTransform beamShift = new();

    private static GradientStopCollection Rainbow(byte alpha = 255)
    {
        Color[] colors = [Color.FromRgb(0xFF, 0x3B, 0x6B), Color.FromRgb(0xFF, 0x9F, 0x1C), Color.FromRgb(0xFF, 0xE0, 0x4A), Color.FromRgb(0x3C, 0xE6, 0x7A),
                          Color.FromRgb(0x2E, 0xD4, 0xFF), Color.FromRgb(0x5B, 0x6C, 0xFF), Color.FromRgb(0xB1, 0x4B, 0xFF), Color.FromRgb(0xFF, 0x3B, 0x6B)];
        var stops = new GradientStopCollection();
        for (int i = 0; i < colors.Length; i++) stops.Add(new GradientStop(Color.FromArgb(alpha, colors[i].R, colors[i].G, colors[i].B), i / (double)(colors.Length - 1)));
        return stops;
    }

    public ScanOverlay()
    {
        var rimBrush = new LinearGradientBrush(Rainbow(), new Point(0, 0), new Point(1, 1)) { RelativeTransform = rimRotation };
        var glowBrush = new LinearGradientBrush(Rainbow(110), new Point(0, 0), new Point(1, 1)) { RelativeTransform = rimRotation };
        glow = new Border { BorderBrush = glowBrush, IsHitTestVisible = false };
        rim = new Border { BorderBrush = rimBrush, IsHitTestVisible = false };
        var beamFill = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 255, 255, 255), 0), new(Color.FromArgb(70, 120, 220, 255), .45), new(Color.FromArgb(150, 255, 255, 255), .5),
            new(Color.FromArgb(70, 255, 150, 230), .55), new(Color.FromArgb(0, 255, 255, 255), 1)
        }, 90);
        beam = new System.Windows.Shapes.Rectangle { Fill = beamFill, VerticalAlignment = VerticalAlignment.Top, RenderTransform = beamShift, IsHitTestVisible = false };
        IsHitTestVisible = false; Visibility = Visibility.Collapsed; Opacity = 0;
        Children.Add(beam); Children.Add(glow); Children.Add(rim);
    }

    /// <summary>Show and animate the overlay over a picture of the given size; <paramref name="cornerRadius"/> follows the picture's rounded corners.</summary>
    public void Start(double width, double height, double cornerRadius)
    {
        double shorter = Math.Min(width, height);
        double rimWidth = Math.Max(3, shorter * 0.004), glowWidth = rimWidth * 3.2, beamHeight = Math.Max(70, height * 0.16);
        rim.BorderThickness = new Thickness(rimWidth); rim.CornerRadius = new CornerRadius(cornerRadius);
        glow.BorderThickness = new Thickness(glowWidth); glow.CornerRadius = new CornerRadius(cornerRadius + glowWidth / 2); glow.Margin = new Thickness(0);
        beam.Height = beamHeight;
        Clip = new RectangleGeometry(new Rect(0, 0, width, height), cornerRadius, cornerRadius);
        BeginAnimation(OpacityProperty, null);
        Visibility = Visibility.Visible;
        if (!Motion.Enabled)
        {
            Opacity = 1; beamShift.Y = height / 2 - beamHeight / 2; return;   // reduced motion: a still rainbow rim, no movement
        }
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        rimRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3.2)) { RepeatBehavior = RepeatBehavior.Forever });
        beamShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-beamHeight, height, TimeSpan.FromSeconds(1.7))
        { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }

    /// <summary>Fade out and collapse; safe to call when it was never started.</summary>
    public void Stop()
    {
        void Hide()
        {
            rimRotation.BeginAnimation(RotateTransform.AngleProperty, null); beamShift.BeginAnimation(TranslateTransform.YProperty, null);
            BeginAnimation(OpacityProperty, null); Opacity = 0; Visibility = Visibility.Collapsed;
        }
        if (!Motion.Enabled || Visibility != Visibility.Visible) { Hide(); return; }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(180) };
        fade.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fade);
    }
}
