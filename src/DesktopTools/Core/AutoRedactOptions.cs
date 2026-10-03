namespace DesktopTools.Core;

public sealed class AutoRedactOptions
{
    public static readonly string[] AvailableCategories = ["email", "phone", "ip", "path", "credential", "username", "account", "serial"];
    public bool Enabled { get; set; }
    public string Language { get; set; } = "en-US";
    public string Style { get; set; } = "Solid";
    public string[] Categories { get; set; } = [.. AvailableCategories];
    public void Normalize()
    {
        Language = string.IsNullOrWhiteSpace(Language) || Language.Length > 40 ? "en-US" : Language.Trim();
        if (Style is not ("Solid" or "Pixelate" or "Blur")) Style = "Solid";
        Categories = (Categories ?? AvailableCategories).Where(AvailableCategories.Contains).Distinct().ToArray();
    }
}
