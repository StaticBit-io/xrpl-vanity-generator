import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from '../build.mjs';
import { SITE_ORIGIN } from '../src/js/config.mjs';

const SITE_DIR = join(dirname(fileURLToPath(import.meta.url)), '..');
const PAGES = ['index.html', 'desktop/index.html', 'cli/index.html', 'privacy/index.html', 'support/index.html'];
const SEARCHED_PAGES = ['index.html', 'desktop/index.html', 'cli/index.html'];

const titleOf = (html) => /<title>([^<]*)<\/title>/.exec(html)[1];
const descriptionOf = (html) => /<meta name="description" content="([^"]*)">/.exec(html)[1];
const textOf = (html) => html.replace(/<(script|style)\b[\s\S]*?<\/\1>/g, ' ').replace(/<[^>]+>/g, ' ');
const structuredData = (html) => /<script type="application\/ld\+json">([\s\S]*?)<\/script>/.exec(html)?.[1];

async function built(t) {
  const root = await mkdtemp(join(tmpdir(), 'xrpl-vanity-seo-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const outDir = join(root, 'out');
  await build({ siteDir: SITE_DIR, outDir });
  return (name) => readFile(join(outDir, name), 'utf8');
}

test('the pages people search for name what they search for', async (t) => {
  const read = await built(t);
  for (const name of SEARCHED_PAGES) {
    const html = await read(name);
    assert.match(titleOf(html), /vanity address/i, name);
    assert.match(descriptionOf(html), /vanity address/i, name);
    assert.match(textOf(html), /vanity address/i, name);
  }
});

test('titles and descriptions fit a results page and do not repeat', async (t) => {
  const read = await built(t);
  const pages = await Promise.all(PAGES.map(read));
  for (const html of pages) {
    assert.ok(titleOf(html).length <= 60, titleOf(html));
    assert.ok(descriptionOf(html).length <= 160, descriptionOf(html));
  }
  assert.equal(new Set(pages.map(titleOf)).size, PAGES.length);
  assert.equal(new Set(pages.map(descriptionOf)).size, PAGES.length);
});

test('the sitemap lists every page and leaves out the 404', async (t) => {
  const read = await built(t);
  const sitemap = await read('sitemap.xml');
  const urls = [...sitemap.matchAll(/<loc>([^<]*)<\/loc>/g)].map((match) => match[1]);
  assert.deepEqual(urls, ['/', '/cli/', '/desktop/', '/privacy/', '/support/'].map((path) => `${SITE_ORIGIN}${path}`));
  assert.match(sitemap, /^<\?xml version="1\.0" encoding="UTF-8"\?>/);
  assert.match(sitemap, /<urlset xmlns="http:\/\/www\.sitemaps\.org\/schemas\/sitemap\/0\.9">/);
});

test('robots.txt points at the sitemap and blocks nothing', async (t) => {
  const read = await built(t);
  const robots = await read('robots.txt');
  assert.ok(robots.includes(`Sitemap: ${SITE_ORIGIN}/sitemap.xml`));
  assert.doesNotMatch(robots, /Disallow/i);
});

test('the home page describes the program to search engines, and only the home page', async (t) => {
  const read = await built(t);
  const home = await read('index.html');
  const data = JSON.parse(structuredData(home));
  assert.equal(data['@type'], 'SoftwareApplication');
  assert.equal(data.name, 'XRPL Vanity');
  assert.equal(data.url, `${SITE_ORIGIN}/`);
  assert.equal(data.description, descriptionOf(home));
  assert.equal(data.publisher.name, 'StaticBit');
  for (const name of PAGES.slice(1)) assert.equal(structuredData(await read(name)), undefined, name);
});

test('the structured data claims no price or rating the site does not have', async (t) => {
  const read = await built(t);
  const data = JSON.parse(structuredData(await read('index.html')));
  for (const field of ['offers', 'aggregateRating', 'review', 'downloadUrl', 'installUrl']) {
    assert.equal(data[field], undefined, field);
  }
});
