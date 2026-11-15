# Optional AI models

DesktopTools offers optional model packs downloaded from each model's publisher, at a pinned revision, after you accept the model's licence. It does not host or bundle these optional weights. The compact U2NetP background model and English–Russian translation are still bundled. This page separates current behavior from the historical feasibility checks; only packs in the embedded catalogs are available in the app. Full publishers, revisions, licence copies, training-data disclosures and file pins: [optional-model inventory](licenses/optional-models.md).

> This model is not part of DesktopTools. It was not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by its publisher.

## How model packs work

- **Settings → AI models** lists every optional model the app knows, with its purpose, licence, state (not downloaded, installed, damaged) and size. Downloaded translation packs from Text tools are listed on the same page and can be removed there. **Run on** shows where models run; this version uses the processor only.
- **Download** opens a window that shows the model's publisher, original authors, source page, pinned revision, licence name and full licence text, training data, known limitations, download size and the warning above. Download stays disabled until you tick *I have read and accept this licence*. Nothing is requested from the network before you accept.
- Files come straight from the publisher's own host (Hugging Face, GitHub, or the publisher's storage) at a pinned revision. Each file is checked against the SHA-256 checksum and size in the app's built-in catalog; a file that does not match is discarded and nothing is installed. A cancelled or failed download installs no partial pack; a hard process interruption can leave staging files, which the next install cleans up.
- Models run only on this PC. Images, recordings and text you process are not sent anywhere. DesktopTools sends no telemetry.
- A pack whose files later fail their checksum is shown as **Damaged** with **Download again**. Initial list checks use file presence and size; full checksums are checked at install and when loading models. The page does not continuously hash every file.
- Accepting a licence contacts no server by itself. The requested download contacts the named publisher and its allowed HTTPS download hosts; they can see your IP address. Consent records are local.

## Where packs are stored

