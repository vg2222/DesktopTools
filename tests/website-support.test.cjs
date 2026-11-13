const assert = require('node:assert/strict');
const { createHash } = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const website = path.join(__dirname, '../website');
const support = path.join(website, 'support');
const script = fs.readFileSync(path.join(support, 'copy.js'), 'utf8');
const html = fs.readFileSync(path.join(support, 'index.html'), 'utf8');
const home = fs.readFileSync(path.join(website, 'index.html'), 'utf8');
const address = html.match(/<input id="donation-address"[^>]* value="([^"]+)"/)?.[1];
assert.equal(address, 'TNuCpZCkeCppELxHfVS2hwMhdeYx9vbrv5');
assert.ok(html.includes(`href="https://tronscan.org/address/${address}/transactions"`));
const qrPath = html.match(/<img src="([^"]+qr\.png)"/)?.[1];
assert.ok(qrPath, 'The support page must include a local QR image');
const qr = fs.readFileSync(path.resolve(support, qrPath));
// This image was decoded against the public address above. Re-decode it before updating the hash.
assert.equal(createHash('sha256').update(qr).digest('hex'),
  '756e9e75190f3a827b565e9504d73a8bd1ab5a8356960aa6019f703ade3f41c5');
assert.match(home, /class="footer-support" href="\.\/support\/" data-language-link/);
assert.match(html, /rel="canonical" href="https:\/\/www\.desktoptools\.cc\/support\/"/);
for (const [, asset] of html.matchAll(/(?:href|src)="(\.\.?\/[^"]+)"/g)) {
  assert.ok(fs.existsSync(path.resolve(support, asset.split('?')[0])), `Missing support asset ${asset}`);
}
const site = fs.readFileSync(path.join(website, 'site.js'), 'utf8');
const translations = vm.runInNewContext(`(${site.match(/const translations = (\{[\s\S]*?\n\});/)[1]})`);
for (const [, key] of html.matchAll(/data-i18n(?:-aria)?="([^"]+)"/g)) {
  for (const [language, dictionary] of Object.entries(translations)) {
    assert.ok(dictionary[key], `Missing support translation ${key} for ${language}`);
  }
}

async function check(clipboard, fallback, expected, selectsAddress) {
  let click;
  let selected = false;
  const input = {
    value: address,
    focus() {},
    select() { selected = true; }
  };
  const button = {
    hidden: true,
    addEventListener(event, handler) { assert.equal(event, 'click'); click = handler; },
    focus() {}
  };
  const status = { dataset: {}, textContent: '' };
  const elements = { 'donation-address': input, 'copy-address': button, 'copy-status': status };
  const document = {
    documentElement: { lang: 'ru' },
    getElementById(id) { return elements[id]; },
    execCommand(command) {
      assert.equal(command, 'copy');
      assert.ok(selected, 'The fallback must select the public address before copying');
      return fallback;
    }
  };
  vm.runInNewContext(script, {
    document,
    navigator: { clipboard },
    translations: { ru: { addressCopied: 'Адрес скопирован', copyAddressManually: 'Скопируйте адрес вручную' } }
  });

  assert.equal(button.hidden, false);
  await click();
  assert.equal(status.dataset.i18n, expected);
  assert.equal(status.textContent, expected === 'addressCopied' ? 'Адрес скопирован' : 'Скопируйте адрес вручную');
  assert.equal(selected, selectsAddress);
}

(async () => {
  let copied;
  await check({ async writeText(value) { copied = value; } }, false, 'addressCopied', false);
  assert.equal(copied, address);
  await check({ async writeText() { throw new Error('Clipboard denied'); } }, true, 'addressCopied', true);
  await check(undefined, false, 'copyAddressManually', true);
  console.log('Website donation clipboard checks passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
