using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Parses a <c>--gpu-devices</c> spec: <c>all</c> (every device on every backend), a bare
    /// backend name (<c>cuda</c> or <c>opencl</c>, case-insensitive — every device on that
    /// backend), or a comma-separated list of <c>backend:index</c> pairs and/or flat indices
    /// from <c>--list-devices</c>.
    /// </summary>
    internal static bool ParseDeviceSpec(string spec, IReadOnlyList<IGpuBackend> backends, out List<DeviceSelector> selectors, out string reason)
    {
        selectors = new List<DeviceSelector>();
        string trimmed = spec.Trim();
        if (trimmed.Length == 0)
        {
            reason = "--gpu-devices needs a value: all, cuda, opencl, or a comma-separated list of backend:index or indices";
            return false;
        }

        if (trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (IGpuBackend backend in backends)
            {
                foreach (GpuDeviceInfo info in backend.EnumerateDevices())
                {
                    selectors.Add(new DeviceSelector(backend.Name, info.Index));
                }
            }

            reason = selectors.Count == 0 ? "no matching device (no CUDA driver and no OpenCL platform)" : string.Empty;
            return selectors.Count > 0;
        }

        // A bare token equal to a known backend name is handled here, before the backend:index /
        // flat-index parser below: a known name whose backend is absent from `backends`, or that
        // enumerates no devices, is a "no matching device" condition, not "unknown token".
        if (trimmed.Equals("cuda", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("opencl", StringComparison.OrdinalIgnoreCase))
        {
            IGpuBackend? named = FindBackend(backends, trimmed);
            if (named is null || named.EnumerateDevices().Count == 0)
            {
                reason = $"no matching device for backend '{trimmed}'";
                return false;
            }

            foreach (GpuDeviceInfo info in named.EnumerateDevices())
            {
                selectors.Add(new DeviceSelector(named.Name, info.Index));
            }

            reason = string.Empty;
            return true;
        }

        List<GpuDeviceInfo> flat = new();
        foreach (IGpuBackend backend in backends)
        {
            flat.AddRange(backend.EnumerateDevices());
        }

        foreach (string raw in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = raw.IndexOf(':');
            if (colon > 0)
            {
                IGpuBackend? backend = FindBackend(backends, raw[..colon]);
                if (backend is null || !int.TryParse(raw[(colon + 1)..], out int index) || index < 0)
                {
                    reason = $"'{raw}' is not a known backend:index (known backends: {string.Join(", ", backends.Select(b => b.Name))})";
                    return false;
                }

                if (index >= backend.EnumerateDevices().Count)
                {
                    reason = $"'{raw}' is out of range for backend '{backend.Name}'; run --list-devices";
                    return false;
                }

                selectors.Add(new DeviceSelector(backend.Name, index));
            }
            else if (int.TryParse(raw, out int flatIndex) && flatIndex >= 0)
            {
                if (flatIndex >= flat.Count)
                {
                    reason = $"device index {flatIndex} is out of range; run --list-devices";
                    return false;
                }

                selectors.Add(new DeviceSelector(flat[flatIndex].Backend, flat[flatIndex].Index));
            }
            else
            {
                reason = $"'{raw}' is neither backend:index nor an index from --list-devices";
                return false;
            }
        }

        reason = selectors.Count == 0 ? "no matching device" : string.Empty;
        return selectors.Count > 0;
    }

    private static IGpuBackend? FindBackend(IReadOnlyList<IGpuBackend> backends, string name)
    {
        foreach (IGpuBackend backend in backends)
        {
            if (string.Equals(backend.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return backend;
            }
        }

        return null;
    }

    public static bool ResolveDevices(CliOptions options, out List<DeviceSelector> selectors, out string reason) =>
        ParseDeviceSpec(options.GpuDevices ?? string.Empty, GpuBackends.All, out selectors, out reason);
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
        List<DeviceSelector>? gpuSelectors = null;
        if (options.GpuDevices is not null)
        {
            if (!SearchRunner.ResolveDevices(options, out gpuSelectors, out string reason))
            {
                Console.Error.WriteLine($"error: --gpu-devices: {reason}");
                return 2;
            }
        }
        else if (options.Gpu)
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

        if (gpuSelectors is not null)
        {
            int rc = RunMultiGpu(gpuSelectors, patterns, sink, cts);
            if (rc != 0) return rc;
        }
        else if (gpuDevice is not null)
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
                string tag = info.IsCpuDevice ? " [cpu]" : string.Empty;
                Console.WriteLine($"{flat,2}  {backend.Name}:{info.Index}  {info.Name}  ({info.Details}){tag}");
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

    private static int RunMultiGpu(IReadOnlyList<DeviceSelector> selectors, PatternSet patterns, MatchSink sink, CancellationTokenSource cts)
    {
        MultiDeviceSearch search = new(selectors, patterns,
            match =>
            {
                bool more = sink.TryReport(match);
                if (!more) cts.Cancel();
                return more;
            },
            line => Console.Error.WriteLine(line));

        MultiDeviceSearchResult result = search.Run(cts.Token);

        foreach (DeviceSearchStats d in result.Devices)
        {
            if (d.Failure is not null && !d.Opened)
            {
                Console.Error.WriteLine($"GPU[{d.Selector}]: failed: {d.Failure}");
                continue;
            }

            double seconds = Math.Max(d.Seconds, 1e-9);
            string tail = d.Failure is null ? string.Empty : $" (stopped: {d.Failure})";
            Console.Error.WriteLine(
                $"GPU[{d.Selector}]: searched {d.CandidatesSearched:N0} candidates in {seconds:F1} s " +
                $"({d.CandidatesSearched / seconds:N0}/s); consistency errors: {d.ConsistencyErrors}.{tail}");
        }

        if (result.DevicesOpened == 0)
        {
            Console.Error.WriteLine("error: --gpu-devices: no device could be opened.");
            return 2;
        }

        double total = Math.Max(result.Seconds, 1e-9);
        Console.Error.WriteLine(
            $"GPU: searched {result.TotalCandidates:N0} candidates on {result.DevicesOpened} device(s) in {total:F1} s " +
            $"({result.TotalCandidates / total:N0}/s); consistency errors: {result.TotalConsistencyErrors}.");
        return 0;
    }
}
