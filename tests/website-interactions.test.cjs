const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname, '..', 'website', 'site.js'), 'utf8');
const translations = vm.runInNewContext(`(${script.match(/const translations = (\{[\s\S]*?\n\});/)[1]})`);
let document;

function node(dataset = {}) {
  const attributes = new Map();
  const listeners = new Map();
  const classes = new Set();
  return {
    dataset,
    attributes,
    listeners,
    classList: {
      add(value) { classes.add(value); },
      contains(value) { return classes.has(value); },
      toggle(value, enabled) { if (enabled) classes.add(value); else classes.delete(value); }
    },
    textContent: '',
    value: '',
    setAttribute(name, value) { attributes.set(name, String(value)); },
    getAttribute(name) { return attributes.get(name) ?? null; },
    addEventListener(name, listener) { listeners.set(name, listener); }
  };
}

const selector = node();
const meta = node();
const navText = node({ i18n: 'navFeatures' });
const navAria = node({ i18nAria: 'mainNav' });
const screenshot = node({ i18nAlt: 'shotHome' });
const hero = node({ i18n: 'heroBody' });
const header = node();
const supportLink = { href: 'http://localhost:8765/support/?ref=home#main' };
const reveal = node();
const walletNote = node({ coin: 'USDT', network: 'Tron (TRC-20)' });

document = {
  documentElement: { lang: '' },
  title: '',
  getElementById(id) { return id === 'language' ? selector : null; },
  querySelector(selectorText) {
    if (selectorText === 'meta[name="description"]') return meta;
    if (selectorText === '.site-header') return header;
    return null;
  },
  querySelectorAll(selectorText) {
    if (selectorText === '[data-i18n]') return [navText, hero];
    if (selectorText === '[data-i18n-alt]') return [screenshot];
    if (selectorText === '[data-i18n-aria]') return [navAria];
    if (selectorText === '[data-reveal]') return [reveal];
    if (selectorText === '[data-i18n-wallet]') return [walletNote];
    if (selectorText === '[data-language-link]') return [supportLink];
    return [];
  }
};

const stored = new Map();
let lastUrl;
const window = {
  location: { search: '?lang=en', href: 'http://localhost:8765/?lang=en' },
  scrollY: 0,
  listeners: [],
  addEventListener(name, listener, options) {
    assert.ok(name === 'scroll' || name === 'resize', 'Unexpected window listener ' + name);
    if (name === 'scroll') assert.equal(options?.passive, true, 'Scroll handlers must be passive');
    this.listeners.push([name, listener]);
  },
  onScroll() { for (const [name, listener] of this.listeners) if (name === 'scroll') listener(); },
  matchMedia() { return { matches: true }; }
};
const context = {
  document,
  window,
  URL,
  URLSearchParams,
  navigator: { language: 'en-US' },
  requestAnimationFrame(callback) { callback(); },
  IntersectionObserver: class {
    constructor() { assert.fail('Reduced motion must not initialize reveal observers'); }
  },
  localStorage: { getItem(key) { return stored.get(key) ?? null; }, setItem(key, value) { stored.set(key, value); } },
  history: { replaceState(_state, _unused, url) { lastUrl = String(url); } }
};
vm.runInNewContext(script, context);

assert.equal(walletNote.textContent, 'Send only USDT on the Tron (TRC-20) network.');
assert.equal(navText.textContent, 'Features', 'Initial navigation is translated');
assert.equal(navAria.getAttribute('aria-label'), 'Main navigation', 'Initial accessible label is translated');
assert.equal(reveal.classList.contains('reveal-ready'), false, 'Reduced motion leaves content visible');
assert.equal(header.classList.contains('is-scrolled'), false, 'Header starts at the top');
window.scrollY = 12;
window.onScroll();
assert.equal(header.classList.contains('is-scrolled'), true, 'Scrolled header updates');
window.scrollY = 0;
window.onScroll();
assert.equal(header.classList.contains('is-scrolled'), false, 'Returning to the top restores the header');

