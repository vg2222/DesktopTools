using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DesktopTools;

/// <summary>Screenshot file names: the date-and-time default, or Smart names built from the captured window (Settings → Capture → File names).</summary>
internal sealed partial class AppController
{
    internal const string SmartFileNamesChoice = "Smart";
    private static readonly TimeSpan HeadlineBudget = TimeSpan.FromMilliseconds(800);
    /// <summary>The window under the region just captured, handed from <see cref="CaptureAsync"/> to <see cref="HandleCapturedImageAsync"/>.</summary>
    private WindowDescription? pendingCaptureWindow;
    /// <summary>The window behind the latest capture, so a later Save dialog can suggest the same smart name.</summary>
    private (BitmapSource Image, WindowDescription? Window)? captureNameContext;

    private bool SmartNamesOn => Settings.CaptureFileNames == SmartFileNamesChoice;

    /// <summary>Looks up the window under the centre of the captured region, only when Smart names are on. DesktopTools' own windows are skipped.</summary>
    private WindowDescription? DescribeCapturedWindow(MonitorInfo monitor, Int32Rect pixels, int imageWidth, int imageHeight)
    {
        if (!SmartNamesOn || imageWidth <= 0 || imageHeight <= 0) return null;
        double scaleX = monitor.Bounds.Width / imageWidth, scaleY = monitor.Bounds.Height / imageHeight;
        int x = (int)Math.Round(monitor.Bounds.X + (pixels.X + pixels.Width / 2.0) * scaleX);
        int y = (int)Math.Round(monitor.Bounds.Y + (pixels.Y + pixels.Height / 2.0) * scaleY);
        try { return NativeWindowService.DescribeTopWindowAt(x, y, Environment.ProcessId); }
        catch (Exception) { return null; } // a name is a convenience; never fail a capture over it
    }

    /// <summary>The file name stem for a capture: rules only, nothing leaves the PC. Flagged text never reaches the name.</summary>
    internal string CaptureFileStem(WindowDescription? window, DateTime time, string? headline) =>
        SmartFileNames.Compose(window?.App, window?.Title, headline, time, IsSensitiveNameText);

    /// <summary>The name the Save dialog suggests. No screenshot reading here: the dialog opens at once.</summary>
    internal string SuggestedSaveName(BitmapSource image, WindowDescription? window, DateTime time) =>
        (SmartNamesOn ? CaptureFileStem(window, time, null) : SmartFileNames.Fallback(time)) + ".png";

    private WindowDescription? WindowFor(BitmapSource image) =>
        captureNameContext is { } context && ReferenceEquals(context.Image, image) ? context.Window : null;

    private async Task<string> AutoSaveStemAsync(BitmapSource image, WindowDescription? window, DateTime time)
    {
        if (!SmartNamesOn) return SmartFileNames.Fallback(time);
        try
        {
            string? headline = null;
            if (!SmartFileNames.TitleIsUsable(window?.App, window?.Title)) headline = await ReadHeadlineAsync(image);
            return CaptureFileStem(window, time, headline);
        }
        catch (Exception) { return SmartFileNames.Fallback(time); } // a naming problem never stops the save
    }

    /// <summary>One quick OCR pass, bounded to 800 ms. A timeout, a missing language or any OCR error means no headline.</summary>
    private async Task<string?> ReadHeadlineAsync(BitmapSource image)
    {
        string? tag;
        try { tag = LocalOcr.SelectLanguage(LocalOcr.Languages, Settings.ScreenTextLanguage)?.Tag; }
        catch (Exception) { return null; }
        if (tag == null) return null;
        using var budget = new CancellationTokenSource(HeadlineBudget);
        Task<string?> reading;
        try { reading = LocalOcr.HeadlineAsync(image, tag, budget.Token); }
        catch (Exception) { return null; }
        if (await Task.WhenAny(reading, Task.Delay(HeadlineBudget)) != reading)
        {
            budget.Cancel();
            _ = reading.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return null;
        }
        try { return await reading; }
        catch (Exception) { return null; }
    }

    /// <summary>True when the sensitive-data check flags the text (an e-mail address, phone number, key and so on). Errors count as sensitive.</summary>
    internal static bool IsSensitiveNameText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var words = new List<OcrWordBox>();
        double x = 0;
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            double width = Math.Max(1, word.Length) * 10;
            words.Add(new OcrWordBox(word, new Rect(x, 0, width, 20), 0));
            x += width + 6;
        }
        if (words.Count == 0) return false;
        try { return SensitiveDataDetector.Detect(new OcrLayout((int)Math.Ceiling(x) + 1, 20, words), AutoRedactOptions.AvailableCategories).Count > 0; }
        catch (Exception) { return true; }
    }
}
