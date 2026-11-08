using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.Tutorial;
using Xunit;

namespace XrplVanity.App.Tests;

public sealed class TutorialScriptTests
{
    public static TheoryData<string> AllLanguages() => new(UiLanguage.All.Select(language => language.Code));

    private static string BodyOf(TextCatalog catalog, TutorialStep step) =>
        catalog.Render(TutorialScript.Steps.Single(s => s.Step == step).Body);

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void Every_Step_Names_Buttons_By_Their_Catalog_Label(string code)
    {
        // A transformed catalog tells the languages apart: a body that spelt a button out as its own
        // text would no longer match the label the catalog gives that button.
        UiLanguage language = UiLanguage.FromCode(code)!;
        TextCatalog catalog = TextCatalog.Transformed(language, phrase => $"[{code}] {phrase}");

        Assert.Contains(catalog[SearchText.Start], BodyOf(catalog, TutorialStep.Search));
        string files = BodyOf(catalog, TutorialStep.ListFiles);
        Assert.Contains(catalog[PatternsText.SaveList], files);
        Assert.Contains(catalog[PatternsText.LoadList], files);
        Assert.Contains(catalog[DevicesText.MeasureSpeed], BodyOf(catalog, TutorialStep.Devices));
    }
}
