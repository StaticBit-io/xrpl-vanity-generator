namespace XrplVanity.App.Core.Localization;

/// <summary>A CLDR plural category, as a catalog names it: <c>one</c>, <c>few</c>, <c>many</c>, <c>other</c>.</summary>
public enum PluralForm
{
    One,
    Few,
    Many,
    Other,
}

/// <summary>
/// The CLDR cardinal rules of the six languages, for integers -- every count the application shows is
/// a whole number; a fractional count takes <c>other</c> in every language (see TextCatalog). The compact-exponent <c>many</c> of Spanish and Portuguese ("1 M de archivos") never
/// arises from a plain integer and is not used.
/// </summary>
public static class PluralRules
{
    /// <summary>The forms a plural phrase in <paramref name="language"/> must carry, no more and no fewer.</summary>
    public static IReadOnlyList<PluralForm> FormsOf(UiLanguage language) => language.Code switch
    {
        "ru" => [PluralForm.One, PluralForm.Few, PluralForm.Many, PluralForm.Other],
        "zh" => [PluralForm.Other],
        _ => [PluralForm.One, PluralForm.Other],
    };

    public static PluralForm FormOf(UiLanguage language, long count)
    {
        long n = Math.Abs(count);
        return language.Code switch
        {
            "ru" => Russian(n),
            "zh" => PluralForm.Other,

            // Brazilian Portuguese counts 0 with 1: "0 arquivo", like French, unlike Portugal.
            "pt" => n <= 1 ? PluralForm.One : PluralForm.Other,
            _ => n == 1 ? PluralForm.One : PluralForm.Other,
        };
    }

    private static PluralForm Russian(long n)
    {
        long lastDigit = n % 10;
        long lastTwo = n % 100;
        if (lastDigit == 1 && lastTwo != 11) return PluralForm.One;
        if (lastDigit is >= 2 and <= 4 && lastTwo is < 12 or > 14) return PluralForm.Few;
        return PluralForm.Many;
    }
}
