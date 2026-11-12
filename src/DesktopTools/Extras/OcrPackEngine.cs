using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Localization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DesktopTools.Extras;

/// <summary>The OCR pack cannot run: a pack is not downloaded (<see cref="Damaged"/> false) or failed its checks (true).</summary>
internal sealed class OcrPackUnavailableException(string message, bool damaged) : Exception(message)
{
    public bool Damaged { get; } = damaged;
}

/// <summary>
/// Reads text with the optional PP-OCRv5 pack (spec section 8): the shared detector finds lines in overlapping strips, one recognizer per script
/// group reads them in width-sorted batches, and the result is the same <see cref="OcrLayout"/> Windows OCR produces. Runs on the processor
/// through <see cref="AiRuntime"/>, which verifies every model file's SHA-256 when it loads.
/// </summary>
internal static class OcrPackEngine
{
    /// <summary>Largest picture the pack reads; larger ones are refused with a crop message, as Windows OCR does.</summary>
    internal const int MaxWidth = 8192;
    internal const long MaxPixels = 48_000_000;
    private static readonly float[] DetectorMean = [.485f, .456f, .406f], DetectorStd = [.229f, .224f, .225f];

    internal static ModelPack? Pack(string id) => ModelPacks.Catalog.FirstOrDefault(pack => pack.Id == id);

    internal static ModelPackState StateOf(OcrPackScript script)
    {
        var detector = Pack(OcrPackPipeline.DetectorPackId); var recognizer = Pack(OcrPackPipeline.PackId(script));
        if (detector == null || recognizer == null) return ModelPackState.Missing;
        var states = new[] { ModelPacks.StateOf(detector), ModelPacks.StateOf(recognizer) };
        return states.Contains(ModelPackState.Damaged) ? ModelPackState.Damaged : states.Contains(ModelPackState.Missing) ? ModelPackState.Missing : ModelPackState.Installed;
    }

    internal static bool IsInstalled(OcrPackScript script) => StateOf(script) == ModelPackState.Installed;

    /// <summary>The packs a script group needs that are not installed, detector first.</summary>
    internal static IReadOnlyList<ModelPack> MissingPacks(OcrPackScript script) =>
        new[] { Pack(OcrPackPipeline.DetectorPackId), Pack(OcrPackPipeline.PackId(script)) }
            .Where(pack => pack != null && ModelPacks.StateOf(pack) != ModelPackState.Installed).Select(pack => pack!).ToArray();

    internal static async Task<OcrLayout> RecognizeWordsAsync(BitmapSource image, OcrPackScript script, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var detector = Pack(OcrPackPipeline.DetectorPackId) ?? throw new OcrPackUnavailableException(L.T("The OCR pack is not available in this version."), false);
        var recognizer = Pack(OcrPackPipeline.PackId(script)) ?? throw new OcrPackUnavailableException(L.T("The OCR pack is not available in this version."), false);
        string detectorFolder = Folder(detector), recognizerFolder = Folder(recognizer);
        int width = image.PixelWidth, height = image.PixelHeight;
        if (width > MaxWidth || height > MaxWidth)
            throw new InvalidOperationException(L.F($"Crop the screenshot to at most {MaxWidth} pixels on each side before extracting text."));
        if ((long)width * height > MaxPixels) throw new InvalidOperationException(L.T("Too much text in this picture. Crop it and try again."));
        // Pixels are copied on the caller's thread: the picture may belong to a window.
        BitmapSource bgra = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4];
        bgra.CopyPixels(pixels, width * 4, 0);

