using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using XrplVanity.Core;
using XrplVanity.Gpu.OpenCl;

namespace XrplVanity.Gpu;

public sealed record DeviceSelector(string Backend, int Index)
{
    /// <summary>
    /// "The first usable device in backend preference order" (the CLI's <c>--gpu</c>). Resolved
    /// by <see cref="MultiDeviceSearch"/> when the worker opens the device; the stats entry then
    /// carries the concrete selector.
    /// </summary>
    public static DeviceSelector FirstAvailable { get; } = new("gpu", -1);

    public bool IsFirstAvailable => Index < 0;

    public override string ToString() => IsFirstAvailable ? "gpu" : $"{Backend}:{Index}";
}

public sealed class DeviceSearchStats
{
    public DeviceSelector Selector { get; internal set; } = null!;
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
/// thread-safe; returning <c>false</c> stops every worker. A device that fails to open or throws
/// mid-run is recorded in its <see cref="DeviceSearchStats.Failure"/> and does not stop the
/// others. A worker never lets an exception escape its thread: an exception from the device or
/// engine is recorded locally and only that worker stops, while an exception from <c>report</c>
/// or <c>log</c> means the caller's sink is unusable, so every worker is asked to stop.
/// </summary>
public sealed class MultiDeviceSearch
{
    private readonly IReadOnlyList<IGpuBackend> _backends;
    private readonly IReadOnlyList<DeviceSelector> _devices;
    private readonly PatternSet _patterns;
    private readonly Func<Match, bool> _report;
    private readonly Action<string> _log;
    private int _dutyPercent;

    public MultiDeviceSearch(IReadOnlyList<DeviceSelector> devices, PatternSet patterns, Func<Match, bool> report, Action<string>? log = null, int dutyPercent = DutyCycle.Full)
        : this(GpuBackends.All, devices, patterns, report, log, dutyPercent)
    {
    }

    internal MultiDeviceSearch(IReadOnlyList<IGpuBackend> backends, IReadOnlyList<DeviceSelector> devices, PatternSet patterns, Func<Match, bool> report, Action<string>? log = null, int dutyPercent = DutyCycle.Full)
    {
        if (dutyPercent < 1 || dutyPercent > DutyCycle.Full)
        {
            throw new ArgumentOutOfRangeException(nameof(dutyPercent), dutyPercent, "The duty cycle must be a whole number from 1 to 100.");
        }

        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(report);
        if (devices.Count == 0)
        {
            throw new ArgumentException("At least one device selector is required.", nameof(devices));
        }

        _backends = backends;
        _devices = devices;
        _patterns = patterns;
        _report = report;
        _log = log ?? (_ => { });
        _dutyPercent = dutyPercent;

        DeviceSearchStats[] devicesStats = new DeviceSearchStats[_devices.Count];
        for (int i = 0; i < _devices.Count; i++)
        {
            devicesStats[i] = new DeviceSearchStats { Selector = _devices[i] };
        }

        Devices = devicesStats;
    }

    /// <summary>
    /// One <see cref="DeviceSearchStats"/> per device selector, in the order they were passed to
    /// the constructor, created up front and mutated live by the workers as they run — read them
    /// (without locking; a torn read of a <c>long</c> is not a concern on x64) at any time,
    /// including while <see cref="Run"/> is still in progress. <see cref="Run"/> returns these
    /// same instances in <see cref="MultiDeviceSearchResult.Devices"/>.
    /// </summary>
    public IReadOnlyList<DeviceSearchStats> Devices { get; }

    /// <summary>
    /// Duty cycle applied by every worker at its next pause (1–100). Set from any thread while
    /// <see cref="Run"/> is in progress.
    /// </summary>
    public int DutyPercent
    {
        get => Volatile.Read(ref _dutyPercent);
        set
        {
            if (value < 1 || value > DutyCycle.Full)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duty cycle must be a whole number from 1 to 100.");
            }

            Volatile.Write(ref _dutyPercent, value);
        }
    }

    public MultiDeviceSearchResult Run(CancellationToken cancellationToken)
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Thread?[] workers = new Thread?[Devices.Count];
        Stopwatch total = Stopwatch.StartNew();

        try
        {
            for (int i = 0; i < Devices.Count; i++)
            {
                DeviceSearchStats s = Devices[i];
                Thread worker = new(() => Worker(s, stop)) { IsBackground = true, Name = $"gpu-{s.Selector}" };
                workers[i] = worker;
                worker.Start();
            }

            JoinAll(workers);
        }
        finally
        {
            // If Thread.Start() threw partway through the loop above, the already-started
            // workers are still running and reference `stop`, which the `using` above is about
            // to dispose. Cancel first so they observe it and exit, then join whichever workers
            // actually got started (the rest are still null) before letting `using` dispose.
            stop.Cancel();
            JoinAll(workers);
        }

        total.Stop();
        int opened = 0;
        long candidates = 0, errors = 0, drops = 0;
        foreach (DeviceSearchStats s in Devices)
        {
            if (s.Opened) opened++;
            candidates += s.CandidatesSearched;
            errors += s.ConsistencyErrors;
            drops += s.OverflowDrops;
        }

