using System.Text.Json;
using System.Windows.Media;

namespace DesktopTools.UI;

// Microsoft Fluent System Icons, MIT. Original SVGs and attribution are in Assets/Icons.
internal static class ToolIcons
{
    private static readonly Dictionary<string, Geometry> Icons = Load();
    internal static Geometry GeometryFor(string name) => Icons.GetValueOrDefault(name, Icons["Chevron"]);
    private static Dictionary<string, Geometry> Load()
    {
        using var stream = typeof(ToolIcons).Assembly.GetManifestResourceStream("DesktopTools.Icons")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!.ToDictionary(pair => pair.Key, pair =>
        {
            var geometry = Geometry.Parse(pair.Value); geometry.Freeze(); return geometry;
        });
    }
}
