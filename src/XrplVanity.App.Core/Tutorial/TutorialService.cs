using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Tutorial;

/// <summary>
/// Drives the first-run tutorial: which step is current, what is lit, and when the user's own
/// actions move it on. The practice run itself happens in the view model's sandbox, so this class
/// holds no search state of its own and writes only the "offered" flag to the settings.
/// <see cref="SearchViewModel.Changed"/> fires on worker threads, so every rule is evaluated under
/// one lock and <see cref="Changed"/> is raised after it is released.
/// </summary>
public sealed class TutorialService : IDisposable
{
    private static readonly TimeSpan TooLong = TimeSpan.FromDays(1);
    private static readonly TimeSpan ShortEnough = TimeSpan.FromSeconds(10);

    /// <summary>The length the first step asks for: shorter words are found too quickly to show a long wait.</summary>
    private const int LongWordLength = 10;

    private readonly SearchViewModel _vm;
    private readonly IResultStore _results;
    private readonly object _lock = new();
    private TutorialStepInfo? _current;
    private int _dutyAtEntry;
    private bool _calibrationSeen;
    private bool _lastCanGoNext;
    private UiText? _lastHint;
    private bool _lastLeaving;
    private bool _beginning;
    private bool _pageDialogOpen;
    private bool _disposed;

    public TutorialService(SearchViewModel vm, IResultStore results)
    {
        _vm = vm;
        _results = results;
        _vm.Changed += OnViewModelChanged;
    }

    /// <summary>
    /// Raised, possibly on a worker thread, when the step changes, <see cref="CanGoNext"/> flips,
    /// <see cref="IsLeaving"/> changes or <see cref="PageDialogOpen"/> changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>True from the offer until the tutorial is left, finished or declined.</summary>
    public bool IsActive { get { lock (_lock) return _current is not null; } }

    public TutorialStepInfo? Current { get { lock (_lock) return _current; } }

    /// <summary>
    /// True after <see cref="Leave"/> while a running search or speed measurement is still being
    /// stopped: no step is current, but the sandbox holds the page until that run completes, and the
    /// regions stay inert so an edit made now is not lost to the restore.
    /// </summary>
    public bool IsLeaving { get { lock (_lock) return ComputeIsLeaving(); } }

    /// <summary>True while a dialog of the page is open, so the bubble stays out of its way.</summary>
    public bool PageDialogOpen { get { lock (_lock) return _pageDialogOpen; } }

    /// <summary>Whether the step's <b>Next</b> button is enabled; false on steps that move on by themselves.</summary>
    public bool CanGoNext { get { lock (_lock) return ComputeCanGoNext(); } }

    /// <summary>
    /// A line shown under the step's text when the user's own state needs it, or null. Adding a long
    /// word next to a short one changes nothing, because any one word is enough for a match.
    /// </summary>
    public UiText? Hint { get { lock (_lock) return ComputeHint(); } }

    /// <summary>1-based position among the numbered steps, which exclude the offer; 0 on the offer or when inactive.</summary>
    public int StepNumber
    {
        get
        {
            lock (_lock)
            {
                if (_current is null || _current.Step == TutorialStep.Offer) return 0;
                bool gpuLeftOut = _current.Step > TutorialStep.GpuDuty && !HasGpuTarget();
                return (int)_current.Step - (gpuLeftOut ? 1 : 0);
            }
        }
    }

    /// <summary>The number of numbered steps for the devices present, which leave out the GPU load step with no GPU.</summary>
    public int StepCount
    {
        get
        {
            lock (_lock) return TutorialScript.Steps.Count - 1 - (HasGpuTarget() ? 0 : 1);
        }
    }

    /// <summary>
    /// Whether the Settings button can start the tutorial: nothing is running and the view model is
    /// not still restoring a previous sandbox.
    /// </summary>
    public bool CanStart
    {
        get
        {
            lock (_lock)
            {
                return _current is null && !_vm.IsSandbox && _vm.State == SearchState.Idle && !_vm.IsCalibrating;
            }
        }
    }

    /// <summary>
    /// True for a user with no flag, no words and no stored results. A store that cannot be read or
    /// is read-only counts as an existing user, so a returning user with damaged data is never
    /// interrupted.
    /// </summary>
    public bool ShouldOffer()
    {
        if (IsActive || _vm.Settings.TutorialOffered || _vm.Patterns.Count > 0) return false;

        int stored;
        try
        {
            stored = _results.LoadAll().Count;
        }
        catch (Exception)
        {
            return false;
        }

        // Read after the load: DpapiResultStore turns read-only inside LoadAll and answers with an
        // empty list, which would otherwise look like a new user.
        return !_results.IsReadOnly && stored == 0;
    }

