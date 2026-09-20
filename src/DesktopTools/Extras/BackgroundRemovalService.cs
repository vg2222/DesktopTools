using DesktopTools.Localization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

public static class BackgroundRemovalService
{
    private const int Side = 320;
    private const string ModelHash = "309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<BitmapSource> RemoveAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        if (!image.IsFrozen) throw new ArgumentException("Background removal requires a frozen image.", nameof(image));
        ImageTransforms.ValidateSize(image.PixelWidth, image.PixelHeight);
        await Gate.WaitAsync(cancellationToken);
        try { return await Task.Run(() => Remove(image, cancellationToken), cancellationToken); }
        finally { Gate.Release(); }
    }
    private static BitmapSource Remove(BitmapSource image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "u2netp.onnx");
        if (!File.Exists(path)) throw new FileNotFoundException(L.T("The background-removal model is missing. Extract the complete DesktopTools package."));
        var model = File.ReadAllBytes(path);
        if (!Convert.ToHexString(SHA256.HashData(model)).Equals(ModelHash, StringComparison.Ordinal))
            throw new InvalidDataException(L.T("The background-removal model is damaged. Extract a fresh DesktopTools package."));
        using var options = new SessionOptions { IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        using var session = new InferenceSession(model, options);
        cancellationToken.ThrowIfCancellationRequested();
        using var run = new RunOptions();
        using var registration = cancellationToken.Register(() => run.Terminate = true);
        try
        {
            var tensor = new DenseTensor<float>(Prepare(image, cancellationToken), new[] { 1, 3, Side, Side });
            using var result = session.Run(new[] { NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), tensor) }, new[] { session.OutputMetadata.Keys.First() }, run);
            cancellationToken.ThrowIfCancellationRequested();
            var values = result.First().AsTensor<float>().ToArray();
            if (values.Length != Side * Side) throw new InvalidDataException(L.T("The background-removal model returned an invalid mask."));
            float minimum = values.Min(), maximum = values.Max();
            if (!float.IsFinite(minimum) || !float.IsFinite(maximum)) throw new InvalidDataException(L.T("The background-removal model returned an invalid mask."));
            float range = maximum - minimum;
            for (int i = 0; i < values.Length; i++) values[i] = range > 1e-6f ? (values[i] - minimum) / range : Math.Clamp(values[i], 0, 1);
            return ApplyMask(image, values, Side, Side, cancellationToken);
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
    }
    internal static float[] Prepare(BitmapSource image, CancellationToken token = default)
    {
        var scaled = new TransformedBitmap(image, new ScaleTransform(Side / (double)image.PixelWidth, Side / (double)image.PixelHeight));
        var rgba = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[Side * Side * 4]; rgba.CopyPixels(pixels, Side * 4, 0);
        var rgb = new float[Side * Side * 3]; float max = 1;
        // Composite existing transparency onto white for segmentation; preserve it in the result.
        for (int i = 0; i < Side * Side; i++)
        {
            float alpha = pixels[i * 4 + 3] / 255f;
            for (int c = 0; c < 3; c++) { float value = pixels[i * 4 + 2 - c] * alpha + 255 * (1 - alpha); rgb[c * Side * Side + i] = value; max = Math.Max(max, value); }
        }
        float[] means = [.485f, .456f, .406f], deviations = [.229f, .224f, .225f];
        for (int c = 0; c < 3; c++) for (int i = 0; i < Side * Side; i++) rgb[c * Side * Side + i] = (rgb[c * Side * Side + i] / max - means[c]) / deviations[c];
        token.ThrowIfCancellationRequested(); return rgb;
    }
    internal static BitmapSource ApplyMask(BitmapSource image, float[] mask, int maskWidth, int maskHeight, CancellationToken token = default)
    {
        if (maskWidth < 1 || maskHeight < 1 || mask.Length != checked(maskWidth * maskHeight) || mask.Any(x => !float.IsFinite(x))) throw new ArgumentException("Invalid foreground mask.");
        int width = image.PixelWidth, height = image.PixelHeight;
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[checked(width * height * 4)]; converted.CopyPixels(pixels, width * 4, 0);
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            double sy = Math.Clamp((y + .5) * maskHeight / height - .5, 0, maskHeight - 1); int y0 = (int)sy, y1 = Math.Min(y0 + 1, maskHeight - 1); double fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                double sx = Math.Clamp((x + .5) * maskWidth / width - .5, 0, maskWidth - 1); int x0 = (int)sx, x1 = Math.Min(x0 + 1, maskWidth - 1); double fx = sx - x0;
                double a = mask[y0 * maskWidth + x0] * (1 - fx) + mask[y0 * maskWidth + x1] * fx;
                double b = mask[y1 * maskWidth + x0] * (1 - fx) + mask[y1 * maskWidth + x1] * fx;
                int index = (y * width + x) * 4 + 3; pixels[index] = (byte)Math.Round(pixels[index] * Math.Clamp(a * (1 - fy) + b * fy, 0, 1));
            }
        }
        var output = BitmapSource.Create(width, height, image.DpiX, image.DpiY, PixelFormats.Bgra32, null, pixels, width * 4); output.Freeze(); return output;
    }
}
