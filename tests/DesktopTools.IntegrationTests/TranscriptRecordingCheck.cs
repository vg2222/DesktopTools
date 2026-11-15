using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

/// <summary>
/// End to end, opt-in (<c>--transcript-recording</c>): plays generated speech aloud while the real recorder captures a region with system audio,
/// then follows the recorder's Transcribe button into the editor and transcribes with the developer's Whisper files. Needs speakers or a
/// loopback-capable output device, an English text-to-speech voice and the files in TRANSCRIPT_MODELS. Writes transcript-recording.png.
/// </summary>
internal static partial class TranscriptChecks
{
    internal static async Task RecordingAsync(Action<string> report)
    {
        string directory = Path.Combine(Path.GetTempPath(), "DesktopTools-transcript-recording-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string source = Path.GetFullPath(ModelSource);
        string root = Path.Combine(Directory.Exists(source) ? source : directory, ".transcript-recording-" + Guid.NewGuid().ToString("N"));
        var oldConfirm = TranscriptPanel.Confirm;
        try
        {
            if (!Directory.Exists(source) || !InstallDevPacks(root)) { report($"NOT TESTED recorded transcript: model files not found in {ModelSource}"); return; }
            var speech = await GeneratedSpeechAsync(directory, "The quick brown fox jumps over the lazy dog. Please call me back at nine thirty tomorrow morning.");
            if (speech == null) { report("NOT TESTED recorded transcript: no English Windows text-to-speech voice"); return; }
            var (audio, speechEnd) = speech.Value;

            // Record a small region of a coloured window on the check monitor while the speech plays through the default output.
            string recording = Path.Combine(directory, "recording.mp4");
            using (var target = new Forms.Form { TopMost = true, StartPosition = Forms.FormStartPosition.Manual, Width = 480, Height = 300, Text = "DesktopTools transcript recording fixture", BackColor = Drawing.Color.SeaGreen })
            {
                var monitor = MonitorService.GetAll().FirstOrDefault(m => Environment.GetEnvironmentVariable("DESKTOPTOOLS_CHECK_MONITOR") is { Length: > 0 } wanted && m.Id.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                    ?? MonitorService.GetAll().First();
                target.Location = new Drawing.Point((int)monitor.Bounds.X + 200, (int)monitor.Bounds.Y + 200);
                target.Show(); target.Refresh(); await Task.Delay(600);
                var corner = target.PointToScreen(new Drawing.Point(20, 20));
                var shown = MonitorService.GetAll().First(m => m.Bounds.Contains(corner.X, corner.Y));
                var region = new ScreenRecorderLib.DisplayRecordingSource(shown.Id)
                {
                    RecorderApi = ScreenRecorderLib.RecorderApi.WindowsGraphicsCapture, IsCursorCaptureEnabled = false, IsBorderRequired = false,
                    SourceRect = new ScreenRecorderLib.ScreenRect(corner.X - shown.Bounds.X, corner.Y - shown.Bounds.Y, 320, 200),
                };
                using var service = new ScreenRecordingService();
                var result = service.StartSource(region, recording, systemAudio: true);
                await Task.Delay(800);
                using (var player = new System.Media.SoundPlayer(audio))
                {
                    player.Play();
                    await Task.Delay(TimeSpan.FromSeconds(speechEnd + 1.0));
                    player.Stop();
                }
                service.Stop();
                Check(await result.WaitAsync(TimeSpan.FromSeconds(30)) == recording, "Recording destination");
            }
            var info = await VideoEditorService.ProbeAsync(recording);
            Check(info.HasAudio, "The recording has no audio track");

            using var controller = new DesktopTools.AppController(true);
            controller.UpdateSettings(s => { s.Animations = false; s.VideoEditorEnabled = true; s.TranscriptNoticeAccepted = false; s.TranscriptModel = "Fast"; s.TranscriptLanguage = "auto"; });
            L.Use("en");
            int notices = 0, confirmations = 0;
            TranscriptPanel.Confirm = (_, title, _, _) => { if (title == L.T("Before you transcribe")) notices++; else confirmations++; return true; };
            using (ModelPacks.UseForTests(ModelPacks.Catalog, root, OcrPackChecks.NoNetwork()))
            {
                // The saved-recording notice offers Transcribe; it opens the editor on the Transcript tab with the recording.
                var recorder = new ScreenRecorderWindow(controller, _ => null);
                VideoEditorWindow? editor = null;
                try
                {
                    recorder.Show();
                    typeof(ScreenRecorderWindow).GetMethod("ShowSavedMarkers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(recorder, [recording]);
                    recorder.UpdateLayout();
                    Click(Tree(recorder).OfType<System.Windows.Controls.Button>().Single(b => Equals(b.Tag, "recording-transcribe")));
                    await Until(() => (editor = System.Windows.Application.Current.Windows.OfType<VideoEditorWindow>().FirstOrDefault()) != null, "Transcribe did not open the editor");
                    var panel = editor!.Transcript ?? throw new Exception("The editor has no Transcript tab");
                    await Until(() => panel.IsVisible && Field<bool>(editor, "mediaOpened"), "The recording did not open on the Transcript tab", 10000);
                    Check(panel.PrimaryLabel == L.T("Transcribe"), "Primary button: " + panel.PrimaryLabel);

                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    Click(Field<System.Windows.Controls.Button>(panel, "primary"));
                    await Until(() => panel.IsBusy, "Transcription did not start");
                    await Until(() => !panel.IsBusy, "Transcription did not finish: " + panel.StatusText, 180000);
                    clock.Stop();
                    var lines = panel.Lines;
                    string timed = string.Join(" | ", lines.Select(l => $"{l.Start.TotalSeconds:0.00}-{l.End.TotalSeconds:0.00} {l.Text}"));
                    string text = string.Join(" ", lines.Select(l => l.Text));
                    Check(notices == 1, "The consent notice was shown " + notices + " times");
                    Check(text.Contains("quick brown fox", StringComparison.OrdinalIgnoreCase) && text.Contains("tomorrow morning", StringComparison.OrdinalIgnoreCase), "Recorded transcript: " + timed + " / " + panel.StatusText);
                    string caption = TranscriptStore.CaptionPath(recording);
                    Check(File.Exists(caption) && CaptionFiles.ParseSrt(File.ReadAllText(caption)).Count == lines.Count, "The caption file next to the recording");
                    report($"MEASURE recorded transcript ({info.Duration:0.0} s, system audio): {clock.Elapsed.TotalSeconds:0.0} s; {timed}");

                    // Choosing a time seeks there; search filters; an edit is kept.
                    editor.UpdateLayout();
                    var times = Tree(panel).OfType<System.Windows.Controls.Button>().Where(b => Equals(b.Tag, "transcript-time")).ToArray();
                    Click(times[^1]);
                    var media = Field<System.Windows.Controls.MediaElement>(editor, "media");
                    await Until(() => Math.Abs(media.Position.TotalSeconds - lines[^1].Start.TotalSeconds) <= .1, "Seek ended at " + media.Position, 3000);
                    var search = Field<System.Windows.Controls.TextBox>(panel, "search");
                    var list = Field<System.Windows.Controls.ItemsControl>(panel, "list");
                    search.Text = "lazy"; int matching = lines.Count(l => l.Text.Contains("lazy", StringComparison.OrdinalIgnoreCase));
                    Check(matching >= 1 && list.Items.Count == matching, "Search for lazy shows " + list.Items.Count + " of " + matching);
                    search.Text = "";
                    editor.UpdateLayout();
                    var box = Tree(panel).OfType<System.Windows.Controls.TextBox>().First(b => Equals(b.Tag, "transcript-text"));
                    string edited = box.Text + " (checked)"; box.Text = edited;
                    await Task.Delay(1000);
                    Check(controller.Transcripts.Load(recording)?.Lines[0].Text == edited, "The edit was not kept");

                    // A trimmed copy gets captions moved by the trim.
                    string copy = Path.Combine(directory, "trimmed.mp4");
                    var edit = new VideoEdit(1.0, info.Duration);
                    await VideoEditorService.ExportAsync(info, edit, copy);
                    confirmations = 0;
                    typeof(VideoEditorWindow).GetMethod("OfferRetimedCaptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(editor, [info, edit, copy]);
                    var retimed = CaptionFiles.ParseSrt(File.ReadAllText(TranscriptStore.CaptionPath(copy)));
                    var kept = controller.Transcripts.Load(recording)!.Lines;
                    Check(confirmations == 1 && retimed.Count >= 1 && Math.Abs(retimed[^1].Start.TotalSeconds - (kept[^1].Start.TotalSeconds - 1.0)) < .01 && retimed[0].Text == edited,
                        "Retimed captions: " + string.Join(" | ", retimed.Select(l => $"{l.Start.TotalSeconds:0.00} {l.Text}")));

                    editor.UpdateLayout(); await Task.Delay(300);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(editor.ActualWidth), (int)Math.Ceiling(editor.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(editor);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using (var stream = File.Create("transcript-recording.png")) encoder.Save(stream);
                }
                finally { editor?.Close(); recorder.Close(); }
            }
            AiRuntime.ReleaseAll();
            report("PASS recorded transcript: recorder Transcribe button, consent notice, real model, seek, search, edit, caption file and retimed copy");
        }
        finally
        {
            TranscriptPanel.Confirm = oldConfirm;
            AiRuntime.ReleaseAll();
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
