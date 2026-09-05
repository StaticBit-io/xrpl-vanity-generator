using System;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.Tests.Search;

public class SearchRequestTests
{
    private static PatternSet Patterns() => new PatternSet.Builder().Add(MatchPosition.Starts, "ab", ignoreCase: false).Build();

    [Fact]
    public void Requires_At_Least_One_Target()
    {
        Assert.Throws<ArgumentException>(() => new SearchRequest(Patterns(), Array.Empty<SearchTarget>(), null));
    }

    [Fact]
    public void Allows_At_Most_One_Cpu_Target()
    {
        Assert.Throws<ArgumentException>(() => new SearchRequest(Patterns(), new SearchTarget[] { new SearchTarget.Cpu(1), new SearchTarget.Cpu(2) }, null));
    }

    [Fact]
    public void FirstAvailableDevice_Cannot_Be_Combined_With_An_Explicit_Device()
    {
        Assert.Throws<ArgumentException>(() => new SearchRequest(Patterns(),
            new SearchTarget[] { new SearchTarget.FirstAvailableDevice(), new SearchTarget.Device(new DeviceSelector("cuda", 0)) }, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Rejects_A_Duty_Outside_1_To_100(int duty)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchRequest(Patterns(), new SearchTarget[] { new SearchTarget.Cpu(1) }, null, duty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_A_Non_Positive_Count(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchRequest(Patterns(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count));
    }

    [Fact]
    public void Cpu_Target_Requires_At_Least_One_Thread()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchTarget.Cpu(0));
    }

    [Fact]
    public void A_Set_With_No_Reachable_Pattern_Is_Rejected()
    {
        // "0" folds to the reachable 'o' (see PatternNormalizer); "@" cannot occur in a base58 address at all.
        PatternSet none = new PatternSet.Builder().Add(MatchPosition.Starts, "@", ignoreCase: false).Build();
        Assert.Throws<ArgumentException>(() => new SearchRequest(none, new SearchTarget[] { new SearchTarget.Cpu(1) }, null));
    }
}
