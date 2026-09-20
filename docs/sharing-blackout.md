# Sharing blackout (experimental)

**Present → Screen blackout**, or **Settings → Privacy**, has a shared toggle:

- Off (default): opaque black windows cover all monitors locally and in compatible full-monitor shares.
- On: transparent click-through masks request blank capture content while leaving the desktop visible locally.

Changing this setting stops active blackout; activate it again to use the new mode. Escape, a display configuration change or quitting DesktopTools removes all masks. The preference is saved locally and included in profiles. No masks activate automatically at startup.

The settings permanently warn that the screen may still be visible in screen-sharing apps and recommend first checking the viewer's output. Activation also displays a warning. Test using another viewer/device with the exact sharing application and source type you will use. Sharing an individual application window may bypass the mask entirely. Apps that ignore affinity, elevated/topmost windows appearing above masks, different capture backends and display changes can expose content. This is not a security guarantee or a replacement for stopping a share.

## Implementation and evidence

App-owned top-level transparent WPF windows use `WDA_MONITOR` (1), not `WDA_EXCLUDEFROMCAPTURE` (0x11). Masks are tagged as presentation surfaces so the control-window privacy preference cannot exclude them. They are topmost, non-activating and click-through, with no polling or animation. Affinity failure closes all masks and reports an error. The implementation never changes another process's window affinity.

[Microsoft documents the affinity API and its limitations](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity). API success only establishes that Windows accepted a request, not that a viewer sees black.

`dotnet run --project tests/DesktopTools.IntegrationTests -c Release -- --blackout-only`

On the development Windows desktop, a red helper window was captured red before and after masking, and black under transparent WDA_MONITOR masks using GDI BitBlt/CAPTUREBLT. Tests also checked the actual HUD masks on every connected monitor, transparent local surfaces, click-through configuration, mode-change cleanup, Escape, five localized settings layouts and warning notification severity. Core tests checked opt-in default and persistence. No keyboard or mouse input was injected.

| Capture combination | Status |
| --- | --- |
| Local GDI full-desktop capture, known helper region | Passed on development machine |
| Discord full-monitor viewer output | Not tested |
| Teams / Zoom / OBS viewer output | Not tested |
| Individual application/window sharing | Not supported as a dependable blackout method |
| Mixed DPI, HDR, exclusive fullscreen, other GPUs | Not tested |

Do not relabel these combinations based only on successful API calls or the app's own preview.
