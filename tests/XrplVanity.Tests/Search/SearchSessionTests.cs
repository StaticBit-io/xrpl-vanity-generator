using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;
using XrplVanity.Tests.Gpu;
using Xunit;

namespace XrplVanity.Tests.Search;

[Collection(XrplVanity.Tests.CpuBoundCollection.Name)]
public class SearchSessionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(120);

    private static PatternSet OneChar() => new PatternSet.Builder().Add(MatchPosition.Starts, "a", ignoreCase: false).Build();
    private static PatternSet Never() => new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();

    private static SearchSummary Await(Task<SearchSummary> completion)
    {
        Assert.True(completion.Wait(Deadline), "session did not complete in time");
        return completion.Result;
    }

    [Fact]
    public void Cpu_Only_Session_Reaches_The_Count_Exactly()
    {
        List<Match> reported = new();
        List<Match> events = new();
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(2) }, count: 5);
        using SearchSession session = new(request, m => { lock (reported) reported.Add(m); });
        session.MatchFound += m => { lock (events) events.Add(m); };

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.CountReached, summary.Outcome);
        Assert.Equal(5, reported.Count);
        Assert.Equal(5, events.Count);
        Assert.Equal(5, summary.Final.Reported);
        TargetProgress cpu = Assert.Single(summary.Final.Targets);
        Assert.Equal("cpu", cpu.Id);
        Assert.Equal("CPU (2 threads)", cpu.Name);
        Assert.Equal(TargetState.Finished, cpu.State);
        Assert.True(cpu.Candidates >= 5);
        Assert.True(summary.Final.TotalCandidates >= 5);
    }

    [Fact]
    public void Stop_Completes_With_Cancelled_And_Joins_Every_Thread()
    {
        SearchRequest request = new(Never(), new SearchTarget[] { new SearchTarget.Cpu(3) }, count: null);
        using SearchSession session = new(request, _ => { });
        session.Start();
        Thread.Sleep(300);

        session.Stop();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.Cancelled, summary.Outcome);
        Assert.Equal(TargetState.Finished, summary.Final.Targets[0].State);
        Assert.True(summary.Final.TotalCandidates > 0);
        session.Stop();   // idempotent
    }

    [Fact]
    public void Progress_Snapshots_Arrive_With_Growing_Counts_And_A_Cpu_Rate()
    {
        SearchRequest request = new(Never(), new SearchTarget[] { new SearchTarget.Cpu(2) }, count: null);
        using SearchSession session = new(request, _ => { });
        List<SearchProgress> snapshots = new();
        session.ProgressChanged += p => { lock (snapshots) snapshots.Add(p); };

        session.Start();
        Thread.Sleep(1200);
        session.Stop();
        Await(session.Completion);

        SearchProgress[] seen;
        lock (snapshots) seen = snapshots.ToArray();
        Assert.True(seen.Length >= 3, $"only {seen.Length} snapshots in 1.2 s");
        Assert.True(seen[^1].TotalCandidates >= seen[0].TotalCandidates);
        Assert.True(seen[^1].Targets[0].RatePerSecond > 0, "CPU rate is zero");
        Assert.Equal(TargetState.Running, seen[1].Targets[0].State);
        Assert.True(session.Progress.Elapsed > TimeSpan.Zero);
    }

    [Fact]
    public void A_Missing_Device_Fails_Alone_While_The_Cpu_Keeps_Searching()
    {
        SearchRequest request = new(OneChar(),
            new SearchTarget[] { new SearchTarget.Cpu(1), new SearchTarget.Device(new DeviceSelector("cuda", 99)) }, count: 2);
        List<string> log = new();
        using SearchSession session = new(request, _ => { }, line => { lock (log) log.Add(line); });

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.CountReached, summary.Outcome);
        TargetProgress device = summary.Final.Targets.Single(t => t.Id == "cuda:99");
        Assert.Equal(TargetState.Failed, device.State);
        Assert.NotNull(device.Failure);
        Assert.Equal(TargetState.Finished, summary.Final.Targets.Single(t => t.Id == "cpu").State);
    }

    [Fact]
    public void When_The_Only_Target_Fails_The_Outcome_Says_So()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Device(new DeviceSelector("cuda", 99)) }, count: 1);
        using SearchSession session = new(request, _ => { });

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.AllTargetsFailed, summary.Outcome);
        Assert.Equal(0, summary.Final.Reported);
    }

    [Fact]
    public void A_Second_Start_Throws()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count: 1);
        using SearchSession session = new(request, _ => { });
        session.Start();
        Assert.Throws<InvalidOperationException>(session.Start);
        Await(session.Completion);
    }

    [Fact]
    public void Duty_Is_Validated_And_Forwarded()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count: 1, dutyPercent: 80);
        using SearchSession session = new(request, _ => { });
        Assert.Equal(80, session.DutyPercent);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.DutyPercent = 0);
        session.DutyPercent = 50;
        Assert.Equal(50, session.DutyPercent);
    }

    [Fact]
    public void A_Throwing_Report_Callback_Stops_The_Session_Without_Crashing_The_Process()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count: 5);
        using SearchSession session = new(request, _ => throw new InvalidOperationException("boom"));

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.Cancelled, summary.Outcome);
        Assert.Equal(1, summary.Final.Reported);
        TargetProgress cpu = Assert.Single(summary.Final.Targets);
        Assert.Equal(TargetState.Failed, cpu.State);
        Assert.NotNull(cpu.Failure);
        Assert.Contains("report callback failed", cpu.Failure);
    }

    [Fact]
    public void A_Throwing_MatchFound_Subscriber_Stops_The_Session_Without_Crashing_The_Process()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count: 5);
        using SearchSession session = new(request, _ => { });
        session.MatchFound += _ => throw new InvalidOperationException("boom");

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.Cancelled, summary.Outcome);
        Assert.Equal(1, summary.Final.Reported);
        TargetProgress cpu = Assert.Single(summary.Final.Targets);
        Assert.Equal(TargetState.Failed, cpu.State);
        Assert.NotNull(cpu.Failure);
        Assert.Contains("report callback failed", cpu.Failure);
    }

    [Fact]
    public void Dispose_On_A_Never_Started_Session_Completes_With_Cancelled_And_Is_Idempotent()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count: 1);
        SearchSession session = new(request, _ => { });

        session.Dispose();

        // A front end may await Completion unconditionally: disposing a session that was never
        // started must settle it rather than leave it pending forever.
        Assert.True(session.Completion.IsCompleted);
        SearchSummary summary = Await(session.Completion);
        Assert.Equal(SessionOutcome.Cancelled, summary.Outcome);
        Assert.Equal(0, summary.Final.TotalCandidates);
        Assert.Equal(TargetState.Pending, Assert.Single(summary.Final.Targets).State);
        session.Dispose();   // idempotent, no throw
    }

    private sealed class ThrowingBackend : IGpuBackend
    {
        public string Name => "broken";
        public IReadOnlyList<GpuDeviceInfo> EnumerateDevices() => throw new InvalidOperationException("broken backend cannot enumerate");
        public IGpuDevice Open(int index) => throw new InvalidOperationException("broken backend cannot open");
    }

    [Fact]
    public void A_Device_Target_On_A_Broken_Backend_Still_Completes_The_Session()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Device(new DeviceSelector("broken", 0)) }, count: 1);
        using SearchSession session = new(request, _ => { }, null, new IGpuBackend[] { new ThrowingBackend() });

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.AllTargetsFailed, summary.Outcome);
        TargetProgress device = Assert.Single(summary.Final.Targets);
        Assert.Equal(TargetState.Failed, device.State);
        Assert.NotNull(device.Failure);
        Assert.Equal(0, device.Candidates);
    }

    [Fact]
    public void Target_Seconds_And_Rate_Are_Target_Local()
    {
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.Cpu(1) }, count: 3);
        using SearchSession session = new(request, _ => { });

        session.Start();
        SearchSummary summary = Await(session.Completion);

        TargetProgress cpu = Assert.Single(summary.Final.Targets);
        Assert.True(cpu.Seconds > 0, "the CPU target reported no time of its own");
        Assert.True(cpu.Seconds <= summary.Final.Elapsed.TotalSeconds + 0.5, $"CPU seconds {cpu.Seconds} exceeds the session elapsed time");
        Assert.Equal(cpu.Candidates / cpu.Seconds, cpu.RatePerSecond, precision: 3);
        Assert.Equal(0, cpu.ConsistencyErrors);
    }

    [Fact]
    public void Stop_Before_Start_Then_Start_Completes_Promptly_With_Cancelled()
    {
        // A pre-cancelled token makes every worker exit at once instead of ever running.
        SearchRequest request = new(Never(), new SearchTarget[] { new SearchTarget.Cpu(2) }, count: null);
        using SearchSession session = new(request, _ => { });

        session.Stop();
        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.Cancelled, summary.Outcome);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Mixed_Cpu_And_Gpu_Session_Stops_Exactly_At_The_Count(string backend)
    {
        GpuTestHelper.RequireDevice(backend).Dispose();   // skip when unavailable
        // Case-insensitive two characters: frequent on the GPU, so both sources report.
        PatternSet frequent = new PatternSet.Builder().Add(MatchPosition.Starts, "ab", ignoreCase: true).Build();
        SearchRequest request = new(frequent,
            new SearchTarget[] { new SearchTarget.Cpu(2), new SearchTarget.Device(new DeviceSelector(backend, 0)) }, count: 50);
        int reported = 0;
        using SearchSession session = new(request, _ => Interlocked.Increment(ref reported));

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.CountReached, summary.Outcome);
        Assert.Equal(50, reported);
        Assert.Equal(50, summary.Final.Reported);
        TargetProgress device = summary.Final.Targets.Single(t => t.Id == $"{backend}:0");
        Assert.Equal(TargetState.Finished, device.State);
        Assert.False(string.IsNullOrEmpty(device.Name));
        // Device-local clock: the worker's own measurement, which excludes opening the device
        // (context creation, and on OpenCL the program compile), so it never exceeds the session.
        Assert.True(device.Seconds > 0, "the device reported no search time of its own");
        Assert.True(device.Seconds <= summary.Final.Elapsed.TotalSeconds + 0.5, $"device seconds {device.Seconds} exceeds the session elapsed time");
    }

    [Fact]
    public void FirstAvailableDevice_Resolves_Its_Id_Or_Fails_Cleanly()
    {
        bool anyDevice = GpuBackends.All.Any(b => b.EnumerateDevices().Count > 0);
        SearchRequest request = new(OneChar(), new SearchTarget[] { new SearchTarget.FirstAvailableDevice() }, count: 1);
        using SearchSession session = new(request, _ => { });

        session.Start();
        SearchSummary summary = Await(session.Completion);

        TargetProgress target = Assert.Single(summary.Final.Targets);
        if (anyDevice)
        {
            Assert.Equal(SessionOutcome.CountReached, summary.Outcome);
            Assert.Matches("^(cuda|opencl):[0-9]+$", target.Id);
        }
        else
        {
            Assert.Equal(SessionOutcome.AllTargetsFailed, summary.Outcome);
            Assert.Equal("gpu", target.Id);
        }
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Duty_Set_During_A_Run_Reaches_The_Device_Loop(string backend)
    {
        GpuTestHelper.RequireDevice(backend).Dispose();
        SearchRequest request = new(Never(), new SearchTarget[] { new SearchTarget.Device(new DeviceSelector(backend, 0)) }, count: null);
        using SearchSession session = new(request, _ => { });
        session.Start();
        session.DutyPercent = 30;
        Assert.Equal(30, session.DutyPercent);
        Assert.Equal(30, session.DeviceSearchForTests!.DutyPercent);
        session.Stop();
        Await(session.Completion);
    }

    [Theory]
    [MemberData(nameof(GpuTestHelper.Backends), MemberType = typeof(GpuTestHelper))]
    public void Device_Announcement_Line_Is_Not_Forwarded_To_The_Session_Log(string backend)
    {
        GpuTestHelper.RequireDevice(backend).Dispose();   // skip when unavailable
        PatternSet frequent = new PatternSet.Builder().Add(MatchPosition.Starts, "ab", ignoreCase: true).Build();
        SearchRequest request = new(frequent, new SearchTarget[] { new SearchTarget.Device(new DeviceSelector(backend, 0)) }, count: 1);
        List<string> log = new();
        using SearchSession session = new(request, _ => { }, line => { lock (log) log.Add(line); });

        session.Start();
        SearchSummary summary = Await(session.Completion);

        Assert.Equal(SessionOutcome.CountReached, summary.Outcome);
        TargetProgress device = summary.Final.Targets.Single(t => t.Id == $"{backend}:0");
        string announcementPrefix = $"GPU[{device.Id}]: {device.Name}";
        string[] seen;
        lock (log) seen = log.ToArray();
        Assert.DoesNotContain(seen, line => line.StartsWith(announcementPrefix, StringComparison.Ordinal));
    }
}
