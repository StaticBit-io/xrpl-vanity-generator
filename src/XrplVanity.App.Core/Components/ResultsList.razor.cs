using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Components;

public partial class ResultsList : IDisposable
{
    private readonly HashSet<string> _revealed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _animated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITimer> _hideTimers = new(StringComparer.Ordinal);
    private IReadOnlyList<SessionResult>? _lastItems;
    private string? _exportError;

    [Parameter, EditorRequired] public IReadOnlyList<SessionResult> Items { get; set; } = Array.Empty<SessionResult>();
    [Parameter] public bool ShowTarget { get; set; }
    [Parameter] public EventCallback<SessionResult> OnDelete { get; set; }
    [Parameter] public int RevealSeconds { get; set; } = 15;
    [Parameter] public int ClipboardClearSeconds { get; set; } = 30;

    protected override void OnParametersSet()
    {
        // A new session replaces the Items instance (SearchViewModel.Start clears _results); a
        // revealed seed from a previous session must not leak into the new one's rendering.
        if (!ReferenceEquals(Items, _lastItems))
        {
            _lastItems = Items;
            if (_revealed.Count > 0 || _hideTimers.Count > 0)
            {
                foreach (ITimer timer in _hideTimers.Values) timer.Dispose();
                _hideTimers.Clear();
                _revealed.Clear();
            }
        }
    }

    private void ToggleReveal(string address)
    {
        if (_revealed.Remove(address))
        {
            StopTimer(address);
            return;
        }

        _revealed.Add(address);
        StopTimer(address);
        _hideTimers[address] = Clock.CreateTimer(_ => InvokeAsync(() =>
        {
            _revealed.Remove(address);
            StopTimer(address);
            StateHasChanged();
        }), null, TimeSpan.FromSeconds(RevealSeconds), Timeout.InfiniteTimeSpan);
    }

    private void StopTimer(string address)
    {
        if (_hideTimers.Remove(address, out ITimer? timer))
        {
            timer.Dispose();
        }
    }

    private async Task Export()
    {
        _exportError = null;
        string? path = await Dialog.PickSaveTsvAsync("xrpl-vanity.tsv");
        if (path is null) return;
        try
        {
            // Items are written in the order given (the newest-first order the view model
            // exposes maps 1:1 onto the file, matching how the results list itself is read).
            TsvExport.Write(path, Items.Select(i => (i.Address, i.Seed)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _exportError = $"Export failed: {ex.Message}";
        }
    }

    public void Dispose()
    {
        foreach (ITimer timer in _hideTimers.Values) timer.Dispose();
        _hideTimers.Clear();
    }
}
