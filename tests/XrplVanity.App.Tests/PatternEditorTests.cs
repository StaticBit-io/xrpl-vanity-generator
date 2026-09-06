using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class PatternEditorTests : BunitContext
{
    private static SearchViewModel Vm() => new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());

    [Fact]
    public void Enter_Adds_A_Chip_With_The_Selected_Position_And_Case()
    {
        SearchViewModel vm = Vm();
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));

        cut.Find("select.pos").Change("Contains");
        cut.Find("input.case").Change(false);
        cut.Find("input.pattern").Input("fast");
        cut.Find("input.pattern").KeyDown("Enter");

        PatternChip chip = Assert.Single(vm.Patterns);
        Assert.Equal("fast", chip.Text);
        Assert.Equal(XrplVanity.Core.MatchPosition.Contains, chip.Position);
        Assert.False(chip.IgnoreCase);
        Assert.Contains("fast", cut.Find(".chip").TextContent);
        Assert.Equal(string.Empty, cut.Find("input.pattern").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void An_Unreachable_Chip_Shows_Its_Warning()
    {
        SearchViewModel vm = Vm();
        // "@" is not in the XRPL base58 alphabet and stays unreachable after normalization
        // (unlike "0", which the normalizer folds onto "o" and which would then be reachable).
        vm.AddPattern("@", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        Assert.Equal("cannot occur in an XRPL address", cut.Find(".chip-warn").TextContent.Trim());
    }

    [Fact]
    public void The_Remove_Button_Drops_The_Chip()
    {
        SearchViewModel vm = Vm();
        vm.AddPattern("ab", XrplVanity.Core.MatchPosition.Starts, false);
        IRenderedComponent<PatternEditor> cut = Render<PatternEditor>(p => p.Add(x => x.Vm, vm));
        cut.Find(".chip button.remove").Click();
        Assert.Empty(vm.Patterns);
        Assert.Empty(cut.FindAll(".chip"));
    }
}
