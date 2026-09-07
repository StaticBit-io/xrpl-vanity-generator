using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;

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

        // Same principle as above: a pair with an unreachable or empty half is not filtered
        // out here either, so it surfaces through PatternSet.Unreachable and its warning.
        foreach ((string starts, string ends) in options.Pairs)
        {
            builder.AddPair(starts, ends, ignoreCase: !options.CaseSensitive);
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

    /// <summary>
    /// Maps an explicit <c>--backend</c>/<c>--device</c> pair to a selector without opening the
    /// device: <c>--backend</c> with an optional index, or a bare <c>--device</c> indexing the
    /// flat list printed by <c>--list-devices</c>.
    /// </summary>
    public static bool TryResolveSelector(CliOptions options, out DeviceSelector? selector, out string reason)
    {
        if (options.Backend is not null)
        {
            IGpuBackend? backend = FindBackend(GpuBackends.All, options.Backend);
            int index = options.Device ?? 0;
            if (backend is null)
            {
                selector = null;
                reason = $"unknown backend '{options.Backend}' (known: cuda, opencl)";
                return false;
            }

            if (index < 0 || index >= backend.EnumerateDevices().Count)
            {
                selector = null;
                reason = $"device index {index} is out of range for backend '{backend.Name}'; run --list-devices";
                return false;
            }

            selector = new DeviceSelector(backend.Name, index);
            reason = string.Empty;
            return true;
        }

        int remaining = options.Device!.Value;
        foreach (IGpuBackend candidate in GpuBackends.All)
        {
            int count = candidate.EnumerateDevices().Count;
            if (remaining < count)
            {
                selector = new DeviceSelector(candidate.Name, remaining);
                reason = string.Empty;
                return true;
            }

            remaining -= count;
        }

        selector = null;
        reason = $"device index {options.Device} is out of range; run --list-devices";
        return false;
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return Run(args, Console.Out, Console.Error, cts.Token);
    }

    /// <summary>The whole CLI with its streams injected, so tests can drive it without touching the console.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellation = default)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }

        if (options.ListDevices)
        {
            return ListDevices(stdout, stderr);
        }

        if (options.Count is int c && c <= 0)
        {
            stderr.WriteLine("Option '--count' must be a positive number.");
            return 2;
        }

        PatternSet patterns = SearchRunner.BuildPatternSet(options);
        foreach (NormalizedPattern u in patterns.Unreachable)
        {
            if (u.Value.Length == 0)
            {
                stderr.WriteLine("warning: empty pattern ignored (it would match every address).");
            }
            else
            {
                stderr.WriteLine($"warning: pattern '{u.Original}' cannot occur in an XRPL address; skipped.");
            }
        }

        foreach (NormalizedPattern s in patterns.CaseFoldingSkipped)
        {
            stderr.WriteLine(
                $"warning: pattern '{s.Original}' is longer than {PatternSet.MaxIgnoreCaseLength} " +
                "characters; searching it case-sensitively instead of case-insensitively.");
        }

        if (!patterns.HasPatterns)
        {
            stderr.WriteLine("error: no reachable patterns to search for.");
            return 2;
        }

        List<SearchTarget> targets = new();
        bool multi = false;
        if (options.GpuDevices is not null)
        {
            if (!SearchRunner.ResolveDevices(options, out List<DeviceSelector> selectors, out string reason))
            {
                stderr.WriteLine($"error: --gpu-devices: {reason}");
                return 2;
            }

            foreach (DeviceSelector selector in selectors)
            {
                targets.Add(new SearchTarget.Device(selector));
            }

            multi = true;
        }
        else if (options.Gpu)
        {
            if (options.Backend is null && options.Device is null)
            {
                targets.Add(new SearchTarget.FirstAvailableDevice());
            }
            else if (!SearchRunner.TryResolveSelector(options, out DeviceSelector? selector, out string reason))
            {
                stderr.WriteLine($"error: --gpu requested but no usable device: {reason}");
                return 2;
            }
            else
            {
                targets.Add(new SearchTarget.Device(selector!));
            }
        }
        else
        {
            targets.Add(new SearchTarget.Cpu(Math.Max(1, options.Threads)));
        }

        if (options.Gpu && options.ThreadsSpecified)
        {
            stderr.WriteLine("note: --threads is ignored with --gpu (the search runs on the device).");
        }

        if (options.Gpu && options.GpuDuty < DutyCycle.Full)
        {
            stderr.WriteLine(multi
                ? $"GPU: duty cycle {options.GpuDuty}% on every device (pausing between launches)."
                : $"GPU: duty cycle {options.GpuDuty}% (pausing between launches).");
        }

        stderr.WriteLine(
            "WARNING: a seed is a secret. Anyone who holds it controls the account. " +
            "Keep matches offline and verify this tool before using generated keys for real funds.");

        using CliMatchWriter writer = new(options.OutFile, stdout);
        SearchRequest request = new(patterns, targets, options.Count, options.GpuDuty);
        using SearchSession session = new(request, writer.Write, line => stderr.WriteLine(line));

        HashSet<string> announced = new(StringComparer.Ordinal);
        session.ProgressChanged += progress => AnnounceDevices(progress, multi, announced, stderr);

        using CancellationTokenRegistration registration = cancellation.Register(session.Stop);
        session.Start();
        SearchSummary summary = session.Completion.GetAwaiter().GetResult();

        // A run short enough to finish inside one 250 ms tick never saw a Running snapshot; the
        // final one still carries the device name, so the announcement is not lost.
        AnnounceDevices(summary.Final, multi, announced, stderr);
        RenderSummary(summary, multi, stderr);
        return ExitCodeFor(summary, multi, stderr);
    }

    /// <summary>
    /// Writes the one-per-device name line the moment a device target is known to have opened —
    /// while it runs, and equally once it has finished or failed, so a run that completes inside
    /// a single progress tick still announces its device. A target that never opened keeps its
    /// selector as its <see cref="TargetProgress.Name"/> and is skipped. Called from the progress
    /// handler, so it must never throw: a throw there stops the search.
    /// </summary>
    internal static void AnnounceDevices(SearchProgress progress, bool multi, HashSet<string> announced, TextWriter stderr)
    {
        foreach (TargetProgress t in progress.Targets)
        {
            bool opened = t.State is TargetState.Running or TargetState.Finished or TargetState.Failed
                && !string.Equals(t.Name, t.Id, StringComparison.Ordinal);
            if (t.Id == "cpu" || !opened || !announced.Add(t.Id))
            {
                continue;
            }

            int colon = t.Id.IndexOf(':');
            stderr.WriteLine(multi ? $"GPU[{t.Id}]: {t.Name}"
                : colon >= 0 ? $"GPU: {t.Id[..colon]}:{t.Name}"
                : $"GPU: {t.Name}");
        }
    }

    /// <summary>
    /// Maps a completed session's outcome to the process exit code, writing the matching error
    /// line to <paramref name="stderr"/> when the run did not succeed. Two failure shapes are
    /// distinguished: no target ever opened (<see cref="SessionOutcome.AllTargetsFailed"/> — the
    /// pre-existing "no usable device" message), and every target opened but then died mid-run
    /// (outcome is <see cref="SessionOutcome.Cancelled"/> because it did open, yet every target's
    /// final <see cref="TargetState"/> is <see cref="TargetState.Failed"/> — reported as a GPU
    /// search failure when a non-CPU target is among the failures, or a plain search failure for
    /// a CPU-only run; either way distinguished from a user Ctrl+C).
    /// </summary>
    internal static int ExitCodeFor(SearchSummary summary, bool multi, TextWriter stderr)
    {
        if (summary.Outcome == SessionOutcome.AllTargetsFailed)
        {
            string failure = summary.Final.Targets.FirstOrDefault(t => t.Failure is not null)?.Failure ?? "no device could be opened";
            stderr.WriteLine(multi ? "error: --gpu-devices: no device could be opened." : $"error: --gpu requested but no usable device: {failure}");
            return 2;
        }

        if (summary.Outcome != SessionOutcome.CountReached && summary.Final.Targets.Count > 0
            && summary.Final.Targets.All(t => t.State == TargetState.Failed))
        {
            string failure = summary.Final.Targets.FirstOrDefault(t => t.Failure is not null)?.Failure ?? "device lost";
            // A CPU-only run never touched a GPU: the report/MatchFound callback can fail the
            // CPU target alone (e.g. a broken --out write), with no device involved at all.
            bool anyGpuFailed = summary.Final.Targets.Any(t => t.State == TargetState.Failed && t.Id != "cpu");
            stderr.WriteLine(anyGpuFailed ? $"error: GPU search failed: {failure}" : $"error: search failed: {failure}");
            return 2;
        }

        // 0 only when a --count target was fully satisfied; otherwise the run ended
        // via Ctrl+C (or a --count-less search the user stopped), reported as 130.
        return summary.Outcome == SessionOutcome.CountReached ? 0 : 130;
    }

    /// <summary>
    /// Writes the end-of-run figures. Every per-target line uses that target's own clock and rate
    /// (<see cref="TargetProgress.Seconds"/>, <see cref="TargetProgress.RatePerSecond"/>), so a
    /// device's context creation and program compile stay outside its time the way they did
    /// before the search moved behind <see cref="SearchSession"/>. Only the multi-device
    /// aggregate is measured on the session clock, because that is the wall time the devices
    /// overlapped in.
    /// </summary>
    internal static void RenderSummary(SearchSummary summary, bool multi, TextWriter stderr)
    {
        SearchProgress final = summary.Final;
        double sessionSeconds = Math.Max(final.Elapsed.TotalSeconds, 1e-9);
        TargetProgress? cpu = final.Targets.FirstOrDefault(t => t.Id == "cpu");
        List<TargetProgress> devices = final.Targets.Where(t => t.Id != "cpu").ToList();

        if (cpu is not null)
        {
            stderr.WriteLine($"CPU: searched {cpu.Candidates:N0} candidates in {Math.Max(cpu.Seconds, 1e-9):F1} s ({cpu.RatePerSecond:N0}/s).");
        }

        if (devices.Count == 0)
        {
            return;
        }

        if (!multi)
        {
            TargetProgress d = devices[0];
            if (d.State == TargetState.Failed && d.Candidates == 0)
            {
                return;   // the caller prints the error line
            }

            string tail = d.Failure is null || d.Candidates == 0 ? string.Empty : $" (stopped: {d.Failure})";
            stderr.WriteLine(
                $"GPU: searched {d.Candidates:N0} candidates in {Math.Max(d.Seconds, 1e-9):F1} s " +
                $"({d.RatePerSecond:N0}/s); consistency errors: {d.ConsistencyErrors}.{tail}");
            return;
        }

        int opened = 0;
        long total = 0;
        long errors = 0;
        foreach (TargetProgress d in devices)
        {
            errors += d.ConsistencyErrors;
            if (d.State == TargetState.Failed && d.Candidates == 0)
            {
                stderr.WriteLine($"GPU[{d.Id}]: failed: {d.Failure}");
                continue;
            }

            opened++;
            total += d.Candidates;
            string tail = d.Failure is null ? string.Empty : $" (stopped: {d.Failure})";
            stderr.WriteLine(
                $"GPU[{d.Id}]: searched {d.Candidates:N0} candidates in {Math.Max(d.Seconds, 1e-9):F1} s " +
                $"({d.RatePerSecond:N0}/s); consistency errors: {d.ConsistencyErrors}.{tail}");
        }

        if (opened > 0)
        {
            stderr.WriteLine(
                $"GPU: searched {total:N0} candidates on {opened} device(s) in {sessionSeconds:F1} s " +
                $"({total / sessionSeconds:N0}/s); consistency errors: {errors}.");
        }
    }

    private static int ListDevices(TextWriter stdout, TextWriter stderr)
    {
        int flat = 0;
        bool any = false;
        foreach (IGpuBackend backend in GpuBackends.All)
        {
            foreach (GpuDeviceInfo info in backend.EnumerateDevices())
            {
                string tag = info.IsCpuDevice ? " [cpu]" : string.Empty;
                stdout.WriteLine($"{flat,2}  {backend.Name}:{info.Index}  {info.Name}  ({info.Details}){tag}");
                flat++;
                any = true;
            }
        }

        if (!any)
        {
            stderr.WriteLine("no GPU devices found (no CUDA driver and no OpenCL platform).");
            return 2;
        }

        return 0;
    }
}
