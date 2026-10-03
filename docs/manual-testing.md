# Manual Windows acceptance checklist

Record Windows build, DesktopTools build, monitor resolutions/scales and the observed result for each run. Automated tests are supporting evidence, not a replacement for these checks. Use ordinary non-sensitive test content.

## Drawing and input

- Open Diagnostics with one click from the sidebar. Verify the Visual C++, Media Foundation, OCR-language and shortcut cards; run checks again, then retry a deliberately blocked shortcut and open its feature settings. A missing file should produce a clear fix link, while a file-presence result must not claim that recording or a sharing viewer was tested.
- In Settings, search by a tool name, translated name and shortcut gesture. Open a result and confirm it goes to that tool's dedicated settings page. Enter should open the first result; Escape should clear the search.

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
6. Enable Smart Region Capture and hover over a window or dialog with screen freeze on; the suggested border must follow the saved frame even if the live window moves. Turn freeze off and hover over a panel, image and browser video to check inner-region suggestions. One click must capture the highlighted pixels and leave no selector overlay in the output. Drag a custom rectangle when a suggestion is wrong; turn Smart Region Capture off and confirm the original drag selection. Repeat at mixed DPI and negative monitor origins, and cancel with Escape. When selecting from an older drawing-session freeze, confirm the selector uses manual drag rather than stale window positions.

## Presentation, redaction and profiles

1. Laser movement leaves a fading trail without permanent annotations; spotlight follows the pointer. Toggle each effect and disable it while active. Confirm underlying interaction matches the intended mode and no effect window survives Quit.
2. Freeze a changing clock/video, draw over the still and capture. Verify the frozen background is used once, while the underlying application continues running. Leave freeze and confirm the live desktop returns.
3. Pin a capture, resize it and change opacity. Close it and confirm no stale topmost window remains.
4. Pin another application's window, then switch among unrelated applications without using the pin shortcut. Their topmost states must not change. Unpin the original window and verify only it changes. Try Pin window on an application window that was already always-on-top before DesktopTools started; DesktopTools must leave it unchanged and explain why.
5. Add solid redaction covers at image edges in a scaled editor. Undo a cover, export a copy and inspect PNG pixels: covered areas must be fully opaque black and the original unchanged. Do not distribute the original sensitive image by mistake.
6. Apply Everyday, Meetings and Teaching profiles, save/override/delete a custom preset, and restart. Tool preferences change while theme/startup/monitor/save path/palette position stay unchanged. A conflicting profile shortcut must leave active preferences and registrations unchanged.

## Auto Redact Screenshot

Test inline values on a long mixed-font page: four emails, eight IPv4 addresses, five prefixed keys and five passwords. Split three keys at `sk-`/`sk-test-` across physical rows. Check all 22 values and 25 separate pieces, including a very small password between its label and a full stop. Verify covers stop at ordinary spacing and do not attach to a later `Version20261003` row. Recheck English/Russian selections. Ordinary prose and IP-adjacent numeric noise must not become passwords or phones; a real phone immediately before or after an IP must retain its complete cover. Close/cancel during narrow gap OCR, and exceed its 64-crop budget to verify crop guidance and no output. Very small or damaged glyphs remain a manual-review case.

Test a long mixed-size prose journal containing 12 emails, 11 IPv4 addresses, six prefixed API keys and 11 password values. Include passwords in comma-separated prose without colons, a password split over two lines, separated punctuation boxes and IPv4 addresses followed by a sentence period. Check full bounds of all 40 values (41 physical pieces for the wrapped password); adjacent values may merge, so finding count alone is insufficient. Repeat English/Russian selections with installed app-language OCR. The generated fixture uses 8px key/password text and readable 10px numeric text; 5–6px numeric glyphs at an 8px Consolas font remain unreliable even after extra sampling. Always inspect missed tiny numbers and use manual covers. Also check an ordinary URL with `?print=1`, password advice ending in `!`/`?`, and label/value words placed on different native OCR lines: they must not create credential suggestions unless a known key is actually present in the URL. Oversized tiny-row budgets must show crop guidance instead of an empty successful scan; cancel/close during analysis must preserve output state.

