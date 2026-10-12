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
