# XRPL Vanity interface languages: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The desktop application and the site speak English, Русский, Deutsch, Español, Português and 中文, picked from the system or browser language and switchable at any time.

**Architecture:** The application copies `xrpl-file`'s localization core (typed ids, JSON catalogs embedded in `XrplVanity.App.Core`, a `Localizer` singleton whose catalog `MainLayout` cascades). The site gets `xrpl-file`'s language list and preference module, hand-written pages per language under `site/src/<code>/`, and a build that checks languages, writes hreflang and the sitemap. Wiring comes first with English only; each language then lands as one task that translates both the application and the site.

**Tech Stack:** .NET 10, MAUI Blazor Hybrid, bUnit 2.9, xUnit v3; Node 22 `node:test`, plain ES modules, Cloudflare Pages.

**Spec:** `docs/2026-11-08-xrpl-vanity-localization-design.md` (approved 2026-11-08). Reference implementation: the `xrpl-file` repository (`app/XrplFile.App.Core/Localization`, `site/src/js/languages.mjs`, `site/src/js/prefs.mjs`, `site/build.mjs`).

## Global Constraints

- Languages, in menu order: `en` English, `ru` Русский, `de` Deutsch, `es` Español, `pt` Português, `zh` 中文. HTML tags `en ru de es pt zh-Hans`; number locales `en-GB ru-RU de-DE es-ES pt-BR zh-Hans-CN`.
- Default: remembered choice, else system or browser language by primary subtag, else English.
- Not in scope: the CLI, `PRIVACY.md` and `/privacy/`, the MSIX manifest and Store listing, `README.md`, `docs/` (except the status notes in Task 15).
- File formats never change with the language: TSV export, pattern list file, `settings.json` keys, history file, QR payload.
- All copy in every language is written with the `writing-site-copy` skill and contains no long dash (U+2014) and no en dash (U+2013).
- No new NuGet or npm dependency. No request from the site to another origin; no inline script, style or event attribute on the site.
- Code and comments in English. Explicit types, no `var`. No AI attribution in commits or the PR.
- App tests run with `dotnet run --project tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj` (not `dotnet test`). Site tests run with `node --test "site/test/*.test.mjs"`, the site builds with `node site/build.mjs`.
- Commits only when the owner has given the word for this branch; the commit steps below mark where a commit belongs.

## Review Focus

1. Language changed while the tutorial runs: the tutorial's sandbox restore must not put the old `Language` back. Test in Task 2.
2. A message already on screen (pattern validation, estimate note, a GPU failure) when the language changes must re-render in the new language, and the typed pattern, results and running search must survive. Test in Task 5.
3. A settings file written by the current release (no `Language` field) and one with an unknown code (`"fr"`, `""`) must load and fall back to the system rule. Tests in Tasks 1 and 2.
4. A visitor with blocked `localStorage` who picks English on `/ru/` must stay on English pages; a visitor who opened `/de/` must never be redirected. Test in Task 12.
5. Speeds and counts formatted with a decimal comma (`1,5 G/s`) must still match `docs/benchmarks.md` in the site test, and the app's estimate must never print `NaN` or `∞` in any locale. Tests in Tasks 3 and 11.

---

## Part A: the application

### Task 1: Localization core

**Files:**
- Create: `src/XrplVanity.App.Core/Localization/UiLanguage.cs`, `TextId.cs`, `UiText.cs`, `TextCatalog.cs`, `PluralRules.cs`, `Localizer.cs`, `SettingsText.cs`
- Create: `src/XrplVanity.App.Core/Localization/Text/{en,ru,de,es,pt,zh}.json`
- Modify: `src/XrplVanity.App.Core/XrplVanity.App.Core.csproj` (embed `Localization/Text/*.json` as `EmbeddedResource` with `LogicalName` `XrplVanity.App.Core.Localization.Text.%(Filename)%(Extension)`)
- Test: `tests/XrplVanity.App.Tests/Localization/CatalogTests.cs`, `LocalizerTests.cs`

