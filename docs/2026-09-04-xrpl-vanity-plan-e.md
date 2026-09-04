# Multi-Device Orchestration — Plan E Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the vanity search on several devices at once — any mix of CUDA and OpenCL devices — with one shared output, an exactly-enforced `--count`, one Ctrl+C, per-device and aggregate statistics, and failure isolation between devices.

**Architecture:** The CUDA backend learns to enumerate and open every ordinal. A new `MultiDeviceSearch` in `XrplVanity.Gpu` runs one worker thread per selected device (device opened on that thread, one `GpuSearchEngine` per worker, matches handed to a thread-safe callback). The CLI gains `--gpu-devices` and `RunMultiGpu`; the harness gains multi-device smokes in both modes.

**Tech Stack:** .NET 10, xUnit v3 4.0.0 (`dotnet run`, never `dotnet test`), CUDA driver API + OpenCL via the existing P/Invoke layers, Docker harness from Plan D.

**Spec:** `docs/2026-09-04-xrpl-vanity-multigpu-design.md`.

## Global Constraints

- `net10.0`; nullable; `XrplVanity.Gpu` has no packages beyond `XrplVanity.Core`; `dotnet build` never requires CUDA/OpenCL. Kernel/PTX untouched (the drift test guards it).
- A device is opened on the thread that uses it (CUDA context thread binding). `GpuSearchEngine` is unchanged. Every printed match still passes through the engine's CPU re-verification gate.
- `--count` is enforced by `MatchSink.TryReport`'s lock: it returns `false` at the exact call that reaches the limit; the orchestrator stops every worker on the first `false`.
- Failure isolation: a device that fails to open or throws mid-run is recorded (`Failure`) and logged as a warning; the others continue; zero devices opened → CLI exit 2.
- `--gpu` alone stays single-device. `--gpu-devices <spec>` implies `--gpu`; `spec` = `all` | `cuda` | `opencl` | comma-separated `backend:index` or flat `--list-devices` indices. Combining with `--backend`/`--device` is a usage error (exit 2). Empty selection → exit 2 with `no matching device`.
- Output: `GPU[<selector>]: <device name>` on open; per device `GPU[<selector>]: searched N candidates in T s (R/s); consistency errors: E.` or `GPU[<selector>]: failed: <reason>`; total `GPU: searched N candidates on K device(s) in T s (R/s); consistency errors: E.`; exit 0 when `--count` satisfied, 130 otherwise, 2 when no device opened.
- Tests: xUnit v3, built-in `Assert`, `GpuTestHelper.SkipOrFail` for unavailable backends; run with `dotnet run --project tests/XrplVanity.Tests -c Release [-- -class "*X"]`. Warning-free build. Linux harness: `bash linux/run-linux-tests.sh [--cpu-only]` (run in the background with a log; ~5 min each).
- Commit locally after each task, Conventional Commits in English, NO `Co-Authored-By` / attribution trailers, do NOT push or publish.

## File Structure

```
src/XrplVanity.Gpu/CudaContext.cs               # TryEnumerate, TryCreate(ordinal), Ordinal, CudaDeviceInfo (Task 1)
src/XrplVanity.Gpu/Cuda/CudaBackend.cs          # every ordinal (Task 1)
src/XrplVanity.Gpu/MultiDeviceSearch.cs         # DeviceSelector, DeviceSearchStats, MultiDeviceSearchResult, MultiDeviceSearch (Task 2)
src/XrplVanity.Cli/CliOptions.cs                # GpuDevices (Task 3)
src/XrplVanity.Cli/Program.cs                   # ResolveDevices, RunMultiGpu, parse of the spec (Task 3)
README.md                                       # (Task 3, Task 4)
linux/in-container.sh                           # multi-device smokes (Task 4)
docs/2026-09-04-xrpl-vanity-multigpu-design.md  # Measured (Task 4)
tests/XrplVanity.Tests/Gpu/CudaEnumerationTests.cs     # (Task 1)
tests/XrplVanity.Tests/Gpu/MultiDeviceSearchTests.cs   # (Task 2)
tests/XrplVanity.Tests/CliOptionsTests.cs              # (Task 3)
tests/XrplVanity.Tests/Gpu/GpuCliTests.cs              # ResolveDevices tests (Task 3)
```

---

### Task 1: CUDA enumeration for every ordinal

**Files:**
- Modify: `src/XrplVanity.Gpu/CudaContext.cs`, `src/XrplVanity.Gpu/Cuda/CudaBackend.cs`
- Create: `tests/XrplVanity.Tests/Gpu/CudaEnumerationTests.cs`

**Interfaces:**
- Consumes: `CudaDriver.cuInit/cuDeviceGetCount/cuDeviceGet/cuDeviceGetName/cuDeviceGetAttribute/cuCtxCreate`, `GpuDeviceInfo`, `CudaDevice`.
- Produces: `public sealed record CudaDeviceInfo(int Ordinal, string Name, int ComputeCapabilityMajor, int ComputeCapabilityMinor);` `CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out string reason)`; `CudaContext.TryCreate(int ordinal, out CudaContext? context, out string reason)`; `CudaContext.Ordinal`; `CudaBackend.EnumerateDevices()` returning one entry per ordinal; `CudaBackend.Open(index)` for any listed ordinal.

- [ ] **Step 1: Failing tests**

