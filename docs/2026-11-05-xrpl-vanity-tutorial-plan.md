# Interactive Tutorial Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A first-run tutorial that walks a newcomer through one real search on the live interface and leaves nothing behind.

**Architecture:** `SearchViewModel` gains a sandbox: while it is on, the live fields change but `_settings` and the result store do not, so a crash needs no repair. A `TutorialService` singleton drives the steps from view-model state. A `TutorialRegion` wrapper dims and disables everything unlit from Blazor alone; a small JS module only measures where the bubble goes.

**Tech Stack:** .NET 10, Blazor Hybrid (MAUI), xUnit v3, bUnit, SCSS.

**Spec:** `docs/2026-11-05-xrpl-vanity-tutorial-design.md`

## Global Constraints

- No third-party library and no network access.
- A tutorial seed never reaches `IResultStore`, a notification or a file.
- `_settings` keeps the user's values throughout; only `RateHints` and `TutorialOffered` are written during a run.
- Thresholds: too long is above 1 day, short enough is 10 seconds or less.
- Interface copy is the table in Task 2: English, no long dash.
- C# style follows `~/.claude/CLAUDE.md`: explicit types, English comments that explain why, XML docs on public members.
- Run the desktop application only in the MauiLab VM, never on the host.
- Nothing is committed, pushed or merged until the owner asks. Commit messages are conventional, with no AI attribution.
- Work on branch `claude/app-tutorial-9da02c`, cut from `origin/dev`.

## Review Focus

1. The window is resized during a step: the bubble stays fully on screen. Test in Task 4.
2. Every device fails during the tutorial search: no results, a failure banner, and Skip still works. Test in Task 2.
3. The window closes while a tutorial search runs: the search stops and the user's state returns. Tests in Tasks 1 and 5.
4. A user with words, results and a custom stop count re-runs the tutorial: all of it returns, and `results.dat` is unchanged. Test in Task 1.
5. Several devices report more than one match, or a late progress tick arrives after the tutorial ended: nothing reaches the store. Test in Task 1.

---

### Task 1: The sandbox in SearchViewModel

**Files:**
- Modify: `src/XrplVanity.App.Core/ViewModels/SearchViewModel.cs`
- Test: `tests/XrplVanity.App.Tests/SearchViewModelSandboxTests.cs`

**Interfaces:**
- Produces:
  - `public bool IsSandbox { get; }`
  - `public bool BeginSandbox()`: false, and no change, unless idle, not calibrating and not already in a sandbox.
  - `public void EndSandbox()`: restores at once when idle. Otherwise it stops the run, and the restore happens on the completion path of the search or calibration.
  - `public bool AcceptsTrayAction(TrayAction action)`: false for `Duty25`, `Duty50`, `Duty80`, `Duty100` and `StopSearch` during a sandbox.

- [ ] **Step 1: Write the failing tests** using `MemorySettingsStore`, `MemoryResultStore` and `FakeNotifications` from `Fakes.cs`, and the `WaitUntil` helper from `SearchViewModelTests.cs`:

```csharp
[Fact] public void Begin_Empties_The_Patterns_And_Stops_At_One_Match()      // Patterns empty, Count == 1
[Fact] public void Begin_Is_Refused_While_A_Search_Runs()                    // false, IsSandbox false
[Fact] public void Sandbox_Edits_Never_Reach_The_Settings_Store()            // AddPattern, SetTargetSelected, DutyPercent, Count: store.Load() equals the state before
[Fact] public void Sandbox_Matches_Reach_Neither_Store_Nor_Notifications()   // real short search; LoadAll() empty, no Notify, Results has the hit
[Fact] public void Rate_Hints_Written_In_A_Sandbox_Keep_The_Users_Patterns() // after a sandbox search: store has RateHints and the original Patterns
[Fact] public void End_Restores_Every_Live_Field()                           // patterns, selection, threads, Count, duty, entry text, session Results
[Fact] public void End_During_A_Search_Stops_It_And_Restores_On_Completion()
[Fact] public void End_Leaves_No_Tutorial_Result_Behind()                    // Results holds only the pre-tutorial rows
[Fact] public void A_Match_Arriving_After_End_Is_Not_Persisted()             // call OnMatch path, flush, LoadAll() empty
[Fact] public void Tray_Load_And_Stop_Are_Refused_Only_In_A_Sandbox()        // Show and Exit stay accepted
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj --filter SearchViewModelSandboxTests`
Expected: FAIL, `IsSandbox` is not defined.

