# DesktopTools website

This is a static website for DesktopTools with no runtime dependencies, build step or third-party scripts. It uses the app's existing Fluent icon assets and real app screenshots.

To preview it locally, serve this directory with any static HTTP server. For example, from `website/` run `python -m http.server 8000` and open `http://localhost:8000`.

The site does not publish automatically. After review and approval, set the repository's GitHub **Settings → Pages → Build and deployment → Source** to **GitHub Actions**, then manually run `.github/workflows/pages.yml` from the Actions tab. GitHub will show the published URL after the workflow succeeds.

Both download buttons open the latest GitHub release, where visitors can choose the installer or portable ZIP. To update website text, edit the relevant HTML page and all five language dictionaries in `site.js`, keeping the same keys in English order. Translate screenshot alt text and accessible labels as well as visible copy; preserve `{coin}` and `{network}` placeholders. The English HTML is the no-JavaScript fallback.

The home page has a centered hero with a screenshot of the app, followed by these sections:

- `#features`: four alternating showcase panels for screenshots, recording, presenting and text tools, followed by six smaller tool cards.
- `#privacy`: local processing, free features, open source and account-free use, plus the limits of capture exclusion.
- `#faq`: six native `details` disclosures.
- `#download`: a final download button and links to checksums and source contributions.

The header links to Features, Privacy, FAQ and the separate Support page. The responsive dark layout uses system fonts and a purple accent. Smooth scrolling, CSS scroll progress and IntersectionObserver reveals respect reduced motion; content stays visible without JavaScript or IntersectionObserver.

Search and sharing metadata use `https://www.desktoptools.cc/` as the canonical URL. `index.html` contains Open Graph tags for Discord, Telegram, Reddit and other link previews, plus a large X card and truthful `SoftwareApplication` structured data. The social image is `images/social-preview.png`, a 1200 × 630 crop of the latest `assets/desktop-tools-social-preview-v4.png`; keep its dimensions and metadata in sync. `sitemap.xml` lists both canonical pages. If the site moves again, update every absolute URL in `index.html`, `sitemap.xml` and `tests/website-seo.test.cjs` before publishing.

After publishing, verify the URL-prefix property in Google Search Console and submit `https://www.desktoptools.cc/sitemap.xml`. Google may take time to crawl and index the page, and neither metadata nor a submitted sitemap guarantees a search result. The five language choices currently render client-side from one HTML page; they are not separate localized pages for search indexing. Link-preview services may cache older images and metadata for a while.

## Screenshots and a future demo video

The WebP images in `images/screens/` were converted from PNG renders produced by the `--github-gallery` integration harness. The source gallery is `docs/screenshots/gallery/`; these are real application windows with clean demo content, not mockups or personal desktop captures. From the repository root on Windows, regenerate the gallery with:

```powershell
dotnet run --project tests/DesktopTools.IntegrationTests/DesktopTools.IntegrationTests.csproj -c Release -p:NuGetAudit=false -- --github-gallery
```

`scripts/capture-screenshots.ps1` runs that harness and packages the gallery index. The generated gallery directory is ignored by Git; a checkout may contain only some of these PNGs. Website conversion is a separate authoring step: resize the selected PNGs and export them as WebP with your image tool, then place the results under `images/screens/`. Do not change the original gallery PNGs as a side effect of exporting website copies.

| Website file | Harness PNG output |
| --- | --- |
| `home.webp` | `01-home-dark.png` |
| `presentation.webp` | `03-presentation-tools-dark.png` |
| `editor.webp` | `05-screenshot-editor-dark.png` |
| `recorder.webp` | `09-screen-recorder-dark.png` |
| `teleprompter.webp` | `13-teleprompter-dark.png` |
| `text.webp` | `18-text-from-screenshot-dark.png` |

`home.webp` is the hero image; the four panels use `editor.webp`, `recorder.webp`, `teleprompter.webp` and `text.webp`. `presentation.webp` is available as an alternate. When replacing an image, keep its HTML `width` and `height` in sync with the exported dimensions, and update alt text in the HTML and every dictionary if the content changes.

To add a recorded demo later, put a reviewed recording with non-sensitive demo content in a local directory such as `media/`. Replace the hero image inside `.product-frame` with a native `video` element, local `source` files and `home.webp` as its `poster`. Use `controls`, `playsinline` and `preload="none"`, keep explicit dimensions, and add a translated accessible label and a fallback link to the recording. Provide captions or a transcript for spoken content. Leave playback user-initiated so reduced-motion visitors do not get an autoplaying demo. The existing `.product-frame video` and `.panel-media video` rules support video without a layout change or a third-party player. Update the relevant tests if a showcase screenshot is replaced by video.

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
