# Manual Windows acceptance checklist

Record Windows build, DesktopTools build, monitor resolutions/scales and the observed result for each run. Automated tests are supporting evidence, not a replacement for these checks. Use ordinary non-sensitive test content.

## Drawing and input

1. Open another process with a clickable button and editable text. Press Ctrl+Alt+D and draw over the button. Expect ink and no underlying click.
2. Press Draw again. Click, scroll and type in that other process; ink remains visible. Press Draw to resume. Repeat with the palette previously focused; no stuck keyboard or mouse capture is acceptable.
3. Exercise pen/highlighter, arrow, line, rectangle, ellipse, text and whole-object eraser. Undo/redo across creation, erase and Clear all; after undo, a new action must discard redo. Verify a single pen dot.
4. Escape during a shape/text edit cancels it first. Escape again hides drawing. Repeat from palette focus and after Alt+Tab. Text entry must not invoke ordinary drawing-tool shortcuts.
5. Hide and restore annotations. They stay attached to their original monitor. Explicitly start a new monitor session and confirm old drawings do not silently move onto unrelated content.
6. Assign an occupied shortcut, including a transaction with another available replacement. Expect an error and all old shortcuts still functioning. Test swaps, disable/re-enable each feature, and Quit.
7. On a fresh settings file, reserve one default global shortcut in another process before starting DesktopTools. The Shortcuts page and first-run setup must identify the conflict; all other enabled shortcuts still work. Release the reservation and select Retry shortcuts; the blocked shortcut should then work without restarting DesktopTools.

## Capture and displays

1. Draw across a recognizable grid, capture a rectangle and compare the saved PNG to the selected pixel bounds. Expect exactly one ink copy, no palette, dim layer, selection border, tooltip or notification. Repeat with annotations disabled.
2. Escape during selection: no output, previous drawing state restored. Repeat from Hidden, Draw and Interact, including freeze mode. Cancel a save dialog and simulate clipboard contention; session remains usable and failure is explained.
3. Repeat at 100%, 150% and 200%, including a secondary monitor left/above primary with negative desktop coordinates. Check pointer-to-ink alignment, crop edges, text and strokes. Record actual scaling separately from synthetic coordinate tests.
4. Disconnect/change resolution of the active monitor while drawing and selecting. Expect safe dismissal and no invisible input blocker. Next activation must use current geometry.
5. Open tooltips/popovers while requesting capture exclusion. Test both enabled and disabled exclusion and manual hide-palette fallback.

## Presentation, redaction and profiles

1. Laser movement leaves a fading trail without permanent annotations; spotlight follows the pointer. Toggle each effect and disable it while active. Confirm underlying interaction matches the intended mode and no effect window survives Quit.
2. Freeze a changing clock/video, draw over the still and capture. Verify the frozen background is used once, while the underlying application continues running. Leave freeze and confirm the live desktop returns.
3. Pin a capture, resize it and change opacity. Close it and confirm no stale topmost window remains.
4. Add solid redaction covers at image edges in a scaled editor. Undo a cover, export a copy and inspect PNG pixels: covered areas must be fully opaque black and the original unchanged. Do not distribute the original sensitive image by mistake.
5. Apply Everyday, Meetings and Teaching profiles, save/override/delete a custom preset, and restart. Tool preferences change while theme/startup/monitor/save path/palette position stay unchanged. A conflicting profile shortcut must leave active preferences and registrations unchanged.

## Lifecycle and appearance

- On a fresh profile, launch under each supported Windows display language and confirm both the app and installer start in that language. On an unsupported Windows language, confirm English. Change language on the first setup step and in Settings → Behavior; the app should relaunch automatically and retain the choice on later launches. Change language while the installer and uninstaller are visibly open: the window must stay open, the language menu must show its five offline flag icons, and an uninstall data choice already selected must remain selected. Confirm the new app uses the installer language; updating an existing install must preserve its saved language.
- In Settings → Updates, select 15 and 30 minutes, restart, and confirm the selection persists. Check the update status, version panel, and actions at narrow window sizes in both themes. In Screen Recorder, verify source and preview actions below the preview and start, pause, and stop in the bottom bar.

1. Close settings; tray remains. Launch again; existing settings opens. Quit; tray, effects, shortcuts and all owned windows disappear. Restart and confirm preferences.
2. Enable startup at login and sign out/in. Disable it and repeat. Check only DesktopTools' current-user Run entry changes. Test from the portable executable in its final folder.
3. Back up settings, replace JSON with malformed content and start. Expect concise recovery status, retained damaged file and defaults. Restore the backup afterward. Check future-version data is preserved.
4. Exercise light/dark/system, transparency on/off, Windows transparency disabled and high contrast. Check readable opaque fallback, rounded windows, resizing, dragging and visible keyboard focus. Inspect at reduced motion settings.
5. Extract the portable zip into a fresh directory on a Windows x64 machine without a separately installed .NET runtime. Launch, draw, capture, quit and relaunch. Preserve bundled notices.

## External sharing

Share the full monitor in Discord to a second participant/device. The remote observer must confirm whether ink appears, palette/popovers appear, or black rectangles/artifacts occur. Repeat with exclusion disabled and manual hiding. Also characterize application-window sharing separately. Enter observations in `compatibility.md`; SetWindowDisplayAffinity success alone is not a pass.

