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

    [Fact]
    public void A_Failing_Worker_Is_Rethrown_From_Run_After_Every_Thread_Has_Stopped()
    {
        const int threads = 4;
        HashSet<Thread> seen = new();
        InvalidOperationException failure = new("sink failed");
        using CancellationTokenSource stop = new();

        // Throws only once every worker has reported, so the set holds each thread Run started.
        MatchSink sink = new(limit: null, _ =>
        {
            seen.Add(Thread.CurrentThread);
            if (seen.Count == threads)
            {
                throw failure;
            }
        });
        CpuSearchWorkers workers = new(threads, OneChar(), sink);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => workers.Run(stop));

        Assert.Same(failure, thrown);
        Assert.False(stop.IsCancellationRequested, "a failure stops this pool, not what shares the source");
        Assert.Equal(threads, seen.Count);
        Assert.All(seen, thread => Assert.False(thread.IsAlive, $"{thread.Name} outlived Run"));
    }

    [Fact]
    public void A_Thread_That_Cannot_Start_Stops_The_Ones_Already_Running()
    {
        PatternSet never = new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();
        List<Thread> started = new();
        InvalidOperationException failure = new("no more threads");
        using CancellationTokenSource stop = new();
        MatchSink sink = new(limit: null, _ => { });
        CpuSearchWorkers workers = new(threads: 3, never, sink, thread =>
        {
            if (started.Count == 2)
            {
                throw failure;
            }

            thread.Start();
            started.Add(thread);
        });

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => workers.Run(stop));

        Assert.Same(failure, thrown);
        Assert.False(stop.IsCancellationRequested, "a failed start stops this pool, not what shares the source");
        Assert.Equal(2, started.Count);
        Assert.All(started, thread => Assert.False(thread.IsAlive, $"{thread.Name} outlived Run"));
    }

    [Fact]
    public void A_Throwing_Cancel_Callback_At_The_Limit_Ends_The_Run_As_A_Failure()
    {
        using CancellationTokenSource stop = new();
        stop.Token.Register(() => throw new ArithmeticException("cancel callback failed"));
        MatchSink sink = new(limit: 1, _ => { });
        CpuSearchWorkers workers = new(threads: 2, OneChar(), sink);

        AggregateException thrown = Assert.Throws<AggregateException>(() => workers.Run(stop));

        Assert.IsType<ArithmeticException>(Assert.Single(thrown.InnerExceptions));
    }
}
