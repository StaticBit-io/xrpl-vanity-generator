using System;
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

    private static byte[] MatchOnDevice(byte[] records, int n, TargetTable table)
    {
        using CudaContext context = GpuTestHelper.RequireContext();
        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        CudaFunction fn = module.GetFunction("k_match_records");

        using DeviceBuffer drec = context.Alloc(records.Length);
        using DeviceBuffer dgroups = context.Alloc(Math.Max(4, table.Groups.Length * 4));
        using DeviceBuffer dchars = context.Alloc(Math.Max(1, table.Chars.Length));
        using DeviceBuffer dflags = context.Alloc(n);
        drec.CopyFrom(records);
        if (table.Groups.Length > 0)
        {
            dgroups.CopyFrom(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(table.Groups)));
        }

        if (table.Chars.Length > 0)
        {
            dchars.CopyFrom(table.Chars);
        }

        context.Launch(fn, (n + 255) / 256, 256, drec, n, dgroups, table.GroupCount, dchars, dflags);
        context.Synchronize();

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

    [Fact]
    public void Device_Predicate_Equals_Cpu_TryMatch_Over_Real_Addresses()
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

        byte[] gpu = MatchOnDevice(Records(addresses), addresses.Length, TargetTable.Build(set));

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

    [Fact]
    public void Each_Position_Is_Checked_Independently()
    {
        string[] addresses =
        {
            "rABCdefghijkmnpqstuvwxyz23456789ABC", // starts "ABC", ends "ABC", contains "ABC"
            "rZZZdefghijkmnpqstuvwxyz23456789ZZZ",
            "rQQQdefghijkmnpqstuvwxyz23456789ZZZ",
        };
        byte[] records = Records(addresses);

        byte[] starts = MatchOnDevice(records, 3, TargetTable.Build(new PatternSet.Builder().Add(MatchPosition.Starts, "ABC", ignoreCase: false).Build()));
        byte[] ends = MatchOnDevice(records, 3, TargetTable.Build(new PatternSet.Builder().Add(MatchPosition.Ends, "ZZZ", ignoreCase: false).Build()));
        byte[] contains = MatchOnDevice(records, 3, TargetTable.Build(new PatternSet.Builder().Add(MatchPosition.Contains, "hijk", ignoreCase: false).Build()));

        Assert.Equal(new byte[] { 1, 0, 0 }, starts);
        Assert.Equal(new byte[] { 0, 1, 1 }, ends);
        Assert.Equal(new byte[] { 1, 1, 1 }, contains);
    }

    [Fact]
    public void Empty_Table_Matches_Nothing()
    {
        string[] addresses = DeriveAddresses(64, 7);
        TargetTable empty = TargetTable.Build(new PatternSet.Builder().Build());

        byte[] gpu = MatchOnDevice(Records(addresses), addresses.Length, empty);

        Assert.All(gpu, f => Assert.Equal(0, f));
    }
}
