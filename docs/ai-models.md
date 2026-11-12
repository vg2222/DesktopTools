# Optional AI models

DesktopTools 1.4.0 (working name) plans optional model packs that the user downloads from each model's publisher, at a pinned revision, after accepting that model's licence. DesktopTools does not host or bundle these models. This page records which models were checked, what was verified on the publisher's own page, and how each one behaved in feasibility checks run on 2026-11-12. Design: [the local AI update spec](superpowers/specs/2026-11-11-local-ai-update-design.md); checks: [the feasibility plan](superpowers/plans/2026-11-11-ai-feasibility-checks.md).

> This model is not part of DesktopTools. It was not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by its publisher.

## How model packs work

- **Settings → AI models** lists every optional model the app knows, with its purpose, licence, state (not downloaded, installed, damaged) and size. Downloaded translation packs from Text tools are listed on the same page and can be removed there. **Run on** shows where models run; this version uses the processor only.
- **Download** opens a window that shows the model's publisher, original authors, source page, pinned revision, licence name and full licence text, training data, known limitations, download size and the warning above. Download stays disabled until you tick *I have read and accept this licence*. Nothing is requested from the network before you accept.
- Files come straight from the publisher's own host (Hugging Face, GitHub, or the publisher's storage) at a pinned revision. Each file is checked against the SHA-256 checksum and size in the app's built-in catalog; a file that does not match is discarded and nothing is installed. A cancelled or failed download leaves nothing behind.
- Models run only on this PC. Images, recordings and text you process are not sent anywhere. DesktopTools sends no telemetry.
- A pack whose files later fail their checksum is shown as **Damaged** with **Download again**.

## Where packs are stored

