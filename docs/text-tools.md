# Text tools: local translation and screen OCR

- **Ctrl+Alt+R** opens selected text for review and translation. Choose English→Russian or Russian→English, then Translate. Source edits and direction changes cancel obsolete work. Unsupported applications get an explicit paste fallback. Clipboard contents are untouched until you click Copy.
- **Ctrl+Alt+E** lets you select an area of the current screen. Windows OCR fills the editable source field; copy it or translate it. No screenshot file, screenshot-history entry or automatic clipboard write is made. This uses temporary captured pixels internally; it does not require taking/saving a screenshot first.
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

Screen text, text tools and the screenshot library read pictures with Windows OCR plus a local preparation step. The picture is read as it is and again as a polarity-neutral "ink" map (every pixel compared with its neighbourhood, so light-on-dark, dark-on-light and coloured text all become dark text on white), at a magnification chosen from the size of the text, in overlapping tiles so large screenshots work. For each place on the page the most word-like reading wins. When another alphabet is installed (for example Russian next to English) it is read too and replaces a line only where it is clearly in that alphabet and clearly more word-like. The sensitive-data check also reads dark-theme screenshots a second time as an ink map.

`--ocr-quality-only` renders text the way screenshots show it and compares the old single-pass reading with this reader as character accuracy. On the development PC (English and Russian OCR installed) the average over 16 cases went from 79% to 95%; tiny 9-10 px dark and light interface text from 45-58% to 87-94%, Russian from 11% to 96%. Plain 11-30 px text was already at 99-100% and stays there. Programming-language punctuation stays near 75% in both. These are synthetic images; real screenshots are noisier and a 9 px font on a busy background is still not read perfectly. Reading takes longer: about 9 seconds for a 2560 × 1392 screenshot with two languages, a second or two for a typical selected region.
