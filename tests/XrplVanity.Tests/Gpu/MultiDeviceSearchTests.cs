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

        // CUDA opens by loading precompiled PTX (fast, mostly GPU-side); OpenCl opens by having
        // the driver JIT-compile the kernel source on the CPU, which is what "compute cache"
        // shortens but does not eliminate. Under the CPU contention of a full suite run (~150
        // other tests scheduling concurrently) that JIT step can take seconds, while CUDA's very
        // first launch already finds far more than a handful of "ab"-prefixed hits. A flat
        // match-count race therefore lets CUDA satisfy any modest limit — even thousands of
        // matches across several launches — before OpenCl finishes opening at all, which is not
        // what this test is checking. So the race is gated: `report` discards every match until
        // the log callback has seen both devices announce they opened, plus a settle window for
        // OpenCl to get its own first launch underway; only after that does the shared limit
        // apply. This keeps the same assertions (an exact match count, both devices show
        // CandidatesSearched > 0) but makes the "both actually ran" guarantee deterministic
        // instead of a coin flip against unbounded scheduling delay.
        const int limit = 20;
        List<Match> matches = new();
        HashSet<string> openedSelectors = new(StringComparer.Ordinal);
        object gate = new();
        bool armed = false;

        void ArmAfterSettling()
        {
            Thread.Sleep(TimeSpan.FromSeconds(3));
            lock (gate)
            {
                armed = true;
            }
        }

        void OnLog(string line)
        {
            lock (gate)
            {
                if (line.Contains($"GPU[{cuda}]", StringComparison.Ordinal))
                {
                    openedSelectors.Add("cuda");
                }

                if (line.Contains($"GPU[{opencl}]", StringComparison.Ordinal))
                {
                    openedSelectors.Add("opencl");
                }

                if (!armed && openedSelectors.Count == 2)
                {
                    new Thread(ArmAfterSettling) { IsBackground = true, Name = "arm-after-settling" }.Start();
                }
            }
        }

        PatternSet patterns = FrequentPattern();
        MultiDeviceSearch search = new(new[] { cuda, opencl }, patterns, match =>
        {
            lock (gate)
            {
                if (!armed)
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
        }, OnLog);

        MultiDeviceSearchResult result = search.Run(CancellationToken.None);

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
}
