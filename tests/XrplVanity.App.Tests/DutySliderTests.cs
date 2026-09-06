using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class DutySliderTests : BunitContext
{
    [Fact]
    public void Moving_The_Slider_Sets_The_Duty_And_Shows_The_Percentage()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>());
        IRenderedComponent<DutySlider> cut = Render<DutySlider>(p => p.Add(x => x.Vm, vm));
        Assert.Equal("100%", cut.Find(".duty-value").TextContent.Trim());
        cut.Find("input[type=range]").Change(80);
        Assert.Equal(80, vm.DutyPercent);
        Assert.Equal("80%", cut.Find(".duty-value").TextContent.Trim());
    }
}
