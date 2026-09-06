using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Components;

/// <summary>
/// Base for components bound to the <see cref="SearchViewModel"/>: subscribes to
/// <see cref="SearchViewModel.Changed"/> (raised on worker threads), marshals re-rendering
/// through <c>InvokeAsync</c>, and ignores notifications that arrive after disposal — the
/// event can already be in flight when the component is torn down.
/// </summary>
public abstract class ViewModelComponent : ComponentBase, IDisposable
{
    private volatile bool _disposed;

    [Parameter, EditorRequired] public SearchViewModel Vm { get; set; } = null!;

    protected override void OnInitialized()
    {
        Vm.Changed += OnViewModelChanged;
    }

    /// <summary>Called on the renderer's sync context after every view-model change; override to refresh derived state.</summary>
    protected virtual void OnViewModelChanged()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ = InvokeAsync(() =>
            {
                if (!_disposed)
                {
                    StateHasChanged();
                }
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

    public virtual void Dispose()
    {
        _disposed = true;
        Vm.Changed -= OnViewModelChanged;
    }
}
