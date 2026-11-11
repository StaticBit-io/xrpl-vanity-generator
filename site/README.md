# XRPL Vanity site

The static site for `xrpl-vanity.staticbit.io`: plain HTML, CSS and ES modules, no framework, no npm
dependencies and no server code. It is written for Cloudflare Pages; moving it to another host means
copying `site/dist/` and translating `src/_headers`.

```
site/
  src/               everything that is published (pages, js/, css/, fonts/, _headers)
  wrangler.toml      the Pages project: name and output directory
  build.mjs          assembles site/dist, writes sitemap.xml and robots.txt, refuses a tree that breaks the checks
  checks.mjs         the checks: third-party requests, inline code, broken links, unfenced e-mail
  chrome-markup.mjs  the shared head, header and footer, and the support address
  serve.mjs          dev server for site/dist
  tools/             contrast.mjs: the contrast report and its check; og-banner.mjs: the link-card picture (not published)
  test/              node:test suites
  dist/              build output, git-ignored
```

## Build, serve, test

Requires Node 22 or newer.

```bash
node site/build.mjs
node site/serve.mjs
node --test "site/test/*.test.mjs"
```

`serve.mjs` binds to `127.0.0.1` only, starts at port 8792 and moves to the next port when one is busy.

`build.mjs` copies `src/**` to `dist/`, fills the markers in each page and checks the result. It removes
`dist/` and fails when a page

- loads anything from another origin (a clicked link is fine),
- has an inline script, inline style or event attribute, which the CSP in `_headers` would block,
- links a site path that was not built,
- holds a `staticbit.io` address outside the `email_off` comments (see below),
- or has a file over 25 MiB, the Cloudflare Pages limit.

Markers: `<!--@chrome:head-->`, `<!--@chrome:header-->`, `<!--@chrome:footer-->`, `<!--@support-email-->`
and `<!--@glyph-field-->`.

## Languages

The site is built in six languages. Adding one is a data change: its words, its pages and its entry in two lists.

| Code | Home | `<html lang>` and hreflang | Number locale | Status |
|---|---|---|---|---|
| `en` | `/` | `en` | `en-GB` | written by hand |
| `ru` | `/ru/` | `ru` | `ru-RU` | translated from English, awaiting the owner's read |
| `de` | `/de/` | `de` | `de-DE` | translated from English, awaiting a native speaker's review |
| `es` | `/es/` | `es` | `es-ES` | translated from English, awaiting a native speaker's review |
| `pt` | `/pt/` | `pt` | `pt-BR` | translated from English, awaiting a native speaker's review |
| `zh` | `/zh/` | `zh-Hans` | `zh-Hans-CN` | translated from English, awaiting a native speaker's review |

The same table, with the same tags and locales, is `UiLanguage` in the desktop application; a test keeps
the two equal. `/privacy/` exists in English only: it carries a canonical and no alternates, and the
language menu sends every other language to its home instead.

- `src/js/languages.mjs` holds `LANGS` (menu order) and `ACTIVE_LANGS` (what ships). Only an active
  language gets pages, a menu entry and `hreflang` alternates; the build refuses a folder for any other.
- `chrome-strings.mjs` holds the header, footer and head words per language, with English's keys.
- `src/<lang>/` holds the language's `index.html`, `desktop/`, `cli/`, `support/` and `404.html`. They keep
  the English pages' ids, internal links, images, scripts and every `<code>` and `<pre>` exactly;
  `test/translations.test.mjs` checks that, and `test/home.test.mjs` checks the speeds against
  `docs/benchmarks.md` in each language's number format.
- `src/js/lang-redirect.js` sends a first visit from an English page to the browser's language. It runs
  before the stylesheets, only when `localStorage` can be read and written and holds no choice, and
  stores nothing; a click in the language menu stores the choice first (`src/js/lang.mjs`), so a reader
  who picks English is never sent away again. It carries its own copy of `ACTIVE_LANGS` and of the
  unprefixed page list, and tests pin both.
- Cyrillic is drawn by `fonts/martian-mono-5.3.0-cyrillic.woff2`; Chinese pages use the system's
  Simplified Chinese faces, since Martian Mono has no Chinese glyphs.

## E-mail addresses

Cloudflare's Email Address Obfuscation is on for the `staticbit.io` zone. It serves a bare address as
`[email protected]` and restores it with a script it injects. The CSP blocks that script, and a visitor
without scripts never runs it. So no page writes an address directly: `<!--@support-email-->` puts it
inside `<!--email_off-->` comments, which Cloudflare leaves alone.

## Turning on the Store button

Set `STORE_URL` in `src/js/config.mjs` to the listing URL and rebuild. Until then the buttons stay
disabled and tagged `soon`. With a URL, a script replaces them with links. A test pins `STORE_URL` to `null`
as a reminder that no listing exists yet: change that test in the same commit.

## Facts on the pages

