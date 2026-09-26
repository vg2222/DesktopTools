using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Native;
using DesktopTools.Localization;

internal static class RecordingPrerequisiteChecks
{
    private static T Field<T>(object target, string name)
        => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    internal static async Task RunAsync()
    {
        L.Use("en");
        using var controller = new AppController(true);
        controller.UpdateSettings(settings =>
        {
            settings.Animations = false;
            settings.RecordingMicrophone = false;
            settings.RecordingSystemAudio = false;
        });

        var monitor = new MonitorInfo("prerequisite-fixture", new Rect(0, 0, 1280, 720), new Rect(0, 0, 1280, 680), 1, 1);
        IReadOnlyList<string> missing = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll"];
        int prerequisiteReads = 0;
        int outputChooserCalls = 0;
        IReadOnlyList<MonitorInfo> monitors = [monitor];
        var recorder = new ScreenRecorderWindow(
            controller,
            _ => { outputChooserCalls++; return null; },
            () => monitors,
            () => { prerequisiteReads++; return missing; });

        try
        {
            recorder.Show();
            recorder.SetSource(new RecordingSelection("Prerequisite fixture", monitor));
            recorder.UpdateLayout();
            var start = Field<Button>(recorder, "start");
            var sourcePreview = Field<Button>(recorder, "sourcePreview");
            Check(start.TranslatePoint(new Point(0, 0), recorder).Y > sourcePreview.TranslatePoint(new Point(0, 0), recorder).Y + 100,
                "Recorder transport controls are not below the preview.");
            var layout = new RenderTargetBitmap((int)recorder.ActualWidth, (int)recorder.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            layout.Render(recorder);
            var image = new PngBitmapEncoder(); image.Frames.Add(BitmapFrame.Create(layout));
            using (var output = File.Create(Path.Combine(Environment.CurrentDirectory, "recorder-layout.png"))) image.Save(output);
            var runtimeHelp = Field<Button>(recorder, "runtimeHelp");
            var status = Field<TextBlock>(recorder, "status");
            Check(runtimeHelp.Visibility == Visibility.Visible && status.Text.Contains("vcruntime140.dll", StringComparison.OrdinalIgnoreCase),
                "Missing runtime warning is absent before recording is attempted");

            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(40);
            Check(controller.Status == L.T("Screen recording is unavailable until Microsoft Visual C++ x64 is installed."),
                "Record click did not report the unavailable feature: " + controller.Status);
            Check(start.IsEnabled, "Missing runtime disabled retry");
            Check(runtimeHelp.Visibility == Visibility.Visible && status.Text.Contains("vcruntime140.dll", StringComparison.OrdinalIgnoreCase),
                "Missing runtime guidance was not shown");
            Check(outputChooserCalls == 0 && Field<Task?>(recorder, "session") == null && Field<ScreenRecordingService?>(recorder, "service") == null,
                "Missing runtime entered output or native recording");

            monitors = [];
            await recorder.RefreshDisplaysAsync();
            Check(status.Text.Contains("vcruntime140.dll", StringComparison.OrdinalIgnoreCase),
                "Display changes replaced the missing-runtime warning");
            monitors = [monitor];
            recorder.SetSource(new RecordingSelection("Prerequisite fixture", monitor));

            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(40);
            Check(prerequisiteReads == 3 && start.IsEnabled && outputChooserCalls == 0,
                "Missing runtime path was not retryable");

            missing = [];
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int attempt = 0; attempt < 50 && Field<Task?>(recorder, "session") != null; attempt++) await Task.Delay(20);
            Check(prerequisiteReads == 4 && outputChooserCalls == 1, "Repaired prerequisites did not reach the cancellable output choice");
            Check(start.IsEnabled && runtimeHelp.Visibility == Visibility.Collapsed
                && Field<Task?>(recorder, "session") == null && Field<ScreenRecordingService?>(recorder, "service") == null,
                "Cancelled retry retained runtime help or recording state");
        }
        finally
        {
            recorder.Close();
            await Task.Delay(30);
        }

        Check(!recorder.IsVisible && Field<Task?>(recorder, "session") == null, "Recorder did not close safely after prerequisite retry");
    }
}
