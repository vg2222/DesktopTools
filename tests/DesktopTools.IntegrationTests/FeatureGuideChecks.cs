using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.UI;

internal static class FeatureGuideChecks
{
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Walk(child))yield return nested;}
    }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        var settings = new DesktopTools.Core.AppSettings { TeleprompterText = "Keep my script", RecordingFramesPerSecond = 30 };
        bool allowSave = true;
        bool Save(Action<DesktopTools.Core.AppSettings> apply) { if (!allowSave) return false; apply(settings); return true; }
        foreach (string feature in new[] { "recorder", "teleprompter", "text-tools", "video-editor" })
        {
            L.Use("en"); var setup = new FeatureSetupWindow(feature, settings, Save); setup.Show(); await Task.Delay(40);
            var firstChoice = Walk(setup).OfType<ComboBox>().FirstOrDefault(); if(firstChoice != null) firstChoice.SelectedIndex = 0; setup.Close();
            if (settings.FeatureSetup.ContainsKey(feature) || settings.TeleprompterText != "Keep my script" || settings.RecordingFramesPerSecond != 30) throw new Exception("Cancelled setup changed preferences");
            setup = new FeatureSetupWindow(feature, settings, Save); setup.Show(); await Task.Delay(40); var done = Walk(setup).OfType<Button>().Single(b => b.Content as string == "Continue to guide");
            allowSave = false; done.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (!setup.IsVisible || setup.ContinueToGuide) throw new Exception("Failed save advanced setup");
            allowSave = true; done.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (setup.IsVisible || !setup.ContinueToGuide || settings.FeatureSetup[feature].Status != DesktopTools.Core.SetupStatus.Completed) throw new Exception("Setup did not complete");
            setup = new FeatureSetupWindow(feature, settings, Save); setup.Show(); await Task.Delay(40); Walk(setup).OfType<Button>().Single(b => b.Content as string == "Skip setup").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (settings.FeatureSetup[feature].Status != DesktopTools.Core.SetupStatus.Skipped || !setup.ContinueToGuide) throw new Exception("Setup skip did not allow independent guide");
        }
        foreach (string feature in new[] { "recorder", "teleprompter", "text-tools", "video-editor" }) controller.Settings.FeatureSetup[feature] = new DesktopTools.Core.OnboardingProgress { Status = DesktopTools.Core.SetupStatus.Completed };
        foreach(var theme in new[]{"Dark","Light"}) foreach(var language in new[]{"ru","en","de","fr","es"})
        {
            L.Use(language); controller.Settings.Theme=theme; controller.ApplyTheme();
            foreach (string feature in new[] { "recorder", "teleprompter", "text-tools", "video-editor" })
            {
                var setup = new FeatureSetupWindow(feature, settings, Save); setup.Show(); await Task.Delay(20); setup.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(setup.ActualWidth),(int)Math.Ceiling(setup.ActualHeight),96,96,PixelFormats.Pbgra32); bitmap.Render(setup); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using(var stream=File.Create($"feature-setup-{feature}-{language}-{theme}.png")) png.Save(stream); setup.Close();
            }
            var sample=BitmapSource.Create(32,32,96,96,PixelFormats.Bgra32,null,new byte[32*32*4],32*4);sample.Freeze();
            Window[] windows = [new ScreenRecorderWindow(controller),new TeleprompterWindow(controller.Settings,controller.UpdateSettings),new TextToolsWindow(controller),new ImageToolsWindow(_=>{}),new VideoEditorWindow(_=>{}),new ScreenshotEditorWindow(sample,_=>{},_=>{},editorLayout:"A"),new ScreenshotEditorWindow(sample,_=>{},_=>{},editorLayout:"B")];
            foreach(var window in windows)
            {
                try
                {
                    window.Show(); await Task.Delay(40); window.UpdateLayout();
                    var button=Walk(window).OfType<Button>().Single(b=>(b.Tag as string)?.StartsWith("feature-guide-")==true); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var tour=(GuidedTour?)window.GetValue(FeatureTourButton.CurrentTourProperty);
                    if(tour?.IsOpen!=true)throw new Exception(window.GetType().Name+": guide did not open");
                    if(language=="ru" && theme=="Dark")
                    {
                        var popup=(System.Windows.Controls.Primitives.Popup)typeof(GuidedTour).GetField("popup",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(tour)!;
                        var card=(FrameworkElement)popup.Child; card.UpdateLayout(); var rendered=new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth),(int)Math.Ceiling(card.ActualHeight),96,96,PixelFormats.Pbgra32);rendered.Render(card);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(rendered));using(var stream=File.Create("feature-guide-"+window.GetType().Name+".png"))png.Save(stream);
                    }
                    for(int i=0;i<8 && tour.IsOpen;i++){tour.Next();await Task.Delay(10);}
                    if(tour.IsOpen)throw new Exception(window.GetType().Name+": guide stuck on unavailable target");
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));tour=(GuidedTour?)window.GetValue(FeatureTourButton.CurrentTourProperty);if(tour?.IsOpen!=true)throw new Exception("Guide could not restart");window.Hide();if(tour.IsOpen)throw new Exception("Hidden owner retained guide");
                    if (window is TeleprompterWindow || window is TextToolsWindow)
                    {
                        window.Show();
                        if (window is TeleprompterWindow compact) compact.SetPresentation(true);
                        else if (window is TextToolsWindow text)
                        {
                            text.SetSource(string.Join("\n", Enumerable.Repeat("Long translated example Пример текста", 30)));
                            typeof(TextToolsWindow).GetMethod("ShowMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(text, new object[] { true });
                        }
                        window.Width = window.MinWidth; window.UpdateLayout();
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); tour = (GuidedTour?)window.GetValue(FeatureTourButton.CurrentTourProperty);
                        if (tour?.IsOpen != true) throw new Exception("Compact/OCR guide failed to start");
                        for (int i = 0; i < 8 && tour.IsOpen; i++) { tour.Next(); await Task.Delay(10); }
                        if (tour.IsOpen) throw new Exception("Compact/OCR guide failed to complete");
                    }
                }
                finally{window.Close();}
            }
        }
    }
}
