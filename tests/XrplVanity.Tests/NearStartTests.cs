using System;
using System.Linq;
using XrplVanity.Cli;
using XrplVanity.Core;
using XrplVanity.Gpu;
using Xunit;

namespace XrplVanity.Tests;

public class NearStartTests
{
    private static PatternSet Near(string pattern, bool ignoreCase = false) =>
        new PatternSet.Builder().Add(MatchPosition.NearStart, pattern, ignoreCase).Build();

    [Theory]
    [InlineData("rXRPLFiLEmQdynWqgxoNv4CHsqFHdgsnpJ")]
    [InlineData("rpXRPLFiLEQdynWqgxoNv4CHsqFHdgsnpJ")]
    public void Matches_At_The_Second_Or_Third_Character(string address)
    {
        Assert.True(Near("XRPLFiLE").TryMatch(address, out MatchPosition position, out string pattern));
        Assert.Equal(MatchPosition.NearStart, position);
        Assert.Equal("XRPLFiLE", pattern);
    }

    [Theory]
    [InlineData("rppXRPLFiLEdynWqgxoNv4CHsqFHdgsnpJ")]
    [InlineData("rpXRPLFiL")]
    public void Does_Not_Match_Further_In_Or_Past_The_End(string address)
    {
        Assert.False(Near("XRPLFiLE").TryMatch(address, out _, out _));
    }

    [Fact]
    public void A_Body_Exactly_The_Pattern_Long_Still_Matches_At_Its_Start()
    {
        Assert.True(Near("XRPLFiLE").TryMatch("rXRPLFiLE", out _, out _));
    }

    [Fact]
    public void Enumerates_Every_Case_Variant_As_NearStart()
    {
        (MatchPosition Position, string Value)[] targets = Near("ab", ignoreCase: true).EnumerateTargets().ToArray();

        Assert.All(targets, t => Assert.Equal(MatchPosition.NearStart, t.Position));
        Assert.Equal(new[] { "AB", "Ab", "aB", "ab" }, targets.Select(t => t.Value).OrderBy(v => v, StringComparer.Ordinal));
    }

    [Fact]
    public void The_Estimate_Adds_A_Plain_58_To_The_Length_For_The_Second_Offset()
    {
        double starts = SearchEstimator.StartsProbability("XRPLFiLE");
        double expected = 1.0 / (starts + Math.Pow(58, -8));

        Assert.Equal(expected, SearchEstimator.ExpectedCandidatesPerMatch(Near("XRPLFiLE"))!.Value, precision: 0);
    }

    [Fact]
    public void A_First_Character_That_Cannot_Follow_The_R_Is_About_24_Times_Sooner_One_Character_Later()
    {
        double asStarts = SearchEstimator.ExpectedCandidatesPerMatch(
            new PatternSet.Builder().Add(MatchPosition.Starts, "XRPLFiLE", ignoreCase: false).Build())!.Value;
        double asNear = SearchEstimator.ExpectedCandidatesPerMatch(Near("XRPLFiLE"))!.Value;

        Assert.InRange(asStarts / asNear, 24.0, 24.7);
        Assert.Equal(asStarts / asNear, SearchEstimator.NearStartSpeedup(new[] { 'X' }), precision: 9);
    }

    [Theory]
    [InlineData('X', 24.0, 24.7)]   // index 30: only in a 33-character address
    [InlineData('S', 24.0, 24.7)]   // index 25
    [InlineData('p', 1.39, 1.41)]   // index 1: a full share of the leading digit
    [InlineData('Q', 2.1, 2.3)]     // index 23: the fractional share
    public void The_Speedup_Depends_On_The_First_Character(char first, double low, double high)
    {
        Assert.InRange(SearchEstimator.NearStartSpeedup(new[] { first }), low, high);
    }

    [Fact]
    public void A_Case_Insensitive_First_Letter_Weighs_Both_Of_Its_Cases()
    {
        // 's' can follow the r, 'S' cannot: together they gain far less than 'S' alone.
        double both = SearchEstimator.NearStartSpeedup(new[] { 's', 'S' });

        Assert.InRange(both, 1.7, 1.9);
    }

    [Fact]
    public void Real_Addresses_Show_The_Bias_The_Estimate_Assumes()
    {
        // 'X' right after the r occurs only in 33-character addresses (about 1 in 23.3 of them,
        // then 1 in 58), but one character later it is a plain 1 in 58.
        Random rng = new(20261102);
        BouncyCastleDeriver deriver = new();
        Span<char> buffer = stackalloc char[48];
        byte[] entropy = new byte[16];
        PatternSet starts = new PatternSet.Builder().Add(MatchPosition.Starts, "X", ignoreCase: false).Build();
        PatternSet near = Near("X");
        int startsHits = 0;
        int nearHits = 0;
        const int Count = 20_000;
        for (int i = 0; i < Count; i++)
        {
            rng.NextBytes(entropy);
            ReadOnlySpan<char> address = buffer[..deriver.DeriveAddress(entropy, buffer)];
            if (starts.TryMatch(address, out _, out _)) startsHits++;
            if (near.TryMatch(address, out _, out _)) nearHits++;
        }

        // Expected about 15 and 360.
        Assert.InRange(startsHits, 3, 35);
        Assert.InRange(nearHits, 290, 430);
    }

