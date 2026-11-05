# Vanity address search in the StaticBit wallet — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user of the StaticBit mobile wallet search on the device for an address containing a chosen text while creating an account, and send searches too long for a phone to the desktop app.

**Architecture:** Two repositories, joined by one NuGet package. Part A makes the CPU search of this repository usable without its GPU code and publishes it as `StaticBit.XrplVanity.Core`. Part B adds a service, a platform seam and one page to the wallet, and the found seed then takes the existing path through `AddWalletCommand` and the wizard. The search runs only while the app is open, because that keeps it clear of every background rule in "Store rules" below.

**Tech Stack:** .NET 10, BouncyCastle, .NET MAUI Blazor Hybrid on Android and iOS, GitHub Packages feed `github-staticbit`. Tests: xUnit v3 in both repositories; the wallet also uses Moq and FluentAssertions under its existing pins.

**Spec:** none — this plan is its own spec. Rates come from the "Android phone" section of `docs/benchmarks.md`.

## Global Constraints

- **Part A branches from `origin/dev`** and its pull requests target `dev` with a squash merge. Part B follows the wallet's own flow from `main`.
- **Tests here run with `dotnet run`, never `dotnet test`:** `dotnet run --project tests/XrplVanity.Tests -c Release --no-build`, one class with `-- -class "*CpuSearchWorkersTests"`. **The wallet uses `dotnet test`:** `dotnet test "src/[Tests]/SB.Application.Tests/SB.Application.Tests.csproj" -c Release`.
- **The seed is a secret.** It never reaches a log, an exception message, a file or analytics. Log the stop reason and the candidate count only.
- **The search runs only while the app is on screen.** No foreground service, no partial wake lock, no background task.
- **Never say "mining"** in the interface, in store text or in code names. The feature generates keys.
- Numeric limits live in `VanityLimits` (Task 5): 30 minutes expected, 90 minutes per run, 3 seconds of calibration, 20% battery, one core left free.
- Explicit types instead of `var`. Comments in English, only where the code cannot say it itself. No AI attribution in commits. Commit messages follow each repository's own language.
- The build stays warning-free in this repository. In the wallet, read `docs/instructions/UI/README.md` before writing UI and reuse its components.

## Background

### Measured rates

On a low-end phone (MT6765, 8 × Cortex-A53) the wallet's release settings reach about 7,700 candidates/s, and BouncyCastle digests raise that to about 8,900/s. LLVM AOT with those digests reaches about 20,800/s. Ten minutes showed no throttling and a drain of roughly 15% an hour.

| Case-insensitive prefix | Expected time at 7,700/s |
| --- | --- |
| 4 characters | 1.2 minutes |
| 5 characters | 35 minutes |
| 6 characters | 17 hours |

Rates differ too much between phones for a constant, so the page calibrates on the device before it predicts.

**The search is memoryless.** Every candidate is independent, so stopping loses nothing and a new run has the same odds. That is why this plan has no pause, no resume and no saved state: every interruption is a plain stop with a "Continue" button.

### Store rules

Read on 2026-11-05 from the App Store Review Guidelines, the Google Play policy pages and the Android developer documentation.

- **Neither store forbids this search, and neither defines "mining".** Apple bans mining on the device (3.1.5(ii)), and Google bans it in its blockchain content policy. The outcome rests with the reviewer.
- **Apple 2.4.2** rules out rapid battery drain, excessive heat and unnecessary strain, with no numbers, and it applies in the foreground too. This is the main risk and the reason for the limits in this plan.
- **Apple 2.5.4** limits background services to their intended purposes. Stopping when the app leaves the screen keeps the feature outside it.
- **Google Play** forbids bypassing power management and sets no CPU or battery limit. Foreground services need a declaration with a video, and `dataSync` is limited to 6 hours a day for apps targeting Android 15. Staying on screen avoids all of this.
- **Android vitals** counts partial wake locks only in the background or under a foreground service. The ANR thresholds are 0.47% overall and 8% per phone model, which is why one core stays free.

