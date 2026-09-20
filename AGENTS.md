# DesktopTools repository guidance

This file is for people and automated collaborators changing the project. For contribution etiquette and the pull-request workflow, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Project layout

DesktopTools is a Windows 11 x64 WPF application. The .NET SDK version is pinned in [global.json](global.json).

- `src/DesktopTools/Core` owns application state, settings, annotation history, and persistence.
- `src/DesktopTools/Native` isolates Windows APIs, monitor coordinates, capture, display affinity, hotkeys, and resource lifetimes.
- `src/DesktopTools/UI` owns windows, controls, and rendering.
- `src/DesktopTools/Extras` contains the remaining desktop tools.
- `installer/` contains the installer; `tests/` contains automated checks.
- `docs/architecture.md` explains key boundaries and capture behavior.

Use the existing state and service boundaries when adding features. Keep coordinate conversions explicit across WPF DIPs, monitor-local coordinates, and physical pixels. Release native resources and unregister callbacks or hotkeys on every exit path.

## Preserve user data and privacy

Never alter an original image or video as a side effect of previewing or exporting a copy. Preserve notes and settings across updates and repairs; constrain deletion to app-managed data when the user explicitly chooses a reset or uninstall option. Do not commit logs, screenshots, recordings, credentials, personal paths, or generated test media that contain private desktop content.

Capture exclusion is best-effort Windows behavior. Keep audience-visible effects separate from recorder controls and other private UI, and do not claim that a surface is hidden from a remote viewer without an actual viewer check. Avoid adding telemetry, global input blocking, or process injection as incidental changes. See [SECURITY.md](SECURITY.md) for vulnerability reporting.

## Build and verify

On Windows, fetch the checksum-verified translation models once, then build and run the relevant checks:

```powershell
./scripts/fetch-translation-models.ps1
./scripts/build.ps1
./scripts/test.ps1
```

Use focused tests for changed state, native interop, capture pixels, settings recovery, shortcuts, and media output. UI, capture, and sharing changes also need applicable cases from [docs/manual-testing.md](docs/manual-testing.md). State what was actually tested, on which environment; mark anything not run as **Not tested**. A passing build or display-affinity API call does not establish remote sharing behavior.

Keep user-facing strings consistent with the supported languages; see [docs/localization.md](docs/localization.md). Keep generated output and local release bundles out of the repository. Do not change a release checksum or scan reference without verifying it against the exact final binary.
