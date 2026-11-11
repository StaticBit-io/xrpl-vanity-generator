using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

    /// <summary>
    /// Each device thread derives a batch of candidates so that one field inversion can
    /// serve all of them, which means the launch divides the range by the batch size. Get
    /// that divisor wrong and the search silently skips or repeats counters while every
    /// address it does return still verifies. Drive k_search over a known key and counter
    /// range and require its hits to be exactly the hits a CPU scan of the same counters
    /// finds: no gaps, no repeats.
    /// </summary>
    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Search_Covers_Every_Counter_In_The_Range_Exactly_Once(string backend)
    {
        const int Count = 8192;
        const ulong Start = 1_234_567_890UL;
        byte[] key = new byte[32];
        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(i * 7 + 1);
        }

        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, "a", ignoreCase: false).Build();

        HashSet<string> expected = ExpectedHits(key, Start, Count, patterns);
        Assert.True(expected.Count > 100, $"the pattern must hit often enough to be sensitive; got {expected.Count}");

        List<string> actual = DeviceHits(backend, key, Start, Count, patterns);

        Assert.Equal(actual.Count, actual.Distinct().Count());
        Assert.Equal(expected.OrderBy(h => h, StringComparer.Ordinal), actual.OrderBy(h => h, StringComparer.Ordinal));
    }

    /// <summary>The same PRF and derivation the kernel runs, counter by counter, on the CPU.</summary>
    private static HashSet<string> ExpectedHits(byte[] key, ulong start, int count, PatternSet patterns)
    {
        BouncyCastleDeriver deriver = new();
        HashSet<string> hits = new(StringComparer.Ordinal);
        byte[] message = new byte[40];
        key.CopyTo(message, 0);
        Span<char> address = stackalloc char[48];
        Span<byte> digest = stackalloc byte[64];
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(32), start + (ulong)i);
            SHA512.HashData(message, digest);
            ReadOnlySpan<byte> entropy = digest[..16];
            int length = deriver.DeriveAddress(entropy, address);
            if (patterns.TryMatch(new string(address[..length]), out _, out _))
            {
                hits.Add(Convert.ToHexString(entropy));
            }
        }

        return hits;
    }

    private static List<string> DeviceHits(string backend, byte[] key, ulong start, int count, PatternSet patterns)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        TargetTable targets = TargetTable.Build(patterns);

        using IGpuBuffer table = device.Alloc(256 * 96);
        device.Launch(device.GetKernel("k_build_base_table"), 256, 128, table);
        device.Synchronize();

        using IGpuBuffer dkey = device.Alloc(32);
        dkey.CopyFrom(key);
        using IGpuBuffer groups = device.Alloc(Math.Max(4, targets.Groups.Length * 4));
        groups.CopyFrom(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(targets.Groups)));
        using IGpuBuffer chars = device.Alloc(Math.Max(1, targets.Chars.Length));
        if (targets.Chars.Length > 0)
        {
            chars.CopyFrom(targets.Chars);
        }

        using IGpuBuffer pairs = device.Alloc(Math.Max(4, targets.Pairs.Length * 4));
        using IGpuBuffer hits = device.Alloc(GpuSearchEngine.HitCapacity * 52);
        using IGpuBuffer hitCount = device.Alloc(4);
        hitCount.Clear();

        int threads = (count + GpuSearchEngine.CandidateBatch - 1) / GpuSearchEngine.CandidateBatch;
        device.Launch(device.GetKernel("k_search"), threads, 128,
            dkey, start, count, groups, targets.GroupCount, chars, pairs, targets.PairCount,
            hits, hitCount, GpuSearchEngine.HitCapacity, table);
        device.Synchronize();

        byte[] countBytes = new byte[4];
        hitCount.CopyTo(countBytes);
        uint found = BitConverter.ToUInt32(countBytes, 0);
        Assert.True(found <= GpuSearchEngine.HitCapacity, $"the hit buffer overflowed with {found} hits; the test range is too dense");

        byte[] records = new byte[GpuSearchEngine.HitCapacity * 52];
        hits.CopyTo(records);
        List<string> result = new((int)found);
        for (int i = 0; i < (int)found; i++)
        {
            result.Add(Convert.ToHexString(records.AsSpan(i * 52, 16)));
        }

        return result;
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
    [Trait(GpuTestHelper.Category, GpuTestHelper.RealGpu)]
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
