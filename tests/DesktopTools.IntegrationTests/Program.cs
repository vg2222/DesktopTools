using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.UI;
using DesktopTools.Native;
using DesktopTools.Extras;

internal static class Program
{
    private static readonly List<string> Results = [];
    private static int failed;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--input-target") return NativeInputChecks.RunTarget(args[1]);
        var root = Environment.CurrentDirectory;
        var output = Path.Combine(root, "artifacts", "integration"); Directory.CreateDirectory(output);
        Environment.CurrentDirectory = output;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DesktopTools;component/UI/Theme.xaml", UriKind.Relative) });
        app.Startup += async (_, _) =>
        {
            try
            {
                if (args.Contains("--github-gallery")) await Test("GitHub screenshot gallery", GitHubGallery.RunAsync);
                else if (args.Contains("--record-prerequisites-only")) await Test("Recorder prerequisite UI guard and retry", RecordingPrerequisiteChecks.RunAsync);
                else if (args.Contains("--updates-only")) await Test("Update discovery, persistent controls, notes feedback and progress", UpdateChecks.RunAsync);
                else if (args.Contains("--interaction-refinement-only")) await Test("Capture privacy and editor interaction refinements", InteractionRefinementChecks.RunAsync);
                else if (args.Contains("--bug-regressions-only")) await Test("Focused bug regressions", BugRegressionChecks.RunAsync);
                else if (args.Contains("--onboarding-refinement-only")) await Test("Onboarding, icons, switches and audience visibility", OnboardingRefinementChecks.RunAsync);
                else if (args.Contains("--release-polish-only")) await Test("Release window, setup and recorder corrections", ReleasePolishChecks.RunAsync);
                else if (args.Contains("--codec-compatibility-only")) await Test("Generated codec import and trimmed exports", CodecCompatibilityChecks.RunAsync);
                else if (args.Contains("--record-sustained-only")) await Test("Sustained numbered recording", RecordingThroughputChecks.RunSustainedAsync);
                else if (args.Contains("--record-throughput-only")) await Test("Numbered recording frames and sustained capture", RecordingThroughputChecks.RunAsync);
                else if (args.Contains("--record-state-only")) await Test("Recorder clock, source changes and pending-start cancellation", RecordingStateChecks.RunAsync);
                else if (args.Contains("--record-encoding-only")) await Test("Hardware-requested and software recording, preferences and profiles", RecordingChecks.EncodingAsync);
                else if (args.Contains("--dialog-layout-only")) await Test("Short dialog scrolling, fixed actions and monitor limits", DialogLayoutChecks.RunAsync);
                else if (args.Contains("--menu-only")) await Test("Menu interaction, scrolling and popup privacy", MenuInteractionChecks.RunAsync);
                else if (args.Contains("--media-backdrop-only")) await Test("Video, translation and OCR native backdrop and opaque fallback", UtilityBackdropChecks.RunMediaAsync);
                else if (args.Contains("--utility-backdrop-only")) await Test("Utility native backdrop and opaque fallback", UtilityBackdropChecks.RunAsync);
                else if (args.Contains("--guide-placement-only")) await Test("Guide placement at screen corners and scaled negative origins", GuidePlacementChecks.RunAsync);
                else if (args.Contains("--region-freeze-only")) await Test("Region freeze uses the selection still", RegionFreezeChecks.RunAsync);
                else if (args.Contains("--capture-lifecycle-only")) await Test("One hundred native capture/edit/export/close cycles", () => CaptureLifecycleChecks.RunAsync(Results.Add));
                else if (args.Contains("--input-only")) await Test("Cross-process native input", async () => { using var controller = new AppController(true); await NativeInputChecks.RunAsync(controller, Results.Add); });
                else if (args.Contains("--blackout-escape-only")) await Test("Escape dismisses blackout without stopping independent aids", BlackoutEscapeChecks.RunAsync);
                else if (args.Contains("--automatic-guide-only")) await Test("Automatic setup entry, tour resume and disabled targets", AutomaticGuideChecks.RunAsync);
                else if (args.Contains("--help-only")) await Test("Help search, repeat setup, existing tool tour and embedded licenses", HelpChecks.RunAsync);
                else if (args.Contains("--inline-format-only")) await Test("Inline text formatting, rendering, commit and cancel", InlineFormattingChecks.RunAsync);
                else if (args.Contains("--feature-guides-only")) await Test("Real feature guide targets, progression, restart and cleanup", FeatureGuideChecks.RunAsync);
                else if (args.Contains("--image-export-only")) await Test("Image export preview, dimensions, formats, cancellation and original safety", ImageExportChecks.RunAsync);
                else if (args.Contains("--record-source-only")) await Test("Recording source search, native preview, selection and cancellation", RecordingSourceChecks.RunAsync);
                else if (args.Contains("--record-quality-only")) await Test("Recording quality drafts, localized layouts and HUD lifecycle", RecordingQualityChecks.RunAsync);
                else if (args.Contains("--reference-media-only")) await Test("Reference media layouts and automatic QR lifecycle", ReferenceMediaChecks.RunAsync);
                else if (args.Contains("--image-handles-only")) await Test("Image crop handles, letterbox, ratio and undo", ImageHandlesChecks.RunAsync);
                else if (args.Contains("--prompter-only")) await Test("Prompter studio, presentation and persistence", PrompterStudioChecks.RunAsync);
                else if (args.Contains("--collection-utilities-only")) await Test("Shelf originals and audio mixer lifecycle", CollectionUtilitiesChecks.RunAsync);
                else if (args.Contains("--notes-only")) await Test("Notes collection search, synchronization and persistence", NotesCollectionChecks.RunAsync);
                else if (args.Contains("--setup-transitions-only")) await Test("Setup forward, back, rapid and reduced-motion transitions", SetupTransitionChecks.RunAsync);
                else if (args.Contains("--setup-only")) await Test("Setup resume, completion, skip and preference preservation", SetupChecks.RunAsync);
                else if (args.Contains("--chrome-pointer-order-only")) await Test("Isolated Chrome pointer drags and explicit pin", ChromeWindowOrderChecks.RunPointerAsync);
                else if (args.Contains("--chrome-window-order-only")) await Test("Isolated Chrome monitor round trips and explicit pin", ChromeWindowOrderChecks.RunAsync);
                else if (args.Contains("--window-order-only")) await Test("Window movement and explicit pin ownership", WindowPinChecks.RunAsync);
                else if (args.Contains("--save-notification-only")) await Test("Screenshot save survives notification expiry", ScreenshotSaveChecks.RunAsync);
                else if (args.Contains("--reset-restart-only")) await Test("Reset data and restart handoff", DataResetChecks.RunAsync);
                else if (args.Contains("--window-dismissal-only")) await Test("Window, notification and modal dismissal lifecycle", WindowDismissalChecks.RunAsync);
                else if (args.Contains("--notifications-only")) await Test("Notification anchor and replacement lifecycle", NotificationLifecycleChecks.RunAsync);
                else if (args.Contains("--foundation-only")) await Test("Bundled typography and shared controls", FoundationChecks.RunAsync);
                else if (args.Contains("--rounded-windows-only")) await Test("Native rounded windows without DWM rounding", RoundedWindowChecks.RunAsync);
                else if (args.Contains("--enable-tools-only")) await Test("Available tools and independent shortcut persistence", DashboardChecks.EnableToolsAsync);
                else if (args.Contains("--page-transition-only")) await Test("Category page transitions and reduced motion", PageTransitionChecks.RunAsync);
                else if (args.Contains("--dashboard-only")) await Test("Dashboard navigation, search, favorites and themes", DashboardChecks.RunAsync);
                else if (args.Contains("--recorder-only")) await Test("Screen recording lifecycle and MP4 output", RecordingChecks.RunAsync);
                else if (args.Contains("--text-tools-only")) await Test("Text tools animation, translation, OCR and cancellation", TextToolsChecks.RunAsync);
                else if (args.Contains("--translation-only")) await Test("Local EN-RU translation", TranslationChecks.RunAsync);
                else if (args.Contains("--translation-metadata")) await Test("Translation model metadata", TranslationChecks.MetadataAsync);
                else if (args.Contains("--toggles-only")) await Test("Switch animations survive Home, utility and aid updates", ToggleAnimationChecks.RunAsync);
                else if (args.Contains("--blackout-only")) await Test("Sharing blackout capture, cancellation and settings", BlackoutChecks.RunAsync);
                else if (args.Contains("--background-only")) await Test("Local background removal, alpha, undo and cancellation", BackgroundRemovalChecks.RunAsync);
                else if (args.Contains("--video-seek-only")) await Test("Coalesced video seeks, playhead dragging and stale-request cleanup", VideoSeekChecks.RunAsync);
                else if (args.Contains("--video-thumbnails-only")) await Test("Bounded portrait and wide video thumbnails", VideoThumbnailChecks.RunAsync);
                else if (args.Contains("--video-crop-only")) await Test("Video crop apply/cancel and edited preview", () => VideoWindowChecks.RunAsync(focused: true));
                else if (args.Contains("--video-frame-layout-only")) await Test("Video Frame inspector compact layout", VideoWindowChecks.RunFrameLayoutAsync);
                else if (args.Contains("--video-ui")) await Test("Video editor languages, edited preview and resource cleanup", () => VideoWindowChecks.RunAsync());
                else if (args.Contains("--video-only")) await Test("Native video trim, cut, crop, rotation, mute and original protection", VideoEditingChecks.RunAsync);
                else if (args.Contains("--effects-only")) await Test("Stationary spotlight and uniform laser", () => { EffectChecks.Run(); return Task.CompletedTask; });
                else if (args.Contains("--localization-only")) await Test("Five languages, both themes, stable choices and notification severity", LocalizationChecks.RunAsync);
                else if (args.Contains("--eyedropper-only")) await Test("Eyedropper cancellation releases every monitor window", EyedropperCheck);
                else if (args.Contains("--visual-only")) await Test("Interface layouts, rounded controls and notification surfaces", VisualChecks.RunAsync);
                else if (args.Contains("--presentation-only")) await Test("Native presentation initialization and lifecycle", PresentationSessionChecks.RunAsync);
                else if (args.Contains("--monitor-only")) await Test("Monitor launch and cross-target cancellation", MonitorLaunchChecks.RunAsync);
                else await RunAsync(args.Contains("--performance"), args.Contains("--no-input"));
            }
            catch (Exception ex) { failed++; Results.Add("FAIL harness: " + ex); }
            finally
            {
                Results.Add($"Failures: {failed}"); File.WriteAllLines(Path.Combine(output, "results.txt"), Results);
                foreach (var result in Results) Console.WriteLine(result);
                Environment.CurrentDirectory = root; app.Shutdown(failed == 0 ? 0 : 1);
            }
        };
        return app.Run();
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Test(string name, Func<Task> test)
    {
        try { await test(); Results.Add("PASS " + name); }
        catch (Exception ex) { failed++; Results.Add("FAIL " + name + ": " + ex); }
    }
    private static async Task Settle() { await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(40); }
    private static async Task<SelectionWindow?> WaitForCaptureSelector(AppController controller)
    {
        for (int i = 0; i < 250; i++)
        {
            var selector = Field<SelectionWindow?>(controller, "selector");
            if (selector?.IsVisible == true) return selector;
            if (!controller.IsBusy) return null;
            await Task.Delay(20);
        }
        return null;
    }
    private static async Task EyedropperCheck()
    {
        using var cancellation = new System.Threading.CancellationTokenSource();
        var sampling = ScreenEyedropperWindow.PickAsync(cancellation.Token);
        await Task.Delay(220);
        Check(Application.Current.Windows.Cast<Window>().OfType<ScreenEyedropperWindow>().Any(), "Sampler did not open");
        cancellation.Cancel();
        try { await sampling; } catch (OperationCanceledException) { }
        Check(!Application.Current.Windows.Cast<Window>().OfType<ScreenEyedropperWindow>().Any(), "Sampler retained monitor windows");
    }
    private static async Task RunAsync(bool performance, bool skipInput)
    {
        await Test("Focused bug regressions", BugRegressionChecks.RunAsync);
        await Test("Pinning restores an isolated helper window", WindowPinChecks.RunAsync);
        await Test("Shortcut recorder requires review and preserves failed changes", () =>
        {
            int saves = 0;
            var recorder = new ShortcutRecorderWindow("Ctrl+Alt+D", _ => { saves++; return false; }, () => "Conflict test");
            try
            {
                Check(recorder.WindowStyle == WindowStyle.None && recorder.AllowsTransparency, "Recorder retained native chrome");
                recorder.Show(); recorder.RecordGesture(Key.LeftCtrl, ModifierKeys.Control);
                Check(!Field<Button>(recorder, "saveButton").IsEnabled, "Modifier alone enabled Save");
                recorder.RecordGesture(Key.F12, ModifierKeys.Control); Check(!Field<Button>(recorder, "saveButton").IsEnabled, "Reserved shortcut enabled Save");
                recorder.RecordGesture(Key.K, ModifierKeys.Control | ModifierKeys.Alt);
                Check(saves == 0 && Field<Button>(recorder, "saveButton").IsEnabled, "Recording saved before explicit review");
                Field<Button>(recorder, "saveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(saves == 1 && recorder.IsVisible && Field<TextBlock>(recorder, "error").Text == "Conflict test", "Failed save did not preserve recorder and explain conflict");
            }
            finally { recorder.Close(); }
            var local = new ShortcutRecorderWindow("P", _ => false, allowUnmodified: true);
            try { local.Show(); local.RecordGesture(Key.B, ModifierKeys.None); Check(Field<Button>(local, "saveButton").IsEnabled, "Drawing recorder rejected bare key"); }
            finally { local.Close(); }
            foreach (var kind in Enum.GetValues<NotificationKind>())
            {
                var notice = new NotificationWindow("Test notification", kind);
                try { notice.Show(); Check(notice.Kind == kind && notice.AllowsTransparency && notice.WindowStyle == WindowStyle.None && notice.Topmost, "Notification shell or severity incorrect"); }
                finally { notice.Close(); }
            }
            Check(AppController.ClassifyNotification("Could not save file") == NotificationKind.Error, "Error severity missing");
            Check(AppController.ClassifyNotification("Shortcut conflict") == NotificationKind.Warning, "Warning severity missing");
            return Task.CompletedTask;
        });
        await Test("Repeated transitions settle without retaining offsets", async () =>
        {
            var panel = new StackPanel(); var window = new Window { Content = panel, Width = 100, Height = 100, ShowActivated = false };
            try { window.Show(); Motion.Transition(panel); await Task.Delay(50); Motion.Transition(panel); await Task.Delay(350);
                Check(Math.Abs(panel.Opacity - 1) < .001 && panel.RenderTransform is TranslateTransform offset && Math.Abs(offset.Y) < .001, "Transition did not settle"); }
            finally { window.Close(); }
        });
        await Test("Shortcut HUD excludes ordinary typing", () => { PresentationHudChecks.RunFormatter(); return Task.CompletedTask; });
        await Test("Presentation HUD toggles and cleanup", () => { PresentationHudLifecycleChecks.Run(); return Task.CompletedTask; });
        await Test("Shortcut animation replacement survives pending dismissal", async () =>
        {
            using var hud = new PresentationHudService(_ => { }) { ShortcutDisplaySeconds = .3, ShortcutAnimations = true };
            var show = typeof(PresentationHudService).GetMethod("ShowShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
            show.Invoke(hud, ["Ctrl + C"]); await Task.Delay(340);
            show.Invoke(hud, ["Ctrl + V"]); await Task.Delay(180);
            Check(Field<Window>(hud, "_keys").IsVisible && Field<TextBlock>(hud, "_keyText").Text == "Ctrl + V", "Old fade completion dismissed a replacement shortcut");
            var dismissal = Stopwatch.StartNew();
            while (Field<Window>(hud, "_keys").IsVisible && dismissal.ElapsedMilliseconds < 2000) await Task.Delay(50);
            Check(!Field<Window>(hud, "_keys").IsVisible, "Shortcut failed to dismiss after duration");
        });
        await Test("File shelf deduplicates references and preserves originals", () =>
        {
            string directory = Path.Combine(Environment.CurrentDirectory, "shelf-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "sample.txt"); File.WriteAllText(file, "original");
            var shelf = new FileShelfWindow(_ => { });
            try
            {
                shelf.AddPaths(new[] { file, file, directory, Path.Combine(directory, "missing") });
                Check(shelf.Paths.Count == 2, "Shelf accepted duplicates or missing paths");
                shelf.Show(); shelf.Hide(); Check(shelf.Paths.Count == 2, "Hiding shelf lost references");
            }
            finally { shelf.Shutdown(); }
            Check(File.ReadAllText(file) == "original", "Shelf modified original");
            File.Delete(file); Directory.Delete(directory);
            return Task.CompletedTask;
        });
        await Test("Audio enumeration is read-only and releases its service", () =>
        {
            using var audio = new AudioSessionService();
            var apps = audio.ReadApplications();
            Check(apps.All(a => double.IsFinite(a.Volume) && a.Volume >= 0 && a.Volume <= 100 && a.SessionCount > 0), "Invalid audio values");
            Check(apps.Select(a => a.Key).Distinct().Count() == apps.Count, "Duplicate audio rows");
            audio.Dispose(); bool rejected = false; try { audio.ReadApplications(); } catch (ObjectDisposedException) { rejected = true; }
            Check(rejected, "Disposed audio service remained usable"); return Task.CompletedTask;
        });
        using var controller = new AppController(true);
        await Test("Escape dismisses blackout without stopping independent aids", BlackoutEscapeChecks.RunAsync);
        await Test("Refreshing a page preserves its scroll offset", async () =>
        {
            controller.OpenMain(); var window = Field<MainWindow>(controller, "main");
            window.Navigate("Shortcuts"); await Settle();
            var scroll = Field<ScrollViewer>(window, "scroller"); scroll.ScrollToVerticalOffset(240); await Settle();
            double offset = scroll.VerticalOffset; Check(offset > 100, "Page did not scroll");
            window.Navigate("Shortcuts"); await Settle();
            Check(Math.Abs(scroll.VerticalOffset - offset) < 1, "Refresh jumped to top");
            window.Hide();
        });
        await Test("Hidden Home refreshes blackout state and custom themes update", async () =>
        {
            controller.OpenMain(); var mainWindow = Field<MainWindow>(controller, "main"); mainWindow.Navigate("Home"); mainWindow.Hide();
            controller.Hud.ToggleBlackout(); controller.CancelActiveTool(); await Settle();
            mainWindow.Show(); await Settle();
            Check(!controller.Hud.IsBlackoutVisible, "Blackout retained active state");
            Check(controller.UpdateSettings(s => { s.ThemePreset = "Custom"; s.PrimaryColor = "#CC5500"; s.BackgroundColor = "#102030"; s.UseCustomBackground = true; s.Animations = false; }), "Theme rejected");
            Check(((SolidColorBrush)Application.Current.Resources["Accent"]).Color == Color.FromRgb(204,85,0), "Custom accent not applied");
            Check(((SolidColorBrush)Application.Current.Resources["Surface"]).Color == Color.FromRgb(16,32,48), "Custom background not applied");
            Check(!Motion.Enabled, "Animation preference ignored");
            Check(AppCapturePrivacy.IsControlWindow(mainWindow), "Main app excluded from privacy classification");
            controller.UpdateSettings(s => { s.ThemePreset = "Classic"; s.UseCustomBackground = false; s.Animations = true; }); mainWindow.Hide();
        });
        await Test("Monitor identity, draw relaunch and cross-monitor capture cancellation", MonitorLaunchChecks.RunAsync);
        await Test("Ordinary settings preserve theme resources and monitor changes end presentation", async () =>
        {
            var brush = Application.Current.Resources["Surface"];
            Check(controller.UpdateSettings(s => s.Thickness = s.Thickness == 3 ? 4 : 3), "Setting failed");
            Check(ReferenceEquals(brush, Application.Current.Resources["Surface"]), "Unrelated setting rebuilt theme resources");
            controller.TogglePresentation("Spotlight");
            Check(controller.UpdateSettings(s => s.MonitorMode = s.MonitorMode == "Primary" ? "Cursor" : "Primary"), "Monitor setting failed");
            Check(Field<PresentationSession?>(controller, "presentation") == null, "Presentation remained on the old monitor");
            await Settle();
        });
        await Test("Notification timeout waits for activity and pauses while hidden", async () =>
        {
            var notice = new Window { Content = Ui.Text("Timeout check"), Width = 180, Height = 80, ShowActivated = false, Left = -10000, Top = -10000 };
            bool closed = false; notice.Closed += (_, _) => closed = true;
            uint? activityTick = 100;
            Motion.AutoDismiss(notice, TimeSpan.FromMilliseconds(500), activitySource: () => activityTick); notice.Show(); notice.Hide();
            await Task.Delay(750); Check(!closed, "Hidden notification timed out during capture");
            notice.Show(); await Task.Delay(750); Check(!closed, "AFK notification dismissed without input");
            activityTick = 101;
            await Task.Delay(1100); Check(closed, "Notification failed to auto-dismiss after user activity");
        });
        await Test("Spotlight retained mask, clipping and stationary allocations", () => { EffectChecks.Run(); return Task.CompletedTask; });
        await Test("Selector closes before its result is consumed", async () =>
        {
            var selector = new SelectionWindow(MonitorService.GetCurrent()); bool closed = false;
            selector.Closed += (_, _) => closed = true; selector.Show();
            var continuation = selector.Result.ContinueWith(_ => Check(closed, "Selection task continued before Closed completed"), TaskScheduler.FromCurrentSynchronizationContext());
            selector.CompleteSelection(new Rect(10, 20, 100, 80)); await continuation;
            Check((await selector.Result) == new Rect(10, 20, 100, 80), "Selected bounds changed");
        });
        await Test("Capture visibility scope tolerates closed windows and preserves activation preference", async () =>
        {
            var a = new Window { Width = 120, Height = 90, ShowActivated = true }; var b = new Window { Width = 120, Height = 90, ShowActivated = false }; a.Show(); b.Show();
            using (new HiddenWindowsScope()) { Check(!a.IsVisible && !b.IsVisible, "Controls remain visible"); a.Close(); }
            Check(b.IsVisible && !b.ShowActivated, "Original activation preference was changed"); b.Close(); await Settle();
        });
        await Test("All settings pages render in both themes", async () =>
        {
            controller.OpenMain(); var main = Field<MainWindow>(controller, "main");
            foreach (string theme in new[] { "Light", "Dark" })
            {
                controller.Settings.Theme = theme; controller.ApplyTheme();
                foreach (string page in new[] { "Home", "Draw", "Capture", "Present", "Profiles", "Shortcuts", "Settings", "About" })
                {
                    main.Navigate(page); await Task.Delay(250); main.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(main);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create($"{page}-{theme}.png"); encoder.Save(stream);
                }
            }
            main.Hide(); await Settle();
        });
        await Test("Draw, Interact and hide retain the annotation document", async () =>
        {
            controller.ToggleDraw(); controller.Document.Add(new Annotation { Points = new[] { new Point(100, 100), new Point(160, 160) } });
            Check(controller.State == OverlayState.Draw, "Did not enter Draw");
            controller.Interact(); Check(controller.State == OverlayState.Interact && controller.Document.Items.Count == 1, "Interact lost drawing");
            Check(!Field<OverlayWindow>(controller, "overlay").IsHitTestVisible && controller.Palette?.IsVisible == false, "Interact still intercepts input");
            controller.ToggleDraw(); Check(controller.State == OverlayState.Draw && Field<OverlayWindow>(controller, "overlay").IsHitTestVisible, "Draw did not restore input");
            controller.HideAnnotations(); Check(controller.Document.Items.Count == 1 && controller.State == OverlayState.Hidden, "Hide lost session"); await Settle();
        });
        await Test("Completed capture returns one clean cropped image and restores state", async () =>
        {
            controller.Settings.CaptureOutput = "Clipboard"; controller.Settings.IncludeAnnotations = true;
            controller.ToggleDraw(); var task = controller.CaptureAsync();
            var selector = await WaitForCaptureSelector(controller);
            Check(selector != null, "Capture selector missing: " + controller.Status);
            selector!.CompleteSelection(new Rect(80, 80, 120, 120)); await task;
            Check(controller.LastCapture != null, "Capture produced no image: " + controller.Status);
            var monitor = Field<OverlayWindow>(controller, "overlay").Monitor;
            Check(controller.LastCapture!.PixelWidth == (int)Math.Ceiling(200 * monitor.ScaleX) - (int)Math.Floor(80 * monitor.ScaleX), "Crop width is misaligned");
            Check(controller.State == OverlayState.Draw && controller.Palette!.IsVisible, "Prior drawing mode not restored");
            Check(!controller.Status.StartsWith("Capture failed"), controller.Status); controller.HideAnnotations();
        });
        await Test("Cancelled capture preserves clipboard result and returns to Hidden", async () =>
        {
            Check(!controller.IsBusy && controller.Settings.CaptureEnabled, "Capture cancellation precondition failed");
            var previous = controller.LastCapture; var task = controller.CaptureAsync();
            var selection = await WaitForCaptureSelector(controller);
            Check(selection != null, "Capture selector missing: " + controller.Status + "; busy=" + controller.IsBusy);
            selection!.Close(); await task;
            Check(ReferenceEquals(previous, controller.LastCapture) && controller.State == OverlayState.Hidden, "Cancel changed output/state");
        });
        await Test("Repeat capture retains region and Escape cancels delayed capture", async () =>
        {
            Check(controller.CanRepeatCapture, "Successful selection did not retain a region");
            var previous = controller.LastCapture!;
            await controller.CaptureAsync(true);
            Check(controller.LastCapture != null && !ReferenceEquals(previous, controller.LastCapture), "Repeat did not produce a new screenshot");
            Check(controller.LastCapture!.PixelWidth == previous.PixelWidth && controller.LastCapture.PixelHeight == previous.PixelHeight, "Repeat changed crop dimensions");
            previous = controller.LastCapture;
            controller.Settings.CaptureDelaySeconds = 3;
            var delayed = controller.CaptureAsync(true); await Task.Delay(80);
            controller.CancelActiveTool(); await delayed;
            Check(!controller.IsBusy && controller.State == OverlayState.Hidden && ReferenceEquals(previous, controller.LastCapture), "Cancelled delay changed capture or left input active");
            controller.Settings.CaptureDelaySeconds = 0;
        });
        await Test("Disabling drawing shortcut leaves the tool available", async () =>
        {
            controller.ToggleDraw();
            Check(controller.UpdateSettings(s => FeatureShortcutCatalog.SetEnabled(s, FeatureShortcutCatalog.All.Single(e => e.Action == "Draw"), false)), "Shortcut update failed");
            Check(controller.Settings.DrawingEnabled && controller.State == OverlayState.Draw, "Shortcut update stopped drawing");
            controller.HideAnnotations();
            Check(controller.State == OverlayState.Hidden, "Hiding drawing retained input");
            Check(controller.UpdateSettings(s => FeatureShortcutCatalog.SetEnabled(s, FeatureShortcutCatalog.All.Single(e => e.Action == "Draw"), true)), "Could not restore shortcut"); await Settle();
        });
        await Test("One hundred draw/interact/hide cycles retain bounded window and handle counts", async () =>
        {
            // Allow lazy WPF resources and input infrastructure to settle before measuring retention.
            for (int warm = 0; warm < 10; warm++) { controller.ToggleDraw(); controller.Interact(false); controller.HideAnnotations(); await Settle(); }
            controller.ToggleDraw(); controller.HideAnnotations(); await Settle();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Settle();
            int windows = Application.Current.Windows.Count; using var process = Process.GetCurrentProcess(); process.Refresh(); int handles = process.HandleCount;
            var timings = new List<double>();
            for (int i = 0; i < 100; i++) { var watch = Stopwatch.StartNew(); controller.ToggleDraw(); timings.Add(watch.Elapsed.TotalMilliseconds); controller.Interact(false); controller.ToggleDraw(); controller.HideAnnotations(); if (i % 10 == 0) await Settle(); }
            await Settle(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Settle(); process.Refresh();
            Check(!controller.IsBusy && controller.Palette != null, "No actual drawing window was cycled");
            Check(Application.Current.Windows.Count <= windows, $"Window count grew across cycles: {windows} -> {Application.Current.Windows.Count}");
            if (process.HandleCount > handles + 20)
            {
                int firstBatch = process.HandleCount;
                for (int repeat = 0; repeat < 100; repeat++) { controller.ToggleDraw(); controller.Interact(false); controller.HideAnnotations(); if (repeat % 10 == 0) await Settle(); }
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Settle(); process.Refresh();
                Check(process.HandleCount <= firstBatch + 5, $"Handles continued accumulating across two batches: {handles} -> {firstBatch} -> {process.HandleCount}");
                Results.Add($"MEASURE handle plateau across two batches: {handles} -> {firstBatch} -> {process.HandleCount}");
            }
            Results.Add($"MEASURE warm activation p95: {timings.Order().ElementAt(94):F2} ms; handles {handles} -> {process.HandleCount}; windows {windows}");
        });
        if (skipInput) Results.Add("SKIP Cross-process native input: --no-input selected; no injected mouse/keyboard events.");
        else await Test("Cross-process native input", () => NativeInputChecks.RunAsync(controller, Results.Add));
        await Test("Native presentation initialization and lifecycle", PresentationSessionChecks.RunAsync);
        await Test("Presentation shortcuts do not stop sessions; stopping releases callbacks", async () =>
        {
            controller.HideAnnotations();
            controller.Settings.LaserMonitorMode = "All";
            controller.Settings.LaserEnabled = true; controller.Settings.SpotlightEnabled = true;
            controller.TogglePresentation("Laser"); await Settle();
            var session = Field<PresentationSession>(controller, "presentation");
            var lasers = Field<System.Collections.Generic.List<PresentationEffectWindow>>(session, "lasers").ToArray();
            Check(lasers.Length == MonitorService.GetAll().Count && lasers.All(effect => Field<bool>(effect, "_rendering") && !effect.IsHitTestVisible), "Laser surfaces are missing, inactive or block desktop input");
            foreach (var effect in lasers)
            {
                effect.Hide(); Check(!Field<bool>(effect, "_rendering"), "Hidden laser still receives frame callbacks"); effect.Show();
            }
            Check(controller.UpdateSettings(s => FeatureShortcutCatalog.SetEnabled(s, FeatureShortcutCatalog.All.Single(e => e.Action == "Laser"), false)), "Could not disable laser shortcut");
            Check(lasers.All(effect => Field<bool>(effect, "_rendering")), "Disabling shortcut stopped laser");
            controller.StopPresentation();
            Check(lasers.All(effect => !Field<bool>(effect, "_rendering") && !Application.Current.Windows.Cast<Window>().Contains(effect)) && Field<Window?>(controller, "presentationControls") == null, "Stopped laser retained callbacks/windows");
            controller.UpdateSettings(s => FeatureShortcutCatalog.SetEnabled(s, FeatureShortcutCatalog.All.Single(e => e.Action == "Laser"), true));
            controller.TogglePresentation("Spotlight"); await Settle();
            session = Field<PresentationSession>(controller, "presentation");
            controller.StopPresentation();
            Check(!Field<DispatcherTimer>(session, "timer").IsEnabled && Field<Window?>(controller, "presentationControls") == null, "Stopped spotlight retained timer/controls");
        });
        await Test("Freeze frame retains its still when its shortcut is disabled", async () =>
        {
            controller.Settings.FreezeEnabled = true; await controller.FreezeAsync();
            Check(controller.State == OverlayState.Draw && Field<BitmapSource>(controller, "frozenFrame") != null, "Freeze did not enter drawing with a still");
            var oldFrame = Field<BitmapSource>(controller, "frozenFrame");
            controller.CancelActiveTool();
            Check(Field<BitmapSource?>(controller, "frozenFrame") == null, "Escape retained a stale frozen frame");
            await controller.FreezeAsync();
            Check(controller.State == OverlayState.Draw && !ReferenceEquals(oldFrame, Field<BitmapSource?>(controller, "frozenFrame")), "Freeze reactivation did not capture a fresh image");
            Check(controller.UpdateSettings(s => FeatureShortcutCatalog.SetEnabled(s, FeatureShortcutCatalog.All.Single(e => e.Action == "Freeze"), false)), "Could not disable freeze shortcut");
            Check(Field<BitmapSource?>(controller, "frozenFrame") != null, "Disabling shortcut removed frozen still");
            controller.HideAnnotations();
            Check(Field<BitmapSource?>(controller, "frozenFrame") == null, "Hiding freeze retained its still");
            controller.UpdateSettings(s => FeatureShortcutCatalog.SetEnabled(s, FeatureShortcutCatalog.All.Single(e => e.Action == "Freeze"), true));
            var cancelled = controller.FreezeAsync(); controller.CancelActiveTool(); await cancelled;
            Check(controller.State == OverlayState.Hidden && !controller.IsBusy && Field<BitmapSource?>(controller, "frozenFrame") == null, "Escape during freeze reactivated the overlay");
        });
        await Test("Pinned image and redaction editor construct and close without orphan windows", async () =>
        {
            Check(controller.LastCapture != null, "Previous capture unavailable"); int before = Application.Current.Windows.Count;
            controller.PinLast(); controller.RedactLast(); await Settle();
            var extras = Application.Current.Windows.Cast<Window>().Where(w => w is PinnedImageWindow or ScreenshotEditorWindow).ToArray();
            Check(extras.Length == 2, "Utility windows did not open");
            foreach (string theme in new[] { "Light", "Dark" })
            {
                controller.Settings.Theme = theme; controller.ApplyTheme();
                foreach (var window in extras)
                {
                    if (window is not PinnedImageWindow) { window.Width = window.MinWidth; window.Height = Math.Max(window.MinHeight, 520); }
                    await Task.Delay(250); window.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create($"{window.GetType().Name}-{theme}.png"); encoder.Save(stream);
                }
            }
            foreach (var window in extras) window.Close();
            for (int i = 0; i < 100 && extras.Any(window => Application.Current.Windows.Cast<Window>().Contains(window)); i++)
                await Task.Delay(20);
            Check(Application.Current.Windows.Count == before, "Utility windows remained after close");
        });
        if (performance) await Test("Idle CPU over sixty seconds", async () =>
        {
            controller.HideAnnotations(); controller.StopPresentation(); foreach (Window w in Application.Current.Windows) w.Hide(); await Settle();
            using var process = Process.GetCurrentProcess(); process.Refresh(); var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(60)); process.Refresh();
            double percent = (process.TotalProcessorTime - cpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            Results.Add($"MEASURE idle CPU: {percent:F3}% normalized across {Environment.ProcessorCount} logical processors over {watch.Elapsed.TotalSeconds:F1}s; private memory {process.PrivateMemorySize64 / 1048576d:F1} MiB");
            Check(percent < .5, $"Idle CPU {percent:F3}% exceeds 0.5% target");
        });
        foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) if (window is not MainWindow) window.Close();
    }
}
