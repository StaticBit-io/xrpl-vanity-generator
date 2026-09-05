# XRPL Vanity Generator — Windows Desktop Application Design

Status: approved 2026-09-05. Sixth design document, after the CLI (initial design), the
CUDA backend (Plan A/B), the backend abstraction + OpenCL backend (Plan C), the Linux
harness + pocl validation (Plan D) and multi-device orchestration (Plan E), all merged.
This document covers two sub-projects executed in order: **Plan F** (a shared search
layer extracted from the CLI) and **Plan G** (the desktop application on top of it).
secp256k1 remains permanently out of scope.

## Background

The generator is a console tool. Its search orchestration — the CPU worker pool, the
single-device GPU loop, the multi-device loop and `MatchSink` — lives in
`src/XrplVanity.Cli/Program.cs` as private static methods; the libraries underneath
(`XrplVanity.Core`, `XrplVanity.Gpu`) expose the pieces (`CandidateEvaluator`,
`GpuSearchEngine`, `MultiDeviceSearch`, `GpuBackends`, `DutyCycle`) but no session object
a second front end could drive.

The user wants a Windows desktop application with a smooth, pleasant interface in the
visual language of the StaticBit Wallet (`SB.MobileApp`). Facts that shape this design:

- The Wallet is a .NET MAUI Blazor Hybrid application (`Microsoft.Maui.Controls` and
  `Microsoft.AspNetCore.Components.WebView.Maui` 10.0.51, SDK 10.0.201). Its visual layer is
  HTML/SCSS on Bootstrap 5; the palette is a dark-only token set in
  `wwwroot/scss/common/var-colors.scss`; the font is Open Sans; the Windows build is a
  packaged MSIX (`Properties/PublishProfiles/MSIX-win-x86.pubxml`, development
  certificate `CN=User Name`, sideloaded).
- The Wallet's reusable Blazor components (`SB.WalletApp.UI.Blazor.Components`) live in a
  private repository and are not published as a package; the richer component library
  (`SB.WalletApp.UI.Components`) depends on the Wallet application layer.
- The GPU kernels (PTX and OpenCL sources) are embedded resources of `XrplVanity.Gpu`, so
  packaging does not need loose files.
- `MultiDeviceSearch` takes the duty cycle as a constructor argument; changing it during a
  run needs a small change in `XrplVanity.Gpu`.
- The CPU path has no candidate counter; the CLI prints no CPU rate.
- The machine that builds the application has the `maui-windows` workload (10.0.20). The
  Linux harness does not build the application.

## Decisions taken with the user

| Question | Decision |
| --- | --- |
| UI stack | .NET MAUI Blazor Hybrid, as in the Wallet |
| Scope of v1 | base set + time-to-hit estimate + persisted, encrypted history + tray with notifications |
| Distribution | packaged MSIX with a self-signed certificate, as in the Wallet; a real StaticBit certificate can replace it later |
| Architecture | a shared search library used by both the CLI and the application (not a CLI-spawning GUI, not a second orchestration inside the GUI) |
| Wallet integration (import a seed into the Wallet) | deferred to v2 |
| Repository | the application lives in this repository; no reference to the Wallet repository — tokens are copied |
| Theme | dark only, as in the Wallet |

## Goals

1. One search engine. The CLI and the application drive the same `SearchSession`; the CLI
   keeps its output lines and exit codes.
2. A desktop application that lets a user enter patterns, pick CPU and GPU devices, set the
   duty cycle, start and stop a search, watch live throughput, see an estimate of the time
   to the next hit, and manage found addresses without ever exposing a seed by accident.
3. Found addresses survive application restarts, encrypted for the current Windows user.
4. A long search continues while the window is minimised to the tray; hits and completion
   raise Windows notifications that never contain a seed.
5. The application looks and feels like the Wallet: its palette, typography, radii and
   motion.

## Non-goals

- Cross-platform builds of the application (Windows only; the libraries stay portable).
- Reuse of Wallet component code across repositories.
- Editing or deriving keys beyond what the CLI already does (no wallet features).
- A light theme, localisation, or an installer other than MSIX.
- UI automation of the MAUI host (covered by a manual checklist).

## Plan F — the search layer

### Project

