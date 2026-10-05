using System.IO;
using DesktopTools.Extras;

internal static class AnimatedWebpTests
{
    public static void Run()
    {
        void Check(bool value, string what) { if (!value) throw new Exception("Animated WebP assertion failed: " + what); }
        const int W = 160, H = 96;
        byte[] Frame(int step)
        {
            var data = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int o = (y * W + x) * 4;
                    data[o] = (byte)(40 + x / 2); data[o + 1] = (byte)(30 + y); data[o + 2] = 70; data[o + 3] = 255;
                    if (x >= step * 8 && x < step * 8 + 24 && y >= 20 && y < 50) { data[o] = 30; data[o + 1] = 30; data[o + 2] = 235; }
                }
            return data;
        }
        var frames = Enumerable.Range(0, 8).Select(Frame).ToList();
        frames.Insert(3, (byte[])frames[2].Clone());
        using var stream = new MemoryStream();
        AnimatedWebp.Write(stream, frames, W, H, 10, 80);
        var bytes = stream.ToArray();
        Check(System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && BitConverter.ToUInt32(bytes, 4) == bytes.Length - 8 && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP", "RIFF layout");
        Check(System.Text.Encoding.ASCII.GetString(bytes, 12, 4) == "VP8X" && (bytes[20] & 0x02) != 0, "animation flag");
        int anmf = 0; for (int i = 12; i + 4 < bytes.Length; i++) if (bytes[i] == 'A' && bytes[i + 1] == 'N' && bytes[i + 2] == 'M' && bytes[i + 3] == 'F') anmf++;
        Check(anmf == 8, "identical frame merged, found frames: " + anmf);
        Check(bytes.Length < 8 * W * H * 4 / 8, "lossy frames are far smaller than raw pixels");
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "animated-webp-test.webp"), bytes);
        try { AnimatedWebp.Write(new MemoryStream(), [new byte[4]], W, H, 10); throw new Exception("bad frame accepted"); } catch (ArgumentException) { }
    }
}