- [ ] **Step 3: Implement the four members.** Keep a private snapshot record of the live fields. Guards, all under `_gate`:
  - `PersistDraft` returns at once in a sandbox.
  - The `DutyPercent` setter skips `SaveSettings`.
  - `OnMatch` adds to `_results` only, not `_pendingPersist` or `_unnotifiedMatches`. Test the flag at the time of the match, and keep a match from a sandbox session out of the store even if the flag has since cleared.
  - `OnCompleted` skips the "Search finished" notification.
  - `ApplyRateHints` stays as it is: `_settings` still holds the user's values, so its write is safe.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj`
Expected: all pass.

### Task 2: The steps and TutorialService

**Files:**
- Modify: `src/XrplVanity.App.Core/Storage/AppSettings.cs` (add `TutorialOffered`, default false, to the record, `Equals` and `GetHashCode`)
- Create: `src/XrplVanity.App.Core/Tutorial/TutorialStep.cs`, `TutorialScript.cs`, `TutorialService.cs`
- Modify: `src/XrplVanity.App/MauiProgram.cs` (register `TutorialService` as a singleton after `SearchViewModel`)
- Test: `tests/XrplVanity.App.Tests/TutorialServiceTests.cs`

**Interfaces:**
- Consumes: Task 1, `Vm.EstimatedTime`, `Vm.IsCalibrating`, `Vm.Results`, `Vm.Targets`, `Vm.LastSummary`, `Vm.SaveSettings`.
- Produces:
  - `enum TutorialStep { Offer, AddWord, ListFiles, Devices, LongWord, ShortWord, Search, Result, QrButton, QrDialog, GpuDuty, StopAfter, NotificationsSetting, DataFolder, Done }`
  - `sealed record TutorialStepInfo(TutorialStep Step, string Route, IReadOnlyList<string> Lit, bool Interactive, string Title, string Body)`
  - `static class TutorialScript { static IReadOnlyList<TutorialStepInfo> Steps; }`
  - `TutorialService(SearchViewModel vm, IResultStore results)`, with:
    - `event Action? Changed`, `bool IsActive`, `TutorialStepInfo? Current`, `bool CanGoNext`, `int StepNumber`, `int StepCount`, `bool CanStart`
    - `bool ShouldOffer()`, `void ShowOffer()`, `bool Begin()`, `void Next()`, `void Leave()`
    - `void QrOpened()`, `void QrClosed()`
    - `bool IsLit(string region)`, `bool IsInert(string region)`

Region names: `title`, `nav`, `start`, `patterns`, `estimate`, `devices`, `duty`, `stats`, `results`, `setting-stop`, `setting-notifications`, `setting-data`, `settings-other`.