`src/XrplVanity.Search` (`net10.0`, class library) references `XrplVanity.Core` and
`XrplVanity.Gpu`. `XrplVanity.Cli` references it and loses its orchestration code.

### Types

```csharp
namespace XrplVanity.Search;

public abstract record SearchTarget
{
    public sealed record Cpu(int Threads) : SearchTarget;                 // Threads >= 1
    public sealed record Device(DeviceSelector Selector) : SearchTarget;  // an explicit backend:index
    public sealed record FirstAvailableDevice : SearchTarget;             // the CLI's --gpu semantics
}

public sealed record SearchRequest(
    PatternSet Patterns,
    IReadOnlyList<SearchTarget> Targets,   // at least one; at most one Cpu; FirstAvailableDevice cannot be combined with Device
    int? Count,                            // null = unbounded
    int DutyPercent = DutyCycle.Full);     // 1..100, applies to GPU targets only

public enum TargetState { Pending, Opening, Running, Finished, Failed }

public sealed record TargetProgress(
    string Id,             // "cpu", "cuda:0", "opencl:1", or "gpu" for FirstAvailableDevice until it resolves
    string Name,           // "CPU (12 threads)" or the device name
    TargetState State,
    long Candidates,
    double RatePerSecond,  // candidates / seconds since the target started running
    string? Failure);

public sealed record SearchProgress(
    TimeSpan Elapsed,
    long TotalCandidates,
    double TotalRatePerSecond,
    long Reported,                          // matches accepted by the sink so far
    IReadOnlyList<TargetProgress> Targets);

public enum SessionOutcome { CountReached, Cancelled, AllTargetsFailed }

public sealed record SearchSummary(SessionOutcome Outcome, SearchProgress Final);

public sealed class MatchSink   // moved from the CLI; the writers become a callback
{
    public MatchSink(int? limit, Action<Match> report);
    public bool CountSatisfied { get; }
    public long Reported { get; }
    public bool TryReport(Match match);   // locked; false once the limit is reached (including on the call that reaches it)
}

public sealed class SearchSession : IDisposable
{
    public SearchSession(SearchRequest request, Action<Match> report, Action<string>? log = null);

    public int DutyPercent { get; set; }                  // 1..100; takes effect at the next pause of every GPU target
    public SearchProgress Progress { get; }               // latest snapshot, safe to read from any thread
    public Task<SearchSummary> Completion { get; }        // completes when every target has stopped

    public event Action<SearchProgress>? ProgressChanged; // at most every 250 ms, on a worker thread
    public event Action<Match>? MatchFound;               // after the sink accepted the match, on the reporting thread

    public void Start();                                  // once; throws InvalidOperationException on a second call
    public void Stop();                                   // idempotent; cancels every target, does not block
}
```

Behaviour:

- `Start` spins one thread per target. The `Cpu` target runs the worker pool that today lives
  in `Program.RunCpu` (per-thread `CandidateEvaluator`, 512-candidate CSPRNG pool), with a
  per-worker candidate counter published to the snapshot each time the pool is refilled.
  The device targets are handed to one `MultiDeviceSearch` (`FirstAvailableDevice` first
  resolves through `GpuBackends.TryOpenFirst` on its own thread and then behaves like a
  `Device` target — open failure is a target failure, not a session failure).
- One `MatchSink` is shared by every target; the session's `report` callback runs inside the
  sink's lock, so consumers see matches in acceptance order and the count is exact across
  CPU and GPU sources, as `MultiDeviceSearch` already guarantees for devices.
