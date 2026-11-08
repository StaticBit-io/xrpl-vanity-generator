# XRPL Vanity: interface languages

Status: draft for the owner's review. Date: 2026-11-08.

## Intent

XRPL Vanity speaks English only. Other StaticBit products already offer the same six languages, and a
person who installs the desktop application or opens the site should get the interface in their own
language when we have it. This change adds Русский, Deutsch, Español, Português and 中文 to the desktop
application and to the site, with English as the default and the fallback.

The approach is not new to StaticBit: `xrpl-file` (`E:\GIT\XRPL\xrpl-file`) ships the same stack (a MAUI
Blazor Hybrid desktop application and a static site with a `build.mjs`) in the same six languages. This
repository's `site/build.mjs` was itself taken from `xrpl-file` with its language logic removed
(`docs/2026-11-04-xrpl-vanity-site-plan.md`). The work brings that approach back, adapted where this
repository differs.

## Goals

- The desktop application and the site are available in six languages: English, Русский, Deutsch,
  Español, Português, 中文.
- First run and first visit pick the language of the system (application) or the browser (site) when it
  is one of the six, and English otherwise. A choice made in the switcher is remembered and wins over the
  system language from then on.
- The application switches language at once, without a restart, and keeps the state of every page
  (patterns typed, a running search, results on screen, an open dialog).
- Tests keep every language complete: the same keys and the same placeholders as English.

## Non-goals

- The CLI. Its help, its errors and its output stay English. Its output (`Address: ...\tSeed: ...`, the
  `--out` file) is read by scripts.
- `PRIVACY.md` and the site's `/privacy/` page. Both stay English only; the test that holds them equal
  word for word is unchanged.
- The Microsoft Store listing and the MSIX manifest (`Package.appxmanifest` display name and
  description). A listing in several languages needs its own copy and screenshots in Partner Center;
  that is a separate task.
- `README.md` and everything in `docs/`.
- Right-to-left languages, regional variants (pt-PT, zh-Hant as a separate catalog), and a translation
  management service.

## Anti-goals (failure even if it works)

- A machine-translation feel: phrases that read like word-for-word English. All copy, in every language,
  is written with the `writing-site-copy` skill and never uses the long dash.
- A format other programs read changing with the language: the TSV export, the pattern list file, the
  history file, the QR payload, the CLI output.
- A language switch that loses state in the application or bounces a visitor of the site back to a
  language they just left.
- A page on the site that loads anything from another origin or carries inline code: the CSP in
  `site/src/_headers` stays as strict as it is.

## Decisions taken with the owner

| Topic | Decision |
|---|---|
| Languages and codes | `en`, `ru`, `de`, `es`, `pt`, `zh`, in that menu order, names in their own language: English, Русский, Deutsch, Español, Português, 中文. Chinese is Simplified; its HTML tag is `zh-Hans`. |
| Default | Remembered choice, else the system or browser language by primary subtag when it is one of the six (`zh-TW`, `pt-PT`, `es-419` map to `zh`, `pt`, `es`), else English. Same rule as `xrpl-file`. |
| Scope | Desktop application and site. Not the CLI. Not `/privacy/`. Not the Store listing. |
| Translations | Written by Claude in all five languages. The owner reads Russian. German, Spanish, Portuguese and Chinese ship without a native speaker's review and are marked so (see Translation status). |

## Shared language facts

Both parts carry the same table, as `xrpl-file` does (`UiLanguage.cs` and `site/src/js/languages.mjs`):

| Code | Name | HTML tag | Number locale |
|---|---|---|---|
| `en` | English | `en` | `en-GB` |
| `ru` | Русский | `ru` | `ru-RU` |
| `de` | Deutsch | `de` | `de-DE` |
| `es` | Español | `es` | `es-ES` |
| `pt` | Português | `pt` | `pt-BR` |
| `zh` | 中文 | `zh-Hans` | `zh-Hans-CN` |

A test on each side holds the table to the other (the C# list and the JS list name the same codes,
names, tags and locales in the same order).

## Desktop application

### Where strings live

- One JSON catalog per language, embedded in `XrplVanity.App.Core` as
  `Localization/Text/<code>.json`: a flat object of key to phrase.
