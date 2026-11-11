// Keeps the document language in step with the interface language, so that screen readers,
// hyphenation and font selection follow what the screens say.

/** Sets the lang attribute of the root element to a BCP 47 tag such as "zh-Hans". */
export function setLang(tag) {
  document.documentElement.lang = tag;
}