- When the sink reports the limit reached, the session cancels every target. When every
  target has failed to open, the session completes with `AllTargetsFailed`. Otherwise the
  session completes with `Cancelled` after `Stop` (or the caller's cancellation) once every
  thread has joined.
- `DutyPercent` is validated on set and forwarded to `MultiDeviceSearch.DutyPercent`, a new
  settable property on the Gpu type (volatile read before each pause; the constructor
  parameter becomes the initial value). The CPU target ignores duty; its throttle is the
  thread count.
- Exceptions inside a target thread are recorded on that target (`Failed` with the
  message) and never escape; a throwing `report` callback stops the session (existing
  `MultiDeviceSearch` semantics).

### Estimator

```csharp
public static class SearchEstimator
{
    public const int NominalAddressLength = 34;   // typical classic address length; used for `contains`

    /// Expected candidates per match for the whole set, or null when the set has no reachable pattern.
    public static double? ExpectedCandidatesPerMatch(PatternSet patterns);

    /// Expected time to the next match at the given rate, or null when either input is null or zero.
    public static TimeSpan? ExpectedTimeToMatch(double? expectedCandidates, double ratePerSecond);
}
```

The probability that a candidate matches one concrete target of length *n* is 58^−*n* for
`Starts` (the prefix excludes the fixed leading `r`, consistent with the measured 58^n in
`docs/benchmarks.md`) and for `Ends`, and (`NominalAddressLength` − 1 − *n* + 1) × 58^−*n* for
`Contains`. `PatternSet.EnumerateTargets()` already yields every case variant as its own
target, so the set's probability is the sum over targets (an upper bound that ignores
overlaps; the estimate is a guide, not a guarantee) and the expected candidates per match
is its reciprocal.

### CLI migration

- `Program.RunCpu`, `RunGpu` and `RunMultiGpu` are replaced by one `SearchSession` built
  from `CliOptions`: `--gpu` → `FirstAvailableDevice`; `--backend`/`--device` → `Device`;
  `--gpu-devices` → one `Device` per selector; otherwise `Cpu(options.Threads)`.
- The CLI's `report` callback writes `Address: …\tSeed: …` to stdout and `address\tseed`
  to `--out`, as `MatchSink` does today. `Ctrl+C` calls `Stop`. Exit codes stay 0 / 2 / 130.
- Every existing output line is preserved. The one intended addition: the CPU path prints
  `CPU: searched N candidates in T s (R/s)` at the end, mirroring the GPU line, now that
  the CPU target counts candidates.
- `SearchRunner.BuildPatternSet` stays in the CLI (it reads CLI options and `--list`
  files). `MatchSink` moves to `XrplVanity.Search` with its namespace.

## Plan G — the desktop application

### Projects

| Project | Target | Role |
| --- | --- | --- |
| `src/XrplVanity.App.Core` | `net10.0`, Razor class library | Pages, components, SCSS and static assets, view model, settings and result stores, service interfaces implemented by the host |
| `src/XrplVanity.App` | `net10.0-windows10.0.22621.0`, MAUI | Host: `MauiProgram`, `MainPage` with `BlazorWebView`, tray icon, notifications, window close handling, clipboard, manifest, icons, publish profile |
| `tests/XrplVanity.App.Tests` | `net10.0`, xUnit v3 + bUnit | Component, view-model and store tests |

Keeping the Razor layer in a `net10.0` library is what makes it testable with bUnit
without the Windows target; the host stays thin. Static web assets of the library are
served by `BlazorWebView` under `_content/XrplVanity.App.Core/`.

Identity: `ApplicationTitle` "XRPL Vanity", `ApplicationId` `io.staticbit.vanity`,
`ApplicationDisplayVersion` from the csproj. Icon and splash: a monogram on the Wallet's
`#141414` background (the same `Color` the Wallet passes to `MauiIcon`), drawn as SVG in
`Resources/AppIcon` and `Resources/Splash`.

### Host services (interfaces in App.Core, implementations in App)

```csharp
public interface IClipboardService { Task CopySecretAsync(string text, TimeSpan clearAfter); Task CopyAsync(string text); }
public interface INotificationService { void Notify(string title, string body); }          // no-op when notifications are unavailable
public interface ITrayService { void Show(TrayMenu menu); void Hide(); event Action<TrayAction>? Activated; }
public interface IWindowService { void ShowWindow(); void HideToTray(); void Exit(); }
public interface IFileDialogService { Task<string?> PickSaveTsvAsync(string suggestedName); }
```

- Clipboard: MAUI `Clipboard.Default`. `CopySecretAsync` clears the clipboard after
  `clearAfter` (30 s) only if it still holds the copied text.
- Notifications: `Microsoft.Windows.AppNotifications.AppNotificationManager` from the
  Windows App SDK that MAUI already depends on; `Register()` at start-up; activation from a
  toast shows the window. Toast bodies carry the address, the pattern and the device — never
  the seed.
- Tray: `H.NotifyIcon.Maui` (MIT). Menu: **Show**, **Duty** (25 / 50 / 80 / 100 %), **Stop
  search**, **Exit**. Double-click shows the window.
