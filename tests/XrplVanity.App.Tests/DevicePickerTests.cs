using AngleSharp.Dom;
using Bunit;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using XrplVanity.Gpu;
using XrplVanity.Search;
using Xunit;

namespace XrplVanity.App.Tests;

public class DevicePickerTests : BunitContext
{
    [Fact]
    public void Lists_Cpu_First_Then_Devices_With_Their_Tags_And_Toggles_Selection()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => new[]
        {
            new DeviceCatalogEntry(new DeviceSelector("cuda", 0), "RTX 5070 Ti", "sm_120", false),
            new DeviceCatalogEntry(new DeviceSelector("opencl", 1), "pocl", "cpu", true),
        });
        IRenderedComponent<DevicePicker> cut = Render<DevicePicker>(p => p.Add(x => x.Vm, vm));

        IReadOnlyList<IElement> rows = cut.FindAll(".target");
        Assert.Equal(3, rows.Count);
        Assert.Contains("CPU", rows[0].TextContent);
        Assert.Contains("RTX 5070 Ti", rows[1].TextContent);
        Assert.Contains("[cpu]", rows[2].TextContent);
        Assert.Single(cut.FindAll("input.threads"));   // only the CPU row has the thread slider

        // Two OpenCL devices of one platform share a name, so every non-CPU row carries its id.
        IReadOnlyList<IElement> ids = cut.FindAll(".device-id");
        Assert.Equal(new[] { "cuda:0", "opencl:1" }, ids.Select(i => i.TextContent.Trim()));

        cut.FindAll("input.select")[1].Change(true);
        Assert.True(vm.Targets[1].Selected);
    }

    [Fact]
    public void Without_Devices_The_No_Gpu_Banner_Appears()
    {
        SearchViewModel vm = new(new MemorySettingsStore(), new MemoryResultStore(), new FakeNotifications(), () => Array.Empty<DeviceCatalogEntry>());
        IRenderedComponent<DevicePicker> cut = Render<DevicePicker>(p => p.Add(x => x.Vm, vm));
        Assert.Contains("No CUDA driver or OpenCL platform found", cut.Find(".banner-yellow").TextContent);
    }
}
