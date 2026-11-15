using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Native;
using DesktopTools.UI;

/// <summary>
/// Renders the region-selection overlay over a generated still frame (no desktop content) to PNG files: a dragged selection with
/// its size and loupe, a smart suggestion, and a selection against the bottom-right corner where the labels must stay on screen.
/// SELECTION_OUT picks the folder.
/// </summary>
internal static class SelectionShots
{
    internal static async Task RunAsync()
    {
        string directory = Path.GetFullPath(Environment.GetEnvironmentVariable("SELECTION_OUT") is { Length: > 0 } chosen ? chosen : Path.Combine("artifacts", "integration", "selection"));
        Directory.CreateDirectory(directory);
        // A second monitor when there is one, so the fixture stays off the display in use.
        var monitors = MonitorService.GetAll();
        var monitor = System.Linq.Enumerable.FirstOrDefault(monitors, m => m.Bounds.X != 0 || m.Bounds.Y != 0) ?? monitors[0];
        int width = (int)monitor.Bounds.Width, height = (int)monitor.Bounds.Height;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(236, 240, 247), Color.FromRgb(205, 216, 235), 30), null, new Rect(0, 0, width, height));
            dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(200, 208, 222)), 1), new Rect(160, 140, 760, 480), 14, 14);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(37, 99, 235)), null, new Rect(200, 190, 220, 44));
            for (int i = 0; i < 9; i++) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(214, 220, 232)), null, new Rect(200, 270 + i * 34, 420 + i % 3 * 90, 14));
            for (int x = 0; x < width; x += 2) dc.DrawRectangle(x % 4 == 0 ? Brushes.Black : Brushes.White, null, new Rect(x, height - 40, 2, 40)); // one-pixel stripes for the loupe
        }
        var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); frame.Render(visual); frame.Freeze();

        var window = new SelectionWindow(monitor, frozenFrame: frame, smartRegion: true, frozenWindows: []);
        window.Show(); window.UpdateLayout(); await Task.Delay(400);
        try
        {
            var surface = (FrameworkElement)window.Content;
            void Set(string name, object? value) => surface.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.SetValue(surface, value);
            void Shot(string name, Rect selection, bool suggestion, Point? pointer)
            {
                Set("Selection", selection); Set("IsSuggestion", suggestion); Set("Pointer", pointer);
                surface.GetType().GetMethod("InvalidateVisual", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.Invoke(surface, null);
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string path = Path.Combine(directory, name + ".png");
                using (var stream = File.Create(path)) encoder.Save(stream);
                Console.WriteLine("SELECTION " + path);
            }
            double w = surface.ActualWidth, h = surface.ActualHeight;
            if (w < 400 || h < 300) throw new Exception("Selection surface did not fill the display.");
            Shot("drag", new Rect(160, 140, 760, 480), false, new Point(920, 620));
            Shot("suggestion", new Rect(160, 140, 760, 480), true, new Point(500, 360));
            Shot("corner", new Rect(w - 420, h - 260, 420, 260), false, new Point(w - 1, h - 1));
            Shot("idle", Rect.Empty, false, new Point(300, 300));
        }
        finally { window.Close(); }
    }
}
