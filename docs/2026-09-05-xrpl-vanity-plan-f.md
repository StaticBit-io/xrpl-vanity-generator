# XRPL Vanity — Plan F: Shared Search Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the CLI's search orchestration into a `XrplVanity.Search` library with a `SearchSession` the CLI and the desktop application both drive, keeping the CLI's contract intact.

**Architecture:** A new `net10.0` class library `src/XrplVanity.Search` owns `MatchSink` (moved from the CLI, writers replaced by a callback), a CPU worker pool with a candidate counter, `SearchSession` (one thread per target, a 250 ms progress ticker, a `Completion` task), `SearchEstimator` and `DeviceCatalog`. `XrplVanity.Gpu` gains a settable `MultiDeviceSearch.DutyPercent` and a `DeviceSelector.FirstAvailable` sentinel so the CLI's `--gpu` semantics run through the same multi-device loop. `Program.Main` becomes a testable `Program.Run(args, stdout, stderr)` that builds a session from `CliOptions` and renders its events.

**Tech Stack:** .NET 10, C# latest, xUnit v3 4.0.0 (run as an executable with `dotnet run`), existing `XrplVanity.Core` / `XrplVanity.Gpu`.

**Spec:** `docs/2026-09-05-xrpl-vanity-desktop-design.md` — section "Plan F — the search layer".

## Global Constraints

- Every project targets `net10.0`; `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<LangVersion>latest</LangVersion>` as in the existing csproj files.
- Tests run with `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*<ClassName>"`; never `dotnet test`. The full suite must stay green on this machine (both `cuda` and `opencl` available; expect `Skipped: 0`).
- GPU-dependent tests open devices through `GpuTestHelper.RequireDevice(backend)` / `GpuBackends.TryOpen` and skip via `GpuTestHelper.SkipOrFail` — never fail because hardware is absent.
- CLI contract that must not change: stdout lines `Address: {address}\tSeed: {seed}`; `--out` lines `{address}\t{seed}`; exit codes 0 (count satisfied) / 2 (usage, no device, no reachable pattern) / 130 (stopped before the count); every `warning:` / `error:` message text in `Program.cs` today; `--list-devices` output. The one intended addition is the line `CPU: searched {N:N0} candidates in {T:F1} s ({R:N0}/s).` on stderr at the end of a CPU run.
- No secrets in logs or tests: never print a seed outside the `Address: …\tSeed: …` line.
- Commit messages: conventional commits, English, no attribution trailers of any kind.
- Coding style: explicit types (no `var`), `_camelCase` private fields, comments in English only where they explain a non-obvious decision.

---

## File structure

| Path | Responsibility |
| --- | --- |
| `src/XrplVanity.Search/XrplVanity.Search.csproj` | New library; references Core and Gpu; `InternalsVisibleTo XrplVanity.Tests` |
| `src/XrplVanity.Search/MatchSink.cs` | Moved from the CLI; `(int? limit, Action<Match> report)`; exact count under a lock |
| `src/XrplVanity.Search/SearchTarget.cs` | `SearchTarget` hierarchy and `SearchRequest` with validation |
| `src/XrplVanity.Search/SearchProgress.cs` | `TargetState`, `TargetProgress`, `SearchProgress`, `SessionOutcome`, `SearchSummary` |
| `src/XrplVanity.Search/CpuSearchWorkers.cs` | The CPU worker pool moved from `Program.RunCpu`, with a candidate counter |
| `src/XrplVanity.Search/SearchSession.cs` | Session: threads per target, progress ticker, completion |
| `src/XrplVanity.Search/SearchEstimator.cs` | Expected candidates per match and ETA |
| `src/XrplVanity.Search/DeviceCatalog.cs` | Device list for front ends |
| `src/XrplVanity.Gpu/MultiDeviceSearch.cs` | `DutyPercent` settable; `DeviceSelector.FirstAvailable` resolution |
| `src/XrplVanity.Gpu/GpuBackends.cs` | `TryOpenFirst` overload that also returns the resolved selector |
| `src/XrplVanity.Cli/CliMatchWriter.cs` | stdout + `--out` writer used as the session's report callback |
| `src/XrplVanity.Cli/Program.cs` | `Run(args, stdout, stderr)`; session construction and rendering; `RunCpu/RunGpu/RunMultiGpu` deleted |
| `tests/XrplVanity.Tests/Search/*.cs` | New tests; `MatchSinkTests.cs` moves here |
| `XrplVanity.sln` | New project registered |

---

### Task 1: Create `XrplVanity.Search` and move `MatchSink` behind a callback

**Files:**
- Create: `src/XrplVanity.Search/XrplVanity.Search.csproj`
- Create: `src/XrplVanity.Search/MatchSink.cs`
- Create: `src/XrplVanity.Cli/CliMatchWriter.cs`
- Delete: `src/XrplVanity.Cli/MatchSink.cs`
- Modify: `src/XrplVanity.Cli/XrplVanity.Cli.csproj` (reference Search)
- Modify: `src/XrplVanity.Cli/Program.cs:294-345` (construct `CliMatchWriter` + `MatchSink`)
- Modify: `tests/XrplVanity.Tests/XrplVanity.Tests.csproj` (reference Search)
- Move + modify: `tests/XrplVanity.Tests/MatchSinkTests.cs` → `tests/XrplVanity.Tests/Search/MatchSinkTests.cs`
- Create: `tests/XrplVanity.Tests/CliMatchWriterTests.cs`
- Modify: `XrplVanity.sln`

**Interfaces:**
- Produces: `XrplVanity.Search.MatchSink(int? limit, Action<Match> report)` with `bool TryReport(Match)`, `bool CountSatisfied`, `long Reported`; `XrplVanity.Cli.CliMatchWriter(string? outFile, TextWriter stdout) : IDisposable` with `void Write(Match)`.

- [ ] **Step 1: Create the library project and register it**