## Review Focus

Failure modes no happy path exercises. Each has a test or a device check in the owning task.

1. **The app leaves the screen in mid-search** (home button, call, lock). The search stops within a second, and the page shows why on return. Task 5, Task 8.
2. **The search library and the wallet's SDK disagree on an address.** The user must never be shown a seed that restores a different address. Task 5.
3. **The user leaves the page in mid-search**, by back navigation or by auto-lock. The threads stop and the screen may sleep again. Task 5, Task 8.
4. **The search ends in any way at all** (found, stopped, exception). `KeepScreenOn` is released every time. Task 5.
5. **A pattern that can never match, or one that is empty.** It is refused before any work starts. Task 5.

---

## Part A — xrpl-vanity

### Task 1: BouncyCastle digests in the deriver

**Files:**
- Modify: `src/XrplVanity.Core/BouncyCastleDeriver.cs`
- Delete: `tools/XrplVanity.MobileBench/ManagedDigestDeriver.cs`
- Modify: `tools/XrplVanity.MobileBench/Bench.cs`, `docs/benchmarks.md`
- Test: `tests/XrplVanity.Tests/AddressDeriverTests.cs`

**Interfaces:**
- Produces: `BouncyCastleDeriver.DeriveAddress`, same signature, no `System.Security.Cryptography` hashes on the hot path. Still one instance per thread.

- [ ] **Step 1: Write the test that pins digest reuse**

```csharp
[Fact]
public void One_Instance_Gives_The_Same_Address_Twice()
// derive the same entropy twice on one BouncyCastleDeriver, then a different entropy,
// then the first again; Assert.Equal on the first and last, Assert.NotEqual on the middle
```

- [ ] **Step 2: Run the deriver and cross-check tests against the current code**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*AddressDeriverTests" -class "*CrossCheckTests"`
Expected: PASS. The behaviour must not change, so these tests are the safety net and not a red step.

- [ ] **Step 3: Move the body of `ManagedDigestDeriver` into `BouncyCastleDeriver`**

Add `Sha512Digest` and `Sha256Digest` fields next to `_ripemd`. `Seed.AppendChecksum` stays as it is: it runs once per match in `Seed.Encode`, and the deriver computes its own checksum with `_sha256`.

- [ ] **Step 4: Remove the second deriver from the benchmark**

Delete `ManagedDigestDeriver.cs`, `CheckDeriversAgree` and the `managed-digests` column in `Bench.cs`. Under "Android phone" in `docs/benchmarks.md`, add one sentence: `BouncyCastleDeriver` has used these digests since this commit, so the third column is what the app now runs.

- [ ] **Step 5: Run the whole suite and compare desktop rates**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release --no-build`
Expected: PASS.
Run the CLI with `--case-sensitive --starts abc --count 6 --threads 1`, interleaved against the previous commit.
Expected: no drop beyond noise from about 16,500/s (the benchmark measured 19,680/s and 22,032/s).

- [ ] **Step 6: Commit**

```bash
git add src/XrplVanity.Core/BouncyCastleDeriver.cs tests/XrplVanity.Tests/AddressDeriverTests.cs tools/XrplVanity.MobileBench docs/benchmarks.md
git commit -m "perf(core): hash in managed code, where Android's one-shot calls cost 12 us each"
```

### Task 2: The CPU search without the GPU

**Files:**
- Move: `src/XrplVanity.Search/CpuSearchWorkers.cs`, `MatchSink.cs`, `SearchEstimator.cs` to `src/XrplVanity.Core/`
- Modify: `src/XrplVanity.Search/SearchSession.cs` and any file the compiler names
- Test: `tests/XrplVanity.Tests/Search/CpuSearchWorkersTests.cs`

**Interfaces:**
- Produces, all in namespace `XrplVanity.Core`: `public sealed class CpuSearchWorkers(int threads, PatternSet patterns, MatchSink sink)` with `long CandidatesSearched`, `TimeSpan Elapsed` and `void Run(CancellationTokenSource stop)`; `public sealed class MatchSink`; `public static class SearchEstimator`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void The_Cpu_Search_Lives_In_Core_And_Needs_No_Gpu()
// Assert.Same(typeof(Seed).Assembly, typeof(CpuSearchWorkers).Assembly);
// Assert.DoesNotContain(typeof(CpuSearchWorkers).Assembly.GetReferencedAssemblies(),
//     a => a.Name == "XrplVanity.Gpu");
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CpuSearchWorkersTests"`
Expected: FAIL on `Assert.Same`.

