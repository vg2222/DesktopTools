using DesktopTools.Core;

/// <summary>Beam search over toy vocabularies: it must find a better whole sequence than greedy, keep parents straight and match argmax with one beam.</summary>
internal static class TranslationBeamSearchTests
{
    // Toy vocabulary: 0 = end, 1 = pad (forbidden), 2..4 = words.
    private const int End = 0, Pad = 1, Vocab = 5;

    private static float[] Row(params (int Token, double Probability)[] entries)
    {
        var row = new float[Vocab];
        Array.Fill(row, -30f);
        foreach (var (token, probability) in entries) row[token] = (float)Math.Log(probability);
        return row;
    }

    // Runs a search where the next distribution depends only on the tokens so far.
    private static int[] Run(TranslationBeamSearch search, Func<IReadOnlyList<int>, float[]> model)
    {
        var live = new List<List<int>> { new() };
        while (!search.Done)
        {
            var logits = new float[live.Count * Vocab];
            for (int r = 0; r < live.Count; r++) model(live[r]).CopyTo(logits, r * Vocab);
            var parents = search.Step(logits, Vocab, Pad);
            var next = new List<List<int>>();
            for (int b = 0; b < parents.Count; b++) next.Add([.. live[parents[b]], search.NextTokens[b]]);
            live = next;
        }
        return search.HasResult ? search.Best : [];
    }

    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }

        // A NaN in the decoder output is an invalid result, not a score that silently poisons the search.
        var nan = Row((2, .5), (3, .5)); nan[4] = float.NaN;
        try { new TranslationBeamSearch(2, End, 10).Step(nan, Vocab, Pad); throw new Exception("NaN logits were accepted"); }
        catch (System.IO.InvalidDataException) { }

        // Greedy takes word 2 (0.6) but every continuation after 2 is poor; word 3 (0.4) is followed by a certain end.
        float[] Trap(IReadOnlyList<int> prefix) => prefix.Count switch
        {
            0 => Row((2, .6), (3, .4)),
            1 when prefix[0] == 2 => Row((4, .34), (End, .33), (2, .33)),
            1 => Row((End, .99), (4, .01)),
            _ => Row((End, .99), (4, .01))
        };
        var greedy = Run(new TranslationBeamSearch(1, End, 10), Trap);
        Check(greedy.Length >= 2 && greedy[0] == 2, "One beam must follow argmax (2 first): " + string.Join(",", greedy));
        var beam = Run(new TranslationBeamSearch(2, End, 10), Trap);
        Check(beam.SequenceEqual([3]), "Two beams must find 3,end: " + string.Join(",", beam));

        // One beam equals argmax at every step, including when the end token wins.
        var argmax = Run(new TranslationBeamSearch(1, End, 10), prefix => prefix.Count < 3 ? Row((4, .5), (2, .3), (3, .2)) : Row((End, .7), (2, .3)));
        Check(argmax.SequenceEqual([4, 4, 4]), "Argmax sequence: " + string.Join(",", argmax));

        // The forbidden (pad) token is never chosen even when it is the most likely.
        var noPad = Run(new TranslationBeamSearch(3, End, 10), prefix => prefix.Count < 2 ? Row((Pad, .9), (2, .05), (3, .05)) : Row((End, .9), (Pad, .1)));
        Check(noPad.Length == 2 && !noPad.Contains(Pad), "Pad was chosen: " + string.Join(",", noPad));

        // Parents: after step 0 both beams come from row 0; at step 1 the best next beams may both extend row 1.
        var search = new TranslationBeamSearch(2, End, 10);
        var first = search.Step(Row((2, .5), (3, .5)), Vocab, Pad);
        Check(first.SequenceEqual([0, 0]) && search.NextTokens.Order().SequenceEqual([2, 3]), "Step 0 parents/tokens");
        int rowOf3 = Array.IndexOf(search.NextTokens, 3);
        var logits = new float[2 * Vocab];
        Row((4, .2), (2, .2), (End, .2), (3, .2)).CopyTo(logits, (1 - rowOf3) * Vocab);
        Row((4, .5), (2, .45), (End, .05)).CopyTo(logits, rowOf3 * Vocab);
        var second = search.Step(logits, Vocab, Pad);
        Check(second.All(p => p == rowOf3) && search.NextTokens.Order().SequenceEqual([2, 4]), "Step 1 must extend the row holding 3 twice");
        Check(search.LiveCount == 2, "Live count");

        // 2,end has the higher total log-probability (-1.39 against -1.76); 3,4,4,end has the higher average (-0.44 against -0.69).
        float[] Lengths(IReadOnlyList<int> prefix) => prefix.Count switch
        {
            0 => Row((2, .5), (3, .5)),
            _ when prefix[0] == 2 => Row((End, .5), (4, .5)),
            < 3 => Row((4, .7), (End, .3)),
            _ => Row((End, .7), (4, .3))
        };
        var penalized = Run(new TranslationBeamSearch(2, End, 10, 1.0), Lengths);
        Check(penalized.SequenceEqual([3, 4, 4]), "Length penalty 1.0 must prefer the higher average: " + string.Join(",", penalized));
        var summed = Run(new TranslationBeamSearch(2, End, 10, 0.0), Lengths);
        Check(summed.SequenceEqual([2]), "Length penalty 0 must prefer the higher total: " + string.Join(",", summed));

        // At the token limit without a finished hypothesis there is no result.
        var limited = new TranslationBeamSearch(2, End, 3);
        Run(limited, _ => Row((2, .6), (3, .4)));
        Check(!limited.HasResult, "A search that never ended must have no result");
        try { _ = limited.Best; throw new Exception("Best without a result"); } catch (InvalidOperationException) { }
    }
}