**Interfaces:**
- Produces: `UiLanguage` (`Code`, `NativeName`, `HtmlTag`, `Culture`; statics `English Russian German Spanish Portuguese Chinese`, `All`, `FromCode(string?)`, `FromTag(string?)`); `readonly record struct TextId(string Key)` with `TextId.Of(string area, string name)`; `UiText` (`Of(TextId)`, `Of(TextId, params (string Name, object Value)[])`, implicit from `TextId`, value equality); `TextCatalog` (`For(UiLanguage)`, `Language`, `this[TextId]`, `this[UiText?]`, `Render(UiText?)`, `Culture`); `Localizer(string? remembered, CultureInfo systemCulture)` with `Language`, `Text`, `Choices`, `event Action? Changed`, `static UiLanguage Pick(string?, CultureInfo)`, `void Choose(UiLanguage)`; `SettingsText.LanguageTitle`, `LanguageLabel`, `LanguageNotRemembered`.

Copy the six files from `xrpl-file/app/XrplFile.App.Core/Localization` and adapt the namespace to `XrplVanity.App.Core.Localization`. Differences from `xrpl-file`: `Localizer` does not persist anything (persistence goes through `SearchViewModel.SaveSettings`, Task 3), so its constructor takes the remembered code, not a store, and `Choose` returns `void`; drop `XrpAmount` support from `UiArg`. Area classes follow `SettingsText`'s pattern (`private static TextId Id([CallerMemberName] string name = "") => TextId.Of("<Area>", name);`).

The six catalogs start with the three `Settings.Language*` keys. Non-English catalogs hold the English text until their language task (Tasks 13 and 14) translates them. Because of that, switching tests cannot tell languages apart by catalog text; they use a test-only catalog: `internal static TextCatalog Transformed(UiLanguage language, Func<string, string> phrase)`, which renders English phrases through `phrase` (tests use `p => "[" + language.Code + "] " + p`). `XrplVanity.App.Tests` sees internals of `XrplVanity.App.Core` (add `InternalsVisibleTo` if missing).

- [ ] **Step 1: Write the failing tests**

`CatalogTests`:
- `Every_Catalog_Has_Exactly_The_English_Keys` (theory over `UiLanguage.All`): key sets equal.
- `Every_Catalog_Has_The_English_Placeholders` (theory): for each key, the set of placeholder names equals English's; plural objects only use `zero one two few many other` and always have `other`.
- `Every_Declared_Id_Exists_In_English`: reflection over every public static `TextId` field of every type in `XrplVanity.App.Core.Localization` named `*Text`; each key is `"<Area>.<Field>"` and is in `en.json`.
- `No_English_Key_Is_Unused`: every `en.json` key is a declared id.
- `Russian_Plurals_Pick_Few_And_Many`: `PluralRules.FormOf(UiLanguage.Russian, n)` gives `One` for 1 and 21, `Few` for 3, `Many` for 5 and 11; English gives `One` for 1 and `Other` for 2; Chinese gives `Other` for 1.

`LocalizerTests`:
- `Pick_Prefers_A_Remembered_Language` (`"de"`, culture `ru-RU` → German).
- `Pick_Ignores_An_Unknown_Remembered_Code` (`"fr"` and `""` with `ru-RU` → Russian).
- `Pick_Maps_Regional_Tags` (`zh-TW`→Chinese, `pt-PT`→Portuguese, `es-419`→Spanish).
- `Pick_Falls_Back_To_English` (`fr-FR`, `CultureInfo.InvariantCulture` → English).
- `Choose_Raises_Changed_Once_And_Only_On_A_Change`.

- [ ] **Step 2: Run the tests, expect a compile failure** (types missing).
- [ ] **Step 3: Implement the core** as described above.
- [ ] **Step 4: Run the tests, expect PASS.**
- [ ] **Step 5: Commit** `feat(app): add the localization core and language catalogs`

### Task 2: Choosing, remembering and cascading the language

