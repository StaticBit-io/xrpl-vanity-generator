using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using XrplVanity.Core;
using XrplVanity.Gpu;

namespace XrplVanity.Cli;

public static class SearchRunner
{
    public static PatternSet BuildPatternSet(CliOptions options)
    {
        PatternSet.Builder builder = new();
        foreach ((MatchPosition position, string pattern) in options.Patterns)
        {
            // Core classifies blank/empty patterns as unreachable (PatternSet.Builder.Add);
            // let them flow through so that classification - and the warning it drives - fires.
            builder.Add(position, pattern, ignoreCase: !options.CaseSensitive);
        }

        if (options.ListFile is not null)
        {
            foreach (string line in File.ReadLines(options.ListFile))
            {
                string word = line.Trim();
                if (word.Length == 0)
                {
                    continue;
                }

                if (word.Length >= options.ListMinLength)
                {
                    builder.Add(MatchPosition.Starts, word, ignoreCase: !options.CaseSensitive);
                }
            }
        }

        return builder.Build();
    }

    public static Match? SearchDeterministic(CliOptions options, IEnumerable<byte[]> entropySource)
    {
        PatternSet patterns = BuildPatternSet(options);
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);
        foreach (byte[] entropy in entropySource)
        {
            if (evaluator.TryEvaluate(entropy, out Match match))
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Headless GPU search: runs the given number of launches and returns every verified match.</summary>
    public static List<Match> SearchGpu(CliOptions options, int launches)
    {
        if (!CudaContext.TryCreate(out CudaContext? context, out string reason))
        {
            throw new InvalidOperationException($"CUDA is not available: {reason}");
        }

        using (context)
        {
            PatternSet patterns = BuildPatternSet(options);
            using GpuSearchEngine engine = new(context!, patterns);
            List<Match> all = new();
            for (int i = 0; i < launches; i++)
            {
                all.AddRange(engine.RunLaunch());
            }

            return all;
        }
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        if (options.Count is int c && c <= 0)
        {
            Console.Error.WriteLine("Option '--count' must be a positive number.");
            return 2;
        }

        PatternSet patterns = SearchRunner.BuildPatternSet(options);
        foreach (NormalizedPattern u in patterns.Unreachable)
        {
            if (u.Value.Length == 0)
            {
                Console.Error.WriteLine("warning: empty pattern ignored (it would match every address).");
            }
            else
            {
                Console.Error.WriteLine(
                    $"warning: pattern '{u.Original}' cannot occur in an XRPL address; skipped.");
            }
        }

        foreach (NormalizedPattern s in patterns.CaseFoldingSkipped)
        {
            Console.Error.WriteLine(
                $"warning: pattern '{s.Original}' is longer than {PatternSet.MaxIgnoreCaseLength} " +
                "characters; searching it case-sensitively instead of case-insensitively.");
        }

        if (!patterns.HasPatterns)
        {
            Console.Error.WriteLine("error: no reachable patterns to search for.");
            return 2;
        }

        CudaContext? gpuContext = null;
        if (options.Gpu && !CudaContext.TryCreate(out gpuContext, out string reason))
        {
            Console.Error.WriteLine($"error: --gpu requested but CUDA is not available: {reason}");
            return 2;
        }

        if (options.Gpu && options.ThreadsSpecified)
        {
            Console.Error.WriteLine("note: --threads is ignored with --gpu (the search runs on the device).");
        }

        Console.Error.WriteLine(
            "WARNING: a seed is a secret. Anyone who holds it controls the account. " +
            "Keep matches offline and verify this tool before using generated keys for real funds.");

        using MatchSink sink = new(options.Count, options.OutFile, Console.Out);
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (gpuContext is not null)
        {
            try
            {
                using (gpuContext)
                {
                    RunGpu(gpuContext, patterns, sink, cts);
                }
            }
            catch (XrplVanity.Gpu.CudaException ex)
            {
                Console.Error.WriteLine($"error: GPU search failed: {ex.Message}");
                return 2;
            }
        }
        else
        {
            RunCpu(options, patterns, sink, cts);
        }

        // 0 only when a --count target was fully satisfied; otherwise the run ended
        // via Ctrl+C (or a --count-less search the user stopped), reported as 130.
        return sink.CountSatisfied ? 0 : 130;
    }

    private static void RunCpu(CliOptions options, PatternSet patterns, MatchSink sink, CancellationTokenSource cts)
    {
        void Worker()
        {
            CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);
            byte[] pool = new byte[16 * 512];
            int poolPos = pool.Length;

            while (!cts.IsCancellationRequested)
            {
                if (poolPos >= pool.Length)
                {
                    RandomNumberGenerator.Fill(pool);
                    poolPos = 0;
                }

                ReadOnlySpan<byte> entropy = pool.AsSpan(poolPos, 16);
                poolPos += 16;

                if (evaluator.TryEvaluate(entropy, out Match match) && !sink.TryReport(match))
                {
                    cts.Cancel();
                    break;
                }
            }
        }

        Thread[] workers = new Thread[Math.Max(1, options.Threads)];
        for (int t = 0; t < workers.Length; t++)
        {
            workers[t] = new Thread(Worker) { IsBackground = true };
            workers[t].Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }
    }

    private static void RunGpu(CudaContext context, PatternSet patterns, MatchSink sink, CancellationTokenSource cts)
    {
        using GpuSearchEngine engine = new(context, patterns);
        Console.Error.WriteLine($"GPU: {engine.DeviceName}");
        Stopwatch stopwatch = Stopwatch.StartNew();
        long reportedOverflow = 0;
        long reportedErrors = 0;

        while (!cts.IsCancellationRequested)
        {
            foreach (Match match in engine.RunLaunch())
            {
                if (!sink.TryReport(match))
                {
                    cts.Cancel();
                    break;
                }
            }

            if (engine.OverflowDrops > reportedOverflow)
            {
                Console.Error.WriteLine(
                    $"warning: {engine.OverflowDrops - reportedOverflow:N0} hits dropped this launch " +
                    $"(hit buffer holds {GpuSearchEngine.HitCapacity}); use a more selective pattern.");
                reportedOverflow = engine.OverflowDrops;
            }

            if (engine.ConsistencyErrors > reportedErrors)
            {
                Console.Error.WriteLine(
                    $"GPU consistency error: {engine.ConsistencyErrors - reportedErrors} device hit(s) the CPU " +
                    "reference could not reproduce were discarded.");
                reportedErrors = engine.ConsistencyErrors;
            }
        }

        double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-9);
        Console.Error.WriteLine(
            $"GPU: searched {engine.CandidatesSearched:N0} candidates in {seconds:F1} s " +
            $"({engine.CandidatesSearched / seconds:N0}/s); consistency errors: {engine.ConsistencyErrors}.");
    }
}
