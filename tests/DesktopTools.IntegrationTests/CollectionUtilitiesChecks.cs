using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Native;

internal static class CollectionUtilitiesChecks
{
    private sealed class Audio : IAudioControlsService
    {
        public int Writes; public bool Disposed; public bool Connected = true;
        public string LastId = "";
        public int ExtraApps;
        public double AppVolume = 65, MasterVolume = 72;
        public string OutputId = "output"; public bool RejectOutput, RejectVolume;
        public bool CanSelectOutput => true;
        public IReadOnlyList<AudioOutputDevice> ReadOutputDevices() => Connected ? new[] { new AudioOutputDevice("output", "Headphones"), new AudioOutputDevice("speakers", "Speakers") } : Array.Empty<AudioOutputDevice>();
        public void SelectOutput(string id) { if (RejectOutput) throw new InvalidOperationException("Test rejection"); Writes++; OutputId = LastId = id; }
        public IReadOnlyList<AudioApplication> ReadApplications() => Connected ? new[] { new AudioApplication(OutputId + "/browser", "Browser", AppVolume, false, 1), new AudioApplication(OutputId + "/music", "Music", 30, true, 1) }
            .Concat(Enumerable.Range(1, ExtraApps).Select(i => new AudioApplication(OutputId + "/extra" + i, "Player " + i.ToString("00"), 50, false, 0))).ToArray() : Array.Empty<AudioApplication>();
        public AudioEndpoint? ReadEndpoint(bool mic) => Connected ? new(mic ? "mic" : OutputId, mic ? "Microphone" : OutputId == "output" ? "Headphones" : "Speakers", mic ? 78 : MasterVolume, false) : null;
        public void SetVolume(string key, double percent) { if (RejectVolume || !key.StartsWith(OutputId + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Stale/rejected volume"); Writes++; LastId = key; }
        public void SetMuted(string key, bool muted) { Writes++; LastId = key; }
        public void SetEndpointVolume(string id, double percent) { if (RejectVolume) throw new InvalidOperationException("Rejected endpoint volume"); Writes++; LastId = id; }
        public void SetEndpointMuted(string id, bool muted) { Writes++; LastId = id; }
        public void Dispose() => Disposed = true;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    private static void Render(Window window, string name)
    {
        window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(name + ".png"); encoder.Save(output);
    }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Theme = "Dark"; s.Animations = false; });
        DesktopTools.Localization.L.Use("en");
        var display = new MonitorInfo("test", new Rect(-2560, 0, 2560, 1440), new Rect(-2560, 0, 2560, 1360), 1.5, 1.5);
        var hudBounds = RecordingHudWindow.BottomPlacement(display, new Size(470, 78));
        Check(display.WorkingArea.Contains(hudBounds) && hudBounds.Bottom == 1330 && hudBounds.Left + hudBounds.Width / 2 == -1280,
            "Recording controls did not center above the taskbar on a scaled negative-origin display");
        var audio = new Audio(); var mixer = new AudioControlsWindow(_ => { }, audio); mixer.Show(); await Task.Delay(80);
        Check(audio.Writes == 0, "Opening audio controls changed volume");
        var sliders = Children(mixer).OfType<Slider>().ToArray(); Check(sliders.Length == 4, "Mixer missing app/master/microphone sliders");
        sliders.Single(s => AutomationProperties.GetName(s) == "Master volume").Value = 50;
        Check(audio.LastId == "output", "Master volume targeted wrong endpoint");
        sliders.Single(s => AutomationProperties.GetName(s) == "Microphone").Value = 40;
        Check(audio.LastId == "mic", "Microphone targeted wrong endpoint");
        sliders.Single(s => AutomationProperties.GetName(s) == "Browser volume").Value = 20;
        Check(audio.LastId == "output/browser", "App volume targeted wrong session");
        int writes = audio.Writes;
        var focused = sliders.Single(s => AutomationProperties.GetName(s) == "Browser volume"); focused.Focus();
        audio.AppVolume = 45; audio.MasterVolume = 62;
        typeof(AudioControlsWindow).GetMethod("OnTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mixer, new object?[] { null, EventArgs.Empty });
        Check(ReferenceEquals(focused, Children(mixer).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "Browser volume")) && focused.Value == 45, "Refresh replaced focused slider or ignored external volume");
        Check(sliders.Single(s => AutomationProperties.GetName(s) == "Master volume").Value == 62 && audio.Writes == writes, "Read-only refresh changed system volume");
        var mute = Children(mixer).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mute Browser"); var muteIcon = mute.Content;
        mixer.Refresh(); Check(ReferenceEquals(muteIcon, mute.Content), "Unchanged refresh recreated mute controls");
        audio.RejectVolume = true;
        focused.Value = 19; sliders.Single(s => AutomationProperties.GetName(s) == "Master volume").Value = 18;
        Check(focused.Value == 45 && sliders.Single(s => AutomationProperties.GetName(s) == "Master volume").Value == 62 && audio.Writes == writes,
            "Rejected volume edit remained on screen or issued a corrective write");
        audio.RejectVolume = false;
        audio.OutputId = "speakers"; focused.Value = 17;
        Check(focused.Value == 45 && audio.Writes == writes, "Stale application control changed the new device");
        mixer.Refresh();
        Check(!ReferenceEquals(focused, Children(mixer).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "Browser volume")), "Output change retained the old application control");
        audio.OutputId = "output"; mixer.Refresh(); focused = Children(mixer).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "Browser volume");
        mixer.UpdateLayout(); await Task.Delay(30);
        Check(focused.CaptureMouse(), "Test slider could not capture the mouse"); audio.AppVolume = 80; mixer.Refresh(); Check(focused.Value == 45, "Refresh interrupted captured slider"); focused.ReleaseMouseCapture(); mixer.Refresh(); Check(focused.Value == 80, "Refresh did not resume after drag");
        Render(mixer, "audio-mixer-Dark");
        audio.ExtraApps = 12; mixer.Refresh(); mixer.UpdateLayout();
        Check(ReferenceEquals(focused, Children(mixer).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "Browser volume")), "Adding sessions replaced existing app sliders");
        var appScroll = Children(mixer).OfType<ScrollViewer>().Single(s => s.Content is StackPanel panel && panel.Children.OfType<StackPanel>().Any());
        Check(appScroll.ScrollableWidth == 0 && appScroll.ScrollableHeight > 0, "Many apps did not produce a vertical-only scroll area");
        appScroll.ScrollToEnd(); await Task.Delay(30); mixer.UpdateLayout();
        var lastSlider = Children(mixer).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "Player 12 volume");
        double lastY = lastSlider.TransformToAncestor(appScroll).Transform(new Point()).Y;
        Check(lastY >= 0 && lastY + lastSlider.ActualHeight <= appScroll.ActualHeight, "Last application cannot be reached by scrolling");
        Render(mixer, "audio-mixer-scrolled-Dark");
        audio.ExtraApps = 0; mixer.Refresh(); appScroll.ScrollToHome();
        var outputChoice = Children(mixer).OfType<ComboBox>().Single();
        outputChoice.SelectedValue = "speakers";
        Check(audio.OutputId == "speakers" && audio.Writes == writes + 1, "Output selection did not issue exactly one explicit change");
        audio.RejectOutput = true;
        Children(mixer).OfType<ComboBox>().Single().SelectedValue = "output";
        Check(audio.OutputId == "speakers" && (string)Children(mixer).OfType<ComboBox>().Single().SelectedValue == "speakers", "Failed output selection did not restore actual device");
        audio.Connected = false; mixer.Refresh();
        Check(!Children(mixer).OfType<Slider>().Any(), "Disconnected endpoint retained controls");
        mixer.Close(); Check(audio.Disposed, "Audio service not disposed");
        using (var native = new AudioSessionService())
        {
            Check(native.ReadOutputDevices().All(device => device.Id.Length > 0), "Invalid native output device list");
            File.WriteAllText("audio-output-capability.txt", "Output selection ABI available: " + native.CanSelectOutput + "; actual switching not performed.");
            // Read-only: never change the user's volume, mute or output device.
            foreach (bool mic in new[] { false, true })
            {
                var endpoint = native.ReadEndpoint(mic);
                if (endpoint != null) Check(endpoint.Volume >= 0 && endpoint.Volume <= 100 && endpoint.Id.Length > 0, "Invalid native endpoint snapshot");
            }
        }
        string folder = Path.Combine(Environment.CurrentDirectory, "Полка café " + Guid.NewGuid().ToString("N"), new string('я', 80)); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "Длинное имя • 日本語.txt"); File.WriteAllText(file, "original");
        var notices = new List<string>(); var shelf = new FileShelfWindow(notices.Add); shelf.Show(); shelf.AddPaths(new[] { file, folder, file, file + ".missing" });
        Check(shelf.Paths.Count == 2 && notices.Count == 2, "Shelf did not explain duplicates/missing paths");
        await Task.Delay(50); Render(shelf, "file-shelf-compact-Dark");
        Children(shelf).OfType<Button>().First(b => AutomationProperties.GetName(b).StartsWith("Remove ")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(shelf.Paths.Count == 1 && File.ReadAllText(file) == "original" && Directory.Exists(folder), "Shelf removal modified originals");
        shelf.Shutdown(); Check(File.ReadAllText(file) == "original", "Shutdown modified original file");
    }
}