**Files:**
- Modify: `src/XrplVanity.App.Core/Storage/AppSettings.cs`, `src/XrplVanity.App/MauiProgram.cs`, `src/XrplVanity.App.Core/Layout/MainLayout.razor`, `src/XrplVanity.App.Core/Pages/SettingsPage.razor(.cs)`, `src/XrplVanity.App.Core/Components/ViewModelComponent.cs`, `src/XrplVanity.App.Core/Pages/ViewModelPage.cs`, `src/XrplVanity.App.Core/_Imports.razor`
- Create: `src/XrplVanity.App.Core/wwwroot/js/language.js`, `src/XrplVanity.App.Core/Localization/LocalizedComponent.cs`
- Test: `tests/XrplVanity.App.Tests/JsonSettingsStoreTests.cs`, `SettingsPageTests.cs`, `MainLayoutTests.cs`, `SearchViewModelSandboxTests.cs`

**Interfaces:**
- Consumes: Task 1's `Localizer`, `TextCatalog`.
- Produces: `AppSettings.Language` (`string?`, default null, part of `Equals`); `LocalizedComponent : ComponentBase` with `[CascadingParameter] TextCatalog? Catalog` and `protected TextCatalog T => Catalog ?? TextCatalog.For(UiLanguage.English)`; the same `T` property on `ViewModelComponent` and `ViewModelPage`. Every later task reads copy through `T[...]`.

Wiring:
- `MauiProgram`: register `Localizer` as a singleton built from `sp.GetRequiredService<ISettingsStore>().Load().Language` and `CultureInfo.CurrentUICulture`.
- `MainLayout` wraps its markup in `<CascadingValue Value="Localizer.Text">`, subscribes to `Localizer.Changed` and re-renders through `InvokeAsync`; after each render where the language changed it calls `setLang(tag)` from `language.js` (imported the way `TutorialOverlay` imports `tutorial.js`), which sets `document.documentElement.lang`.
- Settings page: a first card "Language" (`SettingsText.LanguageTitle`) with `select.language` listing `UiLanguage.All` by `NativeName`. On change: `Localizer.Choose(lang)`, then `Vm.SaveSettings(s => s with { Language = lang.Code })`, then show `SettingsText.LanguageNotRemembered` when `ISettingsStore.LastError` is not null.
- The tutorial sandbox (`SearchViewModel.BeginSandbox`/`RestoreSandbox`) must not snapshot or restore `Language`.
- Without a cascade (bUnit tests that render a component alone) `T` is English, so existing tests keep passing unchanged.

- [ ] **Step 1: Failing tests:**
  - `JsonSettingsStoreTests.A_File_Without_Language_Loads_As_Null` (JSON of the current release) and `Language_Round_Trips`.
  - `SettingsPageTests.Choosing_A_Language_Switches_And_Saves_It`: change `select.language` to `ru`; `Localizer.Language == UiLanguage.Russian`, `settings.Current.Language == "ru"`.
  - `SettingsPageTests.A_Failed_Save_Says_The_Language_Is_Not_Remembered`: `MemorySettingsStore` with a forced `LastError`; `.language-not-remembered` is rendered.
  - `MainLayoutTests.Changing_The_Language_Re_Renders_The_Layout_In_It`: render `MainLayout` with a body that prints `T.Language.Code`; `Choose(German)`; markup shows `de`.
  - `LocalizerTests.The_Settings_Language_Feeds_The_Localizer`: `new Localizer(settingsFromJsonWith("fr").Language, ru-RU)` is Russian (unknown remembered code falls back to the system rule).
  - `SearchViewModelSandboxTests.A_Language_Chosen_During_The_Tutorial_Survives_Its_End`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement the wiring above.**
- [ ] **Step 4: Run all app tests, expect PASS.**
- [ ] **Step 5: Commit** `feat(app): choose the interface language in Settings`

### Task 3: Formatting in the language's locale

**Files:**
- Modify: `src/XrplVanity.App.Core/Components/Formatting.cs` and its 6 call sites
- Create: `src/XrplVanity.App.Core/Localization/CommonText.cs`
- Test: `tests/XrplVanity.App.Tests/Localization/FormattingTests.cs`