Also test a single-column list with unlabelled `DarkSpectre_404`, `PixelHunter_X7` and `QuantumVoid99` at different font sizes (18/14/11), followed by emails, phones, an IP, a key, User ID and a labelled password at sizes down to 9. Check 12 complete value bounds with English/Russian OCR. Include overlapping and separately indexed OCR suffixes; no suffix should remain visible. Confirm distorted OCR table labels do not become extra nickname suggestions. An isolated product/version heading and ordinary prose must not form a handle list. Unlabelled detection is limited to structured underscore or mixed-case numeric lists; plain names and isolated unlabelled identifiers may need manual covers.

Test a dark two-column table with labels and values separated horizontally and no colons: three numbered nicknames, three emails, two phones, an IP, a key, **User ID** and **Пароль**. Confirm all 12 complete values are covered with English/Russian OCR and the matching app-language recognizer installed. Include values split across native OCR lines, an unrelated third column and ordinary password advice; suffixes must remain covered, while the unrelated column and advice remain visible. Table association requires a second aligned labelled row; isolated labels without separators may still need manual covers.

Use invented emails, addresses and keys in test images. Record the installed Windows OCR language and distinguish detector misses from incorrect region placement.

Include a dark monospace strip with `192.0.2.145`, an invented `sk-test-` key containing slashed zeros, **API-ключ:** with an invented value, and labelled **Password:** / **Пароль:** values. Use the matching installed OCR language for localized labels. Confirm complete value bounds and that ordinary password advice is not flagged. In both themes, inspect the thin outlines of **Add cover manually**, **Detection options** and **Drawing options**, including keyboard focus.

Test the full mixed-language sample with 25 invented values at small and larger font sizes: three emails, two phones, four IPs (including IPv6), two paths, five keys/tokens, three passwords, two logins, two account identifiers and two serials. Check that every cover reaches the complete suffix and full telephone prefix. With app-language OCR installed, repeat both English and app-language selections. Also test tightly spaced local phone groups and complementary readings where one recognizer reads a label and another reads its value. Inspect approximate suggestions: OCR recovery can flag email-shaped `*` expressions or include extra words on a labelled field's row; use Ignore/manual adjustments when appropriate.

1. With automatic checking off, capture normally, then open the screenshot editor and use **Check screenshot**. Repeat in Image tools and while adding/editing a guide step. Suggestions must only highlight numbered areas; **Hide all found** must work without selecting any row, creating editable covers in one Undo/Redo step. Select individual areas to hide them or keep them visible; dismissal changes no pixels. Scan a list of seven invented emails with smaller lower rows, including a dark background. Verify all seven and correct border positions. Repeat with Russian OCR and installed English OCR to check supplementary data detection.
2. Enable **Check screenshots before copying or saving** in Capture settings. Keep a known previous capture and clipboard item, then capture again. While review is pending, verify the old clipboard, last capture and history remain unchanged. Close the review without output and confirm they remain unchanged. Repeat captures from Hidden, Draw and Interact states; the editor must remain usable without a drawing input surface intercepting clicks.
3. Accept or dismiss all suggestions and test Copy and Save separately. Paste the copied image into a local image viewer; inspect the saved PNG. Only the reviewed pixels should appear, without highlight borders or handles. Save must not copy. Cancel the Save dialog and test an unwritable destination: the review should stay open, and history/clipboard should be preserved. Explicitly ignore a failed check and confirm this enables output without silently adding covers.
4. Select Solid, Pixelate and Blur; compare Edited preview and exported pixels. Solid must be opaque black, including over transparent pixels and other annotations. Blur and pixelation must not modify pixels outside the selected area. Inspect the original image/file afterward; it must remain unchanged. Draw an additional cover, move it and resize each corner/edge handle. Escape during a gesture cancels it; Undo restores its previous bounds.
5. Crop before scanning, then compare suggestions against the cropped text and exported image. Edit or crop during/after a scan: outdated findings must disappear and output must require another scan or explicit skip. Close the editor during analysis; no late result should reopen a window or publish a capture.
6. Repeat at 100%, 150% and 200% scaling, on a secondary monitor left/above the primary with negative origins and across mixed-DPI displays. Check border alignment and cropped output pixels. These physical checks are separate from generated coordinate tests.
7. Scan a screenshot wider than an OCR tile with invented text crossing the tile boundary. Verify full-image placement and no duplicated finding. For an image exceeding the analysis limit, expect crop guidance and no successful empty result. With no Windows OCR language installed, expect installation guidance; use manual covers or explicitly skip. Change to an unavailable saved language and verify the actual fallback language is displayed.
8. Check Capture preferences survive restart, category exclusions work, and other OCR/translation preferences remain unchanged. Inspect the minimum editor size in EN/RU/DE/FR/ES, both themes and toolbar layouts A/B with a long findings list. Review controls must remain reachable through the properties scrollbar, and Copy/Save and the manual detection button must stay visible. Review translated wording with native speakers.

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

