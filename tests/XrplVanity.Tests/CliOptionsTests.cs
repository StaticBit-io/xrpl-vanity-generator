using XrplVanity.Cli;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Positional_Words_Default_To_Starts()
    {
        CliOptions options = CliOptions.Parse(new[] { "ripple", "hodl" });

        Assert.Equal(2, options.Patterns.Count);
        Assert.All(options.Patterns, p => Assert.Equal(MatchPosition.Starts, p.Position));
        Assert.False(options.CaseSensitive);
    }

    [Fact]
    public void Parses_Position_Flags_And_Options()
    {
        CliOptions options = CliOptions.Parse(new[]
        {
            "--ends", "xyz", "--contains", "dao", "--case-sensitive", "--threads", "8", "--count", "3",
        });

        Assert.Contains(options.Patterns, p => p.Position == MatchPosition.Ends && p.Pattern == "xyz");
        Assert.Contains(options.Patterns, p => p.Position == MatchPosition.Contains && p.Pattern == "dao");
        Assert.True(options.CaseSensitive);
        Assert.Equal(8, options.Threads);
        Assert.Equal(3, options.Count);
    }
}
