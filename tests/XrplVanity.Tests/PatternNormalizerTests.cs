using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class PatternNormalizerTests
{
    [Fact]
    public void Maps_Non_Alphabet_Lookalikes()
    {
        NormalizedPattern p = PatternNormalizer.Normalize("hello");
        Assert.True(p.Reachable);
        Assert.Equal("heLLo", p.Value); // 'l' -> 'L'
    }

    [Fact]
    public void Maps_Zero_And_Capital_O_And_Capital_I()
    {
        Assert.Equal("ooi", PatternNormalizer.Normalize("0Oi").Value);
        Assert.Equal("i", PatternNormalizer.Normalize("I").Value);
    }

    [Fact]
    public void Flags_Unreachable_Characters()
    {
        NormalizedPattern p = PatternNormalizer.Normalize("bit@coin");
        Assert.False(p.Reachable); // '@' cannot occur in an address
    }
}
