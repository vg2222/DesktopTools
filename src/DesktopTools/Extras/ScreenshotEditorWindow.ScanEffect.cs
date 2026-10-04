using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopTools.Extras;

public sealed partial class ScreenshotEditorWindow
{
    // "Check screenshot" visual: a rainbow rim that runs around the picture and a soft light bar that sweeps over it while the text
    // is being analysed. It lives inside the scaled canvas so it hugs the rounded corners of the (optionally styled) picture.
    private Grid? _scanOverlay;
    private Border? _scanRim, _scanGlow;
    private System.Windows.Shapes.Rectangle? _scanBeam;
    private RotateTransform? _rimRotation;
    private TranslateTransform? _beamShift;

    private static GradientStopCollection Rainbow(byte alpha = 255)
    {
        Color[] colors = [Color.FromRgb(0xFF, 0x3B, 0x6B), Color.FromRgb(0xFF, 0x9F, 0x1C), Color.FromRgb(0xFF, 0xE0, 0x4A), Color.FromRgb(0x3C, 0xE6, 0x7A),
                          Color.FromRgb(0x2E, 0xD4, 0xFF), Color.FromRgb(0x5B, 0x6C, 0xFF), Color.FromRgb(0xB1, 0x4B, 0xFF), Color.FromRgb(0xFF, 0x3B, 0x6B)];
        var stops = new GradientStopCollection();
        for (int i = 0; i < colors.Length; i++) stops.Add(new GradientStop(Color.FromArgb(alpha, colors[i].R, colors[i].G, colors[i].B), i / (double)(colors.Length - 1)));
        return stops;
    }

    private void BuildScanOverlay(Grid host)
    {
        _rimRotation = new RotateTransform(0, .5, .5);
        var rimBrush = new LinearGradientBrush(Rainbow(), new Point(0, 0), new Point(1, 1)) { RelativeTransform = _rimRotation };
        var glowBrush = new LinearGradientBrush(Rainbow(110), new Point(0, 0), new Point(1, 1)) { RelativeTransform = _rimRotation };
        _scanGlow = new Border { BorderBrush = glowBrush, IsHitTestVisible = false };
        _scanRim = new Border { BorderBrush = rimBrush, IsHitTestVisible = false };
        _beamShift = new TranslateTransform();
        var beamFill = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 255, 255, 255), 0), new(Color.FromArgb(70, 120, 220, 255), .45), new(Color.FromArgb(150, 255, 255, 255), .5),
            new(Color.FromArgb(70, 255, 150, 230), .55), new(Color.FromArgb(0, 255, 255, 255), 1)
        }, 90);
        _scanBeam = new System.Windows.Shapes.Rectangle { Fill = beamFill, VerticalAlignment = VerticalAlignment.Top, RenderTransform = _beamShift, IsHitTestVisible = false };
        _scanOverlay = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Opacity = 0 };
        _scanOverlay.Children.Add(_scanBeam); _scanOverlay.Children.Add(_scanGlow); _scanOverlay.Children.Add(_scanRim);
        host.Children.Add(_scanOverlay);
    }

    private void StartScanEffect()
    {
        if (_scanOverlay == null || _scanRim == null || _scanGlow == null || _scanBeam == null || _rimRotation == null || _beamShift == null) return;
        double width = _image.PixelWidth, height = _image.PixelHeight + (_beautify.Enabled ? ScreenshotBeautifier.Measure(_image.PixelWidth, _image.PixelHeight, _beautify).Bar : 0);
        double shorter = Math.Min(width, height);
        double radius = _beautify.Enabled ? Math.Max(ScreenshotBeautifier.Measure(_image.PixelWidth, _image.PixelHeight, _beautify).Radius, 6) : Math.Max(8, shorter * 0.015);
        double rim = Math.Max(3, shorter * 0.004), glow = rim * 3.2, beam = Math.Max(70, height * 0.16);
        _scanRim.BorderThickness = new Thickness(rim); _scanRim.CornerRadius = new CornerRadius(radius);
        _scanGlow.BorderThickness = new Thickness(glow); _scanGlow.CornerRadius = new CornerRadius(radius + glow / 2); _scanGlow.Margin = new Thickness(0);
        _scanBeam.Height = beam;
        _scanOverlay.Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        _scanOverlay.BeginAnimation(OpacityProperty, null);
        _scanOverlay.Visibility = Visibility.Visible;
        if (!Motion.Enabled)
        {
            _scanOverlay.Opacity = 1; _beamShift.Y = height / 2 - beam / 2; return;   // reduced motion: a still rainbow rim, no movement
        }
        _scanOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        _rimRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3.2)) { RepeatBehavior = RepeatBehavior.Forever });
        _beamShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-beam, height, TimeSpan.FromSeconds(1.7))
        { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }

    private void StopScanEffect()
    {
        if (_scanOverlay == null || _rimRotation == null || _beamShift == null) return;
        void Hide()
        {
            _rimRotation.BeginAnimation(RotateTransform.AngleProperty, null); _beamShift.BeginAnimation(TranslateTransform.YProperty, null);
            _scanOverlay.BeginAnimation(OpacityProperty, null); _scanOverlay.Opacity = 0; _scanOverlay.Visibility = Visibility.Collapsed;
        }
        if (!Motion.Enabled || _scanOverlay.Visibility != Visibility.Visible) { Hide(); return; }
        var fade = new DoubleAnimation(_scanOverlay.Opacity, 0, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(180) };
        fade.Completed += (_, _) => Hide();
        _scanOverlay.BeginAnimation(OpacityProperty, fade);
    }
}
