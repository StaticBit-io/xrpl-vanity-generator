import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');
const textOf = (markup) => markup.replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>');

const readme = read('../../README.md');
const cli = read('../src/cli/index.html');
const desktop = read('../src/desktop/index.html');

/** The flags of the README's Flags table: the first code span of each row. */
function readmeFlags() {
  const section = readme.slice(readme.indexOf('## Flags'), readme.indexOf('XRPL addresses use the base58 alphabet'));
  return [...section.matchAll(/^\| `(--[a-z-]+)/gm)].map((match) => match[1]).sort();
}

/** The flags the parser accepts. */
function sourceFlags() {
  return [...read('../../src/XrplVanity.Cli/CliOptions.cs').matchAll(/case "(--[a-z-]+)":/g)].map((match) => match[1]).sort();
}

/** The flags of the Options table on the page: the first token of each row head. */
function pageFlags() {
  const table = /aria-label="Command-line options"[\s\S]*?<\/table>/.exec(cli)[0];
  return [...table.matchAll(/<th scope="row"><code>(--[a-z-]+)/g)].map((match) => match[1]).sort();
}

test('the README table is found', () => {
  assert.ok(readmeFlags().length >= 15);
  assert.ok(readmeFlags().includes('--gpu-duty'));
});

test('the page, the README and the parser list exactly the same flags', () => {
  assert.deepEqual(pageFlags(), sourceFlags());
  assert.deepEqual(readmeFlags(), sourceFlags());
});

test('a flag that takes one value is not shown as taking several', () => {
  assert.doesNotMatch(textOf(cli), /--(starts|ends|contains) P\.\.\./);
});

test('the build steps work without the MAUI workload', () => {
  const text = textOf(cli);
  assert.doesNotMatch(text, /dotnet build -c Release/);
  assert.match(text, /dotnet run --project src\/XrplVanity\.Cli -c Release --/);
});

test('the short name in the examples is explained', () => {
  assert.match(textOf(cli), /xrpl-vanity stands for dotnet run --project src\/XrplVanity\.Cli -c Release --/);
});

test('an example does not name a word list the repository lacks', () => {
  assert.doesNotMatch(cli, /google20kwords/);
});

test('the CLI page shows how to read the output', () => {
  const text = textOf(cli);
  assert.match(text, /Address: r\.\.\.\s+Seed: sEd\.\.\./);
  assert.match(text, /--out[^.]*tab/);
});

test('case-insensitive matching is limited to what the search does', () => {
  assert.match(textOf(cli), /12 characters/);
});

test('the first-character penalty is stated against the length, as the docs do', () => {
  assert.doesNotMatch(textOf(cli), /takes about 23 times longer/);
  assert.match(textOf(cli), /about 23 times rarer than its length suggests/);
});

test('the CLI page gives the CLI duty range and says to build from source', () => {
  const text = textOf(cli);
  assert.match(text, /1.100/);
  assert.match(text, /\.NET 10 SDK/);
  assert.doesNotMatch(cli, /\/releases/);
});

test('the desktop page gives the application duty range and its protections', () => {
  const text = textOf(desktop);
  assert.match(text, /25.100/);
  for (const phrase of ['DPAPI', 'WebView2', '1809', 'QR', 'password']) assert.ok(text.includes(phrase), phrase);
});

test('the desktop page warns that a TSV export is not protected', () => {
  assert.match(textOf(desktop), /TSV[\s\S]{0,200}plain text/);
});

test('the desktop page says uninstalling deletes the seeds', () => {
  assert.match(textOf(desktop), /Uninstalling[^.]*results\.dat/);
});

test('the desktop page says a bare-seed code is never encrypted', () => {
  assert.match(textOf(desktop), /bare seed[\s\S]{0,160}never encrypted/);
});

test('the desktop page warns about clipboard history', () => {
  assert.match(textOf(desktop), /clipboard history/i);
});

test('the desktop page names what a first run needs for an estimate', () => {
  assert.match(textOf(desktop), /measure speed/);
});

test('the desktop page names the oldest cards CUDA runs on', () => {
  assert.match(textOf(desktop), /Turing/);
});

test('developer-only material stays on GitHub, and the page links it', () => {
  const text = textOf(desktop);
  for (const phrase of ['Add-AppxPackage', 'MSIX-win-x64', 'smoke checklist', 'make-cert']) {
    assert.ok(!text.includes(phrase), phrase);
  }
  assert.match(desktop, /href="https:\/\/github\.com\/StaticBit-io\/xrpl-vanity-generator\/blob\/main\/docs\/desktop-app\.md"/);
});

for (const [name, html] of [['cli', cli], ['desktop', desktop]]) {
  test(`${name}: no picture, one h1, and heading levels never skip`, () => {
    assert.doesNotMatch(html, /<img\b/);
    const levels = [...html.matchAll(/<h([1-6])\b/g)].map((match) => Number(match[1]));
    assert.equal(levels.filter((level) => level === 1).length, 1);
    assert.equal(levels[0], 1);
    levels.forEach((level, index) => {
      if (index > 0) assert.ok(level <= levels[index - 1] + 1, `h${levels[index - 1]} then h${level}`);
    });
  });

  test(`${name}: wide content scrolls in its own box`, () => {
    assert.doesNotMatch(html.replace(/<div class="reference"[^>]*>\s*<table[\s\S]*?<\/table>/g, ''), /<table\b/);
    assert.doesNotMatch(html.replace(/<div class="well"[^>]*>\s*<pre[\s\S]*?<\/pre>/g, ''), /<pre\b/);
  });

  test(`${name}: the copy has no long dash`, () => {
    assert.doesNotMatch(textOf(html), /—/);
  });
}

test('a reference box scrolls by itself', () => {
  assert.match(read('../src/css/site.css'), /\.reference\s*{[^}]*overflow-x: auto/);
});
