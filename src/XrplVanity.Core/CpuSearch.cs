using System;
using System.Threading;
using System.Threading.Tasks;

namespace XrplVanity.Core;

/// <summary>What a <see cref="CpuSearch.FindFirstAsync"/> call produced; <see cref="Found"/> is null when it was cancelled first.</summary>
public readonly record struct CpuSearchOutcome(Match? Found, long Candidates, TimeSpan Elapsed);

/// <summary>
/// One-call entry points over <see cref="CpuSearchWorkers"/>. The workers are bare threads, call
/// back on a worker thread and cancel the source they are given; here the caller's token is only
/// ever observed, never handed over, the callback merely stores the match, and cancellation is an
/// outcome, not an exception. A failure on a worker thread (the system RNG, out of memory) is not
/// an outcome: it stops the other workers and, once all of them have stopped, faults the returned
/// task with the original exception, unless a match was already found. Progress is reported on an
/// arbitrary thread, so a caller that owns UI state must marshal it.
/// </summary>
public static class CpuSearch
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    // Never matches in practice, so a rate probe measures pure derivation and matching cost.
    private static readonly PatternSet ProbePatterns =
        new PatternSet.Builder().Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false).Build();

    /// <summary>
    /// Measures candidates per second over <paramref name="duration"/>. Cancelling ends the probe
    /// early and returns the rate measured so far; a token that is already cancelled returns 0.
    /// Arguments are validated before the token is looked at, so a bad one throws whatever the token's state.
    /// A failure on a worker thread faults the returned task with the original exception, after
    /// every worker has stopped.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threads"/> is below 1, or <paramref name="duration"/> is not positive.</exception>
    public static async Task<double> MeasureRateAsync(int threads, TimeSpan duration, CancellationToken cancellationToken)
    {
        ValidateThreads(threads);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "The duration must be positive.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }

        MatchSink sink = new(limit: null, _ => { });
        CpuSearchWorkers workers = new(threads, ProbePatterns, sink);
        (long candidates, TimeSpan elapsed) = await RunAsync(workers, duration, progress: null, cancellationToken)
            .ConfigureAwait(false);

        return elapsed > TimeSpan.Zero ? candidates / elapsed.TotalSeconds : 0;
    }

    /// <summary>
    /// Searches until one address matches or <paramref name="cancellationToken"/> is cancelled.
    /// Cancellation is an outcome with <see cref="CpuSearchOutcome.Found"/> null, not an exception.
    /// <paramref name="progress"/> receives the running candidate count about once a second and
    /// once more with the final count, on an arbitrary thread, the final report included: pass a
    /// <see cref="Progress{T}"/> created on the UI thread to have it marshalled there. Arguments
    /// are validated before the token is looked at, so a bad one throws whatever the token's state.
    /// A failure on a worker thread or in <paramref name="progress"/> faults the returned task with
    /// the original exception, after every worker has stopped, but only when no match was stored:
    /// a match is returned as usual and the failure is dropped. Either way the final progress
    /// report is skipped.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="patterns"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="patterns"/> holds nothing reachable to search for.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threads"/> is below 1.</exception>
    public static async Task<CpuSearchOutcome> FindFirstAsync(
        PatternSet patterns, int threads, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (!patterns.HasPatterns)
        {
            throw new ArgumentException("The pattern set has no reachable pattern to search for.", nameof(patterns));
        }

        ValidateThreads(threads);
        if (cancellationToken.IsCancellationRequested)
        {
            return default;
        }

        Match? found = null;
        MatchSink sink = new(limit: 1, match => found = match);
        CpuSearchWorkers workers = new(threads, patterns, sink);
        return await CollectFirstAsync(workers, () => found, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The search behind <see cref="FindFirstAsync"/>, over workers built by the caller so tests
    /// can fail one after a match was stored. <paramref name="found"/> is read only after the
    /// workers have stopped.
    /// </summary>
    internal static async Task<CpuSearchOutcome> CollectFirstAsync(
        CpuSearchWorkers workers, Func<Match?> found, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        try
        {
            (long candidates, TimeSpan elapsed) = await RunAsync(workers, limit: null, progress, cancellationToken)
                .ConfigureAwait(false);

            // Run has returned, so every worker is joined and the sink's write is visible here.
            return new CpuSearchOutcome(found(), candidates, elapsed);
        }
        catch (Exception) when (found() is Match match)
        {
            // After a long search the key is what the user waited for, and it cannot be found again.
            return new CpuSearchOutcome(match, workers.CandidatesSearched, workers.Elapsed);
        }
    }

    private static void ValidateThreads(int threads)
    {
        if (threads < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threads), threads, "At least one CPU thread is required.");
        }
    }

    private static async Task<(long Candidates, TimeSpan Elapsed)> RunAsync(
        CpuSearchWorkers workers, TimeSpan? limit, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        // Run cancels the source it is given when the sink refuses a match, so it gets a private
        // one. It is disposed only after Run has returned: workers read it until they are joined.
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (limit is TimeSpan after)
        {
            stop.CancelAfter(after);
        }

        // Run blocks for the whole search, so it must not take a thread-pool thread.
        Task run = Task.Factory.StartNew(
            () => workers.Run(stop), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            if (progress is null)
            {
                await run.ConfigureAwait(false);
            }
            else
            {
                await ReportWhileRunningAsync(run, workers, progress).ConfigureAwait(false);
            }
        }
        finally
        {
            // Also reached when the progress callback throws: the workers must not outlive the call.
            stop.Cancel();
            await run.ConfigureAwait(false);
        }

        long candidates = workers.CandidatesSearched;
        progress?.Report(candidates);
        return (candidates, workers.Elapsed);
    }

    private static async Task ReportWhileRunningAsync(Task run, CpuSearchWorkers workers, IProgress<long> progress)
    {
        using PeriodicTimer timer = new(ProgressInterval);
        Task tick = timer.WaitForNextTickAsync().AsTask();
        while (await Task.WhenAny(run, tick).ConfigureAwait(false) != run)
        {
            progress.Report(workers.CandidatesSearched);
            tick = timer.WaitForNextTickAsync().AsTask();
        }
    }
}
