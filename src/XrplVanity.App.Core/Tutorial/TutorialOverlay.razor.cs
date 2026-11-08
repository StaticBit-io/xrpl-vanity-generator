using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace XrplVanity.App.Core.Tutorial;

/// <summary>
/// The speech bubble of the first-run tutorial. It lives in the layout, so it survives the move
/// to the Settings page, and it follows the lit regions of the current step: the page measures
/// them in <c>tutorial.js</c> and <see cref="TutorialPlacement"/> decides where the bubble goes.
/// </summary>
public partial class TutorialOverlay
{
    private const string ModulePath = "./_content/XrplVanity.App.Core/js/tutorial.js";

    private ElementReference _bubble;
    private DotNetObjectReference<TutorialOverlay>? _self;
    private IJSObjectReference? _module;
    private TutorialStepInfo? _step;
    private TutorialStep? _shownStep;
    private TutorialRect? _position;
    private int _stepNumber;
    private int _stepCount;
    private bool _canGoNext;
    private string? _hint;
    private bool _confirming;
    private bool _pageDialog;
    private bool _leaving;
    private bool _busy;
    private bool _placed;
    private bool _pendingFocus;
    private bool _watching;
    private volatile bool _disposed;

    /// <summary>The leave confirmation or a dialog of the page is open: the bubble drops below its backdrop and takes no input.</summary>
    private bool BehindDialog => _confirming || _pageDialog;

    private string? BubbleStyle => _position is TutorialRect p
        ? string.Create(CultureInfo.InvariantCulture, $"left:{Math.Round(p.Left, 1)}px;top:{Math.Round(p.Top, 1)}px;")
        : null;

    protected override void OnInitialized()
    {
        Tutorial.Changed += OnTutorialChanged;
        Capture();
    }

