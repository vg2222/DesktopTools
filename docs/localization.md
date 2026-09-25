# Interface translations

English is the source language. `src/DesktopTools/Localization/*.ru.json`, `.de.json`, `.fr.json` and `.es.json` map English UI strings to translations and are embedded with `WithCulture=false`. Keep the same key sets in all four languages and use UTF-8. Duplicate keys across feature catalogs must have identical translations.

Use `L.T` only at display boundaries. Use `L.F` for interpolated messages; preserve every indexed placeholder, alignment and numeric format. Preserve file-dialog filter patterns after `|`. Never translate saved action IDs, profile names, file paths, note/script text, OCR output or shortcut strings. Choice controls translate their display template while retaining original selected values; pass `false` for user-defined lists.

New preferences use the Windows UI language when it is supported; otherwise they use English. A saved language always takes precedence. Changing the language saves the preference and restarts DesktopTools automatically. Unknown saved values recover to English. Profile switching preserves the language setting. System dialogs and native Windows messages are controlled by the operating system.

Run the core tests for catalog parity, placeholders, file filters, Unicode, persistence and stable settings. Run the integration harness with `--localization-only` on Windows for five languages, both themes, all main pages and utility renders. Review the resulting `artifacts/integration/locale-*.png` screenshots. This mode does not inject input into other applications. Native-speaker review of phrasing is still welcome before release.
