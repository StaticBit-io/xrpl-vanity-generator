# XRPL Vanity project site

Design for a public website for XRPL Vanity: the `xrpl-vanity` CLI and the Windows desktop
application. The site lives in this repository and is deployed as a static site.

## Why

The desktop application is being prepared for the Microsoft Store under the publisher StaticBit
(SBP Labs Inc.). The Store listing has fields for a website, a support page and a privacy policy
URL; today the only candidates are files on GitHub. The project also has no page a newcomer can
read in half a minute: the README starts with build commands, and `docs/desktop-app.md` is a
developer document.

A visitor arrives from the Store listing, from GitHub, or from a link shared in the XRPL
community. Within the first half minute they must understand four things:

1. **What the product is**: a generator of XRP Ledger addresses that contain a word you choose.
2. **What they get from it**: an address that starts with, ends with, or contains that word, and
   the seed that controls it.
3. **How safe it is**: everything runs on their machine, and the seed is protected there.
4. **What it runs on**: which operating systems, and what CPU or GPU makes a pattern practical.

## Goals

- A home page that answers the four questions above, in that order of importance, with claims
  that each trace to the code or to a document in this repository.
- A documentation page for each part of the product: one for the desktop application, one for
  the CLI.
- A privacy policy page and a support page whose URLs can go into the Store listing.
- A Microsoft Store button that is present but disabled until a listing URL exists, and becomes
  live by changing one constant.

## Non-goals

- No second language. The site is English only for now. The structure should not make adding a
  language later hard, but no localization machinery is built now.
- No changes to staticbit.io, the company site. It is separate and out of scope.
- No release pipeline. There are no GitHub Releases today; publishing builds is a separate task.
- No deployment, commit or push as part of building the site unless the owner asks for it.

## Anti-goals

The site fails, even if it works technically, if it:

- **Promises what the product does not do.** Every claim must match the code and the docs. In
  particular: Ed25519 only, with secp256k1 stated plainly as unsupported; the desktop
  application is Windows-only.
- **Links to a download that does not exist.** No invented Store URL, no invented release link.
- **Invents numbers.** No made-up metrics, testimonials, user counts or logo walls. Speeds come
  only from `docs/benchmarks.md` and are presented with the hardware they were measured on.
- **Contradicts its own privacy message.** A site that says "nothing leaves your machine" must
  not itself load third-party trackers, analytics or font CDNs.
- **Looks like a generic crypto landing page** or like a sibling of the xrpl-file site. The
  owner asked for a face of its own.

## Constraints

- **Location:** a `site/` folder at the repository root, following the xrpl-file project
  (`E:\GIT\XRPL\xrpl-file\site`, read its `README.md`): `site/src/` holds everything published,
  a small `build.mjs` assembles `site/dist/`, a `serve.mjs` serves it locally, tests run under
  `node --test`. `site/dist/` is git-ignored.
- **Stack:** plain HTML, CSS and ES modules. Node 22 or newer for the build scripts. No npm
  dependencies and no framework.
- **Hosting:** Cloudflare Pages. Host behaviour goes in `site/src/_headers` and
  `site/src/_redirects`, as in xrpl-file. Deployment is not part of this work (see Non-goals);
  the Cloudflare MCP servers in this environment need `/mcp` authorization before they can be
  used.
