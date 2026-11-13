using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DesktopTools.Core;

/// <summary>Token ids and rules read from a Whisper pack's generation_config.json.</summary>
public sealed record WhisperConfig(int StartOfTranscript, int EndOfText, int Transcribe, int NoTimestamps, int[] Suppress, int[] BeginSuppress,
    IReadOnlyDictionary<string, int> Languages)
{
    /// <summary>The "no speech" token sits just before "no timestamps" in every multilingual Whisper vocabulary.</summary>
    public int NoSpeech => NoTimestamps - 1;
    /// <summary>The first timestamp token, &lt;|0.00|&gt;; each next id adds 0.02 s.</summary>
    public int TimestampBegin => NoTimestamps + 1;
}

/// <summary>
/// The pure parts of Whisper transcription: features, the timestamp rules applied to each step's logits, turning tokens into timed lines,
/// and where the next 30-second window starts. Values match the feasibility checks recorded in docs/ai-models.md (Task 5).
/// </summary>
public static partial class WhisperDecoding
{
    public const int SampleRate = 16000, WindowSamples = 480000, Frames = 3000, MelBins = 80, MaxTokensPerWindow = 224;
    /// <summary>Windows with a no-speech probability above this are dropped: it caught silence and keyboard clicks in the checks.</summary>
    public const float NoSpeechThreshold = .6f;
    /// <summary>Windows quieter than this are not run through the model at all.</summary>
    public const double SilenceDbfs = -50;
    /// <summary>Seconds per timestamp token, and samples per timestamp step.</summary>
    public const double TimestampSeconds = .02;
    public const int SamplesPerTimestamp = 320;
    /// <summary>The first timestamp may be at most 1.0 s into the window (50 steps), as in OpenAI's decoder.</summary>
    public const int MaxInitialTimestampIndex = 50;
    private const int FftSize = 400, Hop = 160;

