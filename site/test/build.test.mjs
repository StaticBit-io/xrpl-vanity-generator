import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { build, pagePathOf } from '../build.mjs';
import { renderChrome, supportEmailHtml } from '../chrome-markup.mjs';
import { CHROME_STRINGS } from '../chrome-strings.mjs';
import { findUnfencedEmails } from '../checks.mjs';
import { REPOSITORY_URL, SUPPORT_EMAIL } from '../src/js/config.mjs';

const PAGE = `<!doctype html>
<html lang="en">
<head>
<title>Home</title>
<meta name="description" content="A page.">
<!--@chrome:head-->
</head>
<body>
<!--@chrome:header-->
<main><p>Mail: <!--@support-email--></p></main>
<!--@chrome:footer-->
</body>
</html>
`;

const NAV_PAGES = ['desktop', 'cli', 'privacy', 'support'];

async function fixture(pages = { 'index.html': PAGE }) {
  const root = await mkdtemp(join(tmpdir(), 'xrpl-vanity-site-'));
  const siteDir = join(root, 'site');
  await mkdir(join(siteDir, 'src', 'css'), { recursive: true });
  await mkdir(join(siteDir, 'src', 'js'), { recursive: true });
  for (const name of ['css/tokens.css', 'css/base.css', 'css/site.css', 'favicon.svg', 'js/lang-redirect.js', 'js/lang.mjs']) {
    await writeFile(join(siteDir, 'src', name), 'p { margin: 0; }\n');
  }
  for (const name of NAV_PAGES) {
    await mkdir(join(siteDir, 'src', name), { recursive: true });
    await writeFile(join(siteDir, 'src', name, 'index.html'), PAGE);
  }
  for (const [name, text] of Object.entries(pages)) await writeFile(join(siteDir, 'src', name), text);
  return { root, siteDir, outDir: join(root, 'out') };
}

const exists = (path) => stat(path).then(() => true, () => false);

test('every marker is replaced and none survives into the output', async (t) => {
  const { root, siteDir, outDir } = await fixture();
  t.after(() => rm(root, { recursive: true, force: true }));
  await build({ siteDir, outDir });
  const html = await readFile(join(outDir, 'index.html'), 'utf8');
  assert.doesNotMatch(html, /<!--@/);
  assert.match(html, /<header/);
  assert.match(html, /<footer/);
  assert.match(html, /mailto:support@staticbit\.io/);
});

test('the support address is fenced from Cloudflare', () => {
  assert.deepEqual(findUnfencedEmails('x', supportEmailHtml()), []);
  assert.ok(supportEmailHtml().includes(SUPPORT_EMAIL));
});

test('the header links every page', () => {
  const header = renderChrome('header', { pagePath: '/', title: 'x', description: 'x' });
  for (const name of NAV_PAGES) assert.ok(header.includes(`href="/${name}/"`), name);
});

test('the header marks the page the visitor is on', () => {
  const header = renderChrome('header', { pagePath: '/cli/', title: 'x', description: 'x' });
  assert.match(header, /href="\/cli\/"[^>]*aria-current="page"/);
  assert.doesNotMatch(header, /href="\/desktop\/"[^>]*aria-current/);
});

test('the footer names the publisher, the licence and the repository', () => {
  const footer = renderChrome('footer', { pagePath: '/', title: 'x', description: 'x' });
  assert.match(footer, /StaticBit/);
  assert.match(footer, /Apache-2\.0/);
  assert.ok(footer.includes(REPOSITORY_URL));
});

test('the head links the stylesheets from the site itself and carries the canonical URL', () => {
  const head = renderChrome('head', { pagePath: '/cli/', title: 'CLI', description: 'About the CLI.' });
  assert.match(head, /<link rel="stylesheet" href="\/css\/site\.css">/);
  assert.match(head, /rel="canonical" href="https:\/\/xrpl-vanity\.staticbit\.io\/cli\/"/);
});

test('a page path is the URL the page is served at', () => {
  assert.equal(pagePathOf('index.html'), '/');
  assert.equal(pagePathOf('cli/index.html'), '/cli/');
  assert.equal(pagePathOf('404.html'), '/404');
});

