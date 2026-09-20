[CmdletBinding()]
param([switch]$PackageOnly)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$galleryPath = Join-Path $repoPath 'docs/screenshots/gallery'
Push-Location $repoPath
try {
    if (!$PackageOnly) {
        & dotnet run --project tests/DesktopTools.IntegrationTests/DesktopTools.IntegrationTests.csproj -c Release -p:NuGetAudit=false -- --github-gallery
        if ($LASTEXITCODE -ne 0) { throw 'Screenshot capture failed.' }
    }
    $captions = [ordered]@{
        '01-home-dark' = @('Home — dark', 'Pinned tools, search and tool collections.')
        '02-capture-tools-dark' = @('Capture tools', 'Screenshots, screen recording and pinned captures.')
        '03-presentation-tools-dark' = @('Presentation tools', 'Drawing and tools for guiding an audience.')
        '04-media-tools-dark' = @('Media tools', 'Image, video and color tools in one place.')
        '05-screenshot-editor-dark' = @('Screenshot editor', 'Arrows, shapes and text over a sample presentation.')
        '06-image-editor-dark' = @('Image editor', 'A populated image workspace with direct controls.')
        '07-image-crop-dark' = @('Crop an image', 'Aspect-ratio presets and adjustable crop handles.')
        '08-image-export-dark' = @('Export an image', 'Preview the format, dimensions and JPEG quality.')
        '09-screen-recorder-dark' = @('Screen recorder', 'A safe demo window selected as the recording source.')
        '10-recording-quality-dark' = @('Recording quality', 'Frame-rate and encoder choices.')
        '11-notes-dark' = @('Notes collection', 'Searchable notes with a local editor.')
        '12-floating-note-dark' = @('Floating note', 'A small note window for a launch checklist.')
        '13-teleprompter-dark' = @('Teleprompter studio', 'Prepare a script and choose reading settings.')
        '14-teleprompter-presentation-dark' = @('Teleprompter presentation', 'A compact reading view for a presentation.')
        '15-qr-dark' = @('QR code', 'Generate a QR code for a link.')
        '16-color-picker-dark' = @('Sampled color', 'Color values and a magnified pixel sample.')
        '17-translation-dark' = @('Offline translation', 'Actual local English-to-Russian translation.')
        '18-text-from-screenshot-dark' = @('Text from screenshot', 'Actual Windows OCR applied to a demo image.')
        '19-video-editor-dark' = @('Video editor', 'The supplied DesktopTools hero clip opened with its timeline.')
        '20-shortcuts-dark' = @('Shortcuts', 'Feature bindings with individual enable switches.')
        '21-appearance-dark' = @('Appearance settings', 'Theme, transparency and animation preferences.')
        '22-privacy-dark' = @('Capture privacy', 'Independent visibility choices for tool windows.')
        '23-updates-dark' = @('Update settings', 'Manual checks and configurable automatic checking.')
        '24-first-run-setup-dark' = @('First-run setup', 'Appearance choices with a live preview in an app modal.')
        '25-help-dark' = @('Help', 'Built-in guidance and tool tours.')
        '26-home-light' = @('Home — light', 'The same dashboard with the light theme.')
        '27-screenshot-editor-light' = @('Screenshot editor — light', 'A sample presentation in the light editor.')
        '28-image-editor-light' = @('Image editor — light', 'The image workspace in the light theme.')
    }
    $dimensions = Import-Csv -LiteralPath (Join-Path $galleryPath 'dimensions.csv')
    $markdown = [Collections.Generic.List[string]]::new()
    $markdown.Add('# DesktopTools screenshot gallery')
    $markdown.Add('')
    $markdown.Add('28 lossless PNGs of the real application UI, rendered directly at 2× scale (192 DPI). Full application views are 2240 pixels wide; smaller utility windows retain their natural proportions. Click any image to open the original.')
    $markdown.Add('')
    $markdown.Add('English interface, dark and light themes, clean demo content. These are application renders, not concept mockups. Translucency is disabled for consistent backgrounds. No personal notes, desktop contents or account information are included.')
    $markdown.Add('')
    $markdown.Add('Recommended for the main README: Home, Screenshot editor, Image crop, Screen recorder, Notes collection and Teleprompter studio. The separate `index.html` provides an offline visual index.')
    $markdown.Add('')
    $cards = [Collections.Generic.List[string]]::new()
    foreach ($name in $captions.Keys) {
        $file = $name + '.png'
        if (!(Test-Path -LiteralPath (Join-Path $galleryPath $file))) { throw "Screenshot missing: $file" }
        $size = $dimensions | Where-Object Filename -EQ $file
        if (!$size) { throw "Dimensions missing for $file" }
        $title = $captions[$name][0]; $description = $captions[$name][1]
        $markdown.Add("## $title")
        $markdown.Add('')
        $markdown.Add("$description $($size.Width) × $($size.Height) px.")
        $markdown.Add('')
        $markdown.Add("[![$title]($file)]($file)")
        $markdown.Add('')
        $safeTitle = [Net.WebUtility]::HtmlEncode($title)
        $safeDescription = [Net.WebUtility]::HtmlEncode($description)
        $cards.Add("<article><a href=`"$file`"><img src=`"$file`" alt=`"$safeTitle`" width=`"$($size.Width)`" height=`"$($size.Height)`" loading=`"lazy`"></a><h2>$safeTitle</h2><p>$safeDescription</p><small>$($size.Width) &times; $($size.Height) px &middot; PNG</small></article>")
    }
    $markdown.Add('## Recreate the gallery')
    $markdown.Add('')
    $markdown.Add('From the repository root, run `./scripts/capture-screenshots.ps1`. This uses an isolated demo profile. Windows English OCR and the supplied `assets/desktop-tools-hero.mp4` are needed for the OCR and video views. It does not change the installed app or user data.')
    Set-Content -LiteralPath (Join-Path $galleryPath 'README.md') -Value $markdown -Encoding utf8
    $html = @"
<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>DesktopTools screenshot gallery</title>
<style>
:root{color-scheme:dark}*{box-sizing:border-box}body{margin:0;background:#101114;color:#f0f2f7;font:16px/1.6 'Segoe UI',sans-serif}header,main,footer{max-width:1500px;margin:auto;padding:48px 32px}header{padding-bottom:12px}h1{font-size:clamp(30px,4vw,52px);line-height:1.1;letter-spacing:-.04em;margin:0 0 16px}header p{max-width:740px;color:#b3bac7}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,420px),1fr));gap:42px 28px}article{min-width:0}article>a{display:flex;align-items:center;justify-content:center;aspect-ratio:1.5;background:#191c22;border:1px solid #303640;border-radius:16px;padding:14px;overflow:hidden}img{width:100%;height:100%;object-fit:contain}a:focus-visible{outline:3px solid #478aff;outline-offset:5px}h2{font-size:19px;margin:16px 0 4px}p{margin:0 0 7px;color:#bcc3cf}small,footer{color:#8e98a9}footer{border-top:1px solid #303640}a{color:#9cc4ff}
</style>
<header><h1>DesktopTools, in detail.</h1><p>28 high-resolution screenshots of the real Windows app. Open any image for the full-size PNG. Dark and light themes, populated tools and clean demo content.</p><a href="README.md">GitHub Markdown gallery</a></header>
<main>$($cards -join "`n")</main><footer>Rendered at 192 DPI &middot; Lossless PNG &middot; Demo profile &middot; No personal content</footer></html>
"@
    Set-Content -LiteralPath (Join-Path $galleryPath 'index.html') -Value $html -Encoding utf8
    $archivePath = Join-Path $repoPath 'artifacts/DesktopTools-GitHub-screenshots.zip'
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($galleryPath, $archivePath)
    Write-Output "Prepared $($captions.Count) screenshots and gallery: $galleryPath"
    Get-Item -LiteralPath $archivePath | Select-Object FullName, Length
} finally { Pop-Location }
