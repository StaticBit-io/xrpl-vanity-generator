using Microsoft.AspNetCore.Components;

namespace XrplVanity.App.Core.Localization;

/// <summary>
/// Base for components that show copy: <see cref="T"/> is the catalog the shell cascades. A component
/// rendered without a shell (a unit test, say) speaks English.
/// </summary>
public abstract class LocalizedComponent : ComponentBase
{
    [CascadingParameter] public TextCatalog? Catalog { get; set; }

    protected TextCatalog T => Catalog ?? TextCatalog.For(UiLanguage.English);
}
