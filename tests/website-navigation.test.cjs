const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const website = path.resolve(__dirname, '../website');
const home = fs.readFileSync(path.join(website, 'index.html'), 'utf8');
const support = fs.readFileSync(path.join(website, 'support/index.html'), 'utf8');
function headerNav(html) {
  const header = html.match(/<header\b[^>]*>([\s\S]*?)<\/header>/)?.[1];
  const nav = header?.match(/<nav\b[^>]*class="primary-nav"[^>]*>([\s\S]*?)<\/nav>/)?.[1];
  assert.ok(nav, 'Header needs primary navigation');
  return [...nav.matchAll(/<a\b[^>]*>/g)].map(match => match[0]);
}
function href(tag) { return tag.match(/\bhref="([^"]+)"/)?.[1]; }

const homeLinks = headerNav(home);
assert.deepEqual(homeLinks.map(href), ['#features', '#privacy', '#faq', './support/']);
assert.match(homeLinks[3], /\bdata-language-link\b/, 'Support link must retain the selected language');
const supportLinks = headerNav(support);
assert.deepEqual(supportLinks.map(href), ['../#features', '../#privacy', '../#faq', './']);
assert.match(supportLinks[3], /aria-current="page"/, 'Support navigation must identify the current page');
for (const link of supportLinks) assert.match(link, /\bdata-language-link\b/);

for (const [file, html] of [['index.html', home], ['support/index.html', support]]) {
  for (const [, reference] of html.matchAll(/\bhref="([^"]*#[^"]+)"/g)) {
    if (/^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(reference)) continue;
    const [relative, fragment] = reference.split('#');
    const target = relative ? path.resolve(website, path.dirname(file), relative) : path.join(website, file);
    const targetFile = fs.statSync(target).isDirectory() ? path.join(target, 'index.html') : target;
    assert.ok(fs.readFileSync(targetFile, 'utf8').includes('id="' + fragment + '"'), file + ': missing anchor ' + reference);
  }
}

const downloads = [...home.matchAll(/<a\b[^>]*class="button button-primary"[^>]*>([\s\S]*?)<\/a>/g)];
assert.equal(downloads.length, 2, 'Hero and final call to action each need a download link');
for (const [tag, content] of downloads) {
  assert.equal(href(tag), 'https://github.com/vg2222/DesktopTools/releases/latest');
  assert.match(content, /data-i18n="downloadWindows"/, 'Download label must be translated');
}
assert.equal((home.match(/<a\b[^>]*href="https:\/\/github\.com\/vg2222\/DesktopTools\/releases\/latest"/g) || []).length, 2);
console.log('Website navigation and download checks passed.');
