# XRPL Vanity — Plan G: Windows Desktop Application Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship an installable Windows desktop application (MSIX) for xrpl-vanity in the Wallet's visual language, driving the `XrplVanity.Search` session from Plan F.

**Architecture:** A `net10.0` Razor class library `XrplVanity.App.Core` holds every page, component, the view model, the stores and the service interfaces, so bUnit tests run without the Windows target. A thin MAUI Blazor Hybrid host `XrplVanity.App` (`net10.0-windows10.0.22621.0`) provides DI, the `BlazorWebView`, the tray icon, Windows notifications, window-close handling, the clipboard and the file dialog, and is packaged as MSIX.

**Tech Stack:** .NET MAUI 10 (`$(MauiVersion)` from the `maui-windows` workload), `Microsoft.AspNetCore.Components.WebView.Maui`, Bootstrap 5.3.3 (vendored CSS), `DartSassBuilder` 1.1.0, `System.Security.Cryptography.ProtectedData` 10.0.11, `H.NotifyIcon.Maui` 2.4.1, Windows App SDK `AppNotificationManager`, xUnit v3 4.0.0 + `bunit` 2.9.0, fonts Inter and Cascadia Code (SIL OFL, shipped locally).

**Spec:** `docs/2026-09-05-xrpl-vanity-desktop-design.md` — section "Plan G — the desktop application". **Visual authority:** `docs/mockups/xrpl-vanity-desktop.html` — the approved mockups; when the spec's prose and the mockup differ on layout, spacing, copy or colour, the mockup wins.

## Global Constraints

- Plan F is merged (`XrplVanity.Search` with `SearchSession`, `SearchRequest`, `SearchTarget`, `SearchProgress`, `SearchSummary`, `SearchEstimator`, `DeviceCatalog`, `DeviceCatalogEntry`). Do not change Plan F's public surface.
- `XrplVanity.App.Core` and `XrplVanity.App.Tests` target `net10.0`; `XrplVanity.App` targets `net10.0-windows10.0.22621.0` only (`SupportedOSPlatformVersion` 10.0.17763.0). `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<LangVersion>latest</LangVersion>`.
- Identity: `ApplicationTitle` `XRPL Vanity`, `ApplicationId` `io.staticbit.vanity`, `ApplicationDisplayVersion` `1.0.0`, `ApplicationVersion` `1`.
- Palette tokens, verbatim: `--bg #0D0D0D`, `--box-bg #1A1A1A`, `--chips #2D2D2D`, `--dark-gray #282828`, `--text #F9F9F9`, `--text-secondary #A8A8A8`, `--mint #64CF95`, `--beige #EBAF8E`, `--negative #D6776A`, `--yellow #D3E7AB`, `--secondary-dark #202222`. Cards radius 12px with 1px `--dark-gray` border; chips radius 10px. Fonts: Inter (text), Cascadia Code → Consolas → monospace (addresses, seeds). Transitions 150–200 ms ease-out; everything animated is disabled under `prefers-reduced-motion: reduce`.
- Seeds: masked as `••••••••` by default; revealed seeds hide after the reveal timeout (default 15 s) and on navigation; copying a seed goes through `IClipboardService.CopySecretAsync` with the configured clear delay (default 30 s); notifications, the tray, logs and `settings.json` never contain a seed; `results.dat` is the only file that does, DPAPI-protected.
- Tests: `dotnet run --project tests/XrplVanity.App.Tests -c Release [-- -class "*Name"]`; never `dotnet test`. Existing suites must stay green. The MAUI host has no automated UI tests; Task 8's manual checklist is mandatory before the plan is reported complete.
- The Linux harness must keep passing: `linux/in-container.sh` builds `XrplVanity.Portable.slnf`, not the solution.
- Commit messages: conventional commits, English, no attribution trailers. Explicit types (no `var`), `_camelCase` private fields, English comments only where they explain a decision.

---

## File structure

| Path | Responsibility |
| --- | --- |
| `src/XrplVanity.App.Core/XrplVanity.App.Core.csproj` | Razor class library; references Search; DartSassBuilder; `InternalsVisibleTo XrplVanity.App.Tests` |
| `src/XrplVanity.App.Core/Services/*.cs` | `IClipboardService`, `INotificationService`, `ITrayService`, `IWindowService`, `IFileDialogService`, `ISettingsStore`, `IResultStore`, `TrayMenu`/`TrayAction`, `CloseBehavior` |
| `src/XrplVanity.App.Core/Storage/AppSettings.cs`, `JsonSettingsStore.cs`, `StoredResult.cs`, `DpapiResultStore.cs` | Persistence |
| `src/XrplVanity.App.Core/ViewModels/SearchViewModel.cs`, `PatternChip.cs`, `TargetChoice.cs` | UI state and session lifecycle |
| `src/XrplVanity.App.Core/Components/*.razor` (+ `.razor.cs`) | `PatternEditor`, `EstimateCard`, `DevicePicker`, `DutySlider`, `LiveStats`, `ResultsList`, `ClosePrompt`, `StatusPill`, `Banner` |
| `src/XrplVanity.App.Core/Pages/SearchPage.razor`, `HistoryPage.razor`, `SettingsPage.razor` | Pages |
| `src/XrplVanity.App.Core/Layout/MainLayout.razor`, `NavRail.razor`, `Routes.razor`, `_Imports.razor` | Shell |
| `src/XrplVanity.App.Core/wwwroot/scss/**`, `wwwroot/fonts/**`, `wwwroot/lib/bootstrap/bootstrap.min.css` | Styles and assets |
| `src/XrplVanity.App/*` | MAUI host: `MauiProgram.cs`, `App.xaml(.cs)`, `MainPage.xaml(.cs)`, `wwwroot/index.html`, `Platforms/Windows/**`, `Services/Windows*.cs`, `Resources/**`, `Properties/PublishProfiles/MSIX-win-x64.pubxml` |
| `tests/XrplVanity.App.Tests/**` | bUnit component tests, view-model tests, store tests |
| `app/make-cert.ps1` | Self-signed certificate for MSIX sideloading |
| `docs/desktop-app.md` | Install, run, publish, manual smoke checklist |
| `XrplVanity.Portable.slnf` | Solution filter without the MAUI project (used by the Linux harness) |

---

### Task 1: Projects, styles, fonts and a first bUnit test

**Files:**
- Create: `src/XrplVanity.App.Core/XrplVanity.App.Core.csproj`, `_Imports.razor`, `wwwroot/scss/app.scss`, `wwwroot/scss/common/_var-colors.scss`, `wwwroot/scss/common/_typography.scss`, `wwwroot/scss/common/_motion.scss`, `wwwroot/scss/components/_cards.scss`, `wwwroot/scss/components/_chips.scss`, `wwwroot/scss/components/_buttons.scss`, `wwwroot/lib/bootstrap/bootstrap.min.css`, `wwwroot/fonts/*`, `Components/StatusPill.razor`
- Create: `tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj`, `tests/XrplVanity.App.Tests/StatusPillTests.cs`
- Create: `XrplVanity.Portable.slnf`
- Modify: `XrplVanity.sln`, `.gitignore`, `linux/in-container.sh:30`

**Interfaces:**
- Produces: the CSS custom properties listed in Global Constraints (defined on `:root` in `_var-colors.scss`); utility classes `.card-box`, `.chip`, `.btn-primary-mint`, `.btn-secondary-dark`, `.mono`, `.num`; `StatusPill` component with parameter `SearchState State` (enum `Idle, Starting, Searching, Stopping` defined in `Components/StatusPill.razor.cs`, later reused by the view model).

- [ ] **Step 1: Create the Razor class library**

`src/XrplVanity.App.Core/XrplVanity.App.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AddRazorSupportForMvc>false</AddRazorSupportForMvc>
    <StaticWebAssetBasePath>_content/XrplVanity.App.Core</StaticWebAssetBasePath>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\XrplVanity.Search\XrplVanity.Search.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="10.0.0" />
    <PackageReference Include="DartSassBuilder" Version="1.1.0" PrivateAssets="all" />
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.11" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="XrplVanity.App.Tests" />
  </ItemGroup>
  <ItemGroup>
    <SupportedPlatform Include="browser" />
  </ItemGroup>
</Project>
```

If `Microsoft.AspNetCore.Components.Web` 10.0.0 does not restore, use the latest 10.0.x listed by `dotnet package search Microsoft.AspNetCore.Components.Web --exact-match`.

`_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using XrplVanity.App.Core.Components
@using XrplVanity.App.Core.Services
@using XrplVanity.App.Core.ViewModels
```

- [ ] **Step 2: Add the design tokens and base styles**

`wwwroot/scss/common/_var-colors.scss`:

```scss
:root {
  --bg: #0D0D0D;
  --box-bg: #1A1A1A;
  --chips: #2D2D2D;
  --dark-gray: #282828;
  --text: #F9F9F9;
  --text-secondary: #A8A8A8;
  --mint: #64CF95;
  --beige: #EBAF8E;
  --negative: #D6776A;
  --yellow: #D3E7AB;
  --secondary-dark: #202222;
  --radius-card: 12px;
  --radius-chip: 10px;
  --font-text: "Inter", "Segoe UI", system-ui, sans-serif;
  --font-mono: "Cascadia Code", "Consolas", monospace;
  --ease: 180ms ease-out;
}
```

`wwwroot/scss/common/_typography.scss`:

```scss
@font-face { font-family: "Inter"; font-style: normal; font-weight: 400; src: url("../fonts/Inter-Regular.woff2") format("woff2"); }
@font-face { font-family: "Inter"; font-style: normal; font-weight: 500; src: url("../fonts/Inter-Medium.woff2") format("woff2"); }
@font-face { font-family: "Inter"; font-style: normal; font-weight: 600; src: url("../fonts/Inter-SemiBold.woff2") format("woff2"); }
@font-face { font-family: "Cascadia Code"; font-style: normal; font-weight: 400; src: url("../fonts/CascadiaCode.woff2") format("woff2"); }

html, body { background: var(--bg); color: var(--text); font-family: var(--font-text); font-size: 14px; margin: 0; height: 100%; }
p, label, .text-secondary { color: var(--text-secondary); }
.mono { font-family: var(--font-mono); }
.num { font-family: var(--font-text); font-variant-numeric: tabular-nums; color: var(--beige); font-weight: 600; }
h1, h2, h3 { color: var(--text); font-weight: 600; letter-spacing: -0.01em; }
```

`wwwroot/scss/common/_motion.scss`:

```scss
.t { transition: background-color var(--ease), color var(--ease), border-color var(--ease), opacity var(--ease), transform var(--ease), box-shadow var(--ease); }
@keyframes breathe { 0%, 100% { opacity: 1; } 50% { opacity: 0.55; } }
@keyframes slide-in { from { opacity: 0; transform: translateY(-6px); background-color: rgba(100, 207, 149, 0.18); } to { opacity: 1; transform: none; background-color: transparent; } }
.breathe { animation: breathe 1.8s ease-in-out infinite; }
.slide-in { animation: slide-in 600ms ease-out; }
@media (prefers-reduced-motion: reduce) {
  .t { transition: none; }
  .breathe, .slide-in { animation: none; }
}
```

`wwwroot/scss/components/_cards.scss`, `_chips.scss`, `_buttons.scss`:

```scss
.card-box { background: var(--box-bg); border: 1px solid var(--dark-gray); border-radius: var(--radius-card); padding: 16px; }
.card-box + .card-box { margin-top: 12px; }
.card-title { font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; color: var(--text-secondary); margin-bottom: 10px; }
```

```scss
.chip { display: inline-flex; align-items: center; gap: 8px; background: var(--chips); border-radius: var(--radius-chip); padding: 6px 10px; color: var(--text); }
.chip .chip-pos { color: var(--text-secondary); font-size: 12px; }
.chip .chip-warn { color: var(--yellow); font-size: 12px; }
.chip button { background: transparent; border: 0; color: var(--text-secondary); cursor: pointer; padding: 0 2px; }
.chip button:hover { color: var(--text); }
```

```scss
.btn-primary-mint { background: var(--mint); color: #0D0D0D; border: 0; border-radius: var(--radius-chip); padding: 10px 22px; font-weight: 600; }
.btn-primary-mint:hover:not(:disabled) { filter: brightness(1.08); }
.btn-primary-mint:disabled { opacity: 0.45; }
.btn-stop { background: var(--secondary-dark); color: var(--text); border: 1px solid var(--dark-gray); border-radius: var(--radius-chip); padding: 10px 22px; font-weight: 600; }
.btn-stop:hover { border-color: var(--negative); color: var(--negative); }
.btn-secondary-dark { background: var(--secondary-dark); color: var(--text); border: 1px solid var(--dark-gray); border-radius: var(--radius-chip); padding: 6px 12px; }
.btn-secondary-dark:hover:not(:disabled) { border-color: var(--text-secondary); }
.btn-icon { background: transparent; border: 0; color: var(--text-secondary); padding: 4px; border-radius: 6px; }
.btn-icon:hover { color: var(--text); background: var(--chips); }
```

`wwwroot/scss/app.scss`:

```scss
@use "common/var-colors";
@use "common/typography";
@use "common/motion";
@use "components/cards";
@use "components/chips";
@use "components/buttons";
```

DartSassBuilder compiles `wwwroot/scss/app.scss` to `wwwroot/scss/app.css` at build time (partials with a leading underscore are not compiled on their own). Append to `.gitignore`:

```
# Compiled by DartSassBuilder at build time.
src/XrplVanity.App.Core/wwwroot/scss/*.css
```

- [ ] **Step 3: Vendor Bootstrap and the fonts**

Bootstrap 5.3.3 (`bootstrap.min.css` only): download `https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css` into `wwwroot/lib/bootstrap/bootstrap.min.css` and `https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/LICENSE` next to it. Fonts (both SIL Open Font License): from the Inter 4.x release archive at `https://github.com/rsms/inter/releases` take `Inter-Regular.woff2`, `Inter-Medium.woff2`, `Inter-SemiBold.woff2` (the `web/` folder of the zip) and from `https://github.com/microsoft/cascadia-code/releases` take `CascadiaCode.woff2` (the `woff2/` folder); place them in `wwwroot/fonts/` with the two `LICENSE` files renamed `LICENSE-Inter.txt` and `LICENSE-CascadiaCode.txt`. Record the exact release versions in `docs/desktop-app.md` (Task 8) and in the commit message.

