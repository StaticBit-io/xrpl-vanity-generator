# Vanity address search for the StaticBit wallet — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the CPU search of this repository available to the StaticBit mobile wallet as a NuGet package, and hand the wallet's developers a guide that lets them build the feature without further help.

**Architecture:** All changes land in this repository. The CPU search moves into `XrplVanity.Core`, which has no GPU dependency, and that project becomes the package `StaticBit.XrplVanity.Core`. `docs/wallet-integration.md` tells the wallet's developers how to use it, what the stores allow and how to check the result. **`Wallet.git` is read-only here: nothing in it is changed.**

**Tech Stack:** .NET 10, BouncyCastle, xUnit v3. The consumer is .NET MAUI Blazor Hybrid on Android and iOS, and the feed is GitHub Packages (`github-staticbit`).

**Spec:** none — this plan is its own spec. Rates come from the "Android phone" section of `docs/benchmarks.md`.

## Global Constraints

- **Work on a branch based on `origin/dev`.** Pull requests target `dev` with a squash merge.
- **Do not edit anything under `E:\GIT\XRPL\Wallet.git`.** Reading it is fine.
- **Tests run with `dotnet run`, never `dotnet test`:** whole suite `dotnet run --project tests/XrplVanity.Tests -c Release --no-build`, one class with `-- -class "*CpuSearchWorkersTests"`. After a code change, build first with `dotnet build XrplVanity.sln -c Release`.
- **The build stays warning-free.**
- **The seed is a secret.** The guide must say so, and no example may log it.
- **Never say "mining"** in the guide's suggested interface or store text. The feature generates keys.
- **No time limit on a search.** A long search gets a warning and a link to the desktop app, and the user may still start it. Nothing blocks or ends it for taking long.
- Explicit types instead of `var`. Comments in English, only where the code cannot say it itself. No AI attribution in commits.
- `docs/benchmarks.md` uses CRLF. Edit it without changing the other lines' endings: `sed -i` under Git Bash strips them, so use the Edit tool or a Python script working on bytes.

## Background

### Measured rates

On a low-end phone (MT6765, 8 × Cortex-A53) the wallet's release settings reach about 7,700 candidates/s, and BouncyCastle digests raise that to about 8,900/s. LLVM AOT with those digests reaches about 20,800/s. Ten minutes showed no throttling and a drain of roughly 15% an hour.

| Case-insensitive prefix | Expected time at 7,700/s |
| --- | --- |
| 4 characters | 1.2 minutes |
| 5 characters | 35 minutes |
| 6 characters | 17 hours |

Rates differ too much between phones for a constant, so the wallet measures the rate on the device before it predicts.

**The search is memoryless.** Every candidate is independent, so stopping loses nothing and a new run has the same odds. No pause, resume or saved state is needed: an interruption is a plain stop with a way to start again.

### Store rules