- [ ] **Step 3: Move the three files with `git mv`, change their namespace to `XrplVanity.Core`, and make `CpuSearchWorkers` public**

`SearchSession`, `SearchViewModel` and the tests already import `XrplVanity.Core`, so only stale `using` lines should need attention. Keep `SearchEstimatorTests` and `MatchSinkTests` where they are.

- [ ] **Step 4: Build and run everything**

Run: `dotnet build XrplVanity.sln -c Release`, then `dotnet run --project tests/XrplVanity.Tests -c Release --no-build`
Expected: no warnings, PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "refactor(core): move the CPU search into Core, so a phone needs no GPU code"
```

### Task 3: Measuring the rate on the device

**Files:**
- Modify: `src/XrplVanity.Core/CpuSearchWorkers.cs`
- Test: `tests/XrplVanity.Tests/Search/CpuSearchWorkersTests.cs`

**Interfaces:**
- Consumes: `CpuSearchWorkers`, `MatchSink` (Task 2).
- Produces: `public static double CpuSearchWorkers.MeasureRate(int threads, TimeSpan duration, CancellationToken cancellationToken)`, candidates per second; blocks for `duration` or until cancelled; returns `0` when nothing was counted.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void MeasureRate_Is_Positive_And_Takes_About_The_Duration()
// MeasureRate(2, TimeSpan.FromMilliseconds(500), CancellationToken.None) > 0;
// wall time below 5 seconds

[Fact]
public void MeasureRate_Stops_When_Cancelled()
// a token cancelled after 100 ms and a 60-second duration: returns within 5 seconds
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CpuSearchWorkersTests"`
Expected: build error, `MeasureRate` is not defined.

- [ ] **Step 3: Implement `MeasureRate`**

Run the workers on a case-sensitive `zzzzzzzz` starts pattern with `MatchSink(limit: null, _ => { })`, under a `CancellationTokenSource` linked to the caller's and set with `CancelAfter(duration)`. Return `CandidatesSearched / Elapsed.TotalSeconds`.

- [ ] **Step 4: Run the tests to see them pass**

Run: the command from Step 2.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Core/CpuSearchWorkers.cs tests/XrplVanity.Tests/Search/CpuSearchWorkersTests.cs
git commit -m "feat(core): measure the search rate of the machine it runs on"
```

### Task 4: The package

**Files:**
- Modify: `src/XrplVanity.Core/XrplVanity.Core.csproj`, `README.md`

**Interfaces:**
- Produces: package `StaticBit.XrplVanity.Core` version `0.1.0`, target `net10.0`, dependency `BouncyCastle.Cryptography` 2.7.0.

- [ ] **Step 1: Add package metadata to the project**

`PackageId` `StaticBit.XrplVanity.Core`, `Version` `0.1.0`, `Authors` `StaticBit`, `PackageLicenseExpression` `Apache-2.0`, `RepositoryUrl` `https://github.com/StaticBit-io/xrpl-vanity-generator`, and a one-sentence `Description`.

- [ ] **Step 2: Add a short "Use as a library" section to the README**

Show `PatternSet.Builder`, `MatchSink` with `limit: 1`, and `CpuSearchWorkers.Run`, then say that `Run` blocks and each worker owns its deriver.

- [ ] **Step 3: Pack and inspect**

