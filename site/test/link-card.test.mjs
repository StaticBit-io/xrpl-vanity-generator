import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, statSync } from 'node:fs';
import { renderChrome } from '../chrome-markup.mjs';
import { BASE58_ALPHABET } from '../src/js/glyph-field.mjs';
import { BANNER_ADDRESS, BANNER_FILE, BANNER_HEIGHT, BANNER_WIDTH, BANNER_WORD, renderBanner } from '../tools/og-banner.mjs';
import { checksumValid } from './support/checksum.mjs';

const head = renderChrome('head', { pagePath: '/cli/', title: 'CLI', description: 'About the CLI.' });

test('every page offers the banner by its absolute URL', () => {
  assert.match(head, /<meta property="og:image" content="https:\/\/xrpl-vanity\.staticbit\.io\/og-banner\.png">/);
  assert.match(head, /<meta property="og:image:width" content="1200">/);
  assert.match(head, /<meta property="og:image:height" content="630">/);
  assert.match(head, /<meta property="og:image:alt" content="[^"]+">/);
});

test('the card asks for the large picture and names the site', () => {
  assert.match(head, /<meta name="twitter:card" content="summary_large_image">/);
  assert.match(head, /<meta property="og:site_name" content="XRPL Vanity">/);
});

test('the banner file is a PNG of the size the tags state', () => {
  const png = readFileSync(BANNER_FILE);
  assert.equal(png.subarray(1, 4).toString(), 'PNG');
  assert.equal(png.readUInt32BE(16), BANNER_WIDTH);
  assert.equal(png.readUInt32BE(20), BANNER_HEIGHT);
  assert.ok(statSync(BANNER_FILE).size < 1024 * 1024);
});

test('the banner address carries the brand and could exist as text', () => {
  assert.equal(BANNER_WORD, 'STATiCBiT');
  assert.ok(BANNER_ADDRESS.startsWith(`r${BANNER_WORD}`));
  assert.ok(BANNER_ADDRESS.length >= 25 && BANNER_ADDRESS.length <= 35);
  for (const character of BANNER_ADDRESS) assert.ok(BASE58_ALPHABET.includes(character), character);
});

test('a wallet would reject the banner address', () => {
  assert.equal(checksumValid(BANNER_ADDRESS), false);
});

test('the banner highlights the word and names the product', () => {
  const html = renderBanner();
  assert.ok(html.includes(`<mark>${BANNER_WORD}</mark>`));
  assert.match(html, />xrpl-vanity</);
});

test('the banner draws from the site tokens and its own font file', () => {
  const html = renderBanner();
  assert.match(html, /--color-accent/);
  assert.match(html, /martian-mono-5\.3\.0-latin\.woff2/);
  assert.doesNotMatch(html, /googleapis/);
});
