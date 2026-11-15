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
assert.ok(fs.existsSync(path.join(support, 'wallets.js')), 'Wallet cards need one data-driven configuration');
const config = fs.readFileSync(path.join(support, 'wallets.js'), 'utf8');
const wallets = vm.runInNewContext(config + '\nwallets');
assert.ok(wallets.length > 0, 'Keep a verified receiving wallet available');
for (const wallet of wallets) {
  for (const field of ['coin', 'network', 'address', 'explorerUrl', 'qr']) assert.ok(wallet[field], `Missing wallet ${field}`);
  assert.ok(wallet.explorerUrl.startsWith('https://'));
  assert.ok(wallet.explorerUrl.includes(wallet.address));
  for (const asset of [wallet.qr, wallet.coinIcon, wallet.networkIcon].filter(Boolean)) assert.ok(fs.existsSync(path.resolve(support, asset)), `Missing wallet asset ${asset}`);
}
const address = html.match(/<input id="donation-address"[^>]* value="([^"]+)"/)?.[1];
assert.equal(address, 'TNuCpZCkeCppELxHfVS2hwMhdeYx9vbrv5');
assert.ok(html.includes(`href="https://tronscan.org/address/${address}/transactions"`));
const qrPath = html.match(/<img[^>]* src="([^"]+qr\.png)"/)?.[1];
assert.ok(qrPath, 'The support page must include a local QR image');
assert.equal(wallets[0].address, address, 'No-JS address must match the config');
assert.equal(wallets[0].qr, qrPath, 'No-JS QR must match the config');
const qr = fs.readFileSync(path.resolve(support, qrPath));
// This image was decoded against the public address above. Re-decode it before updating the hash.
assert.equal(createHash('sha256').update(qr).digest('hex'),
  '756e9e75190f3a827b565e9504d73a8bd1ab5a8356960aa6019f703ade3f41c5');
assert.match(home, /class="footer-support" href="\.\/support\/" data-language-link/);
assert.match(html, /rel="canonical" href="https:\/\/www\.desktoptools\.cc\/support\/"/);
for (const [, asset] of html.matchAll(/(?:href|src)="(\.\.?\/[^"]+)"/g)) {
  assert.ok(fs.existsSync(path.resolve(support, asset.split(/[?#]/)[0])), `Missing support asset ${asset}`);
}
assert.match(html, /wallets\.js[\s\S]*render-wallets\.js[\s\S]*site\.js[\s\S]*copy\.js/, 'Render before translating and wiring clipboard handlers');
const site = fs.readFileSync(path.join(website, 'site.js'), 'utf8');
const translations = vm.runInNewContext(`(${site.match(/const translations = (\{[\s\S]*?\n\});/)[1]})`);
for (const [, key] of html.matchAll(/data-i18n(?:-aria)?="([^"]+)"/g)) {
  for (const [language, dictionary] of Object.entries(translations)) {
    assert.ok(dictionary[key], `Missing support translation ${key} for ${language}`);
  }
}

// Exercise the real renderer with two entries to catch shared IDs and omitted fields.
const renderScript = fs.readFileSync(path.join(support, 'render-wallets.js'), 'utf8');
function renderNode() { return { dataset: {}, attributes: {}, setAttribute(k, v) { this.attributes[k] = v; } }; }
function makeCard() {
  const nodes = new Map();
  return {
    ...renderNode(),
    querySelector(selector) { if (!nodes.has(selector)) nodes.set(selector, renderNode()); return nodes.get(selector); },
    cloneNode() { return makeCard(); }
  };
}
let rendered;
const list = { querySelector() { return makeCard(); }, replaceChildren(...cards) { rendered = cards; } };
vm.runInNewContext(renderScript, { wallets: [wallets[0], wallets[0]], document: { getElementById() { return list; } } });
assert.equal(rendered.length, 2);
const ids = new Set();
for (const card of rendered) {
  assert.equal(card.querySelector('input').value, address);
  assert.equal(card.querySelector('[data-wallet-coin]').textContent, wallets[0].coin);
  assert.equal(card.querySelector('[data-wallet-network]').textContent, wallets[0].network);
  assert.equal(card.querySelector('.donation-explorer').href, wallets[0].explorerUrl);
  assert.equal(card.querySelector('[data-wallet-qr]').src, wallets[0].qr);
  assert.equal(card.querySelector('[data-i18n-wallet]').dataset.network, wallets[0].network);
  assert.equal(card.querySelector('label').htmlFor, card.querySelector('input').id);
  for (const selector of ['input', 'button', '[role="status"]', '[data-wallet-coin]', '[data-i18n-wallet]']) {
    const id = card.querySelector(selector).id;
    assert.ok(id && !ids.has(id), 'Every card needs unique accessible IDs');
    ids.add(id);
  }
}
for (const key of ['helpStar', 'helpIssues', 'helpTranslate', 'helpShare']) assert.ok(html.includes('data-i18n="' + key + '"'));
for (const dictionary of Object.values(translations)) {
  assert.ok(dictionary.sendOnly.includes('{coin}') && dictionary.sendOnly.includes('{network}'));
}

async function check(clipboard, fallback, expected, selectsAddress) {
  let click;
  let selected = false;
  const input = {
    value: address,
    focus() {},
    select() { selected = true; }
  };
  const label = { dataset: {}, textContent: '' };
  const button = {
    hidden: true,
    addEventListener(event, handler) { assert.equal(event, 'click'); click = handler; },
    focus() {},
    querySelector() { return label; }
  };
  const status = { dataset: {}, textContent: '' };
  const elements = { 'donation-address': input, 'copy-address': button, 'copy-status': status };
  const document = {
    documentElement: { lang: 'ru' },
    getElementById(id) { return elements[id]; },
    querySelectorAll() { return [{ querySelector(selector) { return selector === 'input' ? input : selector === 'button' ? button : status; } }]; },
    execCommand(command) {
      assert.equal(command, 'copy');
      assert.ok(selected, 'The fallback must select the public address before copying');
      return fallback;
    }
  };
  vm.runInNewContext(script, {
    document,
    navigator: { clipboard },
    translations: { ru: { addressCopied: 'Адрес скопирован', copyAddressManually: 'Скопируйте адрес вручную', copyAddress: 'Скопировать адрес' } }
  });

  assert.equal(button.hidden, false);
  await click();
  assert.equal(status.dataset.i18n, expected);
  assert.equal(status.textContent, expected === 'addressCopied' ? 'Адрес скопирован' : 'Скопируйте адрес вручную');
  assert.equal(selected, selectsAddress);
  assert.equal(label.dataset.i18n, expected === 'addressCopied' ? 'addressCopied' : 'copyAddress');
}

(async () => {
  let copied;
  await check({ async writeText(value) { copied = value; } }, false, 'addressCopied', false);
  assert.equal(copied, address);
  await check({ async writeText() { throw new Error('Clipboard denied'); } }, true, 'addressCopied', true);
  await check(undefined, false, 'copyAddressManually', true);
  console.log('Website donation clipboard checks passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
