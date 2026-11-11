using System.Globalization;

namespace XrplVanity.App.Core.Localization;

/// <summary>
/// The application's one localization service: which language the screens speak, and the catalog that
/// renders it. A singleton; the shell cascades <see cref="Text"/> and re-renders on <see cref="Changed"/>.
/// It persists nothing: the caller stores <see cref="Language"/> with the rest of the settings.
/// </summary>
public sealed class Localizer
{
    /// <param name="remembered">The language code saved by an earlier run, or null when none was chosen.</param>
    /// <param name="systemCulture">The OS UI culture at start-up, the default when nothing was chosen.</param>
    public Localizer(string? remembered, CultureInfo systemCulture)
    {
        ArgumentNullException.ThrowIfNull(systemCulture);

        Language = Pick(remembered, systemCulture);
        Text = TextCatalog.For(Language);
    }

    public UiLanguage Language { get; private set; }

    public TextCatalog Text { get; private set; }

    public IReadOnlyList<UiLanguage> Choices => UiLanguage.All;

    /// <summary>Raised after the language changed; the handler may run on any thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// The remembered choice when it names a language the application has; else the OS UI culture by
    /// primary subtag, walking up its parents (a custom culture can carry an unknown tag over a known
    /// parent); else English. The invariant culture has an empty name, so it ends the walk at English.
    /// </summary>
    public static UiLanguage Pick(string? remembered, CultureInfo systemCulture)
    {
        ArgumentNullException.ThrowIfNull(systemCulture);

        if (UiLanguage.FromCode(remembered) is { } chosen) return chosen;

        for (CultureInfo culture = systemCulture; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            if (UiLanguage.FromTag(culture.Name) is { } system) return system;
        }

        return UiLanguage.English;
    }

    /// <summary>Switches the screens to <paramref name="language"/>; raises <see cref="Changed"/> only when it differs from the current one.</summary>
    public void Choose(UiLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);

        if (language == Language) return;

        Language = language;
        Text = TextCatalog.For(language);
        Changed?.Invoke();
    }
}
