using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class TextToolsChecks
{
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rectangle);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task RunAsync()
    {
        using var controller = new AppController(true); controller.UpdateSettings(s => { s.TranslationEnabled = true; s.ScreenTextEnabled = true; s.TranslationDirection = "en-ru"; s.Animations = true; });
        uint clipboard = GetClipboardSequenceNumber();
        Check(await SelectedTextService.ReadAsync(0) == null, "Invalid selection did not fall back");
        var text = controller.OpenTextTools()!; text.SetSource("Open the window.");
        var processing = text.TranslateAsync(); await Task.Delay(60);
        Check(text.IsProcessing && Field<Button>(text, "cancel").IsVisible, "Busy state absent");
        var activity = Field<Border>(text, "activity"); if (Motion.Enabled) Check(activity.HasAnimatedProperties, "Busy animation absent");
        text.Hide(); Check(!activity.HasAnimatedProperties, "Hidden animation retained"); text.Show();
        await processing; Check(Field<TextBox>(text, "output").Text.Contains("окно"), "Translation UI did not receive result: " + Field<TextBlock>(text, "status").Text);
        var stale = text.TranslateAsync(); Field<TextBox>(text, "source").Text = "Changed source"; await stale;
        Check(Field<TextBox>(text, "output").Text == "" && !text.IsProcessing, "Stale translation replaced edited text");
        foreach (string language in L.Languages)
        {
            text.Close(); L.Use(language); controller.UpdateSettings(s => { s.Theme = language == "ru" ? "Dark" : "Light"; }); text = controller.OpenTextTools()!;
            text.SetSource("Open the window."); text.UpdateLayout();
            var image = new RenderTargetBitmap((int)text.ActualWidth, (int)text.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(text);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create("text-tools-" + language + ".png"); encoder.Save(file);
        }
        L.Use("en");
        var languageTag = LocalOcr.Languages.FirstOrDefault(l => l.Tag.StartsWith("en", StringComparison.Ordinal));
        if (languageTag == null) throw new Exception("English OCR fixture requires an installed Windows language pack");
        Field<ComboBox>(text, "ocrLanguage").SelectedItem = LocalOcr.Languages.First(l => l.Tag == languageTag.Tag);
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 600, 120));
            context.DrawText(new FormattedText("DESKTOP TOOLS 123", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 40, Brushes.Black, 1), new Point(20, 30));
        }
        var sample = new RenderTargetBitmap(600, 120, 96, 96, PixelFormats.Pbgra32); sample.Render(visual); sample.Freeze();
        await text.RecognizeScreenAsync(sample); Check(Field<TextBox>(text, "source").Text.Contains("123"), "OCR UI failed: " + Field<TextBlock>(text, "status").Text);
        Check(ReferenceEquals(Field<Image>(text, "capturedArea").Source, sample) && Field<Image>(text, "capturedArea").IsVisible, "OCR lost selected-area preview");
        Check(Grid.GetColumn(Field<TextBox>(text, "source")) == 1 && !Field<TextBox>(text, "source").IsReadOnly, "OCR result is not editable beside the selected area");
        text.UpdateLayout();
        var ocrRender = new RenderTargetBitmap((int)text.ActualWidth, (int)text.ActualHeight, 96, 96, PixelFormats.Pbgra32); ocrRender.Render(text);
        var ocrEncoder = new PngBitmapEncoder(); ocrEncoder.Frames.Add(BitmapFrame.Create(ocrRender)); using (var ocrFile = File.Create("ocr-selected-area.png")) ocrEncoder.Save(ocrFile);
        foreach (var point in new[] { new Point(0, 0), new Point(599, 119), new Point(40, 50) })
        {
            var zoom = ScreenEyedropperWindow.Magnify(sample, (int)point.X, (int)point.Y);
            Check(zoom.PixelWidth == 13 && zoom.PixelHeight == 13 && ScreenshotPixel.Read(zoom, 6, 6) == ScreenshotPixel.Read(sample, (int)point.X, (int)point.Y), "Pixel zoom center differs from selected pixel");
        }
        Check(controller.LastCapture == null && controller.CaptureHistory.Entries.Count == 0, "OCR added screenshot history");
        var selection = controller.CaptureAsync(textOnly: true); controller.CancelActiveTool(); await selection;
        Check(!controller.IsBusy && !Application.Current.Windows.OfType<SelectionWindow>().Any(), "OCR selection cancellation leaked windows");
        // A WinForms helper is outside WPF's hidden-control scope, so only known fixture pixels are captured.
        using (var helper = new System.Windows.Forms.Form { Text = "DesktopTools OCR fixture", Width = 740, Height = 200, TopMost = true, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(120, 140) })
        using (var editor = new System.Windows.Forms.TextBox { Text = "DESKTOP TOOLS 123", Multiline = true, Dock = System.Windows.Forms.DockStyle.Fill, Font = new System.Drawing.Font("Arial", 32), BackColor = System.Drawing.Color.White, ForeColor = System.Drawing.Color.Black })
        {
            helper.Controls.Add(editor); helper.Show(); helper.Activate(); editor.Focus(); editor.SelectAll(); await Task.Delay(120);
            var editorHandle = editor.Handle; var helperHandle = helper.Handle;
            // Windows can refuse foreground activation in an unattended run. Exercise the exact provider extraction without forcing user focus.
            string? selected = await Task.Run(() => SelectedTextService.ReadElementSelection(System.Windows.Automation.AutomationElement.FromHandle(editorHandle), helperHandle));
            Check(selected?.Contains("DESKTOP TOOLS 123") == true, "UI Automation did not read the helper selection");
            editor.SelectionLength = 0;
            controller.UpdateSettings(s => { s.CaptureEnabled = false; s.ScreenTextEnabled = true; s.CaptureMonitorMode = "All"; s.ScreenTextLanguage = languageTag.Tag; });
            GetWindowRect(helper.Handle, out var bounds); var virtualMonitor = MonitorService.GetVirtualDesktop();
            var scan = controller.CaptureAsync(textOnly: true); var picker = Field<SelectionWindow>(controller, "selector"); await Task.Delay(80); picker.UpdateLayout();
            double scaleX = picker.ActualWidth / virtualMonitor.Bounds.Width, scaleY = picker.ActualHeight / virtualMonitor.Bounds.Height;
            picker.CompleteSelection(new Rect((bounds.Left + 10 - virtualMonitor.Bounds.X) * scaleX, (bounds.Top + 36 - virtualMonitor.Bounds.Y) * scaleY, (bounds.Right - bounds.Left - 20) * scaleX, (bounds.Bottom - bounds.Top - 46) * scaleY));
            await scan; text = controller.OpenTextTools()!;
            for (int i = 0; i < 100 && text.IsProcessing; i++) await Task.Delay(50);
            Check(Field<TextBox>(text, "source").Text.Contains("123"), "Direct screen scan failed: " + Field<TextBlock>(text, "status").Text);
            Check(controller.LastCapture == null && controller.CaptureHistory.Entries.Count == 0 && !controller.IsBusy, "Screen scan wrote screenshot history or remained busy");
        }
        var cancelOnShortcutOff = controller.CaptureAsync(textOnly: true);
        controller.UpdateSettings(s => FeatureShortcutCatalog.SetEnabled(s, FeatureShortcutCatalog.All.Single(e => e.Action == "ScreenText"), false));
        Check(controller.IsBusy, "Disabling a shortcut cancelled screen selection");
        controller.CancelActiveTool(); await cancelOnShortcutOff;
        Check(!controller.IsBusy && !Application.Current.Windows.OfType<SelectionWindow>().Any(), "Cancellation retained screen selection");
        text = controller.OpenTextTools()!; text.SetSource("Open the window.");
        var closing = text.TranslateAsync(); text.Close(); await closing;
        Check(!activity.HasAnimatedProperties, "Old window animation retained");
        Check(GetClipboardSequenceNumber() == clipboard, "Text tools changed clipboard without Copy");
    }
}
