using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Localization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DesktopTools.Extras;

/// <summary>The face finder cannot run: it is not downloaded (<see cref="Damaged"/> false) or failed its checks (true).</summary>
internal sealed class FaceDetectorUnavailableException(string message, bool damaged, Exception? inner = null) : InvalidOperationException(message, inner)
{
    internal bool Damaged { get; } = damaged;
}

/// <summary>
/// Finds where faces are with the optional YuNet pack (catalog id <c>faces</c>, spec section 9) so Check screenshot can suggest covering them.
/// Only positions leave this class: no identity, no landmarks, nothing stored. Runs on the processor through <see cref="AiRuntime"/>,
/// which verifies the file's SHA-256 when it loads.
/// </summary>
internal static class FaceDetector
{
    internal const string PackId = "faces";
    // Chosen on the developer's PC: 0.074 was the highest score on five generated faceless UI screenshots; real faces at 60 and 200 px scored 0.94-0.96.
    internal const float Threshold = .6f;
    private const int MaxSide = 2560, MinFace = 8;
    internal static ModelPack? Pack => ModelPacks.Catalog.FirstOrDefault(pack => pack.Id == PackId);
    /// <summary>Whether the last face pass was skipped (another model busy, or the pack did not load) although the pack is installed.</summary>
    internal static bool LastCheckSkipped => lastCheckSkipped;
    private static volatile bool lastCheckSkipped;
    /// <summary>How long a face pass waits for another model (a long translation, the background model) before it is skipped.</summary>
    internal static readonly TimeSpan BusyWait = TimeSpan.FromSeconds(3);
    internal static ModelPackState State => Pack is { } pack ? ModelPacks.StateOf(pack) : ModelPackState.Missing;

    /// <summary>Faces in <paramref name="image"/>, in its pixel coordinates; none when the pack is missing or damaged (damage is marked).</summary>
    internal static async Task<IReadOnlyList<Int32Rect>> DetectIfInstalledAsync(BitmapSource image, CancellationToken token)
    {
        lastCheckSkipped = false;
        if (State != ModelPackState.Installed) return [];
        try { return await DetectAsync(image, token, BusyWait); }
        catch (FaceDetectorUnavailableException) { return []; }
        // Busy, or a file that is intact but does not load: the text findings still count; the editor says faces were not checked.
        catch (Exception e) when (e is not OperationCanceledException) { lastCheckSkipped = true; return []; }
    }

    internal static async Task<IReadOnlyList<Int32Rect>> DetectAsync(BitmapSource image, CancellationToken token, TimeSpan? wait = null)
    {
        token.ThrowIfCancellationRequested();
        var pack = Pack ?? throw new FaceDetectorUnavailableException(L.T("The face finder is not downloaded."), false);
        string folder = ModelPacks.DirectoryFor(pack) ?? throw Unavailable(pack);
        int width = image.PixelWidth, height = image.PixelHeight;
        double scale = Math.Min(1, MaxSide / (double)Math.Max(width, height));
        // Pixels are copied on the caller's thread: the picture may belong to a window.
        BitmapSource source = scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
        var bgr = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
        int w = bgr.PixelWidth, h = bgr.PixelHeight, stride = (w * 3 + 3) & ~3;
        var pixels = new byte[stride * h]; bgr.CopyPixels(pixels, stride, 0);
        var file = pack.Files[0];
        IReadOnlyList<FaceBox> faces;
        try { faces = await AiRuntime.RunAsync([(Path.Combine(folder, file.File), file.Sha256)], sessions => Run(sessions[0], pixels, stride, w, h, token), token, prepare: null, wait: wait); }
        catch (Exception e) when ((AiRuntime.DamagedPath(e) != null || e is FileNotFoundException or DirectoryNotFoundException) && !token.IsCancellationRequested)
        {
            // Only a failed checksum or a missing file is damage; a file that is intact but does not load would not be fixed by a new download.
            // A pack removed meanwhile is "not downloaded"; otherwise the file is damaged.
            if (ModelPacks.StateOf(pack) != ModelPackState.Missing) ModelPacks.MarkDamaged(pack);
            throw Unavailable(pack, e);
        }
        var found = new List<Int32Rect>();
        foreach (var face in faces)
        {
            var bounds = new Rect(face.Bounds.X / scale, face.Bounds.Y / scale, face.Bounds.Width / scale, face.Bounds.Height / scale);
            bounds.Intersect(new Rect(0, 0, width, height));
            if (bounds.IsEmpty) continue;
            int left = (int)Math.Floor(bounds.Left), top = (int)Math.Floor(bounds.Top), right = (int)Math.Ceiling(bounds.Right), bottom = (int)Math.Ceiling(bounds.Bottom);
            if (right - left < MinFace || bottom - top < MinFace) continue;
            found.Add(new Int32Rect(left, top, right - left, bottom - top));
        }
        return found;
    }

    private static FaceDetectorUnavailableException Unavailable(ModelPack pack, Exception? inner = null) => ModelPacks.StateOf(pack) == ModelPackState.Damaged
        ? new FaceDetectorUnavailableException(L.T("Faces were not checked: the face finder is damaged. Download it again."), true, inner)
        : new FaceDetectorUnavailableException(L.T("The face finder is not downloaded."), false, inner);

    /// <summary>YuNet takes BGR 0-255 without normalisation, padded with zeros to multiples of 32.</summary>
    private static IReadOnlyList<FaceBox> Run(InferenceSession session, byte[] pixels, int stride, int width, int height, CancellationToken token)
    {
        int paddedWidth = Math.Max(32, (width + 31) / 32 * 32), paddedHeight = Math.Max(32, (height + 31) / 32 * 32), plane = paddedWidth * paddedHeight;
        var input = new float[3 * plane];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int from = y * stride + x * 3, to = y * paddedWidth + x;
                input[to] = pixels[from]; input[plane + to] = pixels[from + 1]; input[2 * plane + to] = pixels[from + 2];
            }
        token.ThrowIfCancellationRequested();
        using var run = new RunOptions();
        using var registration = token.Register(() => run.Terminate = true);
        try
        {
            var tensor = new DenseTensor<float>(input, [1, 3, paddedHeight, paddedWidth]);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), tensor)], session.OutputMetadata.Keys.ToArray(), run);
            token.ThrowIfCancellationRequested();
            var outputs = results.ToDictionary(result => result.Name, result => result.AsTensor<float>().ToArray());
            return YuNetDecoder.Decode(outputs, paddedWidth, paddedHeight, Threshold);
        }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
    }
}
