using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using Forms = System.Windows.Forms;

internal static class RecordingSourceChecks
{
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        using var target = new Forms.Form { Text = "DesktopTools source fixture", Width = 500, Height = 300, BackColor = System.Drawing.Color.RoyalBlue, StartPosition = Forms.FormStartPosition.Manual, Left = 20, Top = 20 };
        target.Show(); await Task.Delay(150);
        var windowSource = new RecordingWindowInfo(target.Handle, (uint)Environment.ProcessId, target.Text);
        Check(RecordingWindows.GetAll(true).Any(w => w.Handle == target.Handle), "Visible fixture not enumerated");
        foreach (var theme in new[] { "Dark", "Light" }) foreach (var language in new[] { "ru", "en", "de", "fr", "es" })
        {
            controller.Settings.Theme = theme; controller.ApplyTheme(); L.Use(language);
            var picker = new RecordingSourcePickerWindow(() => new[] { windowSource }); picker.Show(); await Task.Delay(250); picker.SetMode(1); await Task.Delay(100); picker.UpdateLayout();
            try
            {
                Check(picker.Selection == null && !picker.Result.IsCompleted, "Preview confirmed the source automatically");
                Check(Field<Button>(picker, "confirm").IsEnabled, "Valid window cannot be selected");
                // Capture only our picker rectangle: DWM thumbnails are not part of WPF RenderTargetBitmap.
                var origin = picker.PointToScreen(new Point()); var dpi = VisualTreeHelper.GetDpi(picker); var monitor = MonitorService.GetAll().First(m => m.Bounds.Contains(origin));
                var capture = CaptureService.Capture(monitor); var rect = new Int32Rect((int)(origin.X - monitor.Bounds.X), (int)(origin.Y - monitor.Bounds.Y), (int)(picker.ActualWidth * dpi.DpiScaleX), (int)(picker.ActualHeight * dpi.DpiScaleY));
                var stage = Field<Border>(picker, "stage"); var center = stage.PointToScreen(new Point(stage.ActualWidth / 2, stage.ActualHeight / 2)); var pixel = new byte[4]; capture.CopyPixels(new Int32Rect((int)(center.X - monitor.Bounds.X), (int)(center.Y - monitor.Bounds.Y), 1, 1), pixel, 4, 0);
                Check(pixel[0] > 150 && pixel[2] < 120, "Live window thumbnail did not show fixture pixels: " + string.Join(",", pixel));
                var crop = new CroppedBitmap(capture, rect); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop)); using (var output = File.Create($"record-source-{language}-{theme}.png")) encoder.Save(output);
                Field<TextBox>(picker, "search").Text = "no matching fixture"; Check(Field<ListBox>(picker, "list").Items.Count == 0 && !Field<Button>(picker, "confirm").IsEnabled, "Empty search retained stale source");
                Field<TextBox>(picker, "search").Clear(); Check(Field<ListBox>(picker, "list").Items.Count == 1, "Clearing search did not restore source");
            }
            finally { picker.Close(); }
            Check(await picker.Result == null, "Closing picker changed the selected source");
        }
        var accepted = new RecordingSourcePickerWindow(() => new[] { windowSource }); accepted.Show(); await Task.Delay(250); accepted.SetMode(1);
        Field<Button>(accepted, "confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check((await accepted.Result)?.Window == windowSource, "Window confirmation did not return exact handle and process");
        var regionPicker = new RecordingSourcePickerWindow(); regionPicker.Show(); await Task.Delay(250); regionPicker.SetMode(2); await Task.Delay(250);
        Field<Button>(regionPicker, "confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(60);
        var selection = Application.Current.Windows.OfType<DesktopTools.UI.SelectionWindow>().Single(); selection.CompleteSelection(null); await Task.Delay(70);
        Check(regionPicker.IsVisible && !regionPicker.Result.IsCompleted, "Cancelling region dismissed picker or confirmed source");
        Field<Button>(regionPicker, "confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(60);
        selection = Application.Current.Windows.OfType<DesktopTools.UI.SelectionWindow>().Single(); selection.CompleteSelection(new Rect(20, 20, 200, 100));
        var result = await regionPicker.Result.WaitAsync(TimeSpan.FromSeconds(3)); Check(result?.Region is { Width: >= 2, Height: >= 2 } && regionPicker.SelectedPreview != null, "Region selection did not return preview and physical bounds");
        var stale = new RecordingSourcePickerWindow(() => new[] { windowSource }); stale.Show(); await Task.Delay(250); stale.SetMode(1); target.Close();
        Field<Button>(stale, "confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!stale.Result.IsCompleted && !Field<Button>(stale, "confirm").IsEnabled, "Closed source was accepted"); stale.Close();
    }
}
