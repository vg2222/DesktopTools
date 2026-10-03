# Auto Redact Screenshot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Offer local sensitive-text detection and editable redaction before screenshot output, with an independently available manual detection button.

**Architecture:** Extend local OCR with word geometry, match bounded patterns against that layout, and show suggestions in the existing screenshot editor. Accepted regions share the editor's undo/redo and flattened rendering. Automatic review keeps the pending image separate from clipboard, last capture and history until successful output.

**Tech Stack:** Existing .NET 10 Windows WPF application, Windows.Media.Ocr, local regex/address parsing, existing bitmap and theme utilities; no new packages.

**Spec:** `docs/superpowers/specs/2026-09-30-auto-redact-screenshot-design.md`

## Global Constraints

- Work only on `codex/guides-recording-notes`; leave all changes uncommitted. No commits, push, PR, merge, rebase, remote/identity changes or release publication.
- All OCR/detection is local. No accounts, telemetry, content logs or persistent OCR transcript.
- Automatic checking defaults to disabled. The manual detection button remains available independently.
- Solid cover is the default. Suggestions never silently modify an image.
- Input images/files are preserved. Export flattens the reviewed copy; selection and suggestion frames are never exported.
- Keep the existing editor and both layout variants. Use `Ui`, `DesignTokens`, `UtilityWindowChrome`, existing icons and theme brushes; fixed export controls and a scrollable properties pane.
- Display strings use `L.T`/`L.F`, with matching Russian, German, French and Spanish catalogs; English is the source language.
- Physical image pixels are the coordinate system, independent of monitor origin, DPI and editor zoom.

## Review Focus

- Clipboard containing a prior image: cancel or failed analysis must leave it unchanged (Task 6).
- Crop/annotation edits during OCR: stale results must never land on the modified image or a closed editor (Task 4).
- A credential crossing OCR word/tile boundaries: cover all participating characters without duplicate findings (Tasks 1–2).
- Save cancelled or failing: no history publication and no incidental clipboard copy (Task 6).
- A long German findings list at minimum window size: controls must fit and export remain reachable (Task 7).

## File structure and interfaces

### New files

- `src/DesktopTools/Core/AutoRedactOptions.cs`: preferences and normalization.
- `src/DesktopTools/Core/OcrLayout.cs`: `OcrWordBox(string Text, Rect Bounds, int LineIndex)` and `OcrLayout(int PixelWidth, int PixelHeight, IReadOnlyList<OcrWordBox> Words)`.
- `src/DesktopTools/Core/SensitiveDataDetector.cs`: `SensitiveFinding(Guid Id, IReadOnlyList<string> Categories, Int32Rect Bounds, string MaskedExcerpt)` and pure pattern detection.
- `src/DesktopTools/Core/RedactionReviewState.cs`: scan revisions, unresolved findings, explicit skip and decisions.
- `src/DesktopTools/Core/RedactionStyle.cs`: `RedactionStyle { Solid, Pixelate, Blur }`, `RedactionRegion(Int32Rect Bounds, RedactionStyle Style)` and `ScreenshotExportAction { Copy, Save, Apply }`.
- `src/DesktopTools/Extras/SensitiveDataAnalyzer.cs`: compose layout recognition and detection.
- `src/DesktopTools/Extras/ScreenshotEditorWindow.Redaction.cs`: pane, scan cancellation, manual detection, acceptance and resize interactions; make the existing window partial.
- `src/DesktopTools/AppController.Redaction.cs`: automatic-review ownership and typed export completion.
- `src/DesktopTools/UI/MainWindow.AutoRedact.cs`: capture preference rows using existing controls.
- `src/DesktopTools/Localization/auto-redact.{ru,de,fr,es}.json`: feature strings.
- Focused tests: `tests/DesktopTools.Tests/AutoRedactTests.cs`, `tests/DesktopTools.CaptureTests/AutoRedactOcrChecks.cs`, `tests/DesktopTools.IntegrationTests/AutoRedactChecks.cs`.

### Existing files to modify

- `Core/AppSettings.cs`, `Core/SettingsStore.cs`: one options object and its validation.
- `Extras/LocalOcr.cs`: add positioned OCR; preserve plain text API.
- `Core/AnnotationDocument.cs`: style property defaulting to Solid on annotations.
- `Extras/ScreenshotEditDocument.cs`, `Extras/RedactionRenderer.cs`: grouped edits and styles.
- `Extras/ScreenshotEditorWindow.cs`: manual button, shared preview, export gate and typed optional output callback.
- `AppController.cs`: conditional capture review and successful-output helpers; preserve existing public entry points.
- `UI/MainWindow.cs`: invoke the additional capture-settings group.
- Test entry points and `tests/DesktopTools.NativeTests/DesktopTools.NativeTests.csproj`: link the new options file because native checks compile AppSettings directly.
- `docs/releases/1.2.7.md`, `docs/architecture.md`, `docs/manual-testing.md`: behavior, privacy boundary and manual cases.

## Verification commands

Use the available SDK at `../DesktopTools-main/.dotnet/dotnet.exe` if `dotnet` is not on PATH. Restore from the existing package cache when necessary; do not fetch models or packages already present.

```powershell
dotnet run --project tests/DesktopTools.Tests -c Release
dotnet run --project tests/DesktopTools.CaptureTests -c Release
dotnet run --project tests/DesktopTools.IntegrationTests -c Release -- --auto-redact-only
dotnet build DesktopTools.sln -c Release --no-restore -v:q
./scripts/test.ps1
```

Integration images and logs go only under ignored `artifacts/`. Generated OCR samples contain invented data. Record explicit OCR-language skips separately from passing checks.

### Task 1: Preference model and pure detection

**Files:** New options/layout/detector models; modify AppSettings, SettingsStore, native-test project; new AutoRedactTests and core Program registration.

**Interfaces:** `AppSettings.AutoRedact` is `AutoRedactOptions`. Options contain `bool Enabled = false`, `string Language = "en-US"`, `string Style = "Solid"`, and `string[] Categories` initially containing `email`, `phone`, `ip`, `path`, `credential`, `username`, `account`, `serial`. `AutoRedactOptions.Normalize()` recovers unknown values. `SensitiveDataDetector.Detect(OcrLayout layout, IReadOnlyCollection<string> categories)` returns `IReadOnlyList<SensitiveFinding>`.

