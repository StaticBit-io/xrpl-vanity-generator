using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Components;

public partial class ResultsList : IDisposable
{
    private readonly HashSet<string> _revealed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _animated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITimer> _hideTimers = new(StringComparer.Ordinal);
    private IReadOnlyList<SessionResult>? _lastItems;
    private List<SessionResult> _items = new();
    private string? _exportError;
    private volatile bool _disposed;

    [Parameter, EditorRequired] public IReadOnlyList<SessionResult> Items { get; set; } = Array.Empty<SessionResult>();
    [Parameter] public bool ShowTarget { get; set; }
    [Parameter] public EventCallback<SessionResult> OnDelete { get; set; }
    [Parameter] public int RevealSeconds { get; set; } = 15;
    [Parameter] public int ClipboardClearSeconds { get; set; } = 30;

    protected override void OnParametersSet()
    {
        _items = Items.ToList();

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

            _animated.Clear();
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
        _hideTimers[address] = Clock.CreateTimer(_ =>
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

                    _revealed.Remove(address);
                    StopTimer(address);
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
        }, null, TimeSpan.FromSeconds(RevealSeconds), Timeout.InfiniteTimeSpan);
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
            // Items is newest-first in the app; export chronologically (oldest-first), matching
            // the order the CLI's --out would have written.
            TsvExport.Write(path, Items.OrderBy(i => i.FoundAt).Select(i => (i.Address, i.Seed)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _exportError = $"Export failed: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (ITimer timer in _hideTimers.Values) timer.Dispose();
        _hideTimers.Clear();
    }
}
