// The markup every page shares, filled in at build time so it needs no script: the head, the header
// and the footer behind <!--@chrome:...--> markers, and the support address behind <!--@support-email-->.
import { REPOSITORY_URL, SITE_ORIGIN, SUPPORT_EMAIL } from './src/js/config.mjs';

const NAV = [
  ['/desktop/', 'Desktop app'],
  ['/cli/', 'CLI'],
  ['/privacy/', 'Privacy'],
  ['/support/', 'Support'],
];

function escapeHtml(text) {
  return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
}

function renderHead({ pagePath, title, description }) {
  const url = `${SITE_ORIGIN}${pagePath}`;
  return [
    '<meta charset="utf-8">',
    '<meta name="viewport" content="width=device-width, initial-scale=1">',
    '<meta name="color-scheme" content="dark">',
    `<link rel="canonical" href="${url}">`,
    '<link rel="icon" href="/favicon.svg" type="image/svg+xml">',
    '<link rel="stylesheet" href="/css/tokens.css">',
    '<link rel="stylesheet" href="/css/base.css">',
    '<link rel="stylesheet" href="/css/site.css">',
    '<meta property="og:type" content="website">',
    '<meta property="og:site_name" content="XRPL Vanity">',
    `<meta property="og:url" content="${url}">`,
    `<meta property="og:title" content="${escapeHtml(title ?? '')}">`,
    `<meta property="og:description" content="${escapeHtml(description ?? '')}">`,
    `<meta property="og:image" content="${SITE_ORIGIN}/og-banner.png">`,
    '<meta property="og:image:width" content="1200">',
    '<meta property="og:image:height" content="630">',
    `<meta property="og:image:alt" content="An XRP Ledger address carrying the word STATiCBiT, found in a field of address characters">`,
    '<meta name="twitter:card" content="summary_large_image">',
  ].join('\n');
}

function renderHeader({ pagePath }) {
  const links = NAV.map(([href, label]) => {
    const current = href === pagePath ? ' aria-current="page"' : '';
    return `<a href="${href}"${current}>${label}</a>`;
  });
  return `<a class="skip" href="#main">Skip to content</a>
<header class="site-header">
<a class="wordmark" href="/">xrpl-vanity</a>
<nav aria-label="Pages">
${links.join('\n')}
</nav>
</header>`;
}

function renderFooter() {
  return `<footer class="site-footer">
<p>Published by <a href="https://staticbit.io">StaticBit</a>.</p>
<p>Apache-2.0 licence. <a href="${REPOSITORY_URL}">Source on GitHub</a>.</p>
</footer>`;
}

/** The address inside Cloudflare's email_off fence: see findUnfencedEmails in checks.mjs. */
export function supportEmailHtml() {
  return `<!--email_off--><a href="mailto:${SUPPORT_EMAIL}">${SUPPORT_EMAIL}</a><!--/email_off-->`;
}

/** @param {'head'|'header'|'footer'} part @param {{pagePath: string, title: string|null, description: string|null}} page */
export function renderChrome(part, page) {
  if (part === 'head') return renderHead(page);
  if (part === 'header') return renderHeader(page);
  if (part === 'footer') return renderFooter();
  throw new Error(`unknown chrome marker "${part}"`);
}
