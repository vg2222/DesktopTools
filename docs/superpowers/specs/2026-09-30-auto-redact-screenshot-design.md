# Auto Redact Screenshot — design

Date: 2026-09-30
Branch: `codex/guides-recording-notes`
Baseline: `501e6dd` — Add offline translation packs and refresh Windows OCR languages
Status: specification and implementation plan approved by the owner; implementation complete, automated verification recorded in the plan; physical acceptance checks remain

## Purpose and agreed scope

Owner revision (2026-10-01): rename the manual action to **Check screenshot**, simplify the panel with standard DesktopTools controls and numbered regions, and add **Hide all found** without per-row selection. The owner explicitly chose a bulk button rather than automatic pixel changes immediately after scanning. Improve small-text recognition and retain Undo plus reviewed output. The original design below documents the initial agreement; these refinements take precedence where labels differ.

Help people inspect screenshots for potentially sensitive text before sharing them. Reuse DesktopTools capture, local Windows OCR and screenshot editing. Detection produces suggestions; the user decides which areas to hide. The owner approved the proposed design and explicitly requested a button for manual detection.

The first version includes:

- An optional capture preference: check screenshots before copying or saving.
- A visible **Find sensitive data** button in the screenshot editor, including when automatic checking is disabled.
- Local detection of emails, phone numbers, IP addresses, local paths, recognizable credentials and context-labelled identifiers.
- Highlighted suggestions and a list explaining each category.
- Solid cover, pixelation and blur; solid cover is the default.
- Individual acceptance or dismissal, explicit dismissal of all remaining suggestions, manual areas, area resizing, removal, undo and redo.
- Per-category preferences and selection of an installed Windows OCR language.
- English, Russian, German, French and Spanish UI text, release notes and testing documentation.

There is no cloud service, telemetry, account requirement, automatic sharing or persistent OCR transcript. OCR Watch and Live Screen Translation are outside this work.

## Existing behavior that must change

`AppController.CaptureAsync` currently assigns the raw result to `LastCapture`, adds it to session history, then copies or saves it. Checking afterwards would expose the original to the clipboard first.

`LocalOcr.RecognizeAsync` currently returns plain text without positions. `ScreenshotEditorWindow` already edits a copy, supports crop and bounded undo/redo, and exports through `ScreenshotEditDocument`. `RedactionRenderer` currently flattens opaque black rectangles. Capture history is session-only.

Preserve the existing capture behavior when automatic checking is disabled. Automatic checking defaults to disabled so existing settings retain their workflow. The manual button remains available independently.

## Capture and review flow

1. Capture and composite the selected screenshot as today.
2. If automatic checking is enabled, keep this result as a private pending image owned by the review editor. Do not publish it to the clipboard, a file, `LastCapture`, screenshot notifications, pinned images or capture history.
3. Open the existing screenshot editor and start local analysis asynchronously. The capture/selection surface has finished; the review window does not start another capture or selector.
4. Show progress, cancellation and any missing-language or OCR error in the editor. Export stays unavailable while analysis is pending.
5. Draw suggestion outlines separately from actual edits. Selecting a list item highlights its corresponding image area. Finding something does not cover it automatically.
6. The user accepts suggestions with their chosen style or dismisses them. Manual areas can be drawn even if OCR is unavailable. Export requires unresolved suggestions to be accepted or explicitly dismissed; provide a direct **Ignore remaining suggestions** action.
7. Copy or Save uses the current flattened edited image. After a successful output, publish that reviewed result to `LastCapture` and history, and use it in subsequent notifications or pin actions. Save does not also copy. A cancelled save dialog is not a completed export.
8. Closing the review before output cancels the pending capture. Existing clipboard content, previous `LastCapture` and previous history stay unchanged.

The editor retains the original in memory for undo and original comparison. It never replaces an imported source file or stores the OCR text. Choosing to dismiss every suggestion permits explicit output of the unredacted image.

Repeated captures create independently owned review sessions. Completion or cancellation of an earlier analysis cannot update a later screenshot or a closed window. Existing scan-text-only capture continues to open Text tools, without launching Auto Redact.

## Manual detection

The visible **Find sensitive data** button starts or repeats analysis of the current edited crop. This supports ordinary screenshot editing and the editor used by image tools and guides. It does not change their Copy/Save/Apply callbacks or automatically add an imported image to screenshot history.

The button is disabled while its analysis is running. Cancel, close and a new scan invalidate previous work. If image edits change during a scan, invalidate the result rather than apply coordinates from an older image. Analysis failure retains all existing edits and allows manual redaction or an explicit skip.

Suggestions describe a scan of the current image. Accepting or dismissing a finding resolves it within that scan; accepting a group does not discard unrelated findings. Other edits affecting image pixels or crop invalidate outstanding suggestions and show that scanning again is needed. Before export, the user must scan again or explicitly skip that check. Already accepted redactions remain ordinary editable annotations.

## Local OCR and detection

Add a layout-aware OCR result containing recognized words, line membership and bounding rectangles in input-image physical pixels. Preserve `RecognizeAsync` and its existing callers. Map detector matches back to the participating word rectangles; add a small bounded padding to avoid leaving character edges exposed.

Detection runs on OCR text using bounded local rules:

| Category | Detection policy |
| --- | --- |
| Email | Recognizable email structure; trim surrounding punctuation. |
| Phone | Plausible digit count and separators; avoid obvious dates and short counters. |
| IP address | Validate IPv4/IPv6 candidates using address parsing. |
| Local path | Windows drive, UNC and local user-profile paths. |
| Credential | Known key/token shapes or values beside explicit key, token, password or authorization labels. |
| Username / account / serial | Values beside explicit corresponding labels, including common equivalents in supported UI languages; do not classify every arbitrary word or number. |

