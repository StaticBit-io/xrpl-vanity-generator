using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using XrplVanity.Cli;
using XrplVanity.Search;
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

    // -- ExitCodeFor: a device that fails mid-run must not read as a clean Ctrl+C exit ----------

    private static TargetProgress DeviceTarget(TargetState state, long candidates, string? failure) =>
        new("cuda:0", "Test Device", state, candidates, 0, failure);

    private static SearchSummary SummaryWith(SessionOutcome outcome, params TargetProgress[] targets)
    {
        SearchProgress final = new(TimeSpan.FromSeconds(1), 0, 0, 0, targets);
        return new SearchSummary(outcome, final);
    }

    [Fact]
    public void ExitCodeFor_Device_Failed_MidRun_Writes_Gpu_Search_Failed_And_Returns_2()
    {
        StringWriter stderr = new();
        SearchSummary summary = SummaryWith(SessionOutcome.Cancelled, DeviceTarget(TargetState.Failed, candidates: 100, failure: "device lost"));

        int rc = Program.ExitCodeFor(summary, multi: false, stderr);

        Assert.Equal(2, rc);
        Assert.Contains("error: GPU search failed: device lost", stderr.ToString());
    }

    [Fact]
    public void ExitCodeFor_Device_Finished_Writes_No_Error_And_Returns_130()
    {
        StringWriter stderr = new();
        SearchSummary summary = SummaryWith(SessionOutcome.Cancelled, DeviceTarget(TargetState.Finished, candidates: 100, failure: null));

        int rc = Program.ExitCodeFor(summary, multi: false, stderr);

        Assert.Equal(130, rc);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void ExitCodeFor_CountReached_Returns_0()
    {
        StringWriter stderr = new();
        SearchSummary summary = SummaryWith(SessionOutcome.CountReached, DeviceTarget(TargetState.Finished, candidates: 1, failure: null));

        int rc = Program.ExitCodeFor(summary, multi: false, stderr);

        Assert.Equal(0, rc);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void ExitCodeFor_AllTargetsFailed_Single_Writes_The_Existing_No_Usable_Device_Message()
    {
        StringWriter stderr = new();
        SearchSummary summary = SummaryWith(SessionOutcome.AllTargetsFailed, DeviceTarget(TargetState.Failed, candidates: 0, failure: "driver not found"));

        int rc = Program.ExitCodeFor(summary, multi: false, stderr);

        Assert.Equal(2, rc);
        Assert.Contains("error: --gpu requested but no usable device: driver not found", stderr.ToString());
    }

    [Fact]
    public void RenderSummary_Single_Device_Failed_MidRun_Line_Ends_With_Stopped_Tail()
    {
        StringWriter stderr = new();
        SearchSummary summary = SummaryWith(SessionOutcome.Cancelled, DeviceTarget(TargetState.Failed, candidates: 100, failure: "device lost"));

        Program.RenderSummary(summary, multi: false, stderr);

        string[] lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.EndsWith("(stopped: device lost)", lines[0].TrimEnd());
    }
}
