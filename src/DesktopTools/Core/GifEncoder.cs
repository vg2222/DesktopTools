using System.IO;

namespace DesktopTools.Core;

/// <summary>
/// Writes animated GIF files from raw BGRA frames without any third-party code: one shared 255-colour palette (median cut), ordered dithering that
/// keeps still areas identical from frame to frame, and each frame stores only the rectangle that changed.
/// </summary>
public static class GifEncoder
{
    /// <param name="frames">Tightly packed BGRA frames, <paramref name="width"/> × <paramref name="height"/> × 4 bytes each.</param>
    /// <param name="framesPerSecond">Average playback rate; GIF delays are whole hundredths of a second, so individual frames vary by 10 ms to keep the average exact.</param>
    public static void Write(Stream output, IReadOnlyList<byte[]> frames, int width, int height, double framesPerSecond, bool loop = true)
    {
        if (frames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(frames));
        if (width < 1 || height < 1 || width > 65535 || height > 65535) throw new ArgumentOutOfRangeException(nameof(width));
        if (!(framesPerSecond is >= 1 and <= 100)) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        foreach (var frame in frames) if (frame.Length != width * height * 4) throw new ArgumentException("A frame has the wrong size.", nameof(frames));

        var palette = BuildPalette(frames, width, height);
        var lookup = BuildLookup(palette);
        const int transparent = 255;
        using var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("GIF89a"u8.ToArray());
        writer.Write((ushort)width); writer.Write((ushort)height);
        writer.Write((byte)0xF7); writer.Write((byte)0); writer.Write((byte)0);           // global table, 256 entries
        for (int i = 0; i < 256; i++) { var c = i < 255 ? palette[i] : (0, 0, 0); writer.Write((byte)c.Item1); writer.Write((byte)c.Item2); writer.Write((byte)c.Item3); }
        if (loop) { writer.Write(new byte[] { 0x21, 0xFF, 0x0B }); writer.Write("NETSCAPE2.0"u8.ToArray()); writer.Write(new byte[] { 3, 1, 0, 0, 0 }); }

        byte[]? previous = null;
        var indices = new byte[width * height];
        // Timeline in hundredths of a second: frame i starts at round(i * 100 / fps).
        long Start(int i) => (long)Math.Round(i * 100.0 / framesPerSecond);
        var pending = new List<(byte[] Pixels, int X, int Y, int W, int H, long Start)>();
        for (int f = 0; f < frames.Count; f++)
        {
            Quantize(frames[f], width, height, lookup, indices);
            if (previous == null) { pending.Add((Crop(indices, width, 0, 0, width, height), 0, 0, width, height, Start(f))); previous = (byte[])indices.Clone(); continue; }
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                    if (indices[row + x] != previous[row + x]) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            }
            if (maxX < 0) continue;                                                       // identical frame: the previous one simply lasts longer
            int w = maxX - minX + 1, h = maxY - minY + 1;
            var region = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int at = (minY + y) * width + minX + x;
                    region[y * w + x] = indices[at] == previous[at] ? (byte)transparent : indices[at];
                }
            pending.Add((region, minX, minY, w, h, Start(f)));
            Buffer.BlockCopy(indices, 0, previous, 0, indices.Length);
        }
        long end = Start(frames.Count);
        for (int i = 0; i < pending.Count; i++)
        {
            long next = i + 1 < pending.Count ? pending[i + 1].Start : end;
            int delay = (int)Math.Clamp(next - pending[i].Start, 2, 65535);
            var p = pending[i];
            writer.Write(new byte[] { 0x21, 0xF9, 4, (byte)(0x04 | (i > 0 ? 1 : 0)) });  // disposal 1 (leave in place), transparent flag after the first frame
            writer.Write((ushort)delay); writer.Write((byte)transparent); writer.Write((byte)0);
            writer.Write((byte)0x2C); writer.Write((ushort)p.X); writer.Write((ushort)p.Y); writer.Write((ushort)p.W); writer.Write((ushort)p.H); writer.Write((byte)0);
            writer.Write((byte)8);
            WriteBlocks(writer, Lzw(p.Pixels));
        }
        writer.Write((byte)0x3B);
    }

    private static byte[] Crop(byte[] indices, int stride, int x, int y, int w, int h)
    {
        var result = new byte[w * h];
        for (int row = 0; row < h; row++) Buffer.BlockCopy(indices, (y + row) * stride + x, result, row * w, w);
        return result;
    }

    // ----- palette: median cut over a 5-bit-per-channel histogram
    private static List<(int, int, int)> BuildPalette(IReadOnlyList<byte[]> frames, int width, int height)
    {
        var histogram = new int[32768];
        int pixels = width * height;
        int step = Math.Max(1, (int)((long)pixels * frames.Count / 400_000));
        for (int f = 0; f < frames.Count; f += Math.Max(1, frames.Count / 24))
        {
            var data = frames[f];
            for (int p = (f / Math.Max(1, frames.Count / 24)) % step; p < pixels; p += step)
            {
                int o = p * 4; histogram[(data[o + 2] >> 3) << 10 | (data[o + 1] >> 3) << 5 | (data[o] >> 3)]++;
            }
        }
        var boxes = new List<List<int>> { Enumerable.Range(0, 32768).Where(i => histogram[i] > 0).ToList() };
        if (boxes[0].Count == 0) boxes[0].Add(0);
        while (boxes.Count < 255)
        {
            int best = -1; long bestScore = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                long weight = boxes[i].Sum(c => (long)histogram[c]);
                var (range, _) = LongestAxis(boxes[i]);
                long score = weight * range;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            if (best < 0) break;
            var box = boxes[best]; var (_, axis) = LongestAxis(box);
            box.Sort((a, b) => Channel(a, axis).CompareTo(Channel(b, axis)));
            long half = box.Sum(c => (long)histogram[c]) / 2, running = 0; int cut = 1;
            for (int i = 0; i < box.Count - 1; i++) { running += histogram[box[i]]; if (running >= half) { cut = i + 1; break; } cut = i + 1; }
            boxes[best] = box.GetRange(0, cut); boxes.Add(box.GetRange(cut, box.Count - cut));
        }
        var palette = new List<(int, int, int)>();
        foreach (var box in boxes)
        {
            double r = 0, g = 0, b = 0, total = 0;
            foreach (int c in box) { double w = histogram[c]; r += (c >> 10 & 31) * w; g += (c >> 5 & 31) * w; b += (c & 31) * w; total += w; }
            if (total == 0) total = 1;
            palette.Add(((int)Math.Round(r / total * 8 + 4), (int)Math.Round(g / total * 8 + 4), (int)Math.Round(b / total * 8 + 4)));
        }
        while (palette.Count < 255) palette.Add(palette[^1]);
        return palette.Select(c => (Math.Min(255, c.Item1), Math.Min(255, c.Item2), Math.Min(255, c.Item3))).ToList();
    }
    private static int Channel(int color, int axis) => axis == 0 ? color >> 10 & 31 : axis == 1 ? color >> 5 & 31 : color & 31;
    private static (int Range, int Axis) LongestAxis(List<int> box)
    {
        int[] min = [31, 31, 31], max = [0, 0, 0];
        foreach (int c in box) for (int a = 0; a < 3; a++) { int v = Channel(c, a); if (v < min[a]) min[a] = v; if (v > max[a]) max[a] = v; }
        int axis = 0, range = max[0] - min[0];
        for (int a = 1; a < 3; a++) if (max[a] - min[a] > range) { range = max[a] - min[a]; axis = a; }
        return (range + 1, axis);
    }
    private static byte[] BuildLookup(List<(int, int, int)> palette)
    {
        var table = new byte[32768];
        for (int c = 0; c < 32768; c++)
        {
            int r = (c >> 10 & 31) * 8 + 4, g = (c >> 5 & 31) * 8 + 4, b = (c & 31) * 8 + 4, best = 0, bestDistance = int.MaxValue;
            for (int i = 0; i < 255; i++)
            {
                int dr = r - palette[i].Item1, dg = g - palette[i].Item2, db = b - palette[i].Item3, d = 3 * dr * dr + 4 * dg * dg + 2 * db * db;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            table[c] = (byte)best;
        }
        return table;
    }
    private static readonly int[] Bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
    private static void Quantize(byte[] bgra, int width, int height, byte[] lookup, byte[] indices)
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4, nudge = Bayer[(y & 3) << 2 | (x & 3)] - 8;      // -8..+7: position-dependent, so unchanged pixels stay unchanged
                int b = Math.Clamp(bgra[o] + nudge, 0, 255), g = Math.Clamp(bgra[o + 1] + nudge, 0, 255), r = Math.Clamp(bgra[o + 2] + nudge, 0, 255);
                indices[y * width + x] = lookup[(r >> 3) << 10 | (g >> 3) << 5 | (b >> 3)];
            }
    }

    // ----- LZW with 8-bit symbols and variable-width codes
    private static byte[] Lzw(byte[] pixels)
    {
        const int clear = 256, end = 257;
        var output = new List<byte>(pixels.Length / 2 + 16);
        int bits = 0, bitCount = 0;
        void Emit(int code, int size)
        {
            bits |= code << bitCount; bitCount += size;
            while (bitCount >= 8) { output.Add((byte)(bits & 0xFF)); bits >>= 8; bitCount -= 8; }
        }
        var dictionary = new Dictionary<int, int>();
        int next = 258, codeSize = 9;
        Emit(clear, codeSize);
        int prefix = pixels[0];
        for (int i = 1; i < pixels.Length; i++)
        {
            int key = prefix << 8 | pixels[i];
            if (dictionary.TryGetValue(key, out int found)) { prefix = found; continue; }
            Emit(prefix, codeSize);
            if (next < 4096)
            {
                dictionary[key] = next++;
                if (next > (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else { Emit(clear, codeSize); dictionary.Clear(); next = 258; codeSize = 9; }
            prefix = pixels[i];
        }
        Emit(prefix, codeSize);
        Emit(end, codeSize);
        if (bitCount > 0) output.Add((byte)(bits & 0xFF));
        return output.ToArray();
    }
    private static void WriteBlocks(BinaryWriter writer, byte[] data)
    {
        for (int offset = 0; offset < data.Length; offset += 255)
        {
            int length = Math.Min(255, data.Length - offset);
            writer.Write((byte)length); writer.Write(data, offset, length);
        }
        writer.Write((byte)0);
    }
}
