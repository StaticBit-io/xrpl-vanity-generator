using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using XrplVanity.Core;
using XrplVanity.Gpu;
using XrplVanity.Search;

namespace XrplVanity.App.Core.ViewModels;

/// <summary>
/// The application's single source of UI truth: the pattern and target drafts, the running
/// session and its progress, the session results, and the estimate. Session events arrive on
/// worker threads; every mutation here happens under <c>_gate</c> and ends with <see cref="Changed"/>,
/// which components subscribe to and marshal through <c>InvokeAsync(StateHasChanged)</c>.
/// </summary>
public sealed class SearchViewModel : IDisposable
{
    private static readonly TimeSpan LiveRateAfter = TimeSpan.FromSeconds(2);

    private readonly ISettingsStore _settingsStore;
    private readonly IResultStore _resultStore;
    private readonly INotificationService _notifications;
    private readonly Func<IReadOnlyList<DeviceCatalogEntry>> _deviceSource;
    private readonly object _gate = new();
    private readonly List<PatternChip> _patterns = new();
    private readonly List<TargetChoice> _targets = new();
    private readonly List<SessionResult> _results = new();
    private readonly List<StoredResult> _pendingPersist = new();
    private IReadOnlyList<SessionResult> _resultsSnapshot = Array.Empty<SessionResult>();
    private bool _resultsDirty;
    private SessionResult? _lastMatch;
    private string? _historyWriteError;
    private int _unnotifiedMatches;
    private AppSettings _settings;
    private PatternSet? _patternSet;
    private SearchSession? _session;
    private SearchProgress? _progress;
    private int _dutyPercent;
    private int _cpuThreads;
    private int? _count;

    public SearchViewModel(ISettingsStore settingsStore, IResultStore resultStore, INotificationService notifications, Func<IReadOnlyList<DeviceCatalogEntry>> deviceSource)
    {
        _settingsStore = settingsStore;
        _resultStore = resultStore;
        _notifications = notifications;
        _deviceSource = deviceSource;
        _settings = settingsStore.Load();
        // Settings are loaded from disk (or DPAPI storage) and can carry stale or corrupted
        // values from an older build; clamp/normalize here so a bad value never reaches the
        // throwing constructors in Start() (SearchRequest, SearchTarget.Cpu).
        _dutyPercent = Math.Clamp(_settings.DutyPercent, 1, DutyCycle.Full);
        _cpuThreads = Math.Clamp(_settings.CpuThreads, 1, Math.Max(1, Environment.ProcessorCount));
        _count = _settings.Count is int loadedCount && loadedCount < 1 ? null : _settings.Count;
        foreach (PatternDraft draft in _settings.Patterns)
        {
            if (Enum.TryParse(draft.Position, out MatchPosition position))
            {
                _patterns.Add(new PatternChip(draft.Text, position, draft.IgnoreCase));
            }
        }

        RefreshDevices();
        RebuildPatternSet();
    }

    public event Action? Changed;

    public SearchState State { get; private set; } = SearchState.Idle;

    /// <summary>A snapshot taken under <c>_gate</c>: safe to enumerate while a search is running on another thread.</summary>
    public IReadOnlyList<PatternChip> Patterns { get { lock (_gate) return _patterns.ToArray(); } }

    /// <summary>A snapshot taken under <c>_gate</c>: safe to enumerate while a search is running on another thread.</summary>
    public IReadOnlyList<TargetChoice> Targets { get { lock (_gate) return _targets.ToArray(); } }

    /// <summary>
    /// A cached snapshot rebuilt lazily on read, under <c>_gate</c>, only when the underlying
    /// result list actually changed since the last read (<see cref="Start"/> clearing it,
    /// <see cref="OnMatch"/> inserting into it just flag <see cref="_resultsDirty"/> instead of
    /// rebuilding) — the same instance is returned between changes, so a consumer that keys state
    /// off a "did the list change" reference comparison (e.g. <c>ResultsList</c>) does not see a
    /// false positive on every unrelated re-render (a progress tick, for instance). Deferring the
    /// O(n) copy out of <see cref="OnMatch"/> keeps that lock — shared by every CPU worker and GPU
    /// engine — held for O(1) work per match.
    /// </summary>
    public IReadOnlyList<SessionResult> Results
    {
        get
        {
            lock (_gate)
            {
                if (_resultsDirty)
                {
                    _resultsSnapshot = _results.ToArray();
                    _resultsDirty = false;
                }

                return _resultsSnapshot;
            }
        }
    }