**Interfaces:**
- Produces: `Formatting.Candidates(double value, TextCatalog text)`, `Formatting.Rate(double perSecond, TextCatalog text)`, `Formatting.Duration(TimeSpan? value, TextCatalog text)`; `CommonText.Days` (plural, `{count}` with one decimal), `Hours` (`{hours} h {minutes} min`), `Minutes`, `Seconds`, `MoreThanAYear`, `LessThanASecond`, `NoValue` (the dash shown for an unknown value; the English text is `-`).

Numbers use `text.Culture`; SI suffixes `E P T G M` and `/s` are not translated. The match time (`HH:mm:ss` in `ResultsList.razor`) is unchanged.

- [ ] **Step 1: Failing tests:** `Rate_Uses_The_Locale_Decimal_Separator` (`1.5e9` → `"1.5 G/s"` English, `"1,5 G/s"` Russian and German); `Candidates_Groups_Small_Numbers_By_Locale` (`12345` → `"12,345"` en, `"12 345"` with U+00A0 ru, `"12.345"` de); `Duration_Words_Come_From_The_Catalog` (90 min → English `"1 h 30 min"`); `Infinite_And_NaN_Rates_Show_The_No_Value_Text` (for every language, never contains `∞` or `NaN`).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Change the signatures and call sites**; components pass `T` from Task 2.
- [ ] **Step 4: Run all app tests, expect PASS.**
- [ ] **Step 5: Commit** `feat(app): format numbers and durations per language`

### Task 4: View-model messages and notifications as `UiText`

**Files:**
- Modify: `src/XrplVanity.App.Core/ViewModels/SearchViewModel.cs`, `src/XrplVanity.App.Core/Services/INotificationService.cs`, `src/XrplVanity.App/Services/WindowsNotificationService.cs`, `src/XrplVanity.App.Core/Storage/DpapiResultStore.cs` (`ReadOnlyReason`), `tests/XrplVanity.App.Tests/Fakes.cs`
- Create: `src/XrplVanity.App.Core/Localization/SearchText.cs`, `HistoryText.cs`, `NotificationText.cs`
- Test: `tests/XrplVanity.App.Tests/SearchViewModelTests.cs`, `HistoryPageTests.cs`

**Interfaces:**
- Produces: every string-typed message property of `SearchViewModel` becomes `UiText?` with the same name (estimate note, entry hint, near-start hint, start blocker, validation per chip, calibration failure, history write error, read-only reason). `INotificationService.Notify(UiText title, UiText body)`; `WindowsNotificationService` renders with the injected `Localizer`'s current catalog at the moment of the call.
- Lower-layer details (`ex.Message`, `target.Failure`, device names) travel as a string value inside the `UiText`, never as the whole message.

- [ ] **Step 1: Failing tests:** rewrite the existing message assertions in `SearchViewModelTests` to compare `UiText` (`Assert.Equal(UiText.Of(SearchText.AddPatternForEstimate), vm.EstimateNote)`), and add `A_Gpu_Failure_Keeps_The_Driver_Text_Inside_A_Translated_Sentence`, `A_Match_Notification_Carries_Address_Pattern_And_Position_As_Values`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement**; English phrases are today's literals with the long dash removed.
- [ ] **Step 4: Run all app tests, expect PASS.**
- [ ] **Step 5: Commit** `refactor(app): carry view-model messages as text ids`

### Task 5: The Search page and its components

**Files:**
- Modify: `Pages/SearchPage.razor(.cs)`, `Components/PatternEditor.razor(.cs)`, `DevicePicker.*`, `EstimateCard.*`, `LiveStats.*`, `DutySlider.*`, `StatusPill.*`, `ResultsList.*`, `QrCode.*`, `QrExportDialog.*`, `Banner.razor`, `PatternListFile.cs` (dialog titles and filters only), all under `src/XrplVanity.App.Core`
- Create: area classes `PatternsText.cs`, `DevicesText.cs`, `ResultsText.cs`, `QrText.cs` in `Localization/`
- Test: `tests/XrplVanity.App.Tests/SearchPageTests.cs` and the existing component tests

