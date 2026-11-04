// Draws the site's link-card banner, site/src/og-banner.png (1200 x 630), the picture Telegram, X and other
// link previews show:
//
//   node site/tools/og-banner.mjs
//
// The banner is one address found in a field of address characters, with the brand as the matched word.
// The address is an example: it fits the alphabet but has no valid checksum, so a wallet rejects it.
//
// Headless Chrome or Edge renders the page; CHROME overrides the browser path. The PNG is committed, since a
// browser's rendering is not byte-stable, and link-card.test.mjs checks its size.
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { glyphRows } from '../src/js/glyph-field.mjs';

const here = dirname(fileURLToPath(import.meta.url));

export const BANNER_FILE = join(here, '..', 'src', 'og-banner.png');
export const BANNER_WIDTH = 1200;
export const BANNER_HEIGHT = 630;
export const BANNER_WORD = 'STATiCBiT';
export const BANNER_ADDRESS = `r${BANNER_WORD}Hgs4kQe7tWvNpYcX3bLm9fZ`;

const FONT_FILE = 'martian-mono-5.3.0-latin.woff2';
const FIELD = [66, 15, 58];
const CHROMES = [
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
];

/** One row in three has a brighter run, so the field reads as a search rather than a flat texture. */
function fieldMarkup() {
  return glyphRows(...FIELD).map((row, y) => (y % 3 === 1 ? row.replace(/(.{7})(.{5})/, '$1<i>$2</i>') : row)).join('\n');
}

export function renderBanner() {
  const tokens = readFileSync(join(here, '..', 'src', 'css', 'tokens.css'), 'utf8');
  const rest = BANNER_ADDRESS.slice(1 + BANNER_WORD.length);
  return `<!doctype html>
<html lang="en">
<meta charset="utf-8">
<style>
${tokens}
@font-face { font-family: "Martian Mono"; font-weight: 100 800; font-stretch: 75% 112.5%; src: url("${FONT_FILE}") format("woff2"); }
html, body { margin: 0; }
body { width: ${BANNER_WIDTH}px; height: ${BANNER_HEIGHT}px; position: relative; overflow: hidden; background: var(--color-paper); color: var(--color-ink); font-family: var(--font-display); }
.noise { position: absolute; inset: 0; margin: 0; padding: 18px 26px; font: inherit; font-size: 25px; line-height: 1.5; font-stretch: 75%; color: var(--glyph-0); white-space: pre; }
.noise i { color: var(--glyph-2); font-style: normal; }
.found { position: absolute; left: 0; right: 0; top: 246px; padding: 26px 0; background: var(--color-paper); border-block: 2px solid var(--color-rule-2); text-align: center; font-size: 47px; font-weight: 500; font-stretch: 87.5%; }
mark { background: var(--color-accent); color: var(--color-accent-ink); }
.name { position: absolute; left: 40px; bottom: 28px; padding: 4px 12px; background: var(--color-paper); font-size: 30px; font-weight: 500; font-stretch: 112.5%; }
</style>
<pre class="noise">${fieldMarkup()}</pre>
<div class="found">r<mark>${BANNER_WORD}</mark>${rest}</div>
<div class="name">xrpl-vanity</div>
</html>
`;
}

function findChrome() {
  const chrome = [process.env.CHROME, ...CHROMES].find((path) => path && existsSync(path));
  if (!chrome) throw new Error('no Chrome or Edge found; set CHROME');
  return chrome;
}

function shoot() {
  const folder = mkdtempSync(join(tmpdir(), 'xrpl-vanity-banner-'));
  try {
    const page = join(folder, 'banner.html');
    writeFileSync(page, renderBanner());
    copyFileSync(join(here, '..', 'src', 'fonts', FONT_FILE), join(folder, FONT_FILE));
    const result = spawnSync(findChrome(), [
      '--headless=new',
      '--disable-gpu',
      '--hide-scrollbars',
      '--allow-file-access-from-files',
      '--force-device-scale-factor=1',
      `--user-data-dir=${join(folder, 'profile')}`,
      `--window-size=${BANNER_WIDTH},${BANNER_HEIGHT}`,
      '--virtual-time-budget=4000',
      `--screenshot=${BANNER_FILE}`,
      pathToFileURL(page).href,
    ], { stdio: 'ignore', timeout: 60000 });
    if (result.status !== 0) throw new Error(`the browser exited with ${result.status ?? result.error}`);
  } finally {
    rmSync(folder, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  shoot();
  console.log(`wrote ${BANNER_FILE}`);
}
