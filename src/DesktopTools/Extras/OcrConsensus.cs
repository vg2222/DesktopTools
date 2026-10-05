using DesktopTools.Core;
using System.Windows;

namespace DesktopTools.Extras;

/// <summary>
/// Chooses the words of the final text out of several readings of the same picture (different magnifications, plain and prepared pixels, other
/// languages). Readings are first matched by position: lines that sit on top of each other form a row, and within a row words that sit on top
/// of each other form a place. For every place the candidates (one per reading) are judged by the dictionary and by how many independent
/// readings agree, so "Claim offer" beats "Claün offer" and a word that one reading garbled is taken from the reading that got it right.
/// </summary>
internal static class OcrConsensus
{
    internal sealed record Reading(string Name, IReadOnlyList<OcrEnhancer.Line> Lines, double Prior = 0);

    private sealed record Piece(int Reading, OcrWordBox Word, int Line);
    private sealed record Candidate(int Reading, List<OcrWordBox> Words, string Text, double Score);

    /// <param name="lexicon">Says whether a word of the given alphabet is a real word; null when no dictionary exists for it.</param>
    public static IReadOnlyList<OcrEnhancer.Line> Merge(IReadOnlyList<Reading> readings, Func<OcrEnhancer.Script, string, bool?> lexicon)
    {
        var lines = new List<(int Reading, OcrEnhancer.Line Line)>();
        for (int r = 0; r < readings.Count; r++) foreach (var line in readings[r].Lines) if (line.Text.Trim().Length > 0 && !line.Bounds.IsEmpty) lines.Add((r, line));
        if (lines.Count == 0) return [];
        if (readings.Count == 1) return readings[0].Lines.Where(l => OcrEnhancer.Plausibility(l.Text) > 0 || l.Text.Any(char.IsLetterOrDigit)).ToArray();

        // 1. Rows: lines of different readings that cover the same stretch of a text row.
        var parent = Enumerable.Range(0, lines.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var byTop = Enumerable.Range(0, lines.Count).OrderBy(i => lines[i].Line.Bounds.Top).ToArray();
        for (int a = 0; a < byTop.Length; a++)
            for (int b = a + 1; b < byTop.Length; b++)
            {
                var one = lines[byTop[a]]; var two = lines[byTop[b]];
                if (two.Line.Bounds.Top > one.Line.Bounds.Bottom) break;
                if (one.Reading == two.Reading) continue;
                if (Overlap(one.Line.Bounds.Top, one.Line.Bounds.Bottom, two.Line.Bounds.Top, two.Line.Bounds.Bottom) < .5 * Math.Min(one.Line.Bounds.Height, two.Line.Bounds.Height)) continue;
                if (Overlap(one.Line.Bounds.Left, one.Line.Bounds.Right, two.Line.Bounds.Left, two.Line.Bounds.Right) <= 0) continue;
                parent[Find(byTop[a])] = Find(byTop[b]);
            }
        var rows = Enumerable.Range(0, lines.Count).GroupBy(Find).Select(g => g.ToArray()).ToList();

        var result = new List<OcrEnhancer.Line>();
        foreach (var row in rows)
        {
            // 2. Places inside the row: words of different readings that cover the same stretch of the line.
            var pieces = new List<Piece>();
            foreach (int index in row)
            {
                var (reading, line) = lines[index];
                var words = line.Words is { Count: > 0 } ? line.Words : [new OcrWordBox(line.Text, line.Bounds, 0)];
                foreach (var word in words) if (word.Text.Trim().Length > 0) pieces.Add(new Piece(reading, word, index));
            }
            var near = Enumerable.Range(0, pieces.Count).ToArray();
            int Root(int x) { while (near[x] != x) { near[x] = near[near[x]]; x = near[x]; } return x; }
            for (int i = 0; i < pieces.Count; i++)
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    if (pieces[i].Reading == pieces[j].Reading) continue;
                    var p = pieces[i].Word.Bounds; var q = pieces[j].Word.Bounds;
                    double shared = Overlap(p.Left, p.Right, q.Left, q.Right);
                    // Same stretch of the line AND the same height: a word of the row below never stands in for a word of this row.
                    if (shared > 0 && shared >= .4 * Math.Min(p.Width, q.Width) && Overlap(p.Top, p.Bottom, q.Top, q.Bottom) >= .4 * Math.Min(p.Height, q.Height)) near[Root(i)] = Root(j);
                }
            var places = Enumerable.Range(0, pieces.Count).GroupBy(Root).Select(g => g.Select(i => pieces[i]).ToList())
                .OrderBy(g => g.Min(x => x.Word.Bounds.Left)).ToList();

            // 3. For every place: one candidate per reading, judged on its own and by agreement.
            var chosen = new List<List<OcrWordBox>>();
            foreach (var place in places)
            {
                var candidates = place.GroupBy(x => x.Reading).Select(g =>
                {
                    var words = g.Select(x => x.Word).OrderBy(w => w.Bounds.Left).ToList();
                    string text = string.Join(" ", words.Select(w => w.Text));
                    return new Candidate(g.Key, words, text, 0);
                }).ToList();
                if (candidates.Count == 1)
                {
                    // Only one reading saw anything here: keep it unless it is symbol soup.
                    if (OcrEnhancer.Plausibility(candidates[0].Text) > 0 || candidates[0].Text.Any(char.IsLetterOrDigit) && candidates[0].Text.Count(char.IsLetterOrDigit) * 2 >= candidates[0].Text.Length)
                        chosen.Add(candidates[0].Words);
                    continue;
                }
                Candidate? best = null;
                foreach (var candidate in candidates)
                {
                    double agreement = candidates.Count(o => o != candidate && Same(o.Text, candidate.Text));
                    double score = Score(candidate.Text, lexicon) + .5 * Letters(candidate.Text) * Math.Min(agreement, 3) + readings[candidate.Reading].Prior;
                    if (best == null || score > best.Score || (score == best.Score && candidate.Text.Length > best.Text.Length)) best = candidate with { Score = score };
                }
                chosen.Add(best!.Words);
            }
            if (chosen.Count == 0) continue;

            // 4. The row again: the chosen words left to right.
            // (a row linked to its neighbour by one tall line is split back into visual rows)
            var rowsOfWords = new List<List<OcrWordBox>>();
            foreach (var word in chosen.SelectMany(w => w).OrderBy(w => w.Bounds.Top + w.Bounds.Height / 2))
            {
                double centre = word.Bounds.Top + word.Bounds.Height / 2;
                var target = rowsOfWords.FirstOrDefault(r => Math.Abs(centre - r.Average(w => w.Bounds.Top + w.Bounds.Height / 2)) < .6 * Math.Min(word.Bounds.Height, r.Average(w => w.Bounds.Height)));
                if (target == null) rowsOfWords.Add([word]); else target.Add(word);
            }
            foreach (var r in rowsOfWords.OrderBy(r => r.Average(w => w.Bounds.Top))) result.Add(Compose(r.OrderBy(w => w.Bounds.Left).ToList()));
        }
        return result;
    }

