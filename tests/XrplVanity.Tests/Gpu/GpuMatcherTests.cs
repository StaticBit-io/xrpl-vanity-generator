using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests.Gpu;

public class GpuMatcherTests
{
    private static byte[] Records(string[] addresses)
    {
        byte[] records = new byte[addresses.Length * 36];
        for (int i = 0; i < addresses.Length; i++)
        {
            records[i * 36] = (byte)addresses[i].Length;
            Encoding.ASCII.GetBytes(addresses[i], 0, addresses[i].Length, records, i * 36 + 1);
        }

        return records;
    }

    private static byte[] MatchOnDevice(string backend, byte[] records, int n, TargetTable table)
    {
        using IGpuDevice device = GpuTestHelper.RequireDevice(backend);
        IGpuKernel fn = device.GetKernel("k_match_records");

        using IGpuBuffer drec = device.Alloc(records.Length);
        using IGpuBuffer dgroups = device.Alloc(Math.Max(4, table.Groups.Length * 4));
        using IGpuBuffer dchars = device.Alloc(Math.Max(1, table.Chars.Length));
        using IGpuBuffer dpairs = device.Alloc(Math.Max(4, table.Pairs.Length * 4));
        using IGpuBuffer dflags = device.Alloc(n);
        drec.CopyFrom(records);
        if (table.Groups.Length > 0)
        {
            dgroups.CopyFrom(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(table.Groups)));
        }

        if (table.Chars.Length > 0)
        {
            dchars.CopyFrom(table.Chars);
        }