- **Domain:** a subdomain of `staticbit.io`. The name is `xrpl-vanity.staticbit.io`, matching
  `xrpl-file.staticbit.io` (builder's call, see Decisions).
- **Language:** English.
- **Launch order:** `StaticBit-io/xrpl-vanity-generator` is private today and becomes public
  before the site is announced. The site is written as if it were public ("the source is public",
  GitHub links, Issues). The owner may publish it from the private repository to test the
  deployment, but its URLs must not go into the Store listing until the repository is public:
  the GitHub links are 404s for visitors until then.
- **Copy:** written with the `writing-site-copy` skill. Draft lines in this document and in the
  style sketch are placeholders, not approved copy.
- **Fonts and assets:** self-hosted under `site/src/fonts/` with their licence files and a
  `SOURCES.txt`, as xrpl-file does. No Google Fonts or other third-party requests at runtime.
- **Tooling downloads (approved by the owner):** two things are fetched during the build.
  Martian Mono (OFL) goes into `site/src/fonts/` with its licence and is committed with the site.
  `puppeteer-core` goes into a temporary folder outside the repository, for Hallmark's
  `sloplint.mjs --render` only, found through the `HALLMARK_PUPPETEER` variable; it uses the
  installed Chrome. It is not a dependency of the site and nothing about it enters `site/`.
- **Screenshots:** none in the first version. The Terminal theme prints its pictures in glyphs,
  and `docs/images/desktop-search.png` predates recent UI changes, so the desktop page is text
  with glyph or typographic illustrations (builder's call, see Decisions). If screenshots are
  added later, the application is run only in the MauiLab VM through the `maui-vm-lab` skill,
  never on the host.
- **Accessibility:** WCAG 2.2 AA. A dark phosphor theme makes contrast the main risk; produce a
  contrast report as xrpl-file did (`site/tools/contrast.mjs`).

## Visual direction (decided by the owner)

The Hallmark design skill, theme **Terminal**, chosen by the owner from three sketches (Terminal,
Blueprint, Specimen). The owner described the wanted style as "cryptography".

- Hallmark is not installed globally. Use the clone at
  `E:\GIT\XRPL\xrpl-file\run\sketches\.hallmark` (`nutlope/hallmark` v2.0.0, MIT, commit
  `4f6a46c`). Follow `skills/hallmark/SKILL.md` and the theme file
  `skills/hallmark/references/themes/terminal.md`.
- The material, per the theme: a near-black ground with a green cast in three lightness steps,
  pale phosphor ink, one bright phosphor green with a dim wash of it, every glyph in Martian Mono
  (heads wide, text narrow), pictures printed as grids of characters.
- For this product the glyph grid is drawn from the XRPL base58 alphabet
  (`rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz`), so the picture is the keyspace
  itself. A matched word inside an address is the one place the bright green appears as a
  highlight.
- Hallmark's Floor applies in full, including `sloplint.mjs --render` before handing back, and
  its rule against invented metrics.
- Hallmark's optional comp step (image generation through Together AI, which needs
  `TOGETHER_API_KEY`) is not used.
- The approved sketch is option A in `docs/mockups/xrpl-vanity-site-style.html`. It shows the
  direction, not a layout to copy, and unlike the site it loads its fonts from Google Fonts.

## Pages

### Home (`/`)

Answers the four questions. Above the fold, per Hallmark: the headline, one sentence and the
actions.

- **Actions:** a "Get it from Microsoft Store" button, disabled and tagged `soon`, and a link to
  the GitHub repository. The Store URL is a constant in `site/src/js/config.mjs`; `null` keeps
  the button disabled and tagged, a URL makes it live. Follow xrpl-file's `PUBLISHER_URL`
  pattern (`site/src/js/config.mjs` and the `#publisher` button in `site/src/index.html`). The
  disabled state must be the default with scripting off.
- **What it is / what you get:** match modes (start, end, substring, a start and end pair;
  case-sensitive or not), the estimate of candidates and time shown before a run, and the result:
  an address and its seed.
- **Safe:** the application has no network code; results are encrypted at rest with DPAPI for
  the current Windows account; seeds are masked until revealed and hide again after a timeout; a
  copied seed is cleared from the clipboard after a set delay; a result can be moved to the
  StaticBit mobile wallet as a QR code instead of a file. The source is public.
- **Runs on:** CPU with a chosen thread count; GPUs through CUDA (NVIDIA) and OpenCL, several
  devices at once; a duty control to share the GPU. Desktop: Windows 10 1809 or newer, x64, with
  the WebView2 runtime. CLI: .NET 10; verified on Windows and on Linux. Reference speeds from
  `docs/benchmarks.md` with the machine named (i5-11400F, RTX 5070 Ti). The claim follows the
  evidence, not the hope: the page says OpenCL is verified on NVIDIA and says nothing about AMD
  or Intel (owner's decision); multi-device scaling is not given a number because none was
  measured.
- A plain statement that only Ed25519 keys are generated and secp256k1 is not supported.

### Desktop app (`/desktop/`)

Documentation for users of the Windows application, drawn from `docs/desktop-app.md`: what each
screen does, the estimate, GPU duty (25–100 % in the application), results and their storage,
seed masking and clipboard clearing, QR export, the tray, and requirements. Developer-only
sections of that document (building from source, MSIX packaging, the smoke checklist) stay on
GitHub and are linked, not copied.

### CLI (`/cli/`)

Documentation for `xrpl-vanity`, drawn from `README.md`: building with the .NET 10 SDK, match
modes, flags, device selection (`--list-devices`, `--gpu-devices`), `--gpu-duty` (1–100 in the
CLI), how a pattern's cost grows with its length, and how to read the output. There are no
prebuilt CLI binaries; the page says to build from source.

### Privacy (`/privacy/`)

The privacy policy for the desktop application, from `PRIVACY.md`. That file exists only on the
unmerged branch `claude/store-submission-c14468` (commit `92af281`); read it with
`git show claude/store-submission-c14468:PRIVACY.md`. This page's URL replaces the GitHub link
planned for the Store listing's privacy policy field.

The page carries the text of `PRIVACY.md` with two additions (builder's call, see Decisions):

- **QR export.** The policy covers the plain-text TSV export but not the QR code. A password is
  optional and encrypts the code; without one the code on screen carries the seeds unencrypted
  (`docs/desktop-app.md`, QR section). Add a sentence saying so.
- **This website.** What the site loads (only its own files), and that it sets no cookies and runs
  no analytics. The host is not named (owner's decision), so moving the site needs no edit here.

The Contact section of the page names `support@staticbit.io` rather than the Issues URL in
`PRIVACY.md`.

`PRIVACY.md` stays the source for the application's part. The two additions and the new contact
are proposed back to it as a follow-up on the Store branch; until then a test compares the
application sections of the page with `PRIVACY.md`, leaving out Contact, so they cannot drift
silently. "Last updated" is the date the page is first published.

### Support (`/support/`)

The support contact is `support@staticbit.io`, decided by the owner. GitHub Issues is linked as
the place for bug reports (builder's call, see Decisions). The page also covers the common
problems from the Troubleshooting section of `docs/desktop-app.md` (for example a blank window
without WebView2, and the no-GPU banner).

### Shared

A header with the product name and links to the pages, a footer naming StaticBit as the
publisher, the source licence and the GitHub link, and a `404.html`.

## Keeping the site true

The site restates facts that live in the code and in `README.md`, `docs/desktop-app.md`,
`docs/benchmarks.md` and `PRIVACY.md`. When they disagree, the code wins and the site is
corrected. Every factual claim on the home page should be checkable against one of those
sources.

## Decisions

Made by the owner:

- Hallmark, theme Terminal; a face of its own, not xrpl-file's Vantage.
- Same technical setup as xrpl-file: `site/` in this repository, plain HTML/CSS/ES modules,
  Cloudflare Pages, a `staticbit.io` subdomain.
- A disabled Store button tagged `soon`, as in xrpl-file.
- Separate documentation pages for the desktop application and for the CLI.
- English only for now.
- The repository becomes public before the site launches.
- Support goes to `support@staticbit.io`.
- Martian Mono and `puppeteer-core` may be downloaded for the build.

Builder's calls, open to the owner:

- The subdomain name `xrpl-vanity.staticbit.io`.
- GitHub Issues linked next to the support address, for bug reports.
- Self-hosted fonts and no third-party requests, to keep the privacy claim true of the site.
- Developer-only material from `docs/desktop-app.md` stays on GitHub.
- No screenshots in the first version; illustrations are drawn in glyphs.
- "Runs on" claims are limited to what was verified.
- The privacy page adds a QR export sentence and a section about the website to the text of
  `PRIVACY.md`, and a test keeps the application part in sync with that file.

## Left to the builder

- The page composition and macrostructure inside Hallmark's flow, and the hero layout.
- How the base58 glyph field is drawn and animated, within the Floor's motion and no-script
  rules.
- The URL scheme details, favicons and the `_headers` cache policy, following xrpl-file.
- Test coverage of the build script, the Store button states and links between pages.
