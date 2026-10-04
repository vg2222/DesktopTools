using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

/// <summary>How to dress a screenshot for documentation or social media. Sizes are fractions of the image's shorter side so the look
/// stays the same for a 800 px crop and a 4K capture.</summary>
public sealed record BeautifyOptions(bool Enabled = false, string Background = "Ocean", double Padding = 0.07, double Corners = 0.018, bool Shadow = true, bool WindowBar = false)
{
    public static readonly string[] Backgrounds = ["Ocean", "Sunset", "Violet", "Mint", "Graphite", "Paper", "None"];
}

public static class ScreenshotBeautifier
{
    private static (Color From, Color To)? Gradient(string name) => name switch
    {
        "Ocean" => (Color.FromRgb(0x27, 0x64, 0xE7), Color.FromRgb(0x36, 0xDF, 0xF1)),
        "Sunset" => (Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0xFF, 0xC3, 0x71)),
        "Violet" => (Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0xEC, 0x48, 0x99)),
        "Mint" => (Color.FromRgb(0x05, 0x96, 0x69), Color.FromRgb(0x6E, 0xE7, 0xB7)),
        "Graphite" => (Color.FromRgb(0x1F, 0x24, 0x30), Color.FromRgb(0x47, 0x50, 0x63)),
        "Paper" => (Color.FromRgb(0xF4, 0xF5, 0xF7), Color.FromRgb(0xE4, 0xE7, 0xEC)),
        _ => null
    };

    /// <summary>Returns a new image: the screenshot on a background with rounded corners and a soft shadow. The original is not changed.</summary>
    public static BitmapSource Apply(BitmapSource image, BeautifyOptions options)
    {
        if (!options.Enabled) return image;
        double shorter = Math.Min(image.PixelWidth, image.PixelHeight);
        double padding = Math.Round(shorter * Math.Clamp(options.Padding, 0, 0.3));
        double radius = Math.Round(shorter * Math.Clamp(options.Corners, 0, 0.1));
        double bar = options.WindowBar ? Math.Round(Math.Max(28, shorter * 0.06)) : 0;
        double shadow = options.Shadow ? Math.Max(8, shorter * 0.03) : 0;
        // Leave room for the shadow even when padding is small, otherwise it is clipped into a hard edge.
        double margin = Math.Max(padding, shadow * 1.6);
        double width = image.PixelWidth + margin * 2, height = image.PixelHeight + bar + margin * 2;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (Gradient(options.Background) is { } colors)
                dc.DrawRectangle(new LinearGradientBrush(colors.From, colors.To, new Point(0, 0), new Point(1, 1)), null, new Rect(0, 0, width, height));
            var card = new Rect(margin, margin, image.PixelWidth, image.PixelHeight + bar);
            if (shadow > 0)
            {
                // Stacked translucent rounded rectangles: a cheap blur that needs no pixel shader and renders identically everywhere.
                for (int i = 8; i >= 1; i--)
                {
                    double spread = shadow * i / 8;
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)), null,
                        new Rect(card.X - spread, card.Y - spread + shadow * 0.45, card.Width + spread * 2, card.Height + spread * 2), radius + spread, radius + spread);
                }
            }
            dc.PushClip(new RectangleGeometry(card, radius, radius));
            if (bar > 0)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x38)), null, new Rect(card.X, card.Y, card.Width, bar));
                double dot = bar * 0.17;
                Color[] dots = [Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFE, 0xBC, 0x2E), Color.FromRgb(0x28, 0xC8, 0x40)];
                for (int i = 0; i < 3; i++)
                    dc.DrawEllipse(new SolidColorBrush(dots[i]), null, new Point(card.X + bar * 0.5 + i * dot * 2.9, card.Y + bar / 2), dot, dot);
            }
            dc.DrawImage(image, new Rect(card.X, card.Y + bar, image.PixelWidth, image.PixelHeight));
            dc.Pop();
        }
        var target = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        target.Render(visual); target.Freeze();
        return target;
    }
}