| Step | Route | Lit | Interactive | Moves on | Title | Body |
|---|---|---|---|---|---|---|
| Offer | `/` | none | no | **Start tutorial** or **Not now** | Take a short tutorial? | You will run one real search and see what to do with the result. Nothing from it is kept. You can start it any time from Settings. |
| AddWord | `/` | patterns | yes | a reachable word of ten characters or more exists | Add a word | Type a word of ten letters or more and press Enter. |
| ListFiles | `/` | patterns | yes | Next | Keep your list | Save list writes your words to a file. Load list adds them back later. |
| Devices | `/` | devices | yes | calibration ended with an estimate; Next when an estimate already exists | Choose what searches | Tick the CPU and any graphics cards, then press measure speed. The estimate needs that number. |
| LongWord | `/` | patterns, estimate | yes | Next, enabled above 1 day | Check the time | This is how long a match should take. If it looks quick, replace your word with one of ten letters or more. |
| ShortWord | `/` | patterns, estimate | yes | estimate at 10 s or less | Make it shorter | Every extra character multiplies the wait by up to 58. Add a word of two or three letters. |
| Search | `/` | start, patterns, estimate, stats, results | yes | a result exists and the search is idle | Start the search | Press Start. The search stops at the first match. If it runs too long, stop it and shorten the word. |
| Result | `/` | results | no | Next | This is your result | The seed controls this address, and whoever has it controls the account. Keep a real seed secret. This practice result disappears when the tutorial ends. |
| QrButton | `/` | results | yes | `QrOpened` | Move it to a wallet | Press the QR button on the result. |
| QrDialog | `/` | none | no | `QrClosed` | Scan it with a wallet | With a real result, you would show this code and scan it with a wallet on your phone. You do not need to scan this one. Close the dialog to continue. |
| GpuDuty | `/` | duty | yes | `DutyPercent` changed | Set the load | A lower value leaves the card free for other applications. |
| StopAfter | `/settings` | setting-stop | no | Next | Stop after N matches | A search runs until you press Stop. A number here ends it by itself. |
| NotificationsSetting | `/settings` | setting-notifications | no | Next | Notifications | A notification tells you about each match and when a search finishes. |
| DataFolder | `/settings` | setting-data | no | Next | Your data | Your results and settings are kept in this folder. |
| Done | `/` | none | no | **Finish** | You are ready | Finish puts your words and settings back as they were. You can start this tutorial again in Settings. |

A word of ten letters is the shortest that stays above a day on the fastest measured GPU with case ignored: eight letters give about five hours.

- [ ] **Step 1: Write the failing tests** with a fake device source, driving the view model directly:

```csharp
[Fact] public void ShouldOffer_Only_With_No_Flag_No_Patterns_And_No_Results()
[Fact] public void ShouldOffer_Is_False_When_The_Store_Is_ReadOnly_Or_Throws()
[Fact] public void Begin_Starts_The_Sandbox_And_Records_The_Offer()
[Fact] public void Not_Now_Records_The_Offer_And_Starts_No_Sandbox()
[Fact] public void AddWord_Moves_On_When_A_Pattern_Exists()
[Fact] public void LongWord_Next_Needs_An_Estimate_Above_One_Day()
[Fact] public void ShortWord_Moves_On_At_Ten_Seconds()
[Fact] public void Search_Returns_To_ShortWord_When_Idle_And_The_Estimate_Is_Too_Long_Again()
[Fact] public void Search_Moves_To_Result_Only_When_Idle_With_A_Result()
[Fact] public void A_Failed_Search_Stays_On_Search_And_Leave_Still_Works()
[Fact] public void GpuDuty_Is_Left_Out_With_No_Gpu_Target()
[Fact] public void Inert_Is_True_For_Unlit_And_For_Lit_On_A_Non_Interactive_Step()
[Fact] public void Leave_Ends_The_Sandbox_From_Any_Step()                    // Theory over TutorialStep
[Fact] public void Copy_Has_No_Long_Dash()
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj --filter TutorialServiceTests`
Expected: FAIL, the types do not exist.

