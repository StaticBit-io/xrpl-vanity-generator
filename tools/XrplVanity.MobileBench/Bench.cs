using System.Diagnostics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using XrplVanity.Core;

namespace XrplVanity.MobileBench;

/// <summary>How long each part of a run lasts; zero skips the sustained part.</summary>
internal readonly record struct BenchOptions(int StageSeconds, int PipelineSeconds, int SustainSeconds, int WindowSeconds);

/// <summary>
/// Prices the CPU search on the device it runs on: each stage of a derivation alone, the whole
/// candidate loop at several thread counts, and the rate over a long run, where thermal throttling
/// shows. No Android types here, so the same file runs on the desktop as a baseline.
/// </summary>
internal static class Bench
{
    private const int EntropyBytes = 16;
    private const int PoolCandidates = 512;

    /// <summary>Long enough never to match, so the loop measures derivation and not match handling.</summary>
    private const string NeverPattern = "zzzzzzzz";

    public static void Run(BenchOptions options, Action<string> log, Func<string>? probe)
    {
        CheckDeriversAgree(log);
        MeasureStages(options.StageSeconds, log);

        int cores = Environment.ProcessorCount;
        int[] threadCounts = new[] { 1, 2, 4, cores }.Where(n => n <= cores).Distinct().ToArray();

        log("pipeline (candidates/s):");
        foreach (int threads in threadCounts)
        {
            double platform = MeasurePipeline(threads, options.PipelineSeconds, () => new BouncyCastleDeriver());
            double managed = MeasurePipeline(threads, options.PipelineSeconds, () => new ManagedDigestDeriver());
            log($"  threads={threads,-2} shipped={platform,10:N0}  managed-digests={managed,10:N0}");
        }

        if (options.SustainSeconds > 0)
        {
            MeasureSustained(cores, options, log, probe);
        }
    }

    private static void CheckDeriversAgree(Action<string> log)
    {
        IAddressDeriver shipped = new BouncyCastleDeriver();
        IAddressDeriver managed = new ManagedDigestDeriver();
        byte[] entropy = new byte[EntropyBytes];
        char[] a = new char[48];
        char[] b = new char[48];
        for (int i = 0; i < 64; i++)
        {
            RandomNumberGenerator.Fill(entropy);
            int lengthA = shipped.DeriveAddress(entropy, a);
            int lengthB = managed.DeriveAddress(entropy, b);
            if (!a.AsSpan(0, lengthA).SequenceEqual(b.AsSpan(0, lengthB)))
            {
                throw new InvalidOperationException("The two derivers disagree; rates from them would mean nothing.");
            }
        }

        log("derivers agree on 64 random entropies");
    }

    private static void MeasureStages(int seconds, Action<string> log)
    {
        byte[] entropy = RandomNumberGenerator.GetBytes(EntropyBytes);
        byte[] pool = new byte[EntropyBytes * PoolCandidates];
        byte[] hash512 = new byte[64];
        byte[] hash256 = new byte[32];
        byte[] pub33 = RandomNumberGenerator.GetBytes(33);
        byte[] pub32 = new byte[32];
        byte[] payload = RandomNumberGenerator.GetBytes(25);
        payload[0] = 0;
        char[] text = new char[48];
        Sha512Digest sha512 = new();
        Sha256Digest sha256 = new();
        RipeMD160Digest ripemd = new();
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, NeverPattern, ignoreCase: false).Build();
        int textLength = XrplBase58.Encode(payload, text);

