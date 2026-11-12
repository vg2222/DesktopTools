using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;

/// <summary>Serves model files from memory so pack tests never touch the network.</summary>
internal sealed class FakeModelServer : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
    internal readonly List<string> Requests = [];
    internal Func<HttpRequestMessage, HttpResponseMessage?>? Override;
    internal Action<string>? OnRequest;
    internal void Serve(string url, byte[] content) => files[url] = content;
    /// <summary>Answers with a redirect to <paramref name="location"/>.</summary>
    internal void Redirect(string url, string location) => redirects[url] = location;
    private readonly Dictionary<string, string> redirects = new(StringComparer.Ordinal);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string url = request.RequestUri!.ToString();
        lock (Requests) Requests.Add(url);
        OnRequest?.Invoke(url);
        token.ThrowIfCancellationRequested();
        if (Override?.Invoke(request) is { } custom) return Task.FromResult(custom);
        if (redirects.TryGetValue(url, out var location))
        {
            var moved = new HttpResponseMessage(HttpStatusCode.Found);
            moved.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(moved);
        }
        return Task.FromResult(files.TryGetValue(url, out var bytes) && bytes != null
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") });
    }
}

internal static class ModelPackChecks
{
    internal static byte[] Bytes(int length, int seed) { var data = new byte[length]; new Random(seed).NextBytes(data); return data; }
    internal static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>A two-file fixture pack on an allowed host, plus a server that has both files.</summary>
    internal static (ModelPack Pack, FakeModelServer Server) Fixture(string id = "fixture-pack", int seed = 1)
    {
        byte[] first = Bytes(300_000, seed), second = Bytes(70_000, seed + 1);
        string Url(string file) => $"https://huggingface.co/fixture/{id}/resolve/rev1/{file}";
        var pack = new ModelPack(id, "Fixture model", "Tests the pack installer", "tests", "Fixture Publisher", "Fixture Authors",
            "https://huggingface.co/fixture/" + id, "rev1", "mit", "MIT.txt", "Fixture attribution line", "Synthetic bytes", ["Not a real model"],
            [new("model.onnx", Url("model.onnx"), Hash(first), first.Length), new("vocab.json", Url("vocab.json"), Hash(second), second.Length)]);
        ModelCatalog.Validate(pack);
        var server = new FakeModelServer();
        server.Serve(pack.Files[0].Url, first); server.Serve(pack.Files[1].Url, second);
        return (pack, server);
    }

