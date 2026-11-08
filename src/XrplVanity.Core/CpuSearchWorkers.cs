using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;

namespace XrplVanity.Core;

/// <summary>
/// The CPU search: N threads, each with its own deriver and a 512-candidate CSPRNG pool,
/// reporting through the shared sink. The candidate counter is bumped once per pool refill,
/// so it lags the truth by at most 512 per thread.
/// </summary>
public sealed class CpuSearchWorkers
{
    private const int PoolCandidates = 512;
    private const int EntropyBytes = 16;

    private readonly int _threads;
    private readonly PatternSet _patterns;
    private readonly MatchSink _sink;
    private readonly Action<Thread> _startThread;
    private readonly Stopwatch _elapsed = new();
    private long _candidates;
    private Exception? _failure;

    public CpuSearchWorkers(int threads, PatternSet patterns, MatchSink sink)
        : this(threads, patterns, sink, static thread => thread.Start())
    {
    }

    /// <summary>
    /// Takes the way a thread is started as a parameter so tests can make it fail partway through
    /// the pool, which <see cref="Thread.Start()"/> itself only does when the OS runs out of threads.
    /// </summary>
    internal CpuSearchWorkers(int threads, PatternSet patterns, MatchSink sink, Action<Thread> startThread)
    {
        if (threads < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threads), threads, "At least one CPU thread is required.");
        }

        _threads = threads;
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _startThread = startThread;
    }

    public long CandidatesSearched => Volatile.Read(ref _candidates);
    public TimeSpan Elapsed => _elapsed.Elapsed;

    /// <summary>
    /// Runs until <paramref name="stop"/> is cancelled, the sink refuses a match (which cancels
    /// <paramref name="stop"/>), or a worker fails. Every thread has stopped by the time this returns
    /// or throws. A failure stops these threads and leaves <paramref name="stop"/> as it is, so
    /// whatever else shares that source carries on. It is rethrown here with its original type and
    /// stack; when several workers fail, the first one wins and the rest are dropped.
    /// </summary>
    public void Run(CancellationTokenSource stop)
    {
        _failure = null;
        _elapsed.Restart();

        // Disposed only after the joins below: workers read it until they have stopped.
        using CancellationTokenSource poolStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        Thread[] workers = new Thread[_threads];
        int started = 0;
        try
        {
            for (; started < workers.Length; started++)
            {
                workers[started] = new Thread(() => Worker(stop, poolStop)) { IsBackground = true, Name = $"cpu-{started}" };
                _startThread(workers[started]);
            }
        }
        catch (Exception ex)
        {
            Fail(ex, poolStop);
        }
        finally
        {
            // Join throws on a thread that never started, so the one that failed to start is skipped.
            for (int t = 0; t < started; t++)
            {
                workers[t].Join();
            }

            _elapsed.Stop();
        }

        if (_failure is Exception failure)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    // An exception leaving a bare thread ends the process, which on a phone is the whole wallet app.
    private void Worker(CancellationTokenSource stop, CancellationTokenSource poolStop)
    {
        try
        {
            Search(stop, poolStop);
        }
        catch (Exception ex)
        {
            Fail(ex, poolStop);
        }
    }

    // Allocates nothing before the cancel: the failure being handled can be out of memory.
    // Nothing is registered on poolStop, so the cancel has no callback that could throw.
    private void Fail(Exception failure, CancellationTokenSource poolStop)
    {
        Interlocked.CompareExchange(ref _failure, failure, null);
        poolStop.Cancel();
    }

    private void Search(CancellationTokenSource stop, CancellationTokenSource poolStop)
    {
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), _patterns);
        byte[] pool = new byte[EntropyBytes * PoolCandidates];
        int poolPos = pool.Length;
        int sinceRefill = 0;

        while (!poolStop.IsCancellationRequested)
        {
            if (poolPos >= pool.Length)
            {
                Interlocked.Add(ref _candidates, sinceRefill);
                sinceRefill = 0;
                RandomNumberGenerator.Fill(pool);
                poolPos = 0;
            }

            ReadOnlySpan<byte> entropy = pool.AsSpan(poolPos, EntropyBytes);
            poolPos += EntropyBytes;
            sinceRefill++;

            if (evaluator.TryEvaluate(entropy, out Match match) && !_sink.TryReport(match))
            {
                stop.Cancel();
                break;
            }
        }

        Interlocked.Add(ref _candidates, sinceRefill);
    }
}