- Window close: the WinUI `AppWindow.Closing` handler on the Windows platform cancels the
  close and hides to the tray when the setting says so; with the default `Ask`, a modal in
  the page asks once ("Minimise to tray" / "Exit") with a "remember my choice" checkbox and
  stores `CloseBehavior` (`Ask`, `MinimizeToTray`, `Exit`). Exit stops a running session
  and waits for `Completion` with a 10 s cap before the process ends.

### View model

`SearchViewModel` (App.Core, singleton) owns the session lifecycle and the UI state:

- state machine `Idle → Starting → Searching → Stopping → Idle`, with `LastSummary`;
- `PatternDraft` (a list of `PatternChip { Position, Text, IgnoreCase }` plus the
  normalised `PatternSet`, rebuilt on every edit; unreachable and case-folding-skipped
  patterns surface as warnings per chip);
- `TargetsDraft` (CPU on/off with thread count 1..`Environment.ProcessorCount`, the device
  list from `DeviceCatalog` with checkboxes), `Duty`, `Count` (null = until stopped);
- `Progress` (the latest `SearchProgress`, pushed to components at most 4 times per second
  through `InvokeAsync`), `Results` (session results, newest first, virtualised above 200
  rows), `Estimate` (expected candidates, and ETA from the live rate once the session has
  run for 2 s, otherwise from the persisted rate hint of the selected targets, otherwise
  "rate unknown until the first run"; no figures are built in — the estimate card links to
  `docs/benchmarks.md` for reference numbers);
- on `MatchFound`: append to `Results`, persist through `IResultStore`, notify;
- on `Completion`: notify ("Search finished: N found"), store the measured rate per target
  as a hint in settings, return to `Idle`.

`DeviceCatalog` (Search library) is queried once at start-up and on an explicit "Refresh
devices" action; it lists `GpuBackends.All` devices with their `[cpu]` tag (OpenCL CPU
implementations are shown but unchecked by default) plus the CPU pseudo-target.

### Pages and components

One main page, two secondary pages, navigation in a slim left rail as in the Wallet's
`Navigation.razor`:

- **Search** (main). Header: logo, title, status pill (`Idle` / `Searching` with a soft
  breathing animation / `Stopping`), one large primary button (Start / Stop). Left column:
  `PatternEditor` (chips; each chip has a position selector starts/ends/contains, a case
  toggle, a warning line; a text box adds a chip on Enter; a "load list…" action imports a
  word list as `Starts` chips) and `EstimateCard` (expected candidates, ETA, the sentence
  explaining what the estimate assumes). Right column: `DevicePicker` (CPU row with thread
  slider; one row per device with backend, name, details, `[cpu]` tag) and `DutySlider`
  (25–100 %, live during a search). Bottom: `LiveStats` (total rate in beige, candidates,
  elapsed, found, one row per target with state, rate and failure text) and `ResultsList`.
- **History**. All persisted results with search by address or pattern, a reveal/copy per
  row, delete per row, "Clear history" with confirmation, "Export TSV…" for the visible
  rows.
- **Settings**. Close behaviour, notifications on/off, clipboard clear delay (default 30 s),
  seed reveal timeout (default 15 s), default duty, "Forget rate hints", data folder path
  (read-only, with "Open folder").

`ResultsList` rows show the address (monospace, copy button), the pattern that matched, the
device, the time, and the seed masked as `••••••••` with a reveal button; a revealed seed
hides itself after the reveal timeout and on navigation. Copying a seed uses
`CopySecretAsync`. TSV export writes `address<TAB>seed` per line — the CLI's `--out` format.

### Visual language

Tokens copied from the Wallet into `wwwroot/scss/common/_var-colors.scss`, exposed as CSS
custom properties so components and inline styles share them:

| Token | Value | Used for |
| --- | --- | --- |
| `--bg` | `#0D0D0D` | window background |
| `--box-bg` | `#1A1A1A` | cards and panels (radius 12px, 1px border `--dark-gray`) |
| `--chips` / `--dark-gray` | `#2D2D2D` / `#282828` | chips (radius 10px), borders, sliders' track |
| `--text` / `--text-secondary` | `#F9F9F9` / `#A8A8A8` | primary and secondary text, icons |
| `--mint` | `#64CF95` | Start button, positive states, success pill |
| `--beige` | `#EBAF8E` | numbers in statistics and estimates |
| `--negative` | `#D6776A` | errors, failed targets, Stop button hover |
| `--yellow` | `#D3E7AB` | warnings (unreachable patterns, no GPU) |
| `--secondary-dark` | `#202222` | secondary buttons |