Every user-visible literal (text, `title`, `aria-label`, `placeholder`) goes through `T[...]`; components that are not view-model bound inherit `LocalizedComponent`. `TsvExport` and the pattern list file format are untouched.

- [ ] **Step 1: Failing test** `SearchPageTests.Switching_Language_Keeps_State_And_Translates_Messages`: render `SearchPage` inside a test host that cascades a swappable `TextCatalog`, starting with English; add a reachable chip, inject one result, then type the unreachable entry `"0"` so its validation message shows; swap the cascade to `TextCatalog.Transformed(UiLanguage.Russian, p => "[ru] " + p)`; assert the chip, the result row and the typed entry are unchanged and the validation message now starts with `[ru] `.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Move the literals into `en.json`** and copy each new key into the five other catalogs with its English text.
- [ ] **Step 4: Run all app tests, expect PASS.**
- [ ] **Step 5: Commit** `feat(app): translatable Search page`

### Task 6: History, Settings, navigation and dialogs

**Files:**
- Modify: `Pages/HistoryPage.*`, `Pages/SettingsPage.*` (remaining literals), `Layout/NavRail.razor`, `Components/ClosePrompt.*`, `Components/ConfirmDialog.*`
- Create: `ShellText.cs`, `DialogText.cs` (and extend `HistoryText`, `SettingsText`)
- Test: existing `HistoryPageTests`, `SettingsPageTests`, `NavRailTests`, `ClosePromptTests`, `ConfirmDialogTests`

The About line keeps the product name and font names; "Project on GitHub", "StaticBit on GitHub" are translated, "X" and "staticbit.io" are not.

- [ ] **Step 1: Failing test** `NavRailTests.Nav_Labels_Follow_The_Language` (render with a cascaded `TextCatalog.Transformed(UiLanguage.German, p => "[de] " + p)`; every nav label starts with `[de] `).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Move the literals** as in Task 5.
- [ ] **Step 4: Run all app tests, expect PASS;** then `grep` the `.razor` files of `Pages/`, `Components/`, `Layout/` and `Tutorial/` for `>[A-Z][a-z]` text nodes and confirm only product names remain.
- [ ] **Step 5: Commit** `feat(app): translatable History, Settings and dialogs`

### Task 7: The tutorial

**Files:**
- Modify: `src/XrplVanity.App.Core/Tutorial/TutorialScript.cs`, `TutorialStep.cs` (`TutorialStepInfo.Title`/`Body` become `TextId`), `TutorialService.cs` (`ShortestWordHint` becomes a `TextId`, the hint property becomes `UiText?`), `TutorialOverlay.razor(.cs)`, `Components/QrExportDialog.razor` (tutorial note)
- Create: `Localization/TutorialText.cs`
- Test: `tests/XrplVanity.App.Tests/TutorialServiceTests.cs`, `TutorialOverlayTests.cs`, new `TutorialScriptTests.cs`

- [ ] **Step 1: Failing test** `TutorialScriptTests.Every_Step_Names_Buttons_By_Their_Catalog_Label`: for each language, the Search-step body contains the catalog's `Search.StartButton` text, the list-files step contains `Patterns.SaveList` and `Patterns.LoadList`, the devices step contains `Devices.MeasureSpeed`. (Holds for English now; keeps translations honest later.)
- [ ] **Step 2: Run, expect FAIL** (compile: `TextId` vs `string`).
- [ ] **Step 3: Implement**; English copy unchanged.
- [ ] **Step 4: Run all app tests, expect PASS.**
- [ ] **Step 5: Commit** `feat(app): translatable tutorial`

### Task 8: The tray menu

