using System.Globalization;
using XrplVanity.App.Core.Localization;
using Xunit;

namespace XrplVanity.App.Tests.Localization;

public sealed class LocalizerTests
{
    private static readonly CultureInfo RuRu = CultureInfo.GetCultureInfo("ru-RU");

    [Fact]
    public void Pick_Prefers_A_Remembered_Language() =>
        Assert.Same(UiLanguage.German, Localizer.Pick("de", RuRu));

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    public void Pick_Ignores_An_Unknown_Remembered_Code(string remembered) =>
        Assert.Same(UiLanguage.Russian, Localizer.Pick(remembered, RuRu));

    [Theory]
    [InlineData("zh-TW", "zh")]
    [InlineData("pt-PT", "pt")]
    [InlineData("es-419", "es")]
    public void Pick_Maps_Regional_Tags(string tag, string code) =>
        Assert.Same(UiLanguage.FromCode(code), Localizer.Pick(null, CultureInfo.GetCultureInfo(tag)));

    [Fact]
    public void Pick_Falls_Back_To_English()
    {
        Assert.Same(UiLanguage.English, Localizer.Pick(null, CultureInfo.GetCultureInfo("fr-FR")));
        Assert.Same(UiLanguage.English, Localizer.Pick(null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Choose_Raises_Changed_Once_And_Only_On_A_Change()
    {
        Localizer localizer = new("en", CultureInfo.InvariantCulture);
        int raised = 0;
        localizer.Changed += () => raised++;

        localizer.Choose(UiLanguage.English);
        Assert.Equal(0, raised);

        localizer.Choose(UiLanguage.Russian);
        Assert.Equal(1, raised);
        Assert.Same(UiLanguage.Russian, localizer.Language);
        Assert.Same(TextCatalog.For(UiLanguage.Russian), localizer.Text);

        localizer.Choose(UiLanguage.Russian);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Transformed_Catalog_Tells_Languages_Apart()
    {
        TextCatalog catalog = TextCatalog.Transformed(UiLanguage.Russian, p => "[ru] " + p);

        Assert.Equal("[ru] Language", catalog[SettingsText.LanguageTitle]);
    }

    [Fact]
    public void Transformed_Catalog_Fills_Placeholders_And_Picks_Plurals()
    {
        TextCatalog source = TextCatalog.FromJson(UiLanguage.English, """
            {
              "T.Greeting": "Hello {name}",
              "T.Files": { "one": "{count} file", "other": "{count} files" }
            }
            """);
        TextCatalog catalog = TextCatalog.Transformed(UiLanguage.Russian, p => "[ru] " + p, source);

        Assert.Equal("[ru] Hello Ann", catalog.Render(UiText.Of(new TextId("T.Greeting"), ("name", "Ann"))));
        Assert.Equal("[ru] 1 file", catalog.Render(UiText.Of(new TextId("T.Files"), ("count", 1))));
        Assert.Equal("[ru] 5 files", catalog.Render(UiText.Of(new TextId("T.Files"), ("count", 5))));
        Assert.Same(UiLanguage.Russian.Culture, catalog.Culture);
    }
}
