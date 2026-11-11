using Microsoft.AspNetCore.Components;
using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.ViewModels;

namespace XrplVanity.App.Core.Pages;

/// <summary>
/// Base for pages bound to the <see cref="SearchViewModel"/>: subscribes to
/// <see cref="SearchViewModel.Changed"/> (raised on worker threads), marshals re-rendering
/// through <c>InvokeAsync</c>, and ignores notifications that arrive after disposal — the
/// event can already be in flight when the page is torn down. Mirrors
/// <see cref="Components.ViewModelComponent"/>'s guard; kept as a separate base because pages
/// inherit <see cref="ComponentBase"/> directly instead of taking <c>Vm</c> as a parameter.
/// </summary>
public abstract class ViewModelPage : ComponentBase, IDisposable
{
    private volatile bool _disposed;

    [CascadingParameter] public TextCatalog? Catalog { get; set; }

    /// <summary>The catalog the shell cascades; English when the component is rendered without a shell.</summary>
    protected TextCatalog T => Catalog ?? TextCatalog.For(UiLanguage.English);

    [Inject] protected SearchViewModel Vm { get; set; } = null!;

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
