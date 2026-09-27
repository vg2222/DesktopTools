using System;
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
using DesktopTools.Localization;
using DesktopTools.Native;

internal static class EditorChromeChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static Button Caption(Window window, string label) => Walk(window)
        .OfType<Button>().SingleOrDefault(button => AutomationProperties.GetName(button) == L.T(label))
        ?? throw new Exception(window.GetType().Name + " has no " + label + " caption control");

    private static void Render(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(name); encoder.Save(stream);
    }

    internal static Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(settings =>
        {
            settings.Animations = false;
            settings.VideoEditorEnabled = true;
            settings.ImageToolsEnabled = true;
        });
        try
        {
            controller.OpenVideoEditor();
            var video = Application.Current.Windows.OfType<VideoEditorWindow>().Single();
            Check(video.WindowState == WindowState.Maximized, "Video editor did not open maximized");
            Render(video, "editor-chrome-video.png");
            var videoMaximize = Caption(video, "Maximize or restore");
            videoMaximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(video.WindowState == WindowState.Normal, "Video editor did not restore to a resizable window");
            videoMaximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(video.WindowState == WindowState.Maximized, "Video editor did not maximize again");
            Caption(video, "Minimize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(video.WindowState == WindowState.Minimized, "Video editor minimize button did not work");
            controller.OpenVideoEditor();
            Check(video.WindowState == WindowState.Maximized, "Reopening the editor lost its maximized state");
            video.Close();

            controller.OpenImageTools();
            var image = Application.Current.Windows.OfType<ImageToolsWindow>().Single();
            Check(image.WindowState == WindowState.Maximized, "Image editor did not open maximized");
            Render(image, "editor-chrome-image.png");
            Caption(image, "Maximize or restore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(image.WindowState == WindowState.Normal, "Image editor did not restore to a resizable window");
            Caption(image, "Minimize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(image.WindowState == WindowState.Minimized, "Image editor minimize button did not work");
            controller.OpenImageTools();
            Check(image.WindowState == WindowState.Normal, "Reopening a restored image editor changed its window size");
            image.Close();

            var qr = new QrCodeWindow(_ => { });
            try
            {
                qr.Show();
                Caption(qr, "Minimize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(qr.WindowState == WindowState.Minimized, "Feature window minimize button did not work");
                NativeWindowService.ShowForeground(qr);
                Check(qr.WindowState == WindowState.Normal, "Feature window could not be restored from the app");
            }
            finally { qr.Close(); }

            Window[] additional =
            [
                new TextToolsWindow(controller),
                new NotesWindow(Array.Empty<FloatingNote>(), () => { }, _ => { }, _ => { }, () => true, false),
                new AudioControlsWindow(_ => { }),
                new FileShelfWindow(_ => { })
            ];
            foreach (var window in additional)
            {
                try
                {
                    Check(window.ShowInTaskbar, window.GetType().Name + " cannot be restored from the taskbar");
                    window.Show();
                    Caption(window, "Minimize");
                }
                finally { window.Close(); }
            }
        }
        finally
        {
            foreach (var window in Application.Current.Windows.OfType<VideoEditorWindow>().Cast<Window>()
                .Concat(Application.Current.Windows.OfType<ImageToolsWindow>()).ToArray()) window.Close();
        }
        return Task.CompletedTask;
    }
}