        log("stages (microseconds per call, single thread):");
        Stage("Ed25519 public key", seconds, log, () => Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.GeneratePublicKey(hash512.AsSpan(0, 32), pub32));
        Stage("SHA512.HashData 16 B", seconds, log, () => SHA512.HashData(entropy, hash512));
        Stage("SHA256.HashData 33 B", seconds, log, () => SHA256.HashData(pub33, hash256));
        Stage("BC Sha512Digest 16 B", seconds, log, () =>
        {
            sha512.BlockUpdate(entropy);
            sha512.DoFinal(hash512);
        });
        Stage("BC Sha256Digest 33 B", seconds, log, () =>
        {
            sha256.BlockUpdate(pub33);
            sha256.DoFinal(hash256);
        });
        Stage("BC RipeMD160 32 B", seconds, log, () =>
        {
            ripemd.BlockUpdate(hash256);
            ripemd.DoFinal(payload.AsSpan(1));
        });
        Stage("Base58 encode 25 B", seconds, log, () => XrplBase58.Encode(payload, text));
        Stage("Pattern match", seconds, log, () => patterns.TryMatch(text.AsSpan(0, textLength), out _, out _));
        Stage("CSPRNG, per candidate", seconds, log, () => RandomNumberGenerator.Fill(pool), PoolCandidates);
    }

    private static void Stage(string name, int seconds, Action<string> log, Action call, int candidatesPerCall = 1)
    {
        for (int i = 0; i < 32; i++)
        {
            call();
        }

        long calls = 0;
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            for (int i = 0; i < 16; i++)
            {
                call();
            }

            calls += 16;
        }

        watch.Stop();
        double micros = watch.Elapsed.TotalMilliseconds * 1000.0 / (calls * candidatesPerCall);
        log($"  {name,-24}{micros,10:N2}");
    }

    private static double MeasurePipeline(int threads, int seconds, Func<IAddressDeriver> deriver)
    {
        long[] counters = new long[threads];
        using ManualResetEventSlim go = new(false);
        using CancellationTokenSource stop = new();
        Thread[] workers = StartWorkers(threads, counters, go, stop.Token, deriver);

        go.Set();
        Stopwatch watch = Stopwatch.StartNew();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        stop.Cancel();
        JoinAll(workers);
        watch.Stop();
        return counters.Sum() / watch.Elapsed.TotalSeconds;
    }

    private static void MeasureSustained(int threads, BenchOptions options, Action<string> log, Func<string>? probe)
    {
        log($"sustained, {threads} threads, shipped deriver, {options.WindowSeconds}s windows:");
        long[] counters = new long[threads];
        using ManualResetEventSlim go = new(false);
        using CancellationTokenSource stop = new();
        Thread[] workers = StartWorkers(threads, counters, go, stop.Token, () => new BouncyCastleDeriver());

        go.Set();
        Stopwatch watch = Stopwatch.StartNew();
        long previous = 0;
        double previousAt = 0;
        while (watch.Elapsed.TotalSeconds < options.SustainSeconds)
        {
            Thread.Sleep(TimeSpan.FromSeconds(options.WindowSeconds));
            long total = ReadAll(counters);
            double now = watch.Elapsed.TotalSeconds;
            double rate = (total - previous) / (now - previousAt);
            log($"  t={now,6:N0}s {rate,10:N0}/s {probe?.Invoke()}");
            previous = total;
            previousAt = now;
        }

        stop.Cancel();
        JoinAll(workers);
        log($"  average {ReadAll(counters) / watch.Elapsed.TotalSeconds:N0}/s");
    }

    private static Thread[] StartWorkers(int threads, long[] counters, ManualResetEventSlim go, CancellationToken stop, Func<IAddressDeriver> deriver)
    {
        Thread[] workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            int slot = t;
            workers[t] = new Thread(() => Worker(slot, counters, go, stop, deriver())) { IsBackground = true, Name = $"bench-{t}" };
            workers[t].Start();
        }

        return workers;
    }

    /// <summary>The loop of <c>CpuSearchWorkers.Worker</c>, minus the sink: same pool, same evaluator.</summary>
    private static void Worker(int slot, long[] counters, ManualResetEventSlim go, CancellationToken stop, IAddressDeriver deriver)
    {
        PatternSet patterns = new PatternSet.Builder().Add(MatchPosition.Starts, NeverPattern, ignoreCase: false).Build();
        CandidateEvaluator evaluator = new(deriver, patterns);
        byte[] pool = new byte[EntropyBytes * PoolCandidates];
        go.Wait();

        while (!stop.IsCancellationRequested)
        {
            RandomNumberGenerator.Fill(pool);
            for (int offset = 0; offset < pool.Length && !stop.IsCancellationRequested; offset += EntropyBytes)
            {
                evaluator.TryEvaluate(pool.AsSpan(offset, EntropyBytes), out _);
                Volatile.Write(ref counters[slot], counters[slot] + 1);
            }
        }
    }

    private static long ReadAll(long[] counters)
    {
        long total = 0;
        for (int i = 0; i < counters.Length; i++)
        {
            total += Volatile.Read(ref counters[i]);
        }

        return total;
    }

    private static void JoinAll(Thread[] workers)
    {
        foreach (Thread worker in workers)
        {
            worker.Join();
        }
    }
}