`tests/XrplVanity.Tests/Gpu/CudaEnumerationTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using XrplVanity.Gpu;
using XrplVanity.Gpu.Cuda;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public sealed class CudaEnumerationTests
{
    [Fact]
    public void TryEnumerate_Lists_Every_Ordinal_With_Name_And_Capability()
    {
        if (!CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out string reason))
        {
            GpuTestHelper.SkipOrFail("cuda", reason);
        }

        Assert.NotEmpty(devices);
        for (int i = 0; i < devices.Count; i++)
        {
            Assert.Equal(i, devices[i].Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(devices[i].Name));
            Assert.True(devices[i].ComputeCapabilityMajor >= 1);
        }
    }

    [Fact]
    public void Backend_Enumeration_Matches_TryEnumerate_And_Opens_Each_Ordinal()
    {
        if (!CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out string reason))
        {
            GpuTestHelper.SkipOrFail("cuda", reason);
        }

        CudaBackend backend = new();
        IReadOnlyList<GpuDeviceInfo> listed = backend.EnumerateDevices();
        Assert.Equal(devices.Count, listed.Count);
        for (int i = 0; i < listed.Count; i++)
        {
            Assert.Equal("cuda", listed[i].Backend);
            Assert.Equal(i, listed[i].Index);
            Assert.Equal(devices[i].Name, listed[i].Name);
            Assert.Equal($"sm_{devices[i].ComputeCapabilityMajor}{devices[i].ComputeCapabilityMinor}", listed[i].Details);
            Assert.False(listed[i].IsCpuDevice);

            using IGpuDevice opened = backend.Open(i);
            Assert.Contains(devices[i].Name, opened.Name, StringComparison.Ordinal);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => backend.Open(listed.Count));
    }

    [Fact]
    public void TryCreate_With_An_Out_Of_Range_Ordinal_Reports_The_Range()
    {
        if (!CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out string reason))
        {
            GpuTestHelper.SkipOrFail("cuda", reason);
        }

        Assert.False(CudaContext.TryCreate(devices.Count, out CudaContext? context, out string why));
        Assert.Null(context);
        Assert.Contains("out of range", why, StringComparison.Ordinal);
    }
}
```

Run: `dotnet run --project tests/XrplVanity.Tests -c Release -- -class "*CudaEnumerationTests"` → build errors (missing members).

- [ ] **Step 2: Implement**

`src/XrplVanity.Gpu/CudaContext.cs` — add the record (top-level, same namespace):

```csharp
public sealed record CudaDeviceInfo(int Ordinal, string Name, int ComputeCapabilityMajor, int ComputeCapabilityMinor);
```

Add `public int Ordinal { get; }` (set in the constructor; extend the private constructor with an `int ordinal` parameter) and replace `TryCreate` with:

```csharp
    /// <summary>Creates a context on device 0; returns false with a reason instead of throwing.</summary>
    public static bool TryCreate(out CudaContext? context, out string reason) => TryCreate(0, out context, out reason);

    /// <summary>Creates a context on the given device ordinal; returns false with a reason instead of throwing.</summary>
    public static bool TryCreate(int ordinal, out CudaContext? context, out string reason)
    {
        context = null;
        try
        {
            if (!InitAndCount(out int count, out reason))
            {
                return false;
            }

            if (ordinal < 0 || ordinal >= count)
            {
                reason = $"CUDA device ordinal {ordinal} is out of range (0..{count - 1})";
                return false;
            }

            CudaDeviceInfo info = Describe(ordinal);
            CudaDriver.Check(CudaDriver.cuDeviceGet(out int device, ordinal), "cuDeviceGet");
            CudaDriver.Check(CudaDriver.cuCtxCreate(out IntPtr handle, 0, device), "cuCtxCreate");
            context = new CudaContext(handle, ordinal, info.Name, info.ComputeCapabilityMajor, info.ComputeCapabilityMinor);
            reason = string.Empty;
            return true;
        }
        catch (DllNotFoundException)
        {
            reason = "CUDA driver library not found (nvcuda.dll / libcuda.so.1)";
            return false;
        }
        catch (CudaException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>Lists every CUDA device without creating a context.</summary>
    public static bool TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out string reason)
    {
        devices = Array.Empty<CudaDeviceInfo>();
        try
        {
            if (!InitAndCount(out int count, out reason))
            {
                return false;
            }

            CudaDeviceInfo[] list = new CudaDeviceInfo[count];
            for (int i = 0; i < count; i++)
            {
                list[i] = Describe(i);
            }

            devices = list;
            reason = string.Empty;
            return true;
        }
        catch (DllNotFoundException)
        {
            reason = "CUDA driver library not found (nvcuda.dll / libcuda.so.1)";
            return false;
        }
        catch (CudaException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static bool InitAndCount(out int count, out string reason)
    {
        count = 0;
        int result = CudaDriver.cuInit(0);
        if (result != CudaDriver.CudaSuccess)
        {
            reason = $"cuInit failed with CUDA error {result}";
            return false;
        }

        CudaDriver.Check(CudaDriver.cuDeviceGetCount(out count), "cuDeviceGetCount");
        if (count == 0)
        {
            reason = "no CUDA devices";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static CudaDeviceInfo Describe(int ordinal)
    {
        CudaDriver.Check(CudaDriver.cuDeviceGet(out int device, ordinal), "cuDeviceGet");
        byte[] nameBytes = new byte[256];
        CudaDriver.Check(CudaDriver.cuDeviceGetName(nameBytes, nameBytes.Length, device), "cuDeviceGetName");
        string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
        CudaDriver.Check(CudaDriver.cuDeviceGetAttribute(out int major, CudaDriver.AttributeComputeCapabilityMajor, device), "cuDeviceGetAttribute(major)");
        CudaDriver.Check(CudaDriver.cuDeviceGetAttribute(out int minor, CudaDriver.AttributeComputeCapabilityMinor, device), "cuDeviceGetAttribute(minor)");
        return new CudaDeviceInfo(ordinal, name, major, minor);
    }
```

