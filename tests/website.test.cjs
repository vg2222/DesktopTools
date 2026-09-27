const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '..');
const site = path.join(root, 'website');
const html = fs.readFileSync(path.join(site, 'index.html'), 'utf8');
const css = fs.readFileSync(path.join(site, 'styles.css'), 'utf8');
const script = fs.readFileSync(path.join(site, 'site.js'), 'utf8');

for (const id of ['features', 'workflow', 'privacy', 'download']) {
  assert.match(html, new RegExp(`id="${id}"`), `Missing ${id} section`);
}
for (const key of ['heroCapture', 'heroRecord', 'heroPresent', 'heroBody', 'downloadWindows']) {
  assert.match(html, new RegExp(`data-i18n="${key}"`), `Missing ${key} in page`);
}
assert.match(html, /class="hero hero-centered"/, 'Hero background should span the viewport');
assert.match(html, /class="hero-copy"/, 'Hero content should remain centered within the full-width section');
assert.match(html, /class="hero-emblem"/, 'The centered hero should show the app logo');
assert.doesNotMatch(html, /hero-stage|hero-tabs|data-hero-tool|role="tablist"/, 'The unwanted right-side hero card is still present');
assert.equal((html.match(/class="windows-mark"/g) || []).length, 2, 'Both download buttons should use the new Windows mark');
assert.doesNotMatch(html, /⊞/, 'The old text glyph is still used for Windows');
assert.match(html, /<link rel="icon" href="\.\/images\/app-icon\.png"/, 'Favicon does not use the app icon');
assert.equal((html.match(/src="\.\/images\/app-icon\.png"/g) || []).length, 3, 'Use the app icon in header, hero and footer');
assert.doesNotMatch(html, /home-current\.png/, 'Outdated product screenshot is still embedded');
assert.doesNotMatch(html, /in-action|action-video|action\.js|action\.css|hero-watch/, 'The cancelled walkthrough must not be served');
assert.match(html, /https:\/\/github\.com\/vg2222\/DesktopTools\/releases\/latest/);
assert.match(html, /README\.md#-local-first-by-design/, 'Privacy link must point to the project privacy details');
assert.ok(fs.readFileSync(path.join(site, 'images', 'app-icon.png')).equals(fs.readFileSync(path.join(root, 'src', 'DesktopTools', 'Assets', 'Icons', 'AppIcon.png'))), 'Website icon must match the app icon');

const dictionarySource = script.match(/const translations = (\{[\s\S]*?\n\});/);
assert.ok(dictionarySource, 'Translation dictionaries were not found');
const translations = vm.runInNewContext(`(${dictionarySource[1]})`);
assert.deepEqual(Object.keys(translations).sort(), ['de', 'en', 'es', 'fr', 'ru']);
const englishKeys = Object.keys(translations.en).sort();
for (const [language, dictionary] of Object.entries(translations)) {
  assert.deepEqual(Object.keys(dictionary).sort(), englishKeys, `${language} has missing translation keys`);
}
for (const attribute of ['data-i18n', 'data-i18n-alt', 'data-i18n-aria']) {
  for (const key of [...html.matchAll(new RegExp(`${attribute}="([^"]+)"`, 'g'))].map(match => match[1])) {
    assert.ok(englishKeys.includes(key), `Missing English translation for ${key}`);
  }
}
for (const match of html.matchAll(/(?:src|href)="\.\/([^"]+)"/g)) {
  const assetPath = match[1].split('?')[0];
  assert.ok(fs.existsSync(path.join(site, assetPath)), `Missing local asset ${assetPath}`);
}
assert.match(css, /prefers-reduced-motion/, 'Reduced-motion styling is missing');
assert.match(css, /html\s*\{\s*scroll-behavior:\s*smooth;/, 'Smooth scrolling is missing');
assert.match(css, /animation-timeline:\s*scroll\(root block\)/, 'Scroll progress effect is missing');
assert.match(script, /IntersectionObserver/, 'Section reveal behavior is missing');

function colorToken(name) {
  const value = css.match(new RegExp(`--${name}:\\s*(#[0-9a-f]{6})`, 'i'))?.[1];
  assert.ok(value, `Missing ${name} color token`);
  return [1, 3, 5].map(index => parseInt(value.slice(index, index + 2), 16) / 255);
}
function luminance(rgb) {
  const linear = rgb.map(value => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4);
  return linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722;
}
function hue(rgb) {
  const [red, green, blue] = rgb;
  const max = Math.max(...rgb), min = Math.min(...rgb), range = max - min;
  if (!range) return 0;
  let degrees = max === red ? ((green - blue) / range) % 6 : max === green ? (blue - red) / range + 2 : (red - green) / range + 4;
  return (degrees * 60 + 360) % 360;
}
const background = colorToken('background');
const accent = colorToken('accent');
assert.ok(luminance(background) < 0.003, 'Background is not substantially darker');
assert.ok(luminance(accent) < 0.13, 'Primary accent is still too bright');
assert.ok(hue(accent) >= 230 && hue(accent) <= 255, 'Primary accent should be purple-blue');
assert.ok((1.05) / (luminance(accent) + 0.05) >= 4.5, 'White button text needs adequate contrast');

const workflow = fs.readFileSync(path.join(root, '.github', 'workflows', 'pages.yml'), 'utf8');
assert.match(workflow, /workflow_dispatch:/, 'Publishing must require a manual action');
assert.doesNotMatch(workflow, /^\s*(push|schedule):/m, 'Publishing must not run automatically');

console.log('Website checks passed.');
