# Text tools: local translation and screen OCR

- **Ctrl+Alt+R** opens selected text for review and translation. Choose English→Russian or Russian→English, then Translate. Source edits and direction changes cancel obsolete work. Unsupported applications get an explicit paste fallback. Clipboard contents are untouched until you click Copy.
- **Ctrl+Alt+E** lets you select an area of the current screen and opens it in the text view (see below): the picture is dimmed, every recognized word is outlined and can be selected and copied straight from the picture; translating hands the text to this window. No screenshot file, screenshot-history entry or automatic clipboard write is made. This uses temporary captured pixels internally; it does not require taking/saving a screenshot first.
- Both shortcuts are editable using the existing recorder on Shortcuts. Independent enable toggles live in Utilities; Home and the quick wheel expose launch actions. Direction and OCR language preferences save locally and are included in profiles.

OCR uses Windows-installed language packs, selected in the text-tools window. Install missing OCR languages in Windows Settings. Region selection follows the capture monitor setting, including the default all-monitor desktop. Escape cancels region selection and prior drawing mode is restored. Freeze-frame content is ignored for screen-text recognition: it reads the live desktop. DesktopTools control windows are temporarily hidden while reading pixels.

The processing indicator pulses only while work is active and visible, and respects disabled/reduced animation preferences. Cancel/Escape stops processing; close cancels and releases resources. The operation timeout is two minutes; native model initialization can take a moment to respond to cancellation.

## Translation limits

Translation runs on the CPU without network requests or accounts. Both directions are bundled (about 228 MB including model support files). Input is limited to 4,000 characters, 32 sentence chunks and 256 tokens per chunk; generated output is capped at 256 tokens per chunk. Over-limit output is rejected rather than presented as complete. Paragraph separators are retained. Sentence-based greedy generation is intended for short passages and can mistranslate ambiguous words, names or technical terms. Review output; it is not professional translation.

Sessions are initialized per sentence chunk and disposed after use, with no idle model session or polling. Requests are serialized and stale UI results are discarded. This bounds idle memory at the cost of initialization overhead for longer passages. The selected-text provider uses UI Automation TextPattern, avoids password fields and does not inject Ctrl+C. Slow providers time out after three seconds; at most one provider query can remain pending. Applications without accessible selections, elevated apps and browser-specific selection behavior may require paste or OCR.

## Build and licensing

Run `./scripts/fetch-translation-models.ps1` before building from a clean checkout. ONNX weights are excluded from Git history; the script fetches only pinned manifest URLs and verifies SHA256. Portable users need no separate download or .NET installation. Package creation checks every model asset and required notices. CI downloads the pinned models before building.

Attribution and licenses: `Assets/Translation/ATTRIBUTION.md`. EN→RU model: Apache-2.0; RU→EN: CC BY 4.0. Conversion/quantization by Xenova. ONNX Runtime and Microsoft.ML.Tokenizers are MIT. App source remains MIT.

## Verification

`--translation-only` integration checks actual EN↔RU sentences, preservation of opening sentences/paragraphs, input limits, canceled requests and reuse after cancellation. `--text-tools-only` checks the window, animation visibility, stale results, OCR fixture, UI Automation extraction from a known helper, direct screen-region OCR, cancellation/disable cleanup, history preservation and clipboard sequence stability. Native desktop/OCR checks need an interactive Windows session and an installed English OCR pack. Tests do not synthesize keyboard or mouse input.

Foreground hotkey selection in every editor/browser, mixed-DPI screen-text accuracy, large OCR areas and subjective translation quality still need broader manual compatibility testing. Windows refused unattended foreground activation of the helper, so selection extraction was verified against its known UI Automation element without forcing user focus.

## Text recognition quality

