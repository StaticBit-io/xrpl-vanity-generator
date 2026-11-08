import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { ACTIVE_LANGS, DEFAULT_LANG, ENGLISH_ONLY, pathPrefix } from '../src/js/languages.mjs';
import { equivalentPath } from '../src/js/prefs.mjs';
import { CHROME_STRINGS } from '../chrome-strings.mjs';

// Every translated page is held to its English source page: the same structure, the same links and
// the same literal material. The sources are compared, not dist/, so no fresh build is needed.

const ENGLISH_PAGES = ['index.html', 'desktop/index.html', 'cli/index.html', 'support/index.html', '404.html', 'privacy/index.html'];
const TRANSLATED = ACTIVE_LANGS.filter((lang) => lang !== DEFAULT_LANG);

const sourcePath = (lang, page) => new URL(`../src${pathPrefix(lang)}/${page}`, import.meta.url);

/** The URL path of a source page: "cli/index.html" is "/cli/", "404.html" is "/404.html". */
function urlOf(page) {
  return `/${page.replace(/index\.html$/, '')}`;
}

const readPage = (lang, page) => readFileSync(sourcePath(lang, page), 'utf8');

/** The id attributes, in document order. */
function idsOf(html) {
  return [...html.matchAll(/\sid="([^"]*)"/g)].map((match) => match[1]);
}

/** The internal links (href starting with a slash), each mapped to its English equivalent. */
function internalLinksOf(html) {
  return [...html.matchAll(/\shref="(\/[^"]*)"/g)].map((match) => {
    const [path, hash] = match[1].split('#');
    return equivalentPath(path, DEFAULT_LANG) + (hash === undefined ? '' : `#${hash}`);
  });
}

/** The src of every img and script, in document order. */
function sourcesOf(html) {
  return [...html.matchAll(/<(?:img|script)\b[^>]*?\ssrc="([^"]*)"/g)].map((match) => match[1]);
}

/** The literal contents of every code and pre element, in document order. */
function literalsOf(html) {
  return [...html.matchAll(/<(code|pre)\b[^>]*>([\s\S]*?)<\/\1>/g)].map((match) => `${match[1]}:${match[2]}`);
}

/** The first difference between two lists, as a message, or null when they are equal. */
function firstDifference(label, english, translated) {
  if (english.length !== translated.length) return `${label}: ${english.length} in English, ${translated.length} translated`;
  const index = english.findIndex((item, position) => item !== translated[position]);
  return index === -1 ? null : `${label} #${index}: "${english[index]}" in English, "${translated[index]}" translated`;
}

const CHECKS = [
  ['ids', idsOf],
  ['internal links', internalLinksOf],
  ['img and script sources', sourcesOf],
  ['code and pre contents', literalsOf],
];

/** All structural differences between an English page and its translation. */
function differences(english, translated) {
  return CHECKS
    .map(([label, extract]) => firstDifference(label, extract(english), extract(translated)))
    .filter((message) => message !== null);
}

for (const lang of TRANSLATED) {
  test(`${lang}: every English page except the English-only ones exists`, () => {
    for (const page of ENGLISH_PAGES.filter((name) => !ENGLISH_ONLY.includes(urlOf(name)))) {
      assert.ok(existsSync(sourcePath(lang, page)), `${lang}/${page}`);
    }
    for (const page of ENGLISH_PAGES.filter((name) => ENGLISH_ONLY.includes(urlOf(name)))) {
      assert.ok(!existsSync(sourcePath(lang, page)), `${lang}/${page} must not exist`);
    }
  });

  for (const page of ENGLISH_PAGES.filter((name) => !ENGLISH_ONLY.includes(urlOf(name)))) {
    test(`${lang}/${page}: same ids, links, sources and literals as English`, () => {
      assert.deepEqual(differences(readPage(DEFAULT_LANG, page), readPage(lang, page)), []);
    });
  }

  test(`${lang}: the chrome strings have exactly English's keys`, () => {
    assert.ok(CHROME_STRINGS[lang], `CHROME_STRINGS.${lang}`);
    assert.deepEqual(keysOf(CHROME_STRINGS[lang]), keysOf(CHROME_STRINGS[DEFAULT_LANG]));
  });
}

/** The dotted paths of every string in a nested table, sorted. */
function keysOf(table, prefix = '') {
  return Object.entries(table)
    .flatMap(([key, value]) => (typeof value === 'object' && value !== null ? keysOf(value, `${prefix}${key}.`) : [`${prefix}${key}`]))
    .sort();
}

test('the English pages the comparison reads exist', () => {
  for (const page of ENGLISH_PAGES) assert.ok(existsSync(sourcePath(DEFAULT_LANG, page)), page);
  assert.ok(idsOf(readPage(DEFAULT_LANG, 'index.html')).includes('get'));
  assert.ok(sourcesOf(readPage(DEFAULT_LANG, 'index.html')).some((src) => src.endsWith('home.mjs')));
});

// Self-tests: each check must report a translated copy that drifts from the English one.

const ENGLISH = `<h1 id="top">Hi</h1><a href="/cli/">CLI</a><a href="/privacy/">P</a><a href="https://x.io">x</a>
<code>--gpu-duty</code><pre>run --out a.tsv</pre><img src="/og.png"><script type="module" src="/js/home.mjs"></script>`;

test('self-test: an identical structure with other words reports nothing', () => {
  const russian = ENGLISH.replace('Hi', 'Привет').replace('>CLI<', '>Командная строка<');
  assert.deepEqual(differences(ENGLISH, russian), []);
});

test('self-test: a language-prefixed internal link is read as its English equivalent', () => {
  assert.deepEqual(differences(ENGLISH, ENGLISH.replace('href="/cli/"', 'href="/ru/cli/"')), []);
  assert.deepEqual(internalLinksOf('<a href="/ru/desktop/#safe">d</a>'), ['/desktop/#safe']);
});

test('self-test: a missing id is reported', () => {
  assert.match(differences(ENGLISH, ENGLISH.replace(' id="top"', '')).join(), /ids/);
});

test('self-test: an extra internal link is reported', () => {
  assert.match(differences(ENGLISH, `${ENGLISH}<a href="/support/">s</a>`).join(), /internal links/);
});

test('self-test: a changed internal link target is reported', () => {
  assert.match(differences(ENGLISH, ENGLISH.replace('href="/cli/"', 'href="/desktop/"')).join(), /internal links/);
});

test('self-test: a changed code element is reported', () => {
  assert.match(differences(ENGLISH, ENGLISH.replace('--gpu-duty', '--gpu-pause')).join(), /code and pre/);
});

test('self-test: a changed pre element is reported', () => {
  assert.match(differences(ENGLISH, ENGLISH.replace('a.tsv', 'b.tsv')).join(), /code and pre/);
});

test('self-test: a changed img src is reported', () => {
  assert.match(differences(ENGLISH, ENGLISH.replace('/og.png', '/ru/og.png')).join(), /img and script/);
});

test('self-test: a missing script is reported', () => {
  assert.match(differences(ENGLISH, ENGLISH.replace(/<script[\s\S]*<\/script>/, '')).join(), /img and script/);
});

test('self-test: a chrome table with a missing key is caught by the key comparison', () => {
  assert.notDeepEqual(keysOf({ skip: 'a', nav: { cli: 'b' } }), keysOf({ skip: 'a', nav: { cli: 'b', privacy: 'c' } }));
  assert.deepEqual(keysOf({ nav: { cli: 'b' }, skip: 'a' }), keysOf({ skip: 'x', nav: { cli: 'y' } }));
});
