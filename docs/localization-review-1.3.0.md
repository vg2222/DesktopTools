# Local AI update language review

Reviewed against `476fdba..a3c1ab1` on 2026-11-15. English lives in JSON keys and UI source, not a separate English catalog. All 704 JSON catalogs were checked for feature/language coverage, key parity, duplicate/conflicting values, empty values, broken Unicode, exact format placeholders (including numeric formats), file filters and English leftovers. Each language has 2,005 distinct merged keys; there are 23,166 entries including repeated feature keys. The embedded model names, purposes, training-data statements and limitations are present in every language.

Identical English/translated values were inspected. They are units, placeholder-only values, product names or valid shared words (for example French “Licence”, Polish “Model”, German “Revision”). A scan for unchanged multiword English fragments found Windows component names such as Media Feature Pack. No unintended English fallback was found by these checks. This is a catalog and wording review, not a native-speaker certification or a claim that every rendered dialog was inspected by hand.

## Corrections

- German `ai-models`: rewrote the singular and plural ownership warnings; their previous coordinated verbs were ungrammatical. Preserved the distinction between the project's ownership and the publisher's affiliation/endorsement. Standardized the OCR feature label to “Textwerkzeuge”.
- Portuguese `ocr-pack`: aligned download wording with “Baixar” in the model and transcript pages; removed the mixture of “transferir” and “baixar” for the same operation. Used Brazilian wording consistently in the affected messages.
- Spanish `ocr-pack` and `transcripts`: aligned the affected prompts with the informal address already used by model consent, instead of switching between “tú” and “usted”.

## Terms to keep consistent

These are working terms for this update, including normal case/number inflections. “Model pack” is descriptive here; the UI usually names a specific OCR or translation pack.

| Language | Model pack / pack | Licence | Transcript |
|---|---|---|---|
| de | Modellpaket; OCR-Paket, Übersetzungspaket | Lizenz | Transkript |
| es | paquete de modelos; paquete OCR, paquete de traducción | licencia | transcripción |
| fr | pack de modèles; pack OCR, pack de traduction | licence | transcription |
| it | pacchetto di modelli; pacchetto OCR, pacchetto di traduzione | licenza | trascrizione |
| ja | モデルパック; OCR パック, 翻訳パック | ライセンス | 文字起こし |
| pl | pakiet modeli; pakiet OCR, pakiet tłumaczenia | licencja | transkrypcja |
| pt | pacote de modelos; pacote OCR, pacote de tradução | licença | transcrição |
| ru | пакет моделей; OCR-пакет, пакет перевода | лицензия | расшифровка |
| tr | model paketi; OCR paketi, çeviri paketi | lisans | döküm |
| uk | пакет моделей; OCR-пакет, пакет перекладу | ліцензія | розшифровка |
| zh | 模型包; OCR 包, 翻译包 | 许可证 | 转写 |

## Native-speaker review still needed

Native-speaker review of new wording in all 11 languages is **Not tested**. The older Ukrainian, Chinese, Portuguese, Japanese, Polish, Turkish and Italian catalogs also retain their previously documented review gap. Prioritize the exact English keys below; the corresponding values live in each named `*.<language>.json` file. These are review requests, not confirmed remaining translation defects.

| Catalog / languages | Exact key or key group | Review focus |
|---|---|---|
| `ai-models.*`, all 11 | `I have read and accept this licence`; `The licence is shown in its original language. That text is the one that applies.` | Natural acceptance wording and original-language precedence |
| `ai-models.*`, all 11 | `This model is not part of DesktopTools. It was not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by its publisher. It is provided by its publisher as is, under the licence above. Results can be wrong; check them before you rely on them.` | Ownership, endorsement and “as is”; recheck revised German |
| `ai-models.*`, all 11 | `These models are not part of DesktopTools. They were not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by their publishers.` | Same meaning in the plural |
| `ai-models.*`, all 11 | `This model is third-party software provided by its publisher under its own licence. DesktopTools downloads it at your request and runs it only on this PC. Your images, recordings and text are not sent anywhere. The download contacts {0}, which can see your IP address.` | Local processing, user-requested network contact and IP disclosure |
| `transcripts.*`, all 11 | `Recording and transcribing other people can require their consent, depending on where you live. DesktopTools transcribes only on this PC and only when you ask.` | Preserve “can require” rather than making a universal legal claim |
| `transcripts.*`, all 11 | `Made by a speech model on this PC. It can mishear words or invent text during silence; check it before you rely on it.` | Recognition-error warning, without suggesting verified output |
| `transcripts.*`, all 11 | `{0} is already next to the video and was not written by DesktopTools. Replace it with the new transcript?`; `Captions were not saved next to the video. The transcript is kept in DesktopTools.` | Caption files versus the saved working transcript |
| `transcripts.tr`, Turkish | `Accurate` → `Doğru`; `Transcripts: accurate model` → `Dökümler: doğru model` | Whether the label reads as guaranteed correctness; choose a natural relative-accuracy label |
| `transcripts.pl`, Polish; `transcripts.ja`, Japanese; `transcripts.zh`, Chinese | `Transcript`; `Transcribe`; `Save captions` | Natural distinction between a transcript, its creation and exported captions |
| `ocr-pack.*`, all 11 | `Cyrillic`; `Reads Russian, Ukrainian and Belarusian text.`; `Chinese and Japanese` | Alphabet labels must not imply all Cyrillic languages or vertical-text support |
| `ocr-pack.pt`, Portuguese | `The OCR pack is damaged. Download it again on the AI models page. The text was read with Windows OCR.`; `Downloading the OCR pack…` | Check revised download vocabulary and Brazilian wording |
| `tool-improvements.*`, all 11 | `Touch up`; `Restore`; `Erase`; `Brush size` | Mask editing, not an object-removal feature |
| `tool-improvements.pl`, Polish | `Face finder` → `Wyszukiwanie twarzy` | Whether a tool noun is more natural; keep detector/action references consistent |
| `tool-improvements.*`, all 11 | `Finds where faces are in a screenshot so Check screenshot can suggest covering them. It does not recognise who anyone is and stores nothing about faces.` | Locate versus identify; no promise that every face is detected |
| `hidden-details.*`, all 11 | `Remove hidden details (location, device, date, author)`; `Hidden details could not be read. Tick the box to save a copy without any.` | Embedded metadata versus visible picture content |
| `capture-names.*`, all 11 | `Smart names use the app and window title of what you captured, for example "Chrome – Invoice 1042 – 2026-11-11 09-05-07". Window titles can contain document names or message subjects.` | Naming/privacy explanation and a readable localized example |

For the affected AI features, check both themes and minimum dialog size at 100%/200% scale. The localization render harness does not certify every modal consent window; `--model-consent-ui-only` is the dedicated all-language consent check. Physical pointer interaction, mixed-DPI behavior and receiving-viewer privacy remain **Not tested** in this documentation pass.

## Owner review

- Approve the final English consent warnings and translated legal wording, especially the revised German ownership statements.
- Training-data disclosures: decided on 2026-11-15 to keep the per-model note in the consent window as written. The documentation does not turn disclosure into an assurance about dataset rights.
- Confirm the `1.3.0` download filenames and final binaries/checksums at release time. This work leaves the app version and every release checksum unchanged.
- Decide whether the existing generic “Português” label should explicitly select a regional convention in a future language policy. This pass follows the existing Brazilian download/transcript wording.
