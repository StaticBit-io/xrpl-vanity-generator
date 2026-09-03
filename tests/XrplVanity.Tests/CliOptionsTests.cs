using System;
using XrplVanity.Cli;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CliOptionsTests
{
    [Theory]
    [InlineData("--threads")]
    [InlineData("--count")]
    public void Non_Numeric_Value_Throws_ArgumentException_Not_A_Crash(string option)
    {
        // FormatException from int.Parse used to escape Main's ArgumentException catch
        // and crash with a stack trace; it must now surface as a clean ArgumentException.
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(new[] { option, "abc" }));
    }

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

    [Fact]
    public void Gpu_Flag_Is_Off_By_Default_And_Parsed()
    {
        Assert.False(CliOptions.Parse(new[] { "hodl" }).Gpu);
        Assert.True(CliOptions.Parse(new[] { "--gpu", "hodl" }).Gpu);
    }

    [Fact]
    public void ThreadsSpecified_Reflects_Whether_The_User_Passed_Threads()
    {
        Assert.True(CliOptions.Parse(new[] { "--threads", "4", "x" }).ThreadsSpecified);
        Assert.False(CliOptions.Parse(new[] { "x" }).ThreadsSpecified);
    }
}
