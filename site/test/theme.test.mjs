import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { allRows, failures, renderReport, REPORT_PATH } from '../tools/contrast.mjs';
import { contrast, parseColor } from '../tools/color.mjs';

const css = (name) => readFileSync(new URL(`../src/css/${name}`, import.meta.url), 'utf8');
const FONTS = new URL('../src/fonts/', import.meta.url);

test('every glyph is Martian Mono', () => {
  assert.match(css('tokens.css'), /--font-display: "Martian Mono"/);
});

test('the font comes from the site and carries the full width axis', () => {
  const face = /@font-face\s*{[^}]*}/.exec(css('base.css'))?.[0] ?? '';
  assert.match(face, /font-family: "Martian Mono"/);
  assert.match(face, /font-stretch: 75% 112\.5%/);
  assert.match(face, /font-display: swap/);
  const file = /url\("?\/fonts\/([^")]+)/.exec(face)?.[1];
  assert.ok(file, 'the face names a file under /fonts/');
  assert.ok(existsSync(new URL(file, FONTS)), file);
});

test('every font face is a shipped file, listed with its hash, and the Cyrillic one exists', () => {
  const faces = css('base.css').match(/@font-face\s*{[^}]*}/g) ?? [];
  // A Windows checkout with core.autocrlf turns the file CRLF; the pattern below spans lines.
  const sources = readFileSync(new URL('SOURCES.txt', FONTS), 'utf8').replaceAll('\r\n', '\n');
  const files = faces.map((face) => /url\("?\/fonts\/([^")]+)/.exec(face)?.[1]);
  for (const [i, file] of files.entries()) {
    assert.ok(file, `face ${i} names a file under /fonts/`);
    assert.ok(existsSync(new URL(file, FONTS)), file);
    assert.match(sources, new RegExp(`-> ${file.replaceAll('.', '\\.')} .*\\n.*\\n\\s+sha256 [0-9a-f]{64}`), `${file} in SOURCES.txt`);
    assert.match(faces[i], /unicode-range: U\+/, `${file} names its range, so faces split by script`);
  }
  assert.ok(files.some((file) => /cyrillic/.test(file)), 'a Cyrillic face ships for the Russian pages');
});

test('no stylesheet asks Google for a font', () => {
  for (const name of readdirSync(new URL('../src/css/', import.meta.url))) {
    assert.doesNotMatch(css(name), /googleapis|gstatic/, name);
  }
});

test('the font ships with its source and its licence', () => {
  const sources = readFileSync(new URL('SOURCES.txt', FONTS), 'utf8');
  assert.match(sources, /@fontsource-variable\/martian-mono@5\.3\.0/);
  assert.match(readFileSync(new URL('MartianMono-OFL.txt', FONTS), 'utf8'), /SIL OPEN FONT LICENSE/i);
});

test('the contrast arithmetic matches known WCAG values', () => {
  assert.equal(contrast(parseColor('#000000'), parseColor('#ffffff')).toFixed(2), '21.00');
  assert.equal(contrast(parseColor('#777777'), parseColor('#ffffff')).toFixed(2), '4.48');
});

test('every required text and control pair meets AA', () => {
  const rows = allRows();
  assert.ok(rows.length >= 14, 'the table is not empty');
  assert.deepEqual(failures(rows).map((r) => `${r.label} ${r.ratio.toFixed(2)} < ${r.minimum}`), []);
});

test('the report is the one for the current tokens', () => {
  // A Windows checkout may hold the file with CRLF; the report itself is LF.
  assert.equal(readFileSync(REPORT_PATH, 'utf8').replaceAll('\r\n', '\n'), renderReport(allRows()), 'run: node site/tools/contrast.mjs');
});
