using System.Windows;
using DesktopTools.Core;

/// <summary>Decoding YuNet's raw outputs: anchor grid positions, score = sqrt(cls × obj), threshold, NMS and bad values.</summary>
internal static class YuNetDecoderTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    // Empty outputs for a padded 64×64 input: 8×8, 4×4 and 2×2 anchors.
    private static Dictionary<string, float[]> Outputs()
    {
        var outputs = new Dictionary<string, float[]>();
        foreach (int stride in new[] { 8, 16, 32 })
        {
            int count = (64 / stride) * (64 / stride);
            outputs[$"cls_{stride}"] = new float[count]; outputs[$"obj_{stride}"] = new float[count];
            outputs[$"bbox_{stride}"] = new float[count * 4]; outputs[$"kps_{stride}"] = new float[count * 10];
        }
        return outputs;
    }

    private static void Set(Dictionary<string, float[]> outputs, int stride, int row, int column, float cls, float obj, float b0, float b1, float b2, float b3)
    {
        int index = row * (64 / stride) + column;
        outputs[$"cls_{stride}"][index] = cls; outputs[$"obj_{stride}"][index] = obj;
        outputs[$"bbox_{stride}"][index * 4] = b0; outputs[$"bbox_{stride}"][index * 4 + 1] = b1; outputs[$"bbox_{stride}"][index * 4 + 2] = b2; outputs[$"bbox_{stride}"][index * 4 + 3] = b3;
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-3;

    public static void Decode()
    {
        var outputs = Outputs();
        float ln2 = MathF.Log(2);
        Set(outputs, 8, 2, 3, .9f, .9f, .5f, .5f, ln2, ln2);
        var faces = YuNetDecoder.Decode(outputs, 64, 64, .6f);
        Check(faces.Count == 1, $"one face expected, got {faces.Count}");
        var face = faces[0];
        Check(Near(face.Bounds.X + face.Bounds.Width / 2, 28) && Near(face.Bounds.Y + face.Bounds.Height / 2, 20), $"centre {face.Bounds}");
        Check(Near(face.Bounds.Width, 16) && Near(face.Bounds.Height, 16), $"size {face.Bounds}");
        Check(Near(face.Score, .9), $"score {face.Score}");

        // Below the threshold: sqrt(.5 × .5) = .5 < .6.
        var weak = Outputs(); Set(weak, 16, 1, 1, .5f, .5f, .5f, .5f, ln2, ln2);
        Check(YuNetDecoder.Decode(weak, 64, 64, .6f).Count == 0, "a weak score was kept");

        // Values outside 0..1 are clamped before the square root; NaN and infinity are ignored.
        var bad = Outputs();
        Set(bad, 8, 0, 0, float.NaN, .9f, .5f, .5f, ln2, ln2);
        Set(bad, 8, 0, 1, .9f, .9f, float.NaN, .5f, ln2, ln2);
        Set(bad, 8, 0, 2, .9f, .9f, .5f, .5f, float.PositiveInfinity, ln2);
        Set(bad, 32, 1, 1, 2f, 1f, .5f, .5f, ln2, ln2);
        var kept = YuNetDecoder.Decode(bad, 64, 64, .6f);
        Check(kept.Count == 1 && Near(kept[0].Score, 1), $"bad values: {string.Join(", ", kept)}");

        // Missing outputs or wrong lengths are refused.
        var missing = Outputs(); missing.Remove("obj_16");
        Check(Throws(() => YuNetDecoder.Decode(missing, 64, 64, .6f)), "missing output accepted");
        var shortOutput = Outputs(); shortOutput["bbox_8"] = new float[3];
        Check(Throws(() => YuNetDecoder.Decode(shortOutput, 64, 64, .6f)), "short output accepted");
    }

    public static void Nms()
    {
        // IoU of two 10×10 boxes offset by 10/3 horizontally: overlap 6.67×10 / (200 − 66.7) = 0.5 → one kept, the higher score.
        var overlapping = YuNetDecoder.Nms([new FaceBox(new Rect(0, 0, 10, 10), .7f), new FaceBox(new Rect(10 / 3d, 0, 10, 10), .9f)], .3f);
        Check(overlapping.Count == 1 && Near(overlapping[0].Score, .9), $"overlapping: {string.Join(", ", overlapping)}");
        var apart = YuNetDecoder.Nms([new FaceBox(new Rect(0, 0, 10, 10), .7f), new FaceBox(new Rect(30, 0, 10, 10), .9f)], .3f);
        Check(apart.Count == 2 && apart[0].Score > apart[1].Score, "separate boxes must both be kept, best first");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }
}
