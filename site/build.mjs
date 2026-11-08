// Assembles the deployable site into site/dist: site/src/** is copied as is, the markers in its pages are
// filled in, and the result is refused if it breaks a promise the site makes (checks.mjs).
//
//   node site/build.mjs
import { cp, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { renderChrome, supportEmailHtml } from './chrome-markup.mjs';
import { findBrokenLinks, findExternalRefs, findInlineCode, findUnfencedEmails } from './checks.mjs';
import { CHROME_STRINGS } from './chrome-strings.mjs';
import { SITE_ORIGIN } from './src/js/config.mjs';
import { renderGlyphField } from './src/js/glyph-field.mjs';
import { ACTIVE_LANGS, DEFAULT_LANG, ENGLISH_ONLY, LANGS, htmlLang, langFromTag } from './src/js/languages.mjs';

// Cloudflare Pages rejects a deployment with any file over 25 MiB.
export const MAX_FILE_BYTES = 25 * 1024 * 1024;

const CHROME_MARKER = /<!--@chrome:([a-z-]+)-->/g;
const SUPPORT_MARKER = '<!--@support-email-->';
const GLYPH_MARKER = '<!--@glyph-field-->';
// Columns, rows and seed of the hero's picture.
const GLYPH_FIELD = [64, 34, 58];

async function listFiles(root, dir = root) {
  const files = [];
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) files.push(...(await listFiles(root, full)));
    else files.push(full);
  }
  return files;
}

function isInside(parent, child) {
  const rel = relative(resolve(parent), resolve(child));
  return rel === '' || (!rel.startsWith('..') && !isAbsolute(rel));
}

const siteName = (root, file) => relative(root, file).split(sep).join('/');

/** The URL a page is served at: index.html is its folder, other pages lose the .html (Pages' pretty URLs). */
export function pagePathOf(relativeFile) {
  const path = `/${relativeFile.split(sep).join('/')}`;
  if (path === '/index.html') return '/';
  if (path.endsWith('/index.html')) return path.slice(0, -'index.html'.length);
  return path.replace(/\.html$/, '');
}

/** The language a page's folder says it is in: ru/ is Russian, everything else is English. */
function folderLang(name) {
  const [first, ...rest] = name.split('/');
  return rest.length > 0 && first !== DEFAULT_LANG && LANGS.includes(first) ? first : DEFAULT_LANG;
}

const htmlLangTag = (html) => /<html\b[^>]*\blang="([^"]*)"/i.exec(html)?.[1] ?? null;

/**
 * Refuses a tree whose languages and folders disagree: a page whose <html lang> is not its folder's
 * language (a copied page that kept the old tag would ship with the wrong chrome and alternates), a
 * folder for a language that is not active, and an active language that lacks a page the English
 * site has, other than the pages that exist in English only.
 */
async function checkLanguagePages(outDir, langs, chromeStrings) {
  for (const lang of langs) {
    if (!chromeStrings[lang]) throw new Error(`"${lang}" is active but has no chrome strings in site/chrome-strings.mjs`);
  }
  const names = (await listFiles(outDir)).map((file) => siteName(outDir, file));
  for (const lang of LANGS) {
    if (lang !== DEFAULT_LANG && !langs.includes(lang) && names.some((name) => name.startsWith(`${lang}/`))) {
      throw new Error(`site/src/${lang}/ exists but "${lang}" is not in ACTIVE_LANGS (site/src/js/languages.mjs); add it there or remove the folder`);
    }
  }
  for (const name of names.filter((file) => file.endsWith('.html'))) {
    const tag = htmlLangTag(await readFile(join(outDir, name), 'utf8'));
    const expected = folderLang(name);
    if (tag !== htmlLang(expected)) {
      const where = expected === DEFAULT_LANG ? 'the site root' : `the ${expected}/ folder`;
      throw new Error(`${name} is in ${where} but its <html> says ${tag === null ? 'no lang' : `lang="${tag}"`}`);
    }
  }
  const have = new Set(names);
  const englishPages = names.filter((name) => name.endsWith('.html') && folderLang(name) === DEFAULT_LANG);
  for (const lang of langs.filter((code) => code !== DEFAULT_LANG)) {
    for (const name of englishPages) {
      if (ENGLISH_ONLY.includes(pagePathOf(name))) continue;
      if (!have.has(`${lang}/${name}`)) throw new Error(`"${lang}" is active but site/src/${lang}/${name} does not exist`);
    }
  }
}

