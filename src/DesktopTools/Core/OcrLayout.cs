using System.Windows;

namespace DesktopTools.Core;

public sealed record OcrWordBox(string Text, Rect Bounds, int LineIndex);
public sealed record OcrLayout(int PixelWidth, int PixelHeight, IReadOnlyList<OcrWordBox> Words);