        if (table.Pairs.Length > 0)
        {
            dpairs.CopyFrom(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(table.Pairs)));
        }

        device.Launch(fn, n, 256, drec, n, dgroups, table.GroupCount, dchars, dpairs, table.PairCount, dflags);
        device.Synchronize();

        byte[] flags = new byte[n];
        dflags.CopyTo(flags);
        return flags;
    }

    private static string[] DeriveAddresses(int count, int seed)
    {
        Random rng = new(seed);
        BouncyCastleDeriver deriver = new();
        Span<char> buffer = stackalloc char[48];
        byte[] entropy = new byte[16];
        string[] addresses = new string[count];
        for (int i = 0; i < count; i++)
        {
            rng.NextBytes(entropy);
            int len = deriver.DeriveAddress(entropy, buffer);
            addresses[i] = new string(buffer[..len]);
        }

        return addresses;
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Device_Predicate_Equals_Cpu_TryMatch_Over_Real_Addresses(string backend)
    {
        // Short patterns so every position gets plenty of hits among 20,000 real addresses.
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "a", ignoreCase: true)
            .Add(MatchPosition.Starts, "pQ", ignoreCase: false)
            .Add(MatchPosition.Ends, "z", ignoreCase: true)
            .Add(MatchPosition.Ends, "9", ignoreCase: false)
            .Add(MatchPosition.Contains, "ab", ignoreCase: true)
            .Add(MatchPosition.Contains, "xyz", ignoreCase: false)
            .Build();
        string[] addresses = DeriveAddresses(20_000, 20260903);

        byte[] gpu = MatchOnDevice(backend, Records(addresses), addresses.Length, TargetTable.Build(set));

        int hits = 0;
        for (int i = 0; i < addresses.Length; i++)
        {
            bool cpu = set.TryMatch(addresses[i], out _, out _);
            Assert.True(cpu == (gpu[i] == 1), $"index {i} {addresses[i]}: cpu={cpu} gpu={gpu[i]}");
            if (cpu)
            {
                hits++;
            }
        }

        Assert.True(hits > 100, $"only {hits} hits; the pattern set should hit often"); // sanity: the test exercised real matches
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Each_Position_Is_Checked_Independently(string backend)
    {
        string[] addresses =
        {
            "rABCdefghijkmnpqstuvwxyz23456789ABC", // starts "ABC", ends "ABC", contains "ABC"
            "rZZZdefghijkmnpqstuvwxyz23456789ZZZ",
            "rQQQdefghijkmnpqstuvwxyz23456789ZZZ",
        };
        byte[] records = Records(addresses);

        byte[] starts = MatchOnDevice(backend, records, 3, TargetTable.Build(new PatternSet.Builder().Add(MatchPosition.Starts, "ABC", ignoreCase: false).Build()));
        byte[] ends = MatchOnDevice(backend, records, 3, TargetTable.Build(new PatternSet.Builder().Add(MatchPosition.Ends, "ZZZ", ignoreCase: false).Build()));
        byte[] contains = MatchOnDevice(backend, records, 3, TargetTable.Build(new PatternSet.Builder().Add(MatchPosition.Contains, "hijk", ignoreCase: false).Build()));

        Assert.Equal(new byte[] { 1, 0, 0 }, starts);
        Assert.Equal(new byte[] { 0, 1, 1 }, ends);
        Assert.Equal(new byte[] { 1, 1, 1 }, contains);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void The_Device_Agrees_With_The_Cpu_On_Paired_Patterns(string backend)
    {
        PatternSet set = new PatternSet.Builder()
            .AddPair("ab", "yz", ignoreCase: false)
            .AddPair("cd", "wx", ignoreCase: true)
            .Add(MatchPosition.Starts, "qq", ignoreCase: false)
            .Build();

        // Every interesting case: both halves, one half, neither, halves that touch exactly, one
        // rule's prefix against another rule's suffix, and bodies too short to hold either half.
        List<string> addresses = new()
        {
            "rabQQQQQQQyz", "rabQQQQQQQQQ", "rQQQQQQQQQyz", "rQQQQQQQQQQQ",
            "rCDQQQQQQQWx", "rcdQQQQQQQwx", "rqqQQQQQQQQQ", "rabyz", "rab", "ryz",
            "rabQQQQQQQwx", "rcdQQQQQQQyz", "rz",
        };
        addresses.AddRange(DeriveAddresses(200, 20260906));

        string[] corpus = addresses.ToArray();
        byte[] deviceFlags = MatchOnDevice(backend, Records(corpus), corpus.Length, TargetTable.Build(set));

        for (int i = 0; i < corpus.Length; i++)
        {
            bool cpu = set.TryMatch(corpus[i], out _, out _);
            Assert.True(cpu == (deviceFlags[i] == 1), $"index {i} {corpus[i]}: cpu={cpu} gpu={deviceFlags[i]}");
        }
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void The_Device_Agrees_With_The_Cpu_On_NearStart_Patterns(string backend)
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.NearStart, "X", ignoreCase: false)
            .Add(MatchPosition.NearStart, "abc", ignoreCase: true)
            .Build();

        // Offset 0, offset 1, offset 2 (no match), and bodies with room for offset 0 only.
        List<string> addresses = new()
        {
            "rXQQQQQQQQ", "rQXQQQQQQQ", "rQQXQQQQQQ", "rX", "rQX", "rQQ",
            "rAbCQQQQQQ", "rQaBcQQQQQ", "rQQabcQQQQ", "rabc", "rQab",
        };
        addresses.AddRange(DeriveAddresses(20_000, 20261102));

        string[] corpus = addresses.ToArray();
        byte[] deviceFlags = MatchOnDevice(backend, Records(corpus), corpus.Length, TargetTable.Build(set));

        int hits = 0;
        for (int i = 0; i < corpus.Length; i++)
        {
            bool cpu = set.TryMatch(corpus[i], out _, out _);
            Assert.True(cpu == (deviceFlags[i] == 1), $"index {i} {corpus[i]}: cpu={cpu} gpu={deviceFlags[i]}");
            if (cpu) hits++;
        }

        Assert.Equal(new byte[] { 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 0 }, deviceFlags[..11]);
        Assert.True(hits > 200, $"only {hits} hits; 'X' one character in should hit about 1 in 58");
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void The_Device_Agrees_With_The_Cpu_On_NearStart_Pairs(string backend)
    {
        // "X..Z" shares its starts half with "X..Y" but not the near-start choice, so the device
        // must keep the two halves in separate groups.
        PatternSet set = new PatternSet.Builder()
            .AddPair("X", "Y", ignoreCase: false, nearStart: true)
            .AddPair("X", "Z", ignoreCase: false)
            .AddPair("ab", "cd", ignoreCase: true, nearStart: true)
            .Build();

        // Offsets 0, 1, 2; halves touching at each offset; the plain pair one character in;
        // a case-folded pair one character in.
        List<string> addresses = new()
        {
            "rXQQQQY", "rQXQQQY", "rQQXQQY", "rXY", "rQXY", "rXQ",
            "rQXQQZ", "rXQQQZ", "rQaBQQCd", "rQQabcd",
        };
        addresses.AddRange(DeriveAddresses(20_000, 20261103));

        string[] corpus = addresses.ToArray();
        byte[] deviceFlags = MatchOnDevice(backend, Records(corpus), corpus.Length, TargetTable.Build(set));

        for (int i = 0; i < corpus.Length; i++)
        {
            bool cpu = set.TryMatch(corpus[i], out _, out _);
            Assert.True(cpu == (deviceFlags[i] == 1), $"index {i} {corpus[i]}: cpu={cpu} gpu={deviceFlags[i]}");
        }

        Assert.Equal(new byte[] { 1, 1, 0, 1, 1, 0, 0, 1, 1, 0 }, deviceFlags[..10]);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Empty_Table_Matches_Nothing(string backend)
    {
        string[] addresses = DeriveAddresses(64, 7);
        TargetTable empty = TargetTable.Build(new PatternSet.Builder().Build());

        byte[] gpu = MatchOnDevice(backend, Records(addresses), addresses.Length, empty);

        Assert.All(gpu, f => Assert.Equal(0, f));
    }
}
