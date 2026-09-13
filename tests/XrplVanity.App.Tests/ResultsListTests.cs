using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XrplVanity.App.Core.Components;
using XrplVanity.App.Core.ViewModels;
using Xunit;

namespace XrplVanity.App.Tests;

public class ResultsListTests : BunitContext
{
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeFileDialog _dialog = new();
    private readonly FakeTimeProvider _time = new();

    private static SessionResult R(int i) => new($"rAddr{i}", $"sEdSeed{i}", "ab", "Starts", DateTimeOffset.UnixEpoch, "cpu");

    private void RegisterServices()
    {
        Services.AddSingleton<Core.Services.IClipboardService>(_clipboard);
        Services.AddSingleton<Core.Services.IFileDialogService>(_dialog);
        Services.AddSingleton<TimeProvider>(_time);
    }

    private IRenderedComponent<ResultsList> RenderList(params SessionResult[] items)
    {
        RegisterServices();
        return Render<ResultsList>(p => p.Add(x => x.Items, items).Add(x => x.RevealSeconds, 15).Add(x => x.ClipboardClearSeconds, 30));
    }

    /// <summary>Three rows newest-first, the order both the history store and the session view model hand over.</summary>
    private static SessionResult[] ThreeResults() => new[]
    {
        R(3) with { FoundAt = DateTimeOffset.UnixEpoch.AddMinutes(2) },
        R(2) with { FoundAt = DateTimeOffset.UnixEpoch.AddMinutes(1) },
        R(1) with { FoundAt = DateTimeOffset.UnixEpoch },
    };

    private IRenderedComponent<ResultsList> RenderList(
        SessionResult[] items,
        bool selectable,
        Action<IReadOnlyList<SessionResult>>? onDeleteSelected = null,
        IReadOnlyCollection<string>? knownAddresses = null)
    {
        RegisterServices();
        return Render<ResultsList>(p =>
        {
            p.Add(x => x.Items, items)
                .Add(x => x.Selectable, selectable)
                .Add(x => x.RevealSeconds, 15)
                .Add(x => x.ClipboardClearSeconds, 30);

            if (onDeleteSelected is not null)
            {
                p.Add(x => x.OnDeleteSelected, onDeleteSelected);
            }

            if (knownAddresses is not null)
            {
                p.Add(x => x.KnownAddresses, knownAddresses);
            }
        });
    }

    [Fact]
    public void Seeds_Are_Masked_Until_Revealed_And_Hide_Again_After_The_Timeout()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        Assert.Equal("••••••••", cut.Find(".seed").TextContent.Trim());
        Assert.DoesNotContain("sEdSeed1", cut.Markup);

        cut.Find("button.reveal").Click();
        Assert.Equal("sEdSeed1", cut.Find(".seed").TextContent.Trim());