## 2.1.2 manual follow-up
- Cursor monitor mode: activate Draw/Laser/Spotlight/Freeze on A, hide or interact, move pointer to B and activate again. Verify B is used. Drawing starts fresh on B; capture cancellation retains A's document.
- Capture B with A's ink or frozen session: exported pixels must show B without A's ink. Check at mixed DPI and negative monitor origins.
- Inspect notifications on bright/dark desktop wallpapers: one rounded silhouette, no gray rectangular edge. Hover pauses expiry; dismissal leaves latest screenshot available.
- Pin wide, tall and tiny screenshots; drag, proportional resize, hover controls, compact/right-click menus, keyboard access and close. Check across mixed-DPI monitors.
- Keyboard navigation in rounded shortcut fields, dropdowns, sliders and editor buttons; Windows reduced-motion/high-contrast modes. No zoom or slide animation on buttons/pages.

## 1.0.0 update follow-up
- Move laser and spotlight across every physical display and seams at mixed DPI. Test All and Selected settings; verify consistent laser alpha and smooth spotlight motion.
- Select a capture across a negative-origin seam; verify crop pixels and ink alignment. Cancel delayed capture with Escape and disable Capture during countdown. Repeat region; disconnect a monitor and confirm a fresh selection is required.
- Enable click indicators and shortcut display, try ordinary typing and AltGr: no typed text should appear. Disable both and verify no rings/labels remain. Timer pause/reset/close, dragging and Escape.
- Draw then open color/options/monitor dialog; ensure every control stays above ink. Text editor uses Montserrat when installed and Segoe UI otherwise. Test numbered markers, filled shapes, Shift snapping and undo.
- Startup at login defaults on only for new settings. Existing explicit off survives update; toggling off removes this app's startup value.

## September 7 additions
- Activate laser: its most visible state is at most50%opacity and it fades down; stopcontrol has even outerspacing.
- Spotlight must darken bright content without tinting it white/gray. Change app/system theme while active.
- Freeze, Escape, change underlying app content, freeze again: newcontent appears.
- V select: drag object or cornerhandles, Ctrl+D duplicate, Delete selected, undo/redo, Escape midtransform cancels withoutchangingdocument.
- Use arbitrary colors from hue/saturation/brightness controls and hex; keyboardarrows adjust spectrum. Editor eyedropper samples flattened visiblepixels; redaction covers are respected by OCR/export.
- Countdown pause/reset, blackout Escape, ruler rotate/length/drag; close all releaseswindows. Rule markings are logicalpixels, not physicalmillimeters.

## Feature settings and shortcut recorder
- Home feature label/chevron opens its settings; toggling an aid does not navigate. Closing an aid refreshes its Home switch.
- Record a chord, review it, Record again, Save; try an occupied chord and confirm the old binding remains after Cancel. OS-reserved combinations can still be handled by Windows while recording.
- Change click/shortcut/countdown/ruler preferences, save a profile, change preferences and reapply; appearance restores without starting tools.
- Rapid shortcut replacement during dismissal should stay visible for the new duration; reduced motion and animation preference disable movement.
- Stopwatch bottom padding matches top/side surface padding.

### Desktop utilities manual checks

- Drag files/folders from Explorer into shelf, drag an entry to another folder/app, confirm copy semantics and original unchanged. Hide/reopen shelf retains entries; restart clears it.
- Create note, edit title/text, resize/move, close/reopen and restart app; verify text retained. Delete only through manager confirmation. Disable/re-enable notes and verify persistence.
- Play audio in two apps; adjust one volume/mute and confirm the other unchanged. Change default playback device and refresh. Close mixer and verify no refresh timer remains. No audio changes were performed during automated checks.

## GitHub update acceptance

Use a disposable Windows account or VM with two stable published versions and their checksum manifests. These steps exercise real installation and cannot be established by protocol fixtures alone.

- Install the earlier version, create a note and change a setting. Confirm startup/manual checks find the newer release; change the background interval and verify disabling it leaves manual checks available.
- Leave the update notification without any input, then return. Open release notes and confirm the notification remains, reports the browser action and uses the extended duration. When its message expands or contracts, confirm the notification resizes smoothly and nearby notices do not overlap. During download, confirm the rounded progress bar uses the app accent in both light and dark themes and advances smoothly. Dismiss the notice and confirm the sidebar and Updates page still offer the update.
- Choose Update in background. Cancel the initial warning once, then accept. Confirm download and installation progress, app restart and preservation of saved notes/settings. Unsaved work is intentionally discarded after confirmation.
- Launch the existing app from its installed Start menu shortcut, which uses the installation folder as its working directory. Update it with a newer setup and confirm the folder swap succeeds after that app exits; setup must not hold the old folder open through its inherited working directory.
- Interrupt the download and verify the running app survives with an error notification. In a disposable installation, cause replacement to fail and verify rollback, minimized error relaunch, sound and taskbar attention.
- Open matching, newer and older setup versions. Verify maintenance choices, primary Update for newer setup, and continued local maintenance when offline. With an older setup, confirm the three standard actions remain visible and Advanced options reveals Install older version; cancel its warning, then retry in a disposable installation and verify notes/settings remain while app files switch to the older version.
- Uninstall with the default keep-data option, reinstall, then verify notes/settings return. Repeat with confirmed managed-data deletion; files exported outside app directories and unrelated shortcuts must survive.
