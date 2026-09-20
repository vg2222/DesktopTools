# Architecture

DesktopTools is one WPF executable, with Windows Forms used for the tray icon. `App` owns the single-instance mutex/activation signal; `AppController` coordinates settings, windows, shortcuts and lifecycle. Closing settings keeps the tray alive; Quit disposes owned resources.

`Core` contains the annotation document, bounded undo/redo, settings and explicit Hidden / Draw / Interact / Capture states. A session stays attached to one monitor. Disabling drawing or a display topology change dismisses its input surface safely. Annotations are monitor-local logical coordinates.

`UI` renders the same annotation model both live and into output. The canvas and palette have separate top-level HWNDs. Draw takes local pointer input; Interact adds transparent/no-activate styles and restores foreground focus. The transparent canvas never receives the optional Acrylic backdrop. Settings and controls use native material when supported and an opaque fallback otherwise.

`Native` isolates monitor enumeration, physical pixel placement, GDI capture, display affinity, foreground helpers, per-user startup, profiles and RegisterHotKey. Hotkey replacement retains existing combinations, acquires every new combination before retiring old registrations, and commits action mappings only after success. Swapping two actions does not release either combination.

The screenshot pipeline hides app windows, waits for desktop composition, captures physical desktop pixels once, restores UI, and composites annotations once before cropping/output. Monitor bounds may have negative origins. Selection and annotation coordinates are local DIPs; crop coordinates are local physical pixels. PNG export dimensions follow the pixel crop, not virtual desktop origins.

`Extras` contains transient laser/spotlight windows, pinned captures and solid-cover screenshot redaction. Redaction exports a flattened copy while leaving the original image unchanged. Profiles copy only explicitly listed tool, presentation and shortcut preferences; startup, theme, monitor, save destination and palette coordinates remain general settings.

Settings and profiles use versioned JSON and atomic replacement. Malformed settings are retained for diagnosis before defaults are restored. Future schemas are not silently overwritten. There is no backend or persistent annotation storage. Global hooks and global input blocking are not used.