Run: `dotnet pack src/XrplVanity.Core -c Release -o artifacts`
Expected: `artifacts/StaticBit.XrplVanity.Core.0.1.0.nupkg`, containing `lib/net10.0/XrplVanity.Core.dll` and no GPU files. Confirm that `artifacts/` is ignored by git.

- [ ] **Step 4: Commit and open the pull request to `dev`**

```bash
git add src/XrplVanity.Core/XrplVanity.Core.csproj README.md .gitignore
git commit -m "build(core): package the search for the wallet"
```

- [ ] **Step 5: Owner action — publish**

Publishing is outward-facing, so the owner confirms it first. After the merge:

```bash
dotnet nuget push artifacts/StaticBit.XrplVanity.Core.0.1.0.nupkg --source https://nuget.pkg.github.com/StaticBit-io/index.json --api-key <token>
```

`<token>` is a GitHub token with `write:packages`. Part B starts once the feed lists the package.

---

## Part B — wallet (`Wallet.git`)

### Task 5: The search service

**Files:**
- Modify: `Directory.Packages.props`, `src/[Application]/SB.WalletApp.Application/SB.WalletApp.Application.csproj`, `src/[Application]/SB.WalletApp.Application/IoC/AppServiceExtensions.cs`
- Create in `src/[Application]/SB.WalletApp.Application/Services/Vanity/`: `VanityLimits.cs`, `VanityModels.cs`, `IVanityDeviceHost.cs`, `IVanityAddressService.cs`, `VanityAddressService.cs`
- Test: `src/[Tests]/SB.Application.Tests/Services/Vanity/VanityAddressServiceTests.cs`

**Interfaces:**
- Consumes: `StaticBit.XrplVanity.Core` (Part A); `IXrplClientService.WalletFromSeed(string, string?, string?)`; `IAppLifecycleState.IsInBackground`.
- Produces:

