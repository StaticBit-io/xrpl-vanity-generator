// Assembles the deployable site into site/dist: site/src/** is copied as is, the markers in its pages are
// filled in, and the result is refused if it breaks a promise the site makes (checks.mjs).
//
//   node site/build.mjs
import { cp, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { renderChrome, supportEmailHtml } from './chrome-markup.mjs';
import { findBrokenLinks, findExternalRefs, findInlineCode, findUnfencedEmails } from './checks.mjs';
import { SITE_ORIGIN } from './src/js/config.mjs';
import { renderGlyphField } from './src/js/glyph-field.mjs';

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

function fillMarkers(name, html) {
  const title = /<title>([^<]*)<\/title>/i.exec(html)?.[1] ?? null;
  const description = /<meta name="description" content="([^"]*)">/i.exec(html)?.[1] ?? null;
  const page = { pagePath: pagePathOf(name), title, description };
  const filled = html
    .replace(CHROME_MARKER, (_, part) => renderChrome(part, page))
    .replaceAll(SUPPORT_MARKER, () => supportEmailHtml())
    .replaceAll(GLYPH_MARKER, () => renderGlyphField(...GLYPH_FIELD));
  const left = /<!--@[^>]*-->/.exec(filled);
  if (left) throw new Error(`${name}: unknown marker ${left[0]}`);
  return filled;
}

async function fillPages(outDir) {
  const pages = new Map();
  for (const file of await listFiles(outDir)) {
    if (!file.endsWith('.html')) continue;
    const name = siteName(outDir, file);
    const html = fillMarkers(name, await readFile(file, 'utf8'));
    await writeFile(file, html);
    pages.set(name, html);
  }
  return pages;
}

/** The files crawlers ask for by name. The 404 page is not a place a search should send anyone. */
async function writeCrawlerFiles(outDir, pages) {
  const urls = [...pages.keys()]
    .filter((name) => name !== '404.html')
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

/** @param {{ siteDir: string, outDir: string, maxFileBytes?: number }} options */
export async function build({ siteDir, outDir, maxFileBytes = MAX_FILE_BYTES }) {
  if (isInside(outDir, siteDir)) throw new Error(`refusing to build into ${outDir}: it contains the sources`);
  await rm(outDir, { recursive: true, force: true });
  try {
    await cp(join(siteDir, 'src'), outDir, { recursive: true });
    const pages = await fillPages(outDir);
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