- [ ] **Step 4: Write the failing bUnit test**

`tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\XrplVanity.App.Core\XrplVanity.App.Core.csproj" />
    <PackageReference Include="xunit.v3" Version="4.0.0" />
    <PackageReference Include="bunit" Version="2.9.0" />
  </ItemGroup>
</Project>
```

If `bunit` 2.9.0 does not restore against xunit.v3 4.0.0, pin the newest `bunit` 2.x that does (check `dotnet package search bunit --exact-match`) and record the pin in the task report.

`tests/XrplVanity.App.Tests/StatusPillTests.cs`:

```csharp
using Bunit;
using XrplVanity.App.Core.Components;
using Xunit;

namespace XrplVanity.App.Tests;

public class StatusPillTests : BunitContext
{
    [Theory]
    [InlineData(SearchState.Idle, "Idle", false)]
    [InlineData(SearchState.Starting, "Starting", true)]
    [InlineData(SearchState.Searching, "Searching", true)]
    [InlineData(SearchState.Stopping, "Stopping", false)]
    public void Renders_The_State_Text_And_Breathes_Only_While_Active(SearchState state, string text, bool breathes)
    {
        IRenderedComponent<StatusPill> cut = Render<StatusPill>(p => p.Add(x => x.State, state));

        Assert.Equal(text, cut.Find(".pill").TextContent.Trim());
        Assert.Equal(breathes, cut.Find(".pill").ClassList.Contains("breathe"));
    }
}
```

- [ ] **Step 5: Run to verify it fails**

Run: `dotnet build tests/XrplVanity.App.Tests -c Release`
Expected: build error — `StatusPill` / `SearchState` do not exist.

- [ ] **Step 6: Implement `StatusPill`**

`Components/StatusPill.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;

namespace XrplVanity.App.Core.Components;

public enum SearchState { Idle, Starting, Searching, Stopping }

public partial class StatusPill
{
    [Parameter] public SearchState State { get; set; }

    private string Css => State switch
    {
        SearchState.Searching => "pill pill-active breathe",
        SearchState.Starting => "pill pill-active breathe",
        SearchState.Stopping => "pill pill-stopping",
        _ => "pill",
    };
}
```

`Components/StatusPill.razor`:

```razor
<span class="@Css t">@State</span>
```

`Components/StatusPill.razor.css` (scoped):

```css
.pill { display: inline-block; padding: 4px 12px; border-radius: 999px; background: var(--chips); color: var(--text-secondary); font-size: 12px; font-weight: 600; }
.pill-active { background: rgba(100, 207, 149, 0.16); color: var(--mint); }
.pill-stopping { background: rgba(211, 231, 171, 0.16); color: var(--yellow); }
```

- [ ] **Step 7: Register the projects and the solution filter; run the test**

```bash
dotnet sln XrplVanity.sln add src/XrplVanity.App.Core/XrplVanity.App.Core.csproj --solution-folder src
dotnet sln XrplVanity.sln add tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj --solution-folder tests
```

`XrplVanity.Portable.slnf` (the MAUI project is added to the solution in Task 7 and is deliberately absent here):

```json
{
  "solution": {
    "path": "XrplVanity.sln",
    "projects": [
      "src\\XrplVanity.Core\\XrplVanity.Core.csproj",
      "src\\XrplVanity.Gpu\\XrplVanity.Gpu.csproj",
      "src\\XrplVanity.Search\\XrplVanity.Search.csproj",
      "src\\XrplVanity.Cli\\XrplVanity.Cli.csproj",
      "src\\XrplVanity.App.Core\\XrplVanity.App.Core.csproj",
      "tests\\XrplVanity.Tests\\XrplVanity.Tests.csproj",
      "tests\\XrplVanity.App.Tests\\XrplVanity.App.Tests.csproj"
    ]
  }
}
```

In `linux/in-container.sh` replace `dotnet build XrplVanity.sln -c Release` with `dotnet build XrplVanity.Portable.slnf -c Release`, and add after the existing suite run a second suite run for the App tests with the same `set +e` capture pattern and `grep -qF "Failed: 0"` check.

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release`
Expected: 4 passed. Then `dotnet build XrplVanity.Portable.slnf -c Release` succeeds and `wwwroot/scss/app.css` exists in the App.Core folder (and is git-ignored).

- [ ] **Step 8: Commit**

```bash
git add XrplVanity.sln XrplVanity.Portable.slnf .gitignore linux/in-container.sh src/XrplVanity.App.Core tests/XrplVanity.App.Tests
git commit -m "feat(app): XrplVanity.App.Core Razor library with Wallet design tokens, fonts, Bootstrap, and bUnit tests"
```

---

### Task 2: Service interfaces, settings and the DPAPI result store

**Files:**
- Create: `src/XrplVanity.App.Core/Services/IClipboardService.cs`, `INotificationService.cs`, `ITrayService.cs`, `IWindowService.cs`, `IFileDialogService.cs`, `ISettingsStore.cs`, `IResultStore.cs`
- Create: `src/XrplVanity.App.Core/Storage/AppSettings.cs`, `JsonSettingsStore.cs`, `StoredResult.cs`, `DpapiResultStore.cs`
- Test: `tests/XrplVanity.App.Tests/JsonSettingsStoreTests.cs`, `DpapiResultStoreTests.cs`

**Interfaces:**
- Produces (verbatim in the code below): the seven interfaces; `AppSettings` record with defaults; `StoredResult`; `JsonSettingsStore(string folder)`; `DpapiResultStore(string folder)` with `bool IsReadOnly`, `string? ReadOnlyReason`.

- [ ] **Step 1: Write the failing tests**

`tests/XrplVanity.App.Tests/JsonSettingsStoreTests.cs`:

```csharp
using System.IO;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using Xunit;

namespace XrplVanity.App.Tests;

public class JsonSettingsStoreTests
{
    private static string TempFolder() => Directory.CreateTempSubdirectory("xrpl-vanity-settings-").FullName;

    [Fact]
    public void Missing_File_Yields_Defaults()
    {
        JsonSettingsStore store = new(TempFolder());
        AppSettings settings = store.Load();
        Assert.Equal(100, settings.DutyPercent);
        Assert.Equal(CloseBehavior.Ask, settings.CloseBehavior);
        Assert.Equal(30, settings.ClipboardClearSeconds);
        Assert.Equal(15, settings.SeedRevealSeconds);
        Assert.True(settings.NotificationsEnabled);
        Assert.Empty(settings.RateHints);
    }

    [Fact]
    public void Round_Trips_Every_Field_Atomically()
    {
        string folder = TempFolder();
        JsonSettingsStore store = new(folder);
        AppSettings settings = store.Load() with
        {
            DutyPercent = 80,
            CloseBehavior = CloseBehavior.MinimizeToTray,
            Patterns = new[] { new PatternDraft("abc", "Starts", true) },
            Targets = new[] { "cpu", "cuda:0" },
            CpuThreads = 6,
            Count = 3,
            WindowWidth = 1200,
            WindowHeight = 800,
            RateHints = new System.Collections.Generic.Dictionary<string, double> { ["cuda:0"] = 19_400_000 },
        };

        store.Save(settings);

        Assert.Equal(settings, new JsonSettingsStore(folder).Load());
        Assert.False(File.Exists(Path.Combine(folder, "settings.json.tmp")));
    }

    [Fact]
    public void A_Corrupted_File_Falls_Back_To_Defaults()
    {
        string folder = TempFolder();
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{ not json");
        Assert.Equal(100, new JsonSettingsStore(folder).Load().DutyPercent);
    }
}
```

`tests/XrplVanity.App.Tests/DpapiResultStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using XrplVanity.App.Core.Storage;
using Xunit;

namespace XrplVanity.App.Tests;

public class DpapiResultStoreTests
{
    private static string TempFolder() => Directory.CreateTempSubdirectory("xrpl-vanity-results-").FullName;

    private static void RequireWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Skip("DPAPI is Windows-only");
        }
    }

    [Fact]
    public void Round_Trips_And_The_File_Does_Not_Contain_The_Seed_In_Clear()
    {
        RequireWindows();
        string folder = TempFolder();
        DpapiResultStore store = new(folder);
        StoredResult result = new("rAddr1", "sEdSeedSecret", "abc", "Starts", DateTimeOffset.UtcNow, "cuda:0");

        store.Append(result);

        byte[] raw = File.ReadAllBytes(Path.Combine(folder, "results.dat"));
        Assert.DoesNotContain("sEdSeedSecret", System.Text.Encoding.UTF8.GetString(raw));
        Assert.Equal(new[] { result }, new DpapiResultStore(folder).LoadAll());
    }

    [Fact]
    public void Delete_And_Clear_Rewrite_The_File()
    {
        RequireWindows();
        DpapiResultStore store = new(TempFolder());
        store.Append(new("rA", "s1", "a", "Starts", DateTimeOffset.UtcNow, "cpu"));
        store.Append(new("rB", "s2", "b", "Starts", DateTimeOffset.UtcNow, "cpu"));

        store.Delete("rA");
        Assert.Single(store.LoadAll());
        store.Clear();
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void A_Corrupted_File_Makes_The_Store_Read_Only_With_A_Reason()
    {
        RequireWindows();
        string folder = TempFolder();
        File.WriteAllBytes(Path.Combine(folder, "results.dat"), new byte[] { 1, 2, 3 });
        DpapiResultStore store = new(folder);

        Assert.Empty(store.LoadAll());
        Assert.True(store.IsReadOnly);
        Assert.NotNull(store.ReadOnlyReason);
        Assert.Throws<InvalidOperationException>(() => store.Append(new("rA", "s", "a", "Starts", DateTimeOffset.UtcNow, "cpu")));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/XrplVanity.App.Tests -c Release`
Expected: build errors — the types do not exist.

- [ ] **Step 3: Implement the interfaces and stores**

`Services/IClipboardService.cs`:

```csharp
namespace XrplVanity.App.Core.Services;

public interface IClipboardService
{
    Task CopyAsync(string text);

    /// <summary>Copies a secret and clears the clipboard after <paramref name="clearAfter"/> if it still holds the same text.</summary>
    Task CopySecretAsync(string text, TimeSpan clearAfter);
}
```

`Services/INotificationService.cs`, `IWindowService.cs`, `IFileDialogService.cs`:

```csharp
namespace XrplVanity.App.Core.Services;

public interface INotificationService
{
    /// <summary>Shows a Windows notification; a no-op when notifications are unavailable.</summary>
    void Notify(string title, string body);
    bool IsAvailable { get; }
}
```

```csharp
namespace XrplVanity.App.Core.Services;

public enum CloseBehavior { Ask, MinimizeToTray, Exit }

public interface IWindowService
{
    void ShowWindow();
    void HideToTray();
    void Exit();

    /// <summary>Raised when the user presses the window's close button; the handler decides what happens.</summary>
    event Action? CloseRequested;
}
```

```csharp
namespace XrplVanity.App.Core.Services;

public interface IFileDialogService
{
    /// <summary>Returns the chosen path or null when the user cancelled.</summary>
    Task<string?> PickSaveTsvAsync(string suggestedName);
}
```

`Services/ITrayService.cs`:

```csharp
namespace XrplVanity.App.Core.Services;

public enum TrayAction { Show, Duty25, Duty50, Duty80, Duty100, StopSearch, Exit }

public sealed record TrayMenu(bool SearchRunning, int DutyPercent);

public interface ITrayService
{
    void Show(TrayMenu menu);
    void Update(TrayMenu menu);
    void Hide();
    event Action<TrayAction>? Activated;
}
```

`Services/ISettingsStore.cs`, `IResultStore.cs`:

```csharp
using XrplVanity.App.Core.Storage;

namespace XrplVanity.App.Core.Services;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public interface IResultStore
{
    bool IsReadOnly { get; }
    string? ReadOnlyReason { get; }
    IReadOnlyList<StoredResult> LoadAll();
    void Append(StoredResult result);
    void Delete(string address);
    void Clear();
}
```

`Storage/AppSettings.cs`:

```csharp
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Storage;

public sealed record PatternDraft(string Text, string Position, bool IgnoreCase);

public sealed record AppSettings
{
    public IReadOnlyList<PatternDraft> Patterns { get; init; } = Array.Empty<PatternDraft>();
    public IReadOnlyList<string> Targets { get; init; } = new[] { "cpu" };
    public int CpuThreads { get; init; } = Math.Max(1, Environment.ProcessorCount);
    public int DutyPercent { get; init; } = 100;
    public int? Count { get; init; }
    public int WindowWidth { get; init; } = 1100;
    public int WindowHeight { get; init; } = 720;
    public CloseBehavior CloseBehavior { get; init; } = CloseBehavior.Ask;
    public bool NotificationsEnabled { get; init; } = true;
    public int ClipboardClearSeconds { get; init; } = 30;
    public int SeedRevealSeconds { get; init; } = 15;
    public IReadOnlyDictionary<string, double> RateHints { get; init; } = new Dictionary<string, double>();

    public bool Equals(AppSettings? other) =>
        other is not null
        && Patterns.SequenceEqual(other.Patterns)
        && Targets.SequenceEqual(other.Targets)
        && CpuThreads == other.CpuThreads && DutyPercent == other.DutyPercent && Count == other.Count
        && WindowWidth == other.WindowWidth && WindowHeight == other.WindowHeight
        && CloseBehavior == other.CloseBehavior && NotificationsEnabled == other.NotificationsEnabled
        && ClipboardClearSeconds == other.ClipboardClearSeconds && SeedRevealSeconds == other.SeedRevealSeconds
        && RateHints.Count == other.RateHints.Count && RateHints.All(kv => other.RateHints.TryGetValue(kv.Key, out double v) && v == kv.Value);

    public override int GetHashCode() => HashCode.Combine(CpuThreads, DutyPercent, Count, WindowWidth, WindowHeight, CloseBehavior);
}
```

`Storage/JsonSettingsStore.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Storage;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public JsonSettingsStore(string folder)
    {
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
```

`Storage/StoredResult.cs` and `DpapiResultStore.cs`:

```csharp
namespace XrplVanity.App.Core.Storage;

public sealed record StoredResult(string Address, string Seed, string Pattern, string Position, DateTimeOffset FoundAt, string Target);
```

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Storage;

/// <summary>
/// The only file that holds seeds. A UTF-8 JSON array protected with DPAPI for the current
/// Windows user; rewritten as a whole on every change (histories are small). A file that cannot
/// be unprotected or parsed makes the store read-only for the process lifetime rather than
/// letting a later append overwrite what might still be recoverable.
/// </summary>
public sealed class DpapiResultStore : IResultStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("io.staticbit.vanity");
    private readonly string _path;
    private readonly object _gate = new();
    private List<StoredResult>? _cache;

    public DpapiResultStore(string folder)
    {
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "results.dat");
    }

    public bool IsReadOnly { get; private set; }
    public string? ReadOnlyReason { get; private set; }

    public IReadOnlyList<StoredResult> LoadAll()
    {
        lock (_gate)
        {
            return EnsureLoaded().ToArray();
        }
    }

    public void Append(StoredResult result)
    {
        lock (_gate)
        {
            List<StoredResult> all = EnsureLoaded();
            ThrowIfReadOnly();
            all.Add(result);
            Write(all);
        }
    }

    public void Delete(string address)
    {
        lock (_gate)
        {
            List<StoredResult> all = EnsureLoaded();
            ThrowIfReadOnly();
            all.RemoveAll(r => string.Equals(r.Address, address, StringComparison.Ordinal));
            Write(all);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            EnsureLoaded();
            ThrowIfReadOnly();
            _cache = new List<StoredResult>();
            Write(_cache);
        }
    }

    private List<StoredResult> EnsureLoaded()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_path))
        {
            _cache = new List<StoredResult>();
            return _cache;
        }

        try
        {
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            _cache = JsonSerializer.Deserialize<List<StoredResult>>(plain) ?? new List<StoredResult>();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            IsReadOnly = true;
            ReadOnlyReason = $"results.dat could not be read: {ex.Message}";
            _cache = new List<StoredResult>();
        }

        return _cache;
    }

    private void ThrowIfReadOnly()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException(ReadOnlyReason);
        }
    }

    private void Write(List<StoredResult> all)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(all);
        byte[] protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        string temp = _path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, _path, overwrite: true);
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*StoreTests"`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core tests/XrplVanity.App.Tests
git commit -m "feat(app): host service interfaces, JSON settings store and DPAPI-protected result store"
```

---

### Task 3: `SearchViewModel`

**Files:**
- Create: `src/XrplVanity.App.Core/ViewModels/PatternChip.cs`, `TargetChoice.cs`, `SearchViewModel.cs`, `SessionResult.cs`
- Test: `tests/XrplVanity.App.Tests/SearchViewModelTests.cs`, `tests/XrplVanity.App.Tests/Fakes.cs`

**Interfaces:**
- Consumes: Plan F (`SearchSession`, `SearchRequest`, `SearchTarget`, `SearchProgress`, `SearchSummary`, `SearchEstimator`, `DeviceCatalog`), Task 2 stores and services, `SearchState` from Task 1.
- Produces (used by every component and page): the `SearchViewModel` members shown below. `Func<IReadOnlyList<DeviceCatalogEntry>> deviceSource` is injected so tests do not touch real backends.

- [ ] **Step 1: Write the failing tests**

`tests/XrplVanity.App.Tests/Fakes.cs`:

```csharp
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;

namespace XrplVanity.App.Tests;

public sealed class FakeNotifications : INotificationService
{
    public List<(string Title, string Body)> Sent { get; } = new();
    public bool IsAvailable => true;
    public void Notify(string title, string body) { lock (Sent) Sent.Add((title, body)); }
}

public sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; private set; }
    public TimeSpan? ClearAfter { get; private set; }
    public Task CopyAsync(string text) { Text = text; ClearAfter = null; return Task.CompletedTask; }
    public Task CopySecretAsync(string text, TimeSpan clearAfter) { Text = text; ClearAfter = clearAfter; return Task.CompletedTask; }
}

public sealed class MemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } = new();
    public int Saves { get; private set; }
    public AppSettings Load() => Current;
    public void Save(AppSettings settings) { Current = settings; Saves++; }
}