```csharp
public static class VanityLimits
{
    public static readonly TimeSpan MaxExpectedTime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MaxRunTime = TimeSpan.FromMinutes(90);
    public static readonly TimeSpan Calibration = TimeSpan.FromSeconds(3);
    public const double BatteryFloor = 0.20;
    public static int Threads(int processorCount) => Math.Max(1, processorCount - 1);
}

public sealed record VanityQuery(string Pattern, MatchPosition Position, bool IgnoreCase);
public sealed record VanityEstimate(bool Reachable, TimeSpan? ExpectedTime, bool AllowedOnDevice);
public enum VanityStopReason { Found, Cancelled, AppInBackground, BatteryLow, DeviceHot, TimeLimit }
public sealed record VanityOutcome(VanityStopReason Reason, AddWalletCommand? Command, long Candidates);
public enum VanityDeviceCondition { Normal, BatteryLow, Hot }

public interface IVanityDeviceHost
{
    VanityDeviceCondition ReadCondition();
    void KeepScreenOn(bool on);
}

public interface IVanityAddressService
{
    bool IsAvailable { get; }
    Task<double> MeasureRateAsync(CancellationToken cancellationToken);
    VanityEstimate Estimate(VanityQuery query, double ratePerSecond);
    Task<VanityOutcome> SearchAsync(VanityQuery query, IProgress<long>? progress, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Add the package and raise BouncyCastle**

In `Directory.Packages.props`, add `StaticBit.XrplVanity.Core` `0.1.0` and raise `BouncyCastle.Cryptography` from 2.6.1 to 2.7.0, which the package requires. Reference the package from the Application project.
Run: the whole `SB.Application.Tests` and `SB.WalletApp.Security.Tests` suites.
Expected: PASS, so the BouncyCastle bump is proven before anything is built on it.

- [ ] **Step 2: Write the failing tests**

Give `VanityAddressService` an internal constructor taking a poll interval and a run limit, so tests use 20 ms and 300 ms. Fake `IVanityDeviceHost` and `IAppLifecycleState`, and set up `IXrplClientService.WalletFromSeed` with Moq to return the real `XrplWallet.FromSeed` result.

| Test | Query | Assertion |
| --- | --- | --- |
| `Threads_Leave_One_Core_Free` | — | `Threads(8) == 7`, `Threads(1) == 1` |
| `Estimate_Refuses_An_Empty_Pattern` | `""` | `Reachable` false, `AllowedOnDevice` false |
| `Estimate_Allows_Four_Characters_On_A_Slow_Phone` | `abcd`, ignore case, 7,700/s | `AllowedOnDevice` true |
| `Estimate_Blocks_Beyond_Thirty_Minutes` | `abcde`, ignore case, 7,700/s | `AllowedOnDevice` false, `ExpectedTime` above 30 minutes |
| `Search_Returns_A_Command_For_The_Found_Address` | `a`, case-sensitive, `Starts` | `Found`; address starts with `ra`; `KeyValue` starts with `sEd`; `KeyType` is `Seed`; `Algorithm` is `ed25519` |
| `Search_Refuses_When_The_Sdk_Derives_Another_Address` | mock returns another address | throws `InvalidOperationException`; the message contains no `sEd` |
| `Search_Refuses_An_Unreachable_Pattern` | `""` | throws `ArgumentException`, no workers start |
| `Search_Stops_When_The_App_Goes_To_The_Background` | `zzzzzzzz`, flips after start | `AppInBackground`, `Command` null |
| `Search_Stops_On_Low_Battery` / `Search_Stops_When_Hot` | `zzzzzzzz`, condition changes | `BatteryLow` / `DeviceHot` |
| `Search_Stops_At_The_Run_Limit` | `zzzzzzzz` | `TimeLimit` |
| `Search_Reports_Cancelled_Without_Throwing` | token cancelled | `Cancelled`, `Candidates` above 0 |
| `Search_Does_Not_Start_In_The_Background` | already in background | `AppInBackground`, `Candidates` is 0 |
| `Screen_Is_Released_On_Every_Exit` | found, cancelled and the SDK-mismatch throw | `KeepScreenOn(true)` then `KeepScreenOn(false)` once each |
| `Progress_Never_Goes_Backwards` | `zzzzzzzz`, cancelled | reported values non-decreasing |
| `Search_Library_Agrees_With_The_Wallet_Sdk` | no mock: 100 random entropies | `BouncyCastleDeriver` address equals `XrplWallet.FromSeed(Seed.Encode(entropy), null, null).ClassicAddress` |

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test "src/[Tests]/SB.Application.Tests/SB.Application.Tests.csproj" -c Release --filter "FullyQualifiedName~Vanity"`
Expected: build errors, the types are not defined.

- [ ] **Step 4: Implement `VanityAddressService`**

- `IsAvailable` is `!OperatingSystem.IsBrowser()`. It is the single decision point for the entry button and the page, so hiding the feature on iOS after a review refusal is a one-line change.
- `Estimate` builds a `PatternSet`, treats any entry in `Unreachable` as not reachable, and takes the time from `SearchEstimator.ExpectedTimeToMatch`.
- `SearchAsync` runs `CpuSearchWorkers` with `VanityLimits.Threads(Environment.ProcessorCount)` on a `TaskCreationOptions.LongRunning` task, with `MatchSink(limit: 1, …)`. The awaiting side polls the lifecycle, the device condition, the token and the clock once a second, reports `CandidatesSearched`, and cancels the pool on the first reason it finds.
- After a match, call `WalletFromSeed(match.Seed, null, null)`. An empty algorithm means "by the seed's prefix". Throw if `ClassicAddress` differs from `match.Address`, and take `Algorithm` from the model.
- `KeepScreenOn(false)` goes in a `finally`.

Register the service as a singleton, and add `services.TryAddSingleton<IVanityDeviceHost, NullVanityDeviceHost>()` with a null host that reports `Normal`.

- [ ] **Step 5: Run the tests to see them pass**

