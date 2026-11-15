using System.IO;

namespace DesktopTools.Core;

/// <summary>
/// Beam search for a sentence decoder, one step at a time. The caller runs the decoder for <see cref="LiveCount"/> rows (one row at the
/// first step), passes the logits, and reorders its decoder cache with the parent rows <see cref="Step"/> returns. A finished hypothesis
/// is scored by its summed log-probability divided by length^<c>lengthPenalty</c> (the Marian and Hugging Face default is 1.0). With one
/// beam the search is exactly argmax decoding.
/// </summary>
public sealed class TranslationBeamSearch
{
    private readonly int beams, endToken, maxTokens;
    private readonly double lengthPenalty;
    private List<(int[] Tokens, double Score)> live = [([], 0)];
    private readonly List<(int[] Tokens, double Score)> finished = [];
    private int steps;

    public TranslationBeamSearch(int beams, int endToken, int maxTokens, double lengthPenalty = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(beams, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 1);
        (this.beams, this.endToken, this.maxTokens, this.lengthPenalty) = (beams, endToken, maxTokens, lengthPenalty);
    }

    /// <summary>Rows the next <see cref="Step"/> expects.</summary>
    public int LiveCount => live.Count;
    /// <summary>The token each live row received at the last step, in row order.</summary>
    public int[] NextTokens { get; private set; } = [];
    public bool Done { get; private set; }
    public bool HasResult => finished.Count > 0;
    /// <summary>The best finished sequence without the end token.</summary>
    public int[] Best => finished.Count == 0 ? throw new InvalidOperationException("The search finished no sequence.") : finished.MaxBy(f => f.Score).Tokens;

    /// <summary>
    /// Takes logits laid out as <see cref="LiveCount"/> rows of <paramref name="vocab"/> values and returns, for each new live row, the index
    /// of the row it extends. <paramref name="forbiddenToken"/> (the pad token) is never chosen.
    /// </summary>
    public IReadOnlyList<int> Step(ReadOnlySpan<float> logits, int vocab, int forbiddenToken)
    {
        if (Done) throw new InvalidOperationException("The search is done.");
        if (logits.Length != live.Count * vocab) throw new ArgumentException("Expected one row of logits per live beam.", nameof(logits));
        int width = beams == 1 ? 1 : 2 * beams;
        // Best `width` candidates over all rows, kept sorted by descending total score.
        var candidates = new List<(double Score, int Row, int Token)>(width + 1);
        for (int row = 0; row < live.Count; row++)
        {
            var values = logits.Slice(row * vocab, vocab);
            double max = double.NegativeInfinity;
            foreach (float value in values) { if (float.IsNaN(value)) throw new InvalidDataException("The decoder returned an invalid result."); if (value > max) max = value; }
            if (!double.IsFinite(max)) throw new InvalidDataException("The decoder returned an invalid result.");
            double sum = 0;
            foreach (float value in values) sum += Math.Exp(value - max);
            double normalizer = max + Math.Log(sum), baseScore = live[row].Score;
            for (int token = 0; token < vocab; token++)
            {
                if (token == forbiddenToken) continue;
                double score = baseScore + values[token] - normalizer;
                if (candidates.Count == width && score <= candidates[^1].Score) continue;
                int at = candidates.FindIndex(c => score > c.Score);
                candidates.Insert(at < 0 ? candidates.Count : at, (score, row, token));
                if (candidates.Count > width) candidates.RemoveAt(width);
            }
        }
        steps++;
        var next = new List<(int[] Tokens, double Score)>(beams);
        var parents = new List<int>(beams); var tokens = new List<int>(beams);
        for (int rank = 0; rank < candidates.Count && next.Count < beams; rank++)
        {
            var (score, row, token) = candidates[rank];
            if (token == endToken)
            {
                // As in Hugging Face: an end token only counts when it ranks among the best `beams` candidates.
                if (rank < beams) AddFinished(live[row].Tokens, score);
                continue;
            }
            next.Add(([.. live[row].Tokens, token], score)); parents.Add(row); tokens.Add(token);
        }
        live = next; NextTokens = [.. tokens];
        if (beams == 1 && finished.Count > 0) live = [];
        Done = live.Count == 0 || steps >= maxTokens ||
            finished.Count >= beams && live.Max(l => l.Score) / Math.Pow(steps + 1, lengthPenalty) <= finished.Min(f => f.Score);
        return parents;
    }

    private void AddFinished(int[] tokens, double score)
    {
        double normalized = score / Math.Pow(tokens.Length + 1, lengthPenalty);
        if (finished.Count < beams) { finished.Add((tokens, normalized)); return; }
        int worst = 0;
        for (int i = 1; i < finished.Count; i++) if (finished[i].Score < finished[worst].Score) worst = i;
        if (normalized > finished[worst].Score) finished[worst] = (tokens, normalized);
    }
}
