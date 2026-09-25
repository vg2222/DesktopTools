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
using DesktopTools.UI;

internal static class RecorderTrayLayoutChecks
{
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        var recorder = new ScreenRecorderWindow(controller);
        try
        {
            recorder.Show(); await Task.Delay(130); recorder.UpdateLayout();
            T Field<T>(string name) => (T)typeof(ScreenRecorderWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(recorder)!;
            var quality = Field<ComboBox>("qualityChoice");
            var fps = Field<ComboBox>("fpsChoice");
            var start = Field<Button>("start");
            if (quality.ActualWidth < 180 || fps.ActualWidth < 105 || Math.Abs(quality.PointToScreen(new Point()).Y - fps.PointToScreen(new Point()).Y) > 2)
                throw new Exception("Quality and FPS controls do not fit side by side.");
            if (start.PointToScreen(new Point()).Y <= fps.PointToScreen(new Point()).Y + fps.ActualHeight)
                throw new Exception("Recording controls overlap the quality settings.");
            Render(recorder, "recorder-redesign.png");
        }
        finally { recorder.Close(); }

        int chosen = 0;
        var tray = new TrayMenuPopup(() => chosen = 1, () => chosen = 2, () => chosen = 3, () => chosen = 4, () => chosen = 5);
        try
        {
            tray.IsOpen = true; await Task.Delay(130);
            var card = (FrameworkElement)tray.Child;
            if (card.ActualWidth < 250 || card.ActualHeight < 230) throw new Exception("Tray menu did not lay out.");
            Render(card, "tray-menu-redesign.png");
            var action = ((Panel)((Border)card).Child).Children.OfType<Button>().First();
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Application.Current.Dispatcher.InvokeAsync(() => { });
            if (chosen != 1 || tray.IsOpen) throw new Exception("Tray action did not close the menu and run.");
        }
        finally { tray.IsOpen = false; }
    }

    private static void Render(FrameworkElement element, string name)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(name); png.Save(file);
    }
}
