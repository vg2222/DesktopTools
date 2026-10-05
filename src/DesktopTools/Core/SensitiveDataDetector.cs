using System.Windows;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;

namespace DesktopTools.Core;

public sealed record SensitiveFinding(Guid Id, IReadOnlyList<string> Categories, Int32Rect Bounds, string MaskedExcerpt);
public static class SensitiveDataDetector
{
    private enum SecretEvidence { None, Shape, Prose }
    private sealed record Rule(string Category, Regex Pattern, Func<string, bool>? Validate = null, Regex? Context = null, Regex? CellLabel = null, SecretEvidence Evidence = SecretEvidence.None);
    private static Regex Pattern(string text) => new(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex DigitGroups = Pattern(@"\d+");
    private static readonly Regex DateShape = Pattern(@"^\d{4}[-./]\d{1,2}[-./]\d{1,2}$|^\d{1,2}[-./]\d{1,2}[-./]\d{4}$");
    private static readonly Regex ProseToken = Pattern(@"^[\p{L}\p{Nd}][\p{L}\p{Nd}!#$@%&*?+=_-]{3,127}$");
    private static readonly Regex UrlPattern = Pattern(@"(?:https?://|www\.)\S+");
    private static readonly Regex EmailShape = Pattern(@"^[\w.+%'-]+@[\w-]+(?:\.[\w-]+)*\.(?:[a-z]{2,}|xn--[a-z0-9-]+)[.!?,;:]?$");
    private static readonly Regex Ipv4Pattern = Pattern(@"(?<![\w.])(?:[0-9oøeils|]{1,3}[ \t]*\.[ \t]*){3}[0-9oøeils|]{1,3}(?!\w|\.[0-9oøeils|])");
    private static readonly Rule[] Rules =
    [
        new("email", Pattern(@"[\w.+%'-]+[ \t]*@[ \t]*[\w-]+(?:[ \t]*\.[ \t]*[\w-]+)+"), CellLabel: CellPattern(@"email|e-mail|почта")),
        new("email", Pattern(@"[\w.+%'-]+[ \t]*\*[ \t]*[\w-]+(?:[ \t]*\.[ \t]*[\w-]+)*[ \t]*\.[ \t]*[a-z]{2,}\b")),
        new("ip", Ipv4Pattern, PlausibleIpv4),
        new("ip", Pattern(@"(?<![\w:])(?:[a-f0-9]{0,4}[ \t]*:[ \t]*){2,7}[a-f0-9]{0,4}(?![\w:])"), s => IPAddress.TryParse(string.Concat(s.Where(c => !char.IsWhiteSpace(c))), out _)),
        new("phone", Pattern(@"(?<![\w-])\+?\d[\døes() .\-]{5,}[\døes](?![\w-])"), PlausiblePhone),
        new("path", Pattern("(?:[a-zс][ \\t]*:[ \\t]*[\\\\/]|\\\\\\\\)[^\r\n\"<>|]+")),
        new("credential", Pattern(@"\b(?:gh[pousr]_|github_pat_|[s5]k-[a-z]*-?|AKIA)[a-z0-9_\-]{12,}\b")),
        new("credential", Pattern(@"\beyJ[a-z0-9_\-]+\.[a-z0-9_\-]+\.[a-z0-9_\-]+\b")),
        new("credential", Pattern(@"(?<![\w.!#$@%&*?+=_-])[\p{L}\p{Nd}][\p{L}\p{Nd}.!#$@%&*?+=_-]{6,127}(?![\w!#$@%&*?+=_-]|\.[\p{L}\p{Nd}])"), PlausibleSecret, Evidence: SecretEvidence.Shape),
        new("credential", Pattern(@"\b(?:парол(?:ь|ем|и)|passwords?)[ \t]+(?<value>[\p{L}\p{Nd}][\p{L}\p{Nd}!#$@%&*?+=_-]{3,127})(?!\w|\.[\p{L}\p{Nd}])"), PlausibleProseSecret,
            Context: Pattern(@"\b(?:парол(?:ь|ем|и)|passwords?)[ \t]*$"), Evidence: SecretEvidence.Prose),
        ContextRule("credential", @"api[ _-]*(?:key|ключ)|access[ _-]*token|token|secret|password|authorization|пароль|токен|ключ[ _-]*api|passwort|schlüssel|mot[ ]*de[ ]*passe|jeton|contraseña|clave", true),
        ContextRule("username", @"user[ _-]*name|login|nickname|никнейм|имя[ ]*пользователя|логин|benutzername|nom[ ]*d'utilisateur|usuario"),
        ContextRule("account", @"(?:account|user|customer)[ _-]*[i1l][d0b]|account[ _-]*number|идентификатор[ ]*(?:аккаунта|пользователя)|номер[ ]*сч[её]та|konto(?:nummer|[ ]*id)|identifiant[ ]*de[ ]*compte|cuenta"),
        ContextRule("serial", @"serial(?:[ _-]*(?:number|no))?|s/n|серийный[ ]*номер|seriennummer|numéro[ ]*de[ ]*série|número[ ]*de[ ]*serie")
    ];
    // OCR often drops underscores/hyphens and splits a field into words. Cover
    // the complete value on its OCR line, including spaces and quote marks.
    private static Rule ContextRule(string category, string labels, bool credential = false)
    {
        string prefix = @"(?:\b(?:" + labels + @"))[ \t]*[:=][ \t]*";
        return new(category, Pattern(prefix + (credential ? @"(?:(?:Bearer|Basic)[ \t]+)?" : "") + @"(?<value>[^\r\n;<>]{3,})"), Context: Pattern(prefix),
            CellLabel: CellPattern(labels));
    }
    private static Regex CellPattern(string labels) => Pattern(@"^(?:[^\p{L}\p{N}]{1,3}[ \t]*)?(?:" + labels + @")(?:[ \t]+[0-9З])?[ \t]*[:=]?[ \t]*$");
    private static bool PlausibleIpv4(string text)
    {
        if (text.Count(char.IsDigit) < 2) return false;
        string normalized = string.Concat(text.Where(c => !char.IsWhiteSpace(c)).Select(c => char.ToLowerInvariant(c) switch
        { 'ø' or 'o' or 'e' => '0', 'i' or 'l' or '|' => '1', 's' => '5', _ => c }));
        return normalized.Split('.').All(p => int.TryParse(p, out int number) && number is >= 0 and <= 255);
    }
    private static bool PlausiblePhone(string text)
    {
        if (text.Count(char.IsDigit) < 2) return false;
        string normalized = text.Replace('ø', '0').Replace('Ø', '0').Replace('e', '0').Replace('E', '0').Replace('s', '5').Replace('S', '5');
        int digits = normalized.Count(char.IsDigit);
        if (Ipv4Pattern.Matches(normalized).Cast<Match>().Any(m => PlausibleIpv4(m.Value))) return false;
        var groups = DigitGroups.Matches(normalized).Cast<Match>().ToArray();
        bool countryPrefix = groups.Length >= 4 && digits == 11 && groups[0].Value is "1" or "7" or "8";
        if (!normalized.StartsWith('+') && !normalized.Contains('(') && !normalized.Contains('-') && !countryPrefix && groups[0].Length < 2) return false;
        return digits is >= 7 and <= 15 && !IPAddress.TryParse(normalized, out _) &&
            !DateShape.IsMatch(normalized) &&
            (text.StartsWith('+') || text.Any(c => c is ' ' or '(' or '-' ));
    }
    private static bool HardSecretSymbol(char c) => c is '!' or '#' or '$' or '@' or '&' or '*' or '?';
    private static bool PlausibleProseSecret(string text) => text.Count(char.IsLetter) >= 2 && text.Any(HardSecretSymbol) &&
        (text.Any(char.IsDigit) || text.Take(Math.Max(0, text.Length - 1)).Any(HardSecretSymbol));
    private static bool PlausibleSecret(string text) => text.Length >= 8 && text.Count(char.IsDigit) >= 2 && text.Count(char.IsLetter) >= 2 && text.Any(HardSecretSymbol) && !EmailShape.IsMatch(text);
    private static bool WrappedSecretPrefix(string text) => text.Length is >= 6 and <= 128 && text.EndsWith('!') && text.Any(char.IsUpper) && text.Any(char.IsLower);

    public static IReadOnlyList<SensitiveFinding> Detect(OcrLayout layout, IReadOnlyCollection<string> categories)
    {
        using var timing = PipelineMetrics.Measure("privacy.detection");
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.PixelWidth < 1 || layout.PixelHeight < 1 || layout.Words.Count > 10_000 || layout.Words.Sum(w => (long)w.Text.Length + 1) > 200_000)
            throw new ArgumentException("The recognized image exceeds the sensitive-data analysis limit.");
        var found = new List<SensitiveFinding>();
        var incompleteLabels = new List<(Rule Rule, Rect Bounds)>();
        var strongSecrets = new List<Rect>(); var wrappedPrefixes = new List<Rect>();
        var selectedRules = Rules.Where(r => categories.Contains(r.Category)).ToArray();
        foreach (var line in layout.Words.GroupBy(w => w.LineIndex))
        {
            var words = line.OrderBy(w => w.Bounds.X).ToArray();
            var compact = ReadLine(words, true); var spaced = ReadLine(words, false);
            var urls = UrlPattern.Matches(compact.Text).Cast<Match>().ToArray();
            foreach (var rule in selectedRules)
            {
                var reading = rule.Category == "phone" ? spaced : compact;
                string text = rule.Category is "ip" or "credential" ? reading.Text.Replace('ø', '0').Replace('Ø', '0') : reading.Text;
                // Break greedy phone candidates at valid IPs while retaining
                // character offsets and any real phone on either side.
                if (rule.Category == "phone")
                {
                    var characters = text.ToCharArray();
                    foreach (Match address in Ipv4Pattern.Matches(text))
                        if (PlausibleIpv4(address.Value)) Array.Fill(characters, 'x', address.Index, address.Length);
                    text = new string(characters);
                }
                var matches = rule.Pattern.Matches(text);
                foreach (Match match in matches)
                {
                    var value = match.Groups["value"].Success ? match.Groups["value"] : match;
                    if (rule.Evidence != SecretEvidence.None && urls.Any(u => value.Index < u.Index + u.Length && value.Index + value.Length > u.Index)) continue;
                    var bounds = MatchBounds(reading.Offsets, value);
                    if (rule.Validate != null && !rule.Validate(value.Value.Trim()))
                    {
                        if (rule.Evidence == SecretEvidence.Prose && WrappedSecretPrefix(value.Value)) wrappedPrefixes.Add(bounds);
                        continue;
                    }
                    AddBounds(found, rule.Category, bounds, layout);
                    if (rule.Evidence == SecretEvidence.Shape) strongSecrets.Add(bounds);
                }
                if (rule.Context != null && matches.Count == 0)
                    foreach (Match label in rule.Context.Matches(text)) incompleteLabels.Add((rule, MatchBounds(reading.Offsets, label)));
            }
        }
        // Complementary readings may recognize the label and value separately.
        // Match only nearby words on the same physical row, never a distant
        // column. A Y-sorted index bounds the search instead of scanning all text.
        var positioned = layout.Words.Where(w => ValidBounds(w.Bounds)).OrderBy(w => w.Bounds.Y).ToArray();
        double maxHeight = positioned.Length == 0 ? 0 : positioned.Max(w => w.Bounds.Height);
        foreach (var label in incompleteLabels.Where(l => ValidBounds(l.Bounds)))
        {
            int lo = 0, hi = positioned.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (positioned[mid].Bounds.Y < label.Bounds.Y - maxHeight) lo = mid + 1; else hi = mid; }
            var nearby = new List<OcrWordBox>();
            for (int i = lo; i < positioned.Length && positioned[i].Bounds.Y < label.Bounds.Bottom; i++)
            {
                var b = positioned[i].Bounds;
                if (b.Left >= label.Bounds.Right - 1 && Math.Min(b.Bottom, label.Bounds.Bottom) - Math.Max(b.Top, label.Bounds.Top) >= Math.Min(b.Height, label.Bounds.Height) * .5) nearby.Add(positioned[i]);
            }
            Rect value = Rect.Empty; double edge = label.Bounds.Right;
            foreach (var word in nearby.OrderBy(w => w.Bounds.X))
            {
                var b = word.Bounds;
                if (b.Left - edge > Math.Max(b.Height, label.Bounds.Height) * 4) break;
                if (label.Rule.Evidence == SecretEvidence.Prose)
                {
                    string token = word.Text.TrimEnd('.', ',', ';');
                    if (UrlPattern.IsMatch(token) || !ProseToken.IsMatch(token)) break;
                    if (PlausibleProseSecret(token)) AddBounds(found, "credential", b, layout);
                    else if (WrappedSecretPrefix(token)) wrappedPrefixes.Add(b);
                    break;
                }
                value.Union(b); edge = Math.Max(edge, b.Right);
            }
            AddBounds(found, label.Rule.Category, value, layout);
        }
        // A short "Secure!" at a line end is ambiguous on its own. Recover it
        // only beside a password label and a strong continuation on the next
        // row, keeping both pieces as separate covers instead of a large block.
        foreach (var prefix in wrappedPrefixes.Where(ValidBounds))
            if (strongSecrets.Any(s => ValidBounds(s) && s.Top >= prefix.Bottom - 1 && s.Top - prefix.Bottom <= Math.Max(s.Height, prefix.Height) * 3 &&
                s.Left + Math.Max(s.Height, prefix.Height) * 2 < prefix.Left)) AddBounds(found, "credential", prefix, layout);
        if (categories.Contains("credential")) DetectWrappedCredentials(layout, found);
        DetectTableCells(layout, categories, found);
        return Merge(found);
    }

    private sealed record Cell(string Text, Rect Bounds);
    private static readonly Regex WrappedKeyPrefix = Pattern(@"\b[s5]k-(?:[a-z]{1,16}-)?$");
    private static readonly Regex WrappedPasswordPrefix = Pattern(@"(?<![\w.!#$@%&*?+=_-])[a-z][a-z0-9!#$@%&*?+=_-]{7,126}[!#$@&*?]$");
    private static readonly Regex KeyContinuation = Pattern(@"^[a-z0-9_-]+[.,;]?$" );
    private static void DetectWrappedCredentials(OcrLayout layout, List<SensitiveFinding> found)
    {
        var rows = layout.Words.Where(w => ValidBounds(w.Bounds)).GroupBy(w => w.LineIndex)
            .Select(line => line.OrderBy(w => w.Bounds.X).ToArray()).OrderBy(words => words.Min(w => w.Bounds.Top)).ToArray();
        var tops = rows.Select(r => r.Min(w => w.Bounds.Top)).ToArray();
        double maxRowHeight = rows.Length == 0 ? 0 : rows.Max(r => r.Max(w => w.Bounds.Bottom) - r.Min(w => w.Bounds.Top));
        IEnumerable<OcrWordBox[]> NearbyRows(double top, double bottom)
        {
            int lo = 0, hi = tops.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (tops[mid] < top) lo = mid + 1; else hi = mid; }
            for (int i = lo; i < rows.Length && tops[i] <= bottom; i++) yield return rows[i];
        }
        foreach (var words in rows)
        {
            var reading = ReadLine(words, true);
            var prefix = WrappedKeyPrefix.Match(reading.Text);
            bool numericPassword = false;
            if (!prefix.Success)
            {
                prefix = WrappedPasswordPrefix.Match(reading.Text);
                numericPassword = prefix.Success && prefix.Value.Any(char.IsLower) && prefix.Value.Count(char.IsUpper) >= 2 &&
                    prefix.Value[..^1].Any(HardSecretSymbol);
                if (!numericPassword || UrlPattern.Matches(reading.Text).Cast<Match>().Any(u => prefix.Index < u.Index + u.Length && prefix.Index + prefix.Length > u.Index)) continue;
            }
            var start = MatchBounds(reading.Offsets, prefix);
            // A desktop sidebar can have a row between this prefix and its
            // actual continuation. Keep the paragraph's horizontal boundary
            // from preceding text on the same physical row, including separate native lines.
            var leftContext = NearbyRows(start.Top - maxRowHeight, start.Bottom).SelectMany(r => r)
                .Where(w => SameRow(w.Bounds, start) && w.Bounds.Right <= start.Left + 1 && start.Left - w.Bounds.Right <= 1024).ToArray();
            double columnLeft = leftContext.Length > 0 ? leftContext.Min(w => w.Bounds.Left) - start.Height * 2 : 0;
            var localRows = NearbyRows(start.Bottom - 1, start.Bottom + Math.Max(start.Height, maxRowHeight) * 3).Select(r => r.Where(w => w.Bounds.Right > columnLeft).ToArray())
                .Where(r => r.Length > 0).OrderBy(r => r[0].Bounds.Top).ToArray();
            int lo = 0, hi = localRows.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (localRows[mid][0].Bounds.Top < start.Bottom - 1) lo = mid + 1; else hi = mid; }
            if (lo == localRows.Length) continue;
            var nearest = localRows[lo][0].Bounds;
            // Only the immediately following physical row, including its
            // complementary OCR readings, can continue this prefix.
            int end = lo;
            while (end < localRows.Length && end < lo + 32 && localRows[end][0].Bounds.Top < nearest.Bottom - nearest.Height * .5) end++;
            var physical = localRows[lo..end].SelectMany(r => r).Where(w => SameRow(w.Bounds, nearest)).OrderBy(w => w.Bounds.X).ToArray();
            var nextRow = new List<Cell>();
            foreach (var word in physical)
            {
                string piece = word.Text.Replace('ø', '0').Replace('Ø', '0');
                if (nextRow.Count > 0 && Math.Min(nextRow[^1].Bounds.Right, word.Bounds.Right) - word.Bounds.Left >= Math.Min(nextRow[^1].Bounds.Width, word.Bounds.Width) * .5)
                {
                    var previous = nextRow[^1]; var union = previous.Bounds; union.Union(word.Bounds);
                    // Prefer a syntactically valid complementary word. Union
                    // its geometry before terminal punctuation can stop us.
                    bool previousValid = KeyContinuation.IsMatch(previous.Text), currentValid = KeyContinuation.IsMatch(piece);
                    string text = currentValid && (!previousValid || piece.Length > previous.Text.Length) ? piece : previous.Text;
                    nextRow[^1] = new(text, union);
                }
                else nextRow.Add(new(piece, word.Bounds));
            }
            var first = nextRow[0].Bounds;
            if (first.Top - start.Bottom > Math.Max(first.Height, start.Height) * 3 || first.Left + Math.Max(first.Height, start.Height) * 2 >= start.Left) continue;
            var token = new StringBuilder(); Rect bounds = Rect.Empty; double edge = first.Left;
            foreach (var word in nextRow)
            {
                string piece = word.Text;
                if (word.Bounds.Left - edge > Math.Max(2, Math.Min(word.Bounds.Height, first.Height) * .3) || !KeyContinuation.IsMatch(piece)) break;
                token.Append(piece.TrimEnd('.', ',', ';')); bounds.Union(word.Bounds); edge = word.Bounds.Right;
                if (piece.EndsWith('.') || piece.EndsWith(',') || piece.EndsWith(';')) break;
            }
            if (numericPassword)
            {
                if (token.Length is < 4 or > 12 || !token.ToString().All(char.IsDigit)) continue;
            }
            else if (token.Length < 12 || token.ToString().Count(char.IsDigit) < 4) continue;
            AddBounds(found, "credential", start, layout);
            AddBounds(found, "credential", bounds, layout);
        }
    }
    private sealed record TableRow(string Category, Rect Label, Rect Value);
    private static bool SameRow(Rect a, Rect b) => Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) >= Math.Min(a.Height, b.Height) * .5;
    private static void DetectTableCells(OcrLayout layout, IReadOnlyCollection<string> categories, List<SensitiveFinding> found)
    {
        // Windows OCR can put the two columns in separate native lines. Split
        // large gaps into cells, then require a second aligned labelled row.
        // A standalone word "Password" in prose is not sufficient evidence.
        var cells = new List<Cell>();
        foreach (var line in layout.Words.Where(w => ValidBounds(w.Bounds)).GroupBy(w => w.LineIndex))
        {
            var words = line.OrderBy(w => w.Bounds.X).ToArray();
            int first = 0;
            for (int end = 1; end <= words.Length; end++)
            {
                if (end < words.Length && words[end].Bounds.Left - words[end - 1].Bounds.Right <= Math.Max(words[end].Bounds.Height, words[end - 1].Bounds.Height) * 4) continue;
                var part = words[first..end]; var reading = ReadLine(part, false);
                Rect bounds = Rect.Empty; foreach (var word in part) bounds.Union(word.Bounds);
                cells.Add(new(reading.Text.Trim(), bounds)); first = end;
            }
        }
        var positioned = cells.OrderBy(c => c.Bounds.Y).ToArray();
        double maxHeight = positioned.Length == 0 ? 0 : positioned.Max(c => c.Bounds.Height);
        if (categories.Contains("username")) DetectHandleLists(positioned, maxHeight, layout, found);
        var rows = new List<TableRow>();
        foreach (var label in cells)
        {
            var rule = Rules.FirstOrDefault(r => r.CellLabel?.IsMatch(label.Text) == true);
            if (rule == null) continue;
            int lo = 0, hi = positioned.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (positioned[mid].Bounds.Y < label.Bounds.Y - maxHeight) lo = mid + 1; else hi = mid; }
            var right = new List<Cell>();
            for (int i = lo; i < positioned.Length && positioned[i].Bounds.Top < label.Bounds.Bottom; i++)
                if (positioned[i].Bounds.Left > label.Bounds.Right + label.Bounds.Height && SameRow(label.Bounds, positioned[i].Bounds)) right.Add(positioned[i]);
            var value = right.OrderBy(c => c.Bounds.Left).FirstOrDefault();
            if (value == null || value.Bounds.Left - label.Bounds.Right > label.Bounds.Height * 20) continue;
            Rect bounds = value.Bounds;
            int valueLength = 0;
            // A value may span native lines or complementary 2x/3x readings.
            // Follow contiguous boxes, stopping before a distant next column.
            foreach (var other in right.OrderBy(c => c.Bounds.Left))
            {
                if (other.Bounds.Left - bounds.Right > Math.Max(label.Bounds.Height, other.Bounds.Height) * 4) break;
                valueLength += other.Text.Length;
                bounds.Union(other.Bounds);
            }
            if (valueLength < 3) continue;
            rows.Add(new(rule.Category, label.Bounds, bounds));
        }
        var ordered = rows.DistinctBy(r => (r.Category, (int)r.Label.X, (int)r.Label.Y, (int)r.Value.X)).OrderBy(r => r.Label.Y).ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            var row = ordered[i]; if (!categories.Contains(row.Category)) continue;
            // Bound peer work even for pathological overlapping OCR readings.
            bool aligned = ordered.Skip(Math.Max(0, i - 16)).Take(33).Any(other =>
                !SameRow(row.Label, other.Label) && Math.Abs(other.Label.Y - row.Label.Y) <= Math.Max(row.Label.Height, other.Label.Height) * 12 &&
                Math.Abs(other.Label.X - row.Label.X) <= row.Label.Height * 2 && Math.Abs(other.Value.X - row.Value.X) <= row.Label.Height * 2);
            if (aligned) AddBounds(found, row.Category, row.Value, layout);
        }
    }

    private static readonly Regex HandleStartPattern = Pattern(@"^[a-z][a-z0-9_ ]{1,63}$");
    private static readonly Regex HandleValuePattern = Pattern(@"^[a-z][a-z0-9_]{3,63}$");
    private static readonly Regex HandleContinuationPattern = Pattern(@"^[a-z0-9_]{1,64}$");
    private static bool HandleStart(string text) => text.Length <= 64 && HandleStartPattern.IsMatch(text) &&
        (text.Contains('_') || text.Count(char.IsUpper) >= 2);
    private static bool HandleValue(string text)
    {
        string compact = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (!compact.Any(char.IsLower) || !HandleValuePattern.IsMatch(compact)) return false;
        if (Rules.Where(r => r.Category == "credential").Any(r => r.Pattern.Matches(compact).Cast<Match>().Any(m =>
            r.Validate == null || r.Validate(m.Groups["value"].Success ? m.Groups["value"].Value : m.Value)))) return false;
        return compact.Contains('_') || (compact.Count(char.IsUpper) >= 2 && char.IsDigit(compact[^1]));
    }
    private static void DetectHandleLists(Cell[] positioned, double maxHeight, OcrLayout layout, List<SensitiveFinding> found)
    {
        var handles = new List<(Rect Bounds, Rect RightColumn)>();
        foreach (var start in positioned.Where(c => HandleStart(c.Text)))
        {
            if (Rules.Any(r => r.CellLabel?.IsMatch(start.Text) == true)) continue;
            int lo = 0, hi = positioned.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (positioned[mid].Bounds.Y < start.Bounds.Top - maxHeight) lo = mid + 1; else hi = mid; }
            var row = new List<Cell>();
            for (int i = lo; i < positioned.Length && positioned[i].Bounds.Top < start.Bounds.Bottom; i++)
                if (positioned[i].Bounds.Left >= start.Bounds.Left - 1 && SameRow(start.Bounds, positioned[i].Bounds)) row.Add(positioned[i]);
            var text = new StringBuilder(start.Text); Rect bounds = start.Bounds;
            foreach (var next in row.OrderBy(c => c.Bounds.Left).ThenByDescending(c => c.Bounds.Width))
            {
                if (next.Bounds.Right <= bounds.Right) continue;
                if (next.Bounds.Left < bounds.Right - 1)
                {
                    // Complementary OCR suffixes can overlap an already read
                    // name. Extend its geometry without repeating valid text.
                    if (HandleContinuationPattern.IsMatch(next.Text.Replace(" ", "")))
                    {
                        if (!HandleValue(text.ToString()) && text.Length + next.Text.Length < 64) text.Append(' ').Append(next.Text);
                        bounds.Union(next.Bounds);
                    }
                    continue;
                }
                if (next.Bounds.Left - bounds.Right > Math.Max(start.Bounds.Height, next.Bounds.Height) * 1.25 || text.Length + next.Text.Length > 64) break;
                text.Append(' ').Append(next.Text); bounds.Union(next.Bounds);
            }
            if (HandleValue(text.ToString()))
            {
                var right = row.Where(c => c.Bounds.Left - bounds.Right > start.Bounds.Height * 2 && c.Bounds.Left - bounds.Right <= start.Bounds.Height * 20)
                    .OrderBy(c => c.Bounds.Left).FirstOrDefault();
                handles.Add((bounds, right?.Bounds ?? Rect.Empty));
            }
        }
        var candidates = handles.DistinctBy(h => ((int)h.Bounds.X, (int)h.Bounds.Y, (int)h.Bounds.Width)).OrderBy(h => h.Bounds.Y).ToArray();
        var accepted = new List<Rect>();
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            // In a two-column table, OCR-distorted labels can resemble names.
            // Two aligned right-hand cells identify that label column; an
            // isolated unrelated cell does not suppress a real handle list.
            bool labelColumn = !candidate.RightColumn.IsEmpty && candidates.Skip(Math.Max(0, i - 16)).Take(33).Any(other =>
                !other.RightColumn.IsEmpty && !SameRow(candidate.Bounds, other.Bounds) &&
                Math.Abs(candidate.Bounds.Y - other.Bounds.Y) <= Math.Max(candidate.Bounds.Height, other.Bounds.Height) * 12 &&
                Math.Abs(candidate.Bounds.X - other.Bounds.X) <= candidate.Bounds.Height * 2 &&
                Math.Abs(candidate.RightColumn.X - other.RightColumn.X) <= candidate.Bounds.Height * 2);
            if (!labelColumn) accepted.Add(candidate.Bounds);
        }
        var ordered = accepted.ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            var handle = ordered[i];
            bool list = ordered.Skip(Math.Max(0, i - 16)).Take(33).Any(other => !SameRow(handle, other) &&
                Math.Abs(handle.Y - other.Y) <= Math.Max(handle.Height, other.Height) * 12 &&
                Math.Abs(handle.X - other.X) <= Math.Max(handle.Height, other.Height) * 2);
            if (list) AddBounds(found, "username", handle, layout);
        }
    }

    private static bool ValidBounds(Rect b) => !b.IsEmpty && b.Width > 0 && b.Height > 0 && double.IsFinite(b.X) && double.IsFinite(b.Y) && double.IsFinite(b.Right) && double.IsFinite(b.Bottom);
    private static (string Text, List<(int Start, int End, Rect Bounds)> Offsets) ReadLine(OcrWordBox[] words, bool joinFragments)
    {
        var offsets = new List<(int Start, int End, Rect Bounds)>(); var builder = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];
            // Keep spaced arithmetic/bitwise/conditional operators separate.
            bool detachedSymbol = i > 0 && (word.Text.Length == 1 && word.Text[0] is '!' or '#' or '$' or '@' ||
                words[i - 1].Text.Length == 1 && words[i - 1].Text[0] is '!' or '#' or '$' or '@');
            if (i > 0 && (!joinFragments || word.Bounds.X - words[i - 1].Bounds.Right > Math.Max(2, Math.Min(word.Bounds.Height, words[i - 1].Bounds.Height) * (detachedSymbol ? .75 : .3)))) builder.Append(' ');
            int start = builder.Length; builder.Append(word.Text); offsets.Add((start, builder.Length, word.Bounds));
        }
        return (builder.ToString(), offsets);
    }
    private static Rect MatchBounds(List<(int Start, int End, Rect Bounds)> offsets, Group value)
    {
        Rect bounds = Rect.Empty;
        foreach (var word in offsets.Where(w => w.Start < value.Index + value.Length && w.End > value.Index && ValidBounds(w.Bounds))) bounds.Union(word.Bounds);
        return bounds;
    }
    private static void AddBounds(List<SensitiveFinding> found, string category, Rect bounds, OcrLayout layout)
    {
        if (!ValidBounds(bounds)) return;
        int left = (int)Math.Clamp(Math.Floor(bounds.Left) - 3, 0, layout.PixelWidth), top = (int)Math.Clamp(Math.Floor(bounds.Top) - 3, 0, layout.PixelHeight);
        int right = (int)Math.Clamp(Math.Ceiling(bounds.Right) + 3, 0, layout.PixelWidth), bottom = (int)Math.Clamp(Math.Ceiling(bounds.Bottom) + 3, 0, layout.PixelHeight);
        if (right > left && bottom > top) found.Add(new(Guid.NewGuid(), [category], new(left, top, right - left, bottom - top), "••••"));
    }

    internal static IReadOnlyList<SensitiveFinding> Merge(IEnumerable<SensitiveFinding> findings)
    {
        using var timing = PipelineMetrics.Measure("privacy.merge");
        var result = new List<SensitiveFinding>();
        foreach (var finding in findings)
        {
            var bounds = new Rect(finding.Bounds.X, finding.Bounds.Y, finding.Bounds.Width, finding.Bounds.Height);
            var categories = new HashSet<string>(finding.Categories);
            for (int i = result.Count - 1; i >= 0; i--)
            {
                var previous = result[i]; var rect = new Rect(previous.Bounds.X, previous.Bounds.Y, previous.Bounds.Width, previous.Bounds.Height);
                if (!bounds.IntersectsWith(rect) || Math.Min(bounds.Bottom, rect.Bottom) - Math.Max(bounds.Top, rect.Top) < Math.Min(bounds.Height, rect.Height) * .5) continue;
                bounds.Union(rect); categories.UnionWith(previous.Categories); result.RemoveAt(i); i = result.Count;
            }
            result.Add(finding with { Bounds = new((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height), Categories = categories.Order().ToArray() });
        }
        return result.OrderBy(f => f.Bounds.Y).ThenBy(f => f.Bounds.X).ToArray();
    }
}