- [ ] Write failing tests registered through `AutoRedactTests.Run(Action<string, Action> test, Action<bool, string> check)`: default enabled is false; JSON round trip preserves categories/language; unknown category/style values recover; unrelated preferences remain unchanged.
- [ ] Add detection assertions: `alice@example.test` maps to its word box plus clipped 3-pixel padding; a split `alice @ example.test` maps to all three words; invalid `999.1.1.1`, date `2026-09-30`, bare `12345`, and an ordinary person's name do not become sensitive findings. Explicit `Account ID: 12345` and supported-language equivalents produce an account finding.
- [ ] Run core checks and confirm new cases fail for missing behavior, rather than missing fixtures or unrelated compilation errors.
- [ ] Implement types and detector. Use a 100 ms regex timeout, up to 200,000 OCR text characters and 10,000 words; exceeding limits fails explicitly. Validate IPs with `IPAddress.TryParse`; prefer contextual matching over arbitrary long-number/username guesses. Merge intersecting rectangles and retain category labels. Mask list excerpts and never log input text.
- [ ] Run core/native checks; expect no failures. Inspect existing default preferences for compatibility.

### Task 2: Positioned OCR and bounded tiled analysis

**Files:** LocalOcr, new SensitiveDataAnalyzer, capture checks and capture Program registration.

**Interfaces:** `LocalOcr.RecognizeLayoutAsync(BitmapSource image, string languageTag, CancellationToken cancellationToken = default)` returns `Task<OcrLayout>`. `SensitiveDataAnalyzer.AnalyzeAsync(BitmapSource image, string languageTag, IReadOnlyCollection<string> categories, CancellationToken cancellationToken, Func<BitmapSource, string, CancellationToken, Task<OcrLayout>>? recognize = null)` returns `Task<IReadOnlyList<SensitiveFinding>>`. The optional recognizer is a test seam, not a UI choice.

- [ ] Add failing generated-image checks: positioned `alice@example.test`, a crop-origin offset, and a line containing a token split into multiple OCR words; plain `RecognizeAsync` still returns its existing text.
- [ ] Add a generated oversized image with text across a tile boundary; assert source-sized coordinates and no duplicated finding. Cancellation between tiles produces `OperationCanceledException`, not partial successful results.
- [ ] Run capture checks to establish the failure; explicitly skip real recognition when no suitable installed Windows language exists, while still exercising injected-layout tests.
- [x] Implement word extraction from Windows OCR. For oversized images use sequential square tiles of `min(1600, OcrEngine.MaxImageDimension)` pixels and bounded 400-pixel overlap; offset words into the full image, coalesce overlap words, and reconstruct line groups. Reject more than 64 tiles or 48,000,000 image pixels with a localized size error. Check cancellation before/after each tile and do not alter original pixels. The approved 80-pixel overlap was increased after generated OCR split an email across the boundary; see the execution ledger.
- [ ] Run capture checks; verify plain OCR regression, geometry, tile boundaries, injected cancellation and explicit language skip reporting.

### Task 3: Editable styles and matching preview/export

**Files:** RedactionStyle, AnnotationDocument, ScreenshotEditDocument, RedactionRenderer, editor rendering, core rendering checks.

**Interfaces:** `Annotation.RedactionStyle` defaults to `RedactionStyle.Solid`. Add `ScreenshotEditDocument.AddRange(IReadOnlyList<Annotation> items)` as one undo step. Preserve `RedactionRenderer.Apply(BitmapSource image, IReadOnlyList<Int32Rect> covers)`; add `Apply(BitmapSource image, IReadOnlyList<RedactionRegion> regions)` returning a frozen `BitmapSource`.

- [ ] Write failing synthetic-pixel tests: all three styles modify only their bounds, Solid writes black with alpha 255 even over transparent source/ink, source pixels remain unchanged, partially cropped areas retain coordinates, and a two-region AddRange is undone/redone together.
- [ ] Run core checks to confirm the new tests fail.
- [ ] Implement style-aware clipped rendering. Use 12-pixel pixelation cells aligned to each region and a bounded separable blur of radius 8 within the region. Solid covers render last when different styles overlap. Do not blur outside bounds or blend original pixels back through a solid cover.
- [ ] Share rendering between editor preview and export; cache the composed preview by document revision instead of recomputing it on hover. Existing screen-drawing redactions stay Solid.
- [ ] Run core pixel checks and existing capture/edit lifecycle checks. Compare exported pixels to the preview bitmap rather than asserting only that a renderer was called.

### Task 4: Review state and asynchronous lifetime

**Files:** New RedactionReviewState; core AutoRedactTests.

**Interfaces:** Construct state with `RedactionReviewState(bool requireScan = false)`. A fresh ordinary editor can export without running detection; automatic review uses requireScan true. State exposes `bool IsScanning`, `bool NeedsScan`, `bool HasUnresolved`, `bool CanExport`, `IReadOnlyList<SensitiveFinding> Findings`; `long BeginScan()`, `bool CompleteScan(long revision, IReadOnlyList<SensitiveFinding> findings)`, `void FailScan(long revision)`, `void Invalidate()`, `void Resolve(IReadOnlyCollection<Guid> ids)`, `void SkipRemaining()`, `void Close()`. CanExport is false during a scan, after invalidation/failure until explicit skip, or with unresolved findings. Invalidate only requires rechecking if detection was requested for this editor.

- [ ] Write failing cases: stale completion after Invalidate/Close is rejected; the next BeginScan invalidates the previous revision; accepting one finding retains the others; explicit skip permits export; failure does not falsely report zero findings.
- [ ] Pin the edit-during-scan Review Focus case: CompleteScan with an old revision returns false and cannot enable export. A closed state cannot restart scanning.
- [ ] Run core checks to confirm expected failures.
- [ ] Implement the revision state machine without image storage, OCR calls, clipboard calls or dispatcher dependence.
- [ ] Run core checks. Include a test that acceptance of one finding does not require rescanning unrelated findings, while a crop/edit does.

### Task 5: Manual button, suggestions and region correction

**Files:** Existing editor plus new partial extension, analyzer/state integration; AutoRedactChecks; integration Program `--auto-redact-only` route.

