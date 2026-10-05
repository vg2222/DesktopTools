using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools;
using DesktopTools.UI;
using DesktopTools.Localization;

internal static class AutoRedactChecks
{
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    internal static async Task RunAsync()
    {
        var pixels = Enumerable.Repeat((byte)255, 400 * 200 * 4).ToArray();
        var sample = BitmapSource.Create(400, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 1600); sample.Freeze();
        BitmapSource? exported = null;
        var reported = new List<string>();
        var window = new ScreenshotEditorWindow(sample, result => exported = result, reported.Add);
        try
        {
            window.Show(); await Task.Delay(50); window.UpdateLayout();
            var editorMonitor = DesktopTools.Native.MonitorService.GetForWindow(window);
            Check(window.WindowState == WindowState.Normal && Math.Abs(window.ActualWidth * editorMonitor.ScaleX - editorMonitor.WorkingArea.Width * .75) <= 2 &&
                Math.Abs(window.ActualHeight * editorMonitor.ScaleY - editorMonitor.WorkingArea.Height * .75) <= 2, "Editor did not open at 75 percent of its monitor work area");
            Check(Children(window).OfType<Button>().Any(b => b.Name == "FindSensitiveData"), "Manual sensitive-data button is absent");
            var property = window.GetType().GetProperty("AnalyzeSensitiveData", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var finding = new SensitiveFinding(Guid.NewGuid(), ["email"], new(20, 20, 100, 30), "••••");
            var findings = Enumerable.Range(0, 7).Select(i => finding with { Id = Guid.NewGuid(), Bounds = new(20, 20 + i * 22, 100, 18) }).ToArray();
            property.SetValue(window, (Func<BitmapSource, string, IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyList<SensitiveFinding>>>)( (_, _, _, _) => Task.FromResult<IReadOnlyList<SensitiveFinding>>(findings) ));
            await (Task)window.GetType().GetMethod("FindSensitiveDataAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            var doc = Field<ScreenshotEditDocument>(window, "_document");
            Check(doc.Items.Count == 0, "Detection silently edited the image");
            var notice = Children(window).OfType<Border>().Single(b => b.Name == "DetectionNotice");
            Check(notice.Visibility == Visibility.Visible && Children(notice).OfType<TextBlock>().Any(t => t.Text.Contains("miss")), "No notice that detection can miss data after the check");
            Check(reported.Any(m => m.Contains("miss")), "No notification that detection can miss data after the check");
            var list = Children(window).OfType<ListBox>().Single(l => l.Name == "SensitiveFindings"); list.SelectedIndex = 0;
            Check(ReferenceEquals(list.Background, window.FindResource("Card")), "Findings list does not use DesktopTools theme");
            var hideAll = Children(window).OfType<Button>().SingleOrDefault(b => b.Name == "HideAllSensitiveData");
            Check(hideAll?.IsEnabled == true, "Hide all found button is absent or disabled");
            list.SelectedIndex = -1;
            hideAll!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(doc.Items.Count == 7 && doc.Items.All(a => a.Kind == AnnotationKind.Redaction), "Hide all did not create all editable covers without selection");
            Check(!Field<RedactionReviewState>(window, "_reviewState").HasUnresolved, "Hide all left unresolved suggestions");
            var image = doc.Export(); var actual = new byte[pixels.Length]; image.CopyPixels(actual, 1600, 0);
            Check(actual[(25 * 400 + 25) * 4] == 0 && actual[0] == 255, "Accepted cover/export has wrong bounds");
            Check(exported == null, "Accepting a suggestion unexpectedly copied/exported");
            doc.Undo(); Check(doc.Items.Count == 0, "Undo did not restore the original");
            string guidance = L.T("This image is too large for sensitive-data analysis. Crop it and try again.");
            window.AnalyzeSensitiveData = (_, _, _, _) => throw new SensitiveDataAnalysisException(guidance);
            await window.FindSensitiveDataAsync();
            Check(Field<TextBlock>(window, "_scanStatus").Text == guidance, "Analysis limit lost actionable crop guidance");
            Check(notice.Visibility != Visibility.Visible, "The notice stayed after a failed check, which found nothing to be incomplete about");
            var manualState = Field<RedactionReviewState>(window, "_reviewState");
            Check(manualState.CanExport && manualState.NeedsScan, "A failed check blocked Copy and Save in the ordinary editor");
            var skip = Field<Button>(window, "_skipButton");
            Check(skip.Visibility != Visibility.Visible, "'Continue without checking' is shown in the ordinary editor");
            window.AnalyzeSensitiveData = (_, _, _, _) => Task.FromResult<IReadOnlyList<SensitiveFinding>>([]);
            await window.FindSensitiveDataAsync();
            window.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            doc.Add(new() { Kind = AnnotationKind.Redaction, Points = [new(1, 1), new(30, 30)] });
            window.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Check(manualState.NeedsScan && manualState.CanExport && skip.Visibility != Visibility.Visible, "Editing after a check blocked Copy and Save or showed the skip button");
            Check(Field<List<Button>>(window, "_exportButtons").All(b => b.IsEnabled), "Copy/Save buttons are disabled after the image changed");
        }
        finally { window.Close(); }
        using var controller = new AppController(true);
        controller.Settings.AutoRedact.Enabled = true;
        uint clipboardBefore = GetClipboardSequenceNumber();
        var captureHandler = typeof(AppController).GetMethod("HandleCapturedImageAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(captureHandler != null, "Capture has no pre-output review gate");
        await (Task)captureHandler!.Invoke(controller, [sample])!;
        Check(controller.LastCapture == null && controller.CaptureHistory.Entries.Count == 0, "Unreviewed capture entered last image/history");
        var review = Application.Current.Windows.OfType<ScreenshotEditorWindow>().Single();
        Check(GetClipboardSequenceNumber() == clipboardBefore, "Unreviewed capture touched clipboard");
        var redacted = RedactionRenderer.Apply(sample, new Int32Rect[] { new(20, 20, 100, 30) });
        bool saved = await controller.ExportReviewedImageAsync(redacted, ScreenshotExportAction.Save, review, (_, _) => false);
        Check(!saved && controller.LastCapture == null && controller.CaptureHistory.Entries.Count == 0, "Cancelled Save published screenshot");
        saved = await controller.ExportReviewedImageAsync(redacted, ScreenshotExportAction.Save, review, (dialog, _) => { dialog.FileName = Path.GetFullPath("nonexistent-" + Guid.NewGuid() + "/test.png"); return true; });
        Check(!saved && controller.LastCapture == null && controller.CaptureHistory.Entries.Count == 0, "Failed Save published screenshot");
        Check(GetClipboardSequenceNumber() == clipboardBefore, "Cancelled/failed Save copied image");
        review.Close();
        Check(controller.LastCapture == null && controller.CaptureHistory.Entries.Count == 0, "Cancelled review published screenshot");
        var owner = new Window();
        string path = Path.GetFullPath("auto-redact-output.png");
        try
        {
            owner.Show();
            saved = await controller.ExportReviewedImageAsync(redacted, ScreenshotExportAction.Save, owner, (dialog, _) => { dialog.FileName = path; return true; });
            Check(saved && File.Exists(path) && ReferenceEquals(controller.LastCapture, redacted) && controller.CaptureHistory.Entries.Count == 1, "Successful Save did not publish reviewed result");
            Check(GetClipboardSequenceNumber() == clipboardBefore, "Successful Save unexpectedly copied image");
            using var file = File.OpenRead(path); var decoded = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var actual = new byte[pixels.Length]; new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0).CopyPixels(actual, 1600, 0);
            Check(actual[(25 * 400 + 25) * 4] == 0 && actual[0] == 255, "Saved file did not flatten covers");
            await controller.HandleCapturedImageAsync(sample); Application.Current.Windows.OfType<ScreenshotEditorWindow>().Single().Close();
            Check(ReferenceEquals(controller.LastCapture, redacted) && controller.CaptureHistory.Entries.Count == 1, "Second cancelled capture replaced reviewed history");
            if (Clipboard.GetDataObject()?.GetFormats().Length is null or 0)
            {
                try { Check(await controller.ExportReviewedImageAsync(redacted, ScreenshotExportAction.Copy, owner), "Copy did not succeed"); Check(Clipboard.ContainsImage(), "Reviewed Copy has no image"); }
                finally { Clipboard.Clear(); }
                Console.WriteLine("PASS reviewed Copy on initially empty clipboard");
                clipboardBefore = GetClipboardSequenceNumber(); // the Copy above legitimately changed the clipboard; later checks compare from here
            }
            else Console.WriteLine("SKIP reviewed Copy: preserve nonempty clipboard; pre-review/Save sequence checks passed");
        }
        finally { owner.Close(); }
        // Simulate clipboard contention without touching the user's clipboard.
        using (var busyController = new AppController(true))
        {
            int attempts = 0; bool copiedAfterClose = false;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ScreenshotEditorWindow? busyReview = null;
            busyReview = new ScreenshotEditorWindow(sample, _ => { }, _ => { },
                exportAsync: (result, action) => busyController.ExportReviewedImageAsync(result, action, busyReview!,
                    cancellationToken: busyReview!.ExportCancellationToken,
                    writeClipboard: _ => { if (++attempts == 1) { entered.TrySetResult(); throw new ExternalException("Invented clipboard contention"); } copiedAfterClose = true; }));
            busyReview.Show();
            typeof(ScreenshotEditorWindow).GetMethod("Export", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(busyReview, ["Copy"]);
            Check(await Task.WhenAny(entered.Task, Task.Delay(3000)) == entered.Task, "Busy Copy did not reach retry");
            busyReview.Close(); await Task.Delay(250);
            Check(!copiedAfterClose && attempts == 1, "Closed review continued clipboard writes");
            Check(busyController.LastCapture == null && busyController.CaptureHistory.Entries.Count == 0, "Closed review published a pending Copy");
            Check(GetClipboardSequenceNumber() == clipboardBefore, "Injected retry touched user clipboard");
        }
        controller.OpenMain();
        var main = Field<MainWindow>(controller, "main"); main.Navigate("Capture"); await Task.Delay(30); main.UpdateLayout();
        Check(Children(main).OfType<CheckBox>().Any(c => c.Name == "AutoRedactEnabled"), "Capture settings lack the automatic-review toggle");
        main.Close();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<SensitiveFinding>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new ScreenshotEditorWindow(sample, _ => throw new Exception("Closed editor exported"), _ => { });
        late.AnalyzeSensitiveData = (_, _, _, _) => { started.TrySetResult(); return release.Task; };
        late.Show(); var pending = late.FindSensitiveDataAsync();
        Check(await Task.WhenAny(started.Task, Task.Delay(3000)) == started.Task, "Injected local analysis did not start");
        late.Close(); release.SetResult([new(Guid.NewGuid(), ["email"], new(20, 20, 100, 30), "••••")]); await pending;
        Check(Field<ScreenshotEditDocument>(late, "_document").Items.Count == 0, "Late completion edited closed image");
        await LayoutChecksAsync(sample, controller);
    }

    private static async Task LayoutChecksAsync(BitmapSource sample, AppController controller)
    {
        Directory.CreateDirectory("auto-redact-layouts");
        foreach (string language in new[] { "en", "ru", "de", "fr", "es" }) foreach (string theme in new[] { "Light", "Dark" }) foreach (string layout in new[] { "A", "B" })
        {
            L.Use(language); controller.Settings.Theme = theme; controller.ApplyTheme();
            var window = new ScreenshotEditorWindow(sample, _ => { }, _ => { }, editorLayout: layout);
            try
            {
                window.AnalyzeSensitiveData = (_, _, _, _) => Task.FromResult<IReadOnlyList<SensitiveFinding>>(Enumerable.Range(0, 30).Select(i => new SensitiveFinding(Guid.NewGuid(), ["email", "credential"], new(10, 10, 100, 20), "••••")).ToArray());
                window.Show(); window.Width = 860; window.Height = 500;
                await window.FindSensitiveDataAsync(); await Task.Delay(20); window.UpdateLayout();
                foreach (string label in new[] { "Add cover manually", "Detection options" })
                {
                    var button = Children(window).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == L.T(label));
                    var border = ((SolidColorBrush)button.BorderBrush).Color;
                    var background = ((SolidColorBrush)button.Background).Color;
                    double contrast = (Math.Abs(border.R - background.R) + Math.Abs(border.G - background.G) + Math.Abs(border.B - background.B)) / (3d * 255) * border.A / 255;
                    Check(button.BorderThickness.Left >= 1 && contrast >= .1, "Secondary button boundary too faint: " + label + "/" + theme);
                }
                var buttons = Field<List<Button>>(window, "_exportButtons");
                foreach (var button in buttons.Concat(Children(window).OfType<Button>().Where(b => b.Name is "FindSensitiveData" or "HideAllSensitiveData")))
                {
                    var location = button.TranslatePoint(new Point(0, 0), window);
                    Check(button.ActualHeight > 0 && location.X >= 0 && location.Y >= 0 && location.X + button.ActualWidth <= window.ActualWidth + 1 && location.Y + button.ActualHeight <= window.ActualHeight + 1, "Controls clipped: " + language + "/" + theme + "/" + layout);
                }
                var list = Children(window).OfType<ListBox>().Single(l => l.Name == "SensitiveFindings");
                DependencyObject parent = VisualTreeHelper.GetParent(list);
                while (parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
                var viewport = (ScrollViewer)parent;
                var listTop = list.TranslatePoint(new Point(), window).Y;
                var viewportBottom = viewport.TranslatePoint(new Point(0, viewport.ActualHeight), window).Y;
                Check(list.ActualHeight >= 32 && listTop + 32 <= viewportBottom, "Found areas are hidden below the initial viewport: " + language + "/" + theme + "/" + layout);
                Children(window).OfType<Button>().Single(b => b.Name == "HideAllSensitiveData").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Width = 1060; window.Height = 760; window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create($"auto-redact-layouts/{language}-{theme}-{layout}.png"); encoder.Save(output);
            }
            finally { window.Close(); }
        }
        L.Use("en");
    }
}