    /// <summary>
    /// Shows the offer card and records that the offer was made, so a window closed or a crash with
    /// the card still open does not bring it back on the next start.
    /// </summary>
    public void ShowOffer()
    {
        lock (_lock)
        {
            if (_current is not null) return;
            Enter(TutorialStep.Offer);
        }

        if (!_vm.Settings.TutorialOffered)
        {
            _vm.SaveSettings(s => s with { TutorialOffered = true });
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Starts the practice run from the offer or from Settings. Returns false, changing nothing,
    /// when the view model is busy; the offer then stays open.
    /// </summary>
    public bool Begin()
    {
        lock (_lock)
        {
            if (_disposed || (_current is not null && _current.Step != TutorialStep.Offer)) return false;
            _beginning = true;
        }

        bool began = false;
        try
        {
            began = _vm.BeginSandbox();
        }
        finally
        {
            if (!began)
            {
                lock (_lock) _beginning = false;
            }
        }

        if (!began) return false;

        // _settings still holds the user's values, so this write cannot carry a tutorial value to disk.
        // A run started from Settings by a user who was never offered it is recorded here.
        if (!_vm.Settings.TutorialOffered)
        {
            _vm.SaveSettings(s => s with { TutorialOffered = true });
        }

        lock (_lock)
        {
            Enter(TutorialStep.AddWord);
            Settle();
            _beginning = false;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Moves on from a step whose <b>Next</b> or <b>Finish</b> button is enabled; after the last step it leaves.</summary>
    public void Next()
    {
        bool finish;
        lock (_lock)
        {
            if (_current is null || !ComputeCanGoNext()) return;
            finish = _current.Step == TutorialStep.Done;
            if (!finish)
            {
                Enter(_current.Step + 1);
                Settle();
            }
        }

        if (finish)
        {
            Leave();
            return;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Ends the tutorial from any step and gives the user's state back. Declining the offer takes the
    /// same path and starts no sandbox. Safe to call twice. When a run has to be stopped first the
    /// state comes back on its completion: <see cref="IsLeaving"/> covers the gap and
    /// <see cref="Changed"/> is raised again when it closes.
    /// </summary>
    public void Leave()
    {
        TutorialStepInfo? previous;
        lock (_lock)
        {
            previous = _current;
            _current = null;
            _calibrationSeen = false;
            _lastCanGoNext = false;
            _pageDialogOpen = false;
        }

        if (previous is null) return;

        if (previous.Step != TutorialStep.Offer)
        {
            _vm.EndSandbox();
        }

        lock (_lock) _lastLeaving = ComputeIsLeaving();
        Changed?.Invoke();
    }

    /// <summary>
    /// A page dialog opened or closed. The bubble drops behind a dialog and stops taking input, the
    /// way it does for its own leave confirmation.
    /// </summary>
    public void SetPageDialogOpen(bool open)
    {
        lock (_lock)
        {
            if (_pageDialogOpen == open) return;
            _pageDialogOpen = open;
        }

        Changed?.Invoke();
    }

    /// <summary>The QR dialog was opened on the practice result.</summary>
    public void QrOpened() => Advance(TutorialStep.QrButton);

    /// <summary>The QR dialog was closed.</summary>
    public void QrClosed() => Advance(TutorialStep.QrDialog);

    /// <summary>True when the current step lights <paramref name="region"/>.</summary>
    public bool IsLit(string region)
    {
        lock (_lock) return _current is not null && _current.Lit.Contains(region);
    }

    /// <summary>
    /// True when the region must ignore input: unlit while a tutorial runs, lit on a step that only
    /// explains it, or any region while the sandbox is still being wound up after a leave. False when
    /// no tutorial runs and no sandbox remains.
    /// </summary>
    public bool IsInert(string region)
    {
        lock (_lock)
        {
            if (_current is null) return ComputeIsLeaving();
            return !_current.Interactive || !_current.Lit.Contains(region);
        }
    }

    public void Dispose()
    {
        lock (_lock) _disposed = true;
        _vm.Changed -= OnViewModelChanged;
    }

    private void Advance(TutorialStep from)
    {
        lock (_lock)
        {
            if (_current?.Step != from) return;
            Enter(from + 1);
            Settle();
        }

        Changed?.Invoke();
    }

    private void OnViewModelChanged()
    {
        bool changed;
        lock (_lock)
        {
            if (_current is not null)
            {
                changed = Settle();
            }
            else
            {
                // After a leave this is the notification that the sandbox has ended.
                bool leaving = ComputeIsLeaving();
                changed = leaving != _lastLeaving;
                _lastLeaving = leaving;
            }
        }

        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Applies the rules until no step moves on by itself, and notes whether <see cref="CanGoNext"/>
    /// or <see cref="Hint"/> changed. The caller holds the lock and raises <see cref="Changed"/> when
    /// this returns true.
    /// </summary>
    private bool Settle()
    {
        bool moved = false;
        for (int guard = 0; guard < TutorialScript.Steps.Count; guard++)
        {
            TutorialStep? next = NextByRule();
            if (next is null) break;
            Enter(next.Value);
            moved = true;
        }

        bool canGoNext = ComputeCanGoNext();
        bool flipped = canGoNext != _lastCanGoNext;
        _lastCanGoNext = canGoNext;
        UiText? hint = ComputeHint();
        bool hintChanged = !Equals(hint, _lastHint);
        _lastHint = hint;
        return moved || flipped || hintChanged;
    }

    /// <summary>The step the user's own actions have already earned, or null to stay.</summary>
    private TutorialStep? NextByRule()
    {
        if (_current is null) return null;

        TimeSpan? estimate = _vm.EstimatedTime;
        bool calibrating = _vm.IsCalibrating;
        if (calibrating)
        {
            _calibrationSeen = true;
        }
        else if (_calibrationSeen && (estimate is not null || _vm.CalibrationFailure is not null))
        {
            // The hints are written after the flag drops, so a measurement that has not produced an
            // estimate yet keeps the flag until one arrives or it is reported failed.
            if (_current.Step == TutorialStep.Devices && estimate is not null)
            {
                return TutorialStep.LongWord;
            }

            _calibrationSeen = false;
        }

        switch (_current.Step)
        {
            case TutorialStep.AddWord:
                // A chip no address can match leaves no estimate, and Devices does not light the
                // pattern card, so only a reachable word moves on. It must also be long: the estimate
                // step needs a wait of years, which a short word next to it would hide.
                return _vm.ExpectedCandidates is not null && HasLongWord() ? TutorialStep.ListFiles : null;
            case TutorialStep.ListFiles:
                return _vm.ExpectedCandidates is null ? TutorialStep.AddWord : null;
            case TutorialStep.ShortWord:
                return estimate is TimeSpan quick && quick <= ShortEnough ? TutorialStep.Search : null;
            case TutorialStep.Search:
                if (_vm.State != SearchState.Idle) return null;
                if (_vm.Results.Count > 0) return TutorialStep.Result;
                return estimate is TimeSpan slow && slow > ShortEnough ? TutorialStep.ShortWord : null;
            case TutorialStep.GpuDuty:
                return _vm.DutyPercent != _dutyAtEntry ? TutorialStep.StopAfter : null;
            default:
                return null;
        }
    }

    /// <summary>Sets the current step, skipping the GPU load step when there is no GPU. The caller holds the lock.</summary>
    private void Enter(TutorialStep step)
    {
        if (step == TutorialStep.GpuDuty && !HasGpuTarget())
        {
            step = TutorialStep.StopAfter;
        }

        if (step == TutorialStep.Devices)
        {
            _calibrationSeen = false;
        }
        else if (step == TutorialStep.GpuDuty)
        {
            _dutyAtEntry = _vm.DutyPercent;
        }

        _current = TutorialScript.Steps[(int)step];
    }

    private bool ComputeCanGoNext()
    {
        if (_current is null) return false;

        switch (_current.Step)
        {
            case TutorialStep.ListFiles:
                return _vm.ExpectedCandidates is not null;
            case TutorialStep.Result:
            case TutorialStep.StopAfter:
            case TutorialStep.NotificationsSetting:
            case TutorialStep.DataFolder:
            case TutorialStep.Done:
                return true;
            case TutorialStep.Devices:
                return !_vm.IsCalibrating && _vm.EstimatedTime is not null;
            case TutorialStep.LongWord:
                return _vm.EstimatedTime is TimeSpan wait && wait > TooLong;
            default:
                return false;
        }
    }

    private UiText? ComputeHint()
    {
        if (_current?.Step != TutorialStep.LongWord || _vm.Patterns.Count < 2) return null;
        return _vm.EstimatedTime is TimeSpan wait && wait <= TooLong ? TutorialText.ShortestWordHint : null;
    }

    private bool HasLongWord() =>
        _vm.Patterns.Any(p => p.Text.Length + (p.EndsText?.Length ?? 0) >= LongWordLength);

    private bool ComputeIsLeaving() => _current is null && !_beginning && _vm.IsSandbox;

    private bool HasGpuTarget() => _vm.Targets.Any(t => !t.IsCpu && !t.IsCpuDevice);
}
