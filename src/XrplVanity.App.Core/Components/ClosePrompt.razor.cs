using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Components;

public partial class ClosePrompt
{
    private bool _remember;
    private bool _wasVisible;

    [Parameter] public bool Visible { get; set; }
    [Parameter] public EventCallback<(CloseBehavior Choice, bool Remember)> OnChoice { get; set; }

    /// <summary>
    /// The prompt is a single long-lived instance in MainLayout, so a ticked "remember my choice"
    /// that was dismissed rather than acted on would still be ticked the next time it opens.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (Visible && !_wasVisible)
        {
            _remember = false;
        }

        _wasVisible = Visible;
    }
}
