using System;
using System.Collections.Generic;
using System.Diagnostics;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuSearchTests
{
    private readonly ITestOutputHelper _output;

    public GpuSearchTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Finds_Cpu_Verified_Matches_Without_Overflow(string backend)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        // "abc" case-insensitive = 8 variants / 58^3 ~ 4e-5 per candidate -> ~40 hits per 2^20 candidates.
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "abc", ignoreCase: true).Build();
        using GpuSearchEngine engine = new(device, patterns);

        List<Match> matches = engine.RunLaunch();

        Assert.Equal(GpuSearchEngine.CandidatesPerLaunch, engine.CandidatesSearched);
        Assert.True(matches.Count > 0, "expected roughly 40 hits in 2^20 candidates");
        Assert.Equal(0, engine.ConsistencyErrors);
        Assert.Equal(0, engine.OverflowDrops);

        // The seed must re-derive to the address (the gate already checked this; pin it here too).
        BouncyCastleDeriver deriver = new();
        Span<char> buffer = stackalloc char[48];
        foreach (Match m in matches)
        {
            Assert.True(patterns.TryMatch(m.Address, out _, out _), $"{m.Address} does not match on the CPU");
            Assert.Equal(MatchPosition.Starts, m.Position);
            int len = deriver.DeriveAddress(Seed.DecodeEntropy(m.Seed), buffer);
            Assert.Equal(m.Address, new string(buffer[..len]));
        }
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Overflow_Is_Counted_And_Returned_Hits_Are_Capped(string backend)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        // "a" case-insensitive = 2 / 58 ~ 3.4% -> ~36,000 hits per launch, far above the 4,096 slots.
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "a", ignoreCase: true).Build();
        using GpuSearchEngine engine = new(device, patterns);

        List<Match> matches = engine.RunLaunch();

        Assert.Equal(GpuSearchEngine.HitCapacity, matches.Count);
        Assert.True(engine.OverflowDrops > 0, "overflow must be counted, not silent");
        Assert.Equal(0, engine.ConsistencyErrors);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Each_Launch_Uses_Fresh_Entropy(string backend)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "ab", ignoreCase: true).Build();
        using GpuSearchEngine engine = new(device, patterns);

        List<Match> first = engine.RunLaunch();
        List<Match> second = engine.RunLaunch();

        Assert.True(first.Count > 0 && second.Count > 0);
        HashSet<string> seeds = new(StringComparer.Ordinal);
        foreach (Match m in first) seeds.Add(m.Seed);
        foreach (Match m in second)
        {
            Assert.True(seeds.Add(m.Seed), $"seed {m.Seed} repeated across launches");
        }

        Assert.Equal(2L * GpuSearchEngine.CandidatesPerLaunch, engine.CandidatesSearched);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Rejects_A_Pattern_Set_With_No_Targets(string backend)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        PatternSet empty = new PatternSet.Builder().Add(MatchPosition.Starts, "bit@coin", ignoreCase: true).Build();

        Assert.Throws<ArgumentException>(() => new GpuSearchEngine(device, empty));
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Throughput_Meets_The_Floor_For_The_Device_Class(string backend)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        // 8 characters case-sensitive: effectively never matches, so this measures pure search throughput.
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();
        using GpuSearchEngine engine = new(device, patterns);
        engine.RunLaunch(); // warm-up (module JIT, first launch)

        Stopwatch stopwatch = Stopwatch.StartNew();
        const int launches = 4;
        for (int i = 0; i < launches; i++)
        {
            engine.RunLaunch();
        }

        double seconds = stopwatch.Elapsed.TotalSeconds;
        double rate = (double)launches * GpuSearchEngine.CandidatesPerLaunch / seconds;

        long floor = device.IsCpuDevice ? 10_000 : 500_000;
        _output.WriteLine($"GPU search throughput: {rate:N0} candidates/s on {engine.DeviceName} (floor {floor:N0}/s, {(device.IsCpuDevice ? "CPU" : "GPU")} device)");
        Assert.True(rate >= floor, $"throughput {rate:N0}/s is below the {floor:N0}/s floor for a {(device.IsCpuDevice ? "CPU" : "GPU")} device");
    }
}
