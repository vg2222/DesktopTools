using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed class RecordingQualityWindow : Window
{
    internal RecordingQualityWindow(AppSettings settings, Func<string, int, bool, bool> save)
    {
        Title = L.T("Recording quality"); Width = 590; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        string quality = settings.RecordingQuality; int fps = settings.RecordingFramesPerSecond; bool hardware = settings.RecordingHardwareAcceleration;
        var panel = new StackPanel(); var header = UtilityWindowChrome.Header(this, Title, Close, L.T("Close"), 16);
        var description = Ui.Text("", 12, muted: true);
        void Describe() => description.Text = L.T(quality switch { "Economy" => "Smaller files, fewer fine details.", "High" => "More detail, larger files.", _ => "Balanced detail and file size for everyday recording." });
        var choice = Ui.Choice(new[] { "Economy", "Balanced", "High" }, quality, value => { quality = value; Describe(); }); choice.Width = 260;
        panel.Children.Add(Ui.Row(L.T("Quality"), null, choice)); description.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(description); Describe();
        var rate = Ui.Choice(new[] { "24", "30", "60", "90", "120", "144" }, fps.ToString(), value => fps = int.Parse(value), translate: false); rate.Width = 260;
        panel.Children.Add(Ui.Row("FPS", null, rate));
        var hint = Ui.Text(L.T("Frame rate is a target. Actual smoothness depends on the display, source and computer."), 12, muted: true); hint.Margin = new Thickness(0, 8, 0, 20); panel.Children.Add(hint);
        panel.Children.Add(Ui.Row(L.T("Hardware acceleration"), L.T("Use graphics hardware when available. Turn off if recording fails or looks incorrect."), Ui.Toggle(hardware, value => hardware = value)));
        var done = Ui.Button(L.T("Done"), () => { if (save(quality, fps, hardware)) Close(); }, true); done.Width = 200; done.Margin = new Thickness(0, 12, 0, 0); done.HorizontalAlignment = HorizontalAlignment.Center;
        Content = UtilityWindowChrome.DialogCard(this, header, panel, done, 18);
    }
}
