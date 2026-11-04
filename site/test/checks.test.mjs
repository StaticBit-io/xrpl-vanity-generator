import test from 'node:test';
import assert from 'node:assert/strict';
import { findBrokenLinks, findExternalRefs, findInlineCode, findUnfencedEmails } from '../checks.mjs';

test('a stylesheet, script or font on another origin is an external reference', () => {
  assert.equal(findExternalRefs('a.html', '<link rel="stylesheet" href="https://fonts.googleapis.com/x">').length, 1);
  assert.equal(findExternalRefs('a.html', '<script src="//cdn.example/x.js"></script>').length, 1);
  assert.equal(findExternalRefs('a.html', '<img src="http://x.example/a.png">').length, 1);
  assert.equal(findExternalRefs('a.css', '@font-face { src: url(https://x.example/y.woff2); }').length, 1);
  assert.equal(findExternalRefs('a.css', '@import "https://x.example/y.css";').length, 1);
});

test('a link a visitor clicks and an own-origin asset are not external references', () => {
  assert.deepEqual(findExternalRefs('a.html', '<a href="https://github.com/x">x</a>'), []);
  assert.deepEqual(findExternalRefs('a.css', '@font-face { src: url(/fonts/a.woff2); }'), []);
  assert.deepEqual(findExternalRefs('a.html', '<link rel="canonical" href="https://xrpl-vanity.staticbit.io/">'), []);
  assert.deepEqual(findExternalRefs('a.html', '<meta property="og:url" content="https://xrpl-vanity.staticbit.io/">'), []);
});

test('the CSP forbids inline code, so inline code is reported', () => {
  assert.equal(findInlineCode('a.html', '<script>x()</script>').length, 1);
  assert.equal(findInlineCode('a.html', '<p style="color:red">x</p>').length, 1);
  assert.equal(findInlineCode('a.html', '<a onclick="x()">x</a>').length, 1);
  assert.equal(findInlineCode('a.html', '<style>p{}</style>').length, 1);
});

test('a module script from a file and ordinary prose are not inline code', () => {
  assert.deepEqual(findInlineCode('a.html', '<script type="module" src="/js/home.mjs"></script>'), []);
  assert.deepEqual(findInlineCode('a.html', '<p>the style= attribute and one onclick= are words here</p>'), []);
});

test('a root-relative link resolves to a built file, with the fragment dropped', () => {
  const files = new Set(['privacy/index.html', 'index.html', 'css/site.css']);
  const pages = new Map([['index.html', '<a href="/privacy/#contact">p</a><a href="/">h</a><link href="/css/site.css">']]);
  assert.deepEqual(findBrokenLinks(files, pages), []);
});

test('a root-relative link with no built file is broken', () => {
  const pages = new Map([['index.html', '<a href="/nope/">x</a>']]);
  const broken = findBrokenLinks(new Set(['index.html']), pages);
  assert.equal(broken.length, 1);
  assert.match(broken[0], /index\.html.*\/nope\//);
});

test('an external or mail link is not checked as an internal one', () => {
  const pages = new Map([['index.html', '<a href="https://x.example/nope/">x</a><a href="mailto:a@b.c">m</a><a href="#top">t</a>']]);
  assert.deepEqual(findBrokenLinks(new Set(['index.html']), pages), []);
});

test('an address outside the email_off fence is reported, in text and in a mailto link', () => {
  assert.equal(findUnfencedEmails('a.html', '<p>write to support@staticbit.io</p>').length, 1);
  assert.equal(findUnfencedEmails('a.html', '<a href="mailto:support@staticbit.io">mail</a>').length, 1);
});

test('an address inside the fence passes, and one after the fence does not', () => {
  const fenced = '<!--email_off--><a href="mailto:support@staticbit.io">support@staticbit.io</a><!--/email_off-->';
  assert.deepEqual(findUnfencedEmails('a.html', fenced), []);
  assert.equal(findUnfencedEmails('a.html', `${fenced} support@staticbit.io`).length, 1);
});
