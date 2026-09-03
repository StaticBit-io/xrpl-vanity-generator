using System.IO;
using XrplVanity.Cli;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class MatchSinkTests
{
    private static Match M(int i) => new($"rAddr{i}", $"sEdSeed{i}", "Addr", MatchPosition.Starts);

    [Fact]
    public void Reports_Until_The_Limit_Then_Refuses_Surplus()
    {
        StringWriter stdout = new();
        using MatchSink sink = new(limit: 2, outFile: null, stdout);

        Assert.True(sink.TryReport(M(1)));
        Assert.False(sink.CountSatisfied);
        Assert.False(sink.TryReport(M(2)));   // reaches the limit: printed, but the caller must stop
        Assert.True(sink.CountSatisfied);
        Assert.False(sink.TryReport(M(3)));   // surplus: not printed
        Assert.Equal(2, sink.Reported);

        string text = stdout.ToString();
        Assert.Contains("rAddr1", text);
        Assert.Contains("rAddr2", text);
        Assert.DoesNotContain("rAddr3", text);
    }

    [Fact]
    public void Without_A_Limit_It_Never_Refuses()
    {
        StringWriter stdout = new();
        using MatchSink sink = new(limit: null, outFile: null, stdout);

        for (int i = 0; i < 10; i++)
        {
            Assert.True(sink.TryReport(M(i)));
        }

        Assert.False(sink.CountSatisfied);
        Assert.Equal(10, sink.Reported);
    }

    [Fact]
    public void Appends_Address_And_Seed_To_The_Out_File()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (MatchSink sink = new(limit: null, outFile: path, new StringWriter()))
            {
                sink.TryReport(M(7));
            }

            Assert.Equal("rAddr7\tsEdSeed7", File.ReadAllText(path).Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
