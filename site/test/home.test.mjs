import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readdir, readFile, rm } from 'node:fs/promises';
import { readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from '../build.mjs';
import { BASE58_ALPHABET } from '../src/js/glyph-field.mjs';
import { initStore } from '../src/js/home.mjs';
import { REPOSITORY_URL, STORE_URL } from '../src/js/config.mjs';
import { checksumValid } from './support/checksum.mjs';
import { ACTIVE_LANGS, localeOf, pathPrefix } from '../src/js/languages.mjs';

const SITE_DIR = fileURLToPath(new URL('..', import.meta.url));
const REPO_ROOT = fileURLToPath(new URL('../..', import.meta.url));

const out = await mkdtemp(join(tmpdir(), 'xrpl-vanity-home-'));
await build({ siteDir: SITE_DIR, outDir: out });
const html = await readFile(join(out, 'index.html'), 'utf8');
test.after(() => rm(out, { recursive: true, force: true }));

async function allBuiltHtml() {
  const entries = await readdir(out, { recursive: true });
  const pages = entries.filter((name) => name.endsWith('.html'));
  return (await Promise.all(pages.map((name) => readFile(join(out, name), 'utf8')))).join('\n');
}

const textOf = (markup) => markup.replace(/<[^>]+>/g, '').replace(/&amp;/g, '&');

test('the checksum helper knows a real address from a broken one', () => {
  const genesis = 'rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh';
  assert.equal(checksumValid(genesis), true);
  assert.equal(checksumValid(`${genesis.slice(0, -1)}z`), false);
});

test('the Store buttons ship disabled and tagged soon', () => {
  const buttons = [...html.matchAll(/<button\b[^>]*data-store[^>]*>[\s\S]*?<\/button>/g)].map((m) => m[0]);
  assert.ok(buttons.length >= 1);
  for (const button of buttons) {
    assert.match(button, /\sdisabled\b/);
    assert.match(button, /soon/);
    assert.match(button, /Microsoft Store/);
  }
  assert.equal(STORE_URL, null);
});

test('no link pretends to be a download', () => {
  assert.doesNotMatch(html, /apps\.microsoft\.com|microsoft\.com\/store|\/releases/);
});

function fakeDocument() {
  const replaced = [];
  const button = {
    className: 'btn',
    dataset: { store: '' },
    querySelector: () => ({ textContent: 'Get it from Microsoft Store' }),
    replaceWith: (node) => replaced.push(node),
  };
  return {
    replaced,
    querySelectorAll: () => [button],
    createElement: (tag) => ({ tag }),
  };
}

test('a Store URL turns the button into a plain link without the tag', () => {
  const document = fakeDocument();
  initStore(document, 'https://apps.example/x');
  assert.equal(document.replaced.length, 1);
  const [link] = document.replaced;
  assert.equal(link.tag, 'a');
  assert.equal(link.href, 'https://apps.example/x');
  assert.equal(link.className, 'btn');
  assert.equal(link.textContent, 'Get it from Microsoft Store');
});

test('no Store URL changes nothing', () => {
  const document = fakeDocument();
  initStore(document, null);
  assert.deepEqual(document.replaced, []);
});

test('the example addresses look real and a wallet would reject them', () => {
  const addresses = [...html.matchAll(/data-example-address[^>]*>([\s\S]*?)<\/(?:span|code)>/g)].map((m) => textOf(m[1]));
  assert.ok(addresses.length >= 2);
  for (const address of addresses) {
    assert.ok(address.length >= 25 && address.length <= 35, address);
    assert.ok(address.startsWith('r'), address);
    for (const character of address) assert.ok(BASE58_ALPHABET.includes(character), `${address}: ${character}`);
    assert.equal(checksumValid(address), false, address);
  }
  assert.match(textOf(html), /no wallet will accept/i);
});

test('no seed-shaped string is published', async () => {
  assert.doesNotMatch(await allBuiltHtml(), /\bsEd[1-9A-HJ-NP-Za-km-z]{20,}/);
});

test('the four answers appear in their order', () => {
  const order = ['id="get"', 'id="safe"', 'id="runs-on"'].map((marker) => html.indexOf(marker));
  assert.ok(order.every((position) => position > 0), order.join());
  assert.deepEqual(order, [...order].sort((a, b) => a - b));
});

test('the limits are stated plainly', () => {
  const text = textOf(html);
  for (const phrase of ['Ed25519', 'secp256k1', 'Windows 10 version 1809', 'WebView2', '.NET 10 SDK', 'i5-11400F', 'RTX 5070 Ti']) {
    assert.ok(text.includes(phrase), phrase);
  }
  assert.doesNotMatch(text, /macOS/);
});

test('the OpenCL row states only what was verified', () => {
  assert.match(textOf(html), /Verified on NVIDIA\./);
  assert.doesNotMatch(textOf(html), /AMD|untested/);
});

test('the source is linked', () => {
  assert.ok(html.includes(`href="${REPOSITORY_URL}"`));
});

test('every figure cites a file that holds it', () => {
  const figures = [...html.matchAll(/data-source="([^"]+)" data-value="([^"]+)"/g)];
  assert.deepEqual(figures.map((m) => m[2]).sort(), ['118,885', '32,211,960', '32,375,668']);
  for (const [, source, value] of figures) {
    assert.ok(readFileSync(join(REPO_ROOT, source), 'utf8').includes(value), `${source}: ${value}`);
  }
});

test('the field exists without scripts and a screen reader gets one label for it', () => {
  assert.match(html, /<svg class="glyph-field"[^>]* role="img" aria-label="[^"]+"/);
});

test('the stylesheet opens with the stamp and keeps long strings in their box', () => {
  const css = readFileSync(join(SITE_DIR, 'src', 'css', 'site.css'), 'utf8');
  assert.match(css.trimStart(), /^\/\* Hallmark/);
  assert.match(css, /\.addr\s*{[^}]*overflow-wrap: anywhere/);
  assert.match(css, /prefers-reduced-motion: reduce/);
});

test('the hero stays within the word budget', () => {
  const head = textOf(/<h1>([\s\S]*?)<\/h1>/.exec(html)[1]);
  assert.ok(head.trim().split(/\s+/).length <= 7, head);
  const lead = textOf(/<p class="lead">([\s\S]*?)<\/p>/.exec(html)[1]);
  assert.ok(lead.trim().split(/\s+/).length <= 12, lead);
});

test('the copy has no long dash', () => {
  assert.doesNotMatch(textOf(html), /—/);
});

const safeSection = /<section id="safe"[\s\S]*?<\/section>/.exec(html)[0];

test('encryption is claimed for the desktop app only, and the CLI is named for what it does', () => {
  assert.doesNotMatch(safeSection, /Encrypted for your Windows account/);
  assert.match(safeSection, /<dt>[^<]*desktop app[^<]*<\/dt><dd>[^<]*encrypted for your Windows account/i);
  assert.match(textOf(safeSection), /CLI prints seeds in plain text/);
});

test('the clipboard is not promised clean', () => {
  assert.doesNotMatch(textOf(html), /clipboard cleared|wiped from the clipboard/i);
});

test('CUDA names the oldest cards it runs on', () => {
  assert.match(textOf(html), /Turing \(2018\) or newer/);
});

test('several physical cards are not claimed as tested', () => {
  assert.doesNotMatch(textOf(html), /several cards/i);
  assert.match(textOf(html), /several devices/i);
});

/** The number a language writes, read with that language's group and decimal separators. */
function parseLocalized(text, lang) {
  const parts = new Intl.NumberFormat(localeOf(lang)).formatToParts(1234567.5);
  const group = parts.find((part) => part.type === 'group')?.value ?? ',';
  const decimal = parts.find((part) => part.type === 'decimal').value;
  const stripped = text
    .trim()
    .split(group).join('')
    .replace(/[\u00a0\u202f\s]/g, '')
    .replace(decimal, '.');
  return Number(stripped);
}

/** The figures of a home page: the written number, and the English-formatted value the repository file holds. */
function figuresOf(markup) {
  return [...markup.matchAll(/<span data-source="([^"]+)" data-value="([^"]+)">([^<]*)<\/span>/g)]
    .map((match) => ({ source: match[1], value: match[2], written: match[3] }));
}

test('the number reader follows the language separators', () => {
  assert.equal(parseLocalized('118,885', 'en'), 118885);
  assert.equal(parseLocalized('32 375 668', 'ru'), 32375668);
  assert.equal(parseLocalized('32\u00a0375\u00a0668', 'ru'), 32375668);
  assert.equal(parseLocalized('32.375.668', 'de'), 32375668);
  assert.equal(parseLocalized('1,5', 'de'), 1.5);
  assert.notEqual(parseLocalized('118,885', 'de'), 118885);
});

for (const lang of ACTIVE_LANGS) {
  test(`${lang}: every speed on the home page is the number docs/benchmarks.md holds`, async () => {
    const page = await readFile(join(out, ...pathPrefix(lang).split('/').filter(Boolean), 'index.html'), 'utf8');
    const figures = figuresOf(page);
    assert.equal(figures.length, 3);
    for (const { source, value, written } of figures) {
      const text = readFileSync(join(REPO_ROOT, source), 'utf8');
      assert.ok(text.includes(value), `${source}: ${value}`);
      assert.equal(parseLocalized(written, lang), parseLocalized(value, 'en'), `${lang}: ${written}`);
    }
  });
}
