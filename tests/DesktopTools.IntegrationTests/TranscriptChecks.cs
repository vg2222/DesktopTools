using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;

/// <summary>
/// Recording transcripts (spec section 6): audio decoding with Media Foundation, the transcript store and caption files, the editor's
/// Transcript panel, and, when a developer has the checksum-verified Whisper files (TRANSCRIPT_MODELS, default E:\DesktopTools-ai-spikes\models),
/// transcribing generated speech. No network.
/// </summary>
internal static partial class TranscriptChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    internal static async Task RunAsync(Action<string> report)
    {
        string directory = Path.Combine(Path.GetTempPath(), "DesktopTools-transcripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await DecoderAsync(directory, report);
            Store(directory);
            report("PASS transcript store: round trip, stale after the video changes, caption file ownership");
            await PanelAsync(directory, report);
            await LocalesAsync(directory, report);
            await RealModelsAsync(directory, report);
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    /// <summary>A 16-bit PCM WAV file; <paramref name="sample"/> gives each frame's value per channel in [-1, 1].</summary>
    internal static void WriteWav(string path, int rate, int channels, double seconds, Func<int, int, double> sample)
    {
        int frames = (int)Math.Round(rate * seconds);
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
        int dataBytes = frames * channels * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataBytes); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataBytes);
        for (int i = 0; i < frames; i++) for (int c = 0; c < channels; c++) writer.Write((short)Math.Clamp(Math.Round(sample(i, c) * 32767), -32768, 32767));
    }

    private static async Task DecoderAsync(string directory, Action<string> report)
    {
        string wav = Path.Combine(directory, "tone.wav");
        WriteWav(wav, 48000, 2, 2.5, (i, _) => .5 * Math.Sin(2 * Math.PI * 440 * i / 48000.0));
        var samples = new List<float>();
        await Task.Run(() =>
        {
            using var decoder = AudioDecoder.Open(wav);
            Check(Math.Abs(decoder.Duration.TotalSeconds - 2.5) < .02, "Decoder duration " + decoder.Duration);
            var buffer = new float[7001]; int read;
            while ((read = decoder.Read(buffer)) > 0) samples.AddRange(buffer.Take(read));
            Check(decoder.Read(buffer) == 0, "Read after the end returns nothing");
        });
        Check(Math.Abs(samples.Count - 40000) <= 160, "16 kHz sample count " + samples.Count);
        double rms = 20 * Math.Log10(Math.Sqrt(samples.Skip(800).Take(38000).Average(v => (double)v * v)));
        Check(Math.Abs(rms - -9.03) < 1, "Tone level " + rms);
        report($"MEASURE transcript decoder: 2.5 s 48 kHz stereo WAV -> {samples.Count} samples at 16 kHz mono, {rms:0.00} dBFS");

        string silentVideo = await VideoEditingChecks.FixtureAsync(Path.Combine(directory, "silent"));
        try { using var _ = AudioDecoder.Open(silentVideo); throw new Exception("A video with no audio track opened"); }
        catch (NoAudioTrackException) { }
        try { using var _ = AudioDecoder.Open(Path.Combine(directory, "missing.mp4")); throw new Exception("A missing file opened"); }
        catch (FileNotFoundException) { }
        string notMedia = Path.Combine(directory, "notes.mp4"); File.WriteAllText(notMedia, "not a video");
        try { using var _ = AudioDecoder.Open(notMedia); throw new Exception("A text file opened as media"); }
        catch (NoAudioTrackException) { }
    }
    private static void Store(string directory)
    {
        string video = Path.Combine(directory, "store", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(video)!);
        File.WriteAllText(video, "stand-in for a video");
        string data = Path.Combine(directory, "store-data");
        var store = new TranscriptStore(data);
        TranscriptLine[] lines =
        [
            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), "The quick brown fox"),
            new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "jumps over the lazy dog."),
        ];
        Check(store.Load(video) == null, "A transcript was found before any was saved");
        store.Save(video, new TranscriptDocument(lines, "en", "speech-fast", null));
        var loaded = store.Load(video);
        Check(loaded != null && loaded.Lines.SequenceEqual(lines) && loaded.Language == "en" && loaded.Model == "speech-fast", "Transcript round trip");

        // A caption file is written next to the video and its hash is recorded; writing again replaces it without asking.
        int asked = 0;
        bool Ask(string path) { asked++; return false; }
        string caption = Path.Combine(Path.GetDirectoryName(video)!, "clip.srt");
        string? written = store.WriteCaptionFile(video, lines, Ask);
        Check(written == caption && File.Exists(caption) && asked == 0, "Caption file not written next to the video");
        Check(CaptionFiles.ParseSrt(File.ReadAllText(caption)).SequenceEqual(lines), "Caption file content");
        Check(store.Load(video)?.CaptionSha256 is { Length: 64 }, "Caption hash not recorded");
        Check(store.WriteCaptionFile(video, lines.Take(1).ToArray(), Ask) == caption && asked == 0, "Own caption file not replaced silently");

        // After the user edits the file, they are asked; declining leaves it byte for byte.
        File.AppendAllText(caption, "\r\n3\r\n00:00:05,000 --> 00:00:06,000\r\nMy own line\r\n");
        byte[] edited = File.ReadAllBytes(caption);
        Check(store.WriteCaptionFile(video, lines, Ask) == null && asked == 1, "A user's caption file was replaced without asking");
        Check(File.ReadAllBytes(caption).SequenceEqual(edited), "Declining changed the user's caption file");
        Check(store.WriteCaptionFile(video, lines, _ => true) == caption && CaptionFiles.ParseSrt(File.ReadAllText(caption)).Count == 2, "Accepting did not replace the file");

        // A caption file that was there before transcription is the user's.
        string other = Path.Combine(Path.GetDirectoryName(video)!, "talk.mp4");
        File.WriteAllText(other, "another stand-in");
        File.WriteAllText(Path.ChangeExtension(other, ".srt"), "1\r\n00:00:00,000 --> 00:00:01,000\r\nWritten by hand\r\n");
        Check(store.WriteCaptionFile(other, lines, Ask) == null && asked == 2, "A caption file present before was replaced without asking");

        // The video is replaced or edited afterwards: its stored transcript is not shown for the new file.
        File.SetLastWriteTimeUtc(video, File.GetLastWriteTimeUtc(video).AddMinutes(1));
        Check(store.Load(video) == null, "A transcript was shown for a changed video");
        store.Save(video, new TranscriptDocument(lines, "en", "speech-fast", null));
        File.AppendAllText(video, "more");
        Check(store.Load(video) == null, "A transcript was shown for a video of a different size");

        // Unreadable or oversized data is ignored.
        string fresh = Path.Combine(directory, "store", "fresh.mp4");
        File.WriteAllText(fresh, "third stand-in");
        var before = Directory.GetFiles(data, "*.json").ToHashSet();
        store.Save(fresh, new TranscriptDocument(lines, "en", "speech-fast", null));
        string record = Directory.GetFiles(data, "*.json").Single(path => !before.Contains(path));
        File.WriteAllText(record, "{ not json");
        Check(store.Load(fresh) == null, "A damaged transcript file was loaded");
        using (var stream = File.Create(record)) stream.SetLength(TranscriptStore.MaxBytes + 1);
        Check(store.Load(fresh) == null, "An oversized transcript file was loaded");
    }

    internal static string ModelSource => Environment.GetEnvironmentVariable("TRANSCRIPT_MODELS") ?? @"E:\DesktopTools-ai-spikes\models";

    /// <summary>Copies the developer's Whisper files into pack folders under <paramref name="root"/> after checking each against the catalog hash.</summary>
    internal static bool InstallDevPacks(string root)
    {
        var map = new (TranscriptModelSize Size, string Folder)[] { (TranscriptModelSize.Fast, "whisper-base"), (TranscriptModelSize.Accurate, "whisper-small") };
        if (map.Any(item => SpeechTranscriber.Pack(item.Size)!.Files.Any(file => !File.Exists(Path.Combine(ModelSource, item.Folder, file.File))))) return false;
        foreach (var (size, folder) in map)
        {
            var pack = SpeechTranscriber.Pack(size)!;
            Directory.CreateDirectory(Path.Combine(root, pack.Id));
            foreach (var file in pack.Files)
            {
                string from = Path.Combine(ModelSource, folder, file.File);
                using (var stream = File.OpenRead(from))
                    if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"{from} does not match the catalog checksum for {pack.Id}/{file.File}");
                File.Copy(from, Path.Combine(root, pack.Id, file.File), true);
            }
        }
        return true;
    }

    /// <summary>Speech from Windows text-to-speech with 2 s of silence before and 30 s after, as a 16 kHz mono WAV. Null when no English voice is installed.</summary>
    internal static async Task<(string Path, double SpeechEnd)?> GeneratedSpeechAsync(string directory, string sentence, string name = "speech.wav")
    {
        var voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (voice == null) return null;
        string raw = Path.Combine(directory, "tts-" + Guid.NewGuid().ToString("N") + ".wav");
        using (var synthesizer = new Windows.Media.SpeechSynthesis.SpeechSynthesizer { Voice = voice })
        using (var stream = await synthesizer.SynthesizeTextToStreamAsync(sentence))
        await using (var output = File.Create(raw))
            await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(output);
        var speech = new List<float>();
        await Task.Run(() =>
        {
            using var decoder = AudioDecoder.Open(raw);
            var buffer = new float[16000]; int read;
            while ((read = decoder.Read(buffer)) > 0) speech.AddRange(buffer.Take(read));
        });
        File.Delete(raw);
        string path = Path.Combine(directory, name);
        int lead = 2 * 16000, total = lead + speech.Count + 30 * 16000;
        WriteWav(path, 16000, 1, total / 16000.0, (i, _) => i >= lead && i - lead < speech.Count ? speech[i - lead] : 0);
        return (path, (lead + speech.Count) / 16000.0);
    }

    private static async Task RealModelsAsync(string directory, Action<string> report)
    {
        // Copies go next to the developer's files (the same drive) rather than to the system temp folder: about 1.3 GB.
        string source = Path.GetFullPath(ModelSource);
        string root = Path.Combine(Directory.Exists(source) ? source : directory, ".transcript-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!Directory.Exists(source) || !InstallDevPacks(root)) { report($"NOT TESTED transcripts with real Whisper models: files not found in {ModelSource}"); return; }
            var speech = await GeneratedSpeechAsync(directory, "The quick brown fox jumps over the lazy dog. Please call me back at nine thirty tomorrow morning.");
            if (speech == null) { report("NOT TESTED transcripts with real Whisper models: no English Windows text-to-speech voice is installed"); return; }
            var (audio, speechEnd) = speech.Value;
            using (ModelPacks.UseForTests(ModelPacks.Catalog, root, OcrPackChecks.NoNetwork()))
            {
                foreach (var size in new[] { TranscriptModelSize.Fast, TranscriptModelSize.Accurate })
                {
                    Check(SpeechTranscriber.IsInstalled(size), size + " pack not installed in the check folder");
                    var reports = new List<TranscriptProgress>();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var result = await SpeechTranscriber.TranscribeAsync(audio, size, null, new SynchronousProgress<TranscriptProgress>(reports.Add), CancellationToken.None);
                    clock.Stop();
                    string text = string.Join(" ", result.Lines.Select(line => line.Text));
                    string timed = string.Join(" | ", result.Lines.Select(l => $"{l.Start.TotalSeconds:0.00}-{l.End.TotalSeconds:0.00} {l.Text}"));
                    Check(text.Contains("quick brown fox", StringComparison.OrdinalIgnoreCase) && text.Contains("lazy dog", StringComparison.OrdinalIgnoreCase), $"{size} transcript: {timed}");
                    Check(result.Language == "en", $"{size} detected language {result.Language}");
                    Check(Math.Abs(result.Lines[0].Start.TotalSeconds - 2.0) <= .5, $"{size} first line: {timed}");
                    Check(result.Lines.All(line => line.Start.TotalSeconds < speechEnd && line.End.TotalSeconds <= speechEnd + 1.0), $"{size} has text in the trailing silence (speech ends {speechEnd:0.00}): {timed}");
                    Check(result.SkippedWindows >= 0 && reports.Count > 0 && reports[^1].Processed.TotalSeconds >= speechEnd, $"{size} progress");
                    report($"MEASURE transcript {size}: {result.Duration.TotalSeconds:0.0} s audio in {clock.Elapsed.TotalSeconds:0.0} s, {result.SkippedWindows} windows skipped: {timed}");
                }

                // Cancelling stops at the next window and releases the model gate.
                using (var cancel = new CancellationTokenSource())
                {
                    var running = SpeechTranscriber.TranscribeAsync(audio, TranscriptModelSize.Fast, "en", new SynchronousProgress<TranscriptProgress>(_ => cancel.Cancel()), cancel.Token);
                    try { await running; throw new Exception("Cancelled transcription finished"); }
                    catch (OperationCanceledException) { }
                    var gate = AiRuntime.RunAsync(Path.Combine(root, "speech-fast", "encoder_model.onnx"), SpeechTranscriber.Pack(TranscriptModelSize.Fast)!.Files[0].Sha256, _ => 0, CancellationToken.None);
                    Check(await Task.WhenAny(gate, Task.Delay(TimeSpan.FromMinutes(1))) == gate, "The model gate stayed held after cancelling");
                    await gate;
                }
                AiRuntime.ReleaseAll();

                // A damaged file is found at load: the pack is marked damaged and reported as unavailable, not as a crash.
                var fast = SpeechTranscriber.Pack(TranscriptModelSize.Fast)!;
                string encoder = Path.Combine(root, fast.Id, "encoder_model.onnx");
                using (var stream = new FileStream(encoder, FileMode.Open, FileAccess.ReadWrite)) { stream.Position = 4096; int value = stream.ReadByte(); stream.Position = 4096; stream.WriteByte((byte)(value ^ 0x5A)); }
                try { await SpeechTranscriber.TranscribeAsync(audio, TranscriptModelSize.Fast, "en", null, CancellationToken.None); throw new Exception("A damaged encoder was used"); }
                catch (ModelPackUnavailableException e) { Check(e.Damaged && e.Pack.Id == fast.Id, "Damaged pack not reported as damaged"); }
                Check(SpeechTranscriber.StateOf(TranscriptModelSize.Fast) == ModelPackState.Damaged && AiRuntime.LoadedModelPath == null, "Damaged pack not marked");
            }

            // The encoder loaded in memory keeps only last_hidden_state, the same graph size as the feasibility spike's stripped copy.
            byte[] original = File.ReadAllBytes(Path.Combine(source, "whisper-base", "encoder_model.onnx"));
            byte[] trimmed = OnnxModelEdit.KeepOutputs(original, ["last_hidden_state"]);
            Check(OnnxModelEdit.OutputNames(trimmed).SequenceEqual(["last_hidden_state"]), "Encoder outputs after trimming");
            string derived = Path.Combine(source, "derived", "whisper-base-noattn", "encoder_model.onnx");
            if (File.Exists(derived)) Check(trimmed.Length == new FileInfo(derived).Length, $"Trimmed encoder is {trimmed.Length} bytes, the stripped copy {new FileInfo(derived).Length}");
            foreach (string folder in new[] { "whisper-base", "whisper-small" })
            {
                var config = WhisperDecoding.ParseConfig(File.ReadAllText(Path.Combine(source, folder, "generation_config.json")));
                Check(WhisperDecoding.LanguageCodes(config).SequenceEqual(WhisperDecoding.KnownLanguages), folder + " languages differ from the settings list");
            }
            report("PASS transcripts with real Whisper models: fast and accurate, language detection, silence, cancel, damaged pack");
        }
        finally
        {
            AiRuntime.ReleaseAll();
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(instance)!;

    private static IEnumerable<System.Windows.DependencyObject> Tree(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Tree(child)) yield return nested;
        }
    }

    private static async Task Until(Func<bool> condition, string message, int milliseconds = 5000)
    {
        for (int waited = 0; !condition(); waited += 50)
        {
            if (waited > milliseconds) throw new Exception(message);
            await Task.Delay(50);
        }
    }

    private static void Click(System.Windows.Controls.Button button) => button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    /// <summary>The Transcript tab rendered in languages with long words and CJK text; writes transcript-&lt;language&gt;.png for review.</summary>
    private static async Task LocalesAsync(string directory, Action<string> report)
    {
        string video = await VideoEditingChecks.FixtureAsync(Path.Combine(directory, "locales"));
        var store = new TranscriptStore(Path.Combine(directory, "locales-store"));
        store.Save(video, new TranscriptDocument([new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.9), "The quick brown fox")], "en", "speech-fast", null));
        foreach (string language in new[] { "ru", "de", "ja" })
        {
            using var controller = new DesktopTools.AppController(true);
            controller.UpdateSettings(s => { s.Animations = false; s.TranscriptModel = "Fast"; s.TranscriptLanguage = "auto"; });
            DesktopTools.Localization.L.Use(language);
            var window = new VideoEditorWindow(_ => { }, null, store, controller) { Width = 1120, Height = 800 };
            try
            {
                using (ModelPacks.UseForTests(ModelPacks.Catalog, Path.Combine(directory, "locales-models"), OcrPackChecks.NoNetwork()))
                {
                    window.Show();
                    await window.LoadAsync(video);
                    window.ShowTranscriptTab();
                    var panel = window.Transcript ?? throw new Exception("The editor has no Transcript tab");
                    await Until(() => panel.Lines.Count == 1, language + ": the stored transcript did not load");
                    Check(panel.PrimaryLabel != "Download model", language + ": the main button is not translated");
                    window.UpdateLayout(); await Task.Delay(200);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create("transcript-" + language + ".png"); encoder.Save(stream);
                }
            }
            finally { window.Close(); DesktopTools.Localization.L.Use("en"); }
        }
        report("PASS transcript tab renders in ru, de and ja (transcript-*.png)");
    }

    /// <summary>The Transcript tab in a real editor window: download offer, stored lines, click to seek, search, edits kept in app data, no audio.</summary>
    private static async Task PanelAsync(string directory, Action<string> report)
    {
        using var controller = new DesktopTools.AppController(true);
        controller.UpdateSettings(s => { s.Animations = false; s.TranscriptNoticeAccepted = false; s.TranscriptModel = "Fast"; s.TranscriptLanguage = "auto"; });
        string root = Path.Combine(directory, "panel-models");
        string video = await VideoEditingChecks.FixtureAsync(Path.Combine(directory, "panel"));
        string caption = Path.ChangeExtension(video, ".srt");
        File.WriteAllText(caption, "1\r\n00:00:00,000 --> 00:00:01,000\r\nThe user's own captions\r\n");
        byte[] userCaption = File.ReadAllBytes(caption);
        var store = new TranscriptStore(Path.Combine(directory, "panel-store"));
        TranscriptLine[] lines =
        [
            new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.9), "The quick brown fox"),
            new(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.2), "jumps over"),
            new(TimeSpan.FromSeconds(2.6), TimeSpan.FromSeconds(3.4), "the lazy dog"),
        ];
        store.Save(video, new TranscriptDocument(lines, "en", "speech-fast", null));
        int consentAsked = 0, confirmed = 0;
        var oldConsent = TranscriptPanel.AskConsent; var oldConfirm = TranscriptPanel.Confirm;
        TranscriptPanel.AskConsent = (_, _, _) => { consentAsked++; return false; };
        var asked = new List<string>();
        TranscriptPanel.Confirm = (_, title, _, _) => { confirmed++; asked.Add(title); return true; };
        var window = new VideoEditorWindow(_ => { }, null, store, controller);
        try
        {
            using (ModelPacks.UseForTests(ModelPacks.Catalog, root, OcrPackChecks.NoNetwork()))
            {
                window.Show();
                await window.LoadAsync(video);
                window.ShowTranscriptTab();
                var panel = window.Transcript ?? throw new Exception("The editor has no Transcript tab");
                await Until(() => panel.Lines.Count == 3, "The stored transcript did not load: " + panel.Lines.Count);
                Check(panel.IsVisible, "The Transcript tab is not showing");

                // The fast pack is missing: the main button offers the download, and declining the licence downloads nothing.
                Check(panel.PrimaryLabel == DesktopTools.Localization.L.T("Download model"), "Primary button: " + panel.PrimaryLabel);
                Click(Field<System.Windows.Controls.Button>(panel, "primary"));
                await Until(() => consentAsked == 1, "The licence was not shown");
                await Until(() => !panel.IsBusy, "Declined download kept the panel busy");
                Check(!Directory.Exists(Path.Combine(root, "speech-fast")) && panel.StatusText == DesktopTools.Localization.L.T("Download canceled."), "Declined download: " + panel.StatusText);

                // A line's time moves the playhead there.
                await Until(() => Field<bool>(window, "mediaOpened"), "The fixture did not open for playback");
                window.UpdateLayout();
                var times = Tree(panel).OfType<System.Windows.Controls.Button>().Where(b => Equals(b.Tag, "transcript-time")).ToArray();
                Check(times.Length == 3, "Time buttons shown: " + times.Length);
                Click(times[1]);
                var media = Field<System.Windows.Controls.MediaElement>(window, "media");
                await Until(() => Math.Abs(media.Position.TotalSeconds - 1.5) <= .05, "Seek to line 2 ended at " + media.Position, 3000);

                // Search shows only matching lines.
                var search = Field<System.Windows.Controls.TextBox>(panel, "search");
                search.Text = "fox";
                var list = Field<System.Windows.Controls.ItemsControl>(panel, "list");
                Check(list.Items.Count == 1, "Search for fox shows " + list.Items.Count + " lines");
                search.Text = "";
                Check(list.Items.Count == 3, "Clearing the search did not show every line");

                // Edits are kept in app data after a short pause; the caption file next to the video stays the user's.
                window.UpdateLayout();
                var first = Tree(panel).OfType<System.Windows.Controls.TextBox>().First(b => Equals(b.Tag, "transcript-text"));
                first.Text = "The quick red fox";
                await Task.Delay(1000);
                Check(store.Load(video)?.Lines[0].Text == "The quick red fox", "The edit was not saved: " + store.Load(video)?.Lines[0].Text);
                Check(File.ReadAllBytes(caption).SequenceEqual(userCaption), "Editing changed the caption file next to the video");

                // A video with no audio track: a clear message once a model is installed, and nothing written.
                string source = Path.GetFullPath(ModelSource);
                var fast = SpeechTranscriber.Pack(TranscriptModelSize.Fast)!;
                if (fast.Files.Where(f => f.File.EndsWith(".json", StringComparison.Ordinal)).All(f => File.Exists(Path.Combine(source, "whisper-base", f.File))))
                {
                    // Stand-in model files of the right size (never loaded: the audio is checked first) and the real settings files.
                    Directory.CreateDirectory(Path.Combine(root, fast.Id));
                    foreach (var file in fast.Files)
                    {
                        string target = Path.Combine(root, fast.Id, file.File);
                        if (file.File.EndsWith(".json", StringComparison.Ordinal)) File.Copy(Path.Combine(source, "whisper-base", file.File), target, true);
                        else using (var stream = File.Create(target)) stream.SetLength(file.Bytes);
                    }
                    controller.UpdateSettings(s => s.TranscriptModel = "Accurate");
                    controller.UpdateSettings(s => s.TranscriptModel = "Fast");
                    Check(panel.PrimaryLabel == DesktopTools.Localization.L.T("Transcribe"), "Installed pack still offers a download: " + panel.PrimaryLabel);
                    await panel.TranscribeAsync();
                    // The consent notice comes first; the stored transcript then asks before it is replaced.
                    Check(asked.SequenceEqual([DesktopTools.Localization.L.T("Before you transcribe"), DesktopTools.Localization.L.T("Replace the transcript?")]) && controller.Settings.TranscriptNoticeAccepted,
                        "Questions before transcribing: " + string.Join(" / ", asked));
                    Check(panel.StatusText == DesktopTools.Localization.L.T(TranscriptPanel.NoAudioText), "No audio track: " + panel.StatusText);
                    Check(File.ReadAllBytes(caption).SequenceEqual(userCaption) && store.Load(video)?.Lines.Count == 3, "No-audio transcription changed files");
                    await panel.TranscribeAsync();
                    Check(confirmed == 3 && asked[^1] == DesktopTools.Localization.L.T("Replace the transcript?"), "The consent notice was shown again: " + string.Join(" / ", asked));
                }
                else report($"NOT TESTED transcript panel no-audio message: the Whisper settings files are not in {ModelSource}");
                report("PASS transcript panel: download offer, stored lines, click to seek, search, edits kept in app data");
            }
        }
        finally
        {
            TranscriptPanel.AskConsent = oldConsent; TranscriptPanel.Confirm = oldConfirm;
            if (window.IsVisible) window.Close();
        }
    }
}
