using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;

/// <summary>
/// Manual on-screen preview of the background quality pack, the face finder and translation packs (run with --tool-improvements-preview).
/// Models and consents go to a temporary folder; the two developer model files under E:\DesktopTools-ai-spikes are served locally for
/// their catalog addresses, everything else (translation packs) comes from the publisher. Opens the window chosen by TOOL_PREVIEW
/// (image, editor or text) at TOOL_PREVIEW_AT ("x,y"); closing it ends the run.
/// </summary>
internal static class ToolImprovementsPreview
{
    private sealed class LocalFirstHandler(Dictionary<string, string> local) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            local.TryGetValue(request.RequestUri!.ToString(), out var file)
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(file)) })
                : base.SendAsync(request, token);
    }

    internal static async Task PreviewAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "dt-tools-preview-" + Guid.NewGuid().ToString("N"));
        string translations = Path.Combine(root, "Translation");
        Directory.CreateDirectory(translations);
        var packs = ModelPacks.Catalog;
        var local = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, file) in new[] { ("background-quality", BackgroundQualityChecks.DevModel), ("faces", FaceSuggestionChecks.DevModel) })
            if (packs.FirstOrDefault(p => p.Id == id) is { } pack && File.Exists(file)) local[pack.Files[0].Url] = file;
        using var handler = new LocalFirstHandler(local);
        using var scope = ModelPacks.UseForTests(packs, root, handler);
        using var translationRoot = TranslationPacks.UseRootForTests(translations);
        using var controller = new AppController(true);
        Window window;
        string mode = Environment.GetEnvironmentVariable("TOOL_PREVIEW") ?? "image";
        var photo = new BitmapImage(); photo.BeginInit(); photo.CacheOption = BitmapCacheOption.OnLoad; photo.UriSource = new Uri(FaceSuggestionChecks.Astronaut); photo.EndInit(); photo.Freeze();
        if (mode == "image")
        {
            string model = "Standard";
            var tools = new ImageToolsWindow(Console.WriteLine, getBackgroundModel: () => model, setBackgroundModel: value => model = value);
            tools.LoadImage(FaceSuggestionChecks.Astronaut, photo);
            window = tools;
        }
        else if (mode == "editor") window = new ScreenshotEditorWindow(FaceSuggestionChecks.VideoCall(photo, 160).Frame, _ => { }, Console.WriteLine);
        else
        {
            var tools = new TextToolsWindow(controller);
            var text = new StringBuilder();
            string[] sentences = ["The meeting starts at nine in the small room on the second floor.", "Please send the report to the whole team before Friday.",
                "The new version opens files faster and uses less memory.", "We will review the results together next week.", "Check the settings before you restart the computer."];
            for (int i = 0; text.Length < 15000; i++) text.Append(sentences[i % sentences.Length]).Append(i % 7 == 6 ? "\n\n" : " ");
            tools.Show(); tools.ShowMode(false); tools.SetSource(text.ToString());
            window = tools;
        }
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (Environment.GetEnvironmentVariable("TOOL_PREVIEW_AT")?.Split(',') is [var x, var y])
        {
            window.Left = double.Parse(x, System.Globalization.CultureInfo.InvariantCulture); window.Top = double.Parse(y, System.Globalization.CultureInfo.InvariantCulture);
        }
        var completion = new TaskCompletionSource();
        window.Closed += (_, _) => completion.TrySetResult();
        window.Show(); window.Activate();
        Console.WriteLine("Preview models folder: " + root);
        try { await completion.Task.WaitAsync(TimeSpan.FromMinutes(30)); }
        finally
        {
            AiRuntime.ReleaseAll();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