- Model packs: `%LOCALAPPDATA%\DesktopTools\Models\<pack id>\`. Each pack folder also holds `LICENSE.txt`, `ATTRIBUTION.txt` and `SOURCE.txt` (source page and revision).
- Translation packs keep their own folder: `%LOCALAPPDATA%\DesktopTools\Translation\<direction>\`.
- Updates and repairs leave both folders alone. **Export settings** does not include models or licence acceptances, and **Import settings** does not change them.

## Record of your acceptance

`%LOCALAPPDATA%\DesktopTools\Models\consents.json` records, for each pack you accepted: pack id, revision, licence id, the SHA-256 of the licence text you were shown, the time and the app version. It stays on this PC. If a later app version pins a new revision or the licence text changes, the app asks again before the next download; an installed pack keeps working.

## Removing models

- **Remove** on the AI models page deletes that pack's folder and its acceptance record, after you confirm.
- **Settings → Behavior → Reset all app data** deletes all packs, translation packs and acceptance records with the rest of the app data.
- Uninstalling with **Delete all DesktopTools data** removes them too; **Keep notes and settings** keeps them.

## Feasibility results

Measured on one PC: Intel Core i5-11600K (6 cores, 12 threads), 16 GB RAM, NVIDIA GeForce RTX 3060 Ti 8 GB (driver 32.0.16.1692), Windows 11 Pro build 26300.9711, .NET SDK 10.0.402, ONNX Runtime 1.31.1 CPU (graphics card runs: ONNX Runtime DirectML 1.24.4). Single observations, not performance guarantees.

| Feature | Chosen model | Licence status | Download | Processor (CPU) | Graphics card (DirectML) | Decision |
|---|---|---|---|---|---|---|
| Object eraser | LaMa, `Carve/LaMa-ONNX` `lama_fp32.onnx` | Admitted (Apache-2.0) | 208.0 MB | 1.8–2.2 s per erase; session load 7–13 s; about 1.4 GiB private memory | Fails at run (MatMul node error, catchable) | Use LaMa on CPU. MI-GAN not needed as speed fallback |
| Image upscaling | Real-ESRGAN `realesr-general-x4v3`, Qualcomm ONNX export | Admitted (BSD-3-Clause) | 4.5 MB zip | 1920×1080 → 4×: 36.6 s (tile margin 24), 52.9 s (margin 32) | 2.5 s / 3.3 s, output matches CPU | Use 128 px tiles, margin 24 or 32 |
| Transcripts, fast | Whisper base, `Xenova/whisper-base` | Admitted (MIT upstream, Apache-2.0 card) | 295 MB | 2.9–3.6 s per audio minute; peak 1.5 GiB (pruned encoder) | Encoder correct and 10× faster; decoder loop gives wrong text | CPU; strip encoder attention outputs |
| Transcripts, accurate | Whisper small, `Xenova/whisper-small` | Admitted (MIT upstream, Apache-2.0 card) | 972 MB | 8.0–10.5 s per audio minute; peak 2.7 GiB (pruned encoder) | As base | CPU |
| OCR pack | PP-OCRv5 mobile detector + Latin / East Slavic / Chinese-Japanese recognizers (PaddlePaddle's own ONNX) | Admitted (Apache-2.0) | 4.8 MB + 8.0 / 7.9 / 16.5 MB | 20 benchmark images: 99.2–100 % exact phrases vs 95.1 % current reader; full screen 2560×1440 about 5 s | Detector 17× faster; recognizer slower (new shape per line); same accuracy | Recommended engine; batch recognition |
| Background removal, quality | Full U²-Net (rembg `u2net.onnx`) or BiRefNet-lite | Both Admitted (Apache-2.0 / MIT); ISNet Unclear | 176 MB / 224 MB | Load 5.9 s (BiRefNet); quality not compared | **Not tested** | Choose by measurement in the feature plan |
| Face suggestions | YuNet `face_detection_yunet_2026may.onnx` | Admitted (MIT) | 0.2 MB | Contract read; detection quality **Not tested** | **Not tested** | YuNet first, Ultra-Light-Fast (MIT) second |
| Processor / graphics card choice | — | DirectML runtime licence is proprietary; Unclear against SignPath's OSI rule | +18.5 MB (DirectML.dll) | — | See [Graphics card](#task-6-graphics-card) | **Processor only for this update** |

## Candidates and verification

Licences were read on the publisher's own page at the revision listed. Model files are kept for later developer tests in `E:\DesktopTools-ai-spikes\models\<pack>\` (index: `E:\DesktopTools-ai-spikes\models\manifest-all.tsv`); test inputs are in `E:\DesktopTools-ai-spikes\inputs`.

| Pack | Candidate | Status | Publisher / revision | File | Bytes | SHA-256 |
|---|---|---|---|---|---|---|
| Eraser | LaMa | **Admitted** | [Carve/LaMa-ONNX](https://huggingface.co/Carve/LaMa-ONNX/tree/c3c0c9e468934d62e79c329e35d82dd09ff8c444) `c3c0c9e468934d62e79c329e35d82dd09ff8c444`, card apache-2.0; original [advimman/lama](https://github.com/advimman/lama) Apache-2.0; conversion code Carve-Photos/lama Apache-2.0 | `lama_fp32.onnx` (publisher's recommended file) | 208,044,816 | `1faef5301d78db7dda502fe59966957ec4b79dd64e16f03ed96913c7a4eb68d6` |
| Eraser | LaMa (alternative file) | Not used | same revision | `lama.onnx` (publisher does not recommend) | 207,479,252 | `351e481e287f345b7fbfd026068cfb9ec0c7f24b440e6501458ebe54a833d1a1` |
| Eraser, fallback | MI-GAN | **Admitted** | [andraniksargsyan/migan](https://huggingface.co/andraniksargsyan/migan/tree/406830d0fa60666da0071c342ad2fbc8f30c5c64) `406830d0fa60666da0071c342ad2fbc8f30c5c64` (first author; linked from the official [Picsart-AI-Research/MI-GAN](https://github.com/Picsart-AI-Research/MI-GAN) README); code MIT, `LICENSE-WEIGHTS` MIT | `migan_pipeline_v2.onnx` | 28,079,181 | `6f1f3530a1a2324b19752018ce756088b07973cda8d7d890034ace5c8a48c40b` |
| Upscaling | Real-ESRGAN general x4v3 | **Admitted** | Original [xinntao/Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN) BSD-3-Clause, release v0.2.5.0 `realesr-general-x4v3.pth` (4,885,111 B, `8dc7edb9…96292`). ONNX: [qualcomm/Real-ESRGAN-General-x4v3](https://huggingface.co/qualcomm/Real-ESRGAN-General-x4v3/tree/deb4baad8e4992332b996e066e06766110a9559a) `deb4baad8e4992332b996e066e06766110a9559a`, card bsd-3-clause; asset on Qualcomm's S3 at the versioned path `qai-hub-models/models/real_esrgan_general_x4v3/releases/v0.66.0/` | `real_esrgan_general_x4v3-onnx-float.zip` (contains `.onnx` 161,087 B `09e6b675…858e7` + `.data` 4,836,096 B `512d0ec9…f37f`) | 4,540,895 | `fc094329b24a00c936ebc1cc4c51bc59633e7dc36f4a553344342171cbd3c7cf` |
| Speech | Whisper base | **Admitted** | Original [openai/whisper](https://github.com/openai/whisper) README: code and weights MIT. ONNX: [Xenova/whisper-base](https://huggingface.co/Xenova/whisper-base/tree/64da57285918e20ea79ea5c88eed7197933abaa8) `64da57285918e20ea79ea5c88eed7197933abaa8`, card apache-2.0 | `onnx/encoder_model.onnx`; `onnx/decoder_model_merged.onnx`; `tokenizer.json` (+ `vocab.json` 1,036,584, `merges.txt` 493,869, `added_tokens.json`, `generation_config.json`, `preprocessor_config.json`) | 82,474,863; 208,560,983; 2,480,466 | `f0bd7927234639c6e1f293cef18a210cee4e4aea93e200ebbe48e1d7acf6fdb1`; `3fed66b2b8dfa1ae213f768527dff1ef7e3857b8d45a36381850ce4dff8db936`; `27fc476bfe7f17299480be2273fc0608e4d5a99aba2ab5dec5374b4482d1a566` |
| Speech | Whisper small | **Admitted** | [Xenova/whisper-small](https://huggingface.co/Xenova/whisper-small/tree/2d67713f236afa48a18992566e7647f6ca848e13) `2d67713f236afa48a18992566e7647f6ca848e13`, card apache-2.0 | `onnx/encoder_model.onnx`; `onnx/decoder_model_merged.onnx` (tokenizer files as base) | 352,839,389; 615,405,212 | `31a05a14d514440e43746fdaaa8d4e8102c9543e53c5ae1111910af142041406`; `16c197b4c4702c51c767d1931a5673a10fa528bd1e79963891c97d69e0258584` |
| Speech | onnx-community/whisper-small `36050c46` | **Unclear** (card states no licence) → not used | — | — | — | — |
| Speech | onnx-community/whisper-base `27a07b4b` | Admitted, not used (Xenova chosen for both sizes) | card apache-2.0 | — | — | — |
| OCR | PP-OCRv5 mobile detector | **Admitted** | [PaddlePaddle/PP-OCRv5_mobile_det_onnx](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det_onnx/tree/e6f4fa85f00e168c862bc462aebca69eef9b3d3d) `e6f4fa85f00e168c862bc462aebca69eef9b3d3d`, card apache-2.0; [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) Apache-2.0 | `inference.onnx` (+ `inference.yml` 903 B) | 4,826,518 | `a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d` |
| OCR | Latin recognizer | **Admitted** | PaddlePaddle/latin_PP-OCRv5_mobile_rec_onnx `89d3a50e2c27e2e7cceeab0e944c25c807d5db4f` | `inference.onnx` (+ `inference.yml` 6,817 B `0bbe9845…c5f84`) | 8,042,023 | `7888113072263cb471b93f66dd5e2ad70548dc526fa1ace760d0d973dd121498` |
| OCR | East Slavic recognizer | **Admitted** | PaddlePaddle/eslav_PP-OCRv5_mobile_rec_onnx `9a32171fc5718746875e1a261818884517975013` | `inference.onnx` (+ `inference.yml` 4,538 B `025039ba…ee5a`) | 7,887,627 | `b3018ef2b09a0250b6e0c8e871c927098363e5fd4df890cc68e8358eb0aaf1bd` |
| OCR | Chinese / Japanese recognizer | **Admitted** | PaddlePaddle/PP-OCRv5_mobile_rec_onnx `ed152b8b495f84de93cda5709d768548a9127622` | `inference.onnx` (+ `inference.yml` 148,345 B `5dfeb277…5d8e`) | 16,534,782 | `da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092` |
| Background | Full U²-Net | **Admitted** | rembg release v0.0.0 ([danielgatis/rembg](https://github.com/danielgatis/rembg) MIT; [xuebinqin/U-2-Net](https://github.com/xuebinqin/U-2-Net) Apache-2.0), same lineage as the bundled U2NetP | `u2net.onnx` | 175,997,641 | `8d10d2f3bb75ae3b6d527c77944fc5e7dcd94b29809d47a739a7a728a912b491` |
| Background | ISNet general-use | **Unclear** → not downloaded | [xuebinqin/DIS](https://github.com/xuebinqin/DIS) states Apache-2.0 for code and metric only; weights on Google Drive with no licence statement; DIS5K has its own terms | (rembg `isnet-general-use.onnx` 178,648,008 B) | — | — |
| Background | BiRefNet-lite | **Admitted** | Author's [ZhengPeng7/BiRefNet](https://github.com/ZhengPeng7/BiRefNet) MIT, GitHub release v1; identical file in onnx-community/BiRefNet_lite `de15b22b` (card mit) | `BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx` | 224,005,088 | `5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333` |
| Faces | YuNet 2026may | **Admitted** | [opencv/opencv_zoo](https://github.com/opencv/opencv_zoo) commit `48b10fa505aeb51fbd127fc60a5c660040e823b7`; model folder README: all files MIT (Copyright (c) 2020 Shiqi Yu) | `models/face_detection_yunet/face_detection_yunet_2026may.onnx` (dynamic size) | 229,738 | `ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0` |
| Faces | YuNet 2023mar | Admitted, not preferred (fixed size) | opencv/face_detection_yunet `3cc26e7f` | `face_detection_yunet_2023mar.onnx` | 232,589 | `8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4` |
| Faces, second | Ultra-Light-Fast-Generic-Face-Detector-1MB | **Admitted** | [Linzaer/…-1MB](https://github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB) MIT, commit `dffdddda9794a50607cba8f318507a28c1c27cab` | `models/onnx/version-RFB-320.onnx` | 1,270,727 | `34cd7e60aeff28744c657de7a3dc64e872d506741de66987f3426f2b79f88017` |

Training data to disclose (spec 2.2 rule 5): LaMa and MI-GAN — Places2 (Places365, dataset terms non-commercial research); Real-ESRGAN — synthetic degradations of DIV2K, Flickr2K and OST; Whisper — 680,000 hours of web audio, not published; PP-OCRv5 — PaddleOCR internal and public data, not itemised; BiRefNet — DIS5K, HRSOD, UHRSD and others; YuNet and Ultra-Light — WIDER FACE.

Notes for the catalog: the upscaler downloads a zip from Qualcomm's S3 (versioned path, no published checksum; our SHA-256 pins it), so the installer must extract a zip. The OCR character lists are inside each `inference.yml` and must be parsed with ordinal string comparison (a culture-sensitive comparison drops 7,096 entries of the Chinese/Japanese list).

## Task 2: upscaler

- Contract: input `image` float32 [1,3,128,128], RGB, 0..1, no mean; output `upscaled_image` float32 [1,3,512,512], not clipped (range −0.39..1.34) → clamp.
- Reference: the original `.pth` in PyTorch 2.15 on a whole 256×256 generated card; ONNX tiles with margin 32 match it to < 5e-5.
- Tile margin vs whole-image reference: 0 → visible seams (max 0.86); 16 → 328 values > 1/255; 24 → max 0.0025, none > 1/255; 32 → exact. Receptive field 34 px.
- 1920×1080 → 7680×4320 on CPU: margin 24, 312 tiles, 36.6 s; margin 32, 464 tiles, 52.9 s. About 570 MiB private memory during inference (dominated by the 398 MB float output buffer), 983 MiB peak including PNG encoding.
- Decision: admitted; margin 24 (fast) or 32 (exact).

## Task 3: object eraser

- LaMa contract: `image` float32 [B,3,512,512] RGB 0..1, `mask` float32 [B,1,512,512] (1 = remove); output `output` float32 [B,3,512,512] RGB 0..255. Fixed 512×512, opset 17. Reproducing the publisher's demo gives mean difference 0.41 levels against its published output.
- Crop with context: square around the mask's bounding box plus 128 px context (minimum 256), resized to 512, result resized back, only masked pixels written. Pixels outside the mask changed: 0 in all 9 cases. Tested on a generated 1280×720 screenshot-like image and two public-domain/CC0 photos (scikit-image astronaut, coffee); images in `artifacts/spikes/out/erase-*.png`.
- LaMa CPU: 1.8–2.2 s per erase, session load 7–13 s, about 1.4 GiB private memory while loaded.
- MI-GAN: uint8 in/out, any size, 0.5–0.8 s, but visibly worse (smears on photos) and changes pixels outside the mask unless composited.
- Decision: LaMa.

## Task 4: OCR pack

- Detector: input `x` float32 [N,3,H,W] (multiples of 32), BGR, ImageNet mean/std; output probability map [N,1,H,W]; DB post-processing thresh 0.3, box 0.6, unclip 1.5.
- Recognizers: input `x` float32 [N,3,48,W], BGR, (v/255−0.5)/0.5, width ≥ 320 padded; output [N,W/8,K] with K = 838 Latin, 519 East Slavic, 18,385 Chinese/Japanese; CTC greedy, index 0 blank, last index space.
- 20 images from the existing `--ocr-bench` generator (10 English, 10 Russian): exact phrases 122/123 (99.2 %) at native size, 123/123 with the publisher's 960 px resize, vs 117/123 (95.1 %) for the current Windows reader; gains at 9 px and 13 px Russian text. Per image: detector 19–89 ms + recognizer 54–134 ms vs 105–671 ms today.
- Full-screen 2560×1440 generated screen (~150 lines): about 5 s and 1.2–1.35 GiB at native size; 960 px resize 2.8–3.5 s but 16 small lines lost. Recognition runs one line at a time; batching is the obvious speed-up.
- Word boxes from CTC time steps are good enough for selection (off by up to a fraction of a character).
- Decision: recommended engine. Run the full 120-image benchmark in the OCR plan. Chinese/Japanese accuracy **Not tested**.

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

## Legal findings

- **SignPath Foundation** ([terms](https://signpath.org/terms), read 2026-11-12): no statement found about components downloaded at run time. Related requirements: the privacy policy must say the program "will not transfer any information to other networked systems unless specifically requested by the user"; and "The project must use an OSI-approved Open Source license … for all components."
- **EU AI Act Article 50** ([Commission guidelines C(2026) 5054 final, 20.7.2026](https://ai-act-service-desk.ec.europa.eu/sites/default/files/2026-07/guidelines_on_the_implementation_of_the_transparency_obligations_for_certain_ai_systems_under_article_50_of_the_ai_act_bzptwqhk0ikg1dtlddap41psfy_131215.pdf), section 4.3, paragraphs 90–92, pp. 28–29): examples covered by the standard-editing exception include "sharpening for enhanced clarity", "deleting and obscuring backgrounds", pixel filling for clarity and transcriptions of conversations. Marking is required for "Removal, replacement or insertion of objects or persons" that "changes meaning and substance of the content"; a system usable both ways gets the exception only for content altered in a minor, non-substantial way. The guidelines are not binding.

## Spike code

Throwaway code in `artifacts/spikes/AiSpike` (git-ignored); outputs in `artifacts/spikes/out`. Derived Whisper encoders without attention outputs: `E:\DesktopTools-ai-spikes\models\derived\whisper-*-noattn`. Python reference environment: `E:\DesktopTools-ai-spikes\venv` (PyTorch 2.15 CPU, transformers, onnxruntime 1.23.2).
