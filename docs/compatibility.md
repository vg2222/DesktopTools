# Compatibility

DesktopTools 1.2.0 targets Windows 11 x64. The app is self-contained; screen recording additionally requires Microsoft Visual C++ x64 Redistributable and Windows Media Foundation.

## Verified locally

- Release build, automated core/render/native harnesses, and bundled-app startup/shutdown.
- Region capture, drawing state restoration, image editing, local OCR/translation processing, and generated output validation in the development environment.
- Screen recorder source, pause, cancellation, and finalization lifecycle with synthetic inputs.
- H.264 MP4/MOV, MJPEG AVI, WMV/WMA, VP9/Opus MKV, and HEVC/AAC MP4 synthetic imports and exports on the development machine.

## Environment-dependent behavior

| Area | Current boundary |
| --- | --- |
| Mixed DPI | Logical viewport checks pass; physical 150%/200% multi-display input and hot-unplug still need broader testing. |
| Screen sharing | Capture exclusion and sharing-only blackout depend on the viewer's capture method. A second-device Discord/Teams observation is still required. |
| Audio output switching | Windows capability probing and fallback are implemented; audible switching depends on the device and driver. |
| Recording audio | Microphone and system-audio controls are implemented; real-device synchronization and hour-long runs need broader hardware testing. |
| 144 FPS | Available as a quality target; unique decoded-frame throughput depends on the source, encoder, GPU, and system load. |
| Application-window sharing | Separate ink windows may be omitted by the sharing application. |

Secure desktops, elevation screens, and protected/system windows are outside the supported capture and window-control scope. Capture exclusion is a compatibility feature, not a security boundary. Always confirm sensitive sharing behavior from the receiving device.

When reporting a compatibility result, include the DesktopTools version, Windows build, display scales, GPU/encoder, capture or sharing application and version, selected source, and what the receiving viewer observed.
