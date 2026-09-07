using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Pages;

public partial class HistoryPage
{
    private List<StoredResult> _all = new();
    private string[] _knownAddresses = Array.Empty<string>();
    private string _filter = string.Empty;
    private string? _storeError;
    private int _lastSessionResultCount = -1;
    private PendingAction? _pending;

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

    private List<SessionResult> Filtered()
    {
        IEnumerable<StoredResult> matches = _filter.Length == 0
            ? _all
            : _all.Where(r => r.Address.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || r.Pattern.Contains(_filter, StringComparison.OrdinalIgnoreCase));

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
        => _pending = new PendingAction(
            "Delete this result?",
            "Its seed is deleted with it. results.dat is the only place the seed exists, so this cannot be undone.",
            "Delete",
            new[] { item.Address },
            () => Write(() => Store.Delete(item.Address)));

    private void AskDeleteSelected(IReadOnlyList<SessionResult> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        string[] addresses = items.Select(i => i.Address).ToArray();
        _pending = new PendingAction(
            $"Delete {addresses.Length} result{(addresses.Length == 1 ? string.Empty : "s")}?",
            "Their seeds are deleted with them. results.dat is the only place those seeds exist, so this cannot be undone.",
            $"Delete {addresses.Length}",
            addresses,
            () => Write(() => Store.DeleteMany(addresses)));
    }

    private void AskClear()
        => _pending = new PendingAction(
            "Clear the whole history?",
            $"All {_all.Count} results and their seeds are removed from results.dat. This cannot be undone, and the filter does not narrow it — everything goes.",
            "Clear history",
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
            _storeError = ex.Message;
        }

        Reload();
    }

    private void Reload()
    {
        _all = Store.LoadAll().OrderByDescending(r => r.FoundAt).ToList();

        // The list prunes its selection against this, not against the filtered rows it is shown,
        // so typing in the filter never silently unpicks anything.
        _knownAddresses = _all.Select(r => r.Address).ToArray();
        _lastSessionResultCount = Vm.Results.Count;
    }

    /// <summary>What a confirmed dialog will do. Null while no dialog is open.</summary>
    private sealed record PendingAction(string Title, string Message, string ConfirmLabel,
        IReadOnlyList<string>? Addresses, Action Execute);
}