    public static WhisperConfig ParseConfig(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            int Int(string name) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out int number) ? number : throw new InvalidDataException($"The speech model settings are missing {name}.");
            int[] Ints(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.GetInt32()).ToArray() : [];
            if (!root.TryGetProperty("task_to_id", out var tasks) || !tasks.TryGetProperty("transcribe", out var transcribe))
                throw new InvalidDataException("The speech model settings are missing task_to_id.");
            if (!root.TryGetProperty("lang_to_id", out var languageMap) || languageMap.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The speech model settings are missing lang_to_id.");
            var languages = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var language in languageMap.EnumerateObject())
            {
                string code = language.Name.Trim('<', '>', '|');
                if (code.Length > 0) languages[code] = language.Value.GetInt32();
            }
            if (languages.Count == 0) throw new InvalidDataException("The speech model settings list no languages.");
            return new WhisperConfig(Int("decoder_start_token_id"), Int("eos_token_id"), transcribe.GetInt32(), Int("no_timestamps_token_id"),
                Ints("suppress_tokens"), Ints("begin_suppress_tokens"), languages);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The speech model settings are not valid: " + e.Message, e);
        }
    }

    public static IReadOnlyList<string> LanguageCodes(WhisperConfig config) => config.Languages.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray();

    public static string BestLanguage(ReadOnlySpan<float> logits, WhisperConfig config)
    {
        string best = config.Languages.Keys.First(); float bestValue = float.NegativeInfinity;
        foreach (var (code, id) in config.Languages)
            if (id < logits.Length && logits[id] > bestValue) { bestValue = logits[id]; best = code; }
        return best;
    }

    public static bool IsNoSpeech(float noSpeechProbability) => noSpeechProbability > NoSpeechThreshold;

    public static double RmsDbfs(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return double.NegativeInfinity;
        double sum = 0;
        foreach (float value in samples) sum += (double)value * value;
        return 20 * Math.Log10(Math.Sqrt(sum / samples.Length) + 1e-12);
    }

    /// <summary>
    /// Samples of silence (below <see cref="SilenceDbfs"/>, in 100 ms steps) to skip before a window is run, keeping 0.2 s before the first
    /// sound. Whisper allows the first timestamp of a window only within its first second, so a window that opened on a long pause would
    /// time its first line too early. Zero when the pause is under a second or the window has no sound at all (the window gate handles that).
    /// </summary>
    public static int LeadingSilenceSamples(ReadOnlySpan<float> samples)
    {
        const int step = SampleRate / 10, keep = 2, minimum = 10;
        for (int frame = 0; frame * step < samples.Length; frame++)
        {
            var part = samples.Slice(frame * step, Math.Min(step, samples.Length - frame * step));
            if (RmsDbfs(part) >= SilenceDbfs) return frame < minimum ? 0 : (frame - keep) * step;
        }
        return 0;
    }

    private static readonly Lazy<(float[] Filters, double[] Window, double[] Cos, double[] Sin)> tables = new(BuildTables);
    private const int Bins = FftSize / 2 + 1;

    /// <summary>
    /// 80 × 3000 log-mel features of up to 30 s of 16 kHz audio, zero padded: periodic Hann, reflect padding, Slaney mel, log10, a floor
    /// 8 below the maximum, then (x + 4) / 4. Matches the transformers feature extractor to 3e-5 (docs/ai-models.md).
    /// </summary>
    public static float[] LogMel(ReadOnlySpan<float> audio)
    {
        var (filters, window, cos, sin) = tables.Value;
        var x = new float[WindowSamples];
        audio[..Math.Min(audio.Length, WindowSamples)].CopyTo(x);
        var padded = new float[WindowSamples + FftSize];
        for (int i = 0; i < padded.Length; i++)
        {
            int j = i - FftSize / 2;
            if (j < 0) j = -j; else if (j >= WindowSamples) j = 2 * (WindowSamples - 1) - j;
            padded[i] = x[j];
        }
        var mel = new float[MelBins * Frames];
        Parallel.For(0, Frames, frame =>
        {
            Span<double> samples = stackalloc double[FftSize];
            Span<double> power = stackalloc double[Bins];
            for (int i = 0; i < FftSize; i++) samples[i] = padded[frame * Hop + i] * window[i];
            for (int k = 0; k < Bins; k++)
            {
                double re = 0, im = 0; int offset = k * FftSize;
                for (int i = 0; i < FftSize; i++) { re += samples[i] * cos[offset + i]; im -= samples[i] * sin[offset + i]; }
                power[k] = re * re + im * im;
            }
            for (int m = 0; m < MelBins; m++)
            {
                double sum = 0; int offset = m * Bins;
                for (int k = 0; k < Bins; k++) sum += filters[offset + k] * power[k];
                mel[m * Frames + frame] = (float)Math.Log10(Math.Max(sum, 1e-10));
            }
        });
        float max = float.NegativeInfinity;
        foreach (float value in mel) if (value > max) max = value;
        for (int i = 0; i < mel.Length; i++) mel[i] = (Math.Max(mel[i], max - 8f) + 4f) / 4f;
        return mel;
    }

    private static (float[], double[], double[], double[]) BuildTables()
    {
        // librosa.filters.mel(sr=16000, n_fft=400, n_mels=80, htk=False, norm="slaney")
        const double linearStep = 200.0 / 3, logStartHz = 1000, logStep = 0.06875177742094912;
        const double logStartMel = logStartHz / linearStep;
        static double HzToMel(double hz) => hz >= logStartHz ? logStartMel + Math.Log(hz / logStartHz) / logStep : hz / linearStep;
        static double MelToHz(double mel) => mel >= logStartMel ? logStartHz * Math.Exp(logStep * (mel - logStartMel)) : linearStep * mel;
        var points = new double[MelBins + 2];
        double low = HzToMel(0), high = HzToMel(SampleRate / 2);
        for (int i = 0; i < points.Length; i++) points[i] = MelToHz(low + (high - low) * i / (MelBins + 1));
        var filters = new float[MelBins * Bins];
        for (int m = 0; m < MelBins; m++)
        {
            double left = points[m], centre = points[m + 1], right = points[m + 2], norm = 2.0 / (right - left);
            for (int k = 0; k < Bins; k++)
            {
                double hz = k * (double)SampleRate / FftSize;
                double weight = Math.Max(0, Math.Min((hz - left) / (centre - left), (right - hz) / (right - centre)));
                filters[m * Bins + k] = (float)(weight * norm);
            }
        }
        var window = new double[FftSize];
        for (int i = 0; i < FftSize; i++) window[i] = .5 - .5 * Math.Cos(2 * Math.PI * i / FftSize);
        var cos = new double[Bins * FftSize]; var sin = new double[Bins * FftSize];
        for (int k = 0; k < Bins; k++)
            for (int n = 0; n < FftSize; n++) { double angle = 2 * Math.PI * k * n / FftSize; cos[k * FftSize + n] = Math.Cos(angle); sin[k * FftSize + n] = Math.Sin(angle); }
        return (filters, window, cos, sin);
    }

    /// <summary>
    /// Masks tokens that may not come next, in place: suppressed and special tokens, the timestamp pairing and ordering rules, the 1 s limit
    /// for the first timestamp, and text when all timestamps together are more likely than the best text token. <paramref name="tokens"/>
    /// are the tokens chosen so far in this window, without the prompt.
    /// </summary>
    public static void ApplyRules(Span<float> logits, IReadOnlyList<int> tokens, bool firstStep, WhisperConfig config)
    {
        int timestampBegin = config.TimestampBegin, end = config.EndOfText;
        foreach (int id in config.Suppress) if (id >= 0 && id < logits.Length) logits[id] = float.NegativeInfinity;
        if (firstStep) foreach (int id in config.BeginSuppress) if (id >= 0 && id < logits.Length) logits[id] = float.NegativeInfinity;
        // Language, task and other control tokens are never output.
        Mask(logits, end + 1, timestampBegin);

        bool lastWasTimestamp = tokens.Count >= 1 && tokens[^1] >= timestampBegin;
        bool penultimateWasTimestamp = tokens.Count < 2 || tokens[^2] >= timestampBegin;
        if (lastWasTimestamp)
        {
            if (penultimateWasTimestamp) Mask(logits, timestampBegin, logits.Length);
            else Mask(logits, 0, end);
        }
        int lastTimestamp = -1;
        for (int i = tokens.Count - 1; i >= 0; i--) if (tokens[i] >= timestampBegin) { lastTimestamp = tokens[i]; break; }
        if (lastTimestamp >= 0)
            Mask(logits, timestampBegin, lastWasTimestamp && !penultimateWasTimestamp ? lastTimestamp : lastTimestamp + 1);
        if (tokens.Count == 0)
        {
            Mask(logits, 0, timestampBegin);
            Mask(logits, timestampBegin + MaxInitialTimestampIndex + 1, logits.Length);
        }

        // Timestamps win when their total probability beats the most likely text token.
        float maxAll = float.NegativeInfinity;
        foreach (float value in logits) if (value > maxAll) maxAll = value;
        if (float.IsNegativeInfinity(maxAll)) return;
        double stampSum = 0; float maxText = float.NegativeInfinity;
        for (int i = 0; i < logits.Length; i++)
        {
            if (i >= timestampBegin) { if (!float.IsNegativeInfinity(logits[i])) stampSum += Math.Exp(logits[i] - maxAll); }
            else if (logits[i] > maxText) maxText = logits[i];
        }
        if (stampSum > 0 && Math.Log(stampSum) + maxAll > maxText) Mask(logits, 0, timestampBegin);
    }

    private static void Mask(Span<float> logits, int from, int to) { for (int i = Math.Max(0, from); i < Math.Min(to, logits.Length); i++) logits[i] = float.NegativeInfinity; }

    /// <summary>
    /// Turns one window's tokens into lines in file time and says how many samples to move on. A window that ends on a single timestamp, or
    /// whose text never got a closing timestamp, moves a whole window; otherwise unfinished text after the last closed line is dropped and the
    /// next window starts at that line's end, so a word cut at the window edge is heard again.
    /// </summary>
    public static (IReadOnlyList<TranscriptLine> Lines, int SeekSamples) Segments(IReadOnlyList<int> tokens, double windowOffsetSeconds, int windowSamples,
        WhisperConfig config, Func<IReadOnlyList<int>, string> decode)
    {
        int timestampBegin = config.TimestampBegin;
        double windowSeconds = windowSamples / (double)SampleRate;
        var lines = new List<TranscriptLine>();
        var text = new List<int>();
        double? start = null; int lastClosedIndex = -1; double lastClosed = 0;
        void Add(double from, double to, IReadOnlyList<int> ids)
        {
            from = Math.Min(from, windowSeconds); to = Math.Min(to, windowSeconds);
            string value = decode(ids).Trim();
            if (value.Length == 0 || to <= from) return;
            lines.Add(new TranscriptLine(Time(windowOffsetSeconds + from), Time(windowOffsetSeconds + to), value));
        }
        for (int i = 0; i < tokens.Count; i++)
        {
            int token = tokens[i];
            if (token < timestampBegin) { if (token < config.EndOfText) text.Add(token); continue; }
            double seconds = (token - timestampBegin) * TimestampSeconds;
            if (start == null || text.Count == 0) { start = seconds; continue; }
            Add(start.Value, seconds, text.ToArray());
            text.Clear(); start = null; lastClosedIndex = i; lastClosed = seconds;
        }
        bool singleEnding = tokens.Count >= 2 && tokens[^1] >= timestampBegin && tokens[^2] < timestampBegin;
        if (text.Count > 0 && lastClosedIndex < 0)
        {
            Add(start ?? 0, windowSeconds, text);
            return (lines, windowSamples);
        }
        if (singleEnding || lastClosedIndex < 0 || lastClosed <= 0) return (lines, windowSamples);
        int seek = (int)Math.Round(lastClosed / TimestampSeconds) * SamplesPerTimestamp;
        return (lines, Math.Clamp(seek, SamplesPerTimestamp, windowSamples));
    }

    private static TimeSpan Time(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
}

