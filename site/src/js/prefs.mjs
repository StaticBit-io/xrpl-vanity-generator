// Remembered language. localStorage throws in private windows and with blocked site data (even the
// property read can), so every access is guarded and a failure only means "not remembered".
import { ACTIVE_LANGS, ENGLISH_ONLY, LANGS, homePath, isActive, langFromTag, pathPrefix } from './languages.mjs';

export { ACTIVE_LANGS, LANGS };

export const LANG_KEY = 'xv-lang';

export function safeLocalStorage() {
  try {
    return globalThis.localStorage ?? null;
  } catch {
    return null;
  }
}

function readItem(storage, key) {
  try {
    return storage?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

function writeItem(storage, key, value) {
  try {
    storage?.setItem(key, value);
  } catch {
    // Not remembered; the choice still applies to the current page.
  }
}

function browserLanguages() {
  const navigator = globalThis.navigator;
  return navigator?.languages?.length ? navigator.languages : navigator?.language;
}

/**
 * The stored choice if it is an active language, else the first browser language the site offers
 * (matched by primary subtag: zh-TW is Chinese, pt-BR Portuguese), else English. `browserLangs` is
 * navigator.languages or a single tag; `active` defaults to ACTIVE_LANGS and exists so tests can
 * run with more languages than production has switched on.
 */
export function getLang(storage = safeLocalStorage(), browserLangs = browserLanguages(), active = ACTIVE_LANGS) {
  const stored = readItem(storage, LANG_KEY);
  if (isActive(stored, active)) return stored;
  const tags = Array.isArray(browserLangs) ? browserLangs : [browserLangs];
  for (const tag of tags) {
    const lang = langFromTag(tag);
    if (lang !== null && isActive(lang, active)) return lang;
  }
  return 'en';
}

export function setLang(lang, storage = safeLocalStorage()) {
  writeItem(storage, LANG_KEY, isActive(lang) ? lang : 'en');
}

/**
 * The same page in another language: / <-> /ru/ <-> /de/ and /desktop/ <-> /ru/desktop/. Every known
 * prefix is stripped, active or not, so a stray /de/ path still maps back. A page that exists in
 * English only leads to the target language's home instead of to a missing page.
 */
export function equivalentPath(pathname, lang) {
  let rest = pathname;
  for (const known of LANGS) {
    const prefix = pathPrefix(known);
    if (prefix !== '' && (rest === prefix || rest.startsWith(`${prefix}/`))) {
      rest = rest.slice(prefix.length) || '/';
      break;
    }
  }
  if (ENGLISH_ONLY.includes(rest)) return homePath(lang);
  return `${pathPrefix(lang)}${rest}`;
}
