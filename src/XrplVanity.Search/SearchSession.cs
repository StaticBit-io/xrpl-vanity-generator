using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    private readonly SearchRequest _request;
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

    private volatile bool _cpuOpening;
    private volatile bool _cpuFinished;
    private string? _cpuFailure;
    private volatile SearchProgress _progress;
    private bool _started;
    private bool _disposed;

    public SearchSession(SearchRequest request, Action<Match> report, Action<string>? log = null)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _log = log ?? (_ => { });
        _sink = new MatchSink(request.Count, OnAccepted);

        foreach (SearchTarget target in request.Targets)
        {
            switch (target)
            {
                case SearchTarget.Cpu cpu:
                    _cpuTarget = cpu;
                    _cpu = new CpuSearchWorkers(cpu.Threads, request.Patterns, _sink);
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
            _devices = new MultiDeviceSearch(_deviceSelectors, request.Patterns, ReportFromDevice, FilterDeviceAnnouncement, request.DutyPercent);
        }

        _requestedDuty = request.DutyPercent;
        _progress = Snapshot();
    }

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

    private int _requestedDuty;

    public SearchProgress Progress => _progress;
    public Task<SearchSummary> Completion => _completion.Task;

    public event Action<SearchProgress>? ProgressChanged;
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
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        if (_started)
        {
            Completion.Wait(TimeSpan.FromSeconds(30));
        }

        _stop.Dispose();
        _tickerDone.Dispose();
    }

    private void Coordinate()
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
                deviceThread = new Thread(() => deviceResult = _devices.Run(_stop.Token)) { IsBackground = true, Name = "device-pool" };
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

        SearchProgress final = Snapshot();
        _progress = final;
        RaiseProgress(final);

        bool anyOpened = _cpu is not null || (deviceResult?.DevicesOpened ?? 0) > 0;
        SessionOutcome outcome = _sink.CountSatisfied ? SessionOutcome.CountReached
            : anyOpened ? SessionOutcome.Cancelled
            : SessionOutcome.AllTargetsFailed;
        _completion.TrySetResult(new SearchSummary(outcome, final));
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

    /// <summary>Runs inside the sink's lock: the caller's writer first, then the event.</summary>
    private void OnAccepted(Match match)
    {
        _report(match);
        MatchFound?.Invoke(match);
    }

    private SearchProgress Snapshot()
    {
        double seconds = _elapsed.Elapsed.TotalSeconds;
        List<TargetProgress> targets = new(_targetOrder.Count);
        long total = 0;
        double totalRate = 0;
        int deviceIndex = 0;

        foreach (string id in _targetOrder)
        {
            if (id == "cpu")
            {
                long candidates = _cpu!.CandidatesSearched;
                double cpuSeconds = _cpu.Elapsed.TotalSeconds;
                double rate = cpuSeconds > 0 ? candidates / cpuSeconds : 0;
                TargetState state = _cpuFailure is not null ? TargetState.Failed
                    : _cpuFinished ? TargetState.Finished
                    : _cpuOpening ? TargetState.Opening
                    : _started ? TargetState.Running
                    : TargetState.Pending;
                targets.Add(new TargetProgress("cpu", $"CPU ({_cpuTarget!.Threads} threads)", state, candidates, rate, _cpuFailure));
                total += candidates;
                totalRate += rate;
            }
            else
            {
                DeviceSearchStats stats = _devices!.Devices[deviceIndex++];
                TargetState state = stats.Failure is not null ? TargetState.Failed
                    : stats.Seconds > 0 ? TargetState.Finished
                    : stats.Opened ? TargetState.Running
                    : _started ? TargetState.Opening
                    : TargetState.Pending;
                double rate = stats.Opened && stats.Seconds > 0 ? stats.RatePerSecond
                    : stats.Opened && seconds > 0 ? stats.CandidatesSearched / seconds
                    : 0;
                targets.Add(new TargetProgress(stats.Selector.ToString(), stats.DeviceName ?? stats.Selector.ToString(), state,
                    stats.CandidatesSearched, rate, stats.Failure));
                total += stats.CandidatesSearched;
                totalRate += rate;
            }
        }

        return new SearchProgress(_elapsed.Elapsed, total, totalRate, _sink.Reported, targets);
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