**Interfaces:** Extend the public editor constructor with optional `AutoRedactOptions? autoRedact = null`, `bool reviewBeforeOutput = false`, `Func<BitmapSource, ScreenshotExportAction, Task<bool>>? exportAsync = null`; preserve existing constructor arguments and behavior. Add internal `Task FindSensitiveDataAsync()` for the same action as the visible button and an internal settable `Func<BitmapSource, string, IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyList<SensitiveFinding>>> AnalyzeSensitiveData` delegate defaulting to SensitiveDataAnalyzer. The integration harness injects only invented findings through that delegate. Ordinary editors receive disabled-by-default options when no settings object is supplied.

- [ ] Write failing UI checks with an injected analyzer/recognizer seam: manual button exists with automatic checking off, pending outlines do not alter the document, Accept creates editable regions, Ignore resolves without changing pixels, and group acceptance is one undo step.
- [ ] Run the focused integration route to confirm the intended failures.
- [ ] Implement the visible **Find sensitive data** toolbar button using the existing `ScanText`/`Redaction` icon. Add the scrollable properties section with category-labelled rows, selection, style choice, Accept selected, Ignore and Ignore remaining; update button availability from review state.
- [ ] Add a manual redaction action and selected-region corner/edge resize handles, clipping to image bounds and committing once on mouse release. Escape cancels a gesture; Undo restores the prior bounds. Reuse physical canvas coordinates and existing selection/erasing behavior.
- [ ] Wire scans to editor-owned cancellation and revision tokens. Scan `_document.Export()`; offset detected crop coordinates back to full-image coordinates. Disable scan/export as appropriate; cancel when closing and invalidate after unrelated edits. Accept operations update their findings without dropping unrelated rows.
- [ ] Run focused UI checks plus existing image/guide/capture editing checks. Assert no completion callbacks after close and no suggestion frames in exported output.

### Task 6: Hold automatic captures until successful output

**Files:** AppController and new AppController.Redaction partial; editor typed export path; AutoRedactChecks.

**Interfaces:** Add `internal Task<bool> TryCopyAsync(BitmapSource image)`; preserve public `CopyAsync(BitmapSource image)` by awaiting the helper. Add `internal bool TrySaveImage(BitmapSource image, Window owner, Func<SaveFileDialog, Window, bool?> choosePath)` and preserve existing SaveLast overloads. `internal Task HandleCapturedImageAsync(BitmapSource image)` handles the optional review versus existing output path after composition; tests call it with generated images without capturing the user's desktop. `OpenAutoRedactReview(BitmapSource image)` opens an editor with options snapshot and typed `exportAsync` callback. Only successful Copy/Save publishes reviewed output to last capture/history.

- [ ] Write failing controlled-capture tests: automatic review captures into pending state; clipboard, LastCapture and history remain exactly as before until output. Cancellation and injected OCR failure leave them untouched. Test Copy and Save separately.
- [ ] Add save cancellation/failure assertions: callback returns false, history count stays unchanged, previous clipboard bytes stay identical and review remains open. Preserve scan-text-only and automatic-disabled capture routing.
- [ ] Run focused integration checks to prove initial automatic output violates the new boundary.
- [ ] Implement the CaptureAsync branch before LastCapture/history publication. Close any prior capture notice without presenting the new raw image. Release capture busy/selector state before reviewing; the editor's callbacks own the pending bitmap.
- [ ] Implement typed async export, respecting success/failure and preventing double activation. Default editor callbacks still support existing Apply behavior. Automatic review does not call generic RedactLast, whose old callback copies immediately; Save never triggers Copy.
- [ ] Run focused integration and existing `--region-freeze-only`, `--smart-region-only`, `--capture-lifecycle-only` checks. Clipboard tests use only synthetic data and restore the user's original clipboard where safely possible; otherwise report them Not tested instead of overwriting personal content.

### Task 7: Settings, matching DesktopTools UI and languages

**Files:** MainWindow.AutoRedact, MainWindow capture group, four new catalogs, localization tests and focused UI checks.

**Interfaces:** `private void AutoRedactSettings()` adds a Capture settings group; existing `Change(...)` persists normalized options. No new standalone page/window. Manual editor detection remains available through the toolbar.

- [ ] Add persistence checks for Enabled/category/language/style changes, missing-language fallback display, and unchanged translation/OCR preferences. Add catalog parity and placeholder checks for new strings.
- [ ] Add layout assertions for five languages, both themes, editor layouts A/B, minimum window dimensions and at least 30 synthetic findings. Find sensitive data, Copy and Save remain visible; scroll content stays inside the properties pane.
- [ ] Run tests to show the controls/catalogs are absent before implementation.
- [ ] Build settings and editor controls with existing `Ui` helpers, `DesignTokens`, `UtilityWindowChrome` and `Surface/Card/Stroke/Text/Selected` brushes. Use muted explanatory text and the app accent for selection; no new arbitrary green/red status palette. Make the properties pane resizable if necessary without moving the fixed footer.
- [ ] Add EN source copy and RU/DE/FR/ES catalogs with identical keys and indexed placeholders. Descriptions say possible sensitive data and distinguish Solid from Blur/Pixelate. OCR contents are not translated.
- [ ] Run core/localization/focused integration checks and visually inspect generated synthetic previews for theme match, clipping and readable layout. Record native-speaker wording review as a remaining review item if unavailable.

### Task 8: Documentation and final verification

**Files:** release 1.2.7 notes, architecture, manual-testing, plan progress.

- [ ] Document the opt-in pre-output gate, manual button, categories, styles, original preservation, OCR limitations and explicit skip/cancel behavior.
- [ ] Add manual cases for multi-monitor/negative origins/mixed DPI, crop and resize, repeated captures, no OCR language, tile-sized screenshots, manual detection in guide/image tools, clipboard output, save cancellation and long lists in both themes.
- [ ] Build the full Release solution and run `./scripts/test.ps1`; expect exit 0, no new build warnings and all applicable checks passing. Run focused `--auto-redact-only` and `--localization-only` integrations. Broaden tests only for a specific remaining risk or required repository gate.
- [ ] Review the complete diff against the spec, especially initial output, history publication, cancellation, original image preservation and existing Apply callbacks. Run `git diff --check` and inspect `git status --short` for accidental generated/private files.
- [ ] Report actual test counts/results, manual cases not run, changed files, `git branch --show-current`, git status and remaining owner review items. Leave all modifications uncommitted and do not publish anything.

