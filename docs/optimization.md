# Optimizing DesktopTools safely

Speed work is welcome, but several parts of DesktopTools trade time for accuracy or privacy. Measure before and after, keep the numbers in the pull request, and do not accept a faster result that reads text less accurately or finds less private data.

All numbers below were measured on the development PC (Windows 11, English and Russian OCR packs installed) and move with the machine. Compare runs on **your** machine, before and after your change, not against these figures.

## Text recognition

The reader is `LocalOcr.RecognizeEnhancedAsync` / `RecognizeWordsAsync`: a quick 2x reading, then plain, grey and ink-map readings at the magnification suited to the text, combined word by word (`OcrConsensus`) with the Windows spelling dictionary (`SpellLexicon`). [text-tools.md](text-tools.md) explains the method.

| Check | Command | Baseline |
|---|---|---|
| Wide ClearType benchmark | `OCR_BENCH_IMAGES=240 OCR_BENCH_SEED=21 OCR_BENCH_MODES=single,current dotnet run --project tests/DesktopTools.IntegrationTests -c Release -- --ocr-bench` | `current` 92.9% exact phrases, 98.5% characters (1,582 phrases); `single` 69.9% |
| Same, by size | printed in the report (`artifacts/integration/ocr-bench/report.txt`) | 12 px and larger 95-100%, 9-10 px 69-72% |
| Earlier synthetic cases | `... -- --ocr-quality-only` | average 96% (single pass 79%) |
| Word choice and repair logic | `... -- --ocr-consensus-only` | passes |
| Real picture, timing | `OCR_IMAGE=<png> ... -- --ocr-quality-only` | 2240 x 1600 screenshot about 3 s |
| Privacy detection (separate code path) | `./scripts/test.ps1` (CaptureTests) | passes |

`OCR_BENCH_MODES` also accepts single readings `v:<plain|ink|gray>:<scale>:<hq|legacy>`; with several modes the report adds an *oracle* row, the best any reading achieved, which is the ceiling for any selection logic.

Where the time goes, roughly in order: the number and size of readings (`PlanFor`, `ReadingBudget` in `LocalOcr.cs`; cost is pixels x scale squared), `OcrEnhancer.InkMap` and `Enlarge`, bitmap preparation in `ScaledBitmapAsync` (verified pixel formats now copy directly into an owned native buffer; other formats retain PNG conversion), and the pairwise matching in `OcrConsensus.Merge`. Ideas worth measuring: skip the ink-map reading on pictures that are clearly light and high-contrast, reuse the enlarged tiles between readings, decide the plan from the first reading's quality instead of always running all of it.

Rules for this area:

- The sensitive-data check (Hide private data, `RecognizeLayoutAsync`) is a separate path tuned with the original magnification. Changing its readings changed its results before; run `./scripts/test.ps1` after any change that touches `LocalOcr.cs` or `OcrEnhancer.cs`.
- A change that lowers the benchmark's exact-phrase rate needs a reason in the pull request.
- Always compare the same seed and image count.

## Text view and editors

- `TextSelectionView.Surface.OnRender` rebuilds the dimming geometry on every render; pictures with thousands of words are the case to profile.
- Drawing in the editors records only the stroke over a cached picture; `--editor-shots` prints the cost per mouse move on a 2560 x 1392 picture (about 0.04 ms).

## Recording

`--record-probe`, `--record-throughput-only` and `scripts/analyze-recording-probe.py` measure frame rate, dropped frames and corrupt frames of a real recording; [screen-recorder.md](screen-recorder.md) lists the findings. These need an interactive Windows session.

## Memory during ordinary use

`--memory-soak` repeats one user action per round against an owned, borderless window that fills one monitor and repaints a new pattern every round. It samples private bytes before and after a forced collection (every 10 rounds, the harness only), GDI/USER objects and handles, and classifies each scenario: **Leak** (private bytes after collection rise more than 32 MiB between rounds 20-40 and the last quarter), **LateRelease** (only the uncollected value rises), or **Bounded**. CSVs go to `artifacts/integration/memory-soak-<scenario>.csv`.

```powershell
$env:DESKTOPTOOLS_CHECK_MONITOR = 'DISPLAY1'
dotnet run --project tests/DesktopTools.IntegrationTests -c Release -p:RecorderBackendPath=<absolute path to ScreenRecorderLib.dll> -- --memory-soak --scenario capture --rounds 200
```

Capture rounds run the controller's own steps (capture, crop, last capture, history, clipboard encoding without writing the clipboard, capture notice) instead of `CaptureAsync`, because that command hides every app window and writes the real clipboard. The selection overlay itself is not soaked.

### Baseline, 2026-11-12

Development PC: Intel Core i5-11600K, 15.9 GB RAM, NVIDIA GeForce RTX 3060 Ti, Windows 11 Pro 10.0.26300. Monitor: MSI MP252 (`DISPLAY1`), 1920 x 1080 at 100%. Release build with the patched recorder backend, 200 rounds per scenario, each scenario in its own process.

