using AngleSharp.Dom;
using Bunit;
using XrplVanity.App.Core.Layout;
using XrplVanity.App.Core.Localization;
using Xunit;

namespace XrplVanity.App.Tests;

public class NavRailTests : BunitContext
{
    [Fact]
    public void Renders_Three_Nav_Anchors_With_The_Root_Active_By_Default()
    {
        // BunitNavigationManager (registered by BunitContext) starts at http://localhost/, so the
        // "Search" link (href="", NavLinkMatch.All) is the one that should carry "active".
        IRenderedComponent<NavRail> cut = Render<NavRail>();

        IReadOnlyList<IElement> anchors = cut.FindAll("a");
        Assert.Equal(3, anchors.Count);

        foreach (IElement anchor in anchors)
        {
            Assert.Contains("nav", anchor.ClassList);
        }

        Assert.Contains("active", anchors[0].ClassList);
        Assert.DoesNotContain("active", anchors[1].ClassList);
        Assert.DoesNotContain("active", anchors[2].ClassList);
    }

    [Fact]
    public void Nav_Labels_Follow_The_Language()
    {
        TextCatalog german = TextCatalog.Transformed(UiLanguage.German, phrase => "[de] " + phrase);

        IRenderedComponent<NavRail> cut = Render<NavRail>(p => p.AddCascadingValue(german));

        IReadOnlyList<IElement> labels = cut.FindAll("a span");
        Assert.Equal(3, labels.Count);
        foreach (IElement label in labels)
        {
            Assert.StartsWith("[de] ", label.TextContent, StringComparison.Ordinal);
        }
    }
}
