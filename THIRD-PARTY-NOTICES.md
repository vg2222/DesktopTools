# Third-party notices

DesktopTools original source and documentation use the repository MIT license. Bundled third-party icons and other assets are credited below.

The application targets Microsoft .NET 10, WPF and Windows Forms. These open-source projects use the MIT license; their distributions also include notices for incorporated components. A self-contained publication redistributes framework components. Retain the `LICENSE*` and `THIRD-PARTY-NOTICES*` files supplied by the .NET SDK/runtime in the published output; this repository notice does not replace them.

Source license references: [.NET runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [WPF](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT), [Windows Forms](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT).

Windows system APIs, installed system fonts and operating-system materials remain governed by their respective platform terms. GitHub Actions used for CI are development infrastructure and are not bundled with the application.

The screen recorder requires Microsoft Visual C++ x64 Redistributable. DesktopTools setup and the recorder link to Microsoft's [official download page](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist); the redistributable is not bundled or silently installed by DesktopTools. Users obtain it directly from Microsoft and accept the terms in Microsoft's installer. It is licensed separately from DesktopTools.

## Inter 4.1

The unmodified Inter font files are bundled from the [official Inter 4.1 release](https://github.com/rsms/inter/releases/tag/v4.1), Copyright (c) 2016 The Inter Project Authors, under SIL Open Font License 1.1. The complete license is included in `Assets/Fonts/Inter-LICENSE.txt` in the portable application and `src/DesktopTools/Assets/Fonts/Inter-LICENSE.txt` in source. Fonts are embedded for offline use; no system installation is needed.

## QRCoder 1.8.1

QR images are generated locally using [QRCoder](https://www.nuget.org/packages/QRCoder/1.8.1), under the [MIT license](https://github.com/Shane32/QRCoder/blob/v1.8.1/LICENSE.txt).

The MIT License (MIT)

Copyright (c) 2013-2025 Raffael Herrmann
Copyright (c) 2024-2025 Shane Krueger

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Background removal and AI models: ONNX Runtime 1.24.4 (DirectML build) and U²-Net

[Microsoft ONNX Runtime](https://github.com/microsoft/onnxruntime) is distributed under MIT. DesktopTools ships the `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4 build; it does not ship DirectML itself and uses the copy that comes with Windows. Complete upstream license and third-party notices are bundled as `Assets/Models/ONNXRUNTIME-LICENSE.txt` and `Assets/Models/ONNXRUNTIME-NOTICES.txt`.

The compact U2NetP model implements [U²-Net](https://github.com/xuebinqin/U-2-Net), under Apache-2.0; see the bundled `Assets/Models/U2NET-LICENSE.txt`. The unmodified weights come from the [rembg release asset](https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx). Model provenance, checksums and processing differences are documented in [background-removal.md](docs/background-removal.md). No rembg Python code or runtime is bundled.

## Local translation models and tokenization

Text tools bundle OPUS-MT models developed by the Language Technology Research Group at the University of Helsinki, converted and quantized to ONNX by Xenova. English→Russian is Apache-2.0; Russian→English is CC BY 4.0. Full attribution, source links, modification descriptions and licenses are included in `Assets/Translation/ATTRIBUTION.md`, `EN-RU-APACHE-2.0.txt` and `CC-BY-4.0.txt`. Exact revisions and checksums are recorded in the bundled manifest. Optional language packs come from the same authors; their repositories, pinned revisions, licenses, sizes and SHA-256 checksums are recorded in `Assets/Translation/catalog.json`. Each downloaded pack includes its upstream README, attribution and license text. These assets retain their own licenses; DesktopTools source is MIT.

Microsoft.ML.Tokenizers 2.0.1 is MIT licensed. Its complete license and third-party notices are included under `Assets/Translation/TOKENIZERS-LICENSE.txt` and `TOKENIZERS-NOTICES.txt`. ONNX Runtime notices are documented above.

## Screen recording

ScreenRecorderLib 7.0.1 — Copyright (c) 2017 Sverre Skodje, MIT. Source: https://github.com/sskodje/ScreenRecorderLib . Full license: docs/licenses/ScreenRecorderLib-LICENSE.txt. The x64 native wrapper is distributed with DesktopTools. Windows Media Foundation and Microsoft Visual C++ x64 runtime are operating-system/runtime prerequisites; see docs/screen-recorder.md.
## Animated WebP export

SkiaSharp 4.155.0 — Copyright (c) 2015-2016 Xamarin, Inc.; (c) 2017-2018 Microsoft Corporation, MIT. Source: https://github.com/mono/SkiaSharp . Full license: docs/licenses/SkiaSharp-LICENSE.txt. Its native library bundles Skia (BSD-3-Clause) and libwebp (BSD-3-Clause); it is used only to encode the frames of animated WebP files exported from the video editor. The GIF encoder is original code.

## Optional models

Optional AI models are **not distributed with DesktopTools** and are not covered by its MIT license. When a user chooses Download on Settings → AI models, the app shows the model's licence, and only after the user accepts it downloads the files directly from the model's publisher at a pinned revision, checked against SHA-256 checksums in the app's built-in catalog (`Assets/Models/catalog.json`). Each model stays under its own licence; DesktopTools keeps a copy of that licence, an attribution and the source address in the model's folder. The licence texts the catalog can refer to are bundled in `Assets/Models/Licenses/`. These models are not part of DesktopTools and were not created, trained or owned by the DesktopTools project. See [docs/ai-models.md](docs/ai-models.md).

| Model | Publisher and source | Revision | Licence | Used by |
|---|---|---|---|---|
| PP-OCRv5 mobile text detector (`ocr-text-detector`) | PaddlePaddle, [PP-OCRv5_mobile_det_onnx](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det_onnx); model from [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) | `e6f4fa85f00e168c862bc462aebca69eef9b3d3d` | Apache-2.0 | Text tools (DesktopTools OCR pack) |
| PP-OCRv5 mobile Latin recognizer (`ocr-latin`) | PaddlePaddle, [latin_PP-OCRv5_mobile_rec_onnx](https://huggingface.co/PaddlePaddle/latin_PP-OCRv5_mobile_rec_onnx) | `89d3a50e2c27e2e7cceeab0e944c25c807d5db4f` | Apache-2.0 | Text tools (DesktopTools OCR pack) |
| PP-OCRv5 mobile East Slavic recognizer (`ocr-cyrillic`) | PaddlePaddle, [eslav_PP-OCRv5_mobile_rec_onnx](https://huggingface.co/PaddlePaddle/eslav_PP-OCRv5_mobile_rec_onnx) | `9a32171fc5718746875e1a261818884517975013` | Apache-2.0 | Text tools (DesktopTools OCR pack) |
| PP-OCRv5 mobile Chinese/Japanese recognizer (`ocr-chinese-japanese`) | PaddlePaddle, [PP-OCRv5_mobile_rec_onnx](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_rec_onnx) | `ed152b8b495f84de93cda5709d768548a9127622` | Apache-2.0 | Text tools (DesktopTools OCR pack) |
| Whisper base, ONNX export (`speech-fast`) | Xenova (Joshua Lochner), [Xenova/whisper-base](https://huggingface.co/Xenova/whisper-base); model by OpenAI, [openai/whisper](https://github.com/openai/whisper) | `64da57285918e20ea79ea5c88eed7197933abaa8` | MIT (OpenAI code and weights); export card Apache-2.0 | Video editor (Transcript) |
| Whisper small, ONNX export (`speech-accurate`) | Xenova (Joshua Lochner), [Xenova/whisper-small](https://huggingface.co/Xenova/whisper-small); model by OpenAI, [openai/whisper](https://github.com/openai/whisper) | `2d67713f236afa48a18992566e7647f6ca848e13` | MIT (OpenAI code and weights); export card Apache-2.0 | Video editor (Transcript) |
| BiRefNet-lite, general (`background-quality`) | Peng Zheng, [ZhengPeng7/BiRefNet](https://github.com/ZhengPeng7/BiRefNet), GitHub release asset | `v1` (`BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx`) | MIT | Image tools (Background, High quality) |
| YuNet face detector (`faces`) | OpenCV, [opencv_zoo/face_detection_yunet](https://github.com/opencv/opencv_zoo/tree/48b10fa505aeb51fbd127fc60a5c660040e823b7/models/face_detection_yunet); model by Shiqi Yu and co-authors | `48b10fa505aeb51fbd127fc60a5c660040e823b7` | MIT | Screenshot editor (Check screenshot, face suggestions) |

The complete [optional-model inventory](docs/licenses/optional-models.md) lists every AI and translation pack, publisher, pinned revision, licence text and training-data disclosure. It also records every AI file's size and SHA-256; the embedded translation catalog supplies those pins for all translation files. No NuGet package was added or upgraded since 1.3.0. Full optional-model licence copies are available offline under `docs/licenses/` as well as in the app's `Assets/Models/Licenses/` directory.

Training data for PP-OCRv5: PaddleOCR internal and public data; the publisher does not itemise it. OPUS-MT translation uses OPUS parallel corpora collected from public translated texts with their own terms. These disclosures do not claim that dataset terms are unrestricted.

The OCR pack repositories carry no licence file of their own; their model cards state Apache-2.0, as does PaddleOCR. The app shows and stores the bundled `Assets/Models/Licenses/Apache-2.0.txt` for them.

The Whisper export repositories also carry no licence file; OpenAI's repository licenses the code and model weights under MIT, and the export model cards state Apache-2.0. The app shows and stores the bundled `Assets/Models/Licenses/Whisper-MIT-Apache-2.0.txt`, which contains both texts. Whisper was trained on 680,000 hours of audio collected from the web; OpenAI has not published that dataset.

BiRefNet-lite and YuNet carry MIT licences in their publishers' repositories; the app shows and stores copies as `Assets/Models/Licenses/BiRefNet-MIT.txt` and `YuNet-MIT.txt`. BiRefNet was trained on DIS5K, HRSOD, UHRSD and other public segmentation datasets listed by its author; YuNet on WIDER FACE.

# Microsoft Fluent System Icons

UI icons and the application-grid icon are from Microsoft Fluent System Icons 1.1.341, licensed under MIT. Original SVGs, attribution and the full license are in `src/DesktopTools/Assets/Icons`. The application ICO is a rasterization of the original color SVG.

Source: https://github.com/microsoft/fluentui-system-icons

## Installer language flags

The installer bundles small flag PNGs for the English, Russian, German, French and Spanish language choices from [Flagpedia / FlagCDN](https://flagpedia.net/download/api). [Flagpedia's terms](https://flagpedia.net/terms) state that its flag images are public domain. The flags are bundled locally so the language menu works offline.
