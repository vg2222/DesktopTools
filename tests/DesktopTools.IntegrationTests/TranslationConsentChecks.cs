using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopTools.Core;
using DesktopTools.Extras;

/// <summary>
/// Spec 2.4: a new translation pack downloads only after its licence was accepted in the consent window; packs downloaded before this
/// update keep working without asking; removing a pack removes its acceptance. No network: the handler refuses every request.
/// </summary>
internal static class TranslationConsentChecks
{
    internal static async Task RunAsync()
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        foreach (var pack in TranslationPacks.Catalog)
        {
            var consent = TranslationPacks.ConsentPack(pack);
            ModelCatalog.Validate(consent);
            Check(consent.Id == "translation-" + pack.Direction, "Consent pack id: " + consent.Id);
            Check(consent.Revision == pack.Revision && consent.Files.Length == pack.Files.Length, "Consent pack does not pin the catalog files: " + pack.Direction);
            string shipped = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Translation", pack.License == "apache-2.0" ? "EN-RU-APACHE-2.0.txt" : "CC-BY-4.0.txt"));
            Check(ModelConsentStore.LicenseHash(ModelPacks.LicenseText(consent)) == ModelConsentStore.LicenseHash(shipped), "Licence shown differs from the one installed with the pack: " + pack.Direction);
        }

        string root = Path.GetFullPath("translation-consent-" + Guid.NewGuid().ToString("N"));
        string models = Path.Combine(root, "models"), translations = Path.Combine(root, "translation");
        try
        {
            var consents = new ModelConsentStore(models);
            var german = TranslationPacks.ConsentPack(TranslationPacks.Find("en-de"));
            // (b) Not accepted: refused before any request, nothing created.
            try { await TranslationPacks.InstallAsync("en-de", consents, null, CancellationToken.None, translations, OcrPackChecks.NoNetwork()); throw new Exception("Download started without consent"); }
            catch (InvalidOperationException e) when (!e.Message.Contains("Network used")) { }
            Check(!Directory.Exists(Path.Combine(translations, "en-de")), "A pack folder was created without consent");
            // (c) Accepted: the download is attempted (the test handler refuses the network).
            consents.Accept(german, ModelPacks.LicenseText(german), DateTimeOffset.Now, "test");
            bool reached = false;
            try { await TranslationPacks.InstallAsync("en-de", consents, null, CancellationToken.None, translations, OcrPackChecks.NoNetwork()); }
            catch (Exception e) when (e.Message.Contains("Network used") || e.InnerException?.Message.Contains("Network used") == true) { reached = true; }
            Check(reached, "An accepted pack did not start downloading");
            Check(!Directory.Exists(Path.Combine(translations, "en-de")), "A failed download left a pack folder");
            // (d) A pack downloaded before this update (no record) keeps working.
            string old = Path.Combine(translations, "de-en");
            Directory.CreateDirectory(old);
            foreach (var file in TranslationPacks.Find("de-en").Files) File.WriteAllText(Path.Combine(old, file.File), "x");
            Check(TranslationPacks.DirectoryFor("de-en", translations) == old, "An older pack without a consent record was hidden");
            // (e) Remove deletes the acceptance with the files.
            string accepted = Path.Combine(translations, "en-de");
            Directory.CreateDirectory(accepted);
            foreach (var file in TranslationPacks.Find("en-de").Files) File.WriteAllText(Path.Combine(accepted, file.File), "x");
            TranslationPacks.Remove("en-de", translations, consents);
            Check(!Directory.Exists(accepted), "Remove kept the files");
            Check(!new ModelConsentStore(models).IsCurrent(german, ModelPacks.LicenseText(german)), "Remove kept the licence acceptance");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
