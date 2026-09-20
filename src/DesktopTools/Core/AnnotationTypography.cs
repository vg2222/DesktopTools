using System.Windows.Media;

namespace DesktopTools.Core;

public static class AnnotationTypography
{
    public const string BundledFamily = "/DesktopTools;component/Assets/Fonts/#Inter";
    public static string DefaultFamily { get; } = Fonts.SystemFontFamilies.Any(font => font.Source.Equals("Montserrat", StringComparison.OrdinalIgnoreCase)) ? "Montserrat" : BundledFamily;
    // PackUriHelper also initializes the pack URI parser in headless render/export hosts.
    private static readonly Uri FontBaseUri = System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
    public static FontFamily Resolve(string family) => new(FontBaseUri, family);
}
