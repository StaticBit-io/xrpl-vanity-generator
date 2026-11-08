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

    /// <summary>
    /// A hand-built device target. <c>seconds</c> is the target's own clock, so the rate the CLI
    /// prints is derived from it, not from the session elapsed time passed to
    /// <see cref="SummaryWith"/>.
    /// </summary>
    private static TargetProgress DeviceTarget(TargetState state, long candidates, string? failure,
        string id = "cuda:0", double seconds = 2, long consistencyErrors = 0) =>
        new(id, "Test Device", state, candidates, seconds > 0 ? candidates / seconds : 0, failure, seconds, consistencyErrors);

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
    public void ExitCodeFor_All_Targets_Failed_On_A_Cpu_Only_Run_Does_Not_Mention_A_Device()
    {
        StringWriter stderr = new();
        TargetProgress cpu = new("cpu", "CPU", TargetState.Failed, 100, 50, "no more threads", Seconds: 2);
        SearchSummary summary = SummaryWith(SessionOutcome.AllTargetsFailed, cpu);

        int rc = Program.ExitCodeFor(summary, multi: false, stderr);

        Assert.Equal(2, rc);
        Assert.Contains("error: search failed: no more threads", stderr.ToString());
        Assert.DoesNotContain("--gpu", stderr.ToString());
    }

    [Fact]
    public void ExitCodeFor_Cpu_Only_Failed_Writes_Search_Failed_Not_Gpu_And_Returns_2()
    {
        StringWriter stderr = new();
        TargetProgress cpu = new("cpu", "CPU", TargetState.Failed, 100, 50, "report callback failed: disk full", Seconds: 2);
        SearchSummary summary = SummaryWith(SessionOutcome.Cancelled, cpu);

        int rc = Program.ExitCodeFor(summary, multi: false, stderr);

        Assert.Equal(2, rc);
        Assert.Contains("error: search failed: report callback failed: disk full", stderr.ToString());
        Assert.DoesNotContain("GPU search failed", stderr.ToString());
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

    [Fact]
    public void RenderSummary_Single_Device_Uses_The_Device_Clock_And_Reports_Consistency_Errors()
    {
        StringWriter stderr = new();
        // Session elapsed is 1 s (SummaryWith); the device searched for 2 s of its own, so a line
        // built from the session clock would read "1.0 s" and double the rate.
        SearchSummary summary = SummaryWith(SessionOutcome.CountReached,
            DeviceTarget(TargetState.Finished, candidates: 20_000_000, failure: null, seconds: 2, consistencyErrors: 3));

        Program.RenderSummary(summary, multi: false, stderr);

        Assert.Equal("GPU: searched 20,000,000 candidates in 2.0 s (10,000,000/s); consistency errors: 3.", stderr.ToString().TrimEnd());
    }

    [Fact]
    public void RenderSummary_Multi_Device_Lines_Are_Device_Local_And_The_Aggregate_Is_The_Session_Clock()
    {
        StringWriter stderr = new();
        SearchSummary summary = SummaryWith(SessionOutcome.CountReached,
            DeviceTarget(TargetState.Finished, candidates: 6_000_000, failure: null, id: "cuda:0", seconds: 2),
            DeviceTarget(TargetState.Finished, candidates: 2_000_000, failure: null, id: "opencl:0", seconds: 4, consistencyErrors: 1));

        Program.RenderSummary(summary, multi: true, stderr);

        string[] lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("GPU[cuda:0]: searched 6,000,000 candidates in 2.0 s (3,000,000/s); consistency errors: 0.", lines[0].TrimEnd());
        Assert.Equal("GPU[opencl:0]: searched 2,000,000 candidates in 4.0 s (500,000/s); consistency errors: 1.", lines[1].TrimEnd());
        // The aggregate is measured on the session wall clock (1 s here), not on any device's.
        Assert.Equal("GPU: searched 8,000,000 candidates on 2 device(s) in 1.0 s (8,000,000/s); consistency errors: 1.", lines[2].TrimEnd());
    }

    [Fact]
    public void RenderSummary_Cpu_Line_Uses_The_Pool_Clock()
    {
        StringWriter stderr = new();
        TargetProgress cpu = new("cpu", "CPU (2 threads)", TargetState.Finished, 500_000, 250_000, null, Seconds: 2);
        SearchSummary summary = SummaryWith(SessionOutcome.CountReached, cpu);

        Program.RenderSummary(summary, multi: false, stderr);

        Assert.Equal("CPU: searched 500,000 candidates in 2.0 s (250,000/s).", stderr.ToString().TrimEnd());
    }

    // -- AnnounceDevices: the name line must survive a run that finishes inside one tick --------

    [Fact]
    public void AnnounceDevices_Announces_A_Finished_Device_Once()
    {
        StringWriter stderr = new();
        HashSet<string> announced = new(StringComparer.Ordinal);
        SearchProgress progress = new(TimeSpan.FromSeconds(1), 0, 0, 0,
            new[] { DeviceTarget(TargetState.Finished, candidates: 10, failure: null) });

        Program.AnnounceDevices(progress, multi: false, announced, stderr);
        Program.AnnounceDevices(progress, multi: false, announced, stderr);

        Assert.Equal("GPU: cuda:Test Device", stderr.ToString().TrimEnd());
    }

    [Fact]
    public void AnnounceDevices_Skips_A_Target_That_Never_Opened_And_Does_Not_Throw_On_A_Colon_Less_Id()
    {
        StringWriter stderr = new();
        HashSet<string> announced = new(StringComparer.Ordinal);
        // An unresolved --gpu target: id "gpu", and Name still the selector because it never opened.
        SearchProgress unopened = new(TimeSpan.FromSeconds(1), 0, 0, 0,
            new[] { new TargetProgress("gpu", "gpu", TargetState.Failed, 0, 0, "no device could be opened") });

        Program.AnnounceDevices(unopened, multi: false, announced, stderr);
        Assert.Equal(string.Empty, stderr.ToString());

        SearchProgress resolvedWithoutColon = new(TimeSpan.FromSeconds(1), 0, 0, 0,
            new[] { new TargetProgress("gpu", "Test Device", TargetState.Running, 10, 5, null, Seconds: 2) });

        Program.AnnounceDevices(resolvedWithoutColon, multi: false, announced, stderr);
        Assert.Equal("GPU: Test Device", stderr.ToString().TrimEnd());
    }
}
