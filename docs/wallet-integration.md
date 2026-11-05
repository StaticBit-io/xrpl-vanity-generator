# Add a chosen-address search to the wallet

This guide is for the developers of the StaticBit wallet. It shows how to add a search for a
wallet address that contains text the user picks, using the CPU search of this repository, on
Android and iOS. It assumes you know the wallet and have not seen this repository, and it
describes both code bases as they were on 2026-11-05.

## What you get

`StaticBit.XrplVanity.Core` 0.1.0 is a managed C# library in the namespace `XrplVanity.Core`.
It generates Ed25519 family seeds and keeps the first one whose classic address matches a
pattern, for example one with the text `Abc` right after the leading `r` that every classic
address has. It has no GPU code, and on a phone it runs on the CPU only.

The package is not published yet. It becomes available on the `github-staticbit` feed once the
repository's owner publishes it, and the steps below cannot be followed until then. To build it
locally, run `dotnet pack src/XrplVanity.Core -c Release -o artifacts` on `dev`; `artifacts/` is
ignored by git, so a clone does not hold it.

## Add the package

1. In the wallet's `Directory.Packages.props`, raise `BouncyCastle.Cryptography` from 2.6.1 to
   2.7.0, which the package needs, and add the package:

   ```xml
   <PackageVersion Include="BouncyCastle.Cryptography" Version="2.7.0" />
   <PackageVersion Include="StaticBit.XrplVanity.Core" Version="0.1.0" />
   ```

   The bump also applies to `SB.WalletApp.Security`, the other project that references
   BouncyCastle, so run its tests.

2. In `SB.WalletApp.Application.csproj`, reference the package. The Application project is where
   it goes, because the pages and services that use it live in `net10.0` projects that cannot
   see MAUI:

   ```xml
   <PackageReference Include="StaticBit.XrplVanity.Core" />
   ```

The feed `github-staticbit` is already configured in the wallet's `src/nuget.config`. Its
credentials come from the `GH_PACKAGES_TOKEN` environment variable.

## Run a search

