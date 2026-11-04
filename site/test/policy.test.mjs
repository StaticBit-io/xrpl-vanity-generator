import test from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { ISSUES_URL } from '../src/js/config.mjs';

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');
const policy = read('../../PRIVACY.md');

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

test("the application's sections are the policy's own text", () => {
  for (const { heading, body } of markdownSections(policy)) {
    if (heading === 'Contact') continue;
    const section = pageSection(slug(heading));
    assert.ok(section, heading);
    assert.equal(fromHtml(section), fromMarkdown(body), heading);
  }
});

test('the page adds only the section about the website', () => {
  const known = new Set(markdownSections(policy).map((section) => slug(section.heading)));
  const extra = [...privacy.matchAll(/<section data-policy="([^"]+)">/g)].map((match) => match[1]).filter((name) => !known.has(name));
  assert.deepEqual(extra, ['this-website']);
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

test('the page and the policy carry the same date', () => {
  const date = (text) => /Last updated: (\d{4}-\d{2}-\d{2})/.exec(text)?.[1];
  assert.ok(date(policy));
  assert.equal(date(privacy), date(policy));
});

test('the policy says what leaves the computer only when the user moves it', () => {
  const summary = markdownSections(policy).find((section) => section.heading === 'Summary').body;
  assert.doesNotMatch(collapse(summary), /stays on the computer that produced it\.$/);
  assert.match(collapse(summary), /QR code/);
});

test('the policy says a copied seed stays out of clipboard history and sync', () => {
  const clipboard = collapse(markdownSections(policy).find((section) => section.heading === 'Clipboard').body);
  assert.match(clipboard, /stays out of Windows clipboard history and clipboard sync/);
  assert.doesNotMatch(clipboard, /keep their own copy/);
});

test('the policy says uninstalling deletes the seeds', () => {
  const removing = collapse(markdownSections(policy).find((section) => section.heading === 'Removing your data').body);
  assert.match(removing, /Uninstalling[^.]*results\.dat[^.]*every seed/);
});

test('the policy names the support address', () => {
  const contact = markdownSections(policy).find((section) => section.heading === 'Contact').body;
  assert.match(contact, /support@staticbit\.io/);
  assert.ok(!contact.includes(ISSUES_URL));
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

test('the Store documents point at the site for the privacy policy', () => {
  for (const name of ['store-listing.md', 'store-submission.md']) {
    const text = read(`../../docs/${name}`);
    assert.ok(text.includes('https://xrpl-vanity.staticbit.io/privacy/'), name);
    assert.ok(!text.includes('blob/main/PRIVACY.md'), name);
  }
});
