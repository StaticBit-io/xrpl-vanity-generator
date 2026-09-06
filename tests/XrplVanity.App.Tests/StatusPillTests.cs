using Bunit;
using XrplVanity.App.Core.Components;
using Xunit;

namespace XrplVanity.App.Tests;

public class StatusPillTests : BunitContext
{
    [Theory]
    [InlineData(SearchState.Idle, "Idle", false)]
    [InlineData(SearchState.Starting, "Starting", true)]
    [InlineData(SearchState.Searching, "Searching", true)]
    [InlineData(SearchState.Stopping, "Stopping", false)]
    public void Renders_The_State_Text_And_Breathes_Only_While_Active(SearchState state, string text, bool breathes)
    {
        IRenderedComponent<StatusPill> cut = Render<StatusPill>(p => p.Add(x => x.State, state));

        Assert.Equal(text, cut.Find(".pill").TextContent.Trim());
        Assert.Equal(breathes, cut.Find(".pill").ClassList.Contains("breathe"));
    }
}