/// <summary>Whisper's byte-level vocabulary (vocab.json) turned back into text. Special and timestamp ids are skipped.</summary>
public sealed class WhisperVocabulary
{
    private readonly Dictionary<int, byte[]> tokens;
    private const int FirstSpecialToken = 50257;
    private WhisperVocabulary(Dictionary<int, byte[]> tokens) => this.tokens = tokens;

    public static WhisperVocabulary Load(Stream vocabJson)
    {
        Dictionary<string, int>? map;
        try { map = JsonSerializer.Deserialize<Dictionary<string, int>>(vocabJson); }
        catch (JsonException e) { throw new InvalidDataException("The speech model vocabulary is not valid.", e); }
        if (map == null || map.Count == 0) throw new InvalidDataException("The speech model vocabulary is empty.");
        var unicodeToByte = UnicodeToByte();
        var tokens = new Dictionary<int, byte[]>(map.Count);
        foreach (var (symbol, id) in map)
        {
            if (id >= FirstSpecialToken) continue;
            var bytes = new byte[symbol.Length]; bool valid = true;
            for (int i = 0; i < symbol.Length; i++) { if (unicodeToByte.TryGetValue(symbol[i], out byte b)) bytes[i] = b; else { valid = false; break; } }
            if (valid) tokens[id] = bytes;
        }
        return new WhisperVocabulary(tokens);
    }

    public string Decode(IEnumerable<int> ids)
    {
        var bytes = new List<byte>();
        foreach (int id in ids) if (id < FirstSpecialToken && tokens.TryGetValue(id, out var value)) bytes.AddRange(value);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>GPT-2's bytes_to_unicode, inverted: printable bytes map to themselves, the rest to code points from 256.</summary>
    private static Dictionary<char, byte> UnicodeToByte()
    {
        var map = new Dictionary<char, byte>(256);
        int next = 0;
        for (int b = 0; b < 256; b++)
        {
            bool printable = b is >= '!' and <= '~' or >= 0xA1 and <= 0xAC or >= 0xAE and <= 0xFF;
            map[(char)(printable ? b : 256 + next++)] = (byte)b;
        }
        return map;
    }
}
