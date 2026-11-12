using DesktopTools.Core;
using System.IO;

internal static class ModelConsentTests
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var directory = Path.Combine(Path.GetTempPath(), "DesktopTools-consent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pack = new ModelPack("upscale-general", "Image upscaler", "Enlarges images 4x", "images", "Publisher", "Authors",
                "https://huggingface.co/org/repo", "abc123", "bsd-3-clause", "BSD-3-Clause.txt", "attribution", "unknown", ["Invents detail"],
                [new("model.onnx", "https://huggingface.co/org/repo/resolve/abc123/model.onnx", new string('a', 64), 4_600_000)]);
            const string license = "Licence line one\r\nLicence line two\r\n";
            var now = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

            Check(ModelConsentStore.LicenseHash(license) == ModelConsentStore.LicenseHash(license.Replace("\r\n", "\n")), "Line endings change the licence hash");
            Check(ModelConsentStore.LicenseHash(license).Length == 64, "Licence hash is not SHA-256 hex");

            var store = new ModelConsentStore(directory);
            Check(!store.IsCurrent(pack, license) && store.All.Count == 0, "Consent current before Accept");
            store.Accept(pack, license, now, "1.4.0");
            Check(store.IsCurrent(pack, license), "Consent not current after Accept");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Accept left a temporary file");
            Check(File.Exists(Path.Combine(directory, "consents.json")), "consents.json not written");

            var reloaded = new ModelConsentStore(directory);
            Check(reloaded.IsCurrent(pack, license), "Consent lost on reload");
            var record = reloaded.All.Single();
            Check(record.PackId == pack.Id && record.Revision == "abc123" && record.LicenseId == "bsd-3-clause" && record.AcceptedAt == now && record.AppVersion == "1.4.0"
                && record.LicenseSha256 == ModelConsentStore.LicenseHash(license), "Record fields");

            Check(!reloaded.IsCurrent(pack with { Revision = "def456" }, license), "Consent survives a new revision");
            Check(!reloaded.IsCurrent(pack with { LicenseId = "mit" }, license), "Consent survives a new licence id");
            Check(!reloaded.IsCurrent(pack, license.Replace("one", "onE")), "Consent survives a changed licence text");
            Check(reloaded.IsCurrent(pack, license.Replace("\r\n", "\n")), "Line endings alone ask again");

            var other = pack with { Id = "other-pack" };
            reloaded.Accept(other, license, now, "1.4.0");
            reloaded.Accept(pack, license + "changed", now.AddDays(1), "1.4.1");   // a fresh acceptance replaces the old record
            Check(reloaded.All.Count == 2 && reloaded.All.Single(r => r.PackId == pack.Id).AppVersion == "1.4.1", "Re-acceptance did not replace the record");
            reloaded.Remove(pack.Id);
            Check(!reloaded.IsCurrent(pack, license + "changed") && new ModelConsentStore(directory).All.Single().PackId == "other-pack", "Remove did not clear the record");
            reloaded.Remove("missing-pack");

            File.WriteAllText(Path.Combine(directory, "consents.json"), "{broken");
            var damaged = new ModelConsentStore(directory);
            Check(damaged.All.Count == 0 && !damaged.IsCurrent(other, license), "Malformed file did not load as empty");
            Check(File.ReadAllText(Path.Combine(directory, "consents.json.invalid")) == "{broken", "Malformed file not kept as consents.json.invalid");
            damaged.Accept(pack, license, now, "1.4.0");
            Check(new ModelConsentStore(directory).IsCurrent(pack, license), "Store unusable after recovery");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Temporary file left after recovery");

            var absent = new ModelConsentStore(Path.Combine(directory, "not-created"));
            Check(absent.All.Count == 0 && !Directory.Exists(Path.Combine(directory, "not-created")), "Reading created the folder");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