        var model = recognizer.Files.Single(file => file.File.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
        var characterFile = recognizer.Files.Single(file => file.File.EndsWith(".yml", StringComparison.OrdinalIgnoreCase));
        var detectorModel = detector.Files.Single();
        string[] characters = await Task.Run(() => ReadCharacters(recognizer, recognizerFolder, characterFile), token);
        try
        {
            return await AiRuntime.RunAsync(
                [(Path.Combine(detectorFolder, detectorModel.File), detectorModel.Sha256), (Path.Combine(recognizerFolder, model.File), model.Sha256)],
                sessions => Read(sessions[0], sessions[1], characters, pixels, width, height, token), token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or OnnxRuntimeException)
        {
            // The file that failed decides which pack is damaged; without one (shape checks, run errors) it is the recognizer.
            string? failed = AiRuntime.FailedPath(e) ?? (e as FileNotFoundException)?.FileName;
            throw Failure(failed != null && failed.StartsWith(detectorFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? detector : recognizer, detector, recognizer);
        }
    }

    /// <summary>
    /// The error to report when reading failed on <paramref name="suspect"/>. A pack removed meanwhile (Remove on the AI models page
    /// while this reading waited) is reported as not downloaded and nothing is marked; otherwise the suspect pack is marked damaged.
    /// </summary>
    private static OcrPackUnavailableException Failure(ModelPack suspect, params ModelPack[] packs)
    {
        if (packs.FirstOrDefault(pack => ModelPacks.StateOf(pack) == ModelPackState.Missing) is { } removed)
            return new OcrPackUnavailableException(L.F($"{L.T(removed.Name)} is not downloaded."), false);
        return Damaged(suspect);
    }

    private static string Folder(ModelPack pack) => ModelPacks.DirectoryFor(pack) ?? throw new OcrPackUnavailableException(
        ModelPacks.StateOf(pack) == ModelPackState.Damaged
            ? L.T("The OCR pack is damaged. Download it again on the AI models page.")
            : L.F($"{L.T(pack.Name)} is not downloaded."),
        ModelPacks.StateOf(pack) == ModelPackState.Damaged);

    private static OcrPackUnavailableException Damaged(ModelPack pack)
    {
        ModelPacks.MarkDamaged(pack);
        return new OcrPackUnavailableException(L.T("The OCR pack is damaged. Download it again on the AI models page."), true);
    }

    private static string[] ReadCharacters(ModelPack pack, string folder, ModelPackFile file)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, file.File));
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw Damaged(pack);
            var characters = OcrPackPipeline.ParseCharacterList(new System.Text.UTF8Encoding(false, true).GetString(bytes));
            return characters.Length > 0 ? characters : throw Damaged(pack);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException) { throw Failure(pack, pack); }
    }

    private static OcrLayout Read(InferenceSession detector, InferenceSession recognizer, string[] characters, byte[] pixels, int width, int height, CancellationToken token)
    {
        var boxes = new List<OcrPackBox>();
        using (PipelineMetrics.Measure("ocr-pack.detect"))
            foreach (var strip in OcrPackPipeline.Strips(height))
            {
                token.ThrowIfCancellationRequested();
                boxes.AddRange(Detect(detector, pixels, width, height, strip));
                if (boxes.Count > OcrPackPipeline.MaxBoxes) throw new InvalidOperationException(L.T("Too much text in this picture. Crop it and try again."));
            }
        boxes.RemoveAll(box => box.Width < 4 || box.Height < 4);
        var lines = new List<OcrPackLine>();
        using (PipelineMetrics.Measure("ocr-pack.recognize"))
        {
            var widths = boxes.Select(box => ResizedWidth(box)).ToArray();
            foreach (var batch in OcrPackPipeline.Batches(widths))
            {
                token.ThrowIfCancellationRequested();
                lines.AddRange(Recognize(recognizer, characters, pixels, width, batch.Select(i => (boxes[i], widths[i])).ToArray()));
            }
        }
        return OcrPackPipeline.ToLayout(lines, width, height);
    }

    private static IReadOnlyList<OcrPackBox> Detect(InferenceSession detector, byte[] pixels, int width, int height, OcrPackStrip strip)
    {
        int rows = Math.Min(strip.Height, height - strip.Top);
        int paddedWidth = (width + 31) / 32 * 32, paddedHeight = (rows + 31) / 32 * 32;
        var input = new DenseTensor<float>([1, 3, paddedHeight, paddedWidth]);
        var buffer = input.Buffer.Span; int plane = paddedWidth * paddedHeight;
        for (int y = 0; y < rows; y++)
        {
            int source = (strip.Top + y) * width * 4, target = y * paddedWidth;
            for (int x = 0; x < width; x++, source += 4, target++)
                for (int c = 0; c < 3; c++) // BGR byte order, as the publisher's pipeline reads images
                    buffer[c * plane + target] = (pixels[source + c] / 255f - DetectorMean[c]) / DetectorStd[c];
        }
        using var results = detector.Run([NamedOnnxValue.CreateFromTensor(detector.InputMetadata.Keys.First(), input)]);
        var output = results.First().AsTensor<float>();
        if (output.Dimensions.Length != 4 || output.Dimensions[2] != paddedHeight || output.Dimensions[3] != paddedWidth)
            throw new InvalidDataException("The text detector returned an unexpected shape.");
        float[] map = output is DenseTensor<float> dense ? dense.Buffer.ToArray() : output.ToArray();
        return OcrPackPipeline.FindBoxes(map, paddedWidth, paddedHeight, width, rows, strip.Top, strip);
    }

    private const int MaxLineWidth = 16_000;
    private static int ResizedWidth(OcrPackBox box) => Math.Clamp((int)Math.Ceiling(OcrPackPipeline.LineHeight * (double)box.Width / box.Height), 1, MaxLineWidth);

    private static IEnumerable<OcrPackLine> Recognize(InferenceSession recognizer, string[] characters, byte[] pixels, int width, (OcrPackBox Box, int Width)[] batch)
    {
        const int lineHeight = OcrPackPipeline.LineHeight;
        int inputWidth = OcrPackPipeline.InputWidth(batch.Max(item => item.Width));
        var input = new DenseTensor<float>([batch.Length, 3, lineHeight, inputWidth]);
        var buffer = input.Buffer.Span; int plane = lineHeight * inputWidth;
        for (int n = 0; n < batch.Length; n++)
        {
            var (box, lineWidth) = batch[n];
            double scaleX = box.Width / (double)lineWidth, scaleY = box.Height / (double)lineHeight;
            int offset = n * 3 * plane;
            for (int y = 0; y < lineHeight; y++)
            {
                double sy = Math.Clamp((y + .5) * scaleY - .5, 0, box.Height - 1);
                int y0 = (int)sy, y1 = Math.Min(y0 + 1, box.Height - 1); double fy = sy - y0;
                for (int x = 0; x < lineWidth; x++)
                {
                    double sx = Math.Clamp((x + .5) * scaleX - .5, 0, box.Width - 1);
                    int x0 = (int)sx, x1 = Math.Min(x0 + 1, box.Width - 1); double fx = sx - x0;
                    int a = ((box.Y + y0) * width + box.X + x0) * 4, b = ((box.Y + y0) * width + box.X + x1) * 4;
                    int c0 = ((box.Y + y1) * width + box.X + x0) * 4, d = ((box.Y + y1) * width + box.X + x1) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        double value = (pixels[a + c] * (1 - fx) + pixels[b + c] * fx) * (1 - fy) + (pixels[c0 + c] * (1 - fx) + pixels[d + c] * fx) * fy;
                        buffer[offset + c * plane + y * inputWidth + x] = (float)((value / 255 - .5) / .5);
                    }
                }
            }
        }
        using var results = recognizer.Run([NamedOnnxValue.CreateFromTensor(recognizer.InputMetadata.Keys.First(), input)]);
        var output = results.First().AsTensor<float>();
        if (output.Dimensions.Length != 3 || output.Dimensions[0] != batch.Length || output.Dimensions[2] != characters.Length + 2)
            throw new InvalidDataException("The text recognizer does not match its character list.");
        int steps = output.Dimensions[1], classes = output.Dimensions[2];
        float[] scores = output is DenseTensor<float> dense ? dense.Buffer.ToArray() : output.ToArray();
        var lines = new OcrPackLine[batch.Length];
        for (int n = 0; n < batch.Length; n++)
        {
            int usable = (int)Math.Ceiling(batch[n].Width / (inputWidth / (double)steps)) + 1;
            var decoded = OcrPackPipeline.DecodeCtc(scores.AsSpan(n * steps * classes, steps * classes), steps, classes, usable, characters);
            lines[n] = new OcrPackLine(batch[n].Box, decoded, inputWidth, steps);
        }
        return lines;
    }
}
