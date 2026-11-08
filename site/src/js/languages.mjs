// The site's languages in one place. Adding a language is a data change: its name, tags and locale
// are here, its words are in the string tables, its home page is src/<lang>/index.html, and it
// joins ACTIVE_LANGS once all of those exist. The application carries the same table in
// src/XrplVanity.App.Core/Localization/UiLanguage.cs; a test keeps the two equal.

/** Every language the site is built to carry. The order is the order of the language menu. */
export const LANGS = ['en', 'ru', 'de', 'es', 'pt', 'zh'];

/**
 * The languages the site ships and lists. A language outside this list has no menu entry, no hreflang
 * and no page in the build, and getLang never picks it, so the site stays consistent while its
 * strings and pages are being written.
 */
export const ACTIVE_LANGS = ['en', 'ru'];

export const DEFAULT_LANG = 'en';

/** Pages that exist in English only; their language-menu target in any language is that language's home. */
export const ENGLISH_ONLY = ['/privacy/'];

/** Language names are written in their own language and never translated. */
export const LANGUAGE_NAMES = { en: 'English', ru: 'Русский', de: 'Deutsch', es: 'Español', pt: 'Português', zh: '中文' };

// The BCP 47 tag of <html lang>, of hreflang and of the language link. Chinese is tagged with its
// script, because Simplified is what the site writes and the bare "zh" does not say so.
const HTML_LANGS = { zh: 'zh-Hans' };

// The locale that formats numbers, dates and sizes. English is en-GB for its "2 November 2026" date,
// which no reader takes for a month-first one; Portuguese is Brazilian (the larger readership);
// Spanish is es-ES, whose decimal comma is the common written form; Chinese is mainland Simplified.
const LOCALES = { en: 'en-GB', ru: 'ru-RU', de: 'de-DE', es: 'es-ES', pt: 'pt-BR', zh: 'zh-Hans-CN' };

// `active` is a parameter only so tests can exercise languages that are not active in production yet.
export function isActive(lang, active = ACTIVE_LANGS) {
  return active.includes(lang);
}

/** The tag for <html lang> and hreflang. */
export function htmlLang(lang) {
  return HTML_LANGS[lang] ?? lang;
}

/** The Intl locale for a language; English for anything unknown. */
export function localeOf(lang) {
  return LOCALES[lang] ?? LOCALES[DEFAULT_LANG];
}

/**
 * The site language a BCP 47 tag belongs to, by its primary subtag (zh-TW, zh-Hans and zh are all
 * zh; pt-BR is pt; es-419 is es), or null when the site has no such language.
 */
export function langFromTag(tag) {
  const primary = String(tag ?? '').toLowerCase().split(/[-_]/, 1)[0];
  return LANGS.includes(primary) ? primary : null;
}

/** The language of a page's <html lang>, English when it names none the site has. */
export function pageLang(tag) {
  return langFromTag(tag) ?? DEFAULT_LANG;
}

/** The URL prefix of a language's pages: none for English, "/ru" for Russian. */
export function pathPrefix(lang) {
  return lang === DEFAULT_LANG || !LANGS.includes(lang) ? '' : `/${lang}`;
}

/** The home page of a language. */
export function homePath(lang) {
  return `${pathPrefix(lang)}/`;
}

/** The language itself when the site ships it, English otherwise. */
export function activeLang(lang) {
  return isActive(lang) ? lang : DEFAULT_LANG;
}
