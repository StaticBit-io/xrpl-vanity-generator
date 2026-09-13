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

    /// <summary>
    /// One pattern text at two positions and a paired pattern: the column shows those as three
    /// different cells, so the filter that narrows the column has to offer them as three choices.
    /// </summary>
    private static void SeedMixed(MemoryResultStore store)
    {
        store.Append(new StoredResult("rFast1", "s1", "fast", "Starts", Newest, "cpu"));
        store.Append(new StoredResult("rSlow2", "s2", "slow", "Starts", Newest.AddMinutes(-1), "cpu"));
        store.Append(new StoredResult("rFast3", "s3", "fast", "Ends", Newest.AddMinutes(-2), "cpu"));
        store.Append(new StoredResult("rPair4", "s4", "abc:xyz", "Pair", Newest.AddMinutes(-3), "gpu"));
    }

    [Fact]
    public void The_Pattern_Filter_Lists_Each_Pattern_And_Position_Once_With_Its_Count()
    {
        MemoryResultStore store = new();
        SeedMixed(store);
        store.Append(new StoredResult("rFast5", "s5", "fast", "Starts", Newest.AddMinutes(-4), "cpu"));
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        string[] options = cut.FindAll("select.pattern-filter option").Select(o => o.TextContent.Trim()).ToArray();

        Assert.Equal(
            new[] { "All patterns — 5", "abc:xyz (pair) — 1", "fast (ends) — 1", "fast (starts) — 2", "slow (starts) — 1" },
            options);
    }

    [Fact]
    public void Choosing_A_Pattern_Narrows_The_List_To_That_Pattern_At_That_Position()
    {
        MemoryResultStore store = new();
        SeedMixed(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.Find("select.pattern-filter").Change("Starts|fast");

        string address = Assert.Single(cut.FindAll(".result-row .address")).TextContent.Trim();
        Assert.Equal("rFast1", address);   // rFast3 carries the same text at the other position
    }

    [Fact]
    public void The_Pattern_And_Text_Filters_Narrow_Together()
    {
        MemoryResultStore store = new();
        SeedMixed(store);
        store.Append(new StoredResult("rFast5", "s5", "fast", "Starts", Newest.AddMinutes(-4), "cpu"));
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.Find("select.pattern-filter").Change("Starts|fast");
        Assert.Equal(2, cut.FindAll(".result-row").Count);

        cut.Find("input.filter").Input("rFast5");

        string address = Assert.Single(cut.FindAll(".result-row .address")).TextContent.Trim();
        Assert.Equal("rFast5", address);
    }

    /// <summary>
    /// Same invariant as the text filter: the selection is held by address against every stored
    /// row, so narrowing the column hides rows without quietly unpicking them.
    /// </summary>
    [Fact]
    public void A_Selection_Survives_The_Pattern_Filter_Hiding_Its_Rows()
    {
        MemoryResultStore store = new();
        SeedMixed(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.Find("input.pick-all").Change(true);
        cut.Find("select.pattern-filter").Change("Starts|fast");
        Assert.Single(cut.FindAll(".result-row"));

        cut.Find("button.delete-selected").Click();
        Assert.Contains("Delete 4 results?", cut.Find(".modal-box h3").TextContent);
    }

    /// <summary>
    /// Deleting the last row of the chosen pattern removes that option, and a select pointing at
    /// an option that no longer exists would leave the page showing an empty list with no way
    /// back except guessing which control emptied it.
    /// </summary>
    [Fact]
    public void Deleting_The_Last_Row_Of_The_Chosen_Pattern_Falls_Back_To_All_Patterns()
    {
        MemoryResultStore store = new();
        SeedMixed(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.Find("select.pattern-filter").Change("Starts|slow");
        cut.Find("button.delete").Click();
        cut.Find("button.confirm").Click();

        Assert.Equal(3, cut.FindAll(".result-row").Count);
        Assert.DoesNotContain(cut.FindAll("select.pattern-filter option"), o => o.TextContent.Contains("slow"));
    }

    [Fact]
    public void An_Empty_History_Has_No_Pattern_Filter()
    {
        IRenderedComponent<HistoryPage> cut = RenderPage(new MemoryResultStore());

        Assert.Empty(cut.FindAll("select.pattern-filter"));
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
        // Everything selected is gone, so the selection and its bar go with it.
        Assert.Empty(cut.FindAll(".sel-bar"));
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
        // The clear variant deliberately lists no addresses: it is not a manifest of everything stored.
        Assert.Empty(cut.FindAll(".modal-box .what"));
        Assert.Equal(2, store.LoadAll().Count);
        Assert.Equal("Clear history", cut.Find("button.clear").TextContent.Trim());

        cut.Find("button.confirm").Click();
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void An_Open_Dialog_Makes_The_Page_Behind_It_Inert()
    {
        // Otherwise Clear history stays reachable by keyboard from inside the delete dialog.
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        Assert.False(cut.Find("header.page-head").HasAttribute("inert"));

        cut.FindAll("button.delete")[0].Click();

        Assert.True(cut.Find("header.page-head").HasAttribute("inert"));
        Assert.True(cut.Find("div.content-scroll").HasAttribute("inert"));

        cut.Find("button.cancel").Click();

        Assert.False(cut.Find("header.page-head").HasAttribute("inert"));
    }

    [Fact]
    public void An_Empty_History_Offers_Nothing_To_Clear()
    {
        IRenderedComponent<HistoryPage> cut = RenderPage(new MemoryResultStore());

        Assert.Empty(cut.FindAll("button.clear"));
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
    [Fact]
    public void The_Qr_Dialog_Opens_From_A_Row_And_Makes_The_Page_Inert()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.FindAll("button.show-qr")[0].Click();

        Assert.Single(cut.FindAll(".qr-box"));
        Assert.True(cut.Find("header.page-head").HasAttribute("inert"));
        Assert.True(cut.Find(".content-scroll").HasAttribute("inert"));

        cut.Find("button.close").Click();

        Assert.Empty(cut.FindAll(".qr-box"));
        Assert.False(cut.Find("header.page-head").HasAttribute("inert"));
    }

    [Fact]
    public void The_Selection_Bar_Opens_The_Dialog_With_Every_Selected_Row()
    {
        MemoryResultStore store = new();
        SeedTwo(store);
        IRenderedComponent<HistoryPage> cut = RenderPage(store);

        cut.FindAll("input.pick")[0].Change(true);
        cut.FindAll("input.pick")[1].Change(true);
        cut.Find("button.show-qr-selected").Click();

        // Two wallets: the backup format is the only one a single code can carry.
        Assert.True(cut.Find("button.format-seed").HasAttribute("disabled"));
    }
}