- Model packs: `%LOCALAPPDATA%\DesktopTools\Models\<pack id>\`. Each pack folder also holds `LICENSE.txt`, `ATTRIBUTION.txt` and `SOURCE.txt` (source page and revision).
- Translation packs keep their own folder: `%LOCALAPPDATA%\DesktopTools\Translation\<direction>\`.
- Portable builds use the same per-user local app-data folders, not a Models folder beside the executable. Updates and repairs leave both folders alone. **Export settings** does not include models or licence acceptances, and **Import settings** does not change them.

## Record of your acceptance

`%LOCALAPPDATA%\DesktopTools\Models\consents.json` records, for each pack you accepted: pack id, revision, licence id, the SHA-256 of the licence text you were shown, the time and the app version. It stays on this PC. If a later app version pins a new revision or the licence text changes, the app asks again before the next download; an installed pack keeps working.

## Removing models

- **Remove** on the AI models page deletes that pack's folder and its acceptance record, after you confirm.
- **Settings → Behavior → Reset all app data** deletes all packs, translation packs and acceptance records with the rest of the app data.
- Uninstalling with **Delete all DesktopTools data** removes them too; **Keep notes and settings** keeps them.

## OCR pack

These optional models give Text tools, the text view (Scan screen text, Extract text) a second text reader beside Windows OCR.

| Pack id | Model (PaddlePaddle's own ONNX on Hugging Face) | Size | Needed for |
|---|---|---|---|
| `ocr-text-detector` | PP-OCRv5 mobile detector | 4.8 MB | every alphabet |
| `ocr-latin` | PP-OCRv5 Latin recognizer | 8.0 MB | English and other Latin-alphabet languages |
| `ocr-cyrillic` | PP-OCRv5 East Slavic recognizer | 7.9 MB | Russian, Ukrainian, Belarusian |
| `ocr-chinese-japanese` | PP-OCRv5 Chinese/Japanese recognizer | 16.7 MB including character list | Chinese and Japanese, with Latin letters and digits |

- **Choosing it:** Text tools → *Text from screenshot* → **Text reader**: *Windows OCR* or *DesktopTools OCR pack*. By default the pack is used as soon as the alphabet for your Windows display language is downloaded, for Scan screen text, Extract text and Check screenshot; choosing *Windows OCR* keeps Windows OCR. With the pack, the language list shows the three alphabets; the one matching the Windows display language is preselected (Russian, Ukrainian and Belarusian → Cyrillic; Chinese and Japanese → Chinese and Japanese; everything else → Latin). Other Cyrillic languages (Bulgarian, Serbian, Macedonian) use letters the East Slavic recognizer does not have, so they are not mapped to it.
- **Downloading:** **Download** beside the choice asks for consent to each missing pack (the text finder and the chosen alphabet), with the same licence window as Settings → AI models, and only then downloads them. Declining any of them downloads nothing.
- **Fallback:** while a needed pack is not downloaded or is damaged, text is read with Windows OCR and the result says so. A damaged pack is marked **Damaged** on the AI models page. If neither the pack nor any Windows OCR language is available, nothing is read and the message says why.
- **How it reads:** the picture is cut into overlapping 1,024-pixel strips for the detector, lines are found (DB post-processing, threshold 0.3, box score 0.6, unclip 1.5), each line is scaled to 48 pixels high and read in batches of eight lines of similar width, then words are put back into reading order. Pictures with a side over 8,192 pixels are refused with a request to crop them to that size; pictures over 48 million pixels, or with more than 10,000 lines, with *Too much text in this picture. Crop it and try again.*
- **Memory:** the model sessions stay loaded for 60 seconds after the last reading, so repeated scans do not reload them, and are then released. Quit releases them at once; **Remove** releases them, or, if a reading is running, as soon as it finishes.
- Settings saved: `TextReader` (`Auto`, `Windows` or `Pack`; previews of 1.3.0 stored `OcrEngine`, which is ignored) and `OcrPackScript` (`latin`, `cyrillic`, `chinese-japanese`, or empty for the default). An unknown engine value means Auto; an unknown alphabet means the one that matches the Windows display language. Profiles include both.

### Accuracy

`--ocr-bench` (120 generated images, 788 English and Russian phrases, seed 7; mode `pack` reads with the pack directly) on the PC below:

| Reader | Exact phrases | Time for 120 images |
|---|---|---|
| Plain Windows OCR (one reading) | 66.8 % | — |
| Windows OCR with DesktopTools preparation (current default) | 92.6 % | 33 s |
| DesktopTools OCR pack | **99.2 %** | 48 s (includes model load) |

| Text height | 9 px | 10 px | 11 px | 12 px | 13 px | 14 px | 16 px | 18 px | 22 px | 28 px |
|---|---|---|---|---|---|---|---|---|---|---|
| Windows OCR (current) | 76 % | 81 % | 90 % | 99 % | 98 % | 88 % | 94 % | 98 % | 97 % | 97 % |
| OCR pack | 98 % | 99 % | 100 % | 100 % | 99 % | 100 % | 99 % | 98 % | 99 % | 100 % |

The pack's remaining mistakes are mostly a stray character between words ("Claim n your y reward", "Скачать , сейчас") and single-letter confusions ("joumey", "Cohumn", "Continué"). Chinese and Japanese: only a smoke test (one short line of each read exactly); accuracy **Not tested**. Real screenshots are noisier than these generated images.

Measured on the PC listed under [Feasibility results](#feasibility-results), processor only.

## Background quality pack

Image tools → Background → **Background model** offers *Built-in* (the bundled 4.7 MB U2NetP, unchanged) and *High quality (optional download)*: BiRefNet-lite, pack `background-quality`, 224 MB, MIT, from the author's GitHub release `v1` (`BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx`). A release tag is not a commit: if the author ever replaced the file, its SHA-256 would no longer match and the download would be refused (nothing installed), so the catalog would need a new entry. Choosing High quality and **Remove background** asks for the licence in the consent window before the first download; declining keeps the image and the choice and downloads nothing. A missing or damaged pack never falls back silently to the built-in model: the status says so and offers the download. The model sees a 1,024 × 1,024 picture; the mask is scaled back to the original size and multiplied into its alpha, and the original file is never changed.

- **Memory:** the model needs about 5.7 GB of private memory while it runs, so it runs with ONNX Runtime's memory arena off (with the arena on it failed with "bad allocation" on a 16 GB PC) and is refused up front, with a message suggesting Built-in, when less than 6 GiB of commit memory is available. The session stays loaded for 60 seconds after the last removal, then is released; Quit and **Remove** request release; an active call must finish before deferred release.
- **Touch up:** after a removal, **Restore** and **Erase** paint on the mask with a round brush (4–200 px); each stroke is one undo step, Export uses the touched-up mask, and any other edit or opening another image ends Touch up.

Why BiRefNet-lite and not full U²-Net (rembg `u2net.onnx`, 176 MB, Apache-2.0): measured on the PC listed under [Feasibility results](#feasibility-results), ONNX Runtime CPU, 4 threads, on composites of a transparent subject and generated hair over busy, similar-colour and plain backgrounds (same preprocessing as the app):

| Model | Mean alpha error | Error on partly transparent edges | 1920 × 1080 time | Peak memory |
|---|---|---|---|---|
| U2NetP (built-in) | 0.070 | 0.41 | 0.19 s | 310 MB |
| Full U²-Net | 0.062 (−11 %) | about 0.40 | 0.38 s | 461 MB |
| BiRefNet-lite | **0.057 (−19 %)** | **0.22 (−46 %)** | 7.9 s (plus 2.7 s load) | 5.3 GB working set |

Full U²-Net gives no gain on hair; BiRefNet-lite halves the edge error (hair over busy or similar colours: −52 % and −57 % mean error) but is worse on a glass object and on hair over a plain background. By eye on public-domain photos (scikit-image cat, astronaut, coffee) it gives the cleanest fur and no flag leak, but drops the helmet on the table. In the app on the same PC: first removal 14.6 s including the model load, a second removal 8.6 s. ISNet was dropped by the owner (licence unclear). Single observations, not performance guarantees; PCs with less than 16 GB of memory are **Not tested**.

## Face finder

**Check screenshot** (Hide private data) in the screenshot editor can also suggest faces with YuNet, pack `faces`, 229 KB, MIT, from OpenCV's `opencv_zoo` at commit `48b10fa505aeb51fbd127fc60a5c660040e823b7`. A row under the findings offers the download with its size; the licence is accepted in the consent window first. Faces are only suggested, like every other finding: nothing is hidden until you choose it. A damaged pack is reported in the row and the text checks still run.

- **Threshold 0.6.** Measured on the PC listed under [Feasibility results](#feasibility-results): the highest score on five generated faceless screenshots (avatars, text, a drawn smiley) was 0.074; the public-domain astronaut photo scored 0.94 with the face 60 px high and 0.96 at 200 px. 0.6 leaves a wide margin on both sides; faces scoring between about 0.1 and 0.6 (strongly turned or blurred) are not suggested.
- **Speed:** 60 px face found in 156 ms including the model load, 200 px face in 38 ms; screenshots larger than the model input are scaled down and boxes mapped back (checked at 3840 × 2160).
- Real photos of many people, small faces in crowds, and faces of different skin tones and ages: **Not tested** beyond the single photo above.

## Transcripts and translation

The [video editor](video-editor.md#transcript) offers Fast (`speech-fast`, 292,076,206 bytes) and Accurate (`speech-accurate`, 969,285,022 bytes), both multilingual Whisper exports. The current packs contain the encoder, merged decoder, vocabulary, merges and generation configuration; the older feasibility totals below included additional support files. Captions are separate SRT/VTT/TXT files, not burned into video. Working transcripts and corrections stay under `%LOCALAPPDATA%\DesktopTools\transcripts`; users must review recognition errors.

[Text tools](text-tools.md#translation-limits) uses beam search (at most four beams), accepts 20,000 characters and shows progress. New optional translation downloads use the same licence consent; existing packs keep working. OPUS-MT's training disclosure is OPUS parallel corpora collected from public translated texts with their own terms. Every direction's publisher, licence and revision is in [the inventory](licenses/optional-models.md#translation-packs).

## Feasibility results

**Historical developer checks, not a feature list.** The object eraser and ISNet were dropped. Upscaling and GPU support are planned for a later update; none is exposed in this release. Older sample sizes, download totals and prototype pipelines below are retained as research evidence. The current pack list and exact pins are in [the inventory](licenses/optional-models.md).

Measured on one PC: Intel Core i5-11600K (6 cores, 12 threads), 16 GB RAM, NVIDIA GeForce RTX 3060 Ti 8 GB (driver 32.0.16.1692), Windows 11 Pro build 26300.9711, .NET SDK 10.0.402, ONNX Runtime 1.31.1 CPU (graphics card runs: ONNX Runtime DirectML 1.24.4). Single observations, not performance guarantees.

| Feature | Chosen model | Licence status | Download | Processor (CPU) | Graphics card (DirectML) | Decision |
|---|---|---|---|---|---|---|
| Object eraser | LaMa, `Carve/LaMa-ONNX` `lama_fp32.onnx` | Admitted (Apache-2.0) | 208.0 MB | 1.8–2.2 s per erase; session load 7–13 s; about 1.4 GiB private memory | Fails at run (MatMul node error, catchable) | **Dropped from this update by the owner (2026-11-12); no eraser pack** |
| Image upscaling | No candidate qualifies cleanly. Nearest: RealPLKSR ×4, `darktable-org/upscale-realplksr-onnx` | Unclear (weights licence stated only by the converter); Qualcomm Real-ESRGAN rejected by owner (zip on S3, no published checksum) | 29.7 MB | 1920×1080 → 4×: 244 s, peak 1.0 GiB (Swin2SR, the only clean licence: about 24 min) | 12.3 s (Swin2SR 50.7 s), output matches CPU | **Dropped from this update** (owner, 2026-11-13). Swin2SR is the candidate for a later update with GPU support. See [Upscaler](#task-2-upscaler) |
| Transcripts, fast | Whisper base, `Xenova/whisper-base` | Admitted (MIT upstream, Apache-2.0 card) | 295 MB | 2.9–3.6 s per audio minute; peak 1.5 GiB (pruned encoder) | Encoder correct and 10× faster; decoder loop gives wrong text | CPU; strip encoder attention outputs |
| Transcripts, accurate | Whisper small, `Xenova/whisper-small` | Admitted (MIT upstream, Apache-2.0 card) | 972 MB | 8.0–10.5 s per audio minute; peak 2.7 GiB (pruned encoder) | As base | CPU |
| OCR pack | PP-OCRv5 mobile detector + Latin / East Slavic / Chinese-Japanese recognizers (PaddlePaddle's own ONNX) | Admitted (Apache-2.0) | 4.8 MB + 8.0 / 7.9 / 16.5 MB | 20 benchmark images: 99.2–100 % exact phrases vs 95.1 % current reader; full screen 2560×1440 about 5 s | Detector 17× faster; recognizer slower (new shape per line); same accuracy | Implemented as the optional second engine with batch recognition; Windows OCR remains the default |
| Background removal, quality | BiRefNet-lite (author's GitHub release v1); full U²-Net measured and not chosen | Admitted (MIT; U²-Net Apache-2.0); ISNet dropped by owner | 224 MB | 7.9 s at 1920 × 1080, about 5.7 GB private memory; edge error −46 % against the built-in model | **Not tested** | In catalog (`background-quality`); see [Background quality pack](#background-quality-pack) |
| Face suggestions | YuNet `face_detection_yunet_2026may.onnx` | Admitted (MIT) | 0.2 MB | 38–156 ms per screenshot; threshold 0.6 (faceless screenshots ≤ 0.074, faces 0.94–0.96) | **Not tested** | In catalog (`faces`); Ultra-Light-Fast not needed. See [Face finder](#face-finder) |
| Processor / graphics card choice | — | DirectML runtime licence is proprietary; Unclear against SignPath's OSI rule | +18.5 MB (DirectML.dll) | — | See [Graphics card](#task-6-graphics-card) | **Processor only for this update** |

## Historical candidates and verification

Licences were read on the publisher's own page at the revision listed. Model files are kept for later developer tests in `E:\DesktopTools-ai-spikes\models\<pack>\` (index: `E:\DesktopTools-ai-spikes\models\manifest-all.tsv`); test inputs are in `E:\DesktopTools-ai-spikes\inputs`.

| Pack | Candidate | Status | Publisher / revision | File | Bytes | SHA-256 |
|---|---|---|---|---|---|---|
| Eraser | LaMa | Dropped; historical licence check only | [Carve/LaMa-ONNX](https://huggingface.co/Carve/LaMa-ONNX/tree/c3c0c9e468934d62e79c329e35d82dd09ff8c444) `c3c0c9e468934d62e79c329e35d82dd09ff8c444`, card apache-2.0; original [advimman/lama](https://github.com/advimman/lama) Apache-2.0; conversion code Carve-Photos/lama Apache-2.0 | `lama_fp32.onnx` (publisher's recommended file) | 208,044,816 | `1faef5301d78db7dda502fe59966957ec4b79dd64e16f03ed96913c7a4eb68d6` |
| Eraser | LaMa (alternative file) | Not used | same revision | `lama.onnx` (publisher does not recommend) | 207,479,252 | `351e481e287f345b7fbfd026068cfb9ec0c7f24b440e6501458ebe54a833d1a1` |
| Eraser, fallback | MI-GAN | Dropped; historical licence check only | [andraniksargsyan/migan](https://huggingface.co/andraniksargsyan/migan/tree/406830d0fa60666da0071c342ad2fbc8f30c5c64) `406830d0fa60666da0071c342ad2fbc8f30c5c64` (first author; linked from the official [Picsart-AI-Research/MI-GAN](https://github.com/Picsart-AI-Research/MI-GAN) README); code MIT, `LICENSE-WEIGHTS` MIT | `migan_pipeline_v2.onnx` | 28,079,181 | `6f1f3530a1a2324b19752018ce756088b07973cda8d7d890034ace5c8a48c40b` |
| Upscaling | RealPLKSR ×4 (MSSIM pretrain, no GAN) | **Unclear** | [darktable-org/upscale-realplksr-onnx](https://huggingface.co/darktable-org/upscale-realplksr-onnx/tree/0bb636eeeed5fffd256571c3cc440d9fdf62467e) `0bb636eeeed5fffd256571c3cc440d9fdf62467e`, card mit; card says weights MIT "from upstream" [dslisleedh/PLKSR](https://github.com/dslisleedh/PLKSR) (MIT); the weights were actually released by neosr-project in [PLKSR issue #4](https://github.com/dslisleedh/PLKSR/issues/4) (Google Drive), whose MIT statement reads as covering the code; conversion script darktable-ai GPL-3.0+ (not shipped, not run by us); training data not documented | `onnx/model_x4.onnx` | 29,711,010 | `8ce692652494e2e5c31e1f7c7ab61eb084e9a450cde727a374f5d6746cfe054b` (shown by Hugging Face for the LFS file; download matches) |
| Upscaling | Swin2SR real-world ×4 (BSRGAN, PSNR) | **Admitted**, too slow on CPU | [onnx-community/swin2SR-realworld-sr-x4-64-bsrgan-psnr-ONNX](https://huggingface.co/onnx-community/swin2SR-realworld-sr-x4-64-bsrgan-psnr-ONNX/tree/9b3baf051f6708d0b697580489e4415b64c7378e) `9b3baf051f6708d0b697580489e4415b64c7378e`, card apache-2.0; converted from [caidas/swin2SR-realworld-sr-x4-64-bsrgan-psnr](https://huggingface.co/caidas/swin2SR-realworld-sr-x4-64-bsrgan-psnr) `bb13f02e` (apache-2.0), from [mv-lab/swin2sr](https://github.com/mv-lab/swin2sr) Apache-2.0 release v0.0.1 `Swin2SR_RealworldSR_X4_64_BSRGAN_PSNR.pth` | `onnx/model.onnx` | 53,827,735 | `987d88b356554161cbb8f67b7a8f4162cad6dc147839c344e3d5142140f25d6f` (shown by Hugging Face for the LFS file; download matches) |
| Upscaling | Xenova Swin2SR ×4 exports (`realworld` `d0e99269`, `classical` `c60eac19`, `compressed` `7d3f2242`) | **Unclear** (cards state no licence) → not used | — | — | — | — |
| Upscaling | AMD Real-ESRGAN NPU tiles (`amd/realesrgan-*-tiles-amdnpu`) | **Rejected** (Research-only RAIL-MS) | — | — | — | — |
| Upscaling | Real-ESRGAN general x4v3, Qualcomm export | **Rejected by owner** (zip on S3, no published checksum) | Original [xinntao/Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN) BSD-3-Clause, release v0.2.5.0 `realesr-general-x4v3.pth` (4,885,111 B, `8dc7edb9…96292`). ONNX: [qualcomm/Real-ESRGAN-General-x4v3](https://huggingface.co/qualcomm/Real-ESRGAN-General-x4v3/tree/deb4baad8e4992332b996e066e06766110a9559a) `deb4baad8e4992332b996e066e06766110a9559a`, card bsd-3-clause; asset on Qualcomm's S3 at the versioned path `qai-hub-models/models/real_esrgan_general_x4v3/releases/v0.66.0/` | `real_esrgan_general_x4v3-onnx-float.zip` (contains `.onnx` 161,087 B `09e6b675…858e7` + `.data` 4,836,096 B `512d0ec9…f37f`) | 4,540,895 | `fc094329b24a00c936ebc1cc4c51bc59633e7dc36f4a553344342171cbd3c7cf` |
| Speech | Whisper base | **Admitted**, in catalog (`speech-fast`) | Original [openai/whisper](https://github.com/openai/whisper) README: code and weights MIT. ONNX: [Xenova/whisper-base](https://huggingface.co/Xenova/whisper-base/tree/64da57285918e20ea79ea5c88eed7197933abaa8) `64da57285918e20ea79ea5c88eed7197933abaa8`, card apache-2.0 | `onnx/encoder_model.onnx`; `onnx/decoder_model_merged.onnx`; `tokenizer.json` (+ `vocab.json` 1,036,584, `merges.txt` 493,869, `added_tokens.json`, `generation_config.json`, `preprocessor_config.json`) | 82,474,863; 208,560,983; 2,480,466 | `f0bd7927234639c6e1f293cef18a210cee4e4aea93e200ebbe48e1d7acf6fdb1`; `3fed66b2b8dfa1ae213f768527dff1ef7e3857b8d45a36381850ce4dff8db936`; `27fc476bfe7f17299480be2273fc0608e4d5a99aba2ab5dec5374b4482d1a566` |
| Speech | Whisper small | **Admitted**, in catalog (`speech-accurate`) | [Xenova/whisper-small](https://huggingface.co/Xenova/whisper-small/tree/2d67713f236afa48a18992566e7647f6ca848e13) `2d67713f236afa48a18992566e7647f6ca848e13`, card apache-2.0 | `onnx/encoder_model.onnx`; `onnx/decoder_model_merged.onnx` (tokenizer files as base) | 352,839,389; 615,405,212 | `31a05a14d514440e43746fdaaa8d4e8102c9543e53c5ae1111910af142041406`; `16c197b4c4702c51c767d1931a5673a10fa528bd1e79963891c97d69e0258584` |
| Speech | onnx-community/whisper-small `36050c46` | **Unclear** (card states no licence) → not used | — | — | — | — |
| Speech | onnx-community/whisper-base `27a07b4b` | Admitted, not used (Xenova chosen for both sizes) | card apache-2.0 | — | — | — |
| OCR | PP-OCRv5 mobile detector | **Admitted** | [PaddlePaddle/PP-OCRv5_mobile_det_onnx](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det_onnx/tree/e6f4fa85f00e168c862bc462aebca69eef9b3d3d) `e6f4fa85f00e168c862bc462aebca69eef9b3d3d`, card apache-2.0; [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) Apache-2.0 | `inference.onnx` (+ `inference.yml` 903 B) | 4,826,518 | `a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d` |
| OCR | Latin recognizer | **Admitted** | PaddlePaddle/latin_PP-OCRv5_mobile_rec_onnx `89d3a50e2c27e2e7cceeab0e944c25c807d5db4f` | `inference.onnx` (+ `inference.yml` 6,817 B `0bbe9845…c5f84`) | 8,042,023 | `7888113072263cb471b93f66dd5e2ad70548dc526fa1ace760d0d973dd121498` |
| OCR | East Slavic recognizer | **Admitted** | PaddlePaddle/eslav_PP-OCRv5_mobile_rec_onnx `9a32171fc5718746875e1a261818884517975013` | `inference.onnx` (+ `inference.yml` 4,538 B `025039ba…ee5a`) | 7,887,627 | `b3018ef2b09a0250b6e0c8e871c927098363e5fd4df890cc68e8358eb0aaf1bd` |
| OCR | Chinese / Japanese recognizer | **Admitted** | PaddlePaddle/PP-OCRv5_mobile_rec_onnx `ed152b8b495f84de93cda5709d768548a9127622` | `inference.onnx` (+ `inference.yml` 148,345 B `5dfeb277…5d8e`) | 16,534,782 | `da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092` |
| Background | Full U²-Net | Not chosen; measured comparison only | rembg release v0.0.0 ([danielgatis/rembg](https://github.com/danielgatis/rembg) MIT; [xuebinqin/U-2-Net](https://github.com/xuebinqin/U-2-Net) Apache-2.0), same lineage as the bundled U2NetP | `u2net.onnx` | 175,997,641 | `8d10d2f3bb75ae3b6d527c77944fc5e7dcd94b29809d47a739a7a728a912b491` |
| Background | ISNet general-use | **Unclear** → not downloaded | [xuebinqin/DIS](https://github.com/xuebinqin/DIS) states Apache-2.0 for code and metric only; weights on Google Drive with no licence statement; DIS5K has its own terms | (rembg `isnet-general-use.onnx` 178,648,008 B) | — | — |
| Background | BiRefNet-lite | **Admitted** | Author's [ZhengPeng7/BiRefNet](https://github.com/ZhengPeng7/BiRefNet) MIT, GitHub release v1; identical file in onnx-community/BiRefNet_lite `de15b22b` (card mit) | `BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx` | 224,005,088 | `5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333` |
| Faces | YuNet 2026may | **Admitted** | [opencv/opencv_zoo](https://github.com/opencv/opencv_zoo) commit `48b10fa505aeb51fbd127fc60a5c660040e823b7`; model folder README: all files MIT (Copyright (c) 2020 Shiqi Yu) | `models/face_detection_yunet/face_detection_yunet_2026may.onnx` (dynamic size) | 229,738 | `ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0` |
| Faces | YuNet 2023mar | Admitted, not preferred (fixed size) | opencv/face_detection_yunet `3cc26e7f` | `face_detection_yunet_2023mar.onnx` | 232,589 | `8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4` |
| Faces, second | Ultra-Light-Fast-Generic-Face-Detector-1MB | **Admitted** | [Linzaer/…-1MB](https://github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB) MIT, commit `dffdddda9794a50607cba8f318507a28c1c27cab` | `models/onnx/version-RFB-320.onnx` | 1,270,727 | `34cd7e60aeff28744c657de7a3dc64e872d506741de66987f3426f2b79f88017` |

Training data to disclose (spec 2.2 rule 5): LaMa and MI-GAN — Places2 (Places365, dataset terms non-commercial research); Real-ESRGAN — synthetic degradations of DIV2K, Flickr2K and OST; Swin2SR — DIV2K and Flickr2K with BSRGAN degradations; RealPLKSR — not documented (converter assumes DIV2K + Flickr2K); Whisper — 680,000 hours of web audio, not published; PP-OCRv5 — PaddleOCR internal and public data, not itemised; BiRefNet — DIS5K, HRSOD, UHRSD and others; YuNet and Ultra-Light — WIDER FACE.

Notes for the catalog: the Qualcomm upscaler was rejected because it is a zip on Qualcomm's S3 with no checksum published by Qualcomm; the remaining upscaler candidates are single ONNX files whose SHA-256 Hugging Face shows on the publisher's page. The OCR character lists are inside each `inference.yml` and must be parsed with ordinal string comparison (a culture-sensitive comparison drops 7,096 entries of the Chinese/Japanese list).

## Task 2: upscaler

- Contract: input `image` float32 [1,3,128,128], RGB, 0..1, no mean; output `upscaled_image` float32 [1,3,512,512], not clipped (range −0.39..1.34) → clamp.
- Reference: the original `.pth` in PyTorch 2.15 on a whole 256×256 generated card; ONNX tiles with margin 32 match it to < 5e-5.
- Tile margin vs whole-image reference: 0 → visible seams (max 0.86); 16 → 328 values > 1/255; 24 → max 0.0025, none > 1/255; 32 → exact. Receptive field 34 px.
- 1920×1080 → 7680×4320 on CPU: margin 24, 312 tiles, 36.6 s; margin 32, 464 tiles, 52.9 s. About 570 MiB private memory during inference (dominated by the 398 MB float output buffer), 983 MiB peak including PNG encoding.
- Decision (first round): admitted; margin 24 (fast) or 32 (exact). **Later rejected by the owner** because the file is a zip on Qualcomm's S3 with no checksum published by Qualcomm.

### Second round (2026-11-12): other sources

Requirement: a ready ONNX file on the publisher's own page at a pinned revision, with a checksum shown by that page, and a licence passing spec 2.2 for weights, original code and conversion. Searched Hugging Face for ONNX models matching esrgan, realesr, swin2sr, upscal, super-resolution and sr-x4, and checked GitHub releases of the original projects. Same PC as above; CPU runs ONNX Runtime 1.31.1, GPU runs DirectML 1.24.4 on the RTX 3060 Ti.

- **Rejected or Unclear without measuring:** AMD's Real-ESRGAN NPU tiles (Research-only RAIL-MS licence); Xenova's Swin2SR ×4 exports (no licence on the cards); Qualcomm's Hugging Face repos (no ONNX file, only a licence pointer); Real-ESRGAN ONNX uploads by individual accounts (not established publishers); `onnxmodelzoo/super-resolution-10` (×3, luminance only, fixed 224 px, 2016-era quality); OpenCV `dnn_superres` models (TensorFlow `.pb`, not ONNX). The original Real-ESRGAN and Swin2SR projects publish only PyTorch weights, without digests.
- **Swin2SR real-world ×4** (onnx-community, Apache-2.0 chain). Contract: `pixel_values` float32 [B,3,H,W] dynamic, RGB 0..1, H and W multiples of 8 (the model subtracts its mean internally); output `reconstruction` [B,3,4H,4W]. Whole 512×384 image: 42 s and 4.4 GiB on CPU. Tiles are never exactly seamless (shifted-window attention): tile 128 against the whole image gives max 0.57 at margin 0, 0.032 at 16, 0.011 at 24 and 0.0055 at 32 (49 values > 1/255). Speed: a 480×270 image (1/16 of 1080p) took 87 s with 128 px tiles (3.1 s per tile, peak 1.2 GiB) and 72 s with 256 px tiles (peak 4.4 GiB). 1080p at tile 128 / margin 32 is 464 tiles, so about 24 minutes on CPU; a real 1080p run was stopped after 15 minutes. GPU: 1080p in 106 s (tile 128) or 50.7 s (tile 256, 3.2 GiB), output equal to CPU.
- **RealPLKSR ×4** (darktable). Contract: `input` float32 [1,3,256,256], RGB 0..1; output `output` [1,3,1024,1024]. Group normalisation makes the output depend on each tile's content, so overlapping tiles never agree exactly: against margin-96 tiling, max difference 0.14 at margins 8–64 (269k–909k of 9.4M values > 1/255). No seams were visible in a 1080p crop, but blending across the overlap is advisable. 1080p on CPU: 244 s at margin 32 (60 tiles), 454 s at margin 64, peak 1.0 GiB. GPU: 12.3 s / 21.5 s, output equal to CPU (max 0.0002).
- **Qualcomm Real-ESRGAN, re-measured in the same session for comparison:** CPU 54.7 s at margin 32 (peak 549 MiB); GPU 3.4 s.
- **Quality** (public-domain astronaut, CC0 coffee and a generated card, each downscaled 4× then upscaled; PSNR on luminance against the original): bicubic 26.3 / 26.6 / 22.7 dB; Qualcomm Real-ESRGAN 23.9 / 26.2 / 20.7; RealPLKSR 25.2 / 25.3 / 21.7; Swin2SR 26.9 / 27.1 / 21.6. PSNR favours smooth results. By eye (`artifacts/spikes/out/up2-quality-sheet.png`): Real-ESRGAN is sharpest but painterly; RealPLKSR is sharp with more natural faces and edges; Swin2SR is the most natural on photos but soft, and turns a fine checkerboard into diagonal stripes.
- Files: `E:\DesktopTools-ai-spikes\models\upscale-swin2sr\model.onnx` and `E:\DesktopTools-ai-spikes\models\upscale-realplksr\model_x4.onnx`.
- **Result: no candidate meets every requirement.** Swin2SR passes licence and checksum but needs about 24 minutes per 1080p image on the processor, and this update ships Processor only. RealPLKSR has a published checksum and is usable for small images on CPU (244 s at 1080p, so about 1 minute at 960×540), but its weights licence rests on the converter's statement. **Owner decision (2026-11-13): upscaling is dropped from this update.** Swin2SR is the candidate for a later update with GPU support; RealPLKSR could be admitted if its releaser confirms the weights licence.

## Task 3: object eraser

- LaMa contract: `image` float32 [B,3,512,512] RGB 0..1, `mask` float32 [B,1,512,512] (1 = remove); output `output` float32 [B,3,512,512] RGB 0..255. Fixed 512×512, opset 17. Reproducing the publisher's demo gives mean difference 0.41 levels against its published output.
- Crop with context: square around the mask's bounding box plus 128 px context (minimum 256), resized to 512, result resized back, only masked pixels written. Pixels outside the mask changed: 0 in all 9 cases. Tested on a generated 1280×720 screenshot-like image and two public-domain/CC0 photos (scikit-image astronaut, coffee); images in `artifacts/spikes/out/erase-*.png`.
- LaMa CPU: 1.8–2.2 s per erase, session load 7–13 s, about 1.4 GiB private memory while loaded.
- MI-GAN: uint8 in/out, any size, 0.5–0.8 s, but visibly worse (smears on photos) and changes pixels outside the mask unless composited.
- Historical prototype choice: LaMa. **Superseded by the owner on 2026-11-12: object eraser removed from this update.**

## Task 4: OCR pack

- Detector: input `x` float32 [N,3,H,W] (multiples of 32), BGR, ImageNet mean/std; output probability map [N,1,H,W]; DB post-processing thresh 0.3, box 0.6, unclip 1.5.
- Recognizers: input `x` float32 [N,3,48,W], BGR, (v/255−0.5)/0.5, width ≥ 320 padded; output [N,W/8,K] with K = 838 Latin, 519 East Slavic, 18,385 Chinese/Japanese; CTC greedy, index 0 blank, last index space.
- 20 images from the existing `--ocr-bench` generator (10 English, 10 Russian): exact phrases 122/123 (99.2 %) at native size, 123/123 with the publisher's 960 px resize, vs 117/123 (95.1 %) for the current Windows reader; gains at 9 px and 13 px Russian text. Per image: detector 19–89 ms + recognizer 54–134 ms vs 105–671 ms today.
- Full-screen 2560×1440 generated screen (~150 lines): about 5 s and 1.2–1.35 GiB at native size; 960 px resize 2.8–3.5 s but 16 small lines lost. Recognition runs one line at a time; batching is the obvious speed-up.
- Word boxes from CTC time steps are good enough for selection (off by up to a fraction of a character).
- Historical recommendation: offer this as the second engine. The implemented 120-image benchmark and batched pipeline are described under [OCR pack](#ocr-pack); Windows OCR remains the default. Broader Chinese/Japanese accuracy is **Not tested**.

## Task 5: transcripts

- Audio: Media Foundation source reader to 16 kHz mono float. The DesktopTools recorder's MP4 (H.264 + AAC 48 kHz stereo) decoded to 21.525 s, equal to the container length; an 18-minute MP3 within 42 ms of ffmpeg.
- Features: log-mel (80 bins, n_fft 400, hop 160, periodic Hann, reflect padding, Slaney mel, log10, max−8, (x+4)/4) matches the transformers WhisperFeatureExtractor to 2.9e-5.
- Encoder: `input_features` float32 [B,80,3000] → `last_hidden_state` [B,1500,512 base / 768 small]. The Xenova encoder also outputs every layer's attention (base 6 × [B,8,1500,1500]); ONNX Runtime computes them even when not requested. A copy with only `last_hidden_state` kept gives identical text and lowers peak memory from 1.93 to 1.47 GiB (base) and 4.30 to 2.71 GiB (small).
- Decoder (merged): `input_ids` int64, `encoder_hidden_states`, `past_key_values.{layer}.{decoder|encoder}.{key|value}`, `use_cache_branch` bool[1] → `logits` [B,S,51865] + `present.*`. First step: prompt `<|startoftranscript|> <|lang|> <|transcribe|>` (50258, 50259 en / 50263 ru, 50359) with empty cache; later steps one token with cache.
- Tokenizer: Microsoft.ML.Tokenizers 2.0.1 `CodeGenTokenizer` over `vocab.json` + `merges.txt` gives token ids identical to the reference for English, Russian (ё, щ), dashes, guillemets and emoji; special and timestamp ids (≥ 50257) are filtered before decoding.
- Decoding used: greedy, suppressed tokens from `generation_config.json`, OpenAI timestamp rules, 30 s windows, seek to the last complete timestamp pair.
- Accuracy (word error rate on 60 s of public-domain LibriVox recordings, against public-domain text): Gettysburg Address base 8.5 %, small 7.1 % (part of it is the reader's wording); Turgenev "Вешние воды" base 18.8 %, small 11.1 %.
- Recorded clip: generated speech (Windows TTS) played while the DesktopTools recorder captured system audio on the MSI monitor. Capture began after playback had started, so the first sentence is missing; the other four sentences are word-perfect in both models. Segment starts are within 0.03 s of the speech onsets after each pause and the last end within 0.02 s — within the 0.5 s target. Microphone capture **Not tested**.
- Invented text: 30 s of near-silence gave "you" (base) and "." (small); 30 s of generated keyboard clicks gave a repeated sentence 14 times (base) and an invented sentence (small). An RMS gate (< −50 dBFS) catches silence but not clicks. The no-speech probability above 0.6 caught all four cases (speech windows had 0.001–0.11). OpenAI's combined rule (no-speech > 0.6 and average log-probability < −1) misses the confident base repetition. A compression-ratio threshold of 2.4 would misfire on Russian (2.25–2.72 on normal text).
- Speed on CPU: base 2.9–3.6 s and small 8.0–10.5 s per audio minute; model load 0.85 s / 2.0–2.3 s.
- Decision: both sizes feasible on CPU. Use the no-speech probability plus an RMS gate; strip the encoder's attention outputs. Languages other than English and Russian **Not tested**.

## Task 6: graphics card

- Package: the only Windows GPU backend for the .NET ONNX Runtime API on NuGet is `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4 (MIT; there is none for 1.25–1.31; Microsoft describes DirectML as sustained engineering, with new work in Windows ML). One `onnxruntime.dll` serves the process, so switching moves the whole app, including CPU paths, from 1.31.1 to 1.24.4. It needs `DirectML.dll` from `Microsoft.AI.DirectML` 1.15.4 (18.5 MB, Microsoft Software License Terms: redistribution "solely for use on Windows and Xbox", and the software "may collect information about you and your use of the software, and send that to Microsoft"). Windows build 26300 ships `System32\DirectML.dll` 1.15.6, and ONNX Runtime used it correctly when the app-local copy was absent; older Windows builds **Not tested**. Windows ML (`Microsoft.Windows.AI.MachineLearning` 2.5.96, ONNX Runtime 1.28.3) **Not tested**. `Microsoft.ML.OnnxRuntime.Gpu.Windows` 1.31.1 is CUDA (NVIDIA only, needs CUDA and cuDNN).
- Adapters in DirectML device order: #0 RTX 3060 Ti (8,024 MiB), #1 the same RTX 3060 Ti again (second entry from a virtual display driver), #2 Microsoft Basic Render Driver (software). The setting must de-duplicate and hide software adapters.
- Invalid adapter index: `OnnxRuntimeException` at session creation, catchable; a CPU session works afterwards.
- Per model (warm run, CPU → GPU, largest difference from CPU): upscaler tile 95 → 4 ms (8e-6), full 1080p 36.6 → 2.5 s; LaMa fails at run; MI-GAN 500 → 40 ms (1 level); OCR detector 860 → 51 ms (1e-4), recognizer 7 → 4 ms per line but whole screens slower (6.9–7.6 s vs about 5 s) because each line width is a new shape; Whisper encoder base 272 → 27 ms, small 979 → 84 ms (2e-3); Whisper decoder single steps match, but the real decoding loop produced wrong text with no error; bundled U2NetP 135 → 7 ms (9e-5); bundled translation model on GPU crashed the process (access violation, not catchable).
- Existing models with the DirectML package on CPU: U2NetP outputs bit-identical to 1.31.1; translation encoder values differ slightly (int8 kernels), but greedy translations of 8 sentences were token-identical.
- Decision: **ship Processor only in this update.** A try/catch fallback is not enough when one failure is silent wrong output and another an uncatchable crash; the switch would downgrade ONNX Runtime for the whole app; and the DirectML licence must be checked against SignPath's requirement that all components use an OSI-approved licence. Revisit with Windows ML or a newer GPU backend, an allow-list of verified models and out-of-process inference; the upscaler gain (14×) is the strongest reason.
- AMD and Intel graphics, PCs with no dedicated card, other Windows builds: **Not tested**.
- Update (owner request, 1.3.0): **Settings → AI models → Run on** offers *Graphics card* when Windows has `DirectML.dll` 1.15 or later. The app moved to `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4 for every model, does not ship DirectML, and preloads the Windows copy. Only models verified above go to the graphics card, by SHA-256: the OCR text finder and both speech encoders. Translation, the speech decoder, the OCR recognizers, faces and the quality background model stay on the processor. A session that cannot be created or run on the graphics card is recreated on the processor, and the processor is used until restart. Measured after the change on the same PC: OCR pack, full 2560×1440 synthetic screenshot, 3.8 s processor vs 2.5 s graphics card, identical text. AMD/Intel graphics, laptops with switchable graphics and older Windows builds: **Not tested**.

## Owner review before release

The design leaves the sufficiency of training-data disclosure undecided. The catalogs show the known datasets and explicitly state where they are not itemised or published; this does not settle their terms. The owner should review that disclosure and the final consent wording before release. See section 2 of the repository design (`docs/superpowers/specs/2026-11-11-local-ai-update-design.md`). Historical feasibility decisions do not add models to the shipped catalog.

## Spike code

Throwaway code in `artifacts/spikes/AiSpike` (git-ignored); outputs in `artifacts/spikes/out`. Derived Whisper encoders without attention outputs: `E:\DesktopTools-ai-spikes\models\derived\whisper-*-noattn`. Python reference environment: `E:\DesktopTools-ai-spikes\venv` (PyTorch 2.15 CPU, transformers, onnxruntime 1.23.2).
