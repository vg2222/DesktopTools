using System;
using System.IO;
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
        var steps = new[] { new GuideStep(source, "Open the app"), new GuideStep(source, "Choose a tool") };
        string output = Path.Combine(Environment.CurrentDirectory, "step-guide-check.png");
        StepGuideComposer.Save(steps, "Example guide", output);
        var saved = new BitmapImage(new Uri(output));
        if (saved.PixelWidth < 800 || saved.PixelHeight < 300) throw new InvalidOperationException("Guide export did not include both numbered steps");
        string longOutput = Path.Combine(Environment.CurrentDirectory, "step-guide-long-title.png");
        StepGuideComposer.Save(steps, new string('W', 120), longOutput);
        var longTitle = new BitmapImage(new Uri(longOutput));
        if (longTitle.PixelHeight <= saved.PixelHeight + 40) throw new InvalidOperationException("Long guide title did not make room before step 1");
        var after = new byte[pixels.Length]; source.CopyPixels(after, 160 * 4, 0);
        if (!after.AsSpan().SequenceEqual(pixels)) throw new InvalidOperationException("Guide export modified the source screenshot");
        var window = new StepGuideWindow(() => source, _ => { });
        try
        {
            window.Show();
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
