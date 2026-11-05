# Vanity address search for the StaticBit wallet — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the CPU search of this repository available to the StaticBit mobile wallet as a NuGet package, and hand the wallet's developers a guide that lets them build the feature without further help.

**Architecture:** All changes land in this repository. The CPU search moves into `XrplVanity.Core`, which has no GPU dependency, gains a one-call API that is safe to use from a UI, and that project becomes the package `StaticBit.XrplVanity.Core`. `docs/wallet-integration.md` tells the wallet's developers how to use it, what the stores allow and how to check the result. **`Wallet.git` is read-only here: nothing in it is changed.**

**Tech Stack:** .NET 10, BouncyCastle, xUnit v3. The consumer is .NET MAUI Blazor Hybrid on Android and iOS, and the feed is GitHub Packages (`github-staticbit`).

**Spec:** none — this plan is its own spec. Rates come from the "Android phone" section of `docs/benchmarks.md`.

## Global Constraints

- **Work on a branch based on `origin/dev`.** Pull requests target `dev` with a squash merge.
- **Do not edit anything in the wallet repository.** Reading it is fine.
- **Tests run with `dotnet run`, never `dotnet test`.** There are two suites, and both must pass after every code change: `dotnet build XrplVanity.sln -c Release`, then `dotnet run --project tests/XrplVanity.Tests -c Release --no-build` and `dotnet run --project tests/XrplVanity.App.Tests -c Release --no-build`. One class: `-- -class "*CpuSearchTests"`.
- **The build stays warning-free.**
- **The seed is a secret.** The guide must say so, and no example may log it.
- **Never say "mining"** in the suggested interface text or in the review notes. The feature generates keys. The guide's own account of the store rules may use the word.
- **No time limit on a search.** A long search gets a warning and a link to the desktop app, and the user may still start it. Nothing blocks or ends it for taking long.
- Explicit types instead of `var`. Comments in English, only where the code cannot say it itself. No AI attribution in commits.
- `docs/benchmarks.md` uses CRLF. Edit it without changing the other lines' endings: `sed -i` under Git Bash strips them, so use the Edit tool or a Python script working on bytes.

## Background

### Measured rates

On a low-end phone (MT6765, 8 × Cortex-A53), on 8 threads, the wallet's release settings reach about 8,900 candidates/s with the digests of Task 1, and 7,700/s without them. LLVM AOT with those digests reaches about 20,800/s. Ten minutes showed no throttling, and the charge fell by 2 to 3 points. The guide recommends one thread fewer, so expect slightly less than these rates.

| Case-insensitive prefix | Expected time at 8,900/s |
| --- | --- |
| 4 characters | about 1 minute |
| 5 characters | about 31 minutes |
| 6 characters | about 15 hours |

Rates differ too much between phones for a constant, so the wallet measures the rate on the device before it predicts.

**The search is memoryless.** Every candidate is independent, so stopping a running search loses nothing and a new run has the same odds. No pause, resume or saved state is needed. This holds for a search in progress only: a seed that was found and not yet saved is lost if the app locks. **Superseded on 2026-11-05:** the owner chose to keep found keys in a history, as on the desktop; see "Keep what the search finds" in `docs/wallet-integration.md`.

### Store rules