## Execution handoff

The owner approved this plan and inline implementation with DesktopTools-consistent UI. Tasks 1–8 are implemented. The execution ledger under `.superpowers/sdd/2026-09-30-auto-redact-screenshot/progress.md` records observed failing/passing checks and deviations. A single read-only final review found two Important issues; both were reproduced by failing tests and corrected. All changes remain uncommitted on the specified branch. The original detailed checklists above describe the planned checks; the results below state the actual coverage and remaining acceptance checks.

## Verification results — 2026-10-01

| Check | Result |
| --- | --- |
| Release solution build | Passed; zero warnings/errors |
| `scripts/test.ps1` | Passed all six projects: core 71/71, native 51, capture/OCR, image transforms, updates and installer |
| `--auto-redact-only` | Passed: manual detection/accept/undo, output privacy, Save success/cancel/failure, analysis failure/close, cancelled busy Copy retry and 20 editor layouts |
| Real Windows OCR | Passed generated email crossing a tile boundary; plain text OCR regression passed |
| `--smart-region-only` | Passed selection gestures, coordinate fixtures and mode routing |
| `--localization-only` | Passed five languages and two themes in the user session; sandbox run could not locate the pointer |
| `--image-export-only` | Passed preview/formats/cancellation/original preservation |
| `--capture-lifecycle-only` | Diagnostic run passed 100 native capture/edit/export/close cycles; zero retained editors, GDI 33→33, USER 16→16, handles 684→689. Two earlier repeats failed the helper-frame color assertion, so native frame timing remains a manual review item |
| Cropped redaction equality | Failed before fix for Pixelate; passed for Solid, Pixelate and Blur after flattening before crop |
| Close during busy Copy | Failed before fix; passed after editor lifetime cancellation, with an injected clipboard writer and no user clipboard changes |
| `git diff --check` | Passed |

**Not tested:** successful real Copy over a nonempty user clipboard (deliberately preserved), full `--region-freeze-only` output (sandbox capture unavailable; unrestricted test can overwrite the clipboard), physical mixed-DPI/multiple-monitor gestures, manual corner/edge resize and Escape gestures, no-OCR-language hardware environment, cancellation specifically between real OCR tiles, and native-speaker wording. The supported synthetic coordinate/state tests passed; they are not substitutes for those physical cases.

The feature is heuristic: suggestions can miss sensitive text or flag ordinary content. Review the image before sharing. Blur and pixelation provide weaker hiding than solid covers. Neither screenshot pixels nor recognized text are sent to a service or stored as an OCR transcript.

## Working tree at handoff

Branch: `codex/guides-recording-notes`. HEAD: `501e6dd705536533c38200782e3b4fda17aaadc6`. 20 modified tracked files and 18 new untracked files; no staged changes, commits or pushes. Generated previews/logs remain under ignored artifacts and the execution workspace.

```text
 M docs/architecture.md
 M docs/manual-testing.md
 M docs/releases/1.2.7.md
 M src/DesktopTools/AppController.cs
 M src/DesktopTools/Core/AnnotationDocument.cs
 M src/DesktopTools/Core/AppSettings.cs
 M src/DesktopTools/Core/SettingsStore.cs
 M src/DesktopTools/Extras/ImageToolsWindow.cs
 M src/DesktopTools/Extras/LocalOcr.cs
 M src/DesktopTools/Extras/RedactionRenderer.cs
 M src/DesktopTools/Extras/ScreenshotEditDocument.cs
 M src/DesktopTools/Extras/ScreenshotEditorWindow.cs
 M src/DesktopTools/Extras/StepGuideWindow.cs
 M src/DesktopTools/UI/MainWindow.FeatureSettings.cs
 M src/DesktopTools/UI/MainWindow.cs
 M tests/DesktopTools.CaptureTests/Program.cs
 M tests/DesktopTools.IntegrationTests/CaptureLifecycleChecks.cs
 M tests/DesktopTools.IntegrationTests/Program.cs
 M tests/DesktopTools.NativeTests/DesktopTools.NativeTests.csproj
 M tests/DesktopTools.Tests/Program.cs
?? docs/superpowers/plans/2026-09-30-auto-redact-screenshot.md
?? docs/superpowers/specs/2026-09-30-auto-redact-screenshot-design.md
?? src/DesktopTools/AppController.Redaction.cs
?? src/DesktopTools/Core/AutoRedactOptions.cs
?? src/DesktopTools/Core/OcrLayout.cs
?? src/DesktopTools/Core/RedactionReviewState.cs
?? src/DesktopTools/Core/RedactionStyle.cs
?? src/DesktopTools/Core/SensitiveDataDetector.cs
?? src/DesktopTools/Extras/ScreenshotEditorWindow.Redaction.cs
?? src/DesktopTools/Extras/SensitiveDataAnalyzer.cs
?? src/DesktopTools/Localization/auto-redact.de.json
?? src/DesktopTools/Localization/auto-redact.es.json
?? src/DesktopTools/Localization/auto-redact.fr.json
?? src/DesktopTools/Localization/auto-redact.ru.json
?? src/DesktopTools/UI/MainWindow.AutoRedact.cs
?? tests/DesktopTools.CaptureTests/AutoRedactOcrChecks.cs
?? tests/DesktopTools.IntegrationTests/AutoRedactChecks.cs
?? tests/DesktopTools.Tests/AutoRedactTests.cs
```

## Owner-requested refinement — 2026-10-01

- Owner chose **Hide all found** rather than automatic changes immediately after checking. One click creates all covers without row selection; one Undo reverses the group. No Copy/Save occurs until the user requests it.
- Manual entry is now **Check screenshot**. The panel has shorter text, numbered rows and matching canvas badges, a primary bulk button, and compact options using existing DesktopTools controls. The result list remains initially visible in all 20 minimum-size language/theme/layout combinations; selected-row actions appear when needed.
- Reproduced the reported seven-address problem on the supplied screenshot: native OCR read seven address-like words but lost domain punctuation in the five lower small rows. An anonymous generated seven-row fixture also failed with two findings before the fix.
- Bounded OCR tiles are enlarged by two, capped to the Windows OCR dimension limit and mapped back into source pixels. Installed English OCR supplements non-English data detection; intersecting findings are merged.
- The final detection pipeline found seven areas on the supplied screenshot for both English and Russian selections. Only aggregate counts/geometry were logged; the supplied image and OCR values were not copied into the repository or fixtures.
- Generated seven-row small-text OCR, additional English pass without duplicates, Hide all without selection/group Undo, and initially visible list tests all passed after reproducing their failures. The full six-project suite passed (core71/71, native51); final Release build had zero warnings/errors. Original/clipboard/cancel safeguards remained covered by focused integration checks.
- The previous app remained running, so the updated build was placed under ignored `artifacts/auto-redact-refinement/Release/net10.0-windows10.0.19041.0/`. Restart requires exiting the older tray process. Git branch and no-commit rules remain unchanged.