function fillMarkers(name, html, langs, chromeStrings) {
  const title = /<title>([^<]*)<\/title>/i.exec(html)?.[1] ?? null;
  const description = /<meta name="description" content="([^"]*)">/i.exec(html)?.[1] ?? null;
  const page = { lang: langFromTag(htmlLangTag(html)), pagePath: pagePathOf(name), title, description, langs, chromeStrings };
  const filled = html
    .replace(CHROME_MARKER, (_, part) => renderChrome(part, page))
    .replaceAll(SUPPORT_MARKER, () => supportEmailHtml())
    .replaceAll(GLYPH_MARKER, () => renderGlyphField(...GLYPH_FIELD));
  const left = /<!--@[^>]*-->/.exec(filled);
  if (left) throw new Error(`${name}: unknown marker ${left[0]}`);
  return filled;
}

async function fillPages(outDir, langs, chromeStrings) {
  const pages = new Map();
  for (const file of await listFiles(outDir)) {
    if (!file.endsWith('.html')) continue;
    const name = siteName(outDir, file);
    const html = fillMarkers(name, await readFile(file, 'utf8'), langs, chromeStrings);
    await writeFile(file, html);
    pages.set(name, html);
  }
  return pages;
}

/** The files crawlers ask for by name. The 404 page is not a place a search should send anyone. */
async function writeCrawlerFiles(outDir, pages) {
  const urls = [...pages.keys()]
    .filter((name) => name.split('/').at(-1) !== '404.html')
    .map((name) => `${SITE_ORIGIN}${pagePathOf(name)}`)
    .sort();
  const entries = urls.map((url) => `  <url><loc>${url}</loc></url>`).join('\n');
  await writeFile(
    join(outDir, 'sitemap.xml'),
    `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${entries}\n</urlset>\n`,
  );
  await writeFile(join(outDir, 'robots.txt'), `Sitemap: ${SITE_ORIGIN}/sitemap.xml\n`);
}

async function checkPromises(outDir, pages) {
  const files = await listFiles(outDir);
  const problems = [];
  for (const [name, html] of pages) {
    problems.push(...findExternalRefs(name, html), ...findInlineCode(name, html), ...findUnfencedEmails(name, html));
  }
  for (const file of files.filter((f) => f.endsWith('.css'))) {
    problems.push(...findExternalRefs(siteName(outDir, file), await readFile(file, 'utf8')));
  }
  problems.push(...findBrokenLinks(new Set(files.map((file) => siteName(outDir, file))), pages));
  if (problems.length > 0) throw new Error(`the site breaks its own rules:\n${problems.join('\n')}`);
}

async function checkSizes(outDir, maxFileBytes) {
  const tooBig = [];
  for (const file of await listFiles(outDir)) {
    const { size } = await stat(file);
    if (size > maxFileBytes) tooBig.push(`${siteName(outDir, file)} (${size} bytes)`);
  }
  if (tooBig.length > 0) throw new Error(`files over the ${maxFileBytes}-byte limit: ${tooBig.join(', ')}`);
}

/**
 * @param {{ siteDir: string, outDir: string, maxFileBytes?: number, langs?: string[], chromeStrings?: object }} options
 *   `langs` and `chromeStrings` default to the shipped ones; tests pass their own to build a fixture
 *   in a language that is not active yet.
 */
export async function build({ siteDir, outDir, maxFileBytes = MAX_FILE_BYTES, langs = ACTIVE_LANGS, chromeStrings = CHROME_STRINGS }) {
  if (isInside(outDir, siteDir)) throw new Error(`refusing to build into ${outDir}: it contains the sources`);
  await rm(outDir, { recursive: true, force: true });
  try {
    await cp(join(siteDir, 'src'), outDir, { recursive: true });
    await checkLanguagePages(outDir, langs, chromeStrings);
    const pages = await fillPages(outDir, langs, chromeStrings);
    await writeCrawlerFiles(outDir, pages);
    await checkPromises(outDir, pages);
    await checkSizes(outDir, maxFileBytes);
  } catch (error) {
    // A half-built or refused tree must not be mistaken for something deployable.
    await rm(outDir, { recursive: true, force: true });
    throw error;
  }
}

const here = dirname(fileURLToPath(import.meta.url));

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const outDir = join(here, 'dist');
  try {
    await build({ siteDir: here, outDir });
    console.log(`site built into ${outDir}`);
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
