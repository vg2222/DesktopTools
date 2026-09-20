using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Extras;

internal static class EffectChecks
{
    public static void Run()
    {
        var type = typeof(PresentationEffectWindow).GetNestedType("EffectCanvas", BindingFlags.NonPublic)!;
        var canvas = (FrameworkElement)Activator.CreateInstance(type, "Spotlight", new AppSettings { SpotlightRadius = 40, SpotlightDim = .65 })!;
        canvas.Measure(new Size(200, 200)); canvas.Arrange(new Rect(0, 0, 200, 200));
        var update = (Action<Point?>)type.GetMethod("Update")!.CreateDelegate(typeof(Action<Point?>), canvas);
        byte[] Render()
        {
            canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
            byte[] pixels = new byte[200 * 200 * 4]; bitmap.CopyPixels(pixels, 800, 0); return pixels;
        }
        static byte Alpha(byte[] pixels, int x, int y) => pixels[(y * 200 + x) * 4 + 3];
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        update(new Point(100, 100)); var first = Render();
        Check(Alpha(first, 100, 100) == 0 && Alpha(first, 10, 10) > 150 && Alpha(first, 135, 135) > 150,
            "Spotlight must have an exactly circular transparent opening with dimmed pixels outside.");
        update(new Point(40, 40)); var moved = Render();
        Check(Alpha(moved, 40, 40) == 0 && Alpha(moved, 100, 100) > 150,
            "Moving the retained hole must clear its new position and dim the previous position.");
        // Warm the same measurement callsite, including tiered runtime initialization.
        // A short 100-call warmup left one-time allocations in the first 10k batch.
        _ = MeasureStationary(update);
        for (int batch = 0; batch < 3; batch++)
        {
            long allocated = MeasureStationary(update);
            Check(allocated == 0, $"Stationary spotlight updates allocated {allocated} managed bytes.");
        }
        update(new Point(0, 0)); var edge = Render();
        Check(Alpha(edge, 1, 1) == 0 && Alpha(edge, 60, 60) > 150,
            "The circular hole must clip correctly at the monitor corner.");
        update(null); var absent = Render();
        Check(Alpha(absent, 100, 100) == 0, "Leaving the monitor must clear the spotlight mask.");
        var laser = (FrameworkElement)Activator.CreateInstance(type, "Laser", new AppSettings { LaserColor = "#FFFF0000", LaserSize = 12, LaserFadeSeconds = 5 })!;
        laser.Measure(new Size(200, 200)); laser.Arrange(new Rect(0, 0, 200, 200));
        var laserUpdate = (Action<Point?>)type.GetMethod("Update")!.CreateDelegate(typeof(Action<Point?>), laser);
        laserUpdate(new Point(20, 100)); laserUpdate(new Point(100, 100)); laserUpdate(new Point(180, 100));
        laser.UpdateLayout();
        var laserImage = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32); laserImage.Render(laser);
        byte[] laserPixels = new byte[200 * 200 * 4]; laserImage.CopyPixels(laserPixels, 800, 0);
        Check(Math.Abs(Alpha(laserPixels, 100, 100) - Alpha(laserPixels, 60, 100)) <= 1 && Alpha(laserPixels, 100, 100) <= 128,
            "Laser joins must have the same opacity as the continuous line, without white hotspots.");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static long MeasureStationary(Action<Point?> update)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) update(new Point(40, 40));
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

}
