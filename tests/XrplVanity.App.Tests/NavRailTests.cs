using AngleSharp.Dom;
using Bunit;
using XrplVanity.App.Core.Layout;
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
}
