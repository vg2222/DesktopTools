using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;

internal static class StepGuideChecks
{
    internal static System.Threading.Tasks.Task RunAsync()
    {
        var source = new WriteableBitmap(160, 90, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[160 * 90 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 25; pixels[i + 1] = 85; pixels[i + 2] = 190; pixels[i + 3] = 255; }
        source.WritePixels(new System.Windows.Int32Rect(0, 0, 160, 90), pixels, 160 * 4, 0);
        var steps = new[] { new GuideStep(source, "Open the app"), new GuideStep(source, "Choose a tool"),
            new GuideStep(source, "Capture the screen"), new GuideStep(source, "Save the guide") };
        string output = Path.Combine(Environment.CurrentDirectory, "step-guide-check.png");
        StepGuideComposer.Save(steps, "Example guide", output);
        var saved = new BitmapImage(new Uri(output));
        if (saved.PixelWidth != 2000 || saved.PixelHeight < 600) throw new InvalidOperationException("Guide export did not include two rows of steps");
        string singleOutput = Path.Combine(Environment.CurrentDirectory, "step-guide-single-column.png");
        StepGuideComposer.Save(steps, "Example guide", singleOutput, new GuideLayoutOptions(1, Color.FromRgb(6, 6, 6)));
        var singleColumn = new BitmapImage(new Uri(singleOutput));
        if (singleColumn.PixelHeight <= saved.PixelHeight) throw new InvalidOperationException("Three-column guide did not reduce export height");
        string wideOutput = Path.Combine(Environment.CurrentDirectory, "step-guide-six-columns.png");
        var customBackground = Color.FromRgb(44, 56, 78);
        StepGuideComposer.Save(steps, "Example guide", wideOutput, new GuideLayoutOptions(6, customBackground));
        var wide = new BitmapImage(new Uri(wideOutput));
        var corner = ScreenshotPixel.Read(wide, 0, 0);
        if (wide.PixelWidth <= saved.PixelWidth ||
            corner.R != customBackground.R || corner.G != customBackground.G || corner.B != customBackground.B)
            throw new InvalidOperationException("Six-column export or custom background is incorrect");
        string longOutput = Path.Combine(Environment.CurrentDirectory, "step-guide-long-title.png");
        StepGuideComposer.Save(steps, new string('W', 120), longOutput);
        var longTitle = new BitmapImage(new Uri(longOutput));
        if (longTitle.PixelHeight <= saved.PixelHeight + 40) throw new InvalidOperationException("Long guide title did not make room before step 1");
        var after = new byte[pixels.Length]; source.CopyPixels(after, 160 * 4, 0);
        if (!after.AsSpan().SequenceEqual(pixels)) throw new InvalidOperationException("Guide export modified the source screenshot");
        var history = new CaptureHistory(); history.Add(source);
        string folder = Path.Combine(Environment.CurrentDirectory, "guide-folder-fixture"); Directory.CreateDirectory(folder);
        for (int i = 0; i < 4; i++) File.Copy(output, Path.Combine(folder, $"capture-{i}.png"), overwrite: true);
        var window = new StepGuideWindow(() => source, () => history.Entries, folder, _ => { });
        try
        {
            window.Show();
            if (window.Content is not System.Windows.Controls.Border shell ||
                shell.Background is not SolidColorBrush normalSurface ||
                Application.Current.Resources["Surface"] is not SolidColorBrush appSurface || normalSurface.Color != appSurface.Color)
                throw new InvalidOperationException("Feature window ignored the app background");
            var originalSurface = Application.Current.Resources["Surface"];
            try
            {
                Application.Current.Resources["Surface"] = new SolidColorBrush(Color.FromRgb(31, 43, 67));
                if (shell.Background is not SolidColorBrush customSurface || customSurface.Color != Color.FromRgb(31, 43, 67))
                    throw new InvalidOperationException("Feature window did not follow the custom background");
            }
            finally { Application.Current.Resources["Surface"] = originalSurface; }
            var folderTiles = (System.Windows.Controls.Primitives.UniformGrid)typeof(StepGuideWindow)
                .GetField("folderTiles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            if (folderTiles.Columns != 2 || folderTiles.Children.Count != 4)
                throw new InvalidOperationException("Folder screenshots are not shown in two columns");
            for (int i = 0; i < 4; i++)
                typeof(StepGuideWindow).GetMethod("AddLatest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
            window.UpdateLayout();
            if (!System.Windows.Media.VisualTreeHelper.GetChildrenCount(window).Equals(1)) throw new InvalidOperationException("Guide builder did not render");
            var preview = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            preview.Render(window);
            var previewEncoder = new PngBitmapEncoder(); previewEncoder.Frames.Add(BitmapFrame.Create(preview));
            using var previewOutput = File.Create(Path.Combine(Environment.CurrentDirectory, "step-guide-builder.png")); previewEncoder.Save(previewOutput);
        }
        finally { window.Close(); }
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