selector.value = 'ru';
selector.listeners.get('change')({ target: selector });
assert.equal(navText.textContent, 'Функции', 'Navigation text updates with language');
assert.equal(navAria.getAttribute('aria-label'), 'Основная навигация', 'Accessible labels update with language');
assert.equal(walletNote.textContent, 'Отправляйте только USDT через сеть Tron (TRC-20).', 'Wallet warnings change language without changing chain or coin');
assert.equal(document.documentElement.lang, 'ru', 'Document language updates');
assert.match(document.title, /Инструменты для рабочего стола Windows/, 'Page title updates');
assert.match(meta.content, /локальная обработка/, 'Search description updates');
assert.equal(stored.get('desktoptools-site-language'), 'ru', 'Language preference is saved');
assert.match(lastUrl, /\?lang=ru$/, 'Language URL updates');
assert.equal(screenshot.alt, translations.ru.shotHome, 'Screenshot alt text updates in Russian');
assert.equal(supportLink.href, 'http://localhost:8765/support/?ref=home&lang=ru#main', 'Support links retain language, other parameters and anchors');

for (const language of ['de', 'fr', 'es', 'en']) {
  selector.value = language;
  selector.listeners.get('change')({ target: selector });
  const copy = translations[language];
  assert.equal(hero.textContent, copy.heroBody, 'New hero copy changes to ' + language);
  assert.equal(screenshot.alt, copy.shotHome, 'Screenshot alt text changes to ' + language);
  assert.equal(navAria.getAttribute('aria-label'), copy.mainNav);
  assert.equal(document.title, copy.pageTitle);
  assert.equal(meta.content, copy.description);
  assert.equal(walletNote.textContent, copy.sendOnly.replace('{coin}', 'USDT').replace('{network}', 'Tron (TRC-20)'));
  const url = new URL(supportLink.href);
  assert.equal(url.searchParams.get('lang'), language === 'en' ? null : language);
}
assert.equal(supportLink.href, 'http://localhost:8765/support/?ref=home#main', 'English removes only the language parameter');
assert.equal(new URL(lastUrl).searchParams.has('lang'), false);

document.documentElement.dataset = { page: 'support' };
selector.listeners.get('change')({ target: { value: 'fr' } });
assert.equal(document.title, translations.fr.supportTitle, 'Support keeps its own page title');
assert.equal(meta.content, translations.fr.supportDescription, 'Support keeps its own description');
document.documentElement.dataset = {};

// A browser without IntersectionObserver must keep content visible even when motion is allowed.
vm.runInNewContext(script, {
  ...context,
  IntersectionObserver: undefined,
  window: { ...window, matchMedia() { return { matches: false }; } }
});
assert.equal(reveal.classList.contains('reveal-ready'), false);

let revealObserver;
class MockObserver {
  constructor(callback) { this.callback = callback; revealObserver = this; }
  observe(element) { this.observed = element; }
  unobserve(element) { this.unobserved = element; }
}
const animatedContext = {
  ...context,
  window: { ...window, matchMedia() { return { matches: false }; } },
  IntersectionObserver: MockObserver
};
vm.runInNewContext(script, animatedContext);
assert.equal(reveal.classList.contains('reveal-ready'), true, 'Reveal targets are prepared when motion is allowed');
assert.equal(revealObserver.observed, reveal, 'Reveal targets are observed');
revealObserver.callback([{ isIntersecting: false, target: reveal }]);
assert.equal(reveal.classList.contains('is-visible'), false, 'Offscreen content waits for intersection');
assert.equal(revealObserver.unobserved, undefined, 'Offscreen targets stay observed');
revealObserver.callback([{ isIntersecting: true, target: reveal }]);
assert.equal(reveal.classList.contains('is-visible'), true, 'Visible sections are revealed');
assert.equal(revealObserver.unobserved, reveal, 'Revealed sections are unobserved');

console.log('Website interaction checks passed.');
