using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;

namespace DesktopTools.UI;

public static class CaptureComposition
{
    /// <summary>Offset and crop are physical pixels relative to the captured desktop's origin. Annotation coordinates are monitor-local DIPs.</summary>
    public static BitmapSource Composite(BitmapSource desktop, IEnumerable<Annotation> annotations, double scaleX, double scaleY, Vector annotationOffsetPixels, Int32Rect crop)
    {
        ArgumentNullException.ThrowIfNull(desktop); ArgumentNullException.ThrowIfNull(annotations);
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0) throw new ArgumentOutOfRangeException(nameof(scaleX));
        if (!double.IsFinite(annotationOffsetPixels.X) || !double.IsFinite(annotationOffsetPixels.Y)) throw new ArgumentOutOfRangeException(nameof(annotationOffsetPixels));
        if (crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 || (long)crop.X + crop.Width > desktop.PixelWidth || (long)crop.Y + crop.Height > desktop.PixelHeight) throw new ArgumentOutOfRangeException(nameof(crop));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(desktop, new Rect(-crop.X, -crop.Y, desktop.PixelWidth, desktop.PixelHeight));
            dc.PushTransform(new TranslateTransform(annotationOffsetPixels.X - crop.X, annotationOffsetPixels.Y - crop.Y));
            dc.PushTransform(new ScaleTransform(scaleX, scaleY));
            AnnotationRenderer.Draw(dc, annotations); dc.Pop(); dc.Pop();
        }
        var result = new RenderTargetBitmap(crop.Width, crop.Height, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
}
