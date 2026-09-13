using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Components;

public partial class ResultsList : IDisposable
{
    private readonly HashSet<string> _revealed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _animated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITimer> _hideTimers = new(StringComparer.Ordinal);

    // The selection holds the rows themselves, not just their addresses: a row the host's filter
    // hides is no longer in Items, and the bar promises to export and delete it all the same.
    private readonly Dictionary<string, SessionResult> _selected = new(StringComparer.Ordinal);
    private List<SessionResult> _items = new();
    private string? _exportError;
    private volatile bool _disposed;

    [Parameter, EditorRequired] public IReadOnlyList<SessionResult> Items { get; set; } = Array.Empty<SessionResult>();
    [Parameter] public bool ShowTarget { get; set; }
    [Parameter] public EventCallback<SessionResult> OnDelete { get; set; }
    [Parameter] public int RevealSeconds { get; set; } = 15;
    [Parameter] public int ClipboardClearSeconds { get; set; } = 30;
    [Parameter] public bool Selectable { get; set; }

    /// <summary>
    /// Every address the host holds, filter or no filter. Supplied by a host that filters what it
    /// passes in <see cref="Items"/> (HistoryPage), so the selection is pruned against what still
    /// exists rather than against what happens to be on screen. Null means Items is the whole
    /// universe — the Search page's case, where the two are the same thing.
    /// </summary>
    [Parameter] public IReadOnlyCollection<string>? KnownAddresses { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<SessionResult>> OnDeleteSelected { get; set; }

    /// <summary>
    /// Asked to show these rows as a QR code. The dialog belongs to the page: everything inside
    /// .content-scroll is covered by the inert attribute the page sets, so a dialog rendered from
    /// here would go inert along with the page behind it.
    /// </summary>
    [Parameter] public EventCallback<IReadOnlyList<SessionResult>> OnShowQr { get; set; }

    protected override void OnParametersSet()
    {
        _items = Items.ToList();

        // SearchViewModel.Results/HistoryPage.Filtered() hand back a fresh collection instance on
        // every access (including one with the exact same rows, e.g. an unrelated progress tick),
        // so reference equality on Items is not a reliable "new session" signal. Instead, prune
        // per-address state to whatever addresses are still present: an address that stays in
        // Items keeps its revealed/hide-timer/animated state across re-renders (no re-masking, no
        // replayed slide-in), while one that dropped out (deleted row, or a genuinely new session
        // that cleared the list) is cleaned up, including disposing its now-orphaned hide timer.
        HashSet<string> present = new(_items.Select(i => i.Address), StringComparer.Ordinal);

        List<string>? stale = null;
        foreach (string address in _revealed)
        {
            if (!present.Contains(address))
            {
                (stale ??= new List<string>()).Add(address);
            }
        }

        if (stale is not null)
        {
            foreach (string address in stale)
            {
                _revealed.Remove(address);
                StopTimer(address);
            }
        }

        _animated.IntersectWith(present);
        PruneSelection(present);
    }

    /// <summary>
    /// Prunes the selection the same way, but against a wider universe when the host supplies one:
    /// a filtered host removes rows from <see cref="Items"/> for reasons that have nothing to do
    /// with the row still existing, so pruning against the visible rows would quietly unpick
    /// everything the user typed past. Reveal state can be pruned against the visible rows because
    /// hiding a seed costs nothing; a lost selection costs the user the work of making it again.
    /// </summary>
    private void PruneSelection(HashSet<string> present)
    {
        if (_selected.Count == 0)
        {
            return;
        }

        HashSet<string> universe = KnownAddresses is null
            ? present
            : new HashSet<string>(KnownAddresses, StringComparer.Ordinal);

        List<string>? gone = null;
        foreach (string address in _selected.Keys)
        {
            if (!universe.Contains(address))
            {
                (gone ??= new List<string>()).Add(address);
            }
        }

        if (gone is null)
        {
            return;
        }

        foreach (string address in gone)
        {
            _selected.Remove(address);
        }
    }

    private bool AllPicked => _items.Count > 0 && _items.All(i => _selected.ContainsKey(i.Address));

    /// <summary>Some but not all of the visible rows: the checkbox wears a dash rather than a tick.</summary>
    private bool SomePicked => !AllPicked && _items.Any(i => _selected.ContainsKey(i.Address));

    private string PickAllLabel => SomePicked
        ? $"Select all shown; {_items.Count(i => _selected.ContainsKey(i.Address))} of {_items.Count} are selected"
        : "Select all shown";

    private void Pick(SessionResult item, bool picked)
    {
        if (picked)
        {
            _selected[item.Address] = item;
        }
        else
        {
            _selected.Remove(item.Address);
        }
    }

    /// <summary>
    /// "Select all shown" is additive in both directions — it adds the visible rows, and unticking
    /// it removes only those, leaving whatever the filter is hiding still selected. Emptying the
    /// selection outright is <see cref="ClearSelection"/>, wired to the bar's own button.
    /// </summary>
    private void PickAllShown(bool picked)
    {
        foreach (SessionResult item in _items)
        {
            if (picked)
            {
                _selected[item.Address] = item;
            }
            else
            {
                _selected.Remove(item.Address);
            }
        }
    }

    private void ClearSelection() => _selected.Clear();

    /// <summary>
    /// The selected rows in the order <see cref="Items"/> uses (newest first), including the ones
    /// the host's filter is currently hiding — so the bar's count is exactly what gets exported and
    /// exactly what gets deleted.
    /// </summary>
    private List<SessionResult> SelectedItems() => _selected.Values.OrderByDescending(i => i.FoundAt).ToList();

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

    /// <summary>Takes the rows to write so the header button and the selection bar share one picker, one write and one error path.</summary>
    private async Task Export(IReadOnlyList<SessionResult> rows)
    {
        _exportError = null;
        try
        {
            // The picker itself can fail (no owner window yet, a shell extension refusing the
            // dialog), so it belongs inside the same guard as the write.
            string? path = await Dialog.PickSaveAsync("xrpl-vanity.tsv", "Tab-separated values", ".tsv");
            if (path is null) return;

            // Items is newest-first in the app; export chronologically (oldest-first), matching
            // the order the CLI's --out would have written.
            TsvExport.Write(path, rows.OrderBy(i => i.FoundAt).Select(i => (i.Address, i.Seed)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
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