**Files:**
- Modify: `src/XrplVanity.App.Core/Services/ITrayService.cs` (`TrayMenu` gains `string LanguageCode`), `src/XrplVanity.App/Services/WindowsTrayService.cs`, `src/XrplVanity.App/Services/TrayCoordinator.cs`
- Create: `Localization/TrayText.cs`
- Test: `tests/XrplVanity.App.Tests/TrayCoordinatorTests.cs` only if `TrayCoordinator` is reachable from the test project; otherwise this task is covered by the VM check in Task 16

`TrayCoordinator` subscribes to `Localizer.Changed` and pushes a `TrayMenu` with the new `LanguageCode`, so its "only a changed menu" check lets it through. `WindowsTrayService` renders Show, Duty, Stop search, Exit from the `Localizer`'s catalog; the tooltip stays "XRPL Vanity".

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build** `dotnet build src/XrplVanity.App/XrplVanity.App.csproj -c Debug` succeeds with no new warnings.
- [ ] **Step 3: Commit** `feat(app): translatable tray menu`

---

## Part B: the site machinery

### Task 9: Language list and preferences

**Files:**
- Create: `site/src/js/languages.mjs`, `site/src/js/prefs.mjs`
- Test: `site/test/languages.test.mjs`

Copy both modules from `xrpl-file/site/src/js` and keep `LANGS`, `ACTIVE_LANGS`, `DEFAULT_LANG`, `LANGUAGE_NAMES`, `htmlLang`, `localeOf`, `langFromTag`, `pageLang`, `pathPrefix`, `homePath`, `activeLang`, `getLang`, `setLang`, `equivalentPath`, `safeLocalStorage`. Drop the theme functions and the file-page rule. `LANG_KEY = 'xv-lang'`. `ACTIVE_LANGS` starts as `['en']`. Add `ENGLISH_ONLY = ['/privacy/']` and make `equivalentPath(path, lang)` return `homePath(lang)` for an English-only path.

- [ ] **Step 1: Failing tests:** `getLang` cases (stored active, stored inactive, `zh-TW`→`zh` only when active, fallback `en`, throwing storage); `equivalentPath` (`/`↔`/ru/`, `/desktop/`↔`/de/desktop/`, `/privacy/`→`/ru/`); `the C# and JS language tables agree` (parse `UiLanguage.cs` with a regex for `new("<code>", "<name>", "<tag>", "<culture>")` and compare with `LANGS`, `LANGUAGE_NAMES`, `htmlLang`, `localeOf`).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run site tests, expect PASS.**
- [ ] **Step 5: Commit** `feat(site): language list and remembered language`

### Task 10: Language-aware build, chrome and switcher

**Files:**
- Modify: `site/build.mjs`, `site/chrome-markup.mjs`, `site/checks.mjs` (`findBrokenLinks`), `site/src/css/site.css` (switcher)
- Create: `site/chrome-strings.mjs`
- Test: `site/test/build.test.mjs`, `site/test/seo.test.mjs`, `site/test/checks.test.mjs`