`src/XrplVanity.Search/XrplVanity.Search.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\XrplVanity.Core\XrplVanity.Core.csproj" />
    <ProjectReference Include="..\XrplVanity.Gpu\XrplVanity.Gpu.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="XrplVanity.Tests" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

Run from the repository root:

```bash
dotnet sln XrplVanity.sln add src/XrplVanity.Search/XrplVanity.Search.csproj --solution-folder src
dotnet add src/XrplVanity.Cli/XrplVanity.Cli.csproj reference src/XrplVanity.Search/XrplVanity.Search.csproj
dotnet add tests/XrplVanity.Tests/XrplVanity.Tests.csproj reference src/XrplVanity.Search/XrplVanity.Search.csproj
```

- [ ] **Step 2: Write the failing tests**

`tests/XrplVanity.Tests/Search/MatchSinkTests.cs` (replaces the old file; note the namespace):

```csharp
using System.Collections.Generic;
using XrplVanity.Core;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class MatchSinkTests
{
    private static Match M(int i) => new($"rAddr{i}", $"sEdSeed{i}", "Addr", MatchPosition.Starts);

    [Fact]
    public void Reports_Until_The_Limit_Then_Refuses_Surplus()
    {
        List<Match> reported = new();
        MatchSink sink = new(limit: 2, reported.Add);

        Assert.True(sink.TryReport(M(1)));
        Assert.False(sink.CountSatisfied);
        Assert.False(sink.TryReport(M(2)));   // reaches the limit: reported, but the caller must stop
        Assert.True(sink.CountSatisfied);
        Assert.False(sink.TryReport(M(3)));   // surplus: not reported
        Assert.Equal(2, sink.Reported);
        Assert.Equal(new[] { "rAddr1", "rAddr2" }, reported.ConvertAll(m => m.Address));
    }

    [Fact]
    public void Without_A_Limit_It_Never_Refuses()
    {
        List<Match> reported = new();
        MatchSink sink = new(limit: null, reported.Add);

        for (int i = 0; i < 10; i++)
        {
            Assert.True(sink.TryReport(M(i)));
        }

        Assert.False(sink.CountSatisfied);
        Assert.Equal(10, sink.Reported);
        Assert.Equal(10, reported.Count);
    }

    [Fact]
    public void The_Report_Callback_Runs_Inside_The_Lock_So_Order_Equals_Acceptance_Order()
    {
        List<int> order = new();
        MatchSink sink = new(limit: null, m => order.Add(int.Parse(m.Address["rAddr".Length..])));
        System.Threading.Tasks.Parallel.For(0, 200, i => sink.TryReport(M(i)));

        Assert.Equal(200, sink.Reported);
        Assert.Equal(200, order.Count);   // no lost writes: the callback never ran concurrently
    }
}
```

`tests/XrplVanity.Tests/CliMatchWriterTests.cs`:

```csharp
using System.IO;
using XrplVanity.Cli;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CliMatchWriterTests
{
    private static Match M(int i) => new($"rAddr{i}", $"sEdSeed{i}", "Addr", MatchPosition.Starts);

    [Fact]
    public void Writes_The_Stdout_Line_And_Appends_Address_And_Seed_To_The_Out_File()
    {
        string path = Path.GetTempFileName();
        try
        {
            StringWriter stdout = new();
            using (CliMatchWriter writer = new(path, stdout))
            {
                writer.Write(M(7));
            }

            Assert.Equal("Address: rAddr7\tSeed: sEdSeed7", stdout.ToString().Trim());
            Assert.Equal("rAddr7\tsEdSeed7", File.ReadAllText(path).Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Without_An_Out_File_Only_Stdout_Is_Written()
    {
        StringWriter stdout = new();
        using CliMatchWriter writer = new(null, stdout);
        writer.Write(M(1));
        Assert.Contains("rAddr1", stdout.ToString());
    }
}
```

Delete `tests/XrplVanity.Tests/MatchSinkTests.cs` (the old one) and `src/XrplVanity.Cli/MatchSink.cs`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build XrplVanity.sln -c Release`
Expected: build errors — `XrplVanity.Search.MatchSink` and `CliMatchWriter` do not exist, `Program.cs` references the deleted constructor.

- [ ] **Step 4: Implement `MatchSink` in the library and `CliMatchWriter` in the CLI**

`src/XrplVanity.Search/MatchSink.cs`:

```csharp
using System;
using XrplVanity.Core;

namespace XrplVanity.Search;

/// <summary>
/// The single place matches pass through: enforces the optional count limit exactly and hands
/// every accepted match to <c>report</c> under one lock, so the callback never runs concurrently
/// and consumers see matches in acceptance order. Shared by every CPU worker and GPU engine.
/// </summary>
public sealed class MatchSink
{
    private readonly object _gate = new();
    private readonly int? _limit;
    private readonly Action<Match> _report;

    public MatchSink(int? limit, Action<Match> report)
    {
        if (limit is int l && l <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "The limit must be positive or null.");
        }

        _limit = limit;
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    public bool CountSatisfied { get; private set; }
    public long Reported { get; private set; }

    /// <summary>
    /// Reports the match unless the limit is already met. Returns false once the limit is
    /// reached (including on the call that reaches it) so the caller stops searching.
    /// </summary>
    public bool TryReport(Match match)
    {
        lock (_gate)
        {
            if (_limit is int already && Reported >= already)
            {
                return false;
            }

            _report(match);
            Reported++;
            if (_limit is int limit && Reported >= limit)
            {
                CountSatisfied = true;
                return false;
            }

            return true;
        }
    }
}
```

`src/XrplVanity.Cli/CliMatchWriter.cs`:

```csharp
using System;
using System.IO;
using XrplVanity.Core;

namespace XrplVanity.Cli;

/// <summary>Writes an accepted match to stdout and, when given, appends it to the --out file.</summary>
public sealed class CliMatchWriter : IDisposable
{
    private readonly TextWriter _stdout;
    private readonly TextWriter? _outFile;

    public CliMatchWriter(string? outFile, TextWriter stdout)
    {
        _stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
        _outFile = outFile is null ? null : new StreamWriter(outFile, append: true);
    }

    public void Write(Match match)
    {
        _stdout.WriteLine($"Address: {match.Address}\tSeed: {match.Seed}");
        _outFile?.WriteLine($"{match.Address}\t{match.Seed}");
        _outFile?.Flush();
    }

    public void Dispose()
    {
        _outFile?.Dispose();
    }
}
```

In `src/XrplVanity.Cli/Program.cs` add `using XrplVanity.Search;` and replace

```csharp
        using MatchSink sink = new(options.Count, options.OutFile, Console.Out);
```

with

```csharp
        using CliMatchWriter writer = new(options.OutFile, Console.Out);
        MatchSink sink = new(options.Count, writer.Write);
```

Everything else in `Program.cs` compiles unchanged (`sink.TryReport`, `sink.CountSatisfied`).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*MatchSinkTests" -class "*CliMatchWriterTests"`
Expected: 5 passed, 0 failed.

Run the full suite once: `dotnet run --project tests/XrplVanity.Tests -c Release`
Expected: everything green (the GPU CLI tests still use `SearchRunner`).

- [ ] **Step 6: Commit**

```bash
git add XrplVanity.sln src/XrplVanity.Search src/XrplVanity.Cli tests/XrplVanity.Tests
git commit -m "refactor(search): move MatchSink into XrplVanity.Search behind a report callback"
```

---

### Task 2: `MultiDeviceSearch.DutyPercent` settable at run time and `DeviceSelector.FirstAvailable`

**Files:**
- Modify: `src/XrplVanity.Gpu/MultiDeviceSearch.cs`
- Modify: `src/XrplVanity.Gpu/GpuBackends.cs`
- Test: `tests/XrplVanity.Tests/Gpu/MultiDeviceSearchTests.cs`, `tests/XrplVanity.Tests/Gpu/GpuBackendsTests.cs`

**Interfaces:**
- Produces: `DeviceSelector.FirstAvailable` (static, `Backend == "gpu"`, `Index == -1`, `IsFirstAvailable == true`, `ToString() == "gpu"`); `DeviceSearchStats.Selector` gets an internal setter and is replaced by the resolved selector once the device opens; `MultiDeviceSearch.DutyPercent { get; set; }` (1–100, `ArgumentOutOfRangeException` otherwise, read before every pause); `internal static bool GpuBackends.TryOpenFirst(IReadOnlyList<IGpuBackend> backends, out IGpuDevice? device, out DeviceSelector? selector, out string reason)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/XrplVanity.Tests/Gpu/GpuBackendsTests.cs` (it already has a `FakeBackend`; if its constructor differs, adapt the two lines that build backends):

```csharp
    [Fact]
    public void TryOpenFirst_Reports_The_Selector_Of_The_Device_It_Opened()
    {
        IGpuBackend[] backends = { new FakeBackend("cuda", deviceCount: 0), new FakeBackend("opencl", deviceCount: 2) };

        bool opened = GpuBackends.TryOpenFirst(backends, out IGpuDevice? device, out DeviceSelector? selector, out string reason);

        Assert.True(opened, reason);
        Assert.Equal(new DeviceSelector("opencl", 0), selector);
        device!.Dispose();
    }
```

Append to `tests/XrplVanity.Tests/Gpu/MultiDeviceSearchTests.cs`:

```csharp
    [Fact]
    public void FirstAvailable_Sentinel_Is_Distinct_And_Prints_As_Gpu()
    {
        Assert.True(DeviceSelector.FirstAvailable.IsFirstAvailable);
        Assert.False(new DeviceSelector("cuda", 0).IsFirstAvailable);
        Assert.Equal("gpu", DeviceSelector.FirstAvailable.ToString());
    }

    [Fact]
    public void Duty_Percent_Rejects_Values_Outside_1_To_100()
    {
        MultiDeviceSearch search = new(new[] { new DeviceSelector("cuda", 0) }, FrequentPattern(), _ => true);
        Assert.Equal(DutyCycle.Full, search.DutyPercent);
        Assert.Throws<ArgumentOutOfRangeException>(() => search.DutyPercent = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => search.DutyPercent = 101);
        search.DutyPercent = 50;
        Assert.Equal(50, search.DutyPercent);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Lowering_Duty_Mid_Run_Slows_The_Launch_Cadence(string backend)
    {
        RequireSelector(new DeviceSelector(backend, 0));
        // Never matches, so launches are uniform and no report callback fires.
        PatternSet never = new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();
        MultiDeviceSearch search = new(new[] { new DeviceSelector(backend, 0) }, never, _ => true);
        DeviceSearchStats stats = search.Devices[0];
        using CancellationTokenSource cts = new();

        Thread runner = new(() => search.Run(cts.Token)) { IsBackground = true };
        runner.Start();

        long Candidates() => stats.CandidatesSearched;   // a torn read of a long is not a concern on x64 (documented on Devices)
        static long Wait(Func<long> read, long above, TimeSpan deadline)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (read() <= above && sw.Elapsed < deadline) Thread.Sleep(20);
            return read();
        }

        // Warm-up: wait for the second launch to finish so timing excludes device open.
        Assert.True(Wait(Candidates, GpuSearchEngine.CandidatesPerLaunch, TimeSpan.FromSeconds(120)) > GpuSearchEngine.CandidatesPerLaunch, "device did not start");

        long start = Candidates();
        Stopwatch full = Stopwatch.StartNew();
        Wait(Candidates, start + 4L * GpuSearchEngine.CandidatesPerLaunch - 1, TimeSpan.FromSeconds(120));
        full.Stop();

        search.DutyPercent = 25;
        long afterSet = Wait(Candidates, Candidates(), TimeSpan.FromSeconds(120)); // let the current launch drain
        Stopwatch quarter = Stopwatch.StartNew();
        Wait(Candidates, afterSet + 4L * GpuSearchEngine.CandidatesPerLaunch - 1, TimeSpan.FromSeconds(120));
        quarter.Stop();

        cts.Cancel();
        Assert.True(runner.Join(TimeSpan.FromSeconds(120)), "worker did not stop");

        double ratio = quarter.Elapsed.TotalMilliseconds / Math.Max(full.Elapsed.TotalMilliseconds, 1);
        Assert.True(ratio >= 2.0 && ratio <= 8.0, $"full {full.ElapsedMilliseconds} ms, at 25% {quarter.ElapsedMilliseconds} ms, ratio {ratio:F2} (expected about 4x)");
    }

    [Fact]
    public void FirstAvailable_Resolves_To_A_Concrete_Selector_Or_Fails_Cleanly()
    {
        bool anyDevice = GpuBackends.All.Any(b => b.EnumerateDevices().Count > 0);
        MultiDeviceSearch search = new(new[] { DeviceSelector.FirstAvailable }, FrequentPattern(), _ => false);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(120));

        MultiDeviceSearchResult result = search.Run(cts.Token);

        DeviceSearchStats stats = result.Devices[0];
        if (anyDevice)
        {
            Assert.True(stats.Opened, stats.Failure);
            Assert.False(stats.Selector.IsFirstAvailable);
            Assert.Contains(stats.Selector.Backend, new[] { "cuda", "opencl" });
        }
        else
        {
            Assert.False(stats.Opened);
            Assert.NotNull(stats.Failure);
        }
    }
```

Add `using System.Linq;` and `using System.Diagnostics;` at the top of the test file if they are missing.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build XrplVanity.sln -c Release`
Expected: build errors on `DeviceSelector.FirstAvailable`, `IsFirstAvailable`, `DutyPercent` setter and the four-argument `TryOpenFirst`.

- [ ] **Step 3: Implement**

In `src/XrplVanity.Gpu/MultiDeviceSearch.cs` replace the `DeviceSelector` record and the `Selector` property:

```csharp
public sealed record DeviceSelector(string Backend, int Index)
{
    /// <summary>
    /// "The first usable device in backend preference order" (the CLI's <c>--gpu</c>). Resolved
    /// by <see cref="MultiDeviceSearch"/> when the worker opens the device; the stats entry then
    /// carries the concrete selector.
    /// </summary>
    public static DeviceSelector FirstAvailable { get; } = new("gpu", -1);

    public bool IsFirstAvailable => Index < 0;

    public override string ToString() => IsFirstAvailable ? "gpu" : $"{Backend}:{Index}";
}
```

```csharp
    public DeviceSelector Selector { get; internal set; } = null!;
```

Replace the `_dutyPercent` field with a volatile-backed property:

```csharp
    private int _dutyPercent;

    /// <summary>
    /// Duty cycle applied by every worker at its next pause (1–100). Set from any thread while
    /// <see cref="Run"/> is in progress.
    /// </summary>
    public int DutyPercent
    {
        get => Volatile.Read(ref _dutyPercent);
        set
        {
            if (value < 1 || value > DutyCycle.Full)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duty cycle must be a whole number from 1 to 100.");
            }

            Volatile.Write(ref _dutyPercent, value);
        }
    }
```

In the internal constructor keep the range check and assign `_dutyPercent = dutyPercent;`. In `RunDevice` replace `DutyCycle.Pause(launch.Elapsed, _dutyPercent, stop.Token);` with `DutyCycle.Pause(launch.Elapsed, DutyPercent, stop.Token);`, and replace the open block at the top of `RunDevice`:

```csharp
        Stopwatch open = Stopwatch.StartNew();
        IGpuDevice? device;
        string reason;
        bool opened = stats.Selector.IsFirstAvailable
            ? TryOpenFirstAvailable(stats, out device, out reason)
            : GpuBackends.TryOpen(_backends, stats.Selector.Backend, stats.Selector.Index, out device, out reason);
        if (!opened)
        {
            stats.Failure = reason;
            TryLog(stats, stop, $"warning: {stats.Selector} could not be opened: {reason}");
            return;
        }
```

with the helper:

```csharp
    private bool TryOpenFirstAvailable(DeviceSearchStats stats, out IGpuDevice? device, out string reason)
    {
        if (!GpuBackends.TryOpenFirst(_backends, out device, out DeviceSelector? resolved, out reason))
        {
            return false;
        }

        stats.Selector = resolved!;
        return true;
    }
```

In `src/XrplVanity.Gpu/GpuBackends.cs` make the existing three-argument internal `TryOpenFirst` delegate to a new four-argument one:

```csharp
    internal static bool TryOpenFirst(IReadOnlyList<IGpuBackend> backends, out IGpuDevice? device, out string reason) =>
        TryOpenFirst(backends, out device, out _, out reason);

    internal static bool TryOpenFirst(IReadOnlyList<IGpuBackend> backends, out IGpuDevice? device, out DeviceSelector? selector, out string reason)
    {
        List<string> reasons = new();
        foreach (IGpuBackend backend in backends)
        {
            if (backend.EnumerateDevices().Count > 0)
            {
                try
                {
                    device = backend.Open(0);
                    selector = new DeviceSelector(backend.Name, 0);
                    reason = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException or CudaException or OpenClException)
                {
                    reasons.Add($"{backend.Name}: {ex.Message}");
                }
            }
            else
            {
                reasons.Add($"{backend.Name}: no devices");
            }
        }

        device = null;
        selector = null;
        reason = string.Join("; ", reasons);
        return false;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*MultiDeviceSearchTests" -class "*GpuBackendsTests"`
Expected: all passed; the duty test runs on both backends (about 10–20 s each on the reference card).

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Gpu tests/XrplVanity.Tests/Gpu
git commit -m "feat(gpu): MultiDeviceSearch.DutyPercent settable mid-run; DeviceSelector.FirstAvailable"
```

---

### Task 3: Request and progress types, and the CPU worker pool with a candidate counter

**Files:**
- Create: `src/XrplVanity.Search/SearchTarget.cs`
- Create: `src/XrplVanity.Search/SearchProgress.cs`
- Create: `src/XrplVanity.Search/CpuSearchWorkers.cs`
- Test: `tests/XrplVanity.Tests/Search/SearchRequestTests.cs`, `tests/XrplVanity.Tests/Search/CpuSearchWorkersTests.cs`

**Interfaces:**
- Produces: the types in the code below, verbatim; `CpuSearchWorkers(int threads, PatternSet patterns, MatchSink sink)` with `void Run(CancellationTokenSource stop)` (blocking, joins its threads), `long CandidatesSearched` (live), `TimeSpan Elapsed`.

- [ ] **Step 1: Write the failing tests**

`tests/XrplVanity.Tests/Search/SearchRequestTests.cs`:

```csharp
using System;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class SearchRequestTests
{
    private static PatternSet Patterns() => new PatternSet.Builder().Add(MatchPosition.Starts, "ab", ignoreCase: false).Build();

    [Fact]
    public void Requires_At_Least_One_Target()
    {
        Assert.Throws<ArgumentException>(() => new SearchRequest(Patterns(), Array.Empty<SearchTarget>(), null));
    }

    [Fact]
    public void Allows_At_Most_One_Cpu_Target()
    {
        Assert.Throws<ArgumentException>(() => new SearchRequest(Patterns(), new SearchTarget[] { new SearchTarget.Cpu(1), new SearchTarget.Cpu(2) }, null));
    }

    [Fact]
    public void FirstAvailableDevice_Cannot_Be_Combined_With_An_Explicit_Device()
    {
        Assert.Throws<ArgumentException>(() => new SearchRequest(Patterns(),
            new SearchTarget[] { new SearchTarget.FirstAvailableDevice(), new SearchTarget.Device(new DeviceSelector("cuda", 0)) }, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Rejects_A_Duty_Outside_1_To_100(int duty)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchRequest(Patterns(), new SearchTarget[] { new SearchTarget.Cpu(1) }, null, duty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_A_Non_Positive_Count(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchRequest(Patterns(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count));
    }

    [Fact]
    public void Cpu_Target_Requires_At_Least_One_Thread()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchTarget.Cpu(0));
    }

    [Fact]
    public void A_Set_With_No_Reachable_Pattern_Is_Rejected()
    {
        PatternSet none = new PatternSet.Builder().Add(MatchPosition.Starts, "0", ignoreCase: false).Build();
        Assert.Throws<ArgumentException>(() => new SearchRequest(none, new SearchTarget[] { new SearchTarget.Cpu(1) }, null));
    }
}
```

`tests/XrplVanity.Tests/Search/CpuSearchWorkersTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using XrplVanity.Core;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class CpuSearchWorkersTests
{
    // One character matches 1 in 58 candidates: a few milliseconds of CPU work.
    private static PatternSet OneChar() => new PatternSet.Builder().Add(MatchPosition.Starts, "a", ignoreCase: false).Build();

    [Fact]
    public void Stops_Exactly_At_The_Limit_And_Counts_Candidates()
    {
        List<Match> found = new();
        using CancellationTokenSource stop = new();
        MatchSink sink = new(limit: 3, m => { lock (found) found.Add(m); });
        CpuSearchWorkers workers = new(threads: 2, OneChar(), sink);

        workers.Run(stop);   // returns once the sink refuses and every thread has stopped

        Assert.Equal(3, found.Count);
        Assert.All(found, m => Assert.StartsWith("ra", m.Address));
        Assert.True(workers.CandidatesSearched >= 3, $"counted {workers.CandidatesSearched}");
        Assert.True(workers.Elapsed > System.TimeSpan.Zero);
        Assert.True(stop.IsCancellationRequested, "reaching the limit cancels the pool");
    }

    [Fact]
    public void Caller_Cancellation_Stops_All_Threads()
    {
        PatternSet never = new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();
        using CancellationTokenSource stop = new(System.TimeSpan.FromMilliseconds(300));
        MatchSink sink = new(limit: null, _ => { });
        CpuSearchWorkers workers = new(threads: 4, never, sink);

        Thread runner = new(() => workers.Run(stop)) { IsBackground = true };
        runner.Start();

        Assert.True(runner.Join(System.TimeSpan.FromSeconds(10)), "workers did not stop after cancellation");
        Assert.True(workers.CandidatesSearched > 0, "no candidates counted before cancellation");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build XrplVanity.sln -c Release`
Expected: build errors — the types do not exist.

- [ ] **Step 3: Implement the types**

`src/XrplVanity.Search/SearchTarget.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using XrplVanity.Core;
using XrplVanity.Gpu;

namespace XrplVanity.Search;

/// <summary>Where a session searches: the CPU, an explicit device, or the first usable device.</summary>
public abstract record SearchTarget
{
    private SearchTarget()
    {
    }

    public sealed record Cpu : SearchTarget
    {
        public Cpu(int threads)
        {
            if (threads < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(threads), threads, "At least one CPU thread is required.");
            }

            Threads = threads;
        }

        public int Threads { get; }
    }

    public sealed record Device(DeviceSelector Selector) : SearchTarget;

    /// <summary>The first usable device in backend preference order (the CLI's <c>--gpu</c>).</summary>
    public sealed record FirstAvailableDevice : SearchTarget;
}

public sealed record SearchRequest
{
    public SearchRequest(PatternSet patterns, IReadOnlyList<SearchTarget> targets, int? count, int dutyPercent = DutyCycle.Full)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(targets);
        if (!patterns.HasPatterns)
        {
            throw new ArgumentException("The pattern set has no reachable pattern.", nameof(patterns));
        }

        if (targets.Count == 0)
        {
            throw new ArgumentException("At least one target is required.", nameof(targets));
        }

        if (targets.OfType<SearchTarget.Cpu>().Count() > 1)
        {
            throw new ArgumentException("At most one CPU target is allowed.", nameof(targets));
        }

        if (targets.OfType<SearchTarget.FirstAvailableDevice>().Any() && targets.OfType<SearchTarget.Device>().Any())
        {
            throw new ArgumentException("FirstAvailableDevice cannot be combined with an explicit device.", nameof(targets));
        }

        if (count is int c && c <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "The count must be positive or null.");
        }

        if (dutyPercent < 1 || dutyPercent > DutyCycle.Full)
        {
            throw new ArgumentOutOfRangeException(nameof(dutyPercent), dutyPercent, "The duty cycle must be a whole number from 1 to 100.");
        }

        Patterns = patterns;
        Targets = targets;
        Count = count;
        DutyPercent = dutyPercent;
    }

    public PatternSet Patterns { get; }
    public IReadOnlyList<SearchTarget> Targets { get; }
    public int? Count { get; }
    public int DutyPercent { get; }
}
```

`src/XrplVanity.Search/SearchProgress.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace XrplVanity.Search;

public enum TargetState { Pending, Opening, Running, Finished, Failed }

public sealed record TargetProgress(
    string Id,
    string Name,
    TargetState State,
    long Candidates,
    double RatePerSecond,
    string? Failure);

public sealed record SearchProgress(
    TimeSpan Elapsed,
    long TotalCandidates,
    double TotalRatePerSecond,
    long Reported,
    IReadOnlyList<TargetProgress> Targets);

public enum SessionOutcome { CountReached, Cancelled, AllTargetsFailed }

public sealed record SearchSummary(SessionOutcome Outcome, SearchProgress Final);
```

`src/XrplVanity.Search/CpuSearchWorkers.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using XrplVanity.Core;

namespace XrplVanity.Search;

/// <summary>
/// The CPU search: N threads, each with its own deriver and a 512-candidate CSPRNG pool,
/// reporting through the shared sink. The candidate counter is bumped once per pool refill,
/// so it lags the truth by at most 512 per thread.
/// </summary>
internal sealed class CpuSearchWorkers
{
    private const int PoolCandidates = 512;
    private const int EntropyBytes = 16;

    private readonly int _threads;
    private readonly PatternSet _patterns;
    private readonly MatchSink _sink;
    private readonly Stopwatch _elapsed = new();
    private long _candidates;

    public CpuSearchWorkers(int threads, PatternSet patterns, MatchSink sink)
    {
        if (threads < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threads), threads, "At least one CPU thread is required.");
        }

        _threads = threads;
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public long CandidatesSearched => Volatile.Read(ref _candidates);
    public TimeSpan Elapsed => _elapsed.Elapsed;

    /// <summary>Runs until <paramref name="stop"/> is cancelled or the sink refuses a match (which cancels <paramref name="stop"/>).</summary>
    public void Run(CancellationTokenSource stop)
    {
        _elapsed.Restart();
        Thread[] workers = new Thread[_threads];
        for (int t = 0; t < workers.Length; t++)
        {
            workers[t] = new Thread(() => Worker(stop)) { IsBackground = true, Name = $"cpu-{t}" };
            workers[t].Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        _elapsed.Stop();
    }

    private void Worker(CancellationTokenSource stop)
    {
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), _patterns);
        byte[] pool = new byte[EntropyBytes * PoolCandidates];
        int poolPos = pool.Length;
        int sinceRefill = 0;

        while (!stop.IsCancellationRequested)
        {
            if (poolPos >= pool.Length)
            {
                Interlocked.Add(ref _candidates, sinceRefill);
                sinceRefill = 0;
                RandomNumberGenerator.Fill(pool);
                poolPos = 0;
            }

            ReadOnlySpan<byte> entropy = pool.AsSpan(poolPos, EntropyBytes);
            poolPos += EntropyBytes;
            sinceRefill++;

            if (evaluator.TryEvaluate(entropy, out Match match) && !_sink.TryReport(match))
            {
                stop.Cancel();
                break;
            }
        }

        Interlocked.Add(ref _candidates, sinceRefill);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*SearchRequestTests" -class "*CpuSearchWorkersTests"`
Expected: all passed.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Search tests/XrplVanity.Tests/Search
git commit -m "feat(search): SearchRequest/SearchTarget, progress types and the CPU worker pool with a candidate counter"
```

---

### Task 4: `SearchSession`

**Files:**
- Create: `src/XrplVanity.Search/SearchSession.cs`
- Test: `tests/XrplVanity.Tests/Search/SearchSessionTests.cs`

**Interfaces:**
- Consumes: `SearchRequest`, `MatchSink`, `CpuSearchWorkers`, `MultiDeviceSearch` (with `DutyPercent`, `DeviceSelector.FirstAvailable`), `SearchProgress` types.
- Produces: `SearchSession(SearchRequest request, Action<Match> report, Action<string>? log = null)`; `int DutyPercent { get; set; }`; `SearchProgress Progress { get; }`; `Task<SearchSummary> Completion { get; }`; `event Action<SearchProgress>? ProgressChanged`; `event Action<Match>? MatchFound`; `void Start()`; `void Stop()`; `void Dispose()`. Target ids: `"cpu"`, `"{backend}:{index}"`, `"gpu"` while a `FirstAvailableDevice` is unresolved. Target names: `"CPU ({threads} threads)"` and the device name.

- [ ] **Step 1: Write the failing tests**

`tests/XrplVanity.Tests/Search/SearchSessionTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class SearchSessionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(120);

    private static PatternSet OneChar() => new PatternSet.Builder().Add(MatchPosition.Starts, "a", ignoreCase: false).Build();
    private static PatternSet Never() => new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();

    private static SearchSummary Await(Task<SearchSummary> completion)
    {
        Assert.True(completion.Wait(Deadline), "session did not complete in time");
        return completion.Result;
    }

    [Fact]
    public void Cpu_Only_Session_Reaches_The_Count_Exactly()
    {
        List<Match> reported = new();
        List<Match> events = new();
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(2) }, count: 5);
        using SearchSession session = new(request, m => { lock (reported) reported.Add(m); });
        session.MatchFound += m => { lock (events) events.Add(m); };

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.CountReached, summary.Outcome);
        Assert.Equal(5, reported.Count);
        Assert.Equal(5, events.Count);
        Assert.Equal(5, summary.Final.Reported);
        TargetProgress cpu = Assert.Single(summary.Final.Targets);
        Assert.Equal("cpu", cpu.Id);
        Assert.Equal("CPU (2 threads)", cpu.Name);
        Assert.Equal(TargetState.Finished, cpu.State);
        Assert.True(cpu.Candidates >= 5);
        Assert.True(summary.Final.TotalCandidates >= 5);
    }

    [Fact]
    public void Stop_Completes_With_Cancelled_And_Joins_Every_Thread()
    {
        SearchRequest request = new(Never(), new SearchTarget[] { new SearchTarget.Cpu(3) }, count: null);
        using SearchSession session = new(request, _ => { });
        session.Start();
        Thread.Sleep(300);

        session.Stop();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.Cancelled, summary.Outcome);
        Assert.Equal(TargetState.Finished, summary.Final.Targets[0].State);
        Assert.True(summary.Final.TotalCandidates > 0);
        session.Stop();   // idempotent
    }

    [Fact]
    public void Progress_Snapshots_Arrive_With_Growing_Counts_And_A_Cpu_Rate()
    {
        SearchRequest request = new(Never(), new SearchTarget[] { new SearchTarget.Cpu(2) }, count: null);
        using SearchSession session = new(request, _ => { });
        List<SearchProgress> snapshots = new();
        session.ProgressChanged += p => { lock (snapshots) snapshots.Add(p); };

        session.Start();
        Thread.Sleep(1200);
        session.Stop();
        Await(session.Completion);

        SearchProgress[] seen;
        lock (snapshots) seen = snapshots.ToArray();
        Assert.True(seen.Length >= 3, $"only {seen.Length} snapshots in 1.2 s");
        Assert.True(seen[^1].TotalCandidates >= seen[0].TotalCandidates);
        Assert.True(seen[^1].Targets[0].RatePerSecond > 0, "CPU rate is zero");
        Assert.Equal(TargetState.Running, seen[1].Targets[0].State);
        Assert.True(session.Progress.Elapsed > TimeSpan.Zero);
    }

    [Fact]
    public void A_Missing_Device_Fails_Alone_While_The_Cpu_Keeps_Searching()
    {
        SearchRequest request = new(OneChar(),
            new SearchTarget[] { new SearchTarget.Cpu(1), new SearchTarget.Device(new DeviceSelector("cuda", 99)) }, count: 2);
        List<string> log = new();
        using SearchSession session = new(request, _ => { }, line => { lock (log) log.Add(line); });

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.CountReached, summary.Outcome);
        TargetProgress device = summary.Final.Targets.Single(t => t.Id == "cuda:99");
        Assert.Equal(TargetState.Failed, device.State);
        Assert.NotNull(device.Failure);
        Assert.Equal(TargetState.Finished, summary.Final.Targets.Single(t => t.Id == "cpu").State);
    }

    [Fact]
    public void When_The_Only_Target_Fails_The_Outcome_Says_So()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Device(new DeviceSelector("cuda", 99)) }, count: 1);
        using SearchSession session = new(request, _ => { });

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.AllTargetsFailed, summary.Outcome);
        Assert.Equal(0, summary.Final.Reported);
    }

    [Fact]
    public void A_Second_Start_Throws()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count: 1);
        using SearchSession session = new(request, _ => { });
        session.Start();
        Assert.Throws<InvalidOperationException>(session.Start);
        Await(session.Completion);
    }

    [Fact]
    public void Duty_Is_Validated_And_Forwarded()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count: 1, dutyPercent: 80);
        using SearchSession session = new(request, _ => { });
        Assert.Equal(80, session.DutyPercent);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.DutyPercent = 0);
        session.DutyPercent = 50;
        Assert.Equal(50, session.DutyPercent);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Mixed_Cpu_And_Gpu_Session_Stops_Exactly_At_The_Count(string backend)
    {
        GpuTestHelper.RequireDevice(backend).Dispose();   // skip when unavailable
        // Case-insensitive two characters: frequent on the GPU, so both sources report.
        PatternSet frequent = new PatternSet.Builder().Add(MatchPosition.Starts, "ab", ignoreCase: true).Build();
        SearchRequest request = new(frequent,
            new SearchTarget[] { new SearchTarget.Cpu(2), new SearchTarget.Device(new DeviceSelector(backend, 0)) }, count: 50);
        int reported = 0;
        using SearchSession session = new(request, _ => Interlocked.Increment(ref reported));

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.CountReached, summary.Outcome);
        Assert.Equal(50, reported);
        Assert.Equal(50, summary.Final.Reported);
        TargetProgress device = summary.Final.Targets.Single(t => t.Id == $"{backend}:0");
        Assert.Equal(TargetState.Finished, device.State);
        Assert.False(string.IsNullOrEmpty(device.Name));
    }

    [Fact]
    public void FirstAvailableDevice_Resolves_Its_Id_Or_Fails_Cleanly()
    {
        bool anyDevice = GpuBackends.All.Any(b => b.EnumerateDevices().Count > 0);
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.FirstAvailableDevice() }, count: 1);
        using SearchSession session = new(request, _ => { });

        session.Start();
        SearchSummary summary = Await(session.Completion);

        TargetProgress target = Assert.Single(summary.Final.Targets);
        if (anyDevice)
        {
            Assert.Equal(SessionOutcome.CountReached, summary.Outcome);
            Assert.Matches("^(cuda|opencl):[0-9]+$", target.Id);
        }
        else
        {
            Assert.Equal(SessionOutcome.AllTargetsFailed, summary.Outcome);
            Assert.Equal("gpu", target.Id);
        }
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Duty_Set_During_A_Run_Reaches_The_Device_Loop(string backend)
    {
        GpuTestHelper.RequireDevice(backend).Dispose();
        SearchRequest request = new(Never(), new SearchTarget[] { new SearchTarget.Device(new DeviceSelector(backend, 0)) }, count: null);
        using SearchSession session = new(request, _ => { });
        session.Start();
        session.DutyPercent = 30;
        Assert.Equal(30, session.DutyPercent);
        Assert.Equal(30, session.DeviceSearchForTests!.DutyPercent);
        session.Stop();
        Await(session.Completion);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build XrplVanity.sln -c Release`
Expected: build errors — `SearchSession` does not exist.

- [ ] **Step 3: Implement `SearchSession`**

`src/XrplVanity.Search/SearchSession.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XrplVanity.Core;
using XrplVanity.Gpu;

namespace XrplVanity.Search;

/// <summary>
/// One search over one or more targets. <see cref="Start"/> spins one thread for the CPU target
/// and one <see cref="MultiDeviceSearch"/> run (which owns a thread per device) for the device
/// targets; a ticker publishes <see cref="Progress"/> every 250 ms; <see cref="Completion"/>
/// completes when every thread has joined. Events fire on worker threads.
/// </summary>
public sealed class SearchSession : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private readonly SearchRequest _request;
    private readonly Action<Match> _report;
    private readonly Action<string> _log;
    private readonly MatchSink _sink;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<SearchSummary> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _elapsed = new();
    private readonly object _startGate = new();
    private readonly List<string> _targetOrder = new();   // ids in request order

    private readonly SearchTarget.Cpu? _cpuTarget;
    private readonly CpuSearchWorkers? _cpu;
    private readonly List<DeviceSelector> _deviceSelectors = new();
    private readonly MultiDeviceSearch? _devices;
    private readonly ManualResetEventSlim _tickerDone = new();

    private volatile bool _cpuOpening;
    private volatile bool _cpuFinished;
    private string? _cpuFailure;
    private volatile SearchProgress _progress;
    private bool _started;
    private bool _disposed;

    public SearchSession(SearchRequest request, Action<Match> report, Action<string>? log = null)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _log = log ?? (_ => { });
        _sink = new MatchSink(request.Count, OnAccepted);

        foreach (SearchTarget target in request.Targets)
        {
            switch (target)
            {
                case SearchTarget.Cpu cpu:
                    _cpuTarget = cpu;
                    _cpu = new CpuSearchWorkers(cpu.Threads, request.Patterns, _sink);
                    _targetOrder.Add("cpu");
                    break;
                case SearchTarget.Device device:
                    _deviceSelectors.Add(device.Selector);
                    _targetOrder.Add(device.Selector.ToString());
                    break;
                case SearchTarget.FirstAvailableDevice:
                    _deviceSelectors.Add(DeviceSelector.FirstAvailable);
                    _targetOrder.Add(DeviceSelector.FirstAvailable.ToString());
                    break;
            }
        }

        if (_deviceSelectors.Count > 0)
        {
            _devices = new MultiDeviceSearch(_deviceSelectors, request.Patterns, ReportFromDevice, _log, request.DutyPercent);
        }

        _requestedDuty = request.DutyPercent;
        _progress = Snapshot();
    }

    public int DutyPercent
    {
        get => _devices?.DutyPercent ?? _requestedDuty;
        set
        {
            if (value < 1 || value > DutyCycle.Full)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duty cycle must be a whole number from 1 to 100.");
            }

            if (_devices is not null)
            {
                _devices.DutyPercent = value;
            }

            _requestedDuty = value;
        }
    }

    private int _requestedDuty;

    public SearchProgress Progress => _progress;
    public Task<SearchSummary> Completion => _completion.Task;

    public event Action<SearchProgress>? ProgressChanged;
    public event Action<Match>? MatchFound;

    /// <summary>The device search, exposed for tests that check duty propagation.</summary>
    internal MultiDeviceSearch? DeviceSearchForTests => _devices;

    public void Start()
    {
        lock (_startGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("The session has already been started.");
            }

            _started = true;
        }

        _elapsed.Start();
        Thread coordinator = new(Coordinate) { IsBackground = true, Name = "search-session" };
        coordinator.Start();
    }

    public void Stop()
    {
        if (!_stop.IsCancellationRequested)
        {
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Dispose raced with Stop; the session is already finished.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        if (_started)
        {
            Completion.Wait(TimeSpan.FromSeconds(30));
        }

        _stop.Dispose();
        _tickerDone.Dispose();
    }

    private void Coordinate()
    {
        Thread? cpuThread = null;
        Thread? deviceThread = null;
        MultiDeviceSearchResult? deviceResult = null;
        Thread ticker = new(Tick) { IsBackground = true, Name = "search-ticker" };

        try
        {
            if (_cpu is not null)
            {
                cpuThread = new Thread(RunCpu) { IsBackground = true, Name = "cpu-pool" };
                _cpuOpening = true;
                cpuThread.Start();
            }

            if (_devices is not null)
            {
                deviceThread = new Thread(() => deviceResult = _devices.Run(_stop.Token)) { IsBackground = true, Name = "device-pool" };
                deviceThread.Start();
            }

            ticker.Start();
            cpuThread?.Join();
            deviceThread?.Join();
        }
        finally
        {
            Stop();
            _tickerDone.Set();
            ticker.Join();
            _elapsed.Stop();
        }

        SearchProgress final = Snapshot();
        _progress = final;
        RaiseProgress(final);

        bool anyOpened = _cpu is not null || (deviceResult?.DevicesOpened ?? 0) > 0;
        SessionOutcome outcome = _sink.CountSatisfied ? SessionOutcome.CountReached
            : anyOpened ? SessionOutcome.Cancelled
            : SessionOutcome.AllTargetsFailed;
        _completion.TrySetResult(new SearchSummary(outcome, final));
    }

    private void RunCpu()
    {
        try
        {
            _cpuOpening = false;
            _cpu!.Run(_stop);
        }
        catch (Exception ex)
        {
            _cpuFailure = ex.Message;
            SafeLog($"warning: cpu stopped: {ex.Message}");
        }
        finally
        {
            _cpuFinished = true;
        }
    }

    private void Tick()
    {
        while (!_tickerDone.Wait(TickInterval))
        {
            SearchProgress snapshot = Snapshot();
            _progress = snapshot;
            RaiseProgress(snapshot);
        }
    }

    private void RaiseProgress(SearchProgress snapshot)
    {
        try
        {
            ProgressChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            // A throwing subscriber must not kill the ticker or the coordinator; stop the search instead.
            SafeLog($"warning: progress subscriber failed: {ex.Message}");
            Stop();
        }
    }

    private bool ReportFromDevice(Match match)
    {
        bool more = _sink.TryReport(match);
        if (!more)
        {
            Stop();
        }

        return more;
    }

    /// <summary>Runs inside the sink's lock: the caller's writer first, then the event.</summary>
    private void OnAccepted(Match match)
    {
        _report(match);
        MatchFound?.Invoke(match);
    }

    private SearchProgress Snapshot()
    {
        double seconds = _elapsed.Elapsed.TotalSeconds;
        List<TargetProgress> targets = new(_targetOrder.Count);
        long total = 0;
        double totalRate = 0;
        int deviceIndex = 0;

        foreach (string id in _targetOrder)
        {
            if (id == "cpu")
            {
                long candidates = _cpu!.CandidatesSearched;
                double cpuSeconds = _cpu.Elapsed.TotalSeconds;
                double rate = cpuSeconds > 0 ? candidates / cpuSeconds : 0;
                TargetState state = _cpuFailure is not null ? TargetState.Failed
                    : _cpuFinished ? TargetState.Finished
                    : _cpuOpening ? TargetState.Opening
                    : _started ? TargetState.Running
                    : TargetState.Pending;
                targets.Add(new TargetProgress("cpu", $"CPU ({_cpuTarget!.Threads} threads)", state, candidates, rate, _cpuFailure));
                total += candidates;
                totalRate += rate;
            }
            else
            {
                DeviceSearchStats stats = _devices!.Devices[deviceIndex++];
                TargetState state = stats.Failure is not null ? TargetState.Failed
                    : stats.Seconds > 0 ? TargetState.Finished
                    : stats.Opened ? TargetState.Running
                    : _started ? TargetState.Opening
                    : TargetState.Pending;
                double rate = stats.Opened && stats.Seconds > 0 ? stats.RatePerSecond
                    : stats.Opened && seconds > 0 ? stats.CandidatesSearched / seconds
                    : 0;
                targets.Add(new TargetProgress(stats.Selector.ToString(), stats.DeviceName ?? stats.Selector.ToString(), state,
                    stats.CandidatesSearched, rate, stats.Failure));
                total += stats.CandidatesSearched;
                totalRate += rate;
            }
        }

        return new SearchProgress(_elapsed.Elapsed, total, totalRate, _sink.Reported, targets);
    }

    private void SafeLog(string message)
    {
        try
        {
            _log(message);
        }
        catch
        {
            // Logging must never throw a second exception while the session is unwinding.
        }
    }
}
```

Notes for the implementer:

- `DeviceSearchStats.Seconds` is set only when a device worker finishes, so `Seconds > 0` marks `Finished`; a device whose open failed has `Failure` and `Opened == false`, which maps to `Failed` (a device that stopped mid-run with a failure is also `Failed`, and keeps its candidate count).
- `MultiDeviceSearch` writes `stats.Selector` when a `FirstAvailable` selector resolves, so the target id in later snapshots becomes `cuda:0` (or `opencl:0`); the `_targetOrder` entry stays `"gpu"` only as the marker for the loop — the snapshot reads the id from `stats.Selector`.
- `_requestedDuty` keeps the value set through `DutyPercent` on a session with no device target, so the property reads back what was set.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*SearchSessionTests"`
Expected: all passed on both backends (GPU-gated tests take a few seconds each).

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Search/SearchSession.cs tests/XrplVanity.Tests/Search/SearchSessionTests.cs
git commit -m "feat(search): SearchSession runs CPU and device targets with live progress and exact count"
```

---

### Task 5: `SearchEstimator` and `DeviceCatalog`

**Files:**
- Create: `src/XrplVanity.Search/SearchEstimator.cs`
- Create: `src/XrplVanity.Search/DeviceCatalog.cs`
- Test: `tests/XrplVanity.Tests/Search/SearchEstimatorTests.cs`, `tests/XrplVanity.Tests/Search/DeviceCatalogTests.cs`

**Interfaces:**
- Produces: `SearchEstimator.NominalAddressLength == 34`; `static double? ExpectedCandidatesPerMatch(PatternSet)`; `static TimeSpan? ExpectedTimeToMatch(double? expectedCandidates, double ratePerSecond)`; `DeviceCatalogEntry(DeviceSelector Selector, string Name, string Details, bool IsCpuDevice)` with `string Id => Selector.ToString()`; `static IReadOnlyList<DeviceCatalogEntry> DeviceCatalog.Enumerate()` and `internal static IReadOnlyList<DeviceCatalogEntry> Enumerate(IReadOnlyList<IGpuBackend> backends)`.

- [ ] **Step 1: Write the failing tests**

`tests/XrplVanity.Tests/Search/SearchEstimatorTests.cs`:

```csharp
using System;
using XrplVanity.Core;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class SearchEstimatorTests
{
    private static PatternSet Set(MatchPosition position, string pattern, bool ignoreCase = false) =>
        new PatternSet.Builder().Add(position, pattern, ignoreCase).Build();

    [Theory]
    [InlineData("a", 58)]
    [InlineData("ab", 3364)]
    [InlineData("abc", 195_112)]
    [InlineData("abcd", 11_316_496)]
    public void Case_Sensitive_Prefix_Expects_58_To_The_Length(string pattern, double expected)
    {
        Assert.Equal(expected, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, pattern))!.Value, precision: 6);
    }

    [Fact]
    public void Suffix_Has_The_Same_Expectation_As_A_Prefix()
    {
        Assert.Equal(3364, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Ends, "ab"))!.Value, precision: 6);
    }

    [Fact]
    public void Contains_Counts_Every_Position_In_A_Nominal_Address_Body()
    {
        // body length 33 (34 minus the leading r); "ab" fits in 32 positions.
        double expected = 3364.0 / 32.0;
        Assert.Equal(expected, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Contains, "ab"))!.Value, precision: 6);
    }

    [Fact]
    public void Case_Insensitive_Letters_With_Two_Cases_Double_The_Targets()
    {
        // "ab": a/A and b/B both exist in the alphabet -> 4 variants -> 58^2 / 4.
        Assert.Equal(3364.0 / 4.0, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "ab", ignoreCase: true))!.Value, precision: 6);
        // "o" has no upper-case form and "L" no lower-case form: no expansion.
        Assert.Equal(58, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "o", ignoreCase: true))!.Value, precision: 6);
        Assert.Equal(58, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "L", ignoreCase: true))!.Value, precision: 6);
    }

    [Fact]
    public void Several_Patterns_Add_Their_Probabilities()
    {
        PatternSet two = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "a", ignoreCase: false)
            .Add(MatchPosition.Ends, "b", ignoreCase: false)
            .Build();
        Assert.Equal(29, SearchEstimator.ExpectedCandidatesPerMatch(two)!.Value, precision: 6);
    }

    [Fact]
    public void An_Empty_Set_Has_No_Estimate()
    {
        PatternSet none = new PatternSet.Builder().Add(MatchPosition.Starts, "0", ignoreCase: false).Build();
        Assert.Null(SearchEstimator.ExpectedCandidatesPerMatch(none));
    }

    [Fact]
    public void Eta_Divides_Candidates_By_Rate_And_Propagates_Null()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), SearchEstimator.ExpectedTimeToMatch(1000, 100));
        Assert.Null(SearchEstimator.ExpectedTimeToMatch(null, 100));
        Assert.Null(SearchEstimator.ExpectedTimeToMatch(1000, 0));
        Assert.Null(SearchEstimator.ExpectedTimeToMatch(1000, -5));
    }
}
```

`tests/XrplVanity.Tests/Search/DeviceCatalogTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class DeviceCatalogTests
{
    private sealed class FakeBackend : IGpuBackend
    {
        private readonly List<GpuDeviceInfo> _devices;
        public FakeBackend(string name, params GpuDeviceInfo[] devices) { Name = name; _devices = devices.ToList(); }
        public string Name { get; }
        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices() => _devices;
        public IGpuDevice Open(int index) => throw new System.InvalidOperationException("not opened in this test");
    }

    [Fact]
    public void Lists_Every_Device_Of_Every_Backend_In_Order_With_Its_Cpu_Tag()
    {
        IGpuBackend[] backends =
        {
            new FakeBackend("cuda", new GpuDeviceInfo("cuda", 0, "RTX", "sm_120")),
            new FakeBackend("opencl", new GpuDeviceInfo("opencl", 0, "RTX", "OpenCL 3.0"), new GpuDeviceInfo("opencl", 1, "pocl", "cpu", IsCpuDevice: true)),
        };

        IReadOnlyList<DeviceCatalogEntry> entries = DeviceCatalog.Enumerate(backends);

        Assert.Equal(new[] { "cuda:0", "opencl:0", "opencl:1" }, entries.Select(e => e.Id));
        Assert.Equal("RTX", entries[0].Name);
        Assert.Equal("sm_120", entries[0].Details);
        Assert.True(entries[2].IsCpuDevice);
        Assert.False(entries[0].IsCpuDevice);
    }

    [Fact]
    public void A_Backend_That_Throws_On_Enumeration_Is_Skipped()
    {
        IGpuBackend[] backends = { new ThrowingBackend(), new FakeBackend("opencl", new GpuDeviceInfo("opencl", 0, "X", "")) };
        Assert.Single(DeviceCatalog.Enumerate(backends));
    }

    private sealed class ThrowingBackend : IGpuBackend
    {
        public string Name => "cuda";
        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices() => throw new System.InvalidOperationException("driver missing");
        public IGpuDevice Open(int index) => throw new System.InvalidOperationException();
    }

    [Fact]
    public void The_Real_Catalog_Does_Not_Throw()
    {
        IReadOnlyList<DeviceCatalogEntry> entries = DeviceCatalog.Enumerate();
        Assert.All(entries, e => Assert.Matches("^(cuda|opencl):[0-9]+$", e.Id));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build XrplVanity.sln -c Release`
Expected: build errors — the types do not exist.

- [ ] **Step 3: Implement**

`src/XrplVanity.Search/SearchEstimator.cs`:

```csharp
using System;
using XrplVanity.Core;

namespace XrplVanity.Search;

/// <summary>
/// Expected work per match. A candidate matches one concrete target of length n with
/// probability 58^-n for a prefix or suffix (the body excludes the fixed leading 'r'), and
/// (body length - n + 1) × 58^-n for a substring at any position of a nominal 34-character
/// address. The set's probability is the sum over targets (overlaps ignored: an upper bound
/// on the probability, hence a lower bound on the expected candidates) and the expectation is
/// its reciprocal. A guide, not a guarantee.
/// </summary>
public static class SearchEstimator
{
    public const int NominalAddressLength = 34;
    private const double AlphabetSize = 58.0;

    public static double? ExpectedCandidatesPerMatch(PatternSet patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (!patterns.HasPatterns)
        {
            return null;
        }

        int bodyLength = NominalAddressLength - 1;
        double probability = 0;
        foreach ((MatchPosition position, string value) in patterns.EnumerateTargets())
        {
            double single = Math.Pow(AlphabetSize, -value.Length);
            probability += position == MatchPosition.Contains
                ? Math.Max(bodyLength - value.Length + 1, 1) * single
                : single;
        }

        return probability > 0 ? 1.0 / probability : null;
    }

    public static TimeSpan? ExpectedTimeToMatch(double? expectedCandidates, double ratePerSecond)
    {
        if (expectedCandidates is not double candidates || candidates <= 0 || ratePerSecond <= 0 || double.IsNaN(ratePerSecond))
        {
            return null;
        }

        double seconds = candidates / ratePerSecond;
        return seconds > TimeSpan.MaxValue.TotalSeconds ? TimeSpan.MaxValue : TimeSpan.FromSeconds(seconds);
    }
}
```

`src/XrplVanity.Search/DeviceCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using XrplVanity.Gpu;

namespace XrplVanity.Search;

public sealed record DeviceCatalogEntry(DeviceSelector Selector, string Name, string Details, bool IsCpuDevice)
{
    public string Id => Selector.ToString();
}

/// <summary>Every device of every backend, in backend preference order, for front ends to offer.</summary>
public static class DeviceCatalog
{
    public static IReadOnlyList<DeviceCatalogEntry> Enumerate() => Enumerate(GpuBackends.All);

    internal static IReadOnlyList<DeviceCatalogEntry> Enumerate(IReadOnlyList<IGpuBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        List<DeviceCatalogEntry> entries = new();
        foreach (IGpuBackend backend in backends)
        {
            IReadOnlyList<GpuDeviceInfo> devices;
            try
            {
                devices = backend.EnumerateDevices();
            }
            catch (Exception)
            {
                // A backend whose driver or ICD loader is missing must not hide the others.
                continue;
            }

            foreach (GpuDeviceInfo info in devices)
            {
                entries.Add(new DeviceCatalogEntry(new DeviceSelector(backend.Name, info.Index), info.Name, info.Details, info.IsCpuDevice));
            }
        }

        return entries;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*SearchEstimatorTests" -class "*DeviceCatalogTests"`
Expected: all passed.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Search tests/XrplVanity.Tests/Search
git commit -m "feat(search): SearchEstimator (expected candidates and ETA) and DeviceCatalog"
```

---

### Task 6: Migrate the CLI onto `SearchSession` with a testable `Program.Run`

**Files:**
- Modify: `src/XrplVanity.Cli/Program.cs` (the `Program` class only; `SearchRunner` stays)
- Test: `tests/XrplVanity.Tests/CliProgramTests.cs` (new)
- Modify: `README.md` (one sentence on the CPU rate line in the CPU section)

**Interfaces:**
- Consumes: `SearchSession`, `SearchRequest`, `SearchTarget`, `MatchSink`, `CliMatchWriter`, `SearchRunner.ResolveDevices` / `ParseDeviceSpec`, `CliOptions`.
- Produces: `public static int Program.Run(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellation = default)`; `Main` wires `Console.Out`, `Console.Error` and `Ctrl+C`.

- [ ] **Step 1: Write the failing tests**

`tests/XrplVanity.Tests/CliProgramTests.cs`:

```csharp
using System.IO;
using System.Threading;
using XrplVanity.Cli;
using Xunit;

namespace XrplVanity.Tests;

public class CliProgramTests
{
    [Fact]
    public void Cpu_Run_Prints_Matches_Then_The_Cpu_Rate_Line_And_Exits_0()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int rc = Program.Run(new[] { "--case-sensitive", "--starts", "a", "--count", "2", "--threads", "2" }, stdout, stderr);

        Assert.Equal(0, rc);
        string[] lines = stdout.ToString().Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("Address: ra", l.TrimEnd()));
        Assert.All(lines, l => Assert.Contains("\tSeed: s", l));
        Assert.Matches(@"CPU: searched [\d,]+ candidates in \d+\.\d s \([\d,]+/s\)\.", stderr.ToString());
        Assert.Contains("WARNING: a seed is a secret.", stderr.ToString());
    }

    [Fact]
    public void Cancellation_Before_The_Count_Exits_130()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        using CancellationTokenSource cts = new(System.TimeSpan.FromMilliseconds(400));

        int rc = Program.Run(new[] { "--case-sensitive", "--starts", "zzzzzzzz", "--threads", "1" }, stdout, stderr, cts.Token);

        Assert.Equal(130, rc);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void No_Reachable_Pattern_Exits_2()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int rc = Program.Run(new[] { "--starts", "0" }, stdout, stderr);
        Assert.Equal(2, rc);
        Assert.Contains("error: no reachable patterns to search for.", stderr.ToString());
    }

    [Fact]
    public void Unknown_Gpu_Device_Exits_2_With_The_Existing_Message()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int rc = Program.Run(new[] { "--backend", "cuda", "--device", "99", "--starts", "a", "--count", "1" }, stdout, stderr);
        Assert.Equal(2, rc);
        Assert.Contains("error: --gpu requested but no usable device:", stderr.ToString());
    }

    [Fact]
    public void Out_File_Receives_Address_Tab_Seed()
    {
        string path = Path.GetTempFileName();
        try
        {
            int rc = Program.Run(new[] { "--case-sensitive", "--starts", "a", "--count", "1", "--out", path }, new StringWriter(), new StringWriter());
            Assert.Equal(0, rc);
            string line = File.ReadAllText(path).Trim();
            Assert.Matches(@"^ra\S+\ts\S+$", line);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build XrplVanity.sln -c Release`
Expected: build error — `Program.Run` does not exist.

- [ ] **Step 3: Rewrite the `Program` class**

Replace the whole `public static class Program { … }` in `src/XrplVanity.Cli/Program.cs` (keep `SearchRunner` above it, and keep `ListDevices`) with:

```csharp
public static class Program
{
    public static int Main(string[] args)
    {
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return Run(args, Console.Out, Console.Error, cts.Token);
    }

    /// <summary>The whole CLI with its streams injected, so tests can drive it without touching the console.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellation = default)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }

        if (options.ListDevices)
        {
            return ListDevices(stdout, stderr);
        }

        if (options.Count is int c && c <= 0)
        {
            stderr.WriteLine("Option '--count' must be a positive number.");
            return 2;
        }

        PatternSet patterns = SearchRunner.BuildPatternSet(options);
        foreach (NormalizedPattern u in patterns.Unreachable)
        {
            if (u.Value.Length == 0)
            {
                stderr.WriteLine("warning: empty pattern ignored (it would match every address).");
            }
            else
            {
                stderr.WriteLine($"warning: pattern '{u.Original}' cannot occur in an XRPL address; skipped.");
            }
        }

        foreach (NormalizedPattern s in patterns.CaseFoldingSkipped)
        {
            stderr.WriteLine(
                $"warning: pattern '{s.Original}' is longer than {PatternSet.MaxIgnoreCaseLength} " +
                "characters; searching it case-sensitively instead of case-insensitively.");
        }

        if (!patterns.HasPatterns)
        {
            stderr.WriteLine("error: no reachable patterns to search for.");
            return 2;
        }

        List<SearchTarget> targets = new();
        bool multi = false;
        if (options.GpuDevices is not null)
        {
            if (!SearchRunner.ResolveDevices(options, out List<DeviceSelector> selectors, out string reason))
            {
                stderr.WriteLine($"error: --gpu-devices: {reason}");
                return 2;
            }

            foreach (DeviceSelector selector in selectors)
            {
                targets.Add(new SearchTarget.Device(selector));
            }

            multi = true;
        }
        else if (options.Gpu)
        {
            if (options.Backend is null && options.Device is null)
            {
                targets.Add(new SearchTarget.FirstAvailableDevice());
            }
            else if (!SearchRunner.TryResolveSelector(options, out DeviceSelector? selector, out string reason))
            {
                stderr.WriteLine($"error: --gpu requested but no usable device: {reason}");
                return 2;
            }
            else
            {
                targets.Add(new SearchTarget.Device(selector!));
            }
        }
        else
        {
            targets.Add(new SearchTarget.Cpu(Math.Max(1, options.Threads)));
        }

        if (options.Gpu && options.ThreadsSpecified)
        {
            stderr.WriteLine("note: --threads is ignored with --gpu (the search runs on the device).");
        }

        if (options.Gpu && options.GpuDuty < DutyCycle.Full)
        {
            stderr.WriteLine(multi
                ? $"GPU: duty cycle {options.GpuDuty}% on every device (pausing between launches)."
                : $"GPU: duty cycle {options.GpuDuty}% (pausing between launches).");
        }

        stderr.WriteLine(
            "WARNING: a seed is a secret. Anyone who holds it controls the account. " +
            "Keep matches offline and verify this tool before using generated keys for real funds.");

        using CliMatchWriter writer = new(options.OutFile, stdout);
        SearchRequest request = new(patterns, targets, options.Count, options.GpuDuty);
        using SearchSession session = new(request, writer.Write, line => stderr.WriteLine(line));

        HashSet<string> announced = new(StringComparer.Ordinal);
        session.ProgressChanged += progress =>
        {
            foreach (TargetProgress t in progress.Targets)
            {
                if (t.State == TargetState.Running && t.Id != "cpu" && announced.Add(t.Id))
                {
                    stderr.WriteLine(multi ? $"GPU[{t.Id}]: {t.Name}" : $"GPU: {t.Id[..t.Id.IndexOf(':')]}:{t.Name}");
                }
            }
        };

        using CancellationTokenRegistration registration = cancellation.Register(session.Stop);
        session.Start();
        SearchSummary summary = session.Completion.GetAwaiter().GetResult();

        RenderSummary(summary, multi, stderr);

        if (summary.Outcome == SessionOutcome.AllTargetsFailed)
        {
            string failure = summary.Final.Targets.FirstOrDefault(t => t.Failure is not null)?.Failure ?? "no device could be opened";
            stderr.WriteLine(multi ? "error: --gpu-devices: no device could be opened." : $"error: --gpu requested but no usable device: {failure}");
            return 2;
        }

        // 0 only when a --count target was fully satisfied; otherwise the run ended
        // via Ctrl+C (or a --count-less search the user stopped), reported as 130.
        return summary.Outcome == SessionOutcome.CountReached ? 0 : 130;
    }

    private static void RenderSummary(SearchSummary summary, bool multi, TextWriter stderr)
    {
        SearchProgress final = summary.Final;
        double seconds = Math.Max(final.Elapsed.TotalSeconds, 1e-9);
        TargetProgress? cpu = final.Targets.FirstOrDefault(t => t.Id == "cpu");
        List<TargetProgress> devices = final.Targets.Where(t => t.Id != "cpu").ToList();

        if (cpu is not null)
        {
            stderr.WriteLine($"CPU: searched {cpu.Candidates:N0} candidates in {seconds:F1} s ({cpu.Candidates / seconds:N0}/s).");
        }

        if (devices.Count == 0)
        {
            return;
        }

        if (!multi)
        {
            TargetProgress d = devices[0];
            if (d.State == TargetState.Failed && d.Candidates == 0)
            {
                return;   // the caller prints the error line
            }

            stderr.WriteLine($"GPU: searched {d.Candidates:N0} candidates in {seconds:F1} s ({d.Candidates / seconds:N0}/s).");
            return;
        }

        int opened = 0;
        long total = 0;
        foreach (TargetProgress d in devices)
        {
            if (d.State == TargetState.Failed && d.Candidates == 0)
            {
                stderr.WriteLine($"GPU[{d.Id}]: failed: {d.Failure}");
                continue;
            }

            opened++;
            total += d.Candidates;
            string tail = d.Failure is null ? string.Empty : $" (stopped: {d.Failure})";
            stderr.WriteLine($"GPU[{d.Id}]: searched {d.Candidates:N0} candidates in {seconds:F1} s ({d.Candidates / seconds:N0}/s).{tail}");
        }

        if (opened > 0)
        {
            stderr.WriteLine($"GPU: searched {total:N0} candidates on {opened} device(s) in {seconds:F1} s ({total / seconds:N0}/s).");
        }
    }

    private static int ListDevices(TextWriter stdout, TextWriter stderr)
    {
        int flat = 0;
        bool any = false;
        foreach (IGpuBackend backend in GpuBackends.All)
        {
            foreach (GpuDeviceInfo info in backend.EnumerateDevices())
            {
                string tag = info.IsCpuDevice ? " [cpu]" : string.Empty;
                stdout.WriteLine($"{flat,2}  {backend.Name}:{info.Index}  {info.Name}  ({info.Details}){tag}");
                flat++;
                any = true;
            }
        }

        if (!any)
        {
            stderr.WriteLine("no GPU devices found (no CUDA driver and no OpenCL platform).");
            return 2;
        }

        return 0;
    }
}
```

Add to `SearchRunner` (next to `ResolveDevice`, which the GPU tests still use):

```csharp
    /// <summary>
    /// Maps an explicit <c>--backend</c>/<c>--device</c> pair to a selector without opening the
    /// device: <c>--backend</c> with an optional index, or a bare <c>--device</c> indexing the
    /// flat list printed by <c>--list-devices</c>.
    /// </summary>
    public static bool TryResolveSelector(CliOptions options, out DeviceSelector? selector, out string reason)
    {
        if (options.Backend is not null)
        {
            IGpuBackend? backend = FindBackend(GpuBackends.All, options.Backend);
            int index = options.Device ?? 0;
            if (backend is null)
            {
                selector = null;
                reason = $"unknown backend '{options.Backend}' (known: cuda, opencl)";
                return false;
            }

            if (index < 0 || index >= backend.EnumerateDevices().Count)
            {
                selector = null;
                reason = $"device index {index} is out of range for backend '{backend.Name}'; run --list-devices";
                return false;
            }

            selector = new DeviceSelector(backend.Name, index);
            reason = string.Empty;
            return true;
        }

        int remaining = options.Device!.Value;
        foreach (IGpuBackend candidate in GpuBackends.All)
        {
            int count = candidate.EnumerateDevices().Count;
            if (remaining < count)
            {
                selector = new DeviceSelector(candidate.Name, remaining);
                reason = string.Empty;
                return true;
            }

            remaining -= count;
        }

        selector = null;
        reason = $"device index {options.Device} is out of range; run --list-devices";
        return false;
    }
```

Delete `RunCpu`, `RunGpu`, `RunMultiGpu` and the old `ListDevices()` overload. The `note: compiling the OpenCL program …` and the overflow / consistency warnings now arrive through the session's `log` callback (they come from `MultiDeviceSearch`, whose wording names the selector); the consistency-error tail of the old `GPU: searched …` lines is dropped because the per-launch `GPU consistency error …` log line already reports each occurrence.

README: in the section that describes the CPU path (search for "--threads"), add one sentence: "At the end of a CPU run the CLI prints `CPU: searched N candidates in T s (R/s).` on stderr, mirroring the GPU line."

- [ ] **Step 4: Run the tests, then the harness smokes by hand**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release`
Expected: everything green, `Skipped: 0`.

Manual smokes (replace the `linux/in-container.sh` needles by running the same commands on Windows and comparing):

```bash
dotnet run --project src/XrplVanity.Cli -c Release -- --list-devices
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --starts ab --count 1
dotnet run --project src/XrplVanity.Cli -c Release -- --backend opencl --starts ab --count 1
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu-devices all --starts ab --count 2
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu --gpu-duty 50 --case-sensitive --starts abcd --count 3
```

Expected: exit 0 each; `Address: …` lines on stdout; `GPU: cuda:<name>` then `GPU: searched …` for `--gpu`; `GPU[cuda:0]: …` / `GPU[opencl:0]: …` lines plus the aggregate `GPU: searched … on 2 device(s) …` for `--gpu-devices all`. Then read `linux/in-container.sh` and confirm every `check` needle still appears in the corresponding output; if a needle changed, update the needle in the same commit and say so in the report.

- [ ] **Step 5: Commit**

```bash
git add src/XrplVanity.Cli tests/XrplVanity.Tests/CliProgramTests.cs README.md linux/in-container.sh
git commit -m "refactor(cli): drive the search through SearchSession; Program.Run is testable; CPU rate line"
```

---

### Task 7: Linux harness run and design-document "Measured" note

**Files:**
- Modify: `docs/2026-09-05-xrpl-vanity-desktop-design.md` (append a "Measured (Plan F)" subsection under "Plan F — the search layer")

- [ ] **Step 1: Run both harness modes**

```bash
bash linux/run-linux-tests.sh
bash linux/run-linux-tests.sh --cpu-only
```

Expected: both end with the suite green (`Skipped: 0,` in gpu mode) and every smoke `ok`. If a smoke needle fails because of the renamed single-device lines, fix the needle in `linux/in-container.sh` (the contract is the exit code and the `Address:` line, not the diagnostic wording) and re-run.

- [ ] **Step 2: Record the measurements**

Append to the spec, after the "CLI migration" subsection:

```markdown
### Measured (Plan F)

- Windows suite: <N> tests, 0 skipped, <T> s (commit <sha>).
- Linux harness: gpu mode <T> s, cpu-only mode <T> s.
- `--gpu --case-sensitive --starts abcd --count 30` through `SearchSession`: <R>/s (CUDA), versus 19,400,000/s recorded in `docs/benchmarks.md` for the previous loop — the session adds no measurable overhead / adds <x>% (state which).
- CPU rate line, 12 threads, `--case-sensitive --starts abc --count 60`: <R>/s (the first measured CPU rate; `docs/benchmarks.md` estimated 250,000–310,000/s).
```

Fill in every `<…>` with the measured value; a placeholder left in the document is a plan failure. Also add the measured CPU rate to the CPU table in `docs/benchmarks.md` as a new row `xrpl-vanity --case-sensitive --starts abc --count 60 (CPU: line)`.

- [ ] **Step 3: Commit**

```bash
git add docs/2026-09-05-xrpl-vanity-desktop-design.md docs/benchmarks.md linux/in-container.sh
git commit -m "docs: Plan F measurements (session throughput, CPU rate, harness timings)"
```

---

### Task 8: Leading-character model for `Starts` estimates, and the documentation correction

**Why this task exists (added during execution).** The Task 7 measurements showed the CPU rate line at ~115,000–128,000/s while `docs/benchmarks.md` had estimated 250,000–310,000/s from `count × 58^3 / wall time`. The counter is right; the 58^n assumption is wrong for prefixes. A classic address is base58check of `0x00 ‖ 20-byte hash ‖ 4-byte checksum`; the leading zero byte becomes the fixed `r`, and the remaining 192-bit number has `2^192 / 58^32 ≈ 23.34` possible leading base58 digits, so the character right after `r` is (almost) uniform over alphabet indices 1–22 (`pshnaf39wBUDNEGHJKLM4P`), index 23 (`Q`) gets the remaining 0.34 share, and every other character appears there only when the number is short enough to lose a digit (probability `1/23.34`, then uniform over 58). Measured on this machine with `--case-sensitive --starts X --threads 12`: `a` and `p` match once per ~24 candidates, `Q` once per ~62, `c` once per ~1,600 (58 would predict 58 for all of them).

**Files:**
- Modify: `src/XrplVanity.Search/SearchEstimator.cs`
- Modify: `tests/XrplVanity.Tests/Search/SearchEstimatorTests.cs`
- Modify: `docs/benchmarks.md` ("Expected candidates per match" paragraph; the CPU-path table; the "How the figures were obtained" CPU paragraph)
- Modify: `docs/2026-09-05-xrpl-vanity-desktop-design.md` ("Estimator" subsection under Plan F)
- Modify: `README.md` (one sentence in the normalization section about the second character)

**Interfaces:**
- Produces: `SearchEstimator.LeadingDigitCount` (`public const double` = `2^192 / 58^32` written as the literal `23.338`), `SearchEstimator.LeadingCharacterProbability(char c)` (public static, the weights below), unchanged signatures for `ExpectedCandidatesPerMatch` and `ExpectedTimeToMatch`.

- [ ] **Step 1: Update the estimator tests**

Replace the prefix theory and the case-insensitive test in `tests/XrplVanity.Tests/Search/SearchEstimatorTests.cs` with:

```csharp
    private const double Leading = 23.338;

    [Theory]
    [InlineData("a", 1)]
    [InlineData("ab", 58)]
    [InlineData("abc", 3364)]
    [InlineData("abcd", 195_112)]
    public void Case_Sensitive_Prefix_Uses_The_Leading_Digit_Count_Then_58_Per_Character(string pattern, double restFactor)
    {
        // First character after 'r': one of ~23.338 leading digits; every later character: one of 58.
        Assert.Equal(Leading * restFactor, SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, pattern))!.Value, precision: 3);
    }

    [Theory]
    [InlineData('p', 1 / 23.338)]                 // alphabet index 1
    [InlineData('P', 1 / 23.338)]                 // alphabet index 22
    [InlineData('Q', 0.338 / 23.338)]             // alphabet index 23: the partial leading digit
    [InlineData('c', 1 / (23.338 * 58))]          // index 36: only when the number loses a digit
    [InlineData('r', 1 / (23.338 * 58))]          // index 0: same
    [InlineData('z', 1 / (23.338 * 58))]          // index 57: same
    public void Leading_Character_Probability_Follows_The_Base58_Digit_Distribution(char c, double expected)
    {
        Assert.Equal(expected, SearchEstimator.LeadingCharacterProbability(c), precision: 9);
    }

    [Fact]
    public void Leading_Character_Probabilities_Sum_To_About_One()
    {
        double sum = 0;
        foreach (char c in XrplBase58.Alphabet) sum += SearchEstimator.LeadingCharacterProbability(c);
        Assert.InRange(sum, 0.98, 1.0);
    }

    [Fact]
    public void Case_Insensitive_Letters_With_Two_Cases_Double_The_Targets()
    {
        // "ab" ignore-case expands to ab, aB, Ab, AB: a and A both lead ('a' index 5, 'A' index 53).
        double a = SearchEstimator.LeadingCharacterProbability('a') / 58 + SearchEstimator.LeadingCharacterProbability('A') / 58;
        Assert.Equal(1.0 / (2 * a), SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "ab", ignoreCase: true))!.Value, precision: 3);
        // "o" has no upper-case form and "L" no lower-case form: no expansion.
        Assert.Equal(1.0 / SearchEstimator.LeadingCharacterProbability('o'), SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "o", ignoreCase: true))!.Value, precision: 3);
        Assert.Equal(1.0 / SearchEstimator.LeadingCharacterProbability('L'), SearchEstimator.ExpectedCandidatesPerMatch(Set(MatchPosition.Starts, "L", ignoreCase: true))!.Value, precision: 3);
    }

    [Fact]
    public void Several_Patterns_Add_Their_Probabilities()
    {
        PatternSet two = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "a", ignoreCase: false)
            .Add(MatchPosition.Ends, "b", ignoreCase: false)
            .Build();
        double expected = 1.0 / (SearchEstimator.LeadingCharacterProbability('a') + 1.0 / 58);
        Assert.Equal(expected, SearchEstimator.ExpectedCandidatesPerMatch(two)!.Value, precision: 3);
    }
```

Keep the `Ends`, `Contains`, empty-set and ETA tests unchanged (`Ends` and `Contains` are unaffected: the checksum tail and interior positions are uniform).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*SearchEstimatorTests"`
Expected: build error (`LeadingCharacterProbability` missing), or failures on the new expectations.

- [ ] **Step 3: Implement the model**

In `src/XrplVanity.Search/SearchEstimator.cs` add:

```csharp
    /// <summary>
    /// 2^192 / 58^32: the number of base58 digits the character right after the fixed leading
    /// 'r' can take. A classic address encodes 0x00 ‖ hash160 ‖ checksum; the zero byte becomes
    /// 'r' and the remaining 192-bit number has this many possible leading digits, so that
    /// character is (almost) uniform over alphabet indices 1..22, index 23 gets the fractional
    /// remainder, and any other character appears there only when the number is one digit
    /// shorter (probability 1 / LeadingDigitCount, then uniform over 58).
    /// </summary>
    public const double LeadingDigitCount = 23.338;

    /// <summary>Probability that <paramref name="c"/> is the character right after the leading 'r'.</summary>
    public static double LeadingCharacterProbability(char c)
    {
        int index = XrplBase58.Alphabet.IndexOf(c);
        if (index < 0)
        {
            return 0;
        }

        int fullDigits = (int)Math.Floor(LeadingDigitCount);          // 23: indices 1..22 are full
        double remainder = LeadingDigitCount - fullDigits;             // 0.338: index 23's share
        double shorter = 1.0 / (LeadingDigitCount * AlphabetSize);     // a number that lost a digit
        if (index >= 1 && index < fullDigits)
        {
            return 1.0 / LeadingDigitCount;
        }

        if (index == fullDigits)
        {
            return remainder / LeadingDigitCount + shorter;
        }

        return shorter;
    }
```

and change the per-target probability in `ExpectedCandidatesPerMatch`:

```csharp
            double single = position == MatchPosition.Starts
                ? LeadingCharacterProbability(value[0]) * Math.Pow(AlphabetSize, -(value.Length - 1))
                : Math.Pow(AlphabetSize, -value.Length);
```

(`Contains` keeps its position factor on top of `single`; `Ends` uses `single` as before.) Update the class summary comment to describe the leading-character model in one sentence. `XrplBase58.Alphabet` is in `XrplVanity.Core`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*SearchEstimatorTests"`
Expected: all passed. Then the full suite once (`dotnet run --project tests/XrplVanity.Tests -c Release`): 0 failed, 0 skipped.

- [ ] **Step 5: Correct the documentation**

`docs/benchmarks.md`:
- Replace the "Expected candidates per match" paragraph with one that states the model: a case-sensitive prefix of length *n* matches once per `23.34 × 58^(n−1)` candidates when its first character is one of `pshnaf39wBUDNEGHJKLM4P`, once per `69 × 58^(n−1)` for `Q`, and once per `1,354 × 58^(n−1)` for any other first character; suffixes and substrings keep `58^n`; give the measured single-character figures from this task (`a`, `p` ≈ 24; `Q` ≈ 62; `c` ≈ 1,600) as the evidence.
- In the CPU-path table, mark the `≈250,000–310,000/s (estimated…)` row as superseded: keep the row but change its rate cell to "superseded — the estimate assumed 58^3 per match; the measured rate is the `CPU:` row below", so the history stays honest.
- In "How the figures were obtained", the CPU paragraph: replace the `count × 58^length` sentence with the corrected model and say the CLI now counts candidates itself.

`docs/2026-09-05-xrpl-vanity-desktop-design.md`, "Estimator" subsection: replace the sentence about `58^−n` for `Starts` with the leading-character model (one sentence plus the constant), keep `Ends`/`Contains` as they are, and add `LeadingCharacterProbability` to the code block.

`README.md`, in the normalization section after the alphabet sentence, add: "The character right after the leading `r` is not uniform: it is one of `pshnaf39wBUDNEGHJKLM4P` about 94% of the time, `Q` about 1.5%, and anything else only about 0.07% each — so a prefix that starts with, say, `c` or `z` is roughly 58 times rarer than one that starts with `a`."

Run the documentation validator on the three Markdown files: `PYTHONIOENCODING=utf-8 python validate_docs.py docs/benchmarks.md docs/2026-09-05-xrpl-vanity-desktop-design.md README.md`.

- [ ] **Step 6: Commit**

```bash
git add src/XrplVanity.Search/SearchEstimator.cs tests/XrplVanity.Tests/Search/SearchEstimatorTests.cs docs/benchmarks.md docs/2026-09-05-xrpl-vanity-desktop-design.md README.md
git commit -m "fix(search): prefix estimates use the base58 leading-digit distribution; docs: correct the 58^n assumption and the CPU estimate"
```

---

## Self-review

- **Spec coverage.** Types and behaviour of the "Types" and "Behaviour" lists → Tasks 3–4; `DutyPercent` on `MultiDeviceSearch` → Task 2; `FirstAvailableDevice` semantics via `TryOpenFirst` → Tasks 2 and 4; estimator formulas → Task 5; `DeviceCatalog` → Task 5; CLI migration, contract and the CPU line → Task 6; harness → Task 7; `MatchSink` move → Task 1.
- **Placeholders.** The only `<…>` markers are in Task 7's measurement template, which the task explicitly requires to be filled.
- **Type consistency.** `SearchTarget.Cpu(int)`, `SearchTarget.Device(DeviceSelector)`, `SearchTarget.FirstAvailableDevice()` (Task 3) are what Task 4's tests and Task 6's CLI construct; `TargetProgress.Id` for the CPU target is `"cpu"` in Task 4 and Task 6; `MatchSink(int?, Action<Match>)` (Task 1) is what `SearchSession` and `CpuSearchWorkers` use; `DeviceSelector.FirstAvailable.ToString() == "gpu"` (Task 2) matches the unresolved id in Task 4's test.
