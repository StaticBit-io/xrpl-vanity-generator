import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  ACTIVE_LANGS, DEFAULT_LANG, ENGLISH_ONLY, LANGS, LANGUAGE_NAMES, activeLang, homePath, htmlLang, langFromTag,
  localeOf, pageLang, pathPrefix,
} from '../src/js/languages.mjs';
import { LANG_KEY, equivalentPath, getLang, safeLocalStorage, setLang } from '../src/js/prefs.mjs';

const ALL = ['en', 'ru', 'de', 'es', 'pt', 'zh'];
// A language the site does not ship yet, for the checks that an inactive one is ignored; none once all ship.
const INACTIVE = ALL.find((lang) => !ACTIVE_LANGS.includes(lang));

function memoryStorage(initial = {}) {
  const data = new Map(Object.entries(initial));
  return {
    getItem: (key) => (data.has(key) ? data.get(key) : null),
    setItem: (key, value) => { data.set(key, String(value)); },
    data,
  };
}

const throwingStorage = {
  getItem() { throw new Error('blocked'); },
  setItem() { throw new Error('blocked'); },
};

test('the active languages start with English, follow the menu order, and the key is xv-lang', () => {
  assert.deepEqual(LANGS, ALL);
  assert.equal(ACTIVE_LANGS[0], 'en');
  assert.deepEqual(ACTIVE_LANGS, LANGS.filter((lang) => ACTIVE_LANGS.includes(lang)));
  assert.equal(DEFAULT_LANG, 'en');
  assert.equal(LANG_KEY, 'xv-lang');
  if (INACTIVE) assert.equal(activeLang(INACTIVE), 'en', 'an inactive language reads English');
});

test('getLang takes a stored active language, ignores an inactive one', () => {
  assert.equal(getLang(memoryStorage({ [LANG_KEY]: 'ru' }), ['en-US'], ALL), 'ru');
  if (INACTIVE) assert.equal(getLang(memoryStorage({ [LANG_KEY]: INACTIVE }), ['en-US']), 'en', `${INACTIVE} is not active yet`);
  assert.equal(getLang(memoryStorage({ [LANG_KEY]: 'xx' }), ['de-DE'], ALL), 'de');
});

test('zh-TW is Chinese only when zh is active', () => {
  assert.equal(getLang(null, ['zh-TW'], ALL), 'zh');
  assert.equal(getLang(null, ['zh-TW']), ACTIVE_LANGS.includes('zh') ? 'zh' : 'en');
  assert.equal(getLang(null, ['fr', 'pt-PT', 'ru'], ALL), 'pt');
});

test('getLang falls back to English and survives a storage that throws', () => {
  assert.equal(getLang(null, ['fr'], ALL), 'en');
  assert.equal(getLang(null, undefined, ALL), 'en');
  assert.equal(getLang(throwingStorage, ['ru-RU'], ALL), 'ru');
  assert.doesNotThrow(() => setLang('en', throwingStorage));
});

test('getLang does not throw when localStorage access throws, and reads navigator.languages', () => {
  const storageDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
  const navigatorDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  Object.defineProperty(globalThis, 'localStorage', {
    configurable: true,
    get() { throw new DOMException('denied', 'SecurityError'); },
  });
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { languages: ['fr', 'ru-RU'] } });
  try {
    assert.equal(safeLocalStorage(), null);
    assert.equal(getLang(undefined, undefined, ALL), 'ru');
    assert.equal(getLang(), ACTIVE_LANGS.includes('ru') ? 'ru' : 'en');
  } finally {
    if (storageDescriptor) Object.defineProperty(globalThis, 'localStorage', storageDescriptor);
    else delete globalThis.localStorage;
    if (navigatorDescriptor) Object.defineProperty(globalThis, 'navigator', navigatorDescriptor);
    else delete globalThis.navigator;
  }
});

test('setLang stores an active language and anything else as English', () => {
  const storage = memoryStorage();
  setLang('en', storage);
  assert.equal(storage.data.get(LANG_KEY), 'en');
  setLang('xx', storage);
  assert.equal(storage.data.get(LANG_KEY), 'en');
  if (INACTIVE) {
    setLang(INACTIVE, storage);
    assert.equal(storage.data.get(LANG_KEY), 'en');
  }
  for (const lang of ACTIVE_LANGS) {
    setLang(lang, storage);
    assert.equal(storage.data.get(LANG_KEY), lang);
  }
});

test('equivalentPath maps between languages and sends English-only pages home', () => {
  assert.equal(equivalentPath('/', 'ru'), '/ru/');
  assert.equal(equivalentPath('/ru/', 'en'), '/');
  assert.equal(equivalentPath('/desktop/', 'de'), '/de/desktop/');
  assert.equal(equivalentPath('/de/desktop/', 'en'), '/desktop/');
  assert.equal(equivalentPath('/de/desktop/', 'zh'), '/zh/desktop/');
  assert.equal(equivalentPath('/zh', 'de'), '/de/');
  assert.equal(equivalentPath('/russia', 'en'), '/russia', 'a lookalike is not a prefix');
  assert.deepEqual(ENGLISH_ONLY, ['/privacy/']);
  assert.equal(equivalentPath('/privacy/', 'ru'), '/ru/');
  assert.equal(equivalentPath('/de/privacy/', 'en'), '/');
  assert.equal(equivalentPath('/privacy/', 'en'), '/');
});

test('helpers map tags, prefixes and locales', () => {
  assert.equal(pathPrefix('en'), '');
  assert.equal(pathPrefix('pt'), '/pt');
  assert.equal(homePath('zh'), '/zh/');
  assert.equal(langFromTag('pt_BR'), 'pt');
  assert.equal(langFromTag('fr'), null);
  assert.equal(pageLang('zh-Hans'), 'zh');
  assert.equal(pageLang(undefined), 'en');
  assert.equal(localeOf('xx'), 'en-GB');
});

test('the C# and JS language tables agree', () => {
  const source = readFileSync(new URL('../../src/XrplVanity.App.Core/Localization/UiLanguage.cs', import.meta.url), 'utf8');
  const rows = [...source.matchAll(/=\s*new\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\)/g)]
    .map(([, code, name, tag, culture]) => ({ code, name, tag, culture }));
  assert.equal(rows.length, LANGS.length);
  assert.deepEqual(rows.map((row) => row.code), LANGS);
  for (const row of rows) {
    assert.equal(LANGUAGE_NAMES[row.code], row.name, `${row.code} name`);
    assert.equal(htmlLang(row.code), row.tag, `${row.code} html tag`);
    assert.equal(localeOf(row.code), row.culture, `${row.code} locale`);
  }
});