    // Changed is raised on worker threads (view-model events), so the render is marshalled, and a
    // notification already in flight when the overlay is torn down is dropped.
    private void OnTutorialChanged()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                Capture();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer is gone; nothing to refresh.
        }
        catch (InvalidOperationException)
        {
            // The component is no longer attached to a renderer.
        }
    }

    private void Capture()
    {
        _step = Tutorial.Current;
        _canGoNext = Tutorial.CanGoNext;
        _hint = Tutorial.Hint;
        _stepNumber = Tutorial.StepNumber;
        _stepCount = Tutorial.StepCount;
        _pageDialog = Tutorial.PageDialogOpen;
        _leaving = Tutorial.IsLeaving;
        if (_step is null)
        {
            _confirming = false;
            _busy = false;
        }
    }

    private void Start() => _busy = !Tutorial.Begin();

    private void Advance() => Tutorial.Next();

    private void Leave() => Tutorial.Leave();

    private void ConfirmLeave()
    {
        _confirming = false;
        Tutorial.Leave();
    }

    private void CancelLeave()
    {
        _confirming = false;
        _pendingFocus = true;
    }

    /// <summary>
    /// Escape, reported by <c>tutorial.js</c>. The QR dialog owns Escape on its own step, and a
    /// confirmation that is already open owns it too.
    /// </summary>
    [JSInvokable]
    public Task OnEscape() => InvokeAsync(() =>
    {
        if (_disposed || _step is null || _confirming || _step.Step == TutorialStep.QrDialog)
        {
            return;
        }

        _confirming = true;
        StateHasChanged();
    });

    /// <summary>The window was resized or scrolled, or the page changed: measure again without scrolling.</summary>
    [JSInvokable]
    public Task OnViewChanged() => InvokeAsync(async () =>
    {
        if (_disposed || _step is null || _module is null)
        {
            return;
        }

        await MeasureAsync(_step, reveal: false);
    });

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed)
        {
            return;
        }

        TutorialStepInfo? step = _step;
        if (step is null)
        {
            await StopWatchingAsync();
            _position = null;
            _placed = false;
            _shownStep = null;
            _pendingFocus = false;
            return;
        }

        await StartWatchingAsync();
        if (_module is null && !_placed && step.Step != TutorialStep.QrDialog)
        {
            // Nothing can measure the page without the script, and the move to another route below
            // would return before any measurement: show the bubble at the stylesheet's default.
            _placed = true;
            StateHasChanged();
        }

        if (RouteDiffers(step.Route))
        {
            // The measurement waits for the new page: the observer in tutorial.js reports it.
            Nav.NavigateTo(step.Route);
            return;
        }

        if (step.Step == TutorialStep.QrDialog)
        {
            // No bubble is rendered on this step. The dialog focuses its own Close button, and
            // Escape must reach it. The placement is dropped so the next step's bubble is measured
            // from scratch rather than shown at this one's last position.
            _shownStep = step.Step;
            _position = null;
            _placed = false;
            _pendingFocus = false;
            return;
        }

        bool newStep = _shownStep != step.Step;
        if (newStep)
        {
            _shownStep = step.Step;
            _pendingFocus = true;
        }

        bool moved = await MeasureAsync(step, reveal: newStep);
        if (_pendingFocus && _placed && !moved)
        {
            _pendingFocus = false;
            await FocusBubbleAsync();
        }
    }

    /// <summary>Returns true when the bubble moved, which queues a render of its own.</summary>
    private async Task<bool> MeasureAsync(TutorialStepInfo step, bool reveal)
    {
        TutorialMeasurement? measurement = null;
        if (_module is not null)
        {
            try
            {
                measurement = await _module.InvokeAsync<TutorialMeasurement?>("measure", step.Lit, reveal);
            }
            catch (Exception ex) when (IsJsFailure(ex))
            {
                // Without a measurement the stylesheet's top-centre default still shows the bubble.
            }
        }

        if (_disposed)
        {
            return false;
        }

        TutorialRect? position = measurement is { ViewWidth: > 0, ViewHeight: > 0 }
            ? TutorialPlacement.Place(measurement.Targets, measurement.BubbleWidth, measurement.BubbleHeight, measurement.ViewWidth, measurement.ViewHeight)
            : null;
        bool moved = !_placed || position != _position;
        _placed = true;
        _position = position;
        if (moved)
        {
            StateHasChanged();
        }

        return moved;
    }

    private async Task FocusBubbleAsync()
    {
        try
        {
            await _bubble.FocusAsync(preventScroll: true);
        }
        catch (Exception ex) when (IsJsFailure(ex))
        {
            // The step changed in the same batch; the next one takes focus.
        }
    }

    private bool RouteDiffers(string route)
    {
        string relative = Nav.ToBaseRelativePath(Nav.Uri);
        int cut = relative.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0)
        {
            relative = relative[..cut];
        }

        return !string.Equals("/" + relative.Trim('/'), "/" + route.Trim('/'), StringComparison.OrdinalIgnoreCase);
    }

    private async Task StartWatchingAsync()
    {
        if (_watching)
        {
            return;
        }

        // Set first: a failed import must not be retried on every render.
        _watching = true;
        try
        {
            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _self ??= DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("watch", _self);
        }
        catch (Exception ex) when (IsJsFailure(ex))
        {
            // The bubble still shows; only repositioning and Escape are lost.
        }
    }

    private async Task StopWatchingAsync()
    {
        if (!_watching)
        {
            return;
        }

        _watching = false;
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("unwatch");
        }
        catch (Exception ex) when (IsJsFailure(ex))
        {
            // The window is closing; the listeners go with it.
        }
    }

    private static bool IsJsFailure(Exception ex) =>
        ex is JSException or JSDisconnectedException or InvalidOperationException or ObjectDisposedException or OperationCanceledException;

    public void Dispose()
    {
        _disposed = true;
        Tutorial.Changed -= OnTutorialChanged;
        _self?.Dispose();
        _self = null;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Tutorial.Changed -= OnTutorialChanged;
        await StopWatchingAsync();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (Exception ex) when (IsJsFailure(ex))
            {
                // The window is closing; the module goes with it.
            }

            _module = null;
        }

        _self?.Dispose();
        _self = null;
    }
}
