using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.Tutorial;

namespace XrplVanity.App.Core.Pages;

public partial class SearchPage
{
    private bool _clearPending;
    private TutorialStep? _clearStep;
    private IReadOnlyList<ViewModels.SessionResult>? _qrRows;
    private bool _qrOpenedInSandbox;
    private volatile bool _tutorialDisposed;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Tutorial.Changed += OnTutorialChanged;
    }

    // Tutorial.Changed is raised on worker threads, like the view model's, so the work is marshalled.
    private void OnTutorialChanged()
    {
        if (_tutorialDisposed)
        {
            return;
        }

        try
        {
            _ = InvokeAsync(() =>
            {
                if (_tutorialDisposed)
                {
                    return;
                }

                CloseDialogsThatOutliveTheirStep();
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

    /// <summary>
    /// The tutorial's bubble sits above the page's dialogs, so a step can be left behind an open one.
    /// The clear dialog belongs to the step it was opened on, and a QR dialog opened on the practice
    /// result belongs to the tutorial; neither may be confirmed after that is over, because confirming
    /// would act on the user's own words or show a seed the tutorial has already dropped.
    /// </summary>
    private void CloseDialogsThatOutliveTheirStep()
    {
        if (_clearPending && Tutorial.Current?.Step != _clearStep)
        {
            CloseClear();
        }

        if (_qrRows is not null && _qrOpenedInSandbox && !Tutorial.IsActive)
        {
            _qrRows = null;
            _qrOpenedInSandbox = false;
        }
    }

    private void OpenClear()
    {
        _clearStep = Tutorial.Current?.Step;
        _clearPending = true;
        Tutorial.SetPageDialogOpen(true);
    }

    private void CloseClear()
    {
        _clearPending = false;
        _clearStep = null;
        Tutorial.SetPageDialogOpen(false);
    }

    public override void Dispose()
    {
        _tutorialDisposed = true;
        Tutorial.Changed -= OnTutorialChanged;
        if (_clearPending)
        {
            CloseClear();
        }

        base.Dispose();
    }

    private string ClearTitle => T.Render(UiText.Of(SearchText.ClearTitle, ("count", Vm.Patterns.Count)));

    private string ClearMessage => T[SearchText.ClearMessage];

    // The tutorial's bubble would cover the QR dialog, so on that step its text goes into the dialog.
    private string? QrNote =>
        Tutorial.Current is { Step: TutorialStep.QrDialog } step ? step.Body : null;

    // ResultsList raises OnShowQr from its row button and its selection bar; both land here.
    private void ShowQr(IReadOnlyList<ViewModels.SessionResult> rows)
    {
        _qrRows = rows;
        _qrOpenedInSandbox = Vm.IsSandbox;
        Tutorial.QrOpened();
    }

    private void CloseQr()
    {
        _qrRows = null;
        _qrOpenedInSandbox = false;
        Tutorial.QrClosed();
    }

    private void ConfirmClear()
    {
        if (!_clearPending)
        {
            return;
        }

        CloseClear();
        Vm.ClearPatterns();
    }
}