Read on 2026-11-05 from the [App Store Review Guidelines](https://developer.apple.com/app-store/review/guidelines/), the Google Play policy pages and the Android developer documentation.

- **Neither store forbids this search, and neither defines "mining".** Apple bans mining on the device (3.1.5(ii)), and Google bans it in its [blockchain content policy](https://support.google.com/googleplay/android-developer/answer/13607354). The outcome rests with the reviewer.
- **Apple 2.4.2** rules out rapid battery drain, excessive heat and unnecessary strain, with no numbers, and it applies in the foreground too. This is the main risk. With no time limit on a search, the battery and heat stops are what answers it.
- **Apple 2.5.4** limits background services to their intended purposes. Stopping when the app leaves the screen keeps the feature outside it.
- **Google Play** [forbids bypassing power management](https://support.google.com/googleplay/android-developer/answer/9888379) and sets no CPU or battery limit. [Foreground services need a declaration with a video](https://support.google.com/googleplay/android-developer/answer/13392821), and `dataSync` is [limited to 6 hours a day](https://developer.android.com/develop/background-work/services/fgs/timeout) for apps targeting Android 15. A search that stays on screen needs none of this.
- **Android vitals** [counts partial wake locks](https://developer.android.com/topic/performance/vitals/excessive-wakelock) only in the background or under a foreground service. The [ANR thresholds](https://developer.android.com/google/play/vitals#core-vitals-faqs) are 0.47% overall and 8% per phone model, which is why one core stays free.

### Facts about the wallet

Read from `Wallet.git` on 2026-11-05. The guide states them and names the files.

- **Entry and save path.** `src/[UI]/SB.WalletApp.UI.App/Pages/Settings/Wallets/New.razor` is the entry screen. `Add.razor.cs` generates a wallet, fills `AddWalletCommand` with `ClassicAddress`, `KeyValue` (the seed), `KeyType = AccountKeyType.Seed` and `Algorithm`, stores it under `SessionStorageKeys.AddNewWallet`, and navigates to `settings/wallets/wizard`.
- **Algorithm drop-down.** `Add.razor.cs` passes `showAlghoritm=true`, but `Wizard.razor.cs` reads `showAlgorithm`, so the drop-down stays hidden today. A vanity seed must keep it hidden: changing the algorithm re-derives the seed into a different address.
- **Seed check.** `IXrplClientService.WalletFromSeed(seed, null, null)` derives by the seed's prefix and returns the algorithm actually used.
- **Dependencies.** `Directory.Packages.props` pins `BouncyCastle.Cryptography` at 2.6.1; the package needs 2.7.0.
- **Project split.** `SB.WalletApp.UI.App` and the Application project are plain `net10.0`. MAUI exists only in `src/[Clients]/SB.MobileApp`, so a page cannot call `DeviceDisplay`, `PowerManager` or `NSProcessInfo`. The pattern for a platform seam is `IDeviceInfoService`: an interface in the Application project, implemented in `SB.MobileApp` and `SB.WebApp`. The package reference goes in the Application project.
- **Page cancellation.** `PageBase._cancellationToken` is declared and never assigned (`PageBase.cs:44`), so it never cancels. `PageBase.DisposeAsync` cancels and disposes `_cts`. A page must create its own source linked to `_cts.Token`.
- **Lifecycle.** `AppEventNotifier.OnAppLifecycleChanged` reports background and foreground; `Swap.razor.cs:300` shows a page subscribing to it. `IAppLifecycleState.IsInBackground` gives the current value.
- **Auto-lock.** It is checked only in `App.OnResume` (`App.xaml.cs:132`): the timer is set in `OnSleep`, the default is 60 seconds, and the choices run from 0 to 1,800. An app on screen never locks. If the time has passed on resume, the app goes to the sign-in page and the page state is gone.
- **Foreground service.** `OnSleep` starts the wallet's own keep-alive service, and the manifest declares `FOREGROUND_SERVICE_DATA_SYNC`. The search does not use it, and store text must not imply the app has no foreground service.
- **Platforms.** `SB.MobileApp` also builds for Windows, and Blazor WASM is a separate client. The wallet supports Android API 24 and iOS 15.
- **Battery permission.** The manifest does not declare `BATTERY_STATS`, which the MAUI `Battery` API asks for on Android. The sticky `ACTION_BATTERY_CHANGED` broadcast needs no permission; `ReadBattery` in `tools/XrplVanity.MobileBench/MainActivity.cs` uses it and ran on the test phone.

### Desktop app

The site is `https://xrpl-vanity.staticbit.io`, and the desktop page is `https://xrpl-vanity.staticbit.io/desktop/`. **The app is not in the Microsoft Store yet:** that page says it is on its way and links to a build from source, so until the listing is live the link leads to no installer.

## Review Focus

Failure modes no happy path exercises. Each has a test or a check in the owning task.

1. **The CLI or the desktop app loses a type in the move.** The solution builds and both suites pass. Task 2.
2. **A cancelled search leaves threads running or throws into the UI.** `FindFirstAsync` returns with no match and every worker stopped, including for a token already cancelled. Task 3.
3. **The seed escapes through a log.** `Match.ToString()` carries no seed. Task 3.
4. **The package drags in GPU code or the kernels.** The `.nupkg` holds one DLL, and its dependency list holds BouncyCastle alone. Task 4.
5. **The guide's sample drifts from the published API.** The sample is compiled against the packed `.nupkg`, not against project references. Task 5.

---

### Task 1: BouncyCastle digests in the deriver

**Files:**
- Modify: `src/XrplVanity.Core/BouncyCastleDeriver.cs`
- Delete: `tools/XrplVanity.MobileBench/ManagedDigestDeriver.cs`
- Modify: `tools/XrplVanity.MobileBench/Bench.cs`, `docs/benchmarks.md`

**Interfaces:**
- Produces: `BouncyCastleDeriver.DeriveAddress`, same signature, no `System.Security.Cryptography` hashes on the hot path. Still one instance per thread.

No new test: behaviour must not change. `Reused_Deriver_Produces_Correct_Address_On_Second_Call` pins digest reuse and `CrossCheckTests` pins the result against the SDK.

- [ ] **Step 1: Keep a copy of the current CLI for comparison**

Run: `dotnet build src/XrplVanity.Cli -c Release -o <scratch>/cli-old`, where `<scratch>` is a directory outside the repository.
Expected: build succeeds.

- [ ] **Step 2: Move the body of `ManagedDigestDeriver` into `BouncyCastleDeriver`**

Add `Sha512Digest` and `Sha256Digest` fields next to `_ripemd`. `Seed.AppendChecksum` stays as it is: it runs once per match in `Seed.Encode`, and the deriver computes its own checksum with `_sha256`.

- [ ] **Step 3: Remove the second deriver from the benchmark**

Delete `ManagedDigestDeriver.cs`, `CheckDeriversAgree` and the `managed-digests` column in `Bench.cs`.

- [ ] **Step 4: Update "Android phone" in `docs/benchmarks.md`**

Keep the measured table. Reword the paragraph that names `ManagedDigestDeriver` and the 64-entropy comparison so it describes how the column was measured at the time, and add that `BouncyCastleDeriver` has used these digests since this change and that the benchmark now prints one column. Update the "Reproduce" text if it mentions the column.

- [ ] **Step 5: Run both suites**

Run: the commands in Global Constraints.
Expected: no warnings, PASS.

- [ ] **Step 6: Compare desktop rates by interleaving**

The `CPU:` line goes to stderr, and the filter keeps seeds out of the output. Alternate old and new, three runs each:

```bash
dotnet <scratch>/cli-old/XrplVanity.Cli.dll --case-sensitive --starts abc --count 6 --threads 1 2>&1 >/dev/null | grep '^CPU:'
dotnet src/XrplVanity.Cli/bin/Release/net10.0/XrplVanity.Cli.dll --case-sensitive --starts abc --count 6 --threads 1 2>&1 >/dev/null | grep '^CPU:'
```

Expected: the new mean is not clearly below the old one. Runs on this machine spread by about a third, so compare means, not single runs.

- [ ] **Step 7: Build the benchmark app**

Run: `dotnet build tools/XrplVanity.MobileBench -c Release -p:AndroidSdkDirectory=<path>`
Expected: build succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/XrplVanity.Core/BouncyCastleDeriver.cs tools/XrplVanity.MobileBench docs/benchmarks.md
git commit -m "perf(core): hash in managed code, where Android's one-shot calls cost about 12 us each"
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

`SearchSession`, `SearchViewModel` and the tests already import `XrplVanity.Core`, so the compiler should have little to say beyond stale `using` lines. Keep `SearchEstimatorTests` and `MatchSinkTests` where they are.

- [ ] **Step 4: Run both suites**

Run: the commands in Global Constraints.
Expected: no warnings, PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "refactor(core): move the CPU search into Core, so a phone needs no GPU code"
```

### Task 3: A one-call search that is safe from a UI

`CpuSearchWorkers` runs bare threads, calls the report callback on a worker, and calls `Cancel` on the source it is given. A UI that passes its own source or touches a component from the callback can bring the process down. This API removes that hazard rather than documenting it.

**Files:**
- Create: `src/XrplVanity.Core/CpuSearch.cs`
- Modify: `src/XrplVanity.Core/Match.cs`
- Test: `tests/XrplVanity.Tests/Search/CpuSearchTests.cs`

**Interfaces:**
- Consumes: `CpuSearchWorkers`, `MatchSink` (Task 2).
- Produces:

```csharp
public readonly record struct CpuSearchOutcome(Match? Found, long Candidates, TimeSpan Elapsed);

public static class CpuSearch
{
    public static Task<double> MeasureRateAsync(int threads, TimeSpan duration, CancellationToken cancellationToken);
    public static Task<CpuSearchOutcome> FindFirstAsync(PatternSet patterns, int threads, IProgress<long>? progress, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write the failing tests**

In `CpuSearchTests`, in the `CpuBoundCollection`:

| Test | Setup | Assertion |
| --- | --- | --- |
| `FindFirst_Returns_The_Match` | `a`, case-sensitive, `Starts`, 2 threads | `Found` has a value, its address starts with `ra`, `Candidates` at least 1 |
| `FindFirst_Cancelled_Returns_No_Match` | `zzzzzzzz`, token cancelled after 200 ms | no exception, `Found` null, `Candidates` above 0, returns within 5 s |
| `FindFirst_With_A_Token_Already_Cancelled` | `new CancellationToken(canceled: true)` | `Found` null, `Candidates` 0 |
| `FindFirst_Refuses_A_Set_With_No_Patterns` | empty `Builder().Build()` | `ArgumentException` |
| `FindFirst_Reports_Progress` | `zzzzzzzz`, cancelled after 2.5 s, a synchronous `IProgress<long>` | at least 2 reports, non-decreasing, last equals `Candidates` |
| `MeasureRate_Is_Positive_And_Takes_About_The_Duration` | 2 threads, 500 ms | result above 0, under 5 s |
| `MeasureRate_Stops_When_Cancelled` | 60 s duration, cancelled after 100 ms | returns within 5 s |
| `MeasureRate_Returns_Zero_For_A_Token_Already_Cancelled` | already cancelled | result 0 |
| `Match_ToString_Hides_The_Seed` | `new Match("rAddress", "sEdSecret", "ab", MatchPosition.Starts)` | `ToString()` contains `rAddress`, does not contain `sEdSecret` |

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CpuSearchTests"`
Expected: build error, `CpuSearch` is not defined.

- [ ] **Step 3: Implement `CpuSearch`**

- `FindFirstAsync` throws `ArgumentException` when `HasPatterns` is false, and returns a default outcome at once when the token is already cancelled.
- It creates a new `CpuSearchWorkers` and its own `CancellationTokenSource` linked to the caller's token on every call, and disposes that source only after `Run` returns. The caller's source is never passed to the workers.
- `MatchSink(limit: 1, …)` only stores the match.
- `Run` goes on a `TaskCreationOptions.LongRunning` task. The awaiting side reports `CandidatesSearched` about once a second, and once more with the final count.
- Cancellation is an outcome, not an exception.
- `MeasureRateAsync` returns `0` for a cancelled token. Otherwise it runs the same way on a case-sensitive `zzzzzzzz` starts pattern for `duration` and returns `Candidates / Elapsed.TotalSeconds`.

- [ ] **Step 4: Override `ToString` in `Match` so it prints the address, the pattern and the position, and not the seed**

- [ ] **Step 5: Run both suites**

Run: the commands in Global Constraints.
Expected: no warnings, PASS.

- [ ] **Step 6: Commit**

```bash
git add src/XrplVanity.Core/CpuSearch.cs src/XrplVanity.Core/Match.cs tests/XrplVanity.Tests/Search/CpuSearchTests.cs
git commit -m "feat(core): a one-call search and rate probe that are safe from a UI"
```

### Task 4: The package

**Files:**
- Modify: `src/XrplVanity.Core/XrplVanity.Core.csproj`, `README.md`, `.gitignore`

**Interfaces:**
- Produces: package `StaticBit.XrplVanity.Core` version `0.1.0`, target `net10.0`, dependency `BouncyCastle.Cryptography` 2.7.0.

- [ ] **Step 1: Add package metadata to the project**

`PackageId` `StaticBit.XrplVanity.Core`, `PackageVersion` `0.1.0`, `Authors` `StaticBit`, `PackageLicenseExpression` `Apache-2.0`, `RepositoryUrl` `https://github.com/StaticBit-io/xrpl-vanity-generator`, and a one-sentence `Description`. Use `PackageVersion` and not `Version`: `Directory.Build.props` sets `Version` for the product, and the library versions on its own.

- [ ] **Step 2: Add a short "Use as a library" section to the README**

Say what the package is, name the feed, show `PatternSet.Builder` and `CpuSearch.FindFirstAsync`, and link to `docs/wallet-integration.md`.

- [ ] **Step 3: Ignore `artifacts/`, pack and inspect**

Add `artifacts/` to `.gitignore`.
Run: `dotnet pack src/XrplVanity.Core -c Release -o artifacts`, then list the package with `unzip -l artifacts/StaticBit.XrplVanity.Core.0.1.0.nupkg` and read its `.nuspec`.
Expected: `lib/net10.0/XrplVanity.Core.dll` is the only DLL, no `.ptx` or `.h` file is present, and the one dependency is `BouncyCastle.Cryptography` 2.7.0.

- [ ] **Step 4: Commit**

```bash
git add src/XrplVanity.Core/XrplVanity.Core.csproj README.md .gitignore
git commit -m "build(core): package the search for the wallet"
```

### Task 5: The guide for the wallet's developers

**Files:**
- Create: `docs/wallet-integration.md`

**Interfaces:**
- Consumes: the packed `.nupkg` (Task 4); `CpuSearch` (Task 3); every section of this plan's Background.

Write it in English for a developer who knows the wallet and has not seen this repository. Use the skill `developer-documentation`. Reuse the rate table and the store rules from Background, with their links. Use relative links to this repository's files.

- [ ] **Step 1: Write the guide with these sections, in this order**

1. **What you get:** one paragraph, the package name and version, and a line that the search is CPU only on a phone.
2. **Add the package:** the `Directory.Packages.props` entry, the BouncyCastle bump from 2.6.1 to 2.7.0, and the reference in the Application project. The feed `github-staticbit` is already configured in the wallet.
3. **Run a search:** a complete sample that builds a `PatternSet`, refuses a non-empty `Unreachable`, calls `CpuSearch.MeasureRateAsync` for 3 seconds with the same thread count as the search, gets the prediction from `SearchEstimator.ExpectedCandidatesPerMatch` and `ExpectedTimeToMatch`, and calls `CpuSearch.FindFirstAsync`. State that:
   - `ExpectedTimeToMatch` returns null for a rate of `0` or an unreachable set, and can return `TimeSpan.MaxValue`, so the display must handle both;
   - `Match` holds the seed, so never log it field by field; `ToString` leaves the seed out;
   - a Blazor page refreshes through `InvokeAsync(StateHasChanged)`.
4. **Where it fits in the wallet:** a numbered path from `New.razor` to the wizard: a new page beside `Add.razor`, the same "Attention!" confirmation, `AddWalletCommand` filled the same way, the wizard reached without any algorithm parameter. Include the page cancellation fact: use `CancellationTokenSource.CreateLinkedTokenSource(_cts.Token)`, not `_cancellationToken`.
5. **Check the seed before showing it:** call `WalletFromSeed(match.Seed, null, null)`, compare `ClassicAddress` with `match.Address`, take `Algorithm` from the model, and never show a seed whose address differs.
6. **The platform seam:** an interface in the Application project on the pattern of `IDeviceInfoService`, implemented in `SB.MobileApp`, with a null implementation elsewhere. It answers three things: battery level and charging state, heat, and keeping the screen on. Give the APIs:
   - Android battery: the sticky `ACTION_BATTERY_CHANGED` broadcast, as `ReadBattery` in `tools/XrplVanity.MobileBench/MainActivity.cs`; the MAUI `Battery` API asks for `BATTERY_STATS`, which the manifest does not declare;
   - iOS battery: `Battery.Default`;
   - Android heat: `PowerManager.CurrentThermalStatus >= ThermalStatus.Severe`;
   - iOS heat: `NSProcessInfo.ProcessInfo.ThermalState >= NSProcessInfoThermalState.Serious`;
   - screen: `DeviceDisplay.Current.KeepScreenOn`, set on the main thread.
7. **Rules for a search on a phone:**
   - available on Android 10 (API 29) and later and on iOS, through one `IsAvailable`-style check used by both the entry button and the page. That excludes the web client and Windows, which has the desktop app, and Android below 10, which has no thermal status to stop on. It also makes hiding the feature on one platform a one-line change;
   - runs only while the app is on screen: subscribe to `AppEventNotifier.OnAppLifecycleChanged` and stop on background, and stop when the page goes away;
   - the search uses no foreground service and no partial wake lock;
   - `Math.Max(1, Environment.ProcessorCount - 1)` threads;
   - keep the screen on only while searching, and release it on every exit, the moment a seed is found included, so a found seed does not stay lit on an unattended phone;
   - check battery and heat about every 10 seconds; stop when the battery is below 20% and not charging, and on heat;
   - no time limit; a stopped search loses nothing, because every try is independent;
   - a seed found and not yet saved is lost if the app returns through the auto-lock path, on purpose: do not persist it to survive that.
8. **Warn about long searches:** when the expected time is above 30 minutes, show a warning and still let the user start. The warning offers the desktop app, with a link to `https://xrpl-vanity.staticbit.io/desktop/`. Say plainly that the 30 minutes is a suggested trigger for the message and blocks nothing.
9. **Suggested interface text:** this table, exactly.

| Where | Text |
| --- | --- |
| Entry button | Create with a chosen address |
| Estimate | About {time} on this device. It can take three times longer. |
| Warning | This may take about {time} on this device. XRPL Vanity on a computer finds it much faster, and Restore from QR code brings the wallet here. |
| Warning link | Get the desktop app |
| During a search | Keep the app open while it searches. |
| Left the screen | Search stopped because the app left the screen. |
| Low battery | Search stopped because the battery is below 20%. |
| Heat | Search stopped because the device got too hot. |
| Under every stop | Continuing loses nothing: every try has the same chance. |

10. **Store rules and review notes:** the store rules, then one paragraph each for App Review and Google Play saying that this is local key generation the user starts and can stop, that it runs only on screen and on all cores but one, that it stops on low battery and on heat, that the search itself uses no foreground service, and that nothing is sent anywhere and no reward or pooled work is involved.
11. **Expected speed:** the rate table, a link to the "Android phone" section of `benchmarks.md`, and how to rerun `tools/XrplVanity.MobileBench` on another phone.
12. **Check before release:** a list a tester can follow.
    - A four-character search ends in a saved wallet whose address contains the text, and that wallet signs a testnet transaction.
    - Scrolling stays smooth during a search, and Stop answers within a second.
    - The home button and the power button each stop the search. On a return before the auto-lock time, the page says why. On a return after it, the sign-in page appears, no seed is shown, and no threads run.
    - Leaving the page stops the search and lets the screen sleep.
    - After a find, the screen sleeps normally.
    - A warned search can still be started, and the link opens the site.
    - A 30-minute search on a current phone stays at a comfortable temperature.
13. **Not known yet:** iOS has never been built or measured, and its release waits for that. Only one low-end Android phone is measured. LLVM (`-p:RunAOTCompilation=true -p:AndroidEnableProfiledAot=false -p:EnableLLVM=true`) gives about 2.3 times the rate of the release settings with the same digests, but its cost on the wallet is unmeasured. The reviewers' decision cannot be known in advance. The desktop app is not in the Microsoft Store yet, so the link leads to a page with no installer; wait for the listing before release, or accept that.

- [ ] **Step 2: Compile the sample against the packed package**

In a scratch directory outside the repository, create a `net10.0` console project with a `nuget.config` whose only sources are `nuget.org` and the `artifacts` folder, reference `StaticBit.XrplVanity.Core` `0.1.0`, and paste the sample unchanged, with stubs only for what the sample takes as given.
Run: `dotnet restore --packages ./packages`, then `dotnet run -c Release` with a one-character pattern. The local packages folder keeps a stale copy in the global NuGet cache out of the test.
Expected: it builds with no project reference to this repository, finds a match, and exits.

- [ ] **Step 3: Validate the Markdown**

Run: `PYTHONIOENCODING=utf-8 python <developer-documentation skill>/scripts/validate_docs.py docs/wallet-integration.md`
Expected: passes.

- [ ] **Step 4: Check the site link**

Run: `curl -sI https://xrpl-vanity.staticbit.io/desktop/`
Expected: 200.

- [ ] **Step 5: Commit**

```bash
git add docs/wallet-integration.md
git commit -m "docs: tell the wallet's developers how to add the search"
```

---

## After the plan

These are the owner's, and no task performs them:

1. Merge the branch into `dev` through a pull request.
2. Merge `dev` into `main`. The `package` job of `.github/workflows/dotnet.yml` publishes the
   package once both test suites pass.
3. Pass `docs/wallet-integration.md` to the wallet's developers.