**Interfaces:**
- Produces: `CHROME_STRINGS[lang]` with `skip`, `nav.desktop`, `nav.cli`, `nav.privacy`, `nav.support`, `language` (the switcher's label), `published`, `licence`, `source`, `ogImageAlt`; `renderChrome(part, page)` where `page` gains `lang`.

Build rules:
- A page's language is its `<html lang>` (`pageLang`); `checkLanguagePages` (from `xrpl-file`) refuses a page whose tag does not match its folder, a folder for a language not in `ACTIVE_LANGS`, and an active language missing an English page other than those in `ENGLISH_ONLY`.
- Head: canonical as today; for a page not in `ENGLISH_ONLY`, one `<link rel="alternate" hreflang="<htmlLang>">` per active language plus `hreflang="x-default"` to English; JSON-LD on each language's home with that page's description and `inLanguage`.
- Header: nav links carry the page language's prefix, except Privacy, which always links `/privacy/`; the switcher is `<details class="lang">` with `<summary>` showing the current `NativeName` and one `<a hreflang lang data-lang>` per active language to `equivalentPath(pagePath, lang)`, the current one with `aria-current="true"`.
- Sitemap: every built page except every language's `404.html`.
- `findBrokenLinks` already resolves site paths; it must accept `/ru/` style paths that exist and keep refusing ones that do not.

- [ ] **Step 1: Failing tests** (with a fixture `siteDir` holding `en` and a fake `ru` page, `ACTIVE_LANGS` injected as a build option `langs` defaulting to `ACTIVE_LANGS`): `a page in the ru folder with lang="en" is refused`; `an active language missing desktop/ is refused`; `hreflang alternates list every active language and x-default`; `privacy has a canonical and no alternates`; `the switcher links the same page in every language and privacy to each home`; `the sitemap lists every language's pages`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run site tests and `node site/build.mjs`, expect PASS** and an English-only `dist/` identical in pages to before except the switcher and hreflang.
- [ ] **Step 5: Commit** `feat(site): build pages per language with hreflang and a language menu`

### Task 11: Structural parity and facts per language

**Files:**
- Create: `site/test/translations.test.mjs`
- Modify: `site/test/docs-pages.test.mjs`, `site/test/home.test.mjs`

- `translations.test.mjs`, looping over `ACTIVE_LANGS` minus `en` (empty for now): every English page except `ENGLISH_ONLY` exists under the prefix; same `id` attributes in the same order; same internal links after `equivalentPath`; same `img` and `script` `src`s (so each home loads `home.mjs` and its Store buttons); same `<code>` and `<pre>` contents; `CHROME_STRINGS[lang]` has exactly English's keys.
- `docs-pages.test.mjs`: the Options-table check runs for `/cli/` of every active language (the table's `aria-label` is translated, so select it by `data-table="options"`, added to the English page in this task).
- `home.test.mjs`: speeds on every active home page are parsed with that language's decimal separator before matching `docs/benchmarks.md`.

- [ ] **Step 1: Write the tests;** **Step 2:** run, PASS for English. **Step 3: Commit** `test(site): hold every language to the English pages`

### Task 12: First-visit redirect and remembered choice

**Files:**
- Create: `site/src/js/lang-redirect.js` (classic script), `site/src/js/lang.mjs` (module)
- Modify: `site/chrome-markup.mjs` (head: `<script src="/js/lang-redirect.js"></script>` before the stylesheets, `<script type="module" src="/js/lang.mjs"></script>`)
- Test: `site/test/lang-redirect.test.mjs`

Rules (spec, "First visit"): the redirect runs only on an English page not in `ENGLISH_ONLY`, only when `localStorage` can be read and written and holds no choice, and sends to the first browser language that is active and not English. It stores nothing. `lang.mjs` stores `data-lang` on click of a switcher link (`setLang`) before navigation. The classic script cannot import, so it carries its own copies of the active list and key; the test pins them to `languages.mjs` and `prefs.mjs`.

- [ ] **Step 1: Failing tests** (run the script in `node:vm` with fake `location`, `navigator`, `localStorage`): `redirects / to /ru/ for a ru browser with empty storage`; `redirects /desktop/ to /de/desktop/`; `never redirects /de/ or /privacy/`; `never redirects when storage throws`; `never redirects when en is stored`; `keeps search and hash`; `its language list and key equal ACTIVE_LANGS and LANG_KEY`; `lang.mjs stores the clicked language`.
- [ ] **Step 2: Run, expect FAIL.** **Step 3: Implement.** **Step 4: Run site tests and build, expect PASS** (the build's inline-code check must stay green). **Step 5: Commit** `feat(site): send a first visit to the browser's language`

---

## Part C: the languages

Each language task is the same shape. It uses the `writing-site-copy` skill for every phrase, keeps product names, flags, commands, addresses and examples as they are, uses the interface's own translated button names wherever copy names a button, and never uses a long or en dash.

**Files per language `<xx>`:**
- Modify: `src/XrplVanity.App.Core/Localization/Text/<xx>.json` (every key translated)
- Create: `site/src/<xx>/index.html`, `site/src/<xx>/desktop/index.html`, `site/src/<xx>/cli/index.html`, `site/src/<xx>/support/index.html`, `site/src/<xx>/404.html`
- Modify: `site/chrome-strings.mjs` (the language's entry), `site/src/js/languages.mjs` (append to `ACTIVE_LANGS`), `site/src/js/lang-redirect.js` (its copy of the list)
- Modify: `tests/XrplVanity.App.Tests/Localization/CatalogTests.cs` (add the code to `Translated`)

Steps per language:
- [ ] **Step 1:** add the code to `CatalogTests.Translated` and to `ACTIVE_LANGS`; run both test suites; expect FAIL (`Translated_Catalogs_Differ_From_English`: every phrase differs from English except the allow-list `CPU`, `GPU`, `QR`, `XRPL Vanity`, `OpenCL`, `CUDA`, `TSV`, and placeholders-only phrases; and the missing pages).
- [ ] **Step 2:** translate the catalog and write the five pages.
- [ ] **Step 3:** run both test suites and `node site/build.mjs`; expect PASS.
- [ ] **Step 4: Commit** `feat: <Language> interface and site`

`Translated_Catalogs_Differ_From_English` is added to `CatalogTests` in Task 13 (the first language).

### Task 13: Русский
Also: add `site/src/fonts/martian-mono-5.3.0-cyrillic.woff2` from `@fontsource-variable/martian-mono@5.3.0` `files/martian-mono-cyrillic-standard-normal.woff2` (unchanged), a second `@font-face` with that file's `unicode-range` from the package's CSS, its line and sha256 in `SOURCES.txt`; extend the font test (or `theme.test.mjs`) so every `@font-face` file exists and is listed in `SOURCES.txt`. Russian plurals use `one few many other`.

### Task 14: Deutsch, Español, Português, 中文
Four commits, one per language, in this order: `de`, `es`, `pt`, `zh`. Chinese: `<html lang="zh-Hans">`, no plural forms beyond `other`, and add under `:lang(zh)` in `site/src/css/site.css` and the app's `app.scss` a body font stack ending in `"Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", sans-serif` only if Task 16's screenshots show the fallback is wrong.

---

## Part D: finishing

### Task 15: Status notes and second-model read

**Files:**
- Modify: `site/README.md` (a "Languages" section: the table of codes, prefixes, tags, locales and status, as in `xrpl-file/site/README.md`), `docs/desktop-app.md` (a "Languages" paragraph: where to switch, the default rule, the status line)

Status: English and Russian "checked by the owner" (Russian only after the owner has read it; until then "awaiting the owner's read"); German, Spanish, Portuguese, Chinese "translated from English, awaiting a native speaker's review".

- [ ] **Step 1:** check the Codex quota with the `codex-quota` skill; if the windows allow, run `codex-review` on each of `de`, `es`, `pt`, `zh` (catalog and pages, with `en` as reference) asking for meaning errors, unnatural phrasing and button names that disagree with the catalog. Verify each remark before applying it.
- [ ] **Step 2:** write the status notes.
- [ ] **Step 3:** both test suites and the build pass; **commit** `docs: language status and switching`.

### Task 16: Verification in MauiLab and the PR

- [ ] **Step 1:** run the desktop app in the MauiLab VM with the `maui-vm-lab` skill: Debug, `-ExeName XrplVanity.App.exe`, without `-RollBackFirst`. Switch through all six languages on Settings; screenshot Search, History, Settings and one tutorial step in Russian and Chinese; open the tray menu in Russian; trigger a match toast in German. Check: no clipped buttons, no English left, Chinese glyphs render.
- [ ] **Step 2:** `node site/build.mjs && node site/serve.mjs`; open `/`, `/ru/`, `/zh/cli/`, `/privacy/` in the built-in browser; check the switcher, the Cyrillic font, Chinese fallback, the redirect from `/` with a `ru` browser and none after picking English.
- [ ] **Step 3:** run the full app and site test suites once more.
- [ ] **Step 4:** with the owner's word, push and open a PR into `dev`; note in the body that the Linux job may hit its 45-minute limit.
