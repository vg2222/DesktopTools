using System.Windows;
namespace DesktopTools.Core;
public enum RedactionStyle { Solid, Pixelate, Blur }
public enum ScreenshotExportAction { Copy, Save, Apply }
public sealed record RedactionRegion(Int32Rect Bounds, RedactionStyle Style);
