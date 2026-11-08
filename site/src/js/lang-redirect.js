// First-visit language redirect. A classic script because it must run before the stylesheets (the
// English page never paints for a reader who is sent away) and the CSP forbids inline script. It
// cannot import, so ACTIVE and KEY copy ACTIVE_LANGS and LANG_KEY; a test pins both.
//
// "First visit" is read from storage, not from the referrer (Referrer-Policy is no-referrer): the
// reader is redirected only when localStorage can be written and read and holds no choice. The
// language menu stores the choice before navigating (lang.mjs), so picking English is never undone.
// This script stores nothing itself.
(function () {
  const ACTIVE = ['en'];
  const KEY = 'xv-lang';
  // The pages that exist without a language prefix. Anything else (the 404, /privacy/, a mistyped
  // path) is left alone: its prefixed twin may not exist.
  const PAGES = ['/', '/desktop/', '/cli/', '/support/'];

  try {
    if (PAGES.indexOf(location.pathname) === -1) return;
    const storage = localStorage;
    if (!storage) return;
    const probe = KEY + '-probe';
    storage.setItem(probe, '1');
    storage.removeItem(probe);
    if (storage.getItem(KEY) !== null) return;

    const tags = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language];
    for (let i = 0; i < tags.length; i++) {
      const lang = String(tags[i] || '').toLowerCase().split(/[-_]/)[0];
      if (ACTIVE.indexOf(lang) === -1) continue;
      // The first language the site offers decides; English first means the reader prefers English.
      if (lang === 'en') return;
      location.replace('/' + lang + location.pathname + location.search + location.hash);
      return;
    }
  } catch (e) {
    // Storage blocked or unavailable: not a first visit we can tell apart, so stay on English.
  }
})();
