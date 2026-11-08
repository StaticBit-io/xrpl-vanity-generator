using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Localization;
using Xunit;

namespace XrplVanity.App.Tests.Localization;

public sealed class FormattingTests
{
    private static TextCatalog Catalog(UiLanguage language) => TextCatalog.For(language);

    public static TheoryData<string> AllLanguages() => new(UiLanguage.All.Select(language => language.Code));

    [Theory]
    [InlineData("en", "1.5 G/s")]
    [InlineData("ru", "1,5 G/s")]
    [InlineData("de", "1,5 G/s")]
    public void Rate_Uses_The_Locale_Decimal_Separator(string code, string expected) =>
        Assert.Equal(expected, Formatting.Rate(1.5e9, Catalog(UiLanguage.FromCode(code)!)));

    [Theory]
    [InlineData("en", "12,345")]
    [InlineData("ru", "12 345")]
    [InlineData("de", "12.345")]
    public void Candidates_Groups_Small_Numbers_By_Locale(string code, string expected) =>
        Assert.Equal(expected, Formatting.Candidates(12345, Catalog(UiLanguage.FromCode(code)!)));

    [Fact]
    public void Duration_Words_Come_From_The_Catalog() =>
        Assert.Equal("1 h 30 min", Formatting.Duration(TimeSpan.FromMinutes(90), Catalog(UiLanguage.English)));

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void Infinite_And_NaN_Rates_Show_The_No_Value_Text(string code)
    {
        TextCatalog catalog = Catalog(UiLanguage.FromCode(code)!);
        foreach (double value in new[] { double.PositiveInfinity, double.NaN })
        {
            string shown = Formatting.Rate(value, catalog);
            Assert.Equal(catalog[CommonText.NoValue], shown);
            Assert.DoesNotContain("∞", shown, StringComparison.Ordinal);
            Assert.DoesNotContain("NaN", shown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_Fractional_Count_Takes_The_Other_Plural_Form()
    {
        TextCatalog english = Catalog(UiLanguage.English);
        Assert.Equal("1.5 days", english.Render(UiText.Of(CommonText.Days, ("count", 1.5))));
        Assert.Equal("1 day", english.Render(UiText.Of(CommonText.Days, ("count", 1.0))));
        Assert.Equal("1.5 days", english.Render(UiText.Of(CommonText.Days, ("count", 1.5m))));
        Assert.Equal("1.5 days", english.Render(UiText.Of(CommonText.Days, ("count", 1.5f))));
    }

    [Fact]
    public void A_Formatted_Placeholder_Uses_The_Catalog_Culture_Not_The_Ambient_One()
    {
        CultureInfoScope.Run("en-US", () =>
            Assert.Equal("1,5 G/s", Formatting.Rate(1.5e9, Catalog(UiLanguage.Russian))));
        CultureInfoScope.Run("de-DE", () =>
            Assert.Equal("1.5 days", Catalog(UiLanguage.English).Render(UiText.Of(CommonText.Days, ("count", 1.5)))));
    }

    [Fact]
    public void Russian_Days_Render_The_Decimal_Comma() =>
        Assert.Equal("1,5 days", Catalog(UiLanguage.Russian).Render(UiText.Of(CommonText.Days, ("count", 1.5))));

    [Fact]
    public void Durations_Cover_Every_Branch_In_English()
    {
        TextCatalog en = Catalog(UiLanguage.English);
        Assert.Equal("-", Formatting.Duration(null, en));
        Assert.Equal("> 1 year", Formatting.Duration(TimeSpan.FromDays(400), en));
        Assert.Equal("2.5 days", Formatting.Duration(TimeSpan.FromHours(60), en));
        Assert.Equal("1 day", Formatting.Duration(TimeSpan.FromDays(1), en));
        Assert.Equal("4 min", Formatting.Duration(TimeSpan.FromMinutes(4.2), en));
        Assert.Equal("12 s", Formatting.Duration(TimeSpan.FromSeconds(12.4), en));
        Assert.Equal("< 1 s", Formatting.Duration(TimeSpan.FromMilliseconds(235), en));
    }
}

internal static class CultureInfoScope
{
    public static void Run(string name, Action action)
    {
        System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(name);
            action();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }
}
