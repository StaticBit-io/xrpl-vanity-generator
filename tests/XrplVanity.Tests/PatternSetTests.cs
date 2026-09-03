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
}