## IP/key detection and button outlines — 2026-10-01

- Reproduced the owner screenshot with English OCR: slashed zeros became ø/Ø, so neither the IPv4 nor prefixed API key matched. Normalizing only IP/credential matching text preserves character offsets and the original word rectangles. Two regression tests failed before the fix and passed afterward, including Russian API-ключ labels and labelled passwords without flagging ordinary password advice.
- The supplied editor screenshot and its code strip now each yield one IP and one credential area under English and Russian OCR. No supplied image or recognized secret was copied into this repository.
- Added a generated dark Consolas code strip: real Windows OCR finds its IP, API key and labelled password in physical image coordinates. Existing seven-email and tiled-email fixtures still pass.
- The three secondary/manual buttons use the existing theme Divider brush for their 1px border. The boundary contrast check failed before the change and passes across all 20 language/theme/layout cases. Inspected Russian Light A and Dark B previews. The credential category explicitly includes passwords in all five languages.
- Verification: six-project suite exit0, core73/73/native51; Release solution build zero warnings/errors; focused privacy/bulk/outline/20-layout checks exit0. Actual clipboard Copy skipped to preserve the nonempty user clipboard. Physical mixed-DPI displays and native-speaker review remain Not tested.
- Branch codex/guides-recording-notes, HEAD501e6dd unchanged; 20 modified tracked +18 untracked files, nothing staged. New build: ignored artifacts/auto-redact-refinement-2/Release/net10.0-windows10.0.19041.0/.

## Complete-field OCR refinement — 2026-10-01

- Owner reported partial username/telephone covers and missed email, IPv6, paths, account identifiers and serials. Reproduced these on an anonymous generated full25-value dark Consolas fixture. Font14 English detection initially missed parts of emails and several categories; after first fixes font10 exposed s/ø phone digits and Account 10/1b label confusions.
- Detector now preserves spaced phone readings, joins tiny identifier fragments, handles measured numeric/separator substitutions and covers complete labelled values. Native OCR line membership preserves punctuation; independent 2x/3x readings avoid replacing a good sampling result with a bad one. Installed English and app-language recognizers supplement selected OCR without duplicate languages. The new guidance is translated in RU/DE/FR/ES.
- Original owner PNG was tested at its original resolution, without editor overlays. First result23/25 exposed star-for-@ OCR and complementary label/value readings. Additional regressions failed before the fix and pass afterward. The final original-image pipeline yields25/25 under English and Russian selections: email3, phone2, IP4, path2, credential8, username2, account2, serial2. Logged only aggregate counts and bounds; no original image or raw values copied into repository fixtures.
- A known label can use nearby value boxes from another reading, with finite bounds, same-row overlap, indexed Y lookup and a bounded contiguous horizontal gap. Combined context layouts retain distinct line indexes and existing input caps. Email-shaped asterisk matches remain suggestions; approximate recovery can produce false positives, and complete-field covers may include extra words on the same labelled row.
- Read-only code review found one Important introduced issue: compact joining removed local phone separators. The three-box local-phone regression failed then passed after preserving the spaced phone reading. Follow-up review found no remaining Critical/Important issues in the latest changes.
- Final verification: core80/80; six-project suite exit0 including native51, QR/capture/real OCR, images, updates and installer; generated25-value tests pass font14/font10 with en-US/ru and complete horizontal bounds. Release solution build has0warnings/0errors. Focused privacy/bulk/output/20-layout integration checks exit0. Actual clipboard Copy skipped to preserve nonempty user clipboard. Physical mixed-DPI displays and native-speaker wording remain Not tested.
- Files refined this iteration: Core/SensitiveDataDetector.cs; Extras/LocalOcr.cs, SensitiveDataAnalyzer.cs, ScreenshotEditorWindow.Redaction.cs; four auto-redact catalogs; core/capture fixtures and capture test runner; architecture, manual checklist, release notes and this plan. Branch codex/guides-recording-notes and HEAD501e6dd remain unchanged;20modified tracked+18untracked, nothing staged, no commits/push.
- Ready build: ignored artifacts/auto-redact-refinement-3/Release/net10.0-windows10.0.19041.0/. Previous PID14540 still runs refinement-2; requested ordinary tray Exit before replacement. No forced app termination, installation, release publication or Git history changes.
Owner confirmed Exit; the new refinement-3 build is now running as PID26628. Its exact executable path, DesktopTools window title and Responding=true were verified.

## Two-column table refinement — 2026-10-02

- Reproduced the owner's clean table image at its original resolution: seven findings missed User ID, the Russian password and three nicknames. Native Windows OCR placed the label and value columns on separate lines without colons. The third email also had an uncovered local-part prefix despite being counted.
- Added strict standalone table labels for User ID, numbered nicknames and emails. Two nearby aligned labelled rows corroborate the two-column layout. Contiguous value cells join across native OCR lines and complementary readings; large gaps stop before unrelated columns. Only enabled categories generate findings. Ordinary password advice and unaligned far-column text remain excluded by regression tests.
- Regression tests failed before fixes and pass afterward. Read-only review identified separately indexed value suffixes and short first fragments as additional coverage gaps; both were reproduced and fixed. Final focused review reports no remaining Critical/Important findings.
- Final source-image analysis under en-US and ru yields 12/12 complete values: username3, email3, phone2, IP1, credential2, account1. Assertions check full value bounds, including User ID, password and the third email prefix. Original images/transcripts were not copied into tracked fixtures; generated table fixtures use fictional data.
- Verification: core85/85, native51, complete six-project suite exit0, real OCR table12/12 and prior25-value samples at both font sizes/languages pass. Release solution build0warnings/0errors. Focused privacy/bulk/output/20-layout integration exit0. Real clipboard Copy skipped to preserve nonempty clipboard; physical mixed-DPI displays and native-speaker review remain Not tested.
- Files changed this iteration: src/DesktopTools/Core/SensitiveDataDetector.cs; tests/DesktopTools.Tests/AutoRedactTests.cs; tests/DesktopTools.CaptureTests/AutoRedactOcrChecks.cs; docs/architecture.md, docs/manual-testing.md, docs/releases/1.2.7.md and this plan. All feature changes remain uncommitted on codex/guides-recording-notes, HEAD501e6dd unchanged,20modified tracked+18untracked,nothing staged.
- New local build: artifacts/auto-redact-refinement-4/Release/net10.0-windows10.0.19041.0/DesktopTools.exe. Previous refinement-3 PID26628 remains running; requested ordinary tray Exit before launching the replacement. No forced app termination, installation, release publication or Git history changes.
- Table association intentionally requires a second aligned labelled row. Isolated labels without separators, unusual spacing and inaccurate OCR may require manual covers; suggestions still need visual review.

