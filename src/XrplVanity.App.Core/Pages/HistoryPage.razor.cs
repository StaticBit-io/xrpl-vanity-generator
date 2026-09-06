using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Pages;

public partial class HistoryPage
{
    private static readonly TimeSpan ClearConfirmWindow = TimeSpan.FromSeconds(5);

    private List<StoredResult> _all = new();
    private string _filter = string.Empty;
    private string? _storeError;
    private DateTimeOffset? _clearArmedAt;
    private ITimer? _clearRevertTimer;
    private int _lastSessionResultCount = -1;
    private volatile bool _disposed;

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

    // The store turns read-only mid-process when DPAPI cannot write results.dat, and then throws
    // on every write. A Blazor event handler is the worst place for that to escape, so both
    // destructive actions report the reason as a banner and re-read whatever is still on disk.
    private void Delete(SessionResult item)
    {
        _storeError = null;
        try
        {
            Store.Delete(item.Address);
        }
        catch (InvalidOperationException ex)
        {
            _storeError = ex.Message;
        }

        Reload();
    }

    private void OnClearClicked()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        if (_clearArmedAt is DateTimeOffset armedAt && now - armedAt <= ClearConfirmWindow)
        {
            _storeError = null;
            try
            {
                Store.Clear();
            }
            catch (InvalidOperationException ex)
            {
                _storeError = ex.Message;
            }

            _clearArmedAt = null;
            StopClearRevertTimer();
            Reload();
        }
        else
        {
            _clearArmedAt = now;
            ArmClearRevertTimer();
        }
    }

    /// <summary>Reverts "Click again to confirm" back to "Clear history" once the confirm window lapses unclicked.</summary>
    private void ArmClearRevertTimer()
    {
        StopClearRevertTimer();
        _clearRevertTimer = Clock.CreateTimer(_ =>
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

                    _clearArmedAt = null;
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
        }, null, ClearConfirmWindow, Timeout.InfiniteTimeSpan);
    }

    private void StopClearRevertTimer()
    {
        _clearRevertTimer?.Dispose();
        _clearRevertTimer = null;
    }

    private void Reload()
    {
        _all = Store.LoadAll().OrderByDescending(r => r.FoundAt).ToList();
        _lastSessionResultCount = Vm.Results.Count;
    }

    public override void Dispose()
    {
        _disposed = true;
        StopClearRevertTimer();
        base.Dispose();
    }
}