- Keys are `<Area>.<Name>` (`Search.StartButton`, `Settings.LanguageLabel`, `Tutorial.AddWordTitle`).
  Code never writes a key as a string: each area has a static class of typed ids (`SearchText.StartButton`),
  so a misspelt key is a compile error. This is `xrpl-file`'s `TextId` pattern.
- A phrase has named placeholders (`{count}`, `{word}`, `{speedup:0}`) and, where a number is counted,
  CLDR plural forms (`one`, `few`, `many`, `other`) chosen by `count`. Russian needs `few` and `many`.
- Formatting uses the language's locale, passed explicitly; the ambient thread culture is never read or
  set.

The localization core (`Localizer`, `TextCatalog`, `TextId`, `UiText`, `UiLanguage`, `PluralRules`) is
copied from `xrpl-file/app/XrplFile.App.Core/Localization` into `XrplVanity.App.Core/Localization` and
adapted. It is not extracted into a shared package (builder's call recorded here: two repositories, no
shared build, and the code is small).

### What is translated

Everything a person reads in the application window and around it:

- all pages, components and dialogs: Search, History, Settings, the pattern editor, the device picker,
  the estimate card, live stats, the results list, the QR export dialog, the confirm and close prompts,
  status pills;
- messages built in `SearchViewModel` (estimate notes, validation messages, hints, failures);
- the first-run tutorial (`Tutorial/TutorialScript.cs`, merged in PR #20). Tutorial steps name buttons
  ("Save list", "measure speed", "Start", the QR button); the translated step must use the same words as
  the translated button. The tutorial copy moves from string literals into the catalog: the steps in
  `TutorialScript.cs`, the `ShortestWordHint` line in `TutorialService.cs`, the labels in
  `TutorialOverlay.razor` and the tutorial note in `QrExportDialog`;
- the tray menu (`WindowsTrayService`: Show, Duty, Stop search, Exit) and its tooltip stays
  "XRPL Vanity";
- toast notifications (`Match found`, search finished), rendered in the language current at the moment
  the toast is raised;
- window and file dialog titles, where the application sets them.

### What stays as it is

- The product name "XRPL Vanity", addresses, seeds, patterns, device names reported by drivers.
- Technical details that come from lower layers (`XrplVanity.Search`, `XrplVanity.Gpu`, driver and
  exception messages). They are shown inside a translated sentence, untranslated.
- File formats: TSV export (headers included), the pattern list file, `settings.json` keys, the history
  file.
- SI suffixes in speeds and counts (`M`, `G`, `T`, `/s`). The number before them follows the locale:
  `1.5 G/s` in English, `1,5 G/s` in Russian and German.

### Numbers, durations, times

`Components/Formatting.cs` formats with `CultureInfo.InvariantCulture` today. It moves to the language's
locale for the decimal and group separators. Duration words (`days`, `h`, `min`, `s`, `> 1 year`,
`< 1 s`) come from the catalog. The time of a match (`HH:mm:ss`) stays a 24-hour clock in every
language.

### Choosing and remembering

- `AppSettings` gets `Language` (string, null when nothing was chosen). Null means "follow the system";
  a code means the person chose. The settings file written by an older version has no such field and
  reads as null. `AppSettings.Equals` includes it.
- The system language is `CultureInfo.CurrentUICulture` at start-up, walked up its parents as in
  `xrpl-file`'s `Localizer.Pick`.
- If the choice cannot be saved, the switch still happens and the Settings page says it will not
  survive a restart (same behaviour as `xrpl-file`). `ISettingsStore` already reports this through
  `LastError` after `Save`; its contract does not change.
- `Localizer` lives in `XrplVanity.App.Core` and is registered as a singleton. It is created from the
  `AppSettings` the host already loads at start-up and from `CultureInfo.CurrentUICulture`. The tray and
  toast services in `XrplVanity.App` depend on it through DI (Claude's call).

### The switcher

A "Language" row on the Settings page with a select listing the six names in their own language, in the
table's order. Changing it applies at once.

### Switching without losing state

- `Localizer` is a singleton with a `Changed` event; `MainLayout` cascades the current catalog and
  re-renders on `Changed`, so components keep their state.
- View models hold `UiText` (an id and values), not finished strings, for every message that stays on
  screen (validation, hints, failures, estimate notes, the history read-only reason, the tutorial's
  current step), so a message already shown re-renders in the new language. Only a toast is rendered
  once, when it is raised (Claude's call: everything on screen switches, nothing is left behind in the
  old language).
- Existing tests that assert on a view model's message strings are rewritten to compare `UiText` ids,
  or to render through the English catalog; their meaning does not change.
- The tray menu is rebuilt on `Changed` (it is already rebuilt from scratch on every update).
- The WebView's `<html lang>` follows the language (`zh-Hans` for Chinese), so screen readers and the
  browser's font fallback pick the right voice and glyphs.

### Fonts

Inter and Cascadia Code, as bundled, cover Latin-1 and Cyrillic (checked with fontTools). Neither has
CJK glyphs; Chinese falls back to the system font (Microsoft YaHei on Windows). No CJK font is bundled
in the application or on the site (Claude's call: a CJK font is several megabytes, and the application
is Windows only). Builder checks that the fallback looks acceptable in the main screens and adjusts the
font stack under `:lang(zh)` if needed.

## Site

### Pages and addresses

| Page | Languages |
|---|---|
| `/` (home) | all six |
| `/desktop/` | all six |
| `/cli/` | all six (prose translated; flags, commands and output examples unchanged) |
| `/support/` | all six |
| `404` | all six |
| `/privacy/` | English only |

- English stays at the root, so every existing URL keeps working. Other languages live under a prefix:
  `/ru/`, `/de/desktop/`, `/zh/cli/`.
- Every translated page has `<link rel="alternate" hreflang>` for each language plus `x-default`
  (English), and the sitemap lists every language version. `/privacy/` has a canonical link only.
- Cloudflare Pages serves the nearest `404.html` up the path, so `/ru/404.html` answers missing paths
  under `/ru/`.

### Where the copy lives

Following `xrpl-file`: each language has its own source pages, written by hand as natural text, in
`site/src/<code>/...` (English stays in `site/src/`). The shared head, header and footer come from
`chrome-markup.mjs`, which takes the page's language and reads its words from a string table. A build
check refuses a page whose `<html lang>` does not match its folder, a language folder for a code that
is not in the list, and a language missing a page that English has (except `/privacy/`).

A test compares each translated page to its English one structurally: the same section ids, the same
internal links (mapped to the language's prefix), the same images, the same code blocks.

### The switcher

In the header, as on the owner's screenshot: a native `<details>` disclosure listing the six names as
plain links to the same page in each language. It works without JavaScript. With JavaScript, a click
also remembers the choice in `localStorage` (every access guarded, as in `xrpl-file`'s `prefs.mjs`).

On `/privacy/` the entries link to the home page of each language, and choosing one is remembered.
Translated pages link to `/privacy/` as it is. The privacy page carries no note in other languages:
it stays word for word equal to `PRIVACY.md`.

### Build and checks with language prefixes (Claude's call)

- Every language's `/desktop/` and home page carries the Store buttons; the `STORE_URL` switch applies
  to all of them alike.
- The broken-link check and `sitemap.xml` know the prefixes; a link from a translated page to a page
  that exists only in English (`/privacy/`) is valid.
- The hreflang set of a page comes from its path: the same path under each prefix, checked to exist at
  build time. Titles and descriptions stay in each page's own `<head>`; no page-to-page table.
- The markers `build.mjs` fills (`<!--@chrome:*-->`, `<!--@support-email-->`, `<!--@glyph-field-->`)
  work in every language's pages; the chrome markers take the language from the page's `<html lang>`.

### First visit

On an English page other than `/privacy/`, when no choice is remembered, the visitor is sent to the
same page in their browser's language if it is one of the five others. A page under a language prefix
never redirects, so a person who opened `/de/` stays there.

This repository differs from `xrpl-file` here, and the builder must handle it:

- The CSP has no `script-src` exception and the build refuses inline scripts. The redirect is a small
  classic (non-module, blocking) script in `<head>`, loaded from the site's own origin, so it runs
  before first paint without an inline script.
- `Referrer-Policy: no-referrer` is set, so `xrpl-file`'s "arrived from this site" check by referrer
  does not work. Requirement: a visitor who picks English in the switcher is never sent back to another
  language, even when storage is blocked.
- Rule that meets it without changing any URL (Claude's call): the redirect runs only when
  `localStorage` can be read and written and holds no choice. A click in the switcher stores the choice
  before the browser follows the link, so a visitor who picks English has `en` stored and is not
  redirected; a visitor whose storage is blocked is never redirected and reads English until they pick
  a language. The redirect itself stores nothing: only an explicit pick is remembered.

### Fonts

The site bundles only the Latin subset of Martian Mono (`site/src/fonts/SOURCES.txt`). The same package
version, `@fontsource-variable/martian-mono@5.3.0`, has a Cyrillic subset
(`martian-mono-cyrillic-standard-normal.woff2`). It is added as a second `@font-face` with its
`unicode-range`, so only pages that contain Cyrillic download it; `SOURCES.txt` records its source,
range and checksum. German, Spanish and Portuguese are covered by the Latin subset. Chinese falls back
to the system font; the theme's monospace look does not extend to Chinese, and that is accepted.

### Facts kept in step

The existing tests that tie the site to the code apply to every language:

- the Options table on each `/cli/` page lists exactly the parser's flags (`docs-pages.test.mjs`);
- each speed on each home page appears in `docs/benchmarks.md`, after normalising the locale's decimal
  separator;
- the `STORE_URL` switch in `config.mjs` covers the Store buttons on every language's pages.

### Not translated

Flags, commands, output examples, addresses, the link-card image `og-banner.png`, and screenshots of
the application.

## Translation status

As in `xrpl-file`'s `site/README.md`, a "Languages" section lists each language with its status:
English and Russian checked by the owner; German, Spanish, Portuguese and Chinese "translated from
English, awaiting a native speaker's review". The same note goes into `docs/desktop-app.md` for the
application. The status changes only when a native speaker has read the language.

Quality gate in place of a native review (Claude's call): after Claude writes German, Spanish,
Portuguese and Chinese, a second model (OpenAI Codex, when the quota allows) reads each catalog and
each page against English for meaning errors, unnatural phrasing and terms that disagree with the
interface. Claude checks each remark before applying it. Russian goes to the owner.

## Testing

Application (`dotnet run --project tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj`):

- every language catalog has exactly the English keys, the same placeholders, and valid plural forms;
- every declared `TextId` exists in English, and no English key is unused;
- `Localizer.Pick`: remembered choice, system culture with parents, unknown culture, fallback;
- switching language re-renders the Search page without losing typed patterns or results; a validation
  message on screen changes language;
- `Formatting` gives locale separators per language;
- existing bUnit tests pin English explicitly, so they do not depend on the machine's UI culture.

Site (`node --test "site/test/*.test.mjs"`):

- string tables have the same keys in every language;
- every language has every English page except `/privacy/`; `<html lang>` matches the folder;
- hreflang alternates and sitemap entries are complete and consistent;
- translated pages match the English structure;
- the redirect script's rule matches `getLang`, never redirects a prefixed page or `/privacy/`, and
  never bounces a visitor who chose English;
- the existing CLI-flags, benchmark and privacy tests pass for every language.

Manual: the application in the MauiLab VM (Debug, `-ExeName XrplVanity.App.exe`, no rollback), switched
through all six languages, with a screenshot of each main page in Russian and Chinese; the site served
locally and checked in Russian and Chinese.

## Constraints

- The branch starts from `origin/dev` at `e055b4d`, which includes PR #20 (the first-run tutorial);
  the PR goes into `dev`.
- The dotnet CI's Linux job hits its 45-minute limit (`.github/workflows/dotnet.yml`) and was cancelled
  on PR #20; expect the same on this PR and do not treat that cancellation as a test failure.
- No commit, push or merge without the owner's word. No AI attribution.
- Code and comments in English; no new dependencies in the application; no third-party requests on the
  site.

## Left to the builder

- Exact key names and how areas are split into id classes.
- Structure of the site's string tables and the structural comparison test.
- The mechanism that stops the redirect from bouncing a visitor who chose English.
- The CJK font stack tweaks, if the fallback needs any.
