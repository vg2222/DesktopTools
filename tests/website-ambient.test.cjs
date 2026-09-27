const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname, '..', 'website', 'effects.js'), 'utf8');

function startEffects({ reduced = false, fine = true } = {}) {
  const windowEvents = new Map();
  const mediaEvents = new Map();
  const styleValues = new Map();
  const frames = [];
  const body = {
    style: {
      setProperty(name, value) { styleValues.set(name, value); },
      removeProperty(name) { styleValues.delete(name); }
    }
  };
  const motionPreference = {
    matches: reduced,
    addEventListener(name, listener) { mediaEvents.set(name, listener); }
  };
  const context = {
    document: {
      body,
      querySelector() { return null; },
      querySelectorAll() { return []; }
    },
    window: {
      matchMedia(query) { return query.includes('reduced-motion') ? motionPreference : { matches: fine }; },
      addEventListener(name, listener) { windowEvents.set(name, listener); }
    },
    requestAnimationFrame(callback) { frames.push(callback); return frames.length; },
    cancelAnimationFrame() {}
  };
  vm.runInNewContext(script, context);
  return { windowEvents, mediaEvents, styleValues, motionPreference, flushFrame() { frames.shift()?.(); } };
}

const active = startEffects();
assert.equal(typeof active.windowEvents.get('pointermove'), 'function', 'The glow tracks the whole page, not just the hero');
active.windowEvents.get('pointermove')({ clientX: 320, clientY: 240 });
active.flushFrame();
assert.equal(active.styleValues.get('--pointer-x'), '320px', 'Horizontal position uses viewport pixels');
assert.equal(active.styleValues.get('--pointer-y'), '240px', 'Vertical position uses viewport pixels');
active.windowEvents.get('pointermove')({ clientX: 500, clientY: 400 });
active.flushFrame();
assert.equal(active.styleValues.get('--pointer-x'), '500px', 'The glow follows later pointer movement');
active.motionPreference.matches = true;
active.mediaEvents.get('change')();
assert.equal(active.styleValues.size, 0, 'Enabling reduced motion clears the tracked position');

const reduced = startEffects({ reduced: true });
reduced.windowEvents.get('pointermove')({ clientX: 320, clientY: 240 });
assert.equal(reduced.styleValues.size, 0, 'Reduced motion does not track the pointer');

const coarse = startEffects({ fine: false });
coarse.windowEvents.get('pointermove')({ clientX: 320, clientY: 240 });
assert.equal(coarse.styleValues.size, 0, 'Touch pointers do not trigger the glow');

console.log('Website ambient pointer checks passed.');
