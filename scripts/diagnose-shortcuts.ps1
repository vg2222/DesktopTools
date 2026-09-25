# Tests whether Windows will register DesktopTools' enabled global shortcuts.
# Quit DesktopTools from its tray menu before running this script.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (Get-Process -Name DesktopTools -ErrorAction SilentlyContinue) {
    Write-Error 'DesktopTools is still running. Right-click its tray icon, choose Quit, and run this script again.'
    exit 2
}

$settingsPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DesktopTools\settings.json'
$settings = $null
if (Test-Path -LiteralPath $settingsPath) {
    try { $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch {
        Write-Error 'DesktopTools settings could not be read. The file was not changed.'
        exit 2
    }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DesktopToolsShortcutProbe {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr window, int id);
}
'@

$shortcuts = @(
    @{ Action='Translate'; Name='Translate selected text'; Setting='TranslationShortcut'; Default='Ctrl+Alt+R' },
    @{ Action='ScreenText'; Name='Scan screen text'; Setting='ScreenTextShortcut'; Default='Ctrl+Alt+E' },
    @{ Action='QuickWheel'; Name='Quick actions wheel'; Setting='QuickWheelShortcut'; Default='Ctrl+Alt+Q' },
    @{ Action='Teleprompter'; Name='Teleprompter play / pause'; Setting='TeleprompterShortcut'; Default='Ctrl+Alt+M' },
    @{ Action='PinWindow'; Name='Pin active window'; Setting='WindowPinShortcut'; Default='Ctrl+Alt+T' },
    @{ Action='Eyedropper'; Name='Screen eyedropper'; Setting='EyedropperShortcut'; Default='Ctrl+Alt+P' },
    @{ Action='Draw'; Name='Draw / interact'; Setting='DrawShortcut'; Default='Ctrl+Alt+D' },
    @{ Action='Capture'; Name='Capture region'; Setting='CaptureShortcut'; Default='Ctrl+Alt+S' },
    @{ Action='HidePalette'; Name='Hide / show palette'; Setting='HidePaletteShortcut'; Default='Ctrl+Alt+H' },
    @{ Action='Laser'; Name='Laser pointer'; Setting='LaserShortcut'; Default='Ctrl+Alt+L' },
    @{ Action='Spotlight'; Name='Spotlight'; Setting='SpotlightShortcut'; Default='Ctrl+Alt+O' },
    @{ Action='Freeze'; Name='Freeze frame'; Setting='FreezeShortcut'; Default='Ctrl+Alt+F' },
    @{ Action='Recorder'; Name='Screen recorder'; Default='Ctrl+Alt+Shift+R'; Optional=$true },
    @{ Action='Images'; Name='Image tools'; Default='Ctrl+Alt+Shift+I'; Optional=$true },
    @{ Action='Video'; Name='Video editor'; Default='Ctrl+Alt+Shift+V'; Optional=$true },
    @{ Action='QrCodes'; Name='QR codes'; Default='Ctrl+Alt+Shift+Q'; Optional=$true },
    @{ Action='Notes'; Name='Floating notes'; Default='Ctrl+Alt+Shift+N'; Optional=$true },
    @{ Action='FileShelf'; Name='File shelf'; Default='Ctrl+Alt+Shift+F'; Optional=$true },
    @{ Action='AudioControls'; Name='Audio controls'; Default='Ctrl+Alt+Shift+A'; Optional=$true },
    @{ Action='PinLast'; Name='Pin latest screenshot'; Default='Ctrl+Alt+Shift+P'; Optional=$true },
    @{ Action='AidClickIndicators'; Name='Click indicators'; Default='Ctrl+Alt+Shift+1'; Optional=$true },
    @{ Action='AidShortcutDisplay'; Name='Shortcut display'; Default='Ctrl+Alt+Shift+2'; Optional=$true },
    @{ Action='AidStopwatch'; Name='Stopwatch'; Default='Ctrl+Alt+Shift+3'; Optional=$true },
    @{ Action='AidCountdown'; Name='Countdown'; Default='Ctrl+Alt+Shift+4'; Optional=$true },
    @{ Action='AidRuler'; Name='Screen ruler'; Default='Ctrl+Alt+Shift+5'; Optional=$true },
    @{ Action='AidBlackout'; Name='Screen blackout'; Default='Ctrl+Alt+Shift+6'; Optional=$true }
)

function Get-GestureParts([string]$gesture) {
    [uint32]$modifiers = 0
    [uint32]$key = 0
    foreach ($raw in $gesture.Split('+')) {
        $part = $raw.Trim().ToUpperInvariant()
        $modifier = switch ($part) { 'CTRL' { 2 } 'CONTROL' { 2 } 'ALT' { 1 } 'SHIFT' { 4 } 'WIN' { 8 } 'WINDOWS' { 8 } default { 0 } }
        if ($modifier -ne 0) {
            if (($modifiers -band $modifier) -ne 0) { return $null }
            $modifiers = $modifiers -bor $modifier
            continue
        }
        if ($key -ne 0) { return $null }
        if ($part -cmatch '^[A-Z0-9]$') { $key = [uint32][char]$part }
        elseif ($part -match '^F([1-9]|1[0-9]|2[0-4])$') { $key = [uint32](111 + [int]$Matches[1]) }
        else {
            $key = switch ($part) {
                SPACE { 32 } TAB { 9 } ENTER { 13 } RETURN { 13 } ESC { 27 } ESCAPE { 27 }
                BACKSPACE { 8 } DELETE { 46 } DEL { 46 } INSERT { 45 } INS { 45 }
                HOME { 36 } END { 35 } PAGEUP { 33 } PAGEDOWN { 34 }
                LEFT { 37 } UP { 38 } RIGHT { 39 } DOWN { 40 } default { 0 }
            }
        }
        if ($key -eq 0) { return $null }
    }
    if ($modifiers -eq 0 -or $key -eq 0 -or $key -eq 123) { return $null }
    return @{ Modifiers=[uint32]$modifiers; Key=[uint32]$key }
}

$results = @()
$seen = @{}
$nextId = 1000
foreach ($shortcut in $shortcuts) {
    $enabled = -not $shortcut.Optional
    $enabledProperty = if ($settings -and $settings.ShortcutEnabled) { $settings.ShortcutEnabled.PSObject.Properties[$shortcut.Action] } else { $null }
    if ($enabledProperty) { $enabled = [bool]$enabledProperty.Value }
    if (-not $enabled) { continue }

    $gesture = $shortcut.Default
    if ($shortcut.Optional) {
        $property = if ($settings -and $settings.FeatureShortcuts) { $settings.FeatureShortcuts.PSObject.Properties[$shortcut.Action] } else { $null }
    } else {
        $property = if ($settings) { $settings.PSObject.Properties[$shortcut.Setting] } else { $null }
    }
    if ($property) { $gesture = [string]$property.Value }
    if ([string]::IsNullOrWhiteSpace($gesture)) { continue }
    $parts = Get-GestureParts $gesture
    $status = 'Available'
    if ($null -eq $parts) { $status = 'Invalid gesture' }
    elseif ($seen.ContainsKey("$($parts.Modifiers):$($parts.Key)")) { $status = 'Duplicate DesktopTools shortcut' }
    else {
        $seen["$($parts.Modifiers):$($parts.Key)"] = $true
        $nextId++
        $registered = [DesktopToolsShortcutProbe]::RegisterHotKey([IntPtr]::Zero, $nextId, ($parts.Modifiers -bor 0x4000), $parts.Key)
        if ($registered) {
            try { $status = 'Available' }
            finally { [void][DesktopToolsShortcutProbe]::UnregisterHotKey([IntPtr]::Zero, $nextId) }
        } else {
            $nativeCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            $status = "Blocked by Windows or another app (error $nativeCode)"
        }
    }
    $results += [pscustomobject]@{ Action=$shortcut.Name; Shortcut=$gesture; Result=$status }
}

Write-Host 'DesktopTools shortcut registration test'
Write-Host 'This checks enabled shortcuts without changing settings or capturing the desktop.'
if (-not $settings) { Write-Host 'No settings file found; using fresh-install defaults.' }
$results | Format-Table -AutoSize | Out-Host
Write-Host 'Available means Windows accepted registration. It does not prove DesktopTools receives a keypress.'
Write-Host 'If every shortcut is available but none works, start DesktopTools, open Shortcuts, and check the warning there.'
if ($results.Result -match '^Blocked|^Invalid|^Duplicate') { exit 1 }
