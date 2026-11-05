using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using XrplVanity.Core;
using XrplVanity.Gpu;

namespace XrplVanity.Search;

/// <summary>
/// One search over one or more targets. <see cref="Start"/> spins one thread for the CPU target
/// and one <see cref="MultiDeviceSearch"/> run (which owns a thread per device) for the device
/// targets; a ticker publishes <see cref="Progress"/> every 250 ms; <see cref="Completion"/>
/// completes when every thread has joined. Events fire on worker threads.
/// </summary>
public sealed class SearchSession : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private readonly Action<Match> _report;
    private readonly Action<string> _log;
    private readonly MatchSink _sink;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<SearchSummary> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _elapsed = new();
    private readonly object _startGate = new();
    private readonly List<string> _targetOrder = new();   // ids in request order

    private readonly SearchTarget.Cpu? _cpuTarget;
    private readonly CpuSearchWorkers? _cpu;
    private readonly List<DeviceSelector> _deviceSelectors = new();
    private readonly MultiDeviceSearch? _devices;
    private readonly ManualResetEventSlim _tickerDone = new();

    /// <summary>
    /// Session-elapsed seconds at which each device was first seen opened, or a negative value
    /// while it has not opened yet. A running device reports no <c>Seconds</c> of its own (the
    /// worker sets that only when it finishes), so this is what makes the running-time-since-open
    /// — and with it a device-local rate — available on every tick. Written only from the ticker
    /// and coordinator threads, which never run <see cref="Snapshot"/> concurrently.
    /// </summary>
    private readonly double[] _deviceOpenedAt;

    private volatile bool _cpuOpening;
    private volatile bool _cpuFinished;
    private string? _cpuFailure;
    private volatile SearchProgress _progress;
    private bool _started;
    private bool _disposed;
    private string? _callbackFailure;
    private string? _deviceThreadFailure;
    private int _requestedDuty;

    public SearchSession(SearchRequest request, Action<Match> report, Action<string>? log = null)
        : this(request, report, log, GpuBackends.All)
    {
    }

    /// <summary>
    /// Takes the backend list as a parameter so tests can substitute a fake backend that fails
    /// deterministically, the same seam <see cref="MultiDeviceSearch"/> and
    /// <see cref="DeviceCatalog"/> expose. <paramref name="cpuWorkers"/> does the same for the
    /// CPU target, whose threads have no other way to fail on demand.
    /// </summary>
    internal SearchSession(
        SearchRequest request,
        Action<Match> report,
        Action<string>? log,
        IReadOnlyList<IGpuBackend> backends,
        Func<int, PatternSet, MatchSink, CpuSearchWorkers>? cpuWorkers = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(backends);
        Request = request;
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _log = log ?? (_ => { });
        _sink = new MatchSink(request.Count, OnAccepted);

        foreach (SearchTarget target in request.Targets)
        {
            switch (target)
            {
                case SearchTarget.Cpu cpu:
                    _cpuTarget = cpu;
                    _cpu = cpuWorkers?.Invoke(cpu.Threads, request.Patterns, _sink)
                        ?? new CpuSearchWorkers(cpu.Threads, request.Patterns, _sink);
                    _targetOrder.Add("cpu");
                    break;
                case SearchTarget.Device device:
                    _deviceSelectors.Add(device.Selector);
                    _targetOrder.Add(device.Selector.ToString());
                    break;
                case SearchTarget.FirstAvailableDevice:
                    _deviceSelectors.Add(DeviceSelector.FirstAvailable);
                    _targetOrder.Add(DeviceSelector.FirstAvailable.ToString());
                    break;
            }
        }

        if (_deviceSelectors.Count > 0)
        {
            _devices = new MultiDeviceSearch(backends, _deviceSelectors, request.Patterns, ReportFromDevice, FilterDeviceAnnouncement, request.DutyPercent);
        }

        _deviceOpenedAt = new double[_deviceSelectors.Count];
        Array.Fill(_deviceOpenedAt, -1.0);

        _requestedDuty = request.DutyPercent;
        _progress = Snapshot();
    }

    /// <summary>The request this session was built from.</summary>
    public SearchRequest Request { get; }

    public int DutyPercent
    {
        get => _devices?.DutyPercent ?? _requestedDuty;
        set
        {
            if (value < 1 || value > DutyCycle.Full)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duty cycle must be a whole number from 1 to 100.");
            }

            if (_devices is not null)
            {
                _devices.DutyPercent = value;
            }

            _requestedDuty = value;
        }
    }

    public SearchProgress Progress => _progress;

    /// <summary>
    /// Completes exactly once, when every worker thread has joined and the final snapshot is
    /// available — including when the session is disposed before it was ever started (outcome
    /// <see cref="SessionOutcome.Cancelled"/> with the initial snapshot), so a front end can
    /// always await it. It faults only if the coordinator itself failed before it could produce
    /// a summary.
    /// </summary>
    public Task<SearchSummary> Completion => _completion.Task;

    public event Action<SearchProgress>? ProgressChanged;

    /// <summary>
    /// Raised for every accepted match. Runs on a worker thread (the CPU pool's or a device's)
    /// inside the sink's lock, after the caller's <c>report</c> writer: it must not block, and it
    /// must marshal to a UI thread rather than touch UI state directly. A throwing subscriber
    /// stops the session instead of taking the process down.
    /// </summary>
    public event Action<Match>? MatchFound;

    /// <summary>The device search, exposed for tests that check duty propagation.</summary>
    internal MultiDeviceSearch? DeviceSearchForTests => _devices;

    public void Start()
    {
        lock (_startGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("The session has already been started.");
            }

            _started = true;
        }

        _elapsed.Start();
        Thread coordinator = new(Coordinate) { IsBackground = true, Name = "search-session" };
        coordinator.Start();
    }

    public void Stop()
    {
        if (!_stop.IsCancellationRequested)
        {
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Dispose raced with Stop; the session is already finished.
            }
        }
    }

    public void Dispose()
    {
        bool started;
        lock (_startGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            started = _started;
        }

        Stop();
        bool finished = !started || Completion.Wait(TimeSpan.FromSeconds(30));
        if (!started)
        {
            // No coordinator ever ran, so nothing else will ever complete the task; a consumer
            // that awaits Completion unconditionally must not hang on a session it disposed.
            _completion.TrySetResult(new SearchSummary(SessionOutcome.Cancelled, _progress));
        }

        if (finished)
        {
            _stop.Dispose();
            _tickerDone.Dispose();
        }
        else
        {
            // The worker threads are still running past the wait; disposing the primitives they
            // are blocked on would throw ObjectDisposedException on a background thread and take
            // the process down. Leaving them alive is a leak, not a crash.
            SafeLog("warning: session disposed before its threads finished; resources left alive");
        }
    }

    /// <summary>
    /// The coordinator thread's entry point. Everything it does is wrapped so that
    /// <see cref="Completion"/> is settled exactly once, whatever happens: a body that throws
    /// before it produced a summary faults the task instead of leaving every awaiter hanging
    /// forever.
    /// </summary>
    private void Coordinate()
    {
        SearchSummary? summary = null;
        Exception? fatal = null;
        try
        {
            summary = RunToCompletion();
        }
        catch (Exception ex)
        {
            fatal = ex;
            SafeLog($"warning: search session failed: {ex.Message}");
        }
        finally
        {
            if (summary is not null)
            {
                _completion.TrySetResult(summary);
            }
            else
            {
                _completion.TrySetException(fatal ?? new InvalidOperationException("The search session ended without a summary."));
            }
        }
    }

    private SearchSummary RunToCompletion()
    {
        Thread? cpuThread = null;
        Thread? deviceThread = null;
        MultiDeviceSearchResult? deviceResult = null;
        Thread ticker = new(Tick) { IsBackground = true, Name = "search-ticker" };

        try
        {
            if (_cpu is not null)
            {
                cpuThread = new Thread(RunCpu) { IsBackground = true, Name = "cpu-pool" };
                _cpuOpening = true;
                cpuThread.Start();
            }

            if (_devices is not null)
            {
                deviceThread = new Thread(() => deviceResult = RunDevices()) { IsBackground = true, Name = "device-pool" };
                deviceThread.Start();
            }

            ticker.Start();
            cpuThread?.Join();
            deviceThread?.Join();
        }
        finally
        {
            Stop();
            _tickerDone.Set();
            ticker.Join();
            _elapsed.Stop();
        }

        SearchProgress final = Snapshot(final: true);
        _progress = final;
        RaiseProgress(final);

        // A failed CPU counts as one that never opened: nobody cancelled a session that had
        // nothing left running.
        bool anyOpened = (_cpu is not null && _cpuFailure is null) || (deviceResult?.DevicesOpened ?? 0) > 0;
        SessionOutcome outcome = _sink.CountSatisfied ? SessionOutcome.CountReached
            : anyOpened ? SessionOutcome.Cancelled
            : SessionOutcome.AllTargetsFailed;
        return new SearchSummary(outcome, final);
    }

    /// <summary>
    /// <see cref="MultiDeviceSearch"/> nets exceptions per worker, so nothing normally escapes
    /// <see cref="MultiDeviceSearch.Run"/>; but a thread that cannot be started at all would,
    /// and an exception leaving this delegate would kill the device thread, its
    /// <see cref="Thread.Join"/> notwithstanding, and take the process down with it. Record it as
    /// the session's failure and let the coordinator finish normally.
    /// </summary>
    private MultiDeviceSearchResult? RunDevices()
    {
        try
        {
            return _devices!.Run(_stop.Token);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _deviceThreadFailure, $"device search failed: {ex.Message}");
            SafeLog($"warning: device search failed: {ex.Message}");
            Stop();
            return null;
        }
    }

    private void RunCpu()
    {
        try
        {
            _cpuOpening = false;
            _cpu!.Run(_stop);
        }
        catch (Exception ex)
        {
            _cpuFailure = ex.Message;
            SafeLog($"warning: cpu stopped: {ex.Message}");
        }
        finally
        {
            _cpuFinished = true;
        }
    }

    private void Tick()
    {
        while (!_tickerDone.Wait(TickInterval))
        {
            SearchProgress snapshot = Snapshot();
            _progress = snapshot;
            RaiseProgress(snapshot);
        }
    }

    private void RaiseProgress(SearchProgress snapshot)
    {
        try
        {
            ProgressChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            // A throwing subscriber must not kill the ticker or the coordinator; stop the search instead.
            SafeLog($"warning: progress subscriber failed: {ex.Message}");
            Stop();
        }
    }

    private bool ReportFromDevice(Match match)
    {
        bool more = _sink.TryReport(match);
        if (!more)
        {
            Stop();
        }

        return more;
    }

    /// <summary>
    /// Passed to <see cref="MultiDeviceSearch"/> in place of <c>_log</c> directly: drops the
    /// device-name announcement line (<c>"GPU[{selector}]: {name}"</c>) it emits right after
    /// opening a device, because the CLI announces devices itself from the progress snapshot.
    /// Every other line (OpenCL compile note, overflow/consistency warnings, "could not be
    /// opened" warnings) is forwarded to <c>_log</c> unchanged.
    /// </summary>
    private void FilterDeviceAnnouncement(string line)
    {
        foreach (DeviceSearchStats stats in _devices!.Devices)
        {
            if (string.Equals(line, $"GPU[{stats.Selector}]: {stats.DeviceName}", StringComparison.Ordinal))
            {
                return;
            }
        }

        _log(line);
    }

    /// <summary>
    /// Runs inside the sink's lock: the caller's writer first, then the event. Both are
    /// consumer-supplied and can throw; a throw here must not escape onto the reporting thread
    /// (the CPU worker thread, or the device thread), which would take the whole process down.
    /// The match stays counted by the sink either way — it was already accepted.
    /// </summary>
    private void OnAccepted(Match match)
    {
        try
        {
            _report(match);
            MatchFound?.Invoke(match);
        }
        catch (Exception ex)
        {
            string? failure = Volatile.Read(ref _callbackFailure);
            if (failure is null)
            {
                failure = $"report callback failed: {ex.Message}";
                Volatile.Write(ref _callbackFailure, failure);
            }

            SafeLog($"warning: {failure}");
            Stop();
        }
    }

    /// <summary>
    /// Builds a progress snapshot. When <paramref name="final"/> is true (only the coordinator's
    /// last snapshot, taken once every worker thread has joined) and a report/MatchFound callback
    /// or the device thread has thrown, every target that isn't already
    /// <see cref="TargetState.Failed"/> is forced to <see cref="TargetState.Failed"/> with that
    /// failure — intermediate ticks keep showing each target's own state, since a session-wide
    /// failure only reflects the outcome of the whole session once it has actually stopped.
    /// </summary>
    private SearchProgress Snapshot(bool final = false)
    {
        double seconds = _elapsed.Elapsed.TotalSeconds;
        List<TargetProgress> targets = new(_targetOrder.Count);
        long total = 0;
        double totalRate = 0;
        int deviceIndex = 0;
        string? sessionFailure = final
            ? Volatile.Read(ref _callbackFailure) ?? Volatile.Read(ref _deviceThreadFailure)
            : null;

        foreach (string id in _targetOrder)
        {
            string targetId;
            string name;
            TargetState state;
            long candidates;
            double targetSeconds;
            string? failure;
            long consistencyErrors;

            if (id == "cpu")
            {
                candidates = _cpu!.CandidatesSearched;
                targetSeconds = _cpu.Elapsed.TotalSeconds;
                consistencyErrors = 0;
                failure = _cpuFailure;
                state = failure is not null ? TargetState.Failed
                    : _cpuFinished ? TargetState.Finished
                    : _cpuOpening ? TargetState.Opening
                    : _started ? TargetState.Running
                    : TargetState.Pending;
                targetId = "cpu";
                name = $"CPU ({_cpuTarget!.Threads} threads)";
            }
            else
            {
                int index = deviceIndex++;
                DeviceSearchStats stats = _devices!.Devices[index];
                failure = stats.Failure;
                state = failure is not null ? TargetState.Failed
                    : stats.Seconds > 0 ? TargetState.Finished
                    : stats.Opened ? TargetState.Running
                    : _started ? TargetState.Opening
                    : TargetState.Pending;
                targetSeconds = DeviceSeconds(stats, index, seconds);
                candidates = stats.CandidatesSearched;
                consistencyErrors = stats.ConsistencyErrors;
                targetId = stats.Selector.ToString();
                name = stats.DeviceName ?? stats.Selector.ToString();
            }

            if (sessionFailure is not null && state != TargetState.Failed)
            {
                state = TargetState.Failed;
                failure = sessionFailure;
            }

            double rate = targetSeconds > 0 ? candidates / targetSeconds : 0;
            targets.Add(new TargetProgress(targetId, name, state, candidates, rate, failure, targetSeconds, consistencyErrors));
            total += candidates;
            totalRate += rate;
        }

        return new SearchProgress(_elapsed.Elapsed, total, totalRate, _sink.Reported, targets);
    }

    /// <summary>
    /// A device's own search time: the worker's measurement once it has finished, otherwise the
    /// time since this session first saw the device opened — the worker only publishes
    /// <see cref="DeviceSearchStats.Seconds"/> at the end, and the session clock would wrongly
    /// fold context creation and program compilation into the rate.
    /// </summary>
    private double DeviceSeconds(DeviceSearchStats stats, int index, double sessionSeconds)
    {
        if (stats.Seconds > 0)
        {
            return stats.Seconds;
        }

        if (!stats.Opened)
        {
            return 0;
        }

        if (_deviceOpenedAt[index] < 0)
        {
            _deviceOpenedAt[index] = sessionSeconds;
        }

        return Math.Max(sessionSeconds - _deviceOpenedAt[index], 0);
    }

    private void SafeLog(string message)
    {
        try
        {
            _log(message);
        }
        catch
        {
            // Logging must never throw a second exception while the session is unwinding.
        }
    }
}