The class below is a page of the wallet. It builds a `PatternSet`, refuses text that cannot
appear in an address, measures the rate of the device for 3 seconds on the same number of threads
as the search, predicts the time, warns about a long search, runs the search, then checks and stores the seed
that comes back. The guards from [Rules for a search on a phone](#rules-for-a-search-on-a-phone)
are already in it.

It takes these members as given: `PageBase<T>` with `_cts` and `appNavigation`, the platform
interface from [The platform seam](#the-platform-seam), `ConfirmLongSearchAsync`, which is your
dialog with the warning and its link, and `KeepAsync`, which is in [Check the seed before
showing it](#check-the-seed-before-showing-it). In the wallet the class derives from
`PageBase<object>`; `_cts` is the source that `PageBase.DisposeAsync` cancels. The wallet's own
`using` lines for `AppEventNotifier` (`SB.WalletApp.Application.AppEvents`) and
`AppLifecycleChangedArgs` (`SB.WalletApp.Application.AppEvents.Lifecycle`) are left out.

```csharp
using Microsoft.AspNetCore.Components;
using XrplVanity.Core;

public partial class VanitySearch : PageBase<object>
{
    private static readonly TimeSpan LongSearch = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private const int MinBatteryPercent = 20;

    [Inject]
    private IVanitySearchPlatform _platform { get; set; } = default!;

    private bool _running;
    private CancellationTokenSource? _search;
    private string? _stopReason;
    private string _status = "";
    private long _candidates;

    protected override void OnInitialized() =>
        AppEventNotifier.OnAppLifecycleChanged += HandleLifecycleChangedAsync;

    public override ValueTask DisposeAsync()
    {
        AppEventNotifier.OnAppLifecycleChanged -= HandleLifecycleChangedAsync;
        return base.DisposeAsync();
    }

    private async Task StartAsync(string prefix)
    {
        // A direct URL opens this page on a client where the feature is off, such as the web client.
        if (!_platform.IsAvailable())
        {
            _status = "This is not available on this device.";
            return;
        }

        // A second tap while a search runs must not start another: it would overwrite _search,
        // and Stop would then reach only the new one.
        if (_running)
        {
            return;
        }

        _running = true;
        try
        {
            await RunSearchAsync(prefix);
        }
        finally
        {
            _running = false;
        }
    }

    private async Task RunSearchAsync(string prefix)
    {
        // PatternSet matches the part of the address after its fixed leading 'r'.
        if (prefix.StartsWith('r'))
        {
            _status = "Type the text without the leading r: every address already starts with it.";
            return;
        }

        PatternSet patterns = new PatternSet.Builder()
            .Add(MatchPosition.Starts, prefix, ignoreCase: true)
            .Build();
        if (prefix.Length > PatternSet.MaxBodyLength || patterns.Unreachable.Count > 0 || !patterns.HasPatterns)
        {
            _status = "This text cannot appear in an address.";
            return;
        }

        // After a failed save the page holds the only copy of the seed.
        if (_found is null && _seedShown is not null
            && !await notificationService.Confirm("Attention!", "The last key was not saved. Search again and lose it?"))
        {
            return;
        }

        int threads = Math.Max(1, Environment.ProcessorCount - 1);
        _stopReason = null;
        _candidates = 0;

        // A new search must not leave the previous seed on screen. The key itself stays in the history.
        _found = null;
        _seedShown = null;

        // Linked to the page's source, so leaving the page cancels the search.
        using CancellationTokenSource search = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _search = search;
        Task watch = Task.CompletedTask;
        CpuSearchOutcome outcome = default;
        try
        {
            _status = "Measuring the speed of this device.";
            StateHasChanged();
            double rate = await CpuSearch.MeasureRateAsync(threads, TimeSpan.FromSeconds(3), search.Token);
            if (search.IsCancellationRequested)
            {
                _status = _stopReason ?? "Search stopped.";
                StateHasChanged();
                return;
            }

            double? candidatesPerMatch = SearchEstimator.ExpectedCandidatesPerMatch(patterns);
            TimeSpan? expected = SearchEstimator.ExpectedTimeToMatch(candidatesPerMatch, rate);

            // null for a rate of 0 or an unreachable set; TimeSpan.MaxValue when the
            // division overflows a TimeSpan. Both must be handled before showing a time.
            string estimate = "";
            if (expected is TimeSpan wait)
            {
                estimate = $"About {Describe(wait)} on this device. It can take three times longer. ";
                _status = estimate;
                StateHasChanged();
                if (wait > LongSearch && !await ConfirmLongSearchAsync(Describe(wait)))
                {
                    _status = "";
                    return;
                }
            }

            _status = estimate + "Keep the app open while it searches.";
            StateHasChanged();

            // Only the search holds the screen: not the measure, and not the warning dialog,
            // which the user may leave open. The watcher starts together with it.
            _platform.SetKeepScreenOn(true);
            watch = WatchDeviceAsync(search);

            // Progress<long> created here, on the UI thread, posts back to it. The callback
            // goes through InvokeAsync as well, so it is safe even where no context was captured.
            Progress<long> progress = new(count =>
                _ = InvokeAsync(() =>
                {
                    _candidates = count;
                    StateHasChanged();
                }));

            outcome = await CpuSearch.FindFirstAsync(patterns, threads, progress, search.Token);
        }
        finally
        {
            // Every exit gets here, a found seed included, so the screen can sleep again.
            // Turning it off when it was never turned on is harmless.
            search.Cancel();
            await watch;
            _search = null;
            _platform.SetKeepScreenOn(false);
        }

        if (outcome.Found is Match match)
        {
            // Match.ToString leaves the seed out, but do not log its fields one by one.
            _status = await KeepAsync(match);
        }
        else
        {
            _status = _stopReason ?? "Search stopped.";
        }

        StateHasChanged();
    }

    private void OnStopClicked() => Stop("Search stopped.");

    private void Stop(string reason)
    {
        Interlocked.CompareExchange(ref _stopReason, reason, null);
        try
        {
            _search?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The search ended between the read and the call; there is nothing left to stop.
        }
    }

    private Task HandleLifecycleChangedAsync(object sender, AppLifecycleChangedArgs args)
    {
        if (args.IsInBackground)
        {
            Stop("Search stopped because the app left the screen.");
        }

        return Task.CompletedTask;
    }

    private async Task WatchDeviceAsync(CancellationTokenSource search)
    {
        using PeriodicTimer timer = new(CheckInterval);
        try
        {
            do
            {
                DeviceCondition condition = _platform.ReadCondition();
                if (condition.IsHot)
                {
                    Stop("Search stopped because the device got too hot.");
                }
                else if (condition.BatteryPercent is int percent && percent < MinBatteryPercent && !condition.IsCharging)
                {
                    Stop("Search stopped because the battery is below 20%.");
                }
            }
            while (await timer.WaitForNextTickAsync(search.Token));
        }
        catch (OperationCanceledException)
        {
            // The search ended; this is how the loop is told to stop.
        }
    }

    private static string Describe(TimeSpan time)
    {
        if (time == TimeSpan.MaxValue || time.TotalDays >= 365)
        {
            return "a year or more";
        }

        if (time.TotalHours >= 1)
        {
            return Count((int)Math.Round(time.TotalHours), "hour");
        }

        return Count(Math.Max(1, (int)Math.Round(time.TotalMinutes)), "minute");
    }

    private static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
```

Notes on the sample:

- `FindFirstAsync` and `MeasureRateAsync` throw `ArgumentOutOfRangeException` for `threads` below
  1 and for a duration that is not positive, and `FindFirstAsync` throws `ArgumentException` for
  a set with no pattern. They check these before they look at the token. `Math.Max(1, ...)` keeps
  `threads` valid on a single core.
- Cancelling the token is an outcome, not an exception: `Found` is `null`.
- Every classic address starts with `r`, and `PatternSet` matches the part after it. The text the
  user types therefore goes in without the `r`: passing `rAbc` would search for addresses that
  read `rrAbc`. The sample refuses text that starts with a lowercase `r`, so that this cannot
  happen silently. The price is that a user cannot ask for an address whose second character is
  a lowercase `r`, which is about 1 address in 1,350 anyway.
- `StartAsync` returns at once while a search is active, so a second tap cannot start another
  one. Also disable the Start button while `_running` is true, so the user sees that it is
  busy.
- Text longer than 12 characters is matched exactly as typed: `PatternSet.CaseFoldingSkipped` names
  it, so tell the user. Text longer than `PatternSet.MaxBodyLength` can never match, and the
  sample refuses it.
- The builder folds the look-alike characters `0`, `O`, `I` and `l` onto the letters that base58
  has. Any other character outside the alphabet, such as `_` or a space, lands in `Unreachable`,
  and so does an empty pattern.
- `ExpectedTimeToMatch` returns `null` for a rate of `0` or for an unreachable set, and can
  return `TimeSpan.MaxValue`, so the display must handle both. The sample shows no time for
  `null` and "a year or more" for the other.
- The `Match` returned by a find holds the seed. `ToString` leaves the seed out, but never log it
  field by field, and never log the `AddWalletCommand` built from it. The seed is the wallet's
  key.
- Progress reaches your callback on an arbitrary thread, not the caller's. Use a `Progress<long>`
  created on the UI thread, or go through `InvokeAsync`. Do not pass a custom `IProgress<long>`
  that touches a component directly. A Blazor page refreshes through
  `InvokeAsync(StateHasChanged)`.
- The `search` source is private to the call. `CpuSearch` only observes the token it is given and
  never cancels it.

## Where it fits in the wallet

A user adds a wallet from `Pages/Settings/Wallets/New.razor`, under
`src/[UI]/SB.WalletApp.UI.App`, and `Add.razor.cs` next to it is the pattern to follow. The path
for a chosen address:

1. Add an entry to `New.razor` with the text "Create with a chosen address", shown only when the
   platform check from [Rules for a search on a phone](#rules-for-a-search-on-a-phone) says the
   feature is available.
2. Add a page next to `Add.razor` for the text field, the estimate and the search. The sample
   above is its code.
3. Create the cancellation source with `CancellationTokenSource.CreateLinkedTokenSource(_cts.Token)`,
   as the sample does, not with `_cancellationToken`. `PageBase._cancellationToken` is declared and
   never assigned (`PageBase.cs`, line 44), so it never cancels. `PageBase.DisposeAsync` cancels
   and disposes `_cts`.
4. When a seed passes the check in the next section, it is stored at once. Show the address, and
   show the seed only on a tap, in a read-only text box with a copy button, as `Add.razor` does.
5. On Next, show the same "Attention!" confirmation as `Add.razor.cs`. If the user declines, do
   nothing.
6. Fill `AddWalletCommand` the same way, store it under `SessionStorageKeys.AddNewWallet`, and
   navigate to `settings/wallets/wizard` with no query parameter. See the code in the next
   section.
7. Back from the wizard recreates the page, and nothing is lost: the key stays in the history
   described in [Keep what the search finds](#keep-what-the-search-finds), and the user can make
   it a wallet from there.

Do not pass an algorithm parameter to the wizard. `Add.razor.cs` passes `showAlghoritm=true`, but
`Wizard.razor.cs` reads `showAlgorithm`, so the algorithm drop-down is hidden today, and it must
stay hidden for a chosen address: changing the algorithm re-derives the seed into a different
address.

## Check the seed before showing it

A found seed is a secret and the address is only worth having if the wallet derives that same
address from it. Call `IXrplClientService.WalletFromSeed(seed, null, null)`, which derives by the
seed's prefix and returns the algorithm it used. Compare `ClassicAddress` with `match.Address`,
and take `Algorithm` from the returned model. Never show a seed whose address differs.

This is the rest of the class: the check, the write to the history, and the save path of
`Add.razor.cs`. Seed and command are built only when they are needed, so the page does not hold a
secret it is not showing.

```csharp
using Microsoft.AspNetCore.Components;
using XrplVanity.Core;

public partial class VanitySearch
{
    [Inject]
    private IXrplClientService _xrpl { get; set; } = default!;

    [Inject]
    private IFoundKeyStore _foundKeys { get; set; } = default!;

    [Inject]
    private ISessionStorageService _sessionStorageService { get; set; } = default!;

    private FoundKey? _found;
    private string? _seedShown;

    private async Task<string> KeepAsync(Match match)
    {
        XrplWalletModel wallet;
        try
        {
            wallet = await _xrpl.WalletFromSeed(match.Seed, null, null);
        }
        catch (Exception)
        {
            // A seed that cannot be checked is never shown.
            return "The seed could not be checked and was discarded. Search again.";
        }

        if (!string.Equals(wallet.ClassicAddress, match.Address, StringComparison.Ordinal))
        {
            return "The seed did not match its address and was discarded. Search again.";
        }

        try
        {
            // Not the page's token: leaving the page must not abort this write.
            _found = await _foundKeys.AddAsync(match, CancellationToken.None);
            return "";
        }
        catch (Exception)
        {
            // Whatever failed, the seed was checked and the page is now the only sure copy.
            // Do not log the exception's text: it may quote what was being written.
            _seedShown = match.Seed;
            return "This key could not be saved. Copy the seed now: it is lost when you leave this page.";
        }
    }

    private async Task OnShowSeedClickAsync()
    {
        if (_found is not null)
        {
            _seedShown = await _foundKeys.RevealSeedAsync(_found, CancellationToken.None);
        }
    }

    private async Task OnNextClickAsync()
    {
        // After a failed save there is no key to add. Hide Next while _found is null.
        if (_found is null)
        {
            return;
        }

        bool confirmed = await notificationService.Confirm(
            "Attention!",
            "Make sure you save your seed, otherwise you won't be able to restore access to your account.");
        if (!confirmed)
        {
            return;
        }

        string seed = await _foundKeys.RevealSeedAsync(_found, CancellationToken.None);
        XrplWalletModel wallet = await _xrpl.WalletFromSeed(seed, null, null);
        AddWalletCommand command = new()
        {
            ClassicAddress = wallet.ClassicAddress,
            KeyValue = seed,
            KeyType = AccountKeyType.Seed,
            Algorithm = wallet.Algorithm,
            DerivationPath = wallet.DerivationPath,
        };

        await _sessionStorageService.SetAsync(SessionStorageKeys.AddNewWallet, command);
        appNavigation.GoTo("settings/wallets/wizard", command);
    }
}
```

Guard `OnNextClickAsync` with a `ClickGuard`, as `Add.razor.cs` does.

## Keep what the search finds

A found key is stored the moment it passes the check, in a history of its own, as `results.dat`
is on the desktop. A search can run for a long time on a phone nobody is holding, so a seed that
lived only on the page would be lost when the auto-lock sends the app to the sign-in page. A found
key is not a wallet until the user makes it one, because a search often gives several candidates
and only one of them is wanted.

The secret and the record live apart:

- **The seed goes in the vault.** Call `ISecretVault.StoreAsync(new WalletSecret(seed, null), …)`,
  which returns a reference. The vault is unlocked during a search, which only runs signed in and
  on screen.
- **The record goes in plain storage,** through `IKeyValueStorageService`: the address, the
  pattern, its position, the time and the reference. It holds no secret.

Put this in the Application project, and implement it there:

```csharp
using XrplVanity.Core;

public sealed record FoundKey(string Address, string Pattern, string Position, DateTimeOffset FoundAt, string SecretRef);

public interface IFoundKeyStore
{
    /// <summary>Stores the seed in the vault, then the record, in that order.</summary>
    /// <exception cref="InvalidOperationException">The vault is locked; nothing was stored.</exception>
    Task<FoundKey> AddAsync(Match match, CancellationToken cancellationToken);

    Task<IReadOnlyList<FoundKey>> GetAllAsync();

    /// <exception cref="KeyNotFoundException">The vault no longer holds this entry.</exception>
    Task<string> RevealSeedAsync(FoundKey key, CancellationToken cancellationToken);

    Task DeleteAsync(FoundKey key, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);

    /// <summary>The references the history owns, or null when the history cannot be trusted.</summary>
    Task<IReadOnlySet<string>?> TryGetLiveReferencesAsync();
}
```

`AddAsync` stores the seed first. If the app dies or the record write fails between the two, the
result is an entry nobody references, and the sweep below collects it. The other order would leave
a record that points at nothing. When `AddAsync` throws for any reason, the sample keeps the seed
on the page and asks the user to copy it.

### The vault deletes entries no wallet references

**Storing a found key in the vault is not enough.** `VaultMigrationService.CollectOrphansAsync`
runs once per session and deletes every entry that no wallet row references and that was not
written in the current session. A found key that is not a wallet is exactly such an entry. It
works in testing and disappears on the next launch, with no error.

Change the sweep so that the history counts as an owner of its references:

1. Add `IFoundKeyStore` to `VaultMigrationService`.
2. After the two existing guards, call `TryGetLiveReferencesAsync`. If it returns `null`, log it
   and skip the whole run, as the guards do.
3. Add the references it returns to `liveReferences`.

Keep the existing guards as they are, and keep them first: they look at wallets alone.

**An empty history proves nothing on its own.** The sweep's comments describe a corrupted read that
returns "loaded successfully, empty" instead of throwing. For wallets the guard is to skip on an
empty list, but an empty history is also an ordinary state, so that guard cannot work here. Use a
count instead:

1. Keep the number of records in a separate storage key, and write it on every change.
2. Write that key as 0 in a migration under `Migrations/`, before any found key can exist, and
   check that it runs before the first sweep. Every install then has the key.
3. Have `TryGetLiveReferencesAsync` return `null` when the read throws, when the list's length
   disagrees with the count, and **when the key is missing**.

A missing key must mean lost, not zero. A store that comes back empty has lost the count along with
the list, and reading a missing count as 0 would make that empty list look valid and delete every
found key. The two writes are not atomic, so a crash between them leaves a mismatch, and sweeps stay
skipped until the next change to the history. That is the cheap way to fail: an orphaned entry is
inert, and a found key is in no backup.

This is code that protects keys, so review it as such, and add tests: a found key survives a sweep
in a later session; a history that cannot be read skips the sweep; a list that disagrees with the
count skips the sweep; a store that lost both the list and the count skips the sweep.

### The history screen

Follow the desktop's History, described in [docs/desktop-app.md](desktop-app.md):

- A list of found keys, newest first, showing the address, the pattern and the time, with a filter
  by pattern.
- Delete and Clear, each with a confirmation. Both remove the vault entries as well as the
  records.
- Show the seed only on a tap, through `RevealSeedAsync` with a short reason such as `found-key`.
  If it throws `KeyNotFoundException`, say the key is gone and offer to delete the record.
- A way to make a key a wallet, which is the Next path in the sample. `AddAsync` of the wallet
  writes its own vault entry, so the record can stay; mark it through
  `IWalletsService.GetByClassicAddressAsync` rather than deleting it.
- A line that says the keys are only on this phone: see [Suggested interface
  text](#suggested-interface-text). **Found keys are in no backup until they are wallets,** because
  the backup reads wallets, and they do not survive removing the app.

Clear the history wherever the vault itself is recreated (`ISecretVault.CreateAsync` with
`allowOverwrite`): its references then point at nothing. No single reset method turned up by name
in the wallet, so check every path that replaces the vault.

Keep the texts a user searched for separately, as the desktop keeps its pattern drafts in settings.
They are not secrets and need only ordinary storage.

## The platform seam

The pages are plain `net10.0`, so they cannot call `DeviceDisplay`, `PowerManager` or
`NSProcessInfo`. Follow the pattern of `IDeviceInfoService`: an interface in the Application
project, an implementation in `SB.MobileApp`, and a null implementation for every other client.
Register them where `IDeviceInfoService` is registered, in `MobileAppServiceExtensions.cs` and
`WebAppServiceExtensions.cs`. The interface answers three things: the battery level and whether
the device is charging, whether it is hot, and keeping the screen on.

```csharp
public interface IVanitySearchPlatform
{
    /// <summary>One check for the entry button and the page.</summary>
    bool IsAvailable();

    /// <summary>Must not throw; report an unknown battery as null.</summary>
    DeviceCondition ReadCondition();

    /// <summary>Safe to call from any thread.</summary>
    void SetKeepScreenOn(bool keepOn);
}

public readonly record struct DeviceCondition(int? BatteryPercent, bool IsCharging, bool IsHot);

public sealed class NullVanitySearchPlatform : IVanitySearchPlatform
{
    public bool IsAvailable() => false;

    public DeviceCondition ReadCondition() => new(null, false, false);

    public void SetKeepScreenOn(bool keepOn)
    {
    }
}
```

Implement the interface in `SB.MobileApp` with these platform calls:

| What | Android | iOS |
| --- | --- | --- |
| Battery level and charging | The sticky `ACTION_BATTERY_CHANGED` broadcast: `RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged))`, then the level and plugged extras. | `Battery.Default`: `ChargeLevel` and `State` |
| Heat | `PowerManager.CurrentThermalStatus >= ThermalStatus.Severe` | `NSProcessInfo.ProcessInfo.ThermalState >= NSProcessInfoThermalState.Serious` |
| Keep the screen on | `DeviceDisplay.Current.KeepScreenOn`, set on the main thread | the same |

- Do not use the MAUI `Battery` API on Android. It asks for the `BATTERY_STATS` permission, which
  the wallet's manifest does not declare. The sticky broadcast needs no permission and no
  receiver. `ReadBattery` in
  [`tools/XrplVanity.MobileBench/MainActivity.cs`](../tools/XrplVanity.MobileBench/MainActivity.cs)
  reads it, and it ran on the test phone. That method reads the level and the temperature; the
  plugged state is the extra `BatteryManager.ExtraPlugged`, and a non-zero value means the
  device is on a charger.
- `DeviceDisplay.Current.KeepScreenOn` changes UI state, so set it on the main thread: wrap the
  call in `MainThread.BeginInvokeOnMainThread` unless you know the caller is already on it.
- What was checked: the Android calls above compile against .NET 10 for Android, and the
  battery broadcast ran on the test phone. The thermal status was never read on a device, and the
  iOS calls were never built, because no iOS build exists.

## Rules for a search on a phone

- **Where it is available.** Offer the feature on Android 10 (API 29) and later, and on iOS,
  through one `IsAvailable` check that the entry button and the page both use; the sample's
  `StartAsync` makes it first, because a direct URL opens the page on the web client too. The
  check excludes the web client; Windows, which has the desktop app; and Android below 10, which
  has no thermal status to stop on. The wallet itself supports Android 7 (API 24) and iOS 15, so
  the check is what keeps the feature off the older Android versions. It also makes hiding the
  feature on one platform a one-line change. In `SB.MobileApp` it is one expression:
  `OperatingSystem.IsAndroidVersionAtLeast(29) || OperatingSystem.IsIOS()`. It is false on
  Windows and in the browser, and the null implementation returns `false` for every other
  client.
- **Only while the app is on screen.** Subscribe to `AppEventNotifier.OnAppLifecycleChanged`,
  stop when `args.IsInBackground` is true, and stop when the page goes away. The sample does
  both, and `Swap.razor.cs` shows another page subscribing. `IAppLifecycleState.IsInBackground`
  gives the current value.
- **No foreground service and no partial wake lock.** The search runs only while the app is on
  screen and needs neither.
- **Threads.** Use `Math.Max(1, Environment.ProcessorCount - 1)`. One core stays free for the
  interface, because of the ANR thresholds in the Android vitals rule of the store rules below.
- **The screen.** Keep it on only while searching, and release it on every exit, the moment a
  seed is found included, so a found seed does not stay lit on an unattended phone. The
  sample's `finally` does this before the seed is shown.
- **Battery and heat.** Check about every 10 seconds. Stop when the battery is below 20% and the
  device is not charging, and stop on heat.
- **No time limit.** A search runs until it finds a match or the user or a guard stops it. A
  stopped search loses nothing, because every try is independent and a new run has the same odds.
  A running search needs no pause, resume or saved state.
- **A found seed is stored at once.** `KeepAsync` writes it to the vault as soon as it passes the
  check, so the auto-lock cannot lose it. The auto-lock is checked only in `App.OnResume`; the
  timer is set in `OnSleep`, the default is 60 seconds, and the choices run from 0 to 1,800. An app
  on screen never locks. If the time has passed on resume, the app goes to the sign-in page and the
  page state is gone, but the key stays in the history. Only a find whose write failed exists on
  the page alone.

## Warn about long searches

When the expected time is above 30 minutes, show the warning from the table below and still let
the user start. The warning offers the desktop app and links to
`https://xrpl-vanity.staticbit.io/desktop/`. The 30 minutes is a suggested trigger for the
message. It blocks nothing, and nothing ends a search for taking long.

Rates differ too much between phones for a constant, so the wallet measures the rate on the
device before it predicts, as the sample does. The estimate is an average: the real time is
random and can be several times longer or shorter.

## Suggested interface text

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
| Found | Found and saved in your search history. |
| Reveal button | Show seed |
| Make a wallet | Add as a wallet |
| Could not save | This key could not be saved. Copy the seed now: it is lost when you leave this page. |
| Searching again after that | The last key was not saved. Search again and lose it? |
| History note | These keys are only on this phone. They are not in your backups until you add them as wallets. |

## Store rules and review notes

These rules were read on 2026-11-05 from the [App Store Review
Guidelines](https://developer.apple.com/app-store/review/guidelines/), the Google Play policy
pages and the Android developer documentation. Check them again before you submit.

- Neither store forbids this search, and neither defines "mining". Apple bans mining on the
  device (3.1.5(ii)), and Google bans it in its [blockchain content
  policy](https://support.google.com/googleplay/android-developer/answer/13607354). The outcome
  rests with the reviewer.
- Apple 2.4.2 rules out rapid battery drain, excessive heat and unnecessary strain, with no
  numbers, and it applies in the foreground too. This is the main risk. With no time limit on a
  search, the battery and heat stops are what answers it.
- Apple 2.5.4 limits background services to their intended purposes. Stopping when the app
  leaves the screen keeps the feature outside it.
- Google Play [forbids bypassing power
  management](https://support.google.com/googleplay/android-developer/answer/9888379) and sets
  no CPU or battery limit. [Foreground services need a declaration with a
  video](https://support.google.com/googleplay/android-developer/answer/13392821), and
  `dataSync` is [limited to 6 hours a day](https://developer.android.com/develop/background-work/services/fgs/timeout)
  for apps targeting Android 15. A search that stays on screen needs none of this.
- Android vitals [counts partial wake
  locks](https://developer.android.com/topic/performance/vitals/excessive-wakelock) only in the
  background or under a foreground service. The [ANR
  thresholds](https://developer.android.com/google/play/vitals#core-vitals-faqs) are 0.47%
  overall and 8% per phone model, which is why one core stays free.

The wallet already has a foreground service: `OnSleep` starts its own keep-alive service, and
the manifest declares `FOREGROUND_SERVICE_DATA_SYNC`. The search does not use it. Do not write
store text that implies the app has no foreground service; say that the search itself uses none.

Notes for the reviewers. They describe the feature as what it is: local key generation.

**App Review.** The feature generates a wallet key locally, with address text the user chose.
The user starts it and can stop it at any time. It runs only while the app is on screen, on all
cores but one, and stops when the app leaves the screen, when the battery is below 20% and not
charging, and when the device gets hot. The search itself uses no foreground service and does
not run in the background. Nothing is sent anywhere, and no reward, token or pooled work is involved: the
only result is a key for the user's own wallet.

**Google Play.** The feature generates a wallet key locally, with address text the user chose.
The user starts it and can stop it at any time. It runs only while the app is on screen, on all
cores but one, and stops on low battery and on heat. The search itself uses no foreground
service and no partial wake lock, and it does not bypass power management; it only keeps the
screen on while it runs. Nothing is sent anywhere, and no reward or pooled work is involved.

## Expected speed

On a low-end phone (MediaTek MT6765, 8 x Cortex-A53), on 8 threads, the wallet's release
settings reach about 8,900 candidates per second. LLVM AOT reaches about 20,800 per second. Ten
minutes with the platform hashes, which ran at 7,761 per second, showed no throttling, and the
charge fell by 2 points. This guide recommends one
thread fewer, so expect slightly less than these rates. The table is the expected time at 8,900
per second:

| Case-insensitive prefix | Expected time at 8,900/s |
| --- | --- |
| 4 characters | about 1 minute |
| 5 characters | about 31 minutes |
| 6 characters | about 15 hours |

The table is the best case. It assumes the first character after the `r` is one of the common
ones. Text that starts with `c`, `i`, `o`, `R`, `t`, `v`, `x`, `y` or `z`, or with one of the digits
`0`, `1`, `2`, `5`, `6`, `7` and `8`, costs far more, and `q` about three times as much: `xrpl` takes about 62 minutes at 8,900/s,
where `abcd` takes about 1, and `1234` takes about 8 hours. The estimate that the sample shows
accounts for this, because it is computed from the pattern.

The measurements, the build settings and the other runtimes are in the [Android
phone](benchmarks.md#android-phone) section of `benchmarks.md`.

To measure another phone, build and install `tools/XrplVanity.MobileBench`, which is not part of
`XrplVanity.sln`. The steps are in the Android phone part of the [Reproduce
section](benchmarks.md#reproduce). In short:

```bash
dotnet build tools/XrplVanity.MobileBench -c Release
adb install -r tools/XrplVanity.MobileBench/bin/Release/net10.0-android/io.staticbit.xrplvanity.bench-Signed.apk
```

Open the app on the phone. A run starts by itself, lasts about 6 minutes on 8 cores, and prints
one rate column. Read the results from the screen or from the file the Reproduce section names,
not from logcat.

## Check before release

A tester can follow this list on a current Android phone and on an iPhone.

- A four-character search ends in a saved wallet whose address contains the text, and that
  wallet signs a testnet transaction. Use text that starts with one of the common first
  characters, such as `abcd`: `xrpl` takes about an hour.
- Scrolling stays smooth during a search, and Stop answers within a second.
- The home button and the power button each stop the search. On a return before the auto-lock
  time, the page says why. On a return after it, the sign-in page appears, no seed is shown, and
  no threads run.
- Leaving the page stops the search and lets the screen sleep.
- After a find, the screen sleeps normally, and the seed is not on screen until Show seed is
  tapped.
- A search left alone finds a key, the phone sleeps, and after a return through the sign-in page
  the key is in the history.
- A key that is not a wallet is still in the history, with its seed, after two full restarts of
  the app. This is what proves the sweep leaves it alone.
- Deleting a history entry removes its seed: after the next restart the vault holds no entry for
  it. A key added as a wallet still signs after its history entry is deleted.
- A warned search can still be started, and the link opens the site. Following the link leaves
  the app, which stops the search, so the user starts it again on return.
- With the vault write failing, a find shows the seed and asks to copy it, and starting a new
  search asks before it clears the seed.
- A 30-minute search on a current phone stays at a comfortable temperature.

## Not known yet

- iOS has never been built or measured, and its release waits for that.
- Only one low-end Android phone is measured.
- LLVM (`-p:RunAOTCompilation=true -p:AndroidEnableProfiledAot=false -p:EnableLLVM=true`) gives
  about 2.3 times the rate of the release settings with the same digests, but its cost on the
  wallet is unmeasured.
- The reviewers' decision cannot be known in advance.
- `IFoundKeyStore` and the sweep change are a design here, not code that ran: the sample compiles
  against stubs, and nothing has been run against the real vault.
- The desktop app is not in the Microsoft Store yet. The link in the warning leads to a page
  that says it is on its way and links to a build from source, with no installer. Wait for the
  listing before release, or accept that.
