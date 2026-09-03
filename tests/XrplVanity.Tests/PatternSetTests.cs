using System;
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
}
