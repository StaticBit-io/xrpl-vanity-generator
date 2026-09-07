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
    // Distinct timestamps, newest first: the page orders by FoundAt, so pinning them keeps
    // "the first row is rFast1" a fact rather than a race between two UtcNow reads.
    private static readonly DateTimeOffset Newest = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private IRenderedComponent<HistoryPage> RenderPage(MemoryResultStore store)
    {
        Services.AddSingleton<IResultStore>(store);
        Services.AddSingleton(new SearchViewModel(new MemorySettingsStore(), store, new FakeNotifications(), () => Array.Empty<XrplVanity.Search.DeviceCatalogEntry>()));
        Services.AddSingleton<IClipboardService>(new FakeClipboard());
        Services.AddSingleton<IFileDialogService>(new FakeFileDialog());
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        return Render<HistoryPage>();
    }

    private static void SeedTwo(MemoryResultStore store)
    {
        store.Append(new StoredResult("rFast1", "s1", "fast", "Starts", Newest, "cpu"));
        store.Append(new StoredResult("rSlow2", "s2", "slow", "Starts", Newest.AddMinutes(-1), "cpu"));
    }

    [Fact]
    public void Filters_By_Address_Or_Pattern_And_Deletes_Rows()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        Assert.Equal(2, cut.FindAll(".result-row").Count);
        cut.Find("input.filter").Input("fast");
        Assert.Single(cut.FindAll(".result-row"));

        cut.Find("input.filter").Input("");
        cut.FindAll("button.delete")[0].Click();
        cut.Find("button.confirm").Click();
        Assert.Single(store.LoadAll());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".result-row")));
    }

    [Fact]
    public void Deleting_A_Row_Asks_First_And_Cancelling_Keeps_It()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.FindAll("button.delete")[0].Click();

        Assert.Contains("Delete this result?", cut.Find(".modal-box h3").TextContent);
        Assert.Contains("rFast1", cut.Find(".modal-box .what").TextContent);
        Assert.Equal(2, store.LoadAll().Count);

        cut.Find("button.cancel").Click();

        Assert.Empty(cut.FindAll(".modal-box"));
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void Confirming_A_Row_Delete_Removes_Exactly_That_Row()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.FindAll("button.delete")[0].Click();
        cut.Find("button.confirm").Click();

        StoredResult remaining = Assert.Single(store.LoadAll());
        Assert.Equal("rSlow2", remaining.Address);
        Assert.Empty(cut.FindAll(".modal-box"));
    }

    [Fact]
    public void Deleting_A_Selection_Goes_Through_The_Bulk_Write()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.Find("input.pick-all").Change(true);
        cut.Find("button.delete-selected").Click();

        Assert.Contains("Delete 2 results?", cut.Find(".modal-box h3").TextContent);
        Assert.Equal("Delete 2", cut.Find("button.confirm").TextContent.Trim());

        cut.Find("button.confirm").Click();

        Assert.Empty(store.LoadAll());
        Assert.Equal(1, store.DeleteManyCalls);
    }

    /// <summary>
    /// The page hands the list every stored address, not the filtered ones, so a row the filter
    /// hides stays selected — the dialog's count and the addresses actually deleted agree.
    /// </summary>
    [Fact]
    public void A_Selection_Survives_The_Filter_Hiding_Its_Rows()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.Find("input.pick-all").Change(true);
        cut.Find("input.filter").Input("fast");
        Assert.Single(cut.FindAll(".result-row"));

        cut.Find("button.delete-selected").Click();
        Assert.Contains("Delete 2 results?", cut.Find(".modal-box h3").TextContent);
        cut.Find("button.confirm").Click();

        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Clear_History_Asks_Once_Instead_Of_Arming()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.Find("button.clear").Click();

        Assert.Contains("Clear the whole history?", cut.Find(".modal-box h3").TextContent);
        Assert.Equal(2, store.LoadAll().Count);
        Assert.Equal("Clear history", cut.Find("button.clear").TextContent.Trim());

        cut.Find("button.confirm").Click();
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void A_Store_That_Throws_On_Delete_Or_Clear_Shows_A_Banner_Instead_Of_Crashing()
    {
        MemoryResultStore store = new() { ReadOnlyReason = "results.dat could not be written" };
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        store.ThrowOnWrite = true;   // DPAPI stops being able to write while the page is open

        cut.FindAll("button.delete")[0].Click();
        cut.Find("button.confirm").Click();
        Assert.Contains("results.dat could not be written", cut.Find(".banner-negative").TextContent);
        Assert.Equal(2, cut.FindAll(".result-row").Count);   // nothing was deleted, and the list reloaded

        cut.Find("button.clear").Click();
        cut.Find("button.confirm").Click();
        Assert.Contains("results.dat could not be written", cut.Find(".banner-negative").TextContent);
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void A_Failing_Bulk_Delete_Shows_The_Reason_Instead_Of_Crashing()
    {
        MemoryResultStore store = new() { ReadOnlyReason = "results.dat is read-only" };
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        store.ThrowOnWrite = true;

        cut.Find("input.pick-all").Change(true);
        cut.Find("button.delete-selected").Click();
        cut.Find("button.confirm").Click();

        Assert.Contains("read-only", cut.Find(".banner-negative").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void A_Read_Only_Store_Shows_The_Reason_And_Hides_Destructive_Buttons()
    {
        MemoryResultStore store = new() { IsReadOnly = true, ReadOnlyReason = "results.dat could not be read" };
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        Assert.Contains("results.dat could not be read", cut.Find(".banner-negative").TextContent);
        Assert.Empty(cut.FindAll("button.clear"));
        Assert.Empty(cut.FindAll("button.delete"));

        // Reading and exporting stay safe, so the checkbox column and the bar's export survive.
        Assert.Equal(2, cut.FindAll("input.pick").Count);
        cut.Find("input.pick-all").Change(true);
        Assert.NotNull(cut.Find("button.export-selected"));
        Assert.Empty(cut.FindAll("button.delete-selected"));
    }
}