const refusals = [
  ['a third-party stylesheet', 'index.html', PAGE.replace('</head>', '<link rel="stylesheet" href="https://fonts.googleapis.com/x"></head>'), /index\.html.*googleapis/],
  ['inline code', 'index.html', PAGE.replace('</main>', '<script>x()</script></main>'), /index\.html/],
  ['a broken internal link', 'index.html', PAGE.replace('</main>', '<a href="/nope/">x</a></main>'), /index\.html.*\/nope\//],
  ['an unfenced address', 'index.html', PAGE.replace('</main>', '<p>support@staticbit.io</p></main>'), /index\.html/],
];

for (const [label, name, text, message] of refusals) {
  test(`${label} refuses the build and leaves no output`, async (t) => {
    const { root, siteDir, outDir } = await fixture({ [name]: text });
    t.after(() => rm(root, { recursive: true, force: true }));
    await assert.rejects(build({ siteDir, outDir }), message);
    assert.equal(await exists(outDir), false);
  });
}

test('a file over the size limit refuses the build', async (t) => {
  const { root, siteDir, outDir } = await fixture();
  t.after(() => rm(root, { recursive: true, force: true }));
  await assert.rejects(build({ siteDir, outDir, maxFileBytes: 8 }), /limit/);
  assert.equal(await exists(outDir), false);
});

test('an output folder that contains the sources is refused and the sources survive', async (t) => {
  const { root, siteDir } = await fixture();
  t.after(() => rm(root, { recursive: true, force: true }));
  await assert.rejects(build({ siteDir, outDir: root }), /refusing/);
  assert.equal(await exists(join(siteDir, 'src', 'index.html')), true);
});

test('the host headers keep the page to its own origin', () => {
  const headers = readFileSync(new URL('../src/_headers', import.meta.url), 'utf8');
  for (const rule of ["default-src 'self'", "object-src 'none'", "base-uri 'none'", "frame-ancestors 'none'", 'X-Content-Type-Options: nosniff']) {
    assert.ok(headers.includes(rule), rule);
  }
});

// Language-aware build. `langs` and `chromeStrings` are injected so a fixture can carry a language the
// production list has not switched on yet.
const LANG_STRINGS = { ...CHROME_STRINGS, ru: CHROME_STRINGS.en };
const RU_PAGE = PAGE.replace('<html lang="en">', '<html lang="ru">');
const EN_PAGES = ['index.html', ...NAV_PAGES.map((name) => `${name}/index.html`), '404.html'];

async function bilingualFixture(overrides = {}) {
  const fx = await fixture({ 'index.html': PAGE, '404.html': PAGE });
  const ruPages = EN_PAGES.filter((name) => name !== 'privacy/index.html');
  for (const name of ruPages) {
    await mkdir(join(fx.siteDir, 'src', 'ru', dirname(name)), { recursive: true });
    await writeFile(join(fx.siteDir, 'src', 'ru', name), RU_PAGE);
  }
  for (const [name, text] of Object.entries(overrides)) {
    if (text === null) await rm(join(fx.siteDir, 'src', name), { force: true });
    else await writeFile(join(fx.siteDir, 'src', name), text);
  }
  return fx;
}

const buildBoth = (fx) => build({ siteDir: fx.siteDir, outDir: fx.outDir, langs: ['en', 'ru'], chromeStrings: LANG_STRINGS });

test('a page in the ru folder with lang="en" is refused', async (t) => {
  const fx = await bilingualFixture({ 'ru/index.html': PAGE });
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await assert.rejects(buildBoth(fx), /ru\/index\.html.*lang="en"/);
  assert.equal(await exists(fx.outDir), false);
});

test('a page in the site root with lang="ru" is refused', async (t) => {
  const fx = await bilingualFixture({ 'index.html': RU_PAGE });
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await assert.rejects(buildBoth(fx), /index\.html.*lang="ru"/);
});

test('a language folder for a language that is not active is refused', async (t) => {
  const fx = await bilingualFixture();
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await assert.rejects(build({ siteDir: fx.siteDir, outDir: fx.outDir }), /ru.*not in ACTIVE_LANGS/);
});

test('an active language missing desktop/ is refused', async (t) => {
  const fx = await bilingualFixture({ 'ru/desktop/index.html': null });
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await assert.rejects(buildBoth(fx), /ru\/desktop\/index\.html/);
});

test('an active language without chrome strings is refused', async (t) => {
  const fx = await bilingualFixture();
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await assert.rejects(build({ siteDir: fx.siteDir, outDir: fx.outDir, langs: ['en', 'ru'] }), /"ru".*chrome strings/i);
});

test('hreflang alternates list every active language and x-default', async (t) => {
  const fx = await bilingualFixture();
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await buildBoth(fx);
  const html = await readFile(join(fx.outDir, 'ru', 'desktop', 'index.html'), 'utf8');
  assert.ok(html.includes('<link rel="alternate" hreflang="en" href="https://xrpl-vanity.staticbit.io/desktop/">'));
  assert.ok(html.includes('<link rel="alternate" hreflang="ru" href="https://xrpl-vanity.staticbit.io/ru/desktop/">'));
  assert.ok(html.includes('<link rel="alternate" hreflang="x-default" href="https://xrpl-vanity.staticbit.io/desktop/">'));
  assert.ok(html.includes('rel="canonical" href="https://xrpl-vanity.staticbit.io/ru/desktop/"'));
});

test('privacy has a canonical and no alternates', async (t) => {
  const fx = await bilingualFixture();
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await buildBoth(fx);
  const html = await readFile(join(fx.outDir, 'privacy', 'index.html'), 'utf8');
  assert.match(html, /rel="canonical" href="https:\/\/xrpl-vanity\.staticbit\.io\/privacy\/"/);
  assert.doesNotMatch(html, /rel="alternate"/);
});

test('the switcher links the same page in every language and privacy to each home', async (t) => {
  const fx = await bilingualFixture();
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await buildBoth(fx);
  const desktop = await readFile(join(fx.outDir, 'ru', 'desktop', 'index.html'), 'utf8');
  assert.match(desktop, /<details class="lang">/);
  assert.match(desktop, /<a href="\/desktop\/" hreflang="en" lang="en" data-lang="en">English<\/a>/);
  assert.match(desktop, /<a href="\/ru\/desktop\/" hreflang="ru" lang="ru" data-lang="ru" aria-current="true">Русский<\/a>/);
  const privacy = await readFile(join(fx.outDir, 'privacy', 'index.html'), 'utf8');
  assert.match(privacy, /href="\/" hreflang="en"/);
  assert.match(privacy, /href="\/ru\/" hreflang="ru"/);
});

test('the nav carries the language prefix except privacy', async (t) => {
  const fx = await bilingualFixture();
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await buildBoth(fx);
  const html = await readFile(join(fx.outDir, 'ru', 'index.html'), 'utf8');
  assert.ok(html.includes('<a href="/ru/desktop/">'));
  assert.ok(html.includes('<a href="/ru/support/">'));
  assert.ok(html.includes('<a href="/privacy/">'));
  assert.ok(html.includes('class="wordmark" href="/ru/"'));
});

test("the sitemap lists every language's pages and no 404", async (t) => {
  const fx = await bilingualFixture();
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await buildBoth(fx);
  const sitemap = await readFile(join(fx.outDir, 'sitemap.xml'), 'utf8');
  const urls = [...sitemap.matchAll(/<loc>([^<]*)<\/loc>/g)].map((m) => m[1].replace('https://xrpl-vanity.staticbit.io', ''));
  assert.deepEqual(urls, ['/', '/cli/', '/desktop/', '/privacy/', '/ru/', '/ru/cli/', '/ru/desktop/', '/ru/support/', '/support/']);
});

test('the 404 page keeps the switcher on the language homes', async (t) => {
  const fx = await bilingualFixture();
  t.after(() => rm(fx.root, { recursive: true, force: true }));
  await buildBoth(fx);
  const html = await readFile(join(fx.outDir, 'ru', '404.html'), 'utf8');
  assert.match(html, /href="\/" hreflang="en"/);
  assert.match(html, /href="\/ru\/" hreflang="ru"/);
  assert.doesNotMatch(html, /rel="alternate"/);
});

test('with English alone the menu has one entry and the head one alternate plus x-default', async (t) => {
  const { root, siteDir, outDir } = await fixture();
  t.after(() => rm(root, { recursive: true, force: true }));
  await build({ siteDir, outDir });
  const html = await readFile(join(outDir, 'cli', 'index.html'), 'utf8');
  assert.equal([...html.matchAll(/data-lang="/g)].length, 1);
  assert.equal([...html.matchAll(/rel="alternate"/g)].length, 2);
});

test('the chrome strings carry every key of the brief for English', () => {
  const en = CHROME_STRINGS.en;
  for (const key of ['skip', 'language', 'published', 'licence', 'source', 'ogImageAlt']) assert.equal(typeof en[key], 'string', key);
  for (const key of ['desktop', 'cli', 'privacy', 'support']) assert.equal(typeof en.nav[key], 'string', key);
});
