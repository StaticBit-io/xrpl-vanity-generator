using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Tutorial;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.App.Tests;

public class TutorialRegionTests : BunitContext
{
    private (SearchViewModel Vm, TutorialService Tutorial) Register()
    {
        MemoryResultStore store = new();
        SearchViewModel vm = new(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        TutorialService tutorial = new(vm, store);
        Services.AddSingleton(vm);
        Services.AddSingleton(tutorial);
        return (vm, tutorial);
    }

    private IRenderedComponent<TutorialRegion> RenderRegion(string name) =>
        Render<TutorialRegion>(p => p.Add(x => x.Name, name).AddChildContent("<span class=\"inside\">content</span>"));

    [Fact]
    public void Region_Is_Plain_When_No_Tutorial_Runs()
    {
        Register();

        IRenderedComponent<TutorialRegion> cut = RenderRegion(TutorialRegions.Patterns);

        AngleSharp.Dom.IElement region = cut.Find("div");
        Assert.Equal(TutorialRegions.Patterns, region.GetAttribute("data-tutorial"));
        Assert.Equal(new[] { "data-tutorial" }, region.Attributes.Select(a => a.Name).ToArray());
        Assert.Equal("content", region.QuerySelector(".inside")!.TextContent);
    }

    [Fact]
    public void Unlit_Region_Is_Inert_And_Dimmed()
    {
        (SearchViewModel _, TutorialService tutorial) = Register();
        Assert.True(tutorial.Begin());   // AddWord lights only the patterns

        AngleSharp.Dom.IElement region = RenderRegion(TutorialRegions.Devices).Find("div");

        Assert.True(region.HasAttribute("inert"));
        Assert.Contains("tutorial-dimmed", region.ClassList);
        Assert.DoesNotContain("tutorial-lit", region.ClassList);
    }

    [Fact]
    public void Lit_Region_On_An_Interactive_Step_Is_Usable()
    {
        (SearchViewModel _, TutorialService tutorial) = Register();
        Assert.True(tutorial.Begin());

        AngleSharp.Dom.IElement region = RenderRegion(TutorialRegions.Patterns).Find("div");

        Assert.False(region.HasAttribute("inert"));
        Assert.Contains("tutorial-lit", region.ClassList);
        Assert.DoesNotContain("tutorial-dimmed", region.ClassList);
    }

    [Fact]
    public void Lit_Region_On_An_Explaining_Step_Is_Inert()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        TutorialFixtures.ReachResult(vm, tutorial);   // Result lights the results and only explains them

        AngleSharp.Dom.IElement region = RenderRegion(TutorialRegions.Results).Find("div");

        Assert.True(region.HasAttribute("inert"));
        Assert.Contains("tutorial-lit", region.ClassList);
        Assert.DoesNotContain("tutorial-dimmed", region.ClassList);
    }

    [Fact]
    public void Region_Follows_The_Step_Without_A_Parent_Render()
    {
        (SearchViewModel vm, TutorialService tutorial) = Register();
        IRenderedComponent<TutorialRegion> devices = RenderRegion(TutorialRegions.Devices);
        Assert.Empty(devices.Find("div").ClassList);

        Assert.True(tutorial.Begin());
        devices.WaitForAssertion(() => Assert.Contains("tutorial-dimmed", devices.Find("div").ClassList));

        Assert.True(vm.AddPattern("abcdefhjkm", MatchPosition.Starts, true));   // AddWord -> ListFiles -> Next -> Devices
        tutorial.Next();
        devices.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement region = devices.Find("div");
            Assert.Contains("tutorial-lit", region.ClassList);
            Assert.False(region.HasAttribute("inert"));
        });

        tutorial.Leave();
        devices.WaitForAssertion(() => Assert.Equal(new[] { "data-tutorial" }, devices.Find("div").Attributes.Select(a => a.Name).ToArray()));
    }
}