Screen text, text tools, the text view and the screenshot library read pictures with Windows OCR plus local preparation. The first reading is made at 2x and tells how large the text is. Further readings follow at the magnification that suits it (3x for ordinary UI text, 4x for very small text, 2x for large): the picture as it is, as plain luma grey (which removes the coloured fringes of ClearType) and as a polarity-neutral "ink" map (every pixel compared with its neighbourhood, which rescues light-on-dark text), in overlapping tiles so large screenshots work. The number of readings is limited by a pixel budget, so a full-screen capture costs about three seconds.

The readings are combined word by word (`OcrConsensus`): lines of different readings that sit on top of each other form a row, and words that sit on top of each other form a place. For every place the candidate of each reading is scored with the Windows spelling dictionary for the word's alphabet (the language you selected, and English or Russian for the other alphabet when Windows has a dictionary) plus the agreement of the other readings; unknown words, mixed alphabets and stray accents count against a candidate. A word the dictionary does not know that is a few cheap OCR confusions away from a real word (rn for m, an accent mark for a letter, l for i) or that Windows itself suggests with such a difference is replaced by it ("Claün offer" becomes "Claim offer"). A second alphabet is read only when the first result has many unknown words, and its words compete place by place. Languages without a Windows dictionary keep the readings' agreement and the alphabet checks.

`--ocr-bench` draws phrases the way programs draw text (GDI ClearType, coloured sub-pixel edges) in 15 fonts, 10 sizes and 10 colour schemes, with English and Russian phrases, and reports exact phrase matches by size, scheme and font plus the worst misreadings (`OCR_BENCH_IMAGES`, `OCR_BENCH_SEED`, `OCR_BENCH_MODES`; modes `single`, `current` and `v:<plain|ink|gray>:<scale>:<hq|legacy>` for single readings, and an oracle row for the best of all). On the development PC (English and Russian OCR installed), seed 21, 1,582 phrases: plain Windows OCR 69.9% exact, this reader 92.9% (98.5% of characters). 12 pixel text and larger is 95-100% exact; 9-10 pixel text is 69-72%, with thin serif and Courier fonts the weakest. `--ocr-quality-only` keeps the earlier synthetic cases (dark themes, gradients, Russian, code): average 79% to 96%. `--ocr-consensus-only` checks the word choice and repair with a fixed word list. These are synthetic images; real screenshots are noisier.

The sensitive-data check (Hide private data) keeps its own readings with the original magnification it was tuned on; it does not use the word choice.

## Text view (Scan screen text, Extract text)

The text view shows the picture with everything dimmed except the recognized words, which stay at full brightness inside a thin outline, so it is clear what can be copied. While the picture is being read the same rainbow rim and sweeping light as **Check screenshot** run; with reduced motion they are replaced by a still rim.

- **Selecting:** drag over the words (reading order, like a PDF); double-click selects a word, triple-click a line, **Ctrl+A** everything, **Esc** or a click on empty space clears. **Ctrl+C**, **Copy selection** and **Copy all** copy plain text, words joined by spaces and lines by new lines.
- **Links:** web addresses, e-mail addresses and phone numbers get a second colour and an underline. **Ctrl+click** opens them; the right-click menu can copy the link.
- **Fix text:** **F2** or the context menu on a word opens a small box over it. A fix only changes what is copied or translated; the picture is never touched.
- **Translate** sends the selection (or all text) to the local translator in Text tools when translation is enabled.
- **Zoom:** **Ctrl+wheel** zooms, middle-button drag or **Space+drag** pans, **Ctrl+0** fits.
- **Where it lives:** the large "Scan screen text" window; **Extract text** in the screenshot editor (More menu) and in Image tools. In both editors a **Back to editing** button returns to the tools with nothing lost.

Words come from the same best-line selection as the plain text reading (`LocalOcr.RecognizeWordsAsync`), so the boxes and the copied text agree. `--text-selection-only` checks the layout, the dimming (pixels inside and outside the word boxes), selection, fixes, a blank picture, cancelling mid-scan, the standalone window and both editor integrations; the unit tests cover the selection model and the link detector.
