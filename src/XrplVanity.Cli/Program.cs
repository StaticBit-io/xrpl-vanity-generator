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
        if (!ResolveDevice(options, out IGpuDevice? device, out string reason))
        {
            throw new InvalidOperationException(reason);
        }

        using (device)
        {
            PatternSet patterns = BuildPatternSet(options);
            using GpuSearchEngine engine = new(device!, patterns);
            List<Match> all = new();
            for (int i = 0; i < launches; i++)
            {
                all.AddRange(engine.RunLaunch());
            }

            return all;
        }
    }

    /// <summary>
    /// Resolves the device a GPU run should use: an explicit <c>--backend</c> (optionally with
    /// <c>--device</c>), a bare <c>--device</c> indexing the flat list printed by
    /// <c>--list-devices</c>, or (neither given) the first usable device in backend preference
    /// order (CUDA, then OpenCL).
    /// </summary>
    public static bool ResolveDevice(CliOptions options, out IGpuDevice? device, out string reason)
    {
        if (options.Backend is null && options.Device is null)
        {
            return GpuBackends.TryOpenFirst(out device, out reason);
        }

        if (options.Backend is not null)
        {
            return GpuBackends.TryOpen(options.Backend, options.Device ?? 0, out device, out reason);
        }

        // --device without --backend indexes the flat list printed by --list-devices.
        int remaining = options.Device!.Value;
        foreach (IGpuBackend backend in GpuBackends.All)
        {
            int count = backend.EnumerateDevices().Count;
            if (remaining < count)
            {
                return GpuBackends.TryOpen(backend.Name, remaining, out device, out reason);
            }

            remaining -= count;
        }

        device = null;
        reason = $"device index {options.Device} is out of range; run --list-devices";
        return false;
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

        if (options.ListDevices)
        {
            return ListDevices();
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

        IGpuDevice? gpuDevice = null;
        if (options.Gpu)
        {
            Stopwatch resolveStopwatch = Stopwatch.StartNew();
            bool resolved = SearchRunner.ResolveDevice(options, out gpuDevice, out string reason);
            resolveStopwatch.Stop();

            if (!resolved)
            {
                Console.Error.WriteLine($"error: --gpu requested but no usable device: {reason}");
                return 2;
            }

            if (gpuDevice!.Backend == "opencl" && resolveStopwatch.Elapsed.TotalSeconds > 1.0)
            {
                Console.Error.WriteLine($"note: compiling the OpenCL program took {resolveStopwatch.Elapsed.TotalSeconds:F1} s");
            }
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

        if (gpuDevice is not null)
        {
            try
            {
                using (gpuDevice)
                {
                    RunGpu(gpuDevice, patterns, sink, cts);
                }
            }
            catch (XrplVanity.Gpu.CudaException ex)
            {
                Console.Error.WriteLine($"error: GPU search failed: {ex.Message}");
                return 2;
            }
            catch (XrplVanity.Gpu.OpenCl.OpenClException ex)
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

    private static int ListDevices()
    {
        int flat = 0;
        bool any = false;
        foreach (IGpuBackend backend in GpuBackends.All)
        {
            foreach (GpuDeviceInfo info in backend.EnumerateDevices())
            {
                Console.WriteLine($"{flat,2}  {backend.Name}:{info.Index}  {info.Name}  ({info.Details})");
                flat++;
                any = true;
            }
        }

        if (!any)
        {
            Console.Error.WriteLine("no GPU devices found (no CUDA driver and no OpenCL platform).");
            return 2;
        }

        return 0;
    }

    private static void RunGpu(IGpuDevice device, PatternSet patterns, MatchSink sink, CancellationTokenSource cts)
    {
        using GpuSearchEngine engine = new(device, patterns);
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
