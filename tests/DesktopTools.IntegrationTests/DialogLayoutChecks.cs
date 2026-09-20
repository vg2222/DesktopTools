using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class DialogLayoutChecks
{
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Theme = "Dark"; s.Animations = false; }); L.Use("de");
        string message = string.Concat(Enumerable.Repeat("A long explanation must remain readable without hiding the title or action buttons. ", 24));
        var windows = new Window[]
        {
            new ConfirmationDialog(L.T("Delete note"), message, L.T("Delete")),
            new SaveBeforeDisableDialog(() => Task.FromException<bool>(new InvalidOperationException(message))),
            new RecordingQualityWindow(new AppSettings(), (_, _, _) => false),
            new FeatureSetupWindow("recorder", new AppSettings(), _ => false)
        };
        try
        {
            foreach (var window in windows)
            {
                window.Show(); await Settle();
                if (window is RecordingQualityWindow)
                {
                    foreach (var monitor in MonitorService.GetAll())
                    {
                        Check(NativeMethods.SetWindowPos(new WindowInteropHelper(window).Handle, 0, (int)monitor.WorkingArea.X + 40, (int)monitor.WorkingArea.Y + 40,
                            0, 0, 0x01 | 0x04 | 0x10 | 0x0200), "Could not move owned dialog");
                        await Settle();
                        Check(Math.Abs(window.MaxHeight - (monitor.WorkingArea.Height / monitor.ScaleY - 24)) < 1, "Dialog retained the previous display's height limit");
                    }
                }
                window.MaxWidth = 380; window.MaxHeight = 300; window.UpdateLayout();
                if (window is SaveBeforeDisableDialog)
                    Descendants(window).OfType<Button>().Single(b => b.Name == "SaveBeforeDisable").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle(); window.UpdateLayout();
                var layout = (DockPanel)((Border)window.Content).Child;
                var header = (FrameworkElement)layout.Children[0]; var actions = (FrameworkElement)layout.Children[1];
                var scroll = layout.Children.OfType<ScrollViewer>().Single();
                Check(window.ActualWidth <= 381 && window.ActualHeight <= 301 && scroll.ScrollableHeight > 0, "Short dialog did not constrain its content");
                Point headerBefore = header.TranslatePoint(new Point(), window), actionsBefore = actions.TranslatePoint(new Point(), window);
                Check(headerBefore.Y >= 8 && actionsBefore.Y + actions.ActualHeight <= window.ActualHeight - 8, "Dialog chrome lost its inner padding");
                scroll.ScrollToEnd(); await Settle();
                Check(scroll.VerticalOffset > 0, "Dialog body could not scroll");
                Check(header.TranslatePoint(new Point(), window) == headerBefore && actions.TranslatePoint(new Point(), window) == actionsBefore, "Scrolling moved fixed dialog controls");
                foreach (var control in Descendants(header).Concat(Descendants(actions)).Append(actions).OfType<Button>())
                {
                    Point origin = control.TranslatePoint(new Point(), window);
                    Check(origin.X >= 0 && origin.Y >= 0 && origin.X + control.ActualWidth <= window.ActualWidth + 1 && origin.Y + control.ActualHeight <= window.ActualHeight + 1, "Dialog action or close button is clipped");
                }
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create("short-dialog-" + window.GetType().Name + ".png")) png.Save(file);
                window.Close();
            }
        }
        finally { foreach (var window in windows) if (window.IsVisible) window.Close(); L.Use("en"); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static Task Settle() => Task.Delay(80);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