        return new MultiDeviceSearchResult
        {
            Devices = Devices,
            DevicesOpened = opened,
            TotalCandidates = candidates,
            TotalConsistencyErrors = errors,
            TotalOverflowDrops = drops,
            Seconds = total.Elapsed.TotalSeconds,
        };
    }

    private static void JoinAll(Thread?[] workers)
    {
        foreach (Thread? worker in workers)
        {
            worker?.Join();
        }
    }

    private void Worker(DeviceSearchStats stats, CancellationTokenSource stop)
    {
        try
        {
            RunDevice(stats, stop);
        }
        catch (Exception ex)
        {
            // Last-resort safety net: whatever threw here (device/engine failure not already
            // handled below, or anything else unexpected) must never escape this thread — an
            // unhandled exception on a Thread delegate crashes the whole process.
            stats.Failure = ex.Message;
            SafeLog($"warning: {stats.Selector} stopped: {ex.Message}");
        }
    }

    private bool TryOpenFirstAvailable(DeviceSearchStats stats, out IGpuDevice? device, out string reason)
    {
        if (!GpuBackends.TryOpenFirst(_backends, out device, out DeviceSelector? resolved, out reason))
        {
            return false;
        }

        stats.Selector = resolved!;
        return true;
    }

    private void RunDevice(DeviceSearchStats stats, CancellationTokenSource stop)
    {
        Stopwatch open = Stopwatch.StartNew();
        IGpuDevice? device;
        string reason;
        bool opened = stats.Selector.IsFirstAvailable
            ? TryOpenFirstAvailable(stats, out device, out reason)
            : GpuBackends.TryOpen(_backends, stats.Selector.Backend, stats.Selector.Index, out device, out reason);
        if (!opened)
        {
            stats.Failure = reason;
            TryLog(stats, stop, $"warning: {stats.Selector} could not be opened: {reason}");
            return;
        }

        open.Stop();
        using (device)
        {
            stats.DeviceName = device!.Name;
            TryLog(stats, stop, $"GPU[{stats.Selector}]: {device.Name}");
            if (string.Equals(device.Backend, "opencl", StringComparison.OrdinalIgnoreCase) && open.Elapsed.TotalSeconds > 1.0)
            {
                TryLog(stats, stop, $"note: compiling the OpenCL program for {stats.Selector} took {open.Elapsed.TotalSeconds:F1} s");
            }

            Stopwatch run = Stopwatch.StartNew();
            try
            {
                using GpuSearchEngine engine = new(device, _patterns);
                long reportedOverflow = 0;
                long reportedErrors = 0;
                while (!stop.IsCancellationRequested)
                {
                    Stopwatch launch = Stopwatch.StartNew();
                    foreach (Match match in engine.RunLaunch())
                    {
                        if (!TryReport(stats, match))
                        {
                            // Covers both a legitimate "stop" from report (e.g. a match-count
                            // limit reached) and a caught exception from report (handled inside
                            // TryReport) — either way every worker must stop.
                            stop.Cancel();
                            break;
                        }
                    }

                    // Update live so callers reading Devices while Run is still in progress
                    // (e.g. to gate on "has this device found anything yet") see current counts.
                    stats.CandidatesSearched = engine.CandidatesSearched;
                    stats.ConsistencyErrors = engine.ConsistencyErrors;
                    stats.OverflowDrops = engine.OverflowDrops;

                    if (engine.OverflowDrops > reportedOverflow)
                    {
                        TryLog(stats, stop, $"warning: {stats.Selector}: {engine.OverflowDrops - reportedOverflow:N0} hits dropped this launch " +
                             $"(hit buffer holds {GpuSearchEngine.HitCapacity}); use a more selective pattern.");
                        reportedOverflow = engine.OverflowDrops;
                    }

                    if (engine.ConsistencyErrors > reportedErrors)
                    {
                        TryLog(stats, stop, $"GPU consistency error on {stats.Selector}: {engine.ConsistencyErrors - reportedErrors} device hit(s) " +
                             "the CPU reference could not reproduce were discarded.");
                        reportedErrors = engine.ConsistencyErrors;
                    }

                    DutyCycle.Pause(launch.Elapsed, DutyPercent, stop.Token);
                }
            }
            finally
            {
                stats.Seconds = run.Elapsed.TotalSeconds;
            }
        }
    }

    /// <summary>
    /// Calls <c>_report</c>, catching anything it throws. A throwing report callback means the
    /// caller's sink is unusable, so it is treated the same as a legitimate "stop" (a
    /// <c>false</c> return): the caller cancels <paramref name="stop"/> either way, unlike a
    /// device/engine exception, which stays local to this worker.
    /// </summary>
    private bool TryReport(DeviceSearchStats stats, Match match)
    {
        try
        {
            return _report(match);
        }
        catch (Exception ex)
        {
            stats.Failure ??= $"report callback failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Calls <c>_log</c>, catching anything it throws: same reasoning as <see cref="TryReport"/>
    /// — a throwing log callback means the sink is unusable for every worker, not just this one.
    /// </summary>
    private void TryLog(DeviceSearchStats stats, CancellationTokenSource stop, string message)
    {
        try
        {
            _log(message);
        }
        catch (Exception ex)
        {
            stats.Failure ??= $"log callback failed: {ex.Message}";
            stop.Cancel();
        }
    }

    /// <summary>Best-effort log used only by the worker's outermost safety net, where the worker
    /// is already unwinding due to a failure — logging must never throw a second exception.</summary>
    private void SafeLog(string message)
    {
        try
        {
            _log(message);
        }
        catch
        {
            // The worker is already recording a failure and exiting; swallow.
        }
    }
}
