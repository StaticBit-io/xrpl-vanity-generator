using System.Collections.Generic;
using System.Threading;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests.Search;

[Collection(XrplVanity.Tests.CpuBoundCollection.Name)]
public class CpuSearchWorkersTests
{
    // One character matches 1 in 58 candidates: a few milliseconds of CPU work.
    private static PatternSet OneChar() => new PatternSet.Builder().Add(MatchPosition.Starts, "a", ignoreCase: false).Build();

    [Fact]
    public void The_Cpu_Search_Lives_In_Core_And_Needs_No_Gpu()
    {
        Assert.Same(typeof(Seed).Assembly, typeof(CpuSearchWorkers).Assembly);
        Assert.DoesNotContain(typeof(CpuSearchWorkers).Assembly.GetReferencedAssemblies(),
            a => a.Name == "XrplVanity.Gpu");
    }

    [Fact]
    public void Stops_Exactly_At_The_Limit_And_Counts_Candidates()
    {
        List<Match> found = new();
        using CancellationTokenSource stop = new();
        MatchSink sink = new(limit: 3, m => { lock (found) found.Add(m); });
        CpuSearchWorkers workers = new(threads: 2, OneChar(), sink);

        workers.Run(stop);   // returns once the sink refuses and every thread has stopped

        Assert.Equal(3, found.Count);
        Assert.All(found, m => Assert.StartsWith("ra", m.Address));
        Assert.True(workers.CandidatesSearched >= 3, $"counted {workers.CandidatesSearched}");
        Assert.True(workers.Elapsed > System.TimeSpan.Zero);
        Assert.True(stop.IsCancellationRequested, "reaching the limit cancels the pool");
    }

    [Fact]
    public void Caller_Cancellation_Stops_All_Threads()
    {
        PatternSet never = new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();
        using CancellationTokenSource stop = new(System.TimeSpan.FromMilliseconds(300));
        MatchSink sink = new(limit: null, _ => { });
        CpuSearchWorkers workers = new(threads: 4, never, sink);

        Thread runner = new(() => workers.Run(stop)) { IsBackground = true };
        runner.Start();

        Assert.True(runner.Join(System.TimeSpan.FromSeconds(10)), "workers did not stop after cancellation");
        Assert.True(workers.CandidatesSearched > 0, "no candidates counted before cancellation");
    }
}