Owner confirmed tray Exit. Verified no prior DesktopTools process, then launched refinement-4 as PID26080. Exact executable path, window title DesktopTools and Responding=true verified. No forced termination.

## Unlabelled mixed-size handle lists — 2026-10-02

- Reproduced the latest clean source at original resolution: nine findings, with all three unlabelled nicknames missed although both recognizers read them. Font size was not the primary cause: existing username matching required a field label. OCR also dropped underscores/split suffixes. No original image or source transcript was copied into tracked fixtures.
- Added conservative unlabelled-list suggestions for structured underscore identifiers and mixed-case numeric names. At least two distinct aligned rows corroborate a list; physical text heights set spacing tolerances across fonts. Nearby fragments join across native lines. Complementary overlapping suffixes extend cover geometry without duplicating already valid text. Enabled categories remain respected.
- The previous generated table initially regressed to15/12 because distorted English OCR labels resembled names. Added a failing label-column regression and excluded known labels and corroborated separate table-label columns. Previous source table is again12/12 under EN/RU, with complete value bounds.
- A generated full mixed-size 9/11/14/18 fixture exposed e-for-zero substitutions in formatted phone digits. Added a failing phone/date regression, normalized measured substitutions and kept date/IP/digit validation. The mixed-size fixture now has12/12 complete values under EN/RU. Both owner source images are12/12 complete values in the latest pipeline, including all nicknames and password/User ID; former25/7/tiled fixtures remain covered.
- Independent read-only review found one overlapping suffix coverage issue, reproduced RED then fixed. Final focused review reports no remaining Critical/Important findings. Core91/91 has regressions for different sizes, short/separate/overlapping suffixes, unrelated columns, category isolation, distorted labels and ordinary prose.
- Changed files this iteration: src/DesktopTools/Core/SensitiveDataDetector.cs; tests/DesktopTools.Tests/AutoRedactTests.cs; tests/DesktopTools.CaptureTests/AutoRedactOcrChecks.cs; docs/architecture.md, docs/manual-testing.md, docs/releases/1.2.7.md and this plan. New ignored build output: artifacts/auto-redact-refinement-5/Release/net10.0-windows10.0.19041.0/.
- Unlabelled detection is intentionally heuristic. Plain names, isolated unlabelled identifiers and unusual OCR spellings may still need manual covers; identifier-shaped ordinary text can produce false positives. No new UI text was introduced; existing translated username category controls remain in use.

- Final verification: core91/91/native51; full six-project suite exit0; Release solution build0warnings/0errors; focused privacy/output/bulk/20-layout integration exit0. Actual clipboard Copy skipped to preserve the nonempty clipboard. Physical mixed-DPI displays and native-speaker wording remain Not tested. Both supplied12-value source images and the generated mixed-size fixture pass complete-bounds assertions under EN/RU; previous25/7/tiled fixtures pass.
- Runtime: no prior DesktopTools process was present. Launched refinement-5 for owner testing as PID21092; exact path, DesktopTools title and Responding=true verified. No forced termination, installation or release publication. Branch codex/guides-recording-notes/HEAD501e6dd unchanged;20modified tracked+18untracked,nothing staged; no commits/push.

## Mixed-size narrative journal refinement — 2026-10-02

- Reproduced the owner's clean 693x1011 journal: the initial pipeline yielded24 merged regions, with all password values and several punctuated/OCR-distorted IPv4 addresses missed. Added prose/complex-secret suggestions, corroborated wrapped prefixes, sentence-period handling and measured S-for-five digit/prefixed-key recovery. Preserve complete source-word bounds, enabled categories and masked finding labels.
- Final original-image checks cover all40 values:12 emails,11 IPv4 addresses,6 keys and11 passwords. The wrapped password occupies two physical lines, so independent geometric assertions check41 pieces, not just finding count. Both en-US and ru selections with installed Russian app-language OCR cover every piece in36 merged regions. Source pixels/transcripts were not copied into tracked fixtures; source diagnostics retain aggregate categories/counts, masked shapes and coordinates only.
- A generated natural-prose fixture independently exposed words omitted entirely by Windows OCR on a mixed-size page. Inversion did not help, a96px strip did not recover them, but a narrow42px strip at3x did. Added bounded narrow-row readings for primary words at most9 source pixels, with source offsets, cancellation, at most64 extra tiles per recognizer and a10,000 combined-word limit. The full generated journal now passes41 complete pieces with8/10/11/13/14 fonts under EN/RU; previous25-value,12-table,12-list,7-email and tile-boundary fixtures still pass.
- Documented stress limitation: IP octets in an8px Consolas font had only5–6px raster glyphs and remained corrupted after cropping, inversion,4x/6x and nearest/threshold experiments. The numeric tail of the generated fixture uses readable10px text; its passwords/keys retain8px text. IP validation was kept strict rather than accepting those corruptions as addresses. This result does not promise recognition of all ultra-small text; manual covers remain available.
- Read-only review identified ordinary URL and detached arithmetic-operator false positives. Independently reproduced RED regressions for separate-line URL context and `total * rate2026`, then fixed complete-token/URL guards and kept spaced `*`, `&`, `?` separate. Direct URL/advice, label/value complementary readings, wrapped values, detached password punctuation and measured five-for-s key regressions also pass. Final focused static review has no remaining Critical/Important findings.
- Files changed this iteration: src/DesktopTools/Core/SensitiveDataDetector.cs; src/DesktopTools/Extras/LocalOcr.cs; tests/DesktopTools.Tests/AutoRedactTests.cs; tests/DesktopTools.CaptureTests/AutoRedactOcrChecks.cs; docs/architecture.md; docs/manual-testing.md; docs/releases/1.2.7.md; this plan. No new UI strings were introduced; existing five-language category controls remain in use. Ignored output is artifacts/auto-redact-refinement-6/Release/net10.0-windows10.0.19041.0/.
- Verification: final six-project suite exit0, core99/99/native51; native journal and all prior capture fixtures pass; Release solution build0warnings/0errors. Focused privacy/output/bulk/20-layout checks also passed. Actual clipboard Copy remains skipped to preserve the nonempty clipboard; physical mixed-DPI gestures and native-speaker wording remain Not tested. Branch codex/guides-recording-notes/HEAD501e6dd unchanged; all work remains uncommitted. Earlier refinement-5 app retained until ordinary tray Exit for the replacement build.
- Runtime handoff: owner confirmed ordinary tray Exit; no existing DesktopTools process was present. Launched refinement-6 as PID16088, verified exact executable path, DesktopTools title and Responding=true. No forced termination, installation or publication.