Run: the command from Step 3, then the whole `SB.Application.Tests` project.
Expected: PASS.

- [ ] **Step 6: Commit**

Stage the files above and commit in the wallet's commit style, type `feat(wallet)`.

### Task 6: The phone's side of the seam

**Files:**
- Create: `src/[Clients]/SB.MobileApp/Services/MauiVanityDeviceHost.cs`
- Modify: `src/[Clients]/SB.MobileApp/IoC/MobileAppServiceExtensions.cs`, `src/[Application]/SB.WalletApp.Application/Services/Vanity/IVanityDeviceHost.cs`
- Test: `src/[Tests]/SB.Application.Tests/Services/Vanity/VanityDeviceRulesTests.cs`

**Interfaces:**
- Consumes: `IVanityDeviceHost`, `VanityLimits.BatteryFloor` (Task 5).
- Produces: `public static VanityDeviceCondition VanityDeviceRules.Classify(double chargeLevel, bool charging, bool hot)` in the Application project, so the rule is tested without a phone.

- [ ] **Step 1: Write the failing tests**

`Classify(0.5, false, false)` is `Normal`; `Classify(0.19, false, false)` is `BatteryLow`; `Classify(0.19, true, false)` is `Normal`; `Classify(-1, false, false)` is `Normal`, because MAUI reports an unknown level as a negative; `Classify(0.5, false, true)` is `Hot`; `Classify(0.1, false, true)` is `Hot`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test "src/[Tests]/SB.Application.Tests/SB.Application.Tests.csproj" -c Release --filter "FullyQualifiedName~VanityDeviceRules"`
Expected: build error.

- [ ] **Step 3: Implement `Classify` and `MauiVanityDeviceHost`**

- Battery: `Battery.Default.ChargeLevel` and `Battery.Default.State`.
- Android heat: `PowerManager.CurrentThermalStatus >= ThermalStatus.Severe`, behind `OperatingSystem.IsAndroidVersionAtLeast(29)`, because the wallet supports API 24; older versions report not hot.
- iOS heat: `NSProcessInfo.ProcessInfo.ThermalState >= NSProcessInfoThermalState.Serious`.
- `KeepScreenOn`: `DeviceDisplay.Current.KeepScreenOn` through `MainThread.BeginInvokeOnMainThread`.

- [ ] **Step 4: Run the tests and build the phone targets**

Run: the command from Step 2, then `dotnet build "src/[Clients]/SB.MobileApp/SB.MobileApp.csproj" -f net10.0-android -c Debug`, then the same with `-f net10.0-ios` where a Mac is paired.
Expected: PASS, build succeeds.

- [ ] **Step 5: Commit**

Stage the files above and commit in the wallet's commit style.

### Task 7: The page

**Files:**
- Create: `src/[UI]/SB.WalletApp.UI.App/Pages/Settings/Wallets/AddVanity.razor`, `AddVanity.razor.cs`
- Modify: `src/[UI]/SB.WalletApp.UI.App/Pages/Settings/Wallets/New.razor`, `New.razor.cs`

**Interfaces:**
- Consumes: `IVanityAddressService` (Task 5); `SessionStorageKeys.AddNewWallet`; route `settings/wallets/wizard`.
- Produces: route `/settings/wallets/add-vanity`.

- [ ] **Step 1: Add the entry point**

In `New.razor`, add a Dark `Button` under "Create an account", labelled "Create with a chosen address", shown when `IVanityAddressService.IsAvailable`. It navigates to `settings/wallets/add-vanity`.

- [ ] **Step 2: Build the page's four states**

`AddVanity` inherits `PageBase<object>` with `[Authorize]`. When `IsAvailable` is false it goes to `PageUrls.ROOT`, so a deep link cannot reach it. Calibrate once on entry with `MeasureRateAsync(_cancellationToken)`. Pass `_cancellationToken` to the search: `PageBase.DisposeAsync` cancels it, which is what stops the threads when the user leaves.

| State | Shows | Actions |
| --- | --- | --- |
| Compose | text box; `Starts` / `Ends` / `Contains` drop-down, default `Starts`; "Ignore case" toggle, default on; the estimate | Start, enabled only when `Reachable` and `AllowedOnDevice` |
| Searching | candidates tried, elapsed time, "Keep the app open while it searches." | Stop |
| Stopped | the reason text below | Continue, which starts the same query; Back to compose |
| Found | the address, and the seed in a read-only `TextBox` with the copy button, as in `Add.razor` | Next |

Next behaves as in `Add.razor`: confirm with the same "Attention!" dialog, store the command under `SessionStorageKeys.AddNewWallet`, and go to `settings/wallets/wizard` **without** an algorithm parameter. The wizard's algorithm drop-down would otherwise let the user re-derive the seed into a different address.

- [ ] **Step 3: Use this copy exactly**

| Where | Text |
| --- | --- |
| Estimate | About {time} on this device. It can take three times longer. |
| Over the limit | That would take about {time} on this device. XRPL Vanity on a computer finds it much faster. Bring the result here with Restore from QR code. |
| `AppInBackground` | Search stopped because the app left the screen. |
| `BatteryLow` | Search stopped because the battery is below 20%. |
| `DeviceHot` | Search stopped because the device got too hot. |
| `TimeLimit` | Search stopped after 90 minutes. |
| Under every stop | Continuing loses nothing: every try has the same chance. |

- [ ] **Step 4: Build and run the suite**

Run: `dotnet build src/SB.WalletApp.sln`, then the `SB.Application.Tests` project.
Expected: build succeeds, PASS. Razor behaviour is checked on a device in Task 8.

- [ ] **Step 5: Commit**

Stage the page and `New.razor` and commit in the wallet's commit style.

### Task 8: Device checks, LLVM and store text

**Files:**
- Create: `docs/vanity-search-review-notes.md` in the wallet repository

- [ ] **Step 1: Check the feature on the Android test phone**

Install a Release build on the HONOR phone. Its firmware silences app logcat, so judge by the screen. Expected:
- A four-character search finds an address, and the wizard saves a wallet whose address contains the text.
- The new wallet signs a testnet transaction.
- Scrolling stays smooth during a search, and Stop answers within a second.
- The home button, a call and the power button each stop the search, and the page shows the reason on return.
- Leaving the page stops the search and the screen may sleep again.
- With auto-lock set to its shortest value, a search that outlasts it leaves no seed on screen and no threads running.

- [ ] **Step 2: Repeat Step 1 on an iPhone and a current Android phone, and record the rate and the heat**

Run a 30-minute search on each. Add the rates to the "Android phone" section of this repository's `docs/benchmarks.md`, or to a new iOS section. **This is the first iOS measurement, and the iOS release waits for it.** If a phone gets uncomfortably hot, lower the thread count before release.

- [ ] **Step 3: Measure LLVM on the real app**

Build the Android AAB as it ships, then with `-p:RunAOTCompilation=true -p:AndroidEnableProfiledAot=false -p:EnableLLVM=true`. Record download size, build time, cold start and the search rate. **Owner decision:** enable it or stay with the default. The feature works either way; only the rates differ.

- [ ] **Step 4: Write the review notes**

In `docs/vanity-search-review-notes.md`, keep one paragraph for App Review and one for Google Play. They say that the feature is local key generation that the user starts and can stop; that it runs only while the app is on screen, on all cores but one, and stops on low battery, on heat and after 90 minutes; and that nothing is sent anywhere and no reward or pooled work is involved. Mention no other project's terms.

- [ ] **Step 5: Owner action — submit**

Submit with the notes attached. If a reviewer refuses the feature on one platform, change `IsAvailable` to exclude it and resubmit; the QR path from the desktop app keeps working there.

- [ ] **Step 6: Commit**

Stage the notes and commit in the wallet's commit style.
