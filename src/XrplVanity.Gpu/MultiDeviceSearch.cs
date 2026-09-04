using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using XrplVanity.Core;
using XrplVanity.Gpu.OpenCl;

namespace XrplVanity.Gpu;

public sealed record DeviceSelector(string Backend, int Index)
{
    public override string ToString() => $"{Backend}:{Index}";
}

public sealed class DeviceSearchStats
{
    public DeviceSelector Selector { get; init; } = null!;
    public string? DeviceName { get; internal set; }
    public long CandidatesSearched { get; internal set; }
    public long ConsistencyErrors { get; internal set; }
    public long OverflowDrops { get; internal set; }
    public double Seconds { get; internal set; }
    public string? Failure { get; internal set; }
    public bool Opened => DeviceName is not null;
    public double RatePerSecond => Seconds > 0 ? CandidatesSearched / Seconds : 0;
}

public sealed class MultiDeviceSearchResult
{
    public IReadOnlyList<DeviceSearchStats> Devices { get; init; } = Array.Empty<DeviceSearchStats>();
    public int DevicesOpened { get; init; }
    public long TotalCandidates { get; init; }
    public long TotalConsistencyErrors { get; init; }
    public long TotalOverflowDrops { get; init; }
    public double Seconds { get; init; }
}

/// <summary>
/// Runs one <see cref="GpuSearchEngine"/> per selected device, each on its own thread (a device
/// is opened on the thread that uses it), and hands every CPU-verified match to
/// <c>report</c>. <c>report</c> is called concurrently from several threads and must be
/// thread-safe; returning <c>false</c> stops every worker. A device that fails to open or
/// throws mid-run is recorded in its <see cref="DeviceSearchStats.Failure"/> and does not stop
/// the others.
/// </summary>
public sealed class MultiDeviceSearch
{
    private readonly IReadOnlyList<IGpuBackend> _backends;
    private readonly IReadOnlyList<DeviceSelector> _devices;
    private readonly PatternSet _patterns;
    private readonly Func<Match, bool> _report;
    private readonly Action<string> _log;

    public MultiDeviceSearch(IReadOnlyList<DeviceSelector> devices, PatternSet patterns, Func<Match, bool> report, Action<string>? log = null)
        : this(GpuBackends.All, devices, patterns, report, log)
    {
    }

    internal MultiDeviceSearch(IReadOnlyList<IGpuBackend> backends, IReadOnlyList<DeviceSelector> devices, PatternSet patterns, Func<Match, bool> report, Action<string>? log = null)
    {
        if (devices.Count == 0)
        {
            throw new ArgumentException("At least one device selector is required.", nameof(devices));
        }

        _backends = backends;
        _devices = devices;
        _patterns = patterns;
        _report = report;
        _log = log ?? (_ => { });
    }

    public MultiDeviceSearchResult Run(CancellationToken cancellationToken)
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        DeviceSearchStats[] stats = new DeviceSearchStats[_devices.Count];
        Thread[] workers = new Thread[_devices.Count];
        Stopwatch total = Stopwatch.StartNew();

        for (int i = 0; i < _devices.Count; i++)
        {
            DeviceSearchStats s = new() { Selector = _devices[i] };
            stats[i] = s;
            workers[i] = new Thread(() => Worker(s, stop)) { IsBackground = true, Name = $"gpu-{s.Selector}" };
            workers[i].Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        total.Stop();
        int opened = 0;
        long candidates = 0, errors = 0, drops = 0;
        foreach (DeviceSearchStats s in stats)
        {
            if (s.Opened) opened++;
            candidates += s.CandidatesSearched;
            errors += s.ConsistencyErrors;
            drops += s.OverflowDrops;
        }

        return new MultiDeviceSearchResult
        {
            Devices = stats,
            DevicesOpened = opened,
            TotalCandidates = candidates,
            TotalConsistencyErrors = errors,
            TotalOverflowDrops = drops,
            Seconds = total.Elapsed.TotalSeconds,
        };
    }

    private void Worker(DeviceSearchStats stats, CancellationTokenSource stop)
    {
        Stopwatch open = Stopwatch.StartNew();
        if (!GpuBackends.TryOpen(_backends, stats.Selector.Backend, stats.Selector.Index, out IGpuDevice? device, out string reason))
        {
            stats.Failure = reason;
            _log($"warning: {stats.Selector} could not be opened: {reason}");
            return;
        }

        open.Stop();
        using (device)
        {
            stats.DeviceName = device!.Name;
            _log($"GPU[{stats.Selector}]: {device.Name}");
            if (string.Equals(device.Backend, "opencl", StringComparison.OrdinalIgnoreCase) && open.Elapsed.TotalSeconds > 1.0)
            {
                _log($"note: compiling the OpenCL program for {stats.Selector} took {open.Elapsed.TotalSeconds:F1} s");
            }

            Stopwatch run = Stopwatch.StartNew();
            try
            {
                using GpuSearchEngine engine = new(device, _patterns);
                long reportedOverflow = 0;
                long reportedErrors = 0;
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        foreach (Match match in engine.RunLaunch())
                        {
                            if (!_report(match))
                            {
                                stop.Cancel();
                                break;
                            }
                        }

                        if (engine.OverflowDrops > reportedOverflow)
                        {
                            _log($"warning: {stats.Selector}: {engine.OverflowDrops - reportedOverflow:N0} hits dropped this launch " +
                                 $"(hit buffer holds {GpuSearchEngine.HitCapacity}); use a more selective pattern.");
                            reportedOverflow = engine.OverflowDrops;
                        }

                        if (engine.ConsistencyErrors > reportedErrors)
                        {
                            _log($"GPU consistency error on {stats.Selector}: {engine.ConsistencyErrors - reportedErrors} device hit(s) " +
                                 "the CPU reference could not reproduce were discarded.");
                            reportedErrors = engine.ConsistencyErrors;
                        }
                    }
                }
                finally
                {
                    stats.CandidatesSearched = engine.CandidatesSearched;
                    stats.ConsistencyErrors = engine.ConsistencyErrors;
                    stats.OverflowDrops = engine.OverflowDrops;
                }
            }
            catch (Exception ex) when (ex is CudaException or OpenClException or ArgumentException or InvalidOperationException)
            {
                stats.Failure = ex.Message;
                _log($"warning: {stats.Selector} stopped: {ex.Message}");
            }
            finally
            {
                stats.Seconds = run.Elapsed.TotalSeconds;
            }
        }
    }
}