### Mixed-font inline and wrapped-key refinement — 2026-10-02

- Latest clean owner source is 732x1026: four emails, eight IPv4 addresses, five prefixed keys and five passwords, with three wrapped keys (22 logical values /25 physical pieces). Baseline yielded17 merged regions with missed tails, an omitted bottom password and ordinary-word false positives. Independent expected rectangles were measured from source pixels; source images and transcripts remain outside tracked fixtures.
- Gap experiments showed the missing inline value is read at3x in a small local crop even when full-page and narrow-row OCR omit it. Added pure OcrGapRegions planning, <=64 gap crops per recognizer, cancellation/clipping, X/Y source offsets and existing tile/pixel/combined-word limits. Over-budget analysis requests cropping.
- Wrapped prefixes use the nearest physical continuation row, compact gaps and separate covers. Adjacent native-line fragments are assembled before validation; duplicate readings extend bounds without repeating contained text. Valid IP spans split greedy phone candidates without changing offsets. Unlabelled secret shapes require at least two digits; contextual smaller passwords remain supported. Neighbouring padded rows do not merge into broad rectangles.
- New regression tests were observed RED then GREEN: gap geometry/budgets, wrapped keys, ordinary-word/phone false positives and row merge; reviewer cases real phone+IP and unrelated later row (105/107->107/107), then native-line key suffix (107/108->108/108). Generated real OCR fixture covers five wrapped-key/password pieces under EN/RU and preserves a later ordinary row. Previous real OCR fixtures passed before final suite.
- Final suite/build/source checks and review results are recorded below once completed. No new UI strings. Files refined this iteration: Core/SensitiveDataDetector.cs, new Core/OcrGapRegions.cs, Extras/LocalOcr.cs, tests/DesktopTools.Tests/AutoRedactTests.cs, tests/DesktopTools.CaptureTests/AutoRedactOcrChecks.cs, architecture, manual-testing, release1.2.7 and this plan. Ignored output: artifacts/auto-redact-refinement-7/Release/net10.0-windows10.0.19041.0/.
- Final review exposed a complementary terminal-reading geometry gap, reproduced108/109; the new native fixture also exposed a corrupt alternate word suppressing a valid reading. Overlapping physical words now consolidate geometry and use one valid representative before punctuation/length validation. Regression includes wider terminal bounds, corrupt alternate, duplicated short text and separate suffix. Core109/109 green; final suite restarted with this version.
- Fresh final verification: six-project suite exit0, core109/109 and native51; generated wrapped fixture plus previous25/table12/list12/email7/tiled/journal41 fixtures pass EN/RU. Latest owner source all25 physical pieces (22 values) in25 regions under EN/RU, with negative ordinary-word/phone assertions; previous source journal all41 pieces still covered. Release solution build0warnings/errors. Focused privacy/editor/output/bulk/20-layout integration exit0; actual reviewed Copy skipped to preserve a nonempty clipboard, pre-review and Save checks passed. Final static follow-up reports no Critical/Important findings.
- Not tested this iteration: physical mixed-DPI monitor interaction, remote sharing, native-speaker wording and a fresh owner manual acceptance pass. OCR still cannot guarantee every very small/damaged glyph. Source screenshots/transcripts remain external and untracked; no new UI text or settings change.
- Final Git: codex/guides-recording-notes, HEAD501e6dd705536533c38200782e3b4fda17aaadc6 unchanged,20modified tracked+19untracked feature files, nothing staged. git diff --check passed. No commits/push/PR/merge/rebase/remotes or identity changes.
- Owner explicitly authorized closing DesktopTools without further approval. Windows Computer Use failed to initialize (kernel assets path missing), so old verified refinement-6 process16088 was terminated by exact identity/path. First refinement-7 launch exited because system .NET lacks10. Relaunched with the already available local .NET10 path in child-process environment, without installation or global environment changes (PID13520). No installation/release publication. Path/window/response verification recorded in the local execution ledger.

### Full-monitor OCR and initial editor size — 2026-10-03

