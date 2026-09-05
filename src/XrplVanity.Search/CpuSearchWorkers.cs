using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using XrplVanity.Core;

namespace XrplVanity.Search;

/// <summary>
/// The CPU search: N threads, each with its own deriver and a 512-candidate CSPRNG pool,
/// reporting through the shared sink. The candidate counter is bumped once per pool refill,
/// so it lags the truth by at most 512 per thread.
/// </summary>
internal sealed class CpuSearchWorkers
{
    private const int PoolCandidates = 512;
    private const int EntropyBytes = 16;

    private readonly int _threads;
    private readonly PatternSet _patterns;
    private readonly MatchSink _sink;
    private readonly Stopwatch _elapsed = new();
    private long _candidates;

    public CpuSearchWorkers(int threads, PatternSet patterns, MatchSink sink)
    {
        if (threads < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threads), threads, "At least one CPU thread is required.");
        }

        _threads = threads;
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public long CandidatesSearched => Volatile.Read(ref _candidates);
    public TimeSpan Elapsed => _elapsed.Elapsed;

    /// <summary>Runs until <paramref name="stop"/> is cancelled or the sink refuses a match (which cancels <paramref name="stop"/>).</summary>
    public void Run(CancellationTokenSource stop)
    {
        _elapsed.Restart();
        Thread[] workers = new Thread[_threads];
        for (int t = 0; t < workers.Length; t++)
        {
            workers[t] = new Thread(() => Worker(stop)) { IsBackground = true, Name = $"cpu-{t}" };
            workers[t].Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        _elapsed.Stop();
    }

    private void Worker(CancellationTokenSource stop)
    {
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), _patterns);
        byte[] pool = new byte[EntropyBytes * PoolCandidates];
        int poolPos = pool.Length;
        int sinceRefill = 0;

        while (!stop.IsCancellationRequested)
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
