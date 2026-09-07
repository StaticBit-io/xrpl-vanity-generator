using System;
using System.Collections.Generic;
using System.Linq;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class PatternSetTests
{
    [Fact]
    public void Matches_Prefix_After_Leading_R_Case_Insensitive()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "disk", ignoreCase: true)
            .Build();

        bool matched = set.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK",
            out MatchPosition pos, out string pattern);

        Assert.True(matched);
        Assert.Equal(MatchPosition.Starts, pos);
        Assert.Equal("Disk", pattern);
    }

    [Fact]
    public void Prefix_Does_Not_Match_When_Case_Sensitive()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "disk", ignoreCase: false)
            .Build();

        Assert.False(set.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));
    }

    [Fact]
    public void Matches_Suffix_And_Contains()
    {
        PatternSet ends = new PatternSet.Builder()
            .Add(MatchPosition.Ends, "HGK", ignoreCase: false)
            .Build();
        Assert.True(ends.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));

        PatternSet contains = new PatternSet.Builder()
            .Add(MatchPosition.Contains, "qgxo", ignoreCase: true)
            .Build();
        Assert.True(contains.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));
    }

    [Fact]
    public void Collects_Unreachable_Patterns()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "bit@coin", ignoreCase: true)
            .Build();

        Assert.Single(set.Unreachable);
        Assert.False(set.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));
    }

    [Fact]
    public void Case_Insensitive_Covers_Every_Mixed_Case_Combination()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "ab", ignoreCase: true)
            .Build();

        // All four combinations of "ab" must match after the leading 'r'.
        Assert.True(set.TryMatch("rabXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", out _, out _));
        Assert.True(set.TryMatch("rABXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", out _, out _));
        Assert.True(set.TryMatch("rAbXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", out _, out _));
        // "aB" is the combination the old 3-form expansion (value/UPPER/Title) missed.
        Assert.True(set.TryMatch("raBXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", out _, out _));
    }

    [Fact]
    public void One_Case_Only_Letters_Do_Not_Multiply_Variants()
    {
        // 'o' and 'i' exist only in lower case, 'l' only as 'L'; "oil" therefore has a
        // single reachable case form ("oiL"), so it matches that and nothing else.
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "oil", ignoreCase: true)
            .Build();

        Assert.True(set.TryMatch("roiLXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", out _, out string pattern));
        Assert.Equal("oiL", pattern);
    }

    [Fact]
    public void Over_Length_Pattern_Falls_Back_To_Case_Sensitive_And_Is_Reported()
    {
        // 13 two-case letters: one over MaxIgnoreCaseLength (12).
        string longPattern = "abcdefghjkmnp";
        Assert.Equal(PatternSet.MaxIgnoreCaseLength + 1, longPattern.Length);

        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, longPattern, ignoreCase: true)
            .Build();

        Assert.Single(set.CaseFoldingSkipped);

        // The exact typed form matches; an alternate case does not (case-folding was dropped).
        Assert.True(set.TryMatch("r" + longPattern + "XXXXXXXXXXXXXXXXXXXX", out _, out _));
        Assert.False(set.TryMatch("rAbcdefghjkmnpXXXXXXXXXXXXXXXXXXXX", out _, out _));
    }

    [Fact]
    public void Boundary_Length_Still_Expands_Case()
    {
        // Exactly MaxIgnoreCaseLength (12) two-case letters: still fully expanded.
        string atLimit = "abcdefghjkmn";
        Assert.Equal(PatternSet.MaxIgnoreCaseLength, atLimit.Length);

        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, atLimit, ignoreCase: true)
            .Build();

        Assert.Empty(set.CaseFoldingSkipped);
        Assert.True(set.TryMatch("rAbcdefghjkmnXXXXXXXXXXXXXXXXXXXXX", out _, out _));
    }

    [Fact]
    public void EnumerateTargets_Yields_Exactly_The_Expanded_Set()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "ab", ignoreCase: true)      // ab aB Ab AB
            .Add(MatchPosition.Ends, "z", ignoreCase: true)         // z Z
            .Add(MatchPosition.Contains, "q", ignoreCase: false)    // q
            .Build();

        List<(MatchPosition Position, string Value)> targets = set.EnumerateTargets().ToList();

        Assert.Equal(7, targets.Count);
        Assert.Equal(new[] { "AB", "Ab", "aB", "ab" },
            targets.Where(t => t.Position == MatchPosition.Starts).Select(t => t.Value).OrderBy(v => v, StringComparer.Ordinal));
        Assert.Equal(new[] { "Z", "z" },
            targets.Where(t => t.Position == MatchPosition.Ends).Select(t => t.Value).OrderBy(v => v, StringComparer.Ordinal));
        Assert.Equal(new[] { "q" },
            targets.Where(t => t.Position == MatchPosition.Contains).Select(t => t.Value));
    }

    [Fact]
    public void Empty_Pattern_Is_Reported_As_Unreachable_And_Never_Matches()
    {
        PatternSet set = new PatternSet.Builder()
            .Add(MatchPosition.Starts, "", ignoreCase: true)
            .Add(MatchPosition.Contains, "   ", ignoreCase: false)
            .Build();

        Assert.Equal(2, set.Unreachable.Count);
        Assert.False(set.HasPatterns);
        Assert.False(set.TryMatch("rDiskmQdynWqgxoNv4CHsqFHdgsnpJMHGK", out _, out _));
        Assert.Empty(set.EnumerateTargets());
    }

    private static PatternSet Pair(string starts, string ends, bool ignoreCase = false) =>
        new PatternSet.Builder().AddPair(starts, ends, ignoreCase).Build();

    [Fact]
    public void A_Pair_Matches_Only_When_Both_Halves_Hold()
    {
        PatternSet set = Pair("ab", "yz");

        Assert.True(set.TryMatch("rabQQQQQQQyz", out MatchPosition position, out string pattern));
        Assert.Equal(MatchPosition.Pair, position);
        Assert.Equal("ab..yz", pattern);

        Assert.False(set.TryMatch("rabQQQQQQQQQ", out _, out _));   // prefix only
        Assert.False(set.TryMatch("rQQQQQQQQQyz", out _, out _));   // suffix only
        Assert.False(set.TryMatch("rQQQQQQQQQQQ", out _, out _));   // neither
    }

    [Fact]
    public void A_Pair_Whose_Halves_Do_Not_Fit_The_Body_Does_Not_Match()
    {
        PatternSet set = Pair("abcd", "wxyz");
        Assert.False(set.TryMatch("rabcdwxyz", out _, out _));      // body is 8, halves need 8 — fits exactly
        Assert.True(set.TryMatch("rabcdQwxyz", out _, out _));      // body is 9
    }

    [Fact]
    public void The_Two_Halves_Are_Normalized_Independently()
    {
        // l -> L and I -> i, per PatternNormalizer.
        PatternSet set = Pair("Il", "0O");
        Assert.True(set.TryMatch("riLQQQQQQQoo", out _, out string pattern));
        Assert.Equal("Il..0O", pattern);   // Display keeps what the user typed
    }

    [Fact]
    public void An_Unreachable_Half_Makes_The_Whole_Rule_Unreachable()
    {
        PatternSet set = new PatternSet.Builder().AddPair("ab", "@", ignoreCase: false).Build();
        Assert.False(set.HasPatterns);
        NormalizedPattern reported = Assert.Single(set.Unreachable);
        Assert.Equal("ab..@", reported.Original);
    }

    [Fact]
    public void An_Empty_Half_Makes_The_Rule_Unreachable()
    {
        PatternSet set = new PatternSet.Builder().AddPair("ab", "   ", ignoreCase: false).Build();
        Assert.False(set.HasPatterns);
        Assert.Single(set.Unreachable);
    }

    [Fact]
    public void Case_Folding_Expands_Each_Half_On_Its_Own()
    {
        PatternSet set = Pair("ab", "yz", ignoreCase: true);
        PatternSet.Pair pair = Assert.Single(set.Pairs);
        Assert.Equal(4, pair.Starts.Count);   // ab aB Ab AB
        Assert.Equal(4, pair.Ends.Count);     // yz yZ Yz YZ
        Assert.True(set.TryMatch("rABQQQQQQQYz", out _, out _));
    }

    [Fact]
    public void A_Half_Longer_Than_The_Case_Folding_Cap_Is_Reported_And_Searched_Case_Sensitively()
    {
        string longHalf = new string('a', PatternSet.MaxIgnoreCaseLength + 1);
        PatternSet set = new PatternSet.Builder().AddPair(longHalf, "yz", ignoreCase: true).Build();
        PatternSet.Pair pair = Assert.Single(set.Pairs);
        Assert.Single(pair.Starts);           // not expanded
        Assert.Equal(4, pair.Ends.Count);     // still expanded
        Assert.Single(set.CaseFoldingSkipped);
    }

    [Fact]
    public void A_Pair_Is_Reported_Before_A_Plain_Pattern_That_Also_Holds()
    {
        PatternSet set = new PatternSet.Builder()
            .AddPair("ab", "yz", ignoreCase: false)
            .Add(MatchPosition.Starts, "ab", ignoreCase: false)
            .Build();

        Assert.True(set.TryMatch("rabQQQQQQQyz", out MatchPosition position, out string pattern));
        Assert.Equal(MatchPosition.Pair, position);
        Assert.Equal("ab..yz", pattern);
    }
}
