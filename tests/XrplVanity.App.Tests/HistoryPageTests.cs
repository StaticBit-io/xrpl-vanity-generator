using Bunit;
using Microsoft.Extensions.DependencyInjection;
using XrplVanity.App.Core.Pages;
using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class HistoryPageTests : BunitContext
{
    [Fact]
    public void Filters_By_Address_Or_Pattern_And_Deletes_Rows()
    {
        MemoryResultStore store = new();
        store.Append(new StoredResult("rFast1", "s1", "fast", "Starts", DateTimeOffset.UtcNow, "cpu"));
        store.Append(new StoredResult("rSlow2", "s2", "slow", "Starts", DateTimeOffset.UtcNow, "cpu"));
        Services.AddSingleton<IResultStore>(store);
        Services.AddSingleton(new SearchViewModel(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>()));
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        IRenderedComponent<HistoryPage> cut = Render<HistoryPage>();

        Assert.Equal(2, cut.FindAll(".row").Count);
        cut.Find("input.filter").Input("fast");
        Assert.Single(cut.FindAll(".row"));

        cut.Find("input.filter").Input("");
        cut.FindAll("button.delete")[0].Click();
        Assert.Single(store.LoadAll());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".row")));
    }

    [Fact]
    public void A_Read_Only_Store_Shows_The_Reason_And_Hides_Destructive_Buttons()
    {
        MemoryResultStore store = new() { IsReadOnly = true, ReadOnlyReason = "results.dat could not be read" };
        Services.AddSingleton<IResultStore>(store);
        Services.AddSingleton(new SearchViewModel(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>()));
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        IRenderedComponent<HistoryPage> cut = Render<HistoryPage>();
        Assert.Contains("results.dat could not be read", cut.Find(".banner-negative").TextContent);
        Assert.Empty(cut.FindAll("button.clear"));
    }
}
