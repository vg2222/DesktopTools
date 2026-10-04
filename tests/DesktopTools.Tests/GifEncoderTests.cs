using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;

internal static class GifEncoderTests
{
    private const int W = 96, H = 64;
    private static byte[] Frame(int step)
    {
        var data = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int o = (y * W + x) * 4;
                byte b = (byte)(40 + x), g = (byte)(30 + y * 2), r = 70;                         // smooth background
                if (y > 50 && x % 6 < 3) { b = 240; g = 240; r = 240; }                           // static striped footer
                if (x >= step * 6 && x < step * 6 + 14 && y >= 12 && y < 30) { b = 20; g = 20; r = 230; } // moving red block
                data[o] = b; data[o + 1] = g; data[o + 2] = r; data[o + 3] = 255;
            }
        return data;
    }
    private static byte[] Pixel(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var buffer = new byte[4]; converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), buffer, 4, 0); return buffer;
    }
    public static void Run()
    {
        void Check(bool value, string what) { if (!value) throw new Exception("GIF encoder assertion failed: " + what); }
        var frames = Enumerable.Range(0, 10).Select(Frame).ToList();
        frames.Insert(5, (byte[])frames[4].Clone());                                              // an identical frame must be merged, not stored twice
        using var stream = new MemoryStream();
        GifEncoder.Write(stream, frames, W, H, 10);
        var bytes = stream.ToArray();
        Check(System.Text.Encoding.ASCII.GetString(bytes, 0, 6) == "GIF89a" && bytes[^1] == 0x3B, "header and trailer");
        Check(BitConverter.ToUInt16(bytes, 6) == W && BitConverter.ToUInt16(bytes, 8) == H, "canvas size");
        Check(bytes.Length < frames.Count * W * H, "changed regions only: smaller than uncompressed indices");

        stream.Position = 0;
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Check(decoder.Frames.Count == 10, "identical frame merged, got " + decoder.Frames.Count);
        BitmapSource first = decoder.Frames[0];
        Check(first.PixelWidth == W && first.PixelHeight == H, "first frame covers the canvas");
        var footer = Pixel(first, 0, 60);
        Check(footer[2] > 200 && footer[1] > 200 && footer[0] > 200, "static footer keeps its white stripe");
        var sky = Pixel(first, 90, 5);
        Check(Math.Abs(sky[0] - (40 + 90)) < 24 && Math.Abs(sky[2] - 70) < 24, "background colour within palette error: " + string.Join(",", sky));
        var block = Pixel(first, 5, 20);
        Check(block[2] > 190 && block[1] < 70 && block[0] < 70, "red block in the first frame: " + string.Join(",", block));
        // Later frames hold only the changed rectangle (WPF does not compose them); they must be smaller than the canvas.
        Check(decoder.Frames[9].PixelWidth < W && decoder.Frames[9].PixelHeight <= H, "later frames store only what changed");
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "gif-encoder-test.gif"), bytes); // kept for inspecting the result in an image viewer

        using var one = new MemoryStream(); GifEncoder.Write(one, [Frame(0)], W, H, 25, loop: false);
        Check(new GifBitmapDecoder(new MemoryStream(one.ToArray()), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Count == 1, "single frame");
        try { GifEncoder.Write(new MemoryStream(), [new byte[3]], W, H, 10); throw new Exception("bad frame accepted"); } catch (ArgumentException) { }
    }
}