Typography: Open Sans (the TTF ships in `wwwroot/fonts` with an `@font-face`; the MAUI
side also registers it as in the Wallet's `ConfigureFonts`). Bootstrap 5 CSS is vendored
under `wwwroot/lib/bootstrap/` as the Wallet does; SCSS is compiled at build time by the
`DartSassBuilder` package, and compiled CSS is not committed.

Motion: 150–200 ms ease-out transitions on hover, focus and enable/disable; a new result
row slides in from the top with a brief mint highlight; counters in `LiveStats` and
`EstimateCard` animate between values; the status pill breathes while searching; all of
it is disabled under `prefers-reduced-motion`. No layout shift when the state changes
(fixed-height stat tiles, reserved space for warnings).

Window: default 1100×720, minimum 900×600, remembered in settings. The layout collapses
to a single column below 1000 px of width.

### Storage

Both files live in `FileSystem.AppDataDirectory` (the package's `LocalState` folder):

- `settings.json` — `AppSettings` (patterns draft, targets draft, duty, count, window
  size, close behaviour, notification flag, clipboard clear delay, reveal timeout, rate
  hints keyed by target id). Written atomically (temp file + rename) on every change.
- `results.dat` — `ProtectedData.Protect` (`DataProtectionScope.CurrentUser`, optional
  entropy = the UTF-8 bytes of `io.staticbit.vanity`) over a UTF-8 JSON array of
  `StoredResult { Address, Seed, Pattern, Position, FoundAt, Target }`. The whole file is
  rewritten on every append or delete (histories are small); on a `Protect`/`Unprotect`
  failure the history becomes read-only for the session and the History page shows the
  reason. Nothing else on disk ever contains a seed; logs never do.

`ISettingsStore` and `IResultStore` are interfaces in App.Core with the JSON/DPAPI
implementations beside them; tests use the real implementations on a temp folder.

### Error handling

| Situation | Behaviour |
| --- | --- |
| No GPU device found | `DevicePicker` shows only the CPU row and a yellow banner "No CUDA driver or OpenCL platform found — searching on the CPU"; Start stays enabled |
| A selected device fails to open | its `LiveStats` row turns `Failed` with the message; the other targets continue (session semantics) |
| Every target fails | the session ends with `AllTargetsFailed`; a negative banner shows the first failure; Start is enabled again |
| Unreachable or case-folding-skipped pattern | warning line under the chip; Start is disabled while no reachable pattern exists |
| Both CPU and all devices unchecked | Start disabled with a hint |
| DPAPI failure | history read-only, banner on History and on the results list |
| Notifications unavailable | `INotificationService` is a no-op; the toggle in Settings explains it |
| Export fails (disk, permission) | inline error under the export button; nothing partial is left behind (write to a temp file, then move) |

### Testing

`tests/XrplVanity.Tests` (existing project) gains:

- `SearchEstimatorTests` — table cases: `Starts` length 1–4 (58, 3364, 195 112, 11 316 496),
  `Ends`, `Contains` at `NominalAddressLength`, case-insensitive expansion (a letter with
  both cases doubles the targets; `o`, `i`, `L` do not), several patterns summed, empty set
  → null, ETA arithmetic and null propagation.
- `SearchSessionTests` — CPU-only session with a one-character `Starts` pattern reaches
  `Count` exactly and completes with `CountReached`; `Stop` completes with `Cancelled` and
  every thread joins within 5 s; progress snapshots arrive with growing candidate counts and
  a CPU rate above zero; a `Device` target that does not exist fails alone while the CPU
  target keeps running; a session whose only target fails completes with `AllTargetsFailed`;
  `DutyPercent` set during a run is visible on `MultiDeviceSearch` (GPU-gated through
  `GpuTestHelper`); mixed CPU + GPU session stops exactly at `Count` (GPU-gated); a second
  `Start` throws.
- `MultiDeviceSearchTests` — a test that changes `DutyPercent` mid-run and observes a longer
  launch period, alongside the existing tests.
- CLI tests — unchanged; they are the regression gate for Plan F, plus one new test for the
  `CPU: searched …` line.

`tests/XrplVanity.App.Tests` (new; xUnit v3 with bUnit 2.x and its xunit v3 adapter):

- `PatternEditorTests` — adding a chip on Enter, position and case toggles rebuild the
  `PatternSet`, unreachable input shows the warning and disables Start.
- `ResultsListTests` — seeds are masked by default, reveal shows the seed and hides after
  the timeout (fake time), copy calls `CopySecretAsync` with the seed and the configured
  delay, export writes the CLI's TSV format through a fake `IFileDialogService`.
- `EstimateCardTests` — shows "rate unknown" before any run, uses the persisted hint, then
  the live rate after 2 s.
- `SearchViewModelTests` — drives the state machine with a real CPU-only session on a
  one-character pattern; notification and store fakes record the calls; a completion stores
  the rate hint.
- `JsonSettingsStoreTests` and `DpapiResultStoreTests` — round trip on a temp folder; the
  DPAPI test carries a Windows-only trait and is skipped elsewhere; a corrupted
  `results.dat` yields an empty, read-only history with the reason.

Manual smoke checklist (in `docs/desktop-app.md`): install the certificate and the MSIX,
launch, no-GPU banner when the driver is absent, start/stop on CPU and on the GPU, duty
change from the tray during a search, toast on a hit and on completion, close-to-tray
prompt and its memory, history survives a restart, clipboard clears after 30 s, export
opens in a text editor with two tab-separated columns.

### Build and packaging

- `XrplVanity.sln` gains the three projects. The App project builds only on Windows; the
  Linux harness keeps building because `linux/in-container.sh` builds the solution filter
  `XrplVanity.Portable.slnf` (every project except `XrplVanity.App`) instead of the
  solution.
- `Properties/PublishProfiles/MSIX-win-x64.pubxml` (self-contained, `win-x64`,
  `GenerateAppxPackageOnBuild`), the manifest at `Platforms/Windows/Package.appxmanifest`
  with `Identity Name="io.staticbit.vanity"`, `runFullTrust`.
- `app/make-cert.ps1` creates a self-signed code-signing certificate for the manifest's
  publisher and exports the `.pfx`/`.cer`; `docs/desktop-app.md` documents creating it,
  trusting the `.cer` (Local Machine, Trusted People), publishing and installing.
- No CI for the application in this repository yet; the Linux harness and the unit suite
  remain the gates. The App tests run with `dotnet run --project tests/XrplVanity.App.Tests -c Release`
  like the existing suite.

## Plans

- **Plan F** (`docs/2026-09-05-xrpl-vanity-plan-f.md`): `XrplVanity.Search` with
  `SearchSession`, `MatchSink`, `DeviceCatalog`, `SearchEstimator`; `MultiDeviceSearch.DutyPercent`;
  the CPU candidate counter; CLI migration; tests. Deliverable: a CLI with unchanged
  behaviour plus the CPU rate line, and a library the application can build on.
- **Plan G** (`docs/2026-09-05-xrpl-vanity-plan-g.md`): App.Core, App host, App tests,
  MSIX profile, certificate script, `docs/desktop-app.md`, README section. Deliverable: the
  installable application.

Both plans run through subagent-driven development on feature branches and merge into
`main` locally; pushing remains an explicit user command.

## Risks and open points

- `H.NotifyIcon.Maui` and `bunit` (xunit v3 adapter) versions are pinned in Plan G after
  checking nuget.org; if `H.NotifyIcon.Maui` does not build against MAUI 10.0.51, the fallback
  is `H.NotifyIcon.WinUI` used directly from the Windows platform folder.
- Blazor Hybrid renders inside WebView2; the search threads never touch the UI thread, so
  a slow WebView repaint cannot slow the search, but a very high hit rate (short patterns)
  can flood `MatchFound`; the view model batches appends per progress tick.
- A GPU driver reset (TDR) surfaces as a failed target, not a crash; a driver-level
  process kill would take the application and its unsaved session results with it — the
  results are persisted on every hit, so only the in-memory session list is lost.
- The `Contains` estimate assumes a 34-character address; classic addresses are 25–35
  characters, so the estimate is approximate and is labelled as such.
