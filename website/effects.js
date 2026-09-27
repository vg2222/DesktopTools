// Decorative motion follows the pointer only; there is no permanent animation loop.
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
const finePointer = window.matchMedia('(pointer: fine)');
let glowFrame = 0;
let pointer = { x: 0, y: 0 };
const resetGlow = () => {
  cancelAnimationFrame(glowFrame);
  glowFrame = 0;
  document.body.style.removeProperty('--pointer-x');
  document.body.style.removeProperty('--pointer-y');
};
window.addEventListener('pointermove', event => {
  if (reducedMotion.matches || !finePointer.matches) return;
  pointer = { x: event.clientX, y: event.clientY };
  if (!glowFrame) glowFrame = requestAnimationFrame(() => {
    document.body.style.setProperty('--pointer-x', `${pointer.x}px`);
    document.body.style.setProperty('--pointer-y', `${pointer.y}px`);
    glowFrame = 0;
  });
});
reducedMotion.addEventListener('change', resetGlow);

// Track a narrow reading line below the sticky header, without scroll handlers.
const navigationLinks = [...document.querySelectorAll('.primary-nav a[href^="#"]')];
if (navigationLinks.length && typeof IntersectionObserver !== 'undefined') {
  const sections = [...document.querySelectorAll('main > section')];
  let activeSection = null;
  let navigationObserver;
  const updateNavigation = () => navigationLinks.forEach(link => {
    if (activeSection?.id && link.getAttribute('href') === `#${activeSection.id}`) {
      link.setAttribute('aria-current', 'location');
    } else link.removeAttribute('aria-current');
  });
  const observeNavigation = () => {
    navigationObserver?.disconnect();
    const readingLine = Math.min(160, window.innerHeight * .25);
    navigationObserver = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (entry.isIntersecting) activeSection = entry.target;
        else if (activeSection === entry.target) activeSection = null;
      }
      updateNavigation();
    }, { rootMargin: `-${readingLine}px 0px -${Math.max(0, window.innerHeight - readingLine - 1)}px 0px`, threshold: 0 });
    sections.forEach(section => navigationObserver.observe(section));
  };
  observeNavigation();
  window.addEventListener('resize', observeNavigation);
}

const playground = document.querySelector('.playground');
if (playground) {
  const surface = playground.querySelector('.playground-surface');
  const canvas = playground.querySelector('canvas');
  const context = canvas.getContext('2d');
  const clearButton = playground.querySelector('[data-demo-clear]');
  const modes = [...playground.querySelectorAll('[data-demo-mode]')];

  if (context) {
    playground.hidden = false;
    let mode = 'draw';
    let strokes = [];
    let activeStroke = null;
    let activePointer = null;
    let spot = { x: .5, y: .5 };
    let width = 1;
    let height = 1;
    const clamp = value => Math.min(1, Math.max(0, value));

    const refreshClear = () => {
      clearButton.disabled = strokes.length === 0 && (mode !== 'spotlight' || (spot.x === .5 && spot.y === .5));
    };
    const setSpot = point => {
      spot = { x: clamp(point.x), y: clamp(point.y) };
      surface.style.setProperty('--spot-x', `${spot.x * 100}%`);
      surface.style.setProperty('--spot-y', `${spot.y * 100}%`);
      refreshClear();
    };
    const paint = points => {
      if (!points.length) return;
      context.beginPath();
      context.moveTo(points[0].x * width, points[0].y * height);
      if (points.length === 1) context.lineTo(points[0].x * width + .1, points[0].y * height);
      for (let index = 1; index < points.length; index++) context.lineTo(points[index].x * width, points[index].y * height);
      context.stroke();
    };
    const redraw = () => {
      context.clearRect(0, 0, width, height);
      strokes.forEach(paint);
    };
    const resize = () => {
      const rect = surface.getBoundingClientRect();
      width = rect.width;
      height = rect.height;
      const scale = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = Math.round(width * scale);
      canvas.height = Math.round(height * scale);
      context.setTransform(scale, 0, 0, scale, 0, 0);
      context.strokeStyle = '#a5a4ff';
      context.lineWidth = 3;
      context.lineCap = 'round';
      context.lineJoin = 'round';
      context.shadowColor = '#7672db';
      context.shadowBlur = 7;
      redraw();
    };
    const finishStroke = () => {
      if (activePointer !== null && surface.hasPointerCapture(activePointer)) surface.releasePointerCapture(activePointer);
      activePointer = null;
      activeStroke = null;
    };
    const clear = () => {
      finishStroke();
      strokes = [];
      setSpot({ x: .5, y: .5 });
      redraw();
    };
    const newStroke = points => {
      // Bound memory when a visitor keeps drawing for a long time.
      if (strokes.length >= 40) strokes.shift();
      strokes.push(points);
      refreshClear();
      redraw();
    };
    const pointFromEvent = event => {
      const rect = surface.getBoundingClientRect();
      return { x: clamp((event.clientX - rect.left) / rect.width), y: clamp((event.clientY - rect.top) / rect.height) };
    };

    modes.forEach(button => button.addEventListener('click', () => {
      finishStroke();
      mode = button.dataset.demoMode;
      surface.dataset.mode = mode;
      modes.forEach(item => item.setAttribute('aria-pressed', String(item === button)));
      refreshClear();
    }));
    clearButton.addEventListener('click', clear);
    surface.addEventListener('pointerdown', event => {
      if (!event.isPrimary || event.button !== 0) return;
      event.preventDefault();
      surface.focus({ preventScroll: true });
      activePointer = event.pointerId;
      surface.setPointerCapture(event.pointerId);
      const point = pointFromEvent(event);
      if (mode === 'spotlight') setSpot(point);
      else {
        activeStroke = [point];
        newStroke(activeStroke);
      }
    });
    surface.addEventListener('pointermove', event => {
      if (!event.isPrimary) return;
      const point = pointFromEvent(event);
      if (mode === 'spotlight') setSpot(point);
      else if (activeStroke && activePointer === event.pointerId && activeStroke.length < 1500) {
        const previous = activeStroke[activeStroke.length - 1];
        activeStroke.push(point);
        paint([previous, point]);
      }
    });
    surface.addEventListener('pointerup', finishStroke);
    surface.addEventListener('pointercancel', finishStroke);
    surface.addEventListener('lostpointercapture', finishStroke);
    surface.addEventListener('keydown', event => {
      if (event.key === 'Escape') {
        event.preventDefault();
        clear();
      } else if (event.key === 'Enter') {
        event.preventDefault();
        const circle = Array.from({ length: 81 }, (_, index) => {
          const angle = index / 80 * Math.PI * 2;
          return { x: .5 + Math.cos(angle) * .38, y: .5 + Math.sin(angle) * .23 };
        });
        newStroke(circle);
      } else if (mode === 'spotlight' && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
        event.preventDefault();
        setSpot({ x: spot.x + (event.key === 'ArrowRight' ? .05 : event.key === 'ArrowLeft' ? -.05 : 0), y: spot.y + (event.key === 'ArrowDown' ? .05 : event.key === 'ArrowUp' ? -.05 : 0) });
      }
    });
    resize();
    if (typeof ResizeObserver !== 'undefined') new ResizeObserver(resize).observe(surface);
    else window.addEventListener('resize', resize);
  }
}
