// The contrast report: every text and control colour pair the site uses, computed from tokens.css.
//
//   node site/tools/contrast.mjs            writes docs/site-contrast.md
//   node site/tools/contrast.mjs --check    prints the failures and exits non-zero if there are any
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { contrast, parseColor, toHex } from './color.mjs';

const here = dirname(fileURLToPath(import.meta.url));

export const REPORT_PATH = join(here, '..', '..', 'docs', 'site-contrast.md');
const TOKENS_PATH = join(here, '..', 'src', 'css', 'tokens.css');

const TEXT = 4.5;
const UI = 3;

const PAIRS = [
  ['Body text', 'color-ink', 'color-paper', TEXT],
  ['Body text in a well', 'color-ink', 'color-paper-2', TEXT],
  ['Body text on a hovered row', 'color-ink', 'color-paper-3', TEXT],
  ['Secondary text', 'color-ink-2', 'color-paper', TEXT],
  ['Secondary text in a well', 'color-ink-2', 'color-paper-2', TEXT],
  ['Secondary text on a hovered row', 'color-ink-2', 'color-paper-3', TEXT],
  ['Muted text (labels, comments, disabled control)', 'color-muted', 'color-paper', TEXT],
  ['Muted text in a well', 'color-muted', 'color-paper-2', TEXT],
  ['Accent as text', 'color-accent', 'color-paper', TEXT],
  ['Accent as text in a well', 'color-accent', 'color-paper-2', TEXT],
  ['Text on the accent fill (primary control, matched word)', 'color-accent-ink', 'color-accent', TEXT],
  ['Text on the accent wash (selected row)', 'color-ink', 'color-accent-wash', TEXT],
  ['Focus ring', 'color-focus', 'color-paper', UI],
  ['Focus ring in a well', 'color-focus', 'color-paper-2', UI],
  ['Secondary control border', 'color-ink-2', 'color-paper', UI],
  ['Rule between sections (decorative)', 'color-rule', 'color-paper', null],
  ['Table head and well rule (decorative)', 'color-rule-2', 'color-paper', null],
  ['Dimmest glyph in the field (decorative)', 'glyph-0', 'color-paper', null],
  ['Brightest glyph in the field (decorative)', 'glyph-7', 'color-paper', null],
];

/** Token values of the :root block. */
export function readTokens(css) {
  const open = css.indexOf('{', css.indexOf(':root'));
  const body = css.slice(open + 1, css.indexOf('}', open));
  return new Map([...body.matchAll(/--([a-z0-9-]+):\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]));
}

export function allRows(css = readFileSync(TOKENS_PATH, 'utf8')) {
  const tokens = readTokens(css);
  const colour = (name) => {
    const value = tokens.get(name);
    if (value === undefined) throw new Error(`token --${name} is not defined in tokens.css`);
    return parseColor(value);
  };
  return PAIRS.map(([label, foreground, background, minimum]) => {
    const fg = colour(foreground);
    const bg = colour(background);
    const ratio = contrast(fg, bg);
    return {
      label,
      foreground: `--${foreground}`,
      background: `--${background}`,
      foregroundHex: toHex(fg),
      backgroundHex: toHex(bg),
      ratio,
      minimum,
      pass: minimum === null ? null : ratio >= minimum,
    };
  });
}

export function failures(rows) {
  return rows.filter((r) => r.pass === false);
}

function formatRatio(value) {
  return `${(Math.floor(value * 100) / 100).toFixed(2)}:1`;
}

export function renderReport(rows) {
  const failed = failures(rows).length;
  const lines = [
    '# Site contrast',
    '',
    'WCAG 2.2 AA, computed from `site/src/css/tokens.css` by `node site/tools/contrast.mjs`.',
    '`site/test/theme.test.mjs` fails when a required pair misses its threshold or when this file is out',
    'of date. Ratios are the WCAG 2.x relative-luminance contrast, truncated to two decimals.',
    '',
    '- Text: at least 4.5:1. Controls and graphics that carry information: at least 3:1.',
    '- Rows marked "exempt" carry no information and have no threshold.',
    '',
    `Result: ${failed === 0 ? 'every required pair passes.' : `${failed} required pair(s) FAIL.`}`,
    '',
    '| Pair | Foreground | Background | Ratio | Needs | Result |',
    '|---|---|---|---|---|---|',
  ];
  for (const r of rows) {
    const needs = r.minimum === null ? 'exempt' : `${r.minimum}:1`;
    const result = r.pass === null ? 'n/a' : r.pass ? 'pass' : 'FAIL';
    lines.push(`| ${r.label} | \`${r.foreground}\` ${r.foregroundHex} | \`${r.background}\` ${r.backgroundHex} | ${formatRatio(r.ratio)} | ${needs} | ${result} |`);
  }
  lines.push('');
  return lines.join('\n');
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const rows = allRows();
  if (process.argv.includes('--check')) {
    for (const r of failures(rows)) console.error(`FAIL ${r.label} ${formatRatio(r.ratio)} < ${r.minimum}:1`);
    process.exitCode = failures(rows).length > 0 ? 1 : 0;
  } else {
    writeFileSync(REPORT_PATH, renderReport(rows));
    console.log(`wrote ${REPORT_PATH} (${rows.length} rows, ${failures(rows).length} failing)`);
  }
}