    public SearchProgress? Progress => _progress;
    public SearchSummary? LastSummary { get; private set; }
    public string? LastFailure { get; private set; }
    public bool NoGpuFound { get; private set; }

    public int CpuThreads
    {
        get => _cpuThreads;
        set
        {
            int clamped = Math.Clamp(value, 1, Math.Max(1, Environment.ProcessorCount));
            bool changed;
            lock (_gate)
            {
                changed = clamped != _cpuThreads;
                _cpuThreads = clamped;
            }

            if (!changed) return;
            PersistDraft();
            Changed?.Invoke();
        }
    }

    /// <summary>Any value below 1 means unbounded (searches until stopped).</summary>
    public int? Count
    {
        get => _count;
        set
        {
            int? normalized = value is int v && v < 1 ? null : value;
            bool changed;
            lock (_gate)
            {
                changed = normalized != _count;
                _count = normalized;
            }

            if (!changed) return;
            PersistDraft();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Non-null when nothing new can reach <c>results.dat</c>: either the store declared itself
    /// read-only, or a batch flush failed mid-search. Rendered as a banner on History and on the
    /// Search page, because a persist failure during a run means the seeds are gone on restart.
    /// </summary>
    public string? HistoryReadOnlyReason => _resultStore.IsReadOnly
        ? _resultStore.ReadOnlyReason ?? "history is read-only"
        : _historyWriteError;
    public AppSettings Settings => _settings;

    public int DutyPercent
    {
        get => _dutyPercent;
        set
        {
            int clamped = Math.Clamp(value, 1, DutyCycle.Full);
            lock (_gate)
            {
                _dutyPercent = clamped;
                if (_session is not null)
                {
                    _session.DutyPercent = clamped;
                }
            }

            SaveSettings(s => s with { DutyPercent = clamped });
            Changed?.Invoke();
        }
    }

    public double? ExpectedCandidates => _patternSet is null ? null : SearchEstimator.ExpectedCandidatesPerMatch(_patternSet);

    /// <summary>ETA from the live rate after 2 s of searching, else from the rate hints of the selected targets, else null.</summary>
    public TimeSpan? EstimatedTime => SearchEstimator.ExpectedTimeToMatch(ExpectedCandidates, CurrentRate());

    public string EstimateNote
    {
        get
        {
            if (ExpectedCandidates is null) return "add a pattern to see an estimate";
            double rate = CurrentRate();
            if (rate <= 0) return "rate unknown until the first run";
            return _progress is not null && _progress.Elapsed >= LiveRateAfter && State == SearchState.Searching
                ? "from the live rate"
                : "from the last measured rate of the selected targets";
        }
    }

    public bool CanStart => State == SearchState.Idle && StartBlockedReason is null;

    public string? StartBlockedReason
    {
        get
        {
            lock (_gate)
            {
                if (_patternSet is null || !_patternSet.HasPatterns) return "Add at least one reachable pattern.";
                if (!_targets.Any(t => t.Selected)) return "Select the CPU or at least one device.";
                return null;
            }
        }
    }

    public void AddPattern(string text, MatchPosition position, bool ignoreCase)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return;
        lock (_gate)
        {
            _patterns.Add(new PatternChip(trimmed, position, ignoreCase));
            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void RemovePattern(PatternChip chip)
    {
        lock (_gate)
        {
            _patterns.Remove(chip);
            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void UpdatePattern(PatternChip chip, MatchPosition position, bool ignoreCase)
    {
        lock (_gate)
        {
            chip.Position = position;
            chip.IgnoreCase = ignoreCase;
            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    /// <summary>Adds every non-blank line of a word list as a Starts chip.</summary>
    public void AddPatternsFromList(IEnumerable<string> lines, bool ignoreCase)
    {
        lock (_gate)
        {
            foreach (string line in lines)
            {
                string word = line.Trim();
                if (word.Length > 0) _patterns.Add(new PatternChip(word, MatchPosition.Starts, ignoreCase));
            }

            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void SetTargetSelected(string id, bool selected)
    {
        lock (_gate)
        {
            TargetChoice? target = _targets.FirstOrDefault(t => t.Id == id);
            if (target is null) return;
            target.Selected = selected;
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void RefreshDevices()
    {
        IReadOnlyList<DeviceCatalogEntry> entries;
        try
        {
            entries = _deviceSource();
        }
        catch (Exception)
        {
            entries = Array.Empty<DeviceCatalogEntry>();
        }

        lock (_gate)
        {
            // A target is selected only if the saved draft or the current draft selected it;
            // nothing is auto-selected on a fresh install except the CPU.
            HashSet<string> selected = new(_targets.Count == 0 ? _settings.Targets : _targets.Where(t => t.Selected).Select(t => t.Id), StringComparer.Ordinal);
            _targets.Clear();
            _targets.Add(new TargetChoice("cpu", "CPU", $"{Environment.ProcessorCount} logical processors", false, selected.Contains("cpu")));
            foreach (DeviceCatalogEntry e in entries)
            {
                _targets.Add(new TargetChoice(e.Id, e.Name, $"{e.Selector.Backend} · {e.Details}", e.IsCpuDevice, selected.Contains(e.Id)));
            }

            NoGpuFound = !entries.Any(e => !e.IsCpuDevice);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Builds the request and starts the session while holding <c>_gate</c>. Nothing here should
    /// throw once loaded settings are normalized (see the constructor) and <see cref="CpuThreads"/>/
    /// <see cref="Count"/> stay clamped by their own setters, but the construction path still calls
    /// into <c>XrplVanity.Search</c> types with their own throwing constructors — a failure there
    /// must not leave the view model wedged in <see cref="SearchState.Starting"/>, so it is caught
    /// and reported through <see cref="LastFailure"/> instead of escaping <see cref="Start"/>.
    /// </summary>
    public void Start()
    {
        SearchSession? session = null;
        try
        {
            lock (_gate)
            {
                if (!CanStart) return;
                List<SearchTarget> targets = new();
                foreach (TargetChoice t in _targets.Where(t => t.Selected))
                {
                    targets.Add(t.IsCpu
                        ? new SearchTarget.Cpu(_cpuThreads)
                        : new SearchTarget.Device(ParseSelector(t.Id)));
                }

                SearchRequest request = new(_patternSet!, targets, _count, _dutyPercent);
                _results.Clear();
                _resultsSnapshot = Array.Empty<SessionResult>();
                _resultsDirty = false;
                _pendingPersist.Clear();
                _unnotifiedMatches = 0;
                _lastMatch = null;
                _historyWriteError = null;
                LastSummary = null;
                LastFailure = null;
                _progress = null;
                State = SearchState.Starting;
                session = new SearchSession(request, OnMatch, OnLog);
                _session = session;
                session.ProgressChanged += OnProgress;
                session.Completion.ContinueWith(OnSessionCompleted);
                session.Start();
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                LastFailure = ex.Message;
                State = SearchState.Idle;
                _session = null;
            }

            session?.Dispose();
            Changed?.Invoke();
            return;
        }

        PersistDraft();
        Changed?.Invoke();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_session is null || State is SearchState.Idle or SearchState.Stopping) return;
            State = SearchState.Stopping;
            _session.Stop();
        }

        Changed?.Invoke();
    }

    public void SaveSettings(Func<AppSettings, AppSettings> update)
    {
        lock (_gate)
        {
            _settings = update(_settings);
            try
            {
                // ISettingsStore.Save is contracted not to throw, but this runs from Blazor event
                // handlers and from the session-completion continuation, where an escaping
                // exception is either unhandled or unobserved — so the contract is enforced here too.
                _settingsStore.Save(_settings);
            }
            catch (Exception)
            {
                // The in-memory settings stay authoritative; the next successful save persists them.
            }
        }
    }

    public void Dispose()
    {
        SearchSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        session?.Dispose();
    }

    private static DeviceSelector ParseSelector(string id)
    {
        int colon = id.IndexOf(':');
        if (colon < 0)
        {
            throw new ArgumentException($"'{id}' is not a device id", nameof(id));
        }

        return new DeviceSelector(id[..colon], int.Parse(id[(colon + 1)..]));
    }

    private double CurrentRate()
    {
        SearchProgress? progress = _progress;
        if (progress is not null && State == SearchState.Searching && progress.Elapsed >= LiveRateAfter && progress.TotalRatePerSecond > 0)
        {
            return progress.TotalRatePerSecond;
        }

        lock (_gate)
        {
            double sum = 0;
            foreach (TargetChoice t in _targets.Where(t => t.Selected))
            {
                if (_settings.RateHints.TryGetValue(t.Id, out double hint)) sum += hint;
            }

            return sum;
        }
    }

    private void RebuildPatternSet()
    {
        PatternSet.Builder builder = new();
        foreach (PatternChip chip in _patterns)
        {
            builder.Add(chip.Position, chip.Text, chip.IgnoreCase);
        }

        PatternSet set = builder.Build();
        foreach (PatternChip chip in _patterns)
        {
            NormalizedPattern normalized = PatternNormalizer.Normalize(chip.Text);
            chip.Warning = !normalized.Reachable ? "cannot occur in an XRPL address"
                : normalized.Value.Length == 0 ? "empty pattern"
                : chip.IgnoreCase && normalized.Value.Length > PatternSet.MaxIgnoreCaseLength ? $"longer than {PatternSet.MaxIgnoreCaseLength} characters: searched case-sensitively"
                : null;
        }

        _patternSet = set;
    }

    private void PersistDraft()
    {
        // _gate is re-entrant: SaveSettings takes it again to write _settings, which is fine on
        // the same thread and keeps the read of _patterns/_targets and the write of _settings
        // as one atomic step with respect to a concurrent RefreshDevices/SetTargetSelected call.
        lock (_gate)
        {
            SaveSettings(s => s with
            {
                Patterns = _patterns.Select(p => new PatternDraft(p.Text, p.Position.ToString(), p.IgnoreCase)).ToArray(),
                Targets = _targets.Where(t => t.Selected).Select(t => t.Id).ToArray(),
                CpuThreads = _cpuThreads,
                Count = _count,
            });
        }
    }

    private void OnProgress(SearchProgress progress)
    {
        lock (_gate)
        {
            _progress = progress;
            if (State == SearchState.Starting && progress.Targets.Any(t => t.State == TargetState.Running))
            {
                State = SearchState.Searching;
            }
        }

        FlushPendingMatches();
        Changed?.Invoke();
    }

    /// <summary>
    /// Runs inside <c>MatchSink</c>'s lock, shared by every CPU worker and GPU engine: a short
    /// pattern reaches five figures of hits per second here, so this does the cheap part only —
    /// the in-memory list and the pending batch — and raises no event. Persisting, notifying and
    /// <see cref="Changed"/> happen in <see cref="FlushPendingMatches"/> on the progress tick.
    /// </summary>
    private void OnMatch(Match match)
    {
        SessionResult result = new(match.Address, match.Seed, match.Pattern, match.Position.ToString(), DateTimeOffset.Now, ResolveMatchTarget());
        lock (_gate)
        {
            _results.Insert(0, result);
            _resultsDirty = true;
            _pendingPersist.Add(new StoredResult(result.Address, result.Seed, result.Pattern, result.Position, result.FoundAt, result.Target));
            _unnotifiedMatches++;
            _lastMatch = result;
        }
    }

    /// <summary>
    /// Plan F's <c>Match</c> carries no source target, so the only honest attribution is the one
    /// running target when there is exactly one; with the CPU and a GPU both running, any guess
    /// would be wrong most of the time, so the result records "?" instead.
    /// </summary>
    private string ResolveMatchTarget()
    {
        SearchProgress? progress = _progress;
        if (progress is null)
        {
            return "?";
        }

        string? single = null;
        foreach (TargetProgress target in progress.Targets)
        {
            if (target.State != TargetState.Running) continue;
            if (single is not null) return "?";
            single = target.Id;
        }

        return single ?? "?";
    }

    /// <summary>
    /// Persists everything collected since the last flush as one store write and raises at most
    /// one notification for the batch. Called from the progress tick and once more before the
    /// terminal state transition; never throws into its caller.
    /// </summary>
    private void FlushPendingMatches()
    {
        StoredResult[] batch;
        int unnotified;
        SessionResult? last;
        lock (_gate)
        {
            if (_pendingPersist.Count == 0 && _unnotifiedMatches == 0)
            {
                return;
            }

            batch = _pendingPersist.ToArray();
            _pendingPersist.Clear();
            unnotified = _unnotifiedMatches;
            _unnotifiedMatches = 0;
            last = _lastMatch;
        }

        if (batch.Length > 0 && !_resultStore.IsReadOnly)
        {
            try
            {
                _resultStore.AppendRange(batch);
            }
            catch (Exception ex)
            {
                // The session result list still has the matches; the banner says they are not on disk.
                _historyWriteError = _resultStore.ReadOnlyReason ?? $"results could not be written: {ex.Message}";
            }
        }

        if (unnotified == 0 || last is null || !_settings.NotificationsEnabled)
        {
            return;
        }

        try
        {
            if (unnotified == 1)
            {
                _notifications.Notify("Match found", $"{last.Address} · {last.Pattern} ({last.Position})");
            }
            else
            {
                _notifications.Notify("Matches found", $"{unnotified} new · latest {last.Address}");
            }
        }
        catch (Exception)
        {
            // A toast that cannot be raised must not take the search down with it.
        }
    }

    private void OnLog(string line)
    {
        if (line.StartsWith("warning:", StringComparison.Ordinal) || line.StartsWith("error:", StringComparison.Ordinal))
        {
            LastFailure = line;
            Changed?.Invoke();
        }
    }

    private void OnCompleted(SearchSummary summary)
    {
        FlushPendingMatches();
        SearchSession? finished;
        lock (_gate)
        {
            LastSummary = summary;
            _progress = summary.Final;
            State = SearchState.Idle;
            finished = _session;
            _session = null;
            if (summary.Outcome == SessionOutcome.AllTargetsFailed)
            {
                LastFailure = summary.Final.Targets.FirstOrDefault(t => t.Failure is not null)?.Failure ?? "no target could start";
            }
        }

        // After the state transition, never before it: a settings write that fails would otherwise
        // fault this continuation and leave the view model stuck in Searching with a dead session.
        Dictionary<string, double> hints = new(_settings.RateHints);
        foreach (TargetProgress t in summary.Final.Targets)
        {
            if (t.State != TargetState.Failed && t.RatePerSecond > 0) hints[t.Id] = t.RatePerSecond;
        }

        SaveSettings(s => s with { RateHints = hints });

        if (_settings.NotificationsEnabled)
        {
            _notifications.Notify("Search finished", $"{summary.Final.Reported} found · {summary.Outcome}");
        }

        Changed?.Invoke();
        finished?.Dispose();
    }

    /// <summary>
    /// The body of the continuation attached to <see cref="SearchSession.Completion"/> in
    /// <see cref="Start"/>, extracted so a test can drive it directly with a faulted or
    /// cancelled task — <see cref="SearchSession"/> has no seam to make its own completion fail,
    /// so pinning the wiring this way is the only way to cover the failure branch. Unlike the
    /// previous <c>ContinueWith(..., OnlyOnRanToCompletion)</c>, this runs for every completion
    /// state: a faulted or cancelled session must still bring the view model back to
    /// <see cref="SearchState.Idle"/> instead of leaving it stuck in <see cref="SearchState.Starting"/>
    /// or <see cref="SearchState.Searching"/> forever.
    /// </summary>
    internal void OnSessionCompleted(Task<SearchSummary> task)
    {
        if (task.IsFaulted)
        {
            Exception inner = task.Exception!.GetBaseException();
            OnFailed(inner.Message);
        }
        else if (task.IsCanceled)
        {
            OnFailed("search cancelled");
        }
        else
        {
            OnCompleted(task.Result);
        }
    }

    private void OnFailed(string message)
    {
        FlushPendingMatches();
        SearchSession? finished;
        lock (_gate)
        {
            LastFailure = message;
            State = SearchState.Idle;
            finished = _session;
            _session = null;
        }

        Changed?.Invoke();
        finished?.Dispose();
    }
}
