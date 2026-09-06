using AngleSharp.Dom;
using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.App.Tests;

public class LiveStatsTests : BunitContext
{
    [Fact]
    public void Renders_Totals_And_One_Row_Per_Target_With_Failures()
    {
        SearchProgress progress = new(TimeSpan.FromSeconds(12), 24_000_000, 2_000_000, 3, new[]
        {
            new TargetProgress("cuda:0", "RTX 5070 Ti", TargetState.Running, 24_000_000, 2_000_000, null),
            new TargetProgress("opencl:0", "RTX 5070 Ti", TargetState.Failed, 0, 0, "clBuildProgram failed"),
        });
        IRenderedComponent<LiveStats> cut = Render<LiveStats>(p => p.Add(x => x.Progress, progress));

        Assert.Equal("2 M/s", cut.Find(".total-rate").TextContent.Trim());
        Assert.Equal("24 M", cut.Find(".total-candidates").TextContent.Trim());
        Assert.Equal("12 s", cut.Find(".elapsed").TextContent.Trim());
        Assert.Equal("3", cut.Find(".found").TextContent.Trim());
        IReadOnlyList<IElement> rows = cut.FindAll(".target-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("clBuildProgram failed", rows[1].TextContent);
        Assert.Contains("failed", rows[1].ClassName);
    }

    [Fact]
    public void Without_Progress_The_Tiles_Show_Dashes_And_Keep_Their_Height()
    {
        IRenderedComponent<LiveStats> cut = Render<LiveStats>(p => p.Add(x => x.Progress, (SearchProgress?)null));
        Assert.Equal("—", cut.Find(".total-rate").TextContent.Trim());
        Assert.Equal(4, cut.FindAll(".tile").Count);
    }
}
