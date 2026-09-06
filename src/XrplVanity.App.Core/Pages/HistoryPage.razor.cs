using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Pages;

public partial class HistoryPage
{
    private static readonly TimeSpan ClearConfirmWindow = TimeSpan.FromSeconds(5);

    private List<StoredResult> _all = new();
    private string _filter = string.Empty;
    private DateTimeOffset? _clearArmedAt;

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
            Reload();
        }
        else
        {
            _clearArmedAt = now;
        }
    }

    private void Reload() => _all = Store.LoadAll().OrderByDescending(r => r.FoundAt).ToList();
}
