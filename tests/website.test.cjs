const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '..');
const site = path.join(root, 'website');
const html = fs.readFileSync(path.join(site, 'index.html'), 'utf8');
const css = fs.readFileSync(path.join(site, 'styles.css'), 'utf8');
const script = fs.readFileSync(path.join(site, 'site.js'), 'utf8');

function attribute(tag, name) {
  return tag.match(new RegExp('(?:^|\\s)' + name + '=["\']([^"\']*)["\']'))?.[1];
}
for (const id of ['features', 'privacy', 'faq', 'download']) {
  assert.match(html, new RegExp('<section\\b[^>]*\\bid="' + id + '"'), 'Missing ' + id + ' section');
}
const panels = [...html.matchAll(/<article\b[^>]*class="chapter[^"]*"[^>]*>([\s\S]*?)<\/article>/g)];
assert.equal(panels.length, 4, 'Four story chapters explain the main tools');
for (const [, panel] of panels) {
  const images = [...panel.matchAll(/<img\b[^>]*>/g)];
  assert.equal(images.length, 1, 'Each showcase article needs one app screenshot');
  const tag = images[0][0];
  const src = attribute(tag, 'src');
  assert.match(src ?? '', /^\.\/images\/screens\/[^/]+\.webp$/, 'Showcase screenshots must be local WebP files');
  assert.ok(fs.statSync(path.resolve(site, src)).isFile(), 'Missing showcase screenshot ' + src);
  for (const dimension of ['width', 'height']) {
    assert.match(attribute(tag, dimension) ?? '', /^[1-9]\d*$/, 'Screenshot needs a positive ' + dimension);
  }
  assert.ok(attribute(tag, 'alt')?.trim(), 'Screenshot needs descriptive alt text: ' + src);
  assert.ok(attribute(tag, 'data-i18n-alt'), 'Screenshot needs translated alt text: ' + src);
}
const toolList = html.match(/<ul class="tool-list">([\s\S]*?)<\/ul>/)?.[1] ?? '';
assert.equal((toolList.match(/<li>/g) || []).length, 6, 'Six entries cover the smaller jobs');
const stages = [...html.matchAll(/<img\b[^>]*\bdata-stage="([^"]+)"/g)].map(match => match[1]);
const chapterNames = [...html.matchAll(/\bdata-chapter="([^"]+)"/g)].map(match => match[1]);
assert.deepEqual(stages, chapterNames, 'Each story chapter needs its own screen on the sticky stage');
assert.doesNotMatch(html, /hero-badge|scroll-progress/, 'Removed hero badge and scroll progress must stay removed');
const faq = html.match(/<section\b[^>]*\bid="faq"[^>]*>([\s\S]*?)<\/section>/)?.[1];
const questions = [...faq.matchAll(/<details\b[^>]*>([\s\S]*?)<\/details>/g)];
assert.equal(questions.length, 7, 'FAQ should have seven native disclosure elements');
for (const [, question] of questions) {
  assert.match(question, /<summary\b[^>]*>[^<]+<\/summary>/, 'Each FAQ needs a readable question');
  assert.match(question, /<p\b[^>]*>[\s\S]*?\S[\s\S]*?<\/p>/, 'Each FAQ needs an answer');
}
assert.match(html, /<link rel="icon" href="\.\/images\/app-icon\.png"/, 'Favicon must use the app icon');
assert.ok(fs.readFileSync(path.join(site, 'images/app-icon.png')).equals(
  fs.readFileSync(path.join(root, 'src/DesktopTools/Assets/Icons/AppIcon.png'))), 'Website icon must match the app icon');
assert.match(html, /README\.md#-local-first-by-design/, 'Privacy link must point to the project privacy details');

const dictionarySource = script.match(/const translations = (\{[\s\S]*?\n\});/);
assert.ok(dictionarySource, 'Translation dictionaries were not found');
const translations = vm.runInNewContext('(' + dictionarySource[1] + ')');
assert.deepEqual(Object.keys(translations).sort(), ['de', 'en', 'es', 'fr', 'ru']);
const englishKeys = Object.keys(translations.en);
for (const [language, dictionary] of Object.entries(translations)) {
  assert.deepEqual(Object.keys(dictionary), englishKeys, language + ' needs the English key set and order');
  for (const key of englishKeys) {
    assert.ok(typeof dictionary[key] === 'string' && dictionary[key].trim(), language + '.' + key + ' must have text');
    const placeholders = text => (text.match(/\{[^{}]+\}/g) || []).sort();
    assert.deepEqual(placeholders(dictionary[key]), placeholders(translations.en[key]), language + '.' + key + ' must preserve placeholders');
  }
}
for (const page of ['index.html', 'support/index.html']) {
  const content = fs.readFileSync(path.join(site, page), 'utf8');
  for (const [, attributeName, key] of content.matchAll(/\b(data-i18n(?:-alt|-aria)?)="([^"]+)"/g)) {
    for (const [language, dictionary] of Object.entries(translations)) {
      assert.ok(Object.hasOwn(dictionary, key), page + ': ' + attributeName + '=' + key + ' missing in ' + language);
    }
  }
}

function checkLocalAsset(reference, file) {
  // Anchors, embedded data and external URLs do not refer to files in the site.
  if (!reference || /^(?:#|[a-z][a-z\d+.-]*:|\/\/)/i.test(reference)) return;
  const relative = decodeURIComponent(reference.split(/[?#]/)[0]);
  if (!relative) return;
  const asset = relative.startsWith('/') ? path.join(site, relative.slice(1)) : path.resolve(path.dirname(file), relative);
  assert.ok(fs.existsSync(asset), path.relative(site, file) + ': missing local asset ' + reference);
}
function checkAssets(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) { checkAssets(file); continue; }
    if (!/\.(?:html|css)$/.test(file)) continue;
    const content = fs.readFileSync(file, 'utf8');
    if (file.endsWith('.html')) {
      for (const [, reference] of content.matchAll(/\b(?:src|href|poster)=["']([^"']+)["']/g)) checkLocalAsset(reference, file);
      for (const [, sources] of content.matchAll(/\bsrcset="([^"]+)"/g)) {
        for (const source of sources.split(',')) checkLocalAsset(source.trim().split(/\s+/)[0], file);
      }
      for (const [, source] of content.matchAll(/<script\b[^>]*\bsrc=["']([^"']+)["']/g)) {
        assert.doesNotMatch(source, /^(?:https?:)?\/\//i, 'Scripts must remain local');
      }
    }
    for (const match of content.matchAll(/url\(\s*(?:"([^"]*)"|'([^']*)'|([^\s)]+))\s*\)/g)) {
      checkLocalAsset(match[1] ?? match[2] ?? match[3], file);
    }
    for (const [, reference] of content.matchAll(/@import\s+["']([^"']+)["']/g)) checkLocalAsset(reference, file);
  }
}
checkAssets(site);

assert.match(css, /@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*?animation:\s*none\s*!important/, 'Reduced motion must disable animations');
assert.match(css, /@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*?\.reveal-ready\s*\{[^}]*opacity:\s*1;[^}]*transform:\s*none;/, 'Reduced motion must leave reveal content visible');
assert.match(css, /html\s*\{\s*scroll-behavior:\s*smooth;/, 'Smooth scrolling is missing');
assert.match(css, /\.product-frame\s*\{[^}]*--p:/, 'Scroll-linked product frame is missing');
assert.match(script, /\[data-words\]/, 'Word-by-word statement is missing');
assert.match(script, /new IntersectionObserver/, 'Section reveal behavior is missing');

const accentHex = css.match(/--accent:\s*(#[0-9a-f]{6})\s*;/i)?.[1];
assert.ok(accentHex, 'Missing accent color token');
const accent = [1, 3, 5].map(index => parseInt(accentHex.slice(index, index + 2), 16) / 255);
const linear = accent.map(value => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4);
const luminance = linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722;
const [red, green, blue] = accent;
const max = Math.max(...accent), min = Math.min(...accent), range = max - min;
assert.ok(range > 0, 'Accent must be chromatic');
const degrees = max === red ? ((green - blue) / range) % 6 : max === green ? (blue - red) / range + 2 : (red - green) / range + 4;
const hue = (degrees * 60 + 360) % 360;
assert.ok(hue >= 300 && hue <= 340, 'Accent should be magenta, got hue ' + hue);
assert.ok(1.05 / (luminance + 0.05) >= 4.5, 'White button text needs at least 4.5:1 contrast');

const workflow = fs.readFileSync(path.join(root, '.github/workflows/pages.yml'), 'utf8');
assert.match(workflow, /workflow_dispatch:/, 'Publishing must require a manual action');
assert.doesNotMatch(workflow, /^\s*(push|schedule):/m, 'Publishing must not run automatically');
console.log('Website structure, translations, assets and accessibility checks passed.');
