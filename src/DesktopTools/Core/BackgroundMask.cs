using System.Windows;

namespace DesktopTools.Core;

/// <summary>
/// A full-size foreground mask (0 = background, 255 = keep) for touching up a background removal.
/// The original pixels are never changed: <see cref="Compose(ReadOnlySpan{byte}, Span{byte})"/> writes a new image from the source and the mask.
/// </summary>
public sealed class BackgroundMask
{
    private const double FullStrength = .7;
    private readonly byte[] mask;

    public BackgroundMask(byte[] mask, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (width < 1 || height < 1 || mask.Length != checked(width * height)) throw new ArgumentException("Invalid mask size.");
        this.mask = mask; Width = width; Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public byte this[int x, int y] => mask[y * Width + x];

    /// <summary>Upsamples a model mask (0..1) with the same centre-aligned bilinear sampling as the background-removal service.</summary>
    public static BackgroundMask FromModel(float[] values, int modelWidth, int modelHeight, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (modelWidth < 1 || modelHeight < 1 || width < 1 || height < 1 || values.Length != checked(modelWidth * modelHeight) || values.Any(x => !float.IsFinite(x)))
            throw new ArgumentException("Invalid foreground mask.");
        var result = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        {
            double sy = Math.Clamp((y + .5) * modelHeight / height - .5, 0, modelHeight - 1); int y0 = (int)sy, y1 = Math.Min(y0 + 1, modelHeight - 1); double fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                double sx = Math.Clamp((x + .5) * modelWidth / width - .5, 0, modelWidth - 1); int x0 = (int)sx, x1 = Math.Min(x0 + 1, modelWidth - 1); double fx = sx - x0;
                double a = values[y0 * modelWidth + x0] * (1 - fx) + values[y0 * modelWidth + x1] * fx;
                double b = values[y1 * modelWidth + x0] * (1 - fx) + values[y1 * modelWidth + x1] * fx;
                result[y * width + x] = (byte)Math.Round(Math.Clamp(a * (1 - fy) + b * fy, 0, 1) * 255);
            }
        }
        return new BackgroundMask(result, width, height);
    }

    /// <summary>
    /// Paints a round brush centred at (<paramref name="x"/>, <paramref name="y"/>) in image pixels: full strength inside 70 % of the radius,
    /// then a linear falloff to the edge. Restore moves towards 255, erase towards 0; a pixel never moves the other way.
    /// Returns the touched rectangle (empty when the brush is outside the image).
    /// </summary>
    public Int32Rect Paint(double x, double y, double radius, bool restore)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(radius) || radius <= 0) throw new ArgumentException("Invalid brush.");
        int left = Math.Max(0, (int)Math.Floor(x - radius)), top = Math.Max(0, (int)Math.Floor(y - radius));
        int right = Math.Min(Width, (int)Math.Ceiling(x + radius)), bottom = Math.Min(Height, (int)Math.Ceiling(y + radius));
        if (right <= left || bottom <= top) return new Int32Rect(0, 0, 0, 0);
        double inner = radius * FullStrength, falloff = radius - inner;
        for (int py = top; py < bottom; py++)
            for (int px = left; px < right; px++)
            {
                double dx = px + .5 - x, dy = py + .5 - y, distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance >= radius) continue;
                double strength = distance <= inner ? 1 : 1 - (distance - inner) / falloff;
                int index = py * Width + px; double old = mask[index];
                mask[index] = (byte)Math.Round(restore ? old + (255 - old) * strength : old * (1 - strength));
            }
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Writes colour from <paramref name="sourceBgra"/> and alpha = source alpha × mask / 255 for every pixel.</summary>
    public void Compose(ReadOnlySpan<byte> sourceBgra, Span<byte> targetBgra) => Compose(sourceBgra, targetBgra, new Int32Rect(0, 0, Width, Height));

    /// <summary>As <see cref="Compose(ReadOnlySpan{byte}, Span{byte})"/>, limited to <paramref name="region"/> (clipped to the mask).</summary>
    public void Compose(ReadOnlySpan<byte> sourceBgra, Span<byte> targetBgra, Int32Rect region)
    {
        long size = (long)Width * Height * 4;
        if (sourceBgra.Length != size || targetBgra.Length != size) throw new ArgumentException("Pixel buffers must match the mask size.");
        int left = Math.Max(0, region.X), top = Math.Max(0, region.Y);
        int right = Math.Min(Width, region.X + region.Width), bottom = Math.Min(Height, region.Y + region.Height);
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            {
                int pixel = y * Width + x, i = pixel * 4;
                targetBgra[i] = sourceBgra[i]; targetBgra[i + 1] = sourceBgra[i + 1]; targetBgra[i + 2] = sourceBgra[i + 2];
                targetBgra[i + 3] = (byte)((sourceBgra[i + 3] * mask[pixel] + 127) / 255);
            }
    }

    public BackgroundMask Clone() => new((byte[])mask.Clone(), Width, Height);
}
