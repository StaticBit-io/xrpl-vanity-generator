using System.IO;
using System.Threading;
using XrplVanity.Cli;
using Xunit;

namespace XrplVanity.Tests;

[Collection(CpuBoundCollection.Name)]
public class CliProgramTests
{
    [Fact]
    public void Cpu_Run_Prints_Matches_Then_The_Cpu_Rate_Line_And_Exits_0()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int rc = Program.Run(new[] { "--case-sensitive", "--starts", "a", "--count", "2", "--threads", "2" }, stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(0, rc);
        string[] lines = stdout.ToString().Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("Address: ra", l.TrimEnd()));
        Assert.All(lines, l => Assert.Contains("\tSeed: s", l));
        Assert.Matches(@"CPU: searched [\d,]+ candidates in \d+\.\d s \([\d,]+/s\)\.", stderr.ToString());
        Assert.Contains("WARNING: a seed is a secret.", stderr.ToString());
    }

    [Fact]
    public void Cancellation_Before_The_Count_Exits_130()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        using CancellationTokenSource cts = new(System.TimeSpan.FromMilliseconds(400));

        int rc = Program.Run(new[] { "--case-sensitive", "--starts", "zzzzzzzz", "--threads", "1" }, stdout, stderr, cts.Token);

        Assert.Equal(130, rc);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void No_Reachable_Pattern_Exits_2()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        // PatternNormalizer folds '0' -> 'o', which would make this pattern reachable; '@' cannot
        // occur in an XRPL base58 address, so it stays unreachable.
        int rc = Program.Run(new[] { "--starts", "@" }, stdout, stderr, TestContext.Current.CancellationToken);
        Assert.Equal(2, rc);
        Assert.Contains("error: no reachable patterns to search for.", stderr.ToString());
    }

    [Fact]
    public void Unknown_Gpu_Device_Exits_2_With_The_Existing_Message()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int rc = Program.Run(new[] { "--backend", "cuda", "--device", "99", "--starts", "a", "--count", "1" }, stdout, stderr, TestContext.Current.CancellationToken);
        Assert.Equal(2, rc);
        Assert.Contains("error: --gpu requested but no usable device:", stderr.ToString());
    }

    [Fact]
    public void Out_File_Receives_Address_Tab_Seed()
    {
        string path = Path.GetTempFileName();
        try
        {
            int rc = Program.Run(new[] { "--case-sensitive", "--starts", "a", "--count", "1", "--out", path }, new StringWriter(), new StringWriter(), TestContext.Current.CancellationToken);
            Assert.Equal(0, rc);
            string line = File.ReadAllText(path).Trim();
            Assert.Matches(@"^ra\S+\ts\S+$", line);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
