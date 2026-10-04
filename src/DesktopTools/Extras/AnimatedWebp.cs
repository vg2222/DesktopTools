using System.IO;
using SkiaSharp;

namespace DesktopTools.Extras;

/// <summary>
/// Animated WebP writer. Each frame is encoded by Skia's (libwebp) lossy still-image encoder and the encoded frames are wrapped in the
/// WebP animation container (VP8X + ANIM + ANMF chunks). Identical consecutive frames are merged into one longer frame.
/// </summary>
public static class AnimatedWebp
{
    public static void Write(Stream output, IReadOnlyList<byte[]> bgraFrames, int width, int height, double framesPerSecond, int quality = 75, bool loop = true)
    {
        if (bgraFrames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(bgraFrames));
        if (width < 1 || height < 1 || width > 16383 || height > 16383) throw new ArgumentOutOfRangeException(nameof(width));
        if (!(framesPerSecond is >= 1 and <= 100)) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        quality = Math.Clamp(quality, 1, 99);                                     // 100 would switch Skia to lossless, which is far larger
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var chunks = new List<(byte[] Bitstream, int Start)>();
        byte[]? previous = null;
        for (int i = 0; i < bgraFrames.Count; i++)
        {
            var frame = bgraFrames[i];
            if (frame.Length != width * height * 4) throw new ArgumentException("A frame has the wrong size.", nameof(bgraFrames));
            if (previous != null && frame.AsSpan().SequenceEqual(previous)) continue;
            previous = frame;
            using var pixels = SKData.CreateCopy(frame);
            using var image = SKImage.FromPixels(info, pixels, width * 4) ?? throw new InvalidOperationException("Could not wrap a video frame for encoding.");
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, quality) ?? throw new InvalidOperationException("WebP encoding is unavailable.");
            chunks.Add((ExtractBitstream(encoded.ToArray()), (int)Math.Round(i * 1000.0 / framesPerSecond)));
        }
        int end = (int)Math.Round(bgraFrames.Count * 1000.0 / framesPerSecond);

        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("WEBP"u8.ToArray());
            w.Write("VP8X"u8.ToArray()); w.Write(10u); w.Write((byte)0x02); w.Write(new byte[3]); Write24(w, width - 1); Write24(w, height - 1);   // animation flag
            w.Write("ANIM"u8.ToArray()); w.Write(6u); w.Write(0xFF000000u); w.Write((ushort)(loop ? 0 : 1));                                          // opaque black background, 0 = loop forever
            for (int i = 0; i < chunks.Count; i++)
            {
                int next = i + 1 < chunks.Count ? chunks[i + 1].Start : end;
                int duration = Math.Clamp(next - chunks[i].Start, 10, 0xFFFFFF);
                var payload = chunks[i].Bitstream;
                w.Write("ANMF"u8.ToArray()); w.Write((uint)(16 + payload.Length));
                Write24(w, 0); Write24(w, 0); Write24(w, width - 1); Write24(w, height - 1); Write24(w, duration);
                w.Write((byte)0x02);                                                                                                             // do not blend; keep the frame as is
                w.Write(payload);
            }
        }
        using var final = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
        final.Write("RIFF"u8.ToArray()); final.Write((uint)body.Length); final.Write(body.ToArray());
    }

    private static void Write24(BinaryWriter writer, int value) { writer.Write((byte)(value & 0xFF)); writer.Write((byte)(value >> 8 & 0xFF)); writer.Write((byte)(value >> 16 & 0xFF)); }

    /// <summary>Returns the "VP8 " chunk (header and padded data) of a still WebP file.</summary>
    internal static byte[] ExtractBitstream(byte[] file)
    {
        if (file.Length < 20 || file[0] != 'R' || file[1] != 'I' || file[2] != 'F' || file[3] != 'F' || file[8] != 'W' || file[9] != 'E' || file[10] != 'B' || file[11] != 'P')
            throw new InvalidDataException("The encoder did not return a WebP file.");
        for (int position = 12; position + 8 <= file.Length;)
        {
            int size = BitConverter.ToInt32(file, position + 4), padded = size + (size & 1);
            string id = System.Text.Encoding.ASCII.GetString(file, position, 4);
            if (id is "VP8 " or "VP8L")
            {
                var chunk = new byte[8 + padded];
                Buffer.BlockCopy(file, position, chunk, 0, Math.Min(chunk.Length, file.Length - position));
                return chunk;
            }
            position += 8 + padded;
        }
        throw new InvalidDataException("The WebP frame has no image data.");
    }
}
