# DesktopTools website

This is a static website for DesktopTools with no runtime dependencies. It uses the app's existing Fluent icon assets and a browser drawing playground. No screenshots or video walkthrough are included.

To preview it locally, serve this directory with any static HTTP server. For example, from `website/` run `python -m http.server 8000` and open `http://localhost:8000`.

The site does not publish automatically. After review and approval, set the repository's GitHub **Settings → Pages → Build and deployment → Source** to **GitHub Actions**, then manually run `.github/workflows/pages.yml` from the Actions tab. GitHub will show the published URL after the workflow succeeds.

The main release button opens the latest GitHub release, where visitors can choose the installer or portable ZIP. To update website text, edit `index.html` and all five language dictionaries in `site.js`. The English HTML is the no-JavaScript fallback. The centered hero uses the app's real logo and a geometric Windows mark for download buttons. Smooth scrolling, a reading-progress line and section reveals are enabled when the visitor has not requested reduced motion.

Search and sharing metadata use `https://vg2222.github.io/DesktopTools/` as the canonical URL. `index.html` contains Open Graph tags for Discord, Telegram, Reddit and other link previews, plus a large X card and truthful `SoftwareApplication` structured data. The social image is `images/social-preview.png`, a 1200 × 630 crop of the latest `assets/desktop-tools-social-preview-v4.png`; keep its dimensions and metadata in sync. `sitemap.xml` lists the canonical page. If the site moves to a custom domain, update every absolute URL in `index.html`, `sitemap.xml` and `tests/website-seo.test.cjs` before publishing.

After publishing, verify the URL-prefix property in Google Search Console and submit `https://vg2222.github.io/DesktopTools/sitemap.xml`. Google may take time to crawl and index the page, and neither metadata nor a submitted sitemap guarantees a search result. The five language choices currently render client-side from one HTML page; they are not separate localized pages for search indexing. Link-preview services may cache older images and metadata for a while.

The feature section includes a small browser drawing and spotlight demo, not an app screenshot. Drag with a mouse or touch to draw; focus the surface and press Enter for a sample mark, use arrow keys to move the spotlight, or Escape to clear. The circular page glow follows fine pointers when reduced motion is off. Both effects are in `effects.js`; drawings are temporary and never uploaded.

`ambient.css` adds page-wide indigo lighting, a fine grid, finite entrance animations, staggered feature reveals and hover feedback. Motion respects the visitor's reduced-motion setting; there are no perpetual animation loops or scroll hijacking. Navigation marks the current section using IntersectionObserver, including when motion is disabled.

From the repository root, run `node tests/website.test.cjs` to check sections, translations, local assets and manual-only publishing, `node tests/website-interactions.test.cjs` to check language switching and reduced-motion behavior, `node tests/website-navigation.test.cjs` to check the current-section navigation, `node tests/website-ambient.test.cjs` to check page-wide pointer tracking, and `node tests/website-seo.test.cjs` to check search and sharing metadata, sitemap and preview-image dimensions.