Use timeouts and bounded input sizes for regular expressions. Do not add custom user regexes or remote detection. Coalesce duplicate/overlapping findings while retaining their categories. Missed OCR characters and unusual formats can produce omissions or false positives; the UI describes findings as possible sensitive data, and never claims a screenshot is certified safe.

Use the selected installed Windows OCR recognizer. Refresh the language list as existing Text tools do. If the saved language is unavailable, show the chosen fallback; if none exists, show installation guidance without downloading anything automatically.

Oversized images must respect the Windows OCR size limit: analyze bounded, overlapping tiles in sequence, offset word bounds back into the screenshot, and deduplicate overlap results. Limit pathological image/input sizes with an explicit error rather than silently inspecting only part of an image. Do not resize the output image or change capture dimensions for analysis.

Labels in the findings list identify the category and a masked excerpt rather than reproducing entire credentials. OCR text, matches and images stay in memory; diagnostics and logs must not include their contents.

## Redaction rendering and coordinates

Store accepted regions in full-image physical pixel coordinates, independently of editor zoom and monitor DPI. A scan of a crop adds the crop origin back to its OCR rectangles. Crop changes and partially cropped regions must preserve alignment. Negative monitor origins are already resolved before editing the captured bitmap.

Extend editable redaction annotations with a style. Existing redactions retain their default solid black appearance. Annotation move, resize, delete and Undo/Redo operate on these regions; accepting a group of suggestions should be one undoable action.

Preview and export use the same rendering semantics. All redactions are applied after other annotations and flattened into output pixels. Solid blocks are opaque. Pixelation and blur only affect their specified bounds; neither is labelled equivalent to complete removal. Input bitmap pixels and metadata are unchanged.

Suggestion frames, selection outlines and scan status are editor-only visuals and never appear in exported pixels. OCR performed on an already edited image uses the same flattened result that would be exported, so accepted solid covers do not expose the original text to the subsequent scan.

## UI and preferences

Keep the existing screenshot editor, toolbar and Copy/Save/Apply actions. Add the manual detection button beside the editing tools and a scrollable sensitive-data section in the properties pane. Keep export actions visible when the list is long. Match existing light/dark themes and keyboard focus behavior.

The owner explicitly requested the interface to look as close as possible to DesktopTools. Build these controls with existing `Ui`, `DesignTokens`, `UtilityWindowChrome`, icon resources and theme brushes. Retain the current editor shell and layout variants; do not introduce a separate visual style, arbitrary status colors or a new standalone feature window.

The pane provides a finding count, category-labelled rows, selection, Accept, Ignore, Accept selected and Ignore remaining actions, a style choice and a manual-area tool. The image shows pending outlines and the actual result after acceptance. A selected manual/accepted region has resize handles.

Capture settings contain the automatic-check toggle, category toggles and OCR language. Persist preferences through `AppSettings` / `SettingsStore` using stable internal identifiers, not translated display strings. Preserve unrelated settings and profiles. Store no learned personal values, per-user identities or sensitive OCR results.

## Component boundaries

- `Extras/LocalOcr`: text and layout-aware local recognition; no capture or clipboard side effects.
- A small detection component: text/layout in, categorized pixel regions out; no UI or network access.
- `ScreenshotEditDocument` and `RedactionRenderer`: accepted editable regions, style-aware undo/redo, consistent flattened rendering.
- A focused editor extension: asynchronous analysis lifecycle, suggestions pane, manual button and interactions.
- `AppController`: optional pre-output review and explicit Copy/Save completion; pending captures are isolated from ordinary last-capture state.
- `Core` settings and localized catalogs: preference persistence and display text.

Avoid unrelated UI redesigns, capture refactors and additional dependencies. Reuse existing notification, theme and file-dialog behavior.

## Verification and acceptance criteria

Automated checks use synthetic images and invented text, never personal desktop screenshots.

1. Rule tests: positive/negative examples, punctuation, spaced OCR tokens, contextual identifiers, timeouts, duplicate and overlapping matches.
2. Coordinate tests: crop offsets, tile offsets, clipping, padding, zoom independence and partial redactions.
3. Pixel tests: solid blocks are opaque above ink; styles change only their bounds; input pixels remain unchanged; suggestion outlines are absent from export.
4. State tests: accepting/dismissing, grouping, resizing, removal, Undo/Redo, scan invalidation and cancellation.
5. Persistence/localization tests: category and language recovery, unchanged unrelated preferences, catalog parity and placeholders.
6. Capture integration: clipboard/history/last image untouched before approval and on cancellation; Copy publishes edited output; Save only saves; cancelled save remains pending; automatic checking off preserves the prior path.
7. Editor integration: manual button works with automatic checking off; Apply still returns to image tools/guides; close during analysis and repeated scans leave no late callbacks.
8. Real local OCR against generated screenshots, including a crop and an oversized tiled image; skip unsupported OCR languages explicitly.
9. UI review in five languages and both themes, including a long findings list and a small window.
10. Build and relevant repository test scripts; document manual cases for multiple monitors, DPI, repeated capture and clipboard behavior. Report cases not run.

Success means a screenshot requiring review cannot escape through the automatic capture output path before a user decision, suggestions can be corrected manually, and exported pixels match the reviewed result.

## Git and delivery constraints

Work only on `codex/guides-recording-notes`. Leave all changes uncommitted. Do not commit, push, merge, rebase, create PRs, alter remotes/identity or publish a release. Report changed files, actual test results, branch, status and remaining review items.
