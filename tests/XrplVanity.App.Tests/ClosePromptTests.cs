using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.Services;
using Xunit;

namespace XrplVanity.App.Tests;

public class ClosePromptTests : BunitContext
{
    [Fact]
    public void Reports_The_Choice_And_The_Remember_Flag()
    {
        (CloseBehavior Choice, bool Remember)? received = null;
        IRenderedComponent<ClosePrompt> cut = Render<ClosePrompt>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnChoice, v => received = v));

        cut.Find("input.remember").Change(true);
        cut.Find("button.tray").Click();

        Assert.Equal((CloseBehavior.MinimizeToTray, true), received);
    }

    [Fact]
    public void Hidden_Prompt_Renders_Nothing()
    {
        IRenderedComponent<ClosePrompt> cut = Render<ClosePrompt>(p => p.Add(x => x.Visible, false));
        Assert.Empty(cut.FindAll(".modal-box"));
    }
}
