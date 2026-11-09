// Remembers a language picked in the menu before the browser navigates, so the first-visit redirect
// never overrides an explicit choice.
import { setLang } from './prefs.mjs';

// A middle-click raises auxclick and "open in new tab" raises contextmenu, neither raises click; the
// new tab would find nothing stored and be redirected straight back.
function remember(event) {
  const link = event.target?.closest?.('a[data-lang]');
  if (link) setLang(link.dataset.lang);
}

for (const type of ['click', 'auxclick', 'contextmenu']) document.addEventListener(type, remember);