    internal static async Task RunAsync()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string root = Path.GetFullPath("model-pack-fixture-" + Guid.NewGuid().ToString("N"));
        Func<string, long> plenty = _ => long.MaxValue;
        bool NoStaging() => !Directory.Exists(root) || Directory.GetDirectories(root, ".download-*").Length == 0;
        async Task<Exception?> Fails(Task task) { try { await task; return null; } catch (Exception e) { return e; } }
        Exception? Record(Action action) { try { action(); return null; } catch (Exception e) { return e; } }
        try
        {
            // The shipped catalog lists exactly the OCR pack (spec section 8); every entry passes the catalog rules when it is parsed.
            Check(ModelPacks.Catalog.Select(p => p.Id).Order().SequenceEqual(["ocr-chinese-japanese", "ocr-cyrillic", "ocr-latin", "ocr-text-detector"]), "Unexpected shipped catalog: " + string.Join(", ", ModelPacks.Catalog.Select(p => p.Id)));
            Check(ModelPacks.Root.EndsWith(Path.Combine("DesktopTools", "Models"), StringComparison.OrdinalIgnoreCase), "Unexpected pack root " + ModelPacks.Root);
            foreach (string license in new[] { "MIT.txt", "Apache-2.0.txt", "BSD-2-Clause.txt", "BSD-3-Clause.txt", "CC-BY-4.0.txt" })
                Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Licenses", license)), "Licence text not shipped: " + license);

            var (pack, server) = Fixture();
            var consents = new ModelConsentStore(root);
            string licenseText = ModelPacks.LicenseText(pack);
            Check(licenseText.Contains("Permission is hereby granted", StringComparison.Ordinal), "MIT licence text not read");

            // No consent: refused before any request.
            var refused = await Fails(ModelPacks.InstallAsync(pack, consents, null, CancellationToken.None, root, server, plenty));
            Check(refused is InvalidOperationException && server.Requests.Count == 0, "Download started without consent: " + refused);
            Check(ModelPacks.StateOf(pack, root) == ModelPackState.Missing, "State before install");

            consents.Accept(pack, licenseText, DateTimeOffset.UtcNow, "test");

            // Not enough disk space: refused before any request, with the size needed.
            var full = await Fails(ModelPacks.InstallAsync(pack, consents, null, CancellationToken.None, root, server, _ => pack.TotalBytes * 2 - 1));
            Check(full is ModelPackException && server.Requests.Count == 0, "Low disk space was not refused first: " + full);
            Check(full!.Message.Contains(ModelPacks.FormatSize(pack.TotalBytes * 2), StringComparison.Ordinal), "Low-space message does not say what is needed: " + full.Message);

            // Leftover staging folders from an interrupted earlier run are removed by the next install.
            Directory.CreateDirectory(Path.Combine(root, ".download-leftover", "nested"));
            File.WriteAllText(Path.Combine(root, ".download-leftover", "nested", "model.onnx"), "partial");

            // Success.
            double last = 0; var reports = new List<double>();
            var progress = new SynchronousProgress(value => { reports.Add(value); last = value; });
            await ModelPacks.InstallAsync(pack, consents, progress, CancellationToken.None, root, server, plenty);
            Check(ModelPacks.StateOf(pack, root) == ModelPackState.Installed, "State after install");
            string directory = ModelPacks.DirectoryFor(pack, root) ?? throw new Exception("Installed pack has no directory");
            foreach (string file in new[] { "model.onnx", "vocab.json", "LICENSE.txt", "ATTRIBUTION.txt", "SOURCE.txt" })
                Check(File.Exists(Path.Combine(directory, file)), "Installed pack lacks " + file);
            string source = File.ReadAllText(Path.Combine(directory, "SOURCE.txt"));
            Check(source.Contains(pack.Upstream, StringComparison.Ordinal) && source.Contains("rev1", StringComparison.Ordinal) && source.Contains(pack.Files[0].Sha256, StringComparison.Ordinal), "SOURCE.txt incomplete");
            Check(File.ReadAllText(Path.Combine(directory, "LICENSE.txt")) == licenseText, "LICENSE.txt is not the accepted text");
            Check(File.ReadAllText(Path.Combine(directory, "ATTRIBUTION.txt")).Contains("Fixture attribution line", StringComparison.Ordinal), "ATTRIBUTION.txt incomplete");
            Check(last == 1 && reports.Zip(reports.Skip(1)).All(pair => pair.Second >= pair.First), "Progress did not rise to 1");
            Check(NoStaging(), "Staging folder left after success (or leftover not removed)");
            Check(ModelPacks.SizeOnDisk(pack, root) >= pack.TotalBytes, "Size on disk");
            int requests = server.Requests.Count;
            await ModelPacks.InstallAsync(pack, consents, null, CancellationToken.None, root, server, plenty);
            Check(server.Requests.Count == requests, "An installed pack was downloaded again");

            // A file deleted after install: damaged, not usable.
            File.Delete(Path.Combine(directory, "vocab.json"));
            Check(ModelPacks.StateOf(pack, root) == ModelPackState.Damaged && ModelPacks.DirectoryFor(pack, root) == null, "Deleted file not reported as damaged");
            await ModelPacks.InstallAsync(pack, consents, null, CancellationToken.None, root, server, plenty);
            Check(ModelPacks.StateOf(pack, root) == ModelPackState.Installed, "Damaged pack was not repaired by a new download");

            // A truncated or edited file: damaged.
            File.WriteAllBytes(Path.Combine(directory, "model.onnx"), new byte[10]);
            Check(ModelPacks.StateOf(pack, root) == ModelPackState.Damaged, "Truncated file not reported as damaged");

            // MarkDamaged (a later hash check failed) then reinstall.
            await ModelPacks.InstallAsync(pack, consents, null, CancellationToken.None, root, server, plenty);
            ModelPacks.MarkDamaged(pack, root);
            Check(ModelPacks.StateOf(pack, root) == ModelPackState.Damaged && ModelPacks.DirectoryFor(pack, root) == null, "MarkDamaged ignored");
            await ModelPacks.InstallAsync(pack, consents, null, CancellationToken.None, root, server, plenty);
            Check(ModelPacks.StateOf(pack, root) == ModelPackState.Installed, "MarkDamaged pack not repaired");

            // Remove: folder and consent record gone.
            ModelPacks.Remove(pack, consents, root);
            Check(!Directory.Exists(Path.Combine(root, pack.Id)) && ModelPacks.StateOf(pack, root) == ModelPackState.Missing, "Remove left the pack");
            Check(!consents.IsCurrent(pack, licenseText) && new ModelConsentStore(root).All.Count == 0, "Remove left the consent record");
            consents.Accept(pack, licenseText, DateTimeOffset.UtcNow, "test");

            string notExpected = L.T("The downloaded file is not the expected model.");
            async Task ExpectFailure(string name, Action<FakeModelServer> setup, string? message, Func<string, bool>? messageCheck = null)
            {
                var (failingPack, failing) = Fixture("fixture-pack", 1);
                setup(failing);
                var error = await Fails(ModelPacks.InstallAsync(failingPack, consents, null, CancellationToken.None, root, failing, plenty));
                Check(error is ModelPackException, name + ": expected ModelPackException, got " + error);
                if (message != null) Check(error!.Message == message, name + ": message was " + error.Message);
                if (messageCheck != null) Check(messageCheck(error!.Message), name + ": message was " + error.Message);
                Check(ModelPacks.StateOf(failingPack, root) == ModelPackState.Missing, name + ": a partial pack was left");
                Check(NoStaging(), name + ": staging folder left");
            }
            // A proxy or captive portal answering 200 with a page: rejected as not the expected model.
            await ExpectFailure("oversize", s => s.Serve(pack.Files[1].Url, Bytes(70_001, 2)), notExpected);
            await ExpectFailure("truncated", s => s.Serve(pack.Files[1].Url, Bytes(69_999, 2)), notExpected);
            // No Content-Length (chunked): the streaming guards must stop it, not the header check.
            await ExpectFailure("oversize, no length", s => s.Override = r => r.RequestUri!.ToString() == pack.Files[1].Url ? Unannounced(new UnknownLengthStream(Bytes(70_001, 2))) : null, notExpected);
            await ExpectFailure("truncated, no length", s => s.Override = r => r.RequestUri!.ToString() == pack.Files[1].Url ? Unannounced(new UnknownLengthStream(Bytes(69_999, 2))) : null, notExpected);
            {
                var endless = new UnknownLengthStream(null, endless: true);
                await ExpectFailure("endless, no length", s => s.Override = r => r.RequestUri!.ToString() == pack.Files[1].Url ? Unannounced(endless) : null, notExpected);
                Check(endless.Delivered <= 70_000 + 256 * 1024, "Download kept reading past the catalog size: " + endless.Delivered);
            }
            {
                var previous = ModelPacks.StallTimeout; ModelPacks.StallTimeout = TimeSpan.FromMilliseconds(300);
                string stalled = L.F($"The download from {"huggingface.co"} stopped responding. Check the connection and try again.");
                try { await ExpectFailure("stalled", s => s.Override = r => r.RequestUri!.ToString() == pack.Files[1].Url ? Unannounced(new UnknownLengthStream(null, block: true)) : null, stalled); }
                finally { ModelPacks.StallTimeout = previous; }
            }
            // Redirects: followed to the publisher's own download servers, over https only.
            {
                var (redirected, redirectServer) = Fixture("fixture-redirect", 41);
                consents.Accept(redirected, licenseText, DateTimeOffset.UtcNow, "test");
                string cdn = "https://cdn-lfs.hf.co/repos/fixture/vocab.json";
                redirectServer.Serve(cdn, Bytes(70_000, 42));
                redirectServer.Redirect(redirected.Files[1].Url, cdn);
                await ModelPacks.InstallAsync(redirected, consents, null, CancellationToken.None, root, redirectServer, plenty);
                Check(ModelPacks.StateOf(redirected, root) == ModelPackState.Installed && redirectServer.Requests.Contains(cdn), "Redirect to the publisher's CDN was not followed");
                ModelPacks.Remove(redirected, consents, root);
            }
            await ExpectFailure("redirect to another host", s => { s.Redirect(pack.Files[1].Url, "https://downloads.example.com/vocab.json"); s.Serve("https://downloads.example.com/vocab.json", Bytes(70_000, 2)); }, null, m => m.Contains("vocab.json", StringComparison.Ordinal));
            await ExpectFailure("redirect to http", s => { s.Redirect(pack.Files[1].Url, "http://cdn-lfs.hf.co/vocab.json"); s.Serve("http://cdn-lfs.hf.co/vocab.json", Bytes(70_000, 2)); }, null);
            await ExpectFailure("redirect loop", s => s.Redirect(pack.Files[1].Url, pack.Files[1].Url), null);
            {
                var (_, probe) = Fixture("fixture-pack", 1);
                probe.Redirect(pack.Files[1].Url, "https://downloads.example.com/vocab.json");
                await Fails(ModelPacks.InstallAsync(pack, consents, null, CancellationToken.None, root, probe, plenty));
                Check(!probe.Requests.Any(u => u.Contains("example.com", StringComparison.Ordinal)), "A redirect to a foreign host was requested");
            }

            // Reset or import stops a running download and waits until its files are closed and removed.
            {
                var (slowPack, slowServer) = Fixture("fixture-slow", 51);
                consents.Accept(slowPack, licenseText, DateTimeOffset.UtcNow, "test");
                bool started = false;
                slowServer.Override = r => { if (r.RequestUri!.ToString() != slowPack.Files[0].Url) return null; started = true; return Unannounced(new UnknownLengthStream(null, block: true)); };
                var running = ModelPacks.InstallAsync(slowPack, consents, null, CancellationToken.None, root, slowServer, plenty);
                for (int i = 0; i < 100 && !started; i++) await Task.Delay(20);
                Check(started, "Slow download did not start");
                var busy = Record(() => ModelPacks.Remove(pack, consents, root));
                Check(busy is ModelPackException, "Remove did not refuse while a download was running: " + busy);
                Check(await ModelPacks.StopDownloadsAsync(TimeSpan.FromSeconds(5)), "Downloads did not stop");
                Check(running.IsCompleted && (await Fails(running)) is OperationCanceledException, "Stopped download did not end as cancelled");
                Check(NoStaging(), "Stopped download left staging files");
                var after = Fixture("fixture-slow", 51);
                await ModelPacks.InstallAsync(slowPack, consents, null, CancellationToken.None, root, after.Server, plenty);
                Check(ModelPacks.StateOf(slowPack, root) == ModelPackState.Installed, "Downloads stayed blocked after StopDownloadsAsync");
                ModelPacks.Remove(slowPack, consents, root);
            }

            await ExpectFailure("wrong content", s => s.Serve(pack.Files[1].Url, Bytes(70_000, 99)), notExpected);
            await ExpectFailure("html page", s => s.Override = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Sign in to the network</html>") }, notExpected);
            await ExpectFailure("404", s => s.Serve(pack.Files[1].Url, null!), null, m => m.Contains("vocab.json", StringComparison.Ordinal) && m.Contains("404", StringComparison.Ordinal));
            await ExpectFailure("offline", s => s.Override = _ => throw new HttpRequestException("No such host is known."), null, m => m.Contains("huggingface.co", StringComparison.Ordinal));

            // Cancel after the first file.
            {
                var (cancelPack, cancelServer) = Fixture();
                using var cancellation = new CancellationTokenSource();
                cancelServer.OnRequest = url => { if (url.EndsWith("vocab.json", StringComparison.Ordinal)) cancellation.Cancel(); };
                var error = await Fails(ModelPacks.InstallAsync(cancelPack, consents, null, cancellation.Token, root, cancelServer, plenty));
                Check(error is OperationCanceledException, "Cancel did not throw OperationCanceledException: " + error);
                Check(ModelPacks.StateOf(cancelPack, root) == ModelPackState.Missing && NoStaging(), "Cancel left files");
            }

            // A newer licence text: the installed pack keeps working, the next download asks again.
            await ModelPacks.InstallAsync(pack, consents, null, CancellationToken.None, root, server, plenty);
            var store = new ModelConsentStore(root);
            Check(!store.IsCurrent(pack, licenseText + "\nChanged"), "Changed licence did not ask again");
            Check(ModelPacks.StateOf(pack, root) == ModelPackState.Installed, "Installed pack stopped working after a licence change");
            var newer = pack with { Revision = "rev2", Files = pack.Files.Select(f => f with { Url = f.Url.Replace("/rev1/", "/rev2/") }).ToArray() };
            var unconsented = await Fails(ModelPacks.InstallAsync(newer, store, null, CancellationToken.None, root, server, plenty));
            Check(unconsented is InvalidOperationException, "A new revision downloaded without new consent");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>A body with no announced length, as a chunked response has. Counts what was read; a blocking stream never returns data.</summary>
    internal sealed class UnknownLengthStream(byte[]? data, bool endless = false, bool block = false) : Stream
    {
        private int position;
        internal long Delivered;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (block) { await Task.Delay(Timeout.Infinite, token); return 0; }
            int count = endless ? buffer.Length : Math.Min(buffer.Length, data!.Length - position);
            if (!endless) data.AsMemory(position, count).CopyTo(buffer); else buffer.Span[..count].Fill(7);
            position += count; Delivered += count;
            if (endless && Delivered > 64L * 1024 * 1024) throw new IOException("endless stream was read past 64 MB");
            return count;
        }
    }
    internal static HttpResponseMessage Unannounced(UnknownLengthStream stream) => new(HttpStatusCode.OK) { Content = new StreamContent(stream) };

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
}
