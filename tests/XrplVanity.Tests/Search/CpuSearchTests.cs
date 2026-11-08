using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests.Search;

[Collection(XrplVanity.Tests.CpuBoundCollection.Name)]
public class CpuSearchTests
{
    private static PatternSet OneChar() => new PatternSet.Builder().Add(MatchPosition.Starts, "a", ignoreCase: false).Build();

    private static PatternSet Never() => new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();

    // Runs the report on the calling thread, so the test sees every call in order without a sync context.
    private sealed class SyncProgress : IProgress<long>
    {
        public List<long> Reports { get; } = new();

        public void Report(long value) => Reports.Add(value);
    }

    private sealed class ThrowingProgress : IProgress<long>
    {
        public void Report(long value) => throw new InvalidOperationException("progress failed");
    }

    [Fact]
    public async Task FindFirst_Returns_The_Match()
    {
        CpuSearchOutcome outcome = await CpuSearch.FindFirstAsync(OneChar(), threads: 2, progress: null, CancellationToken.None);

        Assert.NotNull(outcome.Found);
        Assert.StartsWith("ra", outcome.Found!.Value.Address);
        Assert.True(outcome.Candidates >= 1, $"counted {outcome.Candidates}");
    }

    [Fact]
    public async Task FindFirst_Cancelled_Returns_No_Match()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        Stopwatch watch = Stopwatch.StartNew();

        CpuSearchOutcome outcome = await CpuSearch.FindFirstAsync(Never(), threads: 2, progress: null, cts.Token);

        Assert.Null(outcome.Found);
        Assert.True(outcome.Candidates > 0, "no candidates counted before cancellation");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task FindFirst_With_A_Token_Already_Cancelled()
    {
        CpuSearchOutcome outcome = await CpuSearch.FindFirstAsync(Never(), threads: 2, progress: null, new CancellationToken(canceled: true));

        Assert.Null(outcome.Found);
        Assert.Equal(0, outcome.Candidates);
    }

    [Fact]
    public async Task FindFirst_Refuses_A_Set_With_No_Patterns()
    {
        PatternSet empty = new PatternSet.Builder().Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => CpuSearch.FindFirstAsync(empty, threads: 1, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task FindFirst_Reports_Progress()
    {
        SyncProgress progress = new();
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(2500));

        CpuSearchOutcome outcome = await CpuSearch.FindFirstAsync(Never(), threads: 2, progress, cts.Token);

        Assert.True(progress.Reports.Count >= 2, $"{progress.Reports.Count} reports");
        for (int i = 1; i < progress.Reports.Count; i++)
        {
            Assert.True(progress.Reports[i] >= progress.Reports[i - 1], "progress went backwards");
        }

        Assert.Equal(outcome.Candidates, progress.Reports[^1]);
    }

    [Fact]
    public async Task MeasureRate_Is_Positive_And_Takes_About_The_Duration()
    {
        Stopwatch watch = Stopwatch.StartNew();

        double rate = await CpuSearch.MeasureRateAsync(threads: 2, TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.True(rate > 0, $"rate {rate}");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task MeasureRate_Stops_When_Cancelled()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));
        Stopwatch watch = Stopwatch.StartNew();

        await CpuSearch.MeasureRateAsync(threads: 2, TimeSpan.FromSeconds(60), cts.Token);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task MeasureRate_Refuses_A_Duration_Of_Zero()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CpuSearch.MeasureRateAsync(threads: 1, TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task Bad_Arguments_Throw_Even_With_A_Cancelled_Token()
    {
        CancellationToken cancelled = new(canceled: true);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CpuSearch.FindFirstAsync(OneChar(), threads: 0, progress: null, cancelled));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CpuSearch.MeasureRateAsync(threads: 0, TimeSpan.FromSeconds(1), cancelled));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CpuSearch.MeasureRateAsync(threads: 1, TimeSpan.Zero, cancelled));
        await Assert.ThrowsAsync<ArgumentException>(
            () => CpuSearch.FindFirstAsync(new PatternSet.Builder().Build(), threads: 1, progress: null, cancelled));
    }

    [Fact]
    public async Task MeasureRate_Returns_Zero_For_A_Token_Already_Cancelled()
    {
        double rate = await CpuSearch.MeasureRateAsync(threads: 2, TimeSpan.FromSeconds(1), new CancellationToken(canceled: true));

        Assert.Equal(0, rate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Worker_Failure_With_No_Match_Reaches_The_Caller_As_An_Exception(bool withProgress)
    {
        InvalidOperationException failure = new("sink failed");
        MatchSink sink = new(limit: null, _ => throw failure);
        CpuSearchWorkers workers = new(threads: 2, OneChar(), sink);
        IProgress<long>? progress = withProgress ? new SyncProgress() : null;

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CpuSearch.CollectFirstAsync(workers, () => null, progress, CancellationToken.None));

        Assert.Same(failure, thrown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Stored_Match_Survives_A_Worker_Failure(bool withProgress)
    {
        Match? found = null;
        MatchSink sink = new(limit: 1, match =>
        {
            found = match;
            throw new InvalidOperationException("failed after the match was stored");
        });
        CpuSearchWorkers workers = new(threads: 1, OneChar(), sink);
        IProgress<long>? progress = withProgress ? new SyncProgress() : null;

        CpuSearchOutcome outcome = await CpuSearch.CollectFirstAsync(workers, () => found, progress, CancellationToken.None);

        Assert.NotNull(outcome.Found);
        Assert.StartsWith("ra", outcome.Found!.Value.Address);
        Assert.True(outcome.Elapsed > TimeSpan.Zero, $"elapsed {outcome.Elapsed}");
    }

    [Fact]
    public async Task A_Throwing_Progress_Does_Not_Cost_A_Found_Match()
    {
        CpuSearchOutcome outcome = await CpuSearch.FindFirstAsync(OneChar(), threads: 2, new ThrowingProgress(), CancellationToken.None);

        Assert.NotNull(outcome.Found);
    }

    [Fact]
    public void Match_ToString_Hides_The_Seed()
    {
        Match match = new("rAddress", "sEdSecret", "ab", MatchPosition.Starts);

        string text = match.ToString();

        Assert.Contains("rAddress", text);
        Assert.DoesNotContain("sEdSecret", text);
    }
}
