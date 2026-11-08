import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { ACTIVE_LANGS } from '../src/js/languages.mjs';
import { LANG_KEY } from '../src/js/prefs.mjs';
import { renderChrome } from '../chrome-markup.mjs';

const SOURCE = readFileSync(new URL('../src/js/lang-redirect.js', import.meta.url), 'utf8');
const ACTIVE_RE = /const ACTIVE = \[[^\]]*\];/;
const KEY_RE = /const KEY = '[^']*';/;

function memoryStorage(initial = {}) {
  const data = new Map(Object.entries(initial));
  return {
    getItem: (key) => (data.has(key) ? data.get(key) : null),
    setItem: (key, value) => { data.set(key, String(value)); },
    removeItem: (key) => { data.delete(key); },
    data,
  };
}

// Runs the production script with the active list replaced, as production has only English active.
function run({ path = '/', search = '', hash = '', languages = ['ru'], storage = memoryStorage(), active = ['en', 'ru', 'de'], throwing = false } = {}) {
  const replaced = [];
  const sandbox = {
    location: { pathname: path, search, hash, replace: (url) => replaced.push(url) },
    navigator: { languages, language: languages[0] },
  };
  if (throwing) Object.defineProperty(sandbox, 'localStorage', { get() { throw new Error('blocked'); } });
  else sandbox.localStorage = storage;
  const source = SOURCE.replace(ACTIVE_RE, `const ACTIVE = ${JSON.stringify(active)};`);
  assert.notEqual(source, SOURCE);
  vm.runInNewContext(source, sandbox);
  return { replaced, storage };
}

test('redirects / to /ru/ for a ru browser with empty storage', () => {
  const { replaced, storage } = run();
  assert.deepEqual(replaced, ['/ru/']);
  assert.equal(storage.data.size, 0);
});

test('redirects /desktop/ to /de/desktop/', () => {
  assert.deepEqual(run({ path: '/desktop/', languages: ['de-AT', 'en'] }).replaced, ['/de/desktop/']);
});

test('never redirects /de/ or /privacy/', () => {
  assert.deepEqual(run({ path: '/de/' }).replaced, []);
  assert.deepEqual(run({ path: '/de/desktop/' }).replaced, []);
  assert.deepEqual(run({ path: '/privacy/' }).replaced, []);
});

test('never redirects a page that is not one of the English pages', () => {
  assert.deepEqual(run({ path: '/no-such-page/' }).replaced, []);
});

test('never redirects when storage throws', () => {
  assert.deepEqual(run({ throwing: true }).replaced, []);
  const failing = { getItem() { throw new Error('x'); }, setItem() { throw new Error('x'); }, removeItem() {} };
  assert.deepEqual(run({ storage: failing }).replaced, []);
});

test('never redirects when storage cannot be written', () => {
  const readOnly = { getItem: () => null, setItem() { throw new Error('quota'); }, removeItem() {} };
  assert.deepEqual(run({ storage: readOnly }).replaced, []);
});

test('never redirects when en is stored', () => {
  assert.deepEqual(run({ storage: memoryStorage({ [LANG_KEY]: 'en' }) }).replaced, []);
  assert.deepEqual(run({ storage: memoryStorage({ [LANG_KEY]: 'ru' }) }).replaced, []);
});

test('keeps search and hash', () => {
  assert.deepEqual(run({ path: '/cli/', search: '?a=1', hash: '#install' }).replaced, ['/ru/cli/?a=1#install']);
});

test('a reader whose first matching language is English stays on English', () => {
  assert.deepEqual(run({ languages: ['en-US', 'ru'] }).replaced, []);
  assert.deepEqual(run({ languages: ['fr', 'ru'] }).replaced, ['/ru/']);
  assert.deepEqual(run({ languages: ['fr'] }).replaced, []);
});

test('with the production list (English only) the script does nothing', () => {
  const replaced = [];
  vm.runInNewContext(SOURCE, {
    location: { pathname: '/', search: '', hash: '', replace: (url) => replaced.push(url) },
    navigator: { languages: ['ru'] },
    localStorage: memoryStorage(),
  });
  assert.deepEqual(replaced, []);
});

test('its language list and key equal ACTIVE_LANGS and LANG_KEY', () => {
  const list = JSON.parse(SOURCE.match(/const ACTIVE = (\[[^\]]*\]);/)[1].replaceAll("'", '"'));
  assert.deepEqual(list, ACTIVE_LANGS);
  assert.equal(SOURCE.match(/const KEY = '([^']*)';/)[1], LANG_KEY);
  assert.match(SOURCE, KEY_RE);
});

test('lang.mjs stores the clicked language', async () => {
  const storage = memoryStorage();
  let listener = null;
  globalThis.localStorage = storage;
  globalThis.document = { addEventListener: (type, fn) => { if (type === 'click') listener = fn; } };
  await import('../src/js/lang.mjs');
  assert.equal(typeof listener, 'function');
  const link = { dataset: { lang: 'en' } };
  listener({ target: { closest: (selector) => (selector === 'a[data-lang]' ? link : null) } });
  assert.equal(storage.data.get(LANG_KEY), 'en');
  listener({ target: { closest: () => null } });
  assert.equal(storage.data.size, 1);
  delete globalThis.document;
  delete globalThis.localStorage;
});

test('the head loads the redirect first and lang.mjs as a module', () => {
  const head = renderChrome('head', { pagePath: '/', title: 't', description: 'd' });
  const redirect = head.indexOf('<script src="/js/lang-redirect.js"></script>');
  assert.ok(redirect >= 0);
  assert.ok(redirect < head.indexOf('<link rel="stylesheet"'));
  assert.match(head, /<script type="module" src="\/js\/lang\.mjs"><\/script>/);
});
