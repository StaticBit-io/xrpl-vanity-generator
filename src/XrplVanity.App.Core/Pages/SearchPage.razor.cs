namespace XrplVanity.App.Core.Pages;

public partial class SearchPage
{
    private bool _clearPending;
    private IReadOnlyList<ViewModels.SessionResult>? _qrRows;

    private string ClearTitle
    {
        get
        {
            int count = Vm.Patterns.Count;
            return count == 1 ? "Clear 1 pattern?" : $"Clear {count} patterns?";
        }
    }

    private static string ClearMessage =>
        "The pattern set is emptied. Anything saved with save list… is untouched, and a search already running is not affected.";

    private void ConfirmClear()
    {
        _clearPending = false;
        Vm.ClearPatterns();
    }
}