    [Fact]
    public void The_Cli_Takes_A_NearStart_Pattern()
    {
        CliOptions options = CliOptions.Parse(new[] { "--near-start", "XRPLFILE", "--case-sensitive" });

        Assert.Equal((MatchPosition.NearStart, "XRPLFILE"), Assert.Single(options.Patterns));
    }

    [Fact]
    public void The_Device_Table_Gives_NearStart_Its_Own_Code_Below_The_Pair_Halves()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.NearStart, "XY", ignoreCase: false)
            .Add(MatchPosition.Starts, "ab", ignoreCase: false)
            .AddPair("cd", "ef", ignoreCase: false)
            .Build();

        TargetTable table = TargetTable.Build(set);

        Assert.Equal(TargetTable.PositionStarts, table.Groups[0]);
        Assert.Equal(TargetTable.PositionNearStart, table.Groups[4]);
        Assert.Equal(TargetTable.PositionPairStarts, table.Groups[8]);
        Assert.Equal(TargetTable.PositionPairEnds, table.Groups[12]);
        Assert.True(TargetTable.PositionNearStart < TargetTable.PositionPairStarts);
    }

    private static PatternSet NearPair(string starts, string ends, bool ignoreCase = false) =>
        new PatternSet.Builder().AddPair(starts, ends, ignoreCase, nearStart: true).Build();

    [Theory]
    [InlineData("rXRPLQQQQQQQQFiLE", true)]
    [InlineData("rpXRPLQQQQQQQFiLE", true)]
    [InlineData("rppXRPLQQQQQQFiLE", false)]
    [InlineData("rpXRPLFiLE", true)]     // halves touch one character in
    [InlineData("rpXRPLFiL", false)]     // too short for both halves one character in
    [InlineData("rpXRPLQQQQQQQFiLQ", false)]
    public void A_NearStart_Pair_Lets_Only_Its_Starts_Half_Move_One_Character(string address, bool expected)
    {
        bool matched = NearPair("XRPL", "FILE").TryMatch(address, out MatchPosition position, out string pattern);

        Assert.Equal(expected, matched);
        if (expected)
        {
            Assert.Equal(MatchPosition.Pair, position);
            Assert.Equal("XRPL..FILE", pattern);
        }
    }

    [Fact]
    public void A_Plain_Pair_Still_Needs_Its_Starts_Half_Right_After_The_R()
    {
        PatternSet plain = new PatternSet.Builder().AddPair("XRPL", "FILE", ignoreCase: false).Build();

        Assert.False(Assert.Single(plain.Pairs).NearStart);
        Assert.False(plain.TryMatch("rpXRPLQQQQQQQFiLE", out _, out _));
    }

    [Fact]
    public void A_NearStart_Pair_Estimate_Adds_The_Second_Offset_To_Its_Starts_Half()
    {
        double starts = SearchEstimator.StartsProbability("XRPL") + Math.Pow(58, -4);
        double expected = 1.0 / (starts * Math.Pow(58, -4));

        Assert.Equal(expected, SearchEstimator.ExpectedCandidatesPerMatch(NearPair("XRPL", "FILE"))!.Value, precision: 0);
    }

    [Fact]
    public void A_NearStart_Pair_Gains_What_A_NearStart_Pattern_With_Its_First_Character_Gains()
    {
        double plain = SearchEstimator.ExpectedCandidatesPerMatch(
            new PatternSet.Builder().AddPair("XRPL", "FILE", ignoreCase: false).Build())!.Value;
        double near = SearchEstimator.ExpectedCandidatesPerMatch(NearPair("XRPL", "FILE"))!.Value;

        Assert.Equal(SearchEstimator.NearStartSpeedup(new[] { 'X' }), plain / near, precision: 9);
    }

    [Fact]
    public void The_Cli_Takes_A_NearStart_Pair()
    {
        CliOptions options = CliOptions.Parse(new[] { "--near-starts-ends", "XRPL:FILE", "--starts-ends", "ab:cd" });

        Assert.Equal(("XRPL", "FILE", true), options.Pairs[0]);
        Assert.Equal(("ab", "cd", false), options.Pairs[1]);
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(new[] { "--near-starts-ends", "XRPL" }));
    }

    [Fact]
    public void The_Device_Table_Keeps_A_NearStart_Half_Apart_From_The_Same_Plain_Half()
    {
        PatternSet set = new PatternSet.Builder()
            .AddPair("ab", "cd", ignoreCase: false, nearStart: true)
            .AddPair("ab", "ef", ignoreCase: false)
            .Build();

        TargetTable table = TargetTable.Build(set);

        uint nearHalf = table.Pairs[0];
        uint plainHalf = table.Pairs[2];
        Assert.NotEqual(nearHalf, plainHalf);
        Assert.Equal(TargetTable.PositionPairNearStarts, table.Groups[nearHalf * 4]);
        Assert.Equal(TargetTable.PositionPairStarts, table.Groups[plainHalf * 4]);
    }
}
