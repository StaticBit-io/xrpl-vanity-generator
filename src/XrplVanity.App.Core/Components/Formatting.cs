using System.Globalization;
using XrplVanity.App.Core.Localization;

namespace XrplVanity.App.Core.Components;

/// <summary>
/// Numbers and durations for the stat tiles. Digits, grouping and the decimal separator follow the
/// catalog's language; the SI suffixes and "/s" are symbols and stay as they are.
/// </summary>
public static class Formatting
{
    public static string Candidates(double value, TextCatalog text)
    {
        CultureInfo culture = text.Culture;
        if (value >= 1e21) return value.ToString("0.#e0", culture);
        if (value >= 1e18) return (value / 1e18).ToString("0.#", culture) + " E";
        if (value >= 1e15) return (value / 1e15).ToString("0.#", culture) + " P";
        if (value >= 1e12) return (value / 1e12).ToString("0.#", culture) + " T";
        if (value >= 1e9) return (value / 1e9).ToString("0.#", culture) + " G";
        if (value >= 1e6) return (value / 1e6).ToString("0.#", culture) + " M";
        return Math.Round(value).ToString("N0", culture);
    }

    /// <summary>A first tick with zero elapsed time divides into infinity; that shows the no-value dash, not "∞ G/s".</summary>
    public static string Rate(double perSecond, TextCatalog text) =>
        double.IsFinite(perSecond) ? Candidates(perSecond, text) + "/s" : text[CommonText.NoValue];

    public static string Duration(TimeSpan? value, TextCatalog text)
    {
        if (value is not TimeSpan t) return text[CommonText.NoValue];
        if (t.TotalDays > 365) return text[CommonText.MoreThanAYear];
        if (t.TotalDays >= 1) return text.Render(UiText.Of(CommonText.Days, ("count", t.TotalDays)));
        if (t.TotalHours >= 1) return text.Render(UiText.Of(CommonText.Hours, ("hours", (int)t.TotalHours), ("minutes", t.Minutes)));
        if (t.TotalMinutes >= 1) return text.Render(UiText.Of(CommonText.Minutes, ("minutes", (int)Math.Round(t.TotalMinutes))));
        double seconds = Math.Round(t.TotalSeconds);
        if (t > TimeSpan.Zero && seconds < 1) return text[CommonText.LessThanASecond];
        return text.Render(UiText.Of(CommonText.Seconds, ("seconds", (int)seconds)));
    }
}
