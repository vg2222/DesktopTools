const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname, '..', 'website', 'effects.js'), 'utf8');
const links = ['features', 'privacy', 'download'].map(id => {
  const attributes = new Map([['href', `#${id}`]]);
  return {
    getAttribute: name => attributes.get(name),
    setAttribute: (name, value) => attributes.set(name, value),
    removeAttribute: name => attributes.delete(name)
  };
});
const sections = ['', 'features', 'workflow', 'privacy', 'download'].map(id => ({ id }));
const observers = [];
const events = new Map();
const context = {
  document: {
    querySelector: () => null,
    querySelectorAll: selector => selector === '.primary-nav a[href^="#"]' ? links : selector === 'main > section' ? sections : []
  },
  window: {
    innerHeight: 900,
    matchMedia: () => ({ matches: true, addEventListener() {} }),
    addEventListener: (event, listener) => events.set(event, listener)
  },
  IntersectionObserver: class {
    constructor(callback, options) { this.callback = callback; this.options = options; this.targets = []; observers.push(this); }
    observe(target) { this.targets.push(target); }
    disconnect() { this.disconnected = true; }
  }
};
vm.runInNewContext(script, context);
assert.equal(observers.length, 1, 'Navigation must track the section in view even with reduced motion');
assert.equal(observers[0].targets.length, sections.length, 'Unlinked sections must also clear the highlight');
const current = () => links.filter(link => link.getAttribute('aria-current') === 'location').map(link => link.getAttribute('href'));
observers[0].callback([{ target: sections[1], isIntersecting: true }]);
assert.deepEqual(current(), ['#features'], 'Features becomes the current location');
observers[0].callback([{ target: sections[1], isIntersecting: false }, { target: sections[3], isIntersecting: true }]);
assert.deepEqual(current(), ['#privacy'], 'Scrolling changes the current link without duplicates');
observers[0].callback([{ target: sections[3], isIntersecting: false }, { target: sections[2], isIntersecting: true }]);
assert.deepEqual(current(), [], 'The workflow section does not leave a misleading nav highlight');
observers[0].callback([{ target: sections[4], isIntersecting: true }]);
assert.deepEqual(current(), ['#download']);
observers[0].callback([{ target: sections[4], isIntersecting: false }]);
assert.deepEqual(current(), [], 'Leaving a section clears the current location');
context.window.innerHeight = 600;
events.get('resize')();
assert.equal(observers[0].disconnected, true, 'Resizing disconnects the old observer');
assert.notEqual(observers[1].options.rootMargin, observers[0].options.rootMargin, 'The reading position adapts to viewport height');
vm.runInNewContext(script, { ...context, IntersectionObserver: undefined });
console.log('Website navigation checks passed.');
