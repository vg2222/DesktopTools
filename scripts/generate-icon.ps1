# Rasterize the unchanged Microsoft Fluent application-grid color SVG. See Assets/Icons/LICENSE.txt.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$repo = Split-Path $PSScriptRoot -Parent
[xml]$svg = Get-Content -LiteralPath (Join-Path $repo 'src/DesktopTools/Assets/Icons/AppIcon.svg') -Raw
$brushes = @{}
foreach ($gradient in $svg.svg.defs.linearGradient) {
    $brush = [Windows.Media.LinearGradientBrush]::new()
    $brush.MappingMode = [Windows.Media.BrushMappingMode]::Absolute
    $brush.StartPoint = [Windows.Point]::new([double]$gradient.x1, [double]$gradient.y1)
    $brush.EndPoint = [Windows.Point]::new([double]$gradient.x2, [double]$gradient.y2)
    foreach ($stop in $gradient.stop) {
        $offset = if ($stop.offset) { [double]$stop.offset } else { 0 }
        $color = [Windows.Media.ColorConverter]::ConvertFromString($stop.'stop-color')
        $brush.GradientStops.Add([Windows.Media.GradientStop]::new($color, $offset))
    }
    $brushes[$gradient.id] = $brush
}
$pictures = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $dc = $visual.RenderOpen()
    $dc.PushTransform([Windows.Media.ScaleTransform]::new($size / 32.0, $size / 32.0))
    foreach ($path in $svg.svg.path) {
        $fill = [string]$path.fill
        $brush = if ($fill.StartsWith('url(#')) { $brushes[$fill.Substring(5, $fill.Length - 6)] } else { [Windows.Media.BrushConverter]::new().ConvertFromString($fill) }
        $dc.DrawGeometry($brush, $null, [Windows.Media.Geometry]::Parse('F1 ' + $path.d))
    }
    $dc.Pop(); $dc.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [IO.MemoryStream]::new()
    try { $encoder.Save($memory); $pictures += ,@($size,$memory.ToArray()) } finally { $memory.Dispose() }
}
$destination = Join-Path $repo 'src/DesktopTools/Assets/DesktopTools.ico'
$stream = [IO.File]::Create($destination); $writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$pictures.Count)
    $offset = 6 + 16 * $pictures.Count
    foreach ($picture in $pictures) {
        $dimension = if ($picture[0] -eq 256) { 0 } else { $picture[0] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$picture[1].Length); $writer.Write([uint32]$offset)
        $offset += $picture[1].Length
    }
    foreach ($picture in $pictures) { $writer.Write([byte[]]$picture[1]) }
} finally { $writer.Dispose(); $stream.Dispose() }
[IO.File]::WriteAllBytes((Join-Path $repo 'src/DesktopTools/Assets/Icons/AppIcon.png'), $pictures[-1][1])
Write-Host 'Generated multi-resolution ICO from Microsoft Fluent application-grid SVG.'
