import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { ISSUES_URL } from '../src/js/config.mjs';

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');
const REPO_ROOT = fileURLToPath(new URL('../..', import.meta.url));
const STORE_BRANCH = 'claude/store-submission-c14468';

/** PRIVACY.md lives on the Store branch until that merges; afterwards it is at the repository root. */
function readPolicy() {
  const file = new URL('../../PRIVACY.md', import.meta.url);
  if (existsSync(file)) return readFileSync(file, 'utf8');
  try {
    return execFileSync('git', ['show', `${STORE_BRANCH}:PRIVACY.md`], { cwd: REPO_ROOT, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
  } catch {
    return null;
  }
}

const decode = (text) => text.replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"');
const collapse = (text) => text.replace(/\s+/g, ' ').trim();
const fromHtml = (html) => collapse(decode(html.replace(/<h2[^>]*>[\s\S]*?<\/h2>/, '').replace(/<[^>]+>/g, ' '))).replace(/ ([.,:;])/g, '$1');
const fromMarkdown = (text) => collapse(text.replace(/^\|[\s:|-]+\|$/gm, '').replace(/[|`*]/g, ' ')).replace(/ ([.,:;])/g, '$1');
const slug = (heading) => heading.toLowerCase().replace(/[^a-z0-9]+/g, '-');

function markdownSections(policy) {
  const parts = policy.split(/^## /m).slice(1);
  return parts.map((part) => {
    const newline = part.indexOf('\n');
    return { heading: part.slice(0, newline).trim(), body: part.slice(newline + 1) };
  });
}

const privacy = read('../src/privacy/index.html');
const support = read('../src/support/index.html');
const pageSection = (name) => new RegExp(`<section data-policy="${name}">([\\s\\S]*?)</section>`).exec(privacy)?.[1];

test("the application's sections are the policy's own text", (t) => {
  const policy = readPolicy();
  if (policy === null) return t.skip('PRIVACY.md not available');
  for (const { heading, body } of markdownSections(policy)) {
    if (heading === 'Contact') continue;
    const section = pageSection(slug(heading));
    assert.ok(section, heading);
    assert.equal(fromHtml(section), fromMarkdown(body), heading);
  }
});

test('the page adds only the QR, website and contact sections', (t) => {
  const policy = readPolicy();
  if (policy === null) return t.skip('PRIVACY.md not available');
  const known = new Set(markdownSections(policy).map((section) => slug(section.heading)));
  const extra = [...privacy.matchAll(/<section data-policy="([^"]+)">/g)].map((match) => match[1]).filter((name) => !known.has(name));
  assert.deepEqual(extra.sort(), ['qr-codes', 'this-website']);
});

test('the QR section says a code without a password is not encrypted', () => {
  assert.match(fromHtml(pageSection('qr-codes')), /without (one|a password)[^.]*not encrypted/i);
});

test('the QR section says a bare-seed code is never encrypted', () => {
  assert.match(fromHtml(pageSection('qr-codes')), /bare seed[^.]*never encrypted/i);
});

test('the website section promises what the site does and names no host', () => {
  const text = fromHtml(pageSection('this-website'));
  assert.doesNotMatch(text, /Cloudflare/);
  assert.match(text, /no cookies/i);
  assert.match(text, /no analytics/i);
});

test('the contact is the support address, fenced from Cloudflare', () => {
  const section = pageSection('contact');
  assert.ok(section.includes('<!--@support-email-->'));
  assert.ok(!section.includes(ISSUES_URL));
});

test('the page carries its own date, not the file it came from', () => {
  const match = /Last updated: (\d{4}-\d{2}-\d{2})/.exec(privacy);
  assert.ok(match);
  assert.notEqual(match[1], '2026-09-16');
});

test('the support page gives the address, the tracker and the common problems', () => {
  assert.ok(support.includes('<!--@support-email-->'));
  assert.ok(support.includes(`href="${ISSUES_URL}"`));
  const text = decode(support.replace(/<[^>]+>/g, ' '));
  assert.match(text, /WebView2/);
  assert.match(text, /No devices/);
  assert.match(text, /History is read-only/);
});

test('the support page warns a visitor never to send a seed', () => {
  assert.match(support, /never[^.]*seed/i);
});

test('no script sets a cookie', () => {
  const folder = new URL('../src/js/', import.meta.url);
  for (const name of readdirSync(folder)) assert.doesNotMatch(readFileSync(new URL(name, folder), 'utf8'), /document\.cookie/, name);
});
