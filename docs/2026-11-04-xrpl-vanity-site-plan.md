# XRPL Vanity project site Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the static XRPL Vanity site in `site/`: home, desktop docs, CLI docs, privacy and support pages in Hallmark's Terminal theme, ready to deploy to Cloudflare Pages.

**Architecture:** Plain HTML, CSS and ES modules under `site/src/`. A dependency-free `site/build.mjs` copies them to `site/dist/`, fills shared markers (head, header, footer, support address, glyph field) and refuses a tree that breaks the site's own promises: third-party requests, inline code, broken internal links, an unfenced e-mail address. `node --test` suites tie the pages to `README.md`, `docs/desktop-app.md`, `docs/benchmarks.md` and `PRIVACY.md`.

**Tech Stack:** Node 22+ (this machine has 24), `node:test`, no npm dependencies in the site. Hallmark v2.0.0 at `E:\GIT\XRPL\xrpl-file\run\sketches\.hallmark` for design and for `sloplint.mjs`.

**Spec:** `docs/2026-11-02-xrpl-vanity-site-design.md`. Reference implementation for structure: `E:\GIT\XRPL\xrpl-file\site` (read `README.md`, `build.mjs`, `serve.mjs`, `tools/contrast.mjs`).

## Global Constraints

- No commit, push or deploy unless the owner has said so. Commit steps below apply only then. Message format: conventional commits, no AI attribution lines.
- `site/` has no `package.json` and no npm dependencies.
- No runtime request leaves the site's own origin: no Google Fonts, no CDN, no analytics.
- No inline `<script>` and no `style=` attribute: the `_headers` CSP forbids them.
- All copy in English, written with the `writing-site-copy` skill. Draft lines in the spec and sketch are not approved copy.
- Design follows `skills/hallmark/SKILL.md` and `references/themes/terminal.md`; the Floor is not waivable. `TOGETHER_API_KEY` and the comp step are not used. `frontend-design` applies only to what Hallmark leaves open.
- Never a proportional face, second hue, rounded corner or light section (theme do-nots).
- No Store URL or download link is invented. `STORE_URL` stays `null`.
- Ed25519 only is stated plainly on the home page. The desktop application is Windows-only.
- Reference speeds, with the machine named (Intel Core i5-11400F; NVIDIA GeForce RTX 5070 Ti): CPU `118,885/s`, `cuda` `32,375,668/s`, `opencl` `32,211,960/s` (`docs/benchmarks.md`, the 0a702ae table). The 26.9 M/s on the style sketch is superseded.
- OpenCL is verified on NVIDIA and pocl; AMD and Intel are "expected to work, untested". No multi-device speed figure.
- GPU duty: 25–100 % in the application, 1–100 in the CLI.
- Requirements: Windows 10 version 1809 (build 17763) or newer, x64, WebView2 runtime; CLI needs the .NET 10 SDK and is verified on Windows and Linux. macOS is not claimed.
- Support address `support@staticbit.io`; repository `https://github.com/StaticBit-io/xrpl-vanity-generator`; site origin `https://xrpl-vanity.staticbit.io`; licence Apache-2.0; publisher StaticBit.
- The desktop application is never run on the host. No screenshots in this version.

## Review Focus

1. **Cloudflare's Email Address Obfuscation is on for the `staticbit.io` zone** (see the comment in xrpl-file's `build.mjs`). A bare `support@staticbit.io` is served as `[email protected]` and an injected script restores it: without scripts the visitor sees no address, the CSP blocks the script, and "the site loads only its own files" is false. Every address must sit inside `<!--email_off-->…<!--/email_off-->` (Task 1).
2. **A phone at 320 px.** A 34-character address, a flag table and a long command must not widen the page; they wrap or scroll inside their own box (Tasks 3 and 4, render gate in Task 6).
3. **Scripts off, or reduced motion.** All text is readable, the Store button is disabled and tagged `soon`, the glyph field is a static `<pre>` (Task 3).
4. **A visitor copies the example address, or takes the site for a generator.** The example has an invalid checksum so a wallet rejects it, is labelled as an example, and no seed-shaped string appears anywhere (Task 3).
5. **Docs drift.** A new CLI flag or a changed policy sentence must fail a test rather than leave the site quietly wrong (Tasks 4 and 5).

---

### Task 1: Build pipeline, checks, dev server and shared chrome

**Files:**
- Create: `site/build.mjs`, `site/checks.mjs`, `site/chrome-markup.mjs`, `site/serve.mjs`, `site/src/_headers`, `site/src/404.html`, `site/src/js/config.mjs`
- Modify: `.gitignore` (add `site/dist/`)
- Test: `site/test/build.test.mjs`, `site/test/checks.test.mjs`, `site/test/serve.test.mjs`

