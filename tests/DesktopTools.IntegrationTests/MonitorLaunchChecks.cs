using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.UI;
using DesktopTools.Extras;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class MonitorLaunchChecks
{
    public static async Task RunAsync()
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        static async Task<SelectionWindow> WaitForSelector(AppController owner)
        {
            for (int i = 0; i < 100; i++)
            {
                var current = Field<SelectionWindow?>(owner, "selector");
                if (current != null) return current;
                if (!owner.IsBusy) break;
                await Task.Delay(20);
            }
            throw new InvalidOperationException("Capture selector did not open: " + owner.Status);
        }
        using var controller = new AppController(true);
        controller.Settings.MonitorMode = "Primary";
        controller.Settings.CaptureMonitorMode = "Selected";
        controller.Settings.CaptureEnabled = true;
        var target = MonitorService.GetCurrent(true);
        var other = target with { Id = "Synthetic other display" };
        Check(AppController.SameMonitor(target with { }, target), "Equal monitor snapshots must reuse their session.");
        Check(!AppController.SameMonitor(other, target), "Different device must start a new session.");
        Check(!AppController.SameMonitor(target with { ScaleX = target.ScaleX + .5 }, target), "Scale change must invalidate old session geometry.");
        Check(!AppController.SameMonitor(null, target), "Missing session cannot supply ink or frozen source.");
        // Synthetic identity uses real bounds so these checks also work on one display.
        void SeedOtherMonitor()
        {
            controller.HideAnnotations();
            typeof(AppController).GetMethod("EnsureOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, [other]);
            controller.Document.Add(new Annotation { Kind = AnnotationKind.Pen, Points = [new Point(30, 30), new Point(40, 40)] });
        }
        SeedOtherMonitor();
        var previousOverlay = Field<OverlayWindow>(controller, "overlay");
        Field<OverlayStateMachine>(controller, "machine").ToggleDraw();
        previousOverlay.Hide(); controller.CheckDrawingFocus(); await Task.Delay(30);
        Check(controller.State == OverlayState.Draw, "Clicking another monitor ended drawing on its selected monitor.");
        previousOverlay.Show();
        nint sentinel = new IntPtr(-12345); Set(controller, "previousForeground", sentinel);
        controller.ToggleDraw();
        Check(Field<OverlayWindow>(controller, "overlay").Monitor == target && !ReferenceEquals(previousOverlay, Field<OverlayWindow>(controller, "overlay")), "Draw reused an overlay from a different monitor.");
        Check(controller.Document.Items.Count == 0 && controller.State == OverlayState.Draw, "Moving drawing must start a fresh drawing session.");
        Check(Field<nint>(controller, "previousForeground") == sentinel, "Moving an active drawing session must preserve its original foreground target.");
        await controller.FreezeAsync();
        Check(Field<nint>(controller, "previousForeground") == sentinel, "Freeze from Draw must preserve its original foreground target.");
        await controller.FreezeAsync();
        controller.ToggleDraw();
        Check(controller.State == OverlayState.Interact, "Draw on the same monitor must toggle to interaction. Status: " + controller.Status);
        SeedOtherMonitor();
        var retained = Field<OverlayWindow>(controller, "overlay");
        Check(!controller.IsBusy && controller.Settings.CaptureEnabled, $"Capture precondition changed: busy={controller.IsBusy}, enabled={controller.Settings.CaptureEnabled}, state={controller.State}");
        var capture = controller.CaptureAsync();
        var selector = await WaitForSelector(controller);
        selector.Close(); await capture;
        Check(ReferenceEquals(retained, Field<OverlayWindow>(controller, "overlay")) && controller.Document.Items.Count == 1 && controller.State == OverlayState.Hidden,
            "Cancelling capture on a new target must preserve the old monitor's annotation session.");
        var staleFrame = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 0, 255, 255 }, 4); staleFrame.Freeze();
        Set(controller, "frozenFrame", staleFrame);
        controller.Document.Add(new Annotation { Kind = AnnotationKind.Rectangle, Points = [new Point(0, 0), new Point(1000, 1000)], Color = Colors.Magenta, Thickness = 1000 });
        controller.Settings.CaptureOutput = "Clipboard";
        var completedCapture = controller.CaptureAsync();
        (await WaitForSelector(controller)).CompleteSelection(new Rect(10, 10, 24, 24)); await completedCapture;
        var output = controller.LastCapture ?? throw new InvalidOperationException(controller.Status);
        Check(output.PixelWidth > 1 && output.PixelHeight > 1, "Capture used an unrelated monitor's stale one-pixel frozen frame.");
        var pixels = new byte[output.PixelWidth * output.PixelHeight * 4]; output.CopyPixels(pixels, output.PixelWidth * 4, 0);
        Check(Enumerable.Range(0, output.PixelWidth * output.PixelHeight).Any(i => pixels[i * 4] != 255 || pixels[i * 4 + 1] != 0 || pixels[i * 4 + 2] != 255),
            "Capture composited the other monitor's solid magenta annotation.");
        Check(ReferenceEquals(retained, Field<OverlayWindow>(controller, "overlay")) && controller.Document.Items.Count == 2, "Successful other-target capture destroyed the old annotation session.");
        await controller.FreezeAsync();
        Check(Field<OverlayWindow>(controller, "overlay").Monitor == target && !ReferenceEquals(staleFrame, Field<BitmapSource>(controller, "frozenFrame")) && controller.State == OverlayState.Draw,
            "Freeze on a new target must replace the unrelated still and enter drawing on the new monitor.");
        Check(controller.Document.Items.Count == 0, "Freeze relocation must begin a fresh annotation session.");
        controller.HideAnnotations();
        controller.Settings.SpotlightMonitorMode = "Selected";
        var oldEffect = new PresentationSession("Spotlight", controller.Settings, false, other);
        typeof(AppController).GetField("presentation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, oldEffect);
        typeof(AppController).GetField("presentationMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, "Spotlight");
        controller.TogglePresentation("Spotlight");
        Check(Field<PresentationSession>(controller, "presentation").ActiveMonitor == target,
            "Same effect launched on a different monitor must move instead of stopping.");
        controller.TogglePresentation("Spotlight");
        Check(Field<PresentationSession?>(controller, "presentation") == null, "Same effect on the same monitor must toggle off.");
        controller.HideAnnotations();
    }
}
