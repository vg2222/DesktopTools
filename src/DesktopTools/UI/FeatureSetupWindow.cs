using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopTools.Core;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal sealed class FeatureSetupWindow : Window
{
    internal bool ContinueToGuide { get; private set; }
    internal FeatureSetupWindow(string feature, AppSettings settings, Func<Action<AppSettings>, bool> save)
    {
        Title = L.T("Feature setup"); Width = 590; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel(); var header = UtilityWindowChrome.Header(this, Title, Close, L.T("Close"), 18);
        panel.Children.Add(Ui.Text(L.T("Adjust these options before the guide. You can change them later."), 13, muted: true));
        Action<AppSettings> apply = _ => { };
        if (feature == "recorder")
        {
            string quality = settings.RecordingQuality; int fps = settings.RecordingFramesPerSecond; bool mic = settings.RecordingMicrophone, audio = settings.RecordingSystemAudio;
            panel.Children.Add(Ui.Row(L.T("Quality"), null, Ui.Choice(new[] { "Economy", "Balanced", "High" }, quality, value => quality = value)));
            panel.Children.Add(Ui.Row("FPS", null, Ui.Choice(new[] { "24", "30", "60", "90", "120", "144" }, fps.ToString(), value => fps = int.Parse(value), translate: false)));
            panel.Children.Add(Ui.Row(L.T("Microphone"), null, Ui.Toggle(mic, value => mic = value)));
            panel.Children.Add(Ui.Row(L.T("System audio"), null, Ui.Toggle(audio, value => audio = value)));
            apply = s => { s.RecordingQuality = quality; s.RecordingFramesPerSecond = fps; s.RecordingMicrophone = mic; s.RecordingSystemAudio = audio; };
        }
        else if (feature == "teleprompter")
        {
            double speed = settings.TeleprompterSpeed, font = settings.TeleprompterFontSize; bool pinned = settings.TeleprompterTopmost;
            panel.Children.Add(Ui.Row(L.T("Speed"), null, Ui.Choice(new[] { 10d, 20, 40, 60, 90, 120, 180 }.Append(speed).Distinct().OrderBy(v => v).Select(v => v.ToString()), speed.ToString(), value => speed = double.Parse(value), translate: false)));
            panel.Children.Add(Ui.Row(L.T("Text size"), null, Ui.Choice(new[] { 18d, 24, 32, 40, 48, 64 }.Append(font).Distinct().OrderBy(v => v).Select(v => v.ToString()), font.ToString(), value => font = double.Parse(value), translate: false)));
            panel.Children.Add(Ui.Row(L.T("Always on top"), null, Ui.Toggle(pinned, value => pinned = value)));
            apply = s => { s.TeleprompterSpeed = speed; s.TeleprompterFontSize = font; s.TeleprompterTopmost = pinned; };
        }
        else if (feature == "text-tools")
        {
            string direction = settings.TranslationDirection, language = settings.ScreenTextLanguage;
            panel.Children.Add(Ui.Row(L.T("Translation direction"), null, Ui.Choice(new[] { "English → Russian", "Russian → English" }, direction == "ru-en" ? "Russian → English" : "English → Russian", value => direction = value == "Russian → English" ? "ru-en" : "en-ru")));
            var languages = DesktopTools.Extras.LocalOcr.Languages; var choice = new ComboBox { ItemsSource = languages, SelectedItem = languages.FirstOrDefault(l => l.Tag == language) ?? languages.FirstOrDefault(), MinWidth = 180 };
            choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is DesktopTools.Extras.OcrLanguage selected) language = selected.Tag; };
            panel.Children.Add(Ui.Row(L.T("Screen text language"), L.T("Uses installed Windows OCR language packs."), choice));
            apply = s => { s.TranslationDirection = direction; s.ScreenTextLanguage = language; };
        }
        else if (feature == "video-editor")
        {
            bool mute = settings.VideoMuteOnOpen;
            panel.Children.Add(Ui.Row(L.T("Mute new videos"), L.T("Start newly opened videos with export audio disabled. You can change Mute for each video."), Ui.Toggle(mute, value => mute = value)));
            apply = s => s.VideoMuteOnOpen = mute;
        }
        else throw new ArgumentException("Feature setup is not defined.", nameof(feature));
        var error = Ui.Text("", 12, muted: true); panel.Children.Add(error);
        void Finish(bool skip)
        {
            if (!save(s => { if (!skip) apply(s); s.FeatureSetup[feature] = new OnboardingProgress { Status = skip ? SetupStatus.Skipped : SetupStatus.Completed }; })) { error.Text = L.T("Changes not yet saved"); return; }
            ContinueToGuide = true; Close();
        }
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; actions.Children.Add(Ui.Button(L.T("Skip setup"), () => Finish(true))); actions.Children.Add(Ui.Button(L.T("Continue to guide"), () => Finish(false), true));
        Content = UtilityWindowChrome.DialogCard(this, header, panel, actions, 22);
    }
}
