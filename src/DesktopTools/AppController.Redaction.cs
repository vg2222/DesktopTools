using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DesktopTools;

internal sealed partial class AppController
{
    private readonly HashSet<ScreenshotEditorWindow> screenshotReviews = [];

    internal async Task HandleCapturedImageAsync(BitmapSource image)
    {
        if (disposed) return;
        if (Settings.AutoRedact.Enabled)
        {
            captureNotice?.Close();
            // Keep the drawing input surface away from the review editor; ink stays saved.
            machine.Hide(); overlay?.Hide(); Palette?.Hide();
            var reviewed = pendingCaptureWindow; pendingCaptureWindow = null;
            OpenAutoRedactReview(image, reviewed); return;
        }
        var window = pendingCaptureWindow; pendingCaptureWindow = null;
        LastCapture = image; CaptureHistory.Add(image); captureNameContext = (image, window);
        if (Settings.CaptureOutput == "Save") SaveLast();
        else if (Settings.CaptureOutput == CaptureAutoSave.Mode) await CopyAndSaveAsync(image, window: window);
        else { await CopyAsync(image); ShowCaptureNotice(); }
    }

    /// <summary>Copies the screenshot and writes it to the save location in one step, with no dialog. Existing files are never replaced.</summary>
    internal async Task<string?> CopyAndSaveAsync(BitmapSource image, Func<BitmapSource, Task<ClipboardResult>>? copy = null, WindowDescription? window = null)
    {
        // Only the file name waits for a smart name (and its OCR headline); the clipboard is written straight away.
        var naming = AutoSaveStemAsync(image, window, DateTime.Now);
        string directory = CaptureAutoSave.ResolveDirectory(Settings.SaveDirectory, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        // The picture is frozen, so the PNG is encoded off the UI thread while the clipboard is written.
        var saving = Task.Run(async () =>
        {
            string stem = await naming.ConfigureAwait(false);
            Directory.CreateDirectory(directory);
            for (int attempt = 0; ; attempt++)
            {
                string path = CaptureAutoSave.UniquePath(directory, stem, File.Exists);
                try
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream);
                    return path;
                }
                catch (IOException) when (attempt < 3 && File.Exists(path)) { } // another capture took the name first
            }
        });
        ClipboardResult copied;
        try { copied = await (copy?.Invoke(image) ?? ClipboardService.SetImageAsync(image, 4000)); }
        catch (Exception ex) { copied = new ClipboardResult(false, ex.Message); }
        string? saved = null; string? saveError = null;
        try { saved = await saving; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException or ArgumentException) { saveError = ex.Message; }
        if (disposed) return saved;
        if (saved != null && copied.Success) ShowCaptureNotice(L.T("Screenshot copied and saved"), saved);
        else if (saved != null) { Report(L.F($"Screenshot saved to {saved}, but the clipboard is busy. ") + copied.Error); ShowCaptureNotice(null, saved); }
        else if (copied.Success) { Report(L.T("Screenshot copied, but it could not be saved: ") + saveError); ShowCaptureNotice(); }
        else Report(L.T("Screenshot could not be copied or saved: ") + saveError);
        return saved;
    }

    private void OpenAutoRedactReview(BitmapSource image, WindowDescription? window = null)
    {
        ScreenshotEditorWindow? review = null;
        review = new ScreenshotEditorWindow(image, _ => { }, Report, autoRedact: Settings.AutoRedact, reviewBeforeOutput: true,
            exportAsync: (result, action) => ExportReviewedImageAsync(result, action, review!, cancellationToken: review!.ExportCancellationToken, window: window),
            translate: Settings.TranslationEnabled ? OpenTextToolsForTranslation : null);
        screenshotReviews.Add(review); review.Closed += (_, _) => screenshotReviews.Remove(review);
        review.SourceInitialized += (_, _) => NativeWindowService.ApplyBackdrop(review, Dark, Settings.Transparency);
        NativeWindowService.ShowForeground(review);
    }

    internal async Task<bool> ExportReviewedImageAsync(BitmapSource image, ScreenshotExportAction action, Window owner,
        Func<SaveFileDialog, Window, bool?>? choosePath = null, CancellationToken cancellationToken = default,
        Action<DataObject>? writeClipboard = null, WindowDescription? window = null)
    {
        if (disposed || cancellationToken.IsCancellationRequested) return false;
        bool success = action switch
        {
            ScreenshotExportAction.Copy => await TryCopyAsync(image, cancellationToken, writeClipboard),
            ScreenshotExportAction.Save => TrySaveImage(image, owner, choosePath ?? ((dialog, parent) => dialog.ShowDialog(parent)), cancellationToken, window),
            _ => false
        };
        if (!success || disposed || cancellationToken.IsCancellationRequested) return false;
        LastCapture = image; CaptureHistory.Add(image); captureNameContext = (image, window); ShowCaptureNotice(); return true;
    }
    private void CloseScreenshotReviews() { foreach (var review in screenshotReviews.ToArray()) review.Close(); screenshotReviews.Clear(); }
}