The pages restate `README.md`, `docs/desktop-app.md`, `docs/benchmarks.md` and `PRIVACY.md`. When they
disagree, the code wins and the site is corrected. The tests catch part of this:

- the Options table on `/cli/`, the README's table and the parser in `CliOptions.cs` must list exactly the same flags (`docs-pages.test.mjs`),
- each speed on the home page must appear in `docs/benchmarks.md`,
- the sections of `/privacy/` must equal `PRIVACY.md` word for word. Contact is the exception, because the
  page fences the address, and This website exists only on the page.


## Design

Hallmark, theme Terminal. The stamp at the top of `src/css/site.css` records the decisions.
`docs/site-contrast.md` is generated by `node site/tools/contrast.mjs`. The font covers the Latin range only
(`src/fonts/SOURCES.txt`), so a character outside it, such as an arrow, falls back to another face.

The mark is Martian Mono's `r` at weight 500 and width 100, drawn as an outline in the site green on the site black. The same path is in `src/favicon.svg`, the application's `appicon.svg`, `splash.svg` and `NavRail.razor`, and `tray.ico` is rendered from it; `logo.test.mjs` keeps them equal. After changing the icon, delete `obj/**/resizetizer`: the MAUI icon step can keep old images.

`src/og-banner.png` is the picture link previews show. `node site/tools/og-banner.mjs` draws it again with headless Chrome or Edge. Its address carries the brand and is an example: it has no valid checksum, so a wallet rejects it. Telegram and others cache a preview by URL; [@WebpageBot](https://t.me/WebpageBot) refreshes one in Telegram.

To repeat Hallmark's rendered check, install `puppeteer-core` outside the repository, point
`HALLMARK_PUPPETEER` at that folder, start `serve.mjs`, and run `sloplint.mjs` from a Hallmark checkout:

```bash
node <hallmark>/scripts/sloplint.mjs site/src/css/site.css --genre atmospheric --url http://127.0.0.1:8792/
node <hallmark>/scripts/sloplint.mjs site/src/css/site.css --genre atmospheric --scope app --url http://127.0.0.1:8792/cli/ --url http://127.0.0.1:8792/desktop/ --url http://127.0.0.1:8792/privacy/ --url http://127.0.0.1:8792/support/ --url http://127.0.0.1:8792/404.html
```

The second line uses `--scope app` because Hallmark's landing-page gates, such as 80 words a screen, do not
fit a reference page or a policy.

## Deployment

`.github/workflows/site.yml` runs the tests and the build on every pull request and on every push to `dev` and
`main`. Nothing publishes by itself. To publish, run the workflow by hand and pick the branch:

```bash
gh workflow run site.yml -f branch=main
```

The same is in Actions > site > Run workflow. The run tests that branch, then the `deploy-site` job builds
`site/dist` and uploads it with `wrangler pages deploy` (direct upload; the Pages project is not connected to Git).

| Branch | Goes to |
|---|---|
| `main` | production: `https://xrpl-vanity.staticbit.io` and `https://xrpl-vanity-site.pages.dev` |
| `dev` | preview: `https://dev.xrpl-vanity-site.pages.dev` |

| Setting | Value |
|---|---|
| Project name | `xrpl-vanity-site` |
| Production branch | `main` |
| Deployed by | a manual run of the `deploy-site` job, wrangler `4.151.1` |
| Build output directory | `site/dist` (named in `site/wrangler.toml`; the job deploys from `site/`) |
| Custom domain | `xrpl-vanity.staticbit.io` |

The job needs two repository secrets, set once by the owner. Without them it fails on its first step and
names them.

| Secret | Value |
|---|---|
| `CLOUDFLARE_API_TOKEN` | an API token of the account that holds the `staticbit.io` zone, with the account permission **Cloudflare Pages: Edit** |
| `CLOUDFLARE_ACCOUNT_ID` | that account's ID |

Without CI, for example while Actions is down: `node site/build.mjs`, then from `site/`:
`npx wrangler@4.151.1 pages deploy --branch main`.

The custom domain is attached once in the Pages project (Custom domains). With the zone and the project in one
Cloudflare account, that also creates the DNS record. Until then production is only at the `pages.dev` address.

## Before launch

1. The repository secrets from [Deployment](#deployment) are set.
2. `support@staticbit.io` receives mail.
3. The repository is public, and its repository, Issues and licence links open when signed out. Until then those links are 404s for visitors.
4. The custom domain is attached, and Cloudflare Web Analytics is off for the project: `/privacy/` says the site runs no analytics.
5. On the live `/support/`, the page source has no `email-decode` script and no `[email protected]`, and no response sets a cookie: a zone feature such as Bot Fight Mode can add `__cf_bm`, which would make `/privacy/` wrong.
6. "Last updated" on `/privacy/` is the publication date.
7. The site is verified in Google Search Console and Bing Webmaster Tools, and `sitemap.xml` is submitted to both.
8. The site's URLs go into the Store listing fields only after all of the above.
