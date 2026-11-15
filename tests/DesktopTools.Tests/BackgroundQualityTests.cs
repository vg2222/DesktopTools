using DesktopTools.Core;
using System.IO;

/// <summary>The background quality pack is exactly the reviewed BiRefNet-lite file, and the model setting falls back to the bundled model.</summary>
internal static class BackgroundQualityTests
{
    public static void Catalog()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "global.json"))) root = Path.GetDirectoryName(root) ?? throw new Exception("Repository root not found");
        using var stream = File.OpenRead(Path.Combine(root, "src", "DesktopTools", "Assets", "Models", "catalog.json"));
        var packs = ModelCatalog.Parse(stream).ToDictionary(p => p.Id);
        Check(packs.TryGetValue("background-quality", out var pack), "Catalog lacks background-quality");
        Check(pack!.Revision == "v1" && pack.Upstream == "https://github.com/ZhengPeng7/BiRefNet", "Upstream or revision");
        Check(pack.LicenseId == "mit" && pack.LicenseFile == "BiRefNet-MIT.txt", "Licence");
        string licence = File.ReadAllText(Path.Combine(root, "src", "DesktopTools", "Assets", "Models", "Licenses", pack.LicenseFile));
        Check(licence.Contains("Copyright (c) 2024 ZhengPeng") && licence.Contains("Permission is hereby granted"), "Licence text is not the publisher's");
        Check(pack.Files.Length == 1, "Exactly one file");
        var file = pack.Files[0];
        Check(file.File == "BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx" && file.Bytes == 224_005_088
            && file.Sha256 == "5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333", "File, size or hash");
        Check(file.Url == "https://github.com/ZhengPeng7/BiRefNet/releases/download/v1/BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx", "URL");
        Check(!string.IsNullOrWhiteSpace(pack.TrainingData) && pack.TrainingData.Contains("DIS5K") && pack.Limitations.Length >= 2, "Training data or limitations");
    }

    public static void Settings()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var settings = new AppSettings();
        Check(settings.BackgroundModel == "Standard", "Default must be the bundled model");
        settings.BackgroundModel = "Huge"; SettingsStore.Validate(settings);
        Check(settings.BackgroundModel == "Standard", "Unknown value kept");
        settings.BackgroundModel = null!; SettingsStore.Validate(settings);
        Check(settings.BackgroundModel == "Standard", "Null value kept");
        settings.BackgroundModel = "Quality"; SettingsStore.Validate(settings);
        Check(settings.BackgroundModel == "Quality", "Valid value lost");
    }
}
