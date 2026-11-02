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
    private static readonly TimeSpan CalibrationHardCap = TimeSpan.FromSeconds(60);

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
    private string _entryText = string.Empty;
    private string _entryEndsText = string.Empty;
    private MatchPosition _entryPosition = MatchPosition.Starts;
    private bool _entryIgnoreCase = true;
    private bool _entryNearStart;
    private SearchSession? _session;
    private SearchProgress? _progress;
    private int _dutyPercent;
    private int _cpuThreads;
    private int? _count;
    private SearchSession? _calibrationSession;
    private int _calibrationStopArmed;

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
            if (!Enum.TryParse(draft.Position, out MatchPosition position)) continue;
            // A Pair draft needs both halves. One saved by a build that predates paired patterns
            // (or corrupted since) carries none, and restoring it would put a chip on screen that
            // names a rule it cannot express — drop it here, the same way a bad value is clamped.
            if (position == MatchPosition.Pair && string.IsNullOrWhiteSpace(draft.EndsText)) continue;
            _patterns.Add(new PatternChip(draft.Text, position, draft.IgnoreCase, draft.EndsText));
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
            if (rate <= 0) return "rate unknown — press \"measure speed\" or run a search once";
            return _progress is not null && _progress.Elapsed >= LiveRateAfter && State == SearchState.Searching
                ? "from the live rate"
                : "from the last measured rate of the selected targets";
        }
    }

    public bool CanStart => State == SearchState.Idle && !IsCalibrating && StartBlockedReason is null;

    /// <summary>How long <see cref="MeasureSpeed"/> lets a target run once it has reported its first candidate; shortened by tests.</summary>
    internal TimeSpan CalibrationDuration { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>True from <see cref="MeasureSpeed"/> until its session's completion has been processed.</summary>
    public bool IsCalibrating { get; private set; }

    /// <summary>The reason the last calibration produced no usable rate, or null. Rendered under the device rows.</summary>
    public string? CalibrationFailure { get; private set; }

    /// <summary>
    /// A calibration measures the selected devices, not a pattern, so — unlike <see cref="CanStart"/> —
    /// it does not require a reachable pattern.
    /// </summary>
    public bool CanMeasureSpeed => State == SearchState.Idle && !IsCalibrating && TargetProblem is null;

    /// <summary>
    /// The pattern draft not yet committed to a chip. Setting these does not persist to settings —
    /// only a committed <see cref="PatternChip"/> (via <see cref="AddPattern"/> or
    /// <see cref="AddPatternFromEntry"/>) is saved.
    /// </summary>
    public string EntryText
    {
        get { lock (_gate) return _entryText; }
        set
        {
            string normalized = value ?? string.Empty;
            bool changed;
            lock (_gate)
            {
                changed = normalized != _entryText;
                _entryText = normalized;
            }

            if (!changed) return;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// The second box, shown only while <see cref="EntryPosition"/> is <see cref="MatchPosition.Pair"/>:
    /// the suffix half of the pending rule. Like <see cref="EntryText"/> it persists nothing until
    /// the pair is committed as one chip.
    /// </summary>
    public string EntryEndsText
    {
        get { lock (_gate) return _entryEndsText; }
        set
        {
            string normalized = value ?? string.Empty;
            bool changed;
            lock (_gate)
            {
                changed = normalized != _entryEndsText;
                _entryEndsText = normalized;
            }

            if (!changed) return;
            Changed?.Invoke();
        }
    }

    public MatchPosition EntryPosition
    {
        get { lock (_gate) return _entryPosition; }
        set
        {
            bool changed;
            lock (_gate)
            {
                changed = value != _entryPosition;
                _entryPosition = value;
            }

            if (!changed) return;
            Changed?.Invoke();
        }
    }

    public bool EntryIgnoreCase
    {
        get { lock (_gate) return _entryIgnoreCase; }
        set
        {
            bool changed;
            lock (_gate)
            {
                changed = value != _entryIgnoreCase;
                _entryIgnoreCase = value;
            }

            if (!changed) return;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Whether a "starts with" entry is committed as <see cref="MatchPosition.NearStart"/>: allowed
    /// at the 2nd or 3rd character of the address rather than only right after the 'r'. Off by
    /// default, because it changes what the address looks like; ignored in every other mode.
    /// </summary>
    public bool EntryNearStart
    {
        get { lock (_gate) return _entryNearStart; }
        set
        {
            bool changed;
            lock (_gate)
            {
                changed = value != _entryNearStart;
                _entryNearStart = value;
            }

            if (!changed) return;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Non-null while a reachable "starts with" entry, with <see cref="EntryNearStart"/> off,
    /// would be found at least <see cref="NearStartHintSpeedup"/> times sooner with it on.
    /// Rendered below the entry row.
    /// </summary>
    public string? NearStartHint
    {
        get
        {
            lock (_gate)
            {
                if (_entryPosition != MatchPosition.Starts || _entryNearStart) return null;
                string trimmedEntry = _entryText.Trim();
                if (NearStartSpeedup(trimmedEntry, _entryIgnoreCase) is not double speedup) return null;
                return $"\"{trimmedEntry}\" can rarely start right after the r: allowing it at the 2nd or 3rd character finds it about {speedup:0}× sooner.";
            }
        }
    }

    /// <summary>
    /// Non-null when there is neither a reachable committed pattern nor a reachable pending entry;
    /// blocks Start. Rendered inside the Patterns card, below the entry row.
    /// </summary>
    public string? PatternProblem
    {
        get
        {
            lock (_gate)
            {
                if (_patternSet is not null && _patternSet.HasPatterns) return null;
                string trimmedEntry = _entryText.Trim();
                if (IsReachableEntry(trimmedEntry)) return null;
                // For a pair the message names the half that is actually at fault: the first box
                // when it is the unreachable one, the second box otherwise.
                string offending = _entryPosition == MatchPosition.Pair && IsReachableHalf(trimmedEntry)
                    ? _entryEndsText.Trim()
                    : trimmedEntry;
                return offending.Length == 0
                    ? "Add at least one reachable pattern."
                    : $"\"{offending}\" cannot occur in an XRPL address.";
            }
        }
    }

    /// <summary>
    /// Non-null while the pending entry is reachable — whether or not a pattern is already
    /// committed. Does not block Start; rendered inside the Patterns card, below the entry row.
    /// </summary>
    public string? PatternHint
    {
        get
        {
            lock (_gate)
            {
                string trimmedEntry = _entryText.Trim();
                if (!IsReachableEntry(trimmedEntry)) return null;
                string typed = trimmedEntry;
                string normalized = PatternNormalizer.Normalize(trimmedEntry).Value;
                if (_entryPosition == MatchPosition.Pair)
                {
                    // Both halves are previewed as one "starts..ends" string — the same shape
                    // PatternSet reports a paired rule under.
                    string trimmedEnds = _entryEndsText.Trim();
                    typed = $"{typed}..{trimmedEnds}";
                    normalized = $"{normalized}..{PatternNormalizer.Normalize(trimmedEnds).Value}";
                }

                return normalized != typed
                    ? $"\"{typed}\" is searched as \"{normalized}\" — press Enter to add it, or just press Start."
                    : $"Press Enter to add \"{typed}\" — Start adds it too.";
            }
        }
    }

    /// <summary>Non-null when no target is selected; rendered beside the Start button.</summary>
    public string? TargetProblem
    {
        get
        {
            lock (_gate)
            {
                return _targets.Any(t => t.Selected) ? null : "Select the CPU or at least one device.";
            }
        }
    }

    public string? StartBlockedReason => PatternProblem ?? TargetProblem;

    /// <summary>Returns false when the pattern is blank or already in the set (see <see cref="PatternChip.Identity"/>).</summary>
    public bool AddPattern(string text, MatchPosition position, bool ignoreCase)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        lock (_gate)
        {
            if (!TryAdd(new PatternChip(trimmed, position, ignoreCase), KnownIdentities())) return false;
            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Adds one rule that matches only an address body starting with <paramref name="starts"/> and
    /// ending with <paramref name="ends"/>. Both halves are required: a blank one adds nothing,
    /// the way <see cref="AddPattern"/> ignores a blank pattern.
    /// </summary>
    public bool AddPair(string starts, string ends, bool ignoreCase)
    {
        string trimmedStarts = starts.Trim();
        string trimmedEnds = ends.Trim();
        if (trimmedStarts.Length == 0 || trimmedEnds.Length == 0) return false;
        lock (_gate)
        {
            if (!TryAdd(new PatternChip(trimmedStarts, MatchPosition.Pair, ignoreCase, trimmedEnds), KnownIdentities())) return false;
            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Commits the pending entry as a chip, the same way pressing Enter in the entry row does.
    /// Returns false without side effects when the trimmed entry is blank — or, in
    /// <see cref="MatchPosition.Pair"/> mode, when either of the two boxes is: the pair commits
    /// as one chip or not at all. Also false when the rule is already in the set.
    /// </summary>
    public bool AddPatternFromEntry()
    {
        string trimmed;
        string trimmedEnds;
        MatchPosition position;
        bool ignoreCase;
        lock (_gate)
        {
            trimmed = _entryText.Trim();
            if (trimmed.Length == 0) return false;
            position = _entryPosition;
            trimmedEnds = _entryEndsText.Trim();
            if (position == MatchPosition.Pair && trimmedEnds.Length == 0) return false;
            ignoreCase = _entryIgnoreCase;
            if (position == MatchPosition.Starts && _entryNearStart) position = MatchPosition.NearStart;
            _entryText = string.Empty;
            // Only in Pair mode: outside it the second box is not on screen, and clearing it would
            // silently drop a draft the user typed before switching modes.
            if (position == MatchPosition.Pair) _entryEndsText = string.Empty;
        }

        // The result is the add's own: a rule the set already holds commits nothing, and the
        // editor says so rather than leaving the user looking for a chip that never appeared.
        return position == MatchPosition.Pair
            ? AddPair(trimmed, trimmedEnds, ignoreCase)
            : AddPattern(trimmed, position, ignoreCase);
    }

    /// <summary>
    /// The identities already in the set, for a caller about to append. Built once per operation
    /// so loading a 20 000-word list stays linear rather than comparing every line against every
    /// chip. The caller holds <c>_gate</c>.
    /// </summary>
    private HashSet<string> KnownIdentities()
    {
        HashSet<string> known = new(StringComparer.Ordinal);
        foreach (PatternChip existing in _patterns) known.Add(existing.Identity);
        return known;
    }

    /// <summary>Appends the chip unless <paramref name="known"/> already holds its rule, keeping the set in step.</summary>
    private bool TryAdd(PatternChip chip, HashSet<string> known)
    {
        if (!known.Add(chip.Identity)) return false;
        _patterns.Add(chip);
        return true;
    }

    /// <summary>Drops every chip. The pending entry is left alone: it was never part of the set.</summary>
    public void ClearPatterns()
    {
        lock (_gate)
        {
            if (_patterns.Count == 0) return;
            _patterns.Clear();
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

    /// <summary>
    /// Lets a "starts with" chip begin at the 2nd or 3rd character, the action behind the chip's
    /// speed-up hint. A chip whose near-start twin is already in the set is dropped instead, so
    /// the set never holds one rule twice.
    /// </summary>
    public void AllowNearStart(PatternChip chip)
    {
        lock (_gate)
        {
            if (chip.Position != MatchPosition.Starts || !_patterns.Contains(chip)) return;
            chip.Position = MatchPosition.NearStart;
            string identity = chip.Identity;
            if (_patterns.Any(p => !ReferenceEquals(p, chip) && p.Identity == identity)) _patterns.Remove(chip);
            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
    }

    /// <summary>
    /// Adds every line a pattern list holds, in the grammar <see cref="PatternListFormat"/> defines
    /// — a plain word among them still becoming a Starts chip carrying <paramref name="ignoreCase"/>.
    /// Blank lines are skipped; returns how many non-blank lines fell outside the grammar, for the
    /// caller to report.
    /// </summary>
    public ListLoadResult AddPatternsFromList(IEnumerable<string> lines, bool ignoreCase)
    {
        int added = 0;
        int duplicates = 0;
        int rejected = 0;
        lock (_gate)
        {
            HashSet<string> known = KnownIdentities();
            foreach (string line in lines)
            {
                if (line.Trim().Length == 0) continue;
                if (!PatternListFormat.TryParse(line, ignoreCase, out PatternChip chip))
                {
                    rejected++;
                }
                else if (TryAdd(chip, known))
                {
                    added++;
                }
                else
                {
                    duplicates++;
                }
            }

            RebuildPatternSet();
        }

        PersistDraft();
        Changed?.Invoke();
        return new ListLoadResult(added, duplicates, rejected);
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
        bool pendingEntryIsReachable;
        lock (_gate) pendingEntryIsReachable = IsReachableEntry(_entryText.Trim());
        if (pendingEntryIsReachable)
        {
            AddPatternFromEntry();
        }

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

    /// <summary>
    /// Runs a short, on-demand benchmark of the selected targets so the estimate has a rate to
    /// show before the user ever runs a real search. Uses its own <see cref="SearchSession"/>
    /// (<see cref="_calibrationSession"/>) over an unmatchable pattern (<c>Starts "zzzzzzzz"</c>)
    /// so nothing is ever reported as found; results, history and notifications are untouched —
    /// only the rate hints in <see cref="AppSettings"/> are updated, via the same
    /// <see cref="ApplyRateHints"/> helper <see cref="OnCompleted"/> uses.
    /// </summary>
    public void MeasureSpeed()
    {
        SearchSession? session = null;
        try
        {
            lock (_gate)
            {
                if (!CanMeasureSpeed) return;
                List<SearchTarget> targets = new();
                foreach (TargetChoice t in _targets.Where(t => t.Selected))
                {
                    targets.Add(t.IsCpu
                        ? new SearchTarget.Cpu(_cpuThreads)
                        : new SearchTarget.Device(ParseSelector(t.Id)));
                }

                PatternSet.Builder builder = new();
                builder.Add(MatchPosition.Starts, "zzzzzzzz", ignoreCase: false);
                SearchRequest request = new(builder.Build(), targets, null, _dutyPercent);

                IsCalibrating = true;
                CalibrationFailure = null;
                _calibrationStopArmed = 0;
                session = new SearchSession(request, _ => { });
                _calibrationSession = session;
                session.ProgressChanged += OnCalibrationProgress;
                session.Completion.ContinueWith(OnCalibrationCompleted);
                session.Start();
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                CalibrationFailure = ex.Message;
                IsCalibrating = false;
                _calibrationSession = null;
            }

            session?.Dispose();
            Changed?.Invoke();
            return;
        }

        // Belt-and-braces cap: stops the session even if no target ever reports a candidate
        // (e.g. a device stuck opening), so a bad run cannot calibrate forever.
        _ = StopSessionAfterAsync(session, CalibrationHardCap);
        Changed?.Invoke();
    }

    /// <summary>
    /// Arms the calibration's stop-after-<see cref="CalibrationDuration"/> countdown on the first
    /// progress snapshot in which any target reports a candidate — device opening and an OpenCL
    /// program build happen before that and must not eat into the measured window. Runs on the
    /// session's ticker thread; <see cref="Interlocked.Exchange(ref int, int)"/> makes the "arm
    /// once" check safe against the tick that follows arriving before the delay task is scheduled.
    /// </summary>
    private void OnCalibrationProgress(SearchProgress progress)
    {
        if (Volatile.Read(ref _calibrationStopArmed) != 0) return;
        if (!progress.Targets.Any(t => t.Candidates > 0)) return;
        if (Interlocked.Exchange(ref _calibrationStopArmed, 1) != 0) return;

        SearchSession? session = _calibrationSession;
        if (session is null) return;
        _ = StopSessionAfterAsync(session, CalibrationDuration);
    }

    /// <summary>
    /// A background <see cref="Task.Delay(TimeSpan)"/> rather than a <see cref="System.Threading.Timer"/>:
    /// the delay's own continuation is what keeps it alive, so there is nothing to dispose, and
    /// racing it against <paramref name="session"/>'s own <see cref="SearchSession.Completion"/>
    /// means a session that ends on its own (Stop, a failure, the hard cap) is not kept waiting on
    /// a stale delay — this returns as soon as either settles.
    /// </summary>
    private static async Task StopSessionAfterAsync(SearchSession session, TimeSpan delay)
    {
        await Task.WhenAny(session.Completion, Task.Delay(delay)).ConfigureAwait(false);
        session.Stop();
    }

    /// <summary>
    /// The body of the continuation attached to the calibration session's <see cref="SearchSession.Completion"/>.
    /// Mirrors <see cref="OnSessionCompleted"/>'s fault/cancel handling but never touches <see cref="_results"/>,
    /// <see cref="LastSummary"/>, the result store or the notifier — a calibration is not a search.
    /// </summary>
    private void OnCalibrationCompleted(Task<SearchSummary> task)
    {
        SearchSummary? summary = null;
        string? failure = null;
        if (task.IsFaulted)
        {
            failure = task.Exception!.GetBaseException().Message;
        }
        else if (task.IsCanceled)
        {
            failure = "calibration cancelled";
        }
        else
        {
            summary = task.Result;
        }

        SearchSession? finished;
        lock (_gate)
        {
            finished = _calibrationSession;
            _calibrationSession = null;
            IsCalibrating = false;
        }

        if (summary is not null)
        {
            IReadOnlyList<TargetProgress> targets = summary.Final.Targets;
            ApplyRateHints(targets);
            bool allFailed = targets.Count > 0 && targets.All(t => t.State == TargetState.Failed);
            CalibrationFailure = allFailed
                ? targets.FirstOrDefault(t => t.Failure is not null)?.Failure ?? "no target could be measured"
                : null;
        }
        else
        {
            CalibrationFailure = failure;
        }

        Changed?.Invoke();
        finished?.Dispose();
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
        SearchSession? calibration;
        lock (_gate)
        {
            session = _session;
            _session = null;
            calibration = _calibrationSession;
            _calibrationSession = null;
        }

        session?.Dispose();
        calibration?.Dispose();
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

    /// <summary>A trimmed half is reachable when normalizing it yields a non-empty, in-alphabet value.</summary>
    private static bool IsReachableHalf(string trimmedHalf) =>
        trimmedHalf.Length > 0 && PatternNormalizer.Normalize(trimmedHalf) is { Reachable: true, Value.Length: > 0 };

    /// <summary>
    /// The pending entry as a whole. In <see cref="MatchPosition.Pair"/> mode it counts as
    /// reachable only when both boxes are non-blank and reachable — a half-typed pair is not a
    /// rule. Reads <c>_entryPosition</c>/<c>_entryEndsText</c>, so callers hold <c>_gate</c>.
    /// </summary>
    private bool IsReachableEntry(string trimmedEntry) =>
        _entryPosition == MatchPosition.Pair
            ? IsReachableHalf(trimmedEntry) && IsReachableHalf(_entryEndsText.Trim())
            : IsReachableHalf(trimmedEntry);

    /// <summary>
    /// The smallest speed-up worth a hint. A first character that can follow the 'r' gains only
    /// about 1.4×, one that cannot gains about 24×; the partial index 23 ('Q') sits near 2.2×.
    /// </summary>
    public const double NearStartHintSpeedup = 2.0;

    /// <summary>
    /// How many times sooner <paramref name="text"/> as a "starts with" pattern is found once it may
    /// also begin one character later, or null when that is below <see cref="NearStartHintSpeedup"/>
    /// or the text is not a reachable pattern. Mirrors the builder's case expansion: a pattern
    /// longer than <see cref="PatternSet.MaxIgnoreCaseLength"/> is searched case-sensitively.
    /// </summary>
    private static double? NearStartSpeedup(string text, bool ignoreCase)
    {
        NormalizedPattern normalized = PatternNormalizer.Normalize(text);
        if (!normalized.Reachable || normalized.Value.Length == 0) return null;
        char first = normalized.Value[0];
        HashSet<char> firsts = new() { first };
        if (ignoreCase && normalized.Value.Length <= PatternSet.MaxIgnoreCaseLength)
        {
            firsts.Add(PatternNormalizer.NormalizeChar(char.ToLowerInvariant(first)));
            firsts.Add(PatternNormalizer.NormalizeChar(char.ToUpperInvariant(first)));
        }

        double speedup = SearchEstimator.NearStartSpeedup(firsts);
        return speedup >= NearStartHintSpeedup ? speedup : null;
    }

    /// <summary>"cannot occur in an XRPL address" / "empty pattern" for one half, or null.</summary>
    private static string? UnreachableWarning(NormalizedPattern normalized) =>
        !normalized.Reachable ? "cannot occur in an XRPL address"
        : normalized.Value.Length == 0 ? "empty pattern"
        : null;

    /// <summary>The case-folding cap warning for one half, or null. Applied per half for a pair.</summary>
    private static string? CaseFoldWarning(NormalizedPattern normalized, bool ignoreCase) =>
        ignoreCase && normalized.Value.Length > PatternSet.MaxIgnoreCaseLength
            ? $"longer than {PatternSet.MaxIgnoreCaseLength} characters: searched case-sensitively"
            : null;

    /// <summary>
    /// A paired chip reports the first half that cannot occur before either half's folding cap:
    /// an unreachable half drops the whole rule, while the cap only changes how it is searched.
    /// </summary>
    private static string? ChipWarning(PatternChip chip)
    {
        if (chip.Position != MatchPosition.Pair)
        {
            NormalizedPattern single = PatternNormalizer.Normalize(chip.Text);
            return UnreachableWarning(single) ?? CaseFoldWarning(single, chip.IgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(chip.EndsText)) return "empty pattern";
        NormalizedPattern starts = PatternNormalizer.Normalize(chip.Text);
        NormalizedPattern ends = PatternNormalizer.Normalize(chip.EndsText.Trim());
        return UnreachableWarning(starts)
            ?? UnreachableWarning(ends)
            ?? CaseFoldWarning(starts, chip.IgnoreCase)
            ?? CaseFoldWarning(ends, chip.IgnoreCase);
    }

    /// <summary>The folded form of a chip, or null when folding changes nothing.</summary>
    private static string? ChipNormalized(PatternChip chip)
    {
        if (chip.Position != MatchPosition.Pair || string.IsNullOrWhiteSpace(chip.EndsText))
        {
            string single = PatternNormalizer.Normalize(chip.Text).Value;
            return single != chip.Text ? single : null;
        }

        string trimmedEnds = chip.EndsText.Trim();
        string typed = $"{chip.Text}..{trimmedEnds}";
        string folded = $"{PatternNormalizer.Normalize(chip.Text).Value}..{PatternNormalizer.Normalize(trimmedEnds).Value}";
        return folded != typed ? folded : null;
    }

    private void RebuildPatternSet()
    {
        PatternSet.Builder builder = new();
        foreach (PatternChip chip in _patterns)
        {
            if (chip.Position == MatchPosition.Pair)
            {
                // UpdatePattern is public and can flip any chip to Pair, including one that has no
                // second half; such a chip contributes no rule and says so through its Warning.
                if (!string.IsNullOrWhiteSpace(chip.EndsText))
                {
                    builder.AddPair(chip.Text, chip.EndsText, chip.IgnoreCase);
                }
            }
            else
            {
                builder.Add(chip.Position, chip.Text, chip.IgnoreCase);
            }
        }

        PatternSet set = builder.Build();
        foreach (PatternChip chip in _patterns)
        {
            chip.Warning = ChipWarning(chip);
            chip.Normalized = ChipNormalized(chip);
            chip.NearStartSpeedup = chip.Position == MatchPosition.Starts ? NearStartSpeedup(chip.Text, chip.IgnoreCase) : null;
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
                Patterns = _patterns.Select(p => new PatternDraft(p.Text, p.Position.ToString(), p.IgnoreCase, p.EndsText)).ToArray(),
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

    /// <summary>
    /// Records the measured rate of every target that ran without failing, shared by
    /// <see cref="OnCompleted"/> (a real search) and <see cref="OnCalibrationCompleted"/>
    /// (a <see cref="MeasureSpeed"/> run) so both paths update <see cref="AppSettings.RateHints"/>
    /// the same way.
    /// </summary>
    private void ApplyRateHints(IReadOnlyList<TargetProgress> targets)
    {
        Dictionary<string, double> hints = new(_settings.RateHints);
        foreach (TargetProgress t in targets)
        {
            if (t.State != TargetState.Failed && t.RatePerSecond > 0) hints[t.Id] = t.RatePerSecond;
        }

        SaveSettings(s => s with { RateHints = hints });
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
        ApplyRateHints(summary.Final.Targets);

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