(`using System.Collections.Generic;` at the top; keep `DeviceName`, `ComputeCapabilityMajor/Minor`, `LoadModule`, `Alloc`, `Launch`, `Synchronize`, `Dispose` untouched; update the class summary to "One CUDA context on one device ordinal. Not thread-safe; one per thread.")

`src/XrplVanity.Gpu/Cuda/CudaBackend.cs`:

```csharp
    public IReadOnlyList<GpuDeviceInfo> EnumerateDevices()
    {
        if (!CudaContext.TryEnumerate(out IReadOnlyList<CudaDeviceInfo> devices, out _))
        {
            return Array.Empty<GpuDeviceInfo>();
        }

        GpuDeviceInfo[] list = new GpuDeviceInfo[devices.Count];
        for (int i = 0; i < devices.Count; i++)
        {
            list[i] = new GpuDeviceInfo(Name, devices[i].Ordinal, devices[i].Name, $"sm_{devices[i].ComputeCapabilityMajor}{devices[i].ComputeCapabilityMinor}");
        }

        return list;
    }

    public IGpuDevice Open(int index)
    {
        if (!CudaContext.TryCreate(index, out CudaContext? context, out string reason))
        {
            if (reason.Contains("out of range", StringComparison.Ordinal))
            {
                throw new ArgumentOutOfRangeException(nameof(index), reason);
            }

            throw new InvalidOperationException($"CUDA is not available: {reason}");
        }

        return new CudaDevice(context!);
    }
```

Remove the "device 0 only" comment. `CudaDevice.Name` stays `$"{_context.DeviceName} (sm_{…})"`.

- [ ] **Step 3: Verify**

Run the class, then `-class "*GpuInfrastructureTests"`, then the whole suite → all green, 0 skipped (150 + 3). `dotnet run --project src/XrplVanity.Cli -c Release -- --list-devices` unchanged on this machine (`0 cuda:0 …`, `1 opencl:0 …`).

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat(gpu): enumerate and open every CUDA device ordinal"
```

---

### Task 2: `MultiDeviceSearch`

**Files:**
- Create: `src/XrplVanity.Gpu/MultiDeviceSearch.cs`, `tests/XrplVanity.Tests/Gpu/MultiDeviceSearchTests.cs`

**Interfaces:**
- Consumes: `GpuBackends.TryOpen(IReadOnlyList<IGpuBackend>, string, int, out IGpuDevice?, out string)` (internal; the tests assembly has `InternalsVisibleTo`), `GpuBackends.All`, `GpuSearchEngine`, `Match`, `PatternSet`, `CudaException`, `OpenClException`.
- Produces (namespace `XrplVanity.Gpu`):

```csharp
public sealed record DeviceSelector(string Backend, int Index) { public override string ToString() => $"{Backend}:{Index}"; }

public sealed class DeviceSearchStats
{
    public DeviceSelector Selector { get; init; } = null!;
    public string? DeviceName { get; internal set; }
    public long CandidatesSearched { get; internal set; }
    public long ConsistencyErrors { get; internal set; }
    public long OverflowDrops { get; internal set; }
    public double Seconds { get; internal set; }
    public string? Failure { get; internal set; }
    public bool Opened => DeviceName is not null;
    public double RatePerSecond => Seconds > 0 ? CandidatesSearched / Seconds : 0;
}

public sealed class MultiDeviceSearchResult
{
    public IReadOnlyList<DeviceSearchStats> Devices { get; init; } = Array.Empty<DeviceSearchStats>();
    public int DevicesOpened { get; init; }
    public long TotalCandidates { get; init; }
    public long TotalConsistencyErrors { get; init; }
    public long TotalOverflowDrops { get; init; }
    public double Seconds { get; init; }
}

