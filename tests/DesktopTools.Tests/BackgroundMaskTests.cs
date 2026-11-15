using System.Windows;
using DesktopTools.Core;

/// <summary>The touch-up mask: model upsampling, soft round brush strokes and composing the source colour with the painted alpha.</summary>
internal static class BackgroundMaskTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static void FromModel()
    {
        // A 2x1 model mask [0, 1] upsampled to 4x2 with the same centre-aligned bilinear sampling as ApplyMask:
        // source x = (x + .5) * 2 / 4 - .5 → -0.25 (clamped 0), 0.25, 0.75, 1.25 (clamped 1).
        var mask = BackgroundMask.FromModel([0f, 1f], 2, 1, 4, 2);
        Check(mask.Width == 4 && mask.Height == 2, "size");
        byte[] expected = [0, 64, 191, 255];
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
                Check(mask[x, y] == expected[x], $"FromModel ({x},{y}) = {mask[x, y]}, expected {expected[x]}");
        Check(Throws(() => BackgroundMask.FromModel([0f, float.NaN], 2, 1, 4, 2)), "NaN mask must be refused");
        Check(Throws(() => BackgroundMask.FromModel([0f], 2, 1, 4, 2)), "wrong length must be refused");
    }

    public static void Paint()
    {
        var mask = new BackgroundMask(new byte[100 * 100], 100, 100);
        var touched = mask.Paint(50, 50, 20, restore: true);
        Check(mask[50, 50] == 255, "restore sets the centre to 255");
        Check(mask[50 + 13, 50] == 255, "full strength inside 70 % of the radius");
        byte edge = mask[50 + 17, 50];
        Check(edge > 0 && edge < 255, $"soft edge at 85 % of the radius is strictly between: {edge}");
        Check(mask[50 + 21, 50] == 0 && mask[50, 50 - 22] == 0, "pixels beyond the radius are unchanged");
        Check(touched.X <= 30 && touched.Y <= 30 && touched.X + touched.Width >= 70 && touched.Y + touched.Height >= 70, $"touched rectangle covers the brush: {touched}");
        Check(touched.X >= 28 && touched.X + touched.Width <= 72, $"touched rectangle is tight: {touched}");

        var full = new BackgroundMask(Enumerable.Repeat((byte)255, 100 * 100).ToArray(), 100, 100);
        full.Paint(50, 50, 20, restore: false);
        Check(full[50, 50] == 0, "erase sets the centre to 0");
        Check(full[50 + 21, 50] == 255, "erase leaves pixels beyond the radius");

        // A soft edge never moves a pixel the wrong way: restoring over a pixel already at 200 keeps at least 200.
        var partial = new BackgroundMask(Enumerable.Repeat((byte)200, 100 * 100).ToArray(), 100, 100);
        partial.Paint(50, 50, 20, restore: true);
        Check(partial[50 + 19, 50] >= 200, "restore never lowers alpha");

        // Out of bounds: clipped, no throw, nothing touched for a brush fully outside.
        var clip = new BackgroundMask(new byte[10 * 10], 10, 10);
        var corner = clip.Paint(-1, -1, 6, restore: true);
        Check(clip[0, 0] == 255 && corner.X == 0 && corner.Y == 0 && corner.Width > 0, $"corner stroke clipped: {corner}");
        var outside = clip.Paint(500, 500, 6, restore: true);
        Check(outside.Width == 0 || outside.Height == 0, $"stroke outside touches nothing: {outside}");
        Check(Throws(() => clip.Paint(5, 5, double.NaN, true)), "NaN radius refused");
    }

    public static void Compose()
    {
        var mask = new BackgroundMask([255, 128, 0, 255], 2, 2);
        // BGRA: two opaque pixels, one half-transparent, one opaque.
        byte[] source = [10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 1, 2, 3, 100];
        byte[] target = new byte[16];
        mask.Compose(source, target);
        Check(target[0] == 10 && target[1] == 20 && target[2] == 30 && target[3] == 255, "full mask keeps the pixel");
        Check(target[4] == 40 && target[5] == 50 && target[6] == 60 && target[7] == 128, $"half mask halves alpha: {target[7]}");
        Check(target[8] == 70 && target[9] == 80 && target[10] == 90 && target[11] == 0, "zero mask clears alpha, keeps colour");
        Check(target[15] == 100, "alpha never above the source's");

        // Region compose touches only that rectangle.
        byte[] region = Enumerable.Repeat((byte)7, 16).ToArray();
        mask.Compose(source, region, new Int32Rect(1, 0, 1, 1));
        Check(region[4] == 40 && region[7] == 128 && region[0] == 7 && region[8] == 7, "region compose is limited to the rectangle");
        Check(Throws(() => mask.Compose(source, new byte[8])), "wrong target size refused");
    }

    public static void Clone()
    {
        var mask = new BackgroundMask(new byte[4], 2, 2);
        var copy = mask.Clone();
        mask.Paint(0, 0, 3, true);
        Check(copy[0, 0] == 0 && mask[0, 0] == 255, "clone is independent");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }
}
