using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Localization;

namespace DesktopTools.Extras;

internal sealed record GuideStep(BitmapSource Image, string Caption);
internal sealed record GuideLayoutOptions(int Columns = 3, bool DarkBackground = false);

internal static class StepGuideComposer
{
    private const int Width = 1600;
    private const int Margin = 60;
    private const int Gap = 24;
    private const int CardPadding = 20;
    private const int MaximumSteps = 20;
    private const int MaximumHeight = 16000;

    private sealed record Layout(GuideStep Step, int ImageWidth, int ImageHeight, FormattedText Caption, int Height);

    internal static void Save(IReadOnlyList<GuideStep> steps, string title, string path, GuideLayoutOptions? options = null)
    {
        if (steps.Count is < 1 or > MaximumSteps) throw new ArgumentException(L.T("A guide needs 1 to 20 screenshots."), nameof(steps));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(L.T("Choose where to save the guide."), nameof(path));
        options ??= new GuideLayoutOptions();
        if (options.Columns is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(options));
        title = string.IsNullOrWhiteSpace(title) ? L.T("Step-by-step guide") : title.Trim();
        if (title.Length > 120) throw new ArgumentException(L.T("The guide title is too long."), nameof(title));

        var background = new SolidColorBrush(options.DarkBackground ? Color.FromRgb(6, 6, 6) : Colors.White);
        var cardColor = new SolidColorBrush(options.DarkBackground ? Color.FromRgb(28, 30, 36) : Color.FromRgb(242, 245, 250));
        var textColor = new SolidColorBrush(options.DarkBackground ? Color.FromRgb(244, 246, 250) : Color.FromRgb(24, 30, 42));
        var mutedColor = new SolidColorBrush(options.DarkBackground ? Color.FromRgb(177, 184, 198) : Color.FromRgb(89, 99, 116));
        var accent = new SolidColorBrush(Color.FromRgb(43, 104, 236));
        int cardWidth = (Width - Margin * 2 - Gap * (options.Columns - 1)) / options.Columns;
        int imageWidth = cardWidth - CardPadding * 2;
        int maximumImageHeight = options.Columns == 1 ? 760 : options.Columns == 2 ? 490 : 310;
        int captionSize = options.Columns == 3 ? 20 : 23;
        var titleText = Text(WrapTitle(title, textColor), 38, Width - Margin * 2, textColor, bold: true);
        int firstRowY = Math.Max(142, 56 + (int)Math.Ceiling(titleText.Height) + 42);
        var layouts = new List<Layout>(steps.Count);
        var rowHeights = new List<int>();
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.Image.PixelWidth <= 0 || step.Image.PixelHeight <= 0) throw new ArgumentException(L.T("A screenshot has invalid dimensions."), nameof(steps));
            if (step.Caption.Length > 500) throw new ArgumentException(L.T("A step caption is too long."), nameof(steps));
            double scale = Math.Min((double)imageWidth / step.Image.PixelWidth, (double)maximumImageHeight / step.Image.PixelHeight);
            int renderedWidth = Math.Max(1, (int)Math.Round(step.Image.PixelWidth * scale));
            int renderedHeight = Math.Max(1, (int)Math.Round(step.Image.PixelHeight * scale));
            var caption = Text(string.IsNullOrWhiteSpace(step.Caption) ? " " : step.Caption.Trim(), captionSize, imageWidth, mutedColor);
            int height = CardPadding + 40 + 14 + renderedHeight + 18 + (int)Math.Ceiling(caption.Height) + CardPadding;
            layouts.Add(new Layout(step, renderedWidth, renderedHeight, caption, height));
            int row = i / options.Columns;
            if (row == rowHeights.Count) rowHeights.Add(height); else rowHeights[row] = Math.Max(rowHeights[row], height);
        }
        int totalHeight = checked(firstRowY + rowHeights.Sum() + Math.Max(0, rowHeights.Count - 1) * Gap + Margin);
        if (totalHeight > MaximumHeight) throw new ArgumentException(L.T("This guide is too tall. Export fewer screenshots."), nameof(steps));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, Width, totalHeight));
            dc.DrawText(titleText, new Point(Margin, 56));
            int y = firstRowY;
            for (int row = 0; row < rowHeights.Count; row++)
            {
                for (int column = 0; column < options.Columns; column++)
                {
                    int index = row * options.Columns + column;
                    if (index >= layouts.Count) break;
                    var item = layouts[index];
                    int x = Margin + column * (cardWidth + Gap);
                    dc.DrawRoundedRectangle(cardColor, null, new Rect(x, y, cardWidth, rowHeights[row]), 18, 18);
                    dc.DrawRoundedRectangle(accent, null, new Rect(x + CardPadding, y + CardPadding, 36, 36), 10, 10);
                    dc.DrawText(Text((index + 1).ToString(CultureInfo.InvariantCulture), 20, 30, Brushes.White, bold: true),
                        new Point(x + CardPadding + 9, y + CardPadding + 5));
                    dc.DrawText(Text(L.T("Step") + " " + (index + 1), 21, imageWidth - 52, textColor, bold: true),
                        new Point(x + CardPadding + 52, y + CardPadding + 6));
                    int pictureY = y + CardPadding + 54;
                    int pictureX = x + CardPadding + (imageWidth - item.ImageWidth) / 2;
                    dc.PushClip(new RectangleGeometry(new Rect(pictureX, pictureY, item.ImageWidth, item.ImageHeight), 12, 12));
                    dc.DrawImage(item.Step.Image, new Rect(pictureX, pictureY, item.ImageWidth, item.ImageHeight));
                    dc.Pop();
                    dc.DrawText(item.Caption, new Point(x + CardPadding, pictureY + item.ImageHeight + 18));
                }
                y += rowHeights[row] + Gap;
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

    private static FormattedText Text(string value, double size, double width, Brush brush, bool bold = false) =>
        new(value, L.Culture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, 1) { MaxTextWidth = width };

    private static string WrapTitle(string title, Brush brush)
    {
        var result = new StringBuilder(); var line = new StringBuilder();
        foreach (char character in title)
        {
            if (character is '\r' or '\n') continue;
            string next = line.ToString() + character;
            if (line.Length > 0 && Text(next, 38, 100000, brush, bold: true).WidthIncludingTrailingWhitespace > Width - Margin * 2)
            {
                result.AppendLine(line.ToString().TrimEnd()); line.Clear();
                if (character == ' ') continue;
            }
            line.Append(character);
        }
        result.Append(line.ToString()); return result.ToString();
    }
}