- Reproduced the owner's full-monitor failure with the supplied 2305x1056 screenshot of the editor: the primary English reading had 309 words and 60 small rows; the old supplemental full-width row tile count exceeded 64. This was a work-budget failure, not an absence of sensitive text. The supplied screenshot includes a reduced preview and is not the original monitor bitmap.
- Added OcrRowRegions: bounded row planning skips uniform horizontal cells using actual pixel detail, refines occupied edges and keeps a ten-pixel horizontal context margin. This preserves trailing values omitted entirely by OCR, unlike cropping to recognized word bounds. Wide-image strips retain an extra top context pixel. Narrow-image strip geometry stays unchanged. Limits are 256 regions/row tiles, six million source pixels, existing image/tile/combined-word limits and cancellation.
- Removed whole-image-width assumptions from wrapped credential continuation. The same wrapped values can now be recognized in a column offset to the center/right of a desktop. Added dotted password shapes; ordinary emails, sentence punctuation, punycode email TLDs, versions and URLs have exclusion regressions.
- The screenshot editor sizes once on Loaded to 75% of its actual monitor work area, centered in physical pixels with WPF DPI conversion. Subsequent resizing is retained. Image dimensions and zoom are unchanged.
- RED/GREEN checks: full-monitor row budget/coverage, omitted trailing value, translated wrapped geometry, dotted secret/exclusions and initial 75% editor size. Core tests now114/114. A generated fixture copies the existing journal pixels byte-for-byte to (1100,100) on a2560x1440 desktop with navigation text; all41 pieces at8/10/11/13/14 fonts pass under EN/RU. Existing cropped journal and other native OCR fixtures remain unchanged and pass. The supplied reduced-preview screenshot now completes with14 suggested regions under EN/RU; this does not claim complete recognition of its downscaled contents.
- Read-only review caught and resolved trailing-value clipping and dotted mail false positives, with observed failing regressions before fixes. Final follow-up reported no remaining Critical/Important findings. Six-project suite passed; after the final email-exclusion adjustment, fresh core114/114 and the complete focused native OCR suite passed again.
- Files changed in this iteration: src/DesktopTools/Core/OcrRowRegions.cs (new), Core/SensitiveDataDetector.cs, Extras/LocalOcr.cs, Extras/ScreenshotEditorWindow.cs; tests/DesktopTools.Tests/AutoRedactTests.cs, tests/DesktopTools.CaptureTests/AutoRedactOcrChecks.cs, tests/DesktopTools.IntegrationTests/AutoRedactChecks.cs; docs/architecture.md, docs/manual-testing.md, docs/releases/1.2.7.md and this plan. Existing supported-language text is reused; no new strings or settings were introduced. Source screenshots and diagnostic transcripts remain outside tracked content. Local output: artifacts/auto-redact-refinement-8/Release/net10.0-windows10.0.19041.0/.
- Remaining limits: OCR can miss tiny/damaged text; manual covers remain necessary. Physical mixed-DPI/negative-origin monitor interactions and fresh owner acceptance are Not tested. Clipboard contents are preserved during integration checks.
- Final verification: Release solution build succeeded with0warnings/0errors; focused editor/privacy/output/bulk/layout integration passed, including initial75% size. Reviewed Copy was skipped to preserve the nonempty clipboard; pre-review/Save checks passed. git diff --check passed. Branch codex/guides-recording-notes and HEAD501e6dd unchanged; no commits, staging, push or PR.
- Runtime: with the owner's standing authorization, closed the verified refinement-7 process21036 and launched refinement-8 using existing local .NET10 in the child environment. PID7988, exact executable path, visible DesktopTools window and Responding=true verified. No installation or global environment changes.

### Browser-sidebar and wrapped numeric-tail refinement — 2026-10-03

- Reproduced24 regions on the new2560x1392 owner source. Most omitted pieces were wrapped API-key prefixes/tails; the globally nearest row belonged to the browser sidebar. Cropping the exact same body pixels recovered many of them, confirming layout-dependent detection rather than lost source resolution.
- Cross-row detection now uses preceding text on the same physical row to establish the paragraph's left boundary even across separate native OCR lines. A Y-index limits nearby work before column filtering. The immediately following body row remains mandatory; unrelated sidebar rows no longer block it.
- Added conservative numeric-tail recovery for mixed-case prefixes with an internal hard symbol and a terminal hard symbol, followed by4-12digits on the next body row. Prefix/tail remain separate covers; ordinary URL fragments and following years are excluded.
- New core regressions observed RED->GREEN for sidebar interference, a separate-native-line prefix, numeric tails, URL exclusion and excessive intermediate allocation. The2000-prefix/4000-word case previously allocated671MB; indexed lookup passes the128MB guard. Core118/118 pass. New generated native desktop fixture covers six wrapped key/password pieces with10px/8px fonts and interleaved sidebar rows; EN/RU pass. Expected owner rectangles are measured independently from original pixels; digit-tail bounds exclude following sentence punctuation.
- Files changed this refinement: Core/SensitiveDataDetector.cs; tests/DesktopTools.Tests/AutoRedactTests.cs; tests/DesktopTools.CaptureTests/AutoRedactOcrChecks.cs; architecture, manual testing, release1.2.7 and this plan. No UI strings/settings changes or original-image edits. Temporary reference crops/diagnostics stay in ignored local artifacts; no user source or OCR transcript is added to tracked fixtures.
- Final source, suite, build, review and runtime checks follow below. Known scope: suggestion accuracy is approximate, including occasional OCR-derived false positives; no universal guarantee for arbitrary tiny/damaged text. Physical mixed-DPI interaction and new owner acceptance remain Not tested.
- Final review also exposed a paragraph-start loss when preceding context occupied several native OCR lines. Combined physical-row fragments within the bounded context window; the exact split-context/sidebar regression was117/118RED then118/118GREEN. Read-only final follow-up reports no remaining Critical/Important findings. A pre-final six-project suite passed; focused native/source checks were rerun after that last context adjustment.
- Owner source verification before the final context adjustment covered all33 physical pieces (29logical values: five emails, nine IP addresses, six keys and nine passwords) under EN/RU, producing32 merged regions. Region count is not an accuracy measure: some neighbours merge and occasional OCR false positives remain. Full source verification is repeated below for the final binary.
- Final source recheck after the context-fragment fix: all33 expected physical pieces covered under EN/RU, zero misses,32merged suggestions. Fresh focused native OCR suite passed all previous fixtures and new six-piece sidebar fixture. Final core118/118 passed; six-project suite had already passed before the last context-only adjustment. No source reference values or OCR transcript were added to tracked tests.
- Final Release solution build: zero warnings/errors. Focused privacy/output/editor integration passed; actual reviewed Copy skipped to preserve a nonempty clipboard, pre-review/Save checks passed. git diff --check passed. Branch codex/guides-recording-notes, HEAD501e6dd unchanged,20modified tracked+20untracked entries, nothing staged; no commits/push/PR.
- With standing owner authorization, closed the verified refinement-8 process13068 and launched refinement-9 using existing local .NET10 only in the child environment. PID29772, exact executable path, visible DesktopTools window and Responding=true verified. No installation, publication or global environment changes.
