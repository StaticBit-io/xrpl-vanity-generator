// The markup every page shares, filled in at build time so it needs no script: the head, the header
// and the footer behind <!--@chrome:...--> markers, and the support address behind <!--@support-email-->.
import { CHROME_STRINGS } from './chrome-strings.mjs';
import { REPOSITORY_URL, SITE_ORIGIN, SUPPORT_EMAIL } from './src/js/config.mjs';
import { ACTIVE_LANGS, DEFAULT_LANG, ENGLISH_ONLY, LANGS, LANGUAGE_NAMES, homePath, htmlLang, pathPrefix } from './src/js/languages.mjs';
import { equivalentPath } from './src/js/prefs.mjs';

const NAV = [
  ['desktop', '/desktop/'],
  ['cli', '/cli/'],
  ['privacy', '/privacy/'],
  ['support', '/support/'],
];

function escapeHtml(text) {
  return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
}

/** The page's path without its language prefix: /ru/desktop/ is /desktop/. */
function englishPathOf(pagePath) {
  for (const lang of LANGS) {
    const prefix = pathPrefix(lang);
    if (prefix !== '' && (pagePath === prefix || pagePath.startsWith(prefix + '/'))) return pagePath.slice(prefix.length) || '/';
  }
  return pagePath;
}

const isNotFoundPage = (pagePath) => englishPathOf(pagePath) === '/404';

function stringsOf(page) {
  const lang = page.lang ?? DEFAULT_LANG;
  const strings = (page.chromeStrings ?? CHROME_STRINGS)[lang];
  if (!strings) throw new Error(`no chrome strings for "${lang}" in site/chrome-strings.mjs`);
  return strings;
}

/** What search engines read about the program. Price and ratings stay out until there are real ones. */
function renderStructuredData(description, lang) {
  const data = {
    '@context': 'https://schema.org',
    '@type': 'SoftwareApplication',
    name: 'XRPL Vanity',
    description,
    url: `${SITE_ORIGIN}${homePath(lang)}`,
    inLanguage: htmlLang(lang),
    image: `${SITE_ORIGIN}/og-banner.png`,
    applicationCategory: 'UtilitiesApplication',
    operatingSystem: 'Windows, Linux',
    license: 'https://www.apache.org/licenses/LICENSE-2.0',
    publisher: { '@type': 'Organization', name: 'StaticBit', url: 'https://staticbit.io' },
  };
  // A "<" in the text could otherwise close the script element early.
  return `<script type="application/ld+json">${JSON.stringify(data).replaceAll('<', '\\u003c')}</script>`;
}

/**
 * One alternate per active language plus x-default (the English page, where a visitor with no matching
 * language belongs). Pages that exist in English only, and the 404, have none: there is nothing to
 * alternate to.
 */
export function languageAlternates(pagePath, langs = ACTIVE_LANGS) {
  const english = englishPathOf(pagePath);
  if (ENGLISH_ONLY.includes(english) || isNotFoundPage(pagePath)) return [];
  const url = (lang) => `${SITE_ORIGIN}${equivalentPath(pagePath, lang)}`;
  return [
    ...langs.map((lang) => `<link rel="alternate" hreflang="${htmlLang(lang)}" href="${url(lang)}">`),
    `<link rel="alternate" hreflang="x-default" href="${url(DEFAULT_LANG)}">`,
  ];
}

