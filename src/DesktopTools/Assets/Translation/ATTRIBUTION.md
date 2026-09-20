# Bundled translation model attribution

Original models: **OPUS-MT**, developed by the **Language Technology Research Group, University of Helsinki (Helsinki-NLP)**. Citation: Jörg Tiedemann and Santhosh Thottingal (2020), *OPUS-MT — Building open translation services for the World*, EAMT.

- English → Russian: [Helsinki-NLP/opus-mt-en-ru](https://huggingface.co/Helsinki-NLP/opus-mt-en-ru), Apache License 2.0; full text in `EN-RU-APACHE-2.0.txt`.
- Russian → English: [Helsinki-NLP/opus-mt-ru-en](https://huggingface.co/Helsinki-NLP/opus-mt-ru-en), Creative Commons Attribution 4.0 International; [license](https://creativecommons.org/licenses/by/4.0/), full text in `CC-BY-4.0.txt`.
- ONNX conversion and quantized distribution: **Xenova** ([EN→RU](https://huggingface.co/Xenova/opus-mt-en-ru), [RU→EN](https://huggingface.co/Xenova/opus-mt-ru-en)). These are converted/quantized adaptations of the original models. DesktopTools bundles those files without further weight changes; decoding uses local SentencePiece normalization, sentence splitting and greedy cached generation rather than claiming identical output to the original runtime.

Immutable model revisions, asset URLs, sizes and SHA256 checksums are in `manifest.json`. Attribution and licenses must accompany redistribution. No endorsement by the model authors is implied. Model output can contain mistakes and inherited biases; review translations before relying on them.

Runtime: Microsoft ONNX Runtime 1.29.0 (MIT; notices under `../Models`) and Microsoft.ML.Tokenizers 2.0.0 (MIT; `TOKENIZERS-LICENSE.txt` and `TOKENIZERS-NOTICES.txt`). DesktopTools application source remains MIT; the separately licensed model assets retain the terms above.
