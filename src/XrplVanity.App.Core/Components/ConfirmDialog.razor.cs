using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace XrplVanity.App.Core.Components;

public partial class ConfirmDialog
{
    /// <summary>Longer selections are summarised: the dialog is a check, not a manifest.</summary>
    public const int MaxListedAddresses = 10;

    private ElementReference _cancel;
    private bool _wasVisible;

    [Parameter] public bool Visible { get; set; }
    [Parameter] public string Title { get; set; } = string.Empty;
    [Parameter] public string Message { get; set; } = string.Empty;
    [Parameter] public IReadOnlyList<string>? Addresses { get; set; }
    [Parameter] public string ConfirmLabel { get; set; } = "Delete";
    [Parameter] public EventCallback OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private Task OnKeyDown(KeyboardEventArgs args)
        => args.Key == "Escape" ? OnCancel.InvokeAsync() : Task.CompletedTask;

    /// <summary>
    /// Focus lands on Cancel, not on the destructive button: a dialog that opens under a finger
    /// already pressing Enter must not delete anything. The box itself is focusable so Escape
    /// reaches the handler even before the user has touched a control.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Visible && !_wasVisible)
        {
            _wasVisible = true;
            try
            {
                await _cancel.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // The element is gone (the dialog closed in the same batch); nothing to focus.
            }
        }
        else if (!Visible)
        {
            _wasVisible = false;
        }
    }
}