## Notes, recorder confidence, and editor preview

- Play known microphone and system audio, enable each source separately, and confirm its meter responds. Record a five-second test from the audio setup area, play it, and verify picture and audible tracks. Confirm the bottom Start/Pause/Stop controls stay on one row. Put another app in front of the recorder setup window; it should stay in front until DesktopTools is activated again. Repeat the audio test with one source muted. Close the recorder and confirm the temporary test clip is removed when no player holds it open.
- Record to a nearly full destination and confirm the free-space warning appears before capture starts. On a normal recording, compare the displayed file FPS with the file's metadata; do not treat it as a count of distinct captured frames.
- In screenshot and image tools, make an edit, toggle Original/Edited, then export. Confirm the exported copy contains the edits and the original file remains unchanged. In video tools, wait for automatic preview, toggle Original/Edited, and confirm the edited preview returns without another render. Check crop guidance and controls at the smallest supported window size in every language.

## 2.1.2 manual follow-up
- Cursor monitor mode: activate Draw/Laser/Spotlight/Freeze on A, hide or interact, move pointer to B and activate again. Verify B is used. Drawing starts fresh on B; capture cancellation retains A's document.
- Capture B with A's ink or frozen session: exported pixels must show B without A's ink. Check at mixed DPI and negative monitor origins.
- Inspect notifications on bright/dark desktop wallpapers: one rounded silhouette, no gray rectangular edge. Hover pauses expiry; dismissal leaves latest screenshot available.
- Pin wide, tall and tiny screenshots; drag, proportional resize, hover controls, compact/right-click menus, keyboard access and close. Check across mixed-DPI monitors.
- Keyboard navigation in rounded shortcut fields, dropdowns, sliders and editor buttons; Windows reduced-motion/high-contrast modes. No zoom or slide animation on buttons/pages.
- In each dashboard category, open every feature's settings icon. Each destination should identify only that feature, show only its shortcut controls, and return to its category. Check recorder, pin screenshot, OCR and translation separately.
- On a machine without Microsoft Visual C++ x64, verify the recorder warns before selecting a source and Record shows a notification without opening Save. Translation and background removal warn and do not start native processing; screen OCR and other image edits still work.

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
- Open Notes and a floating note. Both should start as ordinary windows. Use the feature setting to turn **Always on top** on and off, then reopen Notes; saved text must remain available. A note previously attached to a closed window must now open independently.
- Play audio in two apps; adjust one volume/mute and confirm the other unchanged. Change default playback device and refresh. Close mixer and verify no refresh timer remains. No audio changes were performed during automated checks.

### Screenshot guides and recording markers