public sealed class MultiDeviceSearch
{
    public MultiDeviceSearch(IReadOnlyList<DeviceSelector> devices, PatternSet patterns, Func<Match, bool> report, Action<string>? log = null);
    internal MultiDeviceSearch(IReadOnlyList<IGpuBackend> backends, IReadOnlyList<DeviceSelector> devices, PatternSet patterns, Func<Match, bool> report, Action<string>? log);
    public MultiDeviceSearchResult Run(CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Failing tests**

`tests/XrplVanity.Tests/Gpu/MultiDeviceSearchTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public sealed class MultiDeviceSearchTests
{
    private static PatternSet FrequentPattern() =>
        new PatternSet.Builder().Add(MatchPosition.Starts, "ab", ignoreCase: false).Build();

    private static void RequireSelector(DeviceSelector selector)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(selector.Backend);
        Assert.NotNull(device);
    }

    private static MultiDeviceSearchResult RunWithLimit(IReadOnlyList<DeviceSelector> selectors, int limit, out List<Match> reported)
    {
        List<Match> matches = new();
        object gate = new();
        PatternSet patterns = FrequentPattern();
        MultiDeviceSearch search = new(selectors, patterns, match =>
        {
            lock (gate)
            {
                if (matches.Count >= limit)
                {
                    return false;
                }

                matches.Add(match);
                return matches.Count < limit;
            }
        });
        MultiDeviceSearchResult result = search.Run(CancellationToken.None);
        reported = matches;
        return result;
    }

    [Fact]
    public void Two_Engines_On_The_Same_Cuda_Device_Stop_Exactly_At_The_Limit()
    {
        DeviceSelector cuda = new("cuda", 0);
        RequireSelector(cuda);

        MultiDeviceSearchResult result = RunWithLimit(new[] { cuda, cuda }, limit: 5, out List<Match> reported);

        Assert.Equal(5, reported.Count);
        PatternSet patterns = FrequentPattern();
        Assert.All(reported, m => Assert.True(patterns.TryMatch(m.Address, out _, out _)));
        Assert.Equal(2, result.DevicesOpened);
        Assert.All(result.Devices, d => Assert.Null(d.Failure));
        Assert.All(result.Devices, d => Assert.True(d.CandidatesSearched > 0));
        Assert.Equal(result.Devices.Sum(d => d.CandidatesSearched), result.TotalCandidates);
        Assert.Equal(0, result.TotalConsistencyErrors);
    }

    [Fact]
    public void Cuda_And_OpenCl_Devices_Run_Together()
    {
        DeviceSelector cuda = new("cuda", 0);
        DeviceSelector opencl = new("opencl", 0);
        RequireSelector(cuda);
        RequireSelector(opencl);

        MultiDeviceSearchResult result = RunWithLimit(new[] { cuda, opencl }, limit: 4, out List<Match> reported);

        Assert.Equal(4, reported.Count);
        Assert.Equal(2, result.DevicesOpened);
        Assert.All(result.Devices, d => Assert.Null(d.Failure));
        Assert.Contains(result.Devices, d => d.Selector.Backend == "cuda" && d.CandidatesSearched > 0);
        Assert.Contains(result.Devices, d => d.Selector.Backend == "opencl" && d.CandidatesSearched > 0);
    }

    [Fact]
    public void Caller_Cancellation_Stops_Every_Worker()
    {
        DeviceSelector cuda = new("cuda", 0);
        RequireSelector(cuda);
        // 8 characters case-sensitive: effectively never matches, so only cancellation can end the run.
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(500));
        MultiDeviceSearch search = new(new[] { cuda, cuda }, patterns, _ => true);

        Stopwatch stopwatch = Stopwatch.StartNew();
        MultiDeviceSearchResult result = search.Run(cts.Token);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"workers took {stopwatch.Elapsed} to stop");
        Assert.Equal(2, result.DevicesOpened);
        Assert.All(result.Devices, d => Assert.True(d.CandidatesSearched > 0));
    }

    private sealed class ThrowingBackend : IGpuBackend
    {
        public string Name => "broken";
        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices() => new[] { new GpuDeviceInfo(Name, 0, "broken device", "fake") };
        public IGpuDevice Open(int index) => throw new InvalidOperationException("broken device refuses to open");
    }

    [Fact]
    public void A_Device_That_Fails_To_Open_Does_Not_Stop_The_Others()
    {
        DeviceSelector cuda = new("cuda", 0);
        RequireSelector(cuda);
        List<string> log = new();
        List<IGpuBackend> backends = new(GpuBackends.All) { new ThrowingBackend() };
        List<Match> reported = new();
        object gate = new();
        MultiDeviceSearch search = new(backends, new[] { new DeviceSelector("broken", 0), cuda }, FrequentPattern(),
            match => { lock (gate) { reported.Add(match); return reported.Count < 2; } },
            line => { lock (gate) { log.Add(line); } });

        MultiDeviceSearchResult result = search.Run(CancellationToken.None);

        Assert.Equal(1, result.DevicesOpened);
        DeviceSearchStats broken = Assert.Single(result.Devices, d => d.Selector.Backend == "broken");
        Assert.Contains("refuses to open", broken.Failure);
        Assert.False(broken.Opened);
        Assert.Contains(log, l => l.Contains("broken:0", StringComparison.Ordinal) && l.Contains("could not be opened", StringComparison.Ordinal));
        Assert.Equal(2, reported.Count);
    }

    [Fact]
    public void When_No_Device_Opens_The_Result_Says_So()
    {
        MultiDeviceSearch search = new(new IGpuBackend[] { new ThrowingBackend() },
            new[] { new DeviceSelector("broken", 0), new DeviceSelector("broken", 0) }, FrequentPattern(), _ => true);

        MultiDeviceSearchResult result = search.Run(CancellationToken.None);

        Assert.Equal(0, result.DevicesOpened);
        Assert.Equal(0, result.TotalCandidates);
        Assert.All(result.Devices, d => Assert.NotNull(d.Failure));
    }

    [Fact]
    public void An_Unknown_Backend_Name_Is_A_Failure_Not_An_Exception()
    {
        MultiDeviceSearch search = new(new[] { new DeviceSelector("vulkan", 0) }, FrequentPattern(), _ => true);
        MultiDeviceSearchResult result = search.Run(CancellationToken.None);
        Assert.Equal(0, result.DevicesOpened);
        Assert.Contains("unknown backend", Assert.Single(result.Devices).Failure);
    }
}
```

Run the class → build errors.

- [ ] **Step 2: Implement `src/XrplVanity.Gpu/MultiDeviceSearch.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using XrplVanity.Core;
using XrplVanity.Gpu.OpenCl;

namespace XrplVanity.Gpu;

public sealed record DeviceSelector(string Backend, int Index)
{
    public override string ToString() => $"{Backend}:{Index}";
}

public sealed class DeviceSearchStats
{
    public DeviceSelector Selector { get; init; } = null!;
    public string? DeviceName { get; internal set; }
    public long CandidatesSearched { get; internal set; }
    public long ConsistencyErrors { get; internal set; }
    public long OverflowDrops { get; internal set; }
    public double Seconds { get; internal set; }
    public string? Failure { get; internal set; }
    public bool Opened => DeviceName is not null;
    public double RatePerSecond => Seconds > 0 ? CandidatesSearched / Seconds : 0;
}

public sealed class MultiDeviceSearchResult
{
    public IReadOnlyList<DeviceSearchStats> Devices { get; init; } = Array.Empty<DeviceSearchStats>();
    public int DevicesOpened { get; init; }
    public long TotalCandidates { get; init; }
    public long TotalConsistencyErrors { get; init; }
    public long TotalOverflowDrops { get; init; }
    public double Seconds { get; init; }
}

/// <summary>
/// Runs one <see cref="GpuSearchEngine"/> per selected device, each on its own thread (a device
/// is opened on the thread that uses it), and hands every CPU-verified match to
/// <c>report</c>. <c>report</c> is called concurrently from several threads and must be
/// thread-safe; returning <c>false</c> stops every worker. A device that fails to open or
/// throws mid-run is recorded in its <see cref="DeviceSearchStats.Failure"/> and does not stop
/// the others.
/// </summary>
public sealed class MultiDeviceSearch
{
    private readonly IReadOnlyList<IGpuBackend> _backends;
    private readonly IReadOnlyList<DeviceSelector> _devices;
    private readonly PatternSet _patterns;
    private readonly Func<Match, bool> _report;
    private readonly Action<string> _log;

    public MultiDeviceSearch(IReadOnlyList<DeviceSelector> devices, PatternSet patterns, Func<Match, bool> report, Action<string>? log = null)
        : this(GpuBackends.All, devices, patterns, report, log)
    {
    }

    internal MultiDeviceSearch(IReadOnlyList<IGpuBackend> backends, IReadOnlyList<DeviceSelector> devices, PatternSet patterns, Func<Match, bool> report, Action<string>? log)
    {
        if (devices.Count == 0)
        {
            throw new ArgumentException("At least one device selector is required.", nameof(devices));
        }

        _backends = backends;
        _devices = devices;
        _patterns = patterns;
        _report = report;
        _log = log ?? (_ => { });
    }

    public MultiDeviceSearchResult Run(CancellationToken cancellationToken)
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        DeviceSearchStats[] stats = new DeviceSearchStats[_devices.Count];
        Thread[] workers = new Thread[_devices.Count];
        Stopwatch total = Stopwatch.StartNew();

        for (int i = 0; i < _devices.Count; i++)
        {
            DeviceSearchStats s = new() { Selector = _devices[i] };
            stats[i] = s;
            workers[i] = new Thread(() => Worker(s, stop)) { IsBackground = true, Name = $"gpu-{s.Selector}" };
            workers[i].Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        total.Stop();
        int opened = 0;
        long candidates = 0, errors = 0, drops = 0;
        foreach (DeviceSearchStats s in stats)
        {
            if (s.Opened) opened++;
            candidates += s.CandidatesSearched;
            errors += s.ConsistencyErrors;
            drops += s.OverflowDrops;
        }

        return new MultiDeviceSearchResult
        {
            Devices = stats,
            DevicesOpened = opened,
            TotalCandidates = candidates,
            TotalConsistencyErrors = errors,
            TotalOverflowDrops = drops,
            Seconds = total.Elapsed.TotalSeconds,
        };
    }

    private void Worker(DeviceSearchStats stats, CancellationTokenSource stop)
    {
        Stopwatch open = Stopwatch.StartNew();
        if (!GpuBackends.TryOpen(_backends, stats.Selector.Backend, stats.Selector.Index, out IGpuDevice? device, out string reason))
        {
            stats.Failure = reason;
            _log($"warning: {stats.Selector} could not be opened: {reason}");
            return;
        }

        open.Stop();
        using (device)
        {
            stats.DeviceName = device!.Name;
            _log($"GPU[{stats.Selector}]: {device.Name}");
            if (string.Equals(device.Backend, "opencl", StringComparison.OrdinalIgnoreCase) && open.Elapsed.TotalSeconds > 1.0)
            {
                _log($"note: compiling the OpenCL program for {stats.Selector} took {open.Elapsed.TotalSeconds:F1} s");
            }

            Stopwatch run = Stopwatch.StartNew();
            try
            {
                using GpuSearchEngine engine = new(device, _patterns);
                long reportedOverflow = 0;
                long reportedErrors = 0;
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        foreach (Match match in engine.RunLaunch())
                        {
                            if (!_report(match))
                            {
                                stop.Cancel();
                                break;
                            }
                        }

                        if (engine.OverflowDrops > reportedOverflow)
                        {
                            _log($"warning: {stats.Selector}: {engine.OverflowDrops - reportedOverflow:N0} hits dropped this launch " +
                                 $"(hit buffer holds {GpuSearchEngine.HitCapacity}); use a more selective pattern.");
                            reportedOverflow = engine.OverflowDrops;
                        }

                        if (engine.ConsistencyErrors > reportedErrors)
                        {
                            _log($"GPU consistency error on {stats.Selector}: {engine.ConsistencyErrors - reportedErrors} device hit(s) " +
                                 "the CPU reference could not reproduce were discarded.");
                            reportedErrors = engine.ConsistencyErrors;
                        }
                    }
                }
                finally
                {
                    stats.CandidatesSearched = engine.CandidatesSearched;
                    stats.ConsistencyErrors = engine.ConsistencyErrors;
                    stats.OverflowDrops = engine.OverflowDrops;
                }
            }
            catch (Exception ex) when (ex is CudaException or OpenClException or ArgumentException or InvalidOperationException)
            {
                stats.Failure = ex.Message;
                _log($"warning: {stats.Selector} stopped: {ex.Message}");
            }
            finally
            {
                stats.Seconds = run.Elapsed.TotalSeconds;
            }
        }
    }
}
```

Note: `stop.Cancel()` may be called after `Run` has disposed `stop`? No — `Run` joins every worker before disposing; workers only touch `stop` while alive.

- [ ] **Step 3: Verify**

Run the class → all pass (the cuda-only tests skip on machines without CUDA; the two fake-backend tests run everywhere). Then the whole suite → green, 0 skipped on this machine (150 + 3 + 6). Build warning-free.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat(gpu): MultiDeviceSearch runs one engine per device with a shared thread-safe report callback"
```

---

### Task 3: `--gpu-devices` in the CLI

**Files:**
- Modify: `src/XrplVanity.Cli/CliOptions.cs`, `src/XrplVanity.Cli/Program.cs`, `README.md`, `tests/XrplVanity.Tests/CliOptionsTests.cs`, `tests/XrplVanity.Tests/Gpu/GpuCliTests.cs`

**Interfaces:**
- Consumes: `MultiDeviceSearch`, `DeviceSelector`, `GpuBackends.All`, `MatchSink.TryReport`.
- Produces: `CliOptions.GpuDevices` (`string?`); `SearchRunner.ResolveDevices(CliOptions options, out List<DeviceSelector> selectors, out string reason) -> bool`; `SearchRunner.ParseDeviceSpec(string spec, IReadOnlyList<IGpuBackend> backends, out List<DeviceSelector> selectors, out string reason) -> bool` (internal, testable); `Program.RunMultiGpu`.

- [ ] **Step 1: Failing tests**

Append to `CliOptionsTests`:

```csharp
    [Fact]
    public void Gpu_Devices_Implies_Gpu_And_Conflicts_With_Backend_Or_Device()
    {
        CliOptions options = CliOptions.Parse(new[] { "--gpu-devices", "all", "abc" });
        Assert.True(options.Gpu);
        Assert.Equal("all", options.GpuDevices);

        Assert.Throws<ArgumentException>(() => CliOptions.Parse(new[] { "--gpu-devices", "all", "--backend", "cuda", "abc" }));
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(new[] { "--gpu-devices", "cuda:0", "--device", "0", "abc" }));
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(new[] { "--gpu-devices" }));
        Assert.Null(CliOptions.Parse(new[] { "abc" }).GpuDevices);
    }