| Scenario | Private before collection, MiB (round 20 → 199) | After collection, MiB (round 20 → 199) | 60 s idle, MiB | GDI (round 20 → 199) | USER | Handles | Trend |
|---|---|---|---|---|---|---|---|
| capture | 982 → 5279 | 957 → 5245 | 5182 | 66 → 424 | 31 → 32 | 784 → 788 | **Leak** |
| capture-save | 818 → 5219 | 792 → 5171 | 5177 | 66 → 424 | 34 → 36 | 787 → 795 | **Leak** |
| editor | 1069 → 5351 | 1016 → 5262 | 5266 | 90 → 440 | 48 → 47 | 848 → 841 | **Leak** |
| ocr | 376 → 422 | 291 → 333 | 333 | 34 → 34 | 30 → 31 | 794 → 789 | Bounded |
| background | 434 → 521 | 426 → 440 | 440 | 16 → 16 | 31 → 32 | 769 → 756 | Bounded |
| utilities (9 windows opened and closed per round) | 534 → 557 | 523 → 538 | 538 | 41 → 43 | 54 → 56 | 979 → 988 | Bounded |

In this harness every capture left about 22 MiB of native memory and 2 GDI objects that a collection did not free; the managed heap stays near 155-171 MiB. The clipboard path is not the cause: the product's clipboard encoding and the test-only copy path leak the same amount.

Intended retention for comparison: capture history keeps at most 8 entries or 64 MB (`Extras/CaptureHistory.cs`), plus the last capture. One 1920 x 1080 capture is 8.3 MB decoded, so history and last capture together should hold at most about 64 MB + 8.3 MB ≈ 70 MiB above the idle app. The measured growth after collection over 180 rounds is about 4,300 MiB, 60 times that ceiling. The screenshot library was off in these runs.

### After the fix, 2026-11-12

Cause: a notification closed before its first frame received `ContentRendered` after `Closed` when animations were off, and `Motion.AutoDismiss` restarted its dismissal timer then. The timer never stopped and kept the closed notice and its screenshot thumbnail alive (heap snapshots at rounds 50 and 150: closed notices 56 → 155, full-size bitmaps 57 → 156, history 8 → 8). The handler now ignores `ContentRendered` once the window has closed. `--memory-soak --scenario notice-release` checks that closed notices are collectable with animations on and off, closed before and after their first frame.

The soak dismisses each notice at once, so it reached this path every round, and the baseline growth applies only to notices closed before their first frame. Before the fix, a separate run of the same check found notices released when animations were on (0 of 4 alive) or when the notice had rendered before it closed (0 of 12 alive). How often ordinary use closes a notice before its first frame was not measured.

Same PC, monitor and build settings as the baseline, 200 rounds:

| Scenario | Private before collection, MiB (round 20 → 199) | After collection, MiB (round 20 → 199) | 60 s idle, MiB | GDI (round 20 → 199) | USER | Handles | Trend |
|---|---|---|---|---|---|---|---|
| capture | 744 → 896 | 696 → 705 | 702 | 50 → 52 | 30 → 30 | 783 → 786 | Bounded |
| capture-save | 558 → 648 | 515 → 521 | 521 | 50 → 48 | 33 → 34 | 785 → 794 | Bounded |
| editor | 750 → 747 | 684 → 715 | 715 | 58 → 60 | 48 → 47 | 853 → 844 | Bounded |

GDI objects rise during the first 20 rounds (fonts, brushes and the first windows) and then stay level.

**Not tested:** other monitors and scales, multi-monitor captures larger than the history budget, the selection overlay, Automation and Sample (no smoke-controller path), an hour idle in the tray.

## What to include in a pull request

The command you ran, the numbers before and after on the same machine, the Windows version and display setup, and **Not tested** for anything you could not measure.

## Maintenance measurements and regression commands

See [the 2026-10-05 maintenance audit](maintenance-2026-10-05.md) for findings, accepted changes, same-machine timings, accuracy gates and remaining limits. No reading/magnification/language budgets were reduced.

After building the solution, run the focused harness with `dotnet run --project tests/DesktopTools.IntegrationTests -c Release --no-build -- <mode>`:

- `--maintenance-performance-only`: synthetic privacy pipeline stage timings and image rotation. Optional `DESKTOPTOOLS_PRIVACY_BASELINE` points to a same-machine generated geometry log from the original assembly; the complete geometry/categories must match.
- `--maintenance-transport-only`: exact old-PNG/production bitmap bytes, alpha/formats/scales/DPI, native ownership and bounded tiny-transfer allocations.
- `--maintenance-pixels-only`: bounded brightness sampling allocations and unchanged alpha/DPI/source results.
- `--maintenance-lifecycle-only`: idle scheduling, post-dispose callbacks and asynchronous recorder shutdown preparation.
- `--maintenance-models-ui-only` and `--maintenance-translation-only`: unchanged pixels/history, timer disposal and translation/cancellation/session isolation.
- `--maintenance-idle-only`: 30 s smoke startup/idle sample (excludes normal native registration).
- `--maintenance-recording-only`: 30 s silent owned-window recording with CPU/private-memory sampling; needs an interactive Windows session. This does not establish hours-long stability.

Core tests contain stable allocation/coverage guards; performance timings are observations, not fragile CI speed thresholds. `scripts/test.ps1 -NoBuild` is supported only after a matching configuration/output solution build.
