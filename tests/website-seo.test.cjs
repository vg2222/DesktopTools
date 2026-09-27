const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const website = path.resolve(__dirname, '../website');
const html = fs.readFileSync(path.join(website, 'index.html'), 'utf8');
const baseUrl = 'https://vg2222.github.io/DesktopTools/';

function meta(attribute, key) {
  const tags = [...html.matchAll(/<meta\s+[^>]+>/g)].map(match => match[0]);
  const tag = tags.find(value => value.includes(`${attribute}="${key}"`));
  assert.ok(tag, `Missing ${key} metadata`);
  const content = tag.match(/\bcontent="([^"]*)"/)?.[1];
  assert.ok(content, `${key} metadata has no content`);
  return content;
}

const canonical = html.match(/<link\s+rel="canonical"\s+href="([^"]+)"/)?.[1];
assert.equal(canonical, baseUrl, 'Search engines need the public GitHub Pages URL as canonical');
assert.match(html.match(/<title>([^<]+)<\/title>/)?.[1] ?? '', /DesktopTools.*Windows 11/, 'Title should identify the product and platform');
assert.match(meta('name', 'description'), /screenshots|screen capture/i, 'Description should identify the product use');
assert.equal(meta('property', 'og:type'), 'website');
assert.equal(meta('property', 'og:url'), canonical);
assert.equal(meta('property', 'og:site_name'), 'DesktopTools');
assert.ok(meta('property', 'og:title').includes('DesktopTools'));
assert.ok(meta('property', 'og:description').length >= 60);

const imageUrl = meta('property', 'og:image');
assert.equal(imageUrl, `${baseUrl}images/social-preview.png`, 'Social preview must use a public absolute URL');
assert.equal(meta('name', 'twitter:card'), 'summary_large_image');
assert.equal(meta('name', 'twitter:image'), imageUrl, 'X and Open Graph should share the same preview image');
assert.equal(Number(meta('property', 'og:image:width')), 1200);
assert.equal(Number(meta('property', 'og:image:height')), 630);
assert.ok(meta('property', 'og:image:alt').includes('DesktopTools'));
assert.ok(meta('name', 'twitter:image:alt').includes('DesktopTools'));

const image = fs.readFileSync(path.join(website, 'images/social-preview.png'));
assert.equal(image.subarray(0, 8).toString('hex'), '89504e470d0a1a0a', 'Social preview must be a PNG');
assert.equal(image.readUInt32BE(16), 1200, 'PNG width must match metadata');
assert.equal(image.readUInt32BE(20), 630, 'PNG height must match metadata');
assert.ok(image.length < 5_000_000, 'Preview should be compact enough for link crawlers');

const sitemap = fs.readFileSync(path.join(website, 'sitemap.xml'), 'utf8');
assert.deepEqual([...sitemap.matchAll(/<loc>([^<]+)<\/loc>/g)].map(match => match[1]), [canonical], 'Sitemap should list only the canonical page');

const jsonLd = html.match(/<script\s+type="application\/ld\+json">\s*([\s\S]*?)\s*<\/script>/)?.[1];
assert.ok(jsonLd, 'SoftwareApplication structured data is missing');
const app = JSON.parse(jsonLd);
assert.equal(app['@type'], 'SoftwareApplication');
assert.equal(app.name, 'DesktopTools');
assert.equal(app.url, canonical);
assert.equal(app.operatingSystem, 'Windows 11');
assert.equal(app.offers?.price, 0, 'Free software must not be represented as paid');
assert.ok(!('aggregateRating' in app) && !('review' in app), 'Do not fabricate ratings or reviews');

console.log('Website SEO and social preview checks passed.');
