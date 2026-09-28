using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Localization;

namespace DesktopTools.Extras;

internal sealed record GuideStep(BitmapSource Image, string Caption);

internal static class StepGuideComposer
{
    private const int Width = 1280;
    private const int Margin = 54;
    private const int ImageWidth = Width - Margin * 2;
    private const int MaxImageHeight = 860;
    private const int MaximumSteps = 20;
    private const int MaximumHeight = 16000;

    internal static void Save(IReadOnlyList<GuideStep> steps, string title, string path)
    {
        if (steps.Count is < 1 or > MaximumSteps) throw new ArgumentException(L.T("A guide needs 1 to 20 screenshots."), nameof(steps));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(L.T("Choose where to save the guide."), nameof(path));
        title = string.IsNullOrWhiteSpace(title) ? L.T("Step-by-step guide") : title.Trim();
        if (title.Length > 120) throw new ArgumentException(L.T("The guide title is too long."), nameof(title));
        var titleText = Text(WrapTitle(title), 35, ImageWidth, bold: true);
        int firstStepY = Math.Max(136, 52 + (int)Math.Ceiling(titleText.Height) + 40);
        var layouts = new List<(GuideStep Step, int ImageHeight, FormattedText Caption, int Height)>();
        int totalHeight = firstStepY + 14;
        foreach (var step in steps)
        {
            if (step.Image.PixelWidth <= 0 || step.Image.PixelHeight <= 0) throw new ArgumentException(L.T("A screenshot has invalid dimensions."), nameof(steps));
            if (step.Caption.Length > 500) throw new ArgumentException(L.T("A step caption is too long."), nameof(steps));
            int imageHeight = Math.Max(1, (int)Math.Round(Math.Min((double)ImageWidth / step.Image.PixelWidth, (double)MaxImageHeight / step.Image.PixelHeight) * step.Image.PixelHeight));
            var caption = Text(string.IsNullOrWhiteSpace(step.Caption) ? " " : step.Caption.Trim(), 23, ImageWidth);
            int height = 72 + imageHeight + (int)Math.Ceiling(caption.Height) + 42;
            totalHeight = checked(totalHeight + height);
            if (totalHeight > MaximumHeight) throw new ArgumentException(L.T("This guide is too tall. Export fewer screenshots."), nameof(steps));
            layouts.Add((step, imageHeight, caption, height));
        }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Width, totalHeight));
            dc.DrawText(titleText, new Point(Margin, 52));
            int y = firstStepY;
            for (int i = 0; i < layouts.Count; i++)
            {
                var item = layouts[i];
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(239, 244, 255)), null, new Rect(Margin, y, 40, 40), 12, 12);
                dc.DrawText(Text((i + 1).ToString(CultureInfo.InvariantCulture), 21, 34, bold: true), new Point(Margin + 10, y + 6));
                dc.DrawText(Text(L.T("Step") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), 22, ImageWidth - 56, bold: true), new Point(Margin + 56, y + 7));
                int pictureY = y + 58;
                dc.PushClip(new RectangleGeometry(new Rect(Margin, pictureY, ImageWidth, item.ImageHeight), 14, 14));
                double imageWidth = Math.Min(ImageWidth, (double)item.ImageHeight / item.Step.Image.PixelHeight * item.Step.Image.PixelWidth);
                dc.DrawImage(item.Step.Image, new Rect(Margin + (ImageWidth - imageWidth) / 2, pictureY, imageWidth, item.ImageHeight));
                dc.Pop();
                dc.DrawText(item.Caption, new Point(Margin, pictureY + item.ImageHeight + 18));
                y += item.Height;
            }
        }
        var bitmap = new RenderTargetBitmap(Width, totalHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) encoder.Save(file);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static FormattedText Text(string value, double size, double width, bool bold = false)
    {
        var text = new FormattedText(value, L.Culture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, Brushes.Black, 1) { MaxTextWidth = width };
        return text;
    }
    private static string WrapTitle(string title)
    {
        var result = new StringBuilder(); var line = new StringBuilder();
        foreach (char character in title)
        {
            if (character is '\r' or '\n') continue;
            string next = line.ToString() + character;
            if (line.Length > 0 && Text(next, 35, 100000, bold: true).WidthIncludingTrailingWhitespace > ImageWidth)
            {
                result.AppendLine(line.ToString().TrimEnd()); line.Clear();
                if (character == ' ') continue;
            }
            line.Append(character);
        }
        result.Append(line.ToString());
        return result.ToString();
    }
}