function renderHead(page) {
  const { pagePath, title, description } = page;
  const lang = page.lang ?? DEFAULT_LANG;
  const strings = stringsOf(page);
  const url = `${SITE_ORIGIN}${pagePath}`;
  return [
    '<meta charset="utf-8">',
    '<meta name="viewport" content="width=device-width, initial-scale=1">',
    '<meta name="color-scheme" content="dark">',
    `<link rel="canonical" href="${url}">`,
    ...languageAlternates(pagePath, page.langs),
    '<link rel="icon" href="/favicon.svg" type="image/svg+xml">',
    // Classic and blocking on purpose: the redirect must run before anything paints.
    '<script src="/js/lang-redirect.js"></script>',
    '<script type="module" src="/js/lang.mjs"></script>',
    '<link rel="stylesheet" href="/css/tokens.css">',
    '<link rel="stylesheet" href="/css/base.css">',
    '<link rel="stylesheet" href="/css/site.css">',
    '<meta property="og:type" content="website">',
    '<meta property="og:site_name" content="XRPL Vanity">',
    `<meta property="og:url" content="${url}">`,
    `<meta property="og:title" content="${escapeHtml(title ?? '')}">`,
    `<meta property="og:description" content="${escapeHtml(description ?? '')}">`,
    `<meta property="og:image" content="${SITE_ORIGIN}/og-banner.png">`,
    '<meta property="og:image:width" content="1200">',
    '<meta property="og:image:height" content="630">',
    `<meta property="og:image:alt" content="${escapeHtml(strings.ogImageAlt)}">`,
    '<meta name="twitter:card" content="summary_large_image">',
    ...(pagePath === homePath(lang) ? [renderStructuredData(description ?? '', lang)] : []),
  ].join('\n');
}

// A disclosure of plain links rather than a <select>: it opens with no script, the links can be
// followed by a crawler, and the summary is a native button. data-lang is what a script reads to
// remember the visitor's choice.
function renderLanguageMenu(page, strings) {
  const lang = page.lang ?? DEFAULT_LANG;
  const links = (page.langs ?? ACTIVE_LANGS).map((code) => {
    // A 404 has no counterpart in another language, so its menu leads to that language's home.
    const href = isNotFoundPage(page.pagePath) ? homePath(code) : equivalentPath(page.pagePath, code);
    const current = code === lang ? ' aria-current="true"' : '';
    const tag = htmlLang(code);
    return `<a href="${href}" hreflang="${tag}" lang="${tag}" data-lang="${code}"${current}>${LANGUAGE_NAMES[code]}</a>`;
  });
  return `<details class="lang">
<summary><span class="visually-hidden">${escapeHtml(strings.language)}</span> ${LANGUAGE_NAMES[lang]}</summary>
<div class="lang__list">
${links.join('\n')}
</div>
</details>`;
}

function renderHeader(page) {
  const lang = page.lang ?? DEFAULT_LANG;
  const strings = stringsOf(page);
  const links = NAV.map(([key, path]) => {
    // Privacy exists in English only, so it never takes a language prefix.
    const href = ENGLISH_ONLY.includes(path) ? path : `${pathPrefix(lang)}${path}`;
    const current = href === page.pagePath ? ' aria-current="page"' : '';
    return `<a href="${href}"${current}>${escapeHtml(strings.nav[key])}</a>`;
  });
  return `<a class="skip" href="#main">${escapeHtml(strings.skip)}</a>
<header class="site-header">
<a class="wordmark" href="${homePath(lang)}">xrpl-vanity</a>
<div class="site-header__tools">
<nav aria-label="Pages">
${links.join('\n')}
</nav>
${renderLanguageMenu(page, strings)}
</div>
</header>`;
}

function renderFooter(page) {
  const strings = stringsOf(page);
  const publisher = '<a href="https://staticbit.io">StaticBit</a>';
  const published = escapeHtml(strings.published).replace('{publisher}', publisher);
  return `<footer class="site-footer">
<p>${published}</p>
<p>${escapeHtml(strings.licence)} <a href="${REPOSITORY_URL}">${escapeHtml(strings.source)}</a>.</p>
</footer>`;
}

/** The address inside Cloudflare's email_off fence: see findUnfencedEmails in checks.mjs. */
export function supportEmailHtml() {
  return `<!--email_off--><a href="mailto:${SUPPORT_EMAIL}">${SUPPORT_EMAIL}</a><!--/email_off-->`;
}

/**
 * @param {'head'|'header'|'footer'} part
 * @param {{pagePath: string, title: string|null, description: string|null, lang?: string, langs?: string[], chromeStrings?: object}} page
 *   `lang` is the page's site language (English when omitted); `langs` the active languages and
 *   `chromeStrings` the string table, both injectable so a test can build a language that is not active yet.
 */
export function renderChrome(part, page) {
  if (part === 'head') return renderHead(page);
  if (part === 'header') return renderHeader(page);
  if (part === 'footer') return renderFooter(page);
  throw new Error(`unknown chrome marker "${part}"`);
}
