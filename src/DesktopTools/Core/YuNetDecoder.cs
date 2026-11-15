using System.Windows;

namespace DesktopTools.Core;

/// <summary>A face position found by the face finder, in the coordinates of the image the model saw. No identity, no landmarks kept.</summary>
public sealed record FaceBox(Rect Bounds, float Score);

/// <summary>
/// Decodes the raw outputs of OpenCV Zoo's YuNet (as OpenCV's FaceDetectorYN does): for each stride s of 8, 16 and 32, the anchor at row r and
/// column c of the (H/s)×(W/s) grid has score √(cls × obj) (each clamped to 0..1), centre ((c + b0)·s, (r + b1)·s) and size (e^b2·s, e^b3·s).
/// </summary>
public static class YuNetDecoder
{
    private static readonly int[] Strides = [8, 16, 32];
    private const int MaxCandidates = 5000;

    public static IReadOnlyList<FaceBox> Decode(IReadOnlyDictionary<string, float[]> outputs, int paddedWidth, int paddedHeight, float scoreThreshold, float nmsIou = .3f)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        if (paddedWidth < 32 || paddedHeight < 32 || paddedWidth % 32 != 0 || paddedHeight % 32 != 0) throw new ArgumentException("The input size must be a multiple of 32.");
        var candidates = new List<FaceBox>();
        foreach (int stride in Strides)
        {
            int columns = paddedWidth / stride, count = columns * (paddedHeight / stride);
            float[] cls = Output(outputs, $"cls_{stride}", count), obj = Output(outputs, $"obj_{stride}", count), box = Output(outputs, $"bbox_{stride}", count * 4);
            for (int i = 0; i < count; i++)
            {
                float c = cls[i], o = obj[i];
                if (!float.IsFinite(c) || !float.IsFinite(o)) continue;
                float score = MathF.Sqrt(Math.Clamp(c, 0, 1) * Math.Clamp(o, 0, 1));
                if (score < scoreThreshold) continue;
                float b0 = box[i * 4], b1 = box[i * 4 + 1], b2 = box[i * 4 + 2], b3 = box[i * 4 + 3];
                if (!float.IsFinite(b0) || !float.IsFinite(b1) || !float.IsFinite(b2) || !float.IsFinite(b3)) continue;
                int row = i / columns, column = i % columns;
                double cx = (column + b0) * stride, cy = (row + b1) * stride, width = Math.Exp(b2) * stride, height = Math.Exp(b3) * stride;
                if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) continue;
                candidates.Add(new FaceBox(new Rect(cx - width / 2, cy - height / 2, width, height), score));
            }
        }
        return Nms(candidates.OrderByDescending(face => face.Score).Take(MaxCandidates).ToList(), nmsIou);
    }

    /// <summary>Keeps the highest-scoring box of each group overlapping by more than <paramref name="iou"/>; best first.</summary>
    public static IReadOnlyList<FaceBox> Nms(IReadOnlyList<FaceBox> faces, float iou)
    {
        var kept = new List<FaceBox>();
        foreach (var face in faces.OrderByDescending(face => face.Score))
            if (kept.All(other => Overlap(face.Bounds, other.Bounds) <= iou)) kept.Add(face);
        return kept;
    }

    private static double Overlap(Rect a, Rect b)
    {
        var intersection = Rect.Intersect(a, b);
        if (intersection.IsEmpty) return 0;
        double shared = intersection.Width * intersection.Height, union = a.Width * a.Height + b.Width * b.Height - shared;
        return union <= 0 ? 0 : shared / union;
    }

    private static float[] Output(IReadOnlyDictionary<string, float[]> outputs, string name, int length) =>
        outputs.TryGetValue(name, out var values) && values != null && values.Length == length ? values : throw new ArgumentException($"YuNet output '{name}' is missing or has the wrong size.");
}
