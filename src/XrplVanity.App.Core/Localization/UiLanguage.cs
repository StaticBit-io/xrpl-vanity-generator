using System.Globalization;

namespace XrplVanity.App.Core.Localization;

/// <summary>
/// One of the languages the application speaks: the project site's set, in the site's menu order
/// (<c>site/src/js/languages.mjs</c>), so the site, the element and the application offer the same choice.
/// </summary>
public sealed class UiLanguage
{
    private UiLanguage(string code, string nativeName, string htmlTag, string cultureName)
    {
        Code = code;
        NativeName = nativeName;
        HtmlTag = htmlTag;
        Culture = CultureInfo.GetCultureInfo(cultureName);
    }

    /// <summary>The primary subtag that names the language and its catalog: <c>en</c>, <c>ru</c>.</summary>
    public string Code { get; }

    /// <summary>The language's name in itself; never translated, so a person finds their own language in any UI.</summary>
    public string NativeName { get; }

    /// <summary>The BCP 47 tag for <c>&lt;html lang&gt;</c>. Chinese carries its script, because Simplified is what the catalog writes.</summary>
    public string HtmlTag { get; }

    /// <summary>
    /// The locale numbers are formatted in -- the site's choice per language: <c>en-GB</c>, <c>ru-RU</c>,
    /// <c>de-DE</c>, <c>es-ES</c>, <c>pt-BR</c>, <c>zh-Hans-CN</c>.
    /// </summary>
    public CultureInfo Culture { get; }

    public static UiLanguage English { get; } = new("en", "English", "en", "en-GB");

    public static UiLanguage Russian { get; } = new("ru", "Русский", "ru", "ru-RU");

    public static UiLanguage German { get; } = new("de", "Deutsch", "de", "de-DE");

    public static UiLanguage Spanish { get; } = new("es", "Español", "es", "es-ES");

    public static UiLanguage Portuguese { get; } = new("pt", "Português", "pt", "pt-BR");

    public static UiLanguage Chinese { get; } = new("zh", "中文", "zh-Hans", "zh-Hans-CN");

    /// <summary>Every language, in the order the picker lists them.</summary>
    public static IReadOnlyList<UiLanguage> All { get; } = [English, Russian, German, Spanish, Portuguese, Chinese];

    /// <summary>The language whose code is exactly <paramref name="code"/>, or null.</summary>
    public static UiLanguage? FromCode(string? code) =>
        All.FirstOrDefault(language => string.Equals(language.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The language a BCP 47 tag belongs to by its primary subtag (<c>zh-TW</c>, <c>zh-Hans</c> and
    /// <c>zh</c> are all Chinese; <c>pt-PT</c> is Portuguese; <c>es-419</c> is Spanish), or null when
    /// the application has no such language. The site's <c>langFromTag</c>, for the same answers.
    /// </summary>
    public static UiLanguage? FromTag(string? tag)
    {
        string primary = (tag ?? string.Empty).Trim().Split('-', '_')[0];
        return primary.Length == 0 ? null : FromCode(primary);
    }

    public override string ToString() => Code;
}