Read on 2026-11-05 from the [App Store Review Guidelines](https://developer.apple.com/app-store/review/guidelines/), the Google Play policy pages and the Android developer documentation.

- **Neither store forbids this search, and neither defines "mining".** Apple bans mining on the device (3.1.5(ii)), and Google bans it in its [blockchain content policy](https://support.google.com/googleplay/android-developer/answer/13607354). The outcome rests with the reviewer.
- **Apple 2.4.2** rules out rapid battery drain, excessive heat and unnecessary strain, with no numbers, and it applies in the foreground too. This is the main risk. With no time limit on a search, the battery and heat stops are what answers it.
- **Apple 2.5.4** limits background services to their intended purposes. Stopping when the app leaves the screen keeps the feature outside it.
- **Google Play** [forbids bypassing power management](https://support.google.com/googleplay/android-developer/answer/9888379) and sets no CPU or battery limit. [Foreground services need a declaration with a video](https://support.google.com/googleplay/android-developer/answer/13392821), and `dataSync` is [limited to 6 hours a day](https://developer.android.com/develop/background-work/services/fgs/timeout) for apps targeting Android 15. Staying on screen avoids all of this.
- **Android vitals** [counts partial wake locks](https://developer.android.com/topic/performance/vitals/excessive-wakelock) only in the background or under a foreground service. The [ANR thresholds](https://developer.android.com/google/play/vitals#core-vitals-faqs) are 0.47% overall and 8% per phone model, which is why one core stays free.

### Facts about the wallet

Read from `Wallet.git` on 2026-11-05. The guide states them and names the files.

- `src/[UI]/SB.WalletApp.UI.App/Pages/Settings/Wallets/New.razor` is the entry screen; `Add.razor.cs` generates a wallet and fills `AddWalletCommand` with `ClassicAddress`, `KeyValue` (the seed), `KeyType = AccountKeyType.Seed` and `Algorithm`, stores it under `SessionStorageKeys.AddNewWallet`, and navigates to `settings/wallets/wizard`.
- `Add.razor.cs` passes `showAlghoritm=true`, but `Wizard.razor.cs` reads `showAlgorithm`, so the wizard's algorithm drop-down stays hidden today. A vanity seed must keep it hidden: changing the algorithm re-derives the seed into a different address.
- `IXrplClientService.WalletFromSeed(seed, null, null)` derives by the seed's prefix and returns the algorithm actually used.
- `Directory.Packages.props` pins `BouncyCastle.Cryptography` at 2.6.1; the package needs 2.7.0.
- `IAppLifecycleState.IsInBackground` says whether the app is on screen. `PageBase.DisposeAsync` cancels `_cts`, so `_cancellationToken` stops work when the user leaves a page.
- `IDeviceInfoService` is the existing pattern for a platform seam: an interface in the Application project, implemented in `SB.MobileApp` and `SB.WebApp`. The wallet supports Android API 24 and iOS 15.
- The web client is Blazor WASM, so the entry must be hidden there with `OperatingSystem.IsBrowser()`.

### Desktop app

The site is `https://xrpl-vanity.staticbit.io`, and the desktop page is `https://xrpl-vanity.staticbit.io/desktop/`. **The app is not in the Microsoft Store yet:** that page says it is on its way and links to a build from source, so until the listing is live the link leads to no installer.

## Review Focus

Failure modes no happy path exercises. Each has a test or a check in the owning task.

1. **The CLI or the desktop app loses a type in the move.** Everything in the solution still builds and the whole suite passes. Task 2.
2. **Digest state leaks between candidates** in the rewritten deriver, giving wrong addresses for a matching seed. Task 1.
3. **The package drags in GPU code or the kernels.** The `.nupkg` holds one DLL, and its dependency list holds BouncyCastle alone. Task 4.
4. **`MeasureRate` is called with a token already cancelled.** It returns promptly with `0` and starts nothing lasting. Task 3.
5. **The guide's sample drifts from the published API.** The sample is compiled against the packed `.nupkg`, not against project references. Task 5.

---

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
// derive entropy A, then entropy B, then A again on one BouncyCastleDeriver;
// Assert.Equal on the two A results, Assert.NotEqual on A and B
```

- [ ] **Step 2: Run the deriver and cross-check tests against the current code**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*AddressDeriverTests" -class "*CrossCheckTests"`
Expected: PASS. The behaviour must not change, so these tests are the safety net and not a red step.

- [ ] **Step 3: Move the body of `ManagedDigestDeriver` into `BouncyCastleDeriver`**

Add `Sha512Digest` and `Sha256Digest` fields next to `_ripemd`. `Seed.AppendChecksum` stays as it is: it runs once per match in `Seed.Encode`, and the deriver computes its own checksum with `_sha256`.

- [ ] **Step 4: Remove the second deriver from the benchmark**

Delete `ManagedDigestDeriver.cs`, `CheckDeriversAgree` and the `managed-digests` column in `Bench.cs`. Under "Android phone" in `docs/benchmarks.md`, add one sentence: `BouncyCastleDeriver` has used these digests since this change, so the table's last column is what the code now does, and the benchmark prints one column.

- [ ] **Step 5: Run the whole suite and compare desktop rates**

Run: `dotnet build XrplVanity.sln -c Release`, then the whole suite.
Expected: no warnings, PASS.
Run: `dotnet run --project src/XrplVanity.Cli -c Release --no-build -- --case-sensitive --starts abc --count 6 --threads 1 | grep '^CPU:'` (the filter keeps seeds out of the output)
Expected: a `CPU:` rate of at least 15,000/s. The previous code measured about 16,500/s here.

- [ ] **Step 6: Build the benchmark app**

Run: `dotnet build tools/XrplVanity.MobileBench -c Release -p:AndroidSdkDirectory="C:\Program Files (x86)\Android\android-sdk"`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/XrplVanity.Core/BouncyCastleDeriver.cs tests/XrplVanity.Tests/AddressDeriverTests.cs tools/XrplVanity.MobileBench docs/benchmarks.md
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

`SearchSession`, `SearchViewModel` and the tests already import `XrplVanity.Core`, so the compiler should have little to say beyond stale `using` lines. Keep `SearchEstimatorTests` and `MatchSinkTests` where they are. Check `src/XrplVanity.Core/XrplVanity.Core.csproj` keeps `InternalsVisibleTo` for the tests.

- [ ] **Step 4: Build and run everything**

Run: `dotnet build XrplVanity.sln -c Release`, then the whole suite.
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
- Produces: `public static double CpuSearchWorkers.MeasureRate(int threads, TimeSpan duration, CancellationToken cancellationToken)`, candidates per second; blocks for `duration` or until cancelled; returns `0` when nothing was counted or the token is already cancelled.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void MeasureRate_Is_Positive_And_Takes_About_The_Duration()
// MeasureRate(2, TimeSpan.FromMilliseconds(500), CancellationToken.None) > 0;
// wall time below 5 seconds

[Fact]
public void MeasureRate_Stops_When_Cancelled()
// a token cancelled after 100 ms and a 60-second duration: returns within 5 seconds

[Fact]
public void MeasureRate_Returns_Zero_For_A_Token_Already_Cancelled()
// new CancellationToken(canceled: true): returns 0 within 5 seconds
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CpuSearchWorkersTests"`
Expected: build error, `MeasureRate` is not defined.

- [ ] **Step 3: Implement `MeasureRate`**

Return `0` at once if the token is already cancelled. Otherwise run the workers on a case-sensitive `zzzzzzzz` starts pattern with `MatchSink(limit: null, _ => { })`, under a `CancellationTokenSource` linked to the caller's and set with `CancelAfter(duration)`. Return `CandidatesSearched / Elapsed.TotalSeconds`.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet build XrplVanity.sln -c Release`, then the command from Step 2.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Core/CpuSearchWorkers.cs tests/XrplVanity.Tests/Search/CpuSearchWorkersTests.cs
git commit -m "feat(core): measure the search rate of the machine it runs on"
```

### Task 4: The package

**Files:**
- Modify: `src/XrplVanity.Core/XrplVanity.Core.csproj`, `README.md`, `.gitignore`

**Interfaces:**
- Produces: package `StaticBit.XrplVanity.Core` version `0.1.0`, target `net10.0`, dependency `BouncyCastle.Cryptography` 2.7.0.

- [ ] **Step 1: Add package metadata to the project**

`PackageId` `StaticBit.XrplVanity.Core`, `PackageVersion` `0.1.0`, `Authors` `StaticBit`, `PackageLicenseExpression` `Apache-2.0`, `RepositoryUrl` `https://github.com/StaticBit-io/xrpl-vanity-generator`, and a one-sentence `Description`. Use `PackageVersion` and not `Version`: `Directory.Build.props` sets `Version` for the product, and the library versions on its own.

- [ ] **Step 2: Add a short "Use as a library" section to the README**

Say what the package is, name the feed, show `PatternSet.Builder`, `MatchSink` with `limit: 1` and `CpuSearchWorkers.Run`, and state that `Run` blocks and that each worker owns its deriver. Link to `docs/wallet-integration.md`.

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
- Modify: `docs/benchmarks.md` only if a link needs fixing

**Interfaces:**
- Consumes: the packed `.nupkg` (Task 4); every section of this plan's Background.

Write it in English for a developer who knows the wallet and has not seen this repository. Use the skill `developer-documentation`. Reuse the rate table and the store rules from Background, with their links. Use relative links to this repository's files.

- [ ] **Step 1: Write the guide with these sections, in this order**

1. **What you get:** one paragraph, the package name and version, and a line that the search is CPU only on a phone.
2. **Add the package:** the `Directory.Packages.props` entry, the BouncyCastle bump from 2.6.1 to 2.7.0, and which project references it. The feed `github-staticbit` is already configured in the wallet.
3. **Run a search:** a complete sample that builds a `PatternSet`, calls `CpuSearchWorkers.MeasureRate`, shows `SearchEstimator.ExpectedTimeToMatch`, runs `CpuSearchWorkers` on a long-running task with `MatchSink(limit: 1, …)`, reports `CandidatesSearched`, and cancels by `CancellationTokenSource`. It must not print the seed. State that `PatternSet.Unreachable` must be empty before starting.
4. **Where it fits in the wallet:** the wallet facts, as a numbered path from `New.razor` to the wizard: a new page beside `Add.razor`, the same "Attention!" confirmation, `AddWalletCommand` filled the same way, the wizard reached without any algorithm parameter.
5. **Check the seed before showing it:** call `WalletFromSeed(match.Seed, null, null)`, compare `ClassicAddress` with `match.Address`, take `Algorithm` from the model, and never show a seed whose address differs.
6. **Rules for a search on a phone:**
   - runs only while the app is on screen, and stops on background (poll `IAppLifecycleState` about once a second) or when the page goes away;
   - no foreground service, no partial wake lock;
   - `Math.Max(1, Environment.ProcessorCount - 1)` threads;
   - keep the screen on during a search with `DeviceDisplay.Current.KeepScreenOn`, and release it on every exit;
   - stops when the battery is below 20% and not charging;
   - stops on heat: `PowerManager.CurrentThermalStatus >= ThermalStatus.Severe` on Android, behind `OperatingSystem.IsAndroidVersionAtLeast(29)`, and `NSProcessInfo.ProcessInfo.ThermalState >= NSProcessInfoThermalState.Serious` on iOS;
   - no time limit; explain that the search is memoryless, so a stop loses nothing;
   - hidden in the web client, through one `IsAvailable`-style check used by both the entry button and the page, which also makes hiding it on one platform a one-line change.
7. **Warn about long searches:** when the expected time is above 30 minutes, show a warning and still let the user start. The warning offers the desktop app, with a link to `https://xrpl-vanity.staticbit.io/desktop/`, and says a found wallet comes back through Restore from QR code. Say plainly that the 30 minutes is a suggested trigger for the message and blocks nothing.
8. **Suggested interface text:** this table, exactly.

| Where | Text |
| --- | --- |
| Entry button | Create with a chosen address |
| Estimate | About {time} on this device. It can take three times longer. |
| Warning | This may take about {time} on this device. XRPL Vanity on a computer finds it much faster. |
| Warning link | Get the desktop app |
| During a search | Keep the app open while it searches. |
| Left the screen | Search stopped because the app left the screen. |
| Low battery | Search stopped because the battery is below 20%. |
| Heat | Search stopped because the device got too hot. |
| Under every stop | Continuing loses nothing: every try has the same chance. |

9. **Store rules and review notes:** the store rules, then one paragraph each for App Review and Google Play saying that this is local key generation the user starts and can stop, that it runs only on screen and on all cores but one, that it stops on low battery and on heat, and that nothing is sent anywhere and no reward or pooled work is involved.
10. **Expected speed:** the rate table, a link to the "Android phone" section of `benchmarks.md`, and how to rerun `tools/XrplVanity.MobileBench` on another phone.
11. **Check before release:** a list a tester can follow. A four-character search ends in a saved wallet whose address contains the text, and that wallet signs a testnet transaction. Scrolling stays smooth during a search, and Stop answers within a second. The home button, a call and the power button each stop the search, and the page says why on return. Leaving the page stops the search and lets the screen sleep. A search that outlasts the shortest auto-lock setting leaves no seed on screen and no threads running. A 30-minute search on a current phone stays at a comfortable temperature.
12. **Not known yet:** iOS has never been built or measured, and its release waits for that. Only one low-end Android phone is measured. LLVM (`-p:RunAOTCompilation=true -p:AndroidEnableProfiledAot=false -p:EnableLLVM=true`) gives about 2.3 times the rate of the release settings with the same digests, but its cost on the wallet is unmeasured. The reviewers' decision cannot be known in advance. The desktop app is not in the Microsoft Store yet, so the link leads to a page with no installer until it is.

- [ ] **Step 2: Compile the sample against the packed package**

In a scratch directory outside the repository, create a `net10.0` console project with a `nuget.config` whose only sources are `nuget.org` and the `artifacts` folder, reference `StaticBit.XrplVanity.Core` `0.1.0`, and paste the sample unchanged.
Run: `dotnet run -c Release` with a one-character pattern.
Expected: it builds with no project reference to this repository, finds a match, and exits.

- [ ] **Step 3: Validate the Markdown**

Run: `PYTHONIOENCODING=utf-8 python "C:/Users/Evril/.claude/skills/developer-documentation/scripts/validate_docs.py" docs/wallet-integration.md`
Expected: passes.

- [ ] **Step 4: Check the external links**

Run `curl -sI` on the site link and confirm it answers 200.

- [ ] **Step 5: Commit**

```bash
git add docs/wallet-integration.md
git commit -m "docs: tell the wallet's developers how to add the search"
```

---

## After the plan

These are the owner's, and no task performs them:

1. Merge the branch into `dev` through a pull request.
2. Publish the package; it is an outward-facing step:

```bash
dotnet nuget push artifacts/StaticBit.XrplVanity.Core.0.1.0.nupkg --source https://nuget.pkg.github.com/StaticBit-io/index.json --api-key <token>
```

`<token>` is a GitHub token with `write:packages`.

3. Pass `docs/wallet-integration.md` to the wallet's developers.
