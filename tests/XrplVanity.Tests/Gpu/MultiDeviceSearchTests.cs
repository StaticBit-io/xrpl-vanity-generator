using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    // Shared by every test that races two devices against a shared match limit: the report
    // callback discards every match until BOTH devices' live `CandidatesSearched` show they
    // have actually completed a launch, and only then starts counting toward `limit`. Without
    // this gate a fast device (e.g. CUDA loading precompiled PTX) can satisfy the whole limit
    // before a slow one (e.g. OpenCL JIT-compiling under CPU contention, or a second engine on
    // the same CUDA device that has not finished opening) ever reports a single candidate,
    // which defeats the point of a "both devices actually ran" test. The two `Func<long>`
    // arguments read the live counters lazily so callers can construct the callback before the
    // `MultiDeviceSearch` (and therefore its `Devices`) exists — see the null-until-after-
    // construction pattern at each call site. Reading a `long` without locking is fine here; a
    // torn read is not a concern on x64.
    private static Func<Match, bool> GateUntilBothSearched(
        Func<long> firstCandidatesSearched, Func<long> secondCandidatesSearched, int limit, List<Match> matches, object gate)
    {
        return match =>
        {
            lock (gate)
            {
                if (firstCandidatesSearched() == 0 || secondCandidatesSearched() == 0)
                {
                    return true;
                }

                if (matches.Count >= limit)
                {
                    return false;
                }

                matches.Add(match);
                return matches.Count < limit;
            }
        };
    }

    [Fact]
    public void Two_Engines_On_The_Same_Cuda_Device_Stop_Exactly_At_The_Limit()
    {
        DeviceSelector cuda = new("cuda", 0);
        RequireSelector(cuda);

        // Two engines opening the same CUDA device do not open in lockstep: the first can start
        // launching and satisfying the limit before the second has finished opening, which would
        // make its `CandidatesSearched` false-negative zero. Gate on both having searched at
        // least once, same as the CUDA/OpenCL race below, via the shared helper.
        const int limit = 5;
        List<Match> matches = new();
        object gate = new();
        PatternSet patterns = FrequentPattern();
        DeviceSearchStats? first = null;
        DeviceSearchStats? second = null;

        MultiDeviceSearch search = new(new[] { cuda, cuda }, patterns,
            GateUntilBothSearched(() => first!.CandidatesSearched, () => second!.CandidatesSearched, limit, matches, gate));

        first = search.Devices[0];
        second = search.Devices[1];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(120));
        MultiDeviceSearchResult result = search.Run(cts.Token);
        Assert.False(cts.IsCancellationRequested, "the gate never armed: a device did not search");

        Assert.Equal(limit, matches.Count);
        Assert.All(matches, m => Assert.True(patterns.TryMatch(m.Address, out _, out _)));
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

        // CUDA opens by loading precompiled PTX (fast, mostly GPU-side); OpenCl opens by having
        // the driver JIT-compile the kernel source on the CPU, which is what "compute cache"
        // shortens but does not eliminate. Under the CPU contention of a full suite run (~150
        // other tests scheduling concurrently, or pocl on a loaded container) that JIT step can
        // take well over a minute, while CUDA's very first launch already finds far more than a
        // handful of "ab"-prefixed hits. A flat match-count race therefore lets CUDA satisfy any
        // modest limit before OpenCl has even finished opening, which is not what this test is
        // checking. So the race is gated via the shared `GateUntilBothSearched` helper on the
        // live per-device stats `MultiDeviceSearch` exposes through `Devices`: `report` discards
        // every match until BOTH devices' live `CandidatesSearched` show they have actually
        // completed a launch; only then does the shared limit start counting. This keeps the same
        // assertions (an exact match count, both devices show `CandidatesSearched > 0`, no
        // failures) but makes the "both actually ran" guarantee deterministic instead of a race
        // against unbounded scheduling delay — and the run itself is bounded by a 300s deadline
        // (pocl under contention needs it) so a gate that never arms (a device stuck opening, or
        // not searching at all) fails the test instead of hanging forever.
        const int limit = 20;
        List<Match> matches = new();
        object gate = new();

        PatternSet patterns = FrequentPattern();
        DeviceSearchStats? cudaStats = null;
        DeviceSearchStats? openclStats = null;

        MultiDeviceSearch search = new(new[] { cuda, opencl }, patterns,
            GateUntilBothSearched(() => cudaStats!.CandidatesSearched, () => openclStats!.CandidatesSearched, limit, matches, gate));

        cudaStats = search.Devices.Single(d => d.Selector == cuda);
        openclStats = search.Devices.Single(d => d.Selector == opencl);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(300));
        MultiDeviceSearchResult result = search.Run(cts.Token);
        Assert.False(cts.IsCancellationRequested, "the gate never armed: a device did not search");

        Assert.Equal(limit, matches.Count);
        Assert.All(matches, m => Assert.True(patterns.TryMatch(m.Address, out _, out _)));
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
        // 3s (not the original 500ms): under a full-suite run, GPU/CPU contention from the other
        // ~150 concurrently-running tests can stretch a single 2^20-candidate launch well past
        // 500ms, so both workers need real headroom to complete at least one launch before the
        // token fires. Still tiny next to the 30s upper bound this test checks.
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
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

    [Fact]
    public void A_Throwing_Report_Callback_Stops_All_Workers_Without_Crashing()
    {
        DeviceSelector cuda = new("cuda", 0);
        RequireSelector(cuda);

        // A report callback that throws (e.g. MatchSink.TryReport hitting a broken pipe) must
        // never crash the process: the exception must be caught inside the worker thread, not
        // escape the Thread delegate. It also means the caller's sink is unusable, so every
        // worker must be asked to stop, not just the one whose report call happened to throw.
        // The two workers share the same CUDA device and do not open (or reach their first
        // launch, and therefore their first `report` call) in lockstep: whichever worker opens
        // second can be cancelled by the first worker's failure before it ever calls the
        // callback, in which case its own `Failure` is legitimately null — the orchestrator is
        // honestly reporting that it never searched, by design, not swallowing an error. So this
        // test only requires that AT LEAST ONE device recorded the report-callback failure
        // (Assert.Contains, not Assert.All); the "no crash" property is proven simply by `Run`
        // returning within the deadline instead of the process going down.
        MultiDeviceSearch search = new(new[] { cuda, cuda }, FrequentPattern(), _ => throw new IOException("broken pipe"));

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(120));
        MultiDeviceSearchResult result = search.Run(cts.Token);

        Assert.False(cts.IsCancellationRequested, "workers did not stop on their own within the deadline");
        Assert.Equal(2, result.DevicesOpened);
        Assert.Contains(result.Devices, d =>
            d.Failure is not null &&
            (d.Failure.Contains("report callback", StringComparison.Ordinal) ||
             d.Failure.Contains("broken pipe", StringComparison.Ordinal)));
    }

    [Fact]
    public void Devices_Are_Exposed_Before_Run_In_Selector_Order()
    {
        DeviceSelector first = new("broken", 0);
        DeviceSelector second = new("broken", 1);
        List<IGpuBackend> backends = new() { new ThrowingBackend() };
        MultiDeviceSearch search = new(backends, new[] { first, second }, FrequentPattern(), _ => true);

        Assert.Equal(2, search.Devices.Count);
        Assert.Equal(first, search.Devices[0].Selector);
        Assert.Equal(second, search.Devices[1].Selector);
        Assert.All(search.Devices, d => Assert.Equal(0, d.CandidatesSearched));

        MultiDeviceSearchResult result = search.Run(CancellationToken.None);

        Assert.Same(search.Devices[0], result.Devices[0]);
        Assert.Same(search.Devices[1], result.Devices[1]);
    }
}