        _time.Advance(TimeSpan.FromSeconds(16));
        cut.WaitForAssertion(() => Assert.Equal("••••••••", cut.Find(".seed").TextContent.Trim()));
    }

    [Fact]
    public void Copying_A_Seed_Uses_The_Secret_Path_With_The_Configured_Delay()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.copy-seed").Click();
        Assert.Equal("sEdSeed1", _clipboard.Text);
        Assert.Equal(TimeSpan.FromSeconds(30), _clipboard.ClearAfter);

        cut.Find("button.copy-address").Click();
        Assert.Equal("rAddr1", _clipboard.Text);
        Assert.Null(_clipboard.ClearAfter);
    }

    [Fact]
    public void Export_Writes_Rows_Chronologically_Oldest_First()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tsv");
        _dialog.PathToReturn = path;
        try
        {
            SessionResult older = R(1) with { FoundAt = DateTimeOffset.UnixEpoch };
            SessionResult newer = R(2) with { FoundAt = DateTimeOffset.UnixEpoch.AddMinutes(1) };
            // Items is passed newest-first, matching how the live view model exposes results.
            IRenderedComponent<ResultsList> cut = RenderList(newer, older);
            cut.Find("button.export").Click();
            cut.WaitForAssertion(() => Assert.True(File.Exists(path)));
            Assert.Equal("rAddr1\tsEdSeed1\nrAddr2\tsEdSeed2\n", File.ReadAllText(path).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cancelled_Export_Writes_Nothing_And_Shows_No_Error()
    {
        _dialog.PathToReturn = null;
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.export").Click();
        Assert.Empty(cut.FindAll(".export-error"));
    }

    [Fact]
    public void Empty_List_Shows_The_Placeholder()
    {
        IRenderedComponent<ResultsList> cut = RenderList();
        Assert.Contains("No matches yet", cut.Markup);
    }

    [Fact]
    public void Show_Target_Renders_The_Target_Column_And_Marks_The_Row()
    {
        RegisterServices();
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1) })
            .Add(x => x.ShowTarget, true));

        Assert.Contains("has-target", cut.Find(".result-row").ClassName);
        Assert.Equal("cpu", cut.Find(".target").TextContent.Trim());
    }

    [Fact]
    public void Without_Show_Target_The_Row_Has_Neither_Class_Nor_Column()
    {
        RegisterServices();
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1) })
            .Add(x => x.ShowTarget, false));

        Assert.DoesNotContain("has-target", cut.Find(".result-row").ClassName);
        Assert.Empty(cut.FindAll(".target"));
    }

    [Fact]
    public void On_Delete_Renders_The_Delete_Button_And_Invokes_The_Callback_With_The_Item()
    {
        RegisterServices();
        SessionResult item = R(1);
        SessionResult? deleted = null;
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { item })
            .Add(x => x.OnDelete, EventCallback.Factory.Create<SessionResult>(this, r => deleted = r)));

        Assert.Contains("has-delete", cut.Find(".result-row").ClassName);
        cut.Find("button.delete").Click();
        Assert.Equal(item, deleted);
    }

    [Fact]
    public void Without_On_Delete_There_Is_No_Delete_Button()
    {
        RegisterServices();
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p.Add(x => x.Items, new[] { R(1) }));

        Assert.DoesNotContain("has-delete", cut.Find(".result-row").ClassName);
        Assert.Empty(cut.FindAll("button.delete"));
    }

    [Fact]
    public void A_Revealed_Seed_Survives_A_Re_Render_With_A_New_List_Instance_Of_The_Same_Item()
    {
        // SearchViewModel.Results/HistoryPage.Filtered() hand back a fresh collection instance on
        // every access, even when nothing actually changed (e.g. an unrelated progress tick), so
        // ResultsList must key its per-address state off the addresses present in Items, not off
        // whether the Items reference itself changed.
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.reveal").Click();
        Assert.Equal("sEdSeed1", cut.Find(".seed").TextContent.Trim());

        cut.Render(p => p.Add(x => x.Items, new[] { R(1) }));

        Assert.Equal("sEdSeed1", cut.Find(".seed").TextContent.Trim());
    }

    [Fact]
    public void A_Revealed_Seed_Is_Pruned_When_Its_Row_Leaves_The_List()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.reveal").Click();
        Assert.Equal("sEdSeed1", cut.Find(".seed").TextContent.Trim());

        cut.Render(p => p.Add(x => x.Items, Array.Empty<SessionResult>()));

        Assert.Empty(cut.FindAll(".seed"));
        Assert.Contains("No matches yet", cut.Markup);
    }

    [Fact]
    public void Disposing_After_Reveal_Does_Not_Throw_When_The_Timer_Fires()
    {
        IRenderedComponent<ResultsList> cut = RenderList(R(1));
        cut.Find("button.reveal").Click();

        cut.Instance.Dispose();

        _time.Advance(TimeSpan.FromSeconds(16));
    }

    [Fact]
    public void Without_Selectable_There_Are_No_Checkboxes()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults());

        Assert.Empty(cut.FindAll("input.pick"));
        Assert.Empty(cut.FindAll("input.pick-all"));
        Assert.Empty(cut.FindAll(".sel-bar"));
    }

    [Fact]
    public void The_Selection_Row_Is_Reserved_Whether_Or_Not_Anything_Is_Selected()
    {
        // The actions live inside a row that is always there: ticking the first box or clearing
        // the last one must not add or remove a strip and shift the list under the pointer.
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);

        Assert.Single(cut.FindAll(".sel-row"));

        cut.FindAll("input.pick")[0].Change(true);
        Assert.Single(cut.FindAll(".sel-row"));
        Assert.Single(cut.FindAll(".sel-row .sel-bar"));

        cut.FindAll("input.pick")[0].Change(false);
        Assert.Single(cut.FindAll(".sel-row"));
        // Still in the document, holding its box open, so the row's height never changes.
        Assert.True(cut.Find(".sel-row .sel-bar").ClassList.Contains("idle"));
    }

    [Fact]
    public void Picking_A_Row_Shows_The_Bar_With_The_Count()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);

        Assert.True(cut.Find(".sel-bar").ClassList.Contains("idle"));
        cut.FindAll("input.pick")[0].Change(true);

        Assert.False(cut.Find(".sel-bar").ClassList.Contains("idle"));
        Assert.Contains("1 selected", cut.Find(".sel-bar").TextContent);
        Assert.True(cut.FindAll(".result-row")[0].ClassList.Contains("picked"));
    }

    [Fact]
    public void Pick_All_Selects_Every_Visible_Row_And_Clears_Them_Again()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);

        cut.Find("input.pick-all").Change(true);
        Assert.Contains("3 selected", cut.Find(".sel-bar").TextContent);

        cut.Find("input.pick-all").Change(false);
        Assert.True(cut.Find(".sel-bar").ClassList.Contains("idle"));
    }

    [Fact]
    public void Unticking_Select_All_Shown_Keeps_A_Selection_The_Filter_Is_Hiding()
    {
        // "Select all shown" is additive in both directions: it only ever adds or removes the rows
        // currently on screen, so narrowing the filter and unticking it cannot silently drop the
        // rows the filter is hiding. Emptying the whole selection is the bar's own button.
        SessionResult[] all = ThreeResults();
        IRenderedComponent<ResultsList> cut = RenderList(
            all,
            selectable: true,
            knownAddresses: all.Select(r => r.Address).ToArray());
        cut.Find("input.pick-all").Change(true);

        cut.Render(p => p.Add(x => x.Items, new[] { all[0] }));
        cut.Find("input.pick-all").Change(false);

        Assert.Contains("2 selected", cut.Find(".sel-bar").TextContent);
    }

    [Fact]
    public void A_Partial_Selection_Marks_The_Select_All_Box_Without_Ticking_It()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);

        cut.FindAll("input.pick")[0].Change(true);

        Assert.True(cut.Find("label.pick-all-label").ClassList.Contains("partial"));
        Assert.False(cut.Find("input.pick-all").HasAttribute("checked"));
        Assert.Contains("1 of 3", cut.Find("input.pick-all").GetAttribute("aria-label"));

        cut.Find("input.pick-all").Change(true);

        Assert.False(cut.Find("label.pick-all-label").ClassList.Contains("partial"));
    }

    [Fact]
    public void Clear_Selection_Empties_The_Selection()
    {
        SessionResult[] all = ThreeResults();
        IRenderedComponent<ResultsList> cut = RenderList(
            all,
            selectable: true,
            knownAddresses: all.Select(r => r.Address).ToArray());
        cut.Find("input.pick-all").Change(true);
        cut.Render(p => p.Add(x => x.Items, new[] { all[0] }));

        cut.Find("button.clear-selection").Click();

        Assert.True(cut.Find(".sel-bar").ClassList.Contains("idle"));
    }

    [Fact]
    public void A_Selected_Row_That_Leaves_The_List_Is_Dropped_From_The_Selection()
    {
        // No KnownAddresses: the Search page hands over everything it has, so the visible rows are
        // the whole universe and a row that leaves them is genuinely gone.
        SessionResult[] all = ThreeResults();
        IRenderedComponent<ResultsList> cut = RenderList(all, selectable: true);
        cut.Find("input.pick-all").Change(true);

        cut.Render(p => p.Add(x => x.Items, new[] { all[0] }));

        Assert.Contains("1 selected", cut.Find(".sel-bar").TextContent);
    }

    [Fact]
    public void An_Empty_Universe_Drops_The_Whole_Selection()
    {
        // KnownAddresses empty is a cleared store, not "no universe supplied" — nothing survives.
        SessionResult[] all = ThreeResults();
        IRenderedComponent<ResultsList> cut = RenderList(all, selectable: true, knownAddresses: all.Select(r => r.Address).ToArray());
        cut.Find("input.pick-all").Change(true);

        cut.Render(p => p
            .Add(x => x.Items, Array.Empty<SessionResult>())
            .Add(x => x.KnownAddresses, Array.Empty<string>()));

        Assert.Empty(cut.FindAll(".sel-bar"));
    }

    [Fact]
    public void A_Selection_Hidden_By_The_Filter_Is_Still_Counted_And_Still_Deleted()
    {
        // HistoryPage passes only the rows its filter lets through, plus the unfiltered universe as
        // KnownAddresses. A hidden row must stay selected, stay in the count, and still be handed to
        // the delete callback — otherwise the bar would promise more than it deletes.
        IReadOnlyList<SessionResult>? received = null;
        SessionResult[] all = ThreeResults();
        IRenderedComponent<ResultsList> cut = RenderList(
            all,
            selectable: true,
            onDeleteSelected: v => received = v,
            knownAddresses: all.Select(r => r.Address).ToArray());
        cut.Find("input.pick-all").Change(true);

        cut.Render(p => p.Add(x => x.Items, new[] { all[1] }));

        Assert.Contains("3 selected", cut.Find(".sel-bar").TextContent);
        cut.Find("button.delete-selected").Click();
        Assert.Equal(all.Select(r => r.Address).ToArray(), received!.Select(r => r.Address).ToArray());
    }

    [Fact]
    public void A_Selected_Row_That_Leaves_Known_Addresses_Is_Dropped_From_The_Selection()
    {
        SessionResult[] all = ThreeResults();
        IRenderedComponent<ResultsList> cut = RenderList(
            all,
            selectable: true,
            knownAddresses: all.Select(r => r.Address).ToArray());
        cut.Find("input.pick-all").Change(true);

        // all[2] was deleted elsewhere — gone from the store, so it leaves the selection. all[1] is
        // only filtered out of sight, so it stays: the two cases must not be confused.
        cut.Render(p => p
            .Add(x => x.Items, new[] { all[0] })
            .Add(x => x.KnownAddresses, new[] { all[0].Address, all[1].Address }));

        Assert.Contains("2 selected", cut.Find(".sel-bar").TextContent);
    }

    [Fact]
    public void Export_Selected_Writes_Only_The_Selected_Rows()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tsv");
        _dialog.PathToReturn = path;
        try
        {
            SessionResult[] all = ThreeResults();
            IRenderedComponent<ResultsList> cut = RenderList(all, selectable: true);
            cut.FindAll("input.pick")[0].Change(true);
            cut.FindAll("input.pick")[2].Change(true);

            cut.Find("button.export-selected").Click();

            cut.WaitForAssertion(() => Assert.True(File.Exists(path)));
            string[] lines = File.ReadAllLines(path);
            // Two of the three rows, oldest-first, matching what the CLI's --out would write.
            Assert.Equal(2, lines.Length);
            Assert.StartsWith(all[2].Address + "\t", lines[0]);
            Assert.StartsWith(all[0].Address + "\t", lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_Header_Export_Is_Disabled_While_A_Selection_Exists()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);
        Assert.False(cut.Find("button.export").HasAttribute("disabled"));

        cut.FindAll("input.pick")[0].Change(true);

        Assert.True(cut.Find("button.export").HasAttribute("disabled"));
    }

    [Fact]
    public void Delete_Selected_Hands_Back_Exactly_The_Selected_Items()
    {
        IReadOnlyList<SessionResult>? received = null;
        SessionResult[] all = ThreeResults();
        IRenderedComponent<ResultsList> cut = RenderList(all, selectable: true, onDeleteSelected: v => received = v);
        cut.FindAll("input.pick")[0].Change(true);
        cut.FindAll("input.pick")[2].Change(true);

        cut.Find("button.delete-selected").Click();

        Assert.Equal(new[] { all[0].Address, all[2].Address }, received!.Select(r => r.Address).ToArray());
    }

    [Fact]
    public void Without_A_Delete_Selected_Callback_The_Bar_Offers_Only_Export()
    {
        IRenderedComponent<ResultsList> cut = RenderList(ThreeResults(), selectable: true);
        cut.FindAll("input.pick")[0].Change(true);

        Assert.Empty(cut.FindAll("button.delete-selected"));
        Assert.Single(cut.FindAll("button.export-selected"));
    }
    [Fact]
    public void A_Row_Asks_The_Host_To_Show_Its_Own_Wallet()
    {
        RegisterServices();
        List<SessionResult>? asked = null;
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1), R(2) })
            .Add(x => x.OnShowQr, (IReadOnlyList<SessionResult> rows) => asked = rows.ToList()));

        cut.FindAll("button.show-qr")[1].Click();

        Assert.Equal("rAddr2", Assert.Single(asked!).Address);
    }

    [Fact]
    public void The_Selection_Bar_Asks_For_Every_Selected_Row()
    {
        RegisterServices();
        List<SessionResult>? asked = null;
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1), R(2) })
            .Add(x => x.Selectable, true)
            .Add(x => x.OnShowQr, (IReadOnlyList<SessionResult> rows) => asked = rows.ToList()));

        cut.FindAll("input.pick")[0].Change(true);
        cut.FindAll("input.pick")[1].Change(true);
        cut.Find("button.show-qr-selected").Click();

        Assert.Equal(2, asked!.Count);
    }

    [Fact]
    public void Without_A_Qr_Callback_Neither_Button_Is_Rendered()
    {
        RegisterServices();
        IRenderedComponent<ResultsList> cut = Render<ResultsList>(p => p
            .Add(x => x.Items, new[] { R(1) })
            .Add(x => x.Selectable, true));

        Assert.Empty(cut.FindAll("button.show-qr"));
        Assert.Empty(cut.FindAll("button.show-qr-selected"));
    }
}
