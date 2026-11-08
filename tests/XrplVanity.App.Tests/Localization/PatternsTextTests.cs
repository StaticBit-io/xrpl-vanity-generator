using XrplVanity.App.Core.Localization;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.App.Tests.Localization;

public sealed class PatternsTextTests
{
    [Theory]
    [InlineData("Starts", "starts")]
    [InlineData("Ends", "ends")]
    [InlineData("Contains", "contains")]
    [InlineData("Pair", "pair")]
    [InlineData("NearStart", "nearstart")]
    public void Every_Stored_Position_Reads_As_Its_English_Word(string stored, string word)
    {
        Assert.Equal(word, TextCatalog.For(UiLanguage.English).Render(PatternsText.Position(stored)));
    }

    [Fact]
    public void Every_Position_Value_Has_A_Word()
    {
        foreach (MatchPosition position in Enum.GetValues<MatchPosition>())
        {
            Assert.NotEqual(PatternsText.Raw, PatternsText.Position(position.ToString()).Id);
        }
    }

    [Fact]
    public void An_Unknown_Stored_Position_Is_Shown_As_Stored()
    {
        Assert.Equal("Someday", TextCatalog.For(UiLanguage.English).Render(PatternsText.Position("Someday")));
        Assert.Equal("7", TextCatalog.For(UiLanguage.English).Render(PatternsText.Position("7")));
    }
}