- Open **Step-by-step guide** from Capture tools. Select a recent capture or folder image and confirm the annotation editor opens before the step is added. Try **Add original**, then edit another image with crop, text and an opaque cover and use **Add edited image**; cancel a third image and confirm no step is added. Add multiple files, edit one from its step card, and confirm other steps are unchanged. Confirm folder thumbnails use two columns; drag the divider to resize the picker. Write captions, reorder steps with drag and the arrow controls, remove a step, and export PNG with one through six steps per row. Inspect centered numbers, original image detail, rounded images, and light, dark, and custom export backgrounds. Confirm source screenshots are byte-for-byte unchanged, and canceling Save creates no output.
- Record a short clip and add a marker with the HUD control and Ctrl+Alt+M. Pause, then confirm a marker cannot be added while paused. Stop, click a marker to open the video editor at that time. Close and reopen the editor with the same file; markers must still appear. Replace the video at the same path and confirm old markers are not shown.

## Offline translation and OCR language packs

- Choose a supported Windows language and confirm it appears near the top of the source/target lists in Text tools and feature setup. Swap languages and reopen the window; preserve the saved pair. Unsupported Windows languages should not promise an unavailable model.
- Choose a pair requiring downloads. Review its size, cancel a download, retry, and confirm no partially downloaded model is used. After download, disconnect the network and translate directly and through English. No input text should be transmitted; model requests are to pinned Hugging Face assets only.
- Change the Windows OCR language components in a disposable account, refresh Text tools, and verify the list follows available recognizers. A saved generic code should select the corresponding regional recognizer. Remove that component and confirm fallback to an available recognizer. If none are installed, show the installation guidance.
- Normal app updates preserve downloaded models; an explicitly confirmed full-data reset removes app-managed translation packs. Review non-English output with someone who reads that language. Native-speaker quality across all catalog languages is not established by automated fixtures.

## GitHub update acceptance

Use a disposable Windows account or VM with two stable published versions and their checksum manifests. These steps exercise real installation and cannot be established by protocol fixtures alone.

- Open News offline and confirm the bundled announcement is readable. Restore network access, publish a new ordinary feed item, and confirm a single notification, Home cue and unread indicator. Open News, then restart; read/notified state should persist. Disable automatic news checks and confirm manual refresh still works.
- In a disposable test feed, publish a `security` item whose safe version is newer than the installed version. Before a matching stable release exists, confirm the app shows an in-app warning and a persistent notification with no automatic installer. After publishing a matching stable release, confirm the urgent Update action still asks before downloading/restarting and uses the verified installer. Dismiss the notice, confirm the in-app warning remains, then check for a reminder after about an hour. Withdraw the feed item and confirm the urgent state clears.

- Install the earlier version, create a note and change a setting. Confirm startup/manual checks find the newer release; change the background interval and verify disabling it leaves manual checks available.
- Leave the update notification without any input, then return. Open release notes and confirm the notification remains, reports the browser action and uses the extended duration. When its message expands or contracts, confirm the notification resizes smoothly and nearby notices do not overlap. During download, confirm the rounded progress bar uses the app accent in both light and dark themes and advances smoothly. Dismiss the notice and confirm the sidebar and Updates page still offer the update.
- Choose Update in the notification. Cancel the initial warning once, then accept. Confirm download and installation progress, app restart and preservation of saved notes/settings. Unsaved work is intentionally discarded after confirmation.
- Launch the existing app from its installed Start menu shortcut, which uses the installation folder as its working directory. Update it with a newer setup and confirm the folder swap succeeds after that app exits; setup must not hold the old folder open through its inherited working directory.
- Interrupt the download and verify the running app survives with an error notification. In a disposable installation, cause replacement to fail and verify rollback, minimized error relaunch, sound and taskbar attention.
- Open matching, newer and older setup versions. Verify maintenance choices, primary Update for newer setup, and continued local maintenance when offline. With an older setup, confirm the three standard actions remain visible and Advanced options reveals Install older version; cancel its warning, then retry in a disposable installation and verify notes/settings remain while app files switch to the older version.
- Uninstall with the default keep-data option, reinstall, then verify notes/settings return. Repeat with confirmed managed-data deletion; files exported outside app directories and unrelated shortcuts must survive.

### Full-monitor privacy review and editor opening size