public sealed class MemoryResultStore : IResultStore
{
    private readonly List<StoredResult> _items = new();
    public bool IsReadOnly { get; init; }
    public string? ReadOnlyReason { get; init; }
    public IReadOnlyList<StoredResult> LoadAll() { lock (_items) return _items.ToArray(); }
    public void Append(StoredResult result) { lock (_items) _items.Add(result); }
    public void Delete(string address) { lock (_items) _items.RemoveAll(r => r.Address == address); }
    public void Clear() { lock (_items) _items.Clear(); }
}

public sealed class FakeFileDialog : IFileDialogService
{
    public string? PathToReturn { get; set; }
    public Task<string?> PickSaveTsvAsync(string suggestedName) => Task.FromResult(PathToReturn);
}
```

`tests/XrplVanity.App.Tests/SearchViewModelTests.cs`:

```csharp
using System.Diagnostics;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.App.Tests;

public class SearchViewModelTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(120);

    private static (SearchViewModel Vm, FakeNotifications Notes, MemoryResultStore Results, MemorySettingsStore Settings) Create(params DeviceCatalogEntry[] devices)
    {
        FakeNotifications notes = new();
        MemoryResultStore results = new();
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, results, notes, () => devices);
        return (vm, notes, results, settings);
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < Deadline) Thread.Sleep(25);
        Assert.True(condition(), $"timed out waiting for {what}");
    }

    [Fact]
    public void Starts_Idle_With_The_Cpu_Target_And_No_Patterns()
    {
        (SearchViewModel vm, _, _, _) = Create();
        Assert.Equal(SearchState.Idle, vm.State);
        Assert.Empty(vm.Patterns);
        Assert.False(vm.CanStart);
        Assert.Equal("Add at least one reachable pattern.", vm.StartBlockedReason);
        TargetChoice cpu = Assert.Single(vm.Targets);
        Assert.Equal("cpu", cpu.Id);
        Assert.True(cpu.Selected);
    }

    [Fact]
    public void Adding_A_Chip_Rebuilds_The_Pattern_Set_And_The_Estimate()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("ab", MatchPosition.Starts, ignoreCase: false);

        Assert.True(vm.CanStart);
        Assert.Equal(3364, vm.ExpectedCandidates);
        Assert.Null(vm.EstimatedTime);   // no rate yet
        Assert.Equal("rate unknown until the first run", vm.EstimateNote);
    }

    [Fact]
    public void An_Unreachable_Chip_Carries_A_Warning_And_Blocks_Start()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("0", MatchPosition.Starts, ignoreCase: false);

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("cannot occur in an XRPL address", chip.Warning);
        Assert.False(vm.CanStart);
    }

    [Fact]
    public void Deselecting_Every_Target_Blocks_Start()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.SetTargetSelected("cpu", false);
        Assert.False(vm.CanStart);
        Assert.Equal("Select the CPU or at least one device.", vm.StartBlockedReason);
    }

    [Fact]
    public void Cpu_Devices_From_The_Catalog_Are_Listed_But_Unselected()
    {
        (SearchViewModel vm, _, _, _) = Create(
            new DeviceCatalogEntry(new DeviceSelector("cuda", 0), "RTX", "sm_120", false),
            new DeviceCatalogEntry(new DeviceSelector("opencl", 1), "pocl", "cpu", true));
        Assert.Equal(new[] { "cpu", "cuda:0", "opencl:1" }, vm.Targets.Select(t => t.Id));
        Assert.False(vm.Targets[2].Selected);
        Assert.True(vm.Targets[2].IsCpuDevice);
        Assert.False(vm.NoGpuFound);
    }

    [Fact]
    public void No_Devices_Sets_The_NoGpu_Banner_Flag()
    {
        (SearchViewModel vm, _, _, _) = Create();
        Assert.True(vm.NoGpuFound);
    }

    [Fact]
    public void A_Cpu_Search_Runs_To_The_Count_Persists_Notifies_And_Stores_A_Rate_Hint()
    {
        (SearchViewModel vm, FakeNotifications notes, MemoryResultStore results, MemorySettingsStore settings) = Create();
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.CpuThreads = 2;
        vm.Count = 3;
        int stateChanges = 0;
        vm.Changed += () => Interlocked.Increment(ref stateChanges);

        vm.Start();

        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        Assert.Equal(SessionOutcome.CountReached, vm.LastSummary!.Outcome);
        Assert.Equal(3, vm.Results.Count);
        Assert.All(vm.Results, r => Assert.StartsWith("ra", r.Address));
        Assert.Equal(3, results.LoadAll().Count);
        Assert.Contains(notes.Sent, n => n.Title == "Match found" && n.Body.Contains(vm.Results[0].Address) && !n.Body.Contains(vm.Results[0].Seed));
        Assert.Contains(notes.Sent, n => n.Title == "Search finished" && n.Body.Contains("3"));
        Assert.True(settings.Current.RateHints.ContainsKey("cpu"), "rate hint stored");
        Assert.True(stateChanges > 0);
        Assert.NotNull(vm.EstimatedTime);   // now computed from the hint
    }

    [Fact]
    public void Stop_Returns_To_Idle_With_Cancelled()
    {
        (SearchViewModel vm, _, _, _) = Create();
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");
        vm.Stop();
        WaitUntil(() => vm.State == SearchState.Idle, "idle");
        Assert.Equal(SessionOutcome.Cancelled, vm.LastSummary!.Outcome);
    }

    [Fact]
    public void Duty_Changes_During_A_Search_Are_Forwarded_And_Saved()
    {
        (SearchViewModel vm, _, _, MemorySettingsStore settings) = Create();
        vm.AddPattern("zzzzzzzz", MatchPosition.Starts, false);
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Searching, "searching");
        vm.DutyPercent = 50;
        Assert.Equal(50, settings.Current.DutyPercent);
        vm.Stop();
        WaitUntil(() => vm.State == SearchState.Idle, "idle");
    }

    [Fact]
    public void Read_Only_Store_Does_Not_Break_A_Search()
    {
        FakeNotifications notes = new();
        MemoryResultStore results = new() { IsReadOnly = true, ReadOnlyReason = "corrupt" };
        SearchViewModel vm = new(new MemorySettingsStore(), results, notes, () => Array.Empty<DeviceCatalogEntry>());
        vm.AddPattern("a", MatchPosition.Starts, false);
        vm.Count = 1;
        vm.Start();
        WaitUntil(() => vm.State == SearchState.Idle && vm.LastSummary is not null, "completion");
        Assert.Single(vm.Results);
        Assert.Equal("corrupt", vm.HistoryReadOnlyReason);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/XrplVanity.App.Tests -c Release`
Expected: build errors — the view-model types do not exist.

- [ ] **Step 3: Implement the view model**

`ViewModels/PatternChip.cs`, `TargetChoice.cs`, `SessionResult.cs`:

```csharp
using XrplVanity.Core;

namespace XrplVanity.App.Core.ViewModels;

public sealed class PatternChip
{
    public PatternChip(string text, MatchPosition position, bool ignoreCase)
    {
        Text = text;
        Position = position;
        IgnoreCase = ignoreCase;
    }

    public string Text { get; }
    public MatchPosition Position { get; set; }
    public bool IgnoreCase { get; set; }
    /// <summary>"cannot occur in an XRPL address", "empty pattern", "longer than 12 characters: searched case-sensitively", or null.</summary>
    public string? Warning { get; internal set; }
}
```

```csharp
namespace XrplVanity.App.Core.ViewModels;

public sealed class TargetChoice
{
    public TargetChoice(string id, string name, string details, bool isCpuDevice, bool selected)
    {
        Id = id;
        Name = name;
        Details = details;
        IsCpuDevice = isCpuDevice;
        Selected = selected;
    }

    public string Id { get; }
    public string Name { get; }
    public string Details { get; }
    public bool IsCpuDevice { get; }
    public bool Selected { get; internal set; }
    public bool IsCpu => Id == "cpu";
}
```

```csharp
namespace XrplVanity.App.Core.ViewModels;

public sealed record SessionResult(string Address, string Seed, string Pattern, string Position, DateTimeOffset FoundAt, string Target);
```

`ViewModels/SearchViewModel.cs`:

```csharp
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;

namespace XrplVanity.App.Core.ViewModels;

/// <summary>
/// The application's single source of UI truth: the pattern and target drafts, the running
/// session and its progress, the session results, and the estimate. Session events arrive on
/// worker threads; every mutation here happens under <c>_gate</c> and ends with <see cref="Changed"/>,
/// which components subscribe to and marshal through <c>InvokeAsync(StateHasChanged)</c>.
/// </summary>
public sealed class SearchViewModel : IDisposable
{
    private static readonly TimeSpan LiveRateAfter = TimeSpan.FromSeconds(2);

    private readonly ISettingsStore _settingsStore;
    private readonly IResultStore _resultStore;
    private readonly INotificationService _notifications;
    private readonly Func<IReadOnlyList<DeviceCatalogEntry>> _deviceSource;
    private readonly object _gate = new();
    private readonly List<PatternChip> _patterns = new();
    private readonly List<TargetChoice> _targets = new();
    private readonly List<SessionResult> _results = new();
    private AppSettings _settings;
    private PatternSet? _patternSet;
    private SearchSession? _session;
    private SearchProgress? _progress;
    private int _dutyPercent;

    public SearchViewModel(ISettingsStore settingsStore, IResultStore resultStore, INotificationService notifications, Func<IReadOnlyList<DeviceCatalogEntry>> deviceSource)
    {
        _settingsStore = settingsStore;
        _resultStore = resultStore;
        _notifications = notifications;
        _deviceSource = deviceSource;
        _settings = settingsStore.Load();
        _dutyPercent = _settings.DutyPercent;
        CpuThreads = Math.Clamp(_settings.CpuThreads, 1, Math.Max(1, Environment.ProcessorCount));
        Count = _settings.Count;
        foreach (PatternDraft draft in _settings.Patterns)
        {
            if (Enum.TryParse(draft.Position, out MatchPosition position))
            {
                _patterns.Add(new PatternChip(draft.Text, position, draft.IgnoreCase));
            }
        }

        RefreshDevices();
        RebuildPatternSet();
    }

    public event Action? Changed;

    public SearchState State { get; private set; } = SearchState.Idle;
    public IReadOnlyList<PatternChip> Patterns => _patterns;
    public IReadOnlyList<TargetChoice> Targets => _targets;
    public IReadOnlyList<SessionResult> Results => _results;
    public SearchProgress? Progress => _progress;
    public SearchSummary? LastSummary { get; private set; }
    public string? LastFailure { get; private set; }
    public bool NoGpuFound { get; private set; }
    public int CpuThreads { get; set; }
    public int? Count { get; set; }
    public string? HistoryReadOnlyReason => _resultStore.IsReadOnly ? _resultStore.ReadOnlyReason ?? "history is read-only" : null;
    public AppSettings Settings => _settings;

    public int DutyPercent
    {
        get => _dutyPercent;
        set
        {
            int clamped = Math.Clamp(value, 1, DutyCycle.Full);
            lock (_gate)
            {
                _dutyPercent = clamped;
                if (_session is not null)
                {
                    _session.DutyPercent = clamped;
                }
            }

            SaveSettings(s => s with { DutyPercent = clamped });
            Changed?.Invoke();
        }
    }

    public double? ExpectedCandidates => _patternSet is null ? null : SearchEstimator.ExpectedCandidatesPerMatch(_patternSet);

    /// <summary>ETA from the live rate after 2 s of searching, else from the rate hints of the selected targets, else null.</summary>
    public TimeSpan? EstimatedTime => SearchEstimator.ExpectedTimeToMatch(ExpectedCandidates, CurrentRate());

    public string EstimateNote
    {
        get
        {
            if (ExpectedCandidates is null) return "add a pattern to see an estimate";
            double rate = CurrentRate();
            if (rate <= 0) return "rate unknown until the first run";
            return _progress is not null && _progress.Elapsed >= LiveRateAfter && State == SearchState.Searching
                ? "from the live rate"
                : "from the last measured rate of the selected targets";
        }
    }

    public bool CanStart => State == SearchState.Idle && StartBlockedReason is null;

    public string? StartBlockedReason
    {
        get
        {
            if (_patternSet is null || !_patternSet.HasPatterns) return "Add at least one reachable pattern.";
            if (!_targets.Any(t => t.Selected)) return "Select the CPU or at least one device.";
            return null;
        }
    }

    public void AddPattern(string text, MatchPosition position, bool ignoreCase)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return;
        lock (_gate)
        {
            _patterns.Add(new PatternChip(trimmed, position, ignoreCase));
            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void RemovePattern(PatternChip chip)
    {
        lock (_gate)
        {
            _patterns.Remove(chip);
            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void UpdatePattern(PatternChip chip, MatchPosition position, bool ignoreCase)
    {
        lock (_gate)
        {
            chip.Position = position;
            chip.IgnoreCase = ignoreCase;
            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    /// <summary>Adds every non-blank line of a word list as a Starts chip.</summary>
    public void AddPatternsFromList(IEnumerable<string> lines, bool ignoreCase)
    {
        lock (_gate)
        {
            foreach (string line in lines)
            {
                string word = line.Trim();
                if (word.Length > 0) _patterns.Add(new PatternChip(word, MatchPosition.Starts, ignoreCase));
            }

            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void SetTargetSelected(string id, bool selected)
    {
        lock (_gate)
        {
            TargetChoice? target = _targets.FirstOrDefault(t => t.Id == id);
            if (target is null) return;
            target.Selected = selected;
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void RefreshDevices()
    {
        IReadOnlyList<DeviceCatalogEntry> entries;
        try
        {
            entries = _deviceSource();
        }
        catch (Exception)
        {
            entries = Array.Empty<DeviceCatalogEntry>();
        }

        lock (_gate)
        {
            // A target is selected when the saved draft (first call) or the current draft (refresh)
            // selected it. Nothing is auto-selected beyond the CPU on a fresh install, and OpenCL
            // CPU implementations ([cpu]) are never selected unless the user picked them.
            HashSet<string> selected = new(_targets.Count == 0 ? _settings.Targets : _targets.Where(t => t.Selected).Select(t => t.Id), StringComparer.Ordinal);
            _targets.Clear();
            _targets.Add(new TargetChoice("cpu", "CPU", $"{Environment.ProcessorCount} logical processors", false, selected.Contains("cpu")));
            foreach (DeviceCatalogEntry e in entries)
            {
                _targets.Add(new TargetChoice(e.Id, e.Name, $"{e.Selector.Backend} · {e.Details}", e.IsCpuDevice, selected.Contains(e.Id)));
            }

            NoGpuFound = !entries.Any(e => !e.IsCpuDevice);
        }

        Changed?.Invoke();
    }

    public void Start()
    {
        SearchRequest request;
        lock (_gate)
        {
            if (!CanStart) return;
            List<SearchTarget> targets = new();
            foreach (TargetChoice t in _targets.Where(t => t.Selected))
            {
                targets.Add(t.IsCpu
                    ? new SearchTarget.Cpu(CpuThreads)
                    : new SearchTarget.Device(ParseSelector(t.Id)));
            }

            request = new SearchRequest(_patternSet!, targets, Count, _dutyPercent);
            _results.Clear();
            LastSummary = null;
            LastFailure = null;
            State = SearchState.Starting;
            _session = new SearchSession(request, OnMatch, OnLog);
            _session.ProgressChanged += OnProgress;
            _session.Completion.ContinueWith(t => OnCompleted(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion);
            _session.Start();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_session is null || State is SearchState.Idle or SearchState.Stopping) return;
            State = SearchState.Stopping;
            _session.Stop();
        }

        Changed?.Invoke();
    }

    public void SaveSettings(Func<AppSettings, AppSettings> update)
    {
        lock (_gate)
        {
            _settings = update(_settings);
            _settingsStore.Save(_settings);
        }
    }

    public void Dispose()
    {
        SearchSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        session?.Dispose();
    }

    private static DeviceSelector ParseSelector(string id)
    {
        int colon = id.IndexOf(':');
        return new DeviceSelector(id[..colon], int.Parse(id[(colon + 1)..]));
    }

    private double CurrentRate()
    {
        SearchProgress? progress = _progress;
        if (progress is not null && State == SearchState.Searching && progress.Elapsed >= LiveRateAfter && progress.TotalRatePerSecond > 0)
        {
            return progress.TotalRatePerSecond;
        }

        double sum = 0;
        foreach (TargetChoice t in _targets.Where(t => t.Selected))
        {
            if (_settings.RateHints.TryGetValue(t.Id, out double hint)) sum += hint;
        }

        return sum;
    }

    private void RebuildPatternSet()
    {
        PatternSet.Builder builder = new();
        foreach (PatternChip chip in _patterns)
        {
            builder.Add(chip.Position, chip.Text, chip.IgnoreCase);
        }

        PatternSet set = builder.Build();
        foreach (PatternChip chip in _patterns)
        {
            NormalizedPattern normalized = PatternNormalizer.Normalize(chip.Text);
            chip.Warning = !normalized.Reachable ? "cannot occur in an XRPL address"
                : normalized.Value.Length == 0 ? "empty pattern"
                : chip.IgnoreCase && normalized.Value.Length > PatternSet.MaxIgnoreCaseLength ? $"longer than {PatternSet.MaxIgnoreCaseLength} characters: searched case-sensitively"
                : null;
        }

        _patternSet = set;
    }

    private void PersistDraft()
    {
        SaveSettings(s => s with
        {
            Patterns = _patterns.Select(p => new PatternDraft(p.Text, p.Position.ToString(), p.IgnoreCase)).ToArray(),
            Targets = _targets.Where(t => t.Selected).Select(t => t.Id).ToArray(),
            CpuThreads = CpuThreads,
            Count = Count,
        });
    }

    private void OnProgress(SearchProgress progress)
    {
        lock (_gate)
        {
            _progress = progress;
            if (State == SearchState.Starting && progress.Targets.Any(t => t.State == TargetState.Running))
            {
                State = SearchState.Searching;
            }
        }

        Changed?.Invoke();
    }

    private void OnMatch(Match match)
    {
        string target = _progress?.Targets.FirstOrDefault(t => t.State == TargetState.Running)?.Id ?? "?";
        SessionResult result = new(match.Address, match.Seed, match.Pattern, match.Position.ToString(), DateTimeOffset.Now, target);
        lock (_gate)
        {
            _results.Insert(0, result);
        }

        if (!_resultStore.IsReadOnly)
        {
            try
            {
                _resultStore.Append(new StoredResult(result.Address, result.Seed, result.Pattern, result.Position, result.FoundAt, result.Target));
            }
            catch (Exception)
            {
                // The store reports read-only state itself; the session result list still has the match.
            }
        }

        if (_settings.NotificationsEnabled)
        {
            _notifications.Notify("Match found", $"{match.Address} · {match.Pattern} ({match.Position})");
        }

        Changed?.Invoke();
    }

    private void OnLog(string line)
    {
        if (line.StartsWith("warning:", StringComparison.Ordinal) || line.StartsWith("error:", StringComparison.Ordinal))
        {
            LastFailure = line;
            Changed?.Invoke();
        }
    }

    private void OnCompleted(SearchSummary summary)
    {
        Dictionary<string, double> hints = new(_settings.RateHints);
        foreach (TargetProgress t in summary.Final.Targets)
        {
            if (t.State != TargetState.Failed && t.RatePerSecond > 0) hints[t.Id] = t.RatePerSecond;
        }

        SaveSettings(s => s with { RateHints = hints });
        SearchSession? finished;
        lock (_gate)
        {
            LastSummary = summary;
            _progress = summary.Final;
            State = SearchState.Idle;
            finished = _session;
            _session = null;
            if (summary.Outcome == SessionOutcome.AllTargetsFailed)
            {
                LastFailure = summary.Final.Targets.FirstOrDefault(t => t.Failure is not null)?.Failure ?? "no target could start";
            }
        }

        if (_settings.NotificationsEnabled)
        {
            _notifications.Notify("Search finished", $"{summary.Final.Reported} found · {summary.Outcome}");
        }

        Changed?.Invoke();
        finished?.Dispose();
    }
}
```

Implementation note: `OnMatch` attributes the result to the first running target; when both the CPU and a GPU are running the attribution is a best effort (Plan F's `Match` does not carry its source).

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*SearchViewModelTests"`
Expected: 10 passed (the CPU searches take a few seconds).

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core/ViewModels tests/XrplVanity.App.Tests
git commit -m "feat(app): SearchViewModel drives the session, drafts, estimate, results and rate hints"
```

---

### Task 4: `PatternEditor` and `EstimateCard`

**Files:**
- Create: `src/XrplVanity.App.Core/Components/PatternEditor.razor` (+ `.razor.cs`, `.razor.css`), `EstimateCard.razor` (+ `.razor.cs`, `.razor.css`), `Components/Formatting.cs`
- Test: `tests/XrplVanity.App.Tests/PatternEditorTests.cs`, `EstimateCardTests.cs`

**Interfaces:**
- Consumes: `SearchViewModel` (Task 3), `IFileDialogService` is not needed here (list import uses an `InputFile`).
- Produces: `Formatting.Candidates(double)` → `"3,364"` / `"11.3 M"` / `"1.2 G"`; `Formatting.Duration(TimeSpan?)` → `"—"`, `"12 s"`, `"4 min"`, `"3 h 20 min"`, `"2.5 days"`, `"> 1 year"`; both components take `[Parameter] SearchViewModel Vm`.

- [ ] **Step 1: Write the failing tests**

`tests/XrplVanity.App.Tests/PatternEditorTests.cs`:

```csharp
using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class PatternEditorTests : BunitContext
{
    private static SearchViewModel Vm() => new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());

    [Fact]
    public void Enter_Adds_A_Chip_With_The_Selected_Position_And_Case()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("select.pos").Change("Contains");
        cut.Find("input.case").Change(false);
        cut.Find("input.pattern").Input("fast");
        cut.Find("input.pattern").KeyDown("Enter");

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("fast", chip.Text);
        Assert.Equal(XrplVanity.Core.MatchPosition.Contains, chip.Position);
        Assert.False(chip.IgnoreCase);
        Assert.Contains("fast", cut.Find(".chip").TextContent);
        Assert.Equal(string.Empty, cut.Find("input.pattern").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void An_Unreachable_Chip_Shows_Its_Warning()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("0", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        Assert.Equal("cannot occur in an XRPL address", cut.Find(".chip-warn").TextContent.Trim());
    }

    [Fact]
    public void The_Remove_Button_Drops_The_Chip()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        cut.Find(".chip button.remove").Click();
        Assert.Empty(vm.Patterns);
        Assert.Empty(cut.FindAll(".chip"));
    }
}
```

`tests/XrplVanity.App.Tests/EstimateCardTests.cs`:

```csharp
using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class EstimateCardTests : BunitContext
{
    [Fact]
    public void Without_Patterns_It_Invites_To_Add_One()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        IRenderedComponent<EstimateCard> cut = Render<EstimateCard>(p => p.Add(x => x.Vm, vm));
        Assert.Contains("add a pattern to see an estimate", cut.Markup);
    }

    [Fact]
    public void With_A_Pattern_And_No_Rate_It_Shows_Candidates_And_Rate_Unknown()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        vm.AddPattern("abcd", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<EstimateCard> cut = Render<EstimateCard>(p => p.Add(x => x.Vm, vm));
        Assert.Equal("11.3 M", cut.Find(".candidates").TextContent.Trim());
        Assert.Equal("—", cut.Find(".eta").TextContent.Trim());
        Assert.Contains("rate unknown until the first run", cut.Markup);
    }

    [Fact]
    public void With_A_Rate_Hint_It_Shows_The_Eta()
    {
        MemorySettingsStore settings = new();
        settings.Save(settings.Load() with { RateHints = new Dictionary<string, double> { ["cpu"] = 1000 } });
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);   // 3364 candidates / 1000 per s ≈ 3 s
        IRenderedComponent<EstimateCard> cut = Render<EstimateCard>(p => p.Add(x => x.Vm, vm));
        Assert.Equal("3 s", cut.Find(".eta").TextContent.Trim());
    }

    [Theory]
    [InlineData(58, "58")]
    [InlineData(3364, "3,364")]
    [InlineData(11_316_496, "11.3 M")]
    [InlineData(2.2e9, "2.2 G")]
    public void Formats_Candidates(double value, string expected) => Assert.Equal(expected, Formatting.Candidates(value));

    [Fact]
    public void Formats_Durations()
    {
        Assert.Equal("—", Formatting.Duration(null));
        Assert.Equal("12 s", Formatting.Duration(TimeSpan.FromSeconds(12.4)));
        Assert.Equal("4 min", Formatting.Duration(TimeSpan.FromMinutes(4.2)));
        Assert.Equal("3 h 20 min", Formatting.Duration(TimeSpan.FromMinutes(200)));
        Assert.Equal("2.5 days", Formatting.Duration(TimeSpan.FromHours(60)));
        Assert.Equal("> 1 year", Formatting.Duration(TimeSpan.FromDays(400)));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/XrplVanity.App.Tests -c Release`
Expected: build errors — the components and `Formatting` do not exist.

- [ ] **Step 3: Implement**

`Components/Formatting.cs`:

```csharp
using System.Globalization;

namespace XrplVanity.App.Core.Components;

public static class Formatting
{
    public static string Candidates(double value)
    {
        if (value >= 1e9) return (value / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + " G";
        if (value >= 1e6) return (value / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + " M";
        return Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);
    }

    public static string Rate(double perSecond) => Candidates(perSecond) + "/s";

    public static string Duration(TimeSpan? value)
    {
        if (value is not TimeSpan t) return "—";
        if (t.TotalDays > 365) return "> 1 year";
        if (t.TotalDays >= 1) return t.TotalDays.ToString("0.#", CultureInfo.InvariantCulture) + " days";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{(int)Math.Round(t.TotalMinutes)} min";
        return $"{(int)Math.Round(t.TotalSeconds)} s";
    }
}
```

`Components/PatternEditor.razor`:

```razor
<div class="card-box">
  <div class="card-title">Patterns</div>
  <div class="chips">
    @foreach (PatternChip chip in Vm.Patterns)
    {
      <span class="chip t" @key="chip">
        <span class="chip-pos">@chip.Position.ToString().ToLowerInvariant()</span>
        <span class="mono">@chip.Text</span>
        @if (!chip.IgnoreCase) { <span class="chip-pos" title="case-sensitive">Aa</span> }
        @if (chip.Warning is not null) { <span class="chip-warn">@chip.Warning</span> }
        <button class="remove" title="Remove" @onclick="() => Vm.RemovePattern(chip)">×</button>
      </span>
    }
  </div>
  <div class="entry">
    <select class="pos form-select" @bind="_position">
      <option value="Starts">starts with</option>
      <option value="Ends">ends with</option>
      <option value="Contains">contains</option>
    </select>
    <input class="pattern form-control mono" placeholder="type a pattern and press Enter" @bind="_text" @bind:event="oninput" @onkeydown="OnKeyDown" />
    <label class="case-label"><input class="case" type="checkbox" @bind="_ignoreCase" /> ignore case</label>
    <label class="btn-secondary-dark t">
      load list…
      <InputFile OnChange="OnListChosen" accept=".txt" style="display:none" />
    </label>
  </div>
</div>
```

`Components/PatternEditor.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;

namespace XrplVanity.App.Core.Components;

public partial class PatternEditor : IDisposable
{
    private string _position = "Starts";
    private string _text = string.Empty;
    private bool _ignoreCase = true;

    [Parameter, EditorRequired] public SearchViewModel Vm { get; set; } = null!;

    protected override void OnInitialized() => Vm.Changed += OnChanged;

    private void OnChanged() => InvokeAsync(StateHasChanged);

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key != "Enter") return;
        Vm.AddPattern(_text, Enum.Parse<MatchPosition>(_position), _ignoreCase);
        _text = string.Empty;
    }

    private async Task OnListChosen(InputFileChangeEventArgs e)
    {
        using StreamReader reader = new(e.File.OpenReadStream(maxAllowedSize: 1024 * 1024));
        string content = await reader.ReadToEndAsync();
        Vm.AddPatternsFromList(content.Split('\n'), _ignoreCase);
    }

    public void Dispose() => Vm.Changed -= OnChanged;
}
```

`Components/PatternEditor.razor.css`:

```css
.chips { display: flex; flex-wrap: wrap; gap: 8px; min-height: 36px; margin-bottom: 12px; }
.entry { display: grid; grid-template-columns: 140px 1fr auto auto; gap: 8px; align-items: center; }
.entry .form-select, .entry .form-control { background: var(--chips); color: var(--text); border: 1px solid var(--dark-gray); border-radius: var(--radius-chip); }
.entry .form-control:focus, .entry .form-select:focus { border-color: var(--mint); box-shadow: none; outline: none; }
.case-label { color: var(--text-secondary); white-space: nowrap; display: flex; gap: 6px; align-items: center; }
```

`Components/EstimateCard.razor`:

```razor
<div class="card-box estimate">
  <div class="card-title">Estimate</div>
  <div class="row-2">
    <div><div class="label">candidates per match</div><div class="num candidates">@(Vm.ExpectedCandidates is double c ? Formatting.Candidates(c) : "—")</div></div>
    <div><div class="label">expected time to a match</div><div class="num eta">@Formatting.Duration(Vm.EstimatedTime)</div></div>
  </div>
  <div class="note">@Vm.EstimateNote · assumes a 34-character address; case-insensitive letters count every variant · <a href="https://github.com/StaticBit-io/xrpl-vanity-generator/blob/main/docs/benchmarks.md" target="_blank">reference rates</a></div>
</div>
```

`Components/EstimateCard.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Components;

public partial class EstimateCard : IDisposable
{
    [Parameter, EditorRequired] public SearchViewModel Vm { get; set; } = null!;
    protected override void OnInitialized() => Vm.Changed += OnChanged;
    private void OnChanged() => InvokeAsync(StateHasChanged);
    public void Dispose() => Vm.Changed -= OnChanged;
}
```

`Components/EstimateCard.razor.css`:

```css
.row-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
.label { color: var(--text-secondary); font-size: 12px; }
.num { font-size: 22px; line-height: 1.3; min-height: 30px; }
.note { color: var(--text-secondary); font-size: 12px; margin-top: 10px; min-height: 18px; }
.note a { color: var(--text-secondary); }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release -- -class "*PatternEditorTests" -class "*EstimateCardTests"`
Expected: all passed.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core/Components tests/XrplVanity.App.Tests
git commit -m "feat(app): PatternEditor and EstimateCard components"
```

---

### Task 5: `DevicePicker`, `DutySlider`, `LiveStats`, `ResultsList`

**Files:**
- Create: `src/XrplVanity.App.Core/Components/DevicePicker.razor` (+ `.cs`, `.css`), `DutySlider.razor` (+ `.cs`, `.css`), `LiveStats.razor` (+ `.cs`, `.css`), `ResultsList.razor` (+ `.cs`, `.css`), `Components/TsvExport.cs`
- Test: `tests/XrplVanity.App.Tests/DevicePickerTests.cs`, `DutySliderTests.cs`, `LiveStatsTests.cs`, `ResultsListTests.cs`

**Interfaces:**
- Consumes: `SearchViewModel`, `IClipboardService`, `IFileDialogService`, `Formatting`.
- Produces: `TsvExport.Write(string path, IEnumerable<(string Address, string Seed)> rows)` (temp file + move; `address\tseed\n` per row); `ResultsList` parameters `[Parameter] IReadOnlyList<SessionResult> Items`, `[Parameter] bool ShowTarget`, `[Parameter] EventCallback<SessionResult> OnDelete` (optional), `[Parameter] int RevealSeconds = 15`, `[Parameter] int ClipboardClearSeconds = 30`; `ResultsList` injects `IClipboardService`, `IFileDialogService` and `TimeProvider`.

- [ ] **Step 1: Write the failing tests**

`tests/XrplVanity.App.Tests/ResultsListTests.cs` (the other three component tests follow the same shape: render with a view model built from the fakes, assert the markup):

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class ResultsListTests : BunitContext
{
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeFileDialog _dialog = new();
    private readonly FakeTimeProvider _time = new();

    private static SessionResult R(int i) => new($"rAddr{i}", $"sEdSeed{i}", "ab", "Starts", DateTimeOffset.UnixEpoch, "cpu");

    private IRenderedComponent<ResultsList> RenderList(params SessionResult[] items)
    {
        Services.AddSingleton<Core.Services.IClipboardService>(_clipboard);
        Services.AddSingleton<Core.Services.IFileDialogService>(_dialog);
        Services.AddSingleton<TimeProvider>(_time);
        return Render<ResultsList>(p => p.Add(x => x.Items, items).Add(x => x.RevealSeconds, 15).Add(x => x.ClipboardClearSeconds, 30));
    }

    [Fact]
    public void Seeds_Are_Masked_Until_Revealed_And_Hide_Again_After_The_Timeout()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        Assert.Equal("••••••••", cut.Find(".seed").TextContent.Trim());
        Assert.DoesNotContain("sEdSeed1", cut.Markup);

        cut.Find("button.reveal").Click();
        Assert.Equal("sEdSeed1", cut.Find(".seed").TextContent.Trim());

        _time.Advance(TimeSpan.FromSeconds(16));
        cut.WaitForAssertion(() => Assert.Equal("••••••••", cut.Find(".seed").TextContent.Trim()));
    }

    [Fact]
    public void Copying_A_Seed_Uses_The_Secret_Path_With_The_Configured_Delay()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.copy-seed").Click();
        Assert.Equal("sEdSeed1", _clipboard.Text);
        Assert.Equal(TimeSpan.FromSeconds(30), _clipboard.ClearAfter);

        cut.Find("button.copy-address").Click();
        Assert.Equal("rAddr1", _clipboard.Text);
        Assert.Null(_clipboard.ClearAfter);
    }

    [Fact]
    public void Export_Writes_The_Cli_Tsv_Format()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tsv");
        _dialog.PathToReturn = path;
        try
        {
            IRenderedComponent<ResultsList> cut = RenderList(R(1), R(2));
            cut.Find("button.export").Click();
            cut.WaitForAssertion(() => Assert.True(File.Exists(path)));
            Assert.Equal("rAddr1\tsEdSeed1\nrAddr2\tsEdSeed2\n", File.ReadAllText(path).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cancelled_Export_Writes_Nothing_And_Shows_No_Error()
    {
        _dialog.PathToReturn = null;
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.export").Click();
        Assert.Empty(cut.FindAll(".export-error"));
    }

    [Fact]
    public void Empty_List_Shows_The_Placeholder()
    {
        IRenderedComponent<ResultsList> cut = RenderList();
        Assert.Contains("No matches yet", cut.Markup);
    }
}
```

`tests/XrplVanity.App.Tests/DevicePickerTests.cs`:

```csharp
using AngleSharp.Dom;
using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.App.Tests;

public class DevicePickerTests : BunitContext
{
    [Fact]
    public void Lists_Cpu_First_Then_Devices_With_Their_Tags_And_Toggles_Selection()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => new[]
        {
            new DeviceCatalogEntry(new DeviceSelector("cuda", 0), "RTX 5070 Ti", "sm_120", false),
            new DeviceCatalogEntry(new DeviceSelector("opencl", 1), "pocl", "cpu", true),
        });
        IRenderedComponent<DevicePicker> cut = Render<DevicePicker>(p => p.Add(x => x.Vm, vm));

        IReadOnlyList<IElement> rows = cut.FindAll(".target");
        Assert.Equal(3, rows.Count);
        Assert.Contains("CPU", rows[0].TextContent);
        Assert.Contains("RTX 5070 Ti", rows[1].TextContent);
        Assert.Contains("[cpu]", rows[2].TextContent);
        Assert.Single(cut.FindAll("input.threads"));   // only the CPU row has the thread slider

        cut.FindAll("input.select")[1].Change(true);
        Assert.True(vm.Targets[1].Selected);
    }

    [Fact]
    public void Without_Devices_The_No_Gpu_Banner_Appears()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<DeviceCatalogEntry>());
        IRenderedComponent<DevicePicker> cut = Render<DevicePicker>(p => p.Add(x => x.Vm, vm));
        Assert.Contains("No CUDA driver or OpenCL platform found", cut.Find(".banner-yellow").TextContent);
    }
}
```

`tests/XrplVanity.App.Tests/DutySliderTests.cs`:

```csharp
using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class DutySliderTests : BunitContext
{
    [Fact]
    public void Moving_The_Slider_Sets_The_Duty_And_Shows_The_Percentage()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        IRenderedComponent<DutySlider> cut = Render<DutySlider>(p => p.Add(x => x.Vm, vm));
        Assert.Equal("100%", cut.Find(".duty-value").TextContent.Trim());
        cut.Find("input[type=range]").Change(80);
        Assert.Equal(80, vm.DutyPercent);
        Assert.Equal("80%", cut.Find(".duty-value").TextContent.Trim());
    }
}
```

`tests/XrplVanity.App.Tests/LiveStatsTests.cs`:

```csharp
using AngleSharp.Dom;
using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.App.Tests;

public class LiveStatsTests : BunitContext
{
    [Fact]
    public void Renders_Totals_And_One_Row_Per_Target_With_Failures()
    {
        SearchProgress progress = new(TimeSpan.FromSeconds(12), 24_000_000, 2_000_000, 3, new[]
        {
            new TargetProgress("cuda:0", "RTX 5070 Ti", TargetState.Running, 24_000_000, 2_000_000, null),
            new TargetProgress("opencl:0", "RTX 5070 Ti", TargetState.Failed, 0, 0, "clBuildProgram failed"),
        });
        IRenderedComponent<LiveStats> cut = Render<LiveStats>(p => p.Add(x => x.Progress, progress));

        Assert.Equal("2 M/s", cut.Find(".total-rate").TextContent.Trim());
        Assert.Equal("24 M", cut.Find(".total-candidates").TextContent.Trim());
        Assert.Equal("12 s", cut.Find(".elapsed").TextContent.Trim());
        Assert.Equal("3", cut.Find(".found").TextContent.Trim());
        IReadOnlyList<IElement> rows = cut.FindAll(".target-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("clBuildProgram failed", rows[1].TextContent);
        Assert.Contains("failed", rows[1].ClassName);
    }

    [Fact]
    public void Without_Progress_The_Tiles_Show_Dashes_And_Keep_Their_Height()
    {
        IRenderedComponent<LiveStats> cut = Render<LiveStats>(p => p.Add(x => x.Progress, (SearchProgress?)null));
        Assert.Equal("—", cut.Find(".total-rate").TextContent.Trim());
        Assert.Equal(4, cut.FindAll(".tile").Count);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/XrplVanity.App.Tests -c Release`
Expected: build errors — the components do not exist. (Add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.0.0" />` to the test csproj for `FakeTimeProvider`; if 10.0.0 is unavailable, use the latest 9.x.)

- [ ] **Step 3: Implement**

`Components/TsvExport.cs`:

```csharp
using System.Text;

namespace XrplVanity.App.Core.Components;

public static class TsvExport
{
    /// <summary>Writes address&lt;TAB&gt;seed lines (the CLI's --out format) through a temp file so a failure leaves nothing partial.</summary>
    public static void Write(string path, IEnumerable<(string Address, string Seed)> rows)
    {
        string temp = path + ".tmp";
        StringBuilder sb = new();
        foreach ((string address, string seed) in rows)
        {
            sb.Append(address).Append('\t').Append(seed).Append('\n');
        }

        File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}
```

`Components/ResultsList.razor`:

```razor
@inject IClipboardService Clipboard
@inject IFileDialogService Dialog
@inject TimeProvider Clock

<div class="card-box results">
  <div class="head">
    <div class="card-title">Results <span class="count">@Items.Count</span></div>
    <button class="btn-secondary-dark export t" disabled="@(Items.Count == 0)" @onclick="Export">Export TSV…</button>
  </div>
  @if (_exportError is not null) { <div class="export-error">@_exportError</div> }
  @if (Items.Count == 0)
  {
    <div class="placeholder">No matches yet — start a search and hits appear here as they are verified.</div>
  }
  else
  {
    <Virtualize Items="Items" Context="item" ItemSize="44" OverscanCount="8">
      <div class="row @(_animated.Add(item.Address) ? "slide-in" : "")" @key="item.Address">
        <span class="mono address">@item.Address</span>
        <button class="btn-icon copy-address" title="Copy address" @onclick="() => Clipboard.CopyAsync(item.Address)">⧉</button>
        <span class="pattern">@item.Pattern <span class="text-secondary">(@item.Position.ToLowerInvariant())</span></span>
        @if (ShowTarget) { <span class="target text-secondary">@item.Target</span> }
        <span class="time text-secondary">@item.FoundAt.ToLocalTime().ToString("HH:mm:ss")</span>
        <span class="mono seed">@(_revealed.Contains(item.Address) ? item.Seed : "••••••••")</span>
        <button class="btn-icon reveal" title="@(_revealed.Contains(item.Address) ? "Hide seed" : "Reveal seed")" @onclick="() => ToggleReveal(item.Address)">👁</button>
        <button class="btn-icon copy-seed" title="Copy seed (clipboard clears after @ClipboardClearSeconds s)" @onclick="() => Clipboard.CopySecretAsync(item.Seed, TimeSpan.FromSeconds(ClipboardClearSeconds))">⧉</button>
        @if (OnDelete.HasDelegate) { <button class="btn-icon delete" title="Delete" @onclick="() => OnDelete.InvokeAsync(item)">🗑</button> }
      </div>
    </Virtualize>
  }
</div>
```

`Components/ResultsList.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Components;

public partial class ResultsList : IDisposable
{
    private readonly HashSet<string> _revealed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _animated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITimer> _hideTimers = new(StringComparer.Ordinal);
    private string? _exportError;

    [Parameter, EditorRequired] public IReadOnlyList<SessionResult> Items { get; set; } = Array.Empty<SessionResult>();
    [Parameter] public bool ShowTarget { get; set; }
    [Parameter] public EventCallback<SessionResult> OnDelete { get; set; }
    [Parameter] public int RevealSeconds { get; set; } = 15;
    [Parameter] public int ClipboardClearSeconds { get; set; } = 30;

    private void ToggleReveal(string address)
    {
        if (_revealed.Remove(address))
        {
            StopTimer(address);
            return;
        }

        _revealed.Add(address);
        StopTimer(address);
        _hideTimers[address] = Clock.CreateTimer(_ => InvokeAsync(() =>
        {
            _revealed.Remove(address);
            StopTimer(address);
            StateHasChanged();
        }), null, TimeSpan.FromSeconds(RevealSeconds), Timeout.InfiniteTimeSpan);
    }

    private void StopTimer(string address)
    {
        if (_hideTimers.Remove(address, out ITimer? timer))
        {
            timer.Dispose();
        }
    }

    private async Task Export()
    {
        _exportError = null;
        string? path = await Dialog.PickSaveTsvAsync("xrpl-vanity.tsv");
        if (path is null) return;
        try
        {
            TsvExport.Write(path, Items.Select(i => (i.Address, i.Seed)).Reverse());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _exportError = $"Export failed: {ex.Message}";
        }
    }

    public void Dispose()
    {
        foreach (ITimer timer in _hideTimers.Values) timer.Dispose();
        _hideTimers.Clear();
    }
}
```

`Items` is newest-first (the view model inserts at 0), so the export reverses to oldest-first, matching the order the CLI would have written. The `_animated` set marks a row as already animated after its first render so re-renders do not replay the slide-in; hidden seeds are also cleared in `OnParametersSet` when the `Items` instance changes (a new session).

`Components/ResultsList.razor.css`:

```css
.head { display: flex; justify-content: space-between; align-items: center; }
.count { color: var(--beige); margin-left: 6px; }
.placeholder { color: var(--text-secondary); padding: 18px 0; }
.row { display: grid; grid-template-columns: minmax(280px, 1.4fr) 28px 1fr auto auto minmax(120px, 1fr) 28px 28px 28px; gap: 8px; align-items: center; height: 44px; border-top: 1px solid var(--dark-gray); }
.address { color: var(--text); }
.seed { color: var(--text-secondary); letter-spacing: 0.05em; }
.export-error { color: var(--negative); font-size: 12px; margin-bottom: 8px; }
```

`Components/DevicePicker.razor`:

```razor
<div class="card-box">
  <div class="head"><div class="card-title">Devices</div><button class="btn-icon t" title="Refresh devices" @onclick="Vm.RefreshDevices">⟳</button></div>
  @if (Vm.NoGpuFound) { <div class="banner-yellow">No CUDA driver or OpenCL platform found — searching on the CPU.</div> }
  @foreach (TargetChoice t in Vm.Targets)
  {
    <label class="target t @(t.Selected ? "selected" : "")" @key="t.Id">
      <input class="select" type="checkbox" checked="@t.Selected" disabled="@Busy" @onchange="e => Vm.SetTargetSelected(t.Id, (bool)e.Value!)" />
      <span class="name">@t.Name @if (t.IsCpuDevice) { <span class="tag">[cpu]</span> }</span>
      <span class="details text-secondary">@t.Details</span>
      @if (t.IsCpu)
      {
        <span class="threads-wrap"><input class="threads" type="range" min="1" max="@Environment.ProcessorCount" value="@Vm.CpuThreads" disabled="@Busy" @onchange="e => Vm.CpuThreads = int.Parse((string)e.Value!)" /> <span class="num small">@Vm.CpuThreads</span> threads</span>
      }
    </label>
  }
</div>
```

`DevicePicker.razor.cs` subscribes to `Vm.Changed` like `EstimateCard` and exposes `private bool Busy => Vm.State != SearchState.Idle;`. `DevicePicker.razor.css`: rows as a grid `24px 1fr 1fr auto`, `.selected { border-color: var(--mint); }`, `.tag { color: var(--yellow); }`, `.banner-yellow { background: rgba(211,231,171,0.12); color: var(--yellow); border-radius: var(--radius-chip); padding: 8px 12px; margin-bottom: 10px; }`.

`Components/DutySlider.razor`:

```razor
<div class="card-box">
  <div class="head"><div class="card-title">GPU duty</div><span class="num duty-value">@Vm.DutyPercent%</span></div>
  <input type="range" min="25" max="100" step="5" value="@Vm.DutyPercent" @onchange="e => Vm.DutyPercent = int.Parse((string)e.Value!)" />
  <div class="note text-secondary">Pauses between launches so the card stays free for other applications about @(100 - Vm.DutyPercent)% of the time. Changes apply immediately.</div>
</div>
```

`DutySlider.razor.cs` subscribes to `Vm.Changed`; the CSS styles the range input track `--dark-gray` and thumb `--mint`.

`Components/LiveStats.razor`:

```razor
<div class="card-box">
  <div class="card-title">Live</div>
  <div class="tiles">
    <div class="tile"><div class="label">rate</div><div class="num total-rate">@(Progress is null ? "—" : Formatting.Rate(Progress.TotalRatePerSecond))</div></div>
    <div class="tile"><div class="label">candidates</div><div class="num total-candidates">@(Progress is null ? "—" : Formatting.Candidates(Progress.TotalCandidates))</div></div>
    <div class="tile"><div class="label">elapsed</div><div class="num elapsed">@(Progress is null ? "—" : Formatting.Duration(Progress.Elapsed))</div></div>
    <div class="tile"><div class="label">found</div><div class="num found">@(Progress is null ? "—" : Progress.Reported.ToString())</div></div>
  </div>
  @if (Progress is not null)
  {
    @foreach (TargetProgress t in Progress.Targets)
    {
      <div class="target-row @t.State.ToString().ToLowerInvariant()" @key="t.Id">
        <span class="id mono">@t.Id</span><span class="name">@t.Name</span>
        <span class="state">@t.State</span>
        <span class="num small">@Formatting.Rate(t.RatePerSecond)</span>
        @if (t.Failure is not null) { <span class="failure">@t.Failure</span> }
      </div>
    }
  }
</div>
```

`LiveStats.razor.cs`: `[Parameter] public SearchProgress? Progress { get; set; }` only (the page passes `Vm.Progress`). CSS: `.tiles` four equal columns, `.tile { min-height: 64px; }`, `.target-row.failed .state, .failure { color: var(--negative); }`, `.target-row.running .state { color: var(--mint); }`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release`
Expected: everything passed.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core/Components tests/XrplVanity.App.Tests
git commit -m "feat(app): DevicePicker, DutySlider, LiveStats and ResultsList components with seed masking and TSV export"
```

---

### Task 6: Pages, layout, navigation and the close prompt

**Files:**
- Create: `src/XrplVanity.App.Core/Layout/Routes.razor`, `MainLayout.razor` (+ `.css`), `NavRail.razor` (+ `.css`), `Components/ClosePrompt.razor` (+ `.cs`, `.css`), `Components/Banner.razor`, `Pages/SearchPage.razor` (+ `.cs`, `.css`), `Pages/HistoryPage.razor` (+ `.cs`), `Pages/SettingsPage.razor` (+ `.cs`), `wwwroot/scss/components/_layout.scss`
- Test: `tests/XrplVanity.App.Tests/SearchPageTests.cs`, `HistoryPageTests.cs`, `SettingsPageTests.cs`, `ClosePromptTests.cs`

**Interfaces:**
- Consumes: every component from Tasks 1, 4, 5; `SearchViewModel`; `IResultStore`; `IWindowService`; `ISettingsStore`.
- Produces: routes `/` (Search), `/history`, `/settings`; `ClosePrompt` parameters `bool Visible`, `EventCallback<(CloseBehavior Choice, bool Remember)> OnChoice`; `Routes` is the root component the host registers.

- [ ] **Step 1: Write the failing tests**

`tests/XrplVanity.App.Tests/SearchPageTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Pages;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class SearchPageTests : BunitContext
{
    private SearchViewModel Register()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        return vm;
    }

    [Fact]
    public void Start_Is_Disabled_With_A_Reason_Until_A_Pattern_Exists()
    {
        SearchViewModel vm = Register();
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        Assert.True(cut.Find("button.start").HasAttribute("disabled"));
        Assert.Contains("Add at least one reachable pattern.", cut.Find(".start-hint").TextContent);

        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.start").HasAttribute("disabled")));
    }

    [Fact]
    public void The_Header_Shows_The_Status_Pill_And_Swaps_Start_For_Stop_While_Searching()
    {
        SearchViewModel vm = Register();
        vm.AddPattern("zzzzzzzz", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<SearchPage> cut = Render<SearchPage>();
        cut.Find("button.start").Click();
        cut.WaitForAssertion(() => Assert.Equal("Searching", cut.Find(".pill").TextContent.Trim()), TimeSpan.FromSeconds(30));
        cut.Find("button.stop").Click();
        cut.WaitForAssertion(() => Assert.Equal("Idle", cut.Find(".pill").TextContent.Trim()), TimeSpan.FromSeconds(30));
    }
}
```

`tests/XrplVanity.App.Tests/HistoryPageTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Pages;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class HistoryPageTests : BunitContext
{
    [Fact]
    public void Filters_By_Address_Or_Pattern_And_Deletes_Rows()
    {
        MemoryResultStore store = new();
        store.Append(new StoredResult("rFast1", "s1", "fast", "Starts", DateTimeOffset.UtcNow, "cpu"));
        store.Append(new StoredResult("rSlow2", "s2", "slow", "Starts", DateTimeOffset.UtcNow, "cpu"));
        Services.AddSingleton<IResultStore>(store);
        Services.AddSingleton(new SearchViewModel(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>()));
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        IRenderedComponent<HistoryPage> cut = Render<HistoryPage>();

        Assert.Equal(2, cut.FindAll(".row").Count);
        cut.Find("input.filter").Input("fast");
        Assert.Single(cut.FindAll(".row"));

        cut.Find("input.filter").Input("");
        cut.FindAll("button.delete")[0].Click();
        Assert.Single(store.LoadAll());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".row")));
    }

    [Fact]
    public void A_Read_Only_Store_Shows_The_Reason_And_Hides_Destructive_Buttons()
    {
        MemoryResultStore store = new() { IsReadOnly = true, ReadOnlyReason = "results.dat could not be read" };
        Services.AddSingleton<IResultStore>(store);
        Services.AddSingleton(new SearchViewModel(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>()));
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        IRenderedComponent<HistoryPage> cut = Render<HistoryPage>();
        Assert.Contains("results.dat could not be read", cut.Find(".banner-negative").TextContent);
        Assert.Empty(cut.FindAll("button.clear"));
    }
}
```

`tests/XrplVanity.App.Tests/ClosePromptTests.cs`:

```csharp
using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Services;
using Xunit;

namespace XrplVanity.App.Tests;

public class ClosePromptTests : BunitContext
{
    [Fact]
    public void Reports_The_Choice_And_The_Remember_Flag()
    {
        (CloseBehavior Choice, bool Remember)? received = null;
        IRenderedComponent<ClosePrompt> cut = Render<ClosePrompt>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnChoice, v => received = v));

        cut.Find("input.remember").Change(true);
        cut.Find("button.tray").Click();

        Assert.Equal((CloseBehavior.MinimizeToTray, true), received);
    }

    [Fact]
    public void Hidden_Prompt_Renders_Nothing()
    {
        IRenderedComponent<ClosePrompt> cut = Render<ClosePrompt>(p => p.Add(x => x.Visible, false));
        Assert.Empty(cut.FindAll(".modal-box"));
    }
}
```

`tests/XrplVanity.App.Tests/SettingsPageTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Pages;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class SettingsPageTests : BunitContext
{
    [Fact]
    public void Changing_A_Setting_Saves_It_Immediately()
    {
        MemorySettingsStore settings = new();
        SearchViewModel vm = new(settings, new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        Services.AddSingleton(vm);
        Services.AddSingleton<INotificationService>(new FakeNotifications());
        Services.AddSingleton<IWindowService>(new NullWindowService());
        IRenderedComponent<SettingsPage> cut = Render<SettingsPage>();

        cut.Find("select.close-behavior").Change("Exit");
        Assert.Equal(CloseBehavior.Exit, settings.Current.CloseBehavior);

        cut.Find("input.clipboard-seconds").Change(45);
        Assert.Equal(45, settings.Current.ClipboardClearSeconds);

        cut.Find("button.forget-hints").Click();
        Assert.Empty(settings.Current.RateHints);
    }

    private sealed class NullWindowService : IWindowService
    {
        public event Action? CloseRequested;
        public void ShowWindow() { }
        public void HideToTray() { }
        public void Exit() { }
        public string DataFolder => "C:\\data";
    }
}
```

Add `string DataFolder { get; }` to `IWindowService` in Task 2's file as part of this task (the Settings page shows it with an "Open folder" button, which calls `OpenDataFolder()` — add `void OpenDataFolder();` too).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/XrplVanity.App.Tests -c Release`
Expected: build errors — pages and `ClosePrompt` do not exist.

- [ ] **Step 3: Implement**

`Layout/Routes.razor`:

```razor
<Router AppAssembly="typeof(Routes).Assembly">
  <Found Context="routeData">
    <RouteView RouteData="routeData" DefaultLayout="typeof(MainLayout)" />
  </Found>
  <NotFound><LayoutView Layout="typeof(MainLayout)"><p>Not found.</p></LayoutView></NotFound>
</Router>
```

`Layout/MainLayout.razor`:

```razor
@inherits LayoutComponentBase
@inject SearchViewModel Vm
@inject IWindowService Window

<div class="shell">
  <NavRail />
  <main class="content">@Body</main>
</div>
<ClosePrompt Visible="_askClose" OnChoice="OnCloseChoice" />

@code {
    private bool _askClose;

    protected override void OnInitialized() => Window.CloseRequested += OnCloseRequested;

    private void OnCloseRequested()
    {
        switch (Vm.Settings.CloseBehavior)
        {
            case CloseBehavior.MinimizeToTray: Window.HideToTray(); break;
            case CloseBehavior.Exit: Window.Exit(); break;
            default: _askClose = true; InvokeAsync(StateHasChanged); break;
        }
    }

    private void OnCloseChoice((CloseBehavior Choice, bool Remember) value)
    {
        _askClose = false;
        if (value.Remember) Vm.SaveSettings(s => s with { CloseBehavior = value.Choice });
        if (value.Choice == CloseBehavior.Exit) Window.Exit(); else Window.HideToTray();
    }
}
```

`Layout/NavRail.razor`: a 64 px wide column with three `NavLink`s (Search `/`, History `/history`, Settings `/settings`) drawn as icon + 11 px label, active state in `--mint`, the app monogram at the top; CSS in `NavRail.razor.css`. `wwwroot/scss/components/_layout.scss`: `.shell { display: grid; grid-template-columns: 64px 1fr; height: 100vh; } .content { padding: 20px 24px; overflow: auto; }` and the search page grid below.

`Pages/SearchPage.razor`:

```razor
@page "/"
@inject SearchViewModel Vm

<header class="page-head">
  <div><h1>XRPL Vanity</h1><StatusPill State="Vm.State" /></div>
  <div class="actions">
    @if (Vm.State == SearchState.Idle)
    {
      <button class="btn-primary-mint start t" disabled="@(!Vm.CanStart)" @onclick="Vm.Start">Start</button>
      @if (Vm.StartBlockedReason is not null) { <div class="start-hint text-secondary">@Vm.StartBlockedReason</div> }
    }
    else
    {
      <button class="btn-stop stop t" disabled="@(Vm.State == SearchState.Stopping)" @onclick="Vm.Stop">Stop</button>
    }
  </div>
</header>
@if (Vm.LastFailure is not null) { <Banner Kind="negative" Text="@Vm.LastFailure" /> }
<div class="search-grid">
  <div class="col-left"><PatternEditor Vm="Vm" /><EstimateCard Vm="Vm" /></div>
  <div class="col-right"><DevicePicker Vm="Vm" /><DutySlider Vm="Vm" /></div>
  <div class="col-bottom"><LiveStats Progress="Vm.Progress" /><ResultsList Items="Vm.Results" ShowTarget="true" RevealSeconds="Vm.Settings.SeedRevealSeconds" ClipboardClearSeconds="Vm.Settings.ClipboardClearSeconds" /></div>
</div>
```

`SearchPage.razor.cs` subscribes to `Vm.Changed` → `InvokeAsync(StateHasChanged)`, unsubscribes in `Dispose`. `.search-grid { display: grid; grid-template-columns: 1.2fr 1fr; gap: 12px; } .col-bottom { grid-column: 1 / -1; } @media (max-width: 1000px) { .search-grid { grid-template-columns: 1fr; } }`.

`Components/Banner.razor`: `[Parameter] string Kind` (`yellow` | `negative`), `[Parameter] string Text`; renders `<div class="banner-@Kind">@Text</div>`; the two classes are defined in `_layout.scss` (yellow as in `DevicePicker`, negative with `rgba(214,119,106,0.14)` / `--negative`).

`Components/ClosePrompt.razor`:

```razor
@if (Visible)
{
  <div class="modal-backdrop"></div>
  <div class="modal-box card-box">
    <h3>Close XRPL Vanity?</h3>
    <p>A search keeps running while the window is in the tray.</p>
    <label><input class="remember" type="checkbox" @bind="_remember" /> Remember my choice</label>
    <div class="buttons">
      <button class="btn-primary-mint tray t" @onclick="() => OnChoice.InvokeAsync((CloseBehavior.MinimizeToTray, _remember))">Minimise to tray</button>
      <button class="btn-secondary-dark exit t" @onclick="() => OnChoice.InvokeAsync((CloseBehavior.Exit, _remember))">Exit</button>
    </div>
  </div>
}
```

with `ClosePrompt.razor.cs` declaring `[Parameter] public bool Visible { get; set; }`, `[Parameter] public EventCallback<(CloseBehavior Choice, bool Remember)> OnChoice { get; set; }` and `private bool _remember;`.

`Pages/HistoryPage.razor`: injects `IResultStore` and `SearchViewModel`; loads `Store.LoadAll()` into a list on init (newest first), a `<input class="filter">` filtering by `Address` or `Pattern` (`OrdinalIgnoreCase` contains), `<ResultsList Items="_filtered" ShowTarget="true" OnDelete="Delete" RevealSeconds=… ClipboardClearSeconds=… />`, a `button.clear` ("Clear history", asks for confirmation through a second click within 5 s: the button text changes to "Click again to confirm") hidden when `Store.IsReadOnly`, and `<Banner Kind="negative" Text="@Store.ReadOnlyReason" />` when read-only. `Delete` calls `Store.Delete(item.Address)` and reloads. The page maps `StoredResult` → `SessionResult` for the list.

`Pages/SettingsPage.razor`: injects `SearchViewModel`, `INotificationService`, `IWindowService`; one `card-box` per group; controls with the classes the test uses (`select.close-behavior`, `input.notifications`, `input.clipboard-seconds`, `input.reveal-seconds`, `input.default-duty`, `button.forget-hints`, `button.open-folder`); every change calls `Vm.SaveSettings(s => s with { … })` immediately; the notifications toggle shows "unavailable on this system" and is disabled when `Notifications.IsAvailable` is false; the data folder row shows `Window.DataFolder`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet run --project tests/XrplVanity.App.Tests -c Release`
Expected: everything passed.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.App.Core tests/XrplVanity.App.Tests
git commit -m "feat(app): Search, History and Settings pages with the nav rail and the close prompt"
```

---

### Task 7: The MAUI host

**Files:**
- Create: `src/XrplVanity.App/XrplVanity.App.csproj`, `MauiProgram.cs`, `App.xaml`, `App.xaml.cs`, `MainPage.xaml`, `MainPage.xaml.cs`, `wwwroot/index.html`, `Platforms/Windows/App.xaml`, `Platforms/Windows/App.xaml.cs`, `Platforms/Windows/Package.appxmanifest`, `Platforms/Windows/app.manifest`, `Services/WindowsClipboardService.cs`, `Services/WindowsNotificationService.cs`, `Services/WindowsTrayService.cs`, `Services/WindowsWindowService.cs`, `Services/WindowsFileDialogService.cs`, `Resources/AppIcon/appicon.svg`, `Resources/Splash/splash.svg`, `Resources/Raw/tray.ico`
- Modify: `XrplVanity.sln` (add the project; the `.slnf` stays without it)

**Interfaces:**
- Consumes: everything in App.Core; `DeviceCatalog.Enumerate`; `FileSystem.AppDataDirectory`.
- Produces: the runnable application.

- [ ] **Step 1: Scaffold with the template, then trim to Windows**

Run from `src/`:

```bash
dotnet new maui-blazor -n XrplVanity.App -o XrplVanity.App --framework net10.0
```

Delete the template's `Components/`, `wwwroot/css`, `wwwroot/lib` and every `Platforms/` folder except `Platforms/Windows`. Replace the csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0-windows10.0.22621.0</TargetFrameworks>
    <OutputType>Exe</OutputType>
    <RootNamespace>XrplVanity.App</RootNamespace>
    <UseMaui>true</UseMaui>
    <SingleProject>true</SingleProject>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCssItems>false</EnableDefaultCssItems>
    <ApplicationTitle>XRPL Vanity</ApplicationTitle>
    <ApplicationId>io.staticbit.vanity</ApplicationId>
    <ApplicationDisplayVersion>1.0.0</ApplicationDisplayVersion>
    <ApplicationVersion>1</ApplicationVersion>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <WindowsPackageType>MSIX</WindowsPackageType>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
  </PropertyGroup>
  <ItemGroup>
    <MauiIcon Include="Resources\AppIcon\appicon.svg" Color="#141414" Resize="true" />
    <MauiSplashScreen Include="Resources\Splash\splash.svg" Color="#141414" Resize="true" />
    <MauiAsset Include="Resources\Raw\**" LogicalName="%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Maui" Version="$(MauiVersion)" />
    <PackageReference Include="H.NotifyIcon.Maui" Version="2.4.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\XrplVanity.App.Core\XrplVanity.App.Core.csproj" />
  </ItemGroup>
</Project>
```

`Resources/AppIcon/appicon.svg` (the monogram; the splash uses the same drawing at 256 px):

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <rect width="256" height="256" rx="56" fill="#141414"/>
  <path d="M60 78 L128 178 L196 78" fill="none" stroke="#64CF95" stroke-width="22" stroke-linecap="round" stroke-linejoin="round"/>
  <circle cx="128" cy="186" r="10" fill="#EBAF8E"/>
</svg>
```

Generate `Resources/Raw/tray.ico` (16/24/32 px) from the same SVG with ImageMagick (`magick appicon.svg -define icon:auto-resize=32,24,16 tray.ico`) or any equivalent tool and commit the `.ico`.

- [ ] **Step 2: Host page, MAUI page and DI**

`wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
  <title>XRPL Vanity</title>
  <base href="/" />
  <link rel="stylesheet" href="_content/XrplVanity.App.Core/lib/bootstrap/bootstrap.min.css" />
  <link rel="stylesheet" href="_content/XrplVanity.App.Core/scss/app.css" />
  <link rel="stylesheet" href="XrplVanity.App.styles.css" />
</head>
<body style="background:#0D0D0D">
  <div id="app"></div>
  <script src="_framework/blazor.webview.js" autostart="false"></script>
</body>
</html>
```

`MainPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:core="clr-namespace:XrplVanity.App.Core.Layout;assembly=XrplVanity.App.Core"
             x:Class="XrplVanity.App.MainPage"
             BackgroundColor="#0D0D0D">
  <BlazorWebView x:Name="blazorWebView" HostPage="wwwroot/index.html" BackgroundColor="#0D0D0D">
    <BlazorWebView.RootComponents>
      <RootComponent Selector="#app" ComponentType="{x:Type core:Routes}" />
    </BlazorWebView.RootComponents>
  </BlazorWebView>
</ContentPage>
```

`MauiProgram.cs`:

```csharp
using Microsoft.Extensions.Logging;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.App.Services;
using XrplVanity.Search;

namespace XrplVanity.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        string dataFolder = FileSystem.AppDataDirectory;
        builder.Services.AddSingleton<ISettingsStore>(new JsonSettingsStore(dataFolder));
        builder.Services.AddSingleton<IResultStore>(new DpapiResultStore(dataFolder));
        builder.Services.AddSingleton<INotificationService, WindowsNotificationService>();
        builder.Services.AddSingleton<IClipboardService, WindowsClipboardService>();
        builder.Services.AddSingleton<IFileDialogService, WindowsFileDialogService>();
        builder.Services.AddSingleton<WindowsWindowService>();
        builder.Services.AddSingleton<IWindowService>(sp => sp.GetRequiredService<WindowsWindowService>());
        builder.Services.AddSingleton<ITrayService, WindowsTrayService>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton(sp => new SearchViewModel(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IResultStore>(),
            sp.GetRequiredService<INotificationService>(),
            DeviceCatalog.Enumerate));
        builder.Services.AddSingleton<TrayCoordinator>();
        return builder.Build();
    }
}
```

`Services/TrayCoordinator.cs` (host-side glue, created once from `App.xaml.cs` after the window exists): subscribes to `SearchViewModel.Changed` and calls `ITrayService.Update(new TrayMenu(vm.State != SearchState.Idle, vm.DutyPercent))`; handles `ITrayService.Activated`: `Show` → `IWindowService.ShowWindow()`, `Duty25/50/80/100` → `vm.DutyPercent = n`, `StopSearch` → `vm.Stop()`, `Exit` → `IWindowService.Exit()`; handles `INotificationService` activation (see below) by showing the window.

- [ ] **Step 3: Windows services**

`Services/WindowsClipboardService.cs`:

```csharp
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Services;

public sealed class WindowsClipboardService : IClipboardService
{
    private CancellationTokenSource? _pendingClear;

    public Task CopyAsync(string text)
    {
        _pendingClear?.Cancel();
        return Clipboard.Default.SetTextAsync(text);
    }

    public async Task CopySecretAsync(string text, TimeSpan clearAfter)
    {
        _pendingClear?.Cancel();
        CancellationTokenSource cts = new();
        _pendingClear = cts;
        await Clipboard.Default.SetTextAsync(text);
        _ = ClearLaterAsync(text, clearAfter, cts.Token);
    }

    private static async Task ClearLaterAsync(string text, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            string? current = await Clipboard.Default.GetTextAsync();
            if (string.Equals(current, text, StringComparison.Ordinal))
            {
                await Clipboard.Default.SetTextAsync(string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer copy superseded this one.
        }
    }
}
```

`Services/WindowsNotificationService.cs`:

```csharp
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Services;

public sealed class WindowsNotificationService : INotificationService
{
    public WindowsNotificationService()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) => Activated?.Invoke();
            AppNotificationManager.Default.Register();
            IsAvailable = true;
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    public bool IsAvailable { get; }
    public event Action? Activated;

    public void Notify(string title, string body)
    {
        if (!IsAvailable) return;
        try
        {
            AppNotification notification = new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception)
        {
            // Notifications are best effort; the UI already shows the result.
        }
    }
}
```

`Services/WindowsWindowService.cs`: keeps the MAUI `Window`; `ShowWindow` restores and activates the WinUI window (`Microsoft.UI.Xaml.Window.Activate()` plus `AppWindow.Show()`), `HideToTray` calls `AppWindow.Hide()`, `Exit` stops the view model's session (`Dispose`) and calls `Application.Current.Quit()`; `DataFolder => FileSystem.AppDataDirectory`; `OpenDataFolder` uses `Launcher.Default.OpenAsync(new Uri(DataFolder))`. In `Platforms/Windows/App.xaml.cs` (`OnLaunched`): size the window to the saved `WindowWidth × WindowHeight` with a 900×600 minimum (the Wallet's `App.xaml.cs` shows the `AppWindow` resize pattern), subscribe to `AppWindow.Closing` with `args.Cancel = true; windowService.RaiseCloseRequested();`, save the size on `SizeChanged` through `Vm.SaveSettings`, and create the `TrayCoordinator`.

`Services/WindowsTrayService.cs`: wraps an `H.NotifyIcon.TaskbarIcon` created in code with `IconSource` from `Resources/Raw/tray.ico`, `ToolTipText = "XRPL Vanity"`, a `MenuFlyout` with the items **Show**, a **Duty** submenu (25 % / 50 % / 80 % / 100 %, the current one checked), **Stop search** (enabled only while `SearchRunning`), **Exit**; `Update` rebuilds the flyout; double-click and **Show** raise `Activated(TrayAction.Show)`. If `H.NotifyIcon.Maui` 2.4.1 does not build against MAUI 10, replace the package with `H.NotifyIcon.WinUI` (same API on the WinUI `TaskbarIcon`) and record the swap in the report; the interface does not change.

`Services/WindowsFileDialogService.cs`: `Windows.Storage.Pickers.FileSavePicker` initialised with the window handle (`WinRT.Interop.InitializeWithWindow`), `SuggestedFileName` from the argument, file type `.tsv`; returns the picked path or null.

- [ ] **Step 4: Build and run**

```bash
dotnet build src/XrplVanity.App/XrplVanity.App.csproj -c Debug -f net10.0-windows10.0.22621.0
dotnet build src/XrplVanity.App/XrplVanity.App.csproj -c Debug -f net10.0-windows10.0.22621.0 -t:Run
```

Expected: the window opens at 1100×720 on the Search page in the dark theme with Inter rendered; a CPU search on pattern `a` with count 1 finds a match, shows a toast and the row; closing the window shows the prompt. Fix whatever the first run reveals before committing (fonts not loading = wrong `_content` path; a blank page = `Routes` assembly not referenced in `MainPage.xaml`).

- [ ] **Step 5: Commit**

```bash
dotnet sln XrplVanity.sln add src/XrplVanity.App/XrplVanity.App.csproj --solution-folder src
git add XrplVanity.sln src/XrplVanity.App
git commit -m "feat(app): MAUI Blazor Hybrid host with tray icon, notifications, close-to-tray and Windows services"
```

---

### Task 8: MSIX packaging, certificate script, documentation and the manual checklist

**Files:**
- Create: `src/XrplVanity.App/Properties/PublishProfiles/MSIX-win-x64.pubxml`, `app/make-cert.ps1`, `docs/desktop-app.md`
- Modify: `src/XrplVanity.App/Platforms/Windows/Package.appxmanifest`, `README.md`, `docs/2026-09-05-xrpl-vanity-desktop-design.md` (Measured), `.gitignore` (`app/*.pfx`, `app/*.cer`)

- [ ] **Step 1: Manifest and publish profile**

In `Package.appxmanifest`: `<Identity Name="io.staticbit.vanity" Publisher="CN=StaticBit Vanity Dev" Version="1.0.0.0" />`, `DisplayName` / `PublisherDisplayName` "XRPL Vanity" / "StaticBit", keep `runFullTrust`. The MAUI build fills the version from `ApplicationDisplayVersion`.

`MSIX-win-x64.pubxml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="4.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>x64</Platform>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishDir>bin\publish\</PublishDir>
    <PublishProtocol>FileSystem</PublishProtocol>
    <SelfContained>true</SelfContained>
    <GenerateAppxPackageOnBuild>true</GenerateAppxPackageOnBuild>
    <AppxPackageSigningEnabled>true</AppxPackageSigningEnabled>
    <PackageCertificateThumbprint></PackageCertificateThumbprint>
    <PackageCertificateKeyFile>..\..\app\XrplVanityDev.pfx</PackageCertificateKeyFile>
    <AppxPackageDir>bin\msix\</AppxPackageDir>
    <AppxBundle>Never</AppxBundle>
    <UapAppxPackageBuildMode>SideloadOnly</UapAppxPackageBuildMode>
  </PropertyGroup>
</Project>
```

`app/make-cert.ps1`:

```powershell
# Creates a self-signed code-signing certificate for sideloading the XRPL Vanity MSIX.
# Run once per machine that builds packages. Trust the exported .cer on every machine that installs them.
param([string] $Subject = "CN=StaticBit Vanity Dev", [string] $Password = "xrpl-vanity-dev")

$cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
    -FriendlyName "XRPL Vanity sideload" -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "$PSScriptRoot\XrplVanityDev.pfx" -Password $secure | Out-Null
Export-Certificate -Cert $cert -FilePath "$PSScriptRoot\XrplVanityDev.cer" | Out-Null
Write-Host "Created $($cert.Thumbprint). Trust XrplVanityDev.cer under Local Machine \ Trusted People before installing the MSIX."
```

Add `app/*.pfx` and `app/*.cer` to `.gitignore`. Publish:

```bash
dotnet publish src/XrplVanity.App/XrplVanity.App.csproj -c Release -f net10.0-windows10.0.22621.0 -p:PublishProfile=MSIX-win-x64 -p:PackageCertificatePassword=xrpl-vanity-dev
```

Expected: an `.msix` under `src/XrplVanity.App/bin/msix/`; after trusting the `.cer` (`certutil -addstore -f TrustedPeople app\XrplVanityDev.cer` in an elevated prompt) a double-click installs "XRPL Vanity".

- [ ] **Step 2: Documentation**

`docs/desktop-app.md` (English, sentence-case headings): overview (what the application does, where it stores data and that `results.dat` is the only file with seeds), requirements (Windows 10 1809+, WebView2 runtime, NVIDIA driver for CUDA or an OpenCL platform, otherwise CPU), "Build and run from source" (`dotnet build … -t:Run`), "Package as MSIX" (certificate script, publish command, trusting the certificate, installing), "Fonts and third-party assets" (Inter and Cascadia Code versions and OFL, Bootstrap 5.3.3 MIT, H.NotifyIcon MIT), "Manual smoke checklist" (the spec's list as numbered checkboxes with the expected result per step), "Troubleshooting" (blank window → WebView2 missing; no devices → driver; notifications missing → Windows Focus Assist; history read-only → the banner reason). README: a "Desktop application" section after "Linux" pointing to `docs/desktop-app.md` with one screenshot placeholder replaced by the actual `docs/images/desktop-search.png` captured from the running application (PNG, 1100×720, no seeds visible: mask them before capturing).

- [ ] **Step 3: Run the manual smoke checklist and record the results**

Walk through every checklist item on the installed MSIX and record pass/fail with a one-line observation each in the task report; a failure is fixed before the plan completes. Append to the spec:

```markdown
### Measured (Plan G)

- App test suite: <N> tests, <T> s (commit <sha>). Portable solution filter builds in the Linux harness: gpu mode <T> s, cpu-only mode <T> s.
- Cold start to interactive window (MSIX, Release): <T> s; memory after start: <M> MB (Task Manager).
- CPU search from the GUI, 12 threads, `--starts abc` equivalent: <R>/s in LiveStats versus <R>/s from the CLI's `CPU:` line.
- GPU search from the GUI (`cuda:0`, `abcd` case-sensitive, count 30): <R>/s in LiveStats versus 19,400,000/s from the CLI.
- Manual checklist: <n>/<n> passed.
```

Fill in every `<…>`; a placeholder left in the document is a plan failure.

- [ ] **Step 4: Commit**

```bash
git add .gitignore app/make-cert.ps1 src/XrplVanity.App/Properties src/XrplVanity.App/Platforms/Windows/Package.appxmanifest docs README.md
git commit -m "feat(app): MSIX publish profile, sideload certificate script, desktop documentation and measurements"
```

---

## Self-review

- **Spec coverage.** Projects table → Tasks 1 and 7; host services → Tasks 2 and 7; view model → Task 3; pages and components → Tasks 4–6; visual language (tokens, fonts, Bootstrap, DartSassBuilder, motion, window size, single-column collapse) → Tasks 1 and 6; storage → Task 2; error-handling table → Tasks 3 (no-GPU flag, failed targets, read-only store), 5 (banner, export error) and 6 (banners, hints); testing list → the tests in every task; build and packaging → Tasks 1 (solution filter, harness) and 8; tray and notifications → Task 7; close behaviour → Tasks 6 and 7.
- **Placeholders.** Only Task 8's measurement template, which the task requires to be filled.
- **Type consistency.** `SearchState` lives in `Components/StatusPill.razor.cs` (Task 1) and is used by the view model (Task 3) and pages (Task 6); `TrayMenu(bool SearchRunning, int DutyPercent)` (Task 2) is what `TrayCoordinator` builds (Task 7); `SessionResult` (Task 3) is the `ResultsList` item type (Task 5) and the History page maps `StoredResult` to it (Task 6); `IWindowService` gains `DataFolder` and `OpenDataFolder` in Task 6 and `WindowsWindowService` implements them in Task 7.
- **Mockup dependency.** Tasks 4–6 implement the layouts described here; where the approved mockup (`docs/mockups/xrpl-vanity-desktop.html`) differs in spacing, copy or arrangement, the implementer follows the mockup and notes the difference in the report.
