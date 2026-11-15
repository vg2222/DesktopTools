using DesktopTools.Core;
using DesktopTools.Localization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

/// <summary>Standard is the bundled U2NetP; Quality is the optional BiRefNet-lite pack (catalog id <c>background-quality</c>).</summary>
public enum BackgroundModel { Standard, Quality }

/// <summary>The result image and the model's mask (0..1, at the model's resolution) that produced it, for touch-up.</summary>
public sealed record BackgroundRemovalResult(BitmapSource Image, float[] Mask, int MaskWidth, int MaskHeight);

/// <summary>The quality pack cannot run: it is not downloaded, or it is damaged and must be downloaded again.</summary>
internal sealed class BackgroundQualityUnavailableException(string message, bool damaged, Exception? inner = null) : InvalidOperationException(message, inner)
{
    internal bool Damaged { get; } = damaged;
}

public static class BackgroundRemovalService
{
    private const int Side = 320, QualitySide = 1024;
    private const string ModelHash = "309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8";
    internal const string QualityPackId = "background-quality";
    // BiRefNet-lite peaked at 5.7 GB private memory for a 1024 x 1024 run on the developer's PC; refuse before running when less can be committed.
    private const ulong QualityMemoryBytes = 6UL << 30;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal static ModelPack? QualityPack => ModelPacks.Catalog.FirstOrDefault(pack => pack.Id == QualityPackId);
    internal static ModelPackState QualityState => QualityPack is { } pack ? ModelPacks.StateOf(pack) : ModelPackState.Missing;
    /// <summary>Test seam for the free-memory probe; null uses the memory Windows can still commit.</summary>
    internal static Func<ulong>? AvailableMemory { get; set; }

    public static async Task<BitmapSource> RemoveAsync(BitmapSource image, CancellationToken cancellationToken = default) =>
        (await RemoveWithMaskAsync(image, BackgroundModel.Standard, cancellationToken)).Image;

    /// <summary>
    /// Removes the background with the chosen model. Quality never falls back to Standard: a missing or damaged pack throws
    /// <see cref="BackgroundQualityUnavailableException"/>, too little free memory throws <see cref="InsufficientMemoryException"/>.
    /// </summary>
    public static async Task<BackgroundRemovalResult> RemoveWithMaskAsync(BitmapSource image, BackgroundModel model, CancellationToken cancellationToken)
    {
        if (!image.IsFrozen) throw new ArgumentException("Background removal requires a frozen image.", nameof(image));
        ImageTransforms.ValidateSize(image.PixelWidth, image.PixelHeight);
        if (model == BackgroundModel.Quality) return await RemoveQualityAsync(image, cancellationToken);
        await Gate.WaitAsync(cancellationToken);
        try { return await Task.Run(() => Remove(image, cancellationToken), cancellationToken); }
        finally { Gate.Release(); }
    }

    private static async Task<BackgroundRemovalResult> RemoveQualityAsync(BitmapSource image, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var pack = QualityPack ?? throw new BackgroundQualityUnavailableException(L.T("The high-quality model is not downloaded."), false);
        string folder = ModelPacks.DirectoryFor(pack) ?? throw Unavailable(pack);
        if ((AvailableMemory ?? Native.NativeMethods.AvailableCommitMemory)() < QualityMemoryBytes)
            throw new InsufficientMemoryException(L.T("The high-quality model needs about 6 GB of free memory. Close other apps, or choose Built-in."));
        var file = pack.Files[0];
        float[] input = await Task.Run(() => Prepare(image, QualitySide, false, token), token);
        float[] mask;
        try { mask = await AiRuntime.RunAsync([(Path.Combine(folder, file.File), file.Sha256)], sessions => RunQuality(sessions[0], input, token), token, prepare: null, lowMemory: true); }
        catch (Exception e) when (AiRuntime.DamagedPath(e) != null || e is FileNotFoundException or DirectoryNotFoundException)
        {
            // A file that fails its checksum or is gone: a pack removed meanwhile is "not downloaded"; otherwise it is damaged.
            if (ModelPacks.StateOf(pack) != ModelPackState.Missing) ModelPacks.MarkDamaged(pack);
            throw Unavailable(pack, e);
        }
        catch (Exception e) when (AiRuntime.FailedPath(e) != null && e is not OperationCanceledException && !token.IsCancellationRequested)
        {
            // An intact file that does not load (usually too little memory for the 224 MB model): downloading it again would not help.
            throw new InvalidOperationException(L.T("The high-quality model could not be loaded. Close other apps and try again, or choose Built-in."), e);
        }
        var result = await Task.Run(() => ApplyMask(image, mask, QualitySide, QualitySide, token), token);
        return new BackgroundRemovalResult(result, mask, QualitySide, QualitySide);
    }

    private static BackgroundQualityUnavailableException Unavailable(ModelPack pack, Exception? inner = null) => ModelPacks.StateOf(pack) == ModelPackState.Damaged
        ? new BackgroundQualityUnavailableException(L.T("The high-quality model is damaged. Download it again."), true, inner)
        : new BackgroundQualityUnavailableException(L.T("The high-quality model is not downloaded."), false, inner);

    private static float[] RunQuality(InferenceSession session, float[] input, CancellationToken token)
    {
        using var run = new RunOptions();
        using var registration = token.Register(() => run.Terminate = true);
        try
        {
            var tensor = new DenseTensor<float>(input, new[] { 1, 3, QualitySide, QualitySide });
            using var result = session.Run(new[] { NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), tensor) }, new[] { session.OutputMetadata.Keys.Last() }, run);
            token.ThrowIfCancellationRequested();
            var values = result.First().AsTensor<float>().ToArray();
            if (values.Length != QualitySide * QualitySide) throw new InvalidDataException(L.T("The background-removal model returned an invalid mask."));
            // BiRefNet returns logits; the mask is their sigmoid.
            for (int i = 0; i < values.Length; i++)
            {
                if (!float.IsFinite(values[i])) throw new InvalidDataException(L.T("The background-removal model returned an invalid mask."));
                values[i] = 1f / (1f + MathF.Exp(-values[i]));
            }
            return values;
        }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
    }

    private static BackgroundRemovalResult Remove(BitmapSource image, CancellationToken cancellationToken)
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
            return new BackgroundRemovalResult(ApplyMask(image, values, Side, Side, cancellationToken), values, Side, Side);
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
    }
    internal static float[] Prepare(BitmapSource image, CancellationToken token = default) => Prepare(image, Side, true, token);

    /// <summary>
    /// Scales to <paramref name="side"/> squared, composites transparency onto white and normalizes with the ImageNet mean and deviation.
    /// The U2-Net models divide by the brightest value (as rembg does); BiRefNet divides by 255 (its preprocessor configuration).
    /// </summary>
    private static float[] Prepare(BitmapSource image, int side, bool byMaximum, CancellationToken token)
    {
        int Side = side;
        var scaled = new TransformedBitmap(image, new ScaleTransform(Side / (double)image.PixelWidth, Side / (double)image.PixelHeight));
        var rgba = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[Side * Side * 4]; rgba.CopyPixels(pixels, Side * 4, 0);
        var rgb = new float[Side * Side * 3]; float max = byMaximum ? 1 : 255;
        // Composite existing transparency onto white for segmentation; preserve it in the result.
        for (int i = 0; i < Side * Side; i++)
        {
            float alpha = pixels[i * 4 + 3] / 255f;
            for (int c = 0; c < 3; c++) { float value = pixels[i * 4 + 2 - c] * alpha + 255 * (1 - alpha); rgb[c * Side * Side + i] = value; if (byMaximum) max = Math.Max(max, value); }
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
