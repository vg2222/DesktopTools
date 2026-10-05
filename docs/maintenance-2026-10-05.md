# DesktopTools maintenance audit — 2026-10-05

Base: `736bbc3` on current GitHub `main` (DesktopTools 1.2.8). Work branch: `codex/performance-maintenance`.

The goal is to retain all current features, offline processing, capture geometry, immutable originals and existing accuracy safeguards while removing measured waste and concrete lifecycle hazards. No UI architecture replacement, recording-engine change, permanent ONNX cache or reduced OCR reading budget was selected.

## Findings and decisions

| Priority | Evidence | Decision |
|---|---|---|
| P1 | Reset/import shutdown did not await the recorder, although quit/language/update did | Share the existing asynchronous recorder finalization boundary across all these paths |
| P1 | A queued display/theme callback could still execute after controller disposal | Guard inside the queued delegates |
| P1 | The editor's 120ms style timer survived disposal | Stop/detach it, clear the preview and reject further preview work; dispose idempotently |
| P1 | A 9,047,436-byte interrupted model download was included by the content glob | Exclude `.download` and validate package inventory before archiving |
| P2 | Three sentences recreated vocab/tokenizer/two ONNX sessions three times | Reuse a disposable context per translation route stage, with sentence-local cancellation/output caches |
| P2 | Privacy bitmap preparation accounted for about 3.8 s of a 6.8 s profiled scan | Preserve WPF scaling and native OCR input bytes; copy verified pixel formats directly into one owned native bitmap |
| P2 | Brightness sampling allocated a 32 MB managed pixel array for an 8 MP image | Read the same sampled rows into one 16 KB row buffer |
| P2 | Phone validation constructed regex objects per match | Reuse identical regex instances and select enabled rules once per scan |
| P2 | Left rotation materialized three full images | Materialize one -90degree rotation, with pixel/DPI/history regression checks |
| P2 | Automation always started a 1 Hz timer, including empty/manual-only workflows | Poll only while an armed automatic trigger requires it; preserve deferred startup and stop consumed startup-only polling |
| P2 | Raw catalog inspection found 32 conflicting translations masked by merged-catalog tests | Validate every JSON/catalog family and normalize conflicts to the existing first-resource winner |
| P2 | CI redownloaded unchanged dependencies/models and implicitly rebuilt harnesses | Cache NuGet and pinned ONNX weights, always verify checksums, build all 7 test projects, then use optional `-NoBuild` |
| P3 | Bundled documents contained 16 broken relative links | Include the small missing documents and validate package links |

## Architecture and ownership audit

`AppController` already delegates capture/recording, overlays, settings persistence, utility state and model operations. Its partial files and lazy utility factories are useful existing boundaries. One narrow shared shutdown preparation method addresses a real inconsistency; wholesale service extraction was not justified by file length.

MainWindow uses feature-specific partials; screenshot editor state/rendering is owned by ScreenshotEditDocument and ScreenshotEditorView. Rendering already caches the committed picture and draws only the active stroke. Image edit history is bounded by count and 128 MB, and transform materialization intentionally avoids retained edit chains. Keep these mechanisms.

Reviewed GDI object/DC restoration, DWM thumbnail replacement, recording startup/source cancellation, recorder disposal, marker hotkeys/store limits, presentation callbacks/timers, utility close subscriptions, image/OCR cancellation, ONNX first/previous output lifetimes and semaphore release. The concrete changes above target observed gaps; this is not a claim that every lifetime is proven on every machine.

## Measurements

Environment: Windows build 26300, 12 logical processors, installed English and Russian Windows OCR packs. Synthetic data only. Measurements are machine-specific samples, not universal promises.

| Same-machine check | Before | Accepted change |
|---|---:|---:|
| Privacy scan2560x1440,18 generated mixed 8/10 px rows, separate old/new assembly snapshots | 7.14s | 4.35s |
| Sampled peak private memory for that isolated scan (50ms sampling) | 640.8MiB | 543.3MiB |
| Sampled peak working set for that scan | 642.1MiB | 574.4MiB |
| Profiled bitmap preparation,73 calls | 3791ms | 583ms |
| Phone detector,1200 findings, warm second run | 89.61ms /13,983,448 allocated bytes | 53.69ms /5,595,896bytes |
| Translation,3 fixed sentences EN→RU | 5344.82ms /150,985,688 allocated bytes | 2636.36ms /93,057,784bytes |
| Single sentence translation | 2600.10ms | 2577.23ms |
| Left rotation2560x1440, same picture | three rotations79.63ms /44,236,800pixel payload bytes | one rotation25.87ms /14,745,600bytes |
| Polarity sampling,8MP | 32,000,024managed allocated bytes | 16,024bytes |
| Raw localization conflicts | 32 | 0; all19,536 merged language values preserved |

The full privacy stage profiler measures Windows OCR, bitmap preparation, row/gap planning and reading, layout assembly, supplemental language, detection, merges and cover creation. Nested timings are not additive. `PipelineMetrics` is internal and opt-in via a benchmark scope; the app installs no observer and it persists neither images nor recognized text.

The mixed 8 px benchmark was already imperfect in the base assembly: some rows are missed. The accepted change preserves the complete baseline set of 18 geometry/category results; a count alone is not treated as proof of accuracy. Existing complete-value OCR fixtures remain the quality gates. A first managed-scratch transport prototype raised peak private memory to813.5MiB and was rejected; the accepted native-buffer path avoids that tradeoff.