- [ ] **Step 3: Implement.** The service subscribes to `Vm.Changed`, which fires on worker threads, and evaluates the rule for the current step under its own lock. `Search` also falls back to `ShortWord` when idle and the estimate is above 10 seconds. `Leave` is safe to call twice.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj`
Expected: all pass.

### Task 3: Regions on the pages

**Files:**
- Create: `src/XrplVanity.App.Core/Tutorial/TutorialRegion.razor`, `src/XrplVanity.App.Core/wwwroot/scss/components/_tutorial.scss`
- Modify: `src/XrplVanity.App.Core/wwwroot/scss/app.scss`, `Pages/SearchPage.razor`, `Pages/SearchPage.razor.cs`, `Pages/SettingsPage.razor`, `Layout/MainLayout.razor`, `Components/ResultsList.razor`
- Modify tests that render these with their own `Register`: `SearchPageTests.cs`, `SettingsPageTests.cs`, `MainLayoutTests.cs`
- Test: `tests/XrplVanity.App.Tests/TutorialRegionTests.cs`, `ResultsListTests.cs`

**Interfaces:**
- Consumes: `TutorialService.IsLit`, `IsInert`, `IsActive`, `Changed`, `QrOpened`, `QrClosed`.
- Produces:
  - `<TutorialRegion Name="...">`: a `div` with `data-tutorial="@Name"`, `inert` when `IsInert`, class `tutorial-lit` or `tutorial-dimmed` while active, and no class or attribute beyond `data-tutorial` otherwise.
  - `ResultsList`: `[Parameter] public bool SecretsLocked { get; set; }`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Region_Is_Plain_When_No_Tutorial_Runs()
[Fact] public void Unlit_Region_Is_Inert_And_Dimmed()
[Fact] public void Lit_Region_On_An_Interactive_Step_Is_Usable()
[Fact] public void Lit_Region_On_An_Explaining_Step_Is_Inert()
[Fact] public void Region_Follows_The_Step_Without_A_Parent_Render()
// ResultsListTests
[Fact] public void SecretsLocked_Disables_Reveal_Copy_And_Export_But_Not_Qr()
// SearchPageTests
[Fact] public void Every_Card_Sits_In_A_Region()                             // title, start, patterns, estimate, devices, duty, stats, results
[Fact] public void Opening_And_Closing_The_Qr_Dialog_Moves_The_Tutorial_On()
// SettingsPageTests
[Fact] public void Every_Setting_Row_Sits_In_A_Region()
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj --filter "TutorialRegionTests|ResultsListTests"`
Expected: FAIL, `TutorialRegion` does not exist.

- [ ] **Step 3: Implement.** Subscribe and marshal the way `ViewModelComponent` does. Wrap `NavRail` as `nav`, the Search page's `h1` as `title` and its action buttons as `start`. On Settings, wrap the three named rows, and every other row as `settings-other`. Pass `SecretsLocked="Vm.IsSandbox"` from `SearchPage`. `.tutorial-dimmed` lowers opacity; `.tutorial-lit` uses the mint outline from the mockup. Colours come from `_var-colors.scss`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj`
Expected: all pass, including the page tests with `TutorialService` registered.

### Task 4: The bubble

**Files:**
- Create: `src/XrplVanity.App.Core/Tutorial/TutorialPlacement.cs`, `TutorialOverlay.razor`, `TutorialOverlay.razor.cs`, `TutorialOverlay.razor.css`, `src/XrplVanity.App.Core/wwwroot/js/tutorial.js`
- Modify: `Layout/MainLayout.razor`
- Test: `tests/XrplVanity.App.Tests/TutorialPlacementTests.cs`, `TutorialOverlayTests.cs`

**Interfaces:**
- Consumes: Task 2 service, `ConfirmDialog`.
- Produces:
  - `readonly record struct TutorialRect(double Left, double Top, double Width, double Height)`
  - `static TutorialRect TutorialPlacement.Place(TutorialRect? target, double bubbleWidth, double bubbleHeight, double viewWidth, double viewHeight)`
  - `tutorial.js`: `measure(names)` scrolls the first lit region into view and returns the union rectangle and the viewport size; `watch(dotNetRef)` and `unwatch()` report resize and Escape.
  - `[JSInvokable] TutorialOverlay.OnViewChanged()` and `OnEscape()`

- [ ] **Step 1: Write the failing tests**

