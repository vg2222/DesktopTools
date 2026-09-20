# Third-party notices

DesktopTools original source and documentation use the repository MIT license. Bundled third-party icons and other assets are credited below.

The application targets Microsoft .NET 10, WPF and Windows Forms. These open-source projects use the MIT license; their distributions also include notices for incorporated components. A self-contained publication redistributes framework components. Retain the `LICENSE*` and `THIRD-PARTY-NOTICES*` files supplied by the .NET SDK/runtime in the published output; this repository notice does not replace them.

Source license references: [.NET runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [WPF](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT), [Windows Forms](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT).

Windows system APIs, installed system fonts and operating-system materials remain governed by their respective platform terms. GitHub Actions used for CI are development infrastructure and are not bundled with the application.

The screen recorder requires Microsoft Visual C++ x64 Redistributable. DesktopTools setup and the recorder link to Microsoft's [official download page](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist); the redistributable is not bundled or silently installed by DesktopTools. Users obtain it directly from Microsoft and accept the terms in Microsoft's installer. It is licensed separately from DesktopTools.

## Inter 4.1

The unmodified Inter font files are bundled from the [official Inter 4.1 release](https://github.com/rsms/inter/releases/tag/v4.1), Copyright (c) 2016 The Inter Project Authors, under SIL Open Font License 1.1. The complete license is included in `Assets/Fonts/Inter-LICENSE.txt` in the portable application and `src/DesktopTools/Assets/Fonts/Inter-LICENSE.txt` in source. Fonts are embedded for offline use; no system installation is needed.

## QRCoder 1.8.0

QR images are generated locally using [QRCoder](https://www.nuget.org/packages/QRCoder/1.8.0), under the [MIT license](https://github.com/Shane32/QRCoder/blob/v1.8.0/LICENSE.txt).

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

## Background removal: ONNX Runtime 1.29.0 and U²-Net

[Microsoft ONNX Runtime](https://github.com/microsoft/onnxruntime) is distributed under MIT. Complete upstream license and third-party notices are bundled as `Assets/Models/ONNXRUNTIME-LICENSE.txt` and `Assets/Models/ONNXRUNTIME-NOTICES.txt`.

The compact U2NetP model implements [U²-Net](https://github.com/xuebinqin/U-2-Net), under Apache-2.0; see the bundled `Assets/Models/U2NET-LICENSE.txt`. The unmodified weights come from the [rembg release asset](https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx). Model provenance, checksums and processing differences are documented in [background-removal.md](docs/background-removal.md). No rembg Python code or runtime is bundled.

## Local translation models and tokenization

Text tools bundle OPUS-MT models developed by the Language Technology Research Group at the University of Helsinki, converted and quantized to ONNX by Xenova. English→Russian is Apache-2.0; Russian→English is CC BY 4.0. Full attribution, source links, modification descriptions and licenses are included in `Assets/Translation/ATTRIBUTION.md`, `EN-RU-APACHE-2.0.txt` and `CC-BY-4.0.txt`. Exact revisions and checksums are recorded in the bundled manifest. These assets retain their own licenses; DesktopTools source is MIT.

Microsoft.ML.Tokenizers 2.0.0 is MIT licensed. Its complete license and third-party notices are included under `Assets/Translation/TOKENIZERS-LICENSE.txt` and `TOKENIZERS-NOTICES.txt`. ONNX Runtime notices are documented above.

## Screen recording

ScreenRecorderLib 7.0.1 — Copyright (c) 2017 Sverre Skodje, MIT. Source: https://github.com/sskodje/ScreenRecorderLib . Full license: docs/licenses/ScreenRecorderLib-LICENSE.txt. The x64 native wrapper is distributed with DesktopTools. Windows Media Foundation and Microsoft Visual C++ x64 runtime are operating-system/runtime prerequisites; see docs/screen-recorder.md.
# Microsoft Fluent System Icons

UI icons and the application-grid icon are from Microsoft Fluent System Icons 1.1.341, licensed under MIT. Original SVGs, attribution and the full license are in `src/DesktopTools/Assets/Icons`. The application ICO is a rasterization of the original color SVG.

Source: https://github.com/microsoft/fluentui-system-icons
