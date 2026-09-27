const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname, '..', 'website', 'site.js'), 'utf8');
let document;

function node(dataset = {}) {
  const attributes = new Map();
  const listeners = new Map();
  const classes = new Set();
  return {
    dataset,
    attributes,
    listeners,
    classList: { add(value) { classes.add(value); }, contains(value) { return classes.has(value); } },
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
const reveal = node();

document = {
  documentElement: { lang: '' },
  title: '',
  getElementById(id) { return id === 'language' ? selector : null; },
  querySelector(selectorText) {
    if (selectorText === 'meta[name="description"]') return meta;
    return null;
  },
  querySelectorAll(selectorText) {
    if (selectorText === '[data-i18n]') return [navText];
    if (selectorText === '[data-i18n-aria]') return [navAria];
    if (selectorText === '[data-reveal]') return [reveal];
    return [];
  }
};

const stored = new Map();
let lastUrl;
const window = {
  location: { search: '?lang=en', href: 'http://localhost:8765/?lang=en' },
  matchMedia() { return { matches: true }; }
};
const context = {
  document,
  window,
  URL,
  URLSearchParams,
  navigator: { language: 'en-US' },
  localStorage: { getItem(key) { return stored.get(key) ?? null; }, setItem(key, value) { stored.set(key, value); } },
  history: { replaceState(_state, _unused, url) { lastUrl = String(url); } }
};
vm.runInNewContext(script, context);

assert.equal(navText.textContent, 'Features', 'Initial navigation is translated');
assert.equal(navAria.getAttribute('aria-label'), 'Main navigation', 'Initial accessible label is translated');
assert.equal(reveal.classList.contains('reveal-ready'), false, 'Reduced motion leaves content visible');

selector.value = 'ru';
selector.listeners.get('change')({ target: selector });
assert.equal(navText.textContent, 'Функции', 'Navigation text updates with language');
assert.equal(navAria.getAttribute('aria-label'), 'Основная навигация', 'Accessible labels update with language');
assert.equal(document.documentElement.lang, 'ru', 'Document language updates');
assert.match(document.title, /Инструменты для рабочего стола Windows/, 'Page title updates');
assert.match(meta.content, /локальная обработка/, 'Search description updates');
assert.equal(stored.get('desktoptools-site-language'), 'ru', 'Language preference is saved');
assert.match(lastUrl, /\?lang=ru$/, 'Language URL updates');

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
revealObserver.callback([{ isIntersecting: true, target: reveal }]);
assert.equal(reveal.classList.contains('is-visible'), true, 'Visible sections are revealed');
assert.equal(revealObserver.unobserved, reveal, 'Revealed sections are unobserved');

console.log('Website interaction checks passed.');
