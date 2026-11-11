using XrplVanity.App.Core.Tutorial;
using Xunit;

namespace XrplVanity.App.Tests;

public class TutorialPlacementTests
{
    private const double BubbleWidth = 320;
    private const double BubbleHeight = 200;

    [Fact]
    public void Prefers_The_Right_Of_The_Target()
    {
        TutorialRect placed = TutorialPlacement.Place(One(new TutorialRect(100, 100, 200, 100)), BubbleWidth, BubbleHeight, 1100, 720);

        Assert.Equal(new TutorialRect(312, 100, BubbleWidth, BubbleHeight), placed);
    }

    [Fact]
    public void Falls_Back_To_Left_Then_Below_Then_Above()
    {
        TutorialRect left = TutorialPlacement.Place(One(new TutorialRect(700, 100, 300, 100)), BubbleWidth, BubbleHeight, 1100, 720);
        Assert.Equal(new TutorialRect(368, 100, BubbleWidth, BubbleHeight), left);

        TutorialRect below = TutorialPlacement.Place(One(new TutorialRect(100, 100, 950, 100)), BubbleWidth, BubbleHeight, 1100, 720);
        Assert.Equal(new TutorialRect(100, 212, BubbleWidth, BubbleHeight), below);

        TutorialRect above = TutorialPlacement.Place(One(new TutorialRect(100, 450, 950, 250)), BubbleWidth, BubbleHeight, 1100, 720);
        Assert.Equal(new TutorialRect(100, 238, BubbleWidth, BubbleHeight), above);
    }

    [Theory]
    [InlineData(1100, 720, 0, 0)]
    [InlineData(1100, 720, 900, 0)]
    [InlineData(1100, 720, 0, 620)]
    [InlineData(1100, 720, 900, 620)]
    [InlineData(800, 500, 0, 0)]
    [InlineData(800, 500, 600, 0)]
    [InlineData(800, 500, 0, 400)]
    [InlineData(800, 500, 600, 400)]
    public void Never_Leaves_The_Viewport(double viewWidth, double viewHeight, double left, double top)
    {
        AssertInside(TutorialPlacement.Place(One(new TutorialRect(left, top, 200, 100)), BubbleWidth, BubbleHeight, viewWidth, viewHeight), viewWidth, viewHeight);
    }

    [Theory]
    [InlineData(1100, 720)]
    [InlineData(800, 500)]
    public void Never_Leaves_The_Viewport_When_The_Target_Fills_It(double viewWidth, double viewHeight)
    {
        TutorialRect placed = TutorialPlacement.Place(One(new TutorialRect(0, 0, viewWidth, viewHeight)), BubbleWidth, BubbleHeight, viewWidth, viewHeight);

        AssertInside(placed, viewWidth, viewHeight);
    }

    [Fact]
    public void Covers_The_Least_Of_The_Target_When_No_Side_Has_Room()
    {
        // Only a 100 px strip at the right is free of the target; the bubble overlaps it least there.
        TutorialRect placed = TutorialPlacement.Place(One(new TutorialRect(0, 0, 1000, 720)), BubbleWidth, BubbleHeight, 1100, 720);

        Assert.Equal(1100 - BubbleWidth - TutorialPlacement.Margin, placed.Left);
    }

    [Fact]
    public void Several_Lit_Regions_Are_Judged_One_By_One_Not_As_A_Union()
    {
        // The Search step at 1100x720: the Start button at the top right, Patterns and Estimate in the
        // left column, Stats and Results across the bottom. The right column is unlit and free.
        TutorialRect start = new(900, 18, 178, 40);
        TutorialRect patterns = new(86, 70, 534, 170);
        TutorialRect estimate = new(86, 252, 534, 90);
        TutorialRect stats = new(86, 354, 992, 80);
        TutorialRect results = new(86, 446, 992, 256);
        TutorialRect[] lit = { start, patterns, estimate, stats, results };

        TutorialRect placed = TutorialPlacement.Place(lit, BubbleWidth, BubbleHeight, 1100, 720);

        AssertInside(placed, 1100, 720);
        foreach (TutorialRect region in lit)
        {
            Assert.False(Overlaps(placed, region), $"covers {region}");
        }

        Assert.True(placed.Left >= patterns.Right, "should sit in the unlit right column");
    }

    [Fact]
    public void Covers_The_Least_Of_The_Lit_Regions_When_Every_Side_Covers_Something()
    {
        TutorialRect[] lit = { new(0, 0, 1100, 300), new(0, 320, 1100, 400) };

        TutorialRect placed = TutorialPlacement.Place(lit, BubbleWidth, BubbleHeight, 1100, 720);

        AssertInside(placed, 1100, 720);
        // The 20 px gap between the regions is too small for the bubble; straddling them covers
        // less than parking it wholly on one.
        double covered = lit.Sum(region => Math.Max(0, Math.Min(placed.Right, region.Right) - Math.Max(placed.Left, region.Left))
            * Math.Max(0, Math.Min(placed.Bottom, region.Bottom) - Math.Max(placed.Top, region.Top)));
        Assert.True(covered < BubbleWidth * BubbleHeight, $"covered {covered}");
    }

    [Fact]
    public void With_No_Target_Sits_At_The_Top_Centre()
    {
        TutorialRect placed = TutorialPlacement.Place(Array.Empty<TutorialRect>(), BubbleWidth, BubbleHeight, 1100, 720);

        Assert.Equal(new TutorialRect(390, TutorialPlacement.Margin, BubbleWidth, BubbleHeight), placed);
    }

    private static TutorialRect[] One(TutorialRect rect) => new[] { rect };

    private static void AssertInside(TutorialRect placed, double viewWidth, double viewHeight)
    {
        Assert.True(placed.Left >= TutorialPlacement.Margin, $"left {placed.Left}");
        Assert.True(placed.Top >= TutorialPlacement.Margin, $"top {placed.Top}");
        Assert.True(placed.Right <= viewWidth - TutorialPlacement.Margin, $"right {placed.Right}");
        Assert.True(placed.Bottom <= viewHeight - TutorialPlacement.Margin, $"bottom {placed.Bottom}");
    }

    private static bool Overlaps(TutorialRect a, TutorialRect b) =>
        Math.Min(a.Right, b.Right) > Math.Max(a.Left, b.Left) && Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Top, b.Top);
}
