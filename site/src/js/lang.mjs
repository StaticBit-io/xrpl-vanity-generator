// Remembers a language picked in the menu before the browser navigates, so the first-visit redirect
// never overrides an explicit choice.
import { setLang } from './prefs.mjs';

document.addEventListener('click', (event) => {
  const link = event.target?.closest?.('a[data-lang]');
  if (link) setLang(link.dataset.lang);
});
