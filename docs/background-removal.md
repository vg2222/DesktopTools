# Local background removal

Open **Image tools → Open image → Background → Remove background**. Review the checkerboard preview, enable **Compare** and drag the divider to inspect both sides, then use **Export** to save a copy. Removal selects PNG to retain transparency. Undo restores the previous image; Cancel and closing the window discard unfinished results. JPEG flattens transparency onto white. Existing image size and history limits apply.

Images stay on the computer. **Background model** chooses *Built-in* (default) or *High quality (optional download)*. Built-in uses the bundled 4.7 MB U2NetP model through Microsoft ONNX Runtime 1.24.4 (DirectML build) on the CPU, with no network request, GPU requirement, or worker while idle; each operation disposes its native session; concurrent requests are serialized. Cancellation interrupts inference, but model initialization must finish before cancellation is observed.

High quality uses BiRefNet-lite (224 MB, MIT), downloaded from its author only after you accept its licence in the consent window. It handles hair, fur and similar-coloured backgrounds better, takes about 8 seconds for a 1920 × 1080 picture on the development PC and needs about 6 GB of free memory: with less, it is refused up front with a message suggesting Built-in. Its session stays loaded for 60 seconds after the last removal, then is released; Quit and Remove on Settings → AI models release it at once. A missing or damaged pack is reported and never replaced silently by the built-in model. Measurements and the comparison with full U²-Net: [Optional AI models](ai-models.md#background-quality-pack).

This is automatic salient-object segmentation, not professional matting. The built-in model sees a 320 × 320 image (the high-quality model 1,024 × 1,024): hair, glass, shadows, low contrast and multiple subjects can produce imperfect edges or remove wanted content. Inspect the result before saving.

**Touch up** fixes the mask after a removal: **Restore** paints the original pixels back, **Erase** removes more, with a round brush of 4–200 px (**Brush size**). Each stroke is one undo step and Export uses the touched-up result; any other edit or opening another image ends Touch up. The original file is never changed.

The 400 × 300 synthetic foreground fixture took 661 ms including model initialization on the development machine (12 logical processors); this is a single observation, not a performance guarantee or photographic quality assessment.

## Model and licenses

- Architecture and Apache-2.0 license: [U²-Net](https://github.com/xuebinqin/U-2-Net).
- Bundled weights: [rembg u2netp release asset](https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx).
- SHA256: `309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8`; verified before every inference.
- Upstream published MD5: `8e83ca70e441ab06c318d82300c84806`.
- ONNX Runtime is MIT licensed; its license and third-party notices, plus the U²-Net license, accompany the model in `Assets/Models`.

Preprocessing follows the upstream RGB/channel normalization and 320-pixel input contract, using WPF resizing. Existing transparency is composited onto white for inference. The normalized foreground mask is bilinearly expanded and multiplied into the original alpha; original RGB, dimensions and DPI are retained. WPF interpolation differs from rembg's Pillow interpolation, so output is not promised to match rembg pixel for pixel.

## Verification

`dotnet run --project tests/DesktopTools.IntegrationTests -c Release -- --background-only`

Checks existing-alpha multiplication, RGB/DPI preservation, non-square preprocessing, invalid masks, bundled-model inference, PNG alpha, UI Undo, cancellation, close cleanup, five localized layouts and gate reuse. Artifacts are under `artifacts/integration/background-*`. Wider photographic quality and high-DPI manual usability remain release checklist items.

Photographic spot check: NASA's Eileen Collins portrait from the [scikit-image sample data](https://scikit-image.org/docs/0.20.x/api/skimage.data.html#skimage.data.astronaut), described there as public domain. The person and helmet were retained; part of the flag on the left and a thin halo remained. This confirms the need for preview and does not establish general photo quality. The sample and purple-background inspection composite are local test artifacts only, not bundled product assets. To repeat the optional check, place the sample at `artifacts/integration/background-photo-source.png` before running the test.
