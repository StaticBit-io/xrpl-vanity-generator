using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Pages;

public partial class HistoryPage
{
    private List<StoredResult> _all = new();
    private string[] _knownAddresses = Array.Empty<string>();
    private List<PatternOption> _options = new();
    private string _filter = string.Empty;
    private string _patternKey = string.Empty;
    private UiText? _storeError;
    private int _lastSessionResultCount = -1;
    private PendingAction? _pending;
    private IReadOnlyList<SessionResult>? _qrRows;

    /// <summary>The file the storage note names; it is a name on disk, not copy.</summary>
    private const string StorageFileName = "results.dat";

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Reload();
    }

    /// <summary>
    /// A search running behind this page keeps adding to the store, but Changed also fires on
    /// every progress tick — so the store is re-read only when the session actually found
    /// something since the last reload, not four times a second for the whole run.
    /// </summary>
    protected override void OnViewModelChanged()
    {
        if (Vm.Results.Count != _lastSessionResultCount)
        {
            Reload();
        }

        base.OnViewModelChanged();
    }

    /// <summary>
    /// The pattern column shows text and position together, so the filter over it keys on both:
    /// the same word searched as a prefix and as a suffix are two different rows to pick between.
    /// '|' separates them safely — it is neither a base58 character nor part of a pair's
    /// "prefix:suffix" display.
    /// </summary>
    private static string OptionKey(string pattern, string position) => $"{position}|{pattern}";

    private List<SessionResult> Filtered()
    {
        IEnumerable<StoredResult> matches = _all;

        // The two filters narrow together: picking a pattern and typing an address gives the rows
        // that satisfy both, not their union.
        if (_patternKey.Length > 0)
        {
            matches = matches.Where(r => OptionKey(r.Pattern, r.Position) == _patternKey);
        }

        if (_filter.Length > 0)
        {
            matches = matches.Where(r => r.Address.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || r.Pattern.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        }

        return matches.Select(r => new SessionResult(r.Address, r.Seed, r.Pattern, r.Position, r.FoundAt, r.Target)).ToList();
    }

    /// <summary>
    /// A read-only store gets no destructive callbacks at all. The list renders a row's delete
    /// button and the selection bar's "Delete selected" only when a delegate is attached, so both
    /// are absent rather than disabled — nothing the user could do would make the write succeed.
    /// Read on every render because the store turns read-only mid-process, not only at startup.
    /// </summary>
    private EventCallback<SessionResult> DeleteCallback => Store.IsReadOnly
        ? default
        : EventCallback.Factory.Create<SessionResult>(this, AskDelete);

    private EventCallback<IReadOnlyList<SessionResult>> DeleteSelectedCallback => Store.IsReadOnly
        ? default
        : EventCallback.Factory.Create<IReadOnlyList<SessionResult>>(this, AskDeleteSelected);

    private void AskDelete(SessionResult item)
    {
        // Only the address is captured: a pending action must not hold a seed alive.
        string address = item.Address;
        _pending = new PendingAction(
            UiText.Of(HistoryText.DeleteOneTitle),
            () => UiText.Of(HistoryText.DeleteOneMessage),
            UiText.Of(DialogText.Delete),
            new[] { address },
            () => Write(() => Store.Delete(address)));
    }

    private void AskDeleteSelected(IReadOnlyList<SessionResult> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        string[] addresses = items.Select(i => i.Address).ToArray();
        _pending = new PendingAction(
            UiText.Of(HistoryText.DeleteManyTitle, ("count", addresses.Length)),
            () => UiText.Of(HistoryText.DeleteManyMessage),
            UiText.Of(HistoryText.DeleteManyConfirm, ("count", addresses.Length)),
            addresses,
            () => Write(() => Store.DeleteMany(addresses)));
    }

    private void AskClear()
        => _pending = new PendingAction(
            UiText.Of(HistoryText.ClearTitle),
            () => UiText.Of(HistoryText.ClearMessage, ("count", _all.Count)),
            UiText.Of(HistoryText.ClearHistory),
            null,
            () => Write(Store.Clear));

    private void Confirm()
    {
        PendingAction? pending = _pending;
        _pending = null;
        pending?.Execute();
    }

    private void Cancel() => _pending = null;

    // The store turns read-only mid-process when DPAPI cannot write results.dat, and then throws
    // on every write. A Blazor event handler is the worst place for that to escape, so every
    // destructive action reports the reason as a banner and re-reads whatever is still on disk.
    private void Write(Action action)
    {
        _storeError = null;
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            _storeError = UiText.Of(HistoryText.ActionFailed, ("detail", ex.Message));
        }

        Reload();
    }

    private void Reload()
    {
        _all = Store.LoadAll().OrderByDescending(r => r.FoundAt).ToList();

        // The list prunes its selection against this, not against the filtered rows it is shown,
        // so typing in the filter never silently unpicks anything.
        _knownAddresses = _all.Select(r => r.Address).ToArray();

        _options = _all
            .GroupBy(r => (r.Pattern, r.Position))
            .Select(g => new PatternOption(
                OptionKey(g.Key.Pattern, g.Key.Position),
                g.Key.Pattern,
                g.Key.Position,
                g.Count()))
            .OrderBy(o => o.Pattern, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Position, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Deleting the last row of the chosen pattern takes its option with it. A select still
        // pointing at it would leave an empty list and no visible reason for it, so the choice
        // falls back to the whole history.
        if (_patternKey.Length > 0 && !_options.Any(o => o.Key == _patternKey))
        {
            _patternKey = string.Empty;
        }

        _lastSessionResultCount = Vm.Results.Count;
    }

    /// <summary>
    /// What a confirmed dialog will do. Null while no dialog is open. The message is a function
    /// rather than a string because a search running behind the page keeps adding results: the
    /// clear dialog's count must be read at render time, not at the moment it opened.
    /// </summary>
    private sealed record PendingAction(UiText Title, Func<UiText> Message, UiText ConfirmLabel,
        IReadOnlyList<string>? Addresses, Action Execute);

    /// <summary>
    /// One choice in the pattern filter. Its label is built at render time (<see cref="OptionLabel"/>)
    /// so it follows the language; it reads exactly like the cell in the pattern column
    /// ("fast (starts)"). <paramref name="Position"/> is the stored name, and <paramref name="Count"/>
    /// is how many stored rows carry the pair.
    /// </summary>
    private sealed record PatternOption(string Key, string Pattern, string Position, int Count);

    private static UiText OptionLabel(PatternOption option)
        => UiText.Of(HistoryText.PatternOption, ("pattern", option.Pattern), ("position", PatternsText.Position(option.Position)));
}
