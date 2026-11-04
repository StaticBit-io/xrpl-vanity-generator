// The promises the site makes about itself, as checks over the built files. Each finder returns one
// line per problem, already naming the file, so build.mjs only has to collect them.

const COMMENT = /<!--[\s\S]*?-->/g;
const ABSOLUTE = /^(?:[a-z][a-z0-9+.-]*:)?\/\//i;
const stripComments = (html) => html.replace(COMMENT, '');

function attribute(tag, name) {
  const match = new RegExp(`\\s${name}\\s*=\\s*(?:"([^"]*)"|'([^']*)'|([^\\s>]+))`, 'i').exec(tag);
  return match ? (match[1] ?? match[2] ?? match[3]) : null;
}

/**
 * URLs on another origin that a browser would fetch by itself. Links a visitor clicks do not count,
 * and neither does rel="canonical", which names this site and is never requested.
 */
export function findExternalRefs(name, text) {
  const found = [];
  const report = (url) => found.push(`${name}: loads ${url}`);
  if (name.endsWith('.html')) {
    for (const [tag] of stripComments(text).matchAll(/<(?:script|img|iframe|source|video|audio|embed|track)\b[^>]*>/gi)) {
      const src = attribute(tag, 'src');
      if (src !== null && ABSOLUTE.test(src)) report(src);
    }
    for (const [tag] of stripComments(text).matchAll(/<link\b[^>]*>/gi)) {
      const href = attribute(tag, 'href');
      if (href !== null && ABSOLUTE.test(href) && attribute(tag, 'rel')?.toLowerCase() !== 'canonical') report(href);
    }
  }
  for (const match of text.matchAll(/url\(\s*["']?([^"')\s]+)/gi)) {
    if (ABSOLUTE.test(match[1])) report(match[1]);
  }
  for (const match of text.matchAll(/@import\s+["']([^"']+)/gi)) {
    if (ABSOLUTE.test(match[1])) report(match[1]);
  }
  return found;
}

/** Everything the Content-Security-Policy in _headers would block: the page must not depend on it. */
export function findInlineCode(name, html) {
  const found = [];
  const text = stripComments(html);
  for (const [tag] of text.matchAll(/<script\b[^>]*>/gi)) {
    if (attribute(tag, 'src') === null) found.push(`${name}: inline <script>`);
  }
  if (/<style\b/i.test(text)) found.push(`${name}: inline <style>`);
  for (const [tag] of text.matchAll(/<[a-z][^>]*>/gi)) {
    if (attribute(tag, 'style') !== null) found.push(`${name}: style attribute in ${tag}`);
    const handler = /\son[a-z]+\s*=/i.exec(tag);
    if (handler) found.push(`${name}: event handler attribute in ${tag}`);
  }
  return found;
}

/** Root-relative links and assets with no built file behind them. `/x/` is served from `x/index.html`. */
export function findBrokenLinks(files, pages) {
  const found = [];
  for (const [name, html] of pages) {
    for (const match of stripComments(html).matchAll(/\s(?:href|src)="(\/[^"]*)"/g)) {
      const link = match[1];
      if (link.startsWith('//')) continue;
      const path = link.split(/[#?]/, 1)[0];
      const target = path.endsWith('/') ? `${path.slice(1)}index.html` : path.slice(1);
      if (!files.has(target)) found.push(`${name}: links ${link}, which was not built`);
    }
  }
  return found;
}

/**
 * Cloudflare's Email Address Obfuscation is on for the staticbit.io zone. An address outside these
 * comments is served as "[email protected]" and restored only by a script Cloudflare injects, which
 * the CSP blocks and which a visitor without scripts never runs.
 */
export function findUnfencedEmails(name, html) {
  const outside = html.replace(/<!--email_off-->[\s\S]*?<!--\/email_off-->/g, '');
  return [...outside.matchAll(/[\w.+-]+@staticbit\.io/g)].map((match) => `${name}: ${match[0]} is outside an email_off fence`);
}