## OCR transport safeguards

The original TransformedBitmap scaler, magnification, tile/overlap geometry, reading plans, budgets and language passes remain intact. Fast transport is restricted to tested Bgra32/Pbgra32/Bgr24/Gray8 inputs. Other pixel formats retain PNG conversion. Tests compare the actual production transfer with the previous PNG path byte-for-byte across 32 alpha/format/scale/DPI cases and verify returned native bitmap independence. Native pointer use is confined to the live buffer lock/reference; capacity, stride, plane dimensions and last-row extent are checked before WPF copies. Locks are released before return and every failure disposes the owned bitmap.

Interop design follows [Microsoft buffer access documentation](https://learn.microsoft.com/en-us/windows/win32/winrt/imemorybufferbyteaccess-getbuffer) and [C#/WinRT COM interop guidance](https://github.com/microsoft/CsWinRT/blob/master/docs/interop.md).

## Startup, recorder and retained follow-ups

Smoke controller construction measured1676.57ms; a30s hidden smoke-controller idle sample measured0.122% CPU normalized across12 logical processors,216.2MiB private memory,574 handles. This excludes native normal-startup/hotkey registration and is not a before/after startup claim. Empty/manual automation polling removal is verified by timer-state tests.

The recorder engine/source monitoring/marker system was deliberately retained. Two candidates remain for separately measured work: audio meter polling when both sources are off or studio/HUD are invisible; and tiny region previews that retain a full monitor through CroppedBitmap (a4K source is about31.6MiB). The latter is bounded retention, not an accumulating leak. Persistent endpoint caching would need device-change handling; a detached preview must preserve original selected pixels and DPI.

Background-removal model hashing/session initialization remains per request. Its model is approximately4.9MB and a persistent cache was not introduced without evidence that initialization dominates inference/mask work. Notification-duration slider saves, repeated automation parsing and cached text-view render geometry are follow-up candidates, not proven improvements in this pass.

## Packaging and CI

The12 verified bundled translation assets total228,460,480bytes; the4 ONNX weights account for221,120,044bytes. They are useful offline functionality. The accepted packaging exclusion removes the9,047,436-byte incomplete download without deleting source data or verified models; restoring missing linked documentation initially adds11,690bytes.

NuGet cache keys include SDK/project/build inputs. Model cache keys derive strictly from the checksum manifest and every hit still runs the fetch/verification script. Release concurrency waits for an active publication rather than cancelling it. All 7 test projects now belong to the solution; standalone `test.ps1` still builds as needed, while the validated CI path can use `-NoBuild` after the solution build. Desktop/native harnesses stay sequential: potential hosted-job parallelism was considered, but unmeasured artifact/model transfer costs and desktop isolation do not justify claiming a benefit.

## Verification

Baseline solution build and default 6 harnesses passed before changes (141core tests,54native checks). Regression tests were observed failing before fixes for phone allocation, idle trigger polling, queued callbacks, recorder finalization boundary, disposed preview timer, raw localization conflicts, package guard and full-image brightness allocation.

After changes: build and `scripts/test.ps1 -NoBuild` pass,144core tests and54native checks; complete privacy OCR fixtures pass; installer/package temporary-file/link fixtures pass; focused lifecycle, image/preview disposal, translation exact outputs/cancellation/later-sentence token rejection and pixel/transport tests pass. A safe reset fixture uses only a disposable data directory. Independent read-only review found no remaining critical/important issue and confirmed no merged localization value change.

The wider240-image/seed21/current OCR comparison, interactive recorder/session checks and actual package inventory are recorded below after final runs.

Not tested unless explicitly recorded below: hours-long recording, live audio capture/device switching, fresh multi-monitor mixed-DPI user interaction, ARM64 execution, remote sharing, hosted CI cache hit/timing, ordinary installer execution and native-speaker wording. Source screenshots, recordings, diagnostics and local bundles are not tracked.
### Final measured gates

- The exact original benchmark source was run against the base and changed assemblies:240 images,1582 phrases, seed21, mode`current`. Both report91.1% exact phrases and98.0% characters on this PC. Size/scheme/font breakdowns and every recorded failure detail are identical. Elapsed time was126s versus105s; accuracy is the acceptance gate, not matching another development PC's percentages.
- Interactive owned-window recording succeeded after the sandbox's Graphics Capture limitation was isolated:32.3s wall,29.5s encoded,1.302% normalized CPU,219.0MiB peak private memory. Audio was off, software encoder requested30FPS; actual distinct-frame FPS was not measured. Recorder clock/source/pending-start checks, safe reset/restart fixture and update UI checks pass. Hours-long recording/audio remain Not tested.
- The complete solution build finished with 0 warnings / 0 errors, followed by the validated matching-output `test.ps1 -NoBuild` six-harness path.144/144core and54native checks pass, including package guards and every complete-value privacy OCR fixture. Focused pixel/transport, translation, lifecycle and editor tests pass. Independent native-boundary follow-up found no critical/important issue.
- Local x64 package construction produced a263,450,824-byte portable ZIP and395,805,704-byte setup executable. The portable tree has563files /487,401,209bytes,0interrupted downloads and0`.so`/`.dylib` files. Translation models dominate the payload; RID publishing already excludes other-platform binaries. These are local preview assets, not a published release or an executed installer. The initial host build's optional Git metadata query encountered sandbox ownership; final provenance is verified separately with command-scoped trust for the exact repository.