```

Append to `GpuCliTests` (a fake backend pair keeps these device-free):

```csharp
    private sealed class FakeBackend : IGpuBackend
    {
        private readonly int _count;
        public FakeBackend(string name, int count) { Name = name; _count = count; }
        public string Name { get; }
        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices()
        {
            GpuDeviceInfo[] list = new GpuDeviceInfo[_count];
            for (int i = 0; i < _count; i++) list[i] = new GpuDeviceInfo(Name, i, $"{Name} device {i}", "fake");
            return list;
        }
        public IGpuDevice Open(int index) => throw new InvalidOperationException("fake");
    }

    [Fact]
    public void ParseDeviceSpec_Handles_All_Backend_Names_Pairs_And_Flat_Indices()
    {
        IGpuBackend[] backends = { new FakeBackend("cuda", 2), new FakeBackend("opencl", 1) };

        Assert.True(SearchRunner.ParseDeviceSpec("all", backends, out List<DeviceSelector> all, out _));
        Assert.Equal(new[] { "cuda:0", "cuda:1", "opencl:0" }, all.Select(s => s.ToString()));

        Assert.True(SearchRunner.ParseDeviceSpec("CUDA", backends, out List<DeviceSelector> cuda, out _));
        Assert.Equal(new[] { "cuda:0", "cuda:1" }, cuda.Select(s => s.ToString()));

        Assert.True(SearchRunner.ParseDeviceSpec("opencl:0, cuda:1", backends, out List<DeviceSelector> pairs, out _));
        Assert.Equal(new[] { "opencl:0", "cuda:1" }, pairs.Select(s => s.ToString()));

        Assert.True(SearchRunner.ParseDeviceSpec("0,2", backends, out List<DeviceSelector> flat, out _));
        Assert.Equal(new[] { "cuda:0", "opencl:0" }, flat.Select(s => s.ToString()));

        Assert.False(SearchRunner.ParseDeviceSpec("vulkan", backends, out _, out string reason1));
        Assert.Contains("vulkan", reason1);
        Assert.False(SearchRunner.ParseDeviceSpec("cuda:7", backends, out _, out string reason2));
        Assert.Contains("cuda:7", reason2);
        Assert.False(SearchRunner.ParseDeviceSpec("9", backends, out _, out string reason3));
        Assert.Contains("9", reason3);
        Assert.False(SearchRunner.ParseDeviceSpec("", backends, out _, out _));
        Assert.False(SearchRunner.ParseDeviceSpec("opencl", new IGpuBackend[] { new FakeBackend("cuda", 1) }, out _, out string reason4));
        Assert.Contains("no matching device", reason4);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void ResolveDevices_All_Includes_Every_Available_Backend(string backend)
    {
        using IGpuDevice probe = GpuTestHelper.RequireDevice(backend);
        probe.Dispose();

        CliOptions options = CliOptions.Parse(new[] { "--gpu-devices", "all", "--starts", "ab" });
        Assert.True(SearchRunner.ResolveDevices(options, out List<DeviceSelector> selectors, out string reason), reason);
        Assert.Contains(selectors, s => s.Backend == backend);
    }
```

(add `using System.Linq;`, `using XrplVanity.Gpu;` as needed.) Run both classes → build errors.

- [ ] **Step 2: Implement**

`CliOptions`: property `public string? GpuDevices { get; init; }`; case `"--gpu-devices"`: `gpuDevices = NextValue(args, ref i, arg);` (use the existing value-taking helper — the one `--backend` uses — so a missing value throws `Option '--gpu-devices' requires a value.`); after the loop: `if (gpuDevices is not null && (backend is not null || device is not null)) throw new ArgumentException("Use either --gpu-devices or --backend/--device, not both.");` and `if (gpuDevices is not null) gpu = true;` (next to the existing implication).

`Program.cs` — in `SearchRunner`:

```csharp
    internal static bool ParseDeviceSpec(string spec, IReadOnlyList<IGpuBackend> backends, out List<DeviceSelector> selectors, out string reason)
    {
        selectors = new List<DeviceSelector>();
        string trimmed = spec.Trim();
        if (trimmed.Length == 0)
        {
            reason = "--gpu-devices needs a value: all, cuda, opencl, or a comma-separated list of backend:index or indices";
            return false;
        }

        if (trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (IGpuBackend backend in backends)
            {
                foreach (GpuDeviceInfo info in backend.EnumerateDevices())
                {
                    selectors.Add(new DeviceSelector(backend.Name, info.Index));
                }
            }

            reason = selectors.Count == 0 ? "no matching device (no CUDA driver and no OpenCL platform)" : string.Empty;
            return selectors.Count > 0;
        }

        IGpuBackend? whole = FindBackend(backends, trimmed);
        if (whole is not null)
        {
            foreach (GpuDeviceInfo info in whole.EnumerateDevices())
            {
                selectors.Add(new DeviceSelector(whole.Name, info.Index));
            }

            reason = selectors.Count == 0 ? $"no matching device for backend '{whole.Name}'" : string.Empty;
            return selectors.Count > 0;
        }

        List<GpuDeviceInfo> flat = new();
        foreach (IGpuBackend backend in backends)
        {
            flat.AddRange(backend.EnumerateDevices());
        }

        foreach (string raw in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = raw.IndexOf(':');
            if (colon > 0)
            {
                IGpuBackend? backend = FindBackend(backends, raw[..colon]);
                if (backend is null || !int.TryParse(raw[(colon + 1)..], out int index) || index < 0)
                {
                    reason = $"'{raw}' is not a known backend:index (known backends: {string.Join(", ", backends.Select(b => b.Name))})";
                    return false;
                }

                if (index >= backend.EnumerateDevices().Count)
                {
                    reason = $"'{raw}' is out of range for backend '{backend.Name}'; run --list-devices";
                    return false;
                }

                selectors.Add(new DeviceSelector(backend.Name, index));
            }
            else if (int.TryParse(raw, out int flatIndex) && flatIndex >= 0)
            {
                if (flatIndex >= flat.Count)
                {
                    reason = $"device index {flatIndex} is out of range; run --list-devices";
                    return false;
                }

                selectors.Add(new DeviceSelector(flat[flatIndex].Backend, flat[flatIndex].Index));
            }
            else
            {
                reason = $"'{raw}' is neither backend:index nor an index from --list-devices";
                return false;
            }
        }

        reason = selectors.Count == 0 ? "no matching device" : string.Empty;
        return selectors.Count > 0;
    }

    private static IGpuBackend? FindBackend(IReadOnlyList<IGpuBackend> backends, string name)
    {
        foreach (IGpuBackend backend in backends)
        {
            if (string.Equals(backend.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return backend;
            }
        }

        return null;
    }

    public static bool ResolveDevices(CliOptions options, out List<DeviceSelector> selectors, out string reason) =>
        ParseDeviceSpec(options.GpuDevices ?? string.Empty, GpuBackends.All, out selectors, out reason);
```

(`using System.Linq;` for the `Select`.) In `Program.Main`, before the single-device block:

```csharp
        List<DeviceSelector>? gpuSelectors = null;
        if (options.GpuDevices is not null)
        {
            if (!SearchRunner.ResolveDevices(options, out gpuSelectors, out string reason))
            {
                Console.Error.WriteLine($"error: --gpu-devices: {reason}");
                return 2;
            }
        }
        else if (options.Gpu) { … existing ResolveDevice block unchanged … }
```

and where the run is dispatched:

```csharp
        if (gpuSelectors is not null)
        {
            int rc = RunMultiGpu(gpuSelectors, patterns, sink, cts);
            if (rc != 0) return rc;
        }
        else if (gpuDevice is not null) { … unchanged … }
        else { RunCpu(…); }
```

`RunMultiGpu`:

```csharp
    private static int RunMultiGpu(IReadOnlyList<DeviceSelector> selectors, PatternSet patterns, MatchSink sink, CancellationTokenSource cts)
    {
        MultiDeviceSearch search = new(selectors, patterns,
            match =>
            {
                bool more = sink.TryReport(match);
                if (!more) cts.Cancel();
                return more;
            },
            line => Console.Error.WriteLine(line));

        MultiDeviceSearchResult result = search.Run(cts.Token);

        foreach (DeviceSearchStats d in result.Devices)
        {
            if (d.Failure is not null && !d.Opened)
            {
                Console.Error.WriteLine($"GPU[{d.Selector}]: failed: {d.Failure}");
                continue;
            }

            double seconds = Math.Max(d.Seconds, 1e-9);
            string tail = d.Failure is null ? string.Empty : $" (stopped: {d.Failure})";
            Console.Error.WriteLine(
                $"GPU[{d.Selector}]: searched {d.CandidatesSearched:N0} candidates in {seconds:F1} s " +
                $"({d.CandidatesSearched / seconds:N0}/s); consistency errors: {d.ConsistencyErrors}.{tail}");
        }

        if (result.DevicesOpened == 0)
        {
            Console.Error.WriteLine("error: --gpu-devices: no device could be opened.");
            return 2;
        }

        double total = Math.Max(result.Seconds, 1e-9);
        Console.Error.WriteLine(
            $"GPU: searched {result.TotalCandidates:N0} candidates on {result.DevicesOpened} device(s) in {total:F1} s " +
            $"({result.TotalCandidates / total:N0}/s); consistency errors: {result.TotalConsistencyErrors}.");
        return 0;
    }
```

(the final `return sink.CountSatisfied ? 0 : 130;` stays as the last statement of `Main`.) Note the `--threads` note condition (`options.Gpu && options.ThreadsSpecified`) already covers the multi-device path because `--gpu-devices` implies `Gpu`.

README: flag table row `--gpu-devices <spec> — run on several devices at once: all, cuda, opencl, or a comma-separated list of backend:index / --list-devices indices (implies --gpu; not combinable with --backend/--device)`; a "Multiple devices" paragraph in the GPU section: what `all` means on a one-card machine (the same card through CUDA and OpenCL — use `cuda` for "all NVIDIA cards"), that each device runs its own engine with its own fresh entropy, that `--count` is shared and exact, that output order across devices is not guaranteed, that several OpenCL devices compile their programs one after another at start-up, and the per-device/total statistics lines.

- [ ] **Step 3: Verify and smoke**

Run `-class "*CliOptionsTests"`, `-class "*GpuCliTests"`, then the whole suite → green, 0 skipped. Smokes (record outputs and exit codes; `$?` in Git Bash right after the command):

```bash
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu-devices all --starts ab --count 3
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu-devices cuda --starts ab --count 2
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu-devices 0,1 --starts ab --count 2
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu-devices all --backend cuda --starts ab
dotnet run --project src/XrplVanity.Cli -c Release -- --gpu-devices vulkan --starts ab
```

Expected: 1–3 print `GPU[cuda:0]: …` (and `GPU[opencl:0]: …` for `all`/`0,1`), exactly `--count` matches, per-device and total lines, exit 0; 4 → usage error, exit 2; 5 → `error: --gpu-devices: …vulkan…`, exit 2. Record the aggregate rate of `all` for Task 4.

- [ ] **Step 4: Commit**

```bash
git add src tests README.md
git commit -m "feat(cli): --gpu-devices runs the search on several devices with shared output and an exact --count"
```

---

### Task 4: Harness smokes, Linux runs, measurements

**Files:**
- Modify: `linux/in-container.sh`, `docs/2026-09-04-xrpl-vanity-multigpu-design.md`, `README.md` (Linux section smoke list)

- [ ] **Step 1: Harness smokes** — add after the `--gpu` smoke, before the cpu-only `--backend cuda` block:

```bash
step "smoke: --gpu-devices all"
set +e; OUT="$("${CLI[@]}" --gpu-devices all --starts ab --count 2 2>&1)"; RC=$?; set -e
expect_exit "multi-device search" 0 "$RC"; expect_contains "opencl worker ran" "GPU[opencl:0]: searched" "$OUT"; expect_contains "no consistency errors" "consistency errors: 0." "$OUT"
if [ "$MODE" = gpu ]; then
    expect_contains "cuda worker ran" "GPU[cuda:0]: searched" "$OUT"; expect_contains "two devices" "on 2 device(s)" "$OUT"
else
    expect_contains "one device" "on 1 device(s)" "$OUT"
    step "smoke: --gpu-devices cuda without a GPU must exit 2"
    set +e; OUT="$("${CLI[@]}" --gpu-devices cuda --starts ab --count 1 2>&1)"; RC=$?; set -e
    expect_exit "no cuda devices" 2 "$RC"; expect_contains "reason printed" "no matching device" "$OUT"
fi
```

Keep LF. Update the README Linux section's smoke list accordingly.

- [ ] **Step 2: Linux runs** (background, logs): `bash linux/run-linux-tests.sh --cpu-only > /tmp/linux-cpu-e.log 2>&1`, then `bash linux/run-linux-tests.sh > /tmp/linux-gpu-e.log 2>&1`. Expected: cpu mode 150+9 tests, cuda rows skipped, `on 1 device(s)`, `done: mode=cpu`; gpu mode 0 skipped, `GPU[cuda:0]` + `GPU[opencl:0]`, `on 2 device(s)`, `done: mode=gpu`; both exit 0. Record the multi-device total line from the gpu log (RTX + pocl aggregate).

- [ ] **Step 3: Measured section** — append to the design doc:

```markdown
## Measured (reference machine, one RTX 5070 Ti)

| Configuration | Result |
| --- | --- |
| Windows, `--gpu-devices all` (cuda:0 + opencl:0 = one card through two APIs), CLI | <total line> |
| Windows, `--gpu-devices cuda` (single engine), CLI | <total line> |
| Linux container, `--gpu-devices all` (RTX via CUDA + pocl on 12 vCPU), CLI | <total line> |
| Two engines on `cuda:0` (test) | limit reached exactly; both workers > 0 candidates |
| Start-up with two OpenCL programs | <observation: serialised compile, note lines> |
```

with the actual values (rates rounded to 100,000/s for GPU, 1,000/s for pocl). State plainly that scaling across two or more NVIDIA cards was not measured.

- [ ] **Step 4: Commit**

```bash
git add linux README.md docs
git commit -m "test(linux): multi-device smokes in both harness modes; docs: measured multi-device results"
```

---

## Notes for the executor

- Never touch `GpuSearchEngine`, the kernels or the PTX. The drift test and the CUDA/OpenCL anchors must stay green after every task.
- `report` runs on worker threads: in the CLI it is `MatchSink.TryReport` (locked). In tests, always lock around shared lists/counters.
- Long harness runs go in the background with a log file; never let a foreground shell call time out mid-run.
- Do not push, publish, or open a pull request.