```csharp
// TutorialPlacementTests: gap 12, margin 8
[Fact] public void Prefers_The_Right_Of_The_Target()
[Fact] public void Falls_Back_To_Left_Then_Below_Then_Above()
[Fact] public void Never_Leaves_The_Viewport()                               // Theory: 1100x720, 800x500, target at each corner
[Fact] public void With_No_Target_Sits_At_The_Top_Centre()
// TutorialOverlayTests: JSInterop.Mode = JSRuntimeMode.Loose
[Fact] public void Shows_Nothing_When_Inactive()
[Fact] public void Shows_Title_Body_Step_Number_And_Skip()
[Fact] public void Next_Appears_Only_When_The_Step_Allows_It()
[Fact] public void Offer_Shows_Start_And_Not_Now()
[Fact] public void Navigates_To_The_Route_Of_The_Step()
[Fact] public void Escape_Asks_Before_Leaving()
[Fact] public void Escape_On_The_Qr_Dialog_Step_Is_Left_To_The_Dialog()
[Fact] public void Focus_Moves_To_The_Bubble_On_A_New_Step()
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj --filter "TutorialPlacementTests|TutorialOverlayTests"`
Expected: FAIL, the types do not exist.

- [ ] **Step 3: Implement.** `MainLayout` renders `<TutorialOverlay />` after the shell and calls `ShowOffer` on first render when `ShouldOffer()` is true. The bubble is `role="dialog"`, `tabindex="-1"` and sits above the QR dialog. **Skip** calls `Leave`; Escape shows `ConfirmDialog` first. Re-measure after each step change and each `OnViewChanged`. Import the module from `./_content/XrplVanity.App.Core/js/tutorial.js`, and dispose it the way the page's components dispose their subscriptions.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj`
Expected: all pass.

### Task 5: Settings button, tray and closing the window

**Files:**
- Modify: `Pages/SettingsPage.razor`, `Layout/MainLayout.razor`, `src/XrplVanity.App/Services/TrayCoordinator.cs`
- Test: `SettingsPageTests.cs`, `MainLayoutTests.cs`

**Interfaces:**
- Consumes: `TutorialService.CanStart`, `Begin`, `Leave`, `Vm.AcceptsTrayAction`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SettingsPageTests
[Fact] public void Start_Tutorial_Is_Disabled_While_A_Search_Runs()
[Fact] public void Start_Tutorial_Begins_It()
// MainLayoutTests
[Fact] public void Closing_The_Window_Leaves_The_Tutorial_First()            // Theory over CloseBehavior: IsActive false, IsSandbox false, then the usual action
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj --filter "SettingsPageTests|MainLayoutTests"`
Expected: FAIL.

- [ ] **Step 3: Implement.** Add a **Start tutorial** button with class `run-tutorial` to Settings. Call `Tutorial.Leave()` at the top of `MainLayout.OnCloseRequested`. In `TrayCoordinator.OnTrayActivated`, return early when `!_viewModel.AcceptsTrayAction(action)`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj`
Expected: all pass.

### Task 6: Documents and the run in the VM

**Files:**
- Modify: `docs/desktop-app.md`, `site/src/desktop/index.html`, and `PRIVACY.md` with `site/src/privacy/index.html` only if they list what `settings.json` holds

- [ ] **Step 1: Describe the tutorial** in the desktop guide and on `/desktop/`: one short paragraph each, and how to start it again. Use `writing-site-copy`.

- [ ] **Step 2: Run the site tests**

Run: `node --test "site/test/*.test.mjs"`
Expected: all pass.

- [ ] **Step 3: Verify in MauiLab** with the `maui-vm-lab` skill, using `-Framework 'net10.0-windows10.0.22621.0' -Configuration Debug -ExeName XrplVanity.App.exe` and no `-RollBackFirst`. The VM has no GPU, so step GpuDuty must be absent. With an empty data folder, pass through the tutorial and check by screenshot:
  - the offer appears once and not on the next start;
  - dimming, the bubble and the layout at 1100 by 720 and at the smallest window;
  - the page layout outside the tutorial is unchanged by the region wrappers;
  - after finishing, `results.dat` has no tutorial address and `settings.json` holds the earlier words;
  - killing the process in the middle leaves `settings.json` as it was.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test tests/XrplVanity.App.Tests/XrplVanity.App.Tests.csproj`
Expected: all pass.