**Interfaces:**
- Produces, `site/src/js/config.mjs`: `STORE_URL = null`, `SITE_ORIGIN`, `REPOSITORY_URL`, `ISSUES_URL`, `SUPPORT_EMAIL`.
- Produces, `site/checks.mjs`:
  - `findExternalRefs(name: string, text: string): string[]` — absolute or protocol-relative URLs in `src=`, `<link href=`, CSS `url(` and `@import`. `<a href>` is allowed.
  - `findInlineCode(name, html): string[]` — `<script>` without `src`, any `style=` attribute, any `on*=` attribute.
  - `findBrokenLinks(files: Set<string>, pages: Map<string,string>): string[]` — root-relative `href`/`src` with no built file; `/x/` means `x/index.html`; the fragment is dropped.
  - `findUnfencedEmails(name, html): string[]` — any `@staticbit.io` outside an `email_off` fence.
- Produces, `site/chrome-markup.mjs`: `renderChrome(part: 'head'|'header'|'footer', page: {pagePath, title, description}): string`; `supportEmailHtml(): string`.
- Produces, `site/build.mjs`: `build({siteDir, outDir, maxFileBytes = MAX_FILE_BYTES}): Promise<void>`, `pagePathOf(relativeFile): string`, `MAX_FILE_BYTES = 25 * 1024 * 1024`. Markers in pages: `<!--@chrome:head|header|footer-->`, `<!--@support-email-->`. A later task adds `<!--@glyph-field-->`.
- Produces, `site/serve.mjs`: `START_PORT = 8792`, `resolveRequest(root, pathname): Promise<{status, file?, location?}>`, `createDevServer(root)`, `listenOnFreePort(server, startPort)`.

There are no redirect rules to write: every page is a folder with an `index.html`, so this site has no `_redirects` file and no copy of xrpl-file's `redirects.mjs`.

- [ ] **Step 1: Write the failing tests**

`checks.test.mjs`: `findExternalRefs` flags `<link href="https://fonts.googleapis.com/x">`, `<script src="//cdn.example/x.js">`, `url(https://x/y.woff2)` and passes `<a href="https://github.com/x">` and `url(/fonts/a.woff2)`. `findInlineCode` flags `<script>x()</script>`, `<p style="x">`, `<a onclick="x">` and passes `<script type="module" src="/js/home.mjs"></script>`. `findBrokenLinks` passes `/privacy/#contact` when `privacy/index.html` exists and reports `/nope/`. `findUnfencedEmails` reports a bare address and a bare `mailto:` and passes `<!--email_off--><a href="mailto:support@staticbit.io">support@staticbit.io</a><!--/email_off-->`.

`build.test.mjs`, on a temporary fixture: `supportEmailHtml()` passes `findUnfencedEmails`; markers are replaced and no `<!--@` survives; `renderChrome('footer', …)` names StaticBit, Apache-2.0 and the repository; the header links `/desktop/`, `/cli/`, `/privacy/`, `/support/`; each failed check rejects with a message naming the file and leaves no `outDir`; a file over `maxFileBytes` is refused; an `outDir` containing `siteDir` is refused; `_headers` contains `default-src 'self'`, `object-src 'none'`, `base-uri 'none'`, `frame-ancestors 'none'` and `X-Content-Type-Options: nosniff`.

`serve.test.mjs`: `/` gives 200 on `index.html`; `/cli/` gives 200; `/cli` gives 301 to `/cli/`; `/missing/` gives 404 on `404.html`; `/../secret` never leaves the root; the server binds `127.0.0.1`.

- [ ] **Step 2: Run them to verify they fail**

Run: `node --test site/test/*.test.mjs`
Expected: FAIL, modules not found.

- [ ] **Step 3: Implement the files above**

Take `build.mjs` and `serve.mjs` from xrpl-file and remove the language, element, price and redirect logic. Keep its failure behaviour: `dist/` is removed when any check fails. `supportEmailHtml()` returns the fenced `mailto:` link. `_headers` also caches `/fonts/*` for a year as immutable. `404.html` uses the chrome markers and links home.

- [ ] **Step 4: Run the tests**

