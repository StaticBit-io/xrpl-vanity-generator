using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace XrplVanity.App.Core.Components;

public partial class ConfirmDialog
{
    /// <summary>Longer selections are summarised: the dialog is a check, not a manifest.</summary>
    private const int MaxListedAddresses = 10;

    private ElementReference _box;
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
    /// already pressing Enter must not delete anything. Escape reaches the handler by bubbling
    /// from whatever inside the box has focus, so the box itself is the fallback target.
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
                // Cancel is gone or unfocusable. Fall back to the box, or Escape would do nothing.
                try
                {
                    await _box.FocusAsync();
                }
                catch (InvalidOperationException)
                {
                    // The dialog closed in the same batch; nothing to focus.
                }
            }
        }
        else if (!Visible)
        {
            _wasVisible = false;
        }
    }
}
