# DesktopTools website

This is a static website for DesktopTools with no runtime dependencies, build step or third-party scripts. It uses the app's existing Fluent icon assets and real app screenshots.

To preview it locally, serve this directory with any static HTTP server. For example, from `website/` run `python -m http.server 8000` and open `http://localhost:8000`.

The site does not publish automatically. After review and approval, set the repository's GitHub **Settings → Pages → Build and deployment → Source** to **GitHub Actions**, then manually run `.github/workflows/pages.yml` from the Actions tab. GitHub will show the published URL after the workflow succeeds.

Both download buttons open the latest GitHub release, where visitors can choose the installer or portable ZIP. To update website text, edit the relevant HTML page and all five language dictionaries in `site.js`, keeping the same keys in English order. Translate screenshot alt text and accessible labels as well as visible copy; preserve `{coin}` and `{network}` placeholders. The English HTML is the no-JavaScript fallback.

The home page has a centered hero with a screenshot of the app, followed by these sections:

- `#features`: four chapters (screenshots, recording, presenting and text) beside a sticky demo video, followed by six smaller tool cards. On windows wider than 960px one wheel or trackpad scroll moves one chapter; narrower windows show each video under its chapter.
- `#privacy`: local processing, free features, open source and account-free use, plus the limits of capture exclusion.
- `#faq`: six native `details` disclosures.
- `#download`: a final download button and links to checksums and source contributions.

The header links to Features, Privacy, FAQ and the separate Support page. The responsive dark layout uses system fonts and a magenta accent. Smooth scrolling, CSS scroll progress and IntersectionObserver reveals respect reduced motion; content stays visible without JavaScript or IntersectionObserver.

Search and sharing metadata use `https://www.desktoptools.cc/` as the canonical URL. `index.html` contains Open Graph tags for Discord, Telegram, Reddit and other link previews, plus a large X card and truthful `SoftwareApplication` structured data. The social image is `images/social-preview.png` (1200 × 630), built from the gallery home render by `python scripts/make-social-preview.py`; keep its dimensions and metadata in sync. `sitemap.xml` lists both canonical pages. If the site moves again, update every absolute URL in `index.html`, `sitemap.xml` and `tests/website-seo.test.cjs` before publishing.

After publishing, verify the URL-prefix property in Google Search Console and submit `https://www.desktoptools.cc/sitemap.xml`. Google may take time to crawl and index the page, and neither metadata nor a submitted sitemap guarantees a search result. The five language choices currently render client-side from one HTML page; they are not separate localized pages for search indexing. Link-preview services may cache older images and metadata for a while.

## Screenshots and demo videos

`images/screens/home.webp` is the hero image. It was converted from `01-home-dark.png`, a render from the `--github-gallery` integration harness (`docs/screenshots/gallery/`); these are real application windows with clean demo content, not mockups or personal desktop captures. From the repository root on Windows, regenerate the gallery with:

```powershell
dotnet run --project tests/DesktopTools.IntegrationTests/DesktopTools.IntegrationTests.csproj -c Release -p:NuGetAudit=false -- --github-gallery
```

`scripts/capture-screenshots.ps1` runs that harness and packages the gallery index. The generated gallery directory is ignored by Git. Website conversion is a separate step: resize the PNG and export it as WebP (for example `ffmpeg -i 01-home-dark.png -vf scale=1800:-1 -c:v libwebp -quality 86 home.webp`), keep the HTML `width` and `height` in sync, and never change the original gallery PNGs.

The feature chapters use short screen recordings in `videos/`, recorded against local demo pages with made-up data:

| Chapter | Video |
| --- | --- |
| Screenshots | `01-screenshot.mp4`, then `03-check-screenshot.mp4` |
| Text | `02-extract-text.mp4` |
| Recording | `04-recording.mp4` |
| Presenting | `05-present.mp4` |

Each clip has a `.webp` poster made from its first frame. Encode new clips without audio as H.264 with `-crf 26 -pix_fmt yuv420p -movflags +faststart`, keep them under about 3 MB, and review every frame for personal content (file dialogs, folder names, notifications, account names) before committing. Videos are muted, play only while visible, and step through `data-sources` in order; with reduced motion they stay paused and show controls. Update the alt text keys `shotEditor`, `shotRecorder`, `shotTeleprompter` and `shotText` in every dictionary if a clip changes.

## Website checks

From the repository root, run every website check in PowerShell:

```powershell
Get-ChildItem tests/website*.test.cjs | ForEach-Object {
    node $_.FullName
    if ($LASTEXITCODE -ne 0) { throw "Failed: $($_.Name)" }
}
```

`website.test.cjs` checks sections, screenshots, tool cards, FAQ markup, all five translation dictionaries on both pages, local HTML/CSS assets, reduced motion, scroll progress, accent contrast, the app icon and manual-only publishing. `website-interactions.test.cjs` checks language switching, translated alt text and labels, language-preserving links, header scrolling and reveal behavior. `website-navigation.test.cjs` checks navigation targets and both download links. `website-support.test.cjs` checks wallet rendering and clipboard behavior. `website-seo.test.cjs` checks search and sharing metadata, the sitemap and preview-image dimensions.

## Support wallets

The live wallet list is the single `wallets` array in `support/wallets.js`. It contains only the verified USDT wallet on Tron (TRC-20). `render-wallets.js` creates one card per entry from the initial HTML card, assigns unique labels/status IDs, and `copy.js` attaches independent copy actions. Generic warning copy in all five `site.js` dictionaries substitutes `{coin}` and `{network}`. Scripts are local, deferred and ordered; there is no build, fetch, package install or runtime dependency.

To add a verified wallet, add its QR image under `support/` and one entry to the array with:

- `coin`: the exact coin or token name.
- `network`: the exact network, including its token standard when relevant.
- `address`: the verified public receiving address.
- `explorerUrl`: its full HTTPS address page on the correct chain explorer.
- `qr`: a relative path to a local QR image encoding that exact address.
- Optional `coinIcon` and `networkIcon`: relative paths to local marks. Omit these to use neutral Fluent icons; never reuse another coin's mark.

Do not add sample wallets to the live list. Decode each new QR and compare its complete value with the address before publishing. Each card automatically gets a coin/network warning, selectable address, copy feedback, QR, and explorer link. Run every `tests/website*.test.cjs` check after editing. Adding an entry needs no renderer or test changes. The primary fallback QR checksum is pinned; update it only if replacing that QR, after decoding and verifying the exact file.

The English card in `support/index.html` is the no-JavaScript fallback and remains usable if scripts fail. It is also the renderer's markup template. If replacing the primary wallet, update its address, coin, network, icons, warning, explorer link and QR together. Additional config entries are rendered with JavaScript; keep the fallback primary address available without it. Copy buttons stay hidden without JavaScript. Clipboard failure selects the address for manual copying. The USDT and Tron marks are from the [Tether media assets](https://tether.to/en/media/) and [TRON media kit](https://tron.network/resources/).

The support page also links to the real repository for stars, issues, translation guidance and sharing. It has its own canonical and social metadata; keep `https://www.desktoptools.cc/support/` and the home canonical in sync with `sitemap.xml`.
