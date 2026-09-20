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
using DesktopTools.Core;
using DesktopTools.Extras;

internal static class PrompterStudioChecks
{
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Render(Window window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(name + ".png"); encoder.Save(output);
    }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true); controller.UpdateSettings(s => { s.Theme = "Dark"; s.Animations = false; });
        var settings = new AppSettings { TeleprompterText = string.Join("\n\n", Enumerable.Repeat("Привет всем!\nСегодня я расскажу об идее, которая поможет нам работать спокойнее и эффективнее.", 16)) };
        bool canSave = true;
        var window = new TeleprompterWindow(settings, change => { if (!canSave) return false; change(settings); return true; }); window.Show(); await Task.Delay(70);
        Check(!window.IsPresentation, "Prompter did not start in studio mode");
        var editor = Field<TextBox>(window, "editor"); var scroll = Field<ScrollViewer>(window, "scroll");
        editor.Text += "\nНовая мысль"; double width = window.Width, height = window.Height, left = window.Left, top = window.Top;
        Render(window, "prompter-studio-Dark"); window.SetPresentation(true); await Task.Delay(70);
        Check(window.IsPresentation && settings.TeleprompterText.EndsWith("Новая мысль"), "Presentation entry lost pending edits");
        scroll.ScrollToVerticalOffset(160); await Task.Delay(30); double position = scroll.VerticalOffset;
        Render(window, "prompter-presentation-Dark"); window.SetPresentation(false); await Task.Delay(30); window.SetPresentation(true); await Task.Delay(30);
        Check(Math.Abs(scroll.VerticalOffset - position) < 2, "Mode switch reset reading position");
        window.SetPresentation(false); Check(window.Width == width && window.Height == height && window.Left == left && window.Top == top, "Studio size or position not restored");
        window.TogglePlayback(); await Task.Delay(70); Check(window.IsRunning, "Playback did not start"); window.Hide(); Check(!window.IsRunning, "Hidden prompter kept rendering"); window.Show();
        canSave = false; editor.Text += "\nНе потерять"; window.Close(); Check(window.IsVisible, "Save failure allowed prompter to close");
        canSave = true; window.Close(); Check(settings.TeleprompterText.EndsWith("Не потерять"), "Closing lost pending text");
    }
}
