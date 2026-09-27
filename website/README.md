# DesktopTools website

This is a static website for DesktopTools with no runtime dependencies. It uses the app's existing Fluent icon assets and a browser drawing playground. No screenshots or video walkthrough are included.

To preview it locally, serve this directory with any static HTTP server. For example, from `website/` run `python -m http.server 8000` and open `http://localhost:8000`.

The site does not publish automatically. After review and approval, set the repository's GitHub **Settings → Pages → Build and deployment → Source** to **GitHub Actions**, then manually run `.github/workflows/pages.yml` from the Actions tab. GitHub will show the published URL after the workflow succeeds.

The main release button opens the latest GitHub release, where visitors can choose the installer or portable ZIP. To update website text, edit `index.html` and all five language dictionaries in `site.js`. The English HTML is the no-JavaScript fallback. The centered hero uses the app's real logo and a geometric Windows mark for download buttons. Smooth scrolling, a reading-progress line and section reveals are enabled when the visitor has not requested reduced motion.

The feature section includes a small browser drawing and spotlight demo, not an app screenshot. Drag with a mouse or touch to draw; focus the surface and press Enter for a sample mark, use arrow keys to move the spotlight, or Escape to clear. The circular page glow follows fine pointers when reduced motion is off. Both effects are in `effects.js`; drawings are temporary and never uploaded.

`ambient.css` adds page-wide indigo lighting, a fine grid, finite entrance animations, staggered feature reveals and hover feedback. Motion respects the visitor's reduced-motion setting; there are no perpetual animation loops or scroll hijacking. Navigation marks the current section using IntersectionObserver, including when motion is disabled.

From the repository root, run `node tests/website.test.cjs` to check sections, translations, local assets and manual-only publishing, `node tests/website-interactions.test.cjs` to check language switching and reduced-motion behavior, `node tests/website-navigation.test.cjs` to check the current-section navigation, and `node tests/website-ambient.test.cjs` to check page-wide pointer tracking.
