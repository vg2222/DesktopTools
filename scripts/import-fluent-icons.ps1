param([string]$Source = 'artifacts/fluent-icons/package/icons')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$target = Join-Path $repo 'src/DesktopTools/Assets/Icons'
New-Item -ItemType Directory -Force $target | Out-Null
$map = [ordered]@{
 Color='color'; Help='question_circle'; Window='window'; Refresh='arrow_clockwise'; Lock='lock_closed'
 RotateLeft='arrow_rotate_counterclockwise'; RotateRight='arrow_rotate_clockwise'; FlipHorizontal='flip_horizontal'; FlipVertical='flip_vertical'
 Check='checkmark'; Search='search'; Background='color_background'; Select='cursor'; Shield='shield_checkmark'
 ScanText='scan_text'; Prompter='slide_text'; Ruler='ruler'; Click='cursor_click'; Image='image'; Record='record'; Video='video_clip'
 More='more_horizontal'; Notes='note'; Audio='speaker_2'; MutedAudio='speaker_off'; QR='qr_code'; Translate='translate'; Eyedropper='eyedropper'
 Timer='timer'; Star='star'; File='document'; Folder='folder'; Utilities='desktop_toolbox'; Home='home'; Pen='pen'; Draw='pen'; Capture='screenshot'
 Settings='settings'; Shortcuts='keyboard'; About='info'; Highlighter='highlight'; Arrow='arrow_up_right'; Line='line'; Rectangle='square'; Redaction='square'
 Ellipse='circle'; Text='text_font'; Swap='arrow_swap'; Copy='copy'; Save='save'; Plus='add'; Play='play'; Pause='pause'; Stop='stop'; Number='number_circle_1'
 Eraser='eraser'; Undo='arrow_undo'; Redo='arrow_redo'; Clear='delete'; Close='dismiss'; Minimize='subtract'; Maximize='maximize'; Crop='crop'; Interact='cursor'
 Present='presenter'; Monitor='desktop'; Notifications='alert'; Behavior='cursor_hover'; Laser='target'; Spotlight='flashlight'; Freeze='pause'; Pin='pin'; Profiles='options'; Chevron='chevron_right'
}
$geometry = [ordered]@{}
foreach ($entry in $map.GetEnumerator()) {
 $name = $entry.Value + '_24_regular.svg'
 $file = Join-Path (Join-Path $repo $Source) $name
 if (-not (Test-Path -LiteralPath $file)) { throw "Missing Fluent asset $name" }
 Copy-Item -LiteralPath $file -Destination (Join-Path $target $name)
 [xml]$svg = Get-Content -LiteralPath $file -Raw
 $paths = @($svg.svg.path | ForEach-Object { $_.d })
 if ($paths.Count -eq 0) { throw "No paths in $name" }
 $geometry[$entry.Key] = 'F1 M0,0 L0,0 M24,24 L24,24 M0,0 ' + ($paths -join ' M0,0 ')
}
$geometry | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'geometry.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path (Join-Path $repo $Source) 'apps_32_color.svg') -Destination (Join-Path $target 'AppIcon.svg')
@"
Fluent System Icons 1.1.341, Microsoft Corporation. MIT license.
Source: https://github.com/microsoft/fluentui-system-icons
Package: https://registry.npmjs.org/@fluentui/svg-icons/-/svg-icons-1.1.341.tgz
SVG assets are unchanged. geometry.json concatenates their paths for WPF, retaining the 24-unit viewbox.
AppIcon.svg is apps_32_color.svg. The ICO is rasterized from this original asset.
"@ | Set-Content -LiteralPath (Join-Path $target 'ATTRIBUTION.md') -Encoding utf8