- Capture an entire 2560x1440 or 3840x2160 monitor with a narrow text column, navigation/sidebar text and mixed font sizes. Compare suggestions with a crop containing the exact same source pixels. Include wrapped passwords/keys and a trailing value entirely missed in the primary OCR reading. A moderate desktop must complete without the former supplemental-row "too much text" error; extreme input still returns bounded-analysis guidance.
- Move the text column to the middle/right of the screen. Check complete physical bounds for every expected value, including both pieces of wrapped values. Check EN and RU selection; dotted password-like values should be suggested while normal emails, version strings and URLs retain their appropriate handling.
- Open the screenshot editor on each monitor. Initial outer width and height should each be 75% of that monitor's working area, centered without covering the taskbar. Resize manually and confirm later layout updates do not reset the size. Verify a small work area, negative monitor origin and different per-monitor DPI values. The screenshot's output pixel size must not change with the window size.
- A screenshot of a reduced editor preview is not equivalent to the original full-monitor bitmap. Check both separately and use manual covers for text too small or damaged to read.

### Wrapped secrets beside a browser sidebar

Capture the whole browser with navigation rows whose vertical positions fall between a key prefix and its continuation in the body column. Include `sk-` and `sk-test-` at a row end, a prefix recognized as its own native line, and an invented mixed-case password such as `Example#Access!` followed by four digits on the next row. Confirm both pieces are covered under English and Russian OCR. An intervening ordinary body-column row must stop the association. Check that a URL ending with the same password-like fragment followed by a year is not classified as a password. Suggestions can still include OCR false positives; inspect before applying all covers.
## Desktop Automation revision

- Open the action library, search by action or help text, and insert after the selected row. Add inside Repeat and both If branches; move, duplicate and remove entire blocks from either boundary. Undo an edit. Try the nesting and 500-action limits without losing the existing workflow.
- Choose a target window from the list; shorten its title, record a shortcut, type multiline text, browse for an executable, and select a physical point on each monitor. Check negative origins and mixed DPI. After opening an app, insert Focus window before input actions.
- Test selected action independently; run the full workflow and stop with Esc. Change foreground or cover the click point during playback: stop instead of sending to another app. Move or close the target and confirm a clear action-number error.
- Record a short click/keyboard sequence including switching applications. Cancel during countdown, stop via Esc, close during recording, and review/save recorded steps. Reopen and replay. Never type private data into a test recording.
- Enable and save shortcut, interval, window-appearance, daily and startup triggers. Check while busy, around midnight, with a missing target and after restart. Daily runs missed while closed are not replayed. Disabling automatic triggers leaves manual Run available.
- Exercise wait-for-window/color success, timeout and cancellation; clipboard set/paste; minimize, maximize then minimize/restore. A stalled or elevated target may reject input; verify an error and released modifiers.
- Import/export a workflow containing user text and nested blocks; import must remain disarmed. Open a schema-1 store, save and reopen without losing original workflows. Keep real user settings and translations unchanged.
- Review en/ru/de/fr/es in light, dark and custom backgrounds, including 1040-pixel-wide window, run settings and action library. Check keyboard focus and scrolling on small screens.

## Desktop Automation UI polish

- On each action row, reveal the move/copy/delete icons with hover, selection or keyboard focus. Operate nested blocks from their start and end rows, then undo; parameters remain in the inspector.
- Open Add action, search and clear the search, try a query with no results, and resize/scroll the list. Check that the background fills the window without a gray strip at the bottom.
- Open Run settings, cancel edited fields and reopen; cancelled changes must be discarded. Enter an invalid daily time, correct it and save. Use the window/shortcut pickers. At the minimum size, scroll the fields while Cancel and Save stay visible; Escape cancels and Enter saves.
- Trigger an action error and a successful save. Confirm a readable colored message banner with a dismiss button. Run or record a workflow; check that localized HUD text and Stop fit without clipping and that showing the HUD does not take foreground focus.
- Enable transparency with both the default and a custom background. Over a disposable colored test window, check the active backdrop, then turn transparency off. Verify opaque fallback with Windows transparency disabled or high contrast. Repeat after changing theme.