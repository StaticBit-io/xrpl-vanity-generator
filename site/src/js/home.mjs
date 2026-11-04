// Wires the home page: the Store buttons and the hero's picture. Without scripts the page is complete:
// the buttons stay disabled and tagged, and the picture stays as printed.
import { STORE_URL } from './config.mjs';
import { initGlyphField } from './glyph-field.mjs';

/** @param {string|null} [url] the listing URL; injectable so a test need not edit config.mjs */
export function initStore(document, url = STORE_URL) {
  if (url === null) return;
  for (const button of document.querySelectorAll('[data-store]')) {
    // The button is disabled in the HTML; a configured listing URL turns it into a plain link.
    const link = document.createElement('a');
    link.className = button.className;
    link.href = url;
    link.textContent = button.querySelector('[data-label]').textContent;
    button.replaceWith(link);
  }
}

if (typeof document !== 'undefined') {
  initStore(document);
  const field = document.querySelector('.glyph-field');
  if (field) initGlyphField(field, { reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches });
}
