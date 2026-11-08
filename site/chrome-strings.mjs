// The words of the shared header, footer and head, per language. chrome-markup.mjs renders them at
// build time; a language is added here together with its pages, and build.mjs refuses an active
// language that has no entry. `published` carries a {publisher} slot for the StaticBit link.
export const CHROME_STRINGS = {
  en: {
    skip: 'Skip to content',
    nav: { desktop: 'Desktop app', cli: 'CLI', privacy: 'Privacy', support: 'Support' },
    language: 'Language',
    published: 'Published by {publisher}.',
    licence: 'Apache-2.0 licence.',
    source: 'Source on GitHub',
    ogImageAlt: 'An XRP Ledger address carrying the word STATiCBiT, found in a field of address characters',
  },
  ru: {
    skip: 'Перейти к содержимому',
    nav: { desktop: 'Приложение', cli: 'Командная строка', privacy: 'Конфиденциальность', support: 'Поддержка' },
    language: 'Язык',
    published: 'Издатель: {publisher}.',
    licence: 'Лицензия Apache-2.0.',
    source: 'Исходники на GitHub',
    ogImageAlt: 'Адрес XRP Ledger со словом STATiCBiT среди символов адресов',
  },
};
