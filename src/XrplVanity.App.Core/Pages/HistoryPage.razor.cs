using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Pages;

public partial class HistoryPage
{
    private static readonly TimeSpan ClearConfirmWindow = TimeSpan.FromSeconds(5);

    private List<StoredResult> _all = new();
    private string _filter = string.Empty;
    private DateTimeOffset? _clearArmedAt;
    private ITimer? _clearRevertTimer;
    private volatile bool _disposed;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Reload();
    }

    private List<SessionResult> Filtered()
    {
        IEnumerable<StoredResult> matches = _filter.Length == 0
            ? _all
            : _all.Where(r => r.Address.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || r.Pattern.Contains(_filter, StringComparison.OrdinalIgnoreCase));

        return matches.Select(r => new SessionResult(r.Address, r.Seed, r.Pattern, r.Position, r.FoundAt, r.Target)).ToList();
    }

    private void Delete(SessionResult item)
    {
        Store.Delete(item.Address);
        Reload();
    }

    private void OnClearClicked()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        if (_clearArmedAt is DateTimeOffset armedAt && now - armedAt <= ClearConfirmWindow)
        {
            Store.Clear();
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

    private void Reload() => _all = Store.LoadAll().OrderByDescending(r => r.FoundAt).ToList();

    public override void Dispose()
    {
        _disposed = true;
        StopClearRevertTimer();
        base.Dispose();
    }
}
