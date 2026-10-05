using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;
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
            OpenAutoRedactReview(image); return;
        }
        LastCapture = image; RememberCapture(image);
        if (Settings.CaptureOutput == "Save") SaveLast();
        else { await CopyAsync(image); ShowCaptureNotice(); }
    }

    private void OpenAutoRedactReview(BitmapSource image)
    {
        ScreenshotEditorWindow? review = null;
        review = new ScreenshotEditorWindow(image, _ => { }, Report, autoRedact: Settings.AutoRedact, reviewBeforeOutput: true,
            exportAsync: (result, action) => ExportReviewedImageAsync(result, action, review!, cancellationToken: review!.ExportCancellationToken),
            translate: Settings.TranslationEnabled ? OpenTextToolsForTranslation : null);
        screenshotReviews.Add(review); review.Closed += (_, _) => screenshotReviews.Remove(review);
        review.SourceInitialized += (_, _) => NativeWindowService.ApplyBackdrop(review, Dark, Settings.Transparency);
        NativeWindowService.ShowForeground(review);
    }

    internal async Task<bool> ExportReviewedImageAsync(BitmapSource image, ScreenshotExportAction action, Window owner,
        Func<SaveFileDialog, Window, bool?>? choosePath = null, CancellationToken cancellationToken = default,
        Action<DataObject>? writeClipboard = null)
    {
        if (disposed || cancellationToken.IsCancellationRequested) return false;
        bool success = action switch
        {
            ScreenshotExportAction.Copy => await TryCopyAsync(image, cancellationToken, writeClipboard),
            ScreenshotExportAction.Save => TrySaveImage(image, owner, choosePath ?? ((dialog, window) => dialog.ShowDialog(window)), cancellationToken),
            _ => false
        };
        if (!success || disposed || cancellationToken.IsCancellationRequested) return false;
        LastCapture = image; RememberCapture(image); ShowCaptureNotice(); return true;
    }
    private void CloseScreenshotReviews() { foreach (var review in screenshotReviews.ToArray()) review.Close(); screenshotReviews.Clear(); }
}
