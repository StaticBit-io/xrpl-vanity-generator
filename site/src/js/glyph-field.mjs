// The hero's picture: the letter every XRP Ledger address starts with, printed in the address alphabet.
// The build writes it into the page as inline SVG, so it needs no script; a script only resolves it
// from noise once. All of this is decoration: nothing here touches a key or looks for an address.

export const BASE58_ALPHABET = 'rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz';

const RESOLVE_MS = 1500;
// One cell of the picture in SVG units: a Martian Mono glyph at the theme's narrowest width, with its line.
const CELL_WIDTH = 10;
const CELL_HEIGHT = 17;

function randomFrom(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Brightness 0 to 7 of the point (u, v) of the picture, both in 0..1: a lowercase r on a dark ground. */
export function glyphLevel(u, v) {
  const inStem = u >= 0.3 && u <= 0.42 && v >= 0.22 && v <= 0.82;
  const radius = Math.hypot(u - 0.62, v - 0.46);
  const inArm = u >= 0.42 && v <= 0.4 && radius >= 0.13 && radius <= 0.24;
  if (inStem || inArm) return 6;
  const nearStem = u >= 0.285 && u <= 0.435 && v >= 0.2 && v <= 0.84;
  const nearArm = u >= 0.42 && v <= 0.42 && radius >= 0.115 && radius <= 0.255;
  return nearStem || nearArm ? 3 : 0;
}

/** @returns {string[]} `rows` strings of `cols` base58 characters, the same for the same seed. */
export function glyphRows(cols, rows, seed) {
  const next = randomFrom(seed);
  return Array.from({ length: rows }, () => Array.from({ length: cols }, () => BASE58_ALPHABET[Math.floor(next() * 58)]).join(''));
}

/** The field as SVG text: one tspan per run of equal brightness, a stray lit cell here and there in the dark. */
export function renderGlyphField(cols, rows, seed) {
  const noise = randomFrom(seed ^ 0x5bd1e995);
  const width = cols * CELL_WIDTH;
  const lines = glyphRows(cols, rows, seed).map((text, y) => {
    let html = '';
    let run = '';
    let runLevel = -1;
    const flush = () => {
      html += `<tspan class="g${runLevel}">${run}</tspan>`;
      run = '';
    };
    [...text].forEach((character, x) => {
      const base = glyphLevel((x + 0.5) / cols, (y + 0.5) / rows);
      const roll = noise();
      const level = base === 0 ? (roll > 0.94 ? 2 : roll > 0.55 ? 1 : 0) : Math.min(7, base + (roll > 0.7 ? 1 : 0));
      if (level !== runLevel && run !== '') flush();
      run += character;
      runLevel = level;
    });
    flush();
    return `<text y="${(y + 1) * CELL_HEIGHT}" textLength="${width}">${html}</text>`;
  });
  return `<svg class="glyph-field" viewBox="0 0 ${width} ${rows * CELL_HEIGHT + 4}" role="img" aria-label="A lowercase r printed in XRP Ledger address characters">${lines.join('\n')}</svg>`;
}

/** Resolves the printed field row by row from noise, once. Under reduced motion it stays as printed. */
export function initGlyphField(element, { reducedMotion }) {
  if (reducedMotion) return;
  const runs = [...element.querySelectorAll('tspan')].map((span) => ({ span, text: span.textContent }));
  const noise = (length) => Array.from({ length }, () => BASE58_ALPHABET[Math.floor(Math.random() * 58)]).join('');
  const start = performance.now();
  const frame = (now) => {
    const done = Math.min(1, (now - start) / RESOLVE_MS);
    runs.forEach(({ span, text }, index) => {
      span.textContent = index / runs.length < done ? text : noise(text.length);
    });
    if (done < 1) requestAnimationFrame(frame);
  };
  requestAnimationFrame(frame);
}
