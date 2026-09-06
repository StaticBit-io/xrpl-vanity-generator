using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
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

        Assert.Equal(2, cut.FindAll(".result-row").Count);
        cut.Find("input.filter").Input("fast");
        Assert.Single(cut.FindAll(".result-row"));

        cut.Find("input.filter").Input("");
        cut.FindAll("button.delete")[0].Click();
        Assert.Single(store.LoadAll());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".result-row")));
    }

    [Fact]
    public void Clear_Confirmation_Reverts_To_Clear_History_After_The_Confirm_Window_Elapses()
    {
        MemoryResultStore store = new();
        store.Append(new StoredResult("rFast1", "s1", "fast", "Starts", DateTimeOffset.UtcNow, "cpu"));
        Services.AddSingleton<IResultStore>(store);
        Services.AddSingleton(new SearchViewModel(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>()));
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        FakeTimeProvider time = new();
        Services.AddSingleton<TimeProvider>(time);
        IRenderedComponent<HistoryPage> cut = Render<HistoryPage>();

        cut.Find("button.clear").Click();
        Assert.Equal("Click again to confirm", cut.Find("button.clear").TextContent.Trim());

        time.Advance(TimeSpan.FromSeconds(6));

        cut.WaitForAssertion(() => Assert.Equal("Clear history", cut.Find("button.clear").TextContent.Trim()));
        Assert.Single(store.LoadAll());   // reverted, not cleared
    }

    [Fact]
    public void A_Store_That_Throws_On_Delete_Or_Clear_Shows_A_Banner_Instead_Of_Crashing()
    {
        MemoryResultStore store = new() { ReadOnlyReason = "results.dat could not be written" };
        store.Append(new StoredResult("rFast1", "s1", "fast", "Starts", DateTimeOffset.UtcNow, "cpu"));
        Services.AddSingleton<IResultStore>(store);
        Services.AddSingleton(new SearchViewModel(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>()));
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        IRenderedComponent<HistoryPage> cut = Render<HistoryPage>();

        store.ThrowOnWrite = true;   // DPAPI stops being able to write while the page is open

        cut.FindAll("button.delete")[0].Click();
        Assert.Contains("results.dat could not be written", cut.Find(".banner-negative").TextContent);
        Assert.Single(cut.FindAll(".result-row"));   // nothing was deleted, and the list reloaded

        cut.Find("button.clear").Click();
        cut.Find("button.clear").Click();
        Assert.Contains("results.dat could not be written", cut.Find(".banner-negative").TextContent);
        Assert.Single(store.LoadAll());
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
