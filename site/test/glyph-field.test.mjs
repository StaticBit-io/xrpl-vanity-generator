import test from 'node:test';
import assert from 'node:assert/strict';
import { BASE58_ALPHABET, glyphLevel, glyphRows, initGlyphField, renderGlyphField } from '../src/js/glyph-field.mjs';

test('the alphabet is the XRPL base58 one', () => {
  assert.equal(new Set(BASE58_ALPHABET).size, 58);
  assert.doesNotMatch(BASE58_ALPHABET, /[0OIl]/);
});

test('the rows fill the grid with base58 characters only', () => {
  const rows = glyphRows(40, 6, 1);
  assert.equal(rows.length, 6);
  for (const row of rows) {
    assert.equal(row.length, 40);
    for (const character of row) assert.ok(BASE58_ALPHABET.includes(character), character);
  }
});

test('the rows are the same for a seed and differ between seeds', () => {
  assert.deepEqual(glyphRows(40, 6, 1), glyphRows(40, 6, 1));
  assert.notDeepEqual(glyphRows(40, 6, 1), glyphRows(40, 6, 2));
});

test('the stem of the r is lit and the corners are dim', () => {
  assert.ok(glyphLevel(0.38, 0.5) >= 5);
  assert.ok(glyphLevel(0.02, 0.02) <= 1);
  assert.ok(glyphLevel(0.95, 0.95) <= 1);
});

test('the rendered field is plain markup a page shows without scripts', () => {
  const html = renderGlyphField(40, 6, 1);
  assert.match(html, /^<svg class="glyph-field"[^>]* role="img" aria-label="[^"]+"/);
  assert.match(html, /<tspan class="g\d">/);
  const text = html.replace(/<[^>]+>/g, '');
  assert.equal(text.replace(/\n/g, ''), glyphRows(40, 6, 1).join(''));
  assert.equal((html.match(/<text\b/g) ?? []).length, 6);
});

test('under reduced motion the field is left alone', () => {
  let touched = false;
  const element = { querySelectorAll() { touched = true; return []; } };
  initGlyphField(element, { reducedMotion: true });
  assert.equal(touched, false);
});
