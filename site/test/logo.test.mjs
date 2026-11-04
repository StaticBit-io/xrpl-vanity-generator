import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');
const LOGO_GREEN = '#7ef473';
const LOGO_PAPER = '#040805';

const svgs = {
  favicon: read('../src/favicon.svg'),
  appicon: read('../../src/XrplVanity.App/Resources/AppIcon/appicon.svg'),
  splash: read('../../src/XrplVanity.App/Resources/Splash/splash.svg'),
};
const navRail = read('../../src/XrplVanity.App.Core/Layout/NavRail.razor');

const pathOf = (text) => /<path\b[^>]*\sd="([^"]+)"/.exec(text)?.[1];

test('the site and the application draw the same r', () => {
  const expected = pathOf(svgs.favicon);
  assert.ok(expected);
  assert.equal(pathOf(svgs.appicon), expected);
  assert.equal(pathOf(svgs.splash), expected);
  assert.equal(pathOf(navRail), expected);
});

test('the mark is an outline, so it needs no font', () => {
  for (const [name, text] of Object.entries(svgs)) assert.doesNotMatch(text, /<text\b/, name);
});

test('the mark is the site green on the site black', () => {
  for (const [name, text] of Object.entries(svgs)) {
    assert.ok(text.toLowerCase().includes(LOGO_GREEN), name);
    assert.ok(text.toLowerCase().includes(LOGO_PAPER), name);
  }
});

test('the temporary V is gone', () => {
  for (const text of [...Object.values(svgs), navRail]) assert.ok(!text.includes('M60 78'));
});

test('the icon background matches the tile, so no frame shows around it', () => {
  const project = read('../../src/XrplVanity.App/XrplVanity.App.csproj');
  assert.match(project, /<MauiIcon [^>]*Color="#040805"/);
  assert.match(project, /<MauiSplashScreen [^>]*Color="#040805"/);
});

test('the tray icon carries the sizes Windows asks for', () => {
  const ico = readFileSync(new URL('../../src/XrplVanity.App/Resources/Raw/tray.ico', import.meta.url));
  assert.equal(ico.readUInt16LE(2), 1);
  const count = ico.readUInt16LE(4);
  const widths = Array.from({ length: count }, (_, index) => ico[6 + 16 * index] || 256);
  for (const size of [16, 24, 32, 48]) assert.ok(widths.includes(size), `${size}: ${widths}`);
});

test('the mark in the application rail sits on the same black', () => {
  assert.match(read('../../src/XrplVanity.App.Core/Layout/NavRail.razor.css'), /\.mono-mark\s*{[^}]*background: #040805/);
});
