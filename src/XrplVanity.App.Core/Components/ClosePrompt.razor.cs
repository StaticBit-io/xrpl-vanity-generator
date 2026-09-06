using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Components;

public partial class ClosePrompt
{
    private bool _remember;

    [Parameter] public bool Visible { get; set; }
    [Parameter] public EventCallback<(CloseBehavior Choice, bool Remember)> OnChoice { get; set; }
}