    private static OcrEnhancer.Line Compose(IReadOnlyList<OcrWordBox> all)
    {
        var text = new System.Text.StringBuilder(); var bounds = Rect.Empty; double height = Math.Max(1, all.Average(w => w.Bounds.Height));
        for (int i = 0; i < all.Count; i++)
        {
            if (i > 0) text.Append(all[i].Bounds.Left - all[i - 1].Bounds.Right > height * 2.5 ? "    " : " ");
            text.Append(all[i].Text); bounds.Union(all[i].Bounds);
        }
        return new OcrEnhancer.Line(text.ToString(), bounds, all);
    }

    private static double Overlap(double a1, double a2, double b1, double b2) => Math.Min(a2, b2) - Math.Max(a1, b1);
    private static string Key(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool Same(string a, string b) => Key(a) == Key(b);
    private static int Letters(string text) => text.Count(char.IsLetterOrDigit);

    /// <summary>How much the text looks like real words: dictionary words count for their length, unknown words, mixed alphabets and stray accents count against.</summary>
    internal static double Score(string text, Func<OcrEnhancer.Script, string, bool?> lexicon)
    {
        double score = 0;
        foreach (var raw in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            string core = raw.Trim(raw.Where(c => !char.IsLetterOrDigit(c)).Distinct().Where(c => c != '$' && c != '%' && c != '#').ToArray());
            if (core.Length == 0) { score -= .3 * raw.Length; continue; }
            int letters = core.Count(char.IsLetter), digits = core.Count(char.IsDigit);
            bool technical = core.Contains('@') || core.Contains("://") || core.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || core.Contains('/') || core.Contains('_') || (core.Contains('.') && letters > 0 && core.Trim('.').Contains('.'));
            if (technical) { score += .3 * core.Length; continue; }
            if (letters == 0) { score += .5 * core.Length; continue; }          // numbers, prices, versions
            if (digits > 0) { score += .1 * core.Length - 1.0 * Math.Min(letters, 2); continue; }   // letters inside numbers are usually misread digits
            // A word. Hyphens and apostrophes are part of it.
            string word = new(core.Where(c => char.IsLetter(c)).ToArray());
            var scripts = word.Select(ScriptOfLetter).Distinct().ToArray();
            if (scripts.Length > 1) { score -= 2.5 * word.Length; continue; }
            var script = scripts[0];
            bool? known = word.Length == 1 && script == OcrEnhancer.Script.Latin ? (word is "a" or "A" or "I" ? true : false) : lexicon(script, core.Replace("’", "'"));
            if (known == true) score += word.Length + (word.Length >= 3 ? .5 : 0);
            else if (known == false) score -= .8 * word.Length + (HasStrayAccent(word, script) ? 2 : 0);
            else score += .25 * word.Length;
        }
        return score;
    }

    private static OcrEnhancer.Script ScriptOfLetter(char c) => OcrEnhancer.ScriptOf(c);

    /// <summary>Accented Latin letters inside a word the dictionary does not know are almost always a misread of plain letters (ü for im, é for e).</summary>
    private static bool HasStrayAccent(string word, OcrEnhancer.Script script) => script == OcrEnhancer.Script.Latin && word.Any(c => c > 127);

    // ------------------------------------------------------------------------------------------------------------------------------------------
    // Repair: an unknown word that is one OCR confusion away from a real word becomes that word.

    // OCR output on the left, what was really printed on the right.
    private static readonly (string Seen, string Real)[] Merged = [("rn", "m"), ("m", "rn"), ("vv", "w"), ("w", "vv"), ("cl", "d"), ("d", "cl"), ("ii", "u"), ("li", "h"), ("ri", "n")];
    private static readonly HashSet<string> Confusable = new("il ij it lt l1 i1 mn nr nh hb hk oc oe oa ou ce ae uv vy s5 gq z2 ft nu b6 do rt".Split(' '));
    private static bool Pair(char a, char b) => a == b || Confusable.Contains(new string(a < b ? [a, b] : [b, a]));

    private static double Substitute(char seen, char real)
    {
        if (seen == real) return 0;
        if (Pair(seen, real)) return .4;
        if (seen > 127 && real < 128) return RemoveAccent(seen) == real ? .15 : .5;   // an accented letter inside an English word is a misread plain letter
        return 1;
    }
    private static char RemoveAccent(char c) { var d = c.ToString().Normalize(System.Text.NormalizationForm.FormD); return d.Length > 0 ? d[0] : c; }
    private static double Drop(char real) => "iljtrf'".IndexOf(real) >= 0 ? .6 : 1;   // thin letters are the ones OCR loses

    /// <summary>Distance from what OCR returned to a candidate word, counting letter pairs OCR mixes up as cheap.</summary>
    internal static double OcrDistance(string seen, string real)
    {
        seen = seen.ToLowerInvariant(); real = real.ToLowerInvariant();
        var d = new double[seen.Length + 1, real.Length + 1];
        for (int i = 0; i <= seen.Length; i++) for (int j = 0; j <= real.Length; j++) d[i, j] = double.MaxValue / 4;
        d[0, 0] = 0;
        for (int i = 0; i <= seen.Length; i++)
            for (int j = 0; j <= real.Length; j++)
            {
                double here = d[i, j]; if (here >= double.MaxValue / 8) continue;
                if (i < seen.Length && j < real.Length) d[i + 1, j + 1] = Math.Min(d[i + 1, j + 1], here + Substitute(seen[i], real[j]));
                if (i < seen.Length) d[i + 1, j] = Math.Min(d[i + 1, j], here + 1);                       // OCR added a letter
                if (j < real.Length) d[i, j + 1] = Math.Min(d[i, j + 1], here + Drop(real[j]));          // OCR lost a letter
                foreach (var (s, r) in Merged)
                    if (string.CompareOrdinal(seen, i, s, 0, s.Length) == 0 && string.CompareOrdinal(real, j, r, 0, r.Length) == 0)
                        d[i + s.Length, j + r.Length] = Math.Min(d[i + s.Length, j + r.Length], here + .3);
            }
        return d[seen.Length, real.Length] + .1 * Math.Abs(seen.Length - real.Length);
    }

    private static string? RepairWord(string token, Func<OcrEnhancer.Script, string, bool?> lexicon, Func<OcrEnhancer.Script, string, IReadOnlyList<string>> suggest)
    {
        int start = 0, end = token.Length;
        while (start < end && !char.IsLetter(token[start])) start++;
        while (end > start && !char.IsLetter(token[end - 1])) end--;
        string core = token[start..end];
        if (core.Length < 4 || core.Any(c => !char.IsLetter(c))) return null;
        var script = OcrEnhancer.ScriptOf(core[0]);
        if (script != OcrEnhancer.Script.Latin || lexicon(script, core) != false) return null;
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string suggestion in suggest(script, core)) if (suggestion.All(char.IsLetter)) candidates.Add(suggestion);
        string lower = core.ToLowerInvariant();
        foreach (var (seen, real) in Merged)
            for (int at = lower.IndexOf(seen, StringComparison.Ordinal); at >= 0; at = lower.IndexOf(seen, at + 1, StringComparison.Ordinal))
                candidates.Add(lower[..at] + real + lower[(at + seen.Length)..]);
        string? best = null; double bestCost = double.MaxValue, second = double.MaxValue;
        foreach (string candidate in candidates)
        {
            if (candidate.Equals(core, StringComparison.OrdinalIgnoreCase) || lexicon(script, candidate) != true) continue;
            double cost = OcrDistance(core, candidate);
            if (cost < bestCost) { second = bestCost; bestCost = cost; best = candidate; } else if (cost < second) second = cost;
        }
        if (best == null || bestCost > 1.0 || second - bestCost < .15) return null;
        // Keep the capitals of what was printed.
        best = core.All(char.IsUpper) ? best.ToUpperInvariant() : char.IsUpper(core[0]) && core.Skip(1).All(char.IsLower) ? char.ToUpperInvariant(best[0]) + best[1..].ToLowerInvariant() : best.ToLowerInvariant();
        return token[..start] + best + token[end..];
    }

    public static IReadOnlyList<OcrEnhancer.Line> Repair(IReadOnlyList<OcrEnhancer.Line> lines, Func<OcrEnhancer.Script, string, bool?> lexicon, Func<OcrEnhancer.Script, string, IReadOnlyList<string>> suggest)
    {
        var result = new List<OcrEnhancer.Line>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Words is not { Count: > 0 } words) { result.Add(line); continue; }
            var repaired = new List<OcrWordBox>(words.Count); bool changed = false;
            foreach (var word in words)
            {
                string? better = RepairWord(word.Text, lexicon, suggest);
                if (better != null) { repaired.Add(word with { Text = better }); changed = true; } else repaired.Add(word);
            }
            result.Add(changed ? Compose(repaired) : line);
        }
        return result;
    }
}
