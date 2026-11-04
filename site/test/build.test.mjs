import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { build, pagePathOf } from '../build.mjs';
import { renderChrome, supportEmailHtml } from '../chrome-markup.mjs';
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
  for (const name of ['css/tokens.css', 'css/base.css', 'css/site.css', 'favicon.svg']) {
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
