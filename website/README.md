# DesktopTools website

This is a static website for DesktopTools with no runtime dependencies. It uses the app's existing Fluent icon assets and a browser drawing playground. No screenshots or video walkthrough are included.

To preview it locally, serve this directory with any static HTTP server. For example, from `website/` run `python -m http.server 8000` and open `http://localhost:8000`.

The site does not publish automatically. After review and approval, set the repository's GitHub **Settings → Pages → Build and deployment → Source** to **GitHub Actions**, then manually run `.github/workflows/pages.yml` from the Actions tab. GitHub will show the published URL after the workflow succeeds.

The main release button opens the latest GitHub release, where visitors can choose the installer or portable ZIP. To update website text, edit the relevant HTML page and all five language dictionaries in `site.js`. The English HTML is the no-JavaScript fallback. The indigo hero uses the app's real logo and a geometric Windows mark for download buttons. The feature grid, separate drawing demo, four project principles and release details share the same responsive system-font layout. Smooth scrolling, a reading-progress line and section reveals are enabled when the visitor has not requested reduced motion.

Search and sharing metadata use `https://www.desktoptools.cc/` as the canonical URL. `index.html` contains Open Graph tags for Discord, Telegram, Reddit and other link previews, plus a large X card and truthful `SoftwareApplication` structured data. The social image is `images/social-preview.png`, a 1200 × 630 crop of the latest `assets/desktop-tools-social-preview-v4.png`; keep its dimensions and metadata in sync. `sitemap.xml` lists both canonical pages. If the site moves again, update every absolute URL in `index.html`, `sitemap.xml` and `tests/website-seo.test.cjs` before publishing.

After publishing, verify the URL-prefix property in Google Search Console and submit `https://www.desktoptools.cc/sitemap.xml`. Google may take time to crawl and index the page, and neither metadata nor a submitted sitemap guarantees a search result. The five language choices currently render client-side from one HTML page; they are not separate localized pages for search indexing. Link-preview services may cache older images and metadata for a while.

The feature section includes a small browser drawing and spotlight demo, not an app screenshot. Drag with a mouse or touch to draw; focus the surface and press Enter for a sample mark, use arrow keys to move the spotlight, or Escape to clear. The circular page glow follows fine pointers when reduced motion is off. Both effects are in `effects.js`; drawings are temporary and never uploaded.

`ambient.css` adds indigo lighting and a faint hero grid; hover and finite section reveals are in `styles.css`. Motion respects the visitor's reduced-motion setting; there are no perpetual animation loops or scroll hijacking. Navigation marks the current section using IntersectionObserver, including when motion is disabled.

From the repository root, run `node tests/website.test.cjs` to check sections, translations, local assets and manual-only publishing, `node tests/website-interactions.test.cjs` to check language switching and reduced-motion behavior, `node tests/website-navigation.test.cjs` to check the current-section navigation, `node tests/website-ambient.test.cjs` to check page-wide pointer tracking, and `node tests/website-seo.test.cjs` to check search and sharing metadata, sitemap and preview-image dimensions.

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
