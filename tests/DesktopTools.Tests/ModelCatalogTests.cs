using DesktopTools.Core;
using System.IO;
using System.Text;
using System.Text.Json;

internal static class ModelCatalogTests
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Rejects(ModelPack pack, string rule)
        {
            try { ModelCatalog.Validate(pack); }
            catch (InvalidDataException e) { Check(e.Message.Contains(pack.Id, StringComparison.Ordinal) || rule == "id", "Rejection does not name the pack: " + e.Message); return; }
            throw new Exception("Accepted an unsafe pack: " + rule);
        }
        ModelPack WithFile(ModelPack pack, Func<ModelPackFile, ModelPackFile> change) => pack with { Files = [change(pack.Files[0])] };

        var good = new ModelPack("upscale-general", "Image upscaler", "Enlarges images 4x", "images", "Publisher", "Authors",
            "https://huggingface.co/org/repo", "abc123", "bsd-3-clause", "BSD-3-Clause.txt", "attribution", "unknown", ["Invents detail"],
            [new("model.onnx", "https://huggingface.co/org/repo/resolve/abc123/model.onnx", new string('a', 64), 4_600_000)]);
        ModelCatalog.Validate(good);
        Check(good.TotalBytes == 4_600_000, "Total size");
        foreach (var license in new[] { "mit", "apache-2.0", "bsd-2-clause", "bsd-3-clause", "cc-by-4.0" })
            ModelCatalog.Validate(good with { LicenseId = license });
        Check(ModelCatalog.AcceptedLicenses.Count == 5, "Exactly five accepted licences");
        Rejects(good with { LicenseId = "cc-by-nc-4.0" }, "non-commercial licence");
        Rejects(good with { LicenseId = "MIT" }, "licence ids are exact");
        Rejects(good with { Id = "Bad Id" }, "id");
        Rejects(good with { Id = "-bad" }, "id");
        Rejects(good with { Files = [] }, "no files");
        Rejects(good with { Limitations = [] }, "no limitation");
        Rejects(good with { Limitations = [" "] }, "blank limitation");
        Rejects(good with { Revision = "" }, "no revision");
        Rejects(good with { Publisher = "" }, "no publisher");
        Rejects(good with { LicenseFile = "..\\LICENSE.txt" }, "licence file outside the licence folder");
        Rejects(good with { Upstream = "http://huggingface.co/org/repo" }, "upstream not https");
        Rejects(WithFile(good, f => f with { Url = "http://huggingface.co/org/repo/resolve/abc123/model.onnx" }), "http");
        Rejects(WithFile(good, f => f with { Url = "https://example.com/abc123/model.onnx" }), "unknown host");
        Rejects(WithFile(good, f => f with { Url = "https://huggingface.co.example.com/org/repo/resolve/abc123/model.onnx" }), "look-alike host");
        Rejects(WithFile(good, f => f with { Url = "https://user@huggingface.co/org/repo/resolve/abc123/model.onnx" }), "user info");
        Rejects(WithFile(good, f => f with { Url = "https://huggingface.co:8443/org/repo/resolve/abc123/model.onnx" }), "non-default port");
        Rejects(WithFile(good, f => f with { Url = "https://huggingface.co/org/repo/resolve/main/model.onnx" }), "URL must contain the revision");
        foreach (string branch in new[] { "main", "master", "HEAD", "latest" })
            Rejects(good with { Revision = branch, Files = good.Files.Select(f => f with { Url = f.Url.Replace("/abc123/", "/" + branch + "/") }).ToArray() }, "branch name is not a pinned revision: " + branch);
        Check(ModelCatalog.IsAllowedDownload(new Uri("https://cdn-lfs.hf.co/repos/a/b")) && ModelCatalog.IsAllowedDownload(new Uri("https://cas-bridge.xethub.hf.co/x"))
            && ModelCatalog.IsAllowedDownload(new Uri("https://release-assets.githubusercontent.com/x")) && ModelCatalog.IsAllowedDownload(new Uri("https://huggingface.co/x")), "publisher download servers refused");
        Check(!ModelCatalog.IsAllowedDownload(new Uri("http://cdn-lfs.hf.co/x")) && !ModelCatalog.IsAllowedDownload(new Uri("https://evilhf.co/x"))
            && !ModelCatalog.IsAllowedDownload(new Uri("https://example.com/x")) && !ModelCatalog.IsAllowedDownload(new Uri("https://cdn-lfs.hf.co:8443/x"))
            && !ModelCatalog.IsAllowedDownload(new Uri("https://user@cdn-lfs.hf.co/x")), "a foreign or insecure download server was allowed");
        Rejects(WithFile(good, f => f with { Url = "https://huggingface.co/org/repo/resolve/abc1234/model.onnx" }), "revision must be a whole path segment");
        Rejects(WithFile(good, f => f with { File = "..\\model.onnx" }), "path in file name");
        Rejects(WithFile(good, f => f with { File = "sub/model.onnx" }), "path in file name");
        Rejects(WithFile(good, f => f with { File = "LICENSE.txt" }), "reserved file name");
        Rejects(WithFile(good, f => f with { Sha256 = "abc" }), "short hash");
        Rejects(WithFile(good, f => f with { Sha256 = new string('g', 64) }), "non-hex hash");
        Rejects(WithFile(good, f => f with { Bytes = 0 }), "zero size");
        Rejects(good with { Files = [good.Files[0], good.Files[0] with { Sha256 = new string('b', 64) }] }, "duplicate file name");

        // Hosts the admitted models really use (docs/ai-models.md): Hugging Face, GitHub releases and raw files, Qualcomm's S3 bucket.
        foreach (var url in new[]
        {
            "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx",
            "https://media.githubusercontent.com/media/opencv/opencv_zoo/v0.0.0/models/face.onnx",
            "https://raw.githubusercontent.com/Linzaer/repo/v0.0.0/models/onnx/version-RFB-320.onnx",
            "https://qaihub-public-assets.s3.us-west-2.amazonaws.com/qai-hub-models/models/x/releases/v0.0.0/x.zip",
        })
            ModelCatalog.Validate(good with { Revision = "v0.0.0", Files = [good.Files[0] with { Url = url }] });

        Check(ModelCatalog.Parse(new MemoryStream("[]"u8.ToArray())).Count == 0, "an empty catalog is valid");
        var json = JsonSerializer.Serialize(new[] { good });
        var parsed = ModelCatalog.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Check(parsed.Count == 1 && parsed[0].Id == good.Id && parsed[0].Files[0].Bytes == 4_600_000 && parsed[0].Limitations.SequenceEqual(good.Limitations), "Round trip");
        var duplicate = JsonSerializer.Serialize(new[] { good, good with { Name = "Other" } });
        try { ModelCatalog.Parse(new MemoryStream(Encoding.UTF8.GetBytes(duplicate))); throw new Exception("Accepted two packs with the same id"); }
        catch (InvalidDataException e) { Check(e.Message.Contains(good.Id, StringComparison.Ordinal), "Duplicate message does not name the pack"); }
        var unsafeJson = JsonSerializer.Serialize(new[] { good with { LicenseId = "cc-by-nc-4.0" } });
        try { ModelCatalog.Parse(new MemoryStream(Encoding.UTF8.GetBytes(unsafeJson))); throw new Exception("Parse accepted an unsafe pack"); }
        catch (InvalidDataException) { }
        foreach (var broken in new[] { "{", "null", "[null]", "[{\"Id\":\"x\"}]" })
        {
            try { ModelCatalog.Parse(new MemoryStream(Encoding.UTF8.GetBytes(broken))); throw new Exception("Parse accepted " + broken); }
            catch (InvalidDataException) { }
        }
    }
}
