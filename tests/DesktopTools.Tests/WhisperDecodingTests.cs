using System.IO;
using System.Text;
using DesktopTools.Core;

/// <summary>Whisper's pure parts: generation config, log-mel features, level gate, timestamp rules, segments and seeking, vocabulary decoding.</summary>
internal static class WhisperDecodingTests
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        const string json = """
            { "decoder_start_token_id": 50258, "eos_token_id": 50257, "no_timestamps_token_id": 50363,
              "task_to_id": { "transcribe": 50359, "translate": 50358 }, "suppress_tokens": [1, 2], "begin_suppress_tokens": [220, 50257],
              "lang_to_id": { "<|ru|>": 50263, "<|en|>": 50259 } }
            """;
        var config = WhisperDecoding.ParseConfig(json);
        Check(config.StartOfTranscript == 50258 && config.EndOfText == 50257 && config.Transcribe == 50359 && config.NoTimestamps == 50363, "Special tokens");
        Check(config.NoSpeech == 50362 && config.TimestampBegin == 50364, "Derived tokens");
        Check(config.Suppress.SequenceEqual([1, 2]) && config.BeginSuppress.SequenceEqual([220, 50257]), "Suppressed tokens");
        Check(config.Languages["en"] == 50259 && config.Languages["ru"] == 50263 && WhisperDecoding.LanguageCodes(config).SequenceEqual(["en", "ru"]), "Languages");
        try { WhisperDecoding.ParseConfig("{}"); throw new Exception("Empty config accepted"); } catch (InvalidDataException) { }

        // Log-mel: silence is flat; a 1 kHz tone peaks in the mel band around 1 kHz (bin 26 of 80 for Slaney mel up to 8 kHz).
        var silence = WhisperDecoding.LogMel(new float[WhisperDecoding.WindowSamples]);
        Check(silence.Length == 80 * 3000 && silence.All(v => Math.Abs(v - -1.5f) < 1e-5), "Silence log-mel");
        var tone = new float[16000 * 2];
        for (int i = 0; i < tone.Length; i++) tone[i] = (float)(.5 * Math.Sin(2 * Math.PI * 1000 * i / 16000.0));
        var mel = WhisperDecoding.LogMel(tone);
        int Peak(float[] values) { int best = 0; double bestMean = double.MinValue; for (int m = 0; m < 80; m++) { double mean = 0; for (int f = 0; f < 200; f++) mean += values[m * 3000 + f]; if (mean > bestMean) { bestMean = mean; best = m; } } return best; }
        int peak = Peak(mel);
        Check(peak is >= 25 and <= 28, "1 kHz peak bin " + peak);
        Check(mel.All(float.IsFinite) && mel.Min() >= mel.Max() - 2.0001f, "Dynamic range clamp of 8 log units (2 after scaling)");

        var square = Enumerable.Range(0, 1600).Select(i => i % 2 == 0 ? 1f : -1f).ToArray();
        Check(Math.Abs(WhisperDecoding.RmsDbfs(square)) < .01, "Full-scale square is 0 dBFS");
        var quiet = Enumerable.Range(0, 1600).Select(i => (i % 2 == 0 ? 1f : -1f) * 1e-4f).ToArray();
        Check(WhisperDecoding.RmsDbfs(quiet) < WhisperDecoding.SilenceDbfs, "Quiet is below the gate");
        Check(WhisperDecoding.RmsDbfs(ReadOnlySpan<float>.Empty) < WhisperDecoding.SilenceDbfs, "Empty is silent");

        // Leading silence is skipped in 100 ms steps, keeping 0.2 s before the first sound; less than 1 s is kept as it is.
        float[] Lead(double silentSeconds, int total = WhisperDecoding.WindowSamples)
        {
            var audio = new float[total];
            for (int i = (int)(silentSeconds * WhisperDecoding.SampleRate); i < total; i++) audio[i] = (float)(.3 * Math.Sin(i * .1));
            return audio;
        }
        Check(WhisperDecoding.LeadingSilenceSamples(Lead(2.05)) == 1.8 * WhisperDecoding.SampleRate, "2.05 s of silence skips 1.8 s: " + WhisperDecoding.LeadingSilenceSamples(Lead(2.05)));
        Check(WhisperDecoding.LeadingSilenceSamples(Lead(.9)) == 0, "Under a second of silence is kept");
        Check(WhisperDecoding.LeadingSilenceSamples(Lead(0)) == 0, "No silence");
        Check(WhisperDecoding.LeadingSilenceSamples(new float[WhisperDecoding.WindowSamples]) == 0, "An all-silent window is left to the window gate");
        Check(WhisperDecoding.LeadingSilenceSamples(Lead(20, 16000 * 21)) == 19.8 * WhisperDecoding.SampleRate, "A short final window");
        Check(WhisperDecoding.IsNoSpeech(.61f) && !WhisperDecoding.IsNoSpeech(.6f), "No-speech threshold is above 0.6");

        // Timestamp rules. Vocabulary: 51865 entries; timestamps from 50364.
        const int vocab = 51865; int tb = config.TimestampBegin;
        float[] Logits(float text = 5, float stamps = 0) { var l = new float[vocab]; for (int i = 0; i < vocab; i++) l[i] = i < config.EndOfText ? text : i >= tb ? stamps : 0; return l; }
        bool Masked(float[] l, int id) => float.IsNegativeInfinity(l[id]);
        int Ts(double seconds) => tb + (int)Math.Round(seconds / .02);

        var first = Logits(); WhisperDecoding.ApplyRules(first, [], firstStep: true, config);
        Check(Masked(first, 100) && Masked(first, config.EndOfText) && Masked(first, 1) && Masked(first, config.NoTimestamps), "First token must be a timestamp");
        Check(!Masked(first, Ts(0)) && !Masked(first, Ts(1.0)) && Masked(first, Ts(1.02)), "First timestamp at most 1 s");

        var afterOpen = Logits(); WhisperDecoding.ApplyRules(afterOpen, [Ts(1.0)], false, config);
        Check(Masked(afterOpen, Ts(1.0)) && Masked(afterOpen, Ts(5)) && !Masked(afterOpen, 100), "After an opening timestamp comes text");

        var afterText = Logits(text: 20); WhisperDecoding.ApplyRules(afterText, [Ts(1.0), 100], false, config);
        Check(Masked(afterText, Ts(1.0)) && !Masked(afterText, Ts(1.02)) && !Masked(afterText, 100) && Masked(afterText, 1)
            && Masked(afterText, config.StartOfTranscript) && Masked(afterText, 50259) && Masked(afterText, config.NoSpeech), "Timestamps never go back; suppressed tokens stay suppressed");

        var afterClose = Logits(text: 20, stamps: -10); WhisperDecoding.ApplyRules(afterClose, [Ts(1.0), 100, Ts(1.5)], false, config);
        Check(Masked(afterClose, 100) && !Masked(afterClose, config.EndOfText) && !Masked(afterClose, Ts(1.5)) && Masked(afterClose, Ts(1.48)), "A closing timestamp is followed by another timestamp or the end");

        var afterPair = Logits(); WhisperDecoding.ApplyRules(afterPair, [Ts(1.0), 100, Ts(1.5), Ts(1.5)], false, config);
        Check(Masked(afterPair, Ts(1.5)) && Masked(afterPair, Ts(2)) && !Masked(afterPair, 100), "After a pair comes text");

        var likelyStamp = Logits(text: -10, stamps: 0); WhisperDecoding.ApplyRules(likelyStamp, [Ts(1.0), 100], false, config);
        Check(Masked(likelyStamp, 100) && !Masked(likelyStamp, Ts(2)), "Text is dropped when timestamps together are more likely");

        // Language detection takes the most likely language token.
        var lang = new float[vocab]; lang[50263] = 3; lang[50259] = 2; lang[100] = 9;
        Check(WhisperDecoding.BestLanguage(lang, config) == "ru", "Best language");

        // Segments and seeking.
        var words = new Dictionary<int, string> { [1] = " Hi", [2] = " there", [3] = " a", [4] = " b", [5] = " x" };
        string Decode(IReadOnlyList<int> ids) => string.Concat(ids.Select(id => words[id]));
        static TranscriptLine L(double s, double e, string t) => new(TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e), t);
        const int full = WhisperDecoding.WindowSamples;

        var single = WhisperDecoding.Segments([Ts(0), 1, Ts(1.5), Ts(1.5), 2, Ts(3)], 30, full, config, Decode);
        Check(single.Lines.SequenceEqual([L(30, 31.5, "Hi"), L(31.5, 33, "there")]) && single.SeekSamples == full, "Single timestamp ending moves a whole window");

        var cut = WhisperDecoding.Segments([Ts(0), 1, Ts(1.5), Ts(1.5), 2], 30, full, config, Decode);
        Check(cut.Lines.SequenceEqual([L(30, 31.5, "Hi")]) && cut.SeekSamples == 24000, "Unfinished text is decoded again from the last timestamp");

        var pair = WhisperDecoding.Segments([Ts(0), 3, Ts(2), Ts(2), 4, Ts(4), Ts(4)], 0, full, config, Decode);
        Check(pair.Lines.SequenceEqual([L(0, 2, "a"), L(2, 4, "b")]) && pair.SeekSamples == 64000, "Pair ending seeks to it");

        var open = WhisperDecoding.Segments([Ts(0), 5], 30, 160000, config, Decode);
        Check(open.Lines.SequenceEqual([L(30, 40, "x")]) && open.SeekSamples == 160000, "Text with no timestamp after it runs to the window end");

        var beyond = WhisperDecoding.Segments([Ts(0), 5, Ts(12)], 0, 160000, config, Decode);
        Check(beyond.Lines.SequenceEqual([L(0, 10, "x")]), "Lines end at the audio's end");

        var none = WhisperDecoding.Segments([], 0, full, config, Decode);
        Check(none.Lines.Count == 0 && none.SeekSamples == full, "Nothing decoded moves a whole window");

        var blank = WhisperDecoding.Segments([Ts(0), Ts(1)], 0, full, config, Decode);
        Check(blank.Lines.Count == 0 && blank.SeekSamples == full, "Empty segments are dropped");

        // Vocabulary: GPT-2 byte-level symbols back to UTF-8.
        var vocabulary = WhisperVocabulary.Load(new MemoryStream(Encoding.UTF8.GetBytes("""{"Ġhello":0,"Ġ":1,"Ð":2,"µ":3}""")));
        Check(vocabulary.Decode([0]) == " hello", "Space symbol");
        Check(vocabulary.Decode([2, 3]) == "е", "Two-byte Cyrillic letter");
        Check(vocabulary.Decode([0, 50257, 50364, 9999]) == " hello", "Special, timestamp and unknown ids are skipped");
        Check(vocabulary.Decode([2]) == "�", "A split character becomes a replacement mark");
    }
}