Run: `node --test site/test/*.test.mjs`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add .gitignore site
git commit -m "feat(site): the build, its checks and the dev server"
```

---

### Task 2: Terminal theme, font and contrast report

**Files:**
- Create: `site/src/css/tokens.css`, `site/src/css/base.css`, `site/src/fonts/*`, `site/tools/color.mjs`, `site/tools/contrast.mjs`, `docs/site-contrast.md`
- Test: `site/test/theme.test.mjs`

**Interfaces:**
- Consumes: the build from Task 1.
- Produces: the theme's tokens (`--color-paper`, `--color-ink`, `--color-accent`, `--glyph-0`…`--glyph-7` and the rest of `terminal.md`) and `--font-display`; `allRows(css?)`, `failures(rows)`, `renderReport(rows)`, `REPORT_PATH`.

- [ ] **Step 1: Write the failing test**

`theme.test.mjs`: `tokens.css` contains `--font-display: "Martian Mono"`; no `.css` file under `site/src/css` references `googleapis`; `base.css` has an `@font-face` with `font-stretch: 75% 112.5%` pointing at a file that exists in `site/src/fonts/`; `SOURCES.txt` names `@fontsource-variable/martian-mono@5.3.0` and a licence file sits beside it; `failures(allRows())` is empty, with text pairs at 4.5 and UI pairs at 3 (ink, ink-2 and muted on paper and paper-2; accent-ink on accent; accent on paper; rule-2 on paper; focus on paper); `docs/site-contrast.md` equals `renderReport(allRows())`.

- [ ] **Step 2: Run it to verify it fails**

Run: `node --test site/test/theme.test.mjs`
Expected: FAIL.

- [ ] **Step 3: Scaffold the theme**

Run: `node E:/GIT/XRPL/xrpl-file/run/sketches/.hallmark/skills/hallmark/scripts/theme.mjs scaffold terminal --dir site/src/css`
Expected: `tokens.css` and `base.css` written.

- [ ] **Step 4: Fetch Martian Mono (approved by the owner)**

In a new empty folder under the session scratchpad, run `npm pack @fontsource-variable/martian-mono@5.3.0` and unpack the tarball. Do not run any script from it. Copy to `site/src/fonts/` the Latin `.woff2` that carries both the `wght` and `wdth` axes (expected `files/martian-mono-latin-full-normal.woff2`; the theme needs width 75–112.5) and the package's `LICENSE` as `MartianMono-OFL.txt`. Write `SOURCES.txt` in the form of xrpl-file's. Add the `@font-face` to `base.css` with `font-weight: 100 800`, `font-stretch: 75% 112.5%`, `font-display: swap`.

- [ ] **Step 5: Implement the contrast tool**

Copy `tools/color.mjs` as is. Reduce `tools/contrast.mjs` to the single dark theme and the pairs in Step 1; `REPORT_PATH` is `docs/site-contrast.md`. Run `node site/tools/contrast.mjs` to write the report. If a pair fails, adjust that token and record why in the report; `--glyph-*` cells are decoration and are not measured.

- [ ] **Step 6: Run the tests**

Run: `node --test site/test/*.test.mjs`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add site docs/site-contrast.md
git commit -m "feat(site): the Terminal material, its font and a contrast report"
```

---

### Task 3: Home page

**Files:**
- Create: `site/src/index.html`, `site/src/css/site.css`, `site/src/js/home.mjs`, `site/src/js/glyph-field.mjs`
- Modify: `site/build.mjs` (the `<!--@glyph-field-->` marker)
- Test: `site/test/home.test.mjs`, `site/test/glyph-field.test.mjs`

**Interfaces:**
- Consumes: config, chrome and tokens from Tasks 1 and 2.
- Produces, `glyph-field.mjs`: `BASE58_ALPHABET = 'rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz'`, `glyphRows(cols: number, rows: number, seed: number): string[]` (deterministic), `initGlyphField(element, {reducedMotion: boolean}): void`.
- Produces, `home.mjs`: `initStore(document, url = STORE_URL): void`. `null` leaves the markup untouched; a URL replaces `#store` with an `<a>` of the same id and class and without the `soon` tag, as `initPublisher` does in xrpl-file.
- Produces, `site.css`: the shared classes the docs pages reuse: `.well`, `.addr`, `.reference` (table), `.prose`.

- [ ] **Step 1: Write the copy and pick the composition**

Invoke `writing-site-copy`, then run Hallmark's flow from step 1 with the spec's Home section as the brief. The page answers, in order: what it is, what you get, how safe, what it runs on. The theme's range suits this: figures as a tool's output for the speeds, a reference as rows for the match modes. The match modes include the "near start" option from the README (the word may begin at the 2nd or 3rd character).

- [ ] **Step 2: Write the failing tests**

`glyph-field.test.mjs`: `glyphRows(40, 6, 1)` has 6 rows of 40 characters, all in `BASE58_ALPHABET`, and is equal on a second call; a different seed differs; the alphabet has 58 distinct characters and none of `0 O I l`.

`home.test.mjs`, on the built page:

```js
test('the Store button ships disabled and tagged soon', () => {
  assert.match(button, /\bdisabled\b/);
  assert.match(button, /soon/);
  assert.equal(STORE_URL, null);
});
test('the example address is shaped like one and a wallet would reject it', () => {
  const address = exampleAddress(html);          // the element marked data-example-address
  assert.ok(address.length >= 25 && address.length <= 35);
  assert.equal(checksumValid(address), false);   // base58 over BASE58_ALPHABET, double SHA-256, node:crypto
  assert.match(html, /example/i);
});
test('no seed-shaped string is published', () => {
  assert.doesNotMatch(allBuiltHtml, /\bsEd[1-9A-HJ-NP-Za-km-z]{20,}/);
});
```

Also: a fake `document` shows `initStore` with a URL gives an `<a>` carrying that `href`, and `initStore` with `null` changes nothing. The page contains `Ed25519` and `secp256k1`, `1809`, `WebView2`, `.NET 10`, `untested`, the machine names. Each element with `data-source` and `data-value` cites a file whose text contains that value; the three speeds are present that way. A static `<pre>` holds the glyph field in the built HTML, marked `aria-hidden="true"`. `site.css` has a `prefers-reduced-motion: reduce` block and `.addr` has `overflow-wrap: anywhere`. The first non-empty line of `site.css` is the Hallmark stamp.

- [ ] **Step 3: Run them to verify they fail**

Run: `node --test site/test/home.test.mjs site/test/glyph-field.test.mjs`
Expected: FAIL.

- [ ] **Step 4: Implement the page**

Hero, sections, `home.mjs`, `glyph-field.mjs`. The build fills `<!--@glyph-field-->` from `glyphRows`, so the picture exists without scripts; `initGlyphField` only animates it, once, and not under reduced motion. Its randomness is decoration and must not look like key generation. The matched word in the example address is the only bright-green highlight in the hero. Use the sketch's illustrative address `rDiAMondHgs4kQe7tWvNpYcX3bLm9fZu` if its checksum test passes as invalid.

- [ ] **Step 5: Run the tests and look at it**

Run: `node --test site/test/*.test.mjs && node site/build.mjs`
Expected: PASS. Then open the page through a `.claude/launch.json` entry for `node site/serve.mjs` in the Browser pane at 1280×800 and 375 wide: headline, sentence and actions are visible without scrolling at 1280×800, and the page does not scroll sideways.

- [ ] **Step 6: Commit**

```bash
git add site
git commit -m "feat(site): the home page"
```

---

### Task 4: Desktop and CLI documentation pages

**Files:**
- Create: `site/src/desktop/index.html`, `site/src/cli/index.html`
- Modify: `site/src/css/site.css` (only if `.reference` or `.prose` need more)
- Test: `site/test/docs-pages.test.mjs`

**Interfaces:**
- Consumes: chrome, `site.css` classes; `REPOSITORY_URL`.

- [ ] **Step 1: Write the copy**

Invoke `writing-site-copy`. Sources: `docs/desktop-app.md` and `README.md`. Scope per the spec's Desktop app and CLI sections. Read the code when a document is unclear; the code wins.

- [ ] **Step 2: Write the failing tests**

`docs-pages.test.mjs`:
- Every flag in the README's Flags table appears on `/cli/`, and every `--flag` on `/cli/` appears in `README.md`, so a new or removed flag fails.
- `/cli/` says the duty range is 1–100, says to build from source with the .NET 10 SDK, and offers no binary download.
- `/desktop/` says 25–100, and names `DPAPI`, `WebView2`, `1809`, QR export and the optional password.
- `/desktop/` holds no `Add-AppxPackage`, `MSIX-win-x64` or smoke checklist text, and links them on GitHub.
- No `<img>` on either page.
- Each page has one `<h1>` and heading levels never skip.
- A wide table or `<pre>` sits in a box that scrolls by itself (`.reference` has `overflow-x: auto`).

- [ ] **Step 3: Run them to verify they fail**

Run: `node --test site/test/docs-pages.test.mjs`
Expected: FAIL.

- [ ] **Step 4: Implement both pages**

- [ ] **Step 5: Run the tests**

Run: `node --test site/test/*.test.mjs`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add site
git commit -m "feat(site): documentation for the desktop application and the CLI"
```

---

### Task 5: Privacy and support pages

**Files:**
- Create: `site/src/privacy/index.html`, `site/src/support/index.html`
- Test: `site/test/policy.test.mjs`

**Interfaces:**
- Consumes: chrome, `<!--@support-email-->`, `ISSUES_URL`.
- Produces: `readPolicy(): string | null` in the test file, resolved in order: `PRIVACY.md` at the repository root, `git show claude/store-submission-c14468:PRIVACY.md`, else `null`.

- [ ] **Step 1: Write the failing tests**

`policy.test.mjs`:
- Each `##` section of `PRIVACY.md` except Contact appears on `/privacy/` inside `<section data-policy="<heading slug>">`, equal once both are normalised: tags and Markdown markers removed, whitespace collapsed, table cells in order. When `readPolicy()` is `null` the test calls `t.skip('PRIVACY.md not available')`, so the skip is visible.
- The only other sections are QR codes, This website and Contact.
- The QR section says the code is unencrypted without a password.
- This website names Cloudflare and says the site sets no cookies and runs no analytics.
- Contact uses the fenced `support@staticbit.io` and not the Issues URL.
- "Last updated" is on the page with a date other than the `2026-09-16` of `PRIVACY.md`.
- `/support/` has the fenced address, a link to `ISSUES_URL`, and the blank-window (WebView2) and no-GPU-devices problems from the Troubleshooting section of `docs/desktop-app.md`.
- No cookie is set: no `document.cookie` under `site/src/js`.

- [ ] **Step 2: Run them to verify they fail**

Run: `node --test site/test/policy.test.mjs`
Expected: FAIL.

- [ ] **Step 3: Implement both pages**

The application sections are the policy's own text, not rewritten. `writing-site-copy` applies only to the three new sections and to `/support/`. For This website, say what is true of a Pages site with Web Analytics off: only its own files, no cookies, no analytics, and Cloudflare processes requests as the host, with a link to Cloudflare's privacy policy. Do not claim what Cloudflare logs in detail. "Last updated" holds the build date until publication; the launch checklist in Task 6 fixes it.

- [ ] **Step 4: Run the tests**

Run: `node --test site/test/*.test.mjs`
Expected: PASS, with no skip on this machine.

- [ ] **Step 5: Commit**

```bash
git add site
git commit -m "feat(site): the privacy policy and the support page"
```

---

### Task 6: Hallmark sweep, README and launch checklist

**Files:**
- Create: `site/README.md`
- Modify: whatever the sweep finds

- [ ] **Step 1: Install the render tool outside the repository (approved by the owner)**

In a new folder under the session scratchpad, run `npm install puppeteer-core`. It downloads no browser; the check uses `C:\Program Files\Google\Chrome\Application\chrome.exe`.

- [ ] **Step 2: Run the sweep on every page**

Run `node site/build.mjs`, then for each built page, with `HALLMARK_PUPPETEER` set to that folder:
`node E:/GIT/XRPL/xrpl-file/run/sketches/.hallmark/skills/hallmark/scripts/sloplint.mjs <page> --render`
Expected: no `FAIL`, and no "not run" note, at widths 320 to 1920. Fix each `FAIL`. Fix or waive each `REFLEX` on the record in the `site.css` stamp. Then do Hallmark's judged gates and finish review.

- [ ] **Step 3: Check the claims by hand**

Read each page against the spec's Anti-goals. Every factual sentence on the home page must point at a source; list any that does not and fix it.

- [ ] **Step 4: Write `site/README.md`**

Layout, build, serve, test, how to add the Store URL (`STORE_URL` in `config.mjs`), and a launch checklist:
1. The repository is public; repository, Issues and licence links open signed out.
2. `support@staticbit.io` receives mail.
3. The Pages project and the custom domain `xrpl-vanity.staticbit.io` exist; build command `node site/build.mjs`, output `site/dist`.
4. Cloudflare Web Analytics is off for the project.
5. On the live `/support/`, the page source has no `email-decode` script and no `[email protected]`.
6. "Last updated" on `/privacy/` is the publication date.
7. The site's URLs go into the Store listing fields only after all of the above.
8. `PRIVACY.md` gets the two additions and the new contact on the Store branch.

- [ ] **Step 5: Final verification**

Run: `node --test site/test/*.test.mjs && node site/build.mjs`
Expected: all pass, `site/dist/` built. Report the test count, any skip, and the sweep result as they are.

- [ ] **Step 6: Commit**

```bash
git add site
git commit -m "docs(site): how to build it and what to check before launch"
```
